using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace NabyScape;

// Maximize, restore, minimize and foreground switches re-evaluate automatic tucking right away
// instead of waiting for the two-second runtime tick.
public sealed partial class OverlayWindow
{
    private delegate void WinEventProc(IntPtr hook,uint eventType,IntPtr hwnd,int idObject,int idChild,uint thread,uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin,uint eventMax,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    private const uint EventForeground=0x0003,EventMinimizeStart=0x0016,EventMinimizeEnd=0x0017,EventLocationChange=0x800B,OutOfContextSkipOwnProcess=0x0000|0x0002;
    private readonly DispatcherTimer environmentDebounce=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(120)};
    private WinEventProc? environmentCallback;
    private IntPtr foregroundHook,minimizeHook,locationHook;
    private bool revealOverride;
    private bool EnvironmentRulesActive=>app.Settings.HideMaximized || app.Settings.HideFullscreen;
    private void InitializeEnvironmentWatch()
    {
        environmentDebounce.Tick+=(_,_)=>{environmentDebounce.Stop();if(IsLoaded && !displayRecoveryDisposed)CheckEnvironment();};
        app.SettingsChanged+=ConfigureEnvironmentWatch;ConfigureEnvironmentWatch();
    }
    internal void QueueEnvironmentCheck(){if(displayRecoveryDisposed)return;environmentDebounce.Stop();environmentDebounce.Start();}
    private void ConfigureEnvironmentWatch()
    {
        bool wanted=EnvironmentRulesActive && !DiagnosticRun;
        if(wanted==(foregroundHook!=IntPtr.Zero))return;
        if(!wanted){StopEnvironmentWatch();return;}
        environmentCallback??=EnvironmentEvent;
        foregroundHook=SetWinEventHook(EventForeground,EventForeground,IntPtr.Zero,environmentCallback,0,0,OutOfContextSkipOwnProcess);
        minimizeHook=SetWinEventHook(EventMinimizeStart,EventMinimizeEnd,IntPtr.Zero,environmentCallback,0,0,OutOfContextSkipOwnProcess);
        locationHook=SetWinEventHook(EventLocationChange,EventLocationChange,IntPtr.Zero,environmentCallback,0,0,OutOfContextSkipOwnProcess);
    }
    private void EnvironmentEvent(IntPtr hook,uint eventType,IntPtr hwnd,int idObject,int idChild,uint thread,uint time)
    {
        // Location changes fire for every moving window and caret; only the active window's frame matters.
        if(idObject!=0 || idChild!=0)return;
        if(eventType==EventLocationChange && hwnd!=Native.GetForegroundWindow())return;
        QueueEnvironmentCheck();
    }
    private void StopEnvironmentWatch()
    {
        foreach(var hook in new[]{foregroundHook,minimizeHook,locationHook})if(hook!=IntPtr.Zero)UnhookWinEvent(hook);
        foregroundHook=minimizeHook=locationHook=IntPtr.Zero;environmentDebounce.Stop();
    }
    // An override created by opening the panel lasts only while it is open; closing it lets
    // "Tuck above maximized/fullscreen apps" apply again to the same app.
    private void ReleaseRevealOverride()
    {
        if(!revealOverride)return;
        revealOverride=false;visibilityOverride=IntPtr.Zero;QueueEnvironmentCheck();
    }
}
