using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private IntPtr settingsHandle;
    private bool fittingBounds,boundsQueued,interactiveBounds,boundsRecoveryQueued;
    private HwndSource? settingsSource;
    internal static bool ShouldFitSettingsBounds(bool interactive,bool recovery)=>!interactive || recovery;
    internal static Rect FitWorkingArea(Rect bounds,Rect area,double scale)
    {
        double width=Math.Min(bounds.Width,area.Width),height=Math.Min(bounds.Height,area.Height);
        width=Math.Max(Math.Min(480*scale,area.Width),width);height=Math.Max(Math.Min(300*scale,area.Height),height);
        return new(Math.Clamp(bounds.X,area.Left,area.Right-width),Math.Clamp(bounds.Y,area.Top,area.Bottom-height),width,height);
    }
    private void InitializeSettingsBounds()
    {
        var openingScreen=Forms.Screen.FromPoint(Forms.Cursor.Position);
        SourceInitialized+=(_,_)=>
        {
            Native.DarkTitleBar(this);settingsHandle=new WindowInteropHelper(this).Handle;
            settingsSource=HwndSource.FromHwnd(settingsHandle);settingsSource?.AddHook(SettingsWindowMessage);
            FitSettingsBounds(openingScreen,true);
        };
        LocationChanged+=(_,_)=>QueueSettingsBounds();
        SizeChanged+=(_,_)=>QueueSettingsBounds();
        DpiChanged+=(_,_)=>QueueSettingsBounds(true);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=SettingsDisplaysChanged;
        Closed+=(_,_)=>{Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=SettingsDisplaysChanged;settingsSource?.RemoveHook(SettingsWindowMessage);settingsSource=null;settingsHandle=IntPtr.Zero;};
    }
    private IntPtr SettingsWindowMessage(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam,ref bool handled)
    {
        if(message==0x0231)interactiveBounds=true; // WM_ENTERSIZEMOVE
        else if(message==0x0232){interactiveBounds=false;QueueSettingsBounds();} // WM_EXITSIZEMOVE
        return IntPtr.Zero;
    }
    private void SettingsDisplaysChanged(object? sender,EventArgs e)=>Dispatcher.BeginInvoke(()=>QueueSettingsBounds(true));
    private void QueueSettingsBounds(bool recovery=false)
    {
        if(settingsHandle==IntPtr.Zero || fittingBounds)return;
        boundsRecoveryQueued|=recovery;
        if(boundsQueued)return;boundsQueued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,()=>
        {
            boundsQueued=false;bool recover=boundsRecoveryQueued;boundsRecoveryQueued=false;
            if(settingsHandle!=IntPtr.Zero && IsVisible && ShouldFitSettingsBounds(interactiveBounds,recover))FitSettingsBounds(Forms.Screen.FromHandle(settingsHandle),false);
        });
    }
    private void FitSettingsBounds(Forms.Screen screen,bool initial)
    {
        if(fittingBounds)return;fittingBounds=true;
        try
        {
            double scale=Native.Scale(screen);var work=screen.WorkingArea;var area=new Rect(work.Left,work.Top,work.Width,work.Height);
            MinWidth=Math.Min(480,area.Width/scale);MinHeight=Math.Min(300,area.Height/scale);
            if(!Native.GetWindowRect(settingsHandle,out var native))return;
            var bounds=new Rect(native.Left,native.Top,native.Right-native.Left,native.Bottom-native.Top);
            if(initial)
            {
                double width=Math.Min(1100*scale,area.Width-32),height=Math.Min(780*scale,area.Height-32);
                bounds=new(area.Left+(area.Width-width)/2,area.Top+(area.Height-height)/2,width,height);
            }
            var fitted=FitWorkingArea(bounds,area,scale);
            if(initial || fitted!=bounds)Native.SetWindowPos(settingsHandle,IntPtr.Zero,(int)Math.Round(fitted.X),(int)Math.Round(fitted.Y),(int)Math.Round(fitted.Width),(int)Math.Round(fitted.Height),0x14);
        }
        finally{fittingBounds=false;}
    }
}
