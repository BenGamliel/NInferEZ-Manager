using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using NInferManager.Backend;
using NInferManager.Contracts;

internal static class MemoryMetadataChecks
{
    public static void VerifyLibrary(string settingsPath, string catalogPath, int capacityMiB)
    {
        var settings=JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(settingsPath),SettingsService.JsonOptions)
            ?? throw new InvalidDataException("Settings could not be read.");
        var entries=JsonSerializer.Deserialize<List<ModelCatalogEntry>>(File.ReadAllText(catalogPath),SettingsService.JsonOptions)
            ?? throw new InvalidDataException("Catalog could not be read.");
        foreach(var (key,path) in settings.ExternalModelPaths)
        {
            var profile=settings.Profiles[key];
            var info=new FileInfo(path);
            var geometry=ModelMemoryMetadata.Read(path)??throw new InvalidDataException("Actual artifact metadata could not be read.");
            var entry=entries.FirstOrDefault(x=>x.FileName.Equals(key,StringComparison.OrdinalIgnoreCase));
            var model=new ModelInfo(key,"local",key,key,"NInfer",info.Length,false,false,true,false,0,0,profile)
            {MemoryGeometry=geometry,EstimatedBaseVramMiB=entry?.BaseVramMiB??0,EstimatedKvMiBPerKToken=entry?.KvMiBPerKToken??0,EstimateKvPrecision=entry?.RecommendedKvPrecision??default,EstimateSpeculativeMode=entry?.RecommendedSpeculativeMode??default};
            var fit=ModelMemoryFit.Assess(model,new("Library GPU",0,capacityMiB,0,0){DeviceIndex=profile.Device});
            if(fit.Level!=MemoryFitLevel.Comfortable)throw new InvalidOperationException($"Unexpected library recommendation: {fit.Label}");
            Console.WriteLine($"PASS actual artifact {key}: {fit.Label} (metadata only, no payload/model/GPU load)");
            if(entry is {Featured:true})
            {
                if(entry.SizeBytes!=info.Length)throw new InvalidOperationException("Published variant size does not match the linked model.");
                var family=entries.Where(x=>x.Featured&&x.FamilyId==entry.FamilyId&&!string.IsNullOrEmpty(entry.FamilyId))
                    .Select(x=>new ModelInfo(x.DisplayName,x.Repository,x.FileName,x.ModelId,x.Weights,x.SizeBytes,x.Vision,x.DownloadAvailable,x.FileName==key,false,x.NativeContext,x.MaximumContext,x.RecommendedProfile(),x.Featured)
                    {FamilyId=x.FamilyId,FamilyDisplayName=x.FamilyDisplayName,VariantName=x.VariantName,VariantOrder=x.VariantOrder}).OrderBy(x=>x.VariantOrder).ToArray();
                if(family.Length>1&&ModelRecommendations.Select(family).FileName!=key)throw new InvalidOperationException("Existing variant was not selected in its family card.");
                Console.WriteLine($"PASS linked recommendation {key}: Installed, selected by exact artifact key; saved profile and API alias unchanged");
            }
        }
    }

    public static void Run(AppPaths paths, SettingsService settings, ModelCatalog catalog, Action<bool,string> check)
    {
        var directory=Path.Combine(paths.DataDirectory,"memory-metadata-fixtures");Directory.CreateDirectory(directory);
        const string config="{\"model_type\":\"qwen3_5_text\",\"num_hidden_layers\":4,\"num_key_value_heads\":4,\"head_dim\":256,\"layer_types\":[\"linear_attention\",\"linear_attention\",\"linear_attention\",\"full_attention\"]}";
        string DirectoryJson(string body)=>"{\"components\":{\"text\":{\"config\":"+body+"}},\"files\":[{\"payload_bytes\":8}]}";
        void WriteArtifact(string path,string json)
        {
            var data=Encoding.UTF8.GetBytes(json);var payloadStart=((32+data.Length+4095)/4096)*4096;
            var file=new byte[payloadStart+8];"NINFER\0\x03"u8.CopyTo(file);BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(8,8),(ulong)data.Length);data.CopyTo(file,32);File.WriteAllBytes(path,file);
        }
        var filePath=Path.Combine(directory,"renamed-user-model.ninfer");WriteArtifact(filePath,DirectoryJson(config));
        var geometry=ModelMemoryMetadata.Read(filePath);
        check(geometry is not null&&geometry.Target.FullLayers==1&&geometry.Target.KvHeads==4&&geometry.ArtifactBytes==new FileInfo(filePath).Length,"bounded native header reader derives geometry without filenames, inference, inspection processes or payload reads");
        var entry=catalog.LinkExternal(filePath);var before=JsonSerializer.Serialize(settings.Current.Profiles[entry.FileName],SettingsService.JsonOptions);
        var snapshot=catalog.Snapshot().Single(x=>x.FileName==entry.FileName);
        check(snapshot.MemoryGeometry==geometry&&snapshot.EstimatedBaseVramMiB==0,"local inventory exposes its own geometry even with no catalog calibration");
        check(JsonSerializer.Serialize(settings.Current.Profiles[entry.FileName],SettingsService.JsonOptions)==before,"reading memory metadata leaves all saved profile fields unchanged");
        var cached=entry.MemoryGeometry;catalog.Snapshot();
        check(ReferenceEquals(cached,entry.MemoryGeometry),"unchanged model metadata is cached rather than reparsed on inventory refresh");
        WriteArtifact(filePath,DirectoryJson(config.Replace("\"head_dim\":256","\"head_dim\":128")));File.SetLastWriteTimeUtc(filePath,DateTime.UtcNow.AddSeconds(3));
        var changed=catalog.Snapshot().Single(x=>x.FileName==entry.FileName);
        check(changed.MemoryGeometry?.Target.HeadDimension==128,"changed model invalidates cached memory geometry");
        var replacement=Path.Combine(directory,"replacement.ninfer");WriteArtifact(replacement,DirectoryJson(config));File.SetLastWriteTimeUtc(replacement,File.GetLastWriteTimeUtc(filePath));
        catalog.Relink(entry,replacement);
        check(catalog.Snapshot().Single(x=>x.FileName==entry.FileName).MemoryGeometry?.Target.HeadDimension==256,"same-size same-timestamp relink does not reuse memory geometry from a different file");
        foreach(var invalid in new[]{config.Replace("qwen3_5_text","unsupported_architecture"),config.Replace("full_attention","unknown_attention"),config.Replace("\"num_hidden_layers\":4","\"num_hidden_layers\":5"),config.Replace("\"num_key_value_heads\":4","\"num_key_value_heads\":-1"),"{}"})
        {
            WriteArtifact(filePath,DirectoryJson(invalid));
            check(ModelMemoryMetadata.Read(filePath) is null,"unsupported or malformed model geometry cannot create an invented memory recommendation");
        }
        WriteArtifact(filePath,"{invalid json}");check(ModelMemoryMetadata.Read(filePath) is null,"invalid JSON is isolated to this model instead of breaking inventory");
        WriteArtifact(filePath,DirectoryJson(config));var bytes=File.ReadAllBytes(filePath);BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8,8),(ulong)ModelMemoryMetadata.MaximumDirectoryBytes+1);File.WriteAllBytes(filePath,bytes);
        check(ModelMemoryMetadata.Read(filePath) is null,"oversized directory rejected before allocation");
        File.WriteAllBytes(filePath,[1,2,3]);check(ModelMemoryMetadata.Read(filePath) is null,"truncated header rejected without an exception reaching the UI");
        WriteArtifact(filePath,DirectoryJson(config));bytes=File.ReadAllBytes(filePath);bytes[0]=0;File.WriteAllBytes(filePath,bytes);
        check(ModelMemoryMetadata.Read(filePath) is null,"non-NInfer file cannot supply memory geometry");
        WriteArtifact(filePath,DirectoryJson(config));bytes=File.ReadAllBytes(filePath);File.WriteAllBytes(filePath,bytes[..^1]);
        check(ModelMemoryMetadata.Read(filePath) is null,"truncated declared payload rejected without reading tensor weights");
        WriteArtifact(filePath,DirectoryJson(config));catalog.Relink(entry,filePath);catalog.Snapshot();File.Delete(filePath);
        check(catalog.Snapshot().Single(x=>x.FileName==entry.FileName).MemoryGeometry is null,"missing linked file clears stale memory geometry");
    }
}
