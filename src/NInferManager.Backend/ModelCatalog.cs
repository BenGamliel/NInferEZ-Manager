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
    public bool Featured { get; set; }
    public DateTimeOffset? NewUntilUtc { get; set; }
    public string DownloadUrl => $"https://huggingface.co/{Repository}/resolve/{Revision}/{Uri.EscapeDataString(string.IsNullOrWhiteSpace(RemoteFileName) ? FileName : RemoteFileName)}?download=true";
    public ModelProfile RecommendedProfile() => new()
    {
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
    private readonly HttpClient _http=new(){Timeout=TimeSpan.FromSeconds(20)};
    private readonly List<ModelCatalogEntry> _entries;
    public ModelCatalog(AppPaths paths, SettingsService settings)
    {
        _paths = paths; _settings = settings;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NInferManager.Backend.Assets.catalog.json")
            ?? throw new InvalidOperationException("Embedded model catalog is missing.");
        _entries = JsonSerializer.Deserialize<List<ModelCatalogEntry>>(stream, SettingsService.JsonOptions) ?? [];
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"NInferEZ-Manager/{ProductInfo.Version}");
        try{if(File.Exists(_paths.CatalogCacheFile)){var cache=JsonSerializer.Deserialize<List<ModelCatalogEntry>>(File.ReadAllText(_paths.CatalogCacheFile),SettingsService.JsonOptions);if(cache is not null)foreach(var entry in cache)Merge(entry);}}catch{}
        RestoreExternalArtifacts();
        RefreshLocalArtifacts();
    }
    public IReadOnlyList<ModelCatalogEntry> Entries => _entries;
    public ModelCatalogEntry? Find(string fileName) { lock(_sync)return _entries.FirstOrDefault(x => x.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)); }
    public string ResolvePath(ModelCatalogEntry e) => _settings.Current.ExternalModelPaths.TryGetValue(e.FileName, out var external) && !string.IsNullOrWhiteSpace(external)
        ? Path.GetFullPath(external) : Path.Combine(_paths.ModelsDirectory, e.FileName);
    public bool IsExternal(ModelCatalogEntry e) => _settings.Current.ExternalModelPaths.ContainsKey(e.FileName);
    public bool IsInstalled(ModelCatalogEntry e)
    {
        var path = ResolvePath(e);
        return File.Exists(path) && new FileInfo(path).Length == e.SizeBytes;
    }
    public IReadOnlyList<ModelInfo> Snapshot() { lock(_sync)return _entries.Select(e => new ModelInfo(e.DisplayName, e.Repository, e.FileName, _settings.ModelIdFor(e), e.Weights, e.SizeBytes, e.Vision, e.DownloadAvailable, IsInstalled(e), _settings.Current.ActiveModelFile.Equals(e.FileName, StringComparison.OrdinalIgnoreCase), e.NativeContext, e.MaximumContext, _settings.ProfileFor(e), e.Featured, e.NewUntilUtc is not null && e.NewUntilUtc > DateTimeOffset.UtcNow)).ToList(); }
    public ModelCatalogEntry LinkExternal(string source)
    {
        var path=Path.GetFullPath(source);
        if(!File.Exists(path))throw new FileNotFoundException("The selected model file does not exist.",path);
        if(!Path.GetExtension(path).Equals(".ninfer",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Choose a .ninfer model artifact.");
        var info=new FileInfo(path);
        ModelCatalogEntry entry;
        lock(_sync)
        {
            entry=_entries.FirstOrDefault(x=>x.FileName.Equals(info.Name,StringComparison.OrdinalIgnoreCase)&&x.SizeBytes==info.Length)
                ?? _entries.FirstOrDefault(x=>x.SizeBytes==info.Length)
                ?? AddLocalEntry(info.Name,info.Length,path);
        }
        _settings.Current.ExternalModelPaths[entry.FileName]=path;
        if(string.IsNullOrWhiteSpace(_settings.Current.ActiveModelFile))_settings.Current.ActiveModelFile=entry.FileName;
        _settings.ProfileFor(entry);
        _settings.Save();
        return entry;
    }
    public int RefreshLocalArtifacts()
    {
        var before=Snapshot().Count(x=>x.Installed);
        foreach(var path in Directory.EnumerateFiles(_paths.ModelsDirectory,"*.ninfer",SearchOption.TopDirectoryOnly))
            LinkExternal(path);
        return Math.Max(0,Snapshot().Count(x=>x.Installed)-before);
    }
    public bool ShouldRefresh()=>_settings.Current.AutoCheckCatalog&&(_settings.Current.LastCatalogCheckUtc is null||DateTimeOffset.UtcNow-_settings.Current.LastCatalogCheckUtc>=TimeSpan.FromHours(Math.Max(1,_settings.Current.CatalogCheckHours)));
    public async Task<int> RefreshOnlineAsync(CancellationToken token=default)
    {
        await _refreshGate.WaitAsync(token);
        try
        {
            var before=_entries.Select(x=>x.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            using var response=await _http.GetAsync(ProductInfo.ModelCatalogUrl,token);response.EnsureSuccessStatusCode();
            var feed=JsonSerializer.Deserialize<ModelCatalogFeed>(await response.Content.ReadAsStringAsync(token),SettingsService.JsonOptions)
                ?? throw new InvalidDataException("The NInferEZ model catalog is empty.");
            if(feed.SchemaVersion!=1||feed.Product!="NInferEZ Model Catalog")throw new InvalidDataException("The model catalog schema or product is unsupported.");
            foreach(var entry in feed.Models){ValidateRemote(entry);Merge(entry,true);}
            File.WriteAllText(_paths.CatalogCacheFile,JsonSerializer.Serialize(_entries,SettingsService.JsonOptions));_settings.Current.LastCatalogCheckUtc=DateTimeOffset.UtcNow;_settings.Save();
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
            if(i<0){_entries.Add(e);return;}
            // Online refresh owns changing artifact metadata. Runtime capability and
            // recommended launch settings stay curated by this application version.
            var current=_entries[i];
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
            if(fullProfile)
            {
                current.Revision=e.Revision;current.DownloadAvailable=e.DownloadAvailable;current.RecommendedContext=e.RecommendedContext;
                current.RecommendedMaxTokens=e.RecommendedMaxTokens;current.RecommendedKvPrecision=e.RecommendedKvPrecision;
                current.RecommendedSpeculativeMode=e.RecommendedSpeculativeMode;current.RecommendedDraftTokens=e.RecommendedDraftTokens;
                current.RecommendedLmHeadDraft=e.RecommendedLmHeadDraft;current.RecommendedPrefillChunk=e.RecommendedPrefillChunk;
                current.NativeContext=e.NativeContext;current.MaximumContext=e.MaximumContext;current.BaseVramMiB=e.BaseVramMiB;
                current.KvMiBPerKToken=e.KvMiBPerKToken;current.RecommendedGdnStateFp16=e.RecommendedGdnStateFp16;
                current.RecommendedMtpAttentionWindow=e.RecommendedMtpAttentionWindow;
            }
        }
    }
    private static void ValidateRemote(ModelCatalogEntry entry)
    {
        if(Path.GetFileName(entry.FileName)!=entry.FileName||!entry.FileName.EndsWith(".ninfer",StringComparison.OrdinalIgnoreCase)
            ||entry.Repository.Split('/').Length!=2||entry.Revision.Length!=40||!entry.Revision.All(Uri.IsHexDigit)
            ||entry.Sha256.Length!=64||!entry.Sha256.All(Uri.IsHexDigit)||entry.SizeBytes<=0||entry.ArtifactVersion!=3
            ||entry.RecommendedContext is <1024 or >1048576||entry.MaximumContext is <1024 or >1048576||entry.RecommendedContext>entry.MaximumContext)
            throw new InvalidDataException($"The remote profile for {entry.DisplayName} is invalid.");
    }
    private void RestoreExternalArtifacts()
    {
        foreach(var pair in _settings.Current.ExternalModelPaths.ToArray())
        {
            if(!File.Exists(pair.Value)||Find(pair.Key) is not null)continue;
            var info=new FileInfo(pair.Value);
            lock(_sync)_entries.Add(CreateLocalEntry(pair.Key,info.Length,info.Name));
        }
    }
    private ModelCatalogEntry AddLocalEntry(string fileName,long size,string source)
    {
        var key=fileName;
        if(_entries.Any(x=>x.FileName.Equals(key,StringComparison.OrdinalIgnoreCase)))
            key=$"{Path.GetFileNameWithoutExtension(fileName)}-{size:x}.ninfer";
        var entry=CreateLocalEntry(key,size,Path.GetFileName(source));
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
            Weights="NInfer v3",
            SizeBytes=size,
            Sha256="",
            Vision=false,
            DownloadAvailable=false,
            ArtifactVersion=3,
            RecommendedContext=131072,
            RecommendedMaxTokens=32768,
            RecommendedKvPrecision=KvPrecision.Fp8,
            RecommendedSpeculativeMode=SpeculativeMode.Disabled,
            RecommendedDraftTokens=3,
            RecommendedLmHeadDraft=false,
            RecommendedPrefillChunk=2048,
            NativeContext=262144,
            MaximumContext=524288
        };
    }
    private sealed class ModelCatalogFeed { public int SchemaVersion { get; set; } public string Product { get; set; }=""; public int Revision { get; set; } public DateTimeOffset UpdatedAt { get; set; } public List<ModelCatalogEntry> Models { get; set; }=[]; }
}
