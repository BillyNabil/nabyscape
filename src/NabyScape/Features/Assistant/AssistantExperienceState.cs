using System;
namespace NabyScape;
internal sealed partial class AssistantRuntime
{
    internal bool SpeechCheckBusy {get;private set;}
    private Action? cancelSpeechCheck;
    private bool resumeWakeAfterCheck;
    private int speechCheckWakeVersion;
    internal void BeginSpeechCheck(Action cancel){if(SpeechCheckBusy || Speech.Busy || VoiceConnected || Busy)throw new InvalidOperationException(Localization.Text("Finish the current voice operation first."));bool resume=WakeArmed;StopSession();resumeWakeAfterCheck=resume;speechCheckWakeVersion=wakeVersion;SpeechCheckBusy=true;Speech.Testing=true;cancelSpeechCheck=cancel;}
    internal void EndSpeechCheck(){SpeechCheckBusy=false;Speech.Testing=false;cancelSpeechCheck=null;if(resumeWakeAfterCheck && speechCheckWakeVersion==wakeVersion && Options.Enabled && Options.WakeWord.Enabled){wakeWanted=true;wakeNotBefore=Environment.TickCount64+1200;WakeStatus=Localization.Text("Speech check ended · wake word will resume.");}resumeWakeAfterCheck=false;Notify();}
    internal string CaptureSummary=>(WakeListening?Localization.Text("Wake listening"):WakeArmed?Localization.Text("Wake preparing"):Options.WakeWord.Enabled?Localization.Text("Wake paused"):Localization.Text("Wake off"))+" · "+(SpeechCheckBusy?Localization.Text("Speech test active"):VoiceConnected?Localization.Text("Mic on"):Localization.Text("Mic off"))+" · "+(Desktop.Sharing?Localization.Text("Sharing ")+Desktop.SourceName:Localization.Text("Screen not shared"));
    internal string SetupSummary
    {
        get
        {
            string listening=Localization.Text("Realtime provider"),speaking=Localization.Text("Realtime provider");var local=Options.LocalVoice;
            if(local.Enabled)
            {
                if(local.Managed){try{listening=AssistantOwnSpeech.Asr(local).Name;speaking=AssistantOwnSpeech.Tts(local).Name;}catch{listening=Localization.Text("Model files need attention");speaking=Localization.Text("Open Voice setup");}}
                else{listening=local.AsrModel;speaking=Localization.Text(local.TtsEngine);}
            }
            return Localization.Text("AI replies: ")+(Provider?.Name??Localization.Text("Not configured"))+Localization.Text("\nListening: ")+listening+Localization.Text("\nSpeaking: ")+speaking+"\n"+CaptureSummary;
        }
    }
}
