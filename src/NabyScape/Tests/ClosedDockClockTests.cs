using System;
using System.Linq;
using System.Reflection;

namespace NabyScape;
// Issue #3: closed-dock clock position (Left / Center / Right) and a discoverable, widenable notch width.
internal static class ClosedDockClockTests
{
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool pass,string reason="Closed dock clock invariant failed"){if(!pass)throw new Exception(reason);}
        static bool Near(double a,double b)=>Math.Abs(a-b)<.01;
        test("Closed clock position normalizes, resets and round-trips with the layout profile",()=>
        {
            Assert(new Settings().ClosedClockPosition=="Automatic" && FactoryDefaults.Create().ClosedClockPosition=="Automatic","Upgrades and new installs keep the original layout");
            foreach(var (input,expected) in new[]{("center","Center"),("Centre","Center"),(" LEFT ","Left"),("right","Right"),("Automatic","Automatic"),("middle","Automatic"),("","Automatic")})
            {var s=new Settings{ClosedClockPosition=input};s.Normalize();Assert(s.ClosedClockPosition==expected,"Normalize "+input);}
            var nulled=new Settings{ClosedClockPosition=null!};nulled.Normalize();Assert(nulled.ClosedClockPosition=="Automatic");
            Assert(PreferenceReset.Categories["Layout"].Contains(nameof(Settings.ClosedClockPosition)) && CustomizationProfile.CustomizationKeys.Contains(nameof(Settings.ClosedClockPosition)));
            var target=new Settings();CustomizationProfile.Import(CustomizationProfile.Export(new Settings{ClosedClockPosition="Center"}),target);Assert(target.ClosedClockPosition=="Center","Layout profile");
        });
        test("Closed clock slots keep Automatic unchanged and let live content own the center",()=>
        {
            Assert(ClosedClockLayout.Slot("Automatic",true)==ClosedClockSlot.Leading && ClosedClockLayout.Slot("Automatic",false)==ClosedClockSlot.Trailing,"Automatic = left when idle, right beside activity");
            Assert(ClosedClockLayout.Column(ClosedClockSlot.Leading)==0 && ClosedClockLayout.Gap(ClosedClockSlot.Leading)==0 && ClosedClockLayout.Column(ClosedClockSlot.Trailing)==4 && ClosedClockLayout.Gap(ClosedClockSlot.Trailing)==12,"Original columns and spacing");
            foreach(bool idle in new[]{true,false})Assert(ClosedClockLayout.Slot("Left",idle)==ClosedClockSlot.Leading && ClosedClockLayout.Slot("Right",idle)==ClosedClockSlot.Trailing,"Left/Right are fixed");
            Assert(ClosedClockLayout.Slot("Center",true)==ClosedClockSlot.Center && ClosedClockLayout.Slot("Center",false)==ClosedClockSlot.Trailing,"Center steps right while music, lyrics, timers or notices use the middle");
            Assert(ClosedClockLayout.LeadingInset(ClosedClockSlot.Leading,false,true,37.2)==48 && ClosedClockLayout.LeadingInset(ClosedClockSlot.Leading,true,true,37.2)==0 && ClosedClockLayout.LeadingInset(ClosedClockSlot.Leading,false,false,37.2)==0 && ClosedClockLayout.LeadingInset(ClosedClockSlot.Trailing,false,true,37.2)==0,"A left clock moves the activity icon aside only while active");
        });
        test("Closed clock layout math centers the time on the dock for notch and pill shapes",()=>
        {
            foreach(var (surface,panel,notch,clock) in new[]{(400d,526d,true,38d),(228d,384d,false,44d),(600d,526d,true,38d),(180d,300d,true,52d)})
            {
                Assert(Near(ClosedClockLayout.ClockCenter(ClosedClockSlot.Center,surface,panel,notch,14,clock),surface/2),$"Center {surface}/{panel}/{notch}");
                double content=ClosedClockLayout.ContentWidth(surface,panel,notch),inset=(surface-content)/2;
                Assert(Near(ClosedClockLayout.ClockCenter(ClosedClockSlot.Leading,surface,panel,notch,14,clock)-clock/2,inset+14),"Left edge");
                Assert(Near(ClosedClockLayout.ClockCenter(ClosedClockSlot.Trailing,surface,panel,notch,14,clock)+clock/2,surface-inset-14),"Right edge");
            }
            Assert(ClosedClockLayout.ContentWidth(400,526,true)==368 && ClosedClockLayout.ContentWidth(600,526,true)==494 && ClosedClockLayout.ContentWidth(150,300,false)==138,"Content width matches the dock button");
            var side=ClosedClockLayout.Date(ClosedClockSlot.Leading,340,38);Assert(side.Visible && !side.Leading && side.MaxWidth==220,"Side clock keeps the centered date");
            var centered=ClosedClockLayout.Date(ClosedClockSlot.Center,340,38);Assert(centered.Visible && centered.Leading && centered.MaxWidth==139 && centered.MaxWidth+12<=ClosedClockLayout.Left(ClosedClockSlot.Center,340,38)+.01,"Centered clock moves the date left without overlap");
            Assert(!ClosedClockLayout.Date(ClosedClockSlot.Center,168,40).Visible && !ClosedClockLayout.Date(ClosedClockSlot.Trailing,180,40).Visible,"Narrow docks hide the date instead of crowding the clock");
        });
        test("Closed width bounds let the default notch grow and Settings search finds notch width and clock position",()=>
        {
            foreach(var (input,expected) in new[]{(900d,600d),(100d,180d),(double.NaN,228d),(double.PositiveInfinity,228d),(450d,450d)})
            {var s=new Settings{Width=input};s.Normalize();Assert(s.Width==expected,"Width "+input);}
            var defaults=FactoryDefaults.Create();Assert(defaults.Width>Settings.ClosedWidthMin && defaults.Width+100<=Settings.ClosedWidthMax,"The 400 px default notch can be widened");
            var build=typeof(SettingsWindow).GetMethod("BuildSettingsSearchIndex",BindingFlags.NonPublic|BindingFlags.Static)!;var index=(SettingsSearchItem[])build.Invoke(null,null)!;
            SettingsSearchItem[] Find(string query)=>index.Where(item=>query.Split(' ').All(t=>(item.Title+" "+item.Keywords).Contains(t,StringComparison.OrdinalIgnoreCase))).ToArray();
            foreach(string query in new[]{"notch width","resize notch","notch size","notch bar"})Assert(Find(query).Any(item=>item.Category=="Layout" && item.Control=="Closed width"),"Search "+query);
            foreach(string query in new[]{"center clock","clock position","jam tengah","notch clock"})Assert(Find(query).Any(item=>item.Section=="Closed island" && item.Control=="Closed dock clock position"),"Search "+query);
            Assert(SettingsWindow.EssentialSetting("Layout","Closed island"),"Closed island is visible without Show all settings");
        });
    }
}
