using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NInferManager.Backend;
using NInferManager.Contracts;

internal static class RegressionChecks
{
    public static async Task RunAsync(AppPaths paths,SettingsService settings,ModelCatalog catalog,ManagerLog log,Action<bool,string> assert)
    {
        var bytes=Enumerable.Range(0,32).Select(x=>(byte)x).ToArray();
        var download=new ModelCatalogEntry{FileName="download-fixture.ninfer",Repository="fixture/test",Revision=new string('a',40),SizeBytes=bytes.Length,Sha256=Convert.ToHexString(SHA256.HashData(bytes))};
        var final=Path.Combine(paths.ModelsDirectory,download.FileName);
        using(var service=new ModelDownloadService(paths,settings,log,new HttpClient(new FixtureHandler(bytes))))
        {
            await service.DownloadAsync(download,CancellationToken.None);
            assert(File.ReadAllBytes(final).SequenceEqual(bytes),"Windows download closes writer and verifier before committing");
        }
        File.Delete(final);
        var part=final+".part";
        File.WriteAllBytes(part,bytes[..9]);
        File.WriteAllText(part+".json",$"{download.DownloadUrl}\n{download.SizeBytes}\n{download.Sha256.ToLowerInvariant()}");
        var resume=new FixtureHandler(bytes);
        using(var service=new ModelDownloadService(paths,settings,log,new HttpClient(resume)))await service.DownloadAsync(download,CancellationToken.None);
        assert(resume.Offset==9&&File.ReadAllBytes(final).SequenceEqual(bytes),"Range resume validates and appends the correct offset");
        File.Delete(final);File.WriteAllBytes(part,bytes[..9]);
        File.WriteAllText(part+".json",$"{download.DownloadUrl}\n{download.SizeBytes}\n{download.Sha256.ToLowerInvariant()}");
        using(var service=new ModelDownloadService(paths,settings,log,new HttpClient(new FixtureHandler(bytes,ignoreRange:true))))await service.DownloadAsync(download,CancellationToken.None);
        assert(File.ReadAllBytes(final).SequenceEqual(bytes),"server ignoring Range safely restarts download");
        File.Delete(final);File.WriteAllBytes(part,bytes[..9]);
        File.WriteAllText(part+".json",$"{download.DownloadUrl}\n{download.SizeBytes}\n{download.Sha256.ToLowerInvariant()}");
        using(var service=new ModelDownloadService(paths,settings,log,new HttpClient(new FixtureHandler(bytes,badRange:true))))
        {
            try{await service.DownloadAsync(download,CancellationToken.None);assert(false,"bad range unexpectedly accepted");}
            catch(InvalidDataException){assert(new FileInfo(part).Length==9,"incorrect Content-Range is rejected without corrupting partial");}
        }
        File.Delete(part);File.Delete(part+".json");
        using(var service=new ModelDownloadService(paths,settings,log,new HttpClient(new FixtureHandler(bytes.Reverse().ToArray()))))
        {
            try{await service.DownloadAsync(download,CancellationToken.None);assert(false,"bad SHA unexpectedly accepted");}
            catch(InvalidDataException){assert(!File.Exists(final)&&!File.Exists(part),"hash mismatch never commits and removes untrusted partial");}
        }

        var fullProfile=new ModelProfile{AutoContext=false,MaxContext=65536,DefaultMaxTokens=4096,KvPrecision=KvPrecision.Rk8v4,KvCapacityMode=KvCapacityMode.Custom,CustomKvCapacity=49152,SpeculativeMode=SpeculativeMode.Mtp,DraftTokens=3,MtpAttentionWindow=8192,VisionEnabled=false,ThinkingEnabled=false,PreserveThinking=false,CudaGraphEnabled=false,Temperature=0.6,TopP=0.85,TopK=24,HostStateSlots=2,HostKvMiB=512};
        var preset=Path.Combine(paths.DataDirectory,"roundtrip.ninferez.json");
        File.WriteAllText(preset,ModelPresetImporter.Export(download,fullProfile));
        var imported=ModelPresetImporter.Read(preset,download,new ModelProfile());
        assert(JsonSerializer.Serialize(imported,SettingsService.JsonOptions)==JsonSerializer.Serialize(fullProfile,SettingsService.JsonOptions),"Manager preset roundtrips all typed runtime, cache, sampling and thinking settings");
        var badPreset=File.ReadAllText(preset).Replace("\"mode\": \"exact\"","\"mode\": \"invented\"");File.WriteAllText(preset,badPreset);
        try{ModelPresetImporter.Read(preset,download,new ModelProfile());assert(false,"unknown preset mode accepted");}catch(InvalidDataException){assert(true,"unknown preset mode fails validation");}

        var saved=settings.Snapshot();
        Directory.CreateDirectory(paths.SettingsFile+".tmp");
        try
        {
            try{settings.Patch(new(saved.Revision,JsonSerializer.SerializeToElement(new{idleMinutes=17d})));assert(false,"save unexpectedly succeeded");}
            catch(UnauthorizedAccessException){assert(settings.Current.Revision==saved.Revision&&settings.Current.IdleMinutes==saved.IdleMinutes,"failed settings commit retains previous in-memory state and revision");}
        }
        finally{Directory.Delete(paths.SettingsFile+".tmp");}
        assert(EnginePackageService.RecommendArchitecture("NVIDIA A100 Ampere") is null&&EnginePackageService.RecommendArchitecture("NVIDIA B200 Blackwell") is null,"generic datacenter architecture names do not select RTX runtime");
        var a=Path.Combine(paths.DataDirectory,"A");var b=Path.Combine(paths.DataDirectory,"B");Directory.CreateDirectory(a);Directory.CreateDirectory(b);
        var aModel=Path.Combine(a,"same-name.ninfer");var bModel=Path.Combine(b,"same-name.ninfer");File.WriteAllBytes(aModel,bytes);File.WriteAllBytes(bModel,bytes.Reverse().ToArray());
        var first=catalog.LinkExternal(aModel);var second=catalog.LinkExternal(bModel);
        assert(first.FileName!=second.FileName&&catalog.ResolvePath(first)==aModel&&catalog.ResolvePath(second)==bModel&&first.ModelId!=second.ModelId,"same-name same-size different paths retain independent library entries and API IDs");
        var malformed=Path.Combine(paths.ModelsDirectory,"malformed-sidecar.ninfer");File.WriteAllBytes(malformed,bytes);
        File.WriteAllText(Path.ChangeExtension(malformed,".ninferez.json"),"{\"schemaVersion\":\"wrong-type\"}");
        catalog.RefreshLocalArtifacts();
        var malformedEntry=catalog.Entries.Single(x=>x.DisplayName=="malformed-sidecar");
        assert(malformedEntry.InvalidAdjacentPreset&&catalog.Snapshot().Any(x=>x.FileName==malformedEntry.FileName),"malformed sidecar kinds are isolated to their model without breaking discovery");
        CheckCatalogRestart(paths,bytes,fullProfile,assert);
        var sanitized=SystemOperations.Sanitize("--api-key secret123\nAuthorization: Bearer token456\nFile: Z:\\private\\model.ninfer",paths);
        assert(!sanitized.Contains("secret123")&&!sanitized.Contains("token456")&&!sanitized.Contains("Z:\\private"),"diagnostics scrub CLI keys, Bearer tokens and arbitrary drive paths");

        var configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var fakeRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","NInferManager.FakeEngine","bin",configuration,"net10.0-windows10.0.19041.0","win-x64"));
        if(!Directory.Exists(fakeRoot))throw new DirectoryNotFoundException(fakeRoot);
        foreach(var file in Directory.EnumerateFiles(fakeRoot))File.Copy(file,Path.Combine(paths.EngineDirectory,Path.GetFileName(file)),true);
        File.Copy(Path.Combine(fakeRoot,"NInferManager.FakeEngine.exe"),Path.Combine(paths.EngineDirectory,"ninfer-serve.exe"),true);
        File.WriteAllBytes(final,bytes);
        var local=catalog.LinkExternal(final);
        var p=local.RecommendedProfile();p.AutoContext=false;p.MaxContext=32768;p.DefaultMaxTokens=256;p.VisionEnabled=false;p.SpeculativeMode=SpeculativeMode.Disabled;
        var publicPort=Enumerable.Range(29200,100).First(PortService.IsAvailable);
        var backendPort=Enumerable.Range(29300,100).First(PortService.IsAvailable);
        settings.Update(s=>{s.ActiveModelFile=local.FileName;s.Profiles[local.FileName]=p;s.AllowLocalNetwork=false;s.ApiKey="public-fixture-key";s.PublicPort=publicPort;s.BackendPort=backendPort;s.LockPublicPort=true;s.AutoUnloadEnabled=false;});
        await using var engine=new EngineController(paths,settings,catalog,log);
        await engine.LoadAsync();
        assert(engine.IsLoaded&&engine.Session is not null,"fake engine readiness succeeds with required private API key");
        var session=engine.Session!;
        settings.Update(s=>{s.BackendPort=backendPort+1;s.ApiKey="changed-public-key";s.Profiles[local.FileName].MaxContext=65536;});
        assert(engine.Port==backendPort&&engine.Session!.Profile.MaxContext==32768&&engine.InternalApiKey!=settings.Current.ApiKey,"loaded session keeps actual port, profile and private key after settings change");
        await using(var proxy=new PublicApiProxy(engine,settings,catalog,log))
        {
            await proxy.StartAsync();
            await proxy.RestartAsync(publicPort,true,true,"listener-only-key");
            using(var unauthorized=new HttpClient(new SocketsHttpHandler{UseProxy=false}))
            {
                using var denied=await unauthorized.GetAsync($"http://127.0.0.1:{publicPort}/v1/models");
                assert(denied.StatusCode==HttpStatusCode.Unauthorized,"new network listener enforces its captured key before settings are persisted");
                unauthorized.DefaultRequestHeaders.Authorization=new("Bearer","listener-only-key");
                using var allowed=await unauthorized.GetAsync($"http://127.0.0.1:{publicPort}/v1/models");
                assert(allowed.IsSuccessStatusCode&&settings.Current.ApiKey=="changed-public-key","listener runtime authentication is independent of old stored credentials");
            }
            await proxy.RestartAsync(publicPort,true,false,"changed-public-key");
            using var client=new HttpClient(new SocketsHttpHandler{UseProxy=false});client.DefaultRequestHeaders.Authorization=new("Bearer","changed-public-key");
            using(var response=await client.PostAsync($"http://127.0.0.1:{publicPort}/v1/chat/completions",new StringContent(JsonSerializer.Serialize(new{model=settings.ModelIdFor(local),messages=Array.Empty<object>()}),Encoding.UTF8,"application/json")))
                assert(response.IsSuccessStatusCode&&(await response.Content.ReadAsStringAsync()).Contains("host-only reply"),"proxy forwards using immutable session key after changing public credentials");
            var streaming=new HttpRequestMessage(HttpMethod.Post,$"http://127.0.0.1:{publicPort}/v1/chat/completions"){Content=new StringContent(JsonSerializer.Serialize(new{model=settings.ModelIdFor(local),stream=true,messages=Array.Empty<object>()}),Encoding.UTF8,"application/json")};
            using var streamResponse=await client.SendAsync(streaming,HttpCompletionOption.ResponseHeadersRead);
            var unload=Task.Run(async()=>{await engine.LifecycleGate.WaitAsync();try{await engine.UnloadAsync("host-only concurrent unload");}finally{engine.LifecycleGate.Release();}});
            await Task.Delay(40);
            assert(!unload.IsCompleted,"manual unload waits while a streaming request owns the lifecycle");
            assert((await streamResponse.Content.ReadAsStringAsync()).Contains("[DONE]"),"SSE response remains intact during requested unload");
            await unload;
            assert(engine.State==EngineState.Unloaded&&engine.Session is null,"unload completes only after owned process exits");
            await proxy.SetEnabledAsync(false);assert(!proxy.IsEnabled,"API listener can be disabled without loading a model");
            await proxy.SetEnabledAsync(true);assert(proxy.IsEnabled,"API listener can be re-enabled without loading a model");
            settings.Update(s=>{s.AutoUnloadEnabled=true;s.IdleMinutes=0;s.BackendPort=backendPort;});
            using var after=await client.PostAsync($"http://127.0.0.1:{publicPort}/v1/chat/completions",new StringContent(JsonSerializer.Serialize(new{model=settings.ModelIdFor(local),messages=Array.Empty<object>()}),Encoding.UTF8,"application/json"));
            assert(after.IsSuccessStatusCode&&engine.State==EngineState.Unloaded,"immediate policy unloads only after response completion");
        }
        Environment.SetEnvironmentVariable("NINFEREZ_FAKE_READY_DELAY","4000");
        try
        {
            engine.StartLoad();await Task.Delay(150);engine.CancelLoad();await engine.UnloadAsync("host-only cancellation");
            assert(engine.State==EngineState.Unloaded&&engine.Session is null,"startup cancellation terminates the owned fake engine process");
            var elapsed=engine.OperationStatus.ElapsedSeconds;await Task.Delay(50);
            assert(engine.OperationStatus.ElapsedSeconds==elapsed,"terminal operation elapsed time remains frozen");
        }
        finally{Environment.SetEnvironmentVariable("NINFEREZ_FAKE_READY_DELAY",null);}
    }

