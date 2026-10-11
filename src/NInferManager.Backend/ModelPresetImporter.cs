using System.Text.Json;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public static class ModelPresetImporter
{
    public static ModelProfile Read(string path,ModelCatalogEntry model,ModelProfile baseline)
    {
        try{return ReadCore(path,model,baseline);}
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException or ArgumentException)
        {throw new InvalidDataException("The preset contents are invalid: "+ex.Message,ex);}
    }
    private static ModelProfile ReadCore(string path,ModelCatalogEntry model,ModelProfile baseline)
    {
        if(!path.EndsWith(".ninferez.json",StringComparison.OrdinalIgnoreCase)||new FileInfo(path).Length>64*1024)throw new InvalidDataException("Choose a .ninferez.json preset smaller than 64 KiB.");
        using var document=JsonDocument.Parse(File.ReadAllText(path));var root=document.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidDataException("This preset schema is not supported.");
        var mode=root.GetProperty("mode").GetString();
        if(mode is not("exact" or "custom" or "automatic"))throw new InvalidDataException("Unsupported preset mode.");
        var identity=root.GetProperty("model");
        if(identity.GetProperty("formatVersion").GetInt32()!=3||identity.GetProperty("sizeBytes").GetInt64()!=model.SizeBytes)throw new InvalidDataException("The preset belongs to a different model artifact.");
        var expectedHash=identity.GetProperty("sha256").GetString();
        if(expectedHash is null||expectedHash.Length!=64||!expectedHash.All(Uri.IsHexDigit))throw new InvalidDataException("The preset has no valid artifact identity.");
        if(!string.IsNullOrWhiteSpace(model.Sha256)&&!model.Sha256.Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The preset artifact hash does not match this model identity.");
        var engine=root.GetProperty("engine");
        if(engine.GetProperty("contractVersion").GetInt32()!=1)throw new InvalidDataException("The preset needs a different engine contract.");
        var allowedFeatures=new HashSet<string>(["dflash2","dflash","mtp","proposalHead","ngramDrafting","vision","structuredOutput"],StringComparer.Ordinal);
        if(engine.TryGetProperty("requiredFeatures",out var features)&&features.EnumerateArray().Any(x=>!allowedFeatures.Contains(x.GetString()??"")))throw new InvalidDataException("The preset requires features this adapter does not support.");
        var p=SettingsService.Clone(baseline);var r=root.GetProperty("runtime");
        p.ManageAdvancedSettings=false;p.ManualAdvancedSettings=null;
        p.AutoContext=root.GetProperty("mode").GetString()=="automatic";
        p.MaxContext=r.GetProperty("maxContext").GetInt32();p.CustomKvCapacity=p.MaxContext;
        p.KvCapacityMode=KvCapacityMode.Auto;
        if(r.TryGetProperty("kvCapacity",out var capacity))
        {
            if(capacity.ValueKind==JsonValueKind.String&&capacity.GetString()=="auto")p.KvCapacityMode=KvCapacityMode.Auto;
            else if(capacity.ValueKind==JsonValueKind.Number){p.CustomKvCapacity=capacity.GetInt32();p.KvCapacityMode=KvCapacityMode.Custom;}
            else if(capacity.ValueKind==JsonValueKind.String&&capacity.GetString()=="matchContext")p.KvCapacityMode=KvCapacityMode.MatchContext;
            else throw new InvalidDataException("Unsupported KV capacity setting.");
        }
        if(!Enum.TryParse<KvPrecision>(r.GetProperty("kvCache").GetString(),true,out var kv)||!Enum.IsDefined(kv))throw new InvalidDataException("Unsupported KV precision in preset.");p.KvPrecision=kv;
        var spec=r.GetProperty("speculation").GetString();
        p.SpeculativeMode=spec is "off" or "disabled"?SpeculativeMode.Disabled:Enum.TryParse<SpeculativeMode>(spec,true,out var parsed)&&Enum.IsDefined(parsed)?parsed:throw new InvalidDataException("Unsupported speculation mode in preset.");
        if(r.TryGetProperty("draftTokens",out var draft))p.DraftTokens=draft.GetInt32();
        if(r.TryGetProperty("concurrency",out var concurrency))p.MaxConcurrency=concurrency.GetInt32();
        if(r.TryGetProperty("prefillChunk",out var prefill))p.PrefillChunk=prefill.GetInt32();
        if(r.TryGetProperty("gdnStateFp16",out var state))p.GdnStateFp16=state.GetBoolean();
        if(r.TryGetProperty("lmHeadDraft",out var head))p.LmHeadDraft=head.GetBoolean();
        if(r.TryGetProperty("ngramDraftTokens",out var ngram))p.NgramDraftTokens=ngram.GetInt32();
        if(r.TryGetProperty("vramReserveMiB",out var reserve))p.KvHeadroomMiB=reserve.GetInt32();
        if(r.TryGetProperty("structuredOutput",out var structured))p.StructuredOutput=structured.GetBoolean();
        if(root.TryGetProperty("cache",out var cache))
        {
            if(cache.TryGetProperty("hostCacheMiB",out var host))p.HostCacheMiB=host.GetInt32();
            if(cache.TryGetProperty("alternativePrefixCache",out var alternative))p.AlternativePrefixCache=alternative.GetBoolean();
            p.HostKvMiB=0;p.HostStateSlots=0;
        }
        p.DefaultMaxTokens=Math.Min(p.DefaultMaxTokens,p.MaxContext);
        SettingsService.ValidateProfile(p);
        if(root.TryGetProperty("managerProfile",out var fullProfile))
        {
            var strict=new JsonSerializerOptions(SettingsService.JsonOptions){UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
            p=fullProfile.Deserialize<ModelProfile>(strict)??throw new InvalidDataException("The Manager profile is invalid.");
            SettingsService.ValidateProfile(p);
            if(p.AutoContext!=(mode=="automatic"))throw new InvalidDataException("The preset mode and embedded Manager profile disagree.");
        }
        return p;
    }
    public static string Export(ModelCatalogEntry model,ModelProfile p)
    {
        if(string.IsNullOrWhiteSpace(model.ExpectedSha256??model.Sha256))throw new InvalidOperationException("Verify this model once before exporting a portable preset. No artifact hash is known yet.");
        return JsonSerializer.Serialize(new
    {
        schemaVersion=1,profileName=model.DisplayName,mode=p.AutoContext?"automatic":"exact",
        engine=new{minVersion="0.1.0-preview.1",contractVersion=1,requiredFeatures=Array.Empty<string>()},
        model=new{fileName=model.FileName,sizeBytes=model.SizeBytes,sha256=model.ExpectedSha256??model.Sha256,formatVersion=3},
        runtime=new{maxContext=p.MaxContext,kvCache=p.KvPrecision.ToString().ToLowerInvariant(),kvCapacity=p.KvCapacityMode==KvCapacityMode.Auto?(object)"auto":p.KvCapacityMode==KvCapacityMode.MatchContext?"matchContext":p.CustomKvCapacity,speculation=p.SpeculativeMode==SpeculativeMode.Disabled?"off":p.SpeculativeMode.ToString().ToLowerInvariant(),draftTokens=p.DraftTokens,vramReserveMiB=p.KvHeadroomMiB,concurrency=p.MaxConcurrency,prefillChunk=p.PrefillChunk,gdnStateFp16=p.GdnStateFp16,lmHeadDraft=p.LmHeadDraft,ngramDraftTokens=p.NgramDraftTokens,structuredOutput=p.StructuredOutput},
        cache=new{hostCacheMiB=p.HostCacheMiB??p.HostKvMiB,alternativePrefixCache=p.AlternativePrefixCache},
        identityVerification=model.IdentityVerified?"verified":"unverified",managerProfile=p
    },SettingsService.JsonOptions);
    }
}
