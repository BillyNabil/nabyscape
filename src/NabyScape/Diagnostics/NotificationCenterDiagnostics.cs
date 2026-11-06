using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class NotificationCenterDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var s=app.Settings;s.ReduceMotion=true;s.HideFullscreen=s.HideMaximized=s.AlwaysTucked=false;s.CompactLayout=true;app.ChangeSettings();
        T Field<T>(string name)=>(T)typeof(OverlayWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(island)!;
        void Capture(string name)
        {
            island.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(island.ActualWidth)),Math.Max(1,(int)Math.Ceiling(island.ActualHeight)),96,96,PixelFormats.Pbgra32);bitmap.Render(island);
            int w=Math.Min(bitmap.PixelWidth,(int)Math.Ceiling(island.PaintedWidth)),h=Math.Min(bitmap.PixelHeight,(int)Math.Ceiling(island.PaintedHeight));
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bitmap,new Int32Rect(Math.Max(0,(bitmap.PixelWidth-w)/2),0,Math.Max(1,w),Math.Max(1,h)))));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }
        app.Notifications.Clear();app.Notifications.ClearHistory();
        island.OpenNotificationCenter();await Task.Delay(400);Capture("notification-center-empty");
        check("Empty center explains what will appear",Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<TextBlock>().Any(t=>t.Text=="No notifications yet"));
        island.Collapse();await Task.Delay(100);
        var fixture=new Settings{NotificationDelivery="Notch"};var now=DateTimeOffset.UtcNow;
        app.Notifications.Post(fixture,new("updates","Update ready to install","Open Settings > Application to install and restart when ready."),false,_=>{},now.AddHours(-3));
        app.Notifications.Post(fixture,new("failure","Weather unavailable","The forecast service did not respond. NabyScape will try again later."),false,_=>{},now.AddMinutes(-42));
        app.Notifications.Post(fixture,new("focus","Focus complete","Time for a short break."),false,_=>{},now.AddMinutes(-6));
        app.Notifications.Post(fixture,new("timer","Timer finished","Your timer has finished."),false,_=>{},now.AddSeconds(-10));
        app.Notifications.MarkDisplayed();app.Notifications.Clear();
        check("Center keeps alerts after the popup queue is cleared",app.Notifications.History.Count==4 && app.Notifications.Unread==3);
        island.Reveal(false);await Task.Delay(300);
        var bells=Diagnostics.Descendants(island).OfType<Button>().Where(b=>AutomationProperties.GetAutomationId(b)=="Notifications" && b.IsVisible).ToArray();
        check("The panel header offers a notifications bell",bells.Length==1);
        Capture("panel-with-bell");
        bells.FirstOrDefault()?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(400);
        check("The bell opens the notification center",island.NotificationCenterOpen && !island.IsExpanded);
        Capture("notification-center");
        check("Opening the center marks everything read",app.Notifications.Unread==0);
        var card=Field<Border>("noticeCard");
        check("Center lists every alert newest first",Diagnostics.Descendants(card).OfType<Border>().Where(b=>AutomationProperties.GetName(b).Length>0).Select(b=>AutomationProperties.GetName(b)).Take(4).SequenceEqual(new[]{"Timer finished","Focus complete","Weather unavailable","Update ready to install"}));
        app.Notifications.Post(fixture,new("timer","Second timer","Another one."),false,_=>{},DateTimeOffset.UtcNow);await Task.Delay(100);
        check("New alerts join the open center instead of replacing it",island.NotificationCenterOpen && Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<Border>().Any(b=>AutomationProperties.GetName(b)=="Second timer"));
        Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<Button>().First(b=>AutomationProperties.GetAutomationId(b)=="Remove notification").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
        check("Removing one entry keeps the rest",app.Notifications.History.Count==4 && app.Notifications.History.All(e=>e.Notice.Title!="Second timer"));
        island.Collapse();await Task.Delay(100);
        check("Closing the center returns the dock",!island.NotificationCenterOpen && !island.IsPeeking);
        app.Notifications.ClearHistory();
        // Alerts mirrored from other Windows apps.
        var external=new Settings{NotificationDelivery="Notch",ExternalNotifications=true};
        app.Notifications.Post(external,new("external","Unigram","Rina: are we still on for tonight?",null,"Unigram","Unigram.App"),false,_=>{},DateTimeOffset.UtcNow.AddMinutes(-3));
        app.Notifications.Post(external,new("external","Your answer is ready","Claude finished replying in Project notes.",null,"Claude","Claude.App"),false,_=>{},DateTimeOffset.UtcNow);
        app.Notifications.Dismiss();await Task.Delay(50);
        island.OpenNotice(true);await Task.Delay(400);Capture("external-popup");
        check("Popup names the sending app and offers Open app",Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<TextBlock>().Any(t=>t.Text==(app.Notifications.Current?.Source??"?").ToUpperInvariant()) && Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<Button>().Any(b=>AutomationProperties.GetAutomationId(b)=="Open app"));
        s.ExternalNotificationText=false;island.Collapse();await Task.Delay(80);
        app.Notifications.Post(external,new("external","New message","Private text",null,"Unigram","Unigram.App"),false,_=>{},DateTimeOffset.UtcNow);island.OpenNotice(true);await Task.Delay(300);
        check("Message text stays hidden in pop-ups when turned off",!Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<TextBlock>().Any(t=>t.Text=="Private text"));
        s.ExternalNotificationText=true;island.Collapse();await Task.Delay(80);app.Notifications.Clear();
        island.OpenNotificationCenter();await Task.Delay(400);Capture("external-center");
        check("Center shows other apps with their names",Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<TextBlock>().Any(t=>t.Text=="Claude") && Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<TextBlock>().Any(t=>t.Text=="Unigram"));
        island.Collapse();app.Notifications.ClearHistory();
        // A new alert grows out of the closed dock and folds back on its own.
        island.Collapse();app.Notifications.Clear();app.Notifications.ClearHistory();await Task.Delay(100);
        s.NotificationSeconds=4;app.ChangeSettings();island.DiagnosticBanners=true;
        app.Notifications.Post(external,new("external","AI TOOLS","ElevenLabs Creator Plan 3M + Eleven Reader Ultra Plan 12M - 10 Days Warranty",null,"Unigram","Unigram.App"),false,_=>{},DateTimeOffset.UtcNow);
        app.Notifications.Post(external,new("external","Your answer is ready","Claude finished replying.",null,"Claude","Claude.App"),false,_=>{},DateTimeOffset.UtcNow);
        await Task.Delay(500);Capture("dock-banner");
        check("A new alert opens the dock banner without a click",island.NoticeBannerOpen && !island.IsExpanded);
        check("The banner names the sender and shows the message",Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Unigram")) && Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<TextBlock>().Any(t=>t.Text=="AI TOOLS"));
        await Task.Delay(4600);
        bool bannerTimerOn=Field<System.Windows.Threading.DispatcherTimer>("bannerTimer").IsEnabled;
        check($"The banner folds back and the next alert follows (open {island.NoticeBannerOpen}, peeking {island.IsPeeking}, queue {app.Notifications.Count}, current {app.Notifications.Current?.Title}, timer {bannerTimerOn})",island.NoticeBannerOpen && Diagnostics.Descendants(Field<Border>("noticeCard")).OfType<TextBlock>().Any(t=>t.Text=="Your answer is ready"));
        await Task.Delay(4600);
        check("After the last banner the dock returns, alerts stay in the center",!island.NoticeBannerOpen && !island.IsPeeking && app.Notifications.History.Count==2);
        app.SendTestNotification();await Task.Delay(500);Capture("test-banner");
        check("Send test notification opens a banner even in quiet hours",island.NoticeBannerOpen && app.Notifications.History.Any(e=>e.Notice.Kind=="test"));
        island.Collapse();await Task.Delay(100);app.Notifications.Clear();
        app.Notifications.Post(external,new("external","Nabyscape workspace review","build passed 573/573 tests; banner feature installed & ready to try",null,"Claude","Claude.App"),false,_=>{},DateTimeOffset.UtcNow);
        app.Notifications.Post(new Settings{NotificationDelivery="Notch"},new("failure","Downloads unavailable","This folder has too many files to monitor safely (limit 10,000)."),false,_=>{},DateTimeOffset.UtcNow);
        app.Notifications.Post(new Settings{NotificationDelivery="Notch"},new("failure","Settings recovered","Both NabyScape and LumaIsland data exist. NabyScape data is in use; review the older folder before removing it."),false,_=>{},DateTimeOffset.UtcNow);
        island.Collapse();app.Notifications.Clear();
        island.DiagnosticBanners=false;island.OpenNotificationCenter();await Task.Delay(500);Capture("center-redesign");
        var centerCard=Field<Border>("noticeCard");
        check($"The center fits inside the dock (card {centerCard.ActualHeight:0} + margins within surface {island.PaintedHeight:0}, host {island.ActualHeight:0})",centerCard.ActualHeight+16<=island.PaintedHeight+1 && island.PaintedHeight<=island.ActualHeight+1);
        check($"The center follows Panel width ({island.PaintedWidth:0} vs setting {s.PanelWidth:0})",Math.Abs(island.PaintedWidth-Math.Clamp(s.PanelWidth,300,island.ActualWidth))<2);
        island.Collapse();app.Notifications.ClearHistory();
        // Real Windows read from inside NabyScape (names only).
        try{var apps=await ExternalNotificationWatcher.CurrentAppsAsync();check($"NabyScape can read Windows notifications ({apps.Length} apps in Action Center)",true);File.WriteAllText(Path.Combine(directory,"external-apps.txt"),string.Join(Environment.NewLine,apps));}
        catch(Exception ex){check("NabyScape can read Windows notifications: "+ex.Message,false);}
    }
}
