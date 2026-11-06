using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace NabyScape;

// Settings opens with the essentials of each page. "Show all settings" reveals every section
// and the detailed help text; search can still open any setting directly.
public sealed partial class SettingsWindow
{
    private static readonly Dictionary<string,string[]> EssentialSections=new()
    {
        ["Appearance"]=new[]{"Shape","Color & material"},
        ["Motion"]=new[]{"Motion"},
        ["Layout"]=new[]{"Display","Dock placement","Move & resize","Closed island"},
        ["Visibility"]=new[]{"Automatic tucking","Hide or show the island"},
        ["Controls"]=new[]{"Notch gestures","Global hotkey"},
        ["Content"]=new[]{"Enabled widgets & order","Focus sessions","Clipboard history"},
        ["Audio"]=new[]{"Closed media display","Lyrics"},
        ["Notifications"]=new[]{"Notifications","Other apps"},
        ["Application"]=new[]{"Language & accessibility","Behavior","Updates","Local storage","About NabyScape"},
        ["System"]=new[]{"Clock"},
        ["Launch"]=new[]{"Shortcuts"}
    };
    // Detailed pages that only appear in the sidebar after "Show all settings".
    private static readonly HashSet<string> DetailedCategories=new(){"Motion","Controls","Clipboard","System"};
    private readonly Dictionary<string,TextBlock> navigationGroups=new();
    private CheckBox? showAllSwitch;
    private bool revealAllSettings,revealAllNext;
    private bool ShowAllSettings=>app.Settings.SettingsShowAll || revealAllSettings;
    internal static bool EssentialSetting(string category,string section)=>!DetailedCategories.Contains(category) && (section.Length==0 || !EssentialSections.TryGetValue(category,out var keep) || keep.Contains(section));
    private static string EssentialParent(string id)=>id switch{"Motion"=>"Appearance","Controls"=>"Visibility","Clipboard"=>"Content","System"=>"Content",_=>id};

