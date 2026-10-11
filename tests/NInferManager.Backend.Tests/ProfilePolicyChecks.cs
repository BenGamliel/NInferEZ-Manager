using System.Text.Json;
using NInferManager.Backend;
using NInferManager.Contracts;

internal static class ProfilePolicyChecks
{
    public static void Run(Action<bool,string> check)
    {
        var manual=new ModelProfile{ManageAdvancedSettings=false,AutoContext=false,MaxContext=32768,DefaultMaxTokens=4096,
            VisionEnabled=false,SpeculativeMode=SpeculativeMode.Mtp,DraftTokens=3,MtpAttentionWindow=8192,LmHeadDraft=true,
            NgramDraftTokens=15,PrefillChunk=4096,KvCapacityMode=KvCapacityMode.Custom,CustomKvCapacity=262144,
            HostCacheMiB=512,HostKvMiB=0,Temperature=.4,ChatTemplateFile="manual-template.jinja",GdnStateFp16=true};
        var recommended=new ModelProfile{AutoContext=true,MaxContext=262144,DefaultMaxTokens=4096,SpeculativeMode=SpeculativeMode.Mtp,
            DraftTokens=3,MtpAttentionWindow=8192,LmHeadDraft=true,NgramDraftTokens=15,PrefillChunk=2048,GdnStateFp16=true};
        var managed=ProfileSettingsPolicy.SwitchManagement(manual,true,recommended);
        check(managed.ManualAdvancedSettings is not null,"managed mode captures a bounded typed manual backup");
        check(managed.PrefillChunk==2048&&managed.CustomKvCapacity==32768&&managed.KvCapacityMode==KvCapacityMode.MatchContext,"managed explicit context synchronizes advanced capacity and recommended prefill");
        managed.MaxContext=16384;managed.SpeculativeMode=SpeculativeMode.Disabled;
        managed=ProfileSettingsPolicy.Managed(managed,recommended);
        check(!managed.LmHeadDraft&&managed.NgramDraftTokens==0&&managed.MtpAttentionWindow==0,"disabled acceleration neutralizes all dependent draft flags");
        var restored=ProfileSettingsPolicy.SwitchManagement(managed,false,recommended);
        check(restored.MaxContext==16384&&restored.SpeculativeMode==SpeculativeMode.Disabled,"manual restore leaves the latest basic choices intact");
        check(JsonSerializer.Serialize(ProfileSettingsPolicy.CaptureAdvanced(restored))==JsonSerializer.Serialize(ProfileSettingsPolicy.CaptureAdvanced(manual)),"manual restore recovers every advanced field including expert paths and sampling");
        var afterRestart=SettingsService.Clone(managed);
        var restartRestored=ProfileSettingsPolicy.SwitchManagement(afterRestart,false,recommended);
        check(restartRestored.PrefillChunk==4096&&restartRestored.CustomKvCapacity==262144&&restartRestored.MtpAttentionWindow==8192,"manual backup survives save/JSON/restart without being overwritten by managed values");
        var effective=ProfileSettingsPolicy.Effective(restored);
        check(effective.MtpAttentionWindow==0&&!effective.LmHeadDraft&&restored.MtpAttentionWindow==8192&&restored.LmHeadDraft,"effective launch removes inactive flags without destroying manual preferences");
        foreach(var mode in Enum.GetValues<SpeculativeMode>())
        foreach(var kv in Enum.GetValues<KvPrecision>())
        foreach(var auto in new[]{false,true})
        {
            var selected=SettingsService.Clone(manual);selected.SpeculativeMode=mode;selected.KvPrecision=kv;selected.AutoContext=auto;selected.ManageAdvancedSettings=true;
            var result=ProfileSettingsPolicy.Managed(selected,recommended);
            SettingsService.ValidateProfile(result);
            check(result.KvPrecision==kv&&result.SpeculativeMode==mode&&result.MaxContext==selected.MaxContext
                && result.KvCapacityMode==(auto?KvCapacityMode.Auto:KvCapacityMode.MatchContext)
                && (mode!=SpeculativeMode.Disabled||(!result.LmHeadDraft&&result.NgramDraftTokens==0&&result.MtpAttentionWindow==0))
                && (mode==SpeculativeMode.Mtp||result.MtpAttentionWindow==0),$"managed settings matrix: {mode}/{kv}/auto={auto}");
        }
        var inactive=SettingsService.Clone(manual);inactive.SpeculativeMode=SpeculativeMode.Disabled;
        check(!ProfileSettingsPolicy.IsApplicable(nameof(ModelProfile.MtpAttentionWindow),inactive,false)
            && !ProfileSettingsPolicy.IsApplicable(nameof(ModelProfile.LmHeadDraft),inactive,false)
            && !ProfileSettingsPolicy.IsApplicable(nameof(ModelProfile.NgramDraftTokens),inactive,false),"disabled-mode controls are consistently unavailable");
        inactive.SpeculativeMode=SpeculativeMode.Dflash2;
        check(!ProfileSettingsPolicy.IsApplicable(nameof(ModelProfile.MtpAttentionWindow),inactive,false)
            && ProfileSettingsPolicy.IsApplicable(nameof(ModelProfile.LmHeadDraft),inactive,false),"DFlash exposes drafting but never MTP-only controls");
        check(!ProfileSettingsPolicy.IsApplicable(nameof(ModelProfile.MediaLiveMiB),inactive,false),"non-vision artifacts never enable media controls");
        inactive.KvCapacityMode=KvCapacityMode.MatchContext;
        check(!ProfileSettingsPolicy.IsApplicable(nameof(ModelProfile.CustomKvCapacity),inactive,false)
            && !ProfileSettingsPolicy.IsApplicable(nameof(ModelProfile.KvHeadroomMiB),inactive,false),"custom capacity and automatic reserve are grey outside their policies");
        var error="ninfer-serve: --mtp-attention-window requires --spec mtp";
        check(StartupDiagnostics.Summarize(new[]{error,"usage: ninfer-serve", "Host KV=8192 MiB."})==error,"startup error survives the engine help text instead of displaying its footer");
        try { ProfileSettingsPolicy.RestoreAdvanced(new(),new Dictionary<string,JsonElement>{{"Executable",JsonSerializer.SerializeToElement("bad.exe")}});check(false,"unknown backup key accepted"); }
        catch(ArgumentException){check(true,"manual backup rejects executable and unknown keys");}
        try { var bad=SettingsService.Clone(manual);bad.TopK=21;SettingsService.ValidateProfile(bad);check(false,"unsupported TopK accepted"); }
        catch(ArgumentException){check(true,"sampling limits match the actual engine CLI before launch");}
        void Invalid(Action<ModelProfile> edit,string expected)
        {
            var value=SettingsService.Clone(manual);edit(value);
            try{SettingsService.ValidateProfile(value);check(false,$"invalid {expected} accepted");}
            catch(ArgumentException ex){check(ex.Message.Contains(expected)&&!ex.Message.Contains("Parameter '"),$"manual validation explains {expected} in user-facing terms");}
        }
        Invalid(p=>{p.VisionEnabled=true;p.MediaLiveMiB=0;},"Live image budget");
        Invalid(p=>{p.VisionEnabled=true;p.MediaPreprocessThreads=65;},"Image processing workers");
        Invalid(p=>p.PrefillChunk=1000,"Prompt processing batch");
        Invalid(p=>p.MaxPendingRequests=0,"Pending request limit");
        Invalid(p=>p.ResponseStoreMaxRecords=0,"Response history records");
        Invalid(p=>p.ResponseStoreMaxMiB=0,"Response history memory");
        Invalid(p=>p.CustomKvCapacity=1024,"Custom KV capacity");
        Invalid(p=>p.DefaultMaxTokens=32769,"Default output limit");
        Invalid(p=>p.PresencePenalty=2.1,"Presence penalty");
        Invalid(p=>p.FrequencyPenalty=-2.1,"Frequency penalty");
        Invalid(p=>{p.AlternativePrefixCache=true;p.PrefixReuseEnabled=false;},"Alternative prefix caching");
        var mediaOff=SettingsService.Clone(manual);mediaOff.MediaLiveMiB=0;mediaOff.MediaPreprocessThreads=100;
        SettingsService.ValidateProfile(mediaOff);check(true,"inactive vision preferences do not cause a launch validation error");
        recommended.PrefillChunk=1000;manual.ManageAdvancedSettings=true;
        var rounded=ProfileSettingsPolicy.Managed(manual,recommended);
        check(rounded.PrefillChunk==896,"managed prefill always respects engine 128-token batching without changing basic choices");
        check(StartupDiagnostics.UserMessage("NInfer exited during startup with code 1. ninfer-serve: --media-live-mib is out of range").Contains("at least 1 MiB"),"media startup failure is explained with the required value");
        check(StartupDiagnostics.UserMessage("requested Engine runtime reservation requires 6642400512 bytes, but only 4977590272 bytes are available").Contains("GPU memory"),"runtime memory failure gets actionable guidance instead of byte codes");
        check(ModelPresentation.WeightSummary("bf16, fp32, gguf_iq3_s, gguf_iq3_xxs, nvfp4", "orca-router-iq3-xxs-dflash2.ninfer")=="IQ3_XXS","model card uses one general quantization label rather than every internal tensor format");
        check(ModelPresentation.WeightSummary("bf16, fp32, gguf_iq3_xxs", "unknown.ninfer")=="Mixed quantization","unknown mixed artifacts do not receive an invented dominant quantization label");
    }
}
