using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms=System.Windows.Forms;

namespace NabyScape;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point,uint flags);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct DisplayMode
    {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string DeviceName;
        public ushort SpecVersion,DriverVersion,Size,DriverExtra;public uint Fields;
        public int PositionX,PositionY;public uint Orientation,FixedOutput;
        public short Color,Duplex,YResolution,TTOption,Collate;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string FormName;
        public ushort LogPixels;public uint BitsPerPel,PelsWidth,PelsHeight,DisplayFlags,DisplayFrequency,ICMMethod,ICMIntent,MediaType,DitherType,Reserved1,Reserved2,PanningWidth,PanningHeight;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device,int mode,ref DisplayMode settings);
    internal static uint RefreshRate(Forms.Screen screen)
    {var mode=new DisplayMode{Size=(ushort)Marshal.SizeOf<DisplayMode>(),DeviceName="",FormName=""};return EnumDisplaySettings(screen.DeviceName,-1,ref mode)?mode.DisplayFrequency:0;}
    [DllImport("user32.dll")] internal static extern IntPtr SetWindowsHookEx(int id,HookProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wparam,IntPtr lparam);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
    internal delegate IntPtr HookProc(int code,IntPtr wparam,IntPtr lparam);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Auto)] private struct Memory
    { public uint Length,Load; public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended; }
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] private static extern bool GlobalMemoryStatusEx(ref Memory memory);
    private static long lastIdle,lastTotal;
    public static (double cpu,double used,double total) SystemUsage()
    {
        double cpu=0;
        if(GetSystemTimes(out var idle,out var kernel,out var user))
        {
            var total=kernel+user; var delta=total-lastTotal;
            if(lastTotal!=0 && delta>0) cpu=Math.Clamp(100.0*(delta-(idle-lastIdle))/delta,0,100);
            lastIdle=idle;lastTotal=total;
        }
        var mem=new Memory{Length=(uint)Marshal.SizeOf<Memory>()};
        GlobalMemoryStatusEx(ref mem);
        return(cpu,(mem.TotalPhysical-mem.AvailablePhysical)/1073741824.0,mem.TotalPhysical/1073741824.0);
    }
    public static Forms.Screen ChooseScreen(Settings settings)
    {
        foreach(var s in Forms.Screen.AllScreens) if(s.DeviceName==settings.Monitor) return s;
        return Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    }
    public static double Scale(Forms.Screen screen)
    {
        var monitor=MonitorFromPoint(new(){X=screen.Bounds.Left+screen.Bounds.Width/2,Y=screen.Bounds.Top+screen.Bounds.Height/2},2);
        return GetDpiForMonitor(monitor,0,out var x,out _)==0 ? x/96.0 : 1;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    internal static IntPtr ExternalForegroundWindow()
    {var window=GetForegroundWindow();return IsExternalWindow(window)?window:IntPtr.Zero;}
    internal static bool IsExternalWindow(IntPtr window)
    {
        if(window==IntPtr.Zero || window==GetShellWindow() || window==GetDesktopWindow() || !IsWindowVisible(window) || IsIconic(window))return false;
        GetWindowThreadProcessId(window,out uint process);if(process==Environment.ProcessId)return false;
        var name=new System.Text.StringBuilder(128);GetClassName(window,name,128);
        return name.ToString() is not ("WorkerW" or "Progman" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }
    public static bool FullscreenOn(Forms.Screen screen,IntPtr own)=>IsFullscreenWindow(screen,GetForegroundWindow(),own);
    internal static bool ShouldHideOn(Forms.Screen screen,IntPtr window,bool fullscreen,bool maximized)
    {
        if(!IsExternalWindow(window) || MonitorFromWindow(window,2)!=MonitorFromPoint(new(){X=screen.Bounds.Left+screen.Bounds.Width/2,Y=screen.Bounds.Top+screen.Bounds.Height/2},2))return false;
        return (maximized && IsZoomed(window)) || (fullscreen && IsFullscreenWindow(screen,window,IntPtr.Zero));
    }
    internal static bool IsFullscreenWindow(Forms.Screen screen,IntPtr hwnd,IntPtr own)
    {
        if(hwnd==own || !IsExternalWindow(hwnd) || !GetWindowRect(hwnd,out var r))return false;
        var b=screen.Bounds;return r.Left<=b.Left && r.Top<=b.Top && r.Right>=b.Right && r.Bottom>=b.Bottom;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder text,int max);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    internal static void DarkTitleBar(Window window)
    {var handle=new WindowInteropHelper(window).Handle;int dark=1;DwmSetWindowAttribute(handle,20,ref dark,sizeof(int));}
}

internal sealed class OutsideClick : IDisposable
{
    private IntPtr hook;
    private readonly Native.HookProc callback;
    public OutsideClick(Window window,Action close,Func<System.Windows.Point,bool>? contains=null)
    {
        callback=(code,wparam,lparam)=>
        {
            if(code>=0 && ((int)wparam==0x201 || (int)wparam==0x204))
            {
                var p=Marshal.PtrToStructure<Native.Point>(lparam);
                Native.GetWindowRect(new WindowInteropHelper(window).Handle,out var r);
                var local=window.PointFromScreen(new System.Windows.Point(p.X,p.Y));
                if(p.X<r.Left || p.X>=r.Right || p.Y<r.Top || p.Y>=r.Bottom || (contains!=null && !contains(local)))
                    window.Dispatcher.BeginInvoke(close);
            }
            return Native.CallNextHookEx(hook,code,wparam,lparam);
        };
        hook=Native.SetWindowsHookEx(14,callback,Native.GetModuleHandle(null),0);
    }
    public void Dispose() { if(hook!=IntPtr.Zero) Native.UnhookWindowsHookEx(hook); hook=IntPtr.Zero; GC.KeepAlive(callback); }
}
