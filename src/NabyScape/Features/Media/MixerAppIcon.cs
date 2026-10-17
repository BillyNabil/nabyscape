using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

// A stable local identity remains visible while shell extraction is pending or unavailable.
internal sealed class MixerAppIcon : Grid
{
    internal static string Monogram(string name)
    {
        var words=name.Split(new[]{' ','-','_'},StringSplitOptions.RemoveEmptyEntries);
        string letters=string.Concat(words.Select(w=>w.FirstOrDefault(char.IsLetterOrDigit)).Where(c=>c!='\0').Take(2));
        if(words.Length==1)letters=string.Concat(words[0].Where(char.IsLetterOrDigit).Take(2));
        return letters.Length==0?"•":letters.ToUpperInvariant();
    }
    internal MixerAppIcon(MixerItem item)
    {
        Width=Height=20;IsHitTestVisible=false;
        var text=UI.Text(Monogram(item.Name),9,UI.Brush("#DFE1E7"));text.FontWeight=FontWeights.SemiBold;text.HorizontalAlignment=HorizontalAlignment.Center;text.VerticalAlignment=VerticalAlignment.Center;
        Children.Add(new Border{CornerRadius=new(5),Background=UI.Brush("#27292F"),BorderBrush=UI.Brush("#41444D"),BorderThickness=new(.7),Child=text});
        bool requested=false;
        Loaded+=async(_,_)=>
        {
            if(requested || string.IsNullOrWhiteSpace(item.ProcessPath))return;requested=true;
            var source=await AppIconCache.GetAsync(new Shortcut{Name=item.Name,Target=item.ProcessPath});
            if(source==null)return;
            Children.Clear();Children.Add(new Image{Source=source,Width=20,Height=20,Stretch=Stretch.Uniform});
        };
    }
}
