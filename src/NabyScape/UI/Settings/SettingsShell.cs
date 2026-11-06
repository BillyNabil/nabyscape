using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NabyScape;

public sealed partial class SettingsWindow
{
    // Object tags: Settings diagnostics treat string-tagged borders as setting sections.
    private static readonly object StageControlsTag=new(),NavIndicatorTag=new();
    private const double SidebarWidth=252,MaxContentWidth=860,StageHeight=184;

    // Chrome Naby beside the stacked NABY / SCAPE wordmark used on the website.
    private FrameworkElement SidebarBrand()
    {
        var row=new Grid{Margin=new(22,48,16,20)};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());
        var naby=new Image{Width=42,Height=42,Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
        try{naby.Source=new BitmapImage(new Uri("pack://application:,,,/Assets/naby-chrome.png"));}catch(Exception ex){app.Store.Log(ex);}
        RenderOptions.SetBitmapScalingMode(naby,BitmapScalingMode.HighQuality);row.Children.Add(naby);
        var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(words,1);
        TextBlock Word(string text,Brush color){var t=new TextBlock{Text=text,FontFamily=SettingsTheme.Display,FontWeight=FontWeights.Bold,FontSize=17,Foreground=color,LineHeight=17,LineStackingStrategy=LineStackingStrategy.BlockLineHeight};return t;}
        words.Children.Add(Word("NABY",SettingsTheme.Bone));words.Children.Add(Word("SCAPE",SettingsTheme.Quiet));
        row.Children.Add(words);
        System.Windows.Automation.AutomationProperties.SetName(row,"NabyScape");
        return row;
    }
    private FrameworkElement SidebarFooter()
    {
        var footer=new StackPanel{Margin=new(0)};
        var border=new Border{BorderBrush=SettingsTheme.LineSoft,BorderThickness=new(0,1,0,0),Padding=new(22,10,18,10),Child=footer};
        if(showAllSwitch!=null){if(showAllSwitch.Parent is Panel old)old.Children.Remove(showAllSwitch);showAllSwitch.Margin=new(0);footer.Children.Add(showAllSwitch);}
        
        return border;
    }
    // The stage is a slice of your screen's top edge with the live dock hanging from it.
    private Border StageFrame(Border previewStage,StackPanel controls)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(250)});
        var screen=new Grid{ClipToBounds=true};
        var glow=new RadialGradientBrush{Center=new(.5,0),GradientOrigin=new(.5,0),RadiusX=.55,RadiusY=.9};
        glow.GradientStops.Add(new(Color.FromArgb(0x30,0xC7,0xFF,0x91),0));glow.GradientStops.Add(new(Color.FromArgb(0x0C,0xC7,0xFF,0x91),.45));glow.GradientStops.Add(new(Colors.Transparent,1));glow.Freeze();
        screen.Background=glow;
        var bezel=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,0)};
        bezel.GradientStops.Add(new(Color.FromRgb(0x10,0x14,0x11),0));bezel.GradientStops.Add(new(Color.FromRgb(0x9A,0xA3,0x94),.5));bezel.GradientStops.Add(new(Color.FromRgb(0x10,0x14,0x11),1));bezel.Freeze();
        screen.Children.Add(new Border{Height=2,VerticalAlignment=VerticalAlignment.Top,Background=bezel,IsHitTestVisible=false});
        previewStage.VerticalAlignment=VerticalAlignment.Top;screen.Children.Add(previewStage);
        grid.Children.Add(screen);
        var side=new Border{BorderBrush=SettingsTheme.LineSoft,BorderThickness=new(1,0,0,0),Child=controls,Tag=StageControlsTag};Grid.SetColumn(side,1);grid.Children.Add(side);
        var frame=new Border{Background=Brush("#0A0E0B"),BorderBrush=SettingsTheme.Line,BorderThickness=new(.8),CornerRadius=new(16),Child=grid};
        grid.SizeChanged+=(_,_)=>grid.Clip=new RectangleGeometry(new Rect(grid.RenderSize),15,15);
        return frame;
    }
    private static void StageCompact(Border stage,bool narrow)
    {
        if(stage.Child is not Grid grid)return;
        var side=grid.Children.OfType<Border>().FirstOrDefault(b=>ReferenceEquals(b.Tag,StageControlsTag));if(side==null)return;
        side.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;grid.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(250);
    }
    private static Brush Brush(string hex)=>UI.Brush(hex);
    private static string CategoryEyebrow(string id)=>Localization.Text(id=="Home"?"Welcome":Category(id).Group).ToUpperInvariant();
    private void StyleNavigationSelection(string selected)
    {
        foreach(var entry in navigationButtons)
        {
            bool on=entry.Key==selected;
            entry.Value.FontWeight=on?FontWeights.SemiBold:FontWeights.Normal;
            entry.Value.Background=on?SettingsTheme.LimeWash:Brushes.Transparent;
            entry.Value.Foreground=on?SettingsTheme.Bone:SettingsTheme.Quiet;
            if(entry.Value.Content is Grid row)foreach(var marker in row.Children.OfType<Border>().Where(b=>ReferenceEquals(b.Tag,NavIndicatorTag)))marker.Visibility=on?Visibility.Visible:Visibility.Hidden;
        }
    }
}
