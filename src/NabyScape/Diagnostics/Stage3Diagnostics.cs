using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NabyScape;
internal static class Stage3Diagnostics
{
    internal sealed class Network:HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;string tag="v2.4.3",url=OfficialUpdates.Repository;
            string json=JsonSerializer.Serialize(new{tag_name=tag,html_url=url+"/releases/tag/"+tag,draft=false,prerelease=false,assets=new[]{new{name="NabyScape-win-x64-Setup.exe",browser_download_url=url+"/releases/download/"+tag+"/NabyScape-win-x64-Setup.exe",size=4},new{name="SHA256SUMS",browser_download_url=url+"/releases/download/"+tag+"/SHA256SUMS",size=100}}});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(request.RequestUri==OfficialUpdates.Endpoint?json:"invalid fixture checksum")});
        }
    }
    internal static async Task Run(App app,string directory,Action<string,bool> check)
    {
        var window=new SettingsWindow(app);
        void Navigate()=>typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{"Application"});
        Button Button(string name)=>Diagnostics.Descendants(window).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name);
        void Capture(string name)
        {window.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);}
        try
        {
            window.Show();Navigate();await Task.Delay(100);
            check("Stage3 initial available state unknown and install disabled",app.Updates.Available==null && !Button("Install and restart").IsEnabled && !Button("Download update").IsEnabled);
            foreach(string name in new[]{"Check for updates","Release notes","Download update","Cancel update","Install and restart"})check("Stage3 keyboard action exposed: "+name,Button(name).Focusable && Button(name).IsTabStop);
            check("Stage3 static distinct download example",Diagnostics.Descendants(window).OfType<SettingsScene>().Count(p=>p.Section=="Updates")==1);
            Button("Check for updates").Focus();check("Stage3 check can receive keyboard focus",Button("Check for updates").IsKeyboardFocused);Button("Check for updates").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(100);
            check("Stage3 fake newer release enables download but not portable install",app.Updates.Available?.Version==new Version(2,4,3) && Button("Download update").IsEnabled && !Button("Install and restart").IsEnabled);
            Capture("stage3-updates-wide");
            window.Width=580;window.Height=680;await Task.Delay(100);var status=Diagnostics.Descendants(window).OfType<TextBlock>().Single(t=>AutomationProperties.GetName(t)=="Update status");status.BringIntoView();await Task.Delay(100);
            Point origin=status.TransformToAncestor(window).Transform(new Point());check("Stage3 narrow update status contained",origin.X>=0 && origin.X+status.ActualWidth<=window.ActualWidth+1 && status.ActualHeight>0);
            Capture("stage3-updates-narrow");
            Button("Download update").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(100);
            check("Stage3 fake invalid checksum preserves running app",app.Updates.VerifiedSetup==null && app.Updates.Status.Contains("failed") && !app.Quitting);
            app.Productivity.Note="Stage3 pending note";app.FlushPreferenceEditors();check("Stage3 pending note preserved on update validation failure",app.Store.Load<ProductivityData>("productivity").Note=="Stage3 pending note");
        }
        finally{window.Close();}
    }
}
