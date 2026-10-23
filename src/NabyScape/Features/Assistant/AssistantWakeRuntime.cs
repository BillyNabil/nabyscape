using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal sealed partial class AssistantRuntime
{
    private readonly AssistantWakeWord wake=new();
    private bool backgroundReady,wakeWanted,wakeStarting,wakeFailed,wakeTriggering;
    private int wakeVersion;
    private long wakeNotBefore;
    private Task wakeClosing=Task.CompletedTask,voiceClosing=Task.CompletedTask;
    private AssistantWidgetBridge? widgetBridge;
    internal bool WakeListening=>wake.Listening;
    internal bool WakeArmed=>wakeWanted && !wakeFailed;
    internal string WakeStatus {get;private set;}="Wake word is off. Microphone stays off until you start voice.";
    internal void StartBackgroundFeatures()
    {
        backgroundReady=true;widgetBridge??=new(BridgeRequest);wakeWanted=Options.Enabled && Options.WakeWord.Enabled;Notify();
    }
    private async Task<JsonElement> BridgeRequest(JsonElement request,CancellationToken token)
    {
        if(!app.Dispatcher.CheckAccess())return await app.Dispatcher.InvokeAsync(()=>BridgeRequest(request,token),System.Windows.Threading.DispatcherPriority.Normal,token).Task.Unwrap();
        token.ThrowIfCancellationRequested();string operation=request.TryGetProperty("operation",out var op)?op.GetString()??"":"";
        if(operation=="tools")return JsonSerializer.SerializeToElement(new{tools=WidgetTools().Select(t=>new{name=t.Name,description=t.Description,inputSchema=t.Parameters,annotations=new{readOnlyHint=t.Name=="naby_list_widget_pages",destructiveHint=false,openWorldHint=false}})});
        if(operation!="call" || !request.TryGetProperty("name",out var name) || name.ValueKind!=JsonValueKind.String || !request.TryGetProperty("arguments",out var args))return JsonSerializer.SerializeToElement(new{ok=false,error="Unsupported widget bridge request."});
        string result=await ExecuteWidgetTool(new(Guid.NewGuid().ToString("N"),name.GetString()!,args.GetRawText()),token);using var json=JsonDocument.Parse(result);return json.RootElement.Clone();
    }
    internal void SaveWakeWord(AssistantWakeOptions options)
    {
        options.Phrase=AssistantWakeWord.Phrase(options.Phrase);options.Confidence=Math.Clamp(options.Confidence,.55,.95);
        if(options.Enabled && !Options.Enabled)throw new InvalidOperationException("Enable AI companion before enabling the wake word.");
        if(options.Enabled && !WakeVoiceReady())throw new InvalidOperationException("Choose and save a working voice connection before enabling the wake word. Local speech needs a selected chat model; realtime voice needs a model and WebSocket URL.");
        var previous=Options.WakeWord;Options.WakeWord=options;
        try{app.Store.Save("assistant",Options);}catch{Options.WakeWord=previous;throw;}
        PauseWake();wakeWanted=options.Enabled;wakeFailed=false;WakeStatus=options.Enabled?"Preparing wake listening…":"Wake word is off. Microphone stays off until you start voice.";Notify();
    }
    internal void ToggleWakeListening()
    {
        if(WakeArmed){PauseWake();WakeStatus="Wake word paused · wake microphone off.";}
        else if(Options.Enabled && Options.WakeWord.Enabled){PauseWake();wakeWanted=true;wakeFailed=false;WakeStatus=VoiceConnected?"Wake word will resume after the voice conversation.":"Preparing wake listening…";}
        else{WakeStatus="Enable wake word in Options → Voice settings first.";}
        Notify();
    }
    private void PauseWake()
    {wakeWanted=false;wakeVersion++;wakeClosing=wake.Stop();}
    private void SuspendWake(string reason)
    {wakeVersion++;wakeClosing=wake.Stop();if(wakeWanted)WakeStatus=reason;}
    private bool WakeVoiceReady()
    {var selected=Options.LocalVoice.Enabled?Provider:VoiceProvider;return Setup.Voice.Configured && selected!=null && (Options.LocalVoice.Enabled?!string.IsNullOrWhiteSpace(selected.Model):!string.IsNullOrWhiteSpace(selected.RealtimeModel) && !string.IsNullOrWhiteSpace(selected.RealtimeUrl));}
    private void QueueWake()
    {
        if(!backgroundReady || disposed || !wakeWanted || wakeStarting || wakeTriggering || wakeFailed || !Options.Enabled || !Options.WakeWord.Enabled || SpeechCheckBusy || Speech.Busy || Busy || VoiceConnected || WakeListening)return;
        if(!WakeVoiceReady()){wakeFailed=true;WakeStatus="Complete Voice settings and choose a saved model, then resume wake listening.";return;}
        wakeStarting=true;_ =ArmWake(wakeVersion);
    }
    private async Task ArmWake(int version)
    {
        try
        {
            await wakeClosing;await voiceClosing;long delay=wakeNotBefore-Environment.TickCount64;if(delay>0)await Task.Delay((int)Math.Min(delay,2000));
            if(version!=wakeVersion || disposed || !wakeWanted || !Options.Enabled || !Options.WakeWord.Enabled || SpeechCheckBusy || Speech.Busy || Busy || VoiceConnected)return;
            var options=JsonSerializer.Deserialize<AssistantWakeOptions>(JsonSerializer.Serialize(Options.WakeWord))!;
            await wake.Start(options,()=>Dispatch(()=>_=WakeDetected(version)),message=>Dispatch(()=>{if(version==wakeVersion){wakeFailed=true;WakeStatus=message;wakeClosing=wake.Stop();Notify();}}));
            if(version==wakeVersion && wakeWanted && WakeListening)WakeStatus="Wake microphone on · say “"+options.Phrase+"”. Detection stays on this PC.";
        }
        catch(Exception ex){if(version==wakeVersion){wakeFailed=true;WakeStatus="Wake word unavailable: "+ex.Message;}}
        finally{wakeStarting=false;Dispatch(Notify);}
    }
    private async Task WakeDetected(int version)
    {
        if(disposed || version!=wakeVersion || !wakeWanted || !Options.Enabled || SpeechCheckBusy || Speech.Busy || Busy || VoiceConnected)return;
        wakeTriggering=true;int activation=++wakeVersion;wakeClosing=wake.Stop();
        try{await wakeClosing;if(disposed || !wakeWanted || activation!=wakeVersion)return;app.OpenVoice();await StartVoice();if(wakeWanted && !VoiceConnected){wakeWanted=false;wakeFailed=true;WakeStatus="Voice did not start. Check Voice settings, then resume wake listening.";}}
        catch{wakeWanted=false;wakeFailed=true;WakeStatus="Wake activation failed. Check the microphone and Voice settings.";}
        finally{wakeTriggering=false;Notify();}
    }
    internal async Task EndVoice()
    {
        // Ending a conversation returns to the enabled wake feature even when a
        // provider/setup change paused it before this manually started session.
        bool resume=Options.Enabled && Options.WakeWord.Enabled;StopSession();int version=wakeVersion;await voiceClosing;
        if(disposed || version!=wakeVersion || !resume || !Options.Enabled || !Options.WakeWord.Enabled)return;
        wakeWanted=true;wakeNotBefore=Environment.TickCount64+1200;WakeStatus="Conversation ended · wake word will resume.";Notify();
    }
}
