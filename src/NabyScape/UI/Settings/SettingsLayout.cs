using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
namespace NabyScape;

// Compact, beginner-friendly presentation: icon navigation, one-line section hints and
// "label left, control right" rows that stack again when the window is narrow.
public sealed partial class SettingsWindow
{
    private static string CategoryIcon(string id)=>id switch
    {
        "Home"=>"nav-launch","Appearance"=>"image","Ambience"=>"sun","Motion"=>"nav-focus","Layout"=>"screen","Visibility"=>"eye",
        "Controls"=>"mouse","Content"=>"nav-tools","Audio"=>"nav-media","Clipboard"=>"clipboard","Weather"=>"weather","System"=>"nav-status",
        "Launch"=>"link","AI"=>"chat","Notifications"=>"bell",_=>"nav-settings"
    };
    // Plain-language purpose of each section, shown under its title.
    private static readonly Dictionary<string,string> SectionHints=new()
    {
        ["Shape"]="Pick how the dock looks at the top of your screen.",
        ["Color & material"]="Choose the dock and highlight colors.",
        ["Ambient light"]="A soft glow around the dock that can follow your music.",
        ["Motion"]="How lively animations feel.",
        ["Display"]="Which screen shows the dock.",
        ["Dock placement"]="Which screen edge the dock sits on.",
        ["Move & resize"]="Drag the preview or type an exact position.",
        ["Closed island"]="Size of the dock when it is closed.",
        ["Automatic tucking"]="Slide the dock away so it doesn't cover your apps.",
        ["Hide or show the island"]="A shortcut to hide the dock completely.",
        ["Enabled widgets & order"]="Turn widgets on or off and drag to reorder them.",
        ["Focus sessions"]="Default lengths for focus and break timers.",
        ["Clipboard history"]="Remember things you copy so you can paste them again.",
        ["Closed media display"]="What the closed dock shows while music plays.",
        ["Lyrics"]="Synced song lyrics from online sources.",
        ["Notifications"]="Which alerts you get and where they appear.",
        ["Other apps"]="Show alerts from Claude, Telegram and your other apps too.",
        ["Language & accessibility"]="Language, text size and contrast.",
        ["Behavior"]="Starting NabyScape with Windows.",
        ["Updates"]="Get new versions of NabyScape.",
        ["Local storage"]="Where your data lives, quitting and uninstalling.",
        ["About NabyScape"]="Version information.",
        ["Notch gestures"]="What the mouse wheel and middle click do on the dock.",
        ["Global hotkey"]="A keyboard shortcut that opens the dock.",
        ["Clock"]="Time and date format.",
        ["Shortcuts"]="Apps, folders and websites you open often."
    };
    private static FrameworkElement NavigationContent(string icon,string title)
    {
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());
        // A lime rail marks the open page.
        row.Children.Add(new Border{Tag=NavIndicatorTag,Width=3,Height=16,CornerRadius=new(1.5),Background=SettingsTheme.Lime,Margin=new(-8,0,9,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Hidden});
        var glyph=Icons.Create(icon,16);glyph.Margin=new(0,0,11,0);glyph.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(glyph,1);
        var shapes=glyph is Viewbox {Child:Canvas canvas}?canvas.Children.OfType<Shape>():glyph is Shape single?new[]{single}:Enumerable.Empty<Shape>();
        foreach(var shape in shapes)
            BindingOperations.SetBinding(shape,shape.Stroke!=null?Shape.StrokeProperty:Shape.FillProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1)});
        row.Children.Add(glyph);
        var text=UI.Text(title,13);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.VerticalAlignment=VerticalAlignment.Center;text.ClearValue(TextBlock.ForegroundProperty);Grid.SetColumn(text,2);row.Children.Add(text);
        return row;
    }
    private static void AddSectionHint(StackPanel content,string title)
    {
        if(!SectionHints.TryGetValue(title,out var hint))return;
        var text=UI.Caption(hint,12,UI.Muted);text.Margin=new(0,-9,0,12);text.Tag="section-hint";
        content.Children.Insert(1,text);
    }
    // Responsive "label | control" row: side by side when there is room, stacked otherwise.
    internal static Grid SettingRow(FrameworkElement label,FrameworkElement control,double controlWidth)
    {
        var row=new Grid{Margin=new(0,5,0,5)};
        row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        row.RowDefinitions.Add(new(){Height=GridLength.Auto});row.RowDefinitions.Add(new(){Height=GridLength.Auto});
        label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new(0,0,12,0);row.Children.Add(label);
        control.Margin=new(0);control.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(control);
        bool? wide=null;
        void Layout()
        {
            bool fits=row.ActualWidth<=0 || row.ActualWidth>=controlWidth+150;
            if(wide==fits)return;wide=fits;
            Grid.SetRow(control,fits?0:1);Grid.SetColumn(control,fits?1:0);Grid.SetColumnSpan(control,fits?1:2);
            control.Width=fits?controlWidth:double.NaN;control.Margin=fits?new Thickness(0):new Thickness(0,7,0,0);
        }
        Layout();row.SizeChanged+=(_,_)=>Layout();
        return row;
    }
    private void CompactSettingRows(Border section)
    {
        foreach(var box in SettingTree(section).OfType<ComboBox>().ToArray())
        {
            if(box.Parent is not StackPanel {Orientation:Orientation.Vertical} parent || !double.IsNaN(box.Width))continue;
            int at=parent.Children.IndexOf(box);if(at<0)continue;
            FrameworkElement? label=null;
            if(at>0 && parent.Children[at-1] is TextBlock caption && ReferenceEquals(caption.Foreground,UI.Muted) && caption.Text.Length is >0 and <=40 && (caption.Tag is null || UI.IsHeading(caption)))
            {label=caption;parent.Children.RemoveAt(at-1);at--;}
            label??=UI.Text(Localization.Text(UI.Identity(box)),12);
            if(label is TextBlock text){text.FontSize=12;text.Foreground=UI.Brush("#F2F3F5");}
            parent.Children.RemoveAt(at);
            parent.Children.Insert(at,SettingRow(label,box,220));
        }
    }
}
