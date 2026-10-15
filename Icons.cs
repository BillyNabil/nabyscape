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
        ["calendar"]="M8,2 V6 M16,2 V6 M3,10 H21 M5,4 H19 Q21,4 21,6 V20 Q21,22 19,22 H5 Q3,22 3,20 V6 Q3,4 5,4 Z",
        ["nav-media"]="M12,3 A9,9 0 1 1 11.99,3 M10,8 L16,12 10,16 Z",
        ["nav-focus"]="M8,3 H5 Q3,3 3,5 V8 M16,3 H19 Q21,3 21,5 V8 M3,16 V19 Q3,21 5,21 H8 M16,21 H19 Q21,21 21,19 V16 M12,8 A4,4 0 1 1 11.99,8",
        ["nav-status"]="M5,4 H19 Q21,4 21,6 V15 Q21,17 19,17 H5 Q3,17 3,15 V6 Q3,4 5,4 Z M8,21 H16 M12,17 V21 M7,12 L10,9 13,12 17,8",
        ["nav-launch"]="M 4.0 3.0 H 9.0 Q 10.0 3.0 10.0 4.0 V 9.0 Q 10.0 10.0 9.0 10.0 H 4.0 Q 3.0 10.0 3.0 9.0 V 4.0 Q 3.0 3.0 4.0 3.0 Z M 15.0 3.0 H 20.0 Q 21.0 3.0 21.0 4.0 V 9.0 Q 21.0 10.0 20.0 10.0 H 15.0 Q 14.0 10.0 14.0 9.0 V 4.0 Q 14.0 3.0 15.0 3.0 Z M 15.0 14.0 H 20.0 Q 21.0 14.0 21.0 15.0 V 20.0 Q 21.0 21.0 20.0 21.0 H 15.0 Q 14.0 21.0 14.0 20.0 V 15.0 Q 14.0 14.0 15.0 14.0 Z M 4.0 14.0 H 9.0 Q 10.0 14.0 10.0 15.0 V 20.0 Q 10.0 21.0 9.0 21.0 H 4.0 Q 3.0 21.0 3.0 20.0 V 15.0 Q 3.0 14.0 4.0 14.0 Z",
        ["nav-tools"]="M5,7 H19 Q21,7 21,9 V18 Q21,20 19,20 H5 Q3,20 3,18 V9 Q3,7 5,7 Z M8,7 V5 Q8,3 10,3 H14 Q16,3 16,5 V7 M3,12 Q12,17 21,12 M12,13 V16",
        ["nav-settings"]="M 14 17 H 5 M 19 7 h -9 M 20.0 17.0 A 3.0 3.0 0 1 1 14.0 17.0 A 3.0 3.0 0 1 1 20.0 17.0 M 10.0 7.0 A 3.0 3.0 0 1 1 4.0 7.0 A 3.0 3.0 0 1 1 10.0 7.0",
        ["mixer"]="M 10 5 H 3 M 12 19 H 3 M 14 3 v 4 M 16 17 v 4 M 21 12 h -9 M 21 19 h -5 M 21 5 h -7 M 8 10 v 4 M 8 12 H 3",
        ["settings"]="M 14 17 H 5 M 19 7 h -9 M 20.0 17.0 A 3.0 3.0 0 1 1 14.0 17.0 A 3.0 3.0 0 1 1 20.0 17.0 M 10.0 7.0 A 3.0 3.0 0 1 1 4.0 7.0 A 3.0 3.0 0 1 1 10.0 7.0"
    };
    public static FrameworkElement Create(string name,double size=20,Brush? color=null)
    {
        if(NavigationPaths.TryGetValue(name,out var outline))
        {
            var canvas=new Canvas{Width=24,Height=24};
            canvas.Children.Add(new Path{Data=Geometry.Parse(outline),Stroke=color??Brushes.White,StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});
            return new Viewbox{Width=size,Height=size,Child=canvas,IsHitTestVisible=false};
        }
        var path=new Path{Data=Geometry.Parse(Paths.TryGetValue(name,out var data)?data:Paths["arrow"]),Fill=color??Brushes.White,Stretch=Stretch.Uniform,Width=size,Height=size};
        return path;
    }
    public static Button Button(string icon,string name,System.Action action,bool primary=false,double size=42)
    {
        var button=UI.Button(name,action,primary);button.Content=Create(icon,20,primary?Brushes.Black:Brushes.White);button.Width=size;button.Height=size;button.MinHeight=size;button.Tag=new CornerRadius(size/2);button.Padding=new(System.Math.Max(4,(size-20)/2));button.ToolTip=name;return button;
    }
}
