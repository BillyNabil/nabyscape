using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Automation;
using System.Windows.Threading;

namespace NabyScape;

internal static class AssistantWindowChrome
{
    internal static UIElement Frame(Window window,string title,UIElement content)
    {
        window.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/NabyScape;component/Features/Assistant/AssistantTheme.xaml",UriKind.Relative)});
        window.WindowStyle=WindowStyle.None;window.ResizeMode=ResizeMode.CanResize;window.Background=UI.Brush("#0D0E10");window.Foreground=Brushes.White;window.UseLayoutRounding=true;
        WindowChrome.SetWindowChrome(window,new WindowChrome{CaptionHeight=38,ResizeBorderThickness=new(6),GlassFrameThickness=new(0),CornerRadius=new(12),UseAeroCaptionButtons=false});
        var frame=new Grid();frame.RowDefinitions.Add(new(){Height=new GridLength(38)});frame.RowDefinitions.Add(new());
        var bar=new DockPanel{Background=UI.Brush("#15171A")};
        var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);bar.Children.Add(actions);
        void Caption(string text,string name,Action action)
        {var button=UI.Button(text,action);AutomationProperties.SetName(button,name);button.ToolTip=name;button.Width=40;button.MinHeight=38;button.Margin=new(0);button.Padding=new(0);button.Background=Brushes.Transparent;button.Tag=new CornerRadius(0);WindowChrome.SetIsHitTestVisibleInChrome(button,true);actions.Children.Add(button);}
        Caption("—","Minimize",()=>window.WindowState=WindowState.Minimized);Caption("□","Maximize or restore",()=>window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized);Caption("×","Close",window.Close);
        var label=UI.Text(title,12);label.FontWeight=FontWeights.SemiBold;label.Margin=new(15,0,0,0);bar.Children.Add(label);frame.Children.Add(bar);Grid.SetRow(content,1);frame.Children.Add(content);
        window.StateChanged+=(_,_)=>frame.Margin=window.WindowState==WindowState.Maximized?new Thickness(7):new Thickness(0);
        window.SourceInitialized+=(_,_)=>
        {var handle=new WindowInteropHelper(window).Handle;int dark=1,rounded=2;DwmSetWindowAttribute(handle,20,ref dark,4);DwmSetWindowAttribute(handle,33,ref rounded,4);SetWindowDisplayAffinity(handle,0x11);};
        return frame;
    }
    internal static void Place(Window window,Settings settings,double desiredWidth,double desiredHeight)
    {
        var screen=Native.ChooseScreen(settings);double scale=Native.Scale(screen);var bounds=screen.WorkingArea;
        double width=Math.Max(240,bounds.Width/scale-24),height=Math.Max(180,bounds.Height/scale-24);
        window.MinWidth=Math.Min(360,width);window.MinHeight=Math.Min(300,height);window.Width=Math.Min(desiredWidth,width);window.Height=Math.Min(desiredHeight,height);
        window.Left=bounds.Left/scale+(bounds.Width/scale-window.Width)/2;window.Top=bounds.Top/scale+Math.Min(60,Math.Max(12,(bounds.Height/scale-window.Height)/2));
        window.MaxWidth=width+24;window.MaxHeight=height+24;
        var reflow=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};
        void Schedule(){if(window.IsLoaded){reflow.Stop();reflow.Start();}}
        void DisplayChanged(object? sender,EventArgs args)=>window.Dispatcher.BeginInvoke(Schedule);
        reflow.Tick+=(_,_)=>
        {
            reflow.Stop();if(!window.IsVisible || window.WindowState!=WindowState.Normal)return;
            var current=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle);double dpi=Native.Scale(current);var area=current.WorkingArea;
            double availableWidth=Math.Max(240,area.Width/dpi-16),availableHeight=Math.Max(180,area.Height/dpi-16);
            window.MinWidth=Math.Min(360,availableWidth);window.MinHeight=Math.Min(300,availableHeight);window.MaxWidth=availableWidth+16;window.MaxHeight=availableHeight+16;
            if(window.Width>availableWidth)window.Width=availableWidth;if(window.Height>availableHeight)window.Height=availableHeight;
            double left=Math.Clamp(window.Left,area.Left/dpi,Math.Max(area.Left/dpi,area.Right/dpi-window.Width));double top=Math.Clamp(window.Top,area.Top/dpi,Math.Max(area.Top/dpi,area.Bottom/dpi-window.Height));
            if(Math.Abs(left-window.Left)>.5)window.Left=left;if(Math.Abs(top-window.Top)>.5)window.Top=top;
        };
        window.LocationChanged+=(_,_)=>Schedule();window.DpiChanged+=(_,_)=>Schedule();Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        window.Closed+=(_,_)=>{reflow.Stop();Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;};
    }
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("user32.dll")]private static extern bool SetWindowDisplayAffinity(IntPtr window,uint affinity);
}
