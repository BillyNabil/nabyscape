using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NabyScape;

internal static class LauncherPresentation
{
    internal static void ApplyHeadingTypography(DependencyObject node,bool bold)
    {
        if(node is TextBlock text && UI.IsHeading(text))text.FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal;
        foreach(var child in LogicalTreeHelper.GetChildren(node))if(child is DependencyObject element)ApplyHeadingTypography(element,bold);
    }
    internal static void ApplyMediaTypography(TextBlock current,TextBlock? outgoing,bool bold)
    {current.FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal;if(outgoing!=null)outgoing.FontWeight=current.FontWeight;}
    internal static bool ShowIcon(string mode)=>mode!="Text";
    internal static bool ShowLabel(string mode)=>mode!="Icons";
    internal static string CategoryIcon(string page)=>page switch{"media"=>"nav-media","focus"=>"nav-focus","status"=>"nav-status","tools"=>"nav-tools",_=>"nav-launch"};
}
public sealed partial class OverlayWindow
{
    private Button LaunchButton(Shortcut shortcut)
    {
        var button=UI.Button(shortcut.Name,()=>{try{ShortcutService.Open(shortcut);Collapse();}catch(Exception ex){app.Notify("Could not open shortcut",ex.Message);}});
        button.ToolTip=shortcut.Name+"\n"+shortcut.Target;button.MinHeight=34;button.Padding=new(9,7,9,7);
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        ShortcutIcon? icon=null;
        if(LauncherPresentation.ShowIcon(app.Settings.LaunchStyle)){icon=new ShortcutIcon(shortcut){Margin=new(0,0,LauncherPresentation.ShowLabel(app.Settings.LaunchStyle)?7:0,0)};row.Children.Add(icon);}
        var label=UI.Text(shortcut.Name,11,UI.ContrastText(app.Settings.ControlFill));label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxWidth=115;row.Children.Add(label);
        if(icon!=null && !LauncherPresentation.ShowLabel(app.Settings.LaunchStyle))icon.AvailabilityChanged+=available=>label.Visibility=available?Visibility.Collapsed:Visibility.Visible;
        button.Content=row;return button;
    }
    private void StyleCategory(Button button,string page,bool selected=false)
    {
        bool iconsOnly=app.Settings.CategoryStyle=="Icons" || app.Settings.CategoryStyle=="IconsLabels" && bodyWidth<300;
        button.MinWidth=iconsOnly?32:0;button.MinHeight=iconsOnly?32:30;button.Padding=iconsOnly?new Thickness(8):new Thickness(5,5,5,5);button.Tag=new CornerRadius(iconsOnly?10:15);
        bool combined=!iconsOnly && LauncherPresentation.ShowIcon(app.Settings.CategoryStyle) && LauncherPresentation.ShowLabel(app.Settings.CategoryStyle);
        var row=new StackPanel{Orientation=combined?Orientation.Vertical:Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
        if(LauncherPresentation.ShowIcon(app.Settings.CategoryStyle)){var icon=Icons.Create(LauncherPresentation.CategoryIcon(page),iconsOnly?16:15,selected?UI.ContrastText(app.Settings.SelectionFill):UI.Muted);icon.Margin=combined?new Thickness(0,0,0,3):new Thickness(0);row.Children.Add(icon);}
        if(!iconsOnly && LauncherPresentation.ShowLabel(app.Settings.CategoryStyle)){var label=UI.Text(PageName(page),10.5,selected?UI.ContrastText(app.Settings.SelectionFill):UI.Muted);label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;row.Children.Add(label);}
        button.Content=row;button.ToolTip=PageName(page);UI.Named(button,PageName(page));System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"Selected":"");
    }
    private void ApplyTitleTypography(bool mediaSummary)
    {
        if(!Motion.Enabled)foreach(var label in marqueeLabels.Values)label.Reset();
        ApplyMediaTypography(title,app.Settings.BoldMediaTitle);
        ApplyMediaTypography(peekTitle,app.Settings.BoldMediaTitle);
        ApplyMediaTypography(artist,false);
        ApplyMediaTypography(peekSubtitle,false);
        summary.FontWeight=mediaSummary && app.Settings.BoldMediaTitle?FontWeights.SemiBold:FontWeights.Normal;
        ApplyHeadingTypography(body);
    }
    private void ApplyMediaTypography(TextBlock current,bool bold)
    {previousText.TryGetValue(current,out var outgoing);LauncherPresentation.ApplyMediaTypography(current,outgoing,bold);}
    private void ApplyHeadingTypography(DependencyObject node)=>LauncherPresentation.ApplyHeadingTypography(node,app.Settings.BoldHeadings);
}
