using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NInferManager.Contracts;

namespace NInferManager.WinUI;

public sealed class BackendClient : IDisposable
{
    private readonly HttpClient _http=new(new SocketsHttpHandler{UseProxy=false}){BaseAddress=ControlBaseUri(),Timeout=TimeSpan.FromSeconds(15)};
    private readonly JsonSerializerOptions _json=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=true,Converters={new JsonStringEnumConverter()}};
    private Process? _ownedProcess;
    private static Uri ControlBaseUri()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("NINFEREZ_MANAGER_CONTROL_PORT"), out var configured)
            && configured is >= 1024 and <= 65535 ? configured : 48973;
        return new Uri($"http://127.0.0.1:{port}");
    }
    public async Task EnsureStartedAsync()
    {
        try { if((await _http.GetAsync("/control/v1/health")).IsSuccessStatusCode)return; } catch { }
        // In a single-file build AppContext.BaseDirectory can point at the
        // extraction cache. Environment.ProcessPath remains the real app path.
        var appDirectory=Path.GetDirectoryName(Environment.ProcessPath)
            ?? throw new InvalidOperationException("The application directory could not be resolved.");
        var exe=Path.Combine(appDirectory,"Backend","NInferManager.Backend.exe");
        if(!File.Exists(exe))throw new FileNotFoundException("The NInferEZ Manager backend is missing.",exe);
        _ownedProcess=Process.Start(new ProcessStartInfo(exe){WorkingDirectory=Path.GetDirectoryName(exe)!,UseShellExecute=false,CreateNoWindow=true});
        // A self-contained single-file backend can take longer on its first launch
        // while Windows extracts and scans the bundled runtime.
        var deadline=DateTime.UtcNow.AddSeconds(60);
        while(DateTime.UtcNow<deadline)
        {
            await Task.Delay(250);
            if(_ownedProcess?.HasExited==true)
                throw new InvalidOperationException($"The backend exited during startup with code {_ownedProcess.ExitCode}.");
            try{if((await _http.GetAsync("/control/v1/health")).IsSuccessStatusCode)return;}catch{}
        }
        throw new InvalidOperationException("The backend did not become ready within 60 seconds.");
    }
    public Task<ManagerStatus?> StatusAsync()=>_http.GetFromJsonAsync<ManagerStatus>("/control/v1/status",_json);
    public async Task<List<ModelInfo>> ModelsAsync()=>await _http.GetFromJsonAsync<List<ModelInfo>>("/control/v1/models",_json)??[];
    public Task<ManagerSettings?> SettingsAsync()=>_http.GetFromJsonAsync<ManagerSettings>("/control/v1/settings",_json);
    public async Task SaveSettingsAsync(ManagerSettings value){using var r=await _http.PutAsJsonAsync("/control/v1/settings",value,_json);await CheckAsync(r);}
    public async Task CommandAsync(string command){using var r=await _http.PostAsync("/control/v1/engine/"+command,null);await CheckAsync(r);}
    public Task<EngineOperationStatus?> EngineOperationAsync()=>_http.GetFromJsonAsync<EngineOperationStatus>("/control/v1/engine/operation",_json);
    public async Task CancelEngineOperationAsync(){using var r=await _http.PostAsync("/control/v1/engine/operation/cancel",null);await CheckAsync(r);}
    public Task<EngineLibrarySnapshot?> EnginesAsync()=>_http.GetFromJsonAsync<EngineLibrarySnapshot>("/control/v1/engines",_json);
    public async Task RefreshEnginesAsync(){using var r=await _http.PostAsync("/control/v1/engines/refresh",null);await CheckAsync(r);}
    public async Task InstallEngineAsync(string version,string architecture){using var r=await _http.PostAsync($"/control/v1/engines/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(architecture)}/install",null);await CheckAsync(r);}
    public async Task ActivateEngineAsync(string version,string architecture){using var r=await _http.PostAsync($"/control/v1/engines/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(architecture)}/activate",null);await CheckAsync(r);}
    public Task<EnginePackageProgress?> EnginePackageProgressAsync()=>_http.GetFromJsonAsync<EnginePackageProgress>("/control/v1/engines/progress",_json);
    public async Task CancelEngineInstallAsync(){using var r=await _http.PostAsync("/control/v1/engines/install/cancel",null);await CheckAsync(r);}
    public async Task ActivateAsync(string file){using var r=await _http.PostAsync("/control/v1/models/"+Uri.EscapeDataString(file)+"/activate",null);await CheckAsync(r);}
    public async Task DownloadAsync(string file){using var r=await _http.PostAsync("/control/v1/models/"+Uri.EscapeDataString(file)+"/download",null);await CheckAsync(r);}
    public Task<DownloadState?> DownloadStatusAsync()=>_http.GetFromJsonAsync<DownloadState>("/control/v1/download",_json);
    public async Task CancelDownloadAsync(){using var r=await _http.PostAsync("/control/v1/download/cancel",null);await CheckAsync(r);}
    public async Task<ApiResult?> VerifyAsync(string file){using var r=await _http.PostAsync("/control/v1/models/"+Uri.EscapeDataString(file)+"/verify",null);await CheckAsync(r);return await r.Content.ReadFromJsonAsync<ApiResult>(_json);}
    public async Task ImportAsync(string file,string path){using var r=await _http.PostAsJsonAsync("/control/v1/models/"+Uri.EscapeDataString(file)+"/import",new{path},_json);await CheckAsync(r);}
    public async Task LinkModelAsync(string path){using var r=await _http.PostAsJsonAsync("/control/v1/models/link",new{path},_json);await CheckAsync(r);}
    public async Task DeleteAsync(string file){using var r=await _http.DeleteAsync("/control/v1/models/"+Uri.EscapeDataString(file));await CheckAsync(r);}
    public async Task RefreshCatalogAsync(){using var r=await _http.PostAsync("/control/v1/catalog/refresh",null);await CheckAsync(r);}
    public Task<RequestMetricsSnapshot?> RequestsAsync()=>_http.GetFromJsonAsync<RequestMetricsSnapshot>("/control/v1/requests",_json);
    public Task<string> LogsAsync()=>_http.GetStringAsync("/control/v1/logs");
    public Task<UpdateInfo?> CheckUpdateAsync()=>_http.GetFromJsonAsync<UpdateInfo>("/control/v1/update",_json);
    public async Task DownloadUpdateAsync(UpdateInfo update){using var r=await _http.PostAsJsonAsync("/control/v1/update/download",update,_json);await CheckAsync(r);}
    public Task<UpdateProgress?> UpdateProgressAsync()=>_http.GetFromJsonAsync<UpdateProgress>("/control/v1/update/progress",_json);
    public async Task ApplyUpdateAsync(){using var r=await _http.PostAsJsonAsync("/control/v1/update/apply",new{uiProcessId=Environment.ProcessId},_json);await CheckAsync(r);}
    public async Task<ApiResult?> RestartPortAsync(int port,bool locked,bool save){using var r=await _http.PostAsJsonAsync("/control/v1/port/restart",new{port,locked,save},_json);await CheckAsync(r);return await r.Content.ReadFromJsonAsync<ApiResult>(_json);}
    public async Task<string> CommandPreviewAsync(){using var r=await _http.GetAsync("/control/v1/command");await CheckAsync(r);return await r.Content.ReadAsStringAsync();}
    public async Task<ApiResult?> DiagnosticsAsync(){using var r=await _http.PostAsync("/control/v1/diagnostics",null);await CheckAsync(r);return await r.Content.ReadFromJsonAsync<ApiResult>(_json);}
    public async Task OpenAsync(string target){using var r=await _http.PostAsync("/control/v1/open?target="+Uri.EscapeDataString(target),null);await CheckAsync(r);}
    public async Task TrimAsync(){using var r=await _http.PostAsync("/control/v1/trim",null);await CheckAsync(r);}
    private async Task CheckAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.TryGetProperty("message", out var message) && !string.IsNullOrWhiteSpace(message.GetString()))
                throw new InvalidOperationException(message.GetString());
            if (root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out message) && !string.IsNullOrWhiteSpace(message.GetString()))
                throw new InvalidOperationException(message.GetString());
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"Request failed (HTTP {(int)response.StatusCode}). Check the manager log for details.");
    }
    public void Dispose(){_http.Dispose();try{if(_ownedProcess is {HasExited:false})_ownedProcess.Kill(true);}catch{}finally{_ownedProcess?.Dispose();}}
}