    private void BuildNavigation(StackPanel navigation)
    {
        string lastGroup="";
        foreach(var category in SettingsCategories)
        {
            if(category.Group!=lastGroup)
            {
                lastGroup=category.Group;
                // "Start here" stands alone at the top; a "Settings" label above it repeats the window title.
                if(category.Group!="Settings"){var label=UI.Caption(category.Group,10,UI.Muted);label.Margin=new(14,10,0,4);label.FontFamily=SettingsTheme.Display;label.FontWeight=FontWeights.Bold;label.Text=label.Text.ToUpperInvariant();label.Foreground=SettingsTheme.Quiet;label.Opacity=.75;navigation.Children.Add(label);navigationGroups[category.Group]=label;}
            }
            var item=category;var button=UI.CaptionButton(item.Title,()=>Navigate(item.Id));button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Content=NavigationContent(CategoryIcon(item.Id),Localization.Text(item.Title));button.Padding=new(12,6,12,6);button.Margin=new(0,1,0,1);button.Tag=new CornerRadius(9);button.FontSize=13;button.MinHeight=32;button.ToolTip=Localization.Text(item.Description);navigationButtons[item.Id]=button;navigation.Children.Add(button);
        }
        var label2=new StackPanel();label2.Children.Add(UI.Caption("Show all settings",12));var hint=UI.Caption("Every option and its help text",10,UI.Muted);hint.Margin=new(0,3,0,0);label2.Children.Add(hint);
        // Lives in the sidebar footer (see SidebarFooter).
        showAllSwitch=new CheckBox{Content=label2,IsChecked=app.Settings.SettingsShowAll};UI.LocalizedName(showAllSwitch,"Show all settings");
        showAllSwitch.Click+=(_,_)=>SetShowAllSettings(showAllSwitch.IsChecked==true);
        RefreshNavigationMode();
    }
    private void RefreshNavigationMode()
    {
        bool all=app.Settings.SettingsShowAll;
        foreach(var category in SettingsCategories)if(navigationButtons.TryGetValue(category.Id,out var button))button.Visibility=all || !DetailedCategories.Contains(category.Id)?Visibility.Visible:Visibility.Collapsed;
        foreach(var group in navigationGroups)group.Value.Visibility=SettingsCategories.Any(c=>c.Group==group.Key && navigationButtons.TryGetValue(c.Id,out var b) && b.Visibility==Visibility.Visible)?Visibility.Visible:Visibility.Collapsed;
        if(showAllSwitch!=null && showAllSwitch.IsChecked!=all)showAllSwitch.IsChecked=all;
        if(categorySelector!=null)
        {
            var titles=SettingsCategories.Where(c=>all || !DetailedCategories.Contains(c.Id) || c.Id==current).Select(c=>Localization.Text(c.Title)).ToArray();
            if(!(categorySelector.ItemsSource is string[] old && old.SequenceEqual(titles))){categorySelector.ItemsSource=titles;categorySelector.SelectedItem=CategoryTitle(current);}
        }
    }
    private void SetShowAllSettings(bool value)
    {
        revealAllSettings=false;
        if(app.Settings.SettingsShowAll!=value){app.Settings.SettingsShowAll=value;Save();}
        RefreshNavigationMode();
        Navigate(value?current:EssentialParent(current));
    }
    private void ApplyEssentialsFilter()
    {
        if(!EssentialSections.TryGetValue(current,out var keep) || DetailedCategories.Contains(current) && !ShowAllSettings)return;
        var sections=page.Children.OfType<Border>().Where(b=>b.Tag is string).ToArray();
        int detailed=sections.Count(s=>!keep.Contains((string)s.Tag));
        if(!ShowAllSettings)
        {
            foreach(var section in sections.Where(s=>!keep.Contains((string)s.Tag)))page.Children.Remove(section);
            // Free-standing notes between sections explain detailed options only.
            foreach(var note in page.Children.OfType<TextBlock>().ToArray())page.Children.Remove(note);
            var retained=page.Children.OfType<Border>().Select(b=>b.Tag as string).ToHashSet();settingExamples.RemoveAll(scene=>!retained.Contains(scene.Section));
            foreach(var section in page.Children.OfType<Border>().Where(b=>b.Tag is string))PruneDetailedHelp(section);
        }
        if(detailed==0 || revealAllSettings)return;
        var card=DisclosureCard(ShowAllSettings,detailed);
        int at=page.Children.Count;if(at>0 && page.Children[at-1] is Button)at--;
        page.Children.Insert(at,card);
    }
    private Border DisclosureCard(bool showingAll,int detailed)
    {
        var row=new DockPanel();
        var action=UI.CaptionButton(showingAll?"Show essentials only":"Show all settings",()=>SetShowAllSettings(!showingAll),!showingAll);action.VerticalAlignment=VerticalAlignment.Center;action.Margin=new(12,0,0,0);DockPanel.SetDock(action,Dock.Right);row.Children.Add(action);
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        text.Children.Add(UI.Caption(showingAll?"Showing all settings":"Showing the essentials",13));
        var detail=UI.Text(showingAll?Localization.Text("Detailed options and help text are visible on every page."):Localization.Format(detailed==1?"{0} more section with detailed options.":"{0} more sections with detailed options.",detailed),11,UI.Muted);detail.Margin=new(0,3,0,0);text.Children.Add(detail);
        row.Children.Add(text);
        var card=new Border{Background=Brushes.Transparent,BorderBrush=SettingsTheme.Line,BorderThickness=new(.7),CornerRadius=new(12),Padding=new(16,12,12,12),Margin=new(0,0,0,15),Child=row};
        UI.LocalizedName(card,"Settings detail level");return card;
    }
    // Essentials keep labels and controls; long explanations stay in tooltips and in "Show all settings".
    private static void PruneDetailedHelp(Border section)
    {
        foreach(var text in SettingTree(section).OfType<TextBlock>().ToArray())
        {
            if(!ReferenceEquals(text.Foreground,UI.Muted) || text.Text.Length==0)continue;
            bool inSwitch=false,inControl=false;
            for(var node=LogicalTreeHelper.GetParent(text);node!=null && node!=section;node=LogicalTreeHelper.GetParent(node))
            {
                if(node is CheckBox){inSwitch=true;break;}
                if(node is Control or ColorField){inControl=true;break;}
            }
            if(inControl || !inSwitch && text.Text.Length<48)continue;
            text.Visibility=Visibility.Collapsed;
        }
        foreach(var expander in SettingTree(section).OfType<Expander>().ToArray())
            if(expander.Header is string header && header.StartsWith(Localization.Text("More details"),StringComparison.Ordinal))expander.Visibility=Visibility.Collapsed;
    }
}
