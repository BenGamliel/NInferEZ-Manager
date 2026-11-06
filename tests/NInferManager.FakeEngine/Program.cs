using System.Text.Json;
using Microsoft.Extensions.Logging;

string Value(string name,string fallback)
{
    var index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length?args[index+1]:fallback;
}
if(args.Contains("--json"))
{
    var file=Value("--model","");
    Console.WriteLine(JsonSerializer.Serialize(new{valid=true,containerVersion=3,fileBytes=new FileInfo(file).Length,
        modelFeatures=new{vision=false,mtp=false,dflash=false,dflash2=false},compatibility=new{compatibleWithBuild=true},
        weightFormats=new[]{"CPU test fixture"},components=Array.Empty<object>()}));
    return 0;
}
var port=int.Parse(Value("--port","48174"));var key=Value("--api-key","");var model=Value("--model-id","fake-model");
if(Environment.GetEnvironmentVariable("NINFEREZ_FAKE_CRASH")=="1")return 23;
if(int.TryParse(Environment.GetEnvironmentVariable("NINFEREZ_FAKE_CONTEXT_LIMIT"),out var contextLimit)
    && int.Parse(Value("--max-context","8192"))>contextLimit)
{
    await Task.Delay(100);
    Console.Error.WriteLine("FATAL server failed during startup | minimum Engine runtime reservation requires 8000000000 bytes in addition to 1073741824 bytes of automatic headroom, but only 6000000000 bytes are available after weights");
    return 1;
}
var delay=int.TryParse(Environment.GetEnvironmentVariable("NINFEREZ_FAKE_READY_DELAY"),out var configured)?configured:50;
await Task.Delay(delay);
var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions{Args=[]});builder.Logging.ClearProviders();builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var app=builder.Build();
app.Use(async(context,next)=>
{
    if(context.Request.Headers.Authorization!="Bearer "+key){context.Response.StatusCode=401;return;}
    await next(context);
});
app.MapGet("/v1/models",()=>new{data=new[]{new{id=model}}});
app.MapPost("/v1/chat/completions",async(HttpContext context)=>
{
    using var json=await JsonDocument.ParseAsync(context.Request.Body,cancellationToken:context.RequestAborted);
    if(json.RootElement.GetProperty("model").GetString()!=model){context.Response.StatusCode=400;return;}
    if(json.RootElement.TryGetProperty("stream",out var stream)&&stream.GetBoolean())
    {
        context.Response.ContentType="text/event-stream";
        await context.Response.WriteAsync("data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n",context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        await Task.Delay(350,context.RequestAborted);
        await context.Response.WriteAsync("data: [DONE]\n\n",context.RequestAborted);
    }
    else await context.Response.WriteAsJsonAsync(new{model,choices=new[]{new{message=new{content="host-only reply"}}}},context.RequestAborted);
});
await app.RunAsync();return 0;
