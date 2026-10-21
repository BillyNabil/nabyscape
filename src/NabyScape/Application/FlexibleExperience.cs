using System;
namespace NabyScape;
public sealed partial class Settings
{
    public bool HoverFollowsWidgets {get;set;}=true;
    public bool FollowMonitorAnimation {get;set;}=true;
    public double FollowMonitorDuration {get;set;}=360;
    public bool ClosedAmbient {get;set;}
    public string DockEdge {get;set;}="Top";
    public double DockVertical {get;set;}=50;
    public double DockInset {get;set;}=8;
    public string AmbientPalette {get;set;}="Cover blend";
    public string AmbientDirection {get;set;}="Bottom";
    public double AmbientHeight {get;set;}=1;
    public double AmbientBalance {get;set;}=.5;
    public double AmbientSoftness {get;set;}=.5;
    public string AmbientSecondaryColor {get;set;}="#CB72D9";
    public string WeatherCity {get;set;}="";
    public double? WeatherLatitude {get;set;}
    public double? WeatherLongitude {get;set;}
    public string WeatherUnits {get;set;}="Celsius";
    private void NormalizeFlexible()
    {
        FollowMonitorDuration=double.IsFinite(FollowMonitorDuration)?Math.Clamp(FollowMonitorDuration,180,700):360;
        if(DockEdge is not ("Top" or "Bottom" or "Left" or "Right" or "Floating"))DockEdge="Top";
        DockVertical=double.IsFinite(DockVertical)?Math.Clamp(DockVertical,0,100):50;
        DockInset=double.IsFinite(DockInset)?Math.Clamp(DockInset,0,200):8;
        if(AmbientPalette is not ("Single color" or "Cover blend"))AmbientPalette="Cover blend";
        if(AmbientDirection is not ("Bottom" or "Top" or "Left" or "Right" or "Surround"))AmbientDirection="Bottom";
        AmbientHeight=double.IsFinite(AmbientHeight)?Math.Clamp(AmbientHeight,.25,2):1;
        AmbientBalance=double.IsFinite(AmbientBalance)?Math.Clamp(AmbientBalance,0,1):.5;
        AmbientSoftness=double.IsFinite(AmbientSoftness)?Math.Clamp(AmbientSoftness,0,1):.5;
        if(!ValidColor(AmbientSecondaryColor))AmbientSecondaryColor="#CB72D9";
        WeatherCity=(WeatherCity??"").Trim();if(WeatherCity.Length>120)WeatherCity=WeatherCity[..120];
        if(WeatherLatitude is not {} lat || WeatherLongitude is not {} lon || !double.IsFinite(lat) || !double.IsFinite(lon) || Math.Abs(lat)>90 || Math.Abs(lon)>180){WeatherLatitude=WeatherLongitude=null;}
        if(WeatherUnits is not ("Celsius" or "Fahrenheit"))WeatherUnits="Celsius";
    }
}
internal readonly record struct AmbientLook(string Direction,double Height,double Balance,double Softness)
{
    internal static AmbientLook From(Settings s)=>new(s.AmbientDirection,s.AmbientHeight,s.AmbientBalance,s.AmbientSoftness);
    internal static AmbientLook Default=>new("Bottom",1,.5,.5);
}
