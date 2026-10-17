using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NabyScape;

public sealed class WidgetSetting
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
public sealed class Shortcut
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string? AppId { get; set; }
}
public sealed class Settings
{
    public string NotificationDelivery {get;set;}="Notch";
    public bool NotifyTimer {get;set;}=true;
    public bool NotifyFocus {get;set;}=true;
    public bool NotifyDownloads {get;set;}=true;
    public bool NotifyUpdates {get;set;}=true;
    public bool NotifyFailures {get;set;}=true;
    public bool DownloadsEnabled {get;set;}
    public string DownloadsFolder {get;set;}="";
    public string Preset { get; set; } = "Mac";
    public double Width { get; set; } = 228;
    public double Height { get; set; } = 32;
    public double Radius { get; set; } = 16;
    public bool CompactLayout { get; set; } = true;
    public bool HoverPeek { get; set; } = true;
    public bool DropReveal { get; set; } = true;
    public bool DetachablePreviews { get; set; } = true;
    public bool CoverGestures { get; set; } = true;
    public bool ActivitySatellite { get; set; } = true;
    public string SatelliteSource { get; set; } = "Auto";
    public bool GlassStyle { get; set; } = true;
    public string SettingsMaterial { get; set; } = "";
    public bool LiveActivity { get; set; } = true;
    public string ClosedMediaIcon { get; set; }="Waveform";
    public string ClosedMediaContent { get; set; }="Title";
    public bool ReactiveFeedback { get; set; } = true;
    public bool SmartActivities { get; set; } = true;
    public bool AlbumAccent { get; set; } = true;
    public bool TwoStageHover { get; set; } = true;
    public double PanelWidth { get; set; }=384;
    public double PanelMaxHeight { get; set; }=408;
    public double PanelPadding { get; set; }=32;
    public double PanelScale { get; set; }=1;
    public double SectionGap { get; set; }=18;
    public double HoverWidth { get; set; }=350;
    public double HoverHeight { get; set; }=192;
    public int HoverDelayMs { get; set; }=180;
    public int HoverDetailDelayMs { get; set; }=550;
    public double SwipeDistance { get; set; }=48;
    public double GlassStrength { get; set; }=1;
    public bool Clock24Hour { get; set; }=true;
    public bool ClockSeconds { get; set; }
    public bool ShowDate { get; set; }=true;
    public bool MonitorNetwork { get; set; }=true;
    public bool MonitorStorage { get; set; }=true;
    public bool MonitorProcesses { get; set; }=true;
    public List<WidgetSetting> HoverViews { get; set; }=new[]{"media","timer","focus","calendar","notes","files","system"}.Select(id=>new WidgetSetting{Id=id,Enabled=id!="system"}).ToList();
    public bool ClipboardHistory { get; set; }
    public bool ActivitiesEnabled {get;set;}=true;
    public List<WidgetSetting> Tools {get;set;}=ToolNames.Keys.Select(id=>new WidgetSetting{Id=id}).ToList();
    public static readonly Dictionary<string,string> ToolNames=new(){["files"]="File shelf",["notes"]="Notes",["tasks"]="Tasks",["clips"]="Clipboard",["calendar"]="Calendar",["awake"]="Keep awake",["links"]="Clean links"};
    public bool ToolEnabled(string id)=>Tools.Any(t=>t.Id==id && t.Enabled);
    public string Performance { get; set; } = "Auto";
    public string WheelAction { get; set; } = "volume";
    public string MiddleAction { get; set; } = "toggle";
    public int VolumeStep { get; set; } = 2;
    public bool HotkeyEnabled { get; set; } = true;
    public string HotkeyKey { get; set; } = "I";
    public string HotkeyModifiers { get; set; } = "Ctrl+Alt";
    public double Horizontal { get; set; } = 50;
    public double Top { get; set; } = 0;
    public string Monitor { get; set; } = "";
    public string DisplayMode {get;set;}="";
    public string Background { get; set; } = "#000000";
    public string Accent { get; set; } = "#FFFFFF";
    public string ControlFill { get; set; } = "#16171B";
    public string ControlBorder { get; set; } = "#303238";
    public string SelectionFill { get; set; } = "#34373E";
    public bool PanelSwipe { get; set; } = true;
    public double Opacity { get; set; } = 1;
    public double AnimationMs { get; set; } = 420;
    public bool ReduceMotion { get; set; }
    public bool HideFullscreen { get; set; } = true;
    public bool HideMaximized { get; set; }
    public bool AlwaysTucked {get;set;}
    public double TuckEdge {get;set;}=6;
    public bool TuckHotkeyEnabled {get;set;}=true;
    public string TuckHotkeyKey {get;set;}="T";
    public string TuckHotkeyModifiers {get;set;}="Ctrl+Alt";
    public bool VisibilityHotkeyEnabled { get; set; }=true;
    public string VisibilityHotkeyKey { get; set; }="H";
    public string VisibilityHotkeyModifiers { get; set; }="Ctrl+Alt";
    public string CategoryStyle { get; set; }="IconsLabels";
    public string LaunchStyle { get; set; }="IconsLabels";
    public bool BoldMediaTitle { get; set; }=true;
    public bool BoldHeadings { get; set; }=true;
    public bool WaveformIdleMotion { get; set; }=true;
    public bool VolumeFeedback { get; set; }=true;
    public bool BrightnessFeedback { get; set; }=true;
    public List<string> RecentColors { get; set; }=new();
    public bool AutoCheckUpdates { get; set; }=true;
    public bool AutoStart { get; set; }
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public List<WidgetSetting> Widgets { get; set; } = Defaults();
    public List<Shortcut> Shortcuts { get; set; } = new()
    {
        new() { Name = "Explorer", Target = "explorer.exe" },
        new() { Name = "Settings", Target = "ms-settings:" }
    };
    public static readonly Dictionary<string,string> Names = new()
    {
        ["media"]="Media", ["volume"]="Volume", ["timer"]="Timer", ["stopwatch"]="Stopwatch", ["focus"]="Focus mode",
        ["clock"]="Clock & date", ["system"]="System monitor", ["battery"]="Battery", ["shortcuts"]="Shortcuts",["tools"]="Everyday tools"
    };
    public static List<WidgetSetting> Defaults() => Names.Keys.Select(id => new WidgetSetting { Id=id,Enabled=id!="stopwatch" }).ToList();
    public void Normalize()
    {
        if(Array.IndexOf(SettingsBackdrop.Choices,SettingsMaterial)<0)SettingsMaterial="";
        if(NotificationDelivery is not ("Notch" or "Windows" or "Both" or "Off"))NotificationDelivery="Notch";
        DownloadsFolder??="";
        static double Clamp(double value,double min,double max,double fallback) => double.IsFinite(value) ? Math.Clamp(value,min,max) : fallback;
        TuckEdge=Clamp(TuckEdge,3,12,6);
        if(TuckHotkeyKey==null || TuckHotkeyKey.Length!=1 || TuckHotkeyKey[0]<'A' || TuckHotkeyKey[0]>'Z')TuckHotkeyKey="T";
        if(TuckHotkeyModifiers is not ("Ctrl+Alt" or "Ctrl+Shift" or "Alt+Shift"))TuckHotkeyModifiers="Ctrl+Alt";
        Width=Clamp(Width,180,400,228); Height=Clamp(Height,28,72,32); Radius=Clamp(Radius,0,40,16);
        Horizontal=Clamp(Horizontal,0,100,50); Top=Clamp(Top,0,150,0); Opacity=Clamp(Opacity,.65,1,1);
        AnimationMs=Clamp(AnimationMs,0,600,420);
        PanelWidth=Clamp(PanelWidth,340,640,384);PanelMaxHeight=Clamp(PanelMaxHeight,320,760,408);PanelPadding=Clamp(PanelPadding,16,40,32);PanelScale=Clamp(PanelScale,.85,1.25,1);SectionGap=Clamp(SectionGap,8,28,18);
        HoverWidth=Clamp(HoverWidth,300,500,350);HoverHeight=Clamp(HoverHeight,184,280,192);HoverDelayMs=Math.Clamp(HoverDelayMs,80,900);HoverDetailDelayMs=Math.Clamp(HoverDetailDelayMs,200,1200);SwipeDistance=Clamp(SwipeDistance,24,100,48);GlassStrength=Clamp(GlassStrength,0,1,1);
        var hoverIds=new[]{"media","timer","focus","calendar","notes","files","system","clips","tasks","stopwatch"};HoverViews=(HoverViews??new()).Where(v=>v!=null && hoverIds.Contains(v.Id)).DistinctBy(v=>v.Id).ToList();foreach(var id in hoverIds)if(!HoverViews.Any(v=>v.Id==id))HoverViews.Add(new(){Id=id,Enabled=id is not ("system" or "clips" or "tasks" or "stopwatch")});
        Tools=(Tools??new()).Where(t=>t!=null && ToolNames.ContainsKey(t.Id)).DistinctBy(t=>t.Id).ToList();foreach(var id in ToolNames.Keys)if(!Tools.Any(t=>t.Id==id))Tools.Add(new(){Id=id});
        if(Performance is not ("Auto" or "Hemat" or "Maksimal"))Performance="Auto";
        if(WheelAction is not ("volume" or "pages" or "none"))WheelAction="volume";
        if(MiddleAction is not ("toggle" or "microphone" or "none"))MiddleAction="toggle";
        VolumeStep=Math.Clamp(VolumeStep,1,10);
        if(HotkeyKey==null || HotkeyKey.Length!=1 || HotkeyKey[0]<'A' || HotkeyKey[0]>'Z')HotkeyKey="I";
        if(HotkeyModifiers is not ("Ctrl+Alt" or "Ctrl+Shift" or "Alt+Shift"))HotkeyModifiers="Ctrl+Alt";
        if(VisibilityHotkeyKey==null || VisibilityHotkeyKey.Length!=1 || VisibilityHotkeyKey[0]<'A' || VisibilityHotkeyKey[0]>'Z')VisibilityHotkeyKey="H";
        if(VisibilityHotkeyModifiers is not ("Ctrl+Alt" or "Ctrl+Shift" or "Alt+Shift"))VisibilityHotkeyModifiers="Ctrl+Alt";
        if(SatelliteSource is not ("Auto" or "Countdown" or "CPU" or "RAM" or "Calendar" or "Clock"))SatelliteSource="Auto";
        if(ClosedMediaContent is not ("Title" or "Waveform"))ClosedMediaContent="Title";
        if(ClosedMediaIcon is not ("Waveform" or "Artwork"))ClosedMediaIcon="Waveform";
        if(CategoryStyle is not ("IconsLabels" or "Icons" or "Text"))CategoryStyle="IconsLabels";
        if(LaunchStyle is not ("IconsLabels" or "Icons" or "Text"))LaunchStyle="IconsLabels";
        RecentColors=(RecentColors??new()).Where(ValidColor).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        FocusMinutes=Math.Clamp(FocusMinutes,1,240); BreakMinutes=Math.Clamp(BreakMinutes,1,120);
        if(Preset!="Mac" && Preset!="iPhone") Preset="iPhone";
        if(!ValidColor(Background)) Background="#000000";
        if(!ValidColor(Accent)) Accent="#FFFFFF";
        if(!ValidColor(ControlFill))ControlFill="#16171B";
        if(!ValidColor(ControlBorder))ControlBorder="#303238";
        if(!ValidColor(SelectionFill))SelectionFill="#34373E";
        Widgets=(Widgets ?? new()).Where(w=>w!=null && Names.ContainsKey(w.Id)).DistinctBy(w=>w.Id).ToList();
        foreach(var id in Names.Keys) if(!Widgets.Any(w=>w.Id==id)) Widgets.Add(new(){Id=id,Enabled=id!="stopwatch"});
        Shortcuts=(Shortcuts ?? new()).Where(s=>s!=null && !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Target)).Take(40).ToList();
        Monitor ??= "";
        if(DisplayMode is not ("Primary" or "Selected" or "Follow"))DisplayMode=Monitor.Length>0?"Selected":"Primary";
    }
    public static bool ValidColor(string? value) => value!=null && System.Text.RegularExpressions.Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$");
}

