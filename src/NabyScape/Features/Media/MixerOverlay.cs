using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly AudioMixer audioMixer=new();
    private readonly StackPanel mixerRows=new();
    private readonly TextBlock mixerStatus=UI.Text("Open mixer to load audio devices.",11,UI.Muted);
    private DateTime mixerReadAt=DateTime.MinValue;
    private bool classicMixerOpen;
    private MixerScope mixerScope=MixerScope.Apps;
    private Button mixerAppsTab=null!,mixerOutputsTab=null!;
    private readonly Dictionary<MixerTarget,(FrameworkElement View,Action<MixerItem> Refresh)> mixerItemRows=new();

    internal void OpenMixer()
    {
        if(commandOpen)CloseCommands();
        if(app.Settings.CompactLayout)SelectPage("mixer");
        else{FlushNotes();classicMixerOpen=true;activePage="mixer";BuildBody();ResizeToolPanel();UpdateSystemMonitor();}
        mixerReadAt=DateTime.MinValue;Reveal();UpdateMixer();scroll.ScrollToTop();
    }

    private void CloseMixer()
    {
        if(!classicMixerOpen && activePage!="mixer")return;
        if(classicMixerOpen){classicMixerOpen=false;activePage=hoverPanelWidget==null?"media":PageFor(hoverPanelWidget);BuildBody();ResizeToolPanel();}
        else if(activePage=="mixer")SelectPage(compactTabs.ContainsKey("media")?"media":compactTabs.Keys.FirstOrDefault()??"media");
        scroll.ScrollToTop();
    }

    private FrameworkElement BuildMixerPage()
    {
        if(mixerRows.Parent is Panel previous)previous.Children.Remove(mixerRows);
        if(mixerStatus.Parent is Panel statusParent)statusParent.Children.Remove(mixerStatus);
        var panel=new StackPanel();
        var header=new DockPanel{Margin=new(0,0,0,12)};
        var back=MixerButton(app.Settings.CompactLayout?"Back to media":"Back to widgets",CloseMixer);DockPanel.SetDock(back,Dock.Left);header.Children.Add(back);
        var title=UI.Heading("Audio mixer",16);title.Margin=new(12,0,0,0);header.Children.Add(title);panel.Children.Add(header);
        mixerStatus.Margin=new(0,8,0,12);panel.Children.Add(mixerStatus);
        var scopes=new Grid{Margin=new(0,0,0,12)};scopes.ColumnDefinitions.Add(new());scopes.ColumnDefinitions.Add(new());
        mixerAppsTab=UI.Button("Apps",()=>ChooseMixerScope(MixerScope.Apps));mixerOutputsTab=UI.Button("Outputs",()=>ChooseMixerScope(MixerScope.Outputs));UI.Named(mixerAppsTab,"Show app volumes");UI.Named(mixerOutputsTab,"Show output volumes");
        foreach(var button in new[]{mixerAppsTab,mixerOutputsTab}){button.MinHeight=28;button.FontSize=11;button.Padding=new(5);button.Margin=new(0,0,4,0);scopes.Children.Add(button);}Grid.SetColumn(mixerOutputsTab,1);panel.Children.Add(scopes);UpdateMixerTabs();panel.Children.Add(mixerRows);return panel;
    }

    private Button MixerButton(string label,Action action)
    {
        var button=UI.Button(label,action);button.Template=(ControlTemplate)FindResource("ToolRowTemplate");
        button.Background=Brushes.Transparent;button.Padding=new(9,5,9,5);button.MinHeight=28;button.FontSize=11;
        void Feedback(){if(button.Template.FindName("RowLight",button) is Border light)Motion.Fade(light,button.IsMouseOver || button.IsKeyboardFocusWithin?.065:0,140);}
        button.MouseEnter+=(_,_)=>Feedback();button.MouseLeave+=(_,_)=>Feedback();button.GotKeyboardFocus+=(_,_)=>Feedback();button.LostKeyboardFocus+=(_,_)=>Feedback();return button;
    }

    private void ChooseMixerScope(MixerScope scope){mixerScope=scope;UpdateMixerTabs();mixerReadAt=DateTime.MinValue;UpdateMixer();scroll.ScrollToTop();}
    private void UpdateMixerTabs(){if(mixerAppsTab==null)return;foreach(var pair in new[]{(mixerAppsTab,MixerScope.Apps),(mixerOutputsTab,MixerScope.Outputs)}){pair.Item1.Background=pair.Item2==mixerScope?UI.Brush(app.Settings.SelectionFill):Brushes.Transparent;pair.Item1.Foreground=pair.Item2==mixerScope?UI.ContrastText(app.Settings.SelectionFill):UI.Muted;}}
    internal static bool MixerInteractionActive(bool mouseCaptured,bool sliderFocused,bool volumeKeyDown)=>mouseCaptured || (sliderFocused && volumeKeyDown);

    internal static void ReconcileMixerRows(StackPanel panel,IReadOnlyList<FrameworkElement> desired)
    {
        foreach(var child in panel.Children.Cast<FrameworkElement>().ToArray())
            if(!desired.Contains(child))panel.Children.Remove(child);
        // Existing rows stay attached, including the focused slider or mute button.
        for(int i=0;i<desired.Count;i++)
            if(!panel.Children.Contains(desired[i]))panel.Children.Insert(i,desired[i]);
    }

    private static bool MixerVolumeKey(Key key)=>key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;

    private void UpdateMixer()=>UpdateMixer(false);
    private void UpdateMixer(bool preserveResult)
    {
        if(!expanded || commandOpen || activePage!="mixer" || DateTime.UtcNow-mixerReadAt<TimeSpan.FromSeconds(2))return;
        bool editing=MixerInteractionActive(mixerRows.IsMouseCaptureWithin,Keyboard.FocusedElement is Slider slider && mixerRows.IsAncestorOf(slider),
            new[]{Key.Left,Key.Right,Key.Up,Key.Down,Key.PageUp,Key.PageDown,Key.Home,Key.End}.Any(Keyboard.IsKeyDown));
        if(editing)return;
        mixerReadAt=DateTime.UtcNow;var snapshot=audioMixer.Snapshot();
        var current=snapshot.Outputs.Concat(snapshot.Apps).Select(x=>x.Target).ToHashSet();
        foreach(var target in mixerItemRows.Keys.Where(x=>!current.Contains(x)).ToArray())mixerItemRows.Remove(target);
        FrameworkElement Row(MixerItem item)
        {
            if(!mixerItemRows.TryGetValue(item.Target,out var row))mixerItemRows[item.Target]=row=MixerItemRow(item);
            row.Refresh(item);return row.View;
        }
        var rows=new List<FrameworkElement>();
        if(!preserveResult)mixerStatus.Text=string.Join("; ",snapshot.Errors);
        mixerStatus.Visibility=string.IsNullOrWhiteSpace(mixerStatus.Text)?Visibility.Collapsed:Visibility.Visible;
        var items=mixerScope==MixerScope.Apps?snapshot.Apps:snapshot.Outputs;
        foreach(var item in items)rows.Add(Row(item));
        if(items.Count==0)rows.Add(UI.Text(mixerScope==MixerScope.Apps?"No app audio sessions available.":"No active audio outputs.",12,UI.Muted));
        ReconcileMixerRows(mixerRows,rows);
        double previousHeight=expandedHeight;Dimensions();
        if(Math.Abs(previousHeight-expandedHeight)>.5)motion.Go(surface,IslandSurface.SurfaceHeightProperty,expandedHeight,Math.Min(300,app.Settings.AnimationMs));
    }

    private (FrameworkElement View,Action<MixerItem> Refresh) MixerItemRow(MixerItem item)
    {
        string accessible=MixerAccessibleName(item);
        bool muted=item.Muted;Button? mute=null;
        mute=MixerButton(muted?"\uE74F":"\uE767",()=>
        {
            if(!ApplyMixer(item.Target,null,!muted))return;
            muted=!muted;Motion.Icon(mute!,muted?"muted":"volume",16);UI.Named(mute!,accessible+" "+(muted?"unmute":"mute"));
        });UI.Named(mute,accessible+" "+(muted?"unmute":"mute"));
        Motion.Icon(mute,muted?"muted":"volume",16);mute.FontSize=13;mute.Padding=new(3);mute.ToolTip=accessible+" mute / unmute";
        var (panel,slider,percent)=CreateMixerRowLayout(item,mute);
        bool pending=false,refreshing=false;
        slider.ValueChanged+=(_,_)=>{percent.Text=Math.Round(slider.Value)+"%";if(!refreshing)pending=true;};
        void Commit(){if(!pending)return;pending=false;ApplyMixer(item.Target,slider.Value/100,null);}
        slider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,_)=>Commit()));
        slider.PreviewMouseLeftButtonUp+=(_,_)=>Commit();slider.PreviewKeyUp+=(_,e)=>{if(MixerVolumeKey(e.Key))Commit();};
        slider.LostKeyboardFocus+=(_,_)=>Commit();
        return (panel,latest=>
        {
            muted=latest.Muted;Motion.Icon(mute,muted?"muted":"volume",16);UI.Named(mute,accessible+" "+(muted?"unmute":"mute"));
            refreshing=true;try{slider.Value=latest.Volume*100;}finally{refreshing=false;}
        });
    }

    private static string MixerAccessibleName(MixerItem item)
        =>(item.Target.Scope==MixerScope.Apps?"App":"Output")+" "+item.Name+(item.Target.Scope==MixerScope.Apps?" on "+item.OutputName:"");

    // Shared by the live mixer and layout-only tests; constructing a row never writes audio.
    internal static (Grid View,Slider Slider,TextBlock Percent) CreateMixerRowLayout(MixerItem item,Button mute)
    {
        string accessible=MixerAccessibleName(item);
        var header=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,6,0)};
        var label=UI.Text(item.Name,12);label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.ToolTip=accessible;UI.Named(label,accessible);header.Children.Add(label);
        if(item.Target.Scope==MixerScope.Apps)
        {
            var output=UI.Text(item.OutputName,10,UI.Muted);output.TextTrimming=TextTrimming.CharacterEllipsis;output.TextWrapping=TextWrapping.NoWrap;output.ToolTip=item.OutputName;header.Children.Add(output);
        }
        FrameworkElement icon=item.Target.Scope==MixerScope.Apps?new MixerAppIcon(item):Icons.Create("volume",20,UI.Muted);
        icon.Width=20;icon.Height=20;icon.VerticalAlignment=VerticalAlignment.Center;icon.Margin=new(0,0,8,0);
        var percent=UI.Text(Math.Round(item.Volume*100)+"%",11,UI.Muted);percent.Width=38;percent.TextAlignment=TextAlignment.Right;percent.VerticalAlignment=VerticalAlignment.Center;
        var slider=new Slider{Minimum=0,Maximum=100,Value=item.Volume*100,SmallChange=1,LargeChange=5,TickFrequency=1,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,Height=24,VerticalAlignment=VerticalAlignment.Center};UI.Named(slider,accessible+" volume percent");slider.ToolTip=accessible+" volume";
        // The global button style adds a trailing margin; reserve the full 30 DIP mute target.
        mute.Margin=new Thickness(0);
        var panel=new ResponsiveMixerRow(icon,header,slider,percent,mute);
        return (panel,slider,percent);
    }

    private sealed class ResponsiveMixerRow : Grid
    {
        internal ResponsiveMixerRow(FrameworkElement icon,FrameworkElement header,Slider slider,FrameworkElement percent,FrameworkElement mute)
        {
            Margin=new(0,0,0,14);MinHeight=58;
            ColumnDefinitions.Add(new(){Width=new GridLength(32)});ColumnDefinitions.Add(new());
            ColumnDefinitions.Add(new(){Width=new GridLength(42)});ColumnDefinitions.Add(new(){Width=new GridLength(30)});
            RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});
            Children.Add(icon);SetColumn(header,1);Children.Add(header);SetColumn(percent,2);Children.Add(percent);SetColumn(mute,3);Children.Add(mute);
            SetRow(slider,1);SetColumnSpan(slider,4);slider.Margin=new(32,5,8,0);Children.Add(slider);
        }
    }

    private bool ApplyMixer(MixerTarget target,double? volume,bool? mute)
    {
        string? error=audioMixer.Set(target,volume,mute);mixerStatus.Text=error==null?"":error;
        mixerReadAt=DateTime.MinValue;
        // Reconcile after the input event, when capture and key state have settled.
        Dispatcher.BeginInvoke(()=>UpdateMixer(true));
        return error==null;
    }
}
