using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;

internal static class LyricsRomaji
{
    internal static bool ContainsJapanese(string text)=>text.Any(c=>c is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff' or >= '\uff66' and <= '\uff9f');
    internal static async Task<string[]> Convert(string[] lines,CancellationToken token)
    {
        if(lines.Length>5000 || lines.Sum(s=>s.Length)>512*1024)throw new InvalidDataException("Lyrics are too large for Romaji conversion.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(25));
        using var worker=new Process{StartInfo=new ProcessStartInfo(Environment.ProcessPath!,"--lyrics-romaji-worker")
        {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}};
        if(!worker.Start())throw new IOException("Could not start offline Romaji conversion.");
        using var cancellation=deadline.Token.Register(()=>{try{if(!worker.HasExited)worker.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
        try
        {
            await worker.StandardInput.WriteAsync(JsonSerializer.Serialize(lines).AsMemory(),deadline.Token);worker.StandardInput.Close();
            // Read bounded chunks rather than accepting arbitrary worker output into memory.
            var output=new StringBuilder();var buffer=new char[4096];int read;
            while((read=await worker.StandardOutput.ReadAsync(buffer.AsMemory(),deadline.Token))>0){if(output.Length+read>2*1024*1024)throw new InvalidDataException("Romaji output is too large.");output.Append(buffer,0,read);}
            await worker.WaitForExitAsync(deadline.Token);
            if(worker.ExitCode!=0)throw new IOException("Offline Romaji conversion failed. Original lyrics are still available.");
            var result=JsonSerializer.Deserialize<string[]>(output.ToString());
            if(result==null || result.Length!=lines.Length || result.Any(s=>s==null))throw new InvalidDataException("Invalid Romaji output.");
            return result;
        }
        finally{try{if(!worker.HasExited)worker.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
    }
}

internal sealed partial class LyricsService
{
    private string textMode="Original";
    private int textRevision;
    private LyricResult originalResult=new(Array.Empty<LyricLine>(),"Lyrics are off.");
    private CancellationTokenSource? romanizing;
    private readonly Dictionary<LyricResult,LyricResult> romanized=new();
    internal void SetTextMode(string mode)
    {
        mode=mode=="Romaji"?"Romaji":"Original";if(textMode==mode)return;
        textMode=mode;Present(originalResult);
    }
    private void Present(LyricResult result)
    {
        int revision=++textRevision;romanizing?.Cancel();romanizing=null;
        if(textMode!="Romaji" || !result.Lines.Any(l=>LyricsRomaji.ContainsJapanese(l.Text)) && !LyricsRomaji.ContainsJapanese(result.Plain)){Result=result;Changed?.Invoke();return;}
        if(romanized.TryGetValue(result,out var converted)){Result=converted;Changed?.Invoke();return;}
        Result=result with{Status="Preparing Romaji… · "+result.Status};Changed?.Invoke();
        var operation=new CancellationTokenSource();romanizing=operation;_=ConvertResult(result,revision,operation);
    }
    private async Task ConvertResult(LyricResult original,int revision,CancellationTokenSource operation)
    {
        try
        {
            string[] source=original.Lines.Select(l=>l.Text).Concat(new[]{original.Plain}).ToArray();
            string[] converted=await LyricsRomaji.Convert(source,operation.Token);
            if(disposed || operation.IsCancellationRequested || revision!=textRevision)return;
            var lines=original.Lines.Select((l,i)=>new LyricLine(l.At,converted[i])).ToArray();
            var result=new LyricResult(lines,"Romaji · "+original.Status,converted[^1]);
            if(romanized.Count>=4)romanized.Remove(romanized.Keys.First());romanized[original]=result;Result=result;Changed?.Invoke();
        }
        catch(Exception ex) when(ex is OperationCanceledException or IOException or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {if(!disposed && !operation.IsCancellationRequested && revision==textRevision){Result=original with{Status="Romaji unavailable · showing original lyrics"};Changed?.Invoke();}}
        finally{if(ReferenceEquals(romanizing,operation))romanizing=null;operation.Dispose();}
    }
}
