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
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ManagerLog>();
builder.Services.AddSingleton<ModelCatalog>();
builder.Services.AddSingleton<ModelDownloadService>();
builder.Services.AddSingleton<EnginePackageService>();
builder.Services.AddSingleton<EngineController>();
builder.Services.AddSingleton<PublicApiProxy>();
builder.Services.AddSingleton<UpdateService>();
var app = builder.Build();
app.Use(async (context, next) =>
{
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
var proxy = app.Services.GetRequiredService<PublicApiProxy>();
await proxy.StartAsync();
var appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

app.MapGet("/control/v1/health", () => new { status = "ok", version = appVersion, engine = "ninfer-all" });
app.MapGet("/control/v1/status", async (EngineController engine) =>
{
    var active = engine.ActiveModel;
    return new ManagerStatus(engine.State, $"http://127.0.0.1:{proxy.Port}/v1", proxy.Port,
        proxy.ChangedAutomatically, proxy.PortConflict, active is null?null:app.Services.GetRequiredService<SettingsService>().ModelIdFor(active), active?.DisplayName, proxy.ActiveRequests,
        await GpuMonitor.ReadAsync(), DateTimeOffset.Now);
});
app.MapGet("/control/v1/settings", (SettingsService store) => store.Current);
app.MapPut("/control/v1/settings", (ManagerSettings value, SettingsService store) => Results.Ok(store.Replace(value)));
app.MapPost("/control/v1/port/restart", async (PortChangeRequest value, SettingsService store) =>
{
    await proxy.RestartAsync(value.Port, value.Locked);
    if (value.Save)
    {
        store.Current.PublicPort = value.Port;
        store.Current.LockPublicPort = value.Locked;
        store.Save();
    }
    return new ApiResult(true, $"The API is now available at http://127.0.0.1:{proxy.Port}/v1");
});

app.MapGet("/control/v1/models", (ModelCatalog catalog) => catalog.Snapshot());
app.MapPost("/control/v1/catalog/refresh", async (ModelCatalog catalog, SettingsService store, CancellationToken token) =>
{
    var local=catalog.RefreshLocalArtifacts();
    var online=store.Current.AutoCheckCatalog?await catalog.RefreshOnlineAsync(token):0;
    return new ApiResult(true,$"Library refreshed; {local} local model(s) and {online} online profile(s) added.");
});
app.MapPost("/control/v1/models/link", (LinkModelRequest value, ModelCatalog catalog) =>
{
    var entry=catalog.LinkExternal(value.Path);
    return Results.Ok(new ApiResult(true,$"{entry.DisplayName} was linked without copying the file."));
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
    store.Current.ActiveModelFile = entry.FileName;
    store.ProfileFor(entry);
    store.Save();
    return Results.Ok(new ApiResult(true, $"{entry.DisplayName} is active."));
});
app.MapPost("/control/v1/models/{file}/download", (string file, ModelCatalog catalog, ModelDownloadService downloads) =>
{
    var entry = catalog.Find(file);
    if (entry is null) return Results.NotFound(new ApiResult(false, "Model is not in the catalog."));
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
    if (store.Current.ActiveModelFile.Equals(file, StringComparison.OrdinalIgnoreCase) && engine.IsLoaded)
        await engine.UnloadAsync("active model deletion");
    if (catalog.IsExternal(entry))
    {
        store.Current.ExternalModelPaths.Remove(entry.FileName);
    }
    else
    {
        var path = Path.Combine(paths.ModelsDirectory, entry.FileName);
        if (File.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
    if (store.Current.ActiveModelFile.Equals(file, StringComparison.OrdinalIgnoreCase))
    {
        store.Current.ActiveModelFile = "";
        store.Save();
    }
    store.Save();
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
app.MapGet("/control/v1/requests", (ManagerLog log) => RequestMetricsParser.Parse(log.Tail(10000)));
app.MapPost("/control/v1/diagnostics", (AppPaths paths, SettingsService store, ManagerLog log) =>
    new ApiResult(true, SystemOperations.CreateDiagnostics(paths, store, log)));
app.MapPost("/control/v1/trim", () => { SystemOperations.TrimWorkingSet(); return new ApiResult(true, "Working set trimmed."); });

app.MapGet("/control/v1/update", (UpdateService updates, CancellationToken token) => updates.CheckAsync(token));
app.MapPost("/control/v1/update/download", (UpdateInfo value, UpdateService updates) =>
{
    _ = Task.Run(() => updates.DownloadAsync(value, CancellationToken.None));
    return Results.Accepted(value: new ApiResult(true, "Update download started."));
});
app.MapGet("/control/v1/update/progress", (UpdateService updates) => updates.Current);
app.MapPost("/control/v1/update/apply", (ApplyUpdateRequest value, UpdateService updates, AppPaths paths) =>
{
    var package = updates.Current?.PackagePath;
    if (string.IsNullOrWhiteSpace(package) || !File.Exists(package))
        return Results.BadRequest(new ApiResult(false, "No verified update is ready."));
    if (paths.IsPortable)
    {
        var helper = Path.Combine(Path.GetTempPath(), $"NInferEZ-Manager-Updater-{Guid.NewGuid():N}.exe");
        File.Copy(Environment.ProcessPath!, helper, true);
        var info = new ProcessStartInfo(helper) { UseShellExecute = false };
        foreach (var argument in new[] { "--apply-portable-update", "--zip", package, "--target", paths.AppDirectory, "--ui-pid", value.UiProcessId.ToString(), "--backend-pid", Environment.ProcessId.ToString() })
            info.ArgumentList.Add(argument);
        Process.Start(info);
    }
    else Process.Start(new ProcessStartInfo(package, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
    _ = Task.Run(async () => { await Task.Delay(1200); Environment.Exit(0); });
    return Results.Ok(new ApiResult(true, "The verified update is starting."));
});

app.MapPost("/control/v1/open", (string target, AppPaths paths, ManagerLog log) =>
{
    var value = target switch
    {
        "models" => paths.ModelsDirectory,
        "logs" => log.FilePath,
        _ => paths.DataDirectory
    };
    Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
    return new ApiResult(true, "Opened.");
});

app.Lifetime.ApplicationStopping.Register(() => proxy.DisposeAsync().AsTask().GetAwaiter().GetResult());
var startupCatalog = app.Services.GetRequiredService<ModelCatalog>();
if (startupCatalog.ShouldRefresh())
    _ = Task.Run(async () =>
    {
        try { await startupCatalog.RefreshOnlineAsync(); }
        catch (Exception ex) { app.Services.GetRequiredService<ManagerLog>().Write("Automatic catalog refresh failed", ex); }
    });
var startupEngines = app.Services.GetRequiredService<EnginePackageService>();
if (startupEngines.ShouldRefresh())
    _ = Task.Run(async () =>
    {
        try { await startupEngines.RefreshAsync(); }
        catch (Exception ex) { app.Services.GetRequiredService<ManagerLog>().Write("Automatic engine catalog refresh failed", ex); }
    });
await app.RunAsync();

public sealed record PortChangeRequest(int Port, bool Locked, bool Save);
public sealed record ImportModelRequest(string Path);
public sealed record LinkModelRequest(string Path);
public sealed record ApplyUpdateRequest(int UiProcessId);
