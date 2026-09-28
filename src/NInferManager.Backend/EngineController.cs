using System.Diagnostics;
using System.Globalization;
using System.Collections.Concurrent;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public sealed class EngineController : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly ModelCatalog _catalog;
    private readonly ManagerLog _log;
    private readonly EnginePackageService? _enginePackages;
    private readonly ProcessJob _processJob=new();
    private Process? _process;
    private readonly ConcurrentQueue<string> _startupMessages = new();
    private readonly object _operationSync = new();
    private CancellationTokenSource? _loadCancellation;
    private Task? _loadTask;
    private EngineOperationStatus _operation = IdleOperation();
    public EngineController(AppPaths paths, SettingsService settings, ModelCatalog catalog, ManagerLog log, EnginePackageService? enginePackages = null) { _paths = paths; _settings = settings; _catalog = catalog; _log = log; _enginePackages = enginePackages; }
    public EngineState State { get; private set; }
    public bool IsLoaded => State == EngineState.Ready && _process is { HasExited: false };
    public ModelCatalogEntry? ActiveModel => _catalog.Find(_settings.Current.ActiveModelFile);
    public int Port => _settings.Current.BackendPort;

    public IReadOnlyList<string> BuildArguments()
    {
        var model = ActiveModel ?? throw new InvalidOperationException("Choose an installed model first.");
        var path = _catalog.ResolvePath(model);
        var p = _settings.ProfileFor(model);
        if (p.MaxContext > model.MaximumContext)
            throw new InvalidOperationException($"{model.DisplayName} supports at most {model.MaximumContext:N0} context tokens in this manager profile.");
        if (p.DefaultMaxTokens > p.MaxContext)
            throw new InvalidOperationException("The default output-token limit cannot exceed the selected context window.");
        if (model.ModelId == "qwen3.8-27b-orcarouter-iq3-xxs" && p.SpeculativeMode is SpeculativeMode.Dflash or SpeculativeMode.Dflash2)
            throw new InvalidOperationException("This IQ3_XXS artifact contains MTP weights but no DFlash/DFlash2 drafter. Select MTP or Disabled.");
        var args = new List<string> { path, "--model-id", _settings.ModelIdFor(model), "--max-context", I(p.MaxContext), "--default-max-tokens", I(p.DefaultMaxTokens), "--kv-dtype", p.KvPrecision.ToString().ToLowerInvariant(), "--device", I(p.Device), "--max-concurrency", I(p.MaxConcurrency), "--max-pending-requests", I(p.MaxPendingRequests), "--pending-timeout-ms", I(p.PendingTimeoutMs), "--max-request-mib", I(p.MaxRequestMiB), "--log-stats-interval-ms", I(p.LogStatsIntervalMs), "--media-cache-mib", I(p.MediaCacheMiB), "--media-live-mib", I(p.MediaLiveMiB), "--media-preprocess-threads", I(p.MediaPreprocessThreads), "--device-state-slots", I(p.DeviceStateSlots), "--host-state-slots", I(p.HostStateSlots), "--host-kv-mib", I(p.HostKvMiB), "--response-store-max-records", I(p.ResponseStoreMaxRecords), "--response-store-max-mib", I(p.ResponseStoreMaxMiB), "--prefill-chunk", I(p.PrefillChunk), "--host", "127.0.0.1", "--port", I(Port) };
        if (p.KvCapacityMode == KvCapacityMode.MatchContext) Add(args, "--kv-capacity", p.MaxContext);
        if (p.KvCapacityMode == KvCapacityMode.Auto) Add(args, "--kv-capacity", "auto");
        if (p.KvCapacityMode == KvCapacityMode.Custom) Add(args, "--kv-capacity", p.CustomKvCapacity);
        if (p.SpeculativeMode != SpeculativeMode.Disabled) { Add(args, "--spec", p.SpeculativeMode.ToString().ToLowerInvariant()); Add(args, "--draft-tokens", p.DraftTokens); }
        if (p.LmHeadDraft) args.Add("--lm-head-draft");
        if (p.VisionEnabled && model.Vision) args.Add("--vision");
        if (!p.CudaGraphEnabled) args.Add("--no-cuda-graph");
        if (!p.PrefixReuseEnabled) args.Add("--no-prefix-reuse");
        if (p.GdnStateFp16) args.Add("--gdn-state-fp16");
        if (p.MtpAttentionWindow > 0) Add(args, "--mtp-attention-window", p.MtpAttentionWindow);
        if (p.MaxContext > model.NativeContext) args.Add("--rope-yarn");
        if (!p.ThinkingEnabled) args.Add("--no-thinking");
        if (p.PreserveThinking) args.Add("--preserve-thinking");
        if (p.MaxPrivateContinuations >= 0) Add(args, "--max-private-continuations", p.MaxPrivateContinuations);
        if (p.MaxSharedPrefixes >= 0) Add(args, "--max-shared-prefixes", p.MaxSharedPrefixes);
        if (p.MaxLongAnchorsPerContinuation >= 0) Add(args, "--max-long-anchors-per-continuation", p.MaxLongAnchorsPerContinuation);
        // Retain the setting in the contract for configuration-file compatibility.
        // NInfer 0.8.0 removed the corresponding CLI option, so it must not be emitted.
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
        if (_settings.Current.CorsEnabled) args.Add("--cors");
        if (!string.IsNullOrWhiteSpace(_settings.Current.ApiKey)) Add(args, "--api-key", _settings.Current.ApiKey);
        return args;
    }

    public EngineOperationStatus StartLoad()
    {
        lock (_operationSync)
        {
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
            _loadTask = RunLoadOperationAsync(id, _loadCancellation.Token);
            return OperationStatus;
        }
    }

    public EngineOperationStatus OperationStatus
    {
        get
        {
            lock (_operationSync)
            {
                var elapsed = _operation.StartedAt is null ? _operation.ElapsedSeconds : Math.Max(0, (DateTimeOffset.UtcNow - _operation.StartedAt.Value).TotalSeconds);
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

    public async Task LoadAsync(CancellationToken token = default)
    {
        StartLoad();
        Task? task;
        lock (_operationSync) task = _loadTask;
        if (task is not null) await task.WaitAsync(token);
        var status = OperationStatus;
        if (status.State == EngineOperationState.Failed) throw new InvalidOperationException(status.Error ?? status.Message);
        if (status.State == EngineOperationState.Cancelled) throw new OperationCanceledException(status.Message, token);
    }

    private async Task RunLoadOperationAsync(string operationId, CancellationToken token)
    {
        try
        {
            await LoadCoreAsync(token);
            CompleteOperation(operationId, EngineOperationState.Completed, "Ready", "The model is ready.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            CompleteOperation(operationId, EngineOperationState.Cancelled, "Cancelled", "Model loading was cancelled.");
        }
        catch (Exception ex)
        {
            _log.Write("NInfer load operation failed", ex);
            CompleteOperation(operationId, EngineOperationState.Failed, "Failed", "NInfer could not load the model.", ex.Message);
        }
    }

    private async Task LoadCoreAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (IsLoaded) return;
            var model = ActiveModel ?? throw new InvalidOperationException("Choose an installed model first.");
            var profile = _settings.ProfileFor(model);
            if (profile.AutoContext)
            {
                UpdateOperation("Detecting hardware", "Selecting a context size for the detected GPU.");
                var gpu = await GpuMonitor.ReadAsync();
                if (gpu is null) throw new InvalidOperationException("A supported NVIDIA GPU was not detected.");
                var recommended = RecommendContext(model, gpu);
                if (profile.MaxContext != recommended)
                {
                    profile.MaxContext = recommended;
                    profile.CustomKvCapacity = recommended;
                    _settings.Save();
                    _log.Write($"Auto context selected {recommended:N0} tokens for {gpu.Name} ({gpu.TotalMiB:N0} MiB VRAM)." );
                }
            }
            var engineDirectory = _enginePackages?.ActiveDirectory ?? _paths.EngineDirectory;
            var exe = Path.Combine(engineDirectory, "ninfer-serve.exe");
            var modelPath = _catalog.ResolvePath(model);
            if (!File.Exists(exe)) throw new FileNotFoundException("NInfer runtime is missing.", exe);
            if (!File.Exists(modelPath)) throw new FileNotFoundException("The selected model file is missing. Relink it from Models or choose another model.", modelPath);
            State = EngineState.Loading;
            UpdateOperation("Starting engine", "Starting NInferEZ Engine.");
            var info = new ProcessStartInfo(exe) { WorkingDirectory = engineDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in BuildArguments()) info.ArgumentList.Add(arg);
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            while (_startupMessages.TryDequeue(out _)) { }
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) RecordEngineLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) RecordEngineLine(e.Data); };
            process.Exited += (_, _) => { if (State is EngineState.Loading or EngineState.Ready) State = EngineState.Unloaded; };
            if (!process.Start()) throw new InvalidOperationException("Windows could not start NInfer.");
            _processJob.Assign(process);
            _process = process; process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };
            var deadline = DateTime.UtcNow.AddMinutes(15);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException($"NInfer exited during startup with code {process.ExitCode}. {StartupDetail()}");
                try { if ((await client.GetAsync($"http://127.0.0.1:{Port}/v1/models", token)).IsSuccessStatusCode) { State = EngineState.Ready; return; } } catch { }
                await Task.Delay(150, token);
            }
            throw new TimeoutException($"NInfer did not become ready within 15 minutes. {StartupDetail()}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { State = EngineState.Unloaded; Kill(); throw; }
        catch { State = EngineState.Error; Kill(); throw; }
        finally { _gate.Release(); }
    }
    public async Task UnloadAsync(string reason)
    {
        CancelLoad();
        await _gate.WaitAsync();
        try { State = EngineState.Unloading; _log.Write("Unloading NInfer: " + reason); Kill(); State = EngineState.Unloaded; }
        finally { _gate.Release(); }
    }
    public async Task RestartAsync(CancellationToken token = default) { await UnloadAsync("restart"); await LoadAsync(token); }
    private void Kill() { var p = _process; _process = null; try { if (p is { HasExited: false }) p.Kill(true); } catch { } finally { p?.Dispose(); } }
    private void RecordEngineLine(string line)
    {
        _log.Write("NInfer: " + line);
        _startupMessages.Enqueue(line);
        while (_startupMessages.Count > 12) _startupMessages.TryDequeue(out _);
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
    private string StartupDetail() => _startupMessages.IsEmpty ? "Open the manager log for startup details." : string.Join(" | ", _startupMessages.TakeLast(3));
    private static string I(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    private static int RecommendContext(ModelCatalogEntry model, GpuStatus gpu)
    {
        var available = Math.Max(0, gpu.TotalMiB - Math.Max(gpu.UsedMiB, 1536));
        var baseMiB = model.BaseVramMiB > 0 ? model.BaseVramMiB : (int)Math.Ceiling(model.SizeBytes / 1024d / 1024d) + 1024;
        var tiers = new[] { 524288, 262144, 200000, 131072, 65536, 32768 };
        return tiers.Where(x => x <= model.MaximumContext)
            .FirstOrDefault(x => baseMiB + model.KvMiBPerKToken * (x / 1000d) <= available, 32768);
    }
    private static void Add(List<string> a, string n, object v) { a.Add(n); a.Add(I(v)); }
    public string BuildCommandPreview() => string.Join(" ", new[] { "ninfer-serve.exe" }.Concat(BuildArguments()).Select(Quote));
    private static string Quote(string value) => value.Any(char.IsWhiteSpace) ? '"' + value.Replace("\"", "\\\"") + '"' : value;
    public async ValueTask DisposeAsync() { await UnloadAsync("backend exit"); _processJob.Dispose(); _gate.Dispose(); }
}
