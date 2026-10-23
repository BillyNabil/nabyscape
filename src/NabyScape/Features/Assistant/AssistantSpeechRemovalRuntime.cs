using System.Threading.Tasks;

namespace NabyScape;

internal sealed partial class AssistantRuntime
{
    internal Task<bool> UninstallSpeechModel(string id,bool tts)
        =>SpeechCheckBusy?Task.FromResult(false):Speech.UninstallModel(id,tts,async()=>
        {
            if(VoiceConnected || Busy || Speech.ModelInUse(Options.LocalVoice,id,tts)){StopSession();await voiceClosing;}
            // Keep the selected local mode and IDs. Readiness detects missing
            // weights, so removal never silently switches the user to cloud voice.
        });
}
