using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace NabyScape;
internal static class TimedLyricsDocument
{
    internal static string ToLrc(string body)
    {
        if(body.Length>1024*1024)throw new InvalidDataException("Lyrics document is too large.");
        if(!body.TrimStart().StartsWith("<",StringComparison.Ordinal))return LyricTiming.Parse(body).Length>0?body:"";
        using var input=new StringReader(body);
        using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});
        var xml=XDocument.Load(reader,LoadOptions.PreserveWhitespace);var result=new StringBuilder();
        static double? Time(string? text)
        {
            if(string.IsNullOrWhiteSpace(text))return null;
            if(text.EndsWith("ms",StringComparison.Ordinal) && double.TryParse(text[..^2],NumberStyles.Float,CultureInfo.InvariantCulture,out var milliseconds))return double.IsFinite(milliseconds)&&milliseconds>=0 && milliseconds<=86400000?milliseconds/1000:null;
            if(text.EndsWith("s",StringComparison.Ordinal) && double.TryParse(text[..^1],NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds))return double.IsFinite(seconds)&&seconds>=0 && seconds<=86400?seconds:null;
            var parts=text.Split(':');double value=0;
            foreach(string part in parts){if(!double.TryParse(part,NumberStyles.Float,CultureInfo.InvariantCulture,out var n) || !double.IsFinite(n) || n<0)return null;value=value*60+n;}
            return value<=86400?value:null;
        }
        static string Stamp(double value){long centiseconds=(long)Math.Round(value*100);return "["+(centiseconds/6000).ToString("00",CultureInfo.InvariantCulture)+":"+((centiseconds%6000)/100d).ToString("00.00",CultureInfo.InvariantCulture)+"]";}
        // QQ documents store line starts and word durations in milliseconds.
        foreach(var attribute in xml.Descendants().Attributes("LyricContent").Take(1))
        {
            var pattern=new System.Text.RegularExpressions.Regex(@"\[(\d{1,8}),(\d{1,8})\]([^\[]*)",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
            foreach(System.Text.RegularExpressions.Match line in pattern.Matches(attribute.Value).Cast<System.Text.RegularExpressions.Match>().Take(5000))
            {
                double start=double.Parse(line.Groups[1].Value,CultureInfo.InvariantCulture)/1000;
                if(start>86400)continue;
                string text=System.Text.RegularExpressions.Regex.Replace(line.Groups[3].Value,@"\(\d+,\d+\)","",System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Trim();
                if(text.Length>0)result.Append(Stamp(start)).AppendLine(text);
            }
        }
        var paragraphs=xml.Descendants().Where(e=>e.Name.LocalName=="p").Take(5000).ToArray();
        for(int i=0;i<paragraphs.Length;i++)
        {
            var paragraph=paragraphs[i];
            var at=Time(paragraph.Attribute("begin")?.Value);if(at==null)continue;
            string text=string.Join(" ",paragraph.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));if(text.Length==0)continue;
            result.Append(Stamp(at.Value)).AppendLine(text);
            var end=Time(paragraph.Attribute("end")?.Value);
            var following=paragraphs.Skip(i+1).Select(p=>Time(p.Attribute("begin")?.Value)).FirstOrDefault(t=>t!=null);
            if(end>at && (following==null || following.Value-end.Value>1))result.AppendLine(Stamp(end.Value));
        }
        return result.ToString();
    }
}
