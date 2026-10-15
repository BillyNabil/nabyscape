using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace NabyScape;

internal sealed class DetachedPreviewState
{
    private readonly HashSet<string> open=new(StringComparer.Ordinal);
    internal bool Hidden {get;private set;}
    internal bool Open(string kind)=>open.Add(kind);
    internal void Close(string kind)=>open.Remove(kind);
    internal void SetHidden(bool value)=>Hidden=value;
    internal bool Visible(string kind)=>!Hidden && open.Contains(kind);
}
public sealed class DetachedPreviewGeometry
{
    public double Left {get;set;}=80;
    public double Top {get;set;}=80;
    public double Width {get;set;}=350;
    public double Height {get;set;}=330;
    internal static Rect Clamp(Rect candidate,Rect work)
    {
        double width=Math.Clamp(double.IsFinite(candidate.Width)?candidate.Width:350,Math.Min(280,work.Width),work.Width);
        double height=Math.Clamp(double.IsFinite(candidate.Height)?candidate.Height:330,Math.Min(220,work.Height),work.Height);
        double left=double.IsFinite(candidate.X)?candidate.X:work.Left+24,top=double.IsFinite(candidate.Y)?candidate.Y:work.Top+24;
        return new(Math.Clamp(left,work.Left,work.Right-width),Math.Clamp(top,work.Top,work.Bottom-height),width,height);
    }
}
public sealed class DetachedPreviewManager : IDisposable
{
    private readonly App app;
    private readonly Action<string> openMain;
    private readonly Dictionary<string,DetachedPreviewWindow> windows=new();
    private readonly DetachedPreviewState state=new();
    private bool disposed;
    public DetachedPreviewManager(App app,Action<string> openMainPage){this.app=app;openMain=openMainPage;}
    internal static string NormalizeKind(string kind)=>kind.ToLowerInvariant() switch {"music"=>"media","shelf"=>"files","note"=>"notes",var value=>value};
    public bool CanOpen(string kind)
    {
        kind=NormalizeKind(kind);
        if(disposed || !app.Settings.DetachablePreviews)return false;
        string widget=kind is "calendar" or "notes" or "files"?"tools":kind;
        return kind is "media" or "timer" or "focus" or "calendar" or "notes" or "files" or "system"
            && (app.Settings.Widgets.Any(w=>w.Id==widget && w.Enabled) || kind=="media" && app.Settings.Widgets.Any(w=>w.Id=="volume" && w.Enabled));
    }
    public void Open(string kind)
    {
        kind=NormalizeKind(kind);if(!CanOpen(kind))return;
        if(!windows.TryGetValue(kind,out var window))
        {
            window=new(app,kind,()=>openMain(kind));windows.Add(kind,window);state.Open(kind);
            window.Closed+=(_,_)=>{windows.Remove(kind);state.Close(kind);};
        }
        if(state.Hidden)return;
        window.Show();window.ClampToMonitor();if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;window.Activate();
    }
    public bool AnyVisible(string kind)=>state.Visible(NormalizeKind(kind)) && windows.TryGetValue(NormalizeKind(kind),out var window) && window.IsVisible && window.WindowState!=WindowState.Minimized;
    public void SetHidden(bool hidden)
    {
        if(disposed || hidden==state.Hidden)return;state.SetHidden(hidden);
        foreach(var window in windows.Values.ToArray())if(hidden)window.Hide();else {window.Show();window.ClampToMonitor();}
    }
    public void ApplySettings()
    {
        foreach(var pair in windows.ToArray())if(!CanOpen(pair.Key))pair.Value.Close();else pair.Value.ApplySettings();
    }
    public void Dispose(){if(disposed)return;disposed=true;foreach(var window in windows.Values.ToArray())window.Close();}
}
internal sealed class DetachedPreviewWindow : Window
{
    private readonly App app;
    private readonly string kind;
    private readonly DetachedPreviewView view;
    private readonly Border surface;
    private readonly DispatcherTimer saveGeometry=new(){Interval=TimeSpan.FromMilliseconds(400)};
    private HwndSource? source;
    private bool clamping;
    internal DetachedPreviewWindow(App app,string kind,Action openMain)
    {
        this.app=app;this.kind=kind;
        Title="NabyScape · "+NameFor(kind);Width=350;Height=330;MinWidth=280;MinHeight=220;
        WindowStyle=WindowStyle.SingleBorderWindow;ResizeMode=ResizeMode.CanResize;ShowInTaskbar=true;Topmost=false;Background=Brushes.Black;Foreground=Brushes.White;
        UI.Named(this,Title);var layout=new DockPanel();
        var header=new DockPanel{Margin=new(12,6,10,8),LastChildFill=true};DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        var pin=new CheckBox{Content="Always on top",IsChecked=false,Foreground=UI.Muted,VerticalAlignment=VerticalAlignment.Center,FontSize=11};pin.Style=(Style)app.FindResource("SettingsSwitch");UI.Named(pin,"Always on top");pin.Checked+=(_,_)=>Topmost=true;pin.Unchecked+=(_,_)=>Topmost=false;DockPanel.SetDock(pin,Dock.Right);header.Children.Add(pin);
        var handle=UI.Text(NameFor(kind),13);handle.FontWeight=FontWeights.SemiBold;handle.Cursor=Cursors.SizeAll;handle.ToolTip="Drag to move · Alt+Space for window controls";handle.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==1){DragMove();e.Handled=true;}};header.Children.Add(handle);
        view=new(app,kind,openMain);layout.Children.Add(view.Content);
        surface=new Border{Padding=new(8),Child=layout};Content=surface;ApplySettings();
        try{var stored=app.Store.Load<DetachedPreviewGeometry>("detached-"+kind);var rect=SafeRect(stored);Left=rect.Left;Top=rect.Top;Width=rect.Width;Height=rect.Height;}catch(Exception ex){app.Store.Log(ex);}
        saveGeometry.Tick+=(_,_)=>{ClampToMonitor();PersistGeometry();};LocationChanged+=(_,_)=>ScheduleSave();SizeChanged+=(_,_)=>ScheduleSave();
        IsVisibleChanged+=(_,_)=>{view.SetVisible(IsVisible && WindowState!=WindowState.Minimized);if(!IsVisible){view.Flush();PersistGeometry();}};
        StateChanged+=(_,_)=>{view.SetVisible(IsVisible && WindowState!=WindowState.Minimized);if(WindowState==WindowState.Minimized){view.Flush();saveGeometry.Stop();}};
        SourceInitialized+=(_,_)=>{Native.DarkTitleBar(this);source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source?.AddHook(Messages);ClampToMonitor();};
        Closed+=(_,_)=>{view.Dispose();PersistGeometry();source?.RemoveHook(Messages);source=null;};
    }
    internal static string NameFor(string kind)=>kind switch{"media"=>"Music","files"=>"Shelf","notes"=>"Notes",_=>char.ToUpperInvariant(kind[0])+kind[1..]};
    private static Rect SafeRect(DetachedPreviewGeometry value)=>new(double.IsFinite(value.Left)?value.Left:80,double.IsFinite(value.Top)?value.Top:80,double.IsFinite(value.Width)&&value.Width>0?value.Width:350,double.IsFinite(value.Height)&&value.Height>0?value.Height:330);
    private IntPtr Messages(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam,ref bool handled)
    {if(message is 0x007E or 0x02E0)Dispatcher.BeginInvoke(ClampToMonitor,DispatcherPriority.Loaded);return IntPtr.Zero;}
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromPoint(NativePoint point,uint flags);
    [DllImport("shcore.dll")]private static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
    [StructLayout(LayoutKind.Sequential)]private struct NativePoint{public int X,Y;public NativePoint(int x,int y){X=x;Y=y;}}
    internal void ClampToMonitor()
    {
        if(clamping || WindowState!=WindowState.Normal || new WindowInteropHelper(this).Handle==IntPtr.Zero)return;clamping=true;
        try
        {
            // Locate the nearest monitor in physical coordinates, then convert its work area to WPF units.
            var hwnd=new WindowInteropHelper(this).Handle;
            var screen=hwnd!=IntPtr.Zero?Forms.Screen.FromHandle(hwnd):Forms.Screen.FromPoint(new System.Drawing.Point((int)Left,(int)Top));
            var area=screen.WorkingArea;double scale=1;
            try{var monitor=MonitorFromPoint(new(area.Left+area.Width/2,area.Top+area.Height/2),2);if(GetDpiForMonitor(monitor,0,out var dpi,out _)==0 && dpi>0)scale=dpi/96d;}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
            var work=new Rect(area.Left/scale,area.Top/scale,area.Width/scale,area.Height/scale);
            var rect=DetachedPreviewGeometry.Clamp(new Rect(Left,Top,Width,Height),work);
            MinWidth=Math.Min(280,work.Width);MinHeight=Math.Min(220,work.Height);Left=rect.Left;Top=rect.Top;Width=rect.Width;Height=rect.Height;
        }finally{clamping=false;}
    }
    internal void ApplySettings(){surface.Background=UI.Brush(app.Settings.Background);surface.Opacity=app.Settings.Opacity;view?.ApplySettings();}
    private void ScheduleSave(){if(clamping)return;saveGeometry.Stop();if(IsVisible)saveGeometry.Start();}
    private void PersistGeometry()
    {
        saveGeometry.Stop();if(WindowState!=WindowState.Normal)return;
        try{app.Store.Save("detached-"+kind,new DetachedPreviewGeometry{Left=Left,Top=Top,Width=Width,Height=Height});}catch(Exception ex){app.Store.Log(ex);}
    }
}
