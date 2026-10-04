namespace NInferManager.WinUI;

// This list is the settings product contract, not automatic property discovery.
// A new DTO property does not become an editable engine flag without a descriptor.
internal sealed record SettingDescriptor(string Property, string Label, string Description, string Group,
    double Minimum = 0, double Maximum = 1_000_000, bool VisionOnly = false, bool SpeculativeOnly = false);

internal static class SettingDescriptors
{
    public static IReadOnlyList<SettingDescriptor> Application { get; } =
    [
        new("CatalogCheckHours", "Model catalog interval (hours)", "Checks are cached. Lower values do not make local model loading faster.", "Background checks", 1, 168),
        new("EngineCheckHours", "Engine catalog interval (hours)", "How often a requested engine catalog refresh may check online.", "Background checks", 1, 168),
        new("UpdateCheckHours", "Application update interval (hours)", "Updates are offered, never installed silently during inference.", "Background checks", 1, 168)
    ];

    public static IReadOnlyList<SettingDescriptor> Profile { get; } =
    [
        new("DefaultMaxTokens", "Default output limit (tokens)", "Used only when a client omits max_tokens; the engine still enforces its context limit.", "Context and memory", 1, 524288),
        new("Device", "CUDA device index", "Zero selects the first GPU. This is a launch setting; verify the device index before loading.", "Context and memory", 0, 31),
        new("KvCapacityMode", "KV capacity policy", "Match context keeps the KV allocation aligned with your context limit.", "Context and memory"),
        new("CustomKvCapacity", "Custom KV capacity (tokens)", "Only used when the KV capacity policy is Custom.", "Context and memory", 1024, 524288),
        new("HostKvMiB", "Host KV cache budget (MiB)", "Zero disables a custom host budget. CPU offload may reduce performance.", "Context and memory", 0, 1048576),
        new("HostCacheMiB", "Host cache budget (MiB)", "Empty preserves the engine default. A custom host budget may consume substantial system memory.", "Context and memory", 0, 1048576),
        new("KvHeadroomMiB", "KV memory reserve (MiB)", "Headroom left by the engine's KV allocation policy; this is not a guarantee against all out-of-memory conditions.", "Context and memory", 0, 65536),
        new("DeviceStateSlots", "GPU state slots", "Zero uses the engine default; extra retained state consumes VRAM.", "Context and memory", 0, 4096),
        new("HostStateSlots", "Host state slots", "Zero uses the engine default; retained state consumes system memory.", "Context and memory", 0, 4096),
        new("LmHeadDraft", "LM-head drafting", "Use only with an artifact and speculative mode that support it.", "Acceleration", SpeculativeOnly:true),
        new("NgramDraftTokens", "N-gram draft tokens", "Zero disables N-gram drafting. Validate speed and output behavior before changing a tested preset.", "Acceleration", 0, 63),
        new("MtpAttentionWindow", "MTP attention window (tokens)", "Zero uses the model default. Changing the window can affect output quality.", "Acceleration", 0, 524288, SpeculativeOnly:true),
        new("GdnStateFp16", "FP16 GDN state", "Changes numerical state precision. Leave the recommended profile unchanged unless validated.", "Acceleration"),
        new("ThinkingEnabled", "Enable thinking", "Controls the model's thinking mode when supported by its chat template.", "Generation defaults"),
        new("PreserveThinking", "Preserve thinking history", "Retains thinking state in supported continuation workflows.", "Generation defaults"),
        new("DefaultThinkingBudget", "Thinking budget (tokens)", "Empty uses the engine default; clients may supply their own budget.", "Generation defaults", 0, 524288),
        new("Greedy", "Greedy sampling", "Deterministic token selection. This may override sampling defaults.", "Generation defaults"),
        new("Temperature", "Temperature", "Empty preserves the engine default. Changing this changes the output distribution.", "Generation defaults", 0, 2),
        new("TopP", "Top-p", "Empty preserves the engine default.", "Generation defaults", 0, 1),
        new("TopK", "Top-k", "Empty preserves the engine default; zero disables the cutoff when supported.", "Generation defaults", 0, 100000),
        new("MinP", "Min-p", "Empty preserves the engine default.", "Generation defaults", 0, 1),
        new("PresencePenalty", "Presence penalty", "Empty preserves the engine default.", "Generation defaults", -2, 2),
        new("FrequencyPenalty", "Frequency penalty", "Empty preserves the engine default.", "Generation defaults", -2, 2),
        new("Seed", "Random seed", "Empty uses the engine default. A fixed seed does not guarantee identical output across kernels.", "Generation defaults", -2147483648, 2147483647),
        new("MaxPendingRequests", "Pending request limit", "Bounds the Manager's waiting requests and the engine queue. One inference request runs at a time.", "API limits", 0, 10000),
        new("PendingTimeoutMs", "Pending request timeout (ms)", "How long a request may wait for admission; this is not a model startup timeout.", "API limits", 1, 86400000),
        new("MaxRequestMiB", "Maximum request body (MiB)", "Sets the request body budget. Large image or long-context payloads need more memory.", "API limits", 1, 1024),
        new("StructuredOutput", "Structured output mode", "Enables the engine's supported constrained-output route. Requires compatible client requests.", "API limits"),
        new("ResponseStoreMaxRecords", "Response history records", "Maximum retained Responses API records. Zero disables history when supported.", "API limits", 0, 100000),
        new("ResponseStoreMaxMiB", "Response history memory (MiB)", "Memory budget for the engine's response store.", "API limits", 0, 16384),
        new("MediaCacheMiB", "Image cache budget (MiB)", "Only applicable to vision models; cached media consumes memory.", "Vision", 0, 65536, VisionOnly:true),
        new("MediaLiveMiB", "Live image budget (MiB)", "Memory reserved for active image processing.", "Vision", 0, 65536, VisionOnly:true),
        new("MediaPreprocessThreads", "Image preprocessing threads", "Zero lets the engine choose automatically.", "Vision", 0, 256, VisionOnly:true),
        new("MaxPrivateContinuations", "Private continuation limit", "Minus one uses the engine default.", "Prefix and continuation state", -1, 100000),
        new("MaxSharedPrefixes", "Shared prefix limit", "Minus one uses the engine default.", "Prefix and continuation state", -1, 100000),
        new("MaxLongAnchorsPerContinuation", "Long anchors per continuation", "Minus one uses the engine default.", "Prefix and continuation state", -1, 100000),
        new("MaxCacheMarkersPerRequest", "Cache markers per request", "Minus one uses the engine default. Limits retained prefix markers for each request.", "Prefix and continuation state", -1, 100000),
        new("AlternativePrefixCache", "Alternative prefix cache", "An expert cache policy. Keep the recommended default unless its behavior has been validated for your workload.", "Prefix and continuation state"),
        new("LogLevel", "Engine log level", "Trace and Debug produce more output and may affect performance.", "Diagnostics"),
        new("LogStatsIntervalMs", "Statistics interval (ms)", "Zero disables periodic statistics when supported.", "Diagnostics", 0, 3600000),
        new("ChatTemplateFile", "Chat template file", "Optional local template path. Changing the template can affect model behavior and formatting.", "Expert files"),
        new("ContextCostPresetsFile", "Context cost presets file", "Optional local JSON file; leave empty to use the model's current profile.", "Expert files"),
        new("RequestLogJsonlFile", "Request metrics JSONL file", "Optional output path. Logs may contain private metadata; review before sharing.", "Expert files")
    ];
}
