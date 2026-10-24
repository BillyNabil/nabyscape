using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private void WireSettingPreview(FrameworkElement control,string name)
    {
        void Show(){ConfigurePreviewForSetting(name);RefreshPreview();}
        control.GotKeyboardFocus+=(_,_)=>Show();control.MouseEnter+=(_,_)=>Show();
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> SettingTree(DependencyObject node)
    {foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()){yield return child;foreach(var item in SettingTree(child))yield return item;}}
    private void OrganizeSettings()
    {
        // The page title and description live above the form, outside its scroll area.
        var sections=page.Children.OfType<Border>().Where(b=>b.Tag is string).ToArray();
        sectionIndexHost.Child=null;sectionIndexHost.Visibility=Visibility.Collapsed;
        foreach(var box in SettingTree(page).OfType<ComboBox>())
        {
            string name=UI.Identity(box);if(name.Length==0)continue;
            WireSettingPreview(box,name);string help=SettingDescription(name);if(help.Length>0)box.ToolTip=help;
        }
        foreach(var section in sections)
        {
            if(section.Child is not StackPanel content || content.Children.Count==0)continue;
            var title=(FrameworkElement)content.Children[0];content.Children.RemoveAt(0);
            var head=new DockPanel{Margin=new(0,0,0,12)};
            title.Margin=new(0);head.Children.Add(title);content.Children.Insert(0,head);
        }
        // Every settings section remains directly visible; the sidebar supplies navigation.
    }

    private void ConfigurePreviewForSection(string title)
    {
        previewFocus.Text=Localization.Text(title);
        (previewMode,previewContent)=title switch
        {
            "Enabled widgets & order" or "Text and navigation" =>("Panel","Navigation"),
            "Hover pages" or "Hover & swipe" or "Hover preview" =>("Hover","Music"),
            "AI companion" =>("Panel","AI"),
            "Individual tools" =>("Panel","Tools"),
            "Clipboard history" =>("Panel","Smart clipboard"),
            "Focus sessions" =>("Panel","Focus"),
            "Stopwatch" =>("Panel","Stopwatch"),
            "Automatic tucking" or "Hide or show the island" =>("Panel","Visibility"),
            "Drag to Island" =>("Hover","Drop zone"),
            "Detachable previews" =>("Hover","Detached preview"),
            "Closed cover gestures" =>("Hover","Cover gestures"),
            "Launch display" or "Shortcuts" =>("Panel","Launch"),
            "Clock" or "Monitor details" =>("Panel","System"),
            "Controls & outlines" =>("Panel","Control colors"),
            "Typography" =>("Panel","Typography"),
            "Panel navigation" =>("Panel","Panel navigation"),
            "Live signal" or "Waveform source" =>("Panel","Music"),
            "Live feedback" =>("Closed","Volume feedback"),
            "Open panel" =>("Panel","Music"),
            _ =>("Closed","Music")
        };
    }
    private FrameworkElement VisibilityExample()
    {
        var panel=new StackPanel();var scene=new Grid{Height=108,ClipToBounds=true,Background=UI.Brush("#24272D"),Margin=new(0,0,0,12)};
        var window=new Border{Margin=new(9,17,9,8),CornerRadius=new(5),Background=UI.Brush("#17191D"),Child=UI.Caption("Your maximized app",11,UI.Muted)};((TextBlock)window.Child).HorizontalAlignment=HorizontalAlignment.Center;scene.Children.Add(window);
        bool tuck=app.Settings.AlwaysTucked || app.Settings.HideFullscreen || app.Settings.HideMaximized;double offset=23-app.Settings.TuckEdge;
        var slide=new TranslateTransform(0,tuck?-offset:0);var island=new Border{Width=110,Height=23,CornerRadius=new(0,0,9,9),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,Background=Brushes.Black,RenderTransform=slide};
        var time=UI.Text(DateTime.Now.ToString("HH:mm"),10);time.FontWeight=FontWeights.Bold;time.HorizontalAlignment=HorizontalAlignment.Center;island.Child=time;scene.Children.Add(island);
        scene.MouseEnter+=(_,_)=>{slide.BeginAnimation(TranslateTransform.YProperty,null);slide.Y=0;};scene.MouseLeave+=(_,_)=>slide.Y=tuck?-offset:0;
        panel.Children.Add(scene);panel.Children.Add(UI.Text(tuck?"Hover to reveal · "+app.Settings.TuckEdge.ToString("0")+" DIP tucked edge. Manual hide removes it entirely.":"Auto-tuck is off. The island stays visible.",12));
        panel.Children.Add(UI.Text("Fullscreen: "+(app.Settings.HideFullscreen?"tuck":"visible")+" · Maximized: "+(app.Settings.HideMaximized?"tuck":"visible"),11,UI.Muted));return panel;
    }
}
