using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private ScrollViewer settingsScroll=null!;
    private int? previewTemplateKey;
    private StackPanel? previewLayout;
    private Border? previewMaterial;
    private string previewMode="Closed",previewContent="Music";
    private readonly Dictionary<string,Button> previewModeButtons=new();
    private readonly TextBlock previewDimensions=UI.Text("",11,UI.Muted);
    private FrameworkElement ShapeChoices(Action<string> choose)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());int index=0;
        foreach(var shape in new[]{"Mac","iPhone"})
        {
            var content=new StackPanel();var silhouette=new Border{Width=110,Height=27,Background=Brushes.Black,Margin=new(0,2,0,10)};silhouette.Clip=IslandGeometry.Create(110,27,12,shape=="Mac");content.Children.Add(silhouette);var label=UI.Text(shape=="Mac"?"Notch":"Pill",12);label.HorizontalAlignment=HorizontalAlignment.Center;content.Children.Add(label);
            var button=UI.Button(shape=="Mac"?"Use notch shape":"Use pill shape",()=>choose(shape));button.Content=content;button.Background=UI.Brush(app.Settings.Preset==shape?"#2C2F35":"#1A1C20");button.BorderThickness=new(1);button.BorderBrush=UI.Brush(app.Settings.Preset==shape?"#B9BCC4":"#35383F");button.Padding=new(12);button.Margin=new(index==0?0:6,0,index==0?6:0,0);button.Tag=new CornerRadius(10);Grid.SetColumn(button,index++);grid.Children.Add(button);
        }
        return grid;
    }
    private void ColorControl(StackPanel parent,string name,string value,Action<string> changed,string[] palette)
    {
        var caption=UI.Text(name,12,UI.Muted);caption.Margin=new(0,0,0,8);parent.Children.Add(caption);
        ColorPicker? picker=null;
        picker=new ColorPicker(name,value,palette,app.Settings.RecentColors,color=>{changed(color);Save();},color=>{app.Settings.RecentColors.RemoveAll(c=>c.Equals(color,StringComparison.OrdinalIgnoreCase));app.Settings.RecentColors.Insert(0,color);app.Settings.RecentColors=app.Settings.RecentColors.Take(8).ToList();picker?.SetRecent(name,app.Settings.RecentColors);Save();});picker.Margin=new(0,0,0,16);parent.Children.Add(picker);
    }

    private void LayoutSettings()
    {
        DisplaySettings();var s=app.Settings;var geometry=new StackPanel();AddSlider(geometry,"Closed width",s.Width,180,400,v=>s.Width=v,"px");AddSlider(geometry,"Closed height",s.Height,28,72,v=>s.Height=v,"px");AddSlider(geometry,"Corner radius",s.Radius,0,40,v=>s.Radius=v,"px");AddSlider(geometry,"Horizontal position",s.Horizontal,0,100,v=>s.Horizontal=v,"%");if(s.Preset!="Mac")AddSlider(geometry,"Top offset",s.Top,0,150,v=>s.Top=v,"px");page.Children.Add(Section("Closed island",geometry));
        var panel=new StackPanel();AddSlider(panel,"Panel width",s.PanelWidth,340,640,v=>s.PanelWidth=v,"px");AddSlider(panel,"Maximum panel height",s.PanelMaxHeight,320,760,v=>s.PanelMaxHeight=v,"px");AddSlider(panel,"Content inset",s.PanelPadding,16,40,v=>s.PanelPadding=v,"px");AddSlider(panel,"Content size",s.PanelScale*100,85,125,v=>s.PanelScale=v/100,"%");AddSlider(panel,"Section spacing",s.SectionGap,8,28,v=>s.SectionGap=v,"px");page.Children.Add(Section("Open panel",Check("Compact panel with tabs",s.CompactLayout,v=>s.CompactLayout=v),Advanced("Panel dimensions and spacing",panel)));
        var hover=new StackPanel();AddSlider(hover,"Preview width",s.HoverWidth,300,500,v=>s.HoverWidth=v,"px");AddSlider(hover,"Preview height",s.HoverHeight,184,280,v=>s.HoverHeight=v,"px");page.Children.Add(Section("Hover preview",hover));
        page.Children.Add(Section("Layout profile",UI.Row(UI.Button("Export layout",ExportCustomization),UI.Button("Import layout",ImportCustomization)),UI.Text("Includes appearance, layout, widget order and gestures. Personal notes, shortcuts and startup settings are excluded.",11,UI.Muted)));
    }
    private void AudioSettings()
    {
        MediaAppearanceSettings();AudioFeedbackSettings();
        page.Children.Add(Section("Volume mixer",UI.Text("Choose Apps or Outputs, then drag a volume slider or use mute.",12,UI.Muted),UI.Button("Open volume mixer",()=>app.OpenMixer())));
        var signal=new StackPanel();audioSignalWave=new ActivityVisual{WaveOnly=true,Width=96,Height=16,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,4,0,12)};signal.Children.Add(audioSignalWave);audioSignalState=UI.Text("Reading selected app…",11,UI.Muted);signal.Children.Add(audioSignalState);
        page.Children.Add(Section("Live signal",UI.Text("Follows the app selected in Media, across Windows outputs. Select Spotify there to keep Discord out of the waveform.",11,UI.Muted),signal,UI.Text("The waveform expands small level changes in mastered music. Bars show amplitude history, not bass or treble bands.",11,UI.Muted)));
        page.Children.Add(Section("Audio behavior",UI.Text("Output selection changes the Windows default device. Microphone mute is available from the audio menu.",12,UI.Muted),UI.Button("Windows sound settings",()=>ShortcutService.Open(new(){Target="ms-settings:sound"}))));

    }
    private void SystemSettings()
    {
        var s=app.Settings;page.Children.Add(Section("Monitor details",Check("Network transfer rates",s.MonitorNetwork,v=>s.MonitorNetwork=v),Check("Storage capacity",s.MonitorStorage,v=>s.MonitorStorage=v),Check("Processes by memory",s.MonitorProcesses,v=>s.MonitorProcesses=v),UI.Text("Overview shows CPU, memory and uptime. Switch tabs for network, storage or processes. Sampling stops when Status is hidden.",11,UI.Muted)));
        page.Children.Add(Section("Clock",Check("24-hour clock",s.Clock24Hour,v=>s.Clock24Hour=v),Check("Show seconds in Status",s.ClockSeconds,v=>s.ClockSeconds=v),Check("Show date",s.ShowDate,v=>s.ShowDate=v)));
    }
    private void HoverControls()
    {
        var s=app.Settings;var timing=new StackPanel();AddSlider(timing,"Hover delay",s.HoverDelayMs,80,900,v=>s.HoverDelayMs=(int)v,"ms",10);AddSlider(timing,"Detail delay",s.HoverDetailDelayMs,200,1200,v=>s.HoverDetailDelayMs=(int)v,"ms",10);AddSlider(timing,"Swipe distance",s.SwipeDistance,24,100,v=>s.SwipeDistance=v,"px");page.Children.Add(Section("Hover & swipe",UI.Text("Scroll anywhere on a preview to browse. Click to open its panel.",11,UI.Muted),Check("Preview on hover",s.HoverPeek,v=>s.HoverPeek=v),Check("Two-stage reveal",s.TwoStageHover,v=>s.TwoStageHover=v),Advanced("Hover timing",timing)));
    }
    private void HoverPageSettings()
    {
        var s=app.Settings;
        var views=new StackPanel{MaxWidth=600,HorizontalAlignment=HorizontalAlignment.Stretch};
        for(int i=0;i<s.HoverViews.Count;i++)
        {
            int index=i;var item=s.HoverViews[i];string title=item.Id switch{"media"=>"Music","notes"=>"Note","files"=>"Shelf","clips"=>"Clipboard","tasks"=>"Tasks",_=>char.ToUpperInvariant(item.Id[0])+item.Id[1..]};
            var row=new Grid{Margin=new(0,3,0,3)};row.ColumnDefinitions.Add(new(){Width=new GridLength(26)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(56)});row.ColumnDefinitions.Add(new(){Width=new GridLength(64)});
            row.Children.Add(UI.Text((i+1).ToString(),11,UI.Muted));
            var show=UI.Button(title,()=>{previewMode="Hover";previewContent=title=="Clipboard"?"Smart clipboard":title;RefreshPreview();});show.Content=title;show.Background=Brushes.Transparent;show.HorizontalContentAlignment=HorizontalAlignment.Stretch;show.Padding=new(4,6,4,6);show.Margin=new(0);show.MinHeight=32;show.FontSize=12;show.ToolTip="Preview "+title;Grid.SetColumn(show,1);row.Children.Add(show);
            var enabled=new CheckBox{IsChecked=item.Enabled,VerticalAlignment=VerticalAlignment.Center,MinHeight=32,Height=32,Margin=new(0,0,10,0)};UI.Named(enabled,"Show "+title+" in hover previews");enabled.ToolTip="Show "+title;enabled.Click+=(_,_)=>{item.Enabled=enabled.IsChecked==true;Save();};Grid.SetColumn(enabled,2);row.Children.Add(enabled);
            var controls=UI.Row(UI.Button("Move up "+title,()=>Move(s.HoverViews,index,-1)),UI.Button("Move down "+title,()=>Move(s.HoverViews,index,1)));controls.VerticalAlignment=VerticalAlignment.Center;
            for(int k=0;k<2;k++){var button=(Button)controls.Children[k];var icon=Icons.Create("chevron",10);icon.RenderTransformOrigin=new(.5,.5);icon.RenderTransform=new RotateTransform(k==0?180:0);button.Content=icon;button.Width=28;button.MinHeight=28;button.Padding=new(0);button.Margin=new(2,0,0,0);button.Tag=new CornerRadius(7);button.ToolTip=k==0?"Earlier in the swipe order":"Later in the swipe order";}
            ((Button)controls.Children[0]).IsEnabled=i>0;((Button)controls.Children[1]).IsEnabled=i<s.HoverViews.Count-1;Grid.SetColumn(controls,3);row.Children.Add(controls);views.Children.Add(row);
        }
        page.Children.Add(Section("Hover pages",UI.Text("Scroll through these pages in this order. Click a name to preview it; use the arrows to reorder. Switch off pages you do not need.",11,UI.Muted),views));

    }
    private void RenderSettingsPreview()
    {
        if(preview==null)return;if(previewExamples!=null && previewExamples.Items.Contains(previewContent) && previewExamples.SelectedItem?.ToString()!=previewContent)previewExamples.SelectedItem=previewContent;var s=app.Settings;bool closed=previewMode=="Closed",hover=previewMode=="Hover";double width=closed?s.Width:hover?Math.Min(s.HoverWidth,s.PanelWidth):s.PanelWidth;double height=closed?s.Height:hover?s.HoverHeight:s.PanelMaxHeight;
        bool detailExample=!closed && ((!hover && current!="Layout") || previewContent is "Control colors" or "Typography" or "Panel navigation" or "Drop zone" or "Detached preview" or "Cover gestures" or "Two activities" or "Activities" or "Smart clipboard" or "Shelf" or "Visibility");
        if(detailExample){closed=false;hover=false;width=300;height=previewContent switch{"Control colors"=>330,"Smart clipboard"=>320,"Detached preview"=>260,"Typography"=>220,"Shelf"=>260,"Music"=>250,"System"=>210,"Navigation"=>160,_=>230};}
        AnimatePreviewSize(width,height);preview.Opacity=s.Opacity;preview.Background=UI.Brush(s.Background);RefreshPreviewClip();preview.Margin=new(0,s.Preset=="Mac"?0:s.Top,0,0);
        var inset=new Thickness(closed?18:hover?32:s.PanelPadding,closed?0:15,closed?18:hover?32:s.PanelPadding,0);
        previewDimensions.Text=detailExample?"Detail preview \u00b7 your current appearance":$"{width:0} \u00d7 {height:0} px \u00b7 {s.Preset switch{"Mac"=>"Notch",_=>"Pill"}}";
        int settingsKey=previewContent is "Music" or "Timer" or "Focus" or "System"?HashCode.Combine(s.ClosedMediaContent,s.ClosedMediaIcon,s.LiveActivity,s.BoldMediaTitle,s.BoldHeadings,s.FocusMinutes,s.BreakMinutes):previewRevision;
        int template=HashCode.Combine(current,previewMode,previewContent,settingsKey,app.Media.Title,app.Media.Artist,app.Media.Artwork);
        if(previewTemplateKey==template && previewLayout!=null && previewMaterial!=null)
        {previewLayout.Margin=inset;previewMaterial.Visibility=s.GlassStyle?Visibility.Visible:Visibility.Collapsed;previewMaterial.Background=IslandSurface.Material(s.Background);previewMaterial.Opacity=(closed?.45:1)*s.GlassStrength;return;}
        previewTemplateKey=template;previewFrames.Clear();previewRealtimeFrames.Clear();hasPreviewWave=false;
        var content=new StackPanel{Margin=inset};previewLayout=content;var header=new DockPanel{Height=closed?height:23};var time=UI.Text(DateTime.Now.ToString(s.Clock24Hour?"HH:mm":"h:mm tt"),11);time.FontWeight=FontWeights.Bold;DockPanel.SetDock(time,Dock.Right);header.Children.Add(time);previewFrames.Add(_=>time.Text=DateTime.Now.ToString(s.Clock24Hour?"HH:mm":"h:mm tt"));var appName=UI.Text(closed?"NabyScape":previewContent,11);appName.TextWrapping=TextWrapping.NoWrap;appName.TextTrimming=TextTrimming.CharacterEllipsis;appName.FontWeight=s.BoldHeadings?FontWeights.SemiBold:FontWeights.Normal;header.Children.Add(appName);if(closed || hover)content.Children.Add(header);
        if(closed && previewContent=="Music")
        {
            FrameworkElement? icon=s.ClosedMediaIcon=="Artwork" && app.Media.Artwork!=null?new Border{Width=20,Height=20,CornerRadius=new(5),Clip=new RectangleGeometry(new Rect(0,0,20,20),5,5),Margin=new(0,0,8,0),VerticalAlignment=VerticalAlignment.Center,Child=new Image{Source=app.Media.Artwork,Stretch=Stretch.UniformToFill}}:s.LiveActivity && s.ClosedMediaContent!="Waveform"?PreviewWave():null;
            if(icon!=null){DockPanel.SetDock(icon,Dock.Left);header.Children.Insert(0,icon);}
        }
        if(closed && previewContent=="Music" && s.ClosedMediaContent=="Waveform")
        {
            appName.Visibility=Visibility.Collapsed;var waveform=PreviewWave(true);waveform.Width=double.NaN;waveform.MaxWidth=72;waveform.Margin=new(0,0,8,0);waveform.HorizontalAlignment=HorizontalAlignment.Stretch;header.Children.Add(waveform);
        }
        if(closed && previewContent=="Volume feedback")
        {
            header.Children.Clear();var level=UI.Text($"Volume {app.Audio.Volume*100:0}%",11);header.Children.Add(level);var bar=new ProgressBar{Value=app.Audio.Volume*100,Height=3,Width=64,Margin=new(10,0,0,0)};header.Children.Add(bar);previewFrames.Add(_=>{level.Text=$"Volume {app.Audio.Volume*100:0}%";bar.Value=app.Audio.Volume*100;});
        }
        if(!closed)
        {
            var detail=new StackPanel{Margin=new(0,hover?12:s.SectionGap,0,0),LayoutTransform=new ScaleTransform(s.PanelScale,s.PanelScale)};
            if(previewContent=="Stopwatch")detail.Children.Add(new StopwatchView(app));
            else if(previewContent=="Visibility")detail.Children.Add(VisibilityExample());
            else if(previewContent is "Drop zone" or "Detached preview" or "Cover gestures" or "Two activities" or "Activities" or "Smart clipboard" or "Control colors" or "Typography" or "Panel navigation")detail.Children.Add(FeaturePreview(previewContent));
            else if(previewContent=="Music"){var title=UI.Text(app.Media.Available?app.Media.Title:"Nothing playing",15);title.FontWeight=s.BoldMediaTitle?FontWeights.SemiBold:FontWeights.Normal;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;detail.Children.Add(title);var artist=UI.Text(app.Media.Available?app.Media.Artist:"Windows media",11,UI.Muted);artist.Margin=new(0,5,0,14);detail.Children.Add(artist);var transport=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};foreach(string icon in new[]{"previous","play","next"}){var shape=Icons.Create(icon,icon=="play"?25:16,UI.Brush(s.Accent));shape.Margin=new(15,0,15,0);transport.Children.Add(shape);}detail.Children.Add(transport);var progress=new ProgressBar{Minimum=0,Maximum=1,Height=3,Margin=new(0,14,0,0)};detail.Children.Add(progress);detail.Children.Add(PreviewWave());previewRealtimeFrames.Add(_=>{var t=app.Media.Timeline();progress.Value=t.Item2>0?t.Item1/t.Item2:0;});}
            else if(previewContent is "Timer" or "Focus"){var countdown=previewContent=="Focus"?app.Session.Focus:app.Session.Timer;var digits=UI.Text(countdown.Remaining(DateTimeOffset.UtcNow)>0?countdown.Display(DateTimeOffset.UtcNow):previewContent=="Focus"?$"{s.FocusMinutes:00}:00":"05:00",32);detail.Children.Add(digits);previewFrames.Add(_=>{if(countdown.Remaining(DateTimeOffset.UtcNow)>0)digits.Text=countdown.Display(DateTimeOffset.UtcNow);});var start=UI.Text("Start     +5 min",11,UI.Muted);start.Margin=new(0,18,0,0);detail.Children.Add(start);}
            else if(previewContent=="Tasks"){detail.Children.Add(UI.Heading("Tasks",16));foreach(var task in app.Productivity.Tasks.Take(3)){var line=UI.Text((task.Done?"✓  ":"○  ")+task.Text,12);line.TextWrapping=TextWrapping.NoWrap;line.TextTrimming=TextTrimming.CharacterEllipsis;line.Margin=new(0,6,0,0);detail.Children.Add(line);}if(app.Productivity.Tasks.Count==0)detail.Children.Add(UI.Text("Your tasks appear here. Add them in Notes → Tasks.",12,UI.Muted));}
            else if(previewContent=="Calendar"){detail.Children.Add(UI.Text(DateTime.Now.ToString("ddd, d MMM"),11,UI.Muted));var next=app.Calendar.Events.Where(e=>e.End>DateTimeOffset.UtcNow).OrderBy(e=>e.Start).FirstOrDefault();var title=UI.Text(next?.Title??"No upcoming events",15);title.Margin=new(0,12,0,0);detail.Children.Add(title);}
            else if(previewContent=="Note"){detail.Children.Add(UI.Text("Quick note",11,UI.Muted));detail.Children.Add(new Border{Background=UI.Brush("#24262B"),CornerRadius=new(8),Padding=new(10),Margin=new(0,10,0,0),Child=UI.Text(app.Productivity.Note.Length>0?app.Productivity.Note[..Math.Min(90,app.Productivity.Note.Length)]:"Capture a thought…",12)});}
            else if(previewContent=="Shelf")
            {
                detail.Children.Add(UI.Heading("File shelf",16));detail.Children.Add(UI.Text("Select files, then drag them out together",11,UI.Muted));
                foreach(string file in new[]{"Design notes.pdf","Wallpaper.png"}){var row=new DockPanel{Margin=new(0,10,0,0)};var select=new CheckBox{IsChecked=false,Style=(Style)app.FindResource("TaskToggle"),Width=30};row.Children.Add(select);var icon=Icons.Create("file-text",24);icon.Margin=new(0,0,10,0);row.Children.Add(icon);row.Children.Add(UI.Text(file,12));detail.Children.Add(row);}
                detail.Children.Add(UI.Text("Example files · nothing is added to your shelf",10,UI.Muted));
            }
            else if(previewContent=="Tools"){foreach(string tool in new[]{"File shelf","Notes","Clipboard","Calendar","Keep awake","Clean links"})detail.Children.Add(new Border{Height=30,Child=UI.Text(tool,12)});}
            else if(previewContent=="Launch" || previewContent=="Navigation")
            {
                string style=previewContent=="Launch"?s.LaunchStyle:s.CategoryStyle;
                var rows=new WrapPanel();var samples=previewContent=="Launch"?app.Settings.Shortcuts.Take(4).Select(x=>(x.Name,"arrow")).ToArray():new[]{("Media","nav-media"),("Focus","nav-focus"),("Status","nav-status"),("Launch","nav-launch"),("Tools","nav-tools")};
                foreach(var (name,icon) in samples){var entry=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,0,12,14)};if(style!="Text")entry.Children.Add(Icons.Create(icon,16,UI.Brush(s.Accent)));if(style!="Icons"){var label=UI.Text(name,11);label.Margin=new(style=="Text"?0:7,0,0,0);label.FontWeight=s.BoldHeadings?FontWeights.SemiBold:FontWeights.Normal;label.MaxWidth=96;label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;entry.Children.Add(label);}rows.Children.Add(entry);}detail.Children.Add(rows);
            }
            else if(previewContent=="Startup"){detail.Children.Add(UI.Heading("Start with Windows",16));detail.Children.Add(new CheckBox{Content=s.AutoStart?"Enabled":"Off",IsChecked=s.AutoStart,IsHitTestVisible=false,Focusable=false});detail.Children.Add(UI.Text("Closing Settings keeps NabyScape running. Quit from the tray.",11,UI.Muted));}
            else if(previewContent=="Volume feedback"){detail.Children.Add(Icons.Create("volume",24));detail.Children.Add(UI.Text($"{app.Audio.Volume*100:0}%",24));detail.Children.Add(new ProgressBar{Value=app.Audio.Volume*100,Height=4,Margin=new(0,14,0,0)});}
            else{var cpu=UI.Text($"CPU   {app.Usage.cpu:0}%",14);var memory=UI.Text($"Memory   {app.Usage.used:0.0} / {app.Usage.total:0.0} GB",12);var bar=new ProgressBar{Value=app.Usage.cpu,Maximum=100,Height=3,Margin=new(0,10,0,18),Foreground=Brushes.White,Background=UI.Brush("#40434B")};detail.Children.Add(cpu);detail.Children.Add(bar);detail.Children.Add(memory);previewFrames.Add(_=>{cpu.Text=$"CPU   {app.Usage.cpu:0}%";bar.Value=app.Usage.cpu;memory.Text=$"Memory   {app.Usage.used:0.0} / {app.Usage.total:0.0} GB";});}
            content.Children.Add(detail);
        }
        content.ClipToBounds=true;var layers=new Grid{ClipToBounds=true};previewMaterial=new Border{Background=IslandSurface.Material(s.Background),Opacity=(closed?.45:1)*s.GlassStrength,Visibility=s.GlassStyle?Visibility.Visible:Visibility.Collapsed};layers.Children.Add(previewMaterial);layers.Children.Add(content);preview.Child=layers;previewDimensions.Text=detailExample?"Detail preview · your current appearance":$"{width:0} × {height:0} px · {s.Preset switch{"Mac"=>"Notch",_=>"Pill"}}";foreach(var entry in previewModeButtons){entry.Value.Background=entry.Key==previewMode?UI.Brush("#34373E"):Brushes.Transparent;entry.Value.Foreground=entry.Key==previewMode?Brushes.White:UI.Muted;}
    }
    private void ExportCustomization()
    {var dialog=new Microsoft.Win32.SaveFileDialog{Title="Export NabyScape layout",Filter="NabyScape layout|*.json",FileName="NabyScape-layout.json"};if(dialog.ShowDialog(this)!=true)return;try{File.WriteAllText(dialog.FileName,CustomizationProfile.Export(app.Settings));status.Text="Layout exported";}catch(Exception ex){status.Text="Could not export layout";app.Store.Log(ex);}}
    private void ImportCustomization()
    {var dialog=new Microsoft.Win32.OpenFileDialog{Title="Import NabyScape layout",Filter="NabyScape layout|*.json"};if(dialog.ShowDialog(this)!=true)return;try{if(new FileInfo(dialog.FileName).Length>262144)throw new InvalidDataException("Layout file is too large.");CustomizationProfile.Import(File.ReadAllText(dialog.FileName),app.Settings);Save();Navigate(current);status.Text="Layout imported";}catch(Exception ex){status.Text="Could not import layout. "+ex.Message;app.Store.Log(ex);}}
}
