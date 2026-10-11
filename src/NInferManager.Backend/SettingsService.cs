using System.Text.Json;
using System.Text.Json.Serialization;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public sealed class SettingsService
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly object _gate = new();
    private readonly AppPaths _paths;
    public SettingsService(AppPaths paths) { _paths = paths; Current = Load(); }
    public ManagerSettings Current { get; private set; }
    public string? RecoveryNotice { get; private set; }

    public ManagerSettings Replace(ManagerSettings value)
    {
        Validate(value);
        lock (_gate)
        {
            if (value.Revision != 0 && value.Revision != Current.Revision)
                throw new InvalidOperationException("Settings changed since this view was opened. Refresh and try again.");
            // These fields are owned by the model activate/import/remove endpoints.
            // A settings window may hold an older snapshot and must not undo them.
            value.ActiveModelFile = Current.ActiveModelFile;
            value.ExternalModelPaths = new Dictionary<string, string>(Current.ExternalModelPaths, StringComparer.OrdinalIgnoreCase);
            value.ModelAliases = new Dictionary<string, string>(Current.ModelAliases, StringComparer.OrdinalIgnoreCase);
            value.SeenModelArtifacts = new(Current.SeenModelArtifacts,StringComparer.Ordinal);
            value.Revision = Current.Revision;
            if (value.Profiles.Count == 0) value.Profiles = new(Current.Profiles, StringComparer.OrdinalIgnoreCase);
            value.LastCatalogCheckUtc = Current.LastCatalogCheckUtc;
            value.LastEngineCheckUtc = Current.LastEngineCheckUtc;
            value.LastUpdateCheckUtc = Current.LastUpdateCheckUtc;
            Commit(value);
            return Current;
        }
    }

    public ManagerSettings Patch(SettingsPatchRequest request)
    {
        lock (_gate)
        {
            if (request.ExpectedRevision != Current.Revision)
                throw new InvalidOperationException("Settings changed since this view was opened. Refresh and try again.");
            if (request.Changes.ValueKind != JsonValueKind.Object) throw new ArgumentException("Settings changes must be an object.");
            var next = Clone(Current);
            foreach (var change in request.Changes.EnumerateObject())
            {
                var property = typeof(ManagerSettings).GetProperties().FirstOrDefault(p => p.Name.Equals(change.Name, StringComparison.OrdinalIgnoreCase));
                if (property is null || property.Name is nameof(ManagerSettings.SeenModelArtifacts) or nameof(ManagerSettings.ActiveModelFile) or nameof(ManagerSettings.ModelAliases) or nameof(ManagerSettings.ExternalModelPaths) or nameof(ManagerSettings.Revision) or nameof(ManagerSettings.SchemaVersion) or nameof(ManagerSettings.LastCatalogCheckUtc) or nameof(ManagerSettings.LastEngineCheckUtc) or nameof(ManagerSettings.LastUpdateCheckUtc))
                    throw new ArgumentException($"Setting '{change.Name}' is not editable through this operation.");
                if (property.Name == nameof(ManagerSettings.Profiles))
                {
                    var updates = change.Value.Deserialize<Dictionary<string, ModelProfile>>(JsonOptions) ?? throw new ArgumentException("Profiles must be an object.");
                    foreach (var pair in updates) next.Profiles[pair.Key] = pair.Value;
                }
                else property.SetValue(next, change.Value.Deserialize(property.PropertyType, JsonOptions));
            }
            Validate(next);
            Commit(next);
            return Clone(Current);
        }
    }
    public ManagerSettings Snapshot() { lock (_gate) return Clone(Current); }
    public void Update(Action<ManagerSettings> mutation)
    {
        lock (_gate)
        {
            var next = Clone(Current);
            mutation(next);
            Validate(next);
            Commit(next);
        }
    }
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    public void Save() { lock (_gate) SaveUnsafe(); }
    public ModelProfile ProfileFor(ModelCatalogEntry model)
    {
        lock (_gate)
        {
            if (!Current.Profiles.TryGetValue(model.FileName, out var profile))
            {
                profile = model.RecommendedProfile();
                model.StarterProfile=model.Repository=="Local file";
                var next=Clone(Current);next.Profiles[model.FileName]=profile;
                Commit(next);
            }
            return Current.Profiles[model.FileName];
        }
    }
    public string ModelIdFor(ModelCatalogEntry model)
    {
        lock (_gate)
            return Current.ModelAliases.TryGetValue(model.FileName, out var alias) && !string.IsNullOrWhiteSpace(alias)
                ? alias.Trim()
                : model.ModelId;
    }

    private ManagerSettings Load()
    {
        try
        {
            var settings = File.Exists(_paths.SettingsFile) ? JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(_paths.SettingsFile), JsonOptions) ?? new() : new();
            if (settings.SchemaVersion != 1) throw new InvalidDataException("Unsupported settings schema.");
            Validate(settings);
            settings.Profiles = new(settings.Profiles, StringComparer.OrdinalIgnoreCase);
            settings.ModelAliases = new(settings.ModelAliases, StringComparer.OrdinalIgnoreCase);
            settings.ExternalModelPaths = new(settings.ExternalModelPaths, StringComparer.OrdinalIgnoreCase);
            settings.SeenModelArtifacts = new(settings.SeenModelArtifacts,StringComparer.Ordinal);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidDataException)
        {
            if (File.Exists(_paths.SettingsFile)) File.Copy(_paths.SettingsFile, _paths.SettingsFile + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"), false);
            RecoveryNotice="The settings file was invalid. A recovery copy was preserved and safe defaults were loaded.";
            return new();
        }
    }
    private void SaveUnsafe()
    {
        Commit(Clone(Current));
    }
    private void Commit(ManagerSettings candidate)
    {
        var temp = _paths.SettingsFile + ".tmp";
        candidate.Revision=Current.Revision+1;
        File.WriteAllText(temp, JsonSerializer.Serialize(candidate, JsonOptions));
        File.Move(temp, _paths.SettingsFile, true);
        Current=candidate;
    }
    public static void Validate(ManagerSettings s)
    {
        if (s.SchemaVersion != 1) throw new ArgumentException("Unsupported settings schema.");
        if (!Enum.IsDefined(s.Theme)) throw new ArgumentException("Invalid theme.");
        if(s.ApiKey is null||s.ApiKey.Length>256||s.ApiKey.Any(char.IsControl))throw new ArgumentException("API keys must contain at most 256 printable characters.");
        if (s.PublicPort is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(s.PublicPort), "Public API port must be between 1024 and 65535.");
        if (s.BackendPort is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(s.BackendPort), "Engine port must be between 1024 and 65535.");
        if (s.PublicPort == s.BackendPort) throw new ArgumentException("The public and internal ports must be different.");
        if (!double.IsFinite(s.IdleMinutes) || s.IdleMinutes is < 0 or > 1440) throw new ArgumentException("Model idle time must be between 0 and 1440 minutes. Use 3 minutes for the recommended unload policy.");
        if (s.CatalogCheckHours is < 1 or > 8760) throw new ArgumentException("Model catalog interval must be between 1 and 8760 hours. Use 24 for a daily check.");
        if (s.EngineCheckHours is < 1 or > 8760) throw new ArgumentException("Engine catalog interval must be between 1 and 8760 hours. Use 24 for a daily check.");
        if (s.UpdateCheckHours is < 1 or > 8760) throw new ArgumentException("Application update interval must be between 1 and 8760 hours. Use 24 for a daily check.");
        if (s.Profiles is null || s.ExternalModelPaths is null || s.ModelAliases is null) throw new ArgumentException("Profiles, aliases, and linked model paths must be valid objects.");
        if(s.SeenModelArtifacts is null||s.SeenModelArtifacts.Any(x=>x.Length>512))throw new ArgumentException("Seen artifact identifiers are invalid.");
        foreach(var link in s.ExternalModelPaths)
        {
            if(Path.GetFileName(link.Key)!=link.Key||!link.Key.EndsWith(".ninfer",StringComparison.OrdinalIgnoreCase)||string.IsNullOrWhiteSpace(link.Value)||!Path.IsPathFullyQualified(link.Value))throw new ArgumentException("Linked model entries must use safe filenames and absolute file paths.");
            try{_ = Path.GetFullPath(link.Value);}catch(Exception ex) when(ex is ArgumentException or NotSupportedException){throw new ArgumentException("A linked model path is invalid.",ex);}
        }
        foreach (var alias in s.ModelAliases.Values)
        {
            if (string.IsNullOrWhiteSpace(alias) || alias.Length > 128 || alias.Any(char.IsControl))
                throw new ArgumentException("API model names must contain 1-128 printable characters.");
        }
        foreach (var p in s.Profiles.Values)
        {
            if (p is null) throw new ArgumentException("A model profile cannot be null.");
            ValidateProfile(p);
        }
    }
    public static void ValidateProfile(ModelProfile p)
    {
            if (p.ManualAdvancedSettings is { } backup) ProfileSettingsPolicy.RestoreAdvanced(new ModelProfile(), backup);
            p = ProfileSettingsPolicy.Effective(p);
            if (!Enum.IsDefined(p.KvPrecision) || !Enum.IsDefined(p.KvCapacityMode) || !Enum.IsDefined(p.SpeculativeMode) || !Enum.IsDefined(p.LogLevel)) throw new ArgumentException("Invalid model profile option.");
            Range(p.Device, 0, 63, "CUDA device index");
            Range(p.NgramDraftTokens, 0, 63, "Repeated-text draft tokens");
            if (p.HostCacheMiB is int hostBudget) Range(hostBudget, 0, int.MaxValue, "Combined RAM cache (MiB)");
            if (p.KvCapacityMode == KvCapacityMode.Auto) Range(p.KvHeadroomMiB, 0, 65536, "KV memory reserve (MiB)");
            if (p.HostCacheMiB is not null && (p.HostStateSlots != 0 || p.HostKvMiB != 0)) throw new ArgumentException("Host cache budget cannot be combined with explicit host KV or state budgets.");
            if (p.KvCapacityMode == KvCapacityMode.Custom)
            {
                Range(p.CustomKvCapacity, 1024, 1048576, "Custom KV capacity (tokens)");
                if (p.CustomKvCapacity < p.MaxContext) throw new ArgumentException("Custom KV capacity is smaller than Context limit. Increase it to at least the context limit, or choose Match context.");
            }
            Range(p.MaxPendingRequests, 1, 10000, "Pending request limit");
            Range(p.PendingTimeoutMs, 1, 86400000, "Pending request timeout (ms)");
            Range(p.MaxRequestMiB, 1, 1024, "Maximum request body (MiB)");
            if (p.VisionEnabled)
            {
                Range(p.MediaCacheMiB, 0, int.MaxValue, "Image cache budget (MiB)");
                Range(p.MediaLiveMiB, 1, int.MaxValue, "Live image budget (MiB)");
                Range(p.MediaPreprocessThreads, 0, 64, "Image processing workers");
            }
            Range(p.DeviceStateSlots, 0, int.MaxValue, "GPU state slots");
            Range(p.HostStateSlots, 0, int.MaxValue, "Host state slots");
            Range(p.HostKvMiB, 0, int.MaxValue, "RAM context cache (MiB)");
            Range(p.ResponseStoreMaxRecords, 1, int.MaxValue, "Response history records");
            Range(p.ResponseStoreMaxMiB, 1, int.MaxValue, "Response history memory (MiB)");
            Range(p.LogStatsIntervalMs, 0, int.MaxValue, "Statistics interval (ms)");
            Range(p.MtpAttentionWindow, 0, int.MaxValue, "MTP attention window (tokens)");
            Range(p.MaxPrivateContinuations, -1, int.MaxValue, "Private continuation limit");
            Range(p.MaxSharedPrefixes, -1, int.MaxValue, "Shared prefix limit");
            Range(p.MaxLongAnchorsPerContinuation, -1, int.MaxValue, "Long anchor limit");
            Range(p.MaxCacheMarkersPerRequest, -1, int.MaxValue, "Cache marker limit");
            if (p.AlternativePrefixCache && !p.PrefixReuseEnabled) throw new ArgumentException("Alternative prefix caching needs Reuse previous prompt prefixes enabled. Enable prefix reuse or turn off alternative prefix caching.");
            if (p.Temperature is double temperature && (!double.IsFinite(temperature) || temperature < 0 || temperature > 2)) throw new ArgumentException("Temperature must be between 0 and 2.");
            if (p.TopP is double topP && (!double.IsFinite(topP) || topP < 0 || topP > 1)) throw new ArgumentException("Top-p must be between 0 and 1. Change this setting or leave it empty to use model defaults.");
            if (p.MinP is double minP && (!double.IsFinite(minP) || minP < 0 || minP > 1)) throw new ArgumentException("Min-p must be between 0 and 1. Change this setting or leave it empty to use model defaults.");
            if (p.TopK is int topK) Range(topK, 0, 20, "Top-k");
            if (p.DefaultThinkingBudget is int thinking) Range(thinking, 0, int.MaxValue, "Thinking budget (tokens)");
            if (p.PresencePenalty is double presence && (!double.IsFinite(presence) || presence is < -2 or > 2)) throw new ArgumentException("Presence penalty must be between -2 and 2. Change it or leave it empty to use model defaults.");
            if (p.FrequencyPenalty is double frequency && (!double.IsFinite(frequency) || frequency is < -2 or > 2)) throw new ArgumentException("Frequency penalty must be between -2 and 2. Change it or leave it empty to use model defaults.");
            Range(p.MaxContext, 1024, 1048576, "Context limit (tokens)");
            Range(p.PrefillChunk, 128, 65536, "Prompt processing batch");
            if (p.PrefillChunk % 128 != 0) throw new ArgumentException("Prompt processing batch must be a multiple of 128, such as 1024, 2048 or 4096. Use 2048 or turn on Manage advanced settings for me.");
            if (p.MaxConcurrency != 1) throw new ArgumentException("Active requests must be 1. This Manager processes one generation at a time and queues other requests.");
            Range(p.DefaultMaxTokens, 1, 1048576, "Default output limit (tokens)");
            if (p.DefaultMaxTokens > p.MaxContext) throw new ArgumentException("Default output limit is larger than Context limit. Lower the output limit or turn on Manage advanced settings for me.");
            var maxDraft = p.SpeculativeMode == SpeculativeMode.Mtp ? 5 : 15;
            if (p.SpeculativeMode != SpeculativeMode.Disabled) Range(p.DraftTokens, 1, maxDraft, "Draft tokens");
            if (p.SpeculativeMode == SpeculativeMode.Mtp && p.MtpAttentionWindow > 0 && p.MtpAttentionWindow < p.DraftTokens + 1)
                throw new ArgumentException("MTP attention window must be zero (model default) or at least Draft tokens + 1.");
    }
    private static void Range(int value, int min, int max, string label)
    {
        if (value < min || value > max)
            throw new ArgumentException($"{label} is {value:N0}; it must be between {min:N0} and {max:N0}. Change this setting or turn on Manage advanced settings for me in Inference profile.");
    }
}

public sealed record SettingsPatchRequest(long ExpectedRevision, JsonElement Changes);
