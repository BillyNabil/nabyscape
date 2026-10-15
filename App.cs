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
    public Store Store { get; private set; }=null!;
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
    public event Action<string,string,string>? ActivityRaised;
    public event Action? SettingsChanged;
    public event Action? Updated;
    public (double cpu,double used,double total) Usage { get; private set; }
    public static new App Current=>(App)Application.Current;
    [STAThread] public static void Main(string[] args)
    {
        var culture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture=culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture=culture;
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
        Store=new(diagnosticPath==null ? null : Path.Combine(diagnosticPath,"test-profile"));Settings=diagnosticPath==null?Store.Load<Settings>("settings"):new Settings();Settings.Normalize();Settings.AutoStart=StartupService.Enabled();
        Resources["Accent"]=UI.Brush(Settings.Accent);UI.ApplyControlTheme(Settings);
        Motion.Configure(Settings);
        Session=diagnosticPath==null?Store.Load<SessionState>("session"):new SessionState();Session.Timer??=new();Session.Focus??=new();
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
        menu.Items.Add("Hide / show",null,(_,_)=>Dispatcher.Invoke(()=>overlay?.ToggleVisibility()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit",null,(_,_)=>Dispatcher.Invoke(Quit));
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowSettings);
        overlay=new OverlayWindow(this);MainWindow=overlay;overlay.Show();Previews=new(this,overlay.OpenFeaturePage);
        Media.Changed+=()=>Updated?.Invoke();
        Audio.Changed+=()=>Updated?.Invoke();
        _=Media.Initialize();
        Audio.Read();Usage=Native.SystemUsage();
        int counter=0;
        tick=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        tick.Tick+=(_,_)=>
        {
            CheckCountdowns();
            try{Awake.Tick(DateTimeOffset.UtcNow);}catch(Exception ex){Store.Log(ex);Notify("Could not update keep awake",ex.Message);}
            if(++counter%2==0) { Audio.Read();if(overlay.SystemSummaryVisible || Previews.AnyVisible("system") || settingsWindow?.IsVisible==true)Usage=Native.SystemUsage();overlay.CheckEnvironment(); }
            Updated?.Invoke();
        };
        tick.Start();CheckCountdowns();
        if(Store.Warning!=null) Notify("Settings recovered",Store.Warning);
        if(!Array.Exists(e.Args,a=>a=="--background")) Notify("NabyScape is ready", "Click the island for your widgets. Right-click the tray icon for settings.");
        Microsoft.Win32.SystemEvents.PowerModeChanged+=PowerChanged;
        if(diagnosticPath!=null) _=Diagnostics.Run(this,overlay,diagnosticPath);
        else if(Array.Exists(e.Args,a=>a=="--settings"))ShowSettings();
    }
    private void PowerChanged(object sender,Microsoft.Win32.PowerModeChangedEventArgs e)
    { if(e.Mode==Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(()=> { CheckCountdowns();Awake?.Tick(DateTimeOffset.UtcNow);overlay?.CheckEnvironment();Updated?.Invoke(); }); }
    public void CheckCountdowns()
    {
        var now=DateTimeOffset.UtcNow;bool changed=false;
        if(Session.Timer.Complete(now)) { changed=true;ActivityRaised?.Invoke("timer","Timer finished","Time’s up");Notify("Timer finished", "Your timer has finished.");SystemSounds.Asterisk.Play(); }
        var focusDeadline=Session.Focus.Deadline;
        if(Session.Focus.Complete(now))
        {
            changed=true;
            ActivityRaised?.Invoke("focus","Focus session complete","Take a break");
            if(Session.Focus.Phase=="work")
            {
                // The break starts at the actual work deadline, even across sleep, so a long sleep never starts a fresh break.
                var end=focusDeadline!.Value.AddMinutes(Settings.BreakMinutes);
                if(end>now) { Session.Focus.Start((end-now).TotalSeconds,now,"break",preserveRunId:true);Session.Focus.DurationSeconds=Settings.BreakMinutes*60; Notify("Focus complete", "Time for a short break."); }
                else { Session.Focus.Reset();Notify("Focus session complete", "Your focus session and break have finished."); }
            }
            else { Session.Focus.Reset();Notify("Break finished", "Ready for your next focus session."); }
            SystemSounds.Asterisk.Play();
        }
        if(changed) SaveSession();
    }
    internal void OpenMixer()=>overlay?.OpenMixer();
    public void SaveSession() { try { Store.Save("session",Session); } catch(Exception ex) { Store.Log(ex);Notify("Not saved yet", "Could not save the timer state."); } Updated?.Invoke(); }
    public void ChangeSettings()
    {
        Settings.Normalize();Resources["Accent"]=UI.Brush(Settings.Accent);UI.ApplyControlTheme(Settings);Motion.Configure(Settings);
        Productivity?.SetClipboardEnabled(Settings.ClipboardHistory);
        try { Store.Save("settings",Settings); } catch(Exception ex) { Store.Log(ex);Notify("Not saved yet","These settings will apply until you close the app."); }
        Previews?.ApplySettings();SettingsChanged?.Invoke();Updated?.Invoke();
    }
    public void ShowSettings()
    {
        overlay?.Collapse();
        if(settingsWindow==null) { settingsWindow=new SettingsWindow(this);settingsWindow.Closed+=(_,_)=>settingsWindow=null; }
        settingsWindow.Show();settingsWindow.Activate();
    }
    public void Notify(string title,string text)
    { if(tray!=null) tray.ShowBalloonTip(4500,title,text,Forms.ToolTipIcon.Info); }
    public void Quit()
    { quitting=true;Shutdown(); }
    protected override void OnExit(ExitEventArgs e)
    {
        tick?.Stop();
        Microsoft.Win32.SystemEvents.PowerModeChanged-=PowerChanged;
        if(Session!=null) SaveSession();
        Previews?.Dispose();Activities?.Dispose();overlay?.Cleanup();try{Awake?.Dispose();}catch(Exception ex){Store?.Log(ex);}Productivity?.Dispose();Media?.Dispose();Audio?.Dispose();Brightness?.Dispose();
        if(tray!=null) { tray.Visible=false;tray.Dispose(); }
        openWait?.Unregister(null);exitWait?.Unregister(null);openEvent?.Dispose();exitEvent?.Dispose();mutex?.Dispose();base.OnExit(e);
    }
    public bool Quitting=>quitting;
}
