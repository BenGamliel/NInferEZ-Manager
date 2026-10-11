using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NInferManager.Backend;
using NInferManager.Contracts;
using NInferManager.WinUI;

if(args.Length == 1 && args[0] == "--fixture-parent") { await Task.Delay(Timeout.Infinite); return; }

var sourceRoot = FindSourceRoot();
var fixtureRoot = Path.Combine(sourceRoot, "artifacts", "host-control-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtureRoot);
var data = Path.Combine(fixtureRoot, "Data");
var models = Path.Combine(fixtureRoot, "Models");
Directory.CreateDirectory(data); Directory.CreateDirectory(models);
var publicPort = FreePort(); var controlPort = FreePort(); var internalPort = FreePort();
while (controlPort == publicPort) controlPort = FreePort();
while (internalPort == publicPort || internalPort == controlPort) internalPort = FreePort();
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var publicKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
var settings = new ManagerSettings { PublicPort = publicPort, BackendPort = internalPort, LockPublicPort = true,
    ApiKey = publicKey, AutoCheckCatalog = false, AutoCheckEngines = false, AutoCheckUpdates = false };
File.WriteAllText(Path.Combine(data, "settings.json"), JsonSerializer.Serialize(settings, SettingsService.JsonOptions));
File.WriteAllBytes(Path.Combine(models, "fixture.ninfer"), [0, 1, 2, 3]);
File.WriteAllBytes(Path.Combine(models, "bad-preset.ninfer"), [3, 2, 1, 0]);
File.WriteAllText(Path.Combine(models, "bad-preset.ninferez.json"), "{\"invalid\":\"preset\"}");
var backendPath = args.FirstOrDefault(x => !x.StartsWith('-')) ?? Directory.EnumerateFiles(Path.Combine(sourceRoot, "src", "NInferManager.Backend", "bin"), "NInferManager.Backend.exe", SearchOption.AllDirectories)
    .OrderByDescending(File.GetLastWriteTimeUtc).First();
var start = new ProcessStartInfo(backendPath) { UseShellExecute = false, CreateNoWindow = true,
    RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixtureRoot };
var parentStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))
    parentStart.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
parentStart.ArgumentList.Add("--fixture-parent");
using var fixtureParent = Process.Start(parentStart) ?? throw new Exception("Fixture parent did not start.");
start.Environment["NINFEREZ_MANAGER_CONTROL_PORT"] = controlPort.ToString();
start.Environment["NINFEREZ_MANAGER_CONTROL_TOKEN"] = token;
start.Environment["NINFEREZ_MANAGER_PARENT_PID"] = fixtureParent.Id.ToString();
start.Environment["NINFEREZ_MANAGER_DATA_ROOT"] = data;
start.Environment["NINFEREZ_MANAGER_MODELS_ROOT"] = models;
start.Environment["NINFEREZ_MANAGER_ENGINE_ROOT"] = Path.Combine(fixtureRoot, "Engine");
using var process = Process.Start(start) ?? throw new Exception("Backend fixture did not start.");
var errors = process.StandardError.ReadToEndAsync();
var stdout = process.StandardOutput.ReadToEndAsync();
using var job = new ProcessJob(); job.Assign(process);
job.Assign(fixtureParent);
using var control = new HttpClient(new SocketsHttpHandler { UseProxy = false })
    { BaseAddress = new Uri($"http://127.0.0.1:{controlPort}"), Timeout = TimeSpan.FromSeconds(15) };
control.DefaultRequestHeaders.Add(ControlAuthentication.Header, token);
using var anonymous = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = control.BaseAddress, Timeout = control.Timeout };
using var api = new HttpClient(new SocketsHttpHandler { UseProxy = false })
    { BaseAddress = new Uri($"http://127.0.0.1:{publicPort}"), Timeout = TimeSpan.FromSeconds(15) };
