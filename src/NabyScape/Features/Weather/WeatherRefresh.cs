using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
namespace NabyScape;

// App-level weather schedule: refreshes independently of any clock widget or open page, but only
// while weather is in use. Visible surfaces refresh after 20 minutes, an enabled widget or hover
// page after 30 minutes while you are at the PC, failures back off, and resume/network regain retry.
public sealed partial class App
{
    private DateTimeOffset weatherCheckedAt;
    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo{public uint Size;public uint Time;}
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    // Feature usage decides: the weather widget/tool or its hover page. A city is required.
    internal static bool WeatherInUse(Settings s)=>WeatherService.Key(s).Length>0 && (s.ToolEnabled("weather") || s.HoverPeek && HoverPagePolicy.Pages(s).Contains("weather"));
    internal WeatherDemand WeatherNeed=>Weather.AnyVisible?WeatherDemand.Visible:WeatherInUse(Settings) && overlay?.UserHidden!=true?WeatherDemand.Background:WeatherDemand.None;
    // No keyboard or mouse input for 30 minutes: background refreshes wait until you are back.
    private static bool UserAway(){var info=new LastInputInfo{Size=(uint)Marshal.SizeOf<LastInputInfo>()};return GetLastInputInfo(ref info) && unchecked((uint)Environment.TickCount-info.Time)>30u*60*1000;}
    private void WeatherTick()
    {
        if(diagnosticMode || Weather==null)return;var now=DateTimeOffset.UtcNow;
        if(now>=weatherCheckedAt && now-weatherCheckedAt<TimeSpan.FromSeconds(15))return;weatherCheckedAt=now;
        try{Weather.Tick(Settings,WeatherNeed,now,UserAway(),NetworkInterface.GetIsNetworkAvailable);}catch(Exception ex){Store.Log(ex);}
    }
}
