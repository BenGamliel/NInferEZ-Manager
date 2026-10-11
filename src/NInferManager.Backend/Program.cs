using System.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using NInferManager.Backend;
using NInferManager.Contracts;

if (PortableUpdateRunner.TryRun(args)) return;

var builder = WebApplication.CreateSlimBuilder(args);
var controlPort = int.TryParse(Environment.GetEnvironmentVariable("NINFEREZ_MANAGER_CONTROL_PORT"), out var configuredPort)
    && configuredPort is >= 1024 and <= 65535 ? configuredPort : 48973;
builder.WebHost.UseUrls($"http://127.0.0.1:{controlPort}");
builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = SettingsService.JsonOptions.PropertyNamingPolicy;
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddSingleton<AppPaths>();
builder.Services.AddSingleton<ControlAuthentication>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ManagerLog>();
builder.Services.AddSingleton<ModelCatalog>();
builder.Services.AddSingleton<CatalogMonitor>();
builder.Services.AddHostedService(provider=>provider.GetRequiredService<CatalogMonitor>());
builder.Services.AddSingleton<ModelDownloadService>();
builder.Services.AddSingleton<EnginePackageService>();
builder.Services.AddSingleton<EngineController>();
builder.Services.AddSingleton<PublicApiProxy>();
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddSingleton<IConverterRunner,ConverterRunner>();
builder.Services.AddSingleton<ConversionService>();
builder.Services.AddHostedService(provider=>provider.GetRequiredService<ConversionService>());
var app = builder.Build();
Process? ownedParent=null;
if(int.TryParse(Environment.GetEnvironmentVariable("NINFEREZ_MANAGER_PARENT_PID"),out var parentPid)&&parentPid>0&&parentPid!=Environment.ProcessId)
{
    try
    {
        ownedParent=Process.GetProcessById(parentPid);ownedParent.EnableRaisingEvents=true;
        ownedParent.Exited+=(_,_)=>app.Lifetime.StopApplication();
        if(ownedParent.HasExited)app.Lifetime.StopApplication();
    }
    catch(ArgumentException){app.Lifetime.StopApplication();}
}
app.Lifetime.ApplicationStopped.Register(()=>ownedParent?.Dispose());
var controlAuthentication=app.Services.GetRequiredService<ControlAuthentication>();
app.Use(async (context, next) =>
{
    if (!controlAuthentication.Authorized(context.Request.Headers[ControlAuthentication.Header].ToString()))
    {
        context.Response.StatusCode=401;
        await context.Response.WriteAsJsonAsync(new ApiResult(false,"This control session is not authorized. Open NInferEZ Manager."));
        return;
    }
    try { await next(context); }
    catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
    {
        app.Services.GetRequiredService<ManagerLog>().Write($"Request failed: {context.Request.Method} {context.Request.Path}", ex);
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = ex switch
        {
            FileNotFoundException => StatusCodes.Status404NotFound,
            ArgumentException or InvalidDataException or System.Text.Json.JsonException => StatusCodes.Status400BadRequest,
            InvalidOperationException => StatusCodes.Status409Conflict,
            TimeoutException => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status500InternalServerError
        };
        await context.Response.WriteAsJsonAsync(new ApiResult(false,
            context.Response.StatusCode == 500 ? "The application encountered an internal error. See the manager log for details." : ex.Message));
    }
});
app.Use(async (context,next)=>
{
    var path=context.Request.Path.Value??"";
    var exclusive=context.Request.Method is "POST" or "PUT" or "DELETE" &&
        (path.EndsWith("/activate",StringComparison.Ordinal)||path.EndsWith("/alias",StringComparison.Ordinal)||path.EndsWith("/relink",StringComparison.Ordinal)||context.Request.Method=="DELETE"&&path.StartsWith("/control/v1/models/",StringComparison.Ordinal)
        || path is "/control/v1/engine/load" or "/control/v1/engine/unload" or "/control/v1/engine/restart" or "/control/v1/port/restart" or "/control/v1/update/apply" or "/control/v1/api/state");
    if(!exclusive){await next(context);return;}
    var lifecycle=app.Services.GetRequiredService<EngineController>().LifecycleGate;
    await lifecycle.WaitAsync(context.RequestAborted);
    try{await next(context);}finally{lifecycle.Release();}
});
var proxy = app.Services.GetRequiredService<PublicApiProxy>();
var installCoordinator=app.Services.GetRequiredService<EngineController>();
app.Services.GetRequiredService<EnginePackageService>().CommitInstallation=async (commit,token)=>
{
    await installCoordinator.LifecycleGate.WaitAsync(token);
    try{token.ThrowIfCancellationRequested();await installCoordinator.UnloadAsync("switching verified engine package");commit();}
    finally{installCoordinator.LifecycleGate.Release();}
};
await proxy.StartAsync();
var appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

