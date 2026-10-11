using System.Text.Json;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public sealed class ConversionService(IConverterRunner runner, ManagerLog log) : IHostedService
{
    private static readonly JsonSerializerOptions WireJson=new(SettingsService.JsonOptions){WriteIndented=false};
    private readonly object _gate=new();
    private readonly SemaphoreSlim _capabilityGate=new(1,1);
    private ConversionCapabilities? _capabilities;
    private ConversionStatus _status=new("","",false,false,"Ready",null,0,0,"Choose a source to begin.",false,null,null,null,false);
    private string? _checkedRequest;
    private string? _planSignature;
    private JsonElement? _fileState;
    private CancellationTokenSource? _cancellation;
    private Task _job=Task.CompletedTask;
    private bool _stopping;
    public ConversionStatus Snapshot() { lock(_gate) return _status; }

    public async Task<ConversionCapabilities> CapabilitiesAsync(CancellationToken token=default)
    {
        await _capabilityGate.WaitAsync(token);
        try
        {
            if(_capabilities is not null) return _capabilities;
            ConversionCapabilities? value=null;
            string? error=null;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var code=await runner.RunAsync("capabilities","{}",e=>
            {
                if(Type(e)=="capabilities") value=e.Deserialize<ConversionCapabilities>(SettingsService.JsonOptions);
                else if(Type(e)=="error") error=Text(e,"message");
            },s=>log.Write("Converter: "+s),timeout.Token);
            if(code!=0 || value is null) throw new InvalidOperationException(error??"The conversion runtime did not report its capabilities. Check the converter files and manager log.");
            return _capabilities=value;
        }
        finally { _capabilityGate.Release(); }
    }

    public ConversionStatus Start(string command,ConversionRequest request)
    {
        if(command is not ("probe" or "analyze" or "inspect" or "convert")) throw new ArgumentException("Choose Scan, Check compatibility or Convert.");
        var serialized=ValidateRequest(request,command is "inspect" or "convert");
        lock(_gate)
        {
            if(_stopping) throw new InvalidOperationException("Manager is closing. Reopen it before starting a conversion.");
            if(_status.Running) throw new InvalidOperationException("A conversion or compatibility check is already running. Wait or cancel it first.");
            if(command=="convert" && serialized!=_checkedRequest)
                throw new InvalidOperationException("Check compatibility again after changing the source or conversion settings.");
            var workerRequest=serialized;
            if(command=="convert" && _planSignature is {Length:>0})
            {
                var guarded=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(serialized)!;
                guarded["expectedPlanSignature"]=JsonSerializer.SerializeToElement(_planSignature);
                if(_fileState is { } state)guarded["expectedFileState"]=state;
                workerRequest=JsonSerializer.Serialize(guarded,WireJson);
            }
            _checkedRequest=null;
            _planSignature=null;
            _fileState=null;
            _cancellation?.Dispose();
            _cancellation=new();
            _status=new(Guid.NewGuid().ToString("N"),command,true,false,"Starting",null,0,0,
                command=="inspect"?"Checking the real conversion plan…":"Preparing conversion…",false,null,null,null,false);
            _job=Task.Run(()=>ExecuteAsync(command,serialized,workerRequest,_cancellation.Token));
            return _status;
        }
    }

    public bool Cancel()
    {
        lock(_gate)
        {
            if(!_status.Running) return false;
            _status=_status with{Cancelling=true,Message="Cancelling safely; waiting for temporary output cleanup…"};
            _cancellation?.Cancel();
            return true;
        }
    }

    private async Task ExecuteAsync(string command,string request,string workerRequest,CancellationToken token)
    {
        bool success=false; string? error=null; bool cancelled=false; string? plan=null;
        try
        {
            // Snapshot resource timestamps before inspection; the existing converter
            // rechecks these and the exact prepared plan before publishing anything.
            if(command=="inspect")
            {
                var probeExit=await runner.RunAsync("probe",request,e=>
                {
                    if(Type(e)=="result"&&e.TryGetProperty("fileState",out var state))_fileState=state.Clone();
                    if(Type(e)=="error")error=ReadableError(Text(e,"message"));
                },s=>log.Write("Converter: "+s),token);
                token.ThrowIfCancellationRequested();
                if(probeExit!=0)throw new InvalidOperationException(error??"Scan the source and attach its matching files before checking the plan.");
            }
            var exit=await runner.RunAsync(command,workerRequest,e=>
            {
                lock(_gate)
                {
                    switch(Type(e))
                    {
                        case "stage": _status=_status with{Phase=Text(e,"message"),Message=Text(e,"message")}; break;
                        case "progress": _status=_status with{Percent=Number(e,"percent"),WrittenBytes=Long(e,"writtenBytes"),TotalBytes=Long(e,"totalBytes")}; break;
                        case "compatibility": plan=e.GetRawText(); break;
                        case "result":
                            success=e.TryGetProperty("success",out var s)&&s.ValueKind==JsonValueKind.True;
                            if(command!="convert") plan=e.GetRawText();
                            if(command=="inspect")
                            {
                                _planSignature=Optional(e,"planSignature");
                                if(_fileState is { } checkedFiles)
                                {
                                    var details=e.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
                                    details["fileState"]=checkedFiles;
                                    plan=JsonSerializer.Serialize(details,WireJson);
                                }
                            }
                            _status=_status with{Output=Optional(e,"output"),Report=Optional(e,"report"),RuntimeVerified=e.TryGetProperty("runtimeVerified",out var r)&&r.ValueKind==JsonValueKind.True};
                            break;
                        case "cancelled": cancelled=true; break;
                        case "error": error=ReadableError(Text(e,"message")); log.Write("Converter error: "+Text(e,"message")); break;
                    }
                }
            },s=>log.Write("Converter: "+s),token);
            lock(_gate)
            {
                if(cancelled || exit==2) _status=_status with{Running=false,Cancelling=false,Phase="Cancelled",Message="Cancelled. No incomplete model was published.",CanConvert=false};
                else if(exit!=0 || !success || error is not null) Fail(error??"The converter stopped before completing. Check the manager log.");
                else
                {
                    if(command=="inspect") _checkedRequest=request;
                    _status=_status with{Running=false,Cancelling=false,Phase=command switch{"inspect"=>"Compatible","probe"=>"Scanned","analyze"=>"Options ready",_=>"Complete"},CanConvert=command=="inspect",Plan=plan,
                        Message=command switch{"inspect"=>"Compatibility check passed. Review the plan, then convert.","probe" or "analyze"=>PlanMessage(plan),_=>"Conversion complete. Structural validation passed; GPU execution is not verified unless you selected an engine test."}};
                }
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { lock(_gate)_status=_status with{Running=false,Cancelling=false,Phase="Cancelled",CanConvert=false,Message="Cancelled. No incomplete model was published."}; }
        catch(Exception e) { log.Write("Conversion job failed",e); lock(_gate) Fail(e.Message); }
    }

    private void Fail(string message) { _checkedRequest=null; _status=_status with{Running=false,Cancelling=false,Phase="Failed",CanConvert=false,Message=message}; }
    private static string ReadableError(string message)=>message.Contains("unpack_from requires a buffer",StringComparison.OrdinalIgnoreCase)
        ?"The model file is incomplete or has an invalid header. Select a complete supported GGUF/Safetensors/NInfer file; download it again if necessary. Your source was not changed."
        :message;
    private static string PlanMessage(string? plan)
    {using var doc=JsonDocument.Parse(plan??"{}");return Optional(doc.RootElement,"message")??"Scan finished. Review the required files and options.";}
    public static string ValidateRequest(ConversionRequest request,bool requireOutput=true)
    {
        if(request.Components is null || request.Sources is null || request.Resources is null || request.ShardOverrides is null || request.TensorOverrides is null)
            throw new ArgumentException("Components, companion sources and resources must be valid lists/maps.");
        if(string.IsNullOrWhiteSpace(request.Source)||(!File.Exists(request.Source)&&!Directory.Exists(request.Source)))
            throw new ArgumentException("Choose an existing model file or checkpoint folder.");
        if(requireOutput&&(string.IsNullOrWhiteSpace(request.Output)||!Path.GetExtension(request.Output).Equals(".ninfer",StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose a new output filename ending in .ninfer.");
        if(requireOutput&&(File.Exists(request.Output)||Directory.Exists(request.Output)||File.Exists(request.Output+".conversion.json")||Path.GetFullPath(request.Source).Equals(Path.GetFullPath(request.Output),StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("That output already exists or is the source. Choose another filename; existing files are never replaced.");
        if(request.Device is not ("cpu" or "auto")) throw new ArgumentException("The bundled conversion runtime is CPU-only. Choose CPU or Auto; inference still uses your selected GPU engine.");
        if(!new[]{"auto","q4","q5","q6","q8","bf16","fp8","preserve","recipe"}.Contains(request.Profile)) throw new ArgumentException("Choose an output profile reported by the converter.");
        if(request.RowsPerChunk is <1 or >65536 || request.MaxFileBytes<8192 || request.ProposalRows<1)
            throw new ArgumentException("Chunk rows must be 1–65536, shard size at least 8192 bytes, and proposal rows positive.");
        if(!request.Components.Contains("text")||request.Components.Distinct().Count()!=request.Components.Length||request.Components.Any(x=>!new[]{"text","vision","mtp","dflash","dflash2"}.Contains(x)))
            throw new ArgumentException("Keep Text selected and choose only supported optional components.");
        if(requireOutput&&request.Profile=="recipe"&&string.IsNullOrWhiteSpace(request.Recipe)) throw new ArgumentException("Choose a built-in recipe or your own Python recipe.");
        if(request.ValidateTimeoutSeconds is <1 or >86400)throw new ArgumentException("Engine test timeout must be between 1 and 86400 seconds.");
        foreach(var row in request.TensorOverrides)
        {
            if(row is null||string.IsNullOrWhiteSpace(row.Selector)||row.Parameters is null)throw new ArgumentException("Each per-weight assignment needs a parameter/wildcard selector and valid parameters JSON.");
            if(row.ActivationPolicy is not ("" or "A16Only" or "AllowA8" or "AllowA4"))throw new ArgumentException("Choose a supported activation policy for each assignment.");
        }
        if(!request.AllowCustomCode && (request.Override is {Length:>0} || request.Recipe?.Contains(".py",StringComparison.OrdinalIgnoreCase)==true))
            throw new ArgumentException("Custom Python recipes run code with your file access. Confirm that you trust the recipe before checking or converting.");
        if(request.ValidateEngine is {Length:>0} && (!File.Exists(request.ValidateEngine)||!Path.GetFileName(request.ValidateEngine).Equals("ninfer-serve.exe",StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("For the optional GPU test, choose a matching ninfer-serve.exe, or leave it blank for structural checks only.");
        string? Full(string? path)=>string.IsNullOrWhiteSpace(path)?null:Path.GetFullPath(path);
        string? RecipePath(string? recipe)
        {
            if(recipe is null || !recipe.Contains(".py",StringComparison.OrdinalIgnoreCase))return recipe;
            var end=recipe.IndexOf(".py",StringComparison.OrdinalIgnoreCase)+3;
            return Path.GetFullPath(recipe[..end])+recipe[end..];
        }
        var normalized=request with{Source=Path.GetFullPath(request.Source),Output=Full(request.Output)??"",
            ConfigFolder=Full(request.ConfigFolder),ValidateEngine=Full(request.ValidateEngine),Ranking=Full(request.Ranking),
            ConfigFile=Full(request.ConfigFile),ResourceFolder=Full(request.ResourceFolder),
            Recipe=RecipePath(request.Recipe),Override=RecipePath(request.Override),
            Sources=request.Sources.Where(x=>!string.IsNullOrWhiteSpace(x.Value)).OrderBy(x=>x.Key).ToDictionary(x=>x.Key,x=>Path.GetFullPath(x.Value)),
            Resources=request.Resources.Where(x=>!string.IsNullOrWhiteSpace(x.Value)).OrderBy(x=>x.Key).ToDictionary(x=>x.Key,x=>Path.GetFullPath(x.Value)),
            ShardOverrides=request.ShardOverrides.OrderBy(x=>x.Key).ToDictionary(x=>Path.GetFullPath(x.Key),x=>Path.GetFullPath(x.Value))};
        return JsonSerializer.Serialize(normalized,WireJson);
    }
    private static string Type(JsonElement e)=>Text(e,"type");
    private static string Text(JsonElement e,string name)=>Optional(e,name)??"";
    private static string? Optional(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    private static double? Number(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.TryGetDouble(out var n)&&double.IsFinite(n)?Math.Clamp(n,0,100):null;
    private static long Long(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.TryGetInt64(out var n)?n:0;
    public Task StartAsync(CancellationToken token)=>Task.CompletedTask;
    public async Task StopAsync(CancellationToken token) { lock(_gate) _stopping=true; Cancel(); await _job.WaitAsync(TimeSpan.FromSeconds(35),CancellationToken.None); }
}
