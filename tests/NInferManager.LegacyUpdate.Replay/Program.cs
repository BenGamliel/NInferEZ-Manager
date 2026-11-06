using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Replay only: the real unmodified 1.0.0 updater assembly checks an official-shaped
// feed and verifies actual package bytes. No updater/helper/application is launched.
// A separate process deliberately avoids sharing the Manager's UI mutex or settings.
if(args.Length!=5)throw new ArgumentException("Usage: legacyBackendDll packagePath portable|installed workDirectory outputJson");
var assemblyFile=Path.GetFullPath(args[0]);
var package=Path.GetFullPath(args[1]);
var portable=args[2] switch{"portable"=>true,"installed"=>false,_=>throw new ArgumentException("Choose portable or installed.")};
var work=Path.GetFullPath(args[3]);var reportFile=Path.GetFullPath(args[4]);
Directory.CreateDirectory(work);
var executable=Environment.ProcessPath??throw new Exception("Replay apphost path is missing.");
if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))throw new Exception("Run the replay .exe apphost so its portable marker stays inside the owned test folder.");
var appDirectory=Path.GetDirectoryName(executable)!;
var marker=Path.Combine(appDirectory,"portable.mode");
if(File.Exists(marker))throw new Exception("Replay apphost already has a portable marker; do not change an existing application folder.");
var envNames=new[]{"NINFEREZ_MANAGER_DATA_ROOT","NINFEREZ_MANAGER_MODELS_ROOT","NINFEREZ_MANAGER_ENGINE_ROOT"};
var previous=envNames.ToDictionary(n=>n,Environment.GetEnvironmentVariable);
var context=new LegacyContext(Path.GetDirectoryName(assemblyFile)!);
IDisposable? updater=null;
try
{
    if(portable)File.WriteAllText(marker,"");
    Environment.SetEnvironmentVariable(envNames[0],Path.Combine(work,"Data"));
    Environment.SetEnvironmentVariable(envNames[1],Path.Combine(work,"Models"));
    Environment.SetEnvironmentVariable(envNames[2],Path.Combine(work,"Engine"));
    var assembly=context.LoadFromAssemblyPath(assemblyFile);
    var version=assembly.GetName().Version?.ToString(3);
    if(version!="1.0.0")throw new Exception($"Replay requires the unchanged 1.0.0 backend assembly; received {version}.");
    var pathsType=assembly.GetType("NInferManager.Backend.AppPaths",true)!;
    var settingsType=assembly.GetType("NInferManager.Backend.SettingsService",true)!;
    var updaterType=assembly.GetType("NInferManager.Backend.UpdateService",true)!;
    var paths=Activator.CreateInstance(pathsType)!;
    var settings=Activator.CreateInstance(settingsType,paths)!;
    updater=(IDisposable)Activator.CreateInstance(updaterType,paths,settings)!;
    var actualPortable=(bool)pathsType.GetProperty("IsPortable")!.GetValue(paths)!;
    if(actualPortable!=portable)throw new Exception("Legacy package-type detection disagrees with the fixture.");
    var assetName=portable?"NInferEZ-Manager-Portable-1.1.0.zip":"NInferEZ-Manager-Setup-1.1.0.exe";
    var assetUrl="https://github.com/BenGamliel/NInferEZ-Manager/releases/download/v1.1.0/"+assetName;
    string sha;
    await using(var source=File.OpenRead(package))sha=Convert.ToHexString(await SHA256.HashDataAsync(source)).ToLowerInvariant();
    var size=new FileInfo(package).Length;
    var requests=new List<string>();
    var http=new HttpClient(new ReplayHandler(request=>
    {
        var url=request.RequestUri!.AbsoluteUri;requests.Add(url);
        if(url=="https://api.github.com/repos/BenGamliel/NInferEZ-Manager/releases/latest")
        {
            var feed=new{tag_name="v1.1.0",html_url="https://github.com/BenGamliel/NInferEZ-Manager/releases/tag/v1.1.0",assets=new[]{new{name=assetName,browser_download_url=assetUrl,digest="sha256:"+sha,size}}};
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(feed),Encoding.UTF8,"application/json")};
        }
        if(url!=assetUrl)throw new Exception("Legacy updater requested an unexpected URL.");
        var content=new StreamContent(File.OpenRead(package));content.Headers.ContentLength=size;
        return new(HttpStatusCode.OK){Content=content};
    })){Timeout=Timeout.InfiniteTimeSpan};
    // The old constructor has no injection parameter. Replace its instance-only HTTP
    // transport in this isolated test; the service methods and trust rules are unchanged.
    var httpField=updaterType.GetField("_client",BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("Legacy HTTP transport field is missing.");
    ((HttpClient)httpField.GetValue(updater)!).Dispose();httpField.SetValue(updater,http);
    var check=await Invoke(updaterType.GetMethod("CheckAsync")!,updater,CancellationToken.None)??throw new Exception("Legacy check returned no metadata.");
    var info=check.GetType();
    if(!(bool)info.GetProperty("UpdateAvailable")!.GetValue(check)! || (string?)info.GetProperty("LatestVersion")!.GetValue(check)!="1.1.0" || (string?)info.GetProperty("AssetName")!.GetValue(check)!=assetName)throw new Exception("Legacy client did not offer the matching 1.1.0 update asset.");
    await Invoke(updaterType.GetMethod("DownloadAsync")!,updater,check,CancellationToken.None);
    await Invoke(updaterType.GetMethod("VerifyReadyPackageAsync")!,updater,CancellationToken.None);
    var progress=updaterType.GetProperty("Current")!.GetValue(updater)!;
    var staged=(string?)progress.GetType().GetProperty("PackagePath")!.GetValue(progress)??throw new Exception("Legacy service did not stage the verified package.");
    string stagedSha;await using(var stagedFile=File.OpenRead(staged))stagedSha=Convert.ToHexString(await SHA256.HashDataAsync(stagedFile)).ToLowerInvariant();
    if(stagedSha!=sha || new FileInfo(staged).Length!=size)throw new Exception("Legacy staged package differs from the selected final package.");
    var report=new{legacyVersion=version,legacyAssemblySha256=Hash(assemblyFile),packageType=portable?"portable":"installed",latestVersion="1.1.0",assetName,packagePath=package,stagedPath=staged,bytes=size,sha256=sha,requests,checkedAndDownloadedAndReverified=true,applicationOrHelperLaunched=false,transport="isolated in-memory replay; original service code and URL/hash/size checks unchanged"};
    Directory.CreateDirectory(Path.GetDirectoryName(reportFile)!);await File.WriteAllTextAsync(reportFile,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"PASS: original Manager1.0.0 offered, downloaded and reverified {assetName} ({size} bytes, SHA256 {sha}).");
}
finally
{
    updater?.Dispose();
    if(portable && File.Exists(marker))File.Delete(marker);
    foreach(var pair in previous)Environment.SetEnvironmentVariable(pair.Key,pair.Value);
    context.Unload();
}
static string Hash(string path){using var input=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();}
static async Task<object?> Invoke(MethodInfo method,object instance,params object[] args)
{
    var task=(Task)method.Invoke(instance,args)!;await task;
    return task.GetType().GetProperty("Result")?.GetValue(task);
}
sealed class LegacyContext(string directory):AssemblyLoadContext(isCollectible:true)
{
    protected override Assembly? Load(AssemblyName name)
    {
        if(name.Name is not ("NInferManager.Backend" or "NInferManager.Contracts"))return null;
        return LoadFromAssemblyPath(Path.Combine(directory,name.Name+".dll"));
    }
}
sealed class ReplayHandler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(response(request));
}
