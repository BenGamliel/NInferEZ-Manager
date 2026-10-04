using System.Diagnostics;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public static class GpuMonitor
{
    private static readonly SemaphoreSlim Gate=new(1,1);
    private static IReadOnlyList<GpuStatus> _cached = [];
    private static DateTime _cachedAt=DateTime.MinValue;
    public static async Task<GpuStatus?> ReadAsync(int deviceIndex = 0) => (await ReadAllAsync()).FirstOrDefault(x => x.DeviceIndex == deviceIndex);
    public static async Task<IReadOnlyList<GpuStatus>> ReadAllAsync()
    {
        if(DateTime.UtcNow-_cachedAt<TimeSpan.FromSeconds(10))return _cached;
        await Gate.WaitAsync();
        try
        {
            if(DateTime.UtcNow-_cachedAt<TimeSpan.FromSeconds(10))return _cached;
            using var p = Process.Start(new ProcessStartInfo("nvidia-smi.exe", "--query-gpu=index,uuid,name,memory.used,memory.total,utilization.gpu,temperature.gpu --format=csv,noheader,nounits") { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true });
            if (p is null) return _cached;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                var output = p.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = p.StandardError.ReadToEndAsync(timeout.Token);
                await p.WaitForExitAsync(timeout.Token);
                _cached = Parse(await output);
                await error;
                return _cached;
            }
            catch (OperationCanceledException) { try { p.Kill(true); await p.WaitForExitAsync(); } catch { } return _cached; }
        }
        catch { return _cached; }
        finally{_cachedAt=DateTime.UtcNow;Gate.Release();}
    }
    public static IReadOnlyList<GpuStatus> Parse(string output)
    {
        var list = new List<GpuStatus>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split(',').Select(x => x.Trim()).ToArray();
            if (f.Length < 7 || !int.TryParse(f[0], out var index) || !int.TryParse(f[3], out var used) || !int.TryParse(f[4], out var total)) continue;
            int.TryParse(f[5], out var utilization); int.TryParse(f[6], out var temperature);
            list.Add(new(f[2], used, total, utilization, temperature) { DeviceIndex = index, Uuid = f[1] });
        }
        return list;
    }
}
