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
        ["Ambience"]="CoverAmbient ClosedAmbient AmbientStrength AmbientSpread AmbientMotion AmbientScope AmbientWidgets AmbientColorSource AmbientCustomColor AmbientSecondaryColor AmbientPalette AmbientDirection AmbientHeight AmbientBalance AmbientSoftness AmbientAudioResponse AmbientSensitivity".Split(' '),
        ["Motion"]="InteractionMotion AnimationMs ReduceMotion LiveActivity Performance".Split(' '),
        ["Clipboard"]="ClipboardHistory".Split(' '),
        ["Weather"]="WeatherCity WeatherLatitude WeatherLongitude WeatherUnits".Split(' '),
        ["Visibility"]="HideFullscreen HideMaximized AlwaysTucked TuckEdge TuckHotkeyEnabled TuckHotkeyKey TuckHotkeyModifiers VisibilityHotkeyEnabled VisibilityHotkeyKey VisibilityHotkeyModifiers SmartActivities BrowserMediaPreviews".Split(' '),
        ["Notifications"]="ExternalNotifications ExternalNotificationPopup ExternalNotificationText ExternalNotificationQuietHours ExternalNotificationMuted CountdownSound CountdownSoundVolume CountdownSoundFile NotificationSeconds NotificationAnimation QuietHours QuietStart QuietEnd NotificationsSnoozedUntil NotificationDelivery NotifyTimer NotifyFocus NotifyDownloads NotifyUpdates NotifyFailures DownloadsEnabled DownloadsFolder".Split(' '),
        ["Appearance"]="Preset Radius Background Accent Opacity SettingsMaterial GlassStyle GlassStrength AlbumAccent ControlFill ControlBorder SelectionFill CategoryStyle BoldMediaTitle BoldHeadings RecentColors".Split(' '),
        ["Layout"]="FollowMonitorAnimation FollowMonitorDuration DockUnlocked PanelAutoHeight Width Height Horizontal DockEdge DockVertical DockInset Top Monitor DisplayMode CompactLayout PanelWidth PanelMaxHeight PanelPadding PanelScale SectionGap".Split(' '),
        ["Content"]="HoverFollowsWidgets Widgets Tools HoverViews FocusMinutes BreakMinutes".Split(' '),
        ["Controls"]="HoverPeek TwoStageHover HoverWidth HoverHeight HoverDelayMs HoverDetailDelayMs SwipeDistance PanelSwipe DropReveal DetachablePreviews CoverGestures WheelAction MiddleAction VolumeStep HotkeyEnabled HotkeyKey HotkeyModifiers".Split(' '),
        ["Audio"]="MediaIntegrationEnabled WaveformSource WaveformSensitivity LyricsEnabled LyricsAnimation LyricsTextMode LyricsOffset ClosedMediaIcon ClosedMediaContent ReactiveFeedback WaveformIdleMotion VolumeFeedback BrightnessFeedback".Split(' '),
        ["System"]="Clock24Hour ClockSeconds ShowDate MonitorNetwork MonitorStorage MonitorProcesses".Split(' '),
        ["Launch"]="LaunchStyle".Split(' '),
        ["AI"]=Array.Empty<string>(),
        ["Application"]="AutoCheckUpdates AutoDownloadUpdates UiLanguage AccessibilityTextScale AccessibilityHighContrast SettingsShowAll".Split(' ')
    };
    internal static string Summary(string? category)
    {
        string Detail(string name)=>name switch
        {
            "Appearance"=>Localization.Text("dock shape, colors and text style"),
            "Ambience"=>Localization.Text("ambient colors, light shape, audio response and closed-dock glow"),
            "Motion"=>Localization.Text("animation, reduced motion and performance preferences"),
            "Clipboard"=>Localization.Text("clipboard capture preferences"),
            "Weather"=>Localization.Text("weather city and temperature units"),
            "Visibility"=>Localization.Text("hide, tuck, automatic previews and visibility shortcuts"),
            "Notifications"=>Localization.Text("notification delivery, sounds, quiet hours and download alerts"),
            "Layout"=>Localization.Text("island position, display and panel sizes"),
            "Content"=>Localization.Text("widget/tool order, hover pages and focus durations"),
            "Controls"=>Localization.Text("hover timing, gestures, tuck rules and global shortcuts"),
            "Audio"=>Localization.Text("closed media display, waveform and feedback"),
            "System"=>Localization.Text("clock format and monitor readings"),
            "Launch"=>Localization.Text("launch appearance"),
            "Application"=>Localization.Text("update checks and automatic downloads, interface language, text size and contrast"),
            _=>Localization.Text("update checks, notification delivery and download monitoring")
        };
        string detail=category==null?string.Join("; ",Categories.Where(pair=>pair.Value.Length>0).Select(pair=>Localization.Text(pair.Key)+": "+Detail(pair.Key))):Detail(category);
        return Localization.Format("Reset {0} to defaults ({1} preferences): {2}. Startup, clipboard capture/history, shortcuts, Notes, Tasks, shelf, calendar and activity targets are kept. A preference backup is saved first.",Localization.Text(category??"all preferences"),Keys(category).Length,detail);
    }
    private static string[] Keys(string? category)=>category==null?Categories.Values.SelectMany(v=>v).Where(key=>key!=nameof(Settings.ClipboardHistory)).Distinct().ToArray():Categories[category];
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
        if(json.RootElement.ValueKind!=JsonValueKind.Object || !json.RootElement.TryGetProperty(nameof(Settings.Width),out _))throw new InvalidDataException(Localization.Text("The preference backup is invalid."));
        foreach(string key in Keys(null))
        {
            if(PremiumPreferences.Keys.Contains(key) || key is "DisplayMode" or "SettingsMaterial" || Categories["Application"].Contains(key))continue;
            bool nullable=Nullable.GetUnderlyingType(typeof(Settings).GetProperty(key)!.PropertyType)!=null;
            if(!json.RootElement.TryGetProperty(key,out var value) || value.ValueKind==JsonValueKind.Null && !nullable)
                throw new InvalidDataException(Localization.Text("The preference backup is incomplete: ")+key);
        }
        var restored=json.RootElement.Deserialize<Settings>()??throw new InvalidDataException(Localization.Text("The preference backup is empty."));
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
        return other.Item1==null?"":Localization.Format("{0} shortcut conflicts with {1}. Choose a different key or modifiers.",Localization.Text(action),Localization.Text(other.Item1));
    }
}