var passed = 0;
try
{
    await WaitReady();
    using (var health = await Json(control, "/control/v1/health"))
    {
        Check(health.RootElement.GetProperty("product").GetString() == "NInferEZ Manager", "owned backend product identity");
        Check(health.RootElement.GetProperty("processId").GetInt32() == process.Id, "owned backend PID identity");
        Check(!string.IsNullOrEmpty(health.RootElement.GetProperty("instanceId").GetString()), "session instance identity");
    }
    await Expect(anonymous, HttpMethod.Get, "/control/v1/health", null, 401, "control health requires session token");
    await Expect(anonymous,HttpMethod.Get,"/control/v1/converter/status",null,401,"converter control is private and authenticated");
    using(var conversion=await Json(control,"/control/v1/converter/status"))
        Check(!conversion.RootElement.GetProperty("running").GetBoolean(),"converter API starts idle without runtime or GPU loading");
    await Expect(control,HttpMethod.Post,"/control/v1/converter/check","""{"source":"","output":"missing.ninfer"}""",400,"converter invalid input has actionable400");
    await Expect(control,HttpMethod.Post,"/control/v1/converter/cancel","{}",409,"converter cancel reports no active job rather than an internal error");
    anonymous.DefaultRequestHeaders.Add(ControlAuthentication.Header, new string('0', 64));
    await Expect(anonymous, HttpMethod.Get, "/control/v1/settings", null, 401, "wrong control token is rejected");
    await Expect(anonymous, HttpMethod.Post, "/control/v1/api/state", "{\"enabled\":false}", 401, "unauthorized mutation is rejected");
    await Expect(api, HttpMethod.Get, "/health", null, 200, "public health works before model load");
    await Expect(api, HttpMethod.Get, "/v1/models", null, 401, "public model list enforces configured key");
    api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", publicKey);
    using (var inventory = await Json(api, "/v1/models"))
        Check(inventory.RootElement.GetProperty("data").GetArrayLength() == 2, "public inventory exposes only present local artifacts");
    using (var health = await Json(api, "/health"))
        Check(health.RootElement.GetProperty("engine").GetString() == "Unloaded", "metadata calls do not load an engine");
    using var library = await Json(control, "/control/v1/models");
    using(var notices=await Json(control,"/control/v1/catalog/notifications"))
        Check(notices.RootElement.TryGetProperty("pending",out var pending)&&pending.ValueKind==JsonValueKind.Array&&notices.RootElement.GetProperty("startupCheckComplete").GetBoolean(),"catalog notification API is ready without a model or GPU load");
    await Expect(control,HttpMethod.Post,"/control/v1/catalog/notifications/dismiss","{\"identities\":[\"unknown\"]}",400,"unknown model announcement acknowledgement is rejected");
    var fixture = library.RootElement.EnumerateArray().Single(x => x.GetProperty("fileName").GetString() == "fixture.ninfer");
    var badPreset = library.RootElement.EnumerateArray().Single(x => x.GetProperty("fileName").GetString() == "bad-preset.ninfer");
    Check(badPreset.GetProperty("presetStatus").GetString()?.Contains("could not be imported") == true,
        "malformed adjacent preset is isolated to its model and does not prevent backend startup");
    var file = Uri.EscapeDataString(fixture.GetProperty("fileName").GetString()!);
    await Expect(control, HttpMethod.Post, $"/control/v1/models/{file}/download", null, 200, "already installed artifact never starts another download");
    using(var download=await Json(control,"/control/v1/download"))
        Check(download.RootElement.ValueKind==JsonValueKind.Null||!download.RootElement.GetProperty("running").GetBoolean(),"installed variant download action leaves downloader idle");
    var modelInfo = fixture.Deserialize<ModelInfo>(SettingsService.JsonOptions)!;
    var reported = modelInfo with { Inspected=true, HasMtp=false, HasDflash=false, HasDflash2=true };
    var supportedChoices = SpeculativeChoices.ForModel(reported, SpeculativeMode.Disabled);
    Check(supportedChoices.Select(x=>x.Mode).SequenceEqual([SpeculativeMode.Disabled,SpeculativeMode.Dflash2]), "UI choices contain only artifact-supported speculation modes");
    var exactChoices = SpeculativeChoices.ForModel(reported,SpeculativeMode.Mtp);
    Check(exactChoices.Any(x=>x.Mode==SpeculativeMode.Mtp && !x.ConfirmedSupported && x.Label.Contains("unsupported")), "unsupported exact mode is retained with a warning rather than silently changed");
    Check(SpeculativeChoices.ForModel(reported,SpeculativeMode.Mtp,true).All(x=>x.ConfirmedSupported), "managed UI does not offer an inspected unsupported acceleration mode");
    var unknown = modelInfo with { Inspected=false };
    Check(SpeculativeChoices.ForModel(unknown,SpeculativeMode.Disabled).Count==1, "unknown artifact does not advertise unverified speculation choices");
    Check(SpeculativeChoices.ForModel(unknown,SpeculativeMode.Dflash2).Any(x=>x.Mode==SpeculativeMode.Dflash2 && !x.ConfirmedSupported && x.Label.Contains("unknown")), "unknown current mode remains explicit and unverified");
    Check(modelInfo.RecommendedProfile.SpeculativeMode==SpeculativeMode.Disabled, "UI capability choices never mutate saved numerical profile");
    await Expect(control, HttpMethod.Put, $"/control/v1/models/{file}/alias", "{\"alias\":\"short-test-model\"}", 200, "optional alias can be set");
    await Expect(control, HttpMethod.Put, $"/control/v1/models/{file}/alias", JsonSerializer.Serialize(new { alias = new string('x',129) }), 400, "invalid alias rejected");
    var revision = await Revision();
    await Expect(control, HttpMethod.Patch, "/control/v1/settings", JsonSerializer.Serialize(new { changes = new { theme = "Dark" }, expectedRevision = revision }), 200, "scoped preference PATCH succeeds");
    await Expect(control, HttpMethod.Patch, "/control/v1/settings", JsonSerializer.Serialize(new { changes = new { theme = "Light" }, expectedRevision = revision }), 409, "stale preference revision rejected");
    revision = await Revision();
    await Expect(control, HttpMethod.Patch, "/control/v1/settings", JsonSerializer.Serialize(new { changes = new { idleMinutes = -1 }, expectedRevision = revision }), 400, "invalid preference cannot replace saved state");
    await Expect(control, HttpMethod.Patch, "/control/v1/settings", JsonSerializer.Serialize(new { changes = new { externalModelPaths = "invalid" }, expectedRevision = revision }), 400, "complex ownership fields cannot be patched as scalars");
    using (var inventory = await Json(api, "/v1/models"))
        Check(inventory.RootElement.GetProperty("data").EnumerateArray().Any(x => x.GetProperty("id").GetString() == "short-test-model"), "preferences preserve API alias");
    var profile = fixture.GetProperty("recommendedProfile").Deserialize<ModelProfile>(SettingsService.JsonOptions)!;
    profile.AutoContext = false; profile.MaxRequestMiB = 1;
    revision = await Revision();
    await Expect(control, HttpMethod.Patch, "/control/v1/settings", JsonSerializer.Serialize(new { changes = new { profiles = new Dictionary<string,ModelProfile> { [fixture.GetProperty("fileName").GetString()!] = profile } }, expectedRevision = revision }, SettingsService.JsonOptions), 200, "typed model profile PATCH succeeds");
    await Expect(control, HttpMethod.Post, $"/control/v1/models/{file}/activate", null, 200, "default model selection works while unloaded");
    await Expect(api, HttpMethod.Post, "/v1/chat/completions", JsonSerializer.Serialize(new { model="short-test-model",messages=Array.Empty<object>(),padding=new string('x',1024*1024) }), 413, "request body limit is enforced before model load");
    using (var chunked = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions"))
    {
        chunked.Headers.TransferEncodingChunked = true;
        chunked.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { model="short-test-model", messages=Array.Empty<object>(), padding=new string('x',1024*1024) }))));
        chunked.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await api.SendAsync(chunked);
        Check((int)response.StatusCode == 413, "chunked request exceeding body budget returns413 rather than503");
    }
    await Expect(api, HttpMethod.Post, "/v1/chat/completions", "not-json", 400, "malformed inference JSON rejected before load");
    await Expect(api, HttpMethod.Post, "/v1/chat/completions", "[]", 400, "nonobject inference JSON rejected");
    await Expect(api, HttpMethod.Post, "/v1/chat/completions", "{}", 400, "missing model has actionable400");
    await Expect(api, HttpMethod.Post, "/v1/chat/completions", "{\"model\":\"missing\",\"messages\":[]}", 404, "unknown model rejected before load");
    await Expect(api, HttpMethod.Get, "/v1/not-a-route", null, 404, "unsupported route does not load engine");
    await Expect(api, HttpMethod.Get, "/v1/responses/stored-test", null, 409, "stored-response route does not cold-load engine");
    await Expect(control, HttpMethod.Post, "/control/v1/api/state", "{\"enabled\":false}", 200, "API can be disabled without stopping control plane");
    var stopped = false;
    try { using var response = await api.GetAsync("/health"); } catch (HttpRequestException) { stopped = true; }
    Check(stopped, "disabled public listener no longer accepts connections");
    await Expect(control, HttpMethod.Post, "/control/v1/api/state", "{\"enabled\":true}", 200, "API can be reenabled");
    await Expect(api, HttpMethod.Get, "/health", null, 200, "reenabled API is reachable");
    await Expect(api, HttpMethod.Post, "/v1/chat/completions", "{\"model\":\"short-test-model\",\"messages\":[]}", 503, "missing engine returns actionable503 without inference");
    var externalA = Path.Combine(fixtureRoot, "external-a", "same.ninfer");
    var externalB = Path.Combine(fixtureRoot, "external-b", "same.ninfer");
    Directory.CreateDirectory(Path.GetDirectoryName(externalA)!); Directory.CreateDirectory(Path.GetDirectoryName(externalB)!);
    File.WriteAllBytes(externalA, [1, 2, 3, 4]); File.WriteAllBytes(externalB, [4, 3, 2, 1]);
    await Expect(control, HttpMethod.Post, "/control/v1/models/link", JsonSerializer.Serialize(new { path = externalA }), 200, "first external artifact links without copying");
    await Expect(control, HttpMethod.Post, "/control/v1/models/link", JsonSerializer.Serialize(new { path = externalB }), 200, "same-name same-size different artifact remains separate");
    using (var linkedSettings = await Json(control, "/control/v1/settings"))
        Check(linkedSettings.RootElement.GetProperty("externalModelPaths").EnumerateObject().Select(x => x.Value.GetString()).ToHashSet().SetEquals([externalA,externalB]), "both external paths remain saved independently");
    using (var inventory = await Json(api, "/v1/models"))
    {
        var identifiers = inventory.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToArray();
        Check(identifiers.Length == 4 && identifiers.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4, "all present model API IDs are unique and routable");
        var otherId = identifiers.First(x => x != "short-test-model" && x != "bad-preset")!;
        await Expect(api, HttpMethod.Post, "/v1/chat/completions", JsonSerializer.Serialize(new { model=otherId, messages=Array.Empty<object>(),padding=new string('x',1024*1024) }),503,"body budget follows requested model rather than different selected default");
    }
    Check(Directory.EnumerateFiles(models,"*.ninfer").Count() == 2, "external linking never copies model weight fixtures");
    fixtureParent.Kill(); await fixtureParent.WaitForExitAsync();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    Check(process.ExitCode == 0, "backend exits cleanly when its owned UI parent disappears");
    Console.WriteLine($"{passed} live control/API host checks passed. Tiny fixture only; no GPU, model or native UI was launched.");
}
catch
{
    if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
    Console.Error.WriteLine((await errors) + (await stdout));
    throw;
}
finally
{
    if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
    if (!fixtureParent.HasExited) { fixtureParent.Kill(); await fixtureParent.WaitForExitAsync(); }
    var expected = Path.Combine(sourceRoot, "artifacts", "host-control-tests") + Path.DirectorySeparatorChar;
    if (Path.GetFullPath(fixtureRoot).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) Directory.Delete(fixtureRoot, true);
}

