using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NabyScape;

internal static class Stage2Diagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        void Check(string name,bool pass)=>check("Stage 2: "+name,pass);
        var s=app.Settings;s.HideFullscreen=s.HideMaximized=false;s.ReduceMotion=true;s.SmartActivities=false;s.ActivitySatellite=false;island.DiagnosticEnvironment(IntPtr.Zero);app.ChangeSettings();island.Collapse();
        s.AlwaysTucked=true;app.ChangeSettings();island.CheckEnvironment();await Task.Delay(100);
        Check("always tucked leaves six DIP and stops live rendering",island.IsTucked && Math.Abs(island.VisibleBounds.Height-6)<.1 && !island.LiveRunning);
        foreach(double edge in new[]{3d,12d}){s.TuckEdge=edge;app.ChangeSettings();Check("configurable edge "+edge,Math.Abs(island.VisibleBounds.Height-edge)<.1);}
        s.TuckEdge=6;app.ChangeSettings();island.DiagnosticPointerPresent=true;island.RevealTuckedPreview();await Task.Delay(100);
        Check("edge hover opens preview without panel activation",island.IsPeeking && !island.IsExpanded && !island.IsTucked && !island.IsKeyboardFocusWithin);
        island.DiagnosticPointerPresent=false;island.Collapse();Check("leaving hover returns to tucked state",island.IsTucked);
        s.AlwaysTucked=false;app.ChangeSettings();island.CheckEnvironment();Check("automatic normal state returns visible",!island.IsTucked);
        island.ToggleTuck();Check("manual shortcut action latches tuck",island.ManualTuck && island.IsTucked);
        island.ToggleVisibility();island.ToggleTuck();Check("manual hide dominates latch release",island.UserHidden && !island.IsVisible && !island.ManualTuck);island.ToggleTuck();Check("manual tuck does not reveal manual hide",island.UserHidden && !island.IsVisible && island.ManualTuck);island.ToggleVisibility();island.Collapse();Check("manual show retains tuck latch",island.IsTucked);island.ToggleTuck();Check("second toggle returns to automatic rules",!island.ManualTuck && !island.IsTucked);
        foreach(string kind in new[]{"maximized","fullscreen"})
        {
            var screen=Native.ChooseScreen(s);double scale=Native.Scale(screen);var bounds=screen.Bounds;
            var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"NabyScape.exe")){UseShellExecute=false,CreateNoWindow=true};
            foreach(string arg in new[]{"--window-fixture",kind,(bounds.Left/scale).ToString(System.Globalization.CultureInfo.InvariantCulture),(bounds.Top/scale).ToString(System.Globalization.CultureInfo.InvariantCulture),(bounds.Width/scale).ToString(System.Globalization.CultureInfo.InvariantCulture),(bounds.Height/scale).ToString(System.Globalization.CultureInfo.InvariantCulture)})start.ArgumentList.Add(arg);
            using var external=Process.Start(start)!;
            try
            {
                var watch=Stopwatch.StartNew();while(external.MainWindowHandle==IntPtr.Zero && !external.HasExited && watch.ElapsedMilliseconds<5000){await Task.Delay(100);external.Refresh();}
                Check(kind+" external fixture ready",external.MainWindowHandle!=IntPtr.Zero);
                if(external.MainWindowHandle==IntPtr.Zero)continue;
                await Task.Delay(250);s.HideMaximized=s.HideFullscreen=true;app.ChangeSettings();island.DiagnosticEnvironment(external.MainWindowHandle);island.CheckEnvironment();
                Check(kind+" automatic rule tucks to six DIP",island.IsTucked && Math.Abs(island.VisibleBounds.Height-6)<.1);
                s.HideMaximized=s.HideFullscreen=false;app.ChangeSettings();island.CheckEnvironment();Check(kind+" disabled rule restores visible state",!island.IsTucked);
            }
            finally{island.DiagnosticEnvironment(IntPtr.Zero);if(!external.HasExited){external.CloseMainWindow();if(!await Task.Run(()=>external.WaitForExit(1500)))external.Kill();}}
        }
        PreferenceReset.Reset(app.Store,s,"Appearance");app.ChangeSettings();island.ShowTools("notes");await Task.Delay(50);var editor=Diagnostics.Descendants(island).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Quick notes");editor.Text="Pending note before reset";editor.Select(3,4);int builds=island.BodyBuilds;double left=island.Left;
        s.Accent="#112233";app.ChangeSettings();app.FlushPreferenceEditors();PreferenceReset.Reset(app.Store,s,"Appearance");app.ChangeSettings();await Task.Delay(100);
        var restored=Diagnostics.Descendants(island).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Quick notes");
        Check("reset flushes pending note and retains editor selection and position",app.Productivity.Note=="Pending note before reset" && restored.SelectionStart==3 && restored.SelectionLength==4 && island.Left==left && island.BodyBuilds==builds);
        PreferenceReset.Restore(app.Store,s);app.ChangeSettings();Check("restore updates appearance resources",s.Accent=="#112233");
        var settings=new SettingsWindow(app);try{settings.Show();typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(settings,new object[]{"Visibility"});await Task.Delay(100);
        Check("Settings exposes always edge and configurable tuck shortcut",new[]{"Always tucked","Tucked edge height","Enable tuck shortcut","Tuck hotkey key","Tuck hotkey modifiers"}.All(name=>Diagnostics.Descendants(settings).OfType<FrameworkElement>().Any(e=>AutomationProperties.GetName(e)==name)));
        int registrations=0;island.DiagnosticHotkeyRegistration=(_,_,_,_)=>{registrations++;return true;};
        try
        {
            s.HotkeyEnabled=s.TuckHotkeyEnabled=true;s.VisibilityHotkeyEnabled=false;s.TuckHotkeyKey=s.HotkeyKey;app.ChangeSettings();
            Check("duplicate panel/tuck registration is reported without native registration",app.TuckHotkeyStatus.Contains("conflicts") && app.HotkeyStatus.Contains("conflicts") && registrations==0);
            s.TuckHotkeyKey="T";app.ChangeSettings();Check("configured tuck registration invokes isolated success seam",registrations==2 && app.TuckHotkeyStatus.Contains("Ctrl+Alt+T"));
            island.DiagnosticHotkeyRegistration=(_,_,_,_)=>false;s.TuckHotkeyKey="Y";app.ChangeSettings();Check("registration failure reports actionable feedback",app.TuckHotkeyStatus.Contains("Choose another"));
        }
        finally{s.HotkeyEnabled=s.TuckHotkeyEnabled=s.VisibilityHotkeyEnabled=false;app.ChangeSettings();island.DiagnosticHotkeyRegistration=null;}
        typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(settings,new object[]{"Application"});
        Check("Application exposes reset and restore actions",new[]{"Reset this category","Reset all preferences","Restore last preference backup"}.All(name=>Diagnostics.Descendants(settings).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==name)));
        }finally{settings.Close();}
    }
}
