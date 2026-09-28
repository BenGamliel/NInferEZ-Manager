namespace NInferManager.Contracts;

public enum EngineState { Unloaded, Loading, Ready, Unloading, Error }
public enum EngineOperationState { None, Running, Completed, Cancelled, Failed }
public enum ThemePreference { System, Light, Dark }
public enum KvPrecision { Bf16, Int8, Fp8, Nvfp4, K8v4, Rk8v4 }
public enum KvCapacityMode { MatchContext, Auto, Custom }
public enum SpeculativeMode { Disabled, Mtp, Dflash, Dflash2 }
public enum EngineLogLevel { Trace, Debug, Info, Warning, Error, Critical, Off }

public static class EditableSettingTypes
{
    public static bool Supports(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        return actual == typeof(string)
            || actual == typeof(bool)
            || actual == typeof(int)
            || actual == typeof(double)
            || actual.IsEnum;
    }
}

public sealed record ManagerStatus(
    EngineState Engine,
    string ApiBaseUrl,
    int PublicPort,
    bool PortChangedAutomatically,
    bool PortConflict,
    string? ActiveModelId,
    string? ActiveModelName,
    int ActiveRequests,
    GpuStatus? Gpu,
    DateTimeOffset UpdatedAt);

public sealed record EngineOperationStatus(
    string? OperationId,
    EngineOperationState State,
    string Phase,
    string Message,
    DateTimeOffset? StartedAt,
    double ElapsedSeconds,
    bool CanCancel,
    string? Error = null);

public sealed record GpuStatus(string Name, int UsedMiB, int TotalMiB, int UtilizationPercent, int TemperatureC);