async Task WaitReady()
{
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed < TimeSpan.FromSeconds(20))
    {
        if (process.HasExited) throw new Exception("Backend fixture exited during startup.");
        try { using var response = await control.GetAsync("/control/v1/health"); if(response.IsSuccessStatusCode) return; }
        catch (HttpRequestException) { }
        await Task.Delay(100);
    }
    throw new TimeoutException("Backend fixture readiness timed out.");
}
async Task<long> Revision() { using var json = await Json(control,"/control/v1/settings"); return json.RootElement.GetProperty("revision").GetInt64(); }
async Task Expect(HttpClient client, HttpMethod method, string path, string? body, int expected, string name)
{
    using var request = new HttpRequestMessage(method, path);
    if(body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    using var response = await client.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    Check((int)response.StatusCode == expected, name + $" (expected {expected}, received {(int)response.StatusCode})", text);
}
void Check(bool ok, string name, string? detail=null)
{
    if(!ok) throw new Exception("FAILED: " + name + " " + detail);
    passed++; Console.WriteLine("PASS " + name);
}
static async Task<JsonDocument> Json(HttpClient client, string path) { using var response = await client.GetAsync(path); response.EnsureSuccessStatusCode(); return JsonDocument.Parse(await response.Content.ReadAsStringAsync()); }
static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
static string FindSourceRoot()
{
    for(var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if(File.Exists(Path.Combine(directory.FullName,"NInferEZ.Manager.slnx"))) return directory.FullName;
    throw new DirectoryNotFoundException("Run these tests within the source tree.");
}
