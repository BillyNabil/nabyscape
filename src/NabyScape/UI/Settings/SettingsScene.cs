using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace NabyScape;

// Static, local examples of the controls in this section. Repaint values without rebuilding editors.
internal sealed class SettingsScene : FrameworkElement
{
    internal static readonly string[] Sections={"Other apps","Feature usage","Your setup","Ambient light","Notification presentation","Visualizer","Lyrics","Move & resize","Completion sound","AI companion","Shape","Settings material","Color & material","Surface","Controls & outlines","Typography","Text and navigation","Motion","Animation performance","Dock placement","Display","Closed island","Open panel","Hover preview","Hover & swipe","Enabled widgets & order","Individual tools","Hover pages","Clipboard history","Focus sessions","Stopwatch","Automatic tucking","Manual tuck shortcut","Panel navigation","Drag to Island","Detachable previews","Closed cover gestures","Automatic activity","Notch gestures","Global hotkey","Hide or show the island","Closed media display","Waveform source","Live signal","Live feedback","Volume mixer","Audio behavior","Header wheel","Clock","Monitor details","Launch display","Shortcuts","Layout profile","Notifications","Downloads","Updates","Reset preferences","Behavior","Local storage","Language & accessibility","Private diagnostics","About NabyScape"};
    private readonly string kind;
    internal string Section=>kind;
    private readonly Settings s;
    internal SettingsScene(string kind,Settings settings){if(!Sections.Contains(kind))throw new ArgumentException("Unknown Settings example: "+kind);this.kind=kind;s=settings;ClipToBounds=true;IsHitTestVisible=false;}
    internal sealed record WidgetTile(Rect Bounds,FormattedText Label);
    internal static WidgetTile[] WidgetTiles(Settings settings,double pixelsPerDip=1)
    {
        var labels=settings.Widgets.Where(widget=>widget.Enabled).Select(widget=>new FormattedText(Localization.Text(Settings.Names[widget.Id]),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,UI.Brush("#F3F4F6"),pixelsPerDip){MaxTextWidth=60,Trimming=TextTrimming.None}).ToArray();
        var result=new WidgetTile[labels.Length];double y=15;
        for(int row=0;row<labels.Length;row+=5)
        {
            double height=Math.Max(28,labels.Skip(row).Take(5).Max(label=>label.Height)+12);
            for(int column=0;column<5 && row+column<labels.Length;column++)result[row+column]=new(new Rect(16+column*79,y,72,height),labels[row+column]);
            y+=height+7;
        }
        return result;
    }
    protected override Size MeasureOverride(Size available)
    {
        double height=126;
        if(kind=="Enabled widgets & order"){var tiles=WidgetTiles(s,VisualTreeHelper.GetDpi(this).PixelsPerDip);height=Math.Max(height,(tiles.LastOrDefault()?.Bounds.Bottom??15)+44);}
        double width=double.IsInfinity(available.Width)?420:available.Width;
        return new(width,height*Math.Min(1,width/420));
    }
    protected override void OnRender(DrawingContext dc)
    {
        if(ActualWidth<=0)return;
        double scale=Math.Min(1,ActualWidth/420);dc.PushTransform(new ScaleTransform(scale,scale));
        var white=UI.Brush("#F2F3F5");var muted=UI.Brush("#ADB3BD");var fill=UI.Brush("#24272D");var line=new Pen(UI.Brush("#626874"),1);var edge=new Pen(UI.Brush("#16FFFFFF"),.75);edge.Freeze();
        void Box(double x,double y,double w,double h,Brush? brush=null,double r=8)=>dc.DrawRoundedRectangle(brush??fill,w>=28 && h>=22?edge:null,new Rect(x,y,w,h),r,r);
        void Text(string value,double x,double y,double size=12,bool bold=false,Brush? brush=null,double maxWidth=400)
        {var t=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,bold?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),size,brush??white,VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=Math.Max(10,Math.Min(maxWidth,405-x)),MaxTextHeight=42,Trimming=TextTrimming.CharacterEllipsis};dc.DrawText(t,new Point(x,y));}
        void Caption(string english,double x,double y,double size=12,bool bold=false,Brush? brush=null,double maxWidth=400)
            =>Text(Localization.Text(english),x,y,size,bold,brush,maxWidth);
        void Line(double x,double y,double xx,double yy)=>dc.DrawLine(line,new Point(x,y),new Point(xx,yy));
        void Toggle(double x,double y,bool on){Box(x,y,30,16,on?white:fill,8);dc.DrawEllipse(on?fill:muted,null,new Point(x+(on?22:8),y+8),5,5);}
        void Bar(double x,double y,double w,double value){Box(x,y,w,3,fill,1);Box(x,y,Math.Max(1,w*Math.Clamp(value,0,1)),3,white,1);}
        void Keys(string key,double x,double y,double w=90){Box(x,y,w,30);Caption(key,x+10,y+7,11);}
        void Silhouette(double x,double y,double w,double h,bool notch)
        {
            var geometry=IslandGeometry.Create(w,h,s.Radius,notch);dc.PushTransform(new TranslateTransform(x,y));
            dc.DrawGeometry(UI.Brush(s.Background),edge,geometry);
            if(s.GlassStyle){dc.PushOpacity(s.GlassStrength*.5);dc.DrawGeometry(IslandSurface.GlassMaterial,null,geometry);dc.Pop();}
            dc.Pop();
        }
        void Island(double x=105,double y=20,double w=210,double h=34){Silhouette(x,y,w,h,s.Preset=="Mac");Caption("Music",x+20,y+9,11,true);Caption("12:45",x+w-54,y+9,11,true);}
        void Wave(double x,double y){for(int i=0;i<25;i++){double h=4+15*Math.Pow(Math.Sin(i*.37+1),2)*(.7+.3*Math.Cos(i*.13));Box(x+i*3.6,y-h/2,1.5,h,white,1);}}
        void Cover(double x,double y,double size)
        {
            if(System.Windows.Application.Current is App app && app.Media?.Artwork is ImageSource artwork){dc.PushClip(new RectangleGeometry(new Rect(x,y,size,size),5,5));dc.DrawImage(artwork,new Rect(x,y,size,size));dc.Pop();}
            else {Box(x,y,size,size,UI.Brush("#737D8C"),5);dc.DrawEllipse(null,new Pen(white,1),new Point(x+size/2,y+size/2),size*.27,size*.27);}
        }
        void Note(string message)=>Caption(message,14,99,11,false,muted);
        void FormattedNote(string message)=>Text(message,14,99,11,false,muted);
        var widgetTiles=kind=="Enabled widgets & order"?WidgetTiles(s,VisualTreeHelper.GetDpi(this).PixelsPerDip):Array.Empty<WidgetTile>();
        double widgetBottom=widgetTiles.LastOrDefault()?.Bounds.Bottom??15;
        Box(0,0,420,kind=="Enabled widgets & order"?Math.Max(124,widgetBottom+42):124,UI.Brush("#191C21"),10);
        switch(kind)
        {
            case "Feature usage":
                Caption("Choose what runs",24,18,18,true);Text(Localization.Format("{0} enabled widgets",s.Widgets.Count(w=>w.Enabled)),24,51,12);Note("Essentials or Custom · background switches stay explicit");break;
            case "Your setup":
                Caption("Your next step",24,18,18,true);Caption("Music · AI · Voice · Lyrics · Weather",24,51,12);Note("Saved settings and verified connections have distinct statuses");break;
            case "AI companion":
                dc.DrawEllipse(UI.Brush("#23262D"),new Pen(UI.Brush("#777D88"),1),new Point(53,51),24,24);dc.DrawRoundedRectangle(white,null,new Rect(42,37,7,16),3.5,3.5);dc.DrawRoundedRectangle(white,null,new Rect(57,37,7,16),3.5,3.5);Caption("Connect your AI",94,24,15,true);Caption("Provider → model → chat / voice",94,51,12);Keys("Local key",298,23,95);Note("Optional · keys in Windows Credential Manager · session capture controls");break;
            case "Shape":
                Silhouette(28,22,160,36,true);Silhouette(228,22,160,36,false);Caption("Notch · attached to edge",30,69,11);Caption("Pill · floating",230,69,11);FormattedNote(Localization.Format("Current: {0}",Localization.Text(s.Preset=="Mac"?"Notch":"Pill")));break;
            case "Settings material":
                Box(22,18,168,65,IslandSurface.GlassMaterial);Caption("Settings window",34,31,13,true);Caption(SettingsBackdrop.Requested(s),34,55,11);Caption("Island stays independent",210,32,11);Note("Schematic only - actual status appears below");break;
            case "Color & material":
                Box(22,19,216,68,UI.Brush(s.Background));Box(36,33,34,34,UI.Brush(s.Accent));Caption("Song title",82,34,14,true,UI.ContrastText(s.Background));Caption(s.GlassStyle?"Black glass":"Solid surface",82,57,11,false,UI.ContrastText(s.Background));Text(Localization.Format("Opacity {0:0}%",s.Opacity*100),256,28);Text(Localization.Format("Glass {0:0}%",s.GlassStrength*100),256,52);Note("Your island color and material");break;
            case "Surface":
                var glass=new LinearGradientBrush(UI.Brush(s.Background).Color,UI.Brush("#3E424A").Color,90){Opacity=.25+s.GlassStrength*.6};
                Box(24,18,176,61,Brushes.Black);Box(224,18,176,61,glass);Caption("Solid",39,38,14,true);Caption("Glass",240,38,14,true);FormattedNote(Localization.Format("Glass intensity {0:0}% · black glass {1}",s.GlassStrength*100,Localization.Text(s.GlassStyle?"on":"off")));break;
            case "Controls & outlines":
                Box(18,24,112,34,UI.Brush(s.ControlFill));Caption("Button",36,33,12,false,UI.ContrastText(s.ControlFill));dc.DrawRoundedRectangle(null,new Pen(UI.Brush(s.ControlBorder),1),new Rect(146,24,122,34),6,6);Caption("Input field",158,33);Box(284,24,116,34,UI.Brush(s.SelectionFill));Caption("Selected tab",296,33,12,false,UI.ContrastText(s.SelectionFill));Note("Button fill · input outline · selected state");break;
            case "Typography":
                Caption("Song title",24,23,21,s.BoldMediaTitle);Caption("Artist name",24,54,12,false,muted);Caption("Panel heading",232,27,16,s.BoldHeadings);Caption("Body text stays regular",232,56,11,false,muted);Note("Current title weights; body text stays the same");break;
            case "Text and navigation":
                foreach(var item in new[]{("Media",22d),("Focus",112d),("Status",202d),("Tools",292d)}){Box(item.Item2,22,76,46);if(s.CategoryStyle=="Dots"){dc.DrawEllipse(white,null,new Point(item.Item2+38,44),3,3);continue;}if(s.CategoryStyle!="Text")dc.DrawEllipse(null,new Pen(white,1.4),new Point(item.Item2+38,36),6,6);if(s.CategoryStyle!="Icons")Caption(item.Item1,item.Item2+14,s.CategoryStyle=="Text"?38:49,11);}FormattedNote(Localization.Format("Navigation · {0}",Localization.Text(s.CategoryStyle.Replace("IconsLabels","Icons + labels"))));break;
            case "Motion":
                Box(23,28,68,22,Brushes.Black);Caption("Music",34,33,9);Line(108,40,176,40);Box(193,16,135,63,Brushes.Black);Cover(203,25,22);Caption("Song title",234,28,10,true);Bar(204,65,112,.4);Caption(s.ReduceMotion?"Instant state change":Localization.Format("{0:0} ms transition",s.AnimationMs),23,78,11);Caption("Closed → open",193,87,11,false,muted);break;
            case "Animation performance":
                Caption("Eco",24,16,12,true);Caption("Auto",24,42,12,true);Caption("Maximum",24,68,12,true);for(int row=0;row<3;row++)for(int i=0;i<(row+1)*8;i++)Box(120+i*10,20+row*26,3,12,white,1);Note("Illustrative update cadence · your display sets the visible limit");break;
            case "Dock placement":
                Box(110,12,205,76,UI.Brush("#14171D"),7);dc.DrawRoundedRectangle(null,line,new Rect(110,12,205,76),7,7);
                var marker=s.DockEdge switch{"Left"=>new Rect(111,36,10,30),"Right"=>new Rect(304,36,10,30),"Bottom"=>new Rect(182,77,62,9),"Floating"=>new Rect(182,45,62,12),_=>new Rect(182,14,62,9)};
                dc.DrawRoundedRectangle(white,null,marker,4,4);FormattedNote(Localization.Format("{0} · {1:0} px inset · panels remain upright",Localization.Text(s.DockEdge),s.DockInset));break;
            case "Display":
                dc.DrawRoundedRectangle(null,line,new Rect(24,17,166,67),5,5);dc.DrawRoundedRectangle(null,line,new Rect(220,17,166,67),5,5);Box(s.DisplayMode is "Follow" or "Selected"?266:70,18,76,12,white,3);if(s.DisplayMode=="Follow"){Line(318,39,332,48);Line(318,39,321,54);Caption("250 ms",242,39,10);}
                Caption("Primary display",50,66,11);Caption("Other display",250,66,11);Note(s.DisplayMode=="Follow"?"Follow cursor · move after 250 ms · waits during interaction":s.DisplayMode=="Selected"?Localization.Format("Selected display: {0}",s.Monitor):"Primary display");break;
            case "Closed island":
                Island(90,18,240,34);Line(90,69,330,69);Line(90,63,90,75);Line(330,63,330,75);Text(Localization.Format("{0:0} × {1:0} px · radius {2:0} px",s.Width,s.Height,s.Radius),115,77,11);FormattedNote(Localization.Format("Horizontal position {0:0}% · top offset {1:0} px",s.Horizontal,s.Top));break;
            case "Open panel":
                Box(24,12,210,76,Brushes.Black);Box(37,23,35,14);Caption("Media",86,25,10);Caption("Song title",42,48,13,true);Bar(42,73,172,.4);Text(Localization.Format("{0:0} px wide",s.PanelWidth),253,24);Text(Localization.Format("Inset {0:0} px",s.PanelPadding),253,47,11);Text(Localization.Format("Text {0:0}%",s.PanelScale*100),253,67,11);FormattedNote(Localization.Format("Maximum height {0:0} px · spacing {1:0} px",s.PanelMaxHeight,s.SectionGap));break;
            case "Hover preview":case "Hover & swipe":
                Island(20,18,126,27);Line(159,37,199,37);Box(212,12,186,73,Brushes.Black);Caption("Song title",226,24,13,true);Bar(226,61,156,.35);Caption("Hover",45,63,11);Note(kind=="Hover & swipe"?Localization.Format("Preview after {0} ms · detail after {1} ms",s.HoverDelayMs,s.HoverDetailDelayMs):Localization.Format("Preview {0:0} × {1:0} px",s.HoverWidth,s.HoverHeight));break;
            case "Enabled widgets & order":
                foreach(var tile in widgetTiles){var bounds=tile.Bounds;Box(bounds.X,bounds.Y,bounds.Width,bounds.Height);dc.DrawText(tile.Label,new Point(bounds.X+6,bounds.Y+6));}Caption("Visible panels, in your chosen order",14,Math.Max(99,widgetBottom+12),11,false,muted);break;
            case "Individual tools":
                int ti=0;foreach(var item in s.Tools.Take(7)){double x=16+ti%4*100,y=10+ti/4*40;Caption(Settings.ToolNames[item.Id],x,y,10,false,item.Enabled?white:muted,92);Toggle(x,y+18,item.Enabled);ti++;}Note("Disabled tools disappear; their saved data stays");break;
            case "Hover pages":
                var available=HoverPagePolicy.Pages(s);var enabled=available.Take(4).ToArray();for(int i=0;i<enabled.Length;i++)
                {
                    double x=18+i*99;string id=enabled[i];Box(x,12,88,72,Brushes.Black);
                    Caption(id switch{"media"=>"Music","files"=>"Shelf","notes"=>"Notes","clips"=>"Clipboard",_=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id)},x+8,20,10,true,null,72);
                    switch(id)
                    {
                        case "media":Cover(x+8,41,22);Bar(x+36,44,42,.7);Bar(x+36,53,32,.5);Bar(x+8,72,71,.35);break;
                        case "timer":Caption("05:00",x+10,40,18,true);Bar(x+10,69,67,.45);break;
                        case "focus":dc.DrawEllipse(null,new Pen(muted,2),new Point(x+21,53),10,10);Caption("25:00",x+37,47,11,true,null,45);Caption("Work session",x+9,69,9);break;
                        case "calendar":Caption("16",x+11,39,21,true);Caption("OCT",x+43,48,10);Bar(x+10,73,66,.6);break;
                        case "tasks":for(int j=0;j<3;j++){dc.DrawRectangle(null,line,new Rect(x+10,42+j*12,6,6));Bar(x+23,45+j*12,50,.7);}break;
                        case "files":for(int j=0;j<3;j++)Box(x+11+j*11,49-j*4,28,25,UI.Brush(j==2?"#737D8C":"#353B45"),3);break;
                        case "system":Caption("CPU   RAM",x+9,42,9);Bar(x+9,59,69,.3);Bar(x+9,70,69,.6);break;
                        default:Bar(x+10,45,65,.9);Bar(x+10,56,65,.6);Bar(x+10,67,65,.75);break;
                    }
                }FormattedNote(Localization.Format("{0} available pages · first {1} shown in this example",available.Length,enabled.Length));break;
            case "Clipboard history":
                Caption("Text",22,20,12,true);Caption("Copied thought",22,42,11,false,muted);Caption("Link",158,20,12,true);Text("example.com",158,42,11,false,muted);Box(296,18,40,40);dc.DrawEllipse(muted,null,new Point(324,28),3,3);Line(301,50,312,35);Line(312,35,331,52);Caption("Image",346,30,11);Toggle(22,74,s.ClipboardHistory);Caption(s.ClipboardHistory?"History enabled":"History off",64,74,11);Note("Example items · turning history off deletes saved items");break;
            case "Stopwatch":
                Caption("Stopwatch",28,13,12,true);Caption("00:00",28,33,28,true);Keys("Start",205,29,78);Keys("Reset",292,29,78);Note("Start → Pause → Resume · Reset clears elapsed time");break;
            case "Focus sessions":
                Caption(s.FocusMinutes+":00",28,20,28,true);Caption("Focus",30,59,12,false,muted);Line(170,25,170,77);Caption(s.BreakMinutes+":00",224,20,28,true);Caption("Break",226,59,12,false,muted);Note("Used when the next session starts");break;
            case "Automatic tucking":
                Box(20,18,112,35,Brushes.Black);Caption("Visible",45,66,11);Box(154,18,112,s.TuckEdge,Brushes.Black,2);Text(Localization.Format("Tucked · {0:0} DIP",s.TuckEdge),164,66,11);dc.DrawRoundedRectangle(null,line,new Rect(288,18,112,35),8,8);Caption("Hidden",320,66,11);Note(s.AlwaysTucked?"Always tucked · hover the edge for a preview":"Automatic rules · manual hide removes the whole island");break;
            case "Manual tuck shortcut":
                Keys(s.TuckHotkeyModifiers,24,24,130);Keys(s.TuckHotkeyKey,166,24,40);Caption("Tuck / automatic",232,30,13,true);Note("First press tucks · second returns to automatic rules");break;
            case "Panel navigation":
                Box(20,15,146,68,Brushes.Black);Caption("Media",35,28,14,true);Line(182,48,234,48);Box(252,15,146,68,Brushes.Black);Caption("Status",268,28,14,true);Note("Swipe empty space · Shift + wheel · Ctrl + Tab");break;
            case "Drag to Island":
                for(int i=0;i<3;i++){Box(28+i*12,34-i*8,42,44,UI.Brush(i==2?"#737D8C":"#3C424C"),4);}Line(110,48,198,48);Box(218,19,180,64,Brushes.Black);Caption("Drop files here",239,32,14,true);Caption("References · originals stay",230,59,11);Note(s.DropReveal?"Dragging reveals the drop area":"Drop reveal disabled");break;
            case "Detachable previews":
                Box(18,15,151,68,Brushes.Black);Bar(79,24,30,1);Caption("Island preview",33,49,12);Line(185,48,224,61);Box(244,31,151,57,Brushes.Black);Caption("Mini window",260,49,12,true);Note("Drag the grip to position · or use Detach");break;
            case "Closed cover gestures":
                Cover(22,22,38);Caption("Click",80,21,13,true);Caption("Play / pause",80,43,11);Caption("Hold 500 ms",240,21,13,true);Caption("Choose media session",240,43,11);Note(s.CoverGestures?"Enabled when the closed icon uses album artwork":"Cover gestures disabled");break;
            case "Automatic activity":
                Caption("New song / session ending",24,20,14,true);Island(26,51,225,30);Caption("Brief preview",274,57,11);Note(s.SmartActivities?"Appears without taking keyboard focus":"Automatic previews disabled");break;
            case "Notch gestures":
                Caption("Mouse wheel",24,22,13,true);Caption(s.WheelAction switch{"volume"=>"Volume","pages"=>"Switch tabs",_=>"Off"},24,49,14);Caption("Middle click",232,22,13,true);Caption(s.MiddleAction switch{"toggle"=>"Play / pause","microphone"=>"Mute microphone",_=>"Off"},232,49,14);Note("Hover previews always use the wheel to browse");break;
            case "Global hotkey":case "Hide or show the island":
                bool hide=kind=="Hide or show the island";Keys(hide?s.VisibilityHotkeyModifiers:s.HotkeyModifiers,24,24,130);Keys(hide?s.VisibilityHotkeyKey:s.HotkeyKey,166,24,40);Caption(hide?"Hide / show":"Open / close panel",232,30,13,true);Note("Works while another app is focused");break;
            case "Closed media display":
                Silhouette(32,22,352,40,s.Preset=="Mac");if(s.ClosedMediaIcon=="Artwork")Cover(46,31,22);else Wave(46,42);if(s.ClosedMediaContent=="Waveform")Wave(158,42);else Caption("Song title",160,34,13,true);Caption("12:45",334,36,11,true);Note("Current closed media layout · example title and artwork");break;
            case "Visualizer":case "Waveform source":case "Live signal":
                Caption(s.WaveformSource=="Spectrum"?"Bass → mids → treble":"Amplitude history",22,18,13,true);Wave(125,62);Text(Localization.Format("{0:0.##}× sensitivity",s.WaveformSensitivity),270,38,12);Note(s.WaveformSource=="Spectrum"?"Real frequency bands · selected media output mix":"Measured selected-app amplitude · fast attack, smooth release");break;
            case "Lyrics":
                Caption("A little closer to the music",24,17,11,false,muted);Caption("One line, right on time",24,42,22,true);Caption("Slide     Focus     Karaoke",24,77,11,false,muted);Toggle(355,20,s.LyricsEnabled);FormattedNote(Localization.Format("{0} · Synced lyrics via LRCLIB · no key required",Localization.Text(s.LyricsAnimation)));break;
            case "Move & resize":
                Island(95,22,220,36);Line(88,18,66,18);Line(66,18,66,83);Line(66,83,88,83);Caption("↔",180,70,20);Toggle(352,28,s.DockUnlocked);Note("Drag the top grip to move · lower corners resize each state");break;
            case "Completion sound":
                Caption(s.CountdownSound=="Off"?"Silent":"♪",25,18,28,true);Wave(119,50);Caption(s.CountdownSound,264,25,16,true);Caption((s.CountdownSoundVolume*100).ToString("0")+"%",264,53,12,false,muted);Note("Timer & Focus · audible without Windows notifications");break;
            case "Live feedback":
                Island(24,23,208,34);Bar(54,47,146,.66);Caption("Volume / brightness",252,27,12);Caption("Temporary feedback",252,50,11,false,muted);FormattedNote(Localization.Format("Volume {0} · brightness {1} · quiet motion {2}",Localization.Text(s.VolumeFeedback?"on":"off"),Localization.Text(s.BrightnessFeedback?"on":"off"),Localization.Text(s.WaveformIdleMotion?"on":"off")));break;
            case "Volume mixer":case "Audio behavior":
                Caption("Apps",24,18,13,true);Caption("Output",246,18,13,true);Caption("Music app",24,46,11);Bar(96,55,114,.7);Caption("Windows default",246,46,11);Caption("Mute or set volume",24,75,11,false,muted);Note("Illustrative controls · open the mixer to adjust real audio");break;
            case "Header wheel":
                Caption("One wheel step",24,22,15,true);Caption($"+{s.VolumeStep}%",264,20,26,true);Bar(24,73,365,.6);Note("Volume change per notch when wheel action is Volume");break;
            case "Clock":
                Caption(s.Clock24Hour?"14:35"+(s.ClockSeconds?":08":""):"2:35"+(s.ClockSeconds?":08":"")+" PM",26,18,30,true);Caption(s.ShowDate?"Friday, 16 October":"Date hidden",27,60,12,false,muted);Note("Format example · seconds appear in Status");break;
            case "Monitor details":
                Caption("CPU · Memory",24,20,14,true);Caption("Always in Overview",24,47,11,false,muted);Text(Localization.Format("Network   {0}",Localization.Text(s.MonitorNetwork?"On":"Off")),244,17,11);Text(Localization.Format("Storage     {0}",Localization.Text(s.MonitorStorage?"On":"Off")),244,39,11);Text(Localization.Format("Processes  {0}",Localization.Text(s.MonitorProcesses?"On":"Off")),244,61,11);Note("Additional details are available in their Status tabs");break;
            case "Launch display":
                Caption("Launch",24,18,16,true);Text(Localization.Format("Current style: {0}",Localization.Text(s.LaunchStyle.Replace("IconsLabels","Icons + labels"))),24,50,13);Note("Saved apps and shortcuts use this display style");break;
            case "Shortcuts":
                Caption("Launch",24,18,15,true);Text(Localization.Format("{0} saved shortcuts",s.Shortcuts.Count),250,22,11,false,muted);int si=0;foreach(var item in s.Shortcuts.Take(3)){Box(24+si*128,49,116,29);Text(item.Name,32+si++*128,57,11,false,null,100);}if(si==0)Caption("Add an app, folder or website below",24,56,12);Note("Only your saved shortcuts appear in Launch");break;
            case "Layout profile":
                Caption("Appearance + layout",24,22,16,true);Caption("Content order + gestures",24,50,12);Keys("Export / Import",250,27,143);Note("Excludes notes, clipboard items, app targets and startup");break;
            case "Ambient light":
                Box(36,12,348,82,Brushes.Black,16);dc.PushClip(new RectangleGeometry(new Rect(36,12,348,82),16,16));
                var ambientPalette=(System.Windows.Application.Current as App)?.AmbientAudio?.CoverColors??default;
                var ambientColors=AmbientPolicy.ColorsFor(s,ambientPalette);
                if(s.CoverAmbient){var light=new DrawingGroup{OpacityMask=AmbientPaint.BottomFade(s.AmbientSpread,AmbientLook.From(s))};light.Children.Add(new GeometryDrawing(AmbientPaint.ColorBand(ambientColors,s.AmbientStrength,AmbientLook.From(s)),null,new RectangleGeometry(new Rect(36,12,348,82))));dc.DrawDrawing(light);}dc.Pop();Cover(51,28,36);Caption(s.AmbientScope,102,30,15,true);Text(Localization.Text(s.AmbientAudioResponse)+" · "+(int)(s.AmbientStrength*100)+"%",102,56,11);FormattedNote(Localization.Format("{0} · {1} · audio-reactive",Localization.Text(s.AmbientColorSource),Localization.Text(s.AmbientMotion)));break;
            case "Notification presentation":
                Box(46,10,328,77,Brushes.Black,16);Caption("Focus complete",68,25,16,true);Caption("Time for a short break",68,52,11,false,muted);Caption("2 queued",286,28,10,false,muted);Bar(68,73,280,.65);FormattedNote(Localization.Format("{0} · {1} sec · {2}",Localization.Text(s.NotificationAnimation),s.NotificationSeconds,Localization.Text(s.QuietHours?"quiet hours on":"quiet hours off")));break;
            case "Notifications":
                Box(36,18,215,34,Brushes.Black,17);Caption("Timer finished",53,29,11,true);Keys(s.NotificationDelivery,274,20,110);Note("Notch / Windows / Both / Off · hidden island uses Windows");break;
            case "Other apps":
                Box(24,18,42,47);Text("C",38,26,20,true);Caption("Claude",83,22,14,true);Caption("Your answer is ready",83,49,11,false,muted);Toggle(350,25,s.ExternalNotifications);Note("Example only · alerts other apps show in Windows");break;
            case "Downloads":
                Box(24,18,42,47);Caption("↓",36,25,21);Text("Report.pdf",83,22,14,true);Caption("2 arrivals · drag originals",83,49,11,false,muted);Toggle(350,25,s.DownloadsEnabled);Note("Example only · folder arrivals inferred after stable readable size");break;
            case "Updates":
                Text(Localization.Format("Installed {0}",OfficialUpdates.InstalledVersion),22,15,13,true);Caption("Example: newer stable",225,15,12,true);Bar(24,55,365,.6);Caption("Check > download 60% > verify > install",24,70,12);FormattedNote(Localization.Format("Automatic checks {0} - no automatic installation",Localization.Text(s.AutoCheckUpdates?"on":"off")));break;
            case "Reset preferences":
                Keys("Your preferences",20,24,128);Line(158,39,196,39);Keys("Backup saved",208,24,120);Caption("Reset selected category",22,71,12,true);Note("Notes, Tasks, startup and clipboard capture stay intact");break;
            case "Behavior":
                Caption("Windows sign-in",24,23,15,true);Line(163,41,217,41);Caption("NabyScape",240,23,15,true);Toggle(244,60,s.AutoStart);Caption(s.AutoStart?"Starts automatically":"Start manually",24,68,12,false,muted);break;
            case "Local storage":
                Caption("Saved on this PC",24,21,17,true);Caption("Preferences · Notes · Tasks",24,51,12);Caption("No account required",24,77,11,false,muted);break;
            case "About NabyScape":
                Caption("NabyScape",24,20,23,true);Caption("Windows 11 · x64",24,60,12,false,muted);Text(Localization.Format("Version {0}",typeof(App).Assembly.GetName().Version!.ToString(3)),250,31,12);break;
            case "Language & accessibility":
                Caption("Language",24,18,12,true);Keys(s.UiLanguage=="ru"?"Русский":s.UiLanguage=="es"?"Español":s.UiLanguage=="id"?"Bahasa Indonesia":"English",210,12,176);
                Caption("Text size",24,55,12,true);Caption((s.AccessibilityTextScale*100).ToString("0",CultureInfo.InvariantCulture)+"%",218,55,12);Toggle(350,55,s.AccessibilityHighContrast);Note("Language · text size · contrast · Windows motion preferences");break;
            case "Private diagnostics":
                Caption("Review before sharing",24,18,16,true);Caption("App version · service status · controlled events",24,48,11,false,muted);Keys("Copy",24,73,95);Keys("Export",133,73,95);Note("No automatic upload · conversations and credentials excluded");break;
            default:throw new InvalidOperationException("Missing Settings example: "+kind);
        }
        dc.Pop();
    }
}
