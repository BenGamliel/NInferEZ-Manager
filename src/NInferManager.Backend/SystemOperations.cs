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
            var snapshot=settings.Snapshot();
            var node=JsonSerializer.SerializeToNode(snapshot,SettingsService.JsonOptions)?.AsObject()??new JsonObject();
            node["apiKey"]="";
            node["externalModelPaths"]=new JsonObject();
            node["modelAliases"]=new JsonObject();node["activeModelFile"]="";node["seenModelArtifacts"]=new JsonArray();
            if(node["profiles"] is JsonObject profiles)
            {
                var anonymous=new JsonObject();var index=0;
                foreach(var profile in profiles.Select(x=>x.Value).OfType<JsonObject>())
                {
                profile["contextCostPresetsFile"]="";
                profile["requestLogJsonlFile"]="";
                profile["chatTemplateFile"]="";
                    anonymous[$"model-{++index}"]=profile.DeepClone();
                }
                node["profiles"]=anonymous;
            }
            File.WriteAllText(Path.Combine(staging,"settings.sanitized.json"),node.ToJsonString(SettingsService.JsonOptions));
            var sensitive=snapshot.ExternalModelPaths.Values.Concat(snapshot.ModelAliases.Values).Concat(snapshot.Profiles.Keys).Concat(snapshot.Profiles.Values.SelectMany(p=>new[]{p.ContextCostPresetsFile,p.RequestLogJsonlFile,p.ChatTemplateFile})).Append(snapshot.ApiKey).Where(x=>!string.IsNullOrWhiteSpace(x)).OrderByDescending(x=>x.Length);
            var text=log.Tail(3000);foreach(var value in sensitive)text=text.Replace(value,"<redacted>",StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(Path.Combine(staging,"manager.log"),Sanitize(text,paths));
            File.WriteAllText(Path.Combine(staging,"summary.txt"),$"NInferEZ Manager diagnostics{Environment.NewLine}Created: {DateTimeOffset.Now:O}{Environment.NewLine}OS: {Environment.OSVersion.VersionString}{Environment.NewLine}64-bit: {Environment.Is64BitOperatingSystem}{Environment.NewLine}Known credentials, model identities and file paths are redacted. Review free-text log content before sharing; absolute anonymity is not guaranteed.{Environment.NewLine}");
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

    public static string Sanitize(string value,AppPaths paths)
    {
        value=value.Replace(paths.AppDirectory,"<APP>",StringComparison.OrdinalIgnoreCase)
            .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<USER>",StringComparison.OrdinalIgnoreCase);
        value=System.Text.RegularExpressions.Regex.Replace(value,@"(?i)(--api[-_]key\s+|api[-_ ]?key\s*[:=]\s*|authorization\s*[:=]\s*(?:bearer\s+)?|bearer\s+)\S+","$1<redacted>");
        return System.Text.RegularExpressions.Regex.Replace(value,@"(?i)\b[A-Z]:[\\/][^\r\n|\""<>]+","<PATH>");
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process,IntPtr minimum,IntPtr maximum);
}
