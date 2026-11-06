using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NInferManager.Backend;
using NInferManager.Contracts;

internal static class Release110Checks
{
    public static async Task RunAsync(AppPaths paths, SettingsService settings, ModelCatalog catalog, Action<bool,string> assert)
    {
        assert(ModelVersionPolicy.Meets("1.1.0","1.1.0") && ModelVersionPolicy.Meets("1.10.0","1.1.0"),"stable minimum versions compare numerically");
        assert(!ModelVersionPolicy.Meets("1.0.0","1.1.0") && !ModelVersionPolicy.Meets("1.1.0-preview.1","1.1.0") && !ModelVersionPolicy.Meets(null,"1.1.0"),"old, preview and unknown engines cannot satisfy stable model requirements");
        assert(!ModelVersionPolicy.IsValidMinimum("1.1") && !ModelVersionPolicy.IsValidMinimum("1.01.0") && !ModelVersionPolicy.IsValidMinimum("1.1.0+anything"),"ambiguous minimum versions are rejected");
        var gated=new ModelCatalogEntry{MinimumManagerVersion="1.1.0",MinimumEngineVersion="1.1.0"};
        assert(ModelVersionPolicy.Requirement(gated,"1.0.0",true)?.Contains("Update") == true && ModelVersionPolicy.Requirement(gated,"1.1.0",true) is null,"model launch gate uses the actual engine version");
        gated.MinimumManagerVersion="1.2.0";
        assert(ModelVersionPolicy.Requirement(gated)?.Contains("1.2.0")==true,"newer Manager requirement supplies an actionable update message");
        var embedded=ReleaseEnginePolicy.Embedded();
        var unapprovedFixture=new ReleaseEngineManifest(1,"NInferEZ Manager",ProductInfo.Version,false,[]);
        assert(!ReleaseEnginePolicy.Read(JsonSerializer.Serialize(unapprovedFixture,SettingsService.JsonOptions)).Approved,"an unapproved fixture remains unbound");
        Throws<InvalidDataException>(()=>ReleaseEnginePolicy.RequirePublishable(JsonSerializer.Serialize(unapprovedFixture,SettingsService.JsonOptions)));
        assert(true,"unapproved release manifest cannot pass publication validation");
        if(embedded.Approved)
        {
            ReleaseEnginePolicy.RequirePublishable(JsonSerializer.Serialize(embedded,SettingsService.JsonOptions));
            assert(embedded.Packages.Count==3 && embedded.Packages.Select(p=>p.CudaArchitecture).Distinct().Count()==3,"approved Manager pins one exact package per supported GPU architecture");
            assert(ReleaseEnginePolicy.Select(embedded,"sm120a").EngineVersion=="1.1.0","approved Blackwell Manager selects the qualified 1.1.0 engine");
        }

        var zip=Archive();
        var package=Package(zip);
        var manifest=new ReleaseEngineManifest(1,"NInferEZ Manager",ProductInfo.Version,true,[package]);
        var parsed=ReleaseEnginePolicy.Read(JsonSerializer.Serialize(manifest,SettingsService.JsonOptions));
        assert(ReleaseEnginePolicy.Select(parsed,"sm120a").Sha256==package.Sha256,"Manager release selects its exact pinned GPU package");
        Throws<InvalidOperationException>(()=>ReleaseEnginePolicy.Select(parsed,"sm89"));
        Throws<InvalidDataException>(()=>ReleaseEnginePolicy.Read(JsonSerializer.Serialize(manifest with{ManagerVersion="1.2.0"},SettingsService.JsonOptions)));
        Throws<InvalidDataException>(()=>ReleaseEnginePolicy.Read(JsonSerializer.Serialize(manifest with{Packages=[package,package]},SettingsService.JsonOptions)));
        Throws<InvalidDataException>(()=>ReleaseEnginePolicy.Read(JsonSerializer.Serialize(manifest with{Approved=false},SettingsService.JsonOptions)));
        assert(true,"GPU mismatch, wrong Manager version, duplicate pins and unapproved bindings are rejected");

        var downloadsPath=Path.Combine(paths.DataDirectory,"cache","engine-downloads");
        Directory.CreateDirectory(Path.GetDirectoryName(downloadsPath)!);
        var previousDownloads=downloadsPath+".retry-fixture-"+Guid.NewGuid().ToString("N");
        if(Directory.Exists(downloadsPath))Directory.Move(downloadsPath,previousDownloads);
        try
        {
            var retryBytes=Archive(version:"1.1.5");
            var retryPin=Package(retryBytes) with{EngineVersion="1.1.5"};
            using(var service=Service(retryBytes))
            {
                File.WriteAllText(downloadsPath,"blocks directory creation");
                await ThrowsAsync<IOException>(()=>service.InstallReleasePackageAsync(retryPin,CancellationToken.None));
                assert(service.Current.Stage=="Failed" && !service.Current.Running,"filesystem setup failure reports a completed failed installation");
                File.Delete(downloadsPath);
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await service.InstallReleasePackageAsync(retryPin,deadline.Token);
                assert(service.Current.Stage=="Ready" && ModelVersionPolicy.ReadEngineVersion(service.ActiveDirectory)=="1.1.5","filesystem setup failure releases the install gate so a valid approved pin can retry");
            }
            var cleanupBytes=Archive(version:"1.1.6");
            var cleanupPin=Package(cleanupBytes) with{EngineVersion="1.1.6"};
            var partial=Path.Combine(downloadsPath,cleanupPin.FileName+".partial");
            var beforeCleanupFailure=File.ReadAllText(Path.Combine(paths.EnginesDirectory,"current.json"));
            try
            {
                using var service=Service(cleanupBytes);
                service.CommitInstallation=(commit,token)=>
                {
                    File.SetAttributes(partial,File.GetAttributes(partial)|FileAttributes.ReadOnly);
                    throw new InvalidOperationException("fixture commit rejection");
                };
                try
                {
                    await service.InstallReleasePackageAsync(cleanupPin,CancellationToken.None);
                    throw new Exception("Expected fixture commit rejection.");
                }
                catch(InvalidOperationException error)
                {
                    assert(error.Message=="fixture commit rejection" && service.Current.Stage=="Failed" && service.Current.Error==error.Message,"readonly cleanup failure preserves the original commit error and failed progress");
                }
                assert(File.Exists(partial) && File.ReadAllText(Path.Combine(paths.EnginesDirectory,"current.json"))==beforeCleanupFailure,"failed cleanup retains the owned archive and preserves the previous engine pointer");
                File.SetAttributes(partial,FileAttributes.Normal);File.Delete(partial);
                service.CommitInstallation=(commit,token)=>{commit();return Task.CompletedTask;};
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await service.InstallReleasePackageAsync(cleanupPin,deadline.Token);
                assert(service.Current.Stage=="Ready" && !File.Exists(partial),"unauthorized readonly cleanup releases the install gate and a subsequent retry completes");
            }
            finally {if(File.Exists(partial))File.SetAttributes(partial,FileAttributes.Normal);}
        }
        finally
        {
            if(File.Exists(downloadsPath))File.Delete(downloadsPath);
            if(Directory.Exists(downloadsPath))Directory.Delete(downloadsPath,true);
            if(Directory.Exists(previousDownloads))Directory.Move(previousDownloads,downloadsPath);
        }

        var originalPointer=Path.Combine(paths.EnginesDirectory,"current.json");
        await File.WriteAllTextAsync(originalPointer,"previous engine pointer");
        using(var service=Service(zip))
        {
            var commits=0;
            service.CommitInstallation=(commit,token)=>
            {
                assert(File.ReadAllText(originalPointer)=="previous engine pointer","previous engine remains selected until package is fully staged");
                commits++; commit(); return Task.CompletedTask;
            };
            await service.InstallReleasePackageAsync(package,CancellationToken.None);
            assert(commits==1 && File.Exists(Path.Combine(service.ActiveDirectory,".manager-package.json")),"verified installation writes a receipt and switches the pointer once");
            service.CommitInstallation=(commit,token)=>throw new Exception("already selected pin must not commit again");
            await service.InstallReleasePackageAsync(package,CancellationToken.None);
            assert(true,"same verified package reuses installation without unloading or activation");
        }
        var selected=File.ReadAllText(originalPointer);
        using(var service=Service(zip))
        {
            var called=false;service.CommitInstallation=(commit,token)=>{called=true;commit();return Task.CompletedTask;};
            await ThrowsAsync<InvalidDataException>(()=>service.InstallReleasePackageAsync(package with{Sha256=new string('0',64)},CancellationToken.None));
            assert(!called && File.ReadAllText(originalPointer)==selected,"same-version wrong package SHA is not reused; failed download preserves selected engine");
        }
        var actual=Path.Combine(paths.EnginesDirectory,package.EngineVersion,package.CudaArchitecture,"ninfer-serve.exe");
        File.WriteAllText(actual,"tampered binary");
        using(var service=Service(zip))
        {
            var calls=0;service.CommitInstallation=(commit,token)=>{calls++;commit();return Task.CompletedTask;};
            await service.InstallReleasePackageAsync(package,CancellationToken.None);
            assert(calls==1 && File.ReadAllText(actual)=="test server bytes","changed installed binaries cannot pass cached-package verification");
        }
        foreach(var bad in new[]{Archive(version:"1.0.0"),Archive(unsafePath:true),Archive(duplicate:true),Archive(symbolic:true)})
        {
            using var service=Service(bad);var called=false;service.CommitInstallation=(commit,token)=>{called=true;commit();return Task.CompletedTask;};
            await ThrowsAsync<InvalidDataException>(()=>service.InstallReleasePackageAsync(Package(bad) with{EngineVersion="1.1.1"},CancellationToken.None));
            assert(!called && File.ReadAllText(originalPointer)==selected,"invalid staged archive never unloads or replaces the previous engine");
        }
        using(var client=new HttpClient(new ResponseHandler(_=>new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))
        using(var service=new EnginePackageService(paths,new ManagerLog(paths),settings,client))
        {
            await ThrowsAsync<HttpRequestException>(()=>service.InstallReleasePackageAsync(package with{EngineVersion="1.1.2"},CancellationToken.None));
            assert(File.ReadAllText(originalPointer)==selected,"network failure leaves the previous engine pointer intact");
        }
        using(var cancelled=new CancellationTokenSource())
        using(var service=Service(zip))
        {
            cancelled.Cancel();
            await ThrowsAsync<OperationCanceledException>(()=>service.InstallReleasePackageAsync(package with{EngineVersion="1.1.3"},cancelled.Token));
            assert(File.ReadAllText(originalPointer)==selected,"cancellation never switches the previous engine");
        }
        using(var service=Service(zip[..^1]))
        {
            await ThrowsAsync<InvalidDataException>(()=>service.InstallReleasePackageAsync(package with{EngineVersion="1.1.3"},CancellationToken.None));
            assert(File.ReadAllText(originalPointer)==selected,"truncated package never switches the previous engine");
        }
        Directory.CreateDirectory(paths.EngineDirectory);
        await File.WriteAllTextAsync(Path.Combine(paths.EngineDirectory,"engine-manifest.json"),"{\"engineVersion\":\"1.0.0\"}");
        await using(var engine=new EngineController(paths,settings,catalog,new ManagerLog(paths)))
        {
            await ThrowsAsync<InvalidOperationException>(()=>engine.LoadAsync(requestedModel:new ModelCatalogEntry{MinimumManagerVersion="1.1.0",MinimumEngineVersion="1.1.0"}));
            assert(engine.OperationStatus.Error?.Contains("1.1.0")==true,"old actual engine is blocked before inspector or process startup");
        }
        using(var downloads=new ModelDownloadService(paths,settings,new ManagerLog(paths)))
        {
            Throws<InvalidOperationException>(()=>downloads.Start(new ModelCatalogEntry{MinimumManagerVersion="1.2.0"}));
            await ThrowsAsync<InvalidOperationException>(()=>downloads.DownloadAsync(new ModelCatalogEntry{MinimumManagerVersion="1.2.0"},CancellationToken.None));
            assert(downloads.Current is null,"minimum Manager gate blocks both download entry points before network I/O");
        }
        var flowBytes=Archive(version:"1.1.4");
        var flowPin=Package(flowBytes) with{EngineVersion="1.1.4"};
        var flowManifest=new ReleaseEngineManifest(1,"NInferEZ Manager",ProductInfo.Version,true,[flowPin]);
        var requests=0;
        var releaseDownload=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var client=new HttpClient(new AsyncResponseHandler(async (request,token)=>
        {
            if(Interlocked.Increment(ref requests)==1)return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return await releaseDownload.Task.WaitAsync(token);
        })))
        using(var service=new EnginePackageService(paths,new ManagerLog(paths),settings,client))
        {
            var coordinator=new ProductUpdateCoordinator(service,new ManagerLog(paths),flowManifest,()=>Task.FromResult<string?>("sm120a"));
            assert(coordinator.LoadBlocker() is not null,"pending product update blocks new model loads");
            coordinator.Start();await coordinator.WaitForCompletionAsync();
            assert(coordinator.State.Stage=="Failed" && !coordinator.State.Ready && coordinator.LoadBlocker() is not null && File.ReadAllText(originalPointer)==selected,"failed product completion keeps previous engine and blocks unconfirmed loads");
            coordinator.Start();coordinator.Start();
            for(var wait=0;requests<2 && wait<100;wait++)await Task.Delay(10);
            assert(coordinator.State.Running && !coordinator.State.Ready && requests==2 && File.ReadAllText(originalPointer)==selected,"product update retry is single-flight and reports running while previous engine remains selected");
            releaseDownload.SetResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(flowBytes)});
            await coordinator.WaitForCompletionAsync();
            assert(coordinator.State.Stage=="Ready" && coordinator.State.Ready && coordinator.LoadBlocker() is null && ModelVersionPolicy.ReadEngineVersion(service.ActiveDirectory)=="1.1.4","product completion becomes ready only after the exact pinned package is verified and selected");
        }
        var model=catalog.Entries.First();
        var profile=new ModelProfile{ManageAdvancedSettings=false,AutoContext=false,MaxContext=131072,CustomKvCapacity=131072,DefaultMaxTokens=32768,KvPrecision=KvPrecision.Int8,KvCapacityMode=KvCapacityMode.MatchContext,VisionEnabled=true,VisionResidency="overlay",CudaGraphAllowanceMiB=256,DeviceStateSlots=1,ThinkingEnabled=false,SpeculativeMode=SpeculativeMode.Dflash2,DraftTokens=7,LmHeadDraft=true,NgramDraftTokens=15,PrefillChunk=512,Temperature=1,TopP=.95,TopK=20,MinP=0,PresencePenalty=0,FrequencyPenalty=0};
        var presetModel=new ModelCatalogEntry{DisplayName="OmniEdu",FileName="OmniEdu-Q6-Vision-DFlash2-Q8-Head.ninfer",SizeBytes=25813765120,Sha256="5c7e5b92d63ad2aeeee982702e22cf92c50d99a87fc12327b430230d3992a1a0",MinimumManagerVersion="1.1.0",MinimumEngineVersion="1.1.0"};
        var presetFile=Path.Combine(paths.DataDirectory,"omni.ninferez.json");
        await File.WriteAllTextAsync(presetFile,ModelPresetImporter.Export(presetModel,profile));
        var restored=ModelPresetImporter.Read(presetFile,presetModel,new());
        await File.WriteAllTextAsync(presetFile,ModelPresetImporter.Export(presetModel,new ModelProfile()));
        assert(ModelPresetImporter.Read(presetFile,presetModel,new()).CudaGraphAllowanceMiB is null,"legacy preset with nullable graph reserve round trips");
        assert(restored.VisionResidency=="overlay" && restored.CudaGraphAllowanceMiB==256 && !restored.GdnStateFp16 && restored.KvPrecision==KvPrecision.Int8 && restored.Temperature==1 && restored.PresencePenalty==0,"Omni exact preset round trips quality and measured memory controls");
        var curatedOmni=catalog.Entries.Single(e=>e.ModelId=="omniedu-27b-q6-k");
        var curatedProfile=curatedOmni.RecommendedProfile();
        assert(curatedOmni.MinimumManagerVersion=="1.1.0" && curatedOmni.MinimumEngineVersion=="1.1.0" && curatedOmni.Sha256==presetModel.Sha256 && !curatedProfile.AutoContext,"published embedded Omni identity and version gates are pinned and 128K is explicit");
        assert(curatedProfile.VisionResidency=="overlay" && curatedProfile.CudaGraphAllowanceMiB==256 && curatedProfile.DeviceStateSlots==1 && !curatedProfile.GdnStateFp16 && !curatedProfile.ThinkingEnabled && !curatedProfile.PreserveThinking && curatedProfile.Temperature==1 && curatedProfile.TopP==.95 && curatedProfile.TopK==20 && curatedProfile.PresencePenalty==0 && curatedProfile.FrequencyPenalty==0,"embedded Omni profile preserves exact validated memory and sampling settings");

        var visionOff=ProfileSettingsPolicy.Clone(curatedProfile);
        visionOff.VisionEnabled=false;
        visionOff.MediaLiveMiB=visionOff.MediaCacheMiB=0;
        visionOff.PrefillChunk=4096;
        visionOff.MaxContext=32768;
        settings.Update(s=>s.Profiles[curatedOmni.FileName]=visionOff);
        var modelInfo=SettingsService.Clone(catalog.Snapshot().Single(m=>m.FileName==curatedOmni.FileName));
        assert(modelInfo.RecommendedProfile.MediaLiveMiB==0 && modelInfo.DefaultProfile.MediaLiveMiB==2048
            && !modelInfo.RecommendedProfile.VisionEnabled && modelInfo.DefaultProfile.VisionEnabled,
            "model control response carries catalog defaults separately from the saved profile");
        var edited=ProfileSettingsPolicy.Managed(modelInfo.RecommendedProfile,modelInfo.DefaultProfile);
        edited.VisionEnabled=true;
        var enabled=ProfileSettingsPolicy.Managed(edited,modelInfo.DefaultProfile);
        settings.Update(s=>s.Profiles[curatedOmni.FileName]=enabled);
        var reloaded=new SettingsService(paths).ProfileFor(curatedOmni);
        assert(reloaded.VisionEnabled && reloaded.MediaLiveMiB==2048 && reloaded.MediaCacheMiB==1024
            && reloaded.PrefillChunk==512 && reloaded.VisionResidency=="overlay" && reloaded.CudaGraphAllowanceMiB==256
            && reloaded.MaxContext==32768 && reloaded.CustomKvCapacity==32768 && reloaded.KvPrecision==KvPrecision.Int8,
            "enabling Vision in a saved managed profile restores recommended advanced values and preserves basic settings");

        var actualPreset=Path.Combine(Directory.GetCurrentDirectory(),"docs","presets","OmniEdu-27B-Q6_K-Vision-DFlash2.ninferez.json");
        var actualProfile=ModelPresetImporter.Read(actualPreset,presetModel,new());
        settings.Current.ActiveModelFile=model.FileName;settings.Current.Profiles[model.FileName]=actualProfile;
        await using(var engine=new EngineController(paths,settings,catalog,new ManagerLog(paths)))
        {
            var launch=engine.BuildArguments();
            bool Has(string flag,string value){var index=launch.ToList().IndexOf(flag);return index>=0&&index+1<launch.Count&&launch[index+1]==value;}
            assert(Has("--vision-residency","overlay") && Has("--cuda-graph-allowance-mib","256") && Has("--kv-dtype","int8") && Has("--max-context","131072") && Has("--kv-capacity","131072"),"HF preset produces exact tested Vision, graph reserve and 128K Int8 allocation flags");
            assert(launch.Contains("--no-thinking") && !launch.Contains("--preserve-thinking") && !launch.Contains("--gdn-state-fp16") && Has("--temperature","1") && Has("--presence-penalty","0") && Has("--frequency-penalty","0"),"HF preset preserves author sampling, FP32 state and tested thinking flags");
        }
        Throws<ArgumentException>(()=>SettingsService.ValidateProfile(new ModelProfile{VisionResidency="invalid"}));
        Throws<ArgumentException>(()=>SettingsService.ValidateProfile(new ModelProfile{CudaGraphAllowanceMiB=-1}));
        assert(true,"invalid Vision residency and CUDA graph reserve are rejected");

        EnginePackageService Service(byte[] bytes)=>new(paths,new ManagerLog(paths),settings,new HttpClient(new ResponseHandler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)})));
    }
    private static ReleaseEnginePackage Package(byte[] zip)=>new("1.1.0",1,"windows-x64","sm120a","Blackwell",["RTX 5090"],true,"stable","Test fixture only","NInferEZ-Engine-sm120a-1.1.0.zip",zip.Length,Convert.ToHexString(SHA256.HashData(zip)),"https://github.com/BenGamliel/NInferEZ-Engine/releases/download/v1.1.0/NInferEZ-Engine-sm120a-1.1.0.zip");
    private static byte[] Archive(string version="1.1.0",bool unsafePath=false,bool duplicate=false,bool symbolic=false)
    {
        using var output=new MemoryStream();
        using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            Add("engine-manifest.json",$$"""{"product":"NInferEZ Engine","contractVersion":1,"engineVersion":"{{version}}","cudaArchitecture":"sm120a"}""");
            Add("ninfer-serve.exe","test server bytes");Add("ninfer-inspect.exe","test inspector bytes");
            if(unsafePath)Add("../outside.txt","unsafe");
            if(duplicate)Add("ninfer-serve.exe","duplicate");
            if(symbolic){var entry=zip.CreateEntry("symlink");entry.ExternalAttributes=unchecked((int)0xA1FF0000);}
            void Add(string name,string value){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),Encoding.UTF8);writer.Write(value);}
        }
        return output.ToArray();
    }
    private static void Throws<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}.");}
    private static async Task ThrowsAsync<T>(Func<Task> action) where T:Exception {try{await action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}.");}
    private sealed class AsyncResponseHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> response):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>response(request,token);
    }
    private sealed class ResponseHandler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(response(request));
    }
}
