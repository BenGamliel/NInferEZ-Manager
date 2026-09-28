using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NInferManager.WinUI;

public static class SystemIntegration
{
    private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName="NInferEZ Manager";
    public static bool IsStartupEnabled()
    {
        using var key=Registry.CurrentUser.OpenSubKey(RunKey,false);
        return key?.GetValue(ValueName) is string;
    }
    public static void SetStartupEnabled(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(RunKey,true);
        if(enabled)key.SetValue(ValueName,$"\"{Environment.ProcessPath}\" --minimized",RegistryValueKind.String);
        else key.DeleteValue(ValueName,false);
    }
    public static void HideWindow(IntPtr hwnd)=>ShowWindow(hwnd,0);
    public static void ShowWindow(IntPtr hwnd){ShowWindow(hwnd,5);SetForegroundWindow(hwnd);}
    public static void TrimWorkingSet()
    {
        GC.Collect(2,GCCollectionMode.Optimized,false,true);
        using var process=Process.GetCurrentProcess();
        _=SetProcessWorkingSetSize(process.Handle,new IntPtr(-1),new IntPtr(-1));
    }
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("kernel32.dll")]private static extern bool SetProcessWorkingSetSize(IntPtr process,IntPtr minimum,IntPtr maximum);
}
