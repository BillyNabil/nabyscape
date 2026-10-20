using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NabyScape;

internal static class PreferenceReset
{
    internal static readonly Dictionary<string,string[]> Categories=new()
    {
        ["Appearance"]="Preset Radius Background Accent Opacity SettingsMaterial GlassStyle GlassStrength CoverAmbient AmbientStrength AmbientSpread AmbientMotion AmbientScope AmbientWidgets AmbientColorSource AmbientCustomColor AmbientAudioResponse AmbientSensitivity InteractionMotion AlbumAccent ControlFill ControlBorder SelectionFill CategoryStyle BoldMediaTitle BoldHeadings AnimationMs ReduceMotion LiveActivity Performance RecentColors".Split(' '),
        ["Layout"]="DockUnlocked PanelAutoHeight Width Height Horizontal Top Monitor DisplayMode CompactLayout PanelWidth PanelMaxHeight PanelPadding PanelScale SectionGap".Split(' '),
        ["Content"]="Widgets Tools HoverViews FocusMinutes BreakMinutes".Split(' '),
        ["Controls"]="HoverPeek TwoStageHover HoverWidth HoverHeight HoverDelayMs HoverDetailDelayMs SwipeDistance PanelSwipe DropReveal DetachablePreviews CoverGestures ActivitySatellite SatelliteSource SmartActivities BrowserMediaPreviews WheelAction MiddleAction VolumeStep HotkeyEnabled HotkeyKey HotkeyModifiers HideFullscreen HideMaximized VisibilityHotkeyEnabled VisibilityHotkeyKey VisibilityHotkeyModifiers AlwaysTucked TuckEdge TuckHotkeyEnabled TuckHotkeyKey TuckHotkeyModifiers".Split(' '),
        ["Audio"]="WaveformSource WaveformSensitivity LyricsEnabled LyricsAnimation LyricsOffset ClosedMediaIcon ClosedMediaContent ReactiveFeedback WaveformIdleMotion VolumeFeedback BrightnessFeedback".Split(' '),
        ["System"]="Clock24Hour ClockSeconds ShowDate MonitorNetwork MonitorStorage MonitorProcesses".Split(' '),
        ["Launch"]="LaunchStyle ActivitiesEnabled".Split(' '),
        ["AI"]=Array.Empty<string>(),
        ["Application"]="CountdownSound CountdownSoundVolume CountdownSoundFile AutoCheckUpdates NotificationSeconds NotificationAnimation QuietHours QuietStart QuietEnd NotificationsSnoozedUntil NotificationDelivery NotifyTimer NotifyFocus NotifyDownloads NotifyUpdates NotifyFailures DownloadsEnabled DownloadsFolder".Split(' ')
    };
    internal static string Summary(string? category)
    {
        string Detail(string name)=>name switch
        {
            "Appearance"=>"shape, colors, text weights, motion and performance",
            "Layout"=>"island position, display and panel sizes",
            "Content"=>"widget/tool order, hover pages and focus durations",
            "Controls"=>"hover timing, gestures, tuck rules and global shortcuts",
            "Audio"=>"closed media display, waveform and feedback",
            "System"=>"clock format and monitor readings",
            "Launch"=>"launch appearance and Activities availability",
            _=>"update checks, notification delivery and download monitoring"
        };
        string detail=category==null?string.Join("; ",Categories.Where(pair=>pair.Value.Length>0).Select(pair=>pair.Key+": "+Detail(pair.Key))):Detail(category);
        return $"Reset {(category??"all preferences")} to defaults ({Keys(category).Length} preferences): {detail}. Startup, clipboard capture/history, shortcuts, Notes, Tasks, shelf, calendar and activity targets are kept. A preference backup is saved first.";
    }
    private static string[] Keys(string? category)=>category==null?Categories.Values.SelectMany(v=>v).Distinct().ToArray():Categories[category];
    internal static void Reset(Store store,Settings target,string? category)
    {
        var candidate=Clone(target);var defaults=FactoryDefaults.Create();
        foreach(string key in Keys(category))Copy(defaults,candidate,key);
        candidate.Normalize();
        // Backup must succeed before either persisted or live preferences change.
        store.Save("preference-backup",target);
        Commit(store,target,candidate);
    }
    internal static void Restore(Store store,Settings target)
    {
        string file=Path.Combine(store.DirectoryPath,"preference-backup.json");
        using var json=JsonDocument.Parse(File.ReadAllText(file));
        if(json.RootElement.ValueKind!=JsonValueKind.Object || !json.RootElement.TryGetProperty(nameof(Settings.Width),out _))throw new InvalidDataException("The preference backup is invalid.");
        foreach(string key in Keys(null))if(!PremiumPreferences.Keys.Contains(key) && key!="DisplayMode" && key!="SettingsMaterial" && !Categories["Application"].Contains(key) && (!json.RootElement.TryGetProperty(key,out var value) || value.ValueKind==JsonValueKind.Null))throw new InvalidDataException("The preference backup is incomplete: "+key);
        var restored=json.RootElement.Deserialize<Settings>()??throw new InvalidDataException("The preference backup is empty.");
        var candidate=Clone(target);
        foreach(string key in Keys(null))Copy(restored,candidate,key);
        candidate.Normalize();Commit(store,target,candidate);
    }
    private static Settings Clone(Settings value)=>JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(value))!;
    private static void Copy(Settings source,Settings target,string key){var property=typeof(Settings).GetProperty(key)!;property.SetValue(target,property.GetValue(source));}
    private static void Commit(Store store,Settings target,Settings candidate)
    {
        store.Save("settings",candidate);
        foreach(var property in typeof(Settings).GetProperties().Where(p=>p.CanWrite))property.SetValue(target,property.GetValue(candidate));
    }
}

internal static class HotkeyPolicy
{
    internal static string Conflict(Settings s,string action)
    {
        var shortcuts=new[]{("Panel",s.HotkeyEnabled,s.HotkeyModifiers,s.HotkeyKey),("Visibility",s.VisibilityHotkeyEnabled,s.VisibilityHotkeyModifiers,s.VisibilityHotkeyKey),("Tuck",s.TuckHotkeyEnabled,s.TuckHotkeyModifiers,s.TuckHotkeyKey)};
        var own=shortcuts.First(v=>v.Item1==action);
        var other=shortcuts.FirstOrDefault(v=>v.Item1!=action && own.Item2 && v.Item2 && v.Item3==own.Item3 && v.Item4==own.Item4);
        return other.Item1==null?"":$"{action} shortcut conflicts with {other.Item1}. Choose a different key or modifiers.";
    }
}
