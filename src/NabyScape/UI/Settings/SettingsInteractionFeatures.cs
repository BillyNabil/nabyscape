using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private void InteractionFeatureControls()
    {
        var s=app.Settings;
        var edge=new StackPanel();AddSlider(edge,"Tucked edge height",s.TuckEdge,0,12,v=>s.TuckEdge=v," px");edge.Children.Add(UI.Text("0 hides the dock completely. Use the tuck shortcut or hover the screen edge to reveal it.",11,UI.Muted));
        page.Children.Add(Section("Automatic tucking",Check("Always tucked",s.AlwaysTucked,v=>s.AlwaysTucked=v),edge,Check("Tuck above maximized apps",s.HideMaximized,v=>s.HideMaximized=v),Check("Tuck above fullscreen apps",s.HideFullscreen,v=>s.HideFullscreen=v),UI.Text("Leaves a small edge at the top when Chrome or another app fills this display. Move the pointer to that edge to reveal the preview. Manual hide still hides the whole island.",11,UI.Muted)));
        page.Children.Add(Section("Panel navigation",Check("Swipe between open panels",s.PanelSwipe,v=>s.PanelSwipe=v),UI.Text("Swipe empty space or use Shift+wheel to switch panels. Long lists keep vertical scrolling. Keyboard: Ctrl+Tab.",11,UI.Muted)));
        page.Children.Add(Section("Drag to Island",Check("Reveal shelf when dragging files",s.DropReveal,v=>s.DropReveal=v),UI.Text("Drag files onto the island or its tucked edge. Drop to stage references; select multiple shelf items to drag them out together. Manual hiding stays hidden.",11,UI.Muted)));
        page.Children.Add(Section("Detachable previews",Check("Allow detachable previews",s.DetachablePreviews,v=>s.DetachablePreviews=v),UI.Text("Drag the grip to place a mini window, or choose Detach. Supports Music, Timer, Focus, Calendar, Notes, Shelf and System. Closing it keeps music and sessions running.",11,UI.Muted)));
        page.Children.Add(Section("Closed cover gestures",Check("Control playback from album cover",s.CoverGestures,v=>s.CoverGestures=v),UI.Text("Choose Album artwork in Audio. Click to play/pause; hold 500 ms to choose a session. Keyboard: Ctrl+Space for playback, Ctrl+M for sessions.",11,UI.Muted)));
        var source=new ComboBox();var sourceValues=new[]{"Auto","Countdown","CPU","RAM","Calendar","Clock"};
        foreach(var label in new[]{"Automatic","Timer / Focus","CPU usage","Memory usage","Next calendar event","Clock"})source.Items.Add(label);
        source.SelectedIndex=System.Array.IndexOf(sourceValues,s.SatelliteSource);UI.Named(source,"Companion beside music");
        source.SelectionChanged+=(_,_)=>{if(source.SelectedIndex>=0){s.SatelliteSource=sourceValues[source.SelectedIndex];previewMode="Hover";previewContent="Two activities";Save();}};
        page.Children.Add(Section("Two live activities",Check("Show companion beside music",s.ActivitySatellite,v=>s.ActivitySatellite=v),UI.Text("Companion",12),source,UI.Text("Auto chooses a countdown, an upcoming event, then CPU. Or choose a fixed source. Hover to preview; click to open. Hides while the island opens, during feedback, or when space is limited.",11,UI.Muted)));

    }
    private FrameworkElement FeaturePreview(string kind)
    {
        var panel=new StackPanel();
        var s=app.Settings;
        if(kind=="Activities" && !s.ActivitiesEnabled)return UI.Text("Activities is off. Saved routines are kept.",12,UI.Muted);if(kind=="Smart clipboard")return ClipboardExample();
        if(kind is "Control colors" or "Typography" or "Panel navigation")return ControlExample(kind);
        string? inactive=kind switch
        {
            "Drop zone" when !s.DropReveal=>"Drop reveal is off. Open the shelf to add files.",
            "Detached preview" when !s.DetachablePreviews=>"Detachable previews are off.",
            "Cover gestures" when !s.CoverGestures=>"Cover gestures are off. Click to open the panel.",
            "Cover gestures" when s.ClosedMediaIcon!="Artwork"=>"Choose Artwork in Appearance to use cover gestures.",
            "Two activities" when !s.ActivitySatellite=>"Companion is off. Music stays in the island.",
            _=>null
        };
        if(inactive!=null){panel.Children.Add(UI.Text(inactive,12,UI.Muted));return panel;}
        if(kind=="Drop zone")
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
            for(int i=0;i<3;i++){var icon=Icons.Create(i==1?"music":"nav-tools",26);icon.Margin=new(9,0,9,0);var move=new TranslateTransform();icon.RenderTransform=move;row.Children.Add(icon);int index=i;previewRealtimeFrames.Add(t=>move.Y=app.Settings.ReduceMotion?0:Math.Sin(t*2+index)*3);}
            panel.Children.Add(row);panel.Children.Add(UI.Text("3 files · drop to stage",12));
        }
        else if(kind=="Detached preview")return DetachedExample();
        else if(kind=="Cover gestures")
        {
            var state=UI.Text("Click cover to play",12);var cover=UI.Button("Album cover",()=>state.Text=state.Text.Contains("playing")?"Playback paused":"Music is playing");cover.Content=Icons.Create("music",24);cover.Width=44;cover.Height=44;cover.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(cover);panel.Children.Add(state);
        }
        else if(kind=="Two activities")return CompanionExample();
        else if(kind=="Activities")
        {panel.Children.Add(UI.Text("Study",16));panel.Children.Add(UI.Text("Open workspace → Focus 45 min → Keep awake",11,UI.Muted));var result=UI.Text("Ready",11,UI.Muted);var start=UI.Button("Try preview",()=>result.Text="Workspace ready · Focus started");panel.Children.Add(start);panel.Children.Add(result);}
        else
        {panel.Children.Add(new Border{Height=28,Background=UI.Brush("#8E9AAF"),CornerRadius=new(4)});panel.Children.Add(UI.Text("#8E9AAF · rgb(142, 154, 175)",12));panel.Children.Add(UI.Text("Copy HEX    Copy RGB",11,UI.Muted));}
        return panel;
    }
}
