using System.Diagnostics;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public static class GpuMonitor
{
    private static readonly SemaphoreSlim Gate=new(1,1);
    private static GpuStatus? _cached;
    private static DateTime _cachedAt=DateTime.MinValue;
    public static async Task<GpuStatus?> ReadAsync()
    {
        if(DateTime.UtcNow-_cachedAt<TimeSpan.FromSeconds(10))return _cached;
        await Gate.WaitAsync();
        try
        {
            if(DateTime.UtcNow-_cachedAt<TimeSpan.FromSeconds(10))return _cached;
            using var p = Process.Start(new ProcessStartInfo("nvidia-smi.exe", "--query-gpu=name,memory.used,memory.total,utilization.gpu,temperature.gpu --format=csv,noheader,nounits") { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true });
            if (p is null) return _cached; var line=await p.StandardOutput.ReadLineAsync(); await p.WaitForExitAsync();
            var f=line?.Split(',').Select(x=>x.Trim()).ToArray();_cached=f is {Length:>=5}?new(f[0],int.Parse(f[1]),int.Parse(f[2]),int.Parse(f[3]),int.Parse(f[4])):null;_cachedAt=DateTime.UtcNow;return _cached;
        }
        catch { return _cached; }
        finally{Gate.Release();}
    }
}
