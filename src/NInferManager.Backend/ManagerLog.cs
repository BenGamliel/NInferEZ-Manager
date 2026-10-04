using System.Text;

namespace NInferManager.Backend;

public sealed class ManagerLog : IDisposable
{
    private readonly object _gate = new();
    private readonly AppPaths _paths;
    private readonly Queue<string> _tail = new();
    private readonly RequestMetricsParser.Accumulator _metrics = new();
    private readonly Timer _flush;
    private StreamWriter? _writer;
    private long _writtenBytes;
    private bool _disposed;
    public ManagerLog(AppPaths paths)
    {
        _paths = paths;
        try
        {
            if (File.Exists(FilePath))
            {
                foreach (var line in File.ReadLines(FilePath).TakeLast(10000)) AddToTail(line);
                _writtenBytes = new FileInfo(FilePath).Length;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        _flush = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }
    public string FilePath => _paths.LogFile;
    public void Write(string message, Exception? error = null)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}" + (error is null ? "" : $" | {error.GetType().Name}: {error.Message}");
        lock (_gate)
        {
            if (_disposed) return;
            AddToTail(line);
            try
            {
                if (_writtenBytes >= 5 * 1024 * 1024)
                {
                    _writer?.Dispose(); _writer = null;
                    if (File.Exists(FilePath)) File.Move(FilePath, FilePath + ".previous", true);
                    _writtenBytes = 0;
                }
                _writer ??= new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false), 16384);
                _writer.WriteLine(line);
                _writtenBytes += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                if (error is not null) _writer.Flush();
            }
            catch (IOException) { CloseWriter(); }
            catch (UnauthorizedAccessException) { CloseWriter(); }
        }
    }
    private void AddToTail(string line)
    {
        _tail.Enqueue(line);
        while (_tail.Count > 10000) _tail.Dequeue();
        _metrics.Add(line);
    }
    public void BeginEngineSession() { lock (_gate) _metrics.BeginSession(); }
    public string Tail(int maxLines = 500) { lock (_gate) return string.Join(Environment.NewLine, _tail.TakeLast(Math.Clamp(maxLines, 1, 10000))); }
    public NInferManager.Contracts.RequestMetricsSnapshot Metrics() { lock (_gate) return _metrics.Snapshot(); }
    public void Flush() { lock (_gate) { try { _writer?.Flush(); } catch (IOException) { } } }
    private void CloseWriter() { var writer=_writer;_writer=null;try{writer?.Dispose();}catch(IOException){}catch(UnauthorizedAccessException){} }
    public void Dispose() { _flush.Dispose(); lock (_gate) { if (_disposed) return; _disposed = true; CloseWriter(); } }
}

