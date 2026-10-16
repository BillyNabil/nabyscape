using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NabyScape;

internal static class CustomizationProfile
{
    internal static readonly string[] CustomizationKeys={"AlwaysTucked","TuckEdge","Preset","Width","Height","Radius","Top","Horizontal","Monitor","DisplayMode","Background","Accent","Opacity","GlassStyle","GlassStrength","AlbumAccent","PanelWidth","PanelMaxHeight","PanelPadding","PanelScale","SectionGap","HoverWidth","HoverHeight","HoverDelayMs","HoverDetailDelayMs","SwipeDistance","HoverViews","Widgets","CompactLayout","HoverPeek","TwoStageHover","AnimationMs","ReduceMotion","LiveActivity","ClosedMediaIcon","ClosedMediaContent","Performance","WheelAction","MiddleAction","VolumeStep","Clock24Hour","ClockSeconds","ShowDate","MonitorNetwork","MonitorStorage","MonitorProcesses","CategoryStyle","LaunchStyle","BoldMediaTitle","BoldHeadings","WaveformIdleMotion","VolumeFeedback","BrightnessFeedback"};
    internal static string Export(Settings settings)
    {
        var values=CustomizationKeys.Concat(new[]{"DropReveal","DetachablePreviews","CoverGestures","ActivitySatellite","SatelliteSource","ControlFill","ControlBorder","SelectionFill","PanelSwipe","Tools","ActivitiesEnabled"}).ToDictionary(key=>key,key=>typeof(Settings).GetProperty(key)!.GetValue(settings));
        return JsonSerializer.Serialize(new{format="luma.layout.v1",values},new JsonSerializerOptions{WriteIndented=true});
    }
    internal static void Import(string source,Settings target)
    {
        if(source.Length>262144)throw new InvalidDataException("Layout file is too large.");
        using var document=JsonDocument.Parse(source);
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("format",out var format) || format.GetString()!="luma.layout.v1" || !root.TryGetProperty("values",out var values) || values.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Choose a NabyScape layout file.");
        var allKeys=CustomizationKeys.Concat(new[]{"DropReveal","DetachablePreviews","CoverGestures","ActivitySatellite","SatelliteSource","ControlFill","ControlBorder","SelectionFill","PanelSwipe","Tools","ActivitiesEnabled"}).ToArray();
        var keys=allKeys.Where(key=>values.TryGetProperty(key,out _)).ToArray();if(keys.Length==0)throw new InvalidDataException("The layout has no customization values.");
        var incoming=JsonSerializer.Deserialize<Settings>(values.GetRawText())??throw new InvalidDataException("Layout is empty.");
        var candidate=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(target))!;
        foreach(string key in keys){var property=typeof(Settings).GetProperty(key)!;property.SetValue(candidate,property.GetValue(incoming));}
        if(values.TryGetProperty("Monitor",out _) && !values.TryGetProperty("DisplayMode",out _))candidate.DisplayMode=string.IsNullOrEmpty(incoming.Monitor)?"Primary":"Selected";
        candidate.Normalize();
        foreach(string key in allKeys){var property=typeof(Settings).GetProperty(key)!;property.SetValue(target,property.GetValue(candidate));}
    }
}
