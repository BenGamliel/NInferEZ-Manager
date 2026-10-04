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
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private UpdateInfo? _checked;
    private CancellationTokenSource? _downloadCancellation;
    private Task? _downloadTask;
    public string? VerifiedPackageSha256 { get; private set; }
    public UpdateProgress? Current{get;private set;}
    public UpdateService(AppPaths paths,SettingsService settings){_paths=paths;_settings=settings;_client.DefaultRequestHeaders.UserAgent.ParseAdd("NInferEZ-Manager/0.1");}

    public async Task<UpdateInfo> CheckAsync(CancellationToken token=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response=await _client.GetAsync(ReleasesApi,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        if(!response.IsSuccessStatusCode)return new(false,CurrentVersion,"Unknown",null,"The update service is currently unavailable.");
        using var json=JsonDocument.Parse(await BoundedHttpContent.ReadTextAsync(response.Content,timeout.Token));
        var root=json.RootElement;var tag=root.GetProperty("tag_name").GetString()?.TrimStart('v')??"Unknown";
        var url=root.TryGetProperty("html_url",out var page)?page.GetString():null;
        var available=Version.TryParse(tag,out var latest)&&latest>Version.Parse(CurrentVersion);
        string? expected=_paths.IsPortable?$"NInferEZ-Manager-Portable-{tag}.zip":$"NInferEZ-Manager-Setup-{tag}.exe";
        JsonElement asset=default;var found=false;
        if(root.TryGetProperty("assets",out var assets))foreach(var item in assets.EnumerateArray())if(string.Equals(item.GetProperty("name").GetString(),expected,StringComparison.OrdinalIgnoreCase)){asset=item;found=true;break;}
        _settings.Update(s=>s.LastUpdateCheckUtc=DateTimeOffset.UtcNow);
        if(!available)return new(false,CurrentVersion,tag,url,"You are using the latest version.");
        if(!found)return new(true,CurrentVersion,tag,url,$"Version {tag} is available, but the matching package was not found.");
        var digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;if(digest?.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)==true)digest=digest[7..];
        return _checked = new(true,CurrentVersion,tag,url,$"Version {tag} is available.",expected,asset.GetProperty("browser_download_url").GetString(),digest,asset.GetProperty("size").GetInt64());
    }

    public void StartDownload(UpdateInfo update)
    {
        lock (_downloadGate)
        {
            if (_downloadTask is { IsCompleted: false }) throw new InvalidOperationException("An update download is already running.");
            ValidatePackage(update);
            _downloadCancellation?.Dispose();
            _downloadCancellation = new CancellationTokenSource();
            var token = _downloadCancellation.Token;
            Current = new("Starting",0,update.AssetSize,true);
            _downloadTask = Task.Run(async () => { try { await DownloadAsync(update,token); } catch { } });
        }
    }
    public void CancelDownload() => _downloadCancellation?.Cancel();
    private void ValidatePackage(UpdateInfo update)
    {
        if (_checked is null || update != _checked) throw new InvalidOperationException("Check for updates again before downloading. Only the backend-verified release is accepted.");
        if (string.IsNullOrWhiteSpace(update.AssetName) || Path.GetFileName(update.AssetName)!=update.AssetName || string.IsNullOrWhiteSpace(update.Sha256) || update.Sha256.Length!=64 || !update.Sha256.All(Uri.IsHexDigit)
            || update.AssetSize is <=0 or > 1024L*1024*1024 || !Uri.TryCreate(update.AssetUrl,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="github.com" || !uri.AbsolutePath.StartsWith("/BenGamliel/NInferEZ-Manager/releases/download/",StringComparison.Ordinal))
            throw new InvalidDataException("The release has no safe verifiable update package.");
    }

    public async Task DownloadAsync(UpdateInfo update,CancellationToken token)
    {
        ValidatePackage(update);
        if(!await _downloadGate.WaitAsync(0,token))throw new InvalidOperationException("Another update download is running.");
        VerifiedPackageSha256=null;
        var directory=Path.Combine(_paths.DataDirectory,"Updates");Directory.CreateDirectory(directory);
        var destination=Path.Combine(directory,update.AssetName!);var partial=destination+".part";
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(15));
            using var response=await _client.GetAsync(update.AssetUrl,HttpCompletionOption.ResponseHeadersRead,timeout.Token);response.EnsureSuccessStatusCode();
            long completed=0;
            await using (var input=await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,1024*1024,true))
            {
            var buffer=new byte[1024*1024];
            while(true){var read=await input.ReadAsync(buffer,timeout.Token);if(read==0)break;completed+=read;if(completed>update.AssetSize)throw new InvalidDataException("The update exceeds its verified size.");await output.WriteAsync(buffer.AsMemory(0,read),timeout.Token);Current=new("Downloading",completed,update.AssetSize,true);}
            await output.FlushAsync(token);Current=new("Verifying SHA-256",completed,update.AssetSize,true);
            }
            if(completed!=update.AssetSize)throw new InvalidDataException("The update download is incomplete.");
            await using(var verify=File.OpenRead(partial))
            {
            var hash=Convert.ToHexString(await SHA256.HashDataAsync(verify,token));
            if(!hash.Equals(update.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The update failed SHA-256 verification.");
            }
            VerifiedPackageSha256=update.Sha256;
            File.Move(partial,destination,true);Current=new("Ready to install",completed,update.AssetSize,false,null,destination);
        }
        catch(Exception ex){Current=new("Failed",0,update.AssetSize,false,ex.Message);try{File.Delete(partial);}catch{}throw;}
        finally{_downloadGate.Release();}
    }
    public async Task VerifyReadyPackageAsync(CancellationToken token=default)
    {
        var path=Current?.PackagePath;
        if(path is null||VerifiedPackageSha256 is null)throw new InvalidOperationException("No verified update is ready.");
        await using var stream=File.OpenRead(path);
        if(!Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).Equals(VerifiedPackageSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The update changed after download. Check for updates and download it again.");
    }
    public void Dispose(){_downloadCancellation?.Cancel();_client.Dispose();}
}

