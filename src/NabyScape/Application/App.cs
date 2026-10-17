using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Media;
using Forms=System.Windows.Forms;

namespace NabyScape;

public sealed class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? openEvent,exitEvent;
    private RegisteredWaitHandle? openWait,exitWait;
    private Forms.NotifyIcon? tray;
    private DispatcherTimer? tick;
    private SettingsWindow? settingsWindow;
    private OverlayWindow? overlay;
    private bool quitting;
    internal InternalNotifications Notifications {get;}=new();
    internal DownloadArrivals? Downloads {get;private set;}
    private bool diagnosticMode;
    private string downloadConfiguration="";
    private int downloadEpoch;
    private void DownloadReady(string[] paths)
    {
        int epoch=downloadEpoch;Dispatcher.BeginInvoke(()=>
        {
            if(epoch!=downloadEpoch || !Settings.DownloadsEnabled || Downloads?.Running!=true || paths.Length==0 || !string.Equals(Path.GetDirectoryName(paths[0]),Downloads.Folder,StringComparison.OrdinalIgnoreCase))return;
            PostNotice(new("downloads",paths.Length==1?"Download ready":$"{paths.Length} downloads ready","Folder arrival inferred · drag originals or add to shelf",paths));
        });
    }
    private void DownloadFailed(string error)
    {
        int epoch=downloadEpoch;Dispatcher.BeginInvoke(()=>
        {
            if(epoch==downloadEpoch && Settings.DownloadsEnabled)Notify("Downloads unavailable",error);
        });
    }
    internal void OpenNotifications()=>overlay?.OpenNotice(true);
    private void ConfigureDownloads()
    {
        if(diagnosticMode)return;
        string key=Settings.DownloadsEnabled+"|"+Settings.DownloadsFolder;if(key==downloadConfiguration)return;downloadConfiguration=key;
        Downloads?.Configure(false,"");downloadEpoch++;
        try{Downloads?.Configure(Settings.DownloadsEnabled,Settings.DownloadsEnabled?(Settings.DownloadsFolder.Length==0?DownloadArrivals.KnownFolder():Settings.DownloadsFolder):"");}
        catch(Exception ex){Notify("Downloads unavailable",ex.Message);}
    }
    internal void PostNotice(InternalNotice notice)
    {
        if(diagnosticMode && !Array.Exists(Environment.GetCommandLineArgs(),a=>a=="--stage4-only"))return;
        Notifications.Post(Settings,notice,overlay?.ManuallyHidden==true,n=>tray?.ShowBalloonTip(4500,n.Title,n.Detail,Forms.ToolTipIcon.Info),DateTimeOffset.UtcNow);
    }
    public Store Store { get; private set; }=null!;
    internal OfficialUpdates Updates {get;private set;}=null!;
    public Settings Settings { get; private set; }=null!;
    public SessionState Session { get; private set; }=null!;
    public MediaService Media { get; private set; }=null!;
    public BrightnessService Brightness { get; private set; }=null!;
    public string BrightnessStatus=>Brightness?.Status??"Brightness feedback unavailable";
    public AudioService Audio { get; private set; }=null!;
    public ProductivityService Productivity {get;private set;}=null!;
    internal ActivityActionsService Activities {get;private set;}=null!;
    internal DetachedPreviewManager Previews {get;private set;}=null!;
    public CalendarService Calendar {get;private set;}=null!;
    public KeepAwakeService Awake {get;private set;}=null!;
    public string HotkeyStatus=>!Settings.HotkeyEnabled?"Hotkey off":overlay?.HotkeyError.Length>0?overlay.HotkeyError:$"Active · {Settings.HotkeyModifiers}+{Settings.HotkeyKey}";
    public string VisibilityHotkeyStatus=>!Settings.VisibilityHotkeyEnabled?"Visibility shortcut off":overlay?.VisibilityHotkeyError.Length>0?overlay.VisibilityHotkeyError:$"Hide / show · {Settings.VisibilityHotkeyModifiers}+{Settings.VisibilityHotkeyKey}";
    public string TuckHotkeyStatus=>!Settings.TuckHotkeyEnabled?"Tuck shortcut off":overlay?.TuckHotkeyError.Length>0?overlay.TuckHotkeyError:$"Tuck / return to automatic · {Settings.TuckHotkeyModifiers}+{Settings.TuckHotkeyKey}";
    internal void FlushPreferenceEditors()=>overlay?.FlushPreferenceEditors();
    // Countdown completions route through the shared notification manager.
    public event Action? SettingsChanged;
    public event Action? Updated;
    internal RuntimeChange CurrentUpdate {get;private set;}=RuntimeChange.All;
    internal void PublishUpdate(RuntimeChange change){var previous=CurrentUpdate;CurrentUpdate=change;try{Updated?.Invoke();}finally{CurrentUpdate=previous;}}
    public (double cpu,double used,double total) Usage { get; private set; }
    public static new App Current=>(App)Application.Current;
    [STAThread] public static void Main(string[] args)
    {
        var culture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture=culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture=culture;
        if(Array.Exists(args,a=>a=="--update-handoff")){Environment.ExitCode=UpdateHandoff.Run();return;}
        if(WindowFixture.TryRun(args))return;
        if(Array.Exists(args,a=>a=="--exit")) {try {using var signal=EventWaitHandle.OpenExisting("Local\\LumaIsland.Exit");signal.Set();}catch(WaitHandleCannotBeOpenedException){}return;}
        if(Array.Exists(args,a=>a=="--self-test")) { SelfTests.Run(args);return; }
        if(Array.Exists(args,a=>a=="--install") || Path.GetFileNameWithoutExtension(Environment.ProcessPath!).EndsWith("-Setup",StringComparison.OrdinalIgnoreCase)) { Installer.Run(args);return; }
        new App().Run();
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex=new Mutex(true,"Local\\LumaIsland.Desktop",out var created);
        if(!created) {try {using var signal=EventWaitHandle.OpenExisting("Local\\LumaIsland.OpenSettings");signal.Set();}catch(WaitHandleCannotBeOpenedException){}Shutdown();return; }
        openEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\LumaIsland.OpenSettings");
        exitEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\LumaIsland.Exit");
        openWait=ThreadPool.RegisterWaitForSingleObject(openEvent,(_,_)=>Dispatcher.BeginInvoke(ShowSettings),null,Timeout.Infinite,false);
        exitWait=ThreadPool.RegisterWaitForSingleObject(exitEvent,(_,_)=>Dispatcher.BeginInvoke(Quit),null,Timeout.Infinite,false);
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/NabyScape;component/Theme.xaml",UriKind.Relative)});
        var diagnosticIndex=Array.IndexOf(e.Args,"--diagnostics");
        var diagnosticPath=diagnosticIndex>=0 && diagnosticIndex+1<e.Args.Length ? e.Args[diagnosticIndex+1] : null;
        diagnosticMode=diagnosticPath!=null;
        Store=new(diagnosticPath==null ? null : Path.Combine(diagnosticPath,"test-profile"));Settings=diagnosticPath==null?Store.Load<Settings>("settings"):new Settings();Settings.Normalize();Settings.AutoStart=StartupService.Enabled();
        Updates=new(Store,Array.Exists(e.Args,a=>a=="--stage3-only")?new Stage3Diagnostics.Network():null);
        UI.ResourceBrush("Accent",UI.Brush(Settings.Accent));UI.ApplyControlTheme(Settings);
        Motion.Configure(Settings);
        Session=diagnosticPath==null?Store.Load<SessionState>("session"):new SessionState();Session.Timer??=new();Session.Focus??=new();Session.Stopwatch??=new();Session.Stopwatch.Recover();
        DispatcherUnhandledException+=(_,args)=> { Store.Log(args.Exception);args.Handled=true;Notify("Something went wrong", "The operation failed. Details were saved to errors.log."); };
        Audio=new(Store.Log);Media=new(Store.Log);Productivity=new(Store,Store.Log,diagnosticPath==null);Productivity.SetClipboardEnabled(Settings.ClipboardHistory);
        Brightness=new();Brightness.Start(diagnosticPath==null);
        Calendar=new(Store);Awake=new();Activities=new(Store,Session.Focus,Awake,SaveSession,Store.Log);
        tray=new Forms.NotifyIcon{Text="NabyScape",Visible=diagnosticPath==null};
        using(var stream=GetResourceStream(new Uri("pack://application:,,,/Assets/island.ico")).Stream) tray.Icon=new System.Drawing.Icon(stream);
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("Open island",null,(_,_)=>Dispatcher.Invoke(()=>overlay?.Reveal()));
        menu.Items.Add("Command bar",null,(_,_)=>Dispatcher.Invoke(()=>overlay?.ShowCommands()));
        menu.Items.Add("Settings",null,(_,_)=>Dispatcher.Invoke(ShowSettings));
        menu.Items.Add("Tuck / Return to automatic",null,(_,_)=>Dispatcher.Invoke(()=>overlay?.ToggleTuck()));
        menu.Items.Add("Hide / show",null,(_,_)=>Dispatcher.Invoke(()=>overlay?.ToggleVisibility()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit",null,(_,_)=>Dispatcher.Invoke(Quit));
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowSettings);
        overlay=new OverlayWindow(this);MainWindow=overlay;StartupTrace.Attach(overlay,e.Args,Store.Log);overlay.Show();Previews=new(this,overlay.OpenFeaturePage);
        Notifications.Changed+=()=>overlay.RefreshNotice();
        Updates.UpdateAvailable+=release=>Dispatcher.BeginInvoke(()=>PostNotice(new("updates","Update available",release.Version+" · Open Settings > Application to review.")));
        Downloads=new(DownloadReady,DownloadFailed);
        ConfigureDownloads();
        Media.Changed+=()=>PublishUpdate(RuntimeChange.Media);
        Audio.Changed+=()=>PublishUpdate(RuntimeChange.Audio);
        Productivity.Changed+=()=>PublishUpdate(RuntimeChange.Productivity);Calendar.Changed+=()=>PublishUpdate(RuntimeChange.Calendar);
        if(Array.Exists(e.Args,a=>a is "--performance-benchmark" or "--performance-checks-only" or "--stage1-only" or "--stage2-only" or "--stage3-only" or "--stage4-only" or "--stage5-only" or "--stage6-only" or "--integration-settings-only") || diagnosticPath!=null && Array.Exists(e.Args,a=>a=="--showcase"))Media.DiagnosticFixture=true;else _=Media.Initialize();
        Audio.Read();Usage=Native.SystemUsage();
        int counter=0;
        tick=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        tick.Tick+=(_,_)=>
        {
            CheckCountdowns();Notifications.Tick(DateTimeOffset.UtcNow,overlay.NotificationBusy);
            try{Awake.Tick(DateTimeOffset.UtcNow);}catch(Exception ex){Store.Log(ex);Notify("Could not update keep awake",ex.Message);}
            var changed=RuntimeChange.Clock;
            if(Session.Timer.Running || Session.Focus.Running || Session.Stopwatch.Running)changed|=RuntimeChange.Countdown;
            if(++counter%2==0)
            {
                Audio.Poll();bool system=overlay.SystemSummaryVisible || Previews.AnyVisible("system") || settingsWindow?.NeedsSystemSample==true;
                if(system || overlay.NeedsAdaptiveCpuSample)Usage=Native.SystemUsage();
                if(system)changed|=RuntimeChange.System;
                overlay.CheckEnvironment();
            }
            PublishUpdate(changed);
        };
        tick.Start();CheckCountdowns();
        if(Store.Warning!=null) Notify("Settings recovered",Store.Warning);
        if(!Array.Exists(e.Args,a=>a=="--background")) Notify("NabyScape is ready", "Click the island for your widgets. Right-click the tray icon for settings.");
        Microsoft.Win32.SystemEvents.PowerModeChanged+=PowerChanged;
        if(diagnosticPath==null) _=Updates.Check(false,Settings.AutoCheckUpdates);
        if(diagnosticPath!=null) _=Diagnostics.Run(this,overlay,diagnosticPath);
        else if(Array.Exists(e.Args,a=>a=="--settings"))ShowSettings();
    }
    private void PowerChanged(object sender,Microsoft.Win32.PowerModeChangedEventArgs e)
    { if(e.Mode==Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(()=> { CheckCountdowns();Audio.Read();Awake?.Tick(DateTimeOffset.UtcNow);overlay?.CheckEnvironment();HardwareMonitor.ResetSampling();PublishUpdate(RuntimeChange.All); }); }
    public void CheckCountdowns()
    {
        var now=DateTimeOffset.UtcNow;bool changed=false;
        if(Session.Timer.Complete(now)) { changed=true;PostNotice(new("timer","Timer finished","Your timer has finished."));SystemSounds.Asterisk.Play(); }
        var focusDeadline=Session.Focus.Deadline;
        if(Session.Focus.Complete(now))
        {
            changed=true;

            if(Session.Focus.Phase=="work")
            {
                // The break starts at the actual work deadline, even across sleep, so a long sleep never starts a fresh break.
                var end=focusDeadline!.Value.AddMinutes(Settings.BreakMinutes);
                if(end>now) { Session.Focus.Start((end-now).TotalSeconds,now,"break",preserveRunId:true);Session.Focus.DurationSeconds=Settings.BreakMinutes*60; PostNotice(new("focus","Focus complete","Time for a short break.")); }
                else { Session.Focus.Reset();PostNotice(new("focus","Focus session complete","Your focus session and break have finished.")); }
            }
            else { Session.Focus.Reset();PostNotice(new("focus","Break finished","Ready for your next focus session.")); }
            SystemSounds.Asterisk.Play();
        }
        if(changed) SaveSession();
    }
    internal void OpenMixer()=>overlay?.OpenMixer();
    public void SaveSession() { try { Session.Stopwatch.Checkpoint();Store.Save("session",Session); } catch(Exception ex) { Store.Log(ex);Notify("Not saved yet", "Could not save the timer state."); } PublishUpdate(RuntimeChange.Countdown); }
    public void ChangeSettings()
    {
        Settings.Normalize();if(!Settings.ActivitiesEnabled)Activities?.Stop();UI.ResourceBrush("Accent",UI.Brush(Settings.Accent));UI.ApplyControlTheme(Settings);Motion.Configure(Settings);
        Productivity?.SetClipboardEnabled(Settings.ClipboardHistory);
        try { Store.Save("settings",Settings); } catch(Exception ex) { Store.Log(ex);Notify("Not saved yet","These settings will apply until you close the app."); }
        ConfigureDownloads();if(Settings.NotificationDelivery is "Off" or "Windows")Notifications.Clear();
        Previews?.ApplySettings();SettingsChanged?.Invoke();PublishUpdate(RuntimeChange.Settings);
    }
    public void ShowSettings()
    {
        overlay?.Collapse();
        if(settingsWindow==null) { settingsWindow=new SettingsWindow(this);settingsWindow.Closed+=(_,_)=>settingsWindow=null; }
        settingsWindow.Show();settingsWindow.Activate();
    }
    public void Notify(string title,string text)
    { PostNotice(new("failure",title,text)); }
    public void Quit()
    { quitting=true;Shutdown(); }
    protected override void OnExit(ExitEventArgs e)
    {
        tick?.Stop();Downloads?.Dispose();Updates?.Dispose();
        Microsoft.Win32.SystemEvents.PowerModeChanged-=PowerChanged;
        if(Session!=null) SaveSession();
        Previews?.Dispose();Activities?.Dispose();overlay?.Cleanup();try{Awake?.Dispose();}catch(Exception ex){Store?.Log(ex);}Productivity?.Dispose();Media?.Dispose();Audio?.Dispose();Brightness?.Dispose();
        if(tray!=null) { tray.Visible=false;tray.Dispose(); }
        openWait?.Unregister(null);exitWait?.Unregister(null);openEvent?.Dispose();exitEvent?.Dispose();mutex?.Dispose();base.OnExit(e);
    }
    public bool Quitting=>quitting;
}
