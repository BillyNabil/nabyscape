using System;
namespace NabyScape;

internal enum ClosedClockSlot {Leading,Center,Trailing}
// Pure placement rules for the closed-dock clock, shared by IdleDock and the self-tests.
internal static class ClosedClockLayout
{
    // Live content (music title, waveform, lyrics, timers, voice, notices) owns the middle of the
    // closed dock. A centered clock steps to the right edge while it plays and returns when idle.
    internal static ClosedClockSlot Slot(string position,bool idle)=>position switch
    {
        "Left"=>ClosedClockSlot.Leading,"Right"=>ClosedClockSlot.Trailing,
        "Center"=>idle?ClosedClockSlot.Center:ClosedClockSlot.Trailing,
        _=>idle?ClosedClockSlot.Leading:ClosedClockSlot.Trailing
    };
    // Header column for the clock; a centered clock spans every column so it centers on the whole dock.
    internal static int Column(ClosedClockSlot slot)=>slot==ClosedClockSlot.Trailing?4:0;
    internal static double Gap(ClosedClockSlot slot)=>slot==ClosedClockSlot.Trailing?12:0;
    // A left clock sits before the music artwork/waveform icon, which moves right by the clock width.
    internal static double LeadingInset(ClosedClockSlot slot,bool idle,bool clockShown,double clock)=>slot==ClosedClockSlot.Leading && !idle && clockShown?Math.Ceiling(clock)+10:0;
    internal static double ContentWidth(double surface,double panel,bool notch)=>Math.Max(120,Math.Min(surface,panel)-(notch?32:12));
    // Clock x inside the header (content minus button padding).
    internal static double Left(ClosedClockSlot slot,double header,double clock)=>slot switch{ClosedClockSlot.Leading=>0,ClosedClockSlot.Center=>(header-clock)/2,_=>Math.Max(0,header-clock)};
    // Clock center measured from the dock's left edge; the content is centered on the surface.
    internal static double ClockCenter(ClosedClockSlot slot,double surface,double panel,bool notch,double padding,double clock)
    {
        double content=ContentWidth(surface,panel,notch);return (surface-content)/2+padding+Left(slot,content-2*padding,clock)+clock/2;
    }
    // Idle date: centered with equal clearance for a side clock; beside a centered clock it moves to the left edge.
    internal static (bool Visible,bool Leading,double MaxWidth) Date(ClosedClockSlot slot,double header,double clock)
    {
        if(slot==ClosedClockSlot.Center){double room=Math.Max(0,(header-clock)/2-12);return (room>=70,true,room);}
        double centered=Math.Max(0,header-2*Math.Max(60,clock+10));return (centered>=80,false,centered);
    }
}
