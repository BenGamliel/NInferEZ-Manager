using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public sealed class UpdateService : IDisposable
{
    private const string CurrentVersion=ProductInfo.Version;
    private const string ReleasesApi=ProductInfo.AppReleasesApi;
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly HttpClient _client=new(new SocketsHttpHandler{AutomaticDecompression=DecompressionMethods.All}){Timeout=Timeout.InfiniteTimeSpan};
    public UpdateProgress? Current{get;private set;}
    public UpdateService(AppPaths paths,SettingsService settings){_paths=paths;_settings=settings;_client.DefaultRequestHeaders.UserAgent.ParseAdd("NInferEZ-Manager/0.1");}

    public async Task<UpdateInfo> CheckAsync(CancellationToken token=default)
    {
        using var response=await _client.GetAsync(ReleasesApi,token);
        if(!response.IsSuccessStatusCode)return new(false,CurrentVersion,"Unknown",null,"The update service is currently unavailable.");
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root=json.RootElement;var tag=root.GetProperty("tag_name").GetString()?.TrimStart('v')??"Unknown";
        var url=root.TryGetProperty("html_url",out var page)?page.GetString():null;
        var available=Version.TryParse(tag,out var latest)&&latest>Version.Parse(CurrentVersion);
        string? expected=_paths.IsPortable?$"NInferEZ-Manager-Portable-{tag}.zip":$"NInferEZ-Manager-Setup-{tag}.exe";
        JsonElement asset=default;var found=false;
        if(root.TryGetProperty("assets",out var assets))foreach(var item in assets.EnumerateArray())if(string.Equals(item.GetProperty("name").GetString(),expected,StringComparison.OrdinalIgnoreCase)){asset=item;found=true;break;}
        _settings.Current.LastUpdateCheckUtc=DateTimeOffset.UtcNow;_settings.Save();
        if(!available)return new(false,CurrentVersion,tag,url,"You are using the latest version.");
        if(!found)return new(true,CurrentVersion,tag,url,$"Version {tag} is available, but the matching package was not found.");
        var digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;if(digest?.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)==true)digest=digest[7..];
        return new(true,CurrentVersion,tag,url,$"Version {tag} is available.",expected,asset.GetProperty("browser_download_url").GetString(),digest,asset.GetProperty("size").GetInt64());
    }

    public async Task DownloadAsync(UpdateInfo update,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(update.AssetName)||string.IsNullOrWhiteSpace(update.AssetUrl)||string.IsNullOrWhiteSpace(update.Sha256))throw new InvalidOperationException("The release has no verifiable update package.");
        var directory=Path.Combine(_paths.DataDirectory,"Updates");Directory.CreateDirectory(directory);
        var destination=Path.Combine(directory,update.AssetName);var partial=destination+".part";
        try
        {
            using var response=await _client.GetAsync(update.AssetUrl,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            await using var input=await response.Content.ReadAsStreamAsync(token);await using var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,1024*1024,true);
            var buffer=new byte[1024*1024];long completed=0;
            while(true){var read=await input.ReadAsync(buffer,token);if(read==0)break;await output.WriteAsync(buffer.AsMemory(0,read),token);completed+=read;Current=new("Downloading",completed,update.AssetSize,true);}
            await output.FlushAsync(token);Current=new("Verifying SHA-256",completed,update.AssetSize,true);
            await using var verify=File.OpenRead(partial);var hash=Convert.ToHexString(await SHA256.HashDataAsync(verify,token));
            if(!hash.Equals(update.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The update failed SHA-256 verification.");
            File.Move(partial,destination,true);Current=new("Ready to install",completed,update.AssetSize,false,null,destination);
        }
        catch(Exception ex){Current=new("Failed",0,update.AssetSize,false,ex.Message);try{File.Delete(partial);}catch{}throw;}
    }
    public void Dispose()=>_client.Dispose();
}

public static class PortableUpdateRunner
{
    public static bool TryRun(string[] args)
    {
        if(args.Length==0||!args[0].Equals("--apply-portable-update",StringComparison.OrdinalIgnoreCase))return false;
        var values=args.Skip(1).Chunk(2).Where(x=>x.Length==2).ToDictionary(x=>x[0],x=>x[1],StringComparer.OrdinalIgnoreCase);
        var zip=values["--zip"];var target=Path.GetFullPath(values["--target"]);var uiPid=int.Parse(values["--ui-pid"]);var backendPid=int.Parse(values["--backend-pid"]);
        if(!File.Exists(Path.Combine(target,"portable.mode"))||!File.Exists(Path.Combine(target,"NInferEZ Manager.exe")))throw new InvalidOperationException("The portable target is invalid.");
        Wait(uiPid);Wait(backendPid);
        var staging=Path.Combine(Path.GetTempPath(),"NInferEZ-Manager-Update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try
        {
            ZipFile.ExtractToDirectory(zip,staging,true);
            if(!File.Exists(Path.Combine(staging,"NInferEZ Manager.exe"))||!File.Exists(Path.Combine(staging,"Backend","NInferManager.Backend.exe")))
                throw new InvalidDataException("The portable update package is incomplete.");
            foreach(var name in new[]{"Backend","Docs"}){var path=Path.Combine(target,name);if(Directory.Exists(path))Directory.Delete(path,true);}
            foreach(var file in Directory.EnumerateFiles(staging,"*",SearchOption.AllDirectories))
            {
                var relative=Path.GetRelativePath(staging,file);if(relative.StartsWith("Data"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||relative.StartsWith("Models"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||relative.StartsWith("Engine"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||relative.Equals("portable.mode",StringComparison.OrdinalIgnoreCase))continue;
                var destination=Path.Combine(target,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination,true);
            }
            Process.Start(new ProcessStartInfo(Path.Combine(target,"NInferEZ Manager.exe")){UseShellExecute=true,WorkingDirectory=target});
        }
        finally{try{Directory.Delete(staging,true);}catch{}}
        return true;
    }
    private static void Wait(int pid){try{using var p=Process.GetProcessById(pid);p.WaitForExit(120000);}catch{}}
}
