using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace NabyScape;

// Markdown text is rendered natively. Media is extracted by the transport and
// displayed separately; this renderer never executes HTML or scripts.
internal static class AssistantMarkdown
{
    private static readonly Regex inline=new(@"(`[^`\n]+`|\*\*[^*\n]+\*\*|__[^_\n]+__|\*[^*\n]+\*|\[[^\]\n]+\]\(https?://[^\s)]+\))",RegexOptions.Compiled,TimeSpan.FromMilliseconds(100));
    internal static FrameworkElement Render(string source)
    {
        try{return RenderCore(source);}catch(RegexMatchTimeoutException){return UI.Text(source[..Math.Min(source.Length,24000)],13);}
    }
    private static FrameworkElement RenderCore(string source)
    {
        var page=new StackPanel();string text=source.Length>24000?source[..24000]+"\n\n[Long reply shortened here. Use Copy message for the full text.]":source;
        bool fenced=false;var code=new StringBuilder();
        void Code()
        {
            var box=UI.Input("Code block",code.ToString().TrimEnd());box.IsReadOnly=true;box.AcceptsReturn=true;box.TextWrapping=TextWrapping.Wrap;box.FontFamily=new FontFamily("Cascadia Mono, Consolas");box.FontSize=11;box.MaxHeight=240;box.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;box.Background=UI.Brush("#171B22");box.BorderThickness=new(0);box.Padding=new(10);box.Margin=new(0,7,0,9);page.Children.Add(box);code.Clear();
        }
        foreach(string raw in text.Replace("\r","").Split('\n').Take(600))
        {
            string line=raw.TrimEnd();if(line.TrimStart().StartsWith("```")){if(fenced)Code();fenced=!fenced;continue;}
            if(fenced){code.AppendLine(line);continue;}
            if(line.Length==0){page.Children.Add(new Border{Height=6});continue;}
            if(Regex.IsMatch(line,@"^\s*[-*_]{3,}\s*$",RegexOptions.None,TimeSpan.FromMilliseconds(100))){page.Children.Add(new Border{Height=1,Background=UI.Brush("#343A45"),Margin=new(0,7,0,7)});continue;}
            int heading=line.TakeWhile(c=>c=='#').Count();bool title=heading is >=1 and <=6 && line.Length>heading && line[heading]==' ';
            var label=UI.Text("",title?heading<=2?17:14:13);label.LineHeight=title?24:20;label.Margin=new(0,title?10:2,0,title?5:2);label.VerticalAlignment=VerticalAlignment.Top;
            if(title){line=line[(heading+1)..];label.FontWeight=FontWeights.SemiBold;}
            bool quote=line.TrimStart().StartsWith(">");if(quote){line=line.TrimStart()[1..].TrimStart();label.Foreground=UI.Muted;}
            var bullet=Regex.Match(line,@"^\s*(?:[-*+] |(\d+)\. )(.*)$",RegexOptions.None,TimeSpan.FromMilliseconds(100));
            if(bullet.Success){line=(bullet.Groups[1].Success?bullet.Groups[1].Value+". ":"• ")+bullet.Groups[2].Value;label.Margin=new(10,3,0,3);}
            Fill(label,line);page.Children.Add(quote?new Border{Child=label,BorderBrush=UI.Brush("#5C6C81"),BorderThickness=new(2,0,0,0),Padding=new(10,0,0,0),Margin=new(0,4,0,4)}:label);
        }
        if(fenced && code.Length>0)Code();return page;
    }
    private static void Fill(TextBlock target,string text)
    {
        int end=0;foreach(Match match in inline.Matches(text))
        {
            if(match.Index>end)target.Inlines.Add(new Run(text[end..match.Index]));string value=match.Value;
            if(value.StartsWith('`'))target.Inlines.Add(new Run(value[1..^1]){FontFamily=new FontFamily("Consolas"),Background=UI.Brush("#282E38")});
            else if(value.StartsWith("**") || value.StartsWith("__"))target.Inlines.Add(new Bold(new Run(value[2..^2])));
            else if(value.StartsWith('*'))target.Inlines.Add(new Italic(new Run(value[1..^1])));
            else
            {
                int split=value.IndexOf("](",StringComparison.Ordinal);string url=value[(split+2)..^1];var link=new Hyperlink(new Run(value[1..split])){Foreground=UI.Brush("#ADC8EA"),ToolTip=url};
                link.Click+=(_,_)=>{try{if(Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length==0)ShortcutService.Open(new(){Target=url});}catch{}};target.Inlines.Add(link);
            }
            end=match.Index+match.Length;
        }
        if(end<text.Length)target.Inlines.Add(new Run(text[end..]));
    }
    internal static string Spoken(string text)
    {
        text=text[..Math.Min(text.Length,12000)];
        try{return Regex.Replace(Regex.Replace(text,@"```[\s\S]*?```"," ",RegexOptions.None,TimeSpan.FromMilliseconds(100)),@"[#*`_>]+","",RegexOptions.None,TimeSpan.FromMilliseconds(100)).Trim();}
        catch(RegexMatchTimeoutException){return text;}
    }
}
