using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace NabyScape;

// Who wants a forecast right now. None never downloads; Background keeps an enabled widget/hover page
// reasonably current; Visible is a weather surface that is on screen.
internal enum WeatherDemand {None,Background,Visible}
internal readonly record struct WeatherPlan(WeatherDemand Demand,DateTimeOffset? Updated,DateTimeOffset LastAttempt,int Failures,bool Updating,bool Away=false,bool Nudged=false);

// Pure refresh and presentation rules, shared by WeatherService, the views and the self-tests.
internal static class WeatherSchedule
{
    internal static readonly TimeSpan Fresh=TimeSpan.FromMinutes(20),Background=TimeSpan.FromMinutes(30),Stale=TimeSpan.FromMinutes(45);
    // A saved time this far ahead of the clock means the clock changed; its age is unknown.
    internal static readonly TimeSpan FutureSkew=TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MinimumSpacing=TimeSpan.FromMinutes(1),MaximumRetry=TimeSpan.FromMinutes(30);
    internal static TimeSpan Age(DateTimeOffset now,DateTimeOffset updated)=>updated>now+FutureSkew?TimeSpan.MaxValue:updated>now?TimeSpan.Zero:now-updated;
    // 1, 2, 4, 8, 16, then every 30 minutes while the connection keeps failing.
    internal static TimeSpan Retry(int failures)=>failures<=0?TimeSpan.Zero:failures>=6?MaximumRetry:TimeSpan.FromMinutes(Math.Pow(2,failures-1));
    internal static bool Waiting(DateTimeOffset now,DateTimeOffset lastAttempt,int failures)=>failures>0 && now>=lastAttempt && now-lastAttempt<Retry(failures);
    internal static bool Due(WeatherPlan p,DateTimeOffset now)
    {
        if(p.Demand==WeatherDemand.None || p.Updating || p.Demand==WeatherDemand.Background && p.Away && !p.Nudged)return false;
        // A clock moved backwards never blocks a refresh; a resume or network nudge skips the failure backoff once.
        if(!p.Nudged && (Waiting(now,p.LastAttempt,p.Failures) || now>=p.LastAttempt && now-p.LastAttempt<MinimumSpacing))return false;
        if(p.Updated is not {} updated)return true;
        return Age(now,updated)>=(p.Demand==WeatherDemand.Visible || p.Nudged?Fresh:Background);
    }
    internal static string Ago(TimeSpan age)=>age<TimeSpan.FromHours(1)?Localization.Format("{0} min ago",Math.Max(1,(int)age.TotalMinutes)):age<TimeSpan.FromDays(2)?Localization.Format("{0} h ago",(int)age.TotalHours):Localization.Format("{0} d ago",(int)Math.Min(999,age.TotalDays));
    // Null while recent; otherwise how old the outlook is, or "Saved outlook" when a clock change hides its age.
    internal static string? StaleLabel(DateTimeOffset now,DateTimeOffset updated){var age=Age(now,updated);return age<Stale?null:age==TimeSpan.MaxValue?Localization.Text("Saved outlook"):Ago(age);}
    // Honest status line: clock time while recent, age once stale, and an unknown age after a clock change.
    internal static string Status(DateTimeOffset now,DateTimeOffset? updated,bool updating,bool failed,bool clock24=true)
    {
        if(updating)return Localization.Text("Updating weather…");
        if(updated is not {} at)return failed?Localization.Text("Weather unavailable. Refresh to try again."):"";
        string label=StaleLabel(now,at) is {} stale?(Age(now,at)==TimeSpan.MaxValue?stale:Localization.Format("Updated {0}",stale)):Localization.Format("Updated {0}",at.ToLocalTime().ToString(clock24?"HH:mm":"h:mm tt",CultureInfo.GetCultureInfo("en-US")));
        return failed?Localization.Format("{0} · connection unavailable",label):label;
    }
    // Open-Meteo hourly times are wall-clock times at the forecast location (timezone=auto). Compare
    // them with the location's current time, never this PC's time zone, so saved outlooks drop past hours.
    internal static List<WeatherHour> Upcoming(WeatherSnapshot data,DateTimeOffset now,int count=6)
    {
        if(data.Hours.Count==0 || data.Hours.Any(h=>h.Stamp.Length==0))return data.Hours.Take(count).ToList();
        var local=now.UtcDateTime.AddSeconds(data.UtcOffsetSeconds);var minute=new DateTime(local.Year,local.Month,local.Day,local.Hour,local.Minute,0);
        return data.Hours.Where(h=>LocalStamp(h.Stamp) is {} at && at>=minute).Take(count).ToList();
    }
    internal static DateTime? LocalStamp(string value)=>DateTime.TryParseExact(value,"yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var at)?at:null;
}
