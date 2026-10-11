using System.Reflection;
using System.Text.Json;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public sealed class ModelCatalogEntry
{
    public string DisplayName { get; set; } = "";
    public string Repository { get; set; } = "";
    public string Revision { get; set; } = "main";
    public string FileName { get; set; } = "";
    public string RemoteFileName { get; set; } = "";
    public string FamilyId { get; set; } = "";
    public string FamilyDisplayName { get; set; } = "";
    public string VariantName { get; set; } = "";
    public int VariantOrder { get; set; }
    public string ModelId { get; set; } = "";
    public string Weights { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public bool Vision { get; set; }
    public bool DownloadAvailable { get; set; } = true;
    public int ArtifactVersion { get; set; }
    public int RecommendedContext { get; set; } = 200_000;
    public int RecommendedMaxTokens { get; set; } = 32_768;
    public KvPrecision RecommendedKvPrecision { get; set; } = KvPrecision.Fp8;
    public SpeculativeMode RecommendedSpeculativeMode { get; set; } = SpeculativeMode.Dflash2;
    public int RecommendedDraftTokens { get; set; } = 7;
    public bool RecommendedLmHeadDraft { get; set; } = true;
    public int RecommendedPrefillChunk { get; set; } = 2048;
    public int NativeContext { get; set; } = 262144;
    public int MaximumContext { get; set; } = 524288;
    public int BaseVramMiB { get; set; }
    public double KvMiBPerKToken { get; set; } = 34;
    public bool RecommendedGdnStateFp16 { get; set; }
    public int RecommendedMtpAttentionWindow { get; set; }
    public int RecommendedNgramDraftTokens { get; set; }
    public bool RecommendedStructuredOutput { get; set; }
    public bool Featured { get; set; }
    public DateTimeOffset? NewUntilUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public DateTimeOffset? FirstSeenUtc { get; set; }
    public string? LocalPath { get; set; }
    public bool Inspected { get; set; }
    public string? InspectionMessage { get; set; }
    public bool HasMtp { get; set; }
    public bool HasDflash { get; set; }
    public bool HasDflash2 { get; set; }
    public bool CompatibleWithBuild { get; set; } = true;
    public string? ExpectedSha256 { get; set; }
    public bool IdentityVerified { get; set; }
    public string? PresetStatus { get; set; }
    public bool InvalidAdjacentPreset { get; set; }
    public DateTime? VerifiedWriteUtc { get; set; }
    public DateTime? InspectedWriteUtc { get; set; }
    public DateTime? InspectorWriteUtc { get; set; }
    public string? InspectedEngineDirectory { get; set; }
    public string? InspectedModelPath { get; set; }
    public string? IgnoredAdjacentPresetFingerprint { get; set; }
    public bool StarterProfile { get; set; }
    public bool YarnSupported { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public ModelMemoryGeometry? MemoryGeometry { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public (string Path, long Bytes, DateTime WriteUtc)? MemoryMetadataStamp { get; set; }
    public string DownloadUrl => $"https://huggingface.co/{Repository}/resolve/{Revision}/{Uri.EscapeDataString(string.IsNullOrWhiteSpace(RemoteFileName) ? FileName : RemoteFileName)}?download=true";
    public ModelProfile RecommendedProfile() => new()
    {
        ManageAdvancedSettings = true,
        AutoContext=BaseVramMiB>0,
        VisionEnabled = Vision,
        MaxContext = RecommendedContext,
        DefaultMaxTokens = RecommendedMaxTokens,
        CustomKvCapacity = RecommendedContext,
        KvPrecision = RecommendedKvPrecision,
        SpeculativeMode = RecommendedSpeculativeMode,
        DraftTokens = RecommendedDraftTokens,
        LmHeadDraft = RecommendedLmHeadDraft,
        PrefillChunk = RecommendedPrefillChunk,
        GdnStateFp16 = RecommendedGdnStateFp16,
        MtpAttentionWindow = RecommendedMtpAttentionWindow,
        NgramDraftTokens = RecommendedNgramDraftTokens,
        StructuredOutput = RecommendedStructuredOutput,
        DeviceStateSlots = 0,
        HostStateSlots = 0,
        HostKvMiB = 0
    };
}

public sealed class ModelCatalog
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _refreshGate=new(1,1);
    private readonly object _sync=new();
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private long _revision=1;
    private readonly List<ModelCatalogEntry> _entries;
    public ModelCatalog(AppPaths paths, SettingsService settings,HttpClient? http=null,TimeProvider? time=null)
    {
        _paths = paths; _settings = settings;
        _http=http??new(){Timeout=TimeSpan.FromSeconds(20)};
        _time=time??TimeProvider.System;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NInferManager.Backend.Assets.catalog.json")
            ?? throw new InvalidOperationException("Embedded model catalog is missing.");
        _entries = JsonSerializer.Deserialize<List<ModelCatalogEntry>>(stream, SettingsService.JsonOptions) ?? [];
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"NInferEZ-Manager/{ProductInfo.Version}");
        try
        {
            if(File.Exists(_paths.CatalogCacheFile)&&new FileInfo(_paths.CatalogCacheFile).Length<=8*1024*1024)
            {
                var cache=JsonSerializer.Deserialize<List<ModelCatalogEntry>>(File.ReadAllText(_paths.CatalogCacheFile),SettingsService.JsonOptions);
                if(cache is {Count:<=4000})foreach(var entry in cache)
                {
                    if(entry is null||string.IsNullOrWhiteSpace(entry.FileName))continue;
                    if(entry.DownloadAvailable&&!IsPublisherModel(entry))
                    {
                        var present=_settings.Current.ExternalModelPaths.ContainsKey(entry.FileName)
                            ||File.Exists(entry.LocalPath??Path.Combine(_paths.ModelsDirectory,entry.FileName));
                        if(!present)continue;
                        entry.DownloadAvailable=false;entry.Featured=false;entry.NewUntilUtc=null;
                        entry.Repository="Local file";
                    }
                    Merge(entry);
                }
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException){}
        RestoreExternalArtifacts();
        RefreshLocalArtifacts();
        foreach(var entry in Entries){entry.FirstSeenUtc??=_time.GetUtcNow();ValidateCachedInspection(entry);}
        SaveCache();
    }
    public IReadOnlyList<ModelCatalogEntry> Entries { get { lock(_sync)return _entries.ToArray(); } }
    public ModelCatalogEntry? Find(string fileName) { lock(_sync)return _entries.FirstOrDefault(x => x.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)); }
    public IReadOnlyList<ModelCatalogEntry> InstalledEntries() { lock(_sync)return _entries.Where(IsInstalled).ToList(); }
    public ModelCatalogEntry? FindInstalledByApiName(string modelId)
    {
        if(string.IsNullOrWhiteSpace(modelId))return null;
        lock(_sync)return _entries.FirstOrDefault(x=>IsInstalled(x)&&_settings.ModelIdFor(x).Equals(modelId,StringComparison.Ordinal));
    }
    public string ResolvePath(ModelCatalogEntry e) => _settings.Current.ExternalModelPaths.TryGetValue(e.FileName, out var external) && !string.IsNullOrWhiteSpace(external)
        ? Path.GetFullPath(external) : e.LocalPath is not null?Path.GetFullPath(e.LocalPath):Path.Combine(_paths.ModelsDirectory, e.FileName);
    public bool IsExternal(ModelCatalogEntry e) => _settings.Current.ExternalModelPaths.ContainsKey(e.FileName);
    public bool IsInstalled(ModelCatalogEntry e)
    {
        var path = ResolvePath(e);
        if(!File.Exists(path)){e.IdentityVerified=false;ValidateCachedInspection(e);return false;}
        var info=new FileInfo(path);
        if(e.VerifiedWriteUtc!=info.LastWriteTimeUtc)e.IdentityVerified=false;
        ValidateCachedInspection(e);
        return info.Length == e.SizeBytes;
    }
    public IReadOnlyList<ModelInfo> Snapshot()
    {
        lock(_sync)return _entries.Select(e=>
        {
            var installed=IsInstalled(e);
            RefreshMemoryMetadata(e, installed);
            return new ModelInfo(e.DisplayName,e.Repository,e.FileName,_settings.ModelIdFor(e),e.Weights,e.SizeBytes,e.Vision,e.DownloadAvailable,installed,_settings.Current.ActiveModelFile.Equals(e.FileName,StringComparison.OrdinalIgnoreCase),e.NativeContext,e.MaximumContext,SettingsService.Clone(_settings.ProfileFor(e)),e.Featured,IsRecent(e))
            {Origin=IsExternal(e)?"ExternalLinked":e.LocalPath is not null||File.Exists(Path.Combine(_paths.ModelsDirectory,e.FileName))?"LocalManaged":"Remote",LinkedExternal=IsExternal(e),Missing=IsExternal(e)&&!File.Exists(ResolvePath(e)),Inspected=e.Inspected,InspectionMessage=e.InspectionMessage,PresetStatus=e.PresetStatus,HasMtp=e.HasMtp,HasDflash=e.HasDflash,HasDflash2=e.HasDflash2,EstimatedBaseVramMiB=e.BaseVramMiB,EstimatedKvMiBPerKToken=e.KvMiBPerKToken,EstimateKvPrecision=e.RecommendedKvPrecision,EstimateSpeculativeMode=e.RecommendedSpeculativeMode,MemoryGeometry=e.MemoryGeometry,FamilyId=e.FamilyId,FamilyDisplayName=e.FamilyDisplayName,VariantName=e.VariantName,VariantOrder=e.VariantOrder,RemoteFileName=e.RemoteFileName,Revision=e.Revision};
        }).ToList();
    }
    private void RefreshMemoryMetadata(ModelCatalogEntry entry, bool installed)
    {
        if (!installed) { entry.MemoryGeometry=null; entry.MemoryMetadataStamp=null; return; }
        try
        {
            var path=ResolvePath(entry);
            var info=new FileInfo(path);
            var stamp=(path,info.Length,info.LastWriteTimeUtc);
            if (entry.MemoryMetadataStamp==stamp) return;
            var geometry=ModelMemoryMetadata.Read(path);
            info.Refresh();
            if (!info.Exists || info.Length!=stamp.Length || info.LastWriteTimeUtc!=stamp.LastWriteTimeUtc)
            { entry.MemoryGeometry=null; entry.MemoryMetadataStamp=null; return; }
            entry.MemoryGeometry=geometry; entry.MemoryMetadataStamp=stamp;
        }
        catch (Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException)
        { entry.MemoryGeometry=null; entry.MemoryMetadataStamp=null; }
    }
    public ModelCatalogEntry LinkExternal(string source)
    {
        var path=Path.GetFullPath(source);
        if(!File.Exists(path))throw new FileNotFoundException("The selected model file does not exist.",path);
        if(!Path.GetExtension(path).Equals(".ninfer",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Choose a .ninfer model artifact.");
        var info=new FileInfo(path);
        ModelCatalogEntry entry;
        lock(_sync)
        {
            entry=_entries.FirstOrDefault(x=>ResolvePath(x).Equals(path,StringComparison.OrdinalIgnoreCase))
                ?? _entries.FirstOrDefault(x=>MatchesArtifactName(x,info)&&!IsExternal(x)&&x.LocalPath is null)
                ?? AddLocalEntry(info.Name,info.Length,path);
        }
        _settings.Update(s=>
        {
            s.ExternalModelPaths[entry.FileName]=path;
            if(string.IsNullOrWhiteSpace(s.ActiveModelFile))s.ActiveModelFile=entry.FileName;
        });
        ImportAdjacentPreset(entry,path);
        _settings.ProfileFor(entry);
        ValidateCachedInspection(entry);
        SaveCache();
        return entry;
    }
    public int RefreshLocalArtifacts()
    {
        var added=0;
        var roots=new[]{_paths.ModelsDirectory,Path.Combine(_paths.AppDirectory,"Models")}.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach(var directory in roots.Where(Directory.Exists))
        foreach(var path in Directory.EnumerateFiles(directory,"*.ninfer",SearchOption.TopDirectoryOnly))
        {
            var info=new FileInfo(path);
            ModelCatalogEntry entry;
            lock(_sync)
            {
                var existing=_entries.FirstOrDefault(x=>ResolvePath(x).Equals(path,StringComparison.OrdinalIgnoreCase))
                    ??_entries.FirstOrDefault(x=>MatchesArtifactName(x,info)&&!IsExternal(x));
                entry=existing??AddLocalEntry(info.Name,info.Length,path);
                if(existing is null)added++;
                entry.LocalPath=Path.GetFullPath(path);
            }
            if(_settings.Current.ExternalModelPaths.TryGetValue(entry.FileName,out var linked)&&linked.Equals(path,StringComparison.OrdinalIgnoreCase))_settings.Update(s=>s.ExternalModelPaths.Remove(entry.FileName));
            ImportAdjacentPreset(entry,path);
            ValidateCachedInspection(entry);
        }
        SaveCache();
        return added;
    }
    public bool ShouldRefresh()=>_settings.Current.AutoCheckCatalog&&(_settings.Current.LastCatalogCheckUtc is null||_time.GetUtcNow()-_settings.Current.LastCatalogCheckUtc>=TimeSpan.FromHours(Math.Max(1,_settings.Current.CatalogCheckHours)));
    public async Task<int> RefreshOnlineAsync(CancellationToken token=default)
    {
        await _refreshGate.WaitAsync(token);
        try
        {
            var before=_entries.Select(x=>x.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            using var response=await _http.GetAsync(ProductInfo.ModelCatalogUrl,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            var feed=JsonSerializer.Deserialize<ModelCatalogFeed>(await BoundedHttpContent.ReadTextAsync(response.Content,token),SettingsService.JsonOptions)
                ?? throw new InvalidDataException("The NInferEZ model catalog is empty.");
            if(feed.SchemaVersion!=1||feed.Product!="NInferEZ Model Catalog")throw new InvalidDataException("The model catalog schema or product is unsupported.");
            if(feed.Models.Count>1000)throw new InvalidDataException("The model catalog contains too many entries.");
            foreach(var entry in feed.Models)ValidateRemote(entry);
            foreach(var entry in feed.Models)Merge(entry,true);
            SaveCache();
            _settings.Update(s=>s.LastCatalogCheckUtc=_time.GetUtcNow());
            Interlocked.Increment(ref _revision);
            return _entries.Count(x=>!before.Contains(x.FileName));
        }
        finally{_refreshGate.Release();}
    }
    private void Merge(ModelCatalogEntry e,bool fullProfile=false)
    {
        if(!e.FileName.EndsWith(".ninfer",StringComparison.OrdinalIgnoreCase)||e.SizeBytes<=0||e.ArtifactVersion!=3)return;
        lock(_sync)
        {
            var i=_entries.FindIndex(x=>x.FileName.Equals(e.FileName,StringComparison.OrdinalIgnoreCase));
            if(i<0){e.FirstSeenUtc??=_time.GetUtcNow();_entries.Add(e);return;}
            // Online refresh owns changing artifact metadata. Runtime capability and
            // recommended launch settings stay curated by this application version.
            var current=_entries[i];
            current.FirstSeenUtc??=e.FirstSeenUtc??_time.GetUtcNow();
            if(!fullProfile)current.PublishedAtUtc??=e.PublishedAtUtc;
            if(!fullProfile)
            {
                current.ExpectedSha256=e.ExpectedSha256;current.IdentityVerified=e.IdentityVerified;current.VerifiedWriteUtc=e.VerifiedWriteUtc;current.LocalPath=e.LocalPath;
                current.PresetStatus=e.PresetStatus;current.InvalidAdjacentPreset=e.InvalidAdjacentPreset;
                current.IgnoredAdjacentPresetFingerprint=e.IgnoredAdjacentPresetFingerprint;
                current.Inspected=e.Inspected;current.InspectionMessage=e.InspectionMessage;
                current.HasMtp=e.HasMtp;current.HasDflash=e.HasDflash;current.HasDflash2=e.HasDflash2;current.CompatibleWithBuild=e.CompatibleWithBuild;
                current.InspectedWriteUtc=e.InspectedWriteUtc;current.InspectorWriteUtc=e.InspectorWriteUtc;
                current.InspectedEngineDirectory=e.InspectedEngineDirectory;current.InspectedModelPath=e.InspectedModelPath;
                current.YarnSupported=e.YarnSupported;current.StarterProfile=e.StarterProfile;
                // A local cache must not turn a newly curated variant back into an unknown local model.
                if(current.DownloadAvailable&&current.Repository!="Local file"&&current.Sha256.Length==64)return;
                current.NativeContext=e.NativeContext;current.MaximumContext=e.MaximumContext;
            }
            current.DisplayName=string.IsNullOrWhiteSpace(e.DisplayName)?current.DisplayName:e.DisplayName;
            current.Repository=string.IsNullOrWhiteSpace(e.Repository)?current.Repository:e.Repository;
            current.RemoteFileName=e.RemoteFileName;
            current.ModelId=string.IsNullOrWhiteSpace(e.ModelId)?current.ModelId:e.ModelId;
            current.Weights=string.IsNullOrWhiteSpace(e.Weights)?current.Weights:e.Weights;
            current.SizeBytes=e.SizeBytes;
            current.Sha256=e.Sha256;
            current.Vision=e.Vision;
            current.ArtifactVersion=e.ArtifactVersion;
            current.Featured=e.Featured;current.NewUntilUtc=e.NewUntilUtc;
            current.PublishedAtUtc=e.PublishedAtUtc??current.PublishedAtUtc;
            if(!string.IsNullOrWhiteSpace(e.FamilyId))
            {
                current.FamilyId=e.FamilyId;current.FamilyDisplayName=e.FamilyDisplayName;
                current.VariantName=e.VariantName;current.VariantOrder=e.VariantOrder;
            }
            if(fullProfile)
            {
                current.Revision=e.Revision;current.DownloadAvailable=e.DownloadAvailable;current.RecommendedContext=e.RecommendedContext;
                current.RecommendedMaxTokens=e.RecommendedMaxTokens;current.RecommendedKvPrecision=e.RecommendedKvPrecision;
                current.RecommendedSpeculativeMode=e.RecommendedSpeculativeMode;current.RecommendedDraftTokens=e.RecommendedDraftTokens;
                current.RecommendedLmHeadDraft=e.RecommendedLmHeadDraft;current.RecommendedPrefillChunk=e.RecommendedPrefillChunk;
                current.NativeContext=e.NativeContext;current.MaximumContext=e.MaximumContext;current.BaseVramMiB=e.BaseVramMiB;
                current.KvMiBPerKToken=e.KvMiBPerKToken;current.RecommendedGdnStateFp16=e.RecommendedGdnStateFp16;
                current.RecommendedMtpAttentionWindow=e.RecommendedMtpAttentionWindow;
                current.RecommendedNgramDraftTokens=e.RecommendedNgramDraftTokens;
                current.RecommendedStructuredOutput=e.RecommendedStructuredOutput;
            }
        }
    }
    private static bool MatchesArtifactName(ModelCatalogEntry entry,FileInfo file) => entry.SizeBytes==file.Length
        && (entry.FileName.Equals(file.Name,StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(entry.RemoteFileName)&&entry.RemoteFileName.Equals(file.Name,StringComparison.OrdinalIgnoreCase)));
    private static void ValidateRemote(ModelCatalogEntry entry)
    {
        if(Path.GetFileName(entry.FileName)!=entry.FileName||!entry.FileName.EndsWith(".ninfer",StringComparison.OrdinalIgnoreCase)
            ||!IsPublisherModel(entry)||entry.Repository.Split('/').Length!=2||entry.Revision.Length!=40||!entry.Revision.All(Uri.IsHexDigit)
            ||entry.Sha256.Length!=64||!entry.Sha256.All(Uri.IsHexDigit)||entry.SizeBytes<=0||entry.ArtifactVersion!=3
            ||entry.RecommendedContext is <1024 or >1048576||entry.MaximumContext is <1024 or >1048576||entry.RecommendedContext>entry.MaximumContext)
            throw new InvalidDataException($"The remote profile for {entry.DisplayName} is invalid.");
    }
    private static bool IsPublisherModel(ModelCatalogEntry entry)=>entry.Repository.StartsWith("2beng2/",StringComparison.Ordinal);
    private void RestoreExternalArtifacts()
    {
        foreach(var pair in _settings.Current.ExternalModelPaths.ToArray())
        {
            var info=new FileInfo(pair.Value);
            var entry=Find(pair.Key);
            if(entry is null)
            {
                entry=CreateLocalEntry(pair.Key,info.Exists?info.Length:0,info.Name);
                if(_entries.Any(x=>x.ModelId.Equals(entry.ModelId,StringComparison.Ordinal)))entry.ModelId=Path.GetFileNameWithoutExtension(pair.Key);
                lock(_sync)_entries.Add(entry);
            }
            if(info.Exists)ImportAdjacentPreset(entry,info.FullName);
            ValidateCachedInspection(entry);
        }
    }
    private ModelCatalogEntry AddLocalEntry(string fileName,long size,string source)
    {
        var key=fileName;
        if(_entries.Any(x=>x.FileName.Equals(key,StringComparison.OrdinalIgnoreCase)))
            key=$"{Path.GetFileNameWithoutExtension(fileName)}-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(source).ToUpperInvariant())))[..10].ToLowerInvariant()}.ninfer";
        var entry=CreateLocalEntry(key,size,Path.GetFileName(source));
        if(_entries.Any(x=>x.ModelId.Equals(entry.ModelId,StringComparison.Ordinal)))entry.ModelId=Path.GetFileNameWithoutExtension(key);
        _entries.Add(entry);
        return entry;
    }
    private static ModelCatalogEntry CreateLocalEntry(string key,long size,string displayFileName)
    {
        var name=Path.GetFileNameWithoutExtension(displayFileName);
        return new()
        {
            DisplayName=name,
            Repository="Local file",
            FileName=key,
            ModelId=name,
            Weights="NInfer artifact (not inspected)",
            SizeBytes=size,
            Sha256="",
            Vision=false,
            DownloadAvailable=false,
            ArtifactVersion=3,
            RecommendedContext=32768,
            RecommendedMaxTokens=32768,
            RecommendedKvPrecision=KvPrecision.Fp8,
            RecommendedSpeculativeMode=SpeculativeMode.Disabled,
            RecommendedDraftTokens=3,
            RecommendedLmHeadDraft=false,
            RecommendedPrefillChunk=2048,
            NativeContext=0,
            MaximumContext=1048576
        };
    }
    private void ImportAdjacentPreset(ModelCatalogEntry entry,string modelPath)
    {
        var sidecar=Path.ChangeExtension(modelPath,".ninferez.json");
        if(!File.Exists(sidecar))return;
        try
        {
            if(entry.IgnoredAdjacentPresetFingerprint is{} ignored&&ignored==SmallPresetFingerprint(sidecar))return;
            var profile=ModelPresetImporter.Read(sidecar,entry,entry.RecommendedProfile());
            RememberPresetIdentity(entry,sidecar);
            entry.InvalidAdjacentPreset=false;
            entry.IgnoredAdjacentPresetFingerprint=null;
            if(_settings.Current.Profiles.TryGetValue(entry.FileName,out var saved))
            {
                if(JsonSerializer.Serialize(saved,SettingsService.JsonOptions)!=JsonSerializer.Serialize(profile,SettingsService.JsonOptions))
                    entry.PresetStatus="Saved custom settings retained; adjacent preset identity "+(entry.IdentityVerified?"verified.":"unverified (use Verify to check SHA-256).");
            }
            else _settings.Update(s=>s.Profiles[entry.FileName]=profile);
        }
        catch(Exception ex) when(ex is JsonException or ArgumentException or InvalidDataException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            entry.PresetStatus="Adjacent preset could not be imported: "+ex.Message+". Import a corrected preset before loading.";
            entry.InvalidAdjacentPreset=true;
        }
    }
    public ModelProfile ImportPreset(ModelCatalogEntry entry,string path)
    {
        var profile=ModelPresetImporter.Read(path,entry,_settings.ProfileFor(entry));
        RememberPresetIdentity(entry,path);
        _settings.Update(s=>s.Profiles[entry.FileName]=profile);
        entry.InvalidAdjacentPreset=false;
        entry.IgnoredAdjacentPresetFingerprint=SmallPresetFingerprint(Path.ChangeExtension(ResolvePath(entry),".ninferez.json"));
        SaveCache();
        return profile;
    }
    public void RememberCustomProfile(ModelCatalogEntry entry)
    {
        entry.InvalidAdjacentPreset=false;entry.StarterProfile=false;
        entry.PresetStatus="Custom profile settings saved; model identity "+(entry.IdentityVerified?"verified.":"unverified.");
        entry.IgnoredAdjacentPresetFingerprint=SmallPresetFingerprint(Path.ChangeExtension(ResolvePath(entry),".ninferez.json"));
        SaveCache();
    }
    public void MarkSeen(ModelCatalogEntry entry)=>_settings.Update(s=>s.SeenModelArtifacts.Add(AnnouncementIdentity(entry)));
    private bool IsRecent(ModelCatalogEntry entry)=>entry.DownloadAvailable&&entry.Repository!="Local file"
        &&ModelAnnouncementPolicy.IsRecent(_time.GetUtcNow(),entry.PublishedAtUtc,entry.FirstSeenUtc,entry.NewUntilUtc);
    public CatalogNotificationSnapshot Notifications(bool startupComplete)
    {
        lock(_sync)
        {
            var recent=_entries.Where(x=>x.Featured&&IsRecent(x)).ToArray();
            var seen=_settings.Current.SeenModelArtifacts;
            return new(Interlocked.Read(ref _revision),startupComplete,recent.Select(x=>x.FileName).ToArray(),
                recent.Where(x=>!seen.Contains(AnnouncementIdentity(x))&&!seen.Contains(SeenIdentity(x)))
                    .Select(x=>new ModelAnnouncement(AnnouncementIdentity(x),x.FileName,
                        string.IsNullOrWhiteSpace(x.FamilyDisplayName)?x.DisplayName:x.FamilyDisplayName,x.VariantName)).ToArray());
        }
    }
    public void DismissAnnouncements(IReadOnlyList<string> identities)
    {
        if(identities.Count is <1 or >1000||identities.Any(x=>x is null||x.Length>128))throw new ArgumentException("Choose the model announcements to dismiss.");
        lock(_sync)
        {
            var known=_entries.Where(x=>x.DownloadAvailable).Select(AnnouncementIdentity).ToHashSet(StringComparer.Ordinal);
            if(identities.Any(x=>!known.Contains(x)))throw new ArgumentException("A model announcement is no longer available. Refresh the library.");
            _settings.Update(s=>{foreach(var identity in identities)s.SeenModelArtifacts.Add(identity);});
        }
    }
    private static string AnnouncementIdentity(ModelCatalogEntry entry)=>"catalog:"+Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{entry.Repository}/{entry.FileName}/{entry.Sha256}"))).ToLowerInvariant();
    public void RememberVerification(ModelCatalogEntry entry)
    {
        entry.VerifiedWriteUtc=File.GetLastWriteTimeUtc(ResolvePath(entry));
        SaveCache();
    }
    private void SaveCache()
    {
        lock(_sync)
        {
            var contents=JsonSerializer.Serialize(_entries,SettingsService.JsonOptions);
            if(_entries.Count>4000||System.Text.Encoding.UTF8.GetByteCount(contents)>8*1024*1024)throw new InvalidDataException("The local model metadata cache is too large.");
            var temporary=_paths.CatalogCacheFile+".tmp";
            File.WriteAllText(temporary,contents);
            File.Move(temporary,_paths.CatalogCacheFile,true);
        }
    }
    private static string? SmallPresetFingerprint(string path)
    {
        try
        {
            var info=new FileInfo(path);
            if(!info.Exists)return null;
            if(info.Length>64*1024)return $"oversized:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            using var stream=File.OpenRead(path);
            var bytes=new byte[(int)info.Length];stream.ReadExactly(bytes);
            if(stream.ReadByte()!=-1)return null;
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return null;}
    }
    private void ValidateCachedInspection(ModelCatalogEntry entry)
    {
        var model= new FileInfo(ResolvePath(entry));
        if(!model.Exists||model.Length!=entry.SizeBytes||model.LastWriteTimeUtc!=entry.VerifiedWriteUtc)entry.IdentityVerified=false;
        if(!entry.IdentityVerified&&entry.PresetStatus is not null)
            entry.PresetStatus=entry.PresetStatus.Replace("identity verified","identity unverified (use Verify to check SHA-256)",StringComparison.Ordinal);
        if(!entry.Inspected)return;
        var inspector=entry.InspectedEngineDirectory is null?null:new FileInfo(Path.Combine(entry.InspectedEngineDirectory,"ninfer-inspect.exe"));
        if(model.Exists&&model.Length==entry.SizeBytes&&entry.InspectedModelPath is not null&&model.FullName.Equals(entry.InspectedModelPath,StringComparison.OrdinalIgnoreCase)
            &&model.LastWriteTimeUtc==entry.InspectedWriteUtc&&inspector?.Exists==true&&inspector.LastWriteTimeUtc==entry.InspectorWriteUtc)return;
        entry.Inspected=false;entry.HasMtp=false;entry.HasDflash=false;entry.HasDflash2=false;entry.CompatibleWithBuild=true;
        entry.InspectionMessage="Artifact or inspector changed; inspect with the selected engine to refresh its capabilities.";
        if(entry.Repository=="Local file"){entry.Vision=false;entry.NativeContext=0;entry.MaximumContext=1048576;entry.YarnSupported=false;}
    }
    private static string SeenIdentity(ModelCatalogEntry entry)=>$"{entry.Repository}/{entry.ModelId}/{entry.Revision}/{entry.Sha256}";
    private static void RememberPresetIdentity(ModelCatalogEntry entry,string path)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(path));
        entry.ExpectedSha256=document.RootElement.GetProperty("model").GetProperty("sha256").GetString();
        entry.PresetStatus=$"{document.RootElement.GetProperty("mode").GetString()} settings imported; model identity {(entry.IdentityVerified?"verified":"unverified (use Verify to check SHA-256)")}.";
    }
    public ModelCatalogEntry Relink(ModelCatalogEntry entry,string path)
    {
        path=Path.GetFullPath(path);
        if(!File.Exists(path)||!path.EndsWith(".ninfer",StringComparison.OrdinalIgnoreCase))throw new FileNotFoundException("Choose an existing .ninfer model artifact.",path);
        if(entry.SizeBytes>0&&new FileInfo(path).Length!=entry.SizeBytes)throw new InvalidDataException("The replacement has a different size. Link it as a new model instead of reusing this profile.");
        _settings.Update(s=>s.ExternalModelPaths[entry.FileName]=path);
        entry.SizeBytes=new FileInfo(path).Length;
        entry.IdentityVerified=false;entry.VerifiedWriteUtc=null;
        entry.InspectedModelPath=null;ValidateCachedInspection(entry);
        SaveCache();
        return entry;
    }
    public async Task InspectAsync(ModelCatalogEntry entry,string engineDirectory,CancellationToken token=default)
    {
        var file=ResolvePath(entry);var sourceWrite=File.GetLastWriteTimeUtc(file);var inspectorWrite=File.GetLastWriteTimeUtc(Path.Combine(engineDirectory,"ninfer-inspect.exe"));
        if(entry.Inspected&&entry.InspectedWriteUtc==sourceWrite&&entry.InspectorWriteUtc==inspectorWrite&&entry.InspectedEngineDirectory==engineDirectory)return;
        using var result=await ModelInspection.ReadAsync(engineDirectory,ResolvePath(entry),token);
        if(result is null){entry.InspectionMessage="Install a compatible engine to inspect this artifact.";return;}
        var root=result.RootElement;
        if(root.GetProperty("containerVersion").GetInt32()!=3||root.GetProperty("fileBytes").GetInt64()!=entry.SizeBytes)throw new InvalidDataException("The inspected artifact identity does not match the linked file.");
        var features=root.GetProperty("modelFeatures");
        lock(_sync)
        {
            entry.ArtifactVersion=3;entry.Inspected=true;entry.Vision=features.GetProperty("vision").GetBoolean();
            entry.InspectedWriteUtc=sourceWrite;entry.InspectorWriteUtc=inspectorWrite;entry.InspectedEngineDirectory=engineDirectory;entry.InspectedModelPath=Path.GetFullPath(file);
            entry.HasMtp=features.GetProperty("mtp").GetBoolean();entry.HasDflash=features.GetProperty("dflash").GetBoolean();entry.HasDflash2=features.GetProperty("dflash2").GetBoolean();
            entry.CompatibleWithBuild=root.GetProperty("compatibility").GetProperty("compatibleWithBuild").GetBoolean();
            entry.InspectionMessage=entry.CompatibleWithBuild?"Artifact metadata validated by the selected engine (no GPU allocation).":"This artifact is incompatible with the selected engine build.";
            entry.Weights=root.TryGetProperty("weightFormats",out var formats)?string.Join(", ",formats.EnumerateArray().Select(x=>x.GetString())):"NInfer v3";
            foreach(var component in root.GetProperty("components").EnumerateArray())
            {
                if(component.GetProperty("name").GetString() is not("model" or "target" or "text"))continue;
                var config=component.GetProperty("config");
                if(config.TryGetProperty("model_type",out var kind))entry.YarnSupported=kind.GetString()?.StartsWith("qwen",StringComparison.Ordinal)==true;
                if(config.TryGetProperty("max_position_embeddings",out var context)&&context.TryGetInt32(out var native)&&native>=1024)
                {
                    entry.NativeContext=native;
                    if(entry.Repository=="Local file"&&!entry.YarnSupported)entry.MaximumContext=native;
                    if(entry.StarterProfile&&_settings.Current.Profiles.TryGetValue(entry.FileName,out var starter)&&starter.MaxContext>native)
                        _settings.Update(s=>{s.Profiles[entry.FileName].MaxContext=native;s.Profiles[entry.FileName].CustomKvCapacity=native;s.Profiles[entry.FileName].DefaultMaxTokens=Math.Min(starter.DefaultMaxTokens,native);});
                }
            }
        }
        SaveCache();
    }
    private sealed class ModelCatalogFeed { public int SchemaVersion { get; set; } public string Product { get; set; }=""; public int Revision { get; set; } public DateTimeOffset UpdatedAt { get; set; } public List<ModelCatalogEntry> Models { get; set; }=[]; }
}
