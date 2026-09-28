using System.Net;
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
    private readonly ManagerLog _log;
    private readonly HttpClient _client = new(new SocketsHttpHandler { UseProxy=false, MaxConnectionsPerServer=100 }) { Timeout=Timeout.InfiniteTimeSpan };
    private WebApplication? _app;
    private Timer? _timer;
    private long _lastActivity = DateTime.UtcNow.Ticks;
    private int _active;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    public PublicApiProxy(EngineController engine, SettingsService settings, ManagerLog log) { _engine=engine; _settings=settings; _log=log; }
    public int Port { get; private set; }
    public bool ChangedAutomatically { get; private set; }
    public bool PortConflict { get; private set; }
    public int ActiveRequests => Volatile.Read(ref _active);

    public async Task StartAsync()
    {
        await StartOnPortAsync(_settings.Current.PublicPort,_settings.Current.LockPublicPort,true);
    }
    private async Task StartOnPortAsync(int requested,bool locked,bool startup=false)
    {
        if (PortService.IsAvailable(requested)) Port=requested;
        else if (locked&&startup){Port=requested;PortConflict=true;_log.Write($"Public API port {requested} is locked and already in use.");return;}
        else if (locked) throw new InvalidOperationException($"Port {requested} is already in use and is locked.");
        else { Port=PortService.FindAvailable(_settings.Current.BackendPort); ChangedAutomatically=true; }
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args=[] });
        builder.Logging.ClearProviders(); builder.WebHost.UseKestrel(k=>k.Listen(IPAddress.Loopback,Port));
        var app=builder.Build(); app.Run(HandleAsync); await app.StartAsync(); _app=app;
        _timer=new Timer(_=>_ = CheckIdleAsync(),null,TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(15));
        _log.Write($"Public OpenAI-compatible API listening at http://127.0.0.1:{Port}/v1");
    }
    public async Task RestartAsync(int requested,bool locked)
    {
        if(requested is < 1024 or > 65535)throw new ArgumentOutOfRangeException(nameof(requested));
        if(requested==_settings.Current.BackendPort)throw new InvalidOperationException("The public and internal ports must be different.");
        _timer?.Dispose();_timer=null;
        if(_app is not null){await _app.StopAsync();await _app.DisposeAsync();_app=null;}
        ChangedAutomatically=false;PortConflict=false;
        await StartOnPortAsync(requested,locked);
    }
    private async Task HandleAsync(HttpContext context)
    {
        if(_settings.Current.CorsEnabled)
        {
            context.Response.Headers.AccessControlAllowOrigin="*";
            context.Response.Headers.AccessControlAllowHeaders="Authorization, Content-Type";
            context.Response.Headers.AccessControlAllowMethods="GET, POST, OPTIONS";
        }
        if(context.Request.Method=="OPTIONS"){context.Response.StatusCode=204;return;}
        if (context.Request.Path.Equals("/health") || context.Request.Path.Equals("/manager/health")) { var active=_engine.ActiveModel;await context.Response.WriteAsJsonAsync(new {status="ok",engine=_engine.State.ToString(),model=active is null?null:_settings.ModelIdFor(active)}); return; }
        if (context.Request.Method=="POST" && context.Request.Path.Equals("/manager/unload")) { if(!Authorized(context)){context.Response.StatusCode=401;return;} await _engine.UnloadAsync("public API"); await context.Response.WriteAsJsonAsync(new{status="ok"}); return; }
        if (!context.Request.Path.StartsWithSegments("/v1")) { context.Response.StatusCode=404; return; }
        if (!Authorized(context)) { context.Response.StatusCode=401; await context.Response.WriteAsJsonAsync(new{error=new{message="Invalid or missing API key."}}); return; }
        if (context.Request.Method=="GET" && context.Request.Path.Equals("/v1/models"))
        {
            var active=_engine.ActiveModel;
            var models=active is null?Array.Empty<object>():new object[]{new{id=_settings.ModelIdFor(active),@object="model",created=0,owned_by="local"}};
            await context.Response.WriteAsJsonAsync(new{@object="list",data=models});
            return;
        }
        if (!await _requestGate.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode=429; await context.Response.WriteAsJsonAsync(new{error=new{message="NInferEZ Manager supports one active request. Try again after the current request finishes."}}); return; }
        Interlocked.Increment(ref _active); Interlocked.Exchange(ref _lastActivity,DateTime.UtcNow.Ticks);
        try
        {
            await _engine.LoadAsync(context.RequestAborted);
            var target=new Uri($"http://127.0.0.1:{_engine.Port}{context.Request.Path}{context.Request.QueryString}");
            using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),target);
            if (context.Request.ContentLength is >0 || context.Request.Headers.ContainsKey("Transfer-Encoding")) request.Content=new StreamContent(context.Request.Body);
            foreach(var h in context.Request.Headers.Where(h=>!HopHeaders.Contains(h.Key)&&!h.Key.Equals("Host",StringComparison.OrdinalIgnoreCase))) if(!request.Headers.TryAddWithoutValidation(h.Key,h.Value.ToArray())) request.Content?.Headers.TryAddWithoutValidation(h.Key,h.Value.ToArray());
            using var response=await _client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,context.RequestAborted);
            context.Response.StatusCode=(int)response.StatusCode;
            foreach(var h in response.Headers.Concat(response.Content.Headers).Where(h=>!HopHeaders.Contains(h.Key))) context.Response.Headers[h.Key]=h.Value.ToArray();
            context.Response.Headers.Remove("transfer-encoding"); await response.Content.CopyToAsync(context.Response.Body,context.RequestAborted);
        }
        catch(Exception ex) when(!context.RequestAborted.IsCancellationRequested) { _log.Write("API request failed",ex); if(!context.Response.HasStarted){context.Response.StatusCode=503;await context.Response.WriteAsJsonAsync(new{error=new{message=ex.Message}});} }
        finally { Interlocked.Decrement(ref _active); Interlocked.Exchange(ref _lastActivity,DateTime.UtcNow.Ticks); _requestGate.Release(); }
    }
    private bool Authorized(HttpContext c)
    {
        var key=_settings.Current.ApiKey; if(string.IsNullOrWhiteSpace(key))return true;
        var auth=c.Request.Headers.Authorization.ToString(); return auth.Equals("Bearer "+key,StringComparison.Ordinal);
    }
    private async Task CheckIdleAsync()
    {
        if(!_settings.Current.AutoUnloadEnabled || !_engine.IsLoaded || ActiveRequests>0)return;
        if(DateTime.UtcNow-new DateTime(Interlocked.Read(ref _lastActivity),DateTimeKind.Utc)>=TimeSpan.FromMinutes(_settings.Current.IdleMinutes)) await _engine.UnloadAsync("automatic idle timeout");
    }
    public async ValueTask DisposeAsync() { _timer?.Dispose(); if(_app is not null){await _app.StopAsync();await _app.DisposeAsync();} _client.Dispose(); _requestGate.Dispose(); }
}
