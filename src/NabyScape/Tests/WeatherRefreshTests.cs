using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;
// Issue #13: weather refresh scheduling, honest status and location-time hourly filtering. No network is used.
internal static class WeatherRefreshTests
{
    internal static string Forecast(int offset,string current,DateTime firstHour,int hours=48)
    {
        string Join(Func<int,string> value)=>string.Join(",",Enumerable.Range(0,hours).Select(value));
        return "{\"utc_offset_seconds\":"+offset.ToString(CultureInfo.InvariantCulture)+",\"current\":{\"time\":\""+current+"\",\"temperature_2m\":30.8,\"relative_humidity_2m\":61,\"apparent_temperature\":35.7,\"is_day\":1,\"weather_code\":3,\"wind_speed_10m\":1.7},"+
            "\"hourly\":{\"time\":["+Join(i=>"\""+firstHour.AddHours(i).ToString("yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture)+"\"")+"],\"temperature_2m\":["+Join(i=>(20+i%10).ToString(CultureInfo.InvariantCulture))+"],\"weather_code\":["+Join(_=>"2")+"],\"is_day\":["+Join(i=>(i%24 is >=6 and <18?"1":"0"))+"],\"precipitation_probability\":["+Join(_=>"10")+"]},"+
            "\"daily\":{\"temperature_2m_max\":[31,32],\"temperature_2m_min\":[24,25],\"precipitation_probability_max\":[40,20],\"sunrise\":[\"2026-11-14T05:30\",\"2026-11-15T05:30\"],\"sunset\":[\"2026-11-14T17:50\",\"2026-11-15T17:51\"]}}";
    }
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool pass,string reason="Weather refresh invariant failed"){if(!pass)throw new Exception(reason);}
        var now=new DateTimeOffset(2026,11,14,12,0,0,TimeSpan.Zero);
        test("Weather schedule refreshes when stale, backs off after failures, retries on resume and stays off when unused",()=>
        {
            WeatherPlan Plan(WeatherDemand demand,double? ageMinutes,double attemptMinutesAgo=60,int failures=0,bool updating=false,bool away=false,bool nudged=false)=>new(demand,ageMinutes is {} age?now.AddMinutes(-age):null,now.AddMinutes(-attemptMinutesAgo),failures,updating,away,nudged);
            Assert(!WeatherSchedule.Due(Plan(WeatherDemand.None,null),now) && !WeatherSchedule.Due(Plan(WeatherDemand.None,600),now),"Unused weather never downloads");
            Assert(WeatherSchedule.Due(Plan(WeatherDemand.Background,null),now),"Missing outlook loads");
            Assert(!WeatherSchedule.Due(Plan(WeatherDemand.Visible,19),now) && WeatherSchedule.Due(Plan(WeatherDemand.Visible,20),now),"Visible refresh after 20 minutes");
            Assert(!WeatherSchedule.Due(Plan(WeatherDemand.Background,29),now) && WeatherSchedule.Due(Plan(WeatherDemand.Background,30),now),"Background refresh after 30 minutes");
            Assert(!WeatherSchedule.Due(Plan(WeatherDemand.Background,90,away:true),now) && WeatherSchedule.Due(Plan(WeatherDemand.Visible,90,away:true),now),"Away pauses only background refresh");
            Assert(!WeatherSchedule.Due(Plan(WeatherDemand.Visible,90,updating:true),now),"One shared download at a time");
            Assert(!WeatherSchedule.Due(Plan(WeatherDemand.Visible,null,attemptMinutesAgo:.5),now) && WeatherSchedule.Due(Plan(WeatherDemand.Visible,null,attemptMinutesAgo:1),now),"At most one attempt per minute");
            Assert(new[]{1,2,3,4,5,6,9}.Select(f=>WeatherSchedule.Retry(f).TotalMinutes).SequenceEqual(new double[]{1,2,4,8,16,30,30}),"Retry backoff 1, 2, 4, 8, 16, 30 minutes");
            Assert(!WeatherSchedule.Due(Plan(WeatherDemand.Visible,90,attemptMinutesAgo:3,failures:3),now) && WeatherSchedule.Due(Plan(WeatherDemand.Visible,90,attemptMinutesAgo:4,failures:3),now),"Third failure waits 4 minutes");
            Assert(WeatherSchedule.Due(Plan(WeatherDemand.Background,25,attemptMinutesAgo:.2,failures:4,away:true,nudged:true),now),"Resume/network nudge skips backoff once and uses the 20-minute threshold");
            Assert(!WeatherSchedule.Due(Plan(WeatherDemand.Background,10,nudged:true),now),"Nudge keeps a fresh outlook");
            Assert(WeatherSchedule.Due(Plan(WeatherDemand.Visible,90,attemptMinutesAgo:-120,failures:5),now),"A clock moved backwards never blocks refresh");
            Assert(WeatherSchedule.Age(now,now.AddHours(3))==TimeSpan.MaxValue && WeatherSchedule.Due(Plan(WeatherDemand.Visible,-180),now),"A saved time in the future (clock changed) is refreshed, not trusted as fresh");
            Assert(WeatherSchedule.Age(now,now.AddMinutes(2))==TimeSpan.Zero,"Small clock skew counts as fresh");
        });
        test("Weather status shows clock time while recent, age once stale, failures and unknown age",()=>
        {
            Assert(WeatherSchedule.Status(now,now.AddMinutes(-5),false,false)=="Updated "+now.AddMinutes(-5).ToLocalTime().ToString("HH:mm",CultureInfo.InvariantCulture));
            Assert(WeatherSchedule.Status(now,now.AddMinutes(-50),false,false)=="Updated 50 min ago" && WeatherSchedule.Status(now,now.AddHours(-2),false,false)=="Updated 2 h ago");
            Assert(WeatherSchedule.Status(now,now.AddDays(-3),false,true)=="Updated 3 d ago · connection unavailable");
            Assert(WeatherSchedule.Status(now,now.AddHours(2),false,false)=="Saved outlook" && WeatherSchedule.Status(now,now.AddHours(2),false,true)=="Saved outlook · connection unavailable");
            Assert(WeatherSchedule.Status(now,null,false,true)=="Weather unavailable. Refresh to try again." && WeatherSchedule.Status(now,now.AddHours(-9),true,true)=="Updating weather…");
            Assert(WeatherSchedule.StaleLabel(now,now.AddMinutes(-44))==null && WeatherSchedule.StaleLabel(now,now.AddMinutes(-45))=="45 min ago","Hover glance shows age only once stale");
        });
        test("Weather next hours follow the forecast location's clock across time zones and never show past hours",()=>
        {
            // Jakarta, UTC+7: Open-Meteo returns local wall-clock times.
            var jakarta=WeatherService.Parse(Forecast(25200,"2026-11-14T08:15",new DateTime(2026,11,14,0,0,0)),"k",new DateTimeOffset(2026,11,14,1,15,0,TimeSpan.Zero));
            Assert(jakarta.UtcOffsetSeconds==25200 && jakarta.Hours.Count==36 && jakarta.Hours[0].Stamp=="2026-11-14T09:00" && jakarta.Hours[0].Time=="09:00","Parse keeps a day and a half of stamped hours from the next hour");
            string First(WeatherSnapshot data,DateTimeOffset at)=>WeatherSchedule.Upcoming(data,at).FirstOrDefault()?.Stamp??"";
            Assert(First(jakarta,new DateTimeOffset(2026,11,14,1,27,0,TimeSpan.Zero))=="2026-11-14T09:00");
            Assert(First(jakarta,new DateTimeOffset(2026,11,14,10,27,0,TimeSpan.FromHours(9)))=="2026-11-14T09:00","The PC's own offset does not matter");
            Assert(First(jakarta,new DateTimeOffset(2026,11,14,5,5,0,TimeSpan.Zero))=="2026-11-14T13:00" && WeatherSchedule.Upcoming(jakarta,new DateTimeOffset(2026,11,14,5,5,0,TimeSpan.Zero)).Count==6,"A saved outlook moves on with time");
            Assert(First(jakarta,new DateTimeOffset(2026,11,14,6,0,0,TimeSpan.Zero))=="2026-11-14T13:00","The current hour is included at its start");
            Assert(WeatherSchedule.Upcoming(jakarta,new DateTimeOffset(2026,11,15,23,30,0,TimeSpan.Zero)).Count==0,"Yesterday's hours are never shown");
            // New York, UTC-5: the location's evening crosses midnight while UTC is already the next day.
            var york=WeatherService.Parse(Forecast(-18000,"2026-11-13T22:15",new DateTime(2026,11,13,0,0,0)),"k",new DateTimeOffset(2026,11,14,3,15,0,TimeSpan.Zero));
            var hours=WeatherSchedule.Upcoming(york,new DateTimeOffset(2026,11,14,3,30,0,TimeSpan.Zero));
            Assert(hours.Count==6 && hours[0].Stamp=="2026-11-13T23:00" && hours[1].Stamp=="2026-11-14T00:00","Negative offsets cross the date line correctly");
            var legacy=new WeatherSnapshot{Hours=Enumerable.Range(0,8).Select(i=>new WeatherHour{Time=$"{12+i}:00"}).ToList()};
            Assert(WeatherSchedule.Upcoming(legacy,now).Select(h=>h.Time).SequenceEqual(new[]{"12:00","13:00","14:00","15:00","16:00","17:00"}),"Outlooks saved before 2.27 still render");
        });
        test("Weather downloads finish after views leave, retry with backoff, recover on resume and survive restart",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"nabyscape-weather-"+Guid.NewGuid().ToString("N"));
            try
            {
                var clock=new DateTimeOffset(2026,11,14,1,15,0,TimeSpan.Zero);int calls=0;var gate=new TaskCompletionSource<string>();
                Task<string> Fetch(string url,CancellationToken token){calls++;var next=new TaskCompletionSource<string>();token.Register(()=>next.TrySetCanceled(token));gate=next;return next.Task;}
                string Json()=>Forecast(25200,"2026-11-14T08:15",new DateTime(2026,11,14,0,0,0));
                var s=new Settings{WeatherLatitude=-6.2,WeatherLongitude=106.8};var weather=new WeatherService(new Store(root),Fetch){Now=()=>clock};int changes=0;weather.Changed+=()=>changes++;
                Assert(!weather.Tick(s,WeatherDemand.None,clock,false,()=>true) && calls==0,"Unused weather must not download");
                Task<WeatherSnapshot> shown;
                using(var view=new CancellationTokenSource())
                {
                    shown=weather.Get(s,false,view.Token);Assert(calls==1 && weather.UpdatingFor(s));
                    view.Cancel();Assert(shown.IsCanceled && weather.Updating,"The download continues after the last view leaves");
                    gate.SetResult(Json());
                }
                var first=weather.Saved(s);Assert(first!=null && first.Updated==clock && !weather.Updating && weather.Failures==0 && changes>=2,"A finished background download is saved and announced");
                Assert(weather.Get(s,false,default).Result==first && calls==1,"A fresh outlook is reused without a request");
                clock=clock.AddMinutes(10);Assert(!weather.Tick(s,WeatherDemand.Visible,clock,false,()=>true));
                clock=clock.AddMinutes(11);Assert(!weather.Tick(s,WeatherDemand.Background,clock,false,()=>true),"Background waits 30 minutes");
                Assert(!weather.Tick(s,WeatherDemand.Visible,clock,false,()=>false) && calls==1 && weather.Failures==0,"Offline skips without counting a failure");
                Assert(weather.Tick(s,WeatherDemand.Visible,clock,false,()=>true) && calls==2,"A visible outlook refreshes after 20 minutes without any clock widget");
                gate.SetException(new HttpRequestException("offline"));Assert(weather.Failures==1 && weather.Saved(s)==first && !weather.Updating && weather.FailedFor(s));
                Assert(WeatherSchedule.Status(clock,first!.Updated,false,weather.FailedFor(s))=="Updated "+first.Updated.ToLocalTime().ToString("HH:mm",CultureInfo.InvariantCulture)+" · connection unavailable");
                clock=clock.AddSeconds(30);Assert(!weather.Tick(s,WeatherDemand.Visible,clock,false,()=>true) && weather.Get(s,false,default).Result==first && calls==2,"Backoff holds the schedule and reopened views");
                clock=clock.AddSeconds(31);Assert(weather.Tick(s,WeatherDemand.Visible,clock,false,()=>true) && calls==3);
                gate.SetException(new TaskCanceledException("request timeout"));Assert(weather.Failures==2,"A request timeout counts as a failure, not a closed view");
                clock=clock.AddSeconds(90);Assert(!weather.Tick(s,WeatherDemand.Visible,clock,false,()=>true),"Second failure waits 2 minutes");
                weather.Nudge(clock);Assert(!weather.Tick(s,WeatherDemand.Background,clock.AddSeconds(5),true,()=>true),"Resume waits briefly for the network");
                clock=clock.AddSeconds(11);Assert(weather.Tick(s,WeatherDemand.Background,clock,true,()=>true) && calls==4,"Resume/network regain retries once without backoff");
                gate.SetResult(Json());Assert(weather.Failures==0 && weather.Saved(s)!.Updated==clock && !weather.FailedFor(s));
                Assert(!weather.Tick(s,WeatherDemand.Background,clock.AddMinutes(31),true,()=>true),"Away pauses background refresh");
                Assert(weather.Tick(s,WeatherDemand.Background,clock.AddMinutes(31),false,()=>true) && calls==5);gate.SetResult(Json());
                var other=new Settings{WeatherLatitude=40.7,WeatherLongitude=-74};var old=weather.Get(s,true,default);Assert(calls==6);
                var moved=weather.Get(other,true,default);Assert(calls==7 && old.IsCanceled && weather.Failures==0,"A new city cancels the old download without a failure");
                gate.SetResult(Forecast(-18000,"2026-11-13T22:15",new DateTime(2026,11,13,0,0,0)));Assert(moved.Result.Key==WeatherService.Key(other) && weather.Saved(s)==null && weather.Saved(other)!=null);
                var restarted=new WeatherService(new Store(root),Fetch);
                Assert(restarted.Saved(other) is {} saved && saved.UtcOffsetSeconds==-18000 && saved.Hours.Count>0 && saved.Hours.All(h=>h.Stamp.Length==16),"Saved outlooks keep stamps and offset across restart");
                weather.Dispose();Assert(weather.Get(other,true,default).IsCanceled && calls==7,"No downloads after exit");
            }
            finally{try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
        });
        test("Weather is in use only where Feature usage shows it",()=>
        {
            var s=new Settings{WeatherLatitude=1,WeatherLongitude=2,HoverFollowsWidgets=false};s.Normalize();
            Assert(App.WeatherInUse(s),"Enabled weather widget");
            s.Widgets.Single(w=>w.Id=="weather").Enabled=false;Assert(App.WeatherInUse(s),"Enabled weather hover page");
            s.HoverPeek=false;Assert(!App.WeatherInUse(s),"Hover previews off");
            s.HoverPeek=true;s.HoverViews.Single(v=>v.Id=="weather").Enabled=false;Assert(!App.WeatherInUse(s),"Weather hidden everywhere");
            s.Widgets.Single(w=>w.Id=="weather").Enabled=true;s.WeatherLatitude=s.WeatherLongitude=null;Assert(!App.WeatherInUse(s),"No city chosen");
        });
    }
}