    private static void CheckCatalogRestart(AppPaths outerPaths,byte[] bytes,ModelProfile exact,Action<bool,string> assert)
    {
        var names=new[]{"NINFEREZ_MANAGER_DATA_ROOT","NINFEREZ_MANAGER_MODELS_ROOT","NINFEREZ_MANAGER_ENGINE_ROOT"};
        var original=names.Select(Environment.GetEnvironmentVariable).ToArray();
        var root=Path.Combine(outerPaths.DataDirectory,"restart-fixtures");
        try
        {
            for(var i=0;i<names.Length;i++)Environment.SetEnvironmentVariable(names[i],Path.Combine(root,new[]{"Data","Models","Engine"}[i]));
            var paths=new AppPaths();var settings=new SettingsService(paths);var catalog=new ModelCatalog(paths,settings);
            var external=Path.Combine(root,"External");Directory.CreateDirectory(external);
            var model=Path.Combine(external,"restart-valid.ninfer");File.WriteAllBytes(model,bytes);
            var identity=new ModelCatalogEntry{FileName=Path.GetFileName(model),SizeBytes=bytes.Length,ArtifactVersion=3,Sha256=Convert.ToHexString(SHA256.HashData(bytes))};
            var sidecar=Path.ChangeExtension(model,".ninferez.json");File.WriteAllText(sidecar,ModelPresetImporter.Export(identity,exact));
            var entry=catalog.LinkExternal(model);
            assert(entry.ExpectedSha256==identity.Sha256&&!entry.IdentityVerified&&settings.Current.Profiles[entry.FileName].MaxContext==exact.MaxContext,"adjacent exact preset imports settings and unverified identity for a linked artifact");
            var restartedSettings=new SettingsService(paths);var restarted=new ModelCatalog(paths,restartedSettings);var restored=restarted.Find(entry.FileName)!;
            assert(restored.ExpectedSha256==identity.Sha256&&!restored.IdentityVerified&&restored.PresetStatus?.Contains("unverified")==true,"linked exact preset identity and honest status survive restart");
            assert(JsonSerializer.Serialize(ModelPresetImporter.Read(sidecar,restored,new ModelProfile()),SettingsService.JsonOptions)==JsonSerializer.Serialize(exact,SettingsService.JsonOptions),"restarted linked artifact retains a usable expected identity for preset export/import");

            var custom=SettingsService.Clone(exact);custom.Temperature=0.91;custom.HostCacheMiB=8192;custom.HostStateSlots=0;custom.HostKvMiB=0;custom.NgramDraftTokens=15;
            restartedSettings.Update(s=>s.Profiles[restored.FileName]=custom);restarted.RememberCustomProfile(restored);
            var customSettings=new SettingsService(paths);var customCatalog=new ModelCatalog(paths,customSettings);var customEntry=customCatalog.Find(entry.FileName)!;
            assert(JsonSerializer.Serialize(customSettings.Current.Profiles[entry.FileName],SettingsService.JsonOptions)==JsonSerializer.Serialize(custom,SettingsService.JsonOptions)&&customEntry.PresetStatus?.StartsWith("Custom")==true,"restart never replaces any saved custom numerical fields with adjacent exact settings");
            assert(ModelPresetImporter.Export(customEntry,custom).Contains(identity.Sha256,StringComparison.OrdinalIgnoreCase),"unknown linked model preset remains exportable after restart without automatic hashing");

            Directory.CreateDirectory(paths.EngineDirectory);var inspector=Path.Combine(paths.EngineDirectory,"ninfer-inspect.exe");File.WriteAllBytes(inspector,[1,2,3]);
            customEntry.Inspected=true;customEntry.HasDflash2=true;customEntry.HasMtp=true;customEntry.Vision=true;customEntry.NativeContext=65536;
            customEntry.InspectedModelPath=model;customEntry.InspectedWriteUtc=File.GetLastWriteTimeUtc(model);customEntry.InspectedEngineDirectory=paths.EngineDirectory;customEntry.InspectorWriteUtc=File.GetLastWriteTimeUtc(inspector);
            customCatalog.RememberVerification(customEntry);
            var inspectedCatalog=new ModelCatalog(paths,new SettingsService(paths));var inspected=inspectedCatalog.Find(entry.FileName)!;
            assert(inspected.Inspected&&inspected.HasDflash2&&inspected.HasMtp&&inspected.Vision&&inspected.NativeContext==65536,"cached inspection facts survive restart only with matching model and inspector metadata");
            File.SetLastWriteTimeUtc(model,File.GetLastWriteTimeUtc(model).AddSeconds(5));
            var changedCatalog=new ModelCatalog(paths,new SettingsService(paths));var changed=changedCatalog.Find(entry.FileName)!;
            assert(!changed.Inspected&&!changed.HasDflash2&&!changed.IdentityVerified&&changed.ExpectedSha256==identity.Sha256,"changed artifact invalidates capabilities and verified state while retaining expected preset identity");
            changed.Inspected=true;changed.HasDflash2=true;changed.InspectedModelPath=model;changed.InspectedWriteUtc=File.GetLastWriteTimeUtc(model);changed.InspectedEngineDirectory=paths.EngineDirectory;changed.InspectorWriteUtc=File.GetLastWriteTimeUtc(inspector);
            changedCatalog.RememberVerification(changed);File.SetLastWriteTimeUtc(inspector,File.GetLastWriteTimeUtc(inspector).AddSeconds(5));
            var changedInspectorCatalog=new ModelCatalog(paths,new SettingsService(paths));var changedInspector=changedInspectorCatalog.Find(entry.FileName)!;
            assert(!changedInspector.Inspected&&!changedInspector.HasDflash2,"changed inspector invalidates cached capability results after restart");
            var replacement=Path.Combine(external,"replacement.ninfer");File.WriteAllBytes(replacement,bytes);File.SetLastWriteTimeUtc(replacement,File.GetLastWriteTimeUtc(model));
            changedInspector.IdentityVerified=true;changedInspector.VerifiedWriteUtc=File.GetLastWriteTimeUtc(model);changedInspectorCatalog.Relink(changedInspector,replacement);
            assert(!changedInspector.IdentityVerified&&changedInspector.VerifiedWriteUtc is null&&changedInspector.ExpectedSha256==identity.Sha256,"same-size same-timestamp relink cannot retain verified identity");

            var invalid=Path.Combine(paths.ModelsDirectory,"restart-invalid.ninfer");File.WriteAllBytes(invalid,bytes);var invalidSidecar=Path.ChangeExtension(invalid,".ninferez.json");File.WriteAllText(invalidSidecar,"{\"schemaVersion\":\"invalid\"}");
            changedInspectorCatalog.RefreshLocalArtifacts();changedInspectorCatalog.Snapshot();var invalidEntry=changedInspectorCatalog.Find(Path.GetFileName(invalid))!;
            var invalidCatalog=new ModelCatalog(paths,new SettingsService(paths));
            assert(invalidCatalog.Find(invalidEntry.FileName)!.InvalidAdjacentPreset,"invalid adjacent preset remains blocked after Snapshot saves a fallback profile and restart");
            File.Delete(paths.CatalogCacheFile);var absentCacheSettings=new SettingsService(paths);var absentCache=new ModelCatalog(paths,absentCacheSettings);
            assert(absentCache.Find(invalidEntry.FileName)!.InvalidAdjacentPreset,"missing metadata cache cannot bypass invalid sidecar validation merely because a profile exists");
            var overrideEntry=absentCache.Find(invalidEntry.FileName)!;absentCache.RememberCustomProfile(overrideEntry);
            var overrideCatalog=new ModelCatalog(paths,new SettingsService(paths));
            assert(!overrideCatalog.Find(invalidEntry.FileName)!.InvalidAdjacentPreset&&overrideCatalog.Find(invalidEntry.FileName)!.PresetStatus?.StartsWith("Custom")==true,"explicit custom save persists an intentional override of an unchanged invalid sidecar");
            File.WriteAllText(invalidSidecar,"{\"schemaVersion\":\"changed-invalid\"}");var changedSidecar=new ModelCatalog(paths,new SettingsService(paths));
            assert(changedSidecar.Find(invalidEntry.FileName)!.InvalidAdjacentPreset,"changed sidecar invalidates an earlier explicit custom override and is revalidated");
            File.WriteAllText(paths.CatalogCacheFile,"[");var corruptCache=new ModelCatalog(paths,new SettingsService(paths));
            assert(corruptCache.Find(invalidEntry.FileName)!.InvalidAdjacentPreset,"corrupt metadata cache cannot silently clear a malformed adjacent preset guard");
        }
        finally{for(var i=0;i<names.Length;i++)Environment.SetEnvironmentVariable(names[i],original[i]);}
    }

    private sealed class FixtureHandler(byte[] bytes,bool ignoreRange=false,bool badRange=false):HttpMessageHandler
    {
        public long Offset{get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Offset=request.Headers.Range?.Ranges.FirstOrDefault()?.From??0;
            var partial=Offset>0&&!ignoreRange;
            var response=new HttpResponseMessage(partial?HttpStatusCode.PartialContent:HttpStatusCode.OK){Content=new ByteArrayContent(partial?bytes[(int)Offset..]:bytes)};
            if(partial)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(badRange?Offset+1:Offset,bytes.Length-1,bytes.Length);
            return Task.FromResult(response);
        }
    }
}
