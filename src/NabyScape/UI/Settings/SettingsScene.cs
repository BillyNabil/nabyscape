using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace NabyScape;

// Static, local examples of the controls in this section. Repaint values without rebuilding editors.
internal sealed class SettingsScene : FrameworkElement
{
    private readonly string kind;
    internal string Section=>kind;
    private readonly Settings s;
    internal SettingsScene(string kind,Settings settings){this.kind=kind;s=settings;Height=126;ClipToBounds=true;IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext dc)
    {
        if(ActualWidth<=0)return;
        double scale=Math.Min(1,ActualWidth/420);dc.PushTransform(new ScaleTransform(scale,scale));
        var white=UI.Brush("#F3F4F6");var muted=UI.Brush("#B3B8C2");var fill=UI.Brush("#22252B");var line=new Pen(UI.Brush("#555B65"),1);
        void Box(double x,double y,double w,double h,Brush? brush=null,double r=8)=>dc.DrawRoundedRectangle(brush??fill,null,new Rect(x,y,w,h),r,r);
        void Text(string value,double x,double y,double size=12,bool bold=false,Brush? brush=null,double maxWidth=400)
        {var t=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,bold?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),size,brush??white,VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=Math.Max(10,Math.Min(maxWidth,405-x)),MaxTextHeight=42,Trimming=TextTrimming.CharacterEllipsis};dc.DrawText(t,new Point(x,y));}
        void Line(double x,double y,double xx,double yy)=>dc.DrawLine(line,new Point(x,y),new Point(xx,yy));
        void Toggle(double x,double y,bool on){Box(x,y,30,16,on?white:fill,8);dc.DrawEllipse(on?fill:muted,null,new Point(x+(on?22:8),y+8),5,5);}
        void Bar(double x,double y,double w,double value){Box(x,y,w,3,fill,1);Box(x,y,Math.Max(1,w*Math.Clamp(value,0,1)),3,white,1);}
        void Keys(string key,double x,double y,double w=90){Box(x,y,w,30);Text(key,x+10,y+7,11);}
        void Island(double x=105,double y=20,double w=210,double h=34){Box(x,y,w,h,UI.Brush(s.Background),s.Preset=="Mac"?6:17);Text("Music",x+14,y+9,11,true);Text("12:45",x+w-48,y+9,11,true);}
        void Wave(double x,double y){for(int i=0;i<25;i++){double h=3+(i*13%19);Box(x+i*3.6,y-h/2,1.5,h,white,1);}}
        void Cover(double x,double y,double size)
        {
            if(App.Current.Media.Artwork is ImageSource artwork){dc.PushClip(new RectangleGeometry(new Rect(x,y,size,size),5,5));dc.DrawImage(artwork,new Rect(x,y,size,size));dc.Pop();}
            else {Box(x,y,size,size,UI.Brush("#737D8C"),5);dc.DrawEllipse(null,new Pen(white,1),new Point(x+size/2,y+size/2),size*.27,size*.27);}
        }
        void Note(string message)=>Text(message,14,99,11,false,muted);
        Box(0,0,420,124,UI.Brush("#181A1F"),10);
        switch(kind)
        {
            case "Shape":
                Box(28,22,160,36,Brushes.Black,6);Box(228,22,160,36,Brushes.Black,18);Text("Notch · attached to edge",30,69,11);Text("Pill · floating",230,69,11);Note("Current: "+(s.Preset=="Mac"?"Notch":"Pill"));break;
            case "Color & material":
                Box(22,19,216,68,UI.Brush(s.Background));Box(36,33,34,34,UI.Brush(s.Accent));Text("Song title",82,34,14,true,UI.ContrastText(s.Background));Text(s.GlassStyle?"Black glass":"Solid surface",82,57,11,false,UI.ContrastText(s.Background));Text($"Opacity {s.Opacity*100:0}%",256,28);Text($"Glass {s.GlassStrength*100:0}%",256,52);Note("Your island color and material");break;
            case "Surface":
                var glass=new LinearGradientBrush(UI.Brush(s.Background).Color,UI.Brush("#3E424A").Color,90){Opacity=.25+s.GlassStrength*.6};
                Box(24,18,176,61,Brushes.Black);Box(224,18,176,61,glass);Text("Solid",39,38,14,true);Text("Glass",240,38,14,true);Note($"Glass intensity {s.GlassStrength*100:0}% · black glass {(s.GlassStyle?"on":"off")}");break;
            case "Controls & outlines":
                Box(18,24,112,34,UI.Brush(s.ControlFill));Text("Button",36,33,12,false,UI.ContrastText(s.ControlFill));dc.DrawRoundedRectangle(null,new Pen(UI.Brush(s.ControlBorder),1),new Rect(146,24,122,34),6,6);Text("Input field",158,33);Box(284,24,116,34,UI.Brush(s.SelectionFill));Text("Selected tab",296,33,12,false,UI.ContrastText(s.SelectionFill));Note("Button fill · input outline · selected state");break;
            case "Typography":
                Text("Song title",24,23,21,s.BoldMediaTitle);Text("Artist name",24,54,12,false,muted);Text("Panel heading",232,27,16,s.BoldHeadings);Text("Body text stays regular",232,56,11,false,muted);Note("Current title weights; body text stays the same");break;
            case "Text and navigation":
                foreach(var item in new[]{("Media",22d),("Focus",112d),("Status",202d),("Tools",292d)}){Box(item.Item2,22,76,46);if(s.CategoryStyle!="Text")dc.DrawEllipse(null,new Pen(white,1.4),new Point(item.Item2+38,36),6,6);if(s.CategoryStyle!="Icons")Text(item.Item1,item.Item2+14,s.CategoryStyle=="Text"?38:49,11);}Note("Navigation · "+s.CategoryStyle.Replace("IconsLabels","Icons + labels"));break;
            case "Motion":
                Box(23,28,68,22,Brushes.Black);Text("Music",34,33,9);Line(108,40,176,40);Box(193,16,135,63,Brushes.Black);Cover(203,25,22);Text("Song title",234,28,10,true);Bar(204,65,112,.4);Text(s.ReduceMotion?"Instant state change":$"{s.AnimationMs:0} ms transition",23,78,11);Text("Closed → open",193,87,11,false,muted);break;
            case "Animation performance":
                Text("Eco",24,16,12,true);Text("Auto",24,42,12,true);Text("Maximum",24,68,12,true);for(int row=0;row<3;row++)for(int i=0;i<(row+1)*8;i++)Box(120+i*10,20+row*26,3,12,white,1);Note("Illustrative update cadence · your display sets the visible limit");break;
            case "Display":
                dc.DrawRoundedRectangle(null,line,new Rect(24,17,166,67),5,5);dc.DrawRoundedRectangle(null,line,new Rect(220,17,166,67),5,5);Box(70,18,76,12,white,3);Text("Primary display",50,58,11);Text("Other display",250,58,11);Note(string.IsNullOrEmpty(s.Monitor)?"Follows your primary display":"Selected display: "+s.Monitor);break;
            case "Closed island":
                Island(90,18,240,34);Line(90,69,330,69);Line(90,63,90,75);Line(330,63,330,75);Text($"{s.Width:0} × {s.Height:0} px · radius {s.Radius:0} px",115,77,11);Note($"Horizontal position {s.Horizontal:0}% · top offset {s.Top:0} px");break;
            case "Open panel":
                Box(24,12,210,76,Brushes.Black);Box(37,23,35,14);Text("Media",86,25,10);Text("Song title",42,48,13,true);Bar(42,73,172,.4);Text($"{s.PanelWidth:0} px wide",253,24);Text($"Inset {s.PanelPadding:0} px",253,47,11);Text($"Text {s.PanelScale*100:0}%",253,67,11);Note($"Maximum height {s.PanelMaxHeight:0} px · spacing {s.SectionGap:0} px");break;
            case "Hover preview":case "Hover & swipe":
                Island(20,18,126,27);Line(159,37,199,37);Box(212,12,186,73,Brushes.Black);Text("Song title",226,24,13,true);Bar(226,61,156,.35);Text("Hover",45,63,11);Note(kind=="Hover & swipe"?$"Preview after {s.HoverDelayMs} ms · detail after {s.HoverDetailDelayMs} ms":$"Preview {s.HoverWidth:0} × {s.HoverHeight:0} px");break;
            case "Enabled widgets & order":
                int wi=0;foreach(var item in s.Widgets.Where(w=>w.Enabled)){double x=16+wi%5*79,y=15+wi/5*35;Box(x,y,72,28);Text(Settings.Names[item.Id],x+6,y+8,10,false,null,60);wi++;}Note("Visible panels, in your chosen order");break;
            case "Individual tools":
                int ti=0;foreach(var item in s.Tools.Take(7)){double x=16+ti%4*100,y=10+ti/4*40;Text(Settings.ToolNames[item.Id],x,y,10,false,item.Enabled?white:muted,92);Toggle(x,y+18,item.Enabled);ti++;}Note("Disabled tools disappear; their saved data stays");break;
            case "Hover pages":
                var enabled=s.HoverViews.Where(v=>v.Enabled).Take(4).ToArray();for(int i=0;i<enabled.Length;i++)
                {
                    double x=18+i*99;string id=enabled[i].Id;Box(x,12,88,72,Brushes.Black);
                    Text(id switch{"media"=>"Music","files"=>"Shelf","notes"=>"Notes","clips"=>"Clipboard",_=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id)},x+8,20,10,true,null,72);
                    switch(id)
                    {
                        case "media":Cover(x+8,41,22);Bar(x+36,44,42,.7);Bar(x+36,53,32,.5);Bar(x+8,72,71,.35);break;
                        case "timer":Text("05:00",x+10,40,18,true);Bar(x+10,69,67,.45);break;
                        case "focus":dc.DrawEllipse(null,new Pen(muted,2),new Point(x+21,53),10,10);Text("25:00",x+37,47,11,true,null,45);Text("Work session",x+9,69,9);break;
                        case "calendar":Text("16",x+11,39,21,true);Text("OCT",x+43,48,10);Bar(x+10,73,66,.6);break;
                        case "tasks":for(int j=0;j<3;j++){dc.DrawRectangle(null,line,new Rect(x+10,42+j*12,6,6));Bar(x+23,45+j*12,50,.7);}break;
                        case "files":for(int j=0;j<3;j++)Box(x+11+j*11,49-j*4,28,25,UI.Brush(j==2?"#737D8C":"#353B45"),3);break;
                        case "system":Text("CPU   RAM",x+9,42,9);Bar(x+9,59,69,.3);Bar(x+9,70,69,.6);break;
                        default:Bar(x+10,45,65,.9);Bar(x+10,56,65,.6);Bar(x+10,67,65,.75);break;
                    }
                }Note("Example content · first pages in your current hover order");break;
            case "Clipboard history":
                Text("Text",22,20,12,true);Text("Copied thought",22,42,11,false,muted);Text("Link",158,20,12,true);Text("example.com",158,42,11,false,muted);Box(296,18,40,40);dc.DrawEllipse(muted,null,new Point(324,28),3,3);Line(301,50,312,35);Line(312,35,331,52);Text("Image",346,30,11);Toggle(22,74,s.ClipboardHistory);Text(s.ClipboardHistory?"History enabled":"History off",64,74,11);Note("Example items · turning history off deletes saved items");break;
            case "Focus sessions":
                Text(s.FocusMinutes+":00",28,20,28,true);Text("Focus",30,59,12,false,muted);Line(170,25,170,77);Text(s.BreakMinutes+":00",224,20,28,true);Text("Break",226,59,12,false,muted);Note("Used when the next session starts");break;
            case "Automatic tucking":
                Box(20,15,178,69,UI.Brush("#292D35"));Box(71,15,76,4,white);Text("Maximized app",48,48,11);Box(226,15,174,69,UI.Brush("#292D35"));Box(273,15,80,22,Brushes.Black);Text("Hover top edge",252,55,11);Note($"Maximized: {(s.HideMaximized?"tuck":"visible")} · fullscreen: {(s.HideFullscreen?"tuck":"visible")}");break;
            case "Panel navigation":
                Box(20,15,146,68,Brushes.Black);Text("Media",35,28,14,true);Line(182,48,234,48);Box(252,15,146,68,Brushes.Black);Text("Status",268,28,14,true);Note("Swipe empty space · Shift + wheel · Ctrl + Tab");break;
            case "Drag to Island":
                for(int i=0;i<3;i++){Box(28+i*12,34-i*8,42,44,UI.Brush(i==2?"#737D8C":"#3C424C"),4);}Line(110,48,198,48);Box(218,19,180,64,Brushes.Black);Text("Drop files here",239,32,14,true);Text("References · originals stay",230,59,11);Note(s.DropReveal?"Dragging reveals the drop area":"Drop reveal disabled");break;
            case "Detachable previews":
                Box(18,15,151,68,Brushes.Black);Bar(79,24,30,1);Text("Island preview",33,49,12);Line(185,48,224,61);Box(244,31,151,57,Brushes.Black);Text("Mini window",260,49,12,true);Note("Drag the grip to position · or use Detach");break;
            case "Closed cover gestures":
                Cover(22,22,38);Text("Click",80,21,13,true);Text("Play / pause",80,43,11);Text("Hold 500 ms",240,21,13,true);Text("Choose media session",240,43,11);Note(s.CoverGestures?"Enabled when the closed icon uses album artwork":"Cover gestures disabled");break;
            case "Two live activities":
                Island(22,24,236,35);Box(274,24,122,35,Brushes.Black,17);Text(s.SatelliteSource=="Auto"?"Auto companion":s.SatelliteSource,288,35,11);Note("Companion hides while the preview or panel is open");break;
            case "Automatic activity":
                Text("New song / session ending",24,20,14,true);Island(26,51,225,30);Text("Brief preview",274,57,11);Note(s.SmartActivities?"Appears without taking keyboard focus":"Automatic previews disabled");break;
            case "Notch gestures":
                Text("Mouse wheel",24,22,13,true);Text(s.WheelAction switch{"volume"=>"Volume","pages"=>"Switch tabs",_=>"Off"},24,49,14);Text("Middle click",232,22,13,true);Text(s.MiddleAction switch{"toggle"=>"Play / pause","microphone"=>"Mute microphone",_=>"Off"},232,49,14);Note("Hover previews always use the wheel to browse");break;
            case "Global hotkey":case "Hide or show the island":
                bool hide=kind=="Hide or show the island";Keys(hide?s.VisibilityHotkeyModifiers:s.HotkeyModifiers,24,24,130);Keys(hide?s.VisibilityHotkeyKey:s.HotkeyKey,166,24,40);Text(hide?"Hide / show":"Open / close panel",232,30,13,true);Note("Works while another app is focused");break;
            case "Closed media display":
                Box(32,22,352,40,Brushes.Black);if(s.ClosedMediaIcon=="Artwork")Cover(46,31,22);else Wave(46,42);if(s.ClosedMediaContent=="Waveform")Wave(158,42);else Text("Song title",160,34,13,true);Text("12:45",334,36,11,true);Note("Current closed media layout · example title and artwork");break;
            case "Waveform source":case "Live signal":
                Text("Selected media app",22,20,13,true);Line(166,42,222,42);Wave(256,43);Text("Amplitude history",246,67,11,false,muted);Note("Measured audio levels; these are not frequency bands");break;
            case "Live feedback":
                Island(24,23,208,34);Bar(54,47,146,.66);Text("Volume / brightness",252,27,12);Text("Temporary feedback",252,50,11,false,muted);Note($"Volume {(s.VolumeFeedback?"on":"off")} · brightness {(s.BrightnessFeedback?"on":"off")} · quiet motion {(s.WaveformIdleMotion?"on":"off")}");break;
            case "Volume mixer":case "Audio behavior":
                Text("Apps",24,18,13,true);Text("Output",246,18,13,true);Text("Music app",24,46,11);Bar(96,55,114,.7);Text("Windows default",246,46,11);Text("Mute or set volume",24,75,11,false,muted);Note("Illustrative controls · open the mixer to adjust real audio");break;
            case "Header wheel":
                Text("One wheel step",24,22,15,true);Text($"+{s.VolumeStep}%",264,20,26,true);Bar(24,73,365,.6);Note("Volume change per notch when wheel action is Volume");break;
            case "Clock":
                Text(s.Clock24Hour?"14:35"+(s.ClockSeconds?":08":""):"2:35"+(s.ClockSeconds?":08":"")+" PM",26,18,30,true);Text(s.ShowDate?"Friday, 16 October":"Date hidden",27,60,12,false,muted);Note("Format example · seconds appear in Status");break;
            case "Monitor details":
                Text("CPU · Memory",24,20,14,true);Text("Always in Overview",24,47,11,false,muted);Text("Network   "+(s.MonitorNetwork?"On":"Off"),244,17,11);Text("Storage     "+(s.MonitorStorage?"On":"Off"),244,39,11);Text("Processes  "+(s.MonitorProcesses?"On":"Off"),244,61,11);Note("Additional details are available in their Status tabs");break;
            case "Launch display":
                Text("Launch",24,18,16,true);Text("Current style: "+s.LaunchStyle.Replace("IconsLabels","Icons + labels"),24,50,13);Note("Saved apps and shortcuts use this display style");break;
            case "Shortcuts":
                Text("Launch",24,18,15,true);Text(s.Shortcuts.Count+" saved shortcuts",250,22,11,false,muted);int si=0;foreach(var item in s.Shortcuts.Take(3)){Box(24+si*128,49,116,29);Text(item.Name,32+si++*128,57,11,false,null,100);}if(si==0)Text("Add an app, folder or website below",24,56,12);Note("Only your saved shortcuts appear in Launch");break;
            case "Activity actions":
                Keys("Open app",22,30,106);Line(139,45,159,45);Keys("Start Focus",170,30,106);Line(287,45,307,45);Keys("Keep awake",316,30,90);Note(s.ActivitiesEnabled?"Example routine · starts only when you press Start":"Activities hidden; saved routines are kept");break;
            case "Layout profile":
                Text("Appearance + layout",24,22,16,true);Text("Content order + gestures",24,50,12);Keys("Export / Import",250,27,143);Note("Excludes notes, clipboard items, app targets and startup");break;
            case "Behavior":
                Text("Windows sign-in",24,23,15,true);Line(163,41,217,41);Text("NabyScape",240,23,15,true);Toggle(244,60,s.AutoStart);Text(s.AutoStart?"Starts automatically":"Start manually",24,68,12,false,muted);break;
            case "Local storage":
                Text("Saved on this PC",24,21,17,true);Text("Preferences · Notes · Tasks",24,51,12);Text("No account required",24,77,11,false,muted);break;
            case "About NabyScape":
                Text("NabyScape",24,20,23,true);Text("Windows 11 · x64",24,60,12,false,muted);Text("Version "+typeof(App).Assembly.GetName().Version!.ToString(3),250,31,12);break;
            default:throw new InvalidOperationException("Missing Settings example: "+kind);
        }
        dc.Pop();
    }
}
