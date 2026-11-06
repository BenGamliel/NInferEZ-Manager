using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using NInferManager.Backend;
using NInferManager.Contracts;

// Exact production assemblies, local transport only. This never loads a native model,
// runs a native engine or allocates GPU memory. It verifies and installs the real archive.
if(args.Length!=5)throw new ArgumentException("Usage: buildInfoJson engineArchiveDirectory architecture ownedWorkDirectory outputJson");
var buildFile=Path.GetFullPath(args[0]);var archives=Path.GetFullPath(args[1]);
var architecture=args[2];var work=Path.GetFullPath(args[3]);var reportFile=Path.GetFullPath(args[4]);
using var build=JsonDocument.Parse(await File.ReadAllTextAsync(buildFile));var metadata=build.RootElement;
var backend=typeof(EnginePackageService).Assembly.Location;
var contracts=typeof(ModelProfile).Assembly.Location;
var backendSha=Hash(backend);var contractsSha=Hash(contracts);
if(!backendSha.Equals(metadata.GetProperty("backendAssemblySha256").GetString(),StringComparison.OrdinalIgnoreCase) ||
   !contractsSha.Equals(metadata.GetProperty("contractsAssemblySha256").GetString(),StringComparison.OrdinalIgnoreCase))throw new Exception("Replay assemblies do not match the final Manager build evidence.");
var bundledExe=metadata.GetProperty("backendExecutable").GetString()!;
if(!Hash(bundledExe).Equals(metadata.GetProperty("backendExecutableSha256").GetString(),StringComparison.OrdinalIgnoreCase) ||
   !ContainsFile(bundledExe,backend) || !ContainsFile(bundledExe,contracts))throw new Exception("The replay assemblies cannot be proven byte-identical to components of the final backend executable.");
