using System;
using System.Collections.Generic;
using System.Linq;

namespace NabyScape;

internal static class AudioMixerTests
{
    internal static void Run(Action<string,bool> check)
    {
        check("Gain clamps at both bounds",AudioMixer.BoundedVolume(-.1)==0 && AudioMixer.BoundedVolume(1.1)==1 && AudioMixer.BoundedVolume(.5)==.5);
        bool rejected=false;try{AudioMixer.BoundedVolume(double.NaN);}catch(ArgumentException){rejected=true;}
        check("Non-finite volume rejected",rejected);
        var output=new MixerItem(new(MixerScope.Outputs,"speakers"),"Speakers","Speakers",.98,false);
        var first=new MixerItem(new(MixerScope.Apps,"speakers","instance-a"),"App A","Speakers",.01,false);
        var second=new MixerItem(new(MixerScope.Apps,"headphones","instance-b"),"App B","Headphones",.6,true);
        var snapshot=new MixerSnapshot(new[]{output},new[]{first,second},Array.Empty<string>());
        var writes=new List<(MixerTarget Target,double Value)>();
        string? Write(MixerTarget target,double value){writes.Add((target,value));return null;}
        var apps=AudioMixer.Adjust(snapshot,MixerScope.Apps,-.05,Write);
        check("All apps adjusts sessions across outputs without endpoint gain",apps.Changed==2 && writes.Count==2 && writes.All(x=>x.Target.Scope==MixerScope.Apps) && writes[0].Value==0 && Math.Abs(writes[1].Value-.55)<.00001);
        writes.Clear();var outputs=AudioMixer.Adjust(snapshot,MixerScope.Outputs,.05,Write);
        check("All outputs adjusts only endpoint masters",outputs.Changed==1 && writes.Count==1 && writes[0].Target==output.Target && writes[0].Value==1);
        writes.Clear();var stale=AudioMixer.Adjust(snapshot,MixerScope.Apps,.05,(target,value)=>target==first.Target?"Session ended":Write(target,value));
        check("Stale session failure preserves remaining operations",stale.Changed==1 && stale.Failed==1 && stale.Errors.Single().Contains("Session ended") && writes.Single().Target==second.Target);
        var throwing=AudioMixer.Adjust(snapshot,MixerScope.Apps,.05,(target,value)=>target==first.Target?throw new InvalidOperationException("Disconnected"):null);
        check("Partial backend failure continues",throwing.Changed==1 && throwing.Failed==1);
        var duplicate=snapshot with {Apps=new[]{first,first}};
        writes.Clear();AudioMixer.Adjust(duplicate,MixerScope.Apps,.05,Write);
        check("Duplicate session instances never double-scale",writes.Count==1);
        check("Mixer capture and held volume keys protect active edits",OverlayWindow.MixerInteractionActive(true,false,false) && OverlayWindow.MixerInteractionActive(false,true,true));
        check("Mixer idle focus and released volume keys resume refresh",!OverlayWindow.MixerInteractionActive(false,true,false) && !OverlayWindow.MixerInteractionActive(false,false,true));
        var rows=new System.Windows.Controls.StackPanel();var retained=new System.Windows.Controls.Slider();var staleRow=new System.Windows.Controls.Button();
        rows.Children.Add(retained);rows.Children.Add(staleRow);
        var added=new System.Windows.Controls.Button();
        OverlayWindow.ReconcileMixerRows(rows,new System.Windows.FrameworkElement[]{added,retained});
        check("Mixer refresh keeps slider attached while adding and removing rows",rows.Children.Count==2 && ReferenceEquals(rows.Children[1],retained) && ReferenceEquals(retained.Parent,rows) && staleRow.Parent==null);
        MixerRowLayoutTests.Run(check);
        // Read-only smoke: Snapshot has no path to Set / AdjustAll and never opens a capture endpoint.
        var native=new AudioMixer().Snapshot();
        check($"Native read-only snapshot: {native.Outputs.Count} outputs, {native.Apps.Count} sessions, {native.Errors.Count} errors"+(native.Errors.Count>0?" ("+string.Join("; ",native.Errors)+")":""),native.Outputs.All(x=>x.Target.Scope==MixerScope.Outputs && x.Volume>=0 && x.Volume<=1) && native.Apps.All(x=>x.Target.Scope==MixerScope.Apps && x.Target.SessionId.Length>0 && x.Volume>=0 && x.Volume<=1));
    }
}
