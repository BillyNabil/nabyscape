using System.Linq;

namespace NabyScape;

internal static class HoverPagePolicy
{
    internal static bool IsTool(string id)=>id is "calendar" or "notes" or "files" or "clips" or "tasks";
    internal static bool Available(Settings settings,string id)=>!IsTool(id) || settings.ToolEnabled(id) || settings.Tools.Any(tool=>tool.Id==id && tool.Enabled);
    internal static string[] Pages(Settings settings)=>settings.HoverViews.Where(v=>v.Enabled && Available(settings,v.Id)).Select(v=>v.Id).ToArray();
}
