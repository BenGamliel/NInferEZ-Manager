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

    public ManagerSettings Replace(ManagerSettings value)
    {
        Validate(value);
        lock (_gate)
        {
            // These fields are owned by the model activate/import/remove endpoints.
            // A settings window may hold an older snapshot and must not undo them.
            value.ActiveModelFile = Current.ActiveModelFile;
            value.ExternalModelPaths = new Dictionary<string, string>(Current.ExternalModelPaths, StringComparer.OrdinalIgnoreCase);
            value.LastCatalogCheckUtc = Current.LastCatalogCheckUtc;
            value.LastEngineCheckUtc = Current.LastEngineCheckUtc;
            value.LastUpdateCheckUtc = Current.LastUpdateCheckUtc;
            Current = value;
            SaveUnsafe();
            return Current;
        }
    }

    public void Save() { lock (_gate) SaveUnsafe(); }
    public ModelProfile ProfileFor(ModelCatalogEntry model)
    {
        lock (_gate)
        {
            if (!Current.Profiles.TryGetValue(model.FileName, out var profile))
            {
                profile = model.RecommendedProfile();
                Current.Profiles[model.FileName] = profile;
                SaveUnsafe();
            }
            return profile;
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
        try { return File.Exists(_paths.SettingsFile) ? JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(_paths.SettingsFile), JsonOptions) ?? new() : new(); }
        catch { return new(); }
    }
    private void SaveUnsafe()
    {
        var temp = _paths.SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(temp, _paths.SettingsFile, true);
    }
    public static void Validate(ManagerSettings s)
    {
        if (s.PublicPort is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(s.PublicPort), "Public API port must be between 1024 and 65535.");
        if (s.BackendPort is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(s.BackendPort), "Engine port must be between 1024 and 65535.");
        if (s.PublicPort == s.BackendPort) throw new ArgumentException("The public and internal ports must be different.");
        if (s.IdleMinutes is < 0.1 or > 1440) throw new ArgumentOutOfRangeException(nameof(s.IdleMinutes));
        if (s.CatalogCheckHours is < 1 or > 8760) throw new ArgumentOutOfRangeException(nameof(s.CatalogCheckHours));
        if (s.EngineCheckHours is < 1 or > 8760) throw new ArgumentOutOfRangeException(nameof(s.EngineCheckHours));
        if (s.UpdateCheckHours is < 1 or > 8760) throw new ArgumentOutOfRangeException(nameof(s.UpdateCheckHours));
        if (s.Profiles is null || s.ExternalModelPaths is null || s.ModelAliases is null) throw new ArgumentException("Profiles, aliases, and linked model paths must be valid objects.");
        foreach (var alias in s.ModelAliases.Values)
        {
            if (string.IsNullOrWhiteSpace(alias) || alias.Length > 128 || alias.Any(char.IsControl))
                throw new ArgumentException("API model names must contain 1-128 printable characters.");
        }
        foreach (var p in s.Profiles.Values)
        {
            if (p.MaxContext is < 1024 or > 1_048_576) throw new ArgumentOutOfRangeException(nameof(p.MaxContext));
            if (p.PrefillChunk is < 0 or > 65536) throw new ArgumentOutOfRangeException(nameof(p.PrefillChunk));
            if (p.MaxConcurrency != 1) throw new ArgumentOutOfRangeException(nameof(p.MaxConcurrency), "NInferEZ Manager currently supports exactly one active request.");
            if (p.DefaultMaxTokens is < 1 or > 1_048_576) throw new ArgumentOutOfRangeException(nameof(p.DefaultMaxTokens));
            var maxDraft = p.SpeculativeMode == SpeculativeMode.Mtp ? 5 : 15;
            if (p.SpeculativeMode != SpeculativeMode.Disabled && (p.DraftTokens < 1 || p.DraftTokens > maxDraft)) throw new ArgumentOutOfRangeException(nameof(p.DraftTokens));
        }
    }
}
