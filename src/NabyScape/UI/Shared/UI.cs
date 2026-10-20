using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

internal static class UI
{
    private static readonly System.Collections.Generic.Dictionary<string,SolidColorBrush> brushes=new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Generic.Queue<string> brushOrder=new();
    public static SolidColorBrush Brush(string color)
    {
        lock(brushes){if(brushes.TryGetValue(color,out var cached))return cached;var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;brush.Freeze();if(brushes.Count>=128)brushes.Remove(brushOrder.Dequeue());brushes.Add(color,brush);brushOrder.Enqueue(color);return brush;}
    }
    internal static void ResourceBrush(string key,Brush brush)
    {if(Application.Current==null)return;var old=Application.Current.Resources[key];if(ReferenceEquals(old,brush) || old is SolidColorBrush a && brush is SolidColorBrush b && a.Color==b.Color)return;Application.Current.Resources[key]=brush;}
    public static Brush ContrastText(string color)
    {var c=Brush(color).Color;return c.R*.2126+c.G*.7152+c.B*.0722>150?Brush("#111317"):Brushes.White;}
    public static void ApplyControlTheme(Settings settings)
    {if(Application.Current==null)return;foreach(var entry in new[]{("ControlFill",settings.ControlFill),("ControlBorder",settings.ControlBorder),("SelectionFill",settings.SelectionFill)})ResourceBrush(entry.Item1,Brush(entry.Item2));ResourceBrush("ControlText",ContrastText(settings.ControlFill));ResourceBrush("SelectionText",ContrastText(settings.SelectionFill)); }
    public static readonly Brush Muted=Brush("#A6ABB4");
    public static TextBlock Text(string text,double size=13,Brush? color=null)
        =>new(){Text=text,FontSize=size,Foreground=color ?? Brush("#F2F3F5"),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    private static readonly object HeadingTag=new();
    public static TextBlock Heading(string text,double size=13,Brush? color=null)
    {var heading=Text(text,size,color);heading.Tag=HeadingTag;heading.FontWeight=App.Current is App app && app.Settings?.BoldHeadings==true?FontWeights.SemiBold:FontWeights.Normal;return heading;}
    internal static bool IsHeading(TextBlock text)=>ReferenceEquals(text.Tag,HeadingTag);
    public static TextBlock Label(string text)
    {var heading=Heading(text,11,Muted);heading.FontWeight=FontWeights.Medium;heading.Margin=new(0,0,0,10);return heading;}
    public static Button Button(string text,Action action,bool primary=false)
    {
        var b=new Button{Content=text,Tag=new CornerRadius(8)};AutomationProperties.SetName(b,text);
        if(primary) { b.SetResourceReference(Control.BackgroundProperty,"Accent");b.Foreground=Brushes.Black;b.FontWeight=FontWeights.SemiBold;b.BorderThickness=new(0); }
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
