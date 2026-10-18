using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

// Controlled WPF fixtures only. No provider calls, credentials, microphone,
// desktop capture, clipboard access or injected mouse/keyboard input.
internal static class AssistantDiagnostics
{
    internal sealed class FixtureVoice : IAssistantVoice
    {
        public bool Connected {get;private set;}=true;
        internal bool Stopped,Disposed;
        public event Action<string,string>? Transcript {add{} remove{}}
        public event Action<string>? Status {add{} remove{}}
        public event Action<AssistantCall>? ToolCall {add{} remove{}}
        public event Action<float,bool>? Level {add{} remove{}}
        public CancellationToken CallToken(string id)=>new(true);
        public Task Start(AssistantProvider provider,string? key,string instructions,IReadOnlyList<AssistantTool> tools,IReadOnlyList<AssistantMessage> history,CancellationToken token)=>throw new InvalidOperationException("Fixture voice cannot open devices or a provider.");
        public Task SendText(string text,CancellationToken token)=>Task.CompletedTask;
        public Task SendImage(string image,CancellationToken token)=>Task.CompletedTask;
        public Task SendImage(Func<CancellationToken,Task<string?>> capture,CancellationToken token)=>throw new InvalidOperationException("Fixture voice cannot capture.");
        public Task SendToolResult(AssistantCall call,string result,CancellationToken token,Func<CancellationToken,Task<string?>>? freshImage=null)=>Task.CompletedTask;
        public Task Stop(){Connected=false;Stopped=true;return Task.CompletedTask;}
        public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
    }
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        island.Hide();var ai=app.Assistant;
        check("Assistant defaults are opt-in with no session permissions",!ai.Options.Enabled && !ai.ActionsAllowed && !ai.Desktop.Sharing && !ai.VoiceConnected && ai.Keys is AssistantMemoryKeys);
        var profile=new AssistantProvider{Name="Fixture provider",Model="fixture-model",Tools=true,Vision=true};
        ai.Options.Providers.Add(profile);ai.Options.SelectedProvider=profile.Id;ai.Options.Enabled=true;ai.SaveOptions();
        ai.Conversation.Record("user","Can you help me plan a quiet afternoon?");ai.Conversation.Record("assistant","Of course. We can keep a short task list here, then start a timer when you are ready. Nothing runs until you review it.");
        int Subscribers()=>((MulticastDelegate?)typeof(AssistantRuntime).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ai))?.GetInvocationList().Length??0;
        int before=Subscribers();var window=new AssistantWindow(app){ShowActivated=false};window.Show();await Task.Delay(100);
        var input=Diagnostics.Descendants(window).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Message AI companion");input.Text="A draft stays while the app updates.";input.Select(2,7);
        app.ChangeSettings();await Task.Delay(50);
        check("Assistant settings refresh preserves composer and selection",input.Text=="A draft stays while the app updates." && input.SelectionStart==2 && input.SelectionLength==7);
        var orb=Diagnostics.Descendants(window).OfType<AssistantOrb>().Single();
        check("Assistant idle orb has no render subscription",!orb.Rendering);orb.State=AssistantState.Thinking;check("Assistant active orb wakes on demand",orb.Rendering);orb.ReduceMotion=true;check("Assistant Reduce Motion detaches continuous rendering",!orb.Rendering);orb.State=AssistantState.Idle;orb.ReduceMotion=false;
        ai.SetActions(true);var review=ai.Execute(new("declined","nabyscape","{\"action\":\"timer_start\",\"seconds\":45}"),default);
        check("Assistant pending action exposes review before changing state",ai.Pending?.Id=="declined" && !app.Session.Timer.Running);
        Capture(window,"assistant-action-review",1);ai.Decide(false);string declined=await review;
        check("Assistant Decline leaves the isolated timer untouched",declined.Contains("declined") && !app.Session.Timer.Running && ai.Pending==null);
        review=ai.Execute(new("approved","nabyscape","{\"action\":\"timer_start\",\"seconds\":45}"),default);ai.Decide(true);await review;
        check("Assistant Run once executes exactly the reviewed app action",app.Session.Timer.Running && app.Session.Timer.Remaining(DateTimeOffset.UtcNow)>40 && ai.Pending==null);app.Session.Timer.Reset();app.SaveSession();
        review=ai.Execute(new("stopped","nabyscape","{\"action\":\"timer_start\",\"seconds\":45}"),default);ai.StopSession();await review;
        check("Assistant Stop dismisses approval and revokes session controls",!app.Session.Timer.Running && !ai.ActionsAllowed && ai.Pending==null && !ai.Desktop.Sharing && !ai.VoiceConnected);
        var sessionControls=Diagnostics.Descendants(window).OfType<Expander>().Single(e=>Equals(e.Header,"Session controls"));
        sessionControls.IsExpanded=true;window.UpdateLayout();
        foreach(string target in new[]{"Run once","Decline","Providers","Actions on","Display to share"})
        {
            ai.SetActions(true);ai.Desktop.ControlEnabled=true;
            var fixtureVoice=new FixtureVoice();typeof(AssistantRuntime).GetField("voice",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(ai,fixtureVoice);
            review=ai.Execute(new("escape-"+target,"nabyscape","{\"action\":\"timer_start\",\"seconds\":45}"),default);
            window.UpdateLayout();
            var child=Diagnostics.Descendants(window).OfType<UIElement>().First(e=>AutomationProperties.GetName(e)==target || e is Button b && Equals(b.Content,target));
            KeyEventHandler handledChild=(_,e)=>e.Handled=true;
            child.AddHandler(Keyboard.KeyDownEvent,handledChild);
            try
            {
                // Programmatic routed event only; no activation, native key input,
                // screen or device is required, including for already-handled keys.
                child.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
                await review;
                check("Assistant handled child Escape stops approval from "+target,ai.Pending==null && !ai.ActionsAllowed && !ai.Desktop.ControlEnabled && !ai.VoiceConnected && fixtureVoice.Stopped && fixtureVoice.Disposed && !app.Session.Timer.Running);
            }
            finally { child.RemoveHandler(Keyboard.KeyDownEvent,handledChild); }
        }
        sessionControls.IsExpanded=false;window.UpdateLayout();
        foreach(var size in new[]{(480d,690d),(360d,420d)})
        {
            window.Width=size.Item1;window.Height=size.Item2;await Task.Delay(70);window.UpdateLayout();
            foreach(double scale in new[]{1d,1.5,2d})Capture(window,$"assistant-chat-{size.Item1}-{scale:0.0}x",scale);
            var outer=(ScrollViewer)Diagnostics.Descendants(window).OfType<ScrollViewer>().First();outer.ScrollToEnd();window.UpdateLayout();
            check("Assistant compact layout keeps form scrollable at "+size.Item1,window.ActualWidth<=size.Item1+1 && input.ActualWidth>200 && input.ActualHeight>30 && outer.ComputedHorizontalScrollBarVisibility==Visibility.Collapsed);outer.ScrollToTop();
        }
        window.Close();check("Assistant closing window releases listeners and orb rendering",Subscribers()==before && !orb.Rendering);
        for(int i=0;i<10;i++){var cycle=new AssistantWindow(app){ShowActivated=false};cycle.Show();cycle.Close();}
        check("Assistant repeated open/close does not accumulate runtime listeners",Subscribers()==before);
        var providers=new AssistantProvidersWindow(app){ShowActivated=false};providers.Show();await Task.Delay(70);
        foreach(var size in new[]{(600d,740d),(360d,420d)})
        {
            providers.Width=size.Item1;providers.Height=size.Item2;await Task.Delay(70);providers.UpdateLayout();
            foreach(double scale in new[]{1d,1.5,2d})Capture(providers,$"assistant-providers-{size.Item1}-{scale:0.0}x",scale);
            var scroll=Diagnostics.Descendants(providers).OfType<ScrollViewer>().First();scroll.ScrollToEnd();providers.UpdateLayout();
            check("Assistant providers fit compact width with vertical access at "+size.Item1,scroll.ScrollableHeight>0 && scroll.ComputedHorizontalScrollBarVisibility==Visibility.Collapsed);scroll.ScrollToTop();
        }
        check("Assistant custom caption has accessible window controls",new[]{"Minimize","Maximize or restore","Close"}.All(name=>Diagnostics.Descendants(providers).OfType<Button>().Any(b=>AutomationProperties.GetName(b)==name)));
        providers.Close();ai.Clear();
        check("Assistant Clear removes in-memory messages",ai.Conversation.Messages.Count==0);
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Synthetic WPF fixtures, not a live AI response. 1x/1.5x/2x are render pixel densities of actual WPF windows, not mixed-DPI monitor emulation. No API, microphone, desktop screenshot, credential vault or native input was used. Physical DPI switching and live provider/device interoperability remain manual acceptance checks.");
    }
    private static void Capture(FrameworkElement element,string name,double scale)
    {
        element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*scale),(int)Math.Ceiling(element.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(Path.Combine(OutputDirectory(),name+".png"));encoder.Save(stream);
    }
    private static string OutputDirectory(){var args=Environment.GetCommandLineArgs();return args[Array.IndexOf(args,"--diagnostics")+1];}
}
