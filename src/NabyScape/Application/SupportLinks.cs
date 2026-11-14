using System;
namespace NabyScape;

// Voluntary donations only. NabyScape stays free; nothing is unlocked, tracked or sent.
// Links open in the default browser; the donation pages belong to Saweria and Ko-fi.
internal static class SupportLinks
{
    internal const string Saweria="https://saweria.co/billynabil";
    internal const string KoFi="https://ko-fi.com/billynabil";
    internal static void Open(string url,Action<Exception>? log=null)
    {
        try{ShortcutService.Open(new(){Target=url});}catch(Exception ex){log?.Invoke(ex);}
    }
}
