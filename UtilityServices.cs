using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace NabyScape;

/// <summary>Call on the creating UI thread: Windows execution requests belong to that thread.</summary>
public sealed class KeepAwakeService : IDisposable
{
    private const uint Continuous=0x80000000, SystemRequired=1, DisplayRequired=2;
    private readonly Func<uint,uint> setExecutionState;
    private readonly int ownerThread=Environment.CurrentManagedThreadId;
    private bool disposed;
    public bool Active { get; private set; }
    public DateTimeOffset? Until { get; private set; }
    public bool KeepDisplayOn { get; private set; }
    public event Action? Changed;

    public KeepAwakeService(Func<uint,uint>? setExecutionState=null) =>
        this.setExecutionState=setExecutionState ?? SetThreadExecutionState;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    private void CheckThread()
    {
        if(Environment.CurrentManagedThreadId!=ownerThread)
            throw new InvalidOperationException("Keep awake must be controlled on its creating UI thread.");
    }
    private void CheckUsable()
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(disposed,this);
    }
    public void Start(TimeSpan? duration,bool keepDisplayOn,DateTimeOffset now)
    {
        CheckUsable();
        if(duration.HasValue && (duration.Value<=TimeSpan.Zero || duration.Value>TimeSpan.FromHours(24)))
            throw new ArgumentException("Choose a keep-awake duration greater than zero and at most 24 hours.",nameof(duration));
        var until=duration.HasValue ? now.Add(duration.Value) : (DateTimeOffset?)null;
        if(setExecutionState(Continuous|SystemRequired|(keepDisplayOn?DisplayRequired:0))==0)
            throw new InvalidOperationException("Windows could not enable keep awake. Try again from the island.");
        Active=true; Until=until; KeepDisplayOn=keepDisplayOn; Changed?.Invoke();
    }
    public void Stop()
    {
        CheckUsable();
        if(!Active)return;
        if(setExecutionState(Continuous)==0)
            throw new InvalidOperationException("Windows could not release keep awake. Try stopping it again before quitting.");
        Active=false; Until=null; KeepDisplayOn=false; Changed?.Invoke();
    }
    public void Tick(DateTimeOffset now)
    {
        CheckUsable();
        if(Active && Until.HasValue && now>=Until.Value)Stop();
    }
    public void Dispose()
    {
        CheckThread();
        if(disposed)return;
        Stop();
        disposed=true;
    }
}

public static class CleanLink
{
    private static readonly HashSet<string> TrackingKeys=new(StringComparer.OrdinalIgnoreCase)
    { "fbclid","gclid","dclid","msclkid","mc_cid","mc_eid","igshid","_ga","_gl" };
    private static readonly HashSet<string> SignatureKeys=new(StringComparer.OrdinalIgnoreCase)
    { "X-Amz-Signature","X-Goog-Signature","Signature","sig","oauth_signature","hmac" };

    internal static string ValidateUrl(string input,int maximumLength,string parameter)
    {
        var value=input?.Trim() ?? "";
        if(value.Length==0 || value.Length>maximumLength ||
            !Uri.TryCreate(value,UriKind.Absolute,out var uri) ||
            (uri.Scheme!=Uri.UriSchemeHttp && uri.Scheme!=Uri.UriSchemeHttps) ||
            uri.Host.Length==0 || uri.UserInfo.Length!=0 || !uri.IsWellFormedOriginalString())
            throw new ArgumentException($"Enter a complete HTTP or HTTPS URL without credentials, up to {maximumLength} characters.",parameter);
        return value;
    }
    public static string Clean(string input)
    {
        var value=ValidateUrl(input,8192,nameof(input));
        int fragment=value.IndexOf('#');
        int end=fragment<0?value.Length:fragment;
        int query=value.IndexOf('?');
        if(query<0 || query>end)return value;
        var parameters=value.Substring(query+1,end-query-1).Split('&');
        var keys=parameters.Select(part=>Uri.UnescapeDataString(part.Split('=',2)[0])).ToArray();
        if(keys.Any(SignatureKeys.Contains))return value;
        var remaining=parameters.Where((_,index)=>!keys[index].StartsWith("utm_",StringComparison.OrdinalIgnoreCase) && !TrackingKeys.Contains(keys[index])).ToArray();
        if(remaining.Length==parameters.Length)return value;
        return value[..query]+(remaining.Length>0?"?"+string.Join("&",remaining):"")+(fragment<0?"":value[fragment..]);
    }
}

public sealed class CalendarEvent
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public string MeetingUrl { get; init; } = "";
}

public sealed class CalendarService
{
    private readonly Store store;
    private List<CalendarEvent> events;
    public IReadOnlyList<CalendarEvent> Events => events.AsReadOnly();
    public event Action? Changed;

    public CalendarService(Store store)
    {
        this.store=store ?? throw new ArgumentNullException(nameof(store));
        events=new();
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in store.Load<List<CalendarEvent>>("calendar"))
        {
            if(item==null)continue;
            try
            {
                var title=(item.Title ?? "").Trim();
                if(title.Length>120)title=title[..120];
                var entry=Create(title,item.Start,item.End,item.MeetingUrl ?? "",item.Id);
                if(ids.Add(entry.Id))events.Add(entry);
                if(events.Count==200)break;
            }
            catch(ArgumentException) { /* Invalid stored entries are excluded; loading never writes the source. */ }
        }
    }
    private static CalendarEvent Create(string title,DateTimeOffset start,DateTimeOffset end,string meetingUrl,string? id=null)
    {
        title=title?.Trim() ?? "";
        if(title.Length is <1 or >120)
            throw new ArgumentException("Enter an event title between 1 and 120 characters.",nameof(title));
        if(end<=start || end-start>TimeSpan.FromDays(31))
            throw new ArgumentException("Choose an end after the start, within 31 days.",nameof(end));
        meetingUrl=meetingUrl?.Trim() ?? "";
        if(meetingUrl.Length>0)meetingUrl=CleanLink.ValidateUrl(meetingUrl,2048,nameof(meetingUrl));
        return new CalendarEvent { Id=string.IsNullOrWhiteSpace(id)?Guid.NewGuid().ToString("N"):id,
            Title=title,Start=start,End=end,MeetingUrl=meetingUrl };
    }
    public CalendarEvent Add(string title,DateTimeOffset start,DateTimeOffset end,string meetingUrl)
    {
        if(events.Count>=200)throw new InvalidOperationException("The calendar is full. Remove an event before adding another (200 maximum).");
        var entry=Create(title,start,end,meetingUrl);
        var next=new List<CalendarEvent>(events) { entry };
        store.Save("calendar",next);
        events=next; Changed?.Invoke(); return entry;
    }
    public void Remove(string id)
    {
        var next=events.Where(item=>item.Id!=id).ToList();
        if(next.Count==events.Count)return;
        store.Save("calendar",next);
        events=next; Changed?.Invoke();
    }
}