app.MapGet("/control/v1/health", () => new { status = "ok", product="NInferEZ Manager", instanceId=controlAuthentication.InstanceId, processId=Environment.ProcessId,version = appVersion, engine = "ninfer-all",settingsRecovery=app.Services.GetRequiredService<SettingsService>().RecoveryNotice });
app.MapGet("/control/v1/status", async (EngineController engine) =>
{
    var active = engine.ActiveModel;
    return new ManagerStatus(engine.State, $"http://127.0.0.1:{proxy.Port}/v1", proxy.Port,
        proxy.ChangedAutomatically, proxy.PortConflict, active is null?null:app.Services.GetRequiredService<SettingsService>().ModelIdFor(active), active?.DisplayName, proxy.ActiveRequests,
        await GpuMonitor.ReadAsync(engine.Session?.Profile.Device??0), DateTimeOffset.Now)
        {LoadedModelId=engine.Session?.ModelId,LoadedContext=engine.Session?.Profile.MaxContext,LoadedBackendPort=engine.Session?.Port,LoadedEngineArchitecture=engine.Session?.Architecture,LoadedEngineVersion=engine.Session?.EngineVersion,ApiEnabled=proxy.IsEnabled};
});
app.MapGet("/control/v1/settings", (SettingsService store) => store.Snapshot());
app.MapGet("/control/v1/converter/capabilities", (ConversionService converter,CancellationToken token)=>converter.CapabilitiesAsync(token));
app.MapGet("/control/v1/converter/status", (ConversionService converter)=>converter.Snapshot());
app.MapPost("/control/v1/converter/probe", (ConversionRequest request,ConversionService converter)=>Results.Accepted(value:converter.Start("probe",request)));
app.MapPost("/control/v1/converter/analyze", (ConversionRequest request,ConversionService converter)=>Results.Accepted(value:converter.Start("analyze",request)));
app.MapPost("/control/v1/converter/check", (ConversionRequest request,ConversionService converter)=>Results.Accepted(value:converter.Start("inspect",request)));
app.MapPost("/control/v1/converter/convert", (ConversionRequest request,ConversionService converter)=>Results.Accepted(value:converter.Start("convert",request)));
app.MapPost("/control/v1/converter/cancel", (ConversionService converter)=>converter.Cancel()?Results.Ok(new ApiResult(true,"Safe cancellation requested.")):Results.Conflict(new ApiResult(false,"No conversion is running.")));
app.MapPut("/control/v1/settings", (ManagerSettings value, SettingsService store) => Results.Ok(store.Replace(value)));
app.MapPatch("/control/v1/settings", (SettingsPatchRequest value,SettingsService store,ModelCatalog catalog)=>
{
    if(value.Changes.ValueKind==System.Text.Json.JsonValueKind.Object&&value.Changes.TryGetProperty("profiles",out var proposed)&&proposed.ValueKind==System.Text.Json.JsonValueKind.Object)
    foreach(var profile in proposed.EnumerateObject())
    {
        var model=catalog.Find(profile.Name);
        var p=System.Text.Json.JsonSerializer.Deserialize<ModelProfile>(profile.Value,SettingsService.JsonOptions);
        if(model?.Inspected==true&&p is not null&&
            (p.SpeculativeMode==SpeculativeMode.Mtp&&!model.HasMtp||p.SpeculativeMode==SpeculativeMode.Dflash&&!model.HasDflash||p.SpeculativeMode==SpeculativeMode.Dflash2&&!model.HasDflash2||p.VisionEnabled&&!model.Vision))
            throw new ArgumentException("This model does not contain the selected image or acceleration component. Turn off Image input or choose Disabled/a supported Generation acceleration mode in Inference profile.");
    }
    var saved=store.Patch(value);
    if(value.Changes.TryGetProperty("profiles",out var profiles)&&profiles.ValueKind==System.Text.Json.JsonValueKind.Object)
        foreach(var profile in profiles.EnumerateObject())if(catalog.Find(profile.Name) is{} model)catalog.RememberCustomProfile(model);
    return Results.Ok(saved);
});
app.MapPost("/control/v1/api/state",async(ApiStateRequest value)=>{await proxy.SetEnabledAsync(value.Enabled);return new ApiResult(true,value.Enabled?"The API is enabled.":"The API is disabled.");});
app.MapPost("/control/v1/port/restart", async (PortChangeRequest value, SettingsService store) =>
{
    if(value.ExpectedRevision is long revision&&revision!=store.Current.Revision)throw new InvalidOperationException("Settings changed. Refresh before applying API settings.");
    var previous=store.Snapshot();var previousRuntimePort=proxy.Port;var previousScope=proxy.LocalNetworkEnabled;
    var candidate=SettingsService.Clone(previous);
    candidate.PublicPort=value.Port;candidate.LockPublicPort=value.Locked;candidate.AllowLocalNetwork=value.AllowLocalNetwork;
    if(value.ApiKey is not null)candidate.ApiKey=value.ApiKey;
    if(value.CorsEnabled is bool cors)candidate.CorsEnabled=cors;
    SettingsService.Validate(candidate);
    if(!value.Save&&value.ApiKey is not null)throw new ArgumentException("Credential changes must be saved with the API configuration.");
    await proxy.RestartAsync(value.Port, value.Locked, value.AllowLocalNetwork,value.ApiKey);
    if (value.Save)
    {
        try
        {
        store.Update(s=>
        {
            s.PublicPort=value.Port;s.LockPublicPort=value.Locked;s.AllowLocalNetwork=value.AllowLocalNetwork;
            if(value.ApiKey is not null)s.ApiKey=value.ApiKey;
            if(value.CorsEnabled is bool cors)s.CorsEnabled=cors;
        });
        }
        catch
        {
            await proxy.RestartAsync(previousRuntimePort,true,previousScope,previous.ApiKey);
            throw;
        }
    }
    var scope = value.AllowLocalNetwork ? "this PC and the local network" : "this PC only";
    return new ApiResult(true, $"The API is now available on {scope}, port {proxy.Port}.");
});

