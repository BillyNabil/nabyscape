using System;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace NabyScape;

// Documented DWMWA_SYSTEMBACKDROP_TYPE (38), Windows 11 build 22621+.
// Only ordinary Settings HWNDs use this. The island remains a layered contour.
internal static class SettingsBackdrop
{
    internal static readonly string[] Choices={"Solid","Simulated glass","Native Mica","Native Acrylic"};
    internal readonly record struct Conditions(bool Supported,bool HighContrast,bool Transparency,bool EnergySaver,bool Remote);
    internal readonly record struct Result(string Requested,string Actual,string Reason,int HResult=0)
    {internal bool Native=>Actual is "Native Mica" or "Native Acrylic";internal string Status=>$"Requested: {Requested}. Actual: {Actual}. {Reason}";}
    internal static string Requested(Settings s)=>Array.IndexOf(Choices,s.SettingsMaterial)>=0?s.SettingsMaterial:"Solid";
    internal static Result Resolve(string request,Conditions conditions,Func<int,int> apply)
    {
        if(Array.IndexOf(Choices,request)<0)request="Solid";
        if(request is "Solid" or "Simulated glass")return new(request,request,"Settings only; island material is independent.");
        string reason=!conditions.Supported?"Requires Windows 11 build 22621 or later.":conditions.HighContrast?"High contrast is enabled.":!conditions.Transparency?"Windows transparency effects are off.":conditions.EnergySaver?"Energy saver is active.":conditions.Remote?"Remote sessions use the fallback.":"";
        if(reason.Length>0)return new(request,"Solid",reason);
        int hr=apply(request=="Native Mica"?2:3);
        return hr<0?new(request,"Solid",$"DWM could not enable the material (0x{hr:X8}).",hr):new(request,request,"DWM enabled; Windows controls the final appearance.",hr);
    }
    internal static Conditions ReadConditions()
    {
        bool transparency=false;
        try{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");transparency=key?.GetValue("EnableTransparency") is int value && value!=0;}catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}
        bool powerKnown=GetSystemPowerStatus(out var power);
        return new(OperatingSystem.IsWindowsVersionAtLeast(10,0,22621),SystemParameters.HighContrast,transparency,!powerKnown || power.SystemStatusFlag!=0,System.Windows.Forms.SystemInformation.TerminalServerSession);
    }
    internal static int Apply(IntPtr hwnd,int type)
    {try{return DwmSetWindowAttribute(hwnd,38,ref type,sizeof(int));}catch(DllNotFoundException){return unchecked((int)0x80004001);}catch(EntryPointNotFoundException){return unchecked((int)0x80004001);}}
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus {public byte ACLineStatus,BatteryFlag,BatteryLifePercent,SystemStatusFlag;public uint BatteryLifeTime,BatteryFullLifeTime;}
}
