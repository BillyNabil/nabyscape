using System;
using System.Linq;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

// Windows' installed SAPI recognizer evaluates only a small phrase grammar.
// Wake audio is never sent to a provider, stored, or passed to the chat model.
internal sealed class AssistantWakeWord
{
    private readonly SemaphoreSlim transition=new(1,1);
    private SpeechRecognitionEngine? engine;
    private int generation;
    private volatile bool listening;
    internal bool Listening=>listening;
    internal static RecognizerInfo[] Recognizers()
    {
        try{return SpeechRecognitionEngine.InstalledRecognizers().ToArray();}
        catch(Exception ex) when(ex is PlatformNotSupportedException or System.Runtime.InteropServices.COMException or InvalidOperationException or System.IO.IOException)
        {throw new InvalidOperationException("Windows wake recognition is unavailable for this app architecture. Install a compatible Windows speech engine, or start voice manually with an external local ASR/TTS service.",ex);}
    }
    internal static string Phrase(string value)
    {
        string phrase=string.Join(" ",(value??"").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
        if(phrase.Length is <3 or >64 || phrase.Any(c=>!char.IsLetter(c) && c is not (' ' or '-' or '\'')))throw new InvalidOperationException("Use a wake phrase of 3–64 letters, spaces, apostrophes or hyphens.");return phrase;
    }
    internal async Task Start(AssistantWakeOptions options,Action detected,Action<string> failed)
    {
        int version=Interlocked.Increment(ref generation);await transition.WaitAsync();
        try
        {
            await Task.Run(()=>
            {
                if(version!=Volatile.Read(ref generation))return;
                if(engine!=null){Release(engine);engine=null;}
                string phrase=Phrase(options.Phrase);var available=Recognizers();var selected=options.RecognizerId.Length>0?available.FirstOrDefault(r=>r.Id==options.RecognizerId):available.FirstOrDefault(r=>r.Culture.Name=="en-US")??available.FirstOrDefault();
                if(selected==null)throw new InvalidOperationException("No matching Windows speech recognizer is installed. Install a speech language in Windows Settings → Time & language → Speech, then choose Refresh recognizers.");
                var candidate=new SpeechRecognitionEngine(selected);int fired=0;
                try
                {
                    var phrases=phrase.Equals("Hey Naby",StringComparison.OrdinalIgnoreCase)?new[]{phrase,"Hey Nabi","Hey Nabby","Hey Navy"}:new[]{phrase};
                    var grammar=new Grammar(new GrammarBuilder(new Choices(phrases)){Culture=selected.Culture}){Name="Naby wake phrase"};candidate.LoadGrammar(grammar);
                    candidate.SpeechRecognized+=(_,e)=>{if(version!=Volatile.Read(ref generation) || !listening || e.Result.Confidence<Math.Clamp(options.Confidence,.55,.95) || Interlocked.CompareExchange(ref fired,1,0)!=0)return;listening=false;detected();};
                    candidate.RecognizeCompleted+=(_,e)=>{if(version==Volatile.Read(ref generation) && listening){listening=false;failed(e.Error==null?"Wake listening stopped. Resume it to listen again.":"Windows speech recognition stopped. Check the microphone and selected speech language, then resume wake listening.");}};
                    if(version!=Volatile.Read(ref generation)){candidate.Dispose();return;}
                    candidate.SetInputToDefaultAudioDevice();engine=candidate;listening=true;candidate.RecognizeAsync(RecognizeMode.Multiple);
                    if(version!=Volatile.Read(ref generation)){listening=false;Release(candidate);engine=null;}
                }
                catch{listening=false;engine=null;candidate.Dispose();throw;}
            });
        }
        finally{transition.Release();}
    }
    internal async Task Stop()
    {
        Interlocked.Increment(ref generation);listening=false;await transition.WaitAsync();
        try
        {
            listening=false;var previous=engine;engine=null;if(previous==null)return;
            await Task.Run(()=>Release(previous));
        }
        finally{transition.Release();}
    }
    private static void Release(SpeechRecognitionEngine engine)
    {try{engine.RecognizeAsyncCancel();}catch{}try{engine.SetInputToNull();}catch{}engine.Dispose();}
}
