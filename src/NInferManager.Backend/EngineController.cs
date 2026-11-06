using System.Diagnostics;
using System.Globalization;
using System.Collections.Concurrent;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public sealed class EngineController : IAsyncDisposable
{
    public SemaphoreSlim LifecycleGate { get; } = new(1, 1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _modelSwitchGate = new(1, 1);
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly ModelCatalog _catalog;
    private readonly ManagerLog _log;
    private readonly ContextFitHistory _fitHistory;
    private readonly EnginePackageService? _enginePackages;
    private readonly ProcessJob _processJob=new();
    private Process? _process;
    private readonly ConcurrentQueue<string> _startupMessages = new();
    private readonly ConcurrentQueue<string> _startupFailures = new();
    private readonly object _operationSync = new();
    private CancellationTokenSource? _loadCancellation;
    private Task? _loadTask;
    private EngineOperationStatus _operation = IdleOperation();
    public EngineController(AppPaths paths, SettingsService settings, ModelCatalog catalog, ManagerLog log, EnginePackageService? enginePackages = null)
    {
        _paths = paths; _settings = settings; _catalog = catalog; _log = log; _enginePackages = enginePackages;
        _fitHistory = new(paths.DataDirectory);
    }
    public EngineState State { get; private set; }
    public bool IsLoaded => State == EngineState.Ready && _process is { HasExited: false };
    public ModelCatalogEntry? ActiveModel => _catalog.Find(_settings.Current.ActiveModelFile);
    public LoadedEngineSession? Session { get; private set; }
    public ContextFitResult? LastContextFit { get; private set; }
    public int Port => Session?.Port ?? _settings.Current.BackendPort;
    public string? InternalApiKey => Session?.ApiKey;

    public IReadOnlyList<string> BuildArguments()
    {
        var model = ActiveModel ?? throw new InvalidOperationException("Choose an installed model first.");
        return BuildArguments(model, SettingsService.Clone(_settings.ProfileFor(model)), _settings.Snapshot());
    }
    private IReadOnlyList<string> BuildArguments(ModelCatalogEntry model, ModelProfile p, ManagerSettings settings)
    {
        p = p.ManageAdvancedSettings ? ProfileSettingsPolicy.Managed(p, model.RecommendedProfile()) : ProfileSettingsPolicy.Effective(p);
        if (p.AutoContext) p.KvCapacityMode = KvCapacityMode.Auto;
        var path = _catalog.ResolvePath(model);
        if(model.InvalidAdjacentPreset)throw new InvalidOperationException(model.PresetStatus);
        SettingsService.ValidateProfile(p);
        if (p.MaxContext > model.MaximumContext)
            throw new InvalidOperationException($"{model.DisplayName} supports at most {model.MaximumContext:N0} context tokens in this manager profile.");
        if (p.DefaultMaxTokens > p.MaxContext)
            throw new InvalidOperationException("The default output-token limit cannot exceed the selected context window.");
        if (!model.Inspected && model.ModelId == "qwen3.8-27b-orcarouter-iq3-xxs" && p.SpeculativeMode is SpeculativeMode.Dflash or SpeculativeMode.Dflash2)
            throw new InvalidOperationException("This IQ3_XXS artifact contains MTP weights but no DFlash/DFlash2 drafter. Select MTP or Disabled.");
        if (model.Inspected)
        {
            if (!model.CompatibleWithBuild) throw new InvalidOperationException("The selected engine build cannot execute this artifact. Select a compatible engine in Engine.");
            if (p.SpeculativeMode == SpeculativeMode.Dflash2 && !model.HasDflash2 || p.SpeculativeMode == SpeculativeMode.Dflash && !model.HasDflash || p.SpeculativeMode == SpeculativeMode.Mtp && !model.HasMtp) throw new InvalidOperationException($"This model does not include {p.SpeculativeMode} acceleration. Choose Disabled or a supported mode under Generation acceleration in Inference profile.");
            if (p.VisionEnabled && !model.Vision) throw new InvalidOperationException("This model does not contain a vision component. Disable vision for this profile.");
        }
        var args = new List<string> { path, "--model-id", settings.ModelAliases.GetValueOrDefault(model.FileName, model.ModelId), "--max-context", I(p.MaxContext), "--default-max-tokens", I(p.DefaultMaxTokens), "--kv-dtype", p.KvPrecision.ToString().ToLowerInvariant(), "--device", I(p.Device), "--max-concurrency", I(p.MaxConcurrency), "--max-pending-requests", I(p.MaxPendingRequests), "--pending-timeout-ms", I(p.PendingTimeoutMs), "--max-request-mib", I(p.MaxRequestMiB), "--log-stats-interval-ms", I(p.LogStatsIntervalMs), "--media-cache-mib", I(p.MediaCacheMiB), "--media-live-mib", I(p.MediaLiveMiB), "--media-preprocess-threads", I(p.MediaPreprocessThreads), "--device-state-slots", I(p.DeviceStateSlots), "--host-state-slots", I(p.HostStateSlots), "--host-kv-mib", I(p.HostKvMiB), "--response-store-max-records", I(p.ResponseStoreMaxRecords), "--response-store-max-mib", I(p.ResponseStoreMaxMiB), "--prefill-chunk", I(p.PrefillChunk), "--host", "127.0.0.1", "--port", I(settings.BackendPort) };
        if (p.KvCapacityMode == KvCapacityMode.MatchContext) Add(args, "--kv-capacity", p.MaxContext);
        if (p.KvCapacityMode == KvCapacityMode.Auto) Add(args, "--kv-capacity", "auto");
        if (p.KvCapacityMode == KvCapacityMode.Auto) Add(args, "--kv-headroom-mib", p.KvHeadroomMiB);
        if (p.KvCapacityMode == KvCapacityMode.Custom) Add(args, "--kv-capacity", p.CustomKvCapacity);
        if (p.SpeculativeMode != SpeculativeMode.Disabled) { Add(args, "--spec", p.SpeculativeMode.ToString().ToLowerInvariant()); Add(args, "--draft-tokens", p.DraftTokens); }
        if (p.SpeculativeMode != SpeculativeMode.Disabled && p.LmHeadDraft) args.Add("--lm-head-draft");
        if (p.SpeculativeMode != SpeculativeMode.Disabled && p.NgramDraftTokens > 0) Add(args, "--ngram-draft-tokens", p.NgramDraftTokens);
        if (p.StructuredOutput) args.Add("--structured-output");
        if (p.AlternativePrefixCache) args.Add("--use-alt-prefix-caching");
        if (p.HostCacheMiB is int hostCache)
        {
            for (var index = args.Count - 2; index >= 0; index--) if (args[index] is "--host-state-slots" or "--host-kv-mib") { args.RemoveAt(index + 1); args.RemoveAt(index); }
            Add(args,"--host-cache-mib",hostCache);
        }
        if (p.VisionEnabled && model.Vision) { args.Add("--vision"); Add(args, "--vision-residency", p.VisionResidency); }
        else
        {
            RemovePairs(args, "--media-cache-mib", "--media-live-mib", "--media-preprocess-threads");
        }
        if (p.AlternativePrefixCache)
            RemovePairs(args, "--device-state-slots", "--host-state-slots", "--host-kv-mib");
        if (!p.CudaGraphEnabled) args.Add("--no-cuda-graph");
        else if(p.CudaGraphAllowanceMiB is int graphAllowance) Add(args, "--cuda-graph-allowance-mib", graphAllowance);
        if (!p.PrefixReuseEnabled) args.Add("--no-prefix-reuse");
        if (p.GdnStateFp16) args.Add("--gdn-state-fp16");
        if (p.SpeculativeMode == SpeculativeMode.Mtp && p.MtpAttentionWindow > 0) Add(args, "--mtp-attention-window", p.MtpAttentionWindow);
        if (model.NativeContext>0&&p.MaxContext > model.NativeContext&&(model.Repository!="Local file"||model.YarnSupported)) args.Add("--rope-yarn");
        if (!p.ThinkingEnabled) args.Add("--no-thinking");
        if (p.PreserveThinking) args.Add("--preserve-thinking");
        if (!p.AlternativePrefixCache && p.MaxPrivateContinuations >= 0) Add(args, "--max-private-continuations", p.MaxPrivateContinuations);
        if (!p.AlternativePrefixCache && p.MaxSharedPrefixes >= 0) Add(args, "--max-shared-prefixes", p.MaxSharedPrefixes);
        if (!p.AlternativePrefixCache && p.MaxLongAnchorsPerContinuation >= 0) Add(args, "--max-long-anchors-per-continuation", p.MaxLongAnchorsPerContinuation);
        if (p.MaxCacheMarkersPerRequest >= 0) Add(args, "--max-cache-markers-per-request", p.MaxCacheMarkersPerRequest);
        if (p.DefaultThinkingBudget is not null) Add(args, "--default-thinking-budget", p.DefaultThinkingBudget.Value);
        if (p.Greedy) args.Add("--greedy");
        if (p.Temperature is not null) Add(args, "--temperature", p.Temperature.Value);
        if (p.TopP is not null) Add(args, "--top-p", p.TopP.Value);
        if (p.TopK is not null) Add(args, "--top-k", p.TopK.Value);
        if (p.MinP is not null) Add(args, "--min-p", p.MinP.Value);
        if (p.PresencePenalty is not null) Add(args, "--presence-penalty", p.PresencePenalty.Value);
        if (p.FrequencyPenalty is not null) Add(args, "--frequency-penalty", p.FrequencyPenalty.Value);
        if (p.Seed is not null) Add(args, "--seed", p.Seed.Value);
        if (!string.IsNullOrWhiteSpace(p.ContextCostPresetsFile)) Add(args, "--context-cost-presets", p.ContextCostPresetsFile);
        if (!string.IsNullOrWhiteSpace(p.RequestLogJsonlFile)) Add(args, "--request-log-jsonl", p.RequestLogJsonlFile);
        if (!string.IsNullOrWhiteSpace(p.ChatTemplateFile)) Add(args, "--chat-template", p.ChatTemplateFile);
        Add(args, "--log-level", p.LogLevel.ToString().ToLowerInvariant());
        if (settings.CorsEnabled) args.Add("--cors");
        if (!string.IsNullOrWhiteSpace(settings.ApiKey)) Add(args, "--api-key", settings.ApiKey);
        args[2]=args[2].Trim();
        return args;
    }

    private static void RemovePairs(List<string> args, params string[] flags)
    {
        for (var index = args.Count - 2; index > 0; index--)
            if (flags.Contains(args[index])) { args.RemoveAt(index + 1); args.RemoveAt(index); }
    }

    public Func<string?>? ProductUpdateBlocker { get; set; }

    public EngineOperationStatus StartLoad(ModelCatalogEntry? requestedModel=null)
    {
        lock (_operationSync)
        {
            if(ProductUpdateBlocker?.Invoke() is {} updateBlocker) throw new InvalidOperationException(updateBlocker);
            if (IsLoaded)
            {
                _operation = new EngineOperationStatus(null, EngineOperationState.Completed, "Ready", "The model is already loaded.", null, 0, false);
                return _operation;
            }
            if (_loadTask is { IsCompleted: false }) return OperationStatus;

            var id = Guid.NewGuid().ToString("N");
            _loadCancellation?.Dispose();
            _loadCancellation = new CancellationTokenSource();
            _operation = new EngineOperationStatus(id, EngineOperationState.Running, "Preparing", "Preparing the selected model.", DateTimeOffset.UtcNow, 0, true);
            _loadTask = RunLoadOperationAsync(id, requestedModel??ActiveModel, _loadCancellation.Token);
            return OperationStatus;
        }
    }

    public EngineOperationStatus OperationStatus
    {
        get
        {
            lock (_operationSync)
            {
                var elapsed = _operation.State != EngineOperationState.Running || _operation.StartedAt is null ? _operation.ElapsedSeconds : Math.Max(0, (DateTimeOffset.UtcNow - _operation.StartedAt.Value).TotalSeconds);
                return _operation with { ElapsedSeconds = elapsed };
            }
        }
    }

    public bool CancelLoad()
    {
        lock (_operationSync)
        {
            if (_loadTask is not { IsCompleted: false } || _loadCancellation is null) return false;
            UpdateOperation("Cancelling", "Stopping the current engine load.", false);
            _loadCancellation.Cancel();
            return true;
        }
    }

    public async Task LoadAsync(CancellationToken token = default,ModelCatalogEntry? requestedModel=null)
    {
        StartLoad(requestedModel);
        Task? task;
        lock (_operationSync) task = _loadTask;
        if (task is not null) await task.WaitAsync(token);
        var status = OperationStatus;
        if (status.State == EngineOperationState.Failed) throw new InvalidOperationException(status.Error ?? status.Message);
        if (status.State == EngineOperationState.Cancelled) throw new OperationCanceledException(status.Message, token);
    }

    public async Task EnsureModelAsync(ModelCatalogEntry model, CancellationToken token = default)
    {
        if(!_catalog.IsInstalled(model))throw new FileNotFoundException("The requested model is not installed.",_catalog.ResolvePath(model));
        await _modelSwitchGate.WaitAsync(token);
        try
        {
            var current=Session?.Model ?? ActiveModel;
            if(current is not null&&current.FileName.Equals(model.FileName,StringComparison.OrdinalIgnoreCase))
            {
                await LoadAsync(token,model);
                return;
            }
            if(State!=EngineState.Unloaded||_loadTask is {IsCompleted:false})await UnloadAsync("switching API model");
            _settings.ProfileFor(model);
            _log.Write($"API selected model {_settings.ModelIdFor(model)}.");
            await LoadAsync(token,model);
        }
        finally{_modelSwitchGate.Release();}
    }

    private async Task RunLoadOperationAsync(string operationId,ModelCatalogEntry? selectedModel, CancellationToken token)
    {
        try
        {
            await LoadCoreAsync(selectedModel,token);
            CompleteOperation(operationId, EngineOperationState.Completed, "Ready", "The model is ready.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            CompleteOperation(operationId, EngineOperationState.Cancelled, "Cancelled", "Model loading was cancelled.");
        }
        catch (Exception ex)
        {
            _log.Write("NInfer load operation failed", ex);
            CompleteOperation(operationId, EngineOperationState.Failed, "Failed", "NInfer could not load the model.", StartupDiagnostics.UserMessage(ex.Message));
        }
    }

    private async Task LoadCoreAsync(ModelCatalogEntry? selectedModel,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (IsLoaded) return;
            var model = selectedModel ?? throw new InvalidOperationException("Choose an installed model first.");
            var engineDirectory = _enginePackages?.ActiveDirectory ?? _paths.EngineDirectory;
            await _catalog.ResolveVersionRequirementsAsync(model,token);
            ModelVersionPolicy.Require(model, ModelVersionPolicy.ReadEngineVersion(engineDirectory), includeEngine:true);
            await _catalog.InspectAsync(model, engineDirectory, token);
            var profile = SettingsService.Clone(_settings.ProfileFor(model));
            profile = profile.ManageAdvancedSettings ? ProfileSettingsPolicy.Managed(profile, model.RecommendedProfile()) : ProfileSettingsPolicy.Effective(profile);
            var launchSettings = _settings.Snapshot();
            launchSettings.ApiKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var exe = Path.Combine(engineDirectory, "ninfer-serve.exe");
            var modelPath = _catalog.ResolvePath(model);
            if (!File.Exists(exe)) throw new FileNotFoundException("NInfer runtime is missing.", exe);
            if (!File.Exists(modelPath)) throw new FileNotFoundException("The selected model file is missing. Relink it from Models or choose another model.", modelPath);
            State = EngineState.Loading;
            var sessionId = Guid.NewGuid().ToString("N");
            string? architecture=null,version=null;
            var manifestFile=Path.Combine(engineDirectory,"engine-manifest.json");
            if(File.Exists(manifestFile))
            {
                using var manifest=System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestFile));
                architecture=manifest.RootElement.TryGetProperty("cudaArchitecture",out var arch)?arch.GetString():null;
                version=manifest.RootElement.TryGetProperty("engineVersion",out var v)?v.GetString():null;
            }
            _log.BeginEngineSession();
            if (profile.AutoContext)
                await FitContextAsync(model, profile, launchSettings, exe, engineDirectory, token);
            else
            {
                UpdateOperation("Starting engine", "Starting NInferEZ Engine.");
                await StartEngineAsync(exe, engineDirectory, BuildArguments(model, profile, launchSettings), launchSettings, token);
            }
            token.ThrowIfCancellationRequested();
            Session = new(sessionId, model, launchSettings.ModelAliases.GetValueOrDefault(model.FileName,model.ModelId).Trim(), launchSettings.BackendPort, launchSettings.ApiKey, SettingsService.Clone(profile), engineDirectory,architecture,version);
            if (profile.AutoContext) LastContextFit = new(model.FileName, profile.MaxContext);
            State = EngineState.Ready;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { await KillAsync(); State = EngineState.Unloaded; throw; }
        catch { await KillAsync(); State = EngineState.Error; throw; }
        finally { _gate.Release(); }
    }

    private async Task FitContextAsync(ModelCatalogEntry model, ModelProfile profile, ManagerSettings settings,
        string exe, string engineDirectory, CancellationToken token)
    {
        var ceiling = Math.Min(profile.MaxContext, model.MaximumContext);
        var outputLimit = profile.DefaultMaxTokens;
        profile.KvCapacityMode = KvCapacityMode.Auto;
        UpdateOperation("Fitting context", "Reading available GPU memory.");
        var gpu = await GpuMonitor.ReadAsync(profile.Device, fresh: true).WaitAsync(token);
        var estimate = ModelMemoryFit.EstimateContext(_catalog.Describe(model), gpu, profile, out var bytesPerToken);
        var key = gpu is null ? null : ContextFitHistory.Key(_catalog.ResolvePath(model), exe, gpu, profile,
            model.NativeContext > 0 && ceiling > model.NativeContext);
        var previous = key is null ? null : _fitHistory.Find(key);
        var initial = ContextFitSearch.InitialContext(ceiling, estimate, bytesPerToken, previous, gpu?.AvailableMiB);
        var attempts = 0;
        _log.Write($"Automatic context fit: starting at {initial:N0} tokens (ceiling {ceiling:N0}, free GPU memory {gpu?.AvailableMiB.ToString("N0") ?? "unknown"} MiB).");

        async Task<long?> TryContextAsync(int tokens)
        {
            token.ThrowIfCancellationRequested();
            await KillAsync();
            if (attempts++ > 0 && key is not null)
                gpu = await GpuMonitor.ReadAsync(profile.Device, fresh: true).WaitAsync(token) ?? gpu;
            profile.MaxContext = tokens;
            profile.CustomKvCapacity = tokens;
            profile.DefaultMaxTokens = Math.Min(outputLimit, tokens);
            UpdateOperation("Fitting context", $"Checking {tokens:N0} tokens against available GPU memory.");
            _log.Write($"Automatic context fit: trying {tokens:N0} tokens.");
            try
            {
                await StartEngineAsync(exe, engineDirectory, BuildArguments(model, profile, settings), settings, token);
                return null;
            }
            catch (InvalidOperationException error) when (StartupDiagnostics.IsContextCapacityFailure(error.Message))
            {
                _log.Write($"Automatic context fit: {tokens:N0} tokens did not fit.", error);
                return StartupDiagnostics.ContextCapacityShortfall(error.Message);
            }
        }

        var chosen = await ContextFitSearch.FindAsync(initial, bytesPerToken, TryContextAsync);
        if (chosen == 0)
            throw new InvalidOperationException("Automatic context fitting could not fit the 16,384-token minimum with this profile and the available GPU memory. Full details are in Logs.");
        token.ThrowIfCancellationRequested();
        if (key is not null && gpu is not null && (previous?.Tokens != chosen || attempts > 1))
            _fitHistory.Remember(key, new(chosen, gpu.AvailableMiB));
        _log.Write($"Automatic context fit selected {chosen:N0} tokens after {attempts} load attempt(s) (ceiling {ceiling:N0}).");
    }

    private async Task StartEngineAsync(string exe, string engineDirectory, IReadOnlyList<string> arguments,
        ManagerSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(exe) { WorkingDirectory = engineDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process = process;
        _startupMessages.Clear();
        _startupFailures.Clear();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) RecordEngineLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) RecordEngineLine(e.Data); };
        process.Exited += (_, _) => { if (ReferenceEquals(_process, process) && State == EngineState.Ready) State = EngineState.Unloaded; };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Windows could not start NInfer.");
        }
        catch
        {
            _process = null;
            process.Dispose();
            throw;
        }
        _processJob.Assign(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
        var deadline = DateTime.UtcNow.AddMinutes(15);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                await process.WaitForExitAsync(token);
                throw new InvalidOperationException($"NInfer exited during startup with code {process.ExitCode}. {StartupDetail()}");
            }
            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{settings.BackendPort}/v1/models", token);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new InvalidOperationException("The engine rejected its private readiness credential.");
                if (response.IsSuccessStatusCode && !process.HasExited) return;
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            await Task.Delay(400, token);
        }
        throw new TimeoutException($"NInfer did not become ready within 15 minutes. {StartupDetail()}");
    }
    public async Task UnloadAsync(string reason)
    {
        CancelLoad();
        await _gate.WaitAsync();
        try { State = EngineState.Unloading; _log.Write("Unloading NInfer: " + reason); await KillAsync(); State = EngineState.Unloaded; }
        finally { _gate.Release(); }
    }
    public async Task RestartAsync(CancellationToken token = default) { await UnloadAsync("restart"); await LoadAsync(token); }
    private async Task KillAsync()
    {
        var p = _process; _process = null;
        try
        {
            if (p is { HasExited: false })
            {
                p.Kill(true);
                await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            Session = null;
        }
        finally { p?.Dispose(); }
    }
    private void RecordEngineLine(string line)
    {
        _log.Write("NInfer: " + line);
        _startupMessages.Enqueue(line);
        while (_startupMessages.Count > 12) _startupMessages.TryDequeue(out _);
        if (StartupDiagnostics.IsFailure(line))
        {
            _startupFailures.Enqueue(line);
            while (_startupFailures.Count > 8) _startupFailures.TryDequeue(out _);
        }
        if (line.Contains("calibrat", StringComparison.OrdinalIgnoreCase)) UpdateOperation("Calibrating", "Calibrating optimized routes for this GPU. First launch can take several minutes.");
        else if (line.Contains("load", StringComparison.OrdinalIgnoreCase) && line.Contains("model", StringComparison.OrdinalIgnoreCase)) UpdateOperation("Loading model", "Loading model weights into memory.");
        else if (line.Contains("cuda", StringComparison.OrdinalIgnoreCase) || line.Contains("kernel", StringComparison.OrdinalIgnoreCase)) UpdateOperation("Preparing GPU", "Preparing optimized GPU kernels.");
    }
    private void UpdateOperation(string phase, string message, bool canCancel = true)
    {
        lock (_operationSync)
        {
            if (_operation.State != EngineOperationState.Running) return;
            _operation = _operation with { Phase = phase, Message = message, CanCancel = canCancel };
        }
    }
    private void CompleteOperation(string operationId, EngineOperationState state, string phase, string message, string? error = null)
    {
        lock (_operationSync)
        {
            if (!string.Equals(_operation.OperationId, operationId, StringComparison.Ordinal)) return;
            var elapsed = _operation.StartedAt is null ? 0 : Math.Max(0, (DateTimeOffset.UtcNow - _operation.StartedAt.Value).TotalSeconds);
            _operation = _operation with { State = state, Phase = phase, Message = message, ElapsedSeconds = elapsed, CanCancel = false, Error = error };
        }
    }
    private static EngineOperationStatus IdleOperation() => new(null, EngineOperationState.None, "Idle", "No engine operation is running.", null, 0, false);
    private string StartupDetail() => StartupDiagnostics.Summarize(_startupFailures.IsEmpty ? _startupMessages : _startupFailures);
    private static string I(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    private static void Add(List<string> a, string n, object v) { a.Add(n); a.Add(I(v)); }
    public string BuildCommandPreview()
    {
        var args = BuildArguments().ToArray();
        for (var i = 0; i < args.Length - 1; i++) if (args[i] == "--api-key") args[i + 1] = "[redacted]";
        return string.Join(" ", new[] { "ninfer-serve.exe" }.Concat(args).Select(Quote));
    }
    private static string Quote(string value) => value.Any(char.IsWhiteSpace) ? '"' + value.Replace("\"", "\\\"") + '"' : value;
    public async ValueTask DisposeAsync() { await UnloadAsync("backend exit"); _loadCancellation?.Dispose(); _processJob.Dispose(); _modelSwitchGate.Dispose(); _gate.Dispose(); LifecycleGate.Dispose(); }
}

public sealed record LoadedEngineSession(string Id, ModelCatalogEntry Model, string ModelId, int Port, string ApiKey, ModelProfile Profile, string EngineDirectory,string? Architecture=null,string? EngineVersion=null);
