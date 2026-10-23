using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NabyScape;

internal sealed record AssistantVoiceTiming(long Turn,long AsrMs,long FirstTextMs,long FirstTtsMs,long AudioReadyMs,long TotalMs,bool Streamed)
{
    private static string Time(long ms)=>ms<0?"—":$"{ms/1000d:0.0}s";
    internal string Summary=>$"ASR {Time(AsrMs)} · Reply {Time(FirstTextMs)} · TTS {Time(FirstTtsMs)}";
    internal string Detail=>$"Speech recognition: {Time(AsrMs)}\nReply preparation to first accepted text: {Time(FirstTextMs)}\nFirst voice segment synthesis: {Time(FirstTtsMs)}\nRecognition start to first queued audio: {Time(AudioReadyMs)}\nTurn including playback: {Time(TotalMs)}\nReply includes shared-context preparation and tool work when used. Excludes the silence-detection pause and hardware output latency. Timings stay in memory; speech content is not logged.";
}

internal sealed partial class LocalAssistantVoice
{
    private long turnSerial;
    internal event Action<AssistantVoiceTiming>? Timing;
    internal event Action? TurnStarted;
    private sealed record VoiceChunk(string Text,byte[] Pcm);
    private async Task Turn(string text,CancellationToken token,long asrMs=0)
    {
        TurnStarted?.Invoke();long turn=Interlocked.Increment(ref turnSerial);var clock=Stopwatch.StartNew();
        long firstText=-1,firstTts=-1,audioReady=-1;bool streamed=false;int playing=0;
        void Report(long total=-1)=>Timing?.Invoke(new(turn,asrMs,Volatile.Read(ref firstText),Volatile.Read(ref firstTts),Volatile.Read(ref audioReady),total,streamed));
        Report();Phase?.Invoke(AssistantState.Thinking);Status?.Invoke("Waiting for a reply…");
        using var lifetimeTurn=CancellationTokenSource.CreateLinkedTokenSource(token);var ct=lifetimeTurn.Token;Exception? failure=null;
        var sentences=Channel.CreateBounded<string>(new BoundedChannelOptions(2){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
        var voices=Channel.CreateBounded<VoiceChunk>(new BoundedChannelOptions(2){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
        bool wholeReply=options.Managed && AssistantOwnSpeech.Tts(options) is {Package:null,Id:not "windows"};
        var captions=new ConcurrentQueue<(int Start,string Text)>();var splitter=new VoiceSentences(wholeReply);
        var engine=audio??throw new OperationCanceledException(token);engine.Clear();
        async Task Guard(Func<Task> action){try{await action();}catch(Exception ex){if(ex is not OperationCanceledException)Interlocked.CompareExchange(ref failure,ex,null);lifetimeTurn.Cancel();throw;}}
        async Task Produce()
        {
            try
            {
                int chunks=0;
                async Task Delta(string delta,CancellationToken requestToken)
                {
                    ct.ThrowIfCancellationRequested();requestToken.ThrowIfCancellationRequested();if(delta.Length==0)return;
                    if(!string.IsNullOrWhiteSpace(delta) && Interlocked.CompareExchange(ref firstText,clock.ElapsedMilliseconds,-1)==-1)Report();
                    chunks++;
                    foreach(string sentence in splitter.Append(delta,false))await sentences.Writer.WriteAsync(sentence,ct);
                }
                void Progress(string status){if(Volatile.Read(ref playing)==0)Status?.Invoke(status);}
                string answer=await reply(text,Delta,Progress,ct);ct.ThrowIfCancellationRequested();
                if(chunks==0 && answer.Length>0)await Delta(answer,ct);
                streamed=options.StreamReplies && chunks>1;
                foreach(string sentence in splitter.Append("",true))await sentences.Writer.WriteAsync(sentence,ct);
            }
            finally{sentences.Writer.TryComplete();}
        }
        async Task SynthesizeAll()
        {
            try
            {
                long bytes=0;
                await foreach(string sentence in sentences.Reader.ReadAllAsync(ct))
                {
                    if(Volatile.Read(ref playing)==0)Status?.Invoke("Preparing voice…");
                    var synthesis=Stopwatch.StartNew();byte[] pcm=await Synthesize(sentence,ct);ct.ThrowIfCancellationRequested();
                    if(Interlocked.CompareExchange(ref firstTts,synthesis.ElapsedMilliseconds,-1)==-1)Report();
                    bytes+=pcm.Length;if(bytes>24000L*2*120)throw new IOException("The spoken reply exceeds two minutes. Read the full answer in chat.");
                    await voices.Writer.WriteAsync(new(sentence,pcm),ct);
                }
            }
            finally{voices.Writer.TryComplete();}
        }
        async Task PlayAll()
        {
            int queuedMs=0;
            async Task WaitForPlayback(Func<bool> waiting)
            {
                int played=engine.PlayedMilliseconds;long advanced=Environment.TickCount64;
                while(waiting())
                {
                    ct.ThrowIfCancellationRequested();int current=engine.PlayedMilliseconds;
                    if(current!=played){played=current;advanced=Environment.TickCount64;}
                    else if(Environment.TickCount64-advanced>10000)throw new IOException("Voice playback stopped advancing. Check the Windows output device and start voice again.");
                    await Task.Delay(25,ct);
                }
            }
            await foreach(var chunk in voices.Reader.ReadAllAsync(ct))
            {
                // Keep roughly one second queued before accepting another segment.
                // PCM and synthesized segments cannot grow with an unbounded response.
                await WaitForPlayback(()=>queuedMs-engine.PlayedMilliseconds>1100);
                captions.Enqueue((queuedMs,chunk.Text));
                if(!engine.Append(chunk.Pcm))throw new IOException("The voice playback queue is full.");
                queuedMs+=chunk.Pcm.Length/48;
                if(Interlocked.Exchange(ref playing,1)==0){audioReady=asrMs+clock.ElapsedMilliseconds;Phase?.Invoke(AssistantState.Speaking);Status?.Invoke("Speaking · microphone waits until the reply finishes");Report();}
            }
            // PlayedMilliseconds conservatively subtracts the native 100ms buffer.
            await WaitForPlayback(()=>queuedMs>0 && engine.PlayedMilliseconds+100<queuedMs);
            if(queuedMs>0)await Task.Delay(150,ct);
        }
        using var captionLifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task Captions()
        {
            try
            {
                while(true)
                {
                    captionLifetime.Token.ThrowIfCancellationRequested();
                    while(captions.TryPeek(out var item) && item.Start<=engine.PlayedMilliseconds){captions.TryDequeue(out _);Caption?.Invoke(item.Text);}
                    await Task.Delay(40,captionLifetime.Token);
                }
            }
            catch(OperationCanceledException) when(captionLifetime.IsCancellationRequested){}
        }
        Task captionTask=Captions();
        try
        {
            await Task.WhenAll(Guard(Produce),Guard(SynthesizeAll),Guard(PlayAll));token.ThrowIfCancellationRequested();
            Report(asrMs+clock.ElapsedMilliseconds);Status?.Invoke(splitter.Truncated?"Listening · the full long reply is available in chat.":"Listening locally…");
        }
        catch(Exception ex)
        {
            engine.Clear();var cause=failure??ex;
            if(!token.IsCancellationRequested && cause is not OperationCanceledException && connected)Fail("Voice stopped. "+cause.Message);
            if(failure!=null && !token.IsCancellationRequested)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();throw;
        }
        finally{captionLifetime.Cancel();await captionTask;}
    }
}

// Sentence-sized chunks preserve punctuation across token boundaries. Fenced code
// is skipped; CJK punctuation, decimals and a short list of titles are recognized.
internal sealed class VoiceSentences
{
    private readonly StringBuilder pending=new();
    private readonly bool wholeReply;
    private bool fenced;
    private int ticks,spoken;
    internal bool Truncated {get;private set;}
    internal VoiceSentences(bool wholeReply=false)=>this.wholeReply=wholeReply;
    internal System.Collections.Generic.IEnumerable<string> Append(string text,bool final)
    {
        foreach(char c in text)
        {
            if(c=='`'){if(++ticks==3){fenced=!fenced;ticks=0;}continue;}ticks=0;
            if(!fenced)pending.Append(c);
        }
        // Legacy bundled Piper loads through its one-shot CLI. Keep one synthesis
        // for that path instead of reloading its weights once per sentence.
        if(wholeReply)
        {
            if(!final)yield break;
            string full=AssistantMarkdown.Spoken(pending.ToString());pending.Clear();Truncated=full.Length>6000;
            if(full.Length>0)yield return full[..Math.Min(full.Length,6000)];yield break;
        }
        while(pending.Length>0 && spoken<6000)
        {
            int boundary=0;
            for(int i=0;i<Math.Min(pending.Length,360);i++)
            {
                char c=pending[i];bool end=c is '。' or '！' or '？' or '\n' || (c is '.' or '!' or '?') && (i+1<pending.Length && char.IsWhiteSpace(pending[i+1]) || final && i+1==pending.Length);
                if(c=='.' && end)
                {
                    int start=i-1;while(start>=0 && char.IsLetter(pending[start]))start--;
                    string word=pending.ToString(start+1,i-start-1).ToLowerInvariant();if(word is "mr" or "mrs" or "ms" or "dr" or "prof" or "st" or "vs" or "etc")end=false;
                }
                if(end && i>=12){boundary=i+1;break;}
            }
            if(boundary==0 && pending.Length>360){boundary=360;while(boundary>200 && !char.IsWhiteSpace(pending[boundary-1]))boundary--;}
            if(boundary==0){if(!final)break;boundary=pending.Length;}
            if(boundary<pending.Length && boundary>0 && char.IsHighSurrogate(pending[boundary-1]))boundary--;
            string sentence=AssistantMarkdown.Spoken(pending.ToString(0,boundary));pending.Remove(0,boundary);
            if(sentence.Length==0)continue;
            int take=Math.Min(sentence.Length,6000-spoken);if(take<sentence.Length)Truncated=true;spoken+=take;
            if(take>0)yield return sentence[..take];
        }
        if(spoken>=6000 && pending.Length>0){Truncated=true;pending.Clear();}
    }
}
