using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NInferManager.Contracts;

public static class ProfileSettingsPolicy
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
    public static IReadOnlyList<string> AdvancedProperties { get; } =
    [
        nameof(ModelProfile.DefaultMaxTokens), nameof(ModelProfile.Device), nameof(ModelProfile.KvCapacityMode),
        nameof(ModelProfile.CustomKvCapacity), nameof(ModelProfile.HostKvMiB), nameof(ModelProfile.HostCacheMiB),
        nameof(ModelProfile.KvHeadroomMiB), nameof(ModelProfile.DeviceStateSlots), nameof(ModelProfile.HostStateSlots),
        nameof(ModelProfile.LmHeadDraft), nameof(ModelProfile.NgramDraftTokens), nameof(ModelProfile.MtpAttentionWindow),
        nameof(ModelProfile.GdnStateFp16), nameof(ModelProfile.ThinkingEnabled), nameof(ModelProfile.PreserveThinking),
        nameof(ModelProfile.DefaultThinkingBudget), nameof(ModelProfile.Greedy), nameof(ModelProfile.Temperature),
        nameof(ModelProfile.TopP), nameof(ModelProfile.TopK), nameof(ModelProfile.MinP), nameof(ModelProfile.PresencePenalty),
        nameof(ModelProfile.FrequencyPenalty), nameof(ModelProfile.Seed), nameof(ModelProfile.MaxPendingRequests),
        nameof(ModelProfile.PendingTimeoutMs), nameof(ModelProfile.MaxRequestMiB), nameof(ModelProfile.StructuredOutput),
        nameof(ModelProfile.ResponseStoreMaxRecords), nameof(ModelProfile.ResponseStoreMaxMiB), nameof(ModelProfile.MediaCacheMiB),
        nameof(ModelProfile.MediaLiveMiB), nameof(ModelProfile.MediaPreprocessThreads), nameof(ModelProfile.MaxPrivateContinuations),
        nameof(ModelProfile.MaxSharedPrefixes), nameof(ModelProfile.MaxLongAnchorsPerContinuation), nameof(ModelProfile.MaxCacheMarkersPerRequest),
        nameof(ModelProfile.AlternativePrefixCache), nameof(ModelProfile.LogLevel), nameof(ModelProfile.LogStatsIntervalMs),
        nameof(ModelProfile.ChatTemplateFile), nameof(ModelProfile.ContextCostPresetsFile), nameof(ModelProfile.RequestLogJsonlFile),
        nameof(ModelProfile.PrefillChunk), nameof(ModelProfile.DraftTokens), nameof(ModelProfile.CudaGraphEnabled), nameof(ModelProfile.PrefixReuseEnabled)
    ];
    private static readonly IReadOnlyDictionary<string, PropertyInfo> Properties = AdvancedProperties
        .ToDictionary(x => x, x => typeof(ModelProfile).GetProperty(x)!, StringComparer.Ordinal);

    public static ModelProfile Clone(ModelProfile value) => JsonSerializer.Deserialize<ModelProfile>(JsonSerializer.Serialize(value, Json), Json)!;

    public static Dictionary<string, JsonElement> CaptureAdvanced(ModelProfile profile) => Properties
        .ToDictionary(x => x.Key, x => JsonSerializer.SerializeToElement(x.Value.GetValue(profile), x.Value.PropertyType, Json), StringComparer.Ordinal);

    public static void RestoreAdvanced(ModelProfile target, IReadOnlyDictionary<string, JsonElement> values)
    {
        if (values.Count > Properties.Count) throw new ArgumentException("The manual settings backup is invalid.");
        foreach (var (name, value) in values)
        {
            if (!Properties.TryGetValue(name, out var property) || value.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Undefined)
                throw new ArgumentException("The manual settings backup contains an unsupported setting.");
            try { property.SetValue(target, value.Deserialize(property.PropertyType, Json)); }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
            { throw new ArgumentException($"The manual backup for {name} is invalid.", error); }
        }
    }

    public static ModelProfile SwitchManagement(ModelProfile selected, bool enabled, ModelProfile recommended)
    {
        var next = Clone(selected);
        if (enabled && !selected.ManageAdvancedSettings) next.ManualAdvancedSettings = CaptureAdvanced(selected);
        if (!enabled && selected.ManageAdvancedSettings && next.ManualAdvancedSettings is { } backup) RestoreAdvanced(next, backup);
        next.ManageAdvancedSettings = enabled;
        return enabled ? Managed(next, recommended) : next;
    }

    public static ModelProfile Managed(ModelProfile selected, ModelProfile recommended)
    {
        var next = Clone(selected);
        next.ManualAdvancedSettings ??= CaptureAdvanced(selected);
        RestoreAdvanced(next, CaptureAdvanced(recommended));
        next.KvCapacityMode = next.AutoContext ? KvCapacityMode.Auto : KvCapacityMode.MatchContext;
        next.CustomKvCapacity = next.MaxContext;
        next.DefaultMaxTokens = Math.Min(Math.Max(1, recommended.DefaultMaxTokens), next.MaxContext);
        next.PrefillChunk = Math.Clamp(Math.Min(recommended.PrefillChunk, next.MaxContext) / 128 * 128, 128, 65536);
        next.DraftTokens = Math.Clamp(recommended.DraftTokens, 1, next.SpeculativeMode == SpeculativeMode.Mtp ? 5 : 15);
        next.HostCacheMiB = null;
        next.HostKvMiB = next.HostStateSlots = 0;
        next.ChatTemplateFile = next.ContextCostPresetsFile = next.RequestLogJsonlFile = string.Empty;
        if (next.SpeculativeMode != recommended.SpeculativeMode) next.MtpAttentionWindow = 0;
        return Effective(next);
    }

    public static ModelProfile Effective(ModelProfile saved)
    {
        var next = Clone(saved);
        if (next.SpeculativeMode == SpeculativeMode.Disabled)
        {
            next.LmHeadDraft = false;
            next.NgramDraftTokens = next.MtpAttentionWindow = 0;
        }
        else if (next.SpeculativeMode != SpeculativeMode.Mtp) next.MtpAttentionWindow = 0;
        if (!next.VisionEnabled) next.MediaCacheMiB = next.MediaLiveMiB = next.MediaPreprocessThreads = 0;
        if (next.HostCacheMiB is not null) next.HostKvMiB = next.HostStateSlots = 0;
        if (!next.ThinkingEnabled) next.DefaultThinkingBudget = null;
        if (next.Greedy) next.Temperature = next.TopP = next.MinP = null;
        if (next.Greedy) next.TopK = null;
        if (next.AlternativePrefixCache)
        {
            next.DeviceStateSlots = next.HostStateSlots = next.HostKvMiB = 0;
            next.MaxPrivateContinuations = next.MaxSharedPrefixes = next.MaxLongAnchorsPerContinuation = -1;
        }
        return next;
    }

    public static bool IsApplicable(string property, ModelProfile profile, bool supportsVision) => property switch
    {
        nameof(ModelProfile.LmHeadDraft) or nameof(ModelProfile.NgramDraftTokens) or nameof(ModelProfile.DraftTokens) => profile.SpeculativeMode != SpeculativeMode.Disabled,
        nameof(ModelProfile.MtpAttentionWindow) => profile.SpeculativeMode == SpeculativeMode.Mtp,
        nameof(ModelProfile.CustomKvCapacity) => profile.KvCapacityMode == KvCapacityMode.Custom,
        nameof(ModelProfile.KvHeadroomMiB) => profile.KvCapacityMode == KvCapacityMode.Auto,
        nameof(ModelProfile.HostKvMiB) or nameof(ModelProfile.HostStateSlots) => profile.HostCacheMiB is null && !profile.AlternativePrefixCache,
        nameof(ModelProfile.DeviceStateSlots) or nameof(ModelProfile.MaxPrivateContinuations) or nameof(ModelProfile.MaxSharedPrefixes) or nameof(ModelProfile.MaxLongAnchorsPerContinuation) => !profile.AlternativePrefixCache,
        nameof(ModelProfile.MediaCacheMiB) or nameof(ModelProfile.MediaLiveMiB) or nameof(ModelProfile.MediaPreprocessThreads) => supportsVision && profile.VisionEnabled,
        nameof(ModelProfile.DefaultThinkingBudget) => profile.ThinkingEnabled,
        nameof(ModelProfile.Temperature) or nameof(ModelProfile.TopP) or nameof(ModelProfile.TopK) or nameof(ModelProfile.MinP) => !profile.Greedy,
        _ => true
    };
}
