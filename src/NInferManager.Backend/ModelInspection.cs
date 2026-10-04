using System.Diagnostics;
using System.Text.Json;

namespace NInferManager.Backend;

public static class ModelInspection
{
    public static async Task<JsonDocument?> ReadAsync(string engineDirectory,string path,CancellationToken token=default)
    {
        var exe=Path.Combine(engineDirectory,"ninfer-inspect.exe");
        if(!File.Exists(exe))return null;
        var start=new ProcessStartInfo(exe){WorkingDirectory=engineDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("--model");start.ArgumentList.Add(path);start.ArgumentList.Add("--json");
        using var process=Process.Start(start)??throw new InvalidOperationException("Model inspection could not start.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text=await output;var detail=await error;
            if(text.Length>4*1024*1024)throw new InvalidDataException("The model inspection result is too large.");
            var document=JsonDocument.Parse(text);
            if(process.ExitCode!=0||!document.RootElement.TryGetProperty("valid",out var valid)||!valid.GetBoolean())
            {document.Dispose();throw new InvalidDataException(string.IsNullOrWhiteSpace(detail)?"The model artifact failed engine inspection.":detail.Trim());}
            return document;
        }
        catch
        {
            try{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}}catch{}
            throw;
        }
    }
}
