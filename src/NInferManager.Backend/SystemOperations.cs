using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NInferManager.Backend;

public static class SystemOperations
{
    public static string CreateDiagnostics(AppPaths paths,SettingsService settings,ManagerLog log)
    {
        var outputDirectory=Path.Combine(paths.DataDirectory,"Diagnostics");
        Directory.CreateDirectory(outputDirectory);
        var staging=Path.Combine(Path.GetTempPath(),"NInferEZ-Manager-Diagnostics-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var node=JsonSerializer.SerializeToNode(settings.Current,SettingsService.JsonOptions)?.AsObject()??new JsonObject();
            node["apiKey"]="";
            node["externalModelPaths"]=new JsonObject();
            if(node["profiles"] is JsonObject profiles)foreach(var profile in profiles.Select(x=>x.Value).OfType<JsonObject>())
            {
                profile["contextCostPresetsFile"]="";
                profile["requestLogJsonlFile"]="";
                profile["chatTemplateFile"]="";
            }
            File.WriteAllText(Path.Combine(staging,"settings.sanitized.json"),node.ToJsonString(SettingsService.JsonOptions));
            File.WriteAllText(Path.Combine(staging,"manager.log"),Sanitize(log.Tail(3000),paths));
            File.WriteAllText(Path.Combine(staging,"summary.txt"),$"NInferEZ Manager diagnostics{Environment.NewLine}Created: {DateTimeOffset.Now:O}{Environment.NewLine}OS: {Environment.OSVersion.VersionString}{Environment.NewLine}64-bit: {Environment.Is64BitOperatingSystem}{Environment.NewLine}");
            var destination=Path.Combine(outputDirectory,$"NInferEZ-Manager-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(staging,destination,CompressionLevel.Optimal,false);
            return destination;
        }
        finally{try{Directory.Delete(staging,true);}catch{}}
    }

    public static void TrimWorkingSet()
    {
        if(!OperatingSystem.IsWindows())return;
        GC.Collect(2,GCCollectionMode.Optimized,false,true);
        using var process=Process.GetCurrentProcess();
        _=SetProcessWorkingSetSize(process.Handle,new IntPtr(-1),new IntPtr(-1));
    }

    private static string Sanitize(string value,AppPaths paths)
    {
        value=value.Replace(paths.AppDirectory,"<APP>",StringComparison.OrdinalIgnoreCase)
            .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<USER>",StringComparison.OrdinalIgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(value,@"(?i)(authorization|api[-_ ]?key)\s*[:=]\s*\S+","$1: <redacted>");
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process,IntPtr minimum,IntPtr maximum);
}
