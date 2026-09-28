using System.Runtime.InteropServices;
using NInferManager.Contracts;

namespace NInferManager.WinUI;

public sealed class TrayIconController : IDisposable
{
    private const uint CallbackMessage=0x8000+73;
    private readonly IntPtr _window;
    private readonly IntPtr _icon;
    private readonly WindowProc _windowProc;
    private readonly IntPtr _originalProc;
    private NotifyIconData _data;
    private bool _disposed;
    private bool _modelLoaded;

    public event Action? RestoreRequested;
    public event Action? PrimaryActionRequested;
    public event Action? RestartRequested;
    public event Action<double?>? IdleRequested;
    public event Action? CheckUpdatesRequested;
    public event Action? ExitRequested;

    public TrayIconController(IntPtr window)
    {
        _window=window;
        var iconPath=Path.Combine(AppContext.BaseDirectory,"Assets","NInferEZ.ico");
        ExtractIconEx(File.Exists(iconPath)?iconPath:Environment.ProcessPath!,0,out var large,out var small,1);
        _icon=small!=IntPtr.Zero?small:large;
        if(small!=IntPtr.Zero&&large!=IntPtr.Zero)DestroyIcon(large);
        _windowProc=WndProc;
        _originalProc=SetWindowLongPtr(_window,-4,Marshal.GetFunctionPointerForDelegate(_windowProc));
        _data=new NotifyIconData{cbSize=(uint)Marshal.SizeOf<NotifyIconData>(),hWnd=_window,uID=1,uFlags=0x1|0x2|0x4,uCallbackMessage=CallbackMessage,hIcon=_icon,szTip="NInferEZ Manager - Unloaded",szInfo=string.Empty,szInfoTitle=string.Empty};
        if(!Shell_NotifyIcon(0,ref _data))throw new InvalidOperationException("Windows could not create the notification-area icon.");
        _data.uTimeoutOrVersion=4;Shell_NotifyIcon(4,ref _data);
    }

    public void SetStatus(EngineState state,string? modelName)
    {
        _modelLoaded=state==EngineState.Ready;
        var status=_modelLoaded?modelName??"Ready":state.ToString();
        var value="NInferEZ Manager - "+status;_data.szTip=value.Length>127?value[..127]:value;_data.uFlags=0x4;Shell_NotifyIcon(1,ref _data);
    }

    private IntPtr WndProc(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam)
    {
        if(message==CallbackMessage)
        {
            var mouse=(uint)(lParam.ToInt64()&0xffff);
            if(mouse==0x203)RestoreRequested?.Invoke();
            else if(mouse==0x205)ShowMenu();
            return IntPtr.Zero;
        }
        return CallWindowProc(_originalProc,hwnd,message,wParam,lParam);
    }
    private void ShowMenu()
    {
        var menu=CreatePopupMenu();var idle=CreatePopupMenu();
        try
        {
            Add(menu,1,"Open NInferEZ Manager");AppendMenu(menu,0x800,UIntPtr.Zero,null);
            Add(menu,3,_modelLoaded?"Unload model":"Load model");if(_modelLoaded)Add(menu,5,"Restart NInfer");
            Add(idle,6,"Off");Add(idle,7,"After 3 minutes");Add(idle,8,"After 10 minutes");Add(idle,9,"After 30 minutes");AppendMenu(menu,0x10,(UIntPtr)(ulong)idle.ToInt64(),"Automatic VRAM unload");
            Add(menu,10,"Check for updates");AppendMenu(menu,0x800,UIntPtr.Zero,null);Add(menu,11,"Exit");
            GetCursorPos(out var point);SetForegroundWindow(_window);var command=TrackPopupMenu(menu,0x100|0x2,point.X,point.Y,0,_window,IntPtr.Zero);
            switch(command){case 1:RestoreRequested?.Invoke();break;case 3:PrimaryActionRequested?.Invoke();break;case 5:RestartRequested?.Invoke();break;case 6:IdleRequested?.Invoke(null);break;case 7:IdleRequested?.Invoke(3);break;case 8:IdleRequested?.Invoke(10);break;case 9:IdleRequested?.Invoke(30);break;case 10:CheckUpdatesRequested?.Invoke();break;case 11:ExitRequested?.Invoke();break;}
        }
        finally{DestroyMenu(menu);}
    }
    private static void Add(IntPtr menu,uint id,string text)=>AppendMenu(menu,0,id,text);
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_data.uFlags=0;Shell_NotifyIcon(2,ref _data);SetWindowLongPtr(_window,-4,_originalProc);if(_icon!=IntPtr.Zero)DestroyIcon(_icon);
    }

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct NotifyIconData{public uint cbSize;public IntPtr hWnd;public uint uID,uFlags,uCallbackMessage;public IntPtr hIcon;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string szTip;public uint dwState,dwStateMask;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string szInfo;public uint uTimeoutOrVersion;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string szInfoTitle;public uint dwInfoFlags;public Guid guidItem;public IntPtr hBalloonIcon;}
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    private delegate IntPtr WindowProc(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern bool Shell_NotifyIcon(uint message,ref NotifyIconData data);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern uint ExtractIconEx(string file,int index,out IntPtr large,out IntPtr small,uint count);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")]private static extern IntPtr CallWindowProc(IntPtr previous,IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool AppendMenu(IntPtr menu,uint flags,UIntPtr id,string? text);
    [DllImport("user32.dll")]private static extern uint TrackPopupMenu(IntPtr menu,uint flags,int x,int y,int reserved,IntPtr hwnd,IntPtr rect);
    [DllImport("user32.dll")]private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
}
