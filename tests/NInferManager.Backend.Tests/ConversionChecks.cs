using System.Text.Json;
using NInferManager.Backend;
using NInferManager.Contracts;

internal static class ConversionChecks
{
    public static async Task RunAsync(AppPaths paths,ManagerLog log,Action<bool,string> check)
    {
        var root=Path.Combine(paths.DataDirectory,"converter-fixtures");Directory.CreateDirectory(root);
        var source=Path.Combine(root,"source.gguf");File.WriteAllText(source,"fixture");
        var request=new ConversionRequest{Source=source,Output=Path.Combine(root,"output.ninfer")};
        check(!ConversionService.ValidateRequest(request).Contains('\n'),"converter request is one compact NDJSON line, not indented settings JSON");
        var runner=new FakeRunner();var service=new ConversionService(runner,log);
        await service.StartAsync(default);
        check(runner.Calls==0&&!service.Snapshot().Running,"converter is lazy and does not start Python or touch GPU on Manager startup");
        var caps=await service.CapabilitiesAsync();await service.CapabilitiesAsync();
        check(caps.Devices.SequenceEqual(new[]{"cpu"})&&runner.Calls==1,"converter capabilities are cached and only reported CPU is offered");
        void Reject(ConversionRequest value,string message)
        {bool rejected=false;try{ConversionService.ValidateRequest(value);}catch(ArgumentException){rejected=true;}check(rejected,message);}
        Reject(request with{Source=""},"converter rejects missing source with a user-actionable error");
        Reject(request with{Output=source},"converter never accepts the source as output");
        Reject(request with{Output=source+".txt"},"converter requires a new NInfer output entry");
        Reject(request with{Device="cuda"},"CPU distribution does not claim CUDA conversion");
        Reject(request with{RowsPerChunk=0},"converter rejects invalid chunk rows");
        Reject(request with{ValidateTimeoutSeconds=0},"engine test timeout has an actionable valid range");
        Reject(request with{TensorOverrides=[new(){Selector=""}]},"manual per-weight assignments require a selector");
        check(ConversionService.ValidateRequest(request with{Output="",Device="auto"},false).Contains("auto"),"wizard can discover required files without an output and Auto stays CPU-only");
        var manual=request with{ConfigFile=source,ResourceFolder=root,ShardOverrides=new(){[source]=source},TensorOverrides=[new(){Selector="text/layers/*/mlp/down",Format="q4_g64_fp16"}]};
        check(ConversionService.ValidateRequest(manual).Contains("tensorOverrides")&&ConversionService.ValidateRequest(manual).Contains("shardOverrides"),"manual resource, shard and per-weight settings survive Manager serialization");
        Reject(request with{Components=["vision"]},"converter requires the text component");
        Reject(request with{Components=["text","mtp","mtp"]},"converter rejects duplicate components");
        Reject(request with{Profile="recipe",Recipe="custom.py"},"executable Python recipes require explicit trust");
        check(ConversionService.ValidateRequest(request with{Profile="recipe",Recipe="custom.py",AllowCustomCode=true}).Contains("custom.py"),"trusted custom recipes remain available without shell command construction");
        Reject(request with{ValidateEngine=source},"optional engine test requires a matching executable name");
        File.WriteAllText(request.Output,"existing");Reject(request,"existing output cannot be replaced");File.Delete(request.Output);
        bool preflight=false;try{service.Start("convert",request);}catch(InvalidOperationException){preflight=true;}
        check(preflight,"conversion requires a successful check for the same settings");
        service.Start("probe",request with{Output=""});await Wait(service);
        check(service.Snapshot().Phase=="Scanned"&&!service.Snapshot().CanConvert&&service.Snapshot().Plan!.Contains("requirements"),"wizard probe returns a file checklist without authorizing conversion");
        service.Start("analyze",request with{Output=""});await Wait(service);
        check(service.Snapshot().Phase=="Options ready"&&!service.Snapshot().CanConvert&&service.Snapshot().Plan!.Contains("options"),"wizard analysis exposes actual alternatives without choosing or authorizing one");
        service.Start("inspect",request);await Wait(service);
        check(service.Snapshot().CanConvert&&service.Snapshot().Plan!.Contains("architecture"),"compatibility plan enables conversion and exposes real plan metadata");
        bool changed=false;try{service.Start("convert",request with{Profile="q4"});}catch(InvalidOperationException){changed=true;}
        check(changed,"changed settings invalidate checked conversion authorization");
        runner.Hold=true;service.Start("convert",request);await runner.Started.Task;
        bool duplicate=false;try{service.Start("inspect",request);}catch(InvalidOperationException){duplicate=true;}
        check(duplicate,"only one compatibility/conversion job runs at a time");
        check(service.Snapshot().WrittenBytes==100&&service.Snapshot().Percent==25,"NDJSON byte progress is forwarded without invented speed/progress");
        check(service.Cancel()&&service.Snapshot().Cancelling,"cancel requests graceful cleanup and keeps running until completion");
        await Wait(service);
        check(service.Snapshot().Phase=="Cancelled"&&!service.Snapshot().CanConvert,"cancelled job cannot be published or reused as an unchecked conversion");
        check(!service.Cancel(),"cancel is unavailable when no job exists");
        runner.Hold=false;runner.Fail=true;service.Start("inspect",request);await Wait(service);
        check(service.Snapshot().Message.Contains("matching config")&&!service.Snapshot().CanConvert,"converter failure keeps the actionable source/resource message rather than a raw exit code");
        runner.Fail=false;service.Start("inspect",request);await Wait(service);service.Start("convert",request);await Wait(service);
        check(service.Snapshot().Phase=="Complete"&&service.Snapshot().Output==request.Output&&!service.Snapshot().RuntimeVerified,"completed output is offered for linking without claiming GPU validation");
        check(runner.GuardedConversion,"conversion receives the checked plan signature and file snapshot internally");
        await service.StopAsync(default);
        bool closing=false;try{service.Start("inspect",request);}catch(InvalidOperationException){closing=true;}
        check(closing,"closing Manager rejects new conversion jobs");
    }
    private static async Task Wait(ConversionService service)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while(service.Snapshot().Running)await Task.Delay(10,timeout.Token);
    }
    private sealed class FakeRunner:IConverterRunner
    {
        public int Calls;public bool Hold;public bool Fail;public bool GuardedConversion;
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<int> RunAsync(string command,string request,Action<JsonElement> onEvent,Action<string> diagnostic,CancellationToken cancellation)
        {
            Calls++;void Emit(string json){using var doc=JsonDocument.Parse(json);onEvent(doc.RootElement.Clone());}
            if(command=="capabilities") {Emit("""{"type":"capabilities","version":"0.1.0","profiles":["auto"],"recipes":[],"components":["text"],"inputs":["GGUF"],"devices":["cpu"]}""");return 0;}
            if(command=="probe"){Emit("""{"type":"result","success":true,"ready":true,"requirements":[],"fileState":{},"message":"Ready"}""");return 0;}
            if(command=="analyze"){Emit("""{"type":"result","success":true,"options":[{"profile":"auto","supported":true}],"message":"Select a plan"}""");return 0;}
            Emit("""{"type":"stage","message":"Checking source"}""");
            if(Fail){Emit("""{"type":"error","message":"Choose the matching config/tokenizer folder."}""");return 1;}
            if(Hold)
            {
                Emit("""{"type":"progress","percent":25,"writtenBytes":100,"totalBytes":400}""");Started.TrySetResult();
                try{await Task.Delay(Timeout.Infinite,cancellation);}catch(OperationCanceledException){}
                Emit("""{"type":"cancelled"}""");return 2;
            }
            if(command=="inspect")Emit("""{"type":"result","success":true,"architecture":"Qwen3_5ForCausalLM","encodedBytes":400,"planSignature":"fixture-plan","runtimeVerified":false}""");
            else
            {
                using var doc=JsonDocument.Parse(request);var output=doc.RootElement.GetProperty("output").GetString();
                GuardedConversion=doc.RootElement.TryGetProperty("expectedPlanSignature",out var signature)&&signature.GetString()=="fixture-plan"&&doc.RootElement.TryGetProperty("expectedFileState",out _);
                onEvent(JsonSerializer.SerializeToElement(new{type="result",success=true,output,report=output+".conversion.json",runtimeVerified=false}));
            }
            return 0;
        }
    }
}
