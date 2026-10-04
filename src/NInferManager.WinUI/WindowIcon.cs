using System.Runtime.InteropServices;

namespace NInferManager.WinUI;

internal sealed class WindowIcon : IDisposable
{
    private IntPtr _small;
    private IntPtr _large;

    public WindowIcon(IntPtr window, string path)
    {
        _small = LoadImage(IntPtr.Zero, path, 1, GetSystemMetrics(49), GetSystemMetrics(50), 0x10);
        _large = LoadImage(IntPtr.Zero, path, 1, GetSystemMetrics(11), GetSystemMetrics(12), 0x10);
        if (_small != IntPtr.Zero) SendMessage(window, 0x80, IntPtr.Zero, _small);
        if (_large != IntPtr.Zero) SendMessage(window, 0x80, new IntPtr(1), _large);
    }

    public void Dispose()
    {
        if (_small != IntPtr.Zero) { DestroyIcon(_small); _small = IntPtr.Zero; }
        if (_large != IntPtr.Zero) { DestroyIcon(_large); _large = IntPtr.Zero; }
    }

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, IntPtr value);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
