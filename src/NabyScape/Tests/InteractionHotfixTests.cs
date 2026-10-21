using System;
using System.Linq;

namespace NabyScape;

internal static class InteractionHotfixTests
{
    internal static void Run(Action<string,Action> test)
    {
        static void Assert(bool value){if(!value)throw new Exception("Assertion failed");}
        test("Hover pages retain all explicit choices with every main widget hidden",()=>
        {
            var s=new Settings{HoverFollowsWidgets=false};s.Normalize();foreach(var view in s.HoverViews)view.Enabled=true;foreach(var widget in s.Widgets)widget.Enabled=false;
            var before=s.Widgets.Select(w=>(w.Id,w.Enabled)).ToArray();
            Assert(HoverPagePolicy.Pages(s).Length==HoverPagePolicy.Ids.Length && !s.ClipboardHistory && before.SequenceEqual(s.Widgets.Select(w=>(w.Id,w.Enabled))));
            foreach(var tool in s.Tools.Where(t=>HoverPagePolicy.IsTool(t.Id)))
            {tool.Enabled=false;Assert(!HoverPagePolicy.Pages(s).Contains(tool.Id) && HoverPagePolicy.Pages(s).Length==HoverPagePolicy.Ids.Length-1);tool.Enabled=true;}
            s.HoverViews.Single(v=>v.Id=="system").Enabled=false;Assert(!HoverPagePolicy.Pages(s).Contains("system"));
            s.HoverViews.Reverse();Assert(HoverPagePolicy.Pages(s).SequenceEqual(s.HoverViews.Where(v=>v.Enabled).Select(v=>v.Id)));
        });
        test("Mixer fallback is compact app identity and process lookup uses local limited query",()=>
        {
            Assert(MixerAppIcon.Monogram("Discord")=="DI" && MixerAppIcon.Monogram("LEDKeeper")=="LE" && MixerAppIcon.Monogram("System sounds")=="SS" && MixerAppIcon.Monogram(" ! ")=="•");
            string? local=AudioMixer.LocalProcessImage((uint)Environment.ProcessId);
            Assert(local!=null && local.Equals(Environment.ProcessPath,StringComparison.OrdinalIgnoreCase) && AudioMixer.LocalProcessImage(0)==null && AudioMixer.LocalProcessImage(uint.MaxValue)==null);
            var shortcut=new Shortcut{Name="Current process",Target=local!};var first=AppIconCache.GetAsync(shortcut);var second=AppIconCache.GetAsync(shortcut);
            var icon=first.GetAwaiter().GetResult();Assert(ReferenceEquals(first,second) && icon is {IsFrozen:true,PixelWidth:>0});
        });
    }
}
