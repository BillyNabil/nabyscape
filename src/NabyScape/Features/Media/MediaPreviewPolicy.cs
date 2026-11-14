using System;
namespace NabyScape;
internal static class MediaPreviewPolicy
{
    internal static bool IsBrowser(string? source)
    {
        string value=(source??"").ToLowerInvariant();
        foreach(string name in new[]{"chrome","msedge","microsoftedge","microsoft edge","firefox","brave","opera","vivaldi","arc","browser","aside","zen","floorp","thorium"})
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
    // Optional exclusion: browsers and unidentifiable (hashed) session identities never become media sources.
    internal static bool AllowSession(bool excludeBrowsers,string? source,string? displayName=null)
        =>!excludeBrowsers || !(IsBrowser(source) || IsBrowser(displayName) || OpaqueIdentity(source));
}
