using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public sealed class EnginePackageService : IDisposable
{
    private const long MaximumArchiveBytes = 8L * 1024 * 1024 * 1024;
    private const long MaximumExpandedBytes = 16L * 1024 * 1024 * 1024;
    private readonly AppPaths _paths;
    private readonly ManagerLog _log;
    private readonly SettingsService _settings;
    private readonly HttpClient _client = new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private EngineChannel _channel = BuiltInChannel();
    private CancellationTokenSource? _downloadCancellation;
    public EnginePackageProgress Current { get; private set; } = new("Idle", null, 0, 0, false);

    public EnginePackageService(AppPaths paths, ManagerLog log, SettingsService settings)
    {
        _paths = paths;
        _log = log;
        _settings = settings;
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"NInferEZ-Manager/{ProductInfo.Version}");
        LoadCachedChannel();
    }

    public string ActiveDirectory
    {
        get
        {
            var pointer = ReadPointer();
            if (pointer is not null)
            {
                var root = Path.GetFullPath(Path.Combine(_paths.EnginesDirectory, pointer.RelativePath));
                if (IsUnder(root, _paths.EnginesDirectory) && File.Exists(Path.Combine(root, "ninfer-serve.exe"))) return root;
            }
            return _paths.EngineDirectory;
        }
    }

    public async Task<EngineLibrarySnapshot> SnapshotAsync(CancellationToken token = default)
    {
        var gpu = await GpuMonitor.ReadAsync();
        var recommended = RecommendArchitecture(gpu?.Name);
        var pointer = ReadPointer();
        var packages = _channel.Packages
            .Where(IsSupportedPackage)
            .Select(p => new EnginePackageInfo(p.EngineVersion, p.CudaArchitecture, p.GpuFamily, p.GpuModels, p.Channel,
                p.Qualification, p.NativeNvfp4, p.FileName, p.SizeBytes, p.Sha256, p.Url,
                IsInstalled(p), pointer is not null && pointer.EngineVersion.Equals(p.EngineVersion, StringComparison.OrdinalIgnoreCase)
                    && pointer.CudaArchitecture.Equals(p.CudaArchitecture, StringComparison.OrdinalIgnoreCase),
                p.CudaArchitecture.Equals(recommended, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(p => p.Recommended).ThenBy(p => p.CudaArchitecture, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(gpu?.Name, recommended, pointer?.CudaArchitecture ?? ReadBundledArchitecture(), pointer?.EngineVersion ?? ReadBundledVersion(), packages, _channel.UpdatedAt);
    }

    public async Task RefreshAsync(CancellationToken token = default)
    {
        using var response = await _client.GetAsync(ProductInfo.EngineChannelUrl, token);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(token);
        var channel = ParseAndValidateChannel(json);
        var temporary = _paths.EngineCatalogCacheFile + ".tmp";
        await File.WriteAllTextAsync(temporary, json, token);
        File.Move(temporary, _paths.EngineCatalogCacheFile, true);
        _channel = channel;
        _settings.Current.LastEngineCheckUtc = DateTimeOffset.UtcNow;
        _settings.Save();
        _log.Write($"Engine channel refreshed with {channel.Packages.Count} compatible package(s).");
    }
    public bool ShouldRefresh() => _settings.Current.AutoCheckEngines && (_settings.Current.LastEngineCheckUtc is null || DateTimeOffset.UtcNow - _settings.Current.LastEngineCheckUtc >= TimeSpan.FromHours(Math.Max(1, _settings.Current.EngineCheckHours)));

    public void StartInstall(string version, string architecture)
    {
        if (_downloadCancellation is not null && !_downloadCancellation.IsCancellationRequested && Current.Running)
            throw new InvalidOperationException("An engine package is already downloading.");
        var package = Find(version, architecture);
        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();
        Current = new("Starting", package.FileName, 0, package.SizeBytes, true);
        _ = InstallAsync(package, _downloadCancellation.Token);
    }

    public void CancelInstall() => _downloadCancellation?.Cancel();

    public void Activate(string version, string architecture)
    {
        var package = Find(version, architecture);
        if (!IsInstalled(package)) throw new InvalidOperationException("Install this engine package before activating it.");
        WritePointer(new CurrentEnginePointer(1, package.EngineVersion, package.CudaArchitecture, RelativeRoot(package)));
        _log.Write($"Activated NInferEZ Engine {package.EngineVersion} ({package.CudaArchitecture}).");
    }

    private async Task InstallAsync(EnginePackage package, CancellationToken token)
    {
        await _installGate.WaitAsync(token);
        var downloads = Path.Combine(_paths.DataDirectory, "cache", "engine-downloads");
        Directory.CreateDirectory(downloads);
        var partial = Path.Combine(downloads, package.FileName + ".partial");
        try
        {
            using var response = await _client.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var reported = response.Content.Headers.ContentLength;
            if (reported is > MaximumArchiveBytes) throw new InvalidDataException("The engine package exceeds the download safety limit.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, true))
            {
                var buffer = new byte[1024 * 1024]; long completed = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, token); if (read == 0) break;
                    completed += read; if (completed > MaximumArchiveBytes) throw new InvalidDataException("The engine package exceeded the download safety limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    Current = new("Downloading", package.FileName, completed, package.SizeBytes, true);
                }
            }
            if (new FileInfo(partial).Length != package.SizeBytes) throw new InvalidDataException("The engine download is incomplete.");
            Current = new("Verifying SHA-256", package.FileName, package.SizeBytes, package.SizeBytes, true);
            await using (var stream = File.OpenRead(partial))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The engine package failed SHA-256 verification.");
            }
            Current = new("Installing", package.FileName, package.SizeBytes, package.SizeBytes, true);
            InstallArchive(partial, package);
            Activate(package.EngineVersion, package.CudaArchitecture);
            File.Delete(partial);
            Current = new("Ready", package.FileName, package.SizeBytes, package.SizeBytes, false);
        }
        catch (OperationCanceledException)
        {
            Current = new("Cancelled", package.FileName, 0, package.SizeBytes, false);
        }
        catch (Exception ex)
        {
            _log.Write("Engine package installation failed", ex);
            Current = new("Failed", package.FileName, 0, package.SizeBytes, false, ex.Message);
        }
        finally { _installGate.Release(); }
    }

    private void InstallArchive(string archivePath, EnginePackage package)
    {
        var staging = Path.Combine(_paths.EnginesDirectory, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var rootPrefix = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            long expanded = 0;
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                if (zip.Entries.Count is 0 or > 20000) throw new InvalidDataException("The engine archive entry count is invalid.");
                foreach (var entry in zip.Entries)
                {
                    expanded += entry.Length; if (expanded > MaximumExpandedBytes) throw new InvalidDataException("The expanded engine package exceeds the safety limit.");
                    var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The engine archive contains an unsafe path.");
                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!); entry.ExtractToFile(destination, true);
                }
            }
            var manifests = Directory.EnumerateFiles(staging, "engine-manifest.json", SearchOption.AllDirectories).ToArray();
            if (manifests.Length != 1) throw new InvalidDataException("The engine package must contain exactly one engine manifest.");
            var manifest = JsonSerializer.Deserialize<InstalledManifest>(File.ReadAllText(manifests[0]), JsonOptions()) ?? throw new InvalidDataException("The engine manifest is invalid.");
            if (manifest.Product != "NInferEZ Engine" || manifest.ContractVersion is < ProductInfo.MinimumEngineContract or > ProductInfo.MaximumEngineContract
                || !manifest.EngineVersion.Equals(package.EngineVersion, StringComparison.OrdinalIgnoreCase)
                || !manifest.CudaArchitecture.Equals(package.CudaArchitecture, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The engine manifest does not match the selected package.");
            var source = Path.GetDirectoryName(manifests[0])!;
            if (!File.Exists(Path.Combine(source, "ninfer-serve.exe"))) throw new InvalidDataException("ninfer-serve.exe is missing from the engine package.");
            var destinationRoot = Path.Combine(_paths.EnginesDirectory, RelativeRoot(package));
            var previous = destinationRoot + ".previous";
            Directory.CreateDirectory(Path.GetDirectoryName(destinationRoot)!);
            if (Directory.Exists(previous)) Directory.Delete(previous, true);
            if (Directory.Exists(destinationRoot)) Directory.Move(destinationRoot, previous);
            try { Directory.Move(source, destinationRoot); }
            catch { if (Directory.Exists(destinationRoot)) Directory.Delete(destinationRoot, true); if (Directory.Exists(previous)) Directory.Move(previous, destinationRoot); throw; }
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private void LoadCachedChannel()
    {
        try { if (File.Exists(_paths.EngineCatalogCacheFile)) _channel = ParseAndValidateChannel(File.ReadAllText(_paths.EngineCatalogCacheFile)); }
        catch (Exception ex) { _log.Write("Cached engine channel was rejected; using the built-in snapshot", ex); }
    }
    private static EngineChannel ParseAndValidateChannel(string json)
    {
        var channel = JsonSerializer.Deserialize<EngineChannel>(json, JsonOptions()) ?? throw new InvalidDataException("The engine channel is empty.");
        if (channel.SchemaVersion != 1 || channel.Product != "NInferEZ Engine") throw new InvalidDataException("The engine channel schema or product is unsupported.");
        foreach (var package in channel.Packages) Validate(package);
        return channel with { Packages = channel.Packages.Where(IsSupportedPackage).ToArray() };
    }
    private static void Validate(EnginePackage p)
    {
        if (p.ContractVersion is < ProductInfo.MinimumEngineContract or > ProductInfo.MaximumEngineContract || p.Platform != "windows-x64"
            || p.SizeBytes is <= 0 or > MaximumArchiveBytes || p.Sha256.Length != 64 || !p.Sha256.All(Uri.IsHexDigit)
            || Path.GetFileName(p.FileName) != p.FileName || !Uri.TryCreate(p.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/BenGamliel/NInferEZ-Engine/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("The engine channel contains an unsafe or unsupported package.");
    }
    private static bool IsSupportedPackage(EnginePackage p) => p.Platform == "windows-x64" && p.ContractVersion is >= ProductInfo.MinimumEngineContract and <= ProductInfo.MaximumEngineContract;
    private EnginePackage Find(string version, string architecture) => _channel.Packages.FirstOrDefault(p => p.EngineVersion.Equals(version, StringComparison.OrdinalIgnoreCase) && p.CudaArchitecture.Equals(architecture, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("The selected engine package is not in the verified channel.");
    private bool IsInstalled(EnginePackage p) => File.Exists(Path.Combine(_paths.EnginesDirectory, RelativeRoot(p), "ninfer-serve.exe"));
    private static string RelativeRoot(EnginePackage p) => Path.Combine(p.EngineVersion, p.CudaArchitecture);
    private CurrentEnginePointer? ReadPointer() { try { return File.Exists(_paths.CurrentEngineFile) ? JsonSerializer.Deserialize<CurrentEnginePointer>(File.ReadAllText(_paths.CurrentEngineFile), JsonOptions()) : null; } catch { return null; } }
    private void WritePointer(CurrentEnginePointer pointer) { var temp = _paths.CurrentEngineFile + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(pointer, JsonOptions())); File.Move(temp, _paths.CurrentEngineFile, true); }
    private string? ReadBundledArchitecture() => ReadBundledManifest()?.CudaArchitecture;
    private string? ReadBundledVersion() => ReadBundledManifest()?.EngineVersion;
    private InstalledManifest? ReadBundledManifest() { try { var path = Path.Combine(_paths.EngineDirectory, "engine-manifest.json"); return File.Exists(path) ? JsonSerializer.Deserialize<InstalledManifest>(File.ReadAllText(path), JsonOptions()) : null; } catch { return null; } }
    public static string? RecommendArchitecture(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return null;
        if (gpuName.Contains("RTX 50", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Blackwell", StringComparison.OrdinalIgnoreCase)) return "sm120a";
        if (gpuName.Contains("RTX 40", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Ada", StringComparison.OrdinalIgnoreCase)) return "sm89";
        if (gpuName.Contains("RTX 30", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Ampere", StringComparison.OrdinalIgnoreCase)) return "sm86";
        return null;
    }
    private static bool IsUnder(string path, string root) => path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static EngineChannel BuiltInChannel() => new(1, "NInferEZ Engine", DateTimeOffset.Parse("2026-09-28T00:00:00Z"),
    [
        new("0.1.0-preview.1",1,"windows-x64","sm86","NVIDIA Ampere / RTX 3000 Series",["NVIDIA GeForce RTX 3090","NVIDIA GeForce RTX 3090 Ti"],false,"preview","build-verified-preview","NInferEZ-Engine-0.1.0-preview.1-sm86-windows-x64.zip",1335702326,"0c070c048a57a652294e4ec2d2a98590de368b1ebe331870e425ff483afaf7f3","https://github.com/BenGamliel/NInferEZ-Engine/releases/download/v0.1.0-preview.1/NInferEZ-Engine-0.1.0-preview.1-sm86-windows-x64.zip"),
        new("0.1.0-preview.1",1,"windows-x64","sm89","NVIDIA Ada / RTX 4000 Series",["NVIDIA GeForce RTX 4090"],false,"preview","build-verified-preview","NInferEZ-Engine-0.1.0-preview.1-sm89-windows-x64.zip",1327225574,"1c7f0ff4198d5d5987dad3569c6a3aed7c3e0cb08e684e363cae9e823fee7a3d","https://github.com/BenGamliel/NInferEZ-Engine/releases/download/v0.1.0-preview.1/NInferEZ-Engine-0.1.0-preview.1-sm89-windows-x64.zip"),
        new("0.1.0-preview.1",1,"windows-x64","sm120a","NVIDIA Blackwell / RTX 5000 Series",["NVIDIA GeForce RTX 5090","NVIDIA RTX PRO 6000 Blackwell"],true,"preview","build-verified-preview","NInferEZ-Engine-0.1.0-preview.1-sm120a-windows-x64.zip",1112591446,"ce1081bbd6ef30828a28e26eec8f4e659c19a21696b17e53bb840fe3377e3306","https://github.com/BenGamliel/NInferEZ-Engine/releases/download/v0.1.0-preview.1/NInferEZ-Engine-0.1.0-preview.1-sm120a-windows-x64.zip")
    ]);
    public void Dispose() { _downloadCancellation?.Dispose(); _installGate.Dispose(); _client.Dispose(); }

    private sealed record EngineChannel([property: JsonPropertyName("schemaVersion")] int SchemaVersion, [property: JsonPropertyName("product")] string Product, [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt, [property: JsonPropertyName("packages")] IReadOnlyList<EnginePackage> Packages);
    private sealed record EnginePackage([property: JsonPropertyName("engineVersion")] string EngineVersion, [property: JsonPropertyName("contractVersion")] int ContractVersion, [property: JsonPropertyName("platform")] string Platform, [property: JsonPropertyName("cudaArchitecture")] string CudaArchitecture, [property: JsonPropertyName("gpuFamily")] string GpuFamily, [property: JsonPropertyName("gpuModels")] IReadOnlyList<string> GpuModels, [property: JsonPropertyName("nativeNvfp4")] bool NativeNvfp4, [property: JsonPropertyName("channel")] string Channel, [property: JsonPropertyName("qualification")] string Qualification, [property: JsonPropertyName("fileName")] string FileName, [property: JsonPropertyName("sizeBytes")] long SizeBytes, [property: JsonPropertyName("sha256")] string Sha256, [property: JsonPropertyName("url")] string Url);
    private sealed record CurrentEnginePointer(int SchemaVersion, string EngineVersion, string CudaArchitecture, string RelativePath);
    private sealed record InstalledManifest(string Product, int ContractVersion, string EngineVersion, string CudaArchitecture);
}
