using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
    internal static string CategoryIcon(string page)=>page switch{"media"=>"nav-media","focus"=>"nav-focus","stopwatch"=>"timer","voice"=>"mic","status"=>"nav-status","tools"=>"nav-tools","files"=>"folder","notes" or "tasks"=>"notes","clips"=>"clipboard","calendar"=>"calendar","weather"=>"weather","awake"=>"coffee","links"=>"link",_=>"nav-launch"};
}
public sealed partial class OverlayWindow
{
    private Button LaunchButton(Shortcut shortcut)
    {
        var button=UI.Button(shortcut.Name,()=>{try{ShortcutService.Open(shortcut);Collapse();}catch(Exception ex){app.Notify("Could not open shortcut",ex.Message);}});
        button.ToolTip=shortcut.Name+"\n"+shortcut.Target;button.MinHeight=44;button.Padding=new(9);button.Margin=new(0,0,10,10);button.Background=Brushes.Transparent;button.BorderThickness=new(0);
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        ShortcutIcon? icon=null;
        if(LauncherPresentation.ShowIcon(app.Settings.LaunchStyle)){icon=new ShortcutIcon(shortcut){Margin=new(0,0,LauncherPresentation.ShowLabel(app.Settings.LaunchStyle)?7:0,0)};row.Children.Add(icon);}
        var label=UI.Text(shortcut.Name,11,UI.ContrastText(app.Settings.ControlFill));label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxWidth=115;row.Children.Add(label);
        if(icon!=null && !LauncherPresentation.ShowLabel(app.Settings.LaunchStyle))icon.AvailabilityChanged+=available=>label.Visibility=available?Visibility.Collapsed:Visibility.Visible;
        button.Content=row;return button;
    }
    private (bool Media,bool Summary,bool Headings,bool Motion)? titleAppearance;
    private bool? bodyHeadingWeight;
    private void StyleCategory(Button button,string page,bool selected=false)
    {
        button.Background=Brushes.Transparent;button.MinWidth=18;button.MinHeight=30;button.Padding=new(5);button.Margin=new(0);
        button.Content=new System.Windows.Shapes.Rectangle{Width=selected?13:5,Height=5,RadiusX=2.5,RadiusY=2.5,Fill=Accessibility.HighContrast?SystemColors.WindowTextBrush:selected?Brushes.White:UI.Muted,Opacity=Accessibility.HighContrast || selected?1:.5};
        button.ToolTip=PageName(page);UI.Named(button,PageName(page));System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?Localization.Text("Selected"):"");
    }
    private void ApplyTitleTypography(bool mediaSummary)
    {
        var key=(app.Settings.BoldMediaTitle,mediaSummary,app.Settings.BoldHeadings,Motion.Enabled);if(titleAppearance==key)return;titleAppearance=key;
        if(!Motion.Enabled)foreach(var label in marqueeLabels.Values)label.Reset();
        ApplyMediaTypography(title,app.Settings.BoldMediaTitle);
        ApplyMediaTypography(peekTitle,app.Settings.BoldMediaTitle);
        ApplyMediaTypography(artist,false);
        ApplyMediaTypography(peekSubtitle,false);
        summary.FontWeight=mediaSummary && app.Settings.BoldMediaTitle?FontWeights.SemiBold:FontWeights.Normal;
        if(bodyHeadingWeight!=app.Settings.BoldHeadings)ApplyHeadingTypography(body);
    }
    private void ApplyMediaTypography(TextBlock current,bool bold)
    {previousText.TryGetValue(current,out var outgoing);LauncherPresentation.ApplyMediaTypography(current,outgoing,bold);}
    private void ApplyHeadingTypography(DependencyObject node){LauncherPresentation.ApplyHeadingTypography(node,app.Settings.BoldHeadings);if(ReferenceEquals(node,body))bodyHeadingWeight=app.Settings.BoldHeadings;}
}
