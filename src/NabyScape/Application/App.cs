using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Media;
using Forms=System.Windows.Forms;

namespace NabyScape;

public sealed partial class App : Application
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
    internal LyricsService Lyrics {get;}=new();
    internal WeatherService Weather {get;private set;}=null!;
    internal CountdownAudio CountdownAudio {get;}=new();
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
    internal AmbientAudioDriver AmbientAudio {get;private set;}=null!;
    public ProductivityService Productivity {get;private set;}=null!;
    internal DetachedPreviewManager Previews {get;private set;}=null!;
    public CalendarService Calendar {get;private set;}=null!;
    public KeepAwakeService Awake {get;private set;}=null!;
    public string HotkeyStatus=>!Settings.HotkeyEnabled?"Hotkey off":overlay?.HotkeyError.Length>0?overlay.HotkeyError:$"Active · {Settings.HotkeyModifiers}+{Settings.HotkeyKey}";
    public string VisibilityHotkeyStatus=>!Settings.VisibilityHotkeyEnabled?"Visibility shortcut off":overlay?.VisibilityHotkeyError.Length>0?overlay.VisibilityHotkeyError:$"Hide / show · {Settings.VisibilityHotkeyModifiers}+{Settings.VisibilityHotkeyKey}";
    public string TuckHotkeyStatus=>!Settings.TuckHotkeyEnabled?"Tuck shortcut off":overlay?.TuckHotkeyError.Length>0?overlay.TuckHotkeyError:$"Tuck / return to automatic · {Settings.TuckHotkeyModifiers}+{Settings.TuckHotkeyKey}";
    internal void FlushPreferenceEditors()=>overlay?.FlushPreferenceEditors();
    // Countdown completions route through the shared notification manager.
    internal void OpenVoice()=>overlay?.OpenFeaturePage("voice");
    public event Action? SettingsChanged;
    public event Action? Updated;
    internal RuntimeChange CurrentUpdate {get;private set;}=RuntimeChange.All;
    internal void PublishUpdate(RuntimeChange change){var previous=CurrentUpdate;CurrentUpdate=change;try{Updated?.Invoke();}finally{CurrentUpdate=previous;}}
    public (double cpu,double used,double total) Usage { get; private set; }
    public static new App Current=>(App)Application.Current;
    [STAThread] public static void Main(string[] args)
    {
        if(args.Length==2 && args[0]=="--speech-worker"){AssistantSpeechWorker.Run(args[1]).GetAwaiter().GetResult();return;}
        if(Array.Exists(args,a=>a=="--widget-mcp")){AssistantWidgetBridge.RunStdio().GetAwaiter().GetResult();return;}
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
        var startupClock=System.Diagnostics.Stopwatch.StartNew();
        PrivateDiagnostics.SetServiceState(DiagnosticService.Application,DiagnosticServiceState.Starting);
        base.OnStartup(e);
        bool assistantFixture=Array.Exists(e.Args,a=>a=="--assistant-only") && Array.Exists(e.Args,a=>a=="--diagnostics");
        bool showcaseFixture=Array.Exists(e.Args,a=>a=="--showcase") && Array.Exists(e.Args,a=>a=="--diagnostics");
        bool isolatedFixture=Array.Exists(e.Args,a=>a=="--isolated-diagnostics") && Array.IndexOf(e.Args,"--diagnostics") is int diagnosticAt && diagnosticAt>=0 && diagnosticAt+1<e.Args.Length;
        string instance=isolatedFixture?"Local\\LumaIsland.IsolatedDiagnostics":showcaseFixture?"Local\\LumaIsland.Showcase":assistantFixture?"Local\\LumaIsland.AssistantDiagnostics":"Local\\LumaIsland";
        mutex=new Mutex(true,instance+".Desktop",out var created);
        if(!created) {try {using var signal=EventWaitHandle.OpenExisting(instance+".OpenSettings");signal.Set();}catch(WaitHandleCannotBeOpenedException){}Shutdown();return; }
        openEvent=new EventWaitHandle(false,EventResetMode.AutoReset,instance+".OpenSettings");
        exitEvent=new EventWaitHandle(false,EventResetMode.AutoReset,instance+".Exit");
        openWait=ThreadPool.RegisterWaitForSingleObject(openEvent,(_,_)=>Dispatcher.BeginInvoke(ShowSettings),null,Timeout.Infinite,false);
        exitWait=ThreadPool.RegisterWaitForSingleObject(exitEvent,(_,_)=>Dispatcher.BeginInvoke(Quit),null,Timeout.Infinite,false);
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/NabyScape;component/Theme.xaml",UriKind.Relative)});
        var diagnosticIndex=Array.IndexOf(e.Args,"--diagnostics");
        var diagnosticPath=diagnosticIndex>=0 && diagnosticIndex+1<e.Args.Length ? e.Args[diagnosticIndex+1] : null;
        diagnosticMode=diagnosticPath!=null;
        Store=new(diagnosticPath==null ? null : Path.Combine(diagnosticPath,"test-profile"));bool existingProfile=File.Exists(Path.Combine(Store.DirectoryPath,"settings.json"));Settings=diagnosticPath==null?Store.Load<Settings>("settings"):new Settings();Settings.Normalize();Localization.Configure(Settings);Settings.AutoStart=StartupService.Enabled();
        if(assistantFixture || isolatedFixture){Settings.HotkeyEnabled=Settings.VisibilityHotkeyEnabled=Settings.TuckHotkeyEnabled=false;Settings.HideFullscreen=Settings.HideMaximized=false;}
        Updates=new(Store,Array.Exists(e.Args,a=>a=="--stage3-only")?new Stage3Diagnostics.Network():null,stagingDirectory:diagnosticPath!=null?Path.Combine(Store.DirectoryPath,"updates"):null);
        UI.ResourceBrush("Accent",UI.Brush(Settings.Accent));UI.ApplyControlTheme(Settings);
        Motion.Configure(Settings);
        Session=diagnosticPath==null?Store.Load<SessionState>("session"):new SessionState();Session.Timer??=new();Session.Focus??=new();Session.Stopwatch??=new();Session.Stopwatch.Recover();
        DispatcherUnhandledException+=(_,args)=> { Store.Log(args.Exception);args.Handled=true;Notify("Something went wrong", "The operation failed. Details were saved to errors.log."); };
        Audio=new(Store.Log){SpectrumEnabled=Settings.WaveformSource=="Spectrum"};Media=new(Store.Log);Productivity=new(Store,Store.Log,diagnosticPath==null);Productivity.SetClipboardEnabled(Settings.ClipboardHistory);
        AmbientAudio=new(this);Weather=new(Store);
        Brightness=new();Brightness.Start(diagnosticPath==null && Settings.ReactiveFeedback && Settings.BrightnessFeedback);
        Calendar=new(Store);Awake=new();
        Assistant=new(this,diagnosticPath!=null?new AssistantMemoryKeys():null);
        tray=new Forms.NotifyIcon{Text="NabyScape",Visible=diagnosticPath==null};
        using(var stream=GetResourceStream(new Uri("pack://application:,,,/Assets/island.ico")).Stream) tray.Icon=new System.Drawing.Icon(stream);
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add(Localization.Text("Open island"),null,(_,_)=>Dispatcher.Invoke(()=>overlay?.Reveal()));
        menu.Items.Add(Localization.Text("Command bar"),null,(_,_)=>Dispatcher.Invoke(()=>overlay?.ShowCommands()));
        menu.Items.Add(Localization.Text("Settings"),null,(_,_)=>Dispatcher.Invoke(ShowSettings));
        menu.Items.Add(Localization.Text("Move & resize island"),null,(_,_)=>Dispatcher.Invoke(()=>overlay?.ToggleDockEditing()));
        menu.Items.Add(Localization.Text("AI companion"),null,(_,_)=>Dispatcher.Invoke(ShowAssistant));
        var wakeMenu=menu.Items.Add(Localization.Text("Resume wake word"),null,(_,_)=>Dispatcher.Invoke(()=>Assistant.ToggleWakeListening()));
        menu.Items.Add(Localization.Text("Stop all microphones"),null,(_,_)=>Dispatcher.Invoke(()=>Assistant.StopSession()));
        menu.Opening+=(_,_)=>{wakeMenu.Text=Localization.Text(Assistant.WakeArmed?"Pause wake word":"Resume wake word");wakeMenu.Enabled=Assistant.Options.Enabled && Assistant.Options.WakeWord.Enabled;};
        menu.Items.Add(Localization.Text("Tuck / Return to automatic"),null,(_,_)=>Dispatcher.Invoke(()=>overlay?.ToggleTuck()));
        menu.Items.Add(Localization.Text("Hide / show"),null,(_,_)=>Dispatcher.Invoke(()=>overlay?.ToggleVisibility()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Localization.Text("Quit"),null,(_,_)=>Dispatcher.Invoke(Quit));
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowSettings);
        overlay=new OverlayWindow(this);MainWindow=overlay;StartupTrace.Attach(overlay,e.Args,Store.Log);overlay.Show();Previews=new(this,overlay.OpenFeaturePage);
        Assistant.Changed+=RefreshAssistantTray;if(diagnosticPath==null)Assistant.StartBackgroundFeatures();
        Notifications.Changed+=()=>overlay.RefreshNotice();
        Updates.UpdateAvailable+=release=>Dispatcher.BeginInvoke(()=>PostNotice(new("updates","Update available",release.Version+" · Open Settings > Application to review.")));
        Updates.DownloadReady+=()=>Dispatcher.BeginInvoke(()=>PostNotice(new("updates",Localization.Text("Update ready to install"),Localization.Text("Open Settings > Application to install and restart when ready."))));
        Downloads=new(DownloadReady,DownloadFailed);
        ConfigureDownloads();
        Media.Changed+=()=>PublishUpdate(RuntimeChange.Media);
        Audio.Changed+=()=>{PrivateDiagnostics.SetServiceState(DiagnosticService.Audio,Audio.Available?DiagnosticServiceState.Ready:DiagnosticServiceState.Degraded);PublishUpdate(RuntimeChange.Audio);};
        Productivity.Changed+=()=>PublishUpdate(RuntimeChange.Productivity);Calendar.Changed+=()=>PublishUpdate(RuntimeChange.Calendar);
        if(assistantFixture || isolatedFixture || Array.Exists(e.Args,a=>a is "--performance-benchmark" or "--performance-checks-only" or "--stage1-only" or "--stage2-only" or "--stage3-only" or "--stage4-only" or "--stage5-only" or "--stage6-only" or "--integration-settings-only" or "--interaction-hotfix-only" or "--interaction-tuck-hide-only" or "--interaction-classic-mixer-only") || diagnosticPath!=null && Array.Exists(e.Args,a=>a=="--showcase"))Media.DiagnosticFixture=true;else Media.SetEnabled(MediaFeaturesWanted);
        Audio.Read();Usage=Native.SystemUsage();
        int counter=0;
        tick=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        tick.Tick+=(_,_)=>
        {
            CheckCountdowns();if(NotificationPolicy.Quiet(Settings,DateTimeOffset.UtcNow) && Notifications.Count>0 && Notifications.Current?.Kind!="preview")Notifications.Clear();else Notifications.Tick(DateTimeOffset.UtcNow,overlay.NotificationBusy,Settings.NotificationSeconds);
            try{Awake.Tick(DateTimeOffset.UtcNow);}catch(Exception ex){Store.Log(ex);Notify("Could not update keep awake",ex.Message);}
            var changed=RuntimeChange.Clock;
            if(Session.Timer.Running || Session.Focus.Running || Session.Stopwatch.Running)changed|=RuntimeChange.Countdown;
            if(++counter%2==0)
            {
                Audio.Poll();Audio.TrimIdleMedia();bool system=overlay.SystemSummaryVisible || Previews.AnyVisible("system") || settingsWindow?.NeedsSystemSample==true;
                if(system || overlay.NeedsAdaptiveCpuSample)Usage=Native.SystemUsage();
                if(system)changed|=RuntimeChange.System;
                overlay.CheckEnvironment();
            }
            if(counter%10==0){Productivity.DisplayThumbnails.TrimIdle();AssistantPreviewCache.TrimIdle(Assistant);}
            PublishUpdate(changed);
        };
        tick.Start();CheckCountdowns();
        if(Store.Warning!=null) Notify("Settings recovered",Store.Warning);
        if(!Array.Exists(e.Args,a=>a=="--background")) Notify("NabyScape is ready", "Click the island for your widgets. Right-click the tray icon for settings.");
        InitializeRuntimeRecovery();
        PrivateDiagnostics.SetServiceState(DiagnosticService.Application,DiagnosticServiceState.Ready);
        PrivateDiagnostics.Record(DiagnosticStage.Application,DiagnosticCode.Completed,startupClock.Elapsed);
        if(diagnosticPath==null)UpdateHandoff.SignalStarted(e.Args);
        if(diagnosticPath==null) _=Updates.Check(false,Settings.AutoCheckUpdates,Settings.AutoDownloadUpdates);
        if(diagnosticPath!=null) _=Diagnostics.Run(this,overlay,diagnosticPath);
        else
        {
            bool offerWelcome=false;
            try{offerWelcome=WelcomeSetup.ShouldOffer(Store,existingProfile);}catch(Exception ex){Store.Log(ex);}
            if(offerWelcome)ShowWelcome();
            else if(Array.Exists(e.Args,a=>a=="--settings"))ShowSettings();
        }
    }
    private void RefreshAssistantTray(){PrivateDiagnostics.SetServiceState(DiagnosticService.Assistant,!Assistant.Options.Enabled?DiagnosticServiceState.Disabled:Assistant.State==AssistantState.Error?DiagnosticServiceState.Failed:Assistant.Busy?DiagnosticServiceState.Busy:DiagnosticServiceState.Ready);if(tray!=null)tray.Text=Assistant.VoiceConnected?"NabyScape · Voice microphone on":Assistant.WakeListening?"NabyScape · Wake microphone on":Assistant.WakeArmed?"NabyScape · Wake word preparing":"NabyScape";}
    public void CheckCountdowns()
    {
        var now=DateTimeOffset.UtcNow;bool changed=false;
        if(Session.Timer.Complete(now)) { changed=true;PostNotice(new("timer","Timer finished","Your timer has finished."));if(Settings.NotifyTimer && !NotificationPolicy.Quiet(Settings,now))CountdownAudio.Play(Settings); }
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
            if(Settings.NotifyFocus && !NotificationPolicy.Quiet(Settings,now))CountdownAudio.Play(Settings);
        }
        if(changed) SaveSession();
    }
    internal void OpenMixer()=>overlay?.OpenMixer();
    public void SaveSession() { try { Session.Stopwatch.Checkpoint();Store.Save("session",Session); } catch(Exception ex) { Store.Log(ex);Notify("Not saved yet", "Could not save the timer state."); } PublishUpdate(RuntimeChange.Countdown); }
    internal bool SettingsSaveSucceeded {get;private set;}
    public void ChangeSettings()
    {
        Settings.Normalize();Media.SetEnabled(MediaFeaturesWanted);Brightness.Start(!diagnosticMode && Settings.ReactiveFeedback && Settings.BrightnessFeedback);Audio.SpectrumEnabled=Settings.WaveformSource=="Spectrum";UI.ResourceBrush("Accent",UI.Brush(Settings.Accent));UI.ApplyControlTheme(Settings);Motion.Configure(Settings);
        Productivity?.SetClipboardEnabled(Settings.ClipboardHistory);
        SettingsSaveSucceeded=false;
        try { Store.Save("settings",Settings);SettingsSaveSucceeded=true; } catch(Exception ex) { Store.Log(ex);Notify("Not saved yet","These settings will apply until you close the app."); }
        ConfigureDownloads();if(Settings.NotificationDelivery is "Off" or "Windows" || NotificationPolicy.Quiet(Settings,DateTimeOffset.UtcNow))Notifications.Clear();
        Previews?.ApplySettings();SettingsChanged?.Invoke();PublishUpdate(RuntimeChange.Settings);
    }
    public void ShowSettings()
    {
        overlay?.Collapse();
        if(settingsWindow==null) { settingsWindow=new SettingsWindow(this);settingsWindow.Closed+=(_,_)=>settingsWindow=null; }
        if(diagnosticMode)settingsWindow.ShowActivated=false;
        settingsWindow.Show();if(!diagnosticMode)settingsWindow.Activate();
    }
    public void Notify(string title,string text)
    { PostNotice(new("failure",title,text)); }
    public void Quit()
    { quitting=true;Shutdown(); }
    protected override void OnExit(ExitEventArgs e)
    {
        DisposeRuntimeRecovery();
        if(Assistant!=null)Assistant.Changed-=RefreshAssistantTray;Assistant?.Dispose();assistantWindow?.Close();
        welcomeWindow?.Close();tick?.Stop();Weather?.Dispose();AmbientAudio?.Dispose();Lyrics.Dispose();CountdownAudio.Dispose();Downloads?.Dispose();Updates?.Dispose();
        if(Session!=null) SaveSession();
        Previews?.Dispose();overlay?.Cleanup();try{Awake?.Dispose();}catch(Exception ex){Store?.Log(ex);}Productivity?.Dispose();Media?.Dispose();Audio?.Dispose();Brightness?.Dispose();
        if(tray!=null) { tray.Visible=false;tray.Dispose(); }
        openWait?.Unregister(null);exitWait?.Unregister(null);openEvent?.Dispose();exitEvent?.Dispose();mutex?.Dispose();base.OnExit(e);
    }
    public bool Quitting=>quitting;
}
