using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace NabyScape;

// Official, unmodified Microsoft Fluent System Icons (20 Regular, MIT).
// The pinned SVG assets and upstream revision are retained alongside the code.
internal static class NewsIcons
{
    internal static FrameworkElement Create(string name,double size=18,Brush? ink=null)
    {
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Icons/Fluent/"+name+".svg")).Stream;
        var svg=XDocument.Load(stream);var canvas=new Canvas{Width=20,Height=20};
        foreach(var element in svg.Descendants().Where(e=>e.Name.LocalName=="path"))
        {
            var data=Geometry.Parse(element.Attribute("d")!.Value);
            if(element.Attribute("fill-rule")?.Value=="evenodd")data=Geometry.Parse("F0 "+element.Attribute("d")!.Value);
            var path=new System.Windows.Shapes.Path{Data=data,Fill=Accessibility.HighContrast?SystemColors.WindowTextBrush:ink??UI.Muted};
            if(Accessibility.HighContrast)path.Loaded+=(_,_)=>Accessibility.FollowContentForeground(path,System.Windows.Shapes.Shape.FillProperty);
            canvas.Children.Add(path);
        }
        return new Viewbox{Child=canvas,Width=size,Height=size,IsHitTestVisible=false};
    }
    internal static Button Action(string icon,string name,Action run)
    {
        var button=UI.CaptionButton(name,run);button.Content=Create(icon);
        button.Width=button.Height=button.MinHeight=30;button.Padding=new(5);button.Margin=new(0);button.Tag=new CornerRadius(7);
        button.Background=Brushes.Transparent;button.BorderThickness=new(0);return button;
    }
}
