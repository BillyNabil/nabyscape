using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NabyScape;

public enum SmartClipKind { Text, Link, Image, Color }

/// <summary>Derived presentation only: the original clipboard text and persisted kind are never changed.</summary>
public sealed record SmartClip(SmartClipKind Kind,string Hex="",string Rgb="",string CleanedLink="");

public static class SmartClipboard
{
    private static readonly Regex HexPattern=new(@"\A#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})\z",RegexOptions.CultureInvariant);
    private static readonly Regex RgbPattern=new(@"\Argb\(\s*([0-9]{1,3})\s*,\s*([0-9]{1,3})\s*,\s*([0-9]{1,3})\s*\)\z",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);
    public static SmartClip Classify(ClipItem item)
    {
        if(item.Kind=="image")return new(SmartClipKind.Image);
        var text=item.Text ?? "";
        int r,g,b;
        if(HexPattern.IsMatch(text))
        {
            var digits=text[1..];
            if(digits.Length==3)digits=$"{digits[0]}{digits[0]}{digits[1]}{digits[1]}{digits[2]}{digits[2]}";
            r=int.Parse(digits[..2],NumberStyles.HexNumber,CultureInfo.InvariantCulture);
            g=int.Parse(digits[2..4],NumberStyles.HexNumber,CultureInfo.InvariantCulture);
            b=int.Parse(digits[4..],NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        }
        else
        {
            var rgb=RgbPattern.Match(text);
            if(!rgb.Success || !int.TryParse(rgb.Groups[1].Value,out r) || !int.TryParse(rgb.Groups[2].Value,out g) || !int.TryParse(rgb.Groups[3].Value,out b) || r>255 || g>255 || b>255)
            {
                try { return new(SmartClipKind.Link,CleanedLink:CleanLink.Clean(text)); }
                catch(ArgumentException) { return new(SmartClipKind.Text); }
                catch(UriFormatException) { return new(SmartClipKind.Text); }
            }
        }
        return new(SmartClipKind.Color,$"#{r:X2}{g:X2}{b:X2}",$"rgb({r}, {g}, {b})");
    }
}
