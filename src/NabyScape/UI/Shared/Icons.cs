using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NabyScape;

internal static class Icons
{
    private static readonly Dictionary<string,string> Paths=new()
    {
        ["mixer"]="M3,2 L5,2 5,8 3,8 Z M3,14 L5,14 5,22 3,22 Z M1,8 L7,8 7,14 1,14 Z M11,2 L13,2 13,13 11,13 Z M11,19 L13,19 13,22 11,22 Z M9,13 L15,13 15,19 9,19 Z M19,2 L21,2 21,6 19,6 Z M19,12 L21,12 21,22 19,22 Z M17,6 L23,6 23,12 17,12 Z",
        ["search"]="M10,2 A8,8 0 1 0 10,18 A8,8 0 1 0 10,2 Z M16,16 L22,22 23,21 17,15 Z",
        ["copy"]="M8,7 L21,7 21,22 8,22 Z M3,2 L16,2 16,5 6,5 6,17 3,17 Z",
        ["stop-filled"]="M6,6 H18 V18 H6 Z",
        ["play"]="M7,4 L20,12 7,20 Z",
        ["chevron"]="M4,7 L12,15 20,7 22,9 12,19 2,9 Z",
        ["pause"]="M6,4 L10,4 10,20 6,20 Z M14,4 L18,4 18,20 14,20 Z",
        ["next"]="M3,5 L13,12 3,19 Z M13,5 L22,12 13,19 Z",
        ["previous"]="M21,5 L11,12 21,19 Z M11,5 L2,12 11,19 Z",
        ["volume"]="M3,9 L7,9 12,5 12,19 7,15 3,15 Z M16,7 Q22,12 16,17 L15,15 Q19,12 15,9 Z",
        ["muted"]="M3,9 L7,9 12,5 12,19 7,15 3,15 Z M16,8 L19,11 22,8 23,9 20,12 23,15 22,16 19,13 16,16 15,15 18,12 15,9 Z",
        ["music"]="M10,4 L20,2 20,16 A3.5,3.5 0 1 1 18,13 L18,6 10,8 10,19 A3.5,3.5 0 1 1 8,16 L8,5 Z",
        ["activity"]="M3,9 Q3,8 4,8 Q5,8 5,9 L5,15 Q5,16 4,16 Q3,16 3,15 Z M8,5 Q8,4 9,4 Q10,4 10,5 L10,19 Q10,20 9,20 Q8,20 8,19 Z M13,7 Q13,6 14,6 Q15,6 15,7 L15,17 Q15,18 14,18 Q13,18 13,17 Z M18,10 Q18,9 19,9 Q20,9 20,10 L20,14 Q20,15 19,15 Q18,15 18,14 Z",
        ["settings"]="M12,2 L14,2 15,5 17,6 20,6 22,10 20,12 20,14 21,17 18,20 15,19 13,20 11,22 7,20 7,17 5,15 2,14 2,10 5,9 6,7 6,4 10,2 Z M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z",
        ["timer"]="M9,1 L15,1 15,3 9,3 Z M12,5 A8,8 0 1 1 11.99,5 Z M11,8 L11,14 16,14 16,12 13,12 13,8 Z",
        ["focus"]="M16,2 A10,10 0 1 0 22,16 A9,9 0 0 1 16,2 Z",
        ["clock"]="M12,2 A10,10 0 1 0 12.01,2 Z M11,6 L11,13 17,16 18,14 13,11.5 13,6 Z",
        ["chip"]="M6,6 L18,6 18,18 6,18 Z M9,9 L9,15 15,15 15,9 Z M8,2 L10,2 10,6 8,6 Z M14,2 L16,2 16,6 14,6 Z M8,18 L10,18 10,22 8,22 Z M14,18 L16,18 16,22 14,22 Z M2,8 L6,8 6,10 2,10 Z M2,14 L6,14 6,16 2,16 Z M18,8 L22,8 22,10 18,10 Z M18,14 L22,14 22,16 18,16 Z",
        ["arrow"]="M5,4 L20,4 20,19 17,19 17,9 6,20 4,18 15,7 5,7 Z",
        ["battery"]="M2,6 L19,6 Q21,6 21,8 L21,16 Q21,18 19,18 L2,18 Q0,18 0,16 L0,8 Q0,6 2,6 Z M3,8 L3,16 18,16 18,8 Z M22,9 L24,9 24,15 22,15 Z"
    };
    // Lucide icons, ISC license in LUCIDE-LICENSE.txt. Consistent 24px canvas.
    private static readonly Dictionary<string,string> NavigationPaths=new()
    {
        ["search"]="M10.5,3 A7.5,7.5 0 1 1 10.49,3 M16,16 L21,21",
        ["reset"]="M4,10 A8,8 0 1 1 5,18 M4,4 V10 H10",
        ["plus"]="M5,12 H19 M12,5 V19",
        ["weather"]="M12,3 V1 M5,5 L3,3 M19,5 L21,3 M3,12 H1 M5,17 L3,19 M7,12 A5,5 0 1 1 17,12 M9,20 H19 A4,4 0 0 0 19,12 A5,5 0 0 0 9,13 A3.5,3.5 0 0 0 9,20",
        ["folder"]="M3,7 H10 L12,9 H21 V20 H3 Z M3,7 V4 H10 L12,6 H21 V9",
        ["notes"]="M14,2 H6 Q4,2 4,4 V20 Q4,22 6,22 H18 Q20,22 20,20 V8 Z M14,2 V8 H20 M8,13 H16 M8,17 H14",
        ["clipboard"]="M9,5 H5 V21 H19 V5 H15 M9,3 H15 V7 H9 Z",
        ["coffee"]="M4,8 H17 V16 Q17,20 13,20 H8 Q4,20 4,16 Z M17,9 H20 Q24,13 17,15 M8,3 V5 M13,2 V5",
        ["lyrics"]="M4,5 H20 M4,10 H17 M4,15 H20 M4,20 H13",
        ["chat"]="M5,3 H19 Q21,3 21,5 V15 Q21,17 19,17 H9 L4,21 V17 Q2,17 2,15 V5 Q2,3 5,3 Z M7,8 H16 M7,12 H13",
        ["mic"]="M9,5 A3,3 0 0 1 15,5 V12 A3,3 0 0 1 9,12 Z M5,11 V12 A7,7 0 0 0 19,12 V11 M12,19 V22 M8,22 H16",
        ["mic-off"]="M3,3 L21,21 M9,8 V12 Q9,15 12,15 M15,9 V5 A3,3 0 0 0 10,3 M5,11 V12 A7,7 0 0 0 17,17 M19,12 V11 M12,19 V22 M8,22 H16",
        ["screen"]="M4,4 H20 V17 H4 Z M8,21 H16 M12,17 V21",
        ["send"]="M12,20 V4 M6,10 L12,4 18,10",
        ["trash"]="M3,6 H21 M9,6 V3 H15 V6 M6,6 L7,21 H17 L18,6 M10,10 V17 M14,10 V17",
        ["stop"]="M6,6 H18 V18 H6 Z",
        ["back"]="M15,5 L8,12 15,19",
        ["paperclip"]="M21,11 L12,20 A6,6 0 0 1 4,12 L14,2 A4,4 0 0 1 20,8 L10,18 A2,2 0 0 1 7,15 L16,6",
        ["file-text"]="M14,2 H6 Q4,2 4,4 V20 Q4,22 6,22 H18 Q20,22 20,20 V8 Z M14,2 V8 H20 M8,13 H16 M8,17 H14",
        ["image"]="M5,3 H19 Q21,3 21,5 V19 Q21,21 19,21 H5 Q3,21 3,19 V5 Q3,3 5,3 Z M3,16 L9,10 16,17 M14,15 L17,12 21,16 M14,7 A1,1 0 1 1 13.99,7",
        ["link"]="M10,13 Q13,16 16,13 L20,9 Q23,6 20,3 Q17,0 14,3 L12,5 M14,11 Q11,8 8,11 L4,15 Q1,18 4,21 Q7,24 10,21 L12,19",
        ["x"]="M6,6 L18,18 M18,6 L6,18",
        ["minus"]="M5,12 H19",
        ["volume"]="M3,9 H7 L12,5 V19 L7,15 H3 Z M16,8 Q20,12 16,16 M19,5 Q25,12 19,19",
        ["muted"]="M3,9 H7 L12,5 V19 L7,15 H3 Z M17,9 L23,15 M23,9 L17,15",
        ["calendar"]="M8,2 V6 M16,2 V6 M3,10 H21 M5,4 H19 Q21,4 21,6 V20 Q21,22 19,22 H5 Q3,22 3,20 V6 Q3,4 5,4 Z",
        ["nav-media"]="M12,3 A9,9 0 1 1 11.99,3 M10,8 L16,12 10,16 Z",
        ["nav-focus"]="M9,2 H15 M12,2 V5 M12,5 A8,8 0 1 1 11.99,5 M12,9 V13 L15,15 M18,6 L20,4",
        ["nav-status"]="M5,4 H19 Q21,4 21,6 V15 Q21,17 19,17 H5 Q3,17 3,15 V6 Q3,4 5,4 Z M8,21 H16 M12,17 V21 M7,12 L10,9 13,12 17,8",
        ["nav-launch"]="M 4.0 3.0 H 9.0 Q 10.0 3.0 10.0 4.0 V 9.0 Q 10.0 10.0 9.0 10.0 H 4.0 Q 3.0 10.0 3.0 9.0 V 4.0 Q 3.0 3.0 4.0 3.0 Z M 15.0 3.0 H 20.0 Q 21.0 3.0 21.0 4.0 V 9.0 Q 21.0 10.0 20.0 10.0 H 15.0 Q 14.0 10.0 14.0 9.0 V 4.0 Q 14.0 3.0 15.0 3.0 Z M 15.0 14.0 H 20.0 Q 21.0 14.0 21.0 15.0 V 20.0 Q 21.0 21.0 20.0 21.0 H 15.0 Q 14.0 21.0 14.0 20.0 V 15.0 Q 14.0 14.0 15.0 14.0 Z M 4.0 14.0 H 9.0 Q 10.0 14.0 10.0 15.0 V 20.0 Q 10.0 21.0 9.0 21.0 H 4.0 Q 3.0 21.0 3.0 20.0 V 15.0 Q 3.0 14.0 4.0 14.0 Z",
        ["nav-tools"]="M5,7 H19 Q21,7 21,9 V18 Q21,20 19,20 H5 Q3,20 3,18 V9 Q3,7 5,7 Z M8,7 V5 Q8,3 10,3 H14 Q16,3 16,5 V7 M3,12 Q12,17 21,12 M12,13 V16",
        ["nav-settings"]="M 14 17 H 5 M 19 7 h -9 M 20.0 17.0 A 3.0 3.0 0 1 1 14.0 17.0 A 3.0 3.0 0 1 1 20.0 17.0 M 10.0 7.0 A 3.0 3.0 0 1 1 4.0 7.0 A 3.0 3.0 0 1 1 10.0 7.0",
        ["mixer"]="M 10 5 H 3 M 12 19 H 3 M 14 3 v 4 M 16 17 v 4 M 21 12 h -9 M 21 19 h -5 M 21 5 h -7 M 8 10 v 4 M 8 12 H 3",
        ["settings"]="M 14 17 H 5 M 19 7 h -9 M 20.0 17.0 A 3.0 3.0 0 1 1 14.0 17.0 A 3.0 3.0 0 1 1 20.0 17.0 M 10.0 7.0 A 3.0 3.0 0 1 1 4.0 7.0 A 3.0 3.0 0 1 1 10.0 7.0"
    };
    private static readonly System.Collections.Generic.Dictionary<string,Geometry> geometries=new();
    private static Geometry IconGeometry(string data){lock(geometries){if(geometries.TryGetValue(data,out var geometry))return geometry;geometry=Geometry.Parse(data);geometry.Freeze();geometries.Add(data,geometry);return geometry;}}
    public static FrameworkElement Create(string name,double size=20,Brush? color=null)
    {
        if(Accessibility.HighContrast)color=SystemColors.WindowTextBrush;
        if(NavigationPaths.TryGetValue(name,out var outline))
        {
            var canvas=new Canvas{Width=24,Height=24};
            var stroke=new Path{Data=IconGeometry(outline),Stroke=color??Brushes.White,StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};
            if(Accessibility.HighContrast)stroke.Loaded+=(_,_)=>Accessibility.FollowContentForeground(stroke,Shape.StrokeProperty);
            canvas.Children.Add(stroke);
            return new Viewbox{Width=size,Height=size,Child=canvas,IsHitTestVisible=false};
        }
        var path=new Path{Data=IconGeometry(Paths.TryGetValue(name,out var data)?data:Paths["arrow"]),Fill=color??Brushes.White,Stretch=Stretch.Uniform,Width=size,Height=size};
        if(Accessibility.HighContrast)path.Loaded+=(_,_)=>Accessibility.FollowContentForeground(path,Shape.FillProperty);
        return path;
    }
    public static Button Button(string icon,string name,System.Action action,bool primary=false,double size=42)
    {
        var button=UI.CaptionButton(name,action,primary);button.Content=Create(icon,20,primary?Brushes.Black:Brushes.White);button.Width=size;button.Height=size;button.MinHeight=size;button.Tag=new CornerRadius(size/2);button.Padding=new(System.Math.Max(4,(size-20)/2));button.ToolTip=Localization.Text(name);return button;
    }
}
