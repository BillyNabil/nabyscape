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
        page.Children.Add(Section("Drag to Island",Check("Reveal shelf when dragging files",s.DropReveal,v=>s.DropReveal=v),UI.Text("Drag files onto the island or its tucked edge. Drop to stage references; select multiple shelf items to drag them out together. Manual hiding stays hidden.",11,UI.Muted)));
        page.Children.Add(Section("Detachable previews",Check("Allow detachable previews",s.DetachablePreviews,v=>s.DetachablePreviews=v),UI.Text("Use Detach or drag the grip beside it. Music, Timer, Focus, Calendar, Notes, Shelf and System open as independent windows. Always on top is optional. Closing a window keeps its activity running.",11,UI.Muted)));
        page.Children.Add(Section("Closed cover gestures",Check("Control playback from album cover",s.CoverGestures,v=>s.CoverGestures=v),UI.Text("Choose Artwork in Appearance. Click its cover to play/pause; hold 500 ms to choose a media session. Click elsewhere to open the panel. When the island has keyboard focus: Ctrl+Space plays/pauses; Ctrl+M selects a session. Wheel behavior remains below.",11,UI.Muted)));
        page.Children.Add(Section("Two live activities",Check("Show countdown beside music",s.ActivitySatellite,v=>s.ActivitySatellite=v),UI.Text("Music stays in the island while the earliest running Timer or Focus countdown appears beside it. Hover the countdown to preview it. Hidden when there is not enough room or feedback is showing.",11,UI.Muted)));
        page.Children.Add(Section("Activities",UI.Text("Launch → Activities combines opening apps, folders and websites with Focus and timed Keep awake. Nothing starts automatically. Customize and save the Study template before using it.",11,UI.Muted)));
    }
    private FrameworkElement FeaturePreview(string kind)
    {
        var panel=new StackPanel();
        if(kind=="Drop zone")
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
            for(int i=0;i<3;i++){var icon=Icons.Create(i==1?"music":"nav-tools",26);icon.Margin=new(9,0,9,0);var move=new TranslateTransform();icon.RenderTransform=move;row.Children.Add(icon);int index=i;previewFrames.Add(t=>move.Y=app.Settings.ReduceMotion?0:Math.Sin(t*2+index)*3);}
            panel.Children.Add(row);panel.Children.Add(UI.Text("3 files · drop to stage",12));
        }
        else if(kind=="Detached preview")
        {
            panel.Children.Add(UI.Text("Music · independent window",12));var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,12,0,0)};row.Children.Add(Icons.Create("previous",16));var play=UI.Button("Play",()=>{});bool paused=true;play.Click+=(_,_)=>{paused=!paused;play.Content=paused?"Play":"Pause";};play.Margin=new(14,0,14,0);row.Children.Add(play);row.Children.Add(Icons.Create("next",16));panel.Children.Add(row);var move=new TranslateTransform();panel.RenderTransform=move;previewFrames.Add(t=>move.X=app.Settings.ReduceMotion?0:Math.Sin(t)*4);
        }
        else if(kind=="Cover gestures")
        {
            var state=UI.Text("Click cover to play",12);var cover=UI.Button("Album cover",()=>state.Text=state.Text.Contains("playing")?"Playback paused":"Music is playing");cover.Content=Icons.Create("music",24);cover.Width=44;cover.Height=44;cover.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(cover);panel.Children.Add(state);
        }
        else if(kind=="Two activities")
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(UI.Text("Night Drive",14));var ring=new ActivityVisual{Width=22,Height=22,Margin=new(18,0,8,0)};row.Children.Add(ring);var time=UI.Text("08:42",12);row.Children.Add(time);panel.Children.Add(row);previewFrames.Add(t=>{double elapsed=app.Settings.ReduceMotion?0:t%30;ring.Sample(0,.033,.4+elapsed/100,true,false,false,t);time.Text=TimeSpan.FromSeconds(522-elapsed).ToString(@"mm\:ss");});
        }
        else if(kind=="Activities")
        {panel.Children.Add(UI.Text("Study",16));panel.Children.Add(UI.Text("Open workspace → Focus 45 min → Keep awake",11,UI.Muted));var result=UI.Text("Ready",11,UI.Muted);var start=UI.Button("Try preview",()=>result.Text="Workspace ready · Focus started");panel.Children.Add(start);panel.Children.Add(result);}
        else
        {panel.Children.Add(new Border{Height=28,Background=UI.Brush("#8E9AAF"),CornerRadius=new(4)});panel.Children.Add(UI.Text("#8E9AAF · rgb(142, 154, 175)",12));panel.Children.Add(UI.Text("Copy HEX    Copy RGB",11,UI.Muted));}
        return panel;
    }
}