public sealed class Countdown
{
    [JsonIgnore] public Guid RunId { get; private set; }
    public DateTimeOffset? Deadline { get; set; }
    public double RemainingSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public string Phase { get; set; } = "";
    [JsonIgnore] public bool Running => Deadline.HasValue;
    public double Remaining(DateTimeOffset now) => Math.Max(0,Deadline.HasValue ? (Deadline.Value-now).TotalSeconds : RemainingSeconds);
    public void Start(double seconds,DateTimeOffset now,string phase="",bool preserveRunId=false)
    { if(!preserveRunId)RunId=Guid.NewGuid();DurationSeconds=RemainingSeconds=Math.Max(1,seconds); Deadline=now.AddSeconds(RemainingSeconds); Phase=phase; }
    public double Progress(DateTimeOffset now) => Math.Clamp(1-Remaining(now)/Math.Max(1,DurationSeconds>0?DurationSeconds:RemainingSeconds),0,1);
    public void Pause(DateTimeOffset now) { RemainingSeconds=Remaining(now); Deadline=null; }
    public void Resume(DateTimeOffset now) { if(RemainingSeconds>0) Deadline=now.AddSeconds(RemainingSeconds); }
    public void Reset() { RunId=Guid.NewGuid();Deadline=null; RemainingSeconds=DurationSeconds=0; Phase=""; }
    public bool Complete(DateTimeOffset now)
    { if(!Deadline.HasValue || now<Deadline.Value) return false; Deadline=null; RemainingSeconds=0; return true; }
    public string Display(DateTimeOffset now)
    =>FormatSeconds(Remaining(now));
    internal static string FormatSeconds(double seconds)
    { var span=TimeSpan.FromSeconds(Math.Ceiling(seconds)); return span.TotalHours>=1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"mm\:ss"); }
}
public sealed class SessionState
{
    public StopwatchState Stopwatch {get;set;}=new();
    public Countdown Timer { get; set; } = new();
    public Countdown Focus { get; set; } = new();
}
public sealed partial class Store
{
    public string DirectoryPath { get; }
    public string? Warning { get; private set; }
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    public Store(string? path=null) : this(path,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) { }
    private Store(string? path,string root)
    {
        string? warning=null;
        DirectoryPath=path!=null?Path.GetFullPath(path):ResolveMigration(Path.Combine(root,"LumaIsland"),Path.Combine(root,"NabyScape"),out warning);
        if(path==null)Warning=warning;
        Directory.CreateDirectory(DirectoryPath);
    }
    // Explicit migration entry for isolated roots; Store(path) only opens that path.
    internal static Store Migrate(string legacyRoot,string newRoot)
    {
        string path=ResolveMigration(legacyRoot,newRoot,out var warning);
        return new Store(path){Warning=warning};
    }
    public T Load<T>(string name) where T:new()
    {
        var file=Path.Combine(DirectoryPath,name+".json");
        if(!File.Exists(file)) return new();
        try
        {
            using var document=JsonDocument.Parse(File.ReadAllText(file));
            var value=document.RootElement.Deserialize<T>(Options) ?? new();
            if(value is Settings settings && !settings.CompactLayout)
            {
                // Older classic profiles predate the configurable panel dimensions.
                if(!document.RootElement.TryGetProperty(nameof(Settings.PanelWidth),out _)) settings.PanelWidth=452;
                if(!document.RootElement.TryGetProperty(nameof(Settings.PanelMaxHeight),out _)) settings.PanelMaxHeight=634;
            }
            return value;
        }
        catch(Exception ex) when(ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Warning="Settings could not be read. Defaults were loaded and the original file was preserved.";
            try { File.Copy(file,file+".backup-"+DateTime.Now.ToString("yyyyMMddHHmmss"),true); } catch { }
            return new();
        }
    }
    public void Save<T>(string name,T value)
    {
        var file=Path.Combine(DirectoryPath,name+".json");
        File.WriteAllText(file+".tmp",JsonSerializer.Serialize(value,Options));
        File.Move(file+".tmp",file,true);
    }
    public void Log(Exception ex)
    { try { File.AppendAllText(Path.Combine(DirectoryPath,"errors.log"),DateTimeOffset.Now+" "+ex+Environment.NewLine); } catch { } }
}
