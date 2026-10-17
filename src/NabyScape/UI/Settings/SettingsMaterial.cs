using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace NabyScape;
public sealed partial class SettingsWindow
{
    private readonly TextBlock materialStatus=UI.Text("",11,UI.Muted);
    private readonly DispatcherTimer materialTimer=new(){Interval=TimeSpan.FromSeconds(3)};
    private (string,SettingsBackdrop.Conditions)? materialKey;
    internal SettingsBackdrop.Result MaterialResult {get;private set;}
    private void InitializeMaterial()
    {
        SourceInitialized+=(_,_)=>ApplyMaterial();
        materialTimer.Tick+=(_,_)=>ApplyMaterial();Loaded+=(_,_)=>materialTimer.Start();Closed+=(_,_)=>materialTimer.Stop();
    }
    internal void ApplyMaterial()
    {
        var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero)return;
        string requested=SettingsBackdrop.Requested(app.Settings);var conditions=SettingsBackdrop.ReadConditions();
        if(materialKey is {} key && key==(requested,conditions))return;materialKey=(requested,conditions);
        MaterialResult=SettingsBackdrop.Resolve(requested,conditions,type=>SettingsBackdrop.Apply(hwnd,type));
        if(!MaterialResult.Native)SettingsBackdrop.Apply(hwnd,1);
        // Apply only the final frame state. Cycling through full glass for a solid
        // surface needlessly discards retained WPF client resources.
        var chrome=WindowChrome.GetWindowChrome(this);chrome.GlassFrameThickness=MaterialResult.Native?new Thickness(-1):new Thickness(0);
        var source=HwndSource.FromHwnd(hwnd);if(source?.CompositionTarget!=null)source.CompositionTarget.BackgroundColor=MaterialResult.Native?Colors.Transparent:Color.FromRgb(17,18,20);
        // Retain native blur beneath a dark client tint so secondary text stays readable.
        Background=MaterialResult.Native?UI.Brush("#B0111214"):MaterialResult.Actual=="Simulated glass"?IslandSurface.Material("#111214"):UI.Brush("#111214");
        materialStatus.Text=MaterialResult.Status+" Native materials apply to Settings only. The island preview keeps its existing material.";
        // Repaint retained examples once after the HWND composition change settles;
        // do not rebuild controls or attach a continuous rendering callback.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,()=>{if(IsVisible)foreach(var scene in settingExamples)scene.InvalidateVisual();});
    }
    private void MaterialControls()
    {
        var choice=new ComboBox{ItemsSource=SettingsBackdrop.Choices,SelectedItem=SettingsBackdrop.Requested(app.Settings)};UI.Named(choice,"Settings material");
        choice.SelectionChanged+=(_,_)=>{app.Settings.SettingsMaterial=choice.SelectedItem?.ToString()??"";ApplyMaterial();Save();};
        page.Children.Add(Section("Settings material",choice,materialStatus,UI.Text("Mica is an opaque wallpaper/theme-based foundation. Acrylic is a translucent system blur. This illustration is schematic; see the Settings window itself for the active material. Island glass and its shaped corners remain unchanged.",11,UI.Muted)));
    }
}
