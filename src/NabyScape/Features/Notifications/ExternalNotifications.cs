using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NabyScape;

internal sealed record ExternalToast(uint Id,string App,string AppId,string Title,string Body);

internal static class ExternalNotificationPolicy
{
    // NabyScape's own tray balloons also land in Action Center; never mirror them back.
    internal static bool IsOwn(string app,string appId)=>new[]{app,appId}.Any(v=>v.Contains("NabyScape",StringComparison.OrdinalIgnoreCase) || v.Contains("LumaIsland",StringComparison.OrdinalIgnoreCase));
    internal static bool Allowed(Settings s,string app)=>!s.ExternalNotificationMuted.Contains(app,StringComparer.OrdinalIgnoreCase);
    internal static (string Title,string Body) Split(string app,IReadOnlyList<string> lines)
    {
        var text=lines.Select(l=>l.Trim()).Where(l=>l.Length>0).ToArray();
        return text.Length==0?(app,""):(text[0],string.Join(" · ",text.Skip(1)));
    }
    // Only ids missing from the previous snapshot are new; the first snapshot is a baseline.
    internal static uint[] Fresh(IEnumerable<uint> current,HashSet<uint>? previous)=>previous==null?Array.Empty<uint>():current.Where(id=>!previous.Contains(id)).ToArray();
}

// Unpackaged apps can read Action Center but cannot subscribe to its change event
// (0x80070490), so the watcher compares snapshots on a short timer while enabled.
internal sealed class ExternalNotificationWatcher : IDisposable
{
    private readonly DispatcherTimer timer=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(1500)};
    private readonly Action<Exception> log;
    private HashSet<uint>? seen;
    private bool polling;
    internal string Status { get; private set; }="Off";
    internal event Action<ExternalToast>? Arrived;
    internal event Action? StatusChanged;
    internal ExternalNotificationWatcher(Action<Exception> log){this.log=log;timer.Tick+=async(_,_)=>await Poll();}
    internal bool Running=>timer.IsEnabled;
    internal void Configure(bool enabled)
    {
        if(enabled==timer.IsEnabled)return;
        seen=null;
        if(enabled){timer.Start();_=Poll();}
        else{timer.Stop();SetStatus("Off");}
    }
    private void SetStatus(string value){if(Status==value)return;Status=value;StatusChanged?.Invoke();}
    internal static async Task<UserNotificationListenerAccessStatus> AccessAsync()
    {
        var listener=UserNotificationListener.Current;
        var access=listener.GetAccessStatus();
        return access==UserNotificationListenerAccessStatus.Unspecified?await listener.RequestAccessAsync():access;
    }
    private async Task Poll()
    {
        if(polling || !timer.IsEnabled)return;polling=true;
        try
        {
            if(await AccessAsync()!=UserNotificationListenerAccessStatus.Allowed){SetStatus("Blocked by Windows");seen=null;return;}
            var list=await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast);
            if(!timer.IsEnabled)return;
            var fresh=ExternalNotificationPolicy.Fresh(list.Select(n=>n.Id),seen).ToHashSet();
            seen=list.Select(n=>n.Id).ToHashSet();SetStatus("On");
            foreach(var item in list.Where(n=>fresh.Contains(n.Id)).OrderBy(n=>n.CreationTime))
            {
                var toast=Read(item);if(toast==null || ExternalNotificationPolicy.IsOwn(toast.App,toast.AppId))continue;
                await ExternalAppIcons.LoadAsync(item);
                Arrived?.Invoke(toast);
            }
        }
        catch(Exception ex){SetStatus("Unavailable");log(ex);}
        finally{polling=false;}
    }
    internal static ExternalToast? Read(UserNotification item)
    {
        try
        {
            string app="App",appId="";
            try{app=item.AppInfo?.DisplayInfo?.DisplayName is {Length:>0} name?name:"App";appId=item.AppInfo?.AppUserModelId??"";}catch{/* Some senders expose no app information. */}
            var binding=item.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var lines=binding?.GetTextElements().Select(t=>t.Text??"").ToArray()??Array.Empty<string>();
            var (title,body)=ExternalNotificationPolicy.Split(app,lines);
            return new(item.Id,app,appId,title,body);
        }
        catch{return null;}
    }
    // Apps that currently have alerts in Action Center, for the per-app switches in Settings.
    internal static async Task<string[]> CurrentAppsAsync()
    {
        if(await AccessAsync()!=UserNotificationListenerAccessStatus.Allowed)return Array.Empty<string>();
        var list=await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast);
        return list.Select(Read).Where(t=>t!=null && !ExternalNotificationPolicy.IsOwn(t.App,t.AppId)).Select(t=>t!.App).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public void Dispose(){timer.Stop();}
}

// App logos for mirrored alerts, cached per sender for this session.
internal static class ExternalAppIcons
{
    private static readonly Dictionary<string,ImageSource?> cache=new(StringComparer.OrdinalIgnoreCase);
    internal static ImageSource? Get(string? appId){if(string.IsNullOrEmpty(appId))return null;lock(cache)return cache.TryGetValue(appId,out var image)?image:null;}
    internal static async Task LoadAsync(UserNotification item)
    {
        string appId;try{appId=item.AppInfo?.AppUserModelId??"";}catch{return;}
        if(appId.Length==0)return;lock(cache)if(cache.ContainsKey(appId))return;
        ImageSource? image=null;
        try
        {
            var logo=item.AppInfo!.DisplayInfo.GetLogo(new Windows.Foundation.Size(48,48));
            using var stream=await logo.OpenReadAsync();
            using var source=stream.AsStreamForRead();var copy=new MemoryStream();await source.CopyToAsync(copy);copy.Position=0;
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=48;bitmap.StreamSource=copy;bitmap.EndInit();bitmap.Freeze();image=bitmap;
        }
        catch{/* Fall back to the bell glyph. */}
        lock(cache)cache[appId]=image;
    }
}

internal static class ExternalAppLauncher
{
    // Start-menu apps (packaged and most desktop apps) open through their AppUserModelID.
    internal static bool Open(string? appId)
    {
        if(string.IsNullOrWhiteSpace(appId))return false;
        try{var info=new System.Diagnostics.ProcessStartInfo("explorer.exe"){UseShellExecute=false};info.ArgumentList.Add("shell:AppsFolder\\"+appId);System.Diagnostics.Process.Start(info);return true;}
        catch{return false;}
    }
}
