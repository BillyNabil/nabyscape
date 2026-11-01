using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;
internal static class LyricsRomajiTests
{
    private static void Assert(bool value){if(!value)throw new Exception("Romaji lyrics assertion failed.");}
    internal static void Run(Action<string,Action> test)
    {
        test("Offline Romaji worker reads kanji and particles and preserves non-Japanese text",()=>Task.Run(async()=>
        {
            var result=await LyricsRomaji.Convert(new[]{"君の声が聞こえる","私は学校へ行く","Hello, world!","[Instrumental]",""},CancellationToken.None);
            Assert(result.Length==5 && result[0]=="kimi no koe ga kikoeru" && result[1].Contains("wa",StringComparison.Ordinal) && result[1].Contains(" e iku",StringComparison.Ordinal));
            Assert(result[2]=="Hello, world!" && result[3]=="[Instrumental]" && result[4]=="");
        }).GetAwaiter().GetResult());
        test("Romaji display preserves line timestamps and waveform breaks and restores the original immediately",()=>Task.Run(async()=>
        {
            using var service=new LyricsService();service.SetTextMode("Romaji");var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Changed+=()=>{if(service.Result.Status.StartsWith("Romaji ·",StringComparison.Ordinal))ready.TrySetResult();};
            service.Import("Fixture","Artist",100,"[00:01.25]君の声が聞こえる\n[00:04][Instrumental]\n[00:06]Hello, world!");
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));var converted=service.Result;
            Assert(converted.Lines[0].At==1.25 && converted.Lines[0].Text=="kimi no koe ga kikoeru" && LyricTiming.VocalIndex(converted.Lines,4)==-1 && converted.Lines[2].At==6 && converted.Lines[2].Text=="Hello, world!");
            service.SetTextMode("Original");Assert(service.Result.Lines[0].Text=="君の声が聞こえる");
            service.SetTextMode("Romaji");Assert(ReferenceEquals(service.Result,converted));
        }).GetAwaiter().GetResult());
        test("Switching back cancels pending Romaji without overwriting original lyrics",()=>Task.Run(async()=>
        {
            using var service=new LyricsService();service.SetTextMode("Romaji");service.Import("Fixture","Artist",100,"[00:01]君の声が聞こえる");
            service.SetTextMode("Original");await Task.Delay(150);Assert(service.Result.Lines[0].Text=="君の声が聞こえる" && !service.Result.Status.Contains("Romaji",StringComparison.Ordinal));
        }).GetAwaiter().GetResult());
        test("Lyric text preferences persist and normalize unsupported values",()=>
        {
            var settings=new Settings{LyricsTextMode="Romaji"};settings.Normalize();var saved=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;saved.Normalize();Assert(saved.LyricsTextMode=="Romaji");
            saved.LyricsTextMode="invalid";saved.Normalize();Assert(saved.LyricsTextMode=="Original");
        });
        test("Romaji input is bounded before a worker starts",()=>Task.Run(async()=>
        {
            bool rejected=false;try{await LyricsRomaji.Convert(Enumerable.Repeat("a",5001).ToArray(),CancellationToken.None);}catch(InvalidDataException){rejected=true;}Assert(rejected);
        }).GetAwaiter().GetResult());
    }
}
