using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NabyScape;

// Generate feedback from the preferences actually saved, including custom controls.
// Never include file paths, locations, shortcuts or other free-form user content.
internal static class SettingsChangeFeedback
{
    private static readonly Dictionary<string,string> Labels=new()
    {
        ["CoverAmbient"]="Ambient light",["ClosedAmbient"]="Closed dock ambience",
        ["AmbientStrength"]="Ambient intensity",["AutoCheckUpdates"]="Automatic update checks",
        ["AutoDownloadUpdates"]="Automatic update downloads",["Width"]="Closed dock width",
        ["Height"]="Closed dock height",["TuckEdge"]="Tucked edge height",["Widgets"]="Widgets & order",
        ["HoverViews"]="Hover pages",["Tools"]="Tools & order",["Shortcuts"]="Shortcuts",
        ["Performance"]="Animation performance",["AutoStart"]="Start with Windows"
    };
    internal static string Describe(JsonElement before,Settings settings)
    {
        var after=JsonSerializer.SerializeToElement(settings);var changes=new List<string>();
        foreach(var property in after.EnumerateObject())
        {
            if(before.TryGetProperty(property.Name,out var old) && old.GetRawText()==property.Value.GetRawText())continue;
            string label=Labels.GetValueOrDefault(property.Name)??Regex.Replace(property.Name,"(?<=[a-z0-9])([A-Z])"," $1");
            string value=property.Value.ValueKind switch
            {
                JsonValueKind.True=>Localization.Text("On"),JsonValueKind.False=>Localization.Text("Off"),
                JsonValueKind.Number=>property.Name=="AmbientStrength"?(property.Value.GetDouble()*100).ToString("0")+"%":property.Value.GetRawText(),
                _=>""
            };
            changes.Add(Localization.Text(label)+(value.Length>0?": "+value:" updated"));
        }
        return changes.Count==0?"Saved on this device.":"Saved · "+string.Join(" · ",changes.Take(3))+(changes.Count>3?" · +"+(changes.Count-3)+" more":"");
    }
}
