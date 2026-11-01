using System;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NTextCat;
namespace NabyScape;

internal static class LyricsLanguage
{
    private static readonly Lazy<RankedLanguageIdentifier> identifier=new(()=>
    {
        using var resource=typeof(LyricsLanguage).Assembly.GetManifestResourceStream("NabyScape.LyricsLanguages")??throw new InvalidOperationException("Language profile is missing.");
        using var profile=new GZipStream(resource,CompressionMode.Decompress);
        return new RankedLanguageIdentifierFactory().Load(profile);
    });
    internal static string Detect(string lrc)
    {
        var sample=new StringBuilder();
        foreach(string line in LyricTiming.Parse(lrc).Select(l=>l.Text).Where(t=>t.Length>0 && !t.StartsWith('[') && !t.StartsWith('(')).Distinct().Take(80))
        {sample.AppendLine(line);if(sample.Length>=4000)break;}
        string text=sample.ToString();if(text.Length>4000)text=text[..4000];
        int letters=text.Count(char.IsLetter);if(letters==0)return "und";
        int kana=text.Count(c=>c is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9f');
        int hangul=text.Count(c=>c is >= '\uac00' and <= '\ud7af' or >= '\u1100' and <= '\u11ff');
        int other=text.Count(c=>char.IsLetter(c) && !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '\u00c0' and <= '\u024f' or >= '\u1e00' and <= '\u1eff' or >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff' or >= '\u1100' and <= '\u11ff' or >= '\uac00' and <= '\ud7af' or >= '\uff66' and <= '\uff9f'));
        if((double)other/letters>.15)return "und";
        if(kana>=2 && (double)kana/letters>=.12)return "jpn";
        if(hangul>=2 && (double)hangul/letters>=.12)return "kor";
        var model=identifier.Value;
        lock(model)return model.Identify(text).FirstOrDefault()?.Item1.Iso639_3??"und";
    }
    internal static bool Allowed(string lrc)=>Detect(lrc) is "eng" or "jpn" or "kor";
}