app.MapGet("/control/v1/models", (ModelCatalog catalog) => catalog.Snapshot());
app.MapGet("/control/v1/catalog/notifications",(ModelCatalog catalog,CatalogMonitor monitor)=>catalog.Notifications(monitor.StartupCheckComplete));
app.MapPost("/control/v1/catalog/notifications/dismiss",(DismissModelAnnouncementsRequest request,ModelCatalog catalog)=>
{
    if(request.Identities is null)return Results.BadRequest(new ApiResult(false,"Choose the model announcements to dismiss."));
    catalog.DismissAnnouncements(request.Identities);
    return Results.Ok(new ApiResult(true,"Model announcement dismissed. Recent models retain their NEW badge."));
});
app.MapPost("/control/v1/models/{file}/seen",(string file,ModelCatalog catalog)=>{var entry=catalog.Find(file)??throw new FileNotFoundException("The model is not in the library.");catalog.MarkSeen(entry);return new ApiResult(true,"This artifact was marked as seen.");});
app.MapPost("/control/v1/catalog/refresh", async (ModelCatalog catalog, SettingsService store, CancellationToken token) =>
{
    var local=catalog.RefreshLocalArtifacts();
    var online=await catalog.RefreshOnlineAsync(token);
    return new ApiResult(true,$"Library refreshed; {local} local model(s) and {online} online profile(s) added.");
});
app.MapPost("/control/v1/models/link", async (LinkModelRequest value, ModelCatalog catalog,EnginePackageService packages,CancellationToken token) =>
{
    var entry=catalog.LinkExternal(value.Path);
    await catalog.InspectAsync(entry,packages.ActiveDirectory,token);
    return Results.Ok(new ApiResult(true,$"{entry.DisplayName} was linked without copying the file."));
});
app.MapPost("/control/v1/models/{file}/relink",async(string file,LinkModelRequest value,ModelCatalog catalog,EnginePackageService packages,EngineController engine,CancellationToken token)=>
{
    var entry=catalog.Find(file)??throw new FileNotFoundException("The model is not in the library.");
    if(engine.Session?.Model.FileName==entry.FileName)await engine.UnloadAsync("updating model file location");
    catalog.Relink(entry,value.Path);
    await catalog.InspectAsync(entry,packages.ActiveDirectory,token);
    return new ApiResult(true,"The model location was updated. Its API name and saved profile were preserved.");
});
app.MapPost("/control/v1/models/{file}/profile/import",(string file,ImportModelRequest value,ModelCatalog catalog)=>
{
    var entry=catalog.Find(file)??throw new FileNotFoundException("The model is not in the library.");
    catalog.ImportPreset(entry,value.Path);
    return new ApiResult(true,"The preset settings were imported without changing weights. Model SHA-256 identity was not scanned; use Verify if required.");
});
app.MapGet("/control/v1/models/{file}/profile/export",(string file,ModelCatalog catalog,SettingsService settings)=>
{
    var entry=catalog.Find(file)??throw new FileNotFoundException("The model is not in the library.");
    return Results.Text(ModelPresetImporter.Export(entry,settings.ProfileFor(entry)),"application/json");
});
app.MapPost("/control/v1/models/{file}/activate", (string file, ModelCatalog catalog, SettingsService store, EngineController engine) =>
{
    var entry = catalog.Find(file);
    if (entry is null) return Results.NotFound(new ApiResult(false, "Model is not in the catalog."));
    if (!catalog.IsInstalled(entry)) return Results.BadRequest(new ApiResult(false, "Install the model before selecting it."));
    if (string.Equals(store.Current.ActiveModelFile, entry.FileName, StringComparison.OrdinalIgnoreCase))
        return Results.Ok(new ApiResult(true, $"{entry.DisplayName} is already active."));
    if (engine.State is EngineState.Loading or EngineState.Ready or EngineState.Unloading)
        return Results.Conflict(new ApiResult(false, "Unload the current model before selecting a different one."));
    store.Update(s=>s.ActiveModelFile=entry.FileName);
    store.ProfileFor(entry);
    return Results.Ok(new ApiResult(true, $"{entry.DisplayName} is active."));
});
app.MapPut("/control/v1/models/{file}/alias", async (string file, ModelAliasRequest value, ModelCatalog catalog, SettingsService store, EngineController engine) =>
{
    var entry=catalog.Find(file);
    if(entry is null)return Results.NotFound(new ApiResult(false,"Model is not in the catalog."));
    if(!catalog.IsInstalled(entry))return Results.BadRequest(new ApiResult(false,"Install or link the model before editing its API name."));
    var alias=(value.Alias??string.Empty).Trim();
    if(alias.Length>128||alias.Any(char.IsControl))return Results.BadRequest(new ApiResult(false,"API model names must contain at most 128 printable characters."));
    var effective=string.IsNullOrWhiteSpace(alias)?entry.ModelId:alias;
    if(catalog.InstalledEntries().Any(x=>!x.FileName.Equals(entry.FileName,StringComparison.OrdinalIgnoreCase)&&store.ModelIdFor(x).Equals(effective,StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new ApiResult(false,"Another installed model already uses this API name."));
    if((engine.Session?.Model.FileName??store.Current.ActiveModelFile).Equals(entry.FileName,StringComparison.OrdinalIgnoreCase)&&(engine.State is EngineState.Loading or EngineState.Ready||engine.OperationStatus.State==EngineOperationState.Running))
    {
        if(proxy.ActiveRequests>0)return Results.Conflict(new ApiResult(false,"Wait for the active request to finish before renaming this model."));
        await engine.UnloadAsync("API model name changed");
    }
    store.Update(s=>
    {
        if(string.IsNullOrWhiteSpace(alias)||alias.Equals(entry.ModelId,StringComparison.Ordinal))s.ModelAliases.Remove(entry.FileName);
        else s.ModelAliases[entry.FileName]=alias;
    });
    return Results.Ok(new ApiResult(true,$"API clients can now select this model as '{effective}'."));
});
app.MapPost("/control/v1/models/{file}/download", (string file, ModelCatalog catalog, ModelDownloadService downloads) =>
{
    var entry = catalog.Find(file);
    if (entry is null) return Results.NotFound(new ApiResult(false, "Model is not in the catalog."));
    if (catalog.IsInstalled(entry)) return Results.Ok(new ApiResult(true, "This model variant is already available locally. No download is needed."));
    if (!entry.DownloadAvailable) return Results.BadRequest(new ApiResult(false, "This model is supplied with the full installer or can be imported from an existing NInfer artifact."));
    downloads.Start(entry);
    return Results.Accepted(value: new ApiResult(true, "Download started."));
});
app.MapGet("/control/v1/download", (ModelDownloadService downloads) => downloads.Current);
app.MapPost("/control/v1/download/cancel", (ModelDownloadService downloads) =>
{
    downloads.Cancel();
    return new ApiResult(true, "Download pause requested.");
});
app.MapPost("/control/v1/models/{file}/verify", async (string file, ModelCatalog catalog, ModelDownloadService downloads, CancellationToken token) =>
{
    var entry = catalog.Find(file);
    if (entry is null) return Results.NotFound(new ApiResult(false, "Model is not in the catalog."));
    var ok = await downloads.VerifyAsync(entry, token);
    if(ok)catalog.RememberVerification(entry);
    return Results.Ok(new ApiResult(ok, ok ? "Verification passed." : "Verification failed."));
});
app.MapPost("/control/v1/models/{file}/import", async (string file, ImportModelRequest value, ModelCatalog catalog, ModelDownloadService downloads, CancellationToken token) =>
{
    var entry = catalog.Find(file);
    if (entry is null) return Results.NotFound(new ApiResult(false, "Model is not in the catalog."));
    await downloads.ImportAsync(value.Path, entry, token);
    return Results.Ok(new ApiResult(true, "Existing model linked and verified. The file was not copied."));
});
app.MapDelete("/control/v1/models/{file}", async (string file, ModelCatalog catalog, SettingsService store, EngineController engine, AppPaths paths) =>
{
    var entry = catalog.Find(file);
    if (entry is null) return Results.NotFound();
    if ((engine.Session?.Model.FileName??store.Current.ActiveModelFile).Equals(file, StringComparison.OrdinalIgnoreCase) && (engine.IsLoaded||engine.OperationStatus.State==EngineOperationState.Running))
        await engine.UnloadAsync("active model deletion");
    if (catalog.IsExternal(entry))
    {
        store.Update(s=>s.ExternalModelPaths.Remove(entry.FileName));
    }
    else
    {
        var path = catalog.ResolvePath(entry);
        if (File.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
    if (store.Current.ActiveModelFile.Equals(file, StringComparison.OrdinalIgnoreCase))
    {
        store.Update(s=>s.ActiveModelFile="");
    }
    return Results.Ok(new ApiResult(true, "Model removed from the manager. Linked external files are never deleted."));
});

app.MapPost("/control/v1/engine/load", (EngineController engine) => Results.Accepted(value: engine.StartLoad()));
app.MapGet("/control/v1/engine/operation", (EngineController engine) => engine.OperationStatus);
app.MapPost("/control/v1/engine/operation/cancel", (EngineController engine) =>
    engine.CancelLoad()
        ? Results.Accepted(value: new ApiResult(true, "Engine load cancellation requested."))
        : Results.Conflict(new ApiResult(false, "No cancellable engine load is running.")));
app.MapPost("/control/v1/engine/unload", async (EngineController engine) => { await engine.UnloadAsync("WinUI command"); return new ApiResult(true, "Model unloaded."); });
app.MapPost("/control/v1/engine/restart", async (EngineController engine) => { await engine.UnloadAsync("WinUI restart"); return Results.Accepted(value: engine.StartLoad()); });
app.MapGet("/control/v1/engines", (EnginePackageService packages, CancellationToken token) => packages.SnapshotAsync(token));
app.MapPost("/control/v1/engines/refresh", async (EnginePackageService packages, CancellationToken token) => { await packages.RefreshAsync(token); return Results.Ok(new ApiResult(true, "Engine catalog refreshed.")); });
app.MapPost("/control/v1/engines/{version}/{architecture}/install", (string version, string architecture, EnginePackageService packages, EngineController engine) =>
{
    if (engine.State is EngineState.Loading or EngineState.Ready or EngineState.Unloading) return Results.Conflict(new ApiResult(false, "Unload the model before changing the engine package."));
    packages.StartInstall(version, architecture); return Results.Accepted(value: new ApiResult(true, "Engine download started."));
});
app.MapPost("/control/v1/engines/install/cancel", (EnginePackageService packages) => { packages.CancelInstall(); return Results.Accepted(value: new ApiResult(true, "Engine download cancellation requested.")); });
app.MapGet("/control/v1/engines/progress", (EnginePackageService packages) => packages.Current);
app.MapPost("/control/v1/engines/{version}/{architecture}/activate", (string version, string architecture, EnginePackageService packages, EngineController engine) =>
{
    if (engine.State is EngineState.Loading or EngineState.Ready or EngineState.Unloading) return Results.Conflict(new ApiResult(false, "Unload the model before changing the engine package."));
    packages.Activate(version, architecture); return Results.Ok(new ApiResult(true, $"NInferEZ Engine {version} ({architecture}) is active."));
});
app.MapGet("/control/v1/command", (EngineController engine) => Results.Text(engine.BuildCommandPreview(), "text/plain"));
app.MapGet("/control/v1/logs", (ManagerLog log) => Results.Text(log.Tail(1000), "text/plain"));
app.MapGet("/control/v1/requests", (ManagerLog log) => log.Metrics());
app.MapPost("/control/v1/diagnostics", (AppPaths paths, SettingsService store, ManagerLog log) =>
    new ApiResult(true, SystemOperations.CreateDiagnostics(paths, store, log)));
app.MapPost("/control/v1/trim", () => { SystemOperations.TrimWorkingSet(); return new ApiResult(true, "Working set trimmed."); });

app.MapGet("/control/v1/update", (UpdateService updates, CancellationToken token) => updates.CheckAsync(token));
app.MapPost("/control/v1/update/download", (UpdateInfo value, UpdateService updates) =>
{
    updates.StartDownload(value);
    return Results.Accepted(value: new ApiResult(true, "Update download started."));
});
app.MapGet("/control/v1/update/progress", (UpdateService updates) => updates.Current);
app.MapPost("/control/v1/update/cancel",(UpdateService updates)=>{updates.CancelDownload();return new ApiResult(true,"Update download cancellation requested.");});
app.MapPost("/control/v1/update/apply", async (ApplyUpdateRequest value, UpdateService updates, AppPaths paths,EngineController engine,ConversionService converter,CancellationToken token) =>
{
    if(converter.Snapshot().Running)return Results.Conflict(new ApiResult(false,"Finish or cancel the conversion before applying an application update."));
    var package = updates.Current?.PackagePath;
    if (string.IsNullOrWhiteSpace(package) || !File.Exists(package))
        return Results.BadRequest(new ApiResult(false, "No verified update is ready."));
    await updates.VerifyReadyPackageAsync(token);
    await engine.UnloadAsync("applying verified application update");
    var helperDirectory=Path.Combine(paths.DataDirectory,"Updates","helpers");Directory.CreateDirectory(helperDirectory);
    var helper = Path.Combine(helperDirectory, $"NInferEZ-Manager-Updater-{Guid.NewGuid():N}.exe");
    File.Copy(Environment.ProcessPath!, helper, true);
    var info = new ProcessStartInfo(helper) { UseShellExecute = false,CreateNoWindow=true };
    foreach (var argument in new[] { paths.IsPortable?"--apply-portable-update":"--apply-installed-update", paths.IsPortable?"--zip":"--installer", package, "--target", paths.AppDirectory, "--ui-pid", value.UiProcessId.ToString(), "--backend-pid", Environment.ProcessId.ToString(),"--sha256",updates.VerifiedPackageSha256! })
        info.ArgumentList.Add(argument);
    _ = Process.Start(info) ?? throw new IOException("The application update helper could not start.");
    _ = Task.Run(async () => { await Task.Delay(1200); Environment.Exit(0); });
    return Results.Ok(new ApiResult(true, "The verified update is starting."));
});

app.MapPost("/control/v1/open", (string target, AppPaths paths, ManagerLog log) =>
{
    var value = target switch
    {
        "models" => paths.ModelsDirectory,
        "logs" => Path.GetDirectoryName(log.FilePath)!,
        _ => paths.DataDirectory
    };
    Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
    return new ApiResult(true, "Opened.");
});

app.Lifetime.ApplicationStopping.Register(() => proxy.DisposeAsync().AsTask().GetAwaiter().GetResult());
var startupEngines = app.Services.GetRequiredService<EnginePackageService>();
if (startupEngines.ShouldRefresh())
    _ = Task.Run(async () =>
    {
        try { await startupEngines.RefreshAsync(); }
        catch (Exception ex) { app.Services.GetRequiredService<ManagerLog>().Write("Automatic engine catalog refresh failed", ex); }
    });
await app.RunAsync();

public sealed record PortChangeRequest(int Port, bool Locked, bool AllowLocalNetwork, bool Save,string? ApiKey=null,bool? CorsEnabled=null,long? ExpectedRevision=null);
public sealed record ImportModelRequest(string Path);
public sealed record LinkModelRequest(string Path);
public sealed record ModelAliasRequest(string? Alias);
public sealed record ApplyUpdateRequest(int UiProcessId);
public sealed record ApiStateRequest(bool Enabled);
