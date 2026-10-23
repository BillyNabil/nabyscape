using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

namespace NabyScape;

public sealed partial class App
{
    [Flags] private enum RecoveryChange { None=0, Suspend=1, Session=2, Offline=4, Audio=8, Resume=16, Online=32, Display=64 }
    private readonly object recoveryGate=new();
    private RecoveryChange pendingRecovery;
    private bool recoverySubscribed,recoveryQueued,recoveryDisposed,recoveryRefreshing,recoveryRefreshAgain;
    private CancellationTokenSource? recoveryLifetime;
    internal string RecoveryStatus {get;private set;}="Ready";

    private void InitializeRuntimeRecovery()
    {
        if(diagnosticMode || recoverySubscribed || recoveryDisposed)return;
        recoveryLifetime=new();recoverySubscribed=true;
        SystemEvents.PowerModeChanged+=RuntimePowerChanged;
        SystemEvents.SessionSwitch+=RuntimeSessionChanged;
        NetworkChange.NetworkAvailabilityChanged+=RuntimeNetworkChanged;
        Audio.EndpointsChanged+=RuntimeAudioChanged;
        PrivateDiagnostics.SetServiceState(DiagnosticService.Recovery,DiagnosticServiceState.Ready);
    }
    private void RuntimePowerChanged(object sender,PowerModeChangedEventArgs e)
    {if(e.Mode==PowerModes.Suspend)QueueRecovery(RecoveryChange.Suspend);else if(e.Mode==PowerModes.Resume)QueueRecovery(RecoveryChange.Resume);}
    private void RuntimeSessionChanged(object sender,SessionSwitchEventArgs e)
    {
        if(e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)QueueRecovery(RecoveryChange.Session);
        else if(e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)QueueRecovery(RecoveryChange.Resume);
    }
    private void RuntimeNetworkChanged(object? sender,NetworkAvailabilityEventArgs e)=>QueueRecovery(e.IsAvailable?RecoveryChange.Online:RecoveryChange.Offline);
    private void RuntimeAudioChanged()=>QueueRecovery(RecoveryChange.Audio);
    // Reuse OverlayWindow's existing display subscription; never rewrite the saved monitor choice.
    internal void RuntimeDisplayChanged()=>QueueRecovery(RecoveryChange.Display);
    private void QueueRecovery(RecoveryChange change)
    {
        lock(recoveryGate)
        {
            if(!recoverySubscribed || recoveryDisposed || Dispatcher.HasShutdownStarted)return;
            pendingRecovery|=change;if(recoveryQueued)return;recoveryQueued=true;
        }
        try{Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(ApplyRuntimeRecovery));}
        catch(InvalidOperationException){lock(recoveryGate){recoveryQueued=false;pendingRecovery=RecoveryChange.None;}}
    }
    private void ApplyRuntimeRecovery()
    {
        RecoveryChange change;
        lock(recoveryGate){change=pendingRecovery;pendingRecovery=RecoveryChange.None;recoveryQueued=false;}
        if(recoveryDisposed || quitting || diagnosticMode)return;
        try
        {
            bool interrupted=(change & (RecoveryChange.Suspend|RecoveryChange.Session|RecoveryChange.Offline|RecoveryChange.Audio))!=0;
            if(interrupted || (change.HasFlag(RecoveryChange.Display) && Assistant.Desktop.Sharing))
            {
                string reason=change.HasFlag(RecoveryChange.Suspend)?"Windows is suspending":change.HasFlag(RecoveryChange.Session)?"Windows session changed":change.HasFlag(RecoveryChange.Offline)?"Network connection lost":change.HasFlag(RecoveryChange.Audio)?"Audio devices changed":"Display configuration changed";
                bool active=Assistant.VoiceConnected || Assistant.WakeArmed || Assistant.Busy || Assistant.Desktop.Sharing;
                // Also invalidate EndVoice's pending wake continuation while its session is already closing.
                Assistant.PreventRecoveryRestart();
                if(active)
                {
                    Assistant.StopForRecovery(reason);
                    RecoveryStatus=reason+". Start voice, resume wake listening or retry your request when ready.";
                    PrivateDiagnostics.Record(DiagnosticStage.Recovery,DiagnosticCode.Cancelled);
                    PrivateDiagnostics.SetServiceState(DiagnosticService.Recovery,DiagnosticServiceState.Degraded);
                    Notify("Session paused",RecoveryStatus);
                }
            }
            if((change & (RecoveryChange.Resume|RecoveryChange.Online|RecoveryChange.Audio|RecoveryChange.Display))!=0)
            {
                if(change.HasFlag(RecoveryChange.Resume))
                {
                    RecoverRuntimeStep(CheckCountdowns);RecoverRuntimeStep(Audio.Read);
                    RecoverRuntimeStep(()=>
                    {
                        var now=DateTimeOffset.UtcNow;Awake.Tick(now);
                        if(Awake.Active)Awake.Start(Awake.Until.HasValue?Awake.Until.Value-now:null,Awake.KeepDisplayOn,now);
                    });
                    RecoverRuntimeStep(()=>overlay?.RefreshRuntimeDisplay());HardwareMonitor.ResetSampling();
                }
                if((change & (RecoveryChange.Resume|RecoveryChange.Online))!=0 && !change.HasFlag(RecoveryChange.Offline) && !change.HasFlag(RecoveryChange.Suspend) && !change.HasFlag(RecoveryChange.Session))_=RefreshRuntimeCaches();
                PublishUpdate(RuntimeChange.All);
            }
        }
        catch(Exception){PrivateDiagnostics.Record(DiagnosticStage.Recovery,DiagnosticCode.UnknownFailure);PrivateDiagnostics.SetServiceState(DiagnosticService.Recovery,DiagnosticServiceState.Degraded);RecoveryStatus="Some devices are unavailable. Check Windows settings, then explicitly retry.";}
    }
    private void RecoverRuntimeStep(Action recover)
    {
        try{recover();}
        catch(Exception){PrivateDiagnostics.Record(DiagnosticStage.Recovery,DiagnosticCode.UnknownFailure);PrivateDiagnostics.SetServiceState(DiagnosticService.Recovery,DiagnosticServiceState.Degraded);RecoveryStatus="Some devices are unavailable. Check Windows settings, then explicitly retry.";}
    }
    private async Task RefreshRuntimeCaches()
    {
        if(recoveryRefreshing){recoveryRefreshAgain=true;return;}
        recoveryRefreshing=true;
        try
        {
            do
            {
                recoveryRefreshAgain=false;
                var token=recoveryLifetime?.Token??new CancellationToken(true);
                token.ThrowIfCancellationRequested();
                if(!Media.DiagnosticFixture)await Media.Refresh();
                token.ThrowIfCancellationRequested();
                // Only refresh a previously requested location. Get retains its normal 20-minute cache.
                var weatherKey=WeatherService.Key(Settings);
                if(Weather.Cached is {} cached && !string.IsNullOrWhiteSpace(cached.Key) && !string.IsNullOrWhiteSpace(weatherKey) && cached.Key==weatherKey && NetworkInterface.GetIsNetworkAvailable())await Weather.Get(Settings,false,token);
                if(!recoveryDisposed){PublishUpdate(RuntimeChange.All);PrivateDiagnostics.Record(DiagnosticStage.Recovery,DiagnosticCode.Completed);}
            }while(recoveryRefreshAgain && !recoveryDisposed);
        }
        catch(OperationCanceledException){}
        catch(Exception){if(!recoveryDisposed){PrivateDiagnostics.Record(DiagnosticStage.Recovery,DiagnosticCode.UnknownFailure);PrivateDiagnostics.SetServiceState(DiagnosticService.Recovery,DiagnosticServiceState.Degraded);}}
        finally{recoveryRefreshing=false;}
    }
    private void DisposeRuntimeRecovery()
    {
        lock(recoveryGate){recoveryDisposed=true;pendingRecovery=RecoveryChange.None;}
        if(recoverySubscribed)
        {
            SystemEvents.PowerModeChanged-=RuntimePowerChanged;SystemEvents.SessionSwitch-=RuntimeSessionChanged;
            NetworkChange.NetworkAvailabilityChanged-=RuntimeNetworkChanged;Audio.EndpointsChanged-=RuntimeAudioChanged;
            recoverySubscribed=false;
        }
        recoveryLifetime?.Cancel();recoveryLifetime?.Dispose();recoveryLifetime=null;
    }
}

internal sealed partial class AssistantRuntime
{
    internal void PreventRecoveryRestart()=>wakeVersion++;
    internal void StopForRecovery(string reason)
    {
        StopSession();
        Status=reason+" · microphones and screen sharing off. Start voice or retry when ready.";
        WakeStatus=reason+" · wake microphone off. Resume wake listening explicitly when ready.";
        Notify();
    }
}
