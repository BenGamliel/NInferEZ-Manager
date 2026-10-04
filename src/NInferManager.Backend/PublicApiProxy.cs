using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NInferManager.Backend;

public sealed class PublicApiProxy : IAsyncDisposable
{
    private static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase) { "Connection","Keep-Alive","Proxy-Authenticate","Proxy-Authorization","TE","Trailer","Transfer-Encoding","Upgrade" };
    private readonly EngineController _engine;
    private readonly SettingsService _settings;
    private readonly ModelCatalog _catalog;
    private readonly ManagerLog _log;
    private readonly HttpClient _client = new(new SocketsHttpHandler { UseProxy=false, MaxConnectionsPerServer=100 }) { Timeout=Timeout.InfiniteTimeSpan };
    private WebApplication? _app;
    private Timer? _timer;
    private long _lastActivity = DateTime.UtcNow.Ticks;
    private int _active;
    private int _pending;
    private int _idleCheck;
    private readonly SemaphoreSlim _listenerGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    public PublicApiProxy(EngineController engine, SettingsService settings, ModelCatalog catalog, ManagerLog log) { _engine=engine; _settings=settings; _catalog=catalog; _log=log; }
    public int Port { get; private set; }
    public bool ChangedAutomatically { get; private set; }
    public bool PortConflict { get; private set; }
    public bool LocalNetworkEnabled { get; private set; }
    public int ActiveRequests => Volatile.Read(ref _active);
    public bool IsEnabled => _app is not null;
    public async Task SetEnabledAsync(bool enabled)
    {
        await _listenerGate.WaitAsync();
        try
        {
            if (enabled && _app is null) await StartOnPortAsync(_settings.Current.PublicPort,_settings.Current.LockPublicPort,_settings.Current.AllowLocalNetwork);
            else if (!enabled && _app is not null) { await StopListenerAsync(); }
        }
        finally { _listenerGate.Release(); }
    }
    private async Task StopListenerAsync()
    {
        var app=_app;_app=null;
        if(app is null)return;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try{await app.StopAsync(timeout.Token);}catch(OperationCanceledException){}
        await app.DisposeAsync();
    }

    public async Task StartAsync()
    {
        await StartOnPortAsync(_settings.Current.PublicPort,_settings.Current.LockPublicPort,_settings.Current.AllowLocalNetwork,true);
    }
    private async Task StartOnPortAsync(int requested,bool locked,bool allowLocalNetwork,bool startup=false,string? apiKey=null)
    {
        if (allowLocalNetwork && string.IsNullOrWhiteSpace(apiKey??_settings.Current.ApiKey))
            throw new InvalidOperationException("Set an API key before allowing network access.");
        var actual = requested;
        for (var attempt = 0; attempt < 30; attempt++)
        {
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args=[] });
        var listenAddress=allowLocalNetwork?IPAddress.Any:IPAddress.Loopback;
        builder.Logging.ClearProviders(); builder.WebHost.UseKestrel(k=> { k.Limits.MaxRequestBodySize = 1024L * 1024 * 1024; k.Listen(listenAddress,actual); });
        var listenerKey=apiKey??_settings.Current.ApiKey;
        var app=builder.Build(); app.Run(context=>HandleAsync(context,listenerKey));
        try { await app.StartAsync(); _app=app; Port=actual; ChangedAutomatically=actual!=requested; PortConflict=false; break; }
        catch (IOException)
        {
            await app.DisposeAsync();
            if (locked && startup) { Port=requested; PortConflict=true; _log.Write($"Public API port {requested} is unavailable."); return; }
            if (locked || attempt == 29) throw new InvalidOperationException($"Port {requested} could not be opened. Select an available port.");
            actual = PortService.FindAvailable(_settings.Current.BackendPort);
        }
        }
        LocalNetworkEnabled=allowLocalNetwork;
        _timer ??=new Timer(_=>_ = CheckIdleAsync(),null,TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(15));
        _log.Write($"Public OpenAI-compatible API listening on {(allowLocalNetwork?$"all local-network interfaces (0.0.0.0:{Port})":$"localhost (127.0.0.1:{Port})")}");
    }
    public async Task RestartAsync(int requested,bool locked,bool allowLocalNetwork, string? apiKey = null)
    {
        if(requested is < 1024 or > 65535)throw new ArgumentOutOfRangeException(nameof(requested));
        if(requested==_settings.Current.BackendPort)throw new InvalidOperationException("The public and internal ports must be different.");
        if (allowLocalNetwork && string.IsNullOrWhiteSpace(apiKey ?? _settings.Current.ApiKey)) throw new InvalidOperationException("Set an API key before allowing network access.");
        await _listenerGate.WaitAsync();
        var previousPort = Port; var previousScope = LocalNetworkEnabled; var hadListener = _app is not null;
        var previousKey = _settings.Current.ApiKey;
        try
        {
            if(_app is not null)await StopListenerAsync();
            try { await StartOnPortAsync(requested,locked,allowLocalNetwork,apiKey:apiKey); }
            catch
            {
                if (hadListener) await StartOnPortAsync(previousPort,true,previousScope,apiKey:previousKey);
                throw;
            }
        }
        finally { _listenerGate.Release(); }
    }
    private async Task HandleAsync(HttpContext context,string listenerKey)
    {
        if(_settings.Current.CorsEnabled)
        {
            context.Response.Headers.AccessControlAllowOrigin="*";
            context.Response.Headers.AccessControlAllowHeaders="Authorization, Content-Type";
            context.Response.Headers.AccessControlAllowMethods="GET, POST, DELETE, OPTIONS";
        }
        if(context.Request.Method=="OPTIONS"){context.Response.StatusCode=204;return;}
        if (context.Request.Path.Equals("/health") || context.Request.Path.Equals("/manager/health")) { var active=_engine.ActiveModel;await context.Response.WriteAsJsonAsync(new {status="ok",engine=_engine.State.ToString(),model=active is null?null:_settings.ModelIdFor(active)}); return; }
        if (context.Request.Method=="POST" && context.Request.Path.Equals("/manager/unload"))
        {
            if(!Authorized(context,listenerKey)){context.Response.StatusCode=401;return;}
            await _engine.LifecycleGate.WaitAsync(context.RequestAborted);
            try { await _engine.UnloadAsync("public API"); await context.Response.WriteAsJsonAsync(new{status="ok"}); }
            finally { _engine.LifecycleGate.Release(); }
            return;
        }
        if (!context.Request.Path.StartsWithSegments("/v1")) { context.Response.StatusCode=404; return; }
        if (!Authorized(context,listenerKey)) { context.Response.StatusCode=401; await context.Response.WriteAsJsonAsync(new{error=new{message="Invalid or missing API key."}}); return; }
        if (context.Request.Method=="GET" && context.Request.Path.Equals("/v1/models"))
        {
            var models=_catalog.InstalledEntries().Select(x=>new{id=_settings.ModelIdFor(x),@object="model",created=0,owned_by="local",vision=x.Vision}).ToArray();
            await context.Response.WriteAsJsonAsync(new{@object="list",data=models});
            return;
        }
        var isInference = context.Request.Method == "POST" && (context.Request.Path.Equals("/v1/chat/completions") || context.Request.Path.Equals("/v1/completions") || context.Request.Path.Equals("/v1/responses") || context.Request.Path.Equals("/v1/embeddings"));
        var isStoredResponse = context.Request.Path.StartsWithSegments("/v1/responses") && context.Request.Method is "GET" or "DELETE";
        if (!isInference && !isStoredResponse) { context.Response.StatusCode=404; await context.Response.WriteAsJsonAsync(new{error=new{message="This API route is not supported by the selected engine adapter."}}); return; }
        var profile = _engine.ActiveModel is { } selected ? _settings.ProfileFor(selected) : new NInferManager.Contracts.ModelProfile();
        var pending = Interlocked.Increment(ref _pending);
        if (pending > profile.MaxPendingRequests + 1)
        {
            Interlocked.Decrement(ref _pending); context.Response.StatusCode=429; context.Response.Headers.RetryAfter="1";
            await context.Response.WriteAsJsonAsync(new{error=new{message="The inference queue is full. Try again shortly."}}); return;
        }
        var acquired = false;
        try { acquired = await _engine.LifecycleGate.WaitAsync(TimeSpan.FromMilliseconds(profile.PendingTimeoutMs), context.RequestAborted); }
        finally { Interlocked.Decrement(ref _pending); }
        if (!acquired) { context.Response.StatusCode=429; context.Response.Headers.RetryAfter="1"; await context.Response.WriteAsJsonAsync(new{error=new{message="The inference queue wait timed out."}}); return; }
        Interlocked.Increment(ref _active); Interlocked.Exchange(ref _lastActivity,DateTime.UtcNow.Ticks);
        try
        {
            if (isInference)
            {
                var requestedModel=await ResolveRequestedModelAsync(context);
                if(requestedModel is null)return;
                await _engine.EnsureModelAsync(requestedModel,context.RequestAborted);
            }
            else if (!_engine.IsLoaded) { await WriteModelErrorAsync(context,409,"The response session is no longer loaded. Submit a new inference request."); return; }
            var target=new Uri($"http://127.0.0.1:{_engine.Port}{context.Request.Path}{context.Request.QueryString}");
            using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),target);
            if (context.Request.ContentLength is >0 || context.Request.Headers.ContainsKey("Transfer-Encoding")) request.Content=new StreamContent(context.Request.Body);
            foreach(var h in context.Request.Headers.Where(h=>!HopHeaders.Contains(h.Key)&&!h.Key.Equals("Host",StringComparison.OrdinalIgnoreCase)&&!h.Key.Equals("Authorization",StringComparison.OrdinalIgnoreCase))) if(!request.Headers.TryAddWithoutValidation(h.Key,h.Value.ToArray())) request.Content?.Headers.TryAddWithoutValidation(h.Key,h.Value.ToArray());
            if (!string.IsNullOrWhiteSpace(_engine.InternalApiKey)) request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",_engine.InternalApiKey);
            using var response=await _client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,context.RequestAborted);
            context.Response.StatusCode=(int)response.StatusCode;
            foreach(var h in response.Headers.Concat(response.Content.Headers).Where(h=>!HopHeaders.Contains(h.Key))) context.Response.Headers[h.Key]=h.Value.ToArray();
            context.Response.Headers.Remove("transfer-encoding"); await response.Content.CopyToAsync(context.Response.Body,context.RequestAborted);
        }
        catch(Exception ex) when(!context.RequestAborted.IsCancellationRequested) { _log.Write("API request failed",ex); if(!context.Response.HasStarted){context.Response.StatusCode=ex is BadHttpRequestException bad?bad.StatusCode:503;await context.Response.WriteAsJsonAsync(new{error=new{message=ex.Message}});} }
        finally
        {
            try { if(_settings.Current.AutoUnloadEnabled&&_settings.Current.IdleMinutes==0&&_engine.IsLoaded&&Volatile.Read(ref _pending)==0)await _engine.UnloadAsync("after-request memory policy"); }
            catch(Exception ex){_log.Write("After-request model unload failed",ex);}
            Interlocked.Decrement(ref _active);Interlocked.Exchange(ref _lastActivity,DateTime.UtcNow.Ticks);_engine.LifecycleGate.Release();
        }
    }
    private async Task<ModelCatalogEntry?> ResolveRequestedModelAsync(HttpContext context)
    {
        var contentType=context.Request.ContentType??string.Empty;
        if(!contentType.Contains("json",StringComparison.OrdinalIgnoreCase))
        {
            var active=_engine.ActiveModel;
            if(active is not null&&_catalog.IsInstalled(active))return active;
            await WriteModelErrorAsync(context,400,"A model must be selected for this request.");
            return null;
        }
        var limit = 1024L * 1024 * _catalog.InstalledEntries().Select(x=>_settings.ProfileFor(x).MaxRequestMiB).DefaultIfEmpty(384).Max();
        if (context.Request.ContentLength > limit) { context.Response.StatusCode=413; await context.Response.WriteAsJsonAsync(new{error=new{message="The request exceeds the configured body-size limit."}}); return null; }
        context.Request.EnableBuffering(1024 * 1024, limit);
        string? requested=null;
        try
        {
            using var document=await JsonDocument.ParseAsync(context.Request.Body,cancellationToken:context.RequestAborted);
            if(document.RootElement.ValueKind != JsonValueKind.Object) { await WriteModelErrorAsync(context,400,"The request body must be a JSON object."); return null; }
            if(document.RootElement.TryGetProperty("model",out var model)&&model.ValueKind==JsonValueKind.String)requested=model.GetString();
        }
        catch(JsonException)
        {
            await WriteModelErrorAsync(context,400,"The request body is not valid JSON.");
            return null;
        }
        catch(IOException ex) when(ex.Message.Contains("Buffer limit exceeded",StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode=413;
            await context.Response.WriteAsJsonAsync(new{error=new{message="The request exceeds the configured body-size limit."}});
            return null;
        }
        finally{context.Request.Body.Position=0;}
        if(string.IsNullOrWhiteSpace(requested))
        {
            await WriteModelErrorAsync(context,400,"Set the model field to one of the IDs returned by /v1/models.");
            return null;
        }
        var resolved=_catalog.FindInstalledByApiName(requested);
        if(resolved is not null)
        {
            var selectedLimit=1024L*1024*_settings.ProfileFor(resolved).MaxRequestMiB;
            if(context.Request.Body.Length>selectedLimit){context.Response.StatusCode=413;await context.Response.WriteAsJsonAsync(new{error=new{message="The request exceeds this model's configured body-size limit."}});return null;}
            return resolved;
        }
        await WriteModelErrorAsync(context,404,$"Model '{requested}' is not installed or is not exposed by this manager.");
        return null;
    }
    private static async Task WriteModelErrorAsync(HttpContext context,int status,string message)
    {
        context.Response.StatusCode=status;
        await context.Response.WriteAsJsonAsync(new{error=new{message,type="invalid_request_error",param="model"}});
    }
    private static bool Authorized(HttpContext c,string key)
    {
        if(string.IsNullOrWhiteSpace(key))return true;
        var auth=c.Request.Headers.Authorization.ToString(); return auth.Equals("Bearer "+key,StringComparison.Ordinal);
    }
    private async Task CheckIdleAsync()
    {
        if (Interlocked.Exchange(ref _idleCheck,1)!=0 || _lifetime.IsCancellationRequested) return;
        var acquired = false;
        try
        {
            acquired = await _engine.LifecycleGate.WaitAsync(0,_lifetime.Token);
            if (!acquired || Volatile.Read(ref _pending)>0 || !_settings.Current.AutoUnloadEnabled || !_engine.IsLoaded || ActiveRequests>0)return;
            if(DateTime.UtcNow-new DateTime(Interlocked.Read(ref _lastActivity),DateTimeKind.Utc)>=TimeSpan.FromMinutes(_settings.Current.IdleMinutes)) await _engine.UnloadAsync("automatic idle timeout");
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { _log.Write("Automatic model unload failed",ex); }
        finally { if(acquired)_engine.LifecycleGate.Release(); Interlocked.Exchange(ref _idleCheck,0); }
    }
    public async ValueTask DisposeAsync() { if (_lifetime.IsCancellationRequested) return; _lifetime.Cancel(); _timer?.Dispose(); await StopListenerAsync(); _client.Dispose(); }
}
