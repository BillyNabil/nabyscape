using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private static Grid SettingCaption(string title,SettingIllustration image)=>SettingCaption(UI.Text(title,12),image);
    private static Grid SettingCaption(UIElement label,SettingIllustration image)
    {
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.Children.Add(image);Grid.SetColumn(label,1);row.Children.Add(label);return row;
    }
    private void WireSettingPreview(FrameworkElement control,string name)
    {
        void Show(){ConfigurePreviewForSetting(name);RefreshPreview();}
        control.GotKeyboardFocus+=(_,_)=>Show();control.MouseEnter+=(_,_)=>Show();
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> SettingTree(DependencyObject node)
    {foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()){yield return child;foreach(var item in SettingTree(child))yield return item;}}
    private void OrganizeSettings()
    {
        var descriptions=new System.Collections.Generic.Dictionary<string,string>
        {
            ["Appearance"]="Shape, color, typography and motion. Each example reflects the option beside it.",
            ["Layout"]="Adjust the closed island, hover preview and open panel separately.",
            ["Widgets"]="Choose what appears, arrange it, and add the apps you use most.",
            ["Controls"]="Set up hover, gestures, keyboard shortcuts and everyday tools.",
            ["Audio"]="Choose how playback, output devices and the waveform behave.",
            ["System"]="Select the information shown in Status. Readings stay on this PC.",
            ["Application"]="Startup, automatic tucking and local storage. Your data stays on this device."
        };
        var intro=UI.Text(descriptions[current],12,UI.Muted);intro.Margin=new(0,-14,0,18);page.Children.Insert(1,intro);
        var sections=page.Children.OfType<Border>().Where(b=>b.Tag is string).ToArray();
        var links=new ComboBox{Margin=new(0,0,0,24),HorizontalAlignment=HorizontalAlignment.Stretch};UI.Named(links,"Settings section index");
        links.Items.Add(new ComboBoxItem{Content="Jump to a section…",IsEnabled=false});links.SelectedIndex=0;
        foreach(var section in sections)
        {
            links.Items.Add(new ComboBoxItem{Content=(string)section.Tag,Tag=section});
        }
        page.Children.Insert(2,links);
        links.SelectionChanged+=(_,_)=>{if(links.SelectedItem is ComboBoxItem {Tag:Border section})section.BringIntoView();};
        // Selectors get their own visual as well; numeric editors already belong to a slider.
        foreach(var box in SettingTree(page).OfType<ComboBox>().ToArray())
        {
            if(ReferenceEquals(box,links))continue;
            string name=AutomationProperties.GetName(box);if(name.Length==0)continue;
            WireSettingPreview(box,name);
            if(box.Parent is not Panel owner)continue;
            if(owner is Grid existing && existing.ColumnDefinitions.Count>1)
            {var selectorHelp=new StackPanel();selectorHelp.Children.Add(new SettingIllustration(name));selectorHelp.Children.Add(UI.Text(SettingDescription(name),11));box.ToolTip=selectorHelp;continue;}
            int index=owner.Children.IndexOf(box),column=Grid.GetColumn(box),row=Grid.GetRow(box),span=Grid.GetColumnSpan(box);
            owner.Children.RemoveAt(index);var image=new SettingIllustration(name){Margin=new(0,0,12,0)};var group=SettingCaption(box,image);Grid.SetColumn(group,column);Grid.SetRow(group,row);Grid.SetColumnSpan(group,span);Grid.SetColumn(box,1);Grid.SetRow(box,0);Grid.SetColumnSpan(box,1);owner.Children.Insert(index,group);
            void Update(){image.Value=box.Items.Count>1?(double)box.SelectedIndex/(box.Items.Count-1):.5;image.InvalidateVisual();}Update();box.SelectionChanged+=(_,_)=>Update();
            if(SettingDescription(name) is string help && help.Length>0)box.ToolTip=help;
        }
        // Every section also has a distinct, keyboard-accessible route to the full preview.
        foreach(var section in sections)
        {
            if(section.Child is not StackPanel content || content.Children.Count==0)continue;
            var title=(FrameworkElement)content.Children[0];content.Children.RemoveAt(0);
            var head=new DockPanel{Margin=new(0,0,0,8)};
            var previewButton=UI.Button("Preview "+section.Tag,()=>{ConfigurePreviewForSetting((string)section.Tag);RefreshPreview();});
            previewButton.Content=Icons.Create("nav-media",14,UI.Muted);previewButton.ToolTip="Show example: "+section.Tag;previewButton.Width=30;previewButton.MinHeight=28;previewButton.Padding=new(0);previewButton.Margin=new(8,0,0,0);previewButton.Background=Brushes.Transparent;DockPanel.SetDock(previewButton,Dock.Right);head.Children.Add(previewButton);title.Margin=new(0);head.Children.Add(title);content.Children.Insert(0,head);
        }
    }
    private FrameworkElement VisibilityExample()
    {
        var panel=new StackPanel();var scene=new Grid{Height=108,ClipToBounds=true,Background=UI.Brush("#24272D"),Margin=new(0,0,0,12)};
        var window=new Border{Margin=new(9,17,9,8),CornerRadius=new(5),Background=UI.Brush("#17191D"),Child=UI.Text("Your maximized app",11,UI.Muted)};((TextBlock)window.Child).HorizontalAlignment=HorizontalAlignment.Center;scene.Children.Add(window);
        bool tuck=app.Settings.HideFullscreen || app.Settings.HideMaximized;
        var slide=new TranslateTransform(0,tuck?-15:0);var island=new Border{Width=110,Height=23,CornerRadius=new(0,0,9,9),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,Background=Brushes.Black,RenderTransform=slide};
        var time=UI.Text(DateTime.Now.ToString("HH:mm"),10);time.FontWeight=FontWeights.Bold;time.HorizontalAlignment=HorizontalAlignment.Center;island.Child=time;scene.Children.Add(island);
        scene.MouseEnter+=(_,_)=>{slide.BeginAnimation(TranslateTransform.YProperty,null);slide.Y=0;};scene.MouseLeave+=(_,_)=>slide.Y=tuck?-15:0;
        panel.Children.Add(scene);panel.Children.Add(UI.Text(tuck?"Hover the example to reveal the tucked island.":"Auto-tuck is off. The island stays visible.",12));
        panel.Children.Add(UI.Text("Fullscreen: "+(app.Settings.HideFullscreen?"tuck":"visible")+" · Maximized: "+(app.Settings.HideMaximized?"tuck":"visible"),11,UI.Muted));return panel;
    }
}
