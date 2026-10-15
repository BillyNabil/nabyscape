using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

internal static class UI
{
    public static SolidColorBrush Brush(string color)=>(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    public static readonly Brush Muted=Brush("#A1A1A6");
    public static TextBlock Text(string text,double size=13,Brush? color=null)
        =>new(){Text=text,FontSize=size,Foreground=color ?? Brushes.White,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    private static readonly object HeadingTag=new();
    public static TextBlock Heading(string text,double size=13,Brush? color=null)
    {var heading=Text(text,size,color);heading.Tag=HeadingTag;return heading;}
    internal static bool IsHeading(TextBlock text)=>ReferenceEquals(text.Tag,HeadingTag);
    public static TextBlock Label(string text)
    {var heading=Heading(text,11,Muted);heading.FontWeight=FontWeights.Medium;heading.Margin=new(0,0,0,10);return heading;}
    public static Button Button(string text,Action action,bool primary=false)
    {
        var b=new Button{Content=text,Tag=new CornerRadius(8)};AutomationProperties.SetName(b,text);
        if(primary) { b.SetResourceReference(Control.BackgroundProperty,"Accent");b.Foreground=Brushes.Black;b.BorderThickness=new(0); }
        Motion.Button(b);b.Click+=(_,_)=>action();return b;
    }
    public static StackPanel Row(params UIElement[] elements)
    { var row=new StackPanel{Orientation=Orientation.Horizontal};foreach(var e in elements) row.Children.Add(e);return row; }
    public static Border Card(string title,params UIElement[] content)
    {
        var panel=new StackPanel();panel.Children.Add(Label(title));foreach(var c in content) panel.Children.Add(c);
        return new Border{Background=Brush("#1C1C1E"),BorderThickness=new(0),CornerRadius=new(20),Padding=new(16),Margin=new(0,0,0,12),Child=panel};
    }
    public static TextBox Input(string name,string text,string? tooltip=null)
    { var t=new TextBox{Text=text,ToolTip=tooltip};AutomationProperties.SetName(t,name);return t; }
    public static void Named(DependencyObject control,string name)=>AutomationProperties.SetName(control,name);
    public static ScrollViewer Scroll(UIElement content)=>new(){Content=content,Padding=new(0)};
}
