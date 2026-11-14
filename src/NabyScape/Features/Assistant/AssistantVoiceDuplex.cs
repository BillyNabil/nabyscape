using System;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

// Half-duplex echo guard for live voice. With open speakers the microphone hears Naby, and the
// provider treats that echo as the user barging in, chopping the reply. While assistant audio is
// queued or playing, plus the native output latency and a short tail, microphone frames are not
// uploaded. A stalled output stops extending the pause so the session can never go deaf.
internal sealed class VoiceEchoGate
{
    internal const int PlaybackLatencyMs=100,TailMs=300,StallMs=5000;
    private long openAt=long.MinValue,queueChangedAt;
    private int lastQueued;
    internal VoiceEchoGate(bool halfDuplex)=>HalfDuplex=halfDuplex;
    internal bool HalfDuplex {get;}
    internal bool Muted {get;private set;}
    // Assistant audio was queued or is still audible at `now`.
    internal void Speaking(long now){if(HalfDuplex)openAt=Math.Max(openAt,now+PlaybackLatencyMs+TailMs);}
    // Playback was cleared (interruption or typed input): listen again immediately.
    internal void Cleared(){openAt=long.MinValue;lastQueued=0;Muted=false;}
    internal bool AllowMicrophone(long now,int queuedMs)
    {
        if(!HalfDuplex)return true;
        queuedMs=Math.Max(0,queuedMs);
        if(queuedMs!=lastQueued){lastQueued=queuedMs;queueChangedAt=now;}
        if(queuedMs>0 && now-queueChangedAt<StallMs)Speaking(now);
        Muted=now<openAt;return !Muted;
    }
}

internal sealed partial class AssistantRuntime
{
    internal void SaveVoiceBargeIn(bool value)
    {
        bool previous=Options.VoiceBargeIn;Options.VoiceBargeIn=value;
        try{app.Store.Save("assistant",Options);}catch{Options.VoiceBargeIn=previous;throw;}
        Notify();
    }
}

internal sealed partial class AssistantProviderSettings
{
    private FrameworkElement BargeInSettings()
    {
        const string name="Let me interrupt Naby while it speaks (use headphones)";
        var body=new StackPanel{Margin=new(0,10,0,0)};
        var toggle=new CheckBox{Content=Localization.Text(name),IsChecked=ai.Options.VoiceBargeIn,Margin=new(0,4,0,6)};UI.LocalizedName(toggle,name);
        toggle.Click+=(_,_)=>
        {
            try{ai.SaveVoiceBargeIn(toggle.IsChecked==true);Feedback("Voice interruption saved · "+(ai.Options.VoiceBargeIn?"On":"Off")+(ai.VoiceConnected?". It applies to your next voice conversation.":"."));}
            catch(Exception ex){toggle.IsChecked=ai.Options.VoiceBargeIn;Feedback("Not saved · "+ex.Message,true);}
        };
        body.Children.Add(toggle);
        body.Children.Add(UI.Caption("For live voice (OpenAI Realtime, Gemini Live). Off: the microphone pauses while Naby speaks and for a moment after, so speakers cannot make Naby interrupt itself. On: talk over Naby at any time; use headphones so it does not hear its own voice.",11,UI.Muted));
        return body;
    }
}
