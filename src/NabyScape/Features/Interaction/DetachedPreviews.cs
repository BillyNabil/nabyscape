using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shell;
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
        if(disposed || !app.Settings.DetachablePreviews || kind is "calendar" or "notes" or "files" && !app.Settings.ToolEnabled(kind))return false;
        string widget=kind is "calendar" or "notes" or "files"?"tools":kind;
        return kind is "stopwatch" or "media" or "timer" or "focus" or "calendar" or "notes" or "files" or "system"
            && (app.Settings.HoverViews.Any(v=>v.Id==kind && v.Enabled) || app.Settings.Widgets.Any(w=>w.Id==widget && w.Enabled) || kind=="media" && app.Settings.Widgets.Any(w=>w.Id=="volume" && w.Enabled));
    }
    public void Open(string kind)=>OpenCore(kind,null,false);
    public void OpenAt(string kind,Point screenPixels,bool drag)=>OpenCore(kind,screenPixels,drag);
    private void OpenCore(string kind,Point? screenPixels,bool drag)
    {
        kind=NormalizeKind(kind);if(!CanOpen(kind))return;
        if(!windows.TryGetValue(kind,out var window))
        {
            window=new(app,kind,()=>openMain(kind));windows.Add(kind,window);state.Open(kind);
            window.Closed+=(_,_)=>{windows.Remove(kind);state.Close(kind);};
        }
        if(state.Hidden)return;
        if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;
        window.Show();if(screenPixels is Point anchor)window.PlaceAt(anchor);else window.EnsureInitialPlacement();window.ClampToMonitor();window.Activate();
        if(drag)window.BeginPointerDrag();
    }
    public bool AnyVisible(string kind)=>state.Visible(NormalizeKind(kind)) && windows.TryGetValue(NormalizeKind(kind),out var window) && window.IsVisible && window.WindowState!=WindowState.Minimized;
    public void SetHidden(bool hidden)
    {
        if(disposed || hidden==state.Hidden)return;state.SetHidden(hidden);
        foreach(var window in windows.Values.ToArray())if(hidden)window.Hide();else {window.Show();window.EnsureInitialPlacement();window.ClampToMonitor();}
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
    private bool clamping,restored,placed;
    internal DetachedPreviewWindow(App app,string kind,Action openMain)
    {
        this.app=app;this.kind=kind;
        Title="NabyScape · "+NameFor(kind);Width=350;Height=330;MinWidth=280;MinHeight=220;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;ShowInTaskbar=true;Topmost=false;Background=UI.Brush("#101114");Foreground=Brushes.White;
        FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI");UseLayoutRounding=true;
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new(6),CornerRadius=new(16),GlassFrameThickness=new(0),UseAeroCaptionButtons=false});
        UI.Named(this,Title);var layout=new DockPanel();
        var header=new DockPanel{Margin=new(8,4,2,10),LastChildFill=true,Background=Brushes.Transparent};DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        Button WindowButton(string symbol,string name,Action action)
        {var button=UI.Button(name,action);button.Content=Icons.Create(symbol=="×"?"x":"minus",13);button.Width=28;button.Height=28;button.MinHeight=28;button.Padding=new(0);button.Margin=new(2,0,0,0);button.ToolTip=name;button.FontSize=14;button.Background=Brushes.Transparent;button.BorderThickness=new(0);WindowChrome.SetIsHitTestVisibleInChrome(button,true);DockPanel.SetDock(button,Dock.Right);return button;}
        header.Children.Add(WindowButton("×","Close "+NameFor(kind)+" · Alt+F4",Close));
        header.Children.Add(WindowButton("−","Minimize "+NameFor(kind),()=>WindowState=WindowState.Minimized));
        var pin=new CheckBox{Content="On top",IsChecked=false,Foreground=UI.Muted,VerticalAlignment=VerticalAlignment.Center,FontSize=10,MinHeight=28,Margin=new(8,0,6,0),ToolTip="Keep this mini window above other apps"};pin.Style=(Style)app.FindResource("SettingsSwitch");UI.Named(pin,"Always on top");pin.Checked+=(_,_)=>Topmost=true;pin.Unchecked+=(_,_)=>Topmost=false;DockPanel.SetDock(pin,Dock.Right);header.Children.Add(pin);
        var handle=UI.Heading(NameFor(kind),13);handle.FontWeight=FontWeights.SemiBold;handle.Cursor=Cursors.SizeAll;handle.ToolTip="Drag to move · Alt+Space for window controls";header.Children.Add(handle);
        header.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is not DependencyObject target || IsControl(target,header))return;if(e.ClickCount==1){BeginPointerDrag();e.Handled=true;}};
        view=new(app,kind,openMain);layout.Children.Add(view.Content);
        surface=new Border{Padding=new(10),CornerRadius=new(16),BorderThickness=new(1),Child=layout};surface.SetResourceReference(Border.BorderBrushProperty,"ControlBorder");Content=surface;ApplySettings();
        try{if(File.Exists(Path.Combine(app.Store.DirectoryPath,"detached-"+kind+".json"))){var stored=app.Store.Load<DetachedPreviewGeometry>("detached-"+kind);var rect=SafeRect(stored);Left=rect.Left;Top=rect.Top;Width=rect.Width;Height=rect.Height;restored=true;}}catch(Exception ex){app.Store.Log(ex);}
        saveGeometry.Tick+=(_,_)=>{ClampToMonitor();PersistGeometry();};LocationChanged+=(_,_)=>ScheduleSave();SizeChanged+=(_,_)=>ScheduleSave();
        IsVisibleChanged+=(_,_)=>{view.SetVisible(IsVisible && WindowState!=WindowState.Minimized);if(!IsVisible){view.Flush();PersistGeometry();}};
        StateChanged+=(_,_)=>{view.SetVisible(IsVisible && WindowState!=WindowState.Minimized);if(WindowState==WindowState.Minimized){view.Flush();saveGeometry.Stop();}};
        SourceInitialized+=(_,_)=>{Native.DarkTitleBar(this);source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source?.AddHook(Messages);};
        Closed+=(_,_)=>{view.Dispose();PersistGeometry();source?.RemoveHook(Messages);source=null;};
        PreviewKeyDown+=(_,e)=>{if(e.SystemKey==Key.Space && (Keyboard.Modifiers&ModifierKeys.Alt)!=0){SystemCommands.ShowSystemMenu(this,PointToScreen(new Point(12,38)));e.Handled=true;}};
    }
    private static bool IsControl(DependencyObject target,DependencyObject header)
    {
        for(var current=target;current!=null && current!=header;current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current))if(current is System.Windows.Controls.Primitives.ButtonBase)return true;
        return false;
    }
    internal void BeginPointerDrag()
    {
        // Finish the originating preview's mouse event before transferring its gesture.
        Dispatcher.BeginInvoke(()=>{if(!IsVisible || Mouse.LeftButton!=MouseButtonState.Pressed)return;Mouse.Capture(null);try{DragMove();}catch(InvalidOperationException){}},DispatcherPriority.Input);
    }
    private void PlaceNearIsland()
    {
        if(app.MainWindow is OverlayWindow island && new WindowInteropHelper(island).Handle!=IntPtr.Zero)
        {PlaceAt(island.PointToScreen(new Point(island.ActualWidth/2,Math.Max(24,island.PaintedHeight)+18)));return;}
        var area=Native.ChooseScreen(app.Settings).WorkingArea;PlaceAt(new Point(area.Left+area.Width/2d,area.Top+64));
    }
    internal void EnsureInitialPlacement(){if(placed)return;placed=true;if(!restored)PlaceNearIsland();}
    internal void PlaceAt(Point screenPixels)
    {
        if(!double.IsFinite(screenPixels.X) || !double.IsFinite(screenPixels.Y))return;
        placed=true;
        var screen=Forms.Screen.FromPoint(new System.Drawing.Point(Pixel(screenPixels.X),Pixel(screenPixels.Y)));
        double scale=MonitorScale(screen);var area=screen.WorkingArea;
        // Put the pointer over the empty title area; moving can continue immediately.
        var candidate=new Rect(screenPixels.X-48*scale,screenPixels.Y-22*scale,Width*scale,Height*scale);
        var rect=DetachedPreviewGeometry.Clamp(candidate,new Rect(area.X,area.Y,area.Width,area.Height));
        SetPhysicalBounds(rect,scale);
    }
    private static int Pixel(double value)=>(int)Math.Clamp(Math.Round(value),int.MinValue,int.MaxValue);
    private static double MonitorScale(Forms.Screen screen)
    {
        try{var area=screen.Bounds;var monitor=MonitorFromPoint(new(area.Left+area.Width/2,area.Top+area.Height/2),2);if(GetDpiForMonitor(monitor,0,out var dpi,out _)==0 && dpi>0)return dpi/96d;}
        catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
        return 1;
    }
    private void SetPhysicalBounds(Rect rect,double scale)
    {
        var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero)return;
        bool wasClamping=clamping;clamping=true;
        try{MinWidth=Math.Min(280,rect.Width/scale);MinHeight=Math.Min(220,rect.Height/scale);Native.SetWindowPos(hwnd,IntPtr.Zero,Pixel(rect.X),Pixel(rect.Y),Pixel(rect.Width),Pixel(rect.Height),0x0014);}
        finally{clamping=wasClamping;}
        ScheduleSave();
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
            var hwnd=new WindowInteropHelper(this).Handle;
            if(!Native.GetWindowRect(hwnd,out var bounds))return;
            var screen=Forms.Screen.FromHandle(hwnd);var area=screen.WorkingArea;double scale=MonitorScale(screen);
            var work=new Rect(area.Left,area.Top,area.Width,area.Height);
            var candidate=new Rect(bounds.Left,bounds.Top,Math.Max(1,bounds.Right-bounds.Left),Math.Max(1,bounds.Bottom-bounds.Top));
            var rect=DetachedPreviewGeometry.Clamp(candidate,work);
            MinWidth=Math.Min(280,work.Width/scale);MinHeight=Math.Min(220,work.Height/scale);
            if(rect!=candidate)SetPhysicalBounds(rect,scale);
        }finally{clamping=false;}
    }
    internal void ApplySettings(){surface.Background=UI.Brush(app.Settings.Background);surface.Opacity=app.Settings.Opacity;view?.ApplySettings();}
    private void ScheduleSave(){if(clamping)return;saveGeometry.Stop();if(IsVisible)saveGeometry.Start();}
    private void PersistGeometry()
    {
        saveGeometry.Stop();if(WindowState!=WindowState.Normal || new WindowInteropHelper(this).Handle==IntPtr.Zero || !double.IsFinite(Left) || !double.IsFinite(Top))return;
        try{app.Store.Save("detached-"+kind,new DetachedPreviewGeometry{Left=Left,Top=Top,Width=Width,Height=Height});}catch(Exception ex){app.Store.Log(ex);}
    }
}
