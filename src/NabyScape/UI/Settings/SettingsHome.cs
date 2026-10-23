using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private void SettingsHome()
    {
        HomeFeatureStatuses();
        var intro=UI.Caption("What would you like to change?",20);intro.FontWeight=FontWeights.SemiBold;intro.Margin=new(0,6,0,20);page.Children.Add(intro);
        void Link(string title,string detail,string icon,string target)
        {
            var button=UI.CaptionButton(title,()=>Navigate(target));button.Padding=new(18,16,18,16);button.Margin=new(0,0,0,10);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Background=UI.Brush("#1B1E24");
            var content=new Grid();content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});content.ColumnDefinitions.Add(new());content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var glyph=Icons.Create(icon,22,UI.Muted);glyph.Margin=new(0,0,16,0);content.Children.Add(glyph);var text=new StackPanel();text.Children.Add(UI.Caption(title,15));var hint=UI.Caption(detail,12,UI.Muted);hint.Margin=new(0,5,0,0);text.Children.Add(hint);Grid.SetColumn(text,1);content.Children.Add(text);var arrow=Icons.Create("next",14,UI.Muted);Grid.SetColumn(arrow,2);content.Children.Add(arrow);button.Content=content;page.Children.Add(button);
        }
        Link("Make the dock yours","Change its shape, colors and appearance.","settings","Appearance");
        Link("Move or resize the dock","Choose a screen, position and comfortable size.","screen","Layout");
        Link("Choose your widgets","Keep what you use, then drag to arrange it.","nav-launch","Content");
        Link("Set up Naby AI","Chat, talk, or connect your own Hermes agent.","chat","AI");
        Link("Manage the app","Startup, updates, language and saved preferences.","folder","Application");
        var tips=UI.Caption("Looking for something specific? Search above, or choose a category on the left. Most changes save automatically. AI connections have their own Save button.",12,UI.Muted);tips.Margin=new(0,16,0,0);page.Children.Add(tips);
    }
}
