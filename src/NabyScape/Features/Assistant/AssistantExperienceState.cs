using System;
namespace NabyScape;
internal sealed partial class AssistantRuntime
{
    internal bool SpeechCheckBusy {get;private set;}
    private Action? cancelSpeechCheck;
    private bool resumeWakeAfterCheck;
    private int speechCheckWakeVersion;
    internal void BeginSpeechCheck(Action cancel){if(SpeechCheckBusy || Speech.Busy || VoiceConnected || Busy)throw new InvalidOperationException("Finish the current voice operation first.");bool resume=WakeArmed;StopSession();resumeWakeAfterCheck=resume;speechCheckWakeVersion=wakeVersion;SpeechCheckBusy=true;Speech.Testing=true;cancelSpeechCheck=cancel;}
    internal void EndSpeechCheck(){SpeechCheckBusy=false;Speech.Testing=false;cancelSpeechCheck=null;if(resumeWakeAfterCheck && speechCheckWakeVersion==wakeVersion && Options.Enabled && Options.WakeWord.Enabled){wakeWanted=true;wakeNotBefore=Environment.TickCount64+1200;WakeStatus="Speech check ended · wake word will resume.";}resumeWakeAfterCheck=false;Notify();}
    internal string CaptureSummary=>(WakeListening?"Wake listening":WakeArmed?"Wake preparing":Options.WakeWord.Enabled?"Wake paused":"Wake off")+" · "+(SpeechCheckBusy?"Speech test active":VoiceConnected?"Mic on":"Mic off")+" · "+(Desktop.Sharing?"Sharing "+Desktop.SourceName:"Screen not shared");
    internal string SetupSummary
    {
        get
        {
            string listening="Realtime provider",speaking="Realtime provider";var local=Options.LocalVoice;
            if(local.Enabled)
            {
                if(local.Managed){try{listening=AssistantOwnSpeech.Asr(local).Name;speaking=AssistantOwnSpeech.Tts(local).Name;}catch{listening="Model files need attention";speaking="Open Voice setup";}}
                else{listening=local.AsrModel;speaking=local.TtsEngine;}
            }
            return "AI replies: "+(Provider?.Name??"Not configured")+"\nListening: "+listening+"\nSpeaking: "+speaking+"\n"+CaptureSummary;
        }
    }
}
