using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NInferManager.Contracts;

namespace NInferManager.WinUI;

public sealed class BackendClient : IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _ownedProcess;
    public string? SettingsRecoveryMessage{get;private set;}

    public async Task EnsureStartedAsync()
    {
        var appDirectory = Path.GetDirectoryName(Environment.ProcessPath)
            ?? throw new InvalidOperationException("The application directory could not be resolved.");
        var exe = Path.Combine(appDirectory, "Backend", "NInferManager.Backend.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("The application backend is missing. Repair or extract the complete application package.", exe);

        // Each UI owns a private authenticated backend. A server already on a port
        // is never accepted as ours merely because it responds successfully.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        _http.BaseAddress = new Uri($"http://127.0.0.1:{port}");
        _http.DefaultRequestHeaders.Add("X-NInferEZ-Control-Token", token);
        var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["NINFEREZ_MANAGER_CONTROL_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["NINFEREZ_MANAGER_CONTROL_TOKEN"] = token;
        start.Environment["NINFEREZ_MANAGER_PARENT_PID"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _ownedProcess = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start the application backend.");
        var elapsed = Stopwatch.StartNew();
        try
        {
            while (elapsed.Elapsed < TimeSpan.FromSeconds(60))
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                if (_ownedProcess.HasExited) throw new InvalidOperationException($"The backend exited during startup (code {_ownedProcess.ExitCode}). Check Data/Logs for details.");
                try
                {
                    using var probe = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    probe.CancelAfter(TimeSpan.FromSeconds(1));
                    using var response = await _http.GetAsync("/control/v1/health", probe.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(probe.Token));
                        var root = json.RootElement;
                        if (root.TryGetProperty("product", out var product) && product.GetString() == "NInferEZ Manager"
                            && root.TryGetProperty("instanceId", out var instance) && !string.IsNullOrWhiteSpace(instance.GetString())
                            && root.TryGetProperty("processId",out var pid)&&pid.GetInt32()==_ownedProcess.Id)
                        {if(root.TryGetProperty("settingsRecovery",out var recovery)&&recovery.ValueKind==JsonValueKind.String)SettingsRecoveryMessage=recovery.GetString();return;}
                        throw new InvalidOperationException("The backend identity could not be verified. Close the application and retry with the complete package.");
                    }
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested) { }
                await Task.Delay(250, _lifetime.Token);
            }
            throw new InvalidOperationException("The application backend did not become ready within one minute. No model has been loaded; check Data/Logs and Windows security notifications.");
        }
        catch { StopOwnedProcess(); throw; }
    }

    private async Task<T?> GetAsync<T>(string route)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await _http.GetAsync(route, deadline.Token);
        await CheckAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(_json, deadline.Token);
    }
    private async Task<ApiResult?> SendAsync(HttpMethod method, string route, object? value = null)
    {
        using var request = new HttpRequestMessage(method, route);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);deadline.CancelAfter(route.EndsWith("/verify",StringComparison.Ordinal)?TimeSpan.FromMinutes(10):TimeSpan.FromSeconds(60));
        if (value is not null) request.Content = JsonContent.Create(value, options: _json);
        using var response = await _http.SendAsync(request, deadline.Token);
        await CheckAsync(response);
        if (response.Content.Headers.ContentLength == 0) return null;
        var body = await response.Content.ReadAsStringAsync(_lifetime.Token);
        return string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<ApiResult>(body, _json);
    }

    public Task<ManagerStatus?> StatusAsync() => GetAsync<ManagerStatus>("/control/v1/status");
    public async Task<ConversionCapabilities?> ConversionCapabilitiesAsync()
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(100));
        using var response=await _http.GetAsync("/control/v1/converter/capabilities",deadline.Token);
        await CheckAsync(response);
        return await response.Content.ReadFromJsonAsync<ConversionCapabilities>(_json,deadline.Token);
    }
    public Task<ConversionStatus?> ConversionStatusAsync()=>GetAsync<ConversionStatus>("/control/v1/converter/status");
    public async Task StartConversionAsync(string command,ConversionRequest value)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var response=await _http.PostAsJsonAsync("/control/v1/converter/"+command,value,_json,deadline.Token);
        await CheckAsync(response);
    }
    public async Task CancelConversionAsync()=>await SendAsync(HttpMethod.Post,"/control/v1/converter/cancel");
    public async Task<List<ModelInfo>> ModelsAsync() => await GetAsync<List<ModelInfo>>("/control/v1/models") ?? [];
    public Task<ManagerSettings?> SettingsAsync() => GetAsync<ManagerSettings>("/control/v1/settings");
    public async Task<ManagerSettings?> PatchSettingsAsync(long revision, object changes)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await _http.PatchAsJsonAsync("/control/v1/settings", new { expectedRevision = revision, changes }, _json, deadline.Token);
        await CheckAsync(response);
        return await SettingsAsync();
    }
    public async Task CommandAsync(string command) => await SendAsync(HttpMethod.Post, "/control/v1/engine/" + command);
    public Task<EngineOperationStatus?> EngineOperationAsync() => GetAsync<EngineOperationStatus>("/control/v1/engine/operation");
    public async Task CancelEngineOperationAsync() => await SendAsync(HttpMethod.Post, "/control/v1/engine/operation/cancel");
    public Task<EngineLibrarySnapshot?> EnginesAsync() => GetAsync<EngineLibrarySnapshot>("/control/v1/engines");
    public async Task RefreshEnginesAsync() => await SendAsync(HttpMethod.Post, "/control/v1/engines/refresh");
    public async Task InstallEngineAsync(string version, string architecture) => await SendAsync(HttpMethod.Post, $"/control/v1/engines/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(architecture)}/install");
    public async Task ActivateEngineAsync(string version, string architecture) => await SendAsync(HttpMethod.Post, $"/control/v1/engines/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(architecture)}/activate");
    public Task<EnginePackageProgress?> EnginePackageProgressAsync() => GetAsync<EnginePackageProgress>("/control/v1/engines/progress");
    public async Task CancelEngineInstallAsync() => await SendAsync(HttpMethod.Post, "/control/v1/engines/install/cancel");
    public async Task ActivateAsync(string file) => await SendAsync(HttpMethod.Post, "/control/v1/models/" + Uri.EscapeDataString(file) + "/activate");
    public async Task DownloadAsync(string file) => await SendAsync(HttpMethod.Post, "/control/v1/models/" + Uri.EscapeDataString(file) + "/download");
    public Task<DownloadState?> DownloadStatusAsync() => GetAsync<DownloadState>("/control/v1/download");
    public async Task CancelDownloadAsync() => await SendAsync(HttpMethod.Post, "/control/v1/download/cancel");
    public Task<ApiResult?> VerifyAsync(string file) => SendAsync(HttpMethod.Post, "/control/v1/models/" + Uri.EscapeDataString(file) + "/verify");
    public async Task LinkModelAsync(string path) => await SendAsync(HttpMethod.Post, "/control/v1/models/link", new { path });
    public async Task RelinkModelAsync(string file,string path)=>await SendAsync(HttpMethod.Post,"/control/v1/models/"+Uri.EscapeDataString(file)+"/relink",new{path});
    public async Task MarkModelSeenAsync(string file)=>await SendAsync(HttpMethod.Post,"/control/v1/models/"+Uri.EscapeDataString(file)+"/seen",new{});
    public Task<ApiResult?> ImportPresetAsync(string file,string path)=>SendAsync(HttpMethod.Post,"/control/v1/models/"+Uri.EscapeDataString(file)+"/profile/import",new{path});
    public Task<string> ExportPresetAsync(string file)=>GetTextAsync("/control/v1/models/"+Uri.EscapeDataString(file)+"/profile/export");
    public async Task SetApiEnabledAsync(bool enabled)=>await SendAsync(HttpMethod.Post,"/control/v1/api/state",new{enabled});
    public Task<ApiResult?> SetModelAliasAsync(string file, string? alias) => SendAsync(HttpMethod.Put, "/control/v1/models/" + Uri.EscapeDataString(file) + "/alias", new { alias });
    public async Task DeleteAsync(string file) => await SendAsync(HttpMethod.Delete, "/control/v1/models/" + Uri.EscapeDataString(file));
    public async Task RefreshCatalogAsync() => await SendAsync(HttpMethod.Post, "/control/v1/catalog/refresh");
    public Task<CatalogNotificationSnapshot?> ModelNotificationsAsync()=>GetAsync<CatalogNotificationSnapshot>("/control/v1/catalog/notifications");
    public async Task DismissModelNotificationsAsync(IEnumerable<string> identities)=>await SendAsync(HttpMethod.Post,"/control/v1/catalog/notifications/dismiss",new DismissModelAnnouncementsRequest(identities.ToArray()));
    public Task<RequestMetricsSnapshot?> RequestsAsync() => GetAsync<RequestMetricsSnapshot>("/control/v1/requests");
    private async Task<string> GetTextAsync(string route){using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);deadline.CancelAfter(TimeSpan.FromSeconds(15));using var response=await _http.GetAsync(route,deadline.Token);await CheckAsync(response);return await response.Content.ReadAsStringAsync(deadline.Token);}
    public Task<string> LogsAsync()=>GetTextAsync("/control/v1/logs");
    public Task<UpdateInfo?> CheckUpdateAsync() => GetAsync<UpdateInfo>("/control/v1/update");
    public async Task DownloadUpdateAsync(UpdateInfo update) => await SendAsync(HttpMethod.Post, "/control/v1/update/download", update);
    public Task<UpdateProgress?> UpdateProgressAsync() => GetAsync<UpdateProgress>("/control/v1/update/progress");
    public async Task ApplyUpdateAsync() => await SendAsync(HttpMethod.Post, "/control/v1/update/apply", new { uiProcessId = Environment.ProcessId });
    public void ReleaseOwnershipForUpdate(){_ownedProcess?.Dispose();_ownedProcess=null;}
    public async Task CancelUpdateAsync()=>await SendAsync(HttpMethod.Post,"/control/v1/update/cancel");
    public Task<ApiResult?> RestartPortAsync(int port, bool locked, bool allowLocalNetwork, bool save, string? apiKey = null, bool? corsEnabled = null, long? expectedRevision = null) => SendAsync(HttpMethod.Post, "/control/v1/port/restart", new { port, locked, allowLocalNetwork, save, apiKey, corsEnabled, expectedRevision });
    public Task<string> CommandPreviewAsync()=>GetTextAsync("/control/v1/command");
    public Task<ApiResult?> DiagnosticsAsync() => SendAsync(HttpMethod.Post, "/control/v1/diagnostics");
    public async Task OpenAsync(string target) => await SendAsync(HttpMethod.Post, "/control/v1/open?target=" + Uri.EscapeDataString(target));

    private async Task CheckAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(_lifetime.Token);
        string? detail = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (json.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String) detail = message.GetString();
                else if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out message)) detail = message.GetString();
            }
        }
        catch (JsonException) { }
        throw new InvalidOperationException(detail ?? ((int)response.StatusCode == 409 ? "These settings changed in another action. Reload the page and apply your change again; nothing was overwritten." : $"Request failed (HTTP {(int)response.StatusCode}). Open Logs for details."));
    }

    private void StopOwnedProcess()
    {
        try { if (_ownedProcess is { HasExited: false }) { _ownedProcess.Kill(true); _ownedProcess.WaitForExit(3000); } }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { _ownedProcess?.Dispose(); _ownedProcess = null; }
    }
    public void Dispose() { _lifetime.Cancel(); _http.Dispose(); StopOwnedProcess(); _lifetime.Dispose(); }
}
