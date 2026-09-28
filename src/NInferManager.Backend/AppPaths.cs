namespace NInferManager.Backend;

public sealed class AppPaths
{
    public AppPaths()
    {
        var processDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        AppDirectory = Path.GetFileName(processDirectory).Equals("Backend", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(processDirectory)?.FullName ?? processDirectory
            : processDirectory;
        IsPortable = File.Exists(Path.Combine(AppDirectory, "portable.mode"));
        var dataOverride = Environment.GetEnvironmentVariable("NINFEREZ_MANAGER_DATA_ROOT");
        var modelOverride = Environment.GetEnvironmentVariable("NINFEREZ_MANAGER_MODELS_ROOT");
        var engineOverride = Environment.GetEnvironmentVariable("NINFEREZ_MANAGER_ENGINE_ROOT");
        DataDirectory = Resolve(dataOverride, Path.Combine(AppDirectory, "Data"));
        ModelsDirectory = Resolve(modelOverride, Path.Combine(AppDirectory, "Models"));
        EngineDirectory = Resolve(engineOverride, Path.Combine(AppDirectory, "Engine"));
        EnginesDirectory = Path.Combine(DataDirectory, "engines");
        CurrentEngineFile = Path.Combine(EnginesDirectory, "current.json");
        EngineCatalogCacheFile = Path.Combine(DataDirectory, "engine-channel.cache.json");
        SettingsFile = Path.Combine(DataDirectory, "settings.json");
        LogFile = Path.Combine(DataDirectory, "Logs", "manager.log");
        CatalogCacheFile = Path.Combine(DataDirectory, "catalog.cache.json");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ModelsDirectory);
        Directory.CreateDirectory(EnginesDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
    }

    public string AppDirectory { get; }
    public bool IsPortable { get; }
    public string DataDirectory { get; }
    public string ModelsDirectory { get; }
    public string EngineDirectory { get; }
    public string EnginesDirectory { get; }
    public string CurrentEngineFile { get; }
    public string EngineCatalogCacheFile { get; }
    public string SettingsFile { get; }
    public string LogFile { get; }
    public string CatalogCacheFile { get; }
    private static string Resolve(string? value, string fallback) => Path.GetFullPath(string.IsNullOrWhiteSpace(value) ? fallback : value);
}
