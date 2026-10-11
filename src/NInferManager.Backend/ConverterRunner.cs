using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NInferManager.Backend;

public interface IConverterRunner
{
    Task<int> RunAsync(string command, string request, Action<JsonElement> onEvent,
        Action<string> onDiagnostic, CancellationToken cancellation);
}

public sealed class ConverterRunner(AppPaths paths) : IConverterRunner
{
    public async Task<int> RunAsync(string command, string request, Action<JsonElement> onEvent,
        Action<string> onDiagnostic, CancellationToken cancellation)
    {
        var root = Path.Combine(paths.AppDirectory, "Converter");
        var exe = Path.Combine(root, "NInferEZ.Converter.Backend.exe");
        if (!File.Exists(exe) || !File.Exists(Path.Combine(root,"runtime","python","python.exe")))
            throw new FileNotFoundException("The conversion runtime is missing. Extract the complete Manager package, including its Converter folder.");
        var start = new ProcessStartInfo(exe) { WorkingDirectory=root, UseShellExecute=false,
            CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true,
            RedirectStandardError=true, StandardInputEncoding=new UTF8Encoding(false),
            StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 };
        start.ArgumentList.Add(command);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start the converter.");
        try
        {
            if (command != "capabilities")
            {
                await process.StandardInput.WriteLineAsync(request);
                await process.StandardInput.FlushAsync();
            }
            var output = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (line.Length > 8_000_000) throw new InvalidDataException("The converter returned an oversized response.");
                    using var document = JsonDocument.Parse(line);
                    onEvent(document.RootElement.Clone());
                }
            });
            var diagnostics = Task.Run(async () =>
            {
                var count=0;
                while (await process.StandardError.ReadLineAsync() is { } line)
                    if (count++<80) onDiagnostic(line.Length>2000?line[..2000]:line);
            });
            using var registration = cancellation.Register(() =>
            {
                try { process.StandardInput.WriteLine("cancel"); process.StandardInput.Flush(); }
                catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException) { }
            });
            var completion = process.WaitForExitAsync();
            var cancelled = Task.Delay(Timeout.InfiniteTimeSpan,cancellation);
            if (await Task.WhenAny(completion,cancelled,output) == cancelled)
            {
                if (await Task.WhenAny(completion,Task.Delay(TimeSpan.FromSeconds(30)))!=completion)
                    throw new TimeoutException("The converter did not stop cleanly. Its temporary .ezc- folder may require cleanup; existing models were not replaced.");
            }
            else if (output.IsFaulted) await output;
            await completion;
            await Task.WhenAll(output,diagnostics);
            return process.ExitCode;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree:true); await process.WaitForExitAsync(); }
        }
    }
}
