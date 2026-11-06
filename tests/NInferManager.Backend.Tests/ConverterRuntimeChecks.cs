using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using NInferManager.Backend;

internal static class ConverterRuntimeChecks
{
    public static async Task RunAsync(AppPaths paths,Action<bool,string> assert)
    {
        var root=Path.Combine(paths.DataDirectory,"converter-runtime-checks");Directory.CreateDirectory(root);
        var archive=Path.Combine(root,"runtime.zip");var manifest=Path.Combine(root,"runtime.json");
        WriteArchive(archive);
        await WriteManifest();
        var runtimes=Path.Combine(root,"extracted");
        var installed=await ConverterRuntime.ExtractAsync(archive,manifest,runtimes,CancellationToken.None);
        assert(File.ReadAllText(Path.Combine(installed,"NInferEZ.Converter.Backend.exe"))=="unchanged converter" && File.ReadAllText(Path.Combine(installed,"runtime","python","python.exe"))=="unchanged python","bundled Converter runtime extracts original bytes without executing them");
        assert(await ConverterRuntime.ExtractAsync(archive,manifest,runtimes,CancellationToken.None)==installed,"complete Converter runtime setup is idempotent");
        File.WriteAllText(manifest,JsonSerializer.Serialize(new{schemaVersion=1,converterVersion="1.0.0",bytes=new FileInfo(archive).Length,sha256=new string('0',64)},SettingsService.JsonOptions));
        await ThrowsAsync<InvalidDataException>(()=>ConverterRuntime.ExtractAsync(archive,manifest,Path.Combine(root,"bad-hash"),CancellationToken.None));
        assert(!Directory.Exists(Path.Combine(root,"bad-hash")),"bad Converter runtime SHA creates no extracted runtime");
        WriteArchive(archive,unsafePath:true);await WriteManifest();
        await ThrowsAsync<InvalidDataException>(()=>ConverterRuntime.ExtractAsync(archive,manifest,Path.Combine(root,"bad-path"),CancellationToken.None));
        assert(!File.Exists(Path.Combine(root,"outside.txt")) && !Directory.EnumerateDirectories(Path.Combine(root,"bad-path"),".staging-*").Any(),"unsafe Converter runtime path is rejected and owned staging is cleaned");
        FileStream? lockedStagedFile=null;
        string? retainedStaging=null;
        try
        {
            try
            {
                await ConverterRuntime.ExtractCoreAsync(archive,manifest,Path.Combine(root,"locked-cleanup"),CancellationToken.None,staging=>
                {
                    retainedStaging=staging;
                    lockedStagedFile=new FileStream(Path.Combine(staging,"NInferEZ.Converter.Backend.exe"),FileMode.Open,FileAccess.Read,FileShare.None);
                });
                throw new Exception("Expected the unsafe archive extraction to fail.");
            }
            catch(InvalidDataException error)
            {
                assert(error.Message=="Unsafe Converter runtime archive path." && error.Data["ConverterStagingDirectory"] as string==retainedStaging && error.Data["ConverterCleanupError"] is string,"locked staging cleanup preserves the original extraction error and recovery path");
            }
            assert(retainedStaging is not null && Directory.Exists(retainedStaging) && File.Exists(Path.Combine(retainedStaging,"NInferEZ.Converter.Backend.exe")),"locked Converter staging remains available for recovery");
            WriteArchive(archive);await WriteManifest();
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var retried=await ConverterRuntime.ExtractAsync(archive,manifest,Path.Combine(root,"after-cleanup-failure"),deadline.Token);
            assert(File.Exists(Path.Combine(retried,"runtime","python","python.exe")),"Converter setup releases its semaphore after cleanup fails and another attempt succeeds");
        }
        finally { lockedStagedFile?.Dispose(); }
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await ThrowsAsync<OperationCanceledException>(()=>ConverterRuntime.ExtractAsync(archive,manifest,Path.Combine(root,"cancelled"),cancelled.Token));
        assert(!Directory.Exists(Path.Combine(root,"cancelled")),"cancelled Converter setup creates no runtime");
        async Task WriteManifest()
        {
            using var input=File.OpenRead(archive);
            await File.WriteAllTextAsync(manifest,JsonSerializer.Serialize(new{schemaVersion=1,converterVersion="1.0.0",bytes=input.Length,sha256=Convert.ToHexString(SHA256.HashData(input))},SettingsService.JsonOptions));
        }
    }
    private static void WriteArchive(string file,bool unsafePath=false)
    {
        using var output=File.Create(file);using var zip=new ZipArchive(output,ZipArchiveMode.Create);
        Add("NInferEZ.Converter.Backend.exe","unchanged converter");Add("runtime/python/python.exe","unchanged python");
        if(unsafePath)Add("../outside.txt","unsafe");
        void Add(string name,string text){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(text);}
    }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T:Exception {try{await action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}.");}
}