var release=ReleaseEnginePolicy.Embedded();
if(!release.Approved)throw new Exception("The final Manager binding is not approved; no archive was installed.");
var pin=ReleaseEnginePolicy.Select(release,architecture);
var archive=Path.Combine(archives,pin.FileName);
if(!File.Exists(archive)||new FileInfo(archive).Length!=pin.SizeBytes||!Hash(archive).Equals(pin.Sha256,StringComparison.OrdinalIgnoreCase))throw new Exception("The local corrected engine archive does not match the exact approved release pin.");
var ownerMarker=Path.Combine(work,".manager-engine-replay-owned");
if(Directory.Exists(work)&&Directory.EnumerateFileSystemEntries(work).Any()&&!File.Exists(ownerMarker))throw new Exception("Refusing to alter a non-empty work folder without the replay ownership marker.");
Directory.CreateDirectory(work);File.WriteAllText(ownerMarker,"Manager engine update replay fixture");
var variables=new[]{"NINFEREZ_MANAGER_DATA_ROOT","NINFEREZ_MANAGER_MODELS_ROOT","NINFEREZ_MANAGER_ENGINE_ROOT"};
var previous=variables.ToDictionary(n=>n,Environment.GetEnvironmentVariable);
try
{
    Environment.SetEnvironmentVariable(variables[0],Path.Combine(work,"Data"));
    Environment.SetEnvironmentVariable(variables[1],Path.Combine(work,"Models"));
    Environment.SetEnvironmentVariable(variables[2],Path.Combine(work,"Engine"));
    var paths=new AppPaths();var settings=new SettingsService(paths);var log=new ManagerLog(paths);
    var requests=new List<string>();
    using var client=new HttpClient(new ReplayHandler(request=>
    {
        if(request.RequestUri!.AbsoluteUri!=pin.Url)throw new Exception("Engine replay requested an unexpected URL.");
        requests.Add(request.RequestUri.AbsoluteUri);
        var content=new StreamContent(File.OpenRead(archive));content.Headers.ContentLength=pin.SizeBytes;
        return new(HttpStatusCode.OK){Content=content};
    })){Timeout=Timeout.InfiniteTimeSpan};
    using var packages=new EnginePackageService(paths,log,settings,client);
    var catalog=new ModelCatalog(paths,settings);
    await using var engine=new EngineController(paths,settings,catalog,log,packages);
    var pointerBefore=File.Exists(paths.CurrentEngineFile)?await File.ReadAllTextAsync(paths.CurrentEngineFile):null;
    var oldDirectory=packages.ActiveDirectory;
    var oldExe=Path.Combine(oldDirectory,"ninfer-serve.exe");
    var oldExeSha=File.Exists(oldExe)?Hash(oldExe):null;
    var commits=0;
    packages.CommitInstallation=async(commit,token)=>
    {
        var current=File.Exists(paths.CurrentEngineFile)?await File.ReadAllTextAsync(paths.CurrentEngineFile,token):null;
        if(current!=pointerBefore)throw new Exception("The active engine pointer changed before verification and staging finished.");
        await engine.LifecycleGate.WaitAsync(token);
        try { await engine.UnloadAsync("verified local release replay");commits++;commit(); }
        finally { engine.LifecycleGate.Release(); }
    };
    var coordinator=new ProductUpdateCoordinator(packages,log,architecture:()=>Task.FromResult<string?>(architecture));
    engine.ProductUpdateBlocker=coordinator.LoadBlocker;
    if(coordinator.LoadBlocker() is null)throw new Exception("Pending product update did not block unconfirmed model loads.");
    coordinator.Start();await coordinator.WaitForCompletionAsync();
    if(!coordinator.State.Ready||coordinator.LoadBlocker() is not null)throw new Exception(coordinator.State.Error??"The verified engine setup did not complete.");
    var installed=packages.ActiveDirectory;
    var installedExe=Path.Combine(installed,"ninfer-serve.exe");
    if(ModelVersionPolicy.ReadEngineVersion(installed)!=pin.EngineVersion||!File.Exists(Path.Combine(installed,".manager-package.json")))throw new Exception("Installed engine identity/receipt is missing.");
    if(oldExeSha is not null && !oldDirectory.Equals(installed,StringComparison.OrdinalIgnoreCase) && (!File.Exists(oldExe)||Hash(oldExe)!=oldExeSha))throw new Exception("The previous engine bytes were not preserved.");
    var beforeReuse=requests.Count;
    coordinator.Start();await coordinator.WaitForCompletionAsync();
    if(!coordinator.State.Ready||requests.Count!=beforeReuse||engine.State!=EngineState.Unloaded)throw new Exception("Verified installed pin was fetched again or the native engine was started unexpectedly.");
    var result=new{managerVersion=ProductInfo.Version,architecture,engineVersion=pin.EngineVersion,archive=pin.FileName,archiveBytes=pin.SizeBytes,archiveSha256=pin.Sha256,backendAssemblySha256=backendSha,contractsAssemblySha256=contractsSha,assemblyBytesPresentInFinalExecutable=true,requests,commits,installedDirectory=installed,installedExecutableSha256=Hash(installedExe),previousEnginePreserved=true,verifiedReuseWithoutNetwork=true,coordinatorReady=true,nativeEngineOrModelLaunched=false,gpuMemoryAllocated=false};
    Directory.CreateDirectory(Path.GetDirectoryName(reportFile)!);await File.WriteAllTextAsync(reportFile,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"PASS: exact final Manager assemblies verified, installed and reused {pin.FileName}; no native engine or GPU work was started.");
}
finally { foreach(var item in previous)Environment.SetEnvironmentVariable(item.Key,item.Value); }

static string Hash(string path){using var input=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();}
static bool ContainsFile(string bundle,string component)
{
    var needle=File.ReadAllBytes(component);
    if(needle.Length==0)return false;
    using var input=File.OpenRead(bundle);
    var buffer=new byte[8*1024*1024+needle.Length];var carried=0;
    while(true)
    {
        var read=input.Read(buffer,carried,buffer.Length-carried);
        if(read==0)return false;
        var length=carried+read;
        if(buffer.AsSpan(0,length).IndexOf(needle)>=0)return true;
        carried=Math.Min(needle.Length-1,length);
        buffer.AsSpan(length-carried,carried).CopyTo(buffer);
    }
}
sealed class ReplayHandler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(response(request));
}
