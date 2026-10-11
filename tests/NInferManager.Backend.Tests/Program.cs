using NInferManager.Backend;
using NInferManager.Contracts;
using System.Security.Cryptography;

if (args.Length==4 && args[0]=="--memory-fit-library")
{
    MemoryMetadataChecks.VerifyLibrary(args[1],args[2],int.Parse(args[3]));
    return;
}

var root=Path.Combine(Path.GetTempPath(),"ninfer-manager-winui-tests",Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("NINFEREZ_MANAGER_DATA_ROOT",Path.Combine(root,"Data"));
Environment.SetEnvironmentVariable("NINFEREZ_MANAGER_MODELS_ROOT",Path.Combine(root,"Models"));
Environment.SetEnvironmentVariable("NINFEREZ_MANAGER_ENGINE_ROOT",Path.Combine(root,"Engine"));
try
{
    var paths=new AppPaths();
    var settings=new SettingsService(paths);
    if(args.SequenceEqual(new[]{"--converter-only"}))
    {
        await ConversionChecks.RunAsync(paths,new ManagerLog(paths),Assert);
        return;
    }
    Assert(settings.Current.PublicPort==8173,"default port");
    Assert(!settings.Current.AllowLocalNetwork,"local-network access is disabled by default");
    Assert(EnginePackageService.RecommendArchitecture("NVIDIA GeForce RTX 5080")=="sm120a"&&EnginePackageService.RecommendArchitecture("NVIDIA GeForce RTX 5090")=="sm120a","RTX 5000 Series maps to the Blackwell sm120a package");
    Assert(EnginePackageService.RecommendArchitecture("NVIDIA GeForce RTX 4090")=="sm89","RTX 4000 Series maps to sm89");
    Assert(EnginePackageService.RecommendArchitecture("NVIDIA GeForce RTX 3090")=="sm86","RTX 3000 Series maps to sm86");
    Directory.CreateDirectory(paths.EngineDirectory);
    await File.WriteAllBytesAsync(Path.Combine(paths.EngineDirectory,"ninfer-serve.exe"),[0]);
    await File.WriteAllTextAsync(Path.Combine(paths.EngineDirectory,"engine-manifest.json"),"""
        {"product":"NInferEZ Engine","contractVersion":1,"engineVersion":"1.0.0","cudaArchitecture":"sm120a"}
        """);
    using(var enginePackages=new EnginePackageService(paths,new ManagerLog(paths),settings))
    {
        var engineLibrary=await enginePackages.SnapshotAsync();
        Assert(enginePackages.ActiveDirectory==paths.EngineDirectory,"bundled engine remains the active launch directory when no managed engine is selected");
        Assert(engineLibrary.ActiveArchitecture=="sm120a"&&engineLibrary.Packages.Single(x=>x.CudaArchitecture=="sm120a").Active,"bundled engine manifest is recognized as the installed active package");
    }
    Assert(EditableSettingTypes.Supports(typeof(string))&&EditableSettingTypes.Supports(typeof(bool))&&EditableSettingTypes.Supports(typeof(int))&&EditableSettingTypes.Supports(typeof(double?))&&EditableSettingTypes.Supports(typeof(KvPrecision)),"advanced settings accept supported scalar types");
    Assert(!EditableSettingTypes.Supports(typeof(Dictionary<string,string>))&&!EditableSettingTypes.Supports(typeof(Dictionary<string,ModelProfile>))&&!EditableSettingTypes.Supports(typeof(DateTimeOffset?)),"advanced settings reject dictionaries and unsupported complex types");
    var catalog=new ModelCatalog(paths,settings);
    Assert(catalog.Entries.Count==4,"the curated catalog includes only our four published artifacts");
    Assert(catalog.Entries.All(x=>x.Repository.StartsWith("2beng2/",StringComparison.Ordinal)),"recommendations contain no third-party model download links");
    Assert(catalog.Entries.All(x=>x.Revision.Length==40&&x.Revision.All(Uri.IsHexDigit)),"every bundled catalog download is pinned to an immutable revision");
    var swift=catalog.Entries.Single(x=>x.ModelId=="swift-1.5-qwen3.8-27b-uncensored");
    var swiftProfile=swift.RecommendedProfile();
    Assert(swift.ArtifactVersion==3&&swift.DownloadAvailable&&swift.Revision!="main","Swift is downloaded from a pinned public revision");
    Assert(swiftProfile.MaxContext==262_144&&swiftProfile.CustomKvCapacity==262_144,"Swift context and KV capacity are 256K");
    Assert(swiftProfile.DefaultMaxTokens==32_768,"Swift output limit stays practical");
    Assert(swiftProfile.KvPrecision==KvPrecision.Nvfp4,"Swift uses NVFP4 KV");
    Assert(swiftProfile.SpeculativeMode==SpeculativeMode.Dflash2&&swiftProfile.DraftTokens==7&&swiftProfile.LmHeadDraft,"Swift uses DFlash2 K7 with proposal head");
    Assert(swiftProfile.PrefillChunk==2048&&swiftProfile.VisionEnabled,"Swift uses prefill 2048 with vision");
    Directory.CreateDirectory(paths.ModelsDirectory);
    swift.SizeBytes=4;
    await File.WriteAllBytesAsync(Path.Combine(paths.ModelsDirectory,swift.FileName),new byte[4]);
    Assert(catalog.Snapshot().Single(x=>x.ModelId==swift.ModelId).Installed,"bundled Swift artifact is detected on startup");
    var localArtifact=Path.Combine(paths.ModelsDirectory,"my-local-artifact.ninfer");
    await File.WriteAllBytesAsync(localArtifact,[1,2,3,4,5]);
    Assert(catalog.RefreshLocalArtifacts()==1,"unknown local NInfer artifact is discovered dynamically");
    var discovered=catalog.Snapshot().Single(x=>x.DisplayName=="my-local-artifact");
    Assert(discovered.Installed&&!discovered.DownloadAvailable,"local model is exposed without a catalog download action");
    settings.Current.ActiveModelFile=swift.FileName;
    settings.ProfileFor(swift);
    settings.Save();
    var swiftEngine=new EngineController(paths,settings,catalog,new ManagerLog(paths));
    var swiftArgs=swiftEngine.BuildArguments();
    Assert(Has(swiftArgs,"--max-context","262144")&&Has(swiftArgs,"--kv-capacity","auto"),"Managed Swift keeps its context ceiling and fits KV capacity using the engine");
    Assert(Has(swiftArgs,"--kv-dtype","nvfp4"),"Swift launch command uses NVFP4 KV");
    Assert(Has(swiftArgs,"--spec","dflash2")&&Has(swiftArgs,"--draft-tokens","7")&&swiftArgs.Contains("--lm-head-draft"),"Swift launch command uses DFlash2 K7");
    Assert(swiftArgs.Contains("--vision")&&Has(swiftArgs,"--prefill-chunk","2048"),"Swift launch command uses vision and prefill 2048");
    settings.Current.ModelAliases[swift.FileName]="my-local-model";
    Assert(settings.ModelIdFor(swift)=="my-local-model","user API model name overrides the catalog name");
    Assert(Has(swiftEngine.BuildArguments(),"--model-id","my-local-model"),"engine launch uses the user API model name");
    Assert(catalog.Snapshot().Single(x=>x.FileName==swift.FileName).ModelId=="my-local-model","model inventory exposes the user API model name");
    settings.Current.ModelAliases.Remove(swift.FileName);
    var model=swift;
    settings.Current.ActiveModelFile=model.FileName;
    var profile=settings.ProfileFor(model);
    profile.AutoContext=false;
    profile.MaxContext=150_000;
    profile.CustomKvCapacity=150_000;
    Assert(profile.MaxContext==150_000,"manual NVFP4 context selection");
    Assert(profile.PrefillChunk==2048,"prefill 2048");
    Assert(profile.VisionEnabled,"vision enabled");
    Assert(profile.SpeculativeMode==SpeculativeMode.Dflash2,"DFlash2 enabled");
    Assert(profile.DraftTokens==7,"K7");
    Assert(profile.LmHeadDraft,"optimized proposal head enabled");
    profile.ChatTemplateFile="custom-template.jinja";
    profile.ManageAdvancedSettings=false;
    profile.LogLevel=EngineLogLevel.Debug;
    Assert(profile.HostKvMiB==0&&profile.HostStateSlots==0,"RAM cache disabled");
    settings.Save();
    var reloaded=new SettingsService(paths);
    Assert(reloaded.Current.ActiveModelFile==model.FileName,"settings persist");
    using var log=new ManagerLog(paths);
    var engine=new EngineController(paths,reloaded,catalog,log);
    var engineArgs=engine.BuildArguments();
    Assert(Has(engineArgs,"--max-context","150000"),"command context");
    Assert(Has(engineArgs,"--kv-capacity","150000"),"KV capacity matches the configured context");
    Assert(Has(engineArgs,"--prefill-chunk","2048"),"command prefill");
    Assert(Has(engineArgs,"--spec","dflash2"),"command speculative mode");
    Assert(engineArgs.Contains("--lm-head-draft"),"command optimized proposal head");
    Assert(Has(engineArgs,"--chat-template","custom-template.jinja"),"command custom v0.8 chat template");
    Assert(Has(engineArgs,"--log-level","debug"),"command log level");
    Assert(engineArgs.Contains("--vision"),"command vision");
    Assert(!engineArgs.Contains("--webui")&&!engineArgs.Contains("--webui-dir"),"WinUI does not start the unused engine web UI");
    var iq=catalog.Entries.Single(x=>x.ModelId=="qwen3.8-27b-orcarouter-iq3-xxs");
    settings.Current.ActiveModelFile=iq.FileName;
    settings.Current.Profiles.Remove(iq.FileName);
    var iqArgs=new EngineController(paths,settings,catalog,log).BuildArguments();
    Assert(Has(iqArgs,"--kv-dtype","rk8v4")&&Has(iqArgs,"--spec","mtp"),"IQ3 uses measured RK8V4 and MTP");
    Assert(iqArgs.Contains("--gdn-state-fp16")&&Has(iqArgs,"--mtp-attention-window","8192"),"IQ3 uses its tuned recurrent-state profile");
    Assert(!iq.Vision&&!iqArgs.Contains("--vision"),"IQ3 never receives the unsupported Vision flag");
    Assert(!iqArgs.Contains("--media-live-mib")&&!iqArgs.Contains("--media-cache-mib")&&!iqArgs.Contains("--media-preprocess-threads"),"text-only launch omits all inactive media budgets, never media-live zero");
    Assert(Has(swiftArgs,"--media-live-mib","2048"),"vision launch retains the positive recommended live image budget");
    var originalIqProfile=SettingsService.Clone(settings.ProfileFor(iq));
    var staleIqProfile=settings.ProfileFor(iq);staleIqProfile.ManageAdvancedSettings=false;staleIqProfile.AutoContext=false;
    staleIqProfile.MaxContext=32768;staleIqProfile.DefaultMaxTokens=4096;staleIqProfile.SpeculativeMode=SpeculativeMode.Disabled;
    staleIqProfile.LmHeadDraft=true;staleIqProfile.MtpAttentionWindow=8192;staleIqProfile.NgramDraftTokens=15;
    var disabledArgs=new EngineController(paths,settings,catalog,log).BuildArguments();
    Assert(!disabledArgs.Contains("--lm-head-draft")&&!disabledArgs.Contains("--mtp-attention-window")&&!disabledArgs.Contains("--ngram-draft-tokens")&&!disabledArgs.Contains("--draft-tokens")&&!disabledArgs.Contains("--spec"),"disabled launch strips stale acceleration flags from real command construction");
    Assert(Has(disabledArgs,"--max-context","32768")&&Has(disabledArgs,"--kv-capacity","32768"),"manual 32K context reaches both context and KV flags without automatic override");
    Assert(staleIqProfile.LmHeadDraft&&staleIqProfile.MtpAttentionWindow==8192,"building a safe command does not destroy inactive manual preferences");
    staleIqProfile.AlternativePrefixCache=true;staleIqProfile.PrefixReuseEnabled=true;
    staleIqProfile.DeviceStateSlots=4;staleIqProfile.HostStateSlots=3;staleIqProfile.HostKvMiB=512;staleIqProfile.MaxSharedPrefixes=5;
    var hybridArgs=new EngineController(paths,settings,catalog,log).BuildArguments();
    Assert(hybridArgs.Contains("--use-alt-prefix-caching")&&!hybridArgs.Contains("--device-state-slots")&&!hybridArgs.Contains("--host-state-slots")&&!hybridArgs.Contains("--host-kv-mib")&&!hybridArgs.Contains("--max-shared-prefixes"),"alternative cache launch omits incompatible default-cache flags");
    Assert(staleIqProfile.DeviceStateSlots==4&&staleIqProfile.MaxSharedPrefixes==5,"alternative cache does not destroy manual default-cache preferences");
    settings.Current.Profiles[iq.FileName]=originalIqProfile;
    var external=Path.Combine(root,"existing-model.ninfer");
    await File.WriteAllBytesAsync(external,[1,2,3,4]);
    iq.SizeBytes=4;
    iq.Sha256=Convert.ToHexString(SHA256.HashData([1,2,3,4]));
    using(var downloads=new ModelDownloadService(paths,settings,log))
    {
        await downloads.ImportAsync(external,iq,CancellationToken.None);
        Assert(catalog.IsInstalled(iq)&&catalog.IsExternal(iq),"external model is linked as installed");
        Assert(catalog.ResolvePath(iq)==external,"engine resolves the original external path");
        Assert(!File.Exists(Path.Combine(paths.ModelsDirectory,iq.FileName)),"linking does not copy the model");
        Assert(await downloads.VerifyAsync(iq,CancellationToken.None),"external model verifies in place");
    }
    Assert(new EngineController(paths,settings,catalog,log).BuildArguments()[0]==external,"launch command uses linked model path");
    var staleSnapshot=new ManagerSettings { ActiveModelFile=swift.FileName, Theme=ThemePreference.Dark };
    settings.Replace(staleSnapshot);
    Assert(settings.Current.ActiveModelFile==iq.FileName,"saving a stale settings view preserves active model");
    Assert(settings.Current.ExternalModelPaths.TryGetValue(iq.FileName,out var linkedPath)&&linkedPath==external,"saving a stale settings view preserves external model links");
    Assert(settings.Current.Theme==ThemePreference.Dark,"ordinary settings still update");
    Assert(PortService.IsAvailable(0)==false,"invalid port rejected");
    var apiPort=Enumerable.Range(28173,100).First(PortService.IsAvailable);
    settings.Current.PublicPort=apiPort;
    settings.Current.LockPublicPort=true;
    settings.Current.ApiKey=string.Empty;
    settings.Current.ModelAliases[iq.FileName]="integration-test-model";
    settings.Save();
    var apiEngine=new EngineController(paths,settings,catalog,log);
    await using(var proxy=new PublicApiProxy(apiEngine,settings,catalog,log))
    {
        await proxy.StartAsync();
        Assert(!proxy.LocalNetworkEnabled,"public API binds to localhost by default");
        using var client=new HttpClient(new SocketsHttpHandler{UseProxy=false});
        var healthJson=await client.GetStringAsync($"http://127.0.0.1:{apiPort}/health");
        Assert(healthJson.Contains("\"status\":\"ok\"",StringComparison.Ordinal),"public health endpoint is available before model load");
        var modelsJson=await client.GetStringAsync($"http://127.0.0.1:{apiPort}/v1/models");
        Assert(modelsJson.Contains("integration-test-model",StringComparison.Ordinal),"unloaded API returns the configured model name");
        Assert(modelsJson.Contains("swift-1.5-qwen3.8-27b-uncensored",StringComparison.Ordinal),"unloaded API exposes every installed model");
        using var unknownRequest=new StringContent("{\"model\":\"missing-model\",\"messages\":[]}",System.Text.Encoding.UTF8,"application/json");
        using var unknownResponse=await client.PostAsync($"http://127.0.0.1:{apiPort}/v1/chat/completions",unknownRequest);
        Assert((int)unknownResponse.StatusCode==404,"unknown API model is rejected before loading the engine");
        using var preflight=new HttpRequestMessage(HttpMethod.Options,$"http://127.0.0.1:{apiPort}/v1/chat/completions");
        using var preflightResponse=await client.SendAsync(preflight);
        Assert((int)preflightResponse.StatusCode==204&&preflightResponse.Headers.TryGetValues("Access-Control-Allow-Origin",out var origins)&&origins.Contains("*"),"CORS preflight works when enabled");
        Assert(apiEngine.State==EngineState.Unloaded,"GET /v1/models does not load the engine or allocate GPU memory");
    }
    var lanApiPort=Enumerable.Range(apiPort+1,100).First(PortService.IsAvailable);
    settings.Current.PublicPort=lanApiPort;
    settings.Current.AllowLocalNetwork=true;
    settings.Current.ApiKey="host-only-lan-test-key";
    settings.Save();
    await using(var lanProxy=new PublicApiProxy(apiEngine,settings,catalog,log))
    {
        await lanProxy.StartAsync();
        Assert(lanProxy.LocalNetworkEnabled,"public API can bind to all local-network interfaces when explicitly enabled");
        using var client=new HttpClient(new SocketsHttpHandler{UseProxy=false});
        var healthJson=await client.GetStringAsync($"http://127.0.0.1:{lanApiPort}/health");
        Assert(healthJson.Contains("\"status\":\"ok\"",StringComparison.Ordinal),"local API remains reachable when local-network access is enabled");
    }
    var valid=settings.Snapshot();
    var patchChanges=System.Text.Json.JsonSerializer.SerializeToElement(new {idleMinutes=0d},SettingsService.JsonOptions);
    var patched=settings.Patch(new(valid.Revision,patchChanges));
    Assert(patched.IdleMinutes==0,"immediate after-request memory policy is valid");
    try{settings.Patch(new(valid.Revision,patchChanges));throw new Exception("stale patch unexpectedly succeeded");}catch(InvalidOperationException){Assert(true,"stale settings patch is rejected");}
    Assert(!catalog.IsExternal(catalog.Find(discovered.FileName)!),"Models discovery does not create external links");
    Assert(GpuMonitor.Parse("0, GPU-one, NVIDIA GeForce RTX 5090, 1000, 32000, N/A, N/A\n1, GPU-two, NVIDIA GeForce RTX 4090, 2000, 24000, 2, 31").Count==2,"GPU parsing supports multiple devices and unavailable telemetry");
    try{EngineController.RecommendContext(iq,new("tiny",0,1024,0,0));throw new Exception("no-fit context unexpectedly succeeded");}catch(InvalidOperationException){Assert(true,"no-fit automatic context fails explicitly");}
    Assert(!catalog.FindInstalledByApiName(" Integration-test-model ")?.FileName.Equals(iq.FileName,StringComparison.OrdinalIgnoreCase)??true,"API model routing requires the canonical identifier");
    await RegressionChecks.RunAsync(paths,settings,catalog,log,Assert);
    ProfilePolicyChecks.Run(Assert);
    MemoryFitChecks.Run(Assert);
    MemoryMetadataChecks.Run(paths,settings,catalog,Assert);
    ModelVariantChecks.Run(paths,settings,catalog,Assert);
    await ModelAnnouncementChecks.RunAsync(paths,settings,log,Assert);
    await ConversionChecks.RunAsync(paths,log,Assert);
    PublisherCatalogChecks.Run(root,Assert);
    Console.WriteLine($"All {CheckCount.Count} backend contract, file I/O and fake-engine checks passed. No real model or GPU inference was loaded.");
}
finally
{
    try{Directory.Delete(root,true);}catch{}
    Environment.SetEnvironmentVariable("NINFEREZ_MANAGER_DATA_ROOT",null);
    Environment.SetEnvironmentVariable("NINFEREZ_MANAGER_MODELS_ROOT",null);
    Environment.SetEnvironmentVariable("NINFEREZ_MANAGER_ENGINE_ROOT",null);
}
static bool Has(IReadOnlyList<string> values,string name,string value){for(var i=0;i<values.Count-1;i++)if(values[i]==name&&values[i+1]==value)return true;return false;}
static void Assert(bool ok,string name){if(!ok)throw new InvalidOperationException("FAILED: "+name);CheckCount.Count++;Console.WriteLine("PASS: "+name);}
static class CheckCount{public static int Count;}
