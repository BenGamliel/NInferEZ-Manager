using System.Text;

namespace NInferManager.Backend;

public sealed class ManagerLog
{
    private readonly object _gate = new();
    private readonly AppPaths _paths;
    public ManagerLog(AppPaths paths) => _paths = paths;
    public string FilePath => _paths.LogFile;
    public void Write(string message, Exception? error = null)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}" + (error is null ? "" : $" | {error.GetType().Name}: {error.Message}");
        lock (_gate)
        {
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 5 * 1024 * 1024) File.Move(FilePath, FilePath + ".previous", true);
            File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }
    public string Tail(int maxLines = 500) { lock (_gate) return File.Exists(FilePath) ? string.Join(Environment.NewLine, File.ReadLines(FilePath).TakeLast(maxLines)) : ""; }
}