public sealed class ManagerSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public bool StartMinimized { get; set; }
    public bool StartWithWindows { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool AutoCheckCatalog { get; set; } = true;
    public int CatalogCheckHours { get; set; } = 24;
    public DateTimeOffset? LastCatalogCheckUtc { get; set; }
    public bool AutoCheckEngines { get; set; } = true;
    public int EngineCheckHours { get; set; } = 24;
    public DateTimeOffset? LastEngineCheckUtc { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public int UpdateCheckHours { get; set; } = 24;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public int PublicPort { get; set; } = 8173;
    public bool LockPublicPort { get; set; }
    public int BackendPort { get; set; } = 48174;
    public string ApiKey { get; set; } = string.Empty;
    public bool CorsEnabled { get; set; } = true;
    public bool AutoUnloadEnabled { get; set; } = true;
    public double IdleMinutes { get; set; } = 3;
    public bool FirstRunCompleted { get; set; }
    public string ActiveModelFile { get; set; } = string.Empty;
    public Dictionary<string, string> ExternalModelPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ModelAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ModelProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelProfile
{
    public bool AutoContext { get; set; } = true;
    public bool VisionEnabled { get; set; } = true;
    public int MaxContext { get; set; } = 200_000;
    public int DefaultMaxTokens { get; set; } = 200_000;
    public KvPrecision KvPrecision { get; set; } = KvPrecision.Fp8;
    public KvCapacityMode KvCapacityMode { get; set; } = KvCapacityMode.MatchContext;
    public int CustomKvCapacity { get; set; } = 200_000;
    public int Device { get; set; }
    public SpeculativeMode SpeculativeMode { get; set; } = SpeculativeMode.Dflash2;
    public int DraftTokens { get; set; } = 7;
    public bool LmHeadDraft { get; set; }
    public bool CudaGraphEnabled { get; set; } = true;
    public bool PrefixReuseEnabled { get; set; } = true;
    public bool GdnStateFp16 { get; set; }
    public int MtpAttentionWindow { get; set; }
    public int PrefillChunk { get; set; } = 2048;
    public int MaxConcurrency { get; set; } = 1;
    public int MediaCacheMiB { get; set; } = 1024;
    public int MediaLiveMiB { get; set; } = 2048;
    public int MediaPreprocessThreads { get; set; }
    public int MaxPendingRequests { get; set; } = 50;
    public int PendingTimeoutMs { get; set; } = 3_000_000;
    public int MaxRequestMiB { get; set; } = 384;
    public int LogStatsIntervalMs { get; set; } = 5000;
    public int DeviceStateSlots { get; set; }
    public int HostStateSlots { get; set; }
    public int HostKvMiB { get; set; }
    public int ResponseStoreMaxRecords { get; set; } = 1024;
    public int ResponseStoreMaxMiB { get; set; } = 256;
    public bool ThinkingEnabled { get; set; } = true;
    public bool PreserveThinking { get; set; } = true;
    public int MaxPrivateContinuations { get; set; } = -1;
    public int MaxSharedPrefixes { get; set; } = -1;
    public int MaxLongAnchorsPerContinuation { get; set; } = -1;
    public int MaxCacheMarkersPerRequest { get; set; } = -1;
    public int? DefaultThinkingBudget { get; set; }
    public bool Greedy { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? TopK { get; set; }
    public double? MinP { get; set; }
    public double? PresencePenalty { get; set; }
    public double? FrequencyPenalty { get; set; }
    public int? Seed { get; set; }
    public string ContextCostPresetsFile { get; set; } = string.Empty;
    public string RequestLogJsonlFile { get; set; } = string.Empty;
    public string ChatTemplateFile { get; set; } = string.Empty;
    public EngineLogLevel LogLevel { get; set; } = EngineLogLevel.Info;
}

public sealed record ModelInfo(
    string DisplayName,
    string Repository,
    string FileName,
    string ModelId,
    string Weights,
    long SizeBytes,
    bool Vision,
    bool DownloadAvailable,
    bool Installed,
    bool Active,
    int NativeContext,
    int MaximumContext,
    ModelProfile RecommendedProfile,
    bool Featured = false,
    bool IsNew = false)
{
    public string ModelCardUrl => $"https://huggingface.co/{Repository}";
    public string SizeText => SizeBytes <= 0 ? "Unknown" : $"{SizeBytes / 1024d / 1024d / 1024d:0.00} GiB";
}

public sealed record DownloadState(string ModelFile, string Stage, long Completed, long Total, double BytesPerSecond, bool Running, string? Error = null);

public sealed record RequestMetric(
    DateTime StartedAt,
    int RequestId,
    string Type,
    string Status,
    int PromptTokens,
    int GeneratedTokens,
    int CachedTokens,
    double? TtftMs,
    double? PrefillTokensPerSecond,
    double? DecodeTokensPerSecond,
    double? WallSeconds,
    string Speculative);

public sealed record RequestMetricsSnapshot(
    IReadOnlyList<RequestMetric> Requests,
    int CompletedCount,
    int FailedCount,
    int TotalTokens,
    double? AverageDecode,
    double? AverageTtft);

public sealed record ApiResult(bool Success, string Message);
public sealed record UpdateInfo(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseUrl,
    string Message,
    string? AssetName = null,
    string? AssetUrl = null,
    string? Sha256 = null,
    long AssetSize = 0);

public sealed record UpdateProgress(string Stage, long Completed, long Total, bool Running, string? Error = null, string? PackagePath = null);

public sealed record EnginePackageInfo(
    string EngineVersion,
    string CudaArchitecture,
    string GpuFamily,
    IReadOnlyList<string> GpuModels,
    string Channel,
    string Qualification,
    bool NativeNvfp4,
    string FileName,
    long SizeBytes,
    string Sha256,
    string Url,
    bool Installed,
    bool Active,
    bool Recommended);

public sealed record EngineLibrarySnapshot(
    string? DetectedGpu,
    string? RecommendedArchitecture,
    string? ActiveArchitecture,
    string? ActiveVersion,
    IReadOnlyList<EnginePackageInfo> Packages,
    DateTimeOffset UpdatedAt);

public sealed record EnginePackageProgress(string Stage, string? Package, long Completed, long Total, bool Running, string? Error = null);
