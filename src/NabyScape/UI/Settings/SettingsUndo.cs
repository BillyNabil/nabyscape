using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private readonly Button undoSettings=UI.CaptionButton("Undo",()=>{});
    private readonly Dictionary<string,JsonElement> undoValues=new();
    private static readonly HashSet<string> UndoKeys=new(("Preset Width Height Radius Top Horizontal DockEdge DockVertical DockInset Monitor DisplayMode Background Accent Opacity SettingsMaterial GlassStyle GlassStrength CoverAmbient ClosedAmbient AmbientStrength AmbientSpread AmbientMotion AmbientScope AmbientWidgets AmbientColorSource AmbientCustomColor AmbientSecondaryColor AmbientPalette AmbientDirection AmbientHeight AmbientBalance AmbientSoftness AmbientAudioResponse AmbientSensitivity AlbumAccent ControlFill ControlBorder SelectionFill PanelWidth PanelMaxHeight PanelPadding PanelScale SectionGap HoverWidth HoverHeight AnimationMs ReduceMotion LiveActivity ClosedMediaIcon ClosedMediaContent Performance CategoryStyle LaunchStyle BoldMediaTitle BoldHeadings Widgets Tools HoverViews HoverFollowsWidgets").Split(' '));
    private void CaptureSettingsUndo(JsonElement before)
    {
        var after=JsonSerializer.SerializeToElement(app.Settings);var changes=new Dictionary<string,JsonElement>();
        foreach(string key in UndoKeys)
            if(before.TryGetProperty(key,out var old) && after.TryGetProperty(key,out var next) && old.GetRawText()!=next.GetRawText())changes[key]=old.Clone();
        // Window placement and other unrelated saves must not erase the last visual Undo.
        if(changes.Count==0)return;
        undoValues.Clear();foreach(var item in changes)undoValues[item.Key]=item.Value;
        undoSettings.Visibility=Visibility.Visible;undoSettings.ToolTip="Undo the last saved appearance, layout or widget change. Saved content, connections and background permissions are preserved.";
    }
    private void UndoSettings()
    {
        if(saveTimer.IsEnabled)CommitSettings();if(undoValues.Count==0)return;
        try
        {
            applyingSettings=true;
            foreach(var item in undoValues)
            {
                var property=typeof(Settings).GetProperty(item.Key);
                if(property?.CanWrite==true)property.SetValue(app.Settings,JsonSerializer.Deserialize(item.Value.GetRawText(),property.PropertyType));
            }
            app.ChangeSettings();
            if(!app.SettingsSaveSucceeded){SetSaveFeedback("Undo is active for this session but could not be saved. Choose Retry save.",true);return;}
            savedSettings=JsonSerializer.SerializeToElement(app.Settings);undoValues.Clear();undoSettings.Visibility=Visibility.Collapsed;
            if(current!="AI")Navigate(current);SetSaveFeedback("Undone · your previous appearance, layout or widget choices are saved.");
        }
        catch(Exception ex){app.Store.Log(ex);SetSaveFeedback("Could not undo this change. Choose Retry save.",true);}
        finally{applyingSettings=false;}
    }
}
