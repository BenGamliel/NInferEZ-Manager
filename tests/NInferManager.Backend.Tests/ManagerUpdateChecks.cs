using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NInferManager.Backend;
using NInferManager.Contracts;

internal static class ManagerUpdateChecks
{
    public static async Task RunAsync(AppPaths paths,Action<bool,string> assert)
    {
        var fixtures=Path.Combine(paths.DataDirectory,"manager-update-fixtures");Directory.CreateDirectory(fixtures);
        byte[] package=Encoding.UTF8.GetBytes("verified fixture package bytes");
        var hash=Convert.ToHexString(SHA256.HashData(package));
        foreach(var portable in new[]{true,false})
        {
            var app=Path.Combine(fixtures,portable?"Portable":"Installed");Directory.CreateDirectory(app);
            if(portable)File.WriteAllText(Path.Combine(app,"portable.mode"),"");
            var fixturePaths=new AppPaths(app);
            var settings=new SettingsService(fixturePaths);
            var requests=new List<string>();
            using var service=new UpdateService(fixturePaths,settings,new HttpClient(new Handler(request=>
            {
                requests.Add(request.RequestUri!.AbsoluteUri);
                if(request.RequestUri.AbsoluteUri==ProductInfo.AppReleasesApi)return Json(Release(hash,package.Length));
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(package)};
            })));
            var update=await service.CheckAsync();
            var expected=portable?"NInferEZ-Manager-Portable-2.0.0.zip":"NInferEZ-Manager-Setup-2.0.0.exe";
            assert(update.UpdateAvailable && update.AssetName==expected && update.Sha256==hash,$"{(portable?"portable":"installed")} update selects its verified Manager package type");
            try { service.StartDownload(update with{Sha256=new string('0',64)});throw new Exception("Expected altered metadata rejection."); }
            catch(InvalidOperationException) { }
            assert(requests.Count==1,"UI cannot alter backend-checked release metadata before download");
            await service.DownloadAsync(update,CancellationToken.None);
            await service.VerifyReadyPackageAsync();
            assert(service.Current?.Stage=="Ready to install" && service.VerifiedPackageSha256==hash && requests.Count==2,"one Manager update stages a size-and-SHA verified package without fetching an independent engine channel");
            await File.WriteAllBytesAsync(service.Current!.PackagePath!,[0,1,2]);
            await ThrowsAsync<InvalidDataException>(()=>service.VerifyReadyPackageAsync());
            assert(true,"changed staged Manager package is rejected before application replacement");
        }

        var badPaths=new AppPaths(Path.Combine(fixtures,"BadHash"));
        using(var service=new UpdateService(badPaths,new SettingsService(badPaths),new HttpClient(new Handler(request=>
        {
            if(request.RequestUri!.AbsoluteUri==ProductInfo.AppReleasesApi)return Json(Release(new string('0',64),package.Length));
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(package)};
        }))))
        {
            var update=await service.CheckAsync();
            await ThrowsAsync<InvalidDataException>(()=>service.DownloadAsync(update,CancellationToken.None));
            assert(service.Current is {Stage:"Failed",PackagePath:null} && !File.Exists(Path.Combine(badPaths.DataDirectory,"Updates",update.AssetName!)+".part"),"bad Manager download SHA leaves no ready package or partial file");
        }
        using(var service=new UpdateService(badPaths,new SettingsService(badPaths),new HttpClient(new Handler(_=>Json(Release(hash,1024L*1024*1024+1))))))
        {
            var update=await service.CheckAsync();
            assert(update.UpdateAvailable&&update.AssetUrl is null,"known release without a safe package has no install action");
            try { service.StartDownload(update);throw new Exception("Expected existing updater size-limit rejection."); }
            catch(InvalidOperationException) { }
            assert(true,"Manager release packages must fit the existing 1 GiB updater bound");
        }

        var latestPaths=new AppPaths(Path.Combine(fixtures,"LatestKnown"));
        var latestSettings=new SettingsService(latestPaths);
        var version="2.0.0";
        var offline=false;
        string? downloaded=null;
        using(var service=new UpdateService(latestPaths,latestSettings,new HttpClient(new Handler(request=>
        {
            if(request.RequestUri!.AbsoluteUri==ProductInfo.AppReleasesApi)
                return offline?new HttpResponseMessage(HttpStatusCode.ServiceUnavailable):Json(Release(hash,package.Length,version));
            downloaded=request.RequestUri.AbsoluteUri;
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(package)};
        }))))
        {
            var known=await service.CheckAsync();
            latestSettings.Update(s=>s.DismissedUpdateVersion=known.LatestVersion);
            var restarted=new SettingsService(latestPaths).Snapshot();
            assert(restarted.AvailableApplicationUpdate==new ApplicationUpdateNotice("2.0.0",true)&&restarted.DismissedUpdateVersion=="2.0.0",
                "a known update and its banner dismissal persist independently across a settings reload");
            offline=true;
            var unavailable=await service.CheckAsync();
            assert(unavailable.LatestVersion=="Unknown"&&latestSettings.Current.AvailableApplicationUpdate==restarted.AvailableApplicationUpdate,
                "a temporary update service outage retains the known release");
            offline=false;version="3.0.0";
            var newest=await service.CheckAsync();
            try { service.StartDownload(known);throw new Exception("Expected rejection of the older saved offer."); }
            catch(InvalidOperationException) { }
            await service.DownloadAsync(newest,CancellationToken.None);
            assert(downloaded?.Contains("/v3.0.0/",StringComparison.Ordinal)==true&&
                latestSettings.Current.AvailableApplicationUpdate==new ApplicationUpdateNotice("3.0.0",true)&&latestSettings.Current.DismissedUpdateVersion=="2.0.0",
                "an install recheck skips the previously known release and selects the newest package");
        }
    }
    private static object Release(string sha,long bytes,string version="2.0.0")=>new
    {
        tag_name=$"v{version}",html_url=$"https://github.com/BenGamliel/NInferEZ-Manager/releases/tag/v{version}",
        assets=new[]{$"NInferEZ-Manager-Portable-{version}.zip",$"NInferEZ-Manager-Setup-{version}.exe"}.Select(name=>new
        {
            name,digest="sha256:"+sha,size=bytes,
            browser_download_url=$"https://github.com/BenGamliel/NInferEZ-Manager/releases/download/v{version}/"+name
        }).ToArray()
    };
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    private static async Task ThrowsAsync<T>(Func<Task> action) where T:Exception {try{await action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}.");}
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(response(request));
    }
}
