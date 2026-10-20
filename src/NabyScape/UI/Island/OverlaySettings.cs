using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

// Snapshot only layout inputs, never personal data or a serialized copy of preferences.
internal readonly record struct OverlaySettingsState(int Content,int Reflow,int Geometry,int Hotkeys)
{
    internal static OverlaySettingsState Capture(Settings s)
    {
        var content=new HashCode();content.Add(s.CompactLayout);content.Add(s.CategoryStyle);content.Add(s.LaunchStyle);content.Add(s.ActivitiesEnabled);content.Add(s.LiveActivity);content.Add(s.MonitorNetwork);content.Add(s.MonitorStorage);content.Add(s.MonitorProcesses);
        foreach(var widget in s.Widgets){content.Add(widget.Id);content.Add(widget.Enabled);}foreach(var tool in s.Tools){content.Add(tool.Id);content.Add(tool.Enabled);}foreach(var shortcut in s.Shortcuts){content.Add(shortcut.Name);content.Add(shortcut.Target);content.Add(shortcut.AppId);}
        int reflow=HashCode.Combine(s.PanelWidth,s.PanelPadding,s.PanelScale,s.SectionGap);
        int geometry=HashCode.Combine(HashCode.Combine(s.Width,s.Height,s.Radius,s.Preset,s.Horizontal,s.Top,s.Monitor),s.PanelMaxHeight,s.HoverWidth,s.HoverHeight,s.DisplayMode,s.PanelAutoHeight);
        int hotkeys=HashCode.Combine(HashCode.Combine(s.TuckHotkeyEnabled,s.TuckHotkeyModifiers,s.TuckHotkeyKey),HashCode.Combine(s.HotkeyEnabled,s.HotkeyModifiers,s.HotkeyKey,s.VisibilityHotkeyEnabled,s.VisibilityHotkeyModifiers,s.VisibilityHotkeyKey));
        return new(content.ToHashCode(),reflow,geometry,hotkeys);
    }
}

public sealed partial class OverlayWindow
{
    private int editorRestoreRevision;
    private sealed record EditorState(string Name,string Text,int Start,int Length,double Offset,bool Focused);
    private (EditorState[] Editors,double Scroll) CaptureEditorState()
    {
        var editors=EditorTree(body).OfType<TextBox>().Select(editor=>new EditorState(System.Windows.Automation.AutomationProperties.GetName(editor),editor.Text,editor.SelectionStart,editor.SelectionLength,editor.VerticalOffset,editor.IsKeyboardFocusWithin)).Where(editor=>editor.Name.Length>0).ToArray();
        return(editors,scroll.VerticalOffset);
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> EditorTree(DependencyObject node)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()){yield return child;foreach(var item in EditorTree(child))yield return item;}
    }
    private void RestoreEditorState((EditorState[] Editors,double Scroll) state)
    {
        body.UpdateLayout();
        foreach(var editor in EditorTree(body).OfType<TextBox>())
        {
            var saved=state.Editors.FirstOrDefault(item=>item.Name==System.Windows.Automation.AutomationProperties.GetName(editor));if(saved==null)continue;
            if(editor.Text!=saved.Text)editor.Text=saved.Text;
            editor.Select(Math.Min(saved.Start,editor.Text.Length),Math.Min(saved.Length,Math.Max(0,editor.Text.Length-saved.Start)));editor.ScrollToVerticalOffset(saved.Offset);if(saved.Focused)editor.Focus();
        }
        void RestoreScroll()
        {
            body.UpdateLayout();
            foreach(var editor in EditorTree(body).OfType<TextBox>())
            {
                var saved=state.Editors.FirstOrDefault(item=>item.Name==System.Windows.Automation.AutomationProperties.GetName(editor));
                if(saved!=null)editor.ScrollToVerticalOffset(saved.Offset);
            }
            scroll.ScrollToVerticalOffset(state.Scroll);
        }
        RestoreScroll();int revision=++editorRestoreRevision;
        // WPF queues caret visibility during focus/layout; restore the user's viewport after that work.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,()=>{if(revision==editorRestoreRevision)RestoreScroll();});
    }
    private OverlaySettingsState? appliedSettings;
    private bool bodyInitialized,bodyDirty=true;
    private readonly System.Collections.Generic.List<Action> bodyReflow=new();
    private readonly System.Collections.Generic.HashSet<string> pendingCompactPages=new();
    private void EnsureCompactPage(string page)
    {
        if(!pendingCompactPages.Remove(page))return;
        var old=compactPages[page];int index=compactPageHost.Children.IndexOf(old);
        var view=page=="mixer"?BuildMixerPage():BuildCompactPage(page);view.Visibility=Visibility.Collapsed;compactPages[page]=view;compactPageHost.Children.Remove(old);compactPageHost.Children.Insert(index,view);ApplyHeadingTypography(view);
    }
    internal int BodyBuilds {get;private set;}
    private void EnsureBody(){if(!bodyInitialized || bodyDirty)BuildBody();}
    private bool ApplyIncrementalSettings(OverlaySettingsState next)
    {
        if(appliedSettings is not { } previous){appliedSettings=next;return false;}
        appliedSettings=next;
        if(previous.Content!=next.Content || classicMixerOpen){bodyDirty=true;return false;}
        var s=app.Settings;
        if(previous.Hotkeys!=next.Hotkeys)ConfigureHotkey();
        Motion.Configure(s);Opacity=s.Opacity;surface.Fill=UI.Brush(s.Background);surface.GlassStyle=s.GlassStyle;surface.SetMaterial(s.Background,s.GlassStrength);surface.Notch=s.Preset=="Mac";surface.Radius=s.Radius;
        hoverDelay.Interval=TimeSpan.FromMilliseconds(s.HoverDelayMs);detailDelay.Interval=TimeSpan.FromMilliseconds(s.HoverDetailDelayMs);
        ApplyHeadingTypography(body);ApplyMediaTypography(title,s.BoldMediaTitle);
        foreach(var tab in compactTabs)StyleCategory(tab.Value,tab.Key,tab.Key==(activePage=="mixer"?"media":activePage));
        if(previous.Geometry!=next.Geometry || previous.Reflow!=next.Reflow)
        {
            refreshHz=Native.RefreshRate(Native.ChooseScreen(s));compact.Width=Math.Max(120,Math.Min(s.Width,expandedWidth)-(surface.Notch?32:12));compact.Height=s.Height;compact.MinHeight=s.Height;
            Dimensions();foreach(var reflow in bodyReflow)reflow();Dimensions();foreach(var tab in compactTabs)StyleCategory(tab.Value,tab.Key,tab.Key==(activePage=="mixer"?"media":activePage));Width=WindowHostWidth;Height=Math.Max(HostHeight,s.Height);
            surface.BeginUpdate();
            try{motion.Snap(surface,IslandSurface.SurfaceWidthProperty,expanded?expandedWidth:peeking?PeekSurfaceWidth:s.Width);motion.Snap(surface,IslandSurface.SurfaceHeightProperty,expanded?expandedHeight:peeking?(assistantPeeking?AssistantPeekHeight:peekDetailed?s.HoverHeight:100):s.Height);}
            finally{surface.EndUpdate();}
            if(peeking)PreparePeekDeck();
        }
        surface.Refresh();ApplyEnvironmentTuck();Position();Update();ConfigureLiveActivity();return true;
    }
}
