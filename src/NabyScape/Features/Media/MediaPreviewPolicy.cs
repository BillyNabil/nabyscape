using System;
namespace NabyScape;
internal static class MediaPreviewPolicy
{
    internal static bool IsBrowser(string? source)
    {
        string value=(source??"").ToLowerInvariant();
        foreach(string name in new[]{"chrome","msedge","firefox","brave","opera","vivaldi","arc","browser"})
            if(value.Contains(name,StringComparison.Ordinal))return true;
        return false;
    }
    internal static bool OpaqueIdentity(string? source)
    {
        if(Guid.TryParse(source,out _))return true;
        if(string.IsNullOrEmpty(source) || source.Length<12)return false;
        foreach(char c in source)if(!Uri.IsHexDigit(c))return false;
        return true;
    }
    internal static bool AllowAutomatic(Settings settings,string? source)=>settings.BrowserMediaPreviews || !(IsBrowser(source) || OpaqueIdentity(source));
}
