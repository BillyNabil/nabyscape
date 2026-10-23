using System.Linq;
namespace NabyScape;
internal static class HoverPagePolicy
{
    internal static readonly string[] Ids={"media","volume","timer","focus","calendar","notes","files","system","clips","tasks","stopwatch","weather","clock","battery","shortcuts","awake","links","voice"};
    internal static bool IsTool(string id)=>Settings.ToolNames.ContainsKey(id);
    internal static bool Available(Settings s,string id)=>s.HoverFollowsWidgets?s.Widgets.Any(w=>w.Id==id && w.Enabled):!IsTool(id) || s.ToolEnabled(id) || s.Tools.Any(t=>t.Id==id && t.Enabled);
    internal static string[] Pages(Settings s)=>s.HoverFollowsWidgets?s.Widgets.Where(w=>w.Enabled && Ids.Contains(w.Id)).Select(w=>w.Id).ToArray():s.HoverViews.Where(v=>v.Enabled && Available(s,v.Id)).Select(v=>v.Id).ToArray();
}
