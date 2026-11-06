using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace NInferManager.Backend;

/// <summary>Extracts unchanged stable Converter bytes from one bounded application component.</summary>
public static class ConverterRuntime
{
    private static readonly SemaphoreSlim Gate = new(1,1);
    public static async Task<string> EnsureAsync(AppPaths paths,CancellationToken token)
    {
        var backend=Path.Combine(paths.AppDirectory,"Backend");
        if(File.Exists(Path.Combine(backend,"Converter-runtime.zip")))return await ExtractAsync(Path.Combine(backend,"Converter-runtime.zip"),Path.Combine(backend,"Converter-runtime.json"),Path.Combine(paths.DataDirectory,"runtimes","converter"),token);
        foreach(var legacy in new[]{Path.Combine(backend,"Converter"),Path.Combine(paths.AppDirectory,"Converter")})
            if(File.Exists(Path.Combine(legacy,"NInferEZ.Converter.Backend.exe")) && File.Exists(Path.Combine(legacy,"runtime","python","python.exe")))return legacy;
        return await ExtractAsync(Path.Combine(backend,"Converter-runtime.zip"),Path.Combine(backend,"Converter-runtime.json"),Path.Combine(paths.DataDirectory,"runtimes","converter"),token);
    }
    public static Task<string> ExtractAsync(string archive,string manifestFile,string runtimeRoot,CancellationToken token)
        => ExtractCoreAsync(archive,manifestFile,runtimeRoot,token);
    internal static async Task<string> ExtractCoreAsync(string archive,string manifestFile,string runtimeRoot,CancellationToken token,Action<string>? beforeCleanup = null)
    {
        await Gate.WaitAsync(token);
        string? staging=null;
        Exception? extractionFailure=null;
        try
        {
            if(!File.Exists(archive)||!File.Exists(manifestFile))throw new FileNotFoundException("The conversion runtime is missing. Extract the complete Manager package.");
            if(new FileInfo(manifestFile).Length>16384)throw new InvalidDataException("Invalid Converter runtime manifest.");
            var manifest=JsonSerializer.Deserialize<RuntimeManifest>(await File.ReadAllTextAsync(manifestFile,token),SettingsService.JsonOptions)
                ?? throw new InvalidDataException("Invalid Converter runtime manifest.");
            if(manifest.SchemaVersion!=1 || manifest.ConverterVersion!="1.0.0" || manifest.Bytes<=0 || manifest.Bytes>1024L*1024*1024 || manifest.Sha256?.Length!=64 || !manifest.Sha256.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid Converter runtime identity.");
            var destination=Path.GetFullPath(Path.Combine(runtimeRoot,"1.0.0-"+manifest.Sha256[..12].ToLowerInvariant()));
            var complete=Path.Combine(destination,".complete");
            if(File.Exists(complete) && await File.ReadAllTextAsync(complete,token)==manifest.Sha256 &&
                File.Exists(Path.Combine(destination,"NInferEZ.Converter.Backend.exe")) && File.Exists(Path.Combine(destination,"runtime","python","python.exe")))return destination;
            if(new FileInfo(archive).Length!=manifest.Bytes)throw new InvalidDataException("The Converter runtime archive is incomplete.");
            await using(var input=File.OpenRead(archive))
                if(!Convert.ToHexString(await SHA256.HashDataAsync(input,token)).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The Converter runtime failed SHA-256 verification.");
            Directory.CreateDirectory(runtimeRoot);
            staging=Path.Combine(Path.GetFullPath(runtimeRoot),".staging-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            var prefix=Path.GetFullPath(staging)+Path.DirectorySeparatorChar;
            using(var zip=ZipFile.OpenRead(archive))
            {
                if(zip.Entries.Count is 0 or >20000)throw new InvalidDataException("Invalid Converter runtime archive entry count.");
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long expanded=0;
                foreach(var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    var name=entry.FullName.Replace('\\','/');var parts=name.Split('/',StringSplitOptions.RemoveEmptyEntries);
                    if(name.StartsWith('/') || name.Contains(':') || parts.Any(p=>p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')) || (entry.ExternalAttributes >> 16 & 0xF000)==0xA000)throw new InvalidDataException("Unsafe Converter runtime archive path.");
                    var output=Path.GetFullPath(Path.Combine(staging,name));
                    if(!output.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!names.Add(output))throw new InvalidDataException("Unsafe or duplicate Converter runtime archive path.");
                    expanded=checked(expanded+entry.Length);if(expanded>4L*1024*1024*1024)throw new InvalidDataException("The expanded Converter runtime exceeds its limit.");
                    if(name.EndsWith('/')){Directory.CreateDirectory(output);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);entry.ExtractToFile(output,false);
                }
            }
            if(!File.Exists(Path.Combine(staging,"NInferEZ.Converter.Backend.exe")) || !File.Exists(Path.Combine(staging,"runtime","python","python.exe")))throw new InvalidDataException("The Converter runtime is incomplete.");
            await File.WriteAllTextAsync(Path.Combine(staging,".complete"),manifest.Sha256,token);
            if(Directory.Exists(destination))throw new IOException("An incomplete Converter runtime was preserved. Remove or inspect it before retrying setup.");
            Directory.Move(staging,destination);staging=null;return destination;
        }
        catch(Exception error)
        {
            extractionFailure=error;
            throw;
        }
        finally
        {
            try
            {
                if(staging is not null && Directory.Exists(staging))
                {
                    var prefix=Path.GetFullPath(runtimeRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                    if(!Path.GetFullPath(staging).StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe Converter staging cleanup path.");
                    beforeCleanup?.Invoke(staging);
                    Directory.Delete(staging,true);
                }
            }
            catch(Exception cleanupError) when(extractionFailure is not null && cleanupError is IOException or UnauthorizedAccessException)
            {
                // Keep the original extraction/cancellation failure. Locked staging is
                // retained for recovery, but must never prevent another setup attempt.
                extractionFailure.Data["ConverterStagingDirectory"]=staging;
                extractionFailure.Data["ConverterCleanupError"]=cleanupError.Message;
            }
            finally { Gate.Release(); }
        }
    }
    private sealed record RuntimeManifest(int SchemaVersion,string ConverterVersion,long Bytes,string Sha256);
}
