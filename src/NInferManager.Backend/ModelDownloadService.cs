using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public sealed class ModelDownloadService : IDisposable
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly ManagerLog _log;
    private readonly HttpClient _client = new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private CancellationTokenSource? _currentCancellation;
    public ModelDownloadService(AppPaths paths, SettingsService settings, ManagerLog log) { _paths = paths; _settings = settings; _log = log; _client.DefaultRequestHeaders.UserAgent.ParseAdd($"NInferEZ-Manager/{ProductInfo.Version}"); }
    public DownloadState? Current { get; private set; }

    public void Start(ModelCatalogEntry entry)
    {
        if(_currentCancellation is not null)throw new InvalidOperationException("Another model download is already running.");
        _currentCancellation=new CancellationTokenSource();
        _=Task.Run(async()=>
        {
            try{await DownloadAsync(entry,_currentCancellation.Token);}
            catch(OperationCanceledException){Current=new(entry.FileName,"Paused",Current?.Completed??0,entry.SizeBytes,0,false);}
            catch{ }
            finally{_currentCancellation.Dispose();_currentCancellation=null;}
        });
    }
    public void Cancel()=>_currentCancellation?.Cancel();

    public async Task DownloadAsync(ModelCatalogEntry entry, CancellationToken token)
    {
        if (!await _downloadGate.WaitAsync(0, token)) throw new InvalidOperationException("Another model download is already running.");
        try
        {
            var final = Path.Combine(_paths.ModelsDirectory, entry.FileName);
            var part = final + ".part";
            var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset > entry.SizeBytes) { File.Delete(part); offset = 0; }
            using var request = new HttpRequestMessage(HttpMethod.Get, entry.DownloadUrl);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (offset > 0 && response.StatusCode == HttpStatusCode.OK) { File.Delete(part); offset = 0; }
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(part, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, true);
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
            long completed = offset;
            var previous = DateTime.UtcNow; var previousBytes = completed;
            try
            {
                while (true)
                {
                    var read = await input.ReadAsync(buffer, token); if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), token); completed += read;
                    var now = DateTime.UtcNow;
                    if ((now - previous).TotalMilliseconds >= 500) { Current = new(entry.FileName, "Downloading", completed, entry.SizeBytes, (completed - previousBytes) / (now - previous).TotalSeconds, true); previous = now; previousBytes = completed; }
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            await output.FlushAsync(token);
            if (completed != entry.SizeBytes) throw new InvalidDataException($"Downloaded {completed:N0} bytes; expected {entry.SizeBytes:N0}.");
            Current = new(entry.FileName, "Verifying SHA-256", completed, entry.SizeBytes, 0, true);
            await using var verify = File.OpenRead(part);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(verify, token));
            if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 verification failed. The partial file was kept.");
            File.Move(part, final, true);
            _settings.Current.ExternalModelPaths.Remove(entry.FileName);
            _settings.Save();
            Current = new(entry.FileName, "Installed and verified", completed, entry.SizeBytes, 0, false);
            _log.Write("Model installed: " + entry.FileName);
        }
        catch (Exception ex) { Current = new(entry.FileName, "Failed", 0, entry.SizeBytes, 0, false, ex.Message); _log.Write("Model download failed", ex); throw; }
        finally { _downloadGate.Release(); }
    }
    public async Task<bool> VerifyAsync(ModelCatalogEntry entry,CancellationToken token)
    {
        var path=_settings.Current.ExternalModelPaths.TryGetValue(entry.FileName,out var external) ? external : Path.Combine(_paths.ModelsDirectory,entry.FileName);
        if(!File.Exists(path)||new FileInfo(path).Length!=entry.SizeBytes)return false;
        if(string.IsNullOrWhiteSpace(entry.Sha256))
        {
            Current=new(entry.FileName,"File is available",entry.SizeBytes,entry.SizeBytes,0,false);
            return true;
        }
        Current=new(entry.FileName,"Verifying SHA-256",0,entry.SizeBytes,0,true);
        await using var input=File.OpenRead(path);
        var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,token));
        var ok=hash.Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase);
        Current=new(entry.FileName,ok?"Verification passed":"Verification failed",entry.SizeBytes,entry.SizeBytes,0,false,ok?null:"SHA-256 mismatch.");
        return ok;
    }
    public async Task ImportAsync(string source,ModelCatalogEntry entry,CancellationToken token)
    {
        if(!File.Exists(source))throw new FileNotFoundException("The selected model file does not exist.",source);
        if(new FileInfo(source).Length!=entry.SizeBytes)throw new InvalidDataException("The selected file has the wrong size for this model.");
        await using(var input=File.OpenRead(source))
        {
            var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,token));
            if(!hash.Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The selected file failed SHA-256 verification.");
        }
        _settings.Current.ExternalModelPaths[entry.FileName]=Path.GetFullPath(source);
        _settings.Save();
        Current=new(entry.FileName,"Linked and verified",entry.SizeBytes,entry.SizeBytes,0,false);
        _log.Write("Model linked without copying: "+entry.FileName);
    }
    public void Dispose() { _currentCancellation?.Cancel();_currentCancellation?.Dispose();_client.Dispose(); _downloadGate.Dispose(); }
}
