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

internal static class AssistantIslandDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var ai=app.Assistant;var s=app.Settings;s.Width=327;s.Height=36;s.PanelWidth=526;s.PanelMaxHeight=417;s.HoverWidth=447;s.HoverHeight=207;s.PanelScale=1.04;s.HideMaximized=s.HideFullscreen=false;app.ChangeSettings();
        island.Show();island.Collapse();await Task.Delay(350);
        Button Button(string name)=>Diagnostics.Descendants(island).OfType<Button>().First(b=>AutomationProperties.GetName(b)==name);
        var character=Button("AI character on island");var closedOrb=Diagnostics.Descendants(character).OfType<AssistantOrb>().Single();
        check("Island character is discoverable before enabling AI or adding a key",!ai.Options.Enabled && ai.Options.IslandCharacter && character.IsVisible && !closedOrb.Rendering && !ai.VoiceConnected && !ai.Desktop.Sharing);
        Capture("island-ai-closed");
        island.PreviewAssistant();await Task.Delay(350);
        check("Hover reveals the character and Chat/Talk inside the same island",island.AssistantPeeking && island.IsPeeking && !island.IsExpanded && island.PaintedHeight<=190.1 && Button("Chat here").IsVisible);
        check("AI hover has no voice or screen side effects",!ai.VoiceConnected && !ai.Desktop.Sharing && !ai.ActionsAllowed);
        Capture("island-ai-hover");
        typeof(OverlayWindow).GetMethod("BeginFileDrop",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(island,null);
        check("Shelf drag takes priority over the AI hover",!island.AssistantPeeking && island.PeekPage=="files");
        typeof(OverlayWindow).GetMethod("FinishFileDrop",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(island,new object[]{false});
        check("Canceling shelf drag restores the AI hover",island.AssistantPeeking);
        int windows=app.Windows.Count;app.ShowAssistant();await Task.Delay(350);
        check("Tray and command entry opens inline without creating a Companion window",island.AssistantOpen && app.Windows.Count==windows && !app.Windows.Cast<Window>().Any(w=>w is AssistantWindow));
        var view=island.IslandAssistantView!;var editor=view.Editor;editor.Text="A draft right inside my island.";editor.Select(2,5);
        app.PublishUpdate(RuntimeChange.Clock);app.PublishUpdate(RuntimeChange.Countdown);
        check("Inline draft and selection survive app heartbeat",editor.Text=="A draft right inside my island." && editor.SelectionStart==2 && editor.SelectionLength==5);
        Capture("island-ai-chat");
        island.CloseAssistant();await Task.Delay(300);island.Reveal(false);island.SelectPage("media",false);await Task.Delay(120);island.OpenAssistant(false);await Task.Delay(300);
        check("Returning through normal widgets keeps the same inline editor and draft",ReferenceEquals(view,island.IslandAssistantView) && editor.Text=="A draft right inside my island." && editor.SelectionStart==2 && editor.SelectionLength==5);
        int Listeners()=>((MulticastDelegate?)typeof(AssistantRuntime).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ai))?.GetInvocationList().Length??0;
        int baseline=Listeners();
        for(int i=0;i<12;i++){island.CloseAssistant();island.Reveal(false);island.SelectPage("media",false);island.OpenAssistant(false);}
        check("Repeated normal/AI navigation does not accumulate runtime listeners",Listeners()==baseline);
        foreach(double width in new[]{360d,526d})
        {
            s.PanelWidth=width;app.ChangeSettings();await Task.Delay(250);island.UpdateLayout();
            check("Inline editor stays inside island bounds at "+width,editor.ActualWidth>150 && editor.ActualWidth<=island.PaintedWidth-20);
            Capture("island-ai-chat-"+width);
        }
        island.CloseAssistant();await Task.Delay(300);
        ai.Options.Enabled=true;ai.Conversation.Record("user","Hello from my island");ai.Conversation.Record("assistant","I'm right here. We can chat or talk without leaving your island.");
        await Task.Delay(350);
        check("A new reply can emerge from the closed island without opening a window",island.AssistantPeeking && app.Windows.Count==windows && !island.IsExpanded);
        Capture("island-ai-reply");
        ai.Options.Providers.Add(new(){Name="Fixture",Model="fixture",Tools=true});ai.Options.SelectedProvider=ai.Options.Providers[0].Id;ai.SetActions(true);island.OpenAssistant(false);await Task.Delay(150);
        var approval=ai.Execute(new("inline-review","nabyscape","{\"action\":\"timer_start\",\"seconds\":60}"),default);await Task.Delay(200);
        check("Reviewed actions stay inside the island without a dialog",ai.Pending?.Id=="inline-review" && Button("Run once").IsVisible && app.Windows.Count==windows && !app.Session.Timer.Running);
        Capture("island-ai-review");ai.Decide(false);await approval;
        var live=new AssistantDiagnostics.FixtureVoice();typeof(AssistantRuntime).GetField("voice",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(ai,live);ai.SetIslandCharacter(true);
        island.Collapse();await Task.Delay(250);
        check("Collapsing inline chat keeps a live session with a visible microphone indicator",ai.VoiceConnected && !live.Stopped && character.IsVisible && closedOrb.Live);
        s.AlwaysTucked=true;app.ChangeSettings();island.Collapse();island.RevealTuckedPreview();await Task.Delay(200);
        check("A tucked live character reveals AI hover at the top edge",island.AssistantPeeking && !island.IsTucked && ai.VoiceConnected);
        s.AlwaysTucked=false;app.ChangeSettings();
        island.Collapse();island.ToggleVisibility();ai.Conversation.Record("assistant","A hidden island must stay hidden.");await Task.Delay(100);
        check("Replies respect manual hide, stop the live session and do not reveal the island",island.UserHidden && !island.IsVisible && !ai.VoiceConnected && live.Stopped && live.Disposed && !ai.Desktop.Sharing && !closedOrb.Rendering);
        island.ToggleVisibility();ai.SetIslandCharacter(false);await Task.Delay(100);
        check("Island character can be turned off independently",!character.IsVisible && !ai.Options.IslandCharacter && !ai.VoiceConnected);
        ai.SetIslandCharacter(true);island.Collapse();await Task.Delay(250);
        check("Turning the character back on does not start capture",character.IsVisible && !ai.VoiceConnected && !ai.Desktop.Sharing && !ai.ActionsAllowed);
        ai.Clear();
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Actual WPF island render with synthetic conversation. No provider API, credential vault, microphone/speaker, desktop capture or native input used. A shared runtime drives closed character, hover greeting and inline chat. No separate Companion window is created by the tested entry point.");
        void Capture(string name)
        {
            island.UpdateLayout();var full=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth),(int)Math.Ceiling(island.ActualHeight),96,96,PixelFormats.Pbgra32);full.Render(island);
            int width=Math.Min(full.PixelWidth,(int)Math.Ceiling(island.PaintedWidth)),height=Math.Min(full.PixelHeight,(int)Math.Ceiling(island.PaintedHeight));var crop=new CroppedBitmap(full,new Int32Rect(Math.Max(0,(full.PixelWidth-width)/2),0,width,height));
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(crop));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
    }
}
