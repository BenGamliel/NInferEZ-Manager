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
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private readonly object _operationSync = new();
    private Task? _operation;
    private CancellationTokenSource? _currentCancellation;
    public ModelDownloadService(AppPaths paths, SettingsService settings, ManagerLog log,HttpClient? client=null) { _paths = paths; _settings = settings; _log = log; _client=client??new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan }; _client.DefaultRequestHeaders.UserAgent.ParseAdd($"NInferEZ-Manager/{ProductInfo.Version}"); }
    public DownloadState? Current { get; private set; }

    public void Start(ModelCatalogEntry entry)
    {
        lock (_operationSync)
        {
            if(_operation is {IsCompleted:false})throw new InvalidOperationException("Another model download is already running.");
            _currentCancellation?.Dispose();
            var cancellation=_currentCancellation=new CancellationTokenSource();
            Current=new(entry.FileName,"Starting",0,entry.SizeBytes,0,true);
            _operation=Task.Run(async()=>
            {
                try{await DownloadAsync(entry,cancellation.Token);}
                catch(OperationCanceledException){Current=new(entry.FileName,"Paused",Current?.Completed??0,entry.SizeBytes,0,false);}
                catch{ }
            });
        }
    }
    public void Cancel()=>_currentCancellation?.Cancel();

    public async Task DownloadAsync(ModelCatalogEntry entry, CancellationToken token)
    {
        if (!await _downloadGate.WaitAsync(0, token)) throw new InvalidOperationException("Another model download is already running.");
        try
        {
            var final = Path.Combine(_paths.ModelsDirectory, entry.FileName);
            var part = final + ".part";
            if(Path.GetFileName(entry.FileName)!=entry.FileName || entry.SizeBytes<=0 || entry.Sha256.Length!=64 || !entry.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("This model has no safe verifiable download identity.");
            var identityFile=part+".json";
            var identity=$"{entry.DownloadUrl}\n{entry.SizeBytes}\n{entry.Sha256.ToLowerInvariant()}";
            if(File.Exists(part)&&(!File.Exists(identityFile)||File.ReadAllText(identityFile)!=identity))File.Delete(part);
            File.WriteAllText(identityFile,identity);
            var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset > entry.SizeBytes) { File.Delete(part); offset = 0; }
            long completed = offset;
            if(offset<entry.SizeBytes)
            {
            using var request = new HttpRequestMessage(HttpMethod.Get, entry.DownloadUrl);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (offset > 0 && response.StatusCode == HttpStatusCode.OK) { File.Delete(part); offset = 0; }
            response.EnsureSuccessStatusCode();
            if(response.StatusCode==HttpStatusCode.PartialContent && (response.Content.Headers.ContentRange?.From!=offset || response.Content.Headers.ContentRange?.Length!=entry.SizeBytes))throw new InvalidDataException("The server returned an incompatible download range.");
            if(offset>0&&response.StatusCode!=HttpStatusCode.PartialContent)throw new InvalidDataException("The server did not provide the requested download range.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(part, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, true))
            {
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
            completed = offset;
            var previous = DateTime.UtcNow; var previousBytes = completed;
            try
            {
                while (true)
                {
                    using var readTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                    var read = await input.ReadAsync(buffer, readTimeout.Token); if (read == 0) break;
                    if(completed+read>entry.SizeBytes)throw new InvalidDataException("The download exceeded the expected model size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token); completed += read;
                    var now = DateTime.UtcNow;
                    if ((now - previous).TotalMilliseconds >= 500) { Current = new(entry.FileName, "Downloading", completed, entry.SizeBytes, (completed - previousBytes) / (now - previous).TotalSeconds, true); previous = now; previousBytes = completed; }
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            await output.FlushAsync(token);
            }
            }
            if (completed != entry.SizeBytes) throw new InvalidDataException($"Downloaded {completed:N0} bytes; expected {entry.SizeBytes:N0}.");
            Current = new(entry.FileName, "Verifying SHA-256", completed, entry.SizeBytes, 0, true);
            await using(var verify = File.OpenRead(part))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(verify, token));
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 verification failed. The untrusted download was removed; retry the download.");
            }
            File.Move(part, final, true);
            entry.LocalPath=final;entry.Inspected=false;entry.VerifiedWriteUtc=File.GetLastWriteTimeUtc(final);
            File.Delete(identityFile);
            _settings.Update(s=>s.ExternalModelPaths.Remove(entry.FileName));
            Current = new(entry.FileName, "Installed and verified", completed, entry.SizeBytes, 0, false);
            entry.IdentityVerified=true;
            _log.Write("Model installed: " + entry.FileName);
        }
        catch (Exception ex)
        {
            if(ex is InvalidDataException&&ex.Message.StartsWith("SHA-256 verification failed",StringComparison.Ordinal))
            {
                var failedPart=Path.Combine(_paths.ModelsDirectory,entry.FileName)+".part";
                File.Delete(failedPart);File.Delete(failedPart+".json");
            }
            Current = new(entry.FileName, "Failed", 0, entry.SizeBytes, 0, false, ex.Message); _log.Write("Model download failed", ex); throw;
        }
        finally { _downloadGate.Release(); }
    }
    public async Task<bool> VerifyAsync(ModelCatalogEntry entry,CancellationToken token)
    {
        var path=_settings.Current.ExternalModelPaths.TryGetValue(entry.FileName,out var external) ? external : entry.LocalPath??Path.Combine(_paths.ModelsDirectory,entry.FileName);
        if(!File.Exists(path)||new FileInfo(path).Length!=entry.SizeBytes)return false;
        var expected=entry.ExpectedSha256??entry.Sha256;
        if(string.IsNullOrWhiteSpace(expected))
        {
            Current=new(entry.FileName,"Computing artifact identity",0,entry.SizeBytes,0,true);
            await using(var source=File.OpenRead(path))expected=Convert.ToHexString(await SHA256.HashDataAsync(source,token));
            entry.Sha256=expected;
            entry.ExpectedSha256=expected;
            entry.IdentityVerified=true;
            Current=new(entry.FileName,"Artifact identity verified",entry.SizeBytes,entry.SizeBytes,0,false);
            return true;
        }
        Current=new(entry.FileName,"Verifying SHA-256",0,entry.SizeBytes,0,true);
        await using var input=File.OpenRead(path);
        var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,token));
        var ok=hash.Equals(expected,StringComparison.OrdinalIgnoreCase);
        entry.IdentityVerified=ok;
        if(entry.PresetStatus is not null)entry.PresetStatus=ok?"Preset settings retained; model SHA-256 identity verified.":"Model SHA-256 does not match the preset identity.";
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
        _settings.Update(s=>s.ExternalModelPaths[entry.FileName]=Path.GetFullPath(source));
        Current=new(entry.FileName,"Linked and verified",entry.SizeBytes,entry.SizeBytes,0,false);
        _log.Write("Model linked without copying: "+entry.FileName);
    }
    public void Dispose() { _currentCancellation?.Cancel(); _client.Dispose(); }
}
