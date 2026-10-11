using NInferManager.Contracts;

internal static class MemoryFitChecks
{
    public static void Run(Action<bool,string> check)
    {
        ModelInfo Model(int gib) => new ModelInfo("Fixture","fixture/test","fixture.ninfer","fixture","IQ3",gib*1024L*1024*1024,false,false,true,false,32768,262144,new(){AutoContext=false,MaxContext=32768,DefaultMaxTokens=4096,SpeculativeMode=SpeculativeMode.Disabled})
            {EstimatedBaseVramMiB=gib*1024+1024,EstimatedKvMiBPerKToken=20,EstimateKvPrecision=KvPrecision.Fp8,EstimateSpeculativeMode=SpeculativeMode.Disabled};
        GpuStatus Gpu(int gib,int used=0) => new("Fixture GPU",used,gib*1024,0,0);
        check(ModelMemoryFit.Assess(Model(21),Gpu(16)).Level==MemoryFitLevel.ExceedsCapacity,"model alone exceeds capacity: not recommended");
        check(ModelMemoryFit.Assess(Model(10),Gpu(12)).Level==MemoryFitLevel.Close,"10 GiB model on 12 GiB GPU: reduced context fits but headroom is tight");
        check(ModelMemoryFit.Assess(Model(5),Gpu(16)).Level==MemoryFitLevel.Comfortable,"small calibrated model and 64K fit comfortably");
        check(ModelMemoryFit.Assess(Model(10),Gpu(32)).Level==MemoryFitLevel.Comfortable,"10 GiB model plus 64K on 32 GiB: fits");
        check(ModelMemoryFit.Assess(Model(21),Gpu(24)).Level==MemoryFitLevel.Close,"64K fits with limited headroom: close");
        check(ModelMemoryFit.Assess(Model(16),Gpu(16)).Level==MemoryFitLevel.ExceedsCapacity,"equal file and capacity leaves no room for working memory or reduced context");
        check(ModelMemoryFit.Assess(Model(0),Gpu(16)).Level==MemoryFitLevel.Unknown,"missing artifact size receives no recommendation");
        check(ModelMemoryFit.Assess(Model(5),null).Label=="","missing GPU telemetry does not add a fourth visible label");
        var loaded=ModelMemoryFit.Assess(Model(10),Gpu(32,30000));
        check(loaded.Level==ModelMemoryFit.Assess(Model(10),Gpu(32)).Level,"loaded model is not double-counted as occupied memory");
        check(loaded.Explanation.Contains("not a measurement of current free VRAM"),"help distinguishes general capacity advice from current free memory");
        var calibrated=Model(10) with {EstimatedBaseVramMiB=12500,EstimatedKvMiBPerKToken=30};
        var longProfile=ProfileSettingsPolicy.Clone(calibrated.RecommendedProfile);longProfile.MaxContext=262144;
        var longContext=calibrated with {RecommendedProfile=longProfile};
        check(ModelMemoryFit.Assess(longContext,Gpu(16)).Level==MemoryFitLevel.Close,"64K reference is tight on 16 GiB regardless of saved 256K");
        check(ModelMemoryFit.Assess(longContext,Gpu(16)).Explanation.Contains("smaller context"),"close recommendation gives simple actionable advice");
        longProfile.AutoContext=true;
        check(ModelMemoryFit.Assess(longContext,Gpu(16)).Level==MemoryFitLevel.Close,"auto context never changes the general fit baseline");
        longProfile.KvPrecision=KvPrecision.Bf16;
        check(ModelMemoryFit.Assess(longContext,Gpu(32)).Level==MemoryFitLevel.Unknown,"changed KV precision without geometry cannot reuse incompatible calibration or imply tight memory");
        var uncalibrated=Model(5) with {EstimatedBaseVramMiB=0};
        check(ModelMemoryFit.Assess(uncalibrated,Gpu(32)).Level==MemoryFitLevel.Unknown,"missing context data means unknown, not falsely tight memory");
        var other=ProfileSettingsPolicy.Clone(Model(5).RecommendedProfile);other.Device=1;
        check(ModelMemoryFit.Assess(Model(5) with {RecommendedProfile=other},Gpu(16)).Level==MemoryFitLevel.Unknown,"configured GPU mismatch receives no recommendation");
        var before=System.Text.Json.JsonSerializer.Serialize(longContext.RecommendedProfile);
        ModelMemoryFit.Assess(longContext,Gpu(16));
        check(before==System.Text.Json.JsonSerializer.Serialize(longContext.RecommendedProfile),"recommendations never change profiles or context");
        var custom=ProfileSettingsPolicy.Clone(calibrated.RecommendedProfile);custom.KvCapacityMode=KvCapacityMode.Custom;custom.CustomKvCapacity=524288;
        check(ModelMemoryFit.Assess(calibrated with {RecommendedProfile=custom},Gpu(32)).Level==MemoryFitLevel.Comfortable,"manual custom KV allocation does not override reference suitability");
        var nvfp4=Model(21) with {EstimatedBaseVramMiB=23800,EstimatedKvMiBPerKToken=25};
        var nvProfile=ProfileSettingsPolicy.Clone(nvfp4.RecommendedProfile);nvProfile.MaxContext=262144;
        nvfp4=nvfp4 with {RecommendedProfile=nvProfile};
        var rtx5090=new GpuStatus("RTX 5090",0,32576,0,0);
        var nvFit=ModelMemoryFit.Assess(nvfp4,rtx5090);
        check(nvFit.Level==MemoryFitLevel.Comfortable && nvFit.Label=="Fits","NVFP4 on 5090 shows only Fits despite saved 256K");
        nvProfile.MaxContext=65536;
        check(ModelMemoryFit.Assess(nvfp4,rtx5090)==nvFit,"64K selection leaves recommendation unchanged");
        nvProfile.MaxContext=131072;
        check(ModelMemoryFit.Assess(nvfp4,rtx5090)==nvFit,"128K selection leaves recommendation unchanged");
        check(ModelMemoryFit.Assess(nvfp4,Gpu(24)).Level==MemoryFitLevel.ExceedsCapacity,"NVFP4 on 24 GiB is not recommended when even 32K exceeds capacity");
        var reducedOnly=Model(10) with {EstimatedBaseVramMiB=12000,EstimatedKvMiBPerKToken=70};
        check(ModelMemoryFit.Assess(reducedOnly,Gpu(16)).Label=="Close","32K fits but 64K exceeds capacity: close rather than not recommended");
        var oversized=reducedOnly with {EstimatedKvMiBPerKToken=140};
        check(ModelMemoryFit.Assess(oversized,Gpu(16)).Label=="Not recommended","model fits alone but model plus 32K exceeds capacity: not recommended");
        var boundary=Model(10) with {EstimatedBaseVramMiB=15360,EstimatedKvMiBPerKToken=32};
        check(ModelMemoryFit.Assess(boundary,Gpu(16)).Label=="Close","32K demand exactly equals GPU capacity: close");
        check(ModelMemoryFit.Assess(uncalibrated,Gpu(32)).Label=="","missing context data does not add a visible fourth label");
        foreach(var candidate in new[]{Model(5),nvfp4,reducedOnly,oversized})
        {
            var fit=ModelMemoryFit.Assess(candidate,Gpu(32));
            check(fit.Label is "Fits" or "Close" or "Not recommended","recommendation vocabulary contains only three choices");
            check(!fit.Label.Any(char.IsDigit)&&!fit.Explanation.Any(char.IsDigit),"recommendation and hover explanation contain no technical numbers");
        }
        check(ModelMemoryFit.Assess(Model(5) with {EstimatedKvMiBPerKToken=double.NaN},Gpu(32)).Level==MemoryFitLevel.Unknown,"invalid KV slope cannot produce a fit or tightness claim");
        var geometry=new ModelMemoryGeometry(13_024_883_200,new(16,0,4,256,0),true,null,new(0,5,8,128,2048));
        var localProfile=new ModelProfile{KvPrecision=KvPrecision.Rk8v4,SpeculativeMode=SpeculativeMode.Disabled};
        var local=Model(12) with {EstimatedBaseVramMiB=0,MemoryGeometry=geometry,RecommendedProfile=localProfile};
        check(ModelMemoryFit.Assess(local,rtx5090).Label=="Fits","local Q3 plus metadata-derived RK8V4 context fits on 5090 rather than being penalized for missing catalog identity");
        check(ModelMemoryFit.Assess(nvfp4,rtx5090).Label==ModelMemoryFit.Assess(local,rtx5090).Label,"actual large Swift and smaller local Q3 receive consistent suitability on 5090");
        check(geometry.TryEstimate(localProfile,out var rkSlope,out _)&&Math.Abs(rkSlope-28.05)<.001,"RK8V4 KV estimate follows engine key/value planes and scale overhead");
        var kvByteCounts=new Dictionary<KvPrecision,int>{{KvPrecision.Bf16,65536},{KvPrecision.Int8,33792},{KvPrecision.Fp8,33024},{KvPrecision.Nvfp4,18432},{KvPrecision.K8v4,25728},{KvPrecision.Rk8v4,26112}};
        foreach(var (precision,bytes) in kvByteCounts)
        {
            localProfile.KvPrecision=precision;
            check(geometry.TryEstimate(localProfile,out var slope,out _)&&Math.Abs(slope-bytes/1024d*1.10)<.001,$"metadata context estimate supports {precision} independently of catalog KV precision");
        }
        localProfile.KvPrecision=KvPrecision.Rk8v4;localProfile.SpeculativeMode=SpeculativeMode.Dflash2;
        check(geometry.TryEstimate(localProfile,out var dflashSlope,out var fixedDraft)&&Math.Abs(dflashSlope-rkSlope)<.001&&fixedDraft==40,"sliding DFlash2 cache adds fixed memory, not full-context memory");
        check(ModelMemoryFit.Assess(local,rtx5090).Label=="Fits","local Q3 remains a comfortable recommendation with its DFlash2 component enabled");
        localProfile.SpeculativeMode=SpeculativeMode.Mtp;
        check(geometry.TryEstimate(localProfile,out var mtpSlope,out _)&&mtpSlope>rkSlope,"MTP adds its separate full-context cache");
        var fullDraft=geometry with {Dflash=new(5,0,8,128,0)};
        localProfile.SpeculativeMode=SpeculativeMode.Dflash;
        check(fullDraft.TryEstimate(localProfile,out var fullDraftSlope,out _)&&fullDraftSlope>mtpSlope,"full-attention draft adds a separate BF16 context cache");
        check(ModelMemoryFit.Assess(local,rtx5090).Level==MemoryFitLevel.Unknown,"unsupported draft selection without component geometry is not called Close");
        localProfile.SpeculativeMode=SpeculativeMode.Disabled;
        check(ModelMemoryFit.Assess(local with {MemoryGeometry=geometry with {ArtifactBytes=40L*1024*1024*1024}},rtx5090).Level==MemoryFitLevel.ExceedsCapacity,"sharded artifact total, not small entry file alone, determines fit");
        int Severity(MemoryFitLevel level)=>level switch{MemoryFitLevel.Comfortable=>0,MemoryFitLevel.Close=>1,MemoryFitLevel.ExceedsCapacity=>2,_=>3};
        foreach(var candidate in new[]{Model(5),Model(10),nvfp4,local,reducedOnly,oversized})
        {
            var prior=3;
            foreach(var capacityGiB in new[]{8,12,16,24,32,48,64})
            {
                var current=Severity(ModelMemoryFit.Assess(candidate,Gpu(capacityGiB)).Level);
                check(current<=prior,"increasing VRAM never worsens a known model recommendation");prior=current;
            }
        }
    }
}
