using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Lucene.Net.Analysis.Cjk;
using Lucene.Net.Analysis.Ja;
using Lucene.Net.Analysis.Ja.TokenAttributes;
using Lucene.Net.Analysis.TokenAttributes;
namespace NabyScape;

// The dictionary lives only in this short-lived process, never in the dock process.
internal static class LyricsRomajiWorker
{
    internal static void Run()
    {
        Console.InputEncoding=Console.OutputEncoding=new UTF8Encoding(false);
        try
        {
            var body=new StringBuilder();var buffer=new char[4096];int read;
            while((read=Console.In.Read(buffer,0,buffer.Length))>0){if(body.Length+read>2*1024*1024)throw new InvalidDataException();body.Append(buffer,0,read);}
            var lines=JsonSerializer.Deserialize<string[]>(body.ToString())??throw new InvalidDataException();
            if(lines.Length>5000)throw new InvalidDataException();int length=0;
            for(int i=0;i<lines.Length;i++){if(lines[i]==null || (length+=lines[i].Length)>512*1024)throw new InvalidDataException();lines[i]=RomanizeLine(lines[i]);}
            Console.Out.Write(JsonSerializer.Serialize(lines));
        }
        catch{Environment.ExitCode=1;}
    }
    internal static string RomanizeLine(string line)
    {
        if(!LyricsRomaji.ContainsJapanese(line))return line;
        using var reader=new StringReader(line);
        using var tokenizer=new JapaneseTokenizer(reader,null,false,JapaneseTokenizerMode.NORMAL);
        using var width=new CJKWidthFilter(tokenizer);
        using var stream=new JapaneseReadingFormFilter(width,true);
        var term=stream.AddAttribute<ICharTermAttribute>();var offset=stream.AddAttribute<IOffsetAttribute>();var part=stream.AddAttribute<IPartOfSpeechAttribute>();
        var result=new StringBuilder();int previous=0;stream.Reset();
        while(stream.IncrementToken())
        {
            int start=offset.StartOffset,end=offset.EndOffset;if(start<previous || end>line.Length)continue;
            string surface=line[start..end];string word=LyricsRomaji.ContainsJapanese(surface)?term.ToString():surface;
            if((part.GetPartOfSpeech()??"").StartsWith("助詞",StringComparison.Ordinal))word=surface switch{"は"=>"wa","へ"=>"e","を"=>"o",_=>word};
            result.Append(line.AsSpan(previous,start-previous));
            if(start==previous && result.Length>0 && word.Length>0 && char.IsLetterOrDigit(result[^1]) && char.IsLetterOrDigit(word[0]))result.Append(' ');
            result.Append(word);previous=end;
        }
        stream.End();result.Append(line.AsSpan(previous));return result.ToString();
    }
}
