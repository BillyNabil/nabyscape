using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private void SettingsHome()
    {
        page.Children.Add(HomeHero());
        var intro=UI.Caption("What would you like to do?",15);intro.FontFamily=SettingsTheme.Display;intro.FontWeight=FontWeights.Bold;intro.Margin=new(0,6,0,12);page.Children.Add(intro);
        // Plain tasks instead of category names: each tile opens the page that does it.
        var tiles=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new(-5,0,-5,0)};UI.LocalizedName(tiles,"Common tasks");
        void Tile(string title,string detail,string icon,string target)
        {
            var button=UI.CaptionButton(title,()=>Navigate(target));button.Padding=new(16,14,16,14);button.Margin=new(5,0,5,10);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Top;button.Background=SettingsTheme.Card;button.BorderBrush=SettingsTheme.LineSoft;button.BorderThickness=new(1);button.Tag=new CornerRadius(14);
            var content=new Grid();content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});content.ColumnDefinitions.Add(new());
            var glyphBox=new Border{Width=36,Height=36,CornerRadius=new(11),Background=SettingsTheme.LimeWash,Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Top,Child=Icons.Create(icon,17,SettingsTheme.Lime)};content.Children.Add(glyphBox);
            var text=new StackPanel();var name=UI.Caption(title,14);name.FontFamily=SettingsTheme.Display;name.FontWeight=FontWeights.Bold;text.Children.Add(name);var hint=UI.Caption(detail,12,UI.Muted);hint.Margin=new(0,3,0,0);text.Children.Add(hint);Grid.SetColumn(text,1);content.Children.Add(text);
            button.Content=content;tiles.Children.Add(button);
        }
        Tile("Change the look","Shape, colors and glow.","image","Appearance");
        Tile("Move or resize","Screen, edge and size.","screen","Layout");
        Tile("Auto-hide the dock","Stay out of the way of apps.","eye","Visibility");
        Tile("Pick your widgets","Show only what you use.","nav-tools","Content");
        Tile("Music & lyrics","What plays on the dock.","nav-media","Audio");
        Tile("Notifications","Alerts and quiet hours.","bell","Notifications");
        Tile("Talk to Naby AI","Chat or voice assistant.","chat","AI");
        Tile("App & updates","Startup, language, updates.","nav-settings","Application");
        page.Children.Add(tiles);
        // Readiness of each feature follows the tasks, so newcomers see what to do first.
        HomeFeatureStatuses();
        tiles.SizeChanged+=(_,_)=>tiles.Columns=tiles.ActualWidth<480?1:2;
    }
    // The welcome card is the page header for Start here: one sentence, one primary action, Naby.
    private Border HomeHero()
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        var eyebrow=UI.Caption("Welcome to NabyScape",11);eyebrow.Text=eyebrow.Text.ToUpperInvariant();eyebrow.FontFamily=SettingsTheme.Display;eyebrow.FontWeight=FontWeights.Bold;eyebrow.Foreground=SettingsTheme.Lime;text.Children.Add(eyebrow);
        var title=UI.Caption("Make the dock yours.",30);title.FontFamily=SettingsTheme.Display;title.FontWeight=FontWeights.Bold;title.Foreground=SettingsTheme.Bone;title.Margin=new(0,8,0,8);text.Children.Add(title);
        var body=UI.Caption("Pick something to change below, or let quick setup choose good defaults for you. Every change saves by itself.",13);body.Foreground=SettingsTheme.Quiet;body.MaxWidth=420;body.HorizontalAlignment=HorizontalAlignment.Left;text.Children.Add(body);
        var setup=UI.CaptionButton("Run quick setup",app.ShowWelcome,true);setup.Padding=new(18,9,18,9);setup.Tag=new CornerRadius(18);
        var search=UI.CaptionButton("Search settings",()=>{settingsSearch?.Focus();});search.Padding=new(16,9,16,9);search.Tag=new CornerRadius(18);search.Background=Brushes.Transparent;search.BorderBrush=SettingsTheme.Line;search.BorderThickness=new(1);
        var actions=UI.Row(setup,search);actions.Margin=new(0,18,0,0);text.Children.Add(actions);
        grid.Children.Add(text);
        var art=new Grid{Width=150,Height=150,Margin=new(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        var halo=new RadialGradientBrush();halo.GradientStops.Add(new(Color.FromArgb(0x55,0xC7,0xFF,0x91),0));halo.GradientStops.Add(new(Color.FromArgb(0x10,0xC7,0xFF,0x91),.55));halo.GradientStops.Add(new(Colors.Transparent,1));halo.Freeze();
        art.Children.Add(new System.Windows.Shapes.Ellipse{Fill=halo,Margin=new(-24)});
        var naby=new Image{Width=128,Height=128};try{naby.Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/naby-chrome.png"));}catch(Exception ex){app.Store.Log(ex);}
        RenderOptions.SetBitmapScalingMode(naby,BitmapScalingMode.HighQuality);art.Children.Add(naby);Grid.SetColumn(art,1);grid.Children.Add(art);
        var card=new Border{Background=SettingsTheme.Card,BorderBrush=SettingsTheme.Line,BorderThickness=new(1),CornerRadius=new(18),Padding=new(30,26,26,26),Margin=new(0,0,0,26),Child=grid,ClipToBounds=true};
        grid.SizeChanged+=(_,_)=>art.Visibility=grid.ActualWidth<520?Visibility.Collapsed:Visibility.Visible;
        UI.LocalizedName(card,"Welcome");return card;
    }
}
