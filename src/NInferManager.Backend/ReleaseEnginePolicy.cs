using System.Reflection;
using System.Text.Json;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public sealed record ReleaseEnginePackage(string EngineVersion, int ContractVersion, string Platform,
    string CudaArchitecture, string GpuFamily, IReadOnlyList<string> GpuModels, bool NativeNvfp4,
    string Channel, string Qualification, string FileName, long SizeBytes, string Sha256, string Url);
public sealed record ReleaseEngineManifest(int SchemaVersion, string Product, string ManagerVersion,
    bool Approved, IReadOnlyList<ReleaseEnginePackage> Packages);

/// <summary>The Manager release, rather than a mutable latest-engine feed, owns its exact engine pins.</summary>
public static class ReleaseEnginePolicy
{
    public static ReleaseEngineManifest Read(string json)
    {
        if (json.Length > 2 * 1024 * 1024) throw new InvalidDataException("The release manifest is too large.");
        var value = JsonSerializer.Deserialize<ReleaseEngineManifest>(json, SettingsService.JsonOptions)
            ?? throw new InvalidDataException("The release manifest is missing.");
        if (value.SchemaVersion != 1 || value.Product != "NInferEZ Manager" || value.ManagerVersion != ProductInfo.Version ||
            value.Packages is null || value.Packages.Count > 3)
            throw new InvalidDataException("The release manifest does not match this Manager version.");
        if (!value.Approved && value.Packages.Count != 0)
            throw new InvalidDataException("An unapproved release cannot contain active engine bindings.");
        if (value.Approved && value.Packages.Count == 0)
            throw new InvalidDataException("An approved release must pin at least one tested engine package.");
        foreach (var package in value.Packages)
        {
            if(package is null || string.IsNullOrEmpty(package.EngineVersion) || !ModelVersionPolicy.IsValidMinimum(package.EngineVersion))throw new InvalidDataException("Release engine pins must use stable numeric versions.");
            EnginePackageService.ValidateReleasePackage(package);
        }
        if (value.Packages.Select(p => p.CudaArchitecture).Distinct(StringComparer.Ordinal).Count() != value.Packages.Count)
            throw new InvalidDataException("A release must pin exactly one package per supported GPU architecture.");
        return value;
    }
    public static ReleaseEngineManifest Embedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NInferManager.Backend.Assets.release-engine.json")
            ?? throw new InvalidDataException("The release engine binding is missing.");
        using var reader = new StreamReader(stream);
        return Read(reader.ReadToEnd());
    }
    public static ReleaseEnginePackage Select(ReleaseEngineManifest manifest, string? architecture)
    {
        if (!manifest.Approved) throw new InvalidOperationException("This build is awaiting release approval.");
        return manifest.Packages.SingleOrDefault(p => p.CudaArchitecture == architecture)
            ?? throw new InvalidOperationException("This NInferEZ release does not contain a tested package for the detected GPU. The previous installation was preserved.");
    }
    public static void RequirePublishable(string json)
    {
        if (!Read(json).Approved) throw new InvalidDataException("Release blocked: connect approved, tested engine packages before publishing Manager.");
    }
    public static void RequireSelectedPin(string version, string architecture)
    {
        var manifest = Embedded();
        if (!manifest.Approved) throw new InvalidOperationException("This local Manager is awaiting its approved engine binding.");
        if (!manifest.Packages.Any(p => p.EngineVersion == version && p.CudaArchitecture == architecture))
            throw new InvalidOperationException("This engine is not part of the tested Manager release. Check for a NInferEZ Manager update.");
    }
}

/// <summary>Single product setup after an update or a clean installation. Never activates an unverified pin.</summary>
public sealed class ProductUpdateCoordinator
{
    private readonly EnginePackageService _packages;
    private readonly ManagerLog _log;
    private readonly object _sync = new();
    private Task? _operation;
    private readonly ReleaseEngineManifest _manifest;
    private readonly Func<Task<string?>> _architecture;
    private volatile ProductUpdateState _state;
    public ProductUpdateState State => _state.Running
        ? _state with { Completed = _packages.Current.Completed, Total = _packages.Current.Total }
        : _state;
    public ProductUpdateCoordinator(EnginePackageService packages, ManagerLog log,
        ReleaseEngineManifest? manifest = null, Func<Task<string?>>? architecture = null)
    {
        _packages = packages; _log = log;
        _manifest = manifest is null ? ReleaseEnginePolicy.Embedded() : ReleaseEnginePolicy.Read(JsonSerializer.Serialize(manifest,SettingsService.JsonOptions));
        _architecture = architecture ?? (async () => (await _packages.SnapshotAsync()).RecommendedArchitecture);
        _state = new(_manifest.Approved ? "Pending" : "Awaiting approval", false, !_manifest.Approved,
            _manifest.Approved ? "Completing the NInferEZ update." : "This local build is waiting for the tested engine to be approved and connected.");
    }
    public Task WaitForCompletionAsync()
    {
        lock(_sync) return _operation ?? Task.CompletedTask;
    }
    public void Start()
    {
        lock (_sync)
        {
            if (!_manifest.Approved || _operation is { IsCompleted: false }) return;
            _state = new("Updating", true, false, "Completing the NInferEZ update.");
            _operation = Task.Run(CompleteAsync);
        }
    }
    public string? LoadBlocker() => !_manifest.Approved || _state.Ready ? null :
        _state.Error ?? "The NInferEZ update is still completing. Wait for it to finish before loading a model.";
    private async Task CompleteAsync()
    {
        try
        {
            var pin = ReleaseEnginePolicy.Select(_manifest, await _architecture());
            await _packages.InstallReleasePackageAsync(pin, CancellationToken.None);
            _state = new("Ready", false, true, "NInferEZ is up to date.");
        }
        catch (Exception error)
        {
            _log.Write("NInferEZ update completion failed; the previous engine remains selected", error);
            _state = new("Failed", false, false, "The NInferEZ update could not finish. Retry the update; your models and previous engine are preserved.", Error: error.Message);
        }
    }
}
