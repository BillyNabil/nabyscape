using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class AssistantIslandDiagnostics
{
    private sealed class FixtureDesktop : IAssistantDesktopBackend
    {
        internal int Starts,Stops,Captures,Inputs;
        public IReadOnlyList<AssistantScreenTarget> Targets()=>new[]{new AssistantScreenTarget("fixture","Fixture display",new System.Drawing.Rectangle(0,0,640,480))};
        public void Begin(IntPtr owner)=>Starts++;
        public void End()=>Stops++;
        public AssistantScreenFrame Capture(AssistantScreenTarget target){Captures++;throw new InvalidOperationException("Island fixture cannot capture a display.");}
        public void Execute(AssistantScreenFrame frame,JsonElement action,CancellationToken token){Inputs++;throw new InvalidOperationException("Island fixture cannot send native input.");}
    }
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var ai=app.Assistant;ai.Options.Enabled=false;var s=app.Settings;s.Width=327;s.Height=36;s.PanelWidth=526;s.PanelMaxHeight=417;s.HoverWidth=447;s.HoverHeight=207;s.PanelScale=1.04;s.HideMaximized=s.HideFullscreen=false;app.ChangeSettings();
        var desktop=new FixtureDesktop();typeof(AssistantDesktop).GetField("fixture",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(ai.Desktop,desktop);
        T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
        void Invoke(string name,params object[] args)=>typeof(OverlayWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(island,args);
        AssistantDiagnostics.FixtureVoice LiveVoice()
        {
            var fixture=new AssistantDiagnostics.FixtureVoice();typeof(AssistantRuntime).GetField("voice",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(ai,fixture);
            typeof(AssistantRuntime).GetField("state",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(ai,AssistantState.Listening);ai.SetIslandCharacter(true);return fixture;
        }
        island.Show();island.Collapse();await Task.Delay(350);
        Button Button(string name)=>Diagnostics.Descendants(island).OfType<Button>().First(b=>AutomationProperties.GetName(b)==name);
        var character=Button("AI character on island");var closedOrb=Diagnostics.Descendants(character).OfType<AssistantOrb>().Single();
        check("Island character is discoverable before enabling AI or adding a key",!ai.Options.Enabled && ai.Options.IslandCharacter && character.IsVisible && !closedOrb.Rendering && !ai.VoiceConnected && !ai.Desktop.Sharing);
        Capture("island-ai-closed");
        check("Closed character stays square and fits the compact header",Math.Abs(closedOrb.ActualWidth-closedOrb.ActualHeight)<.1 && closedOrb.ActualHeight>=30 && closedOrb.ActualHeight<=s.Height);
        island.DiagnosticRun=false;Invoke("ConfigureDisplayFollow");
        check("Visible closed character wakes shared desktop pointer sampling",Field<System.Windows.Threading.DispatcherTimer>(island,"pointerCheck").IsEnabled);
        island.DiagnosticRun=true;Invoke("ConfigureDisplayFollow");
        double idleOffset=closedOrb.IdleOffset;await Task.Delay(420);
        check("Closed dock character animates while untouched without starting voice or a managed render loop",closedOrb.IdleAnimating && Math.Abs(closedOrb.IdleOffset-idleOffset)>.1 && !closedOrb.Rendering && !ai.VoiceConnected);
        Invoke("TrackAssistantPointerAt",closedOrb.PointToScreen(new Point(closedOrb.ActualWidth/2+360,closedOrb.ActualHeight/2+280)));await Task.Delay(210);
        check("Desktop pointer coordinates outside the dock guide the closed character eyes",Math.Abs(closedOrb.Gaze.X-6)<.1 && Math.Abs(closedOrb.Gaze.Y-4)<.1 && !ai.VoiceConnected);
        closedOrb.SetHover(true);closedOrb.LookAt(-1,-.3);await Task.Delay(230);Capture("island-ai-closed-curious");
        check("Closed character responds to nearby pointer without opening audio or a render loop",closedOrb.Gaze.X< -5.8 && !closedOrb.Rendering && !ai.VoiceConnected);
        closedOrb.Press(true);await Task.Delay(100);Capture("island-ai-closed-press");closedOrb.Press(false);closedOrb.SetHover(false);closedOrb.LookAt(0,0);await Task.Delay(320);
        closedOrb.ReduceMotion=true;closedOrb.Music=true;Capture("island-ai-closed-headphones");closedOrb.Music=false;closedOrb.ReduceMotion=false;
        island.PreviewActivity();
        check("Normal hover immediately hides the closed AI overlay before its transition",island.IsPeeking && !island.AssistantPeeking && !character.IsVisible);
        await Task.Delay(300);Capture("island-normal-hover");island.Collapse();await Task.Delay(300);
        island.PreviewAssistant();await Task.Delay(350);
        check("Hover reveals the character and Chat/Talk inside the same island",island.AssistantPeeking && island.IsPeeking && !island.IsExpanded && island.PaintedHeight<=190.1 && Button("Chat here").IsVisible);
        check("AI hover has no voice or screen side effects",!ai.VoiceConnected && !ai.Desktop.Sharing);
        Capture("island-ai-hover");
        typeof(OverlayWindow).GetMethod("BeginFileDrop",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(island,null);
        check("Shelf drag takes priority over the AI hover",!island.AssistantPeeking && island.PeekPage=="files");
        typeof(OverlayWindow).GetMethod("FinishFileDrop",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(island,new object[]{false});
        check("Canceling shelf drag restores the AI hover",island.AssistantPeeking);
        int windows=app.Windows.Count;app.ShowAssistant();await Task.Delay(350);
        check("Tray and command entry opens inline without creating a Companion window",island.AssistantOpen && app.Windows.Count==windows && !app.Windows.Cast<Window>().Any(w=>w is AssistantWindow));
        var view=island.IslandAssistantView!;var editor=view.Editor;editor.Text="A draft right inside my island.";editor.Select(2,5);
        check("Inline chat has no provider picker or provider configuration controls",!Diagnostics.Descendants(view).OfType<ComboBox>().Any(c=>AutomationProperties.GetName(c)=="AI provider") && !Diagnostics.Descendants(view).OfType<Button>().Any(b=>Equals(b.Content,"Providers")));
        app.PublishUpdate(RuntimeChange.Clock);app.PublishUpdate(RuntimeChange.Countdown);
        check("Inline draft and selection survive app heartbeat",editor.Text=="A draft right inside my island." && editor.SelectionStart==2 && editor.SelectionLength==5);
        Capture("island-ai-chat");
        island.CloseAssistant();await Task.Delay(300);island.Reveal(false);island.SelectPage("access",false);await Task.Delay(550);double normalHeight=island.PaintedHeight;
        island.OpenAssistant(false);await Task.Delay(550);
        var viewport=Field<ScrollViewer>(island,"scroll");
        check("Opening AI from a short expanded widget resizes the painted surface to its chat viewport",normalHeight<350 && island.PaintedHeight>normalHeight+30 && Math.Abs(island.PaintedHeight-viewport.Height-26)<1);
        check("Returning through normal widgets keeps the same inline editor and draft",ReferenceEquals(view,island.IslandAssistantView) && editor.Text=="A draft right inside my island." && editor.SelectionStart==2 && editor.SelectionLength==5);
        island.ShowCommands(false);Field<TextBox>(island,"commandInput").Text="AI companion";await Task.Delay(550);double commandHeight=island.PaintedHeight;
        Button("AI companion").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(550);
        check("The filtered command entry resizes its short surface for inline AI and keeps the draft",commandHeight<350 && island.AssistantOpen && island.PaintedHeight>commandHeight+30 && Math.Abs(island.PaintedHeight-viewport.Height-26)<1 && editor.Text=="A draft right inside my island.");
        int Listeners()=>((MulticastDelegate?)typeof(AssistantRuntime).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ai))?.GetInvocationList().Length??0;
        int baseline=Listeners();
        for(int i=0;i<12;i++){island.CloseAssistant();island.Reveal(false);island.SelectPage("media",false);island.OpenAssistant(false);}
        check("Repeated normal/AI navigation does not accumulate runtime listeners",Listeners()==baseline);
        foreach(double width in new[]{360d,526d})
        {
            s.PanelWidth=width;app.ChangeSettings();await Task.Delay(250);island.UpdateLayout();
            check("Inline editor stays inside island bounds at "+width,editor.ActualWidth>150 && editor.ActualWidth<=island.PaintedWidth-20);
            check("Scaled AI content fits the panel viewport at "+width,view.ActualHeight*s.PanelScale<=viewport.ViewportHeight+.5);Capture("island-ai-chat-"+width);
        }
        island.CloseAssistant();await Task.Delay(300);
        ai.Options.Enabled=true;ai.Conversation.Record("user","Hello from my island");ai.Conversation.Record("assistant","I'm right here. We can chat or talk without leaving your island.");
        await Task.Delay(350);
        check("A new reply can emerge from the closed island without opening a window",island.AssistantPeeking && app.Windows.Count==windows && !island.IsExpanded);
        Capture("island-ai-reply");
        ai.Options.Providers.Add(new(){Name="Fixture",Model="fixture",Tools=true});ai.Options.SelectedProvider=ai.Options.Providers[0].Id;island.OpenAssistant(false);await Task.Delay(150);
        var send=Button("Send");string retainedDraft=editor.Text;editor.Clear();check("Empty messages keep Send disabled",!send.IsEnabled);editor.Text=retainedDraft;check("Typing enables Send without changing the draft",send.IsEnabled);
        check("Composer removes the redundant keyboard hint",!Diagnostics.Descendants(view).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Enter ")));Capture("island-ai-refined-composer");
        var live=LiveVoice();
        island.Collapse();await Task.Delay(250);
        check("Collapsing inline chat keeps a live session with a visible microphone indicator",ai.VoiceConnected && !live.Stopped && character.IsVisible && closedOrb.Live);
        island.PreviewAssistant();await Task.Delay(250);var peekOrb=Field<AssistantOrb>(island,"peekAssistant");
        check("The visible listening peek owns an active orb render subscription",peekOrb.IsVisible && peekOrb.Rendering);
        island.OpenAssistant(false);await Task.Delay(250);
        check("Opening live inline AI collapses the faded peek and releases its orb rendering",!peekOrb.IsVisible && !peekOrb.Rendering && Diagnostics.Descendants(view).OfType<AssistantOrb>().Single().Rendering && ai.VoiceConnected);
        Button("Back to island").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(250);
        check("Back to island keeps voice running and preserves the inline draft",!island.AssistantOpen && ai.VoiceConnected && !live.Stopped && editor.Text=="A draft right inside my island.");
        island.PreviewAssistant();await Task.Delay(200);Invoke("HideAssistantPeek");island.PreviewAssistant();await Task.Delay(550);
        check("A reveal during the peek exit fade keeps the new listening peek visible",island.AssistantPeeking && peekOrb.IsVisible && peekOrb.Rendering && Field<Grid>(island,"assistantPeek").Opacity>.99);
        bool motionEnabled=Motion.Enabled;Motion.Enabled=false;island.OpenAssistant(false);
        check("Reduced-motion inline entry collapses the hidden peek immediately",!peekOrb.IsVisible && !peekOrb.Rendering && ai.VoiceConnected);Motion.Enabled=motionEnabled;
        s.AlwaysTucked=true;app.ChangeSettings();island.DiagnosticPointerPresent=true;island.Collapse();island.RevealTuckedPreview();await Task.Delay(200);
        check("A tucked live character reveals AI hover at the top edge",island.AssistantPeeking && !island.IsTucked && ai.VoiceConnected);
        island.DiagnosticPointerPresent=false;island.Collapse();await Task.Delay(200);Invoke("BeginFileDrop");Invoke("ShowShelfDropPreview",(object)Array.Empty<string>());island.CheckEnvironment();await Task.Delay(200);island.CheckEnvironment();
        check("A file drag onto a tucked live session retains visible files and feedback across environment checks without a pointer",!island.DiagnosticPointerPresent && !island.IsTucked && !island.AssistantPeeking && island.PeekPage=="files" && Field<Border>(island,"shelfDropPreview").IsVisible && ai.VoiceConnected);
        Invoke("FinishFileDrop",false);await Task.Delay(200);
        check("Canceling a tucked live file drag returns to the tucked edge without stopping voice",island.IsTucked && !island.IsPeeking && !island.AssistantPeeking && ai.VoiceConnected && !live.Stopped);
        Invoke("BeginFileDrop");
        var droppedPath=Path.Combine(directory,"island-ai-closed.png");
        app.Productivity.AddFiles(new[]{droppedPath});Invoke("FinishFileDrop",true);
        check("An accepted tucked live file drop stages the fixture and keeps files visible",app.Productivity.Shelf.Any(item=>item.Path==droppedPath) && island.IsPeeking && island.PeekPage=="files" && !island.AssistantPeeking && !island.IsTucked && ai.VoiceConnected && !live.Stopped);
        s.AlwaysTucked=false;app.ChangeSettings();
        island.OpenAssistant(false);ai.Desktop.Start("fixture",IntPtr.Zero);int stopsBefore=desktop.Stops;bool closeCanceled=false;System.ComponentModel.CancelEventHandler closing=(_,e)=>closeCanceled=e.Cancel;island.Closing+=closing;island.Close();island.Closing-=closing;await Task.Delay(150);
        check("The actual Closing path cancels close, manually hides and stops fixture voice, screen and control",closeCanceled && island.UserHidden && !island.IsVisible && app.Windows.Cast<Window>().Contains(island) && !ai.VoiceConnected && live.Stopped && live.Disposed && !ai.Desktop.Sharing && desktop.Stops>stopsBefore);
        island.ToggleVisibility();live=LiveVoice();island.OpenAssistant(false);ai.Desktop.Start("fixture",IntPtr.Zero);
        var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(island)!,0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};island.RaiseEvent(escape);await Task.Delay(150);
        check("AI Escape stops fixture voice and screen sharing while leaving the island visible",escape.Handled && island.IsVisible && !island.AssistantOpen && !ai.VoiceConnected && live.Stopped && live.Disposed && !ai.Desktop.Sharing);
        live=LiveVoice();
        island.Collapse();island.ToggleVisibility();ai.Conversation.Record("assistant","A hidden island must stay hidden.");await Task.Delay(100);
        check("Replies respect manual hide, stop the live session and do not reveal the island",island.UserHidden && !island.IsVisible && !ai.VoiceConnected && live.Stopped && live.Disposed && !ai.Desktop.Sharing && !closedOrb.Rendering);
        island.ToggleVisibility();ai.SetIslandCharacter(false);await Task.Delay(100);
        check("Island character can be turned off independently",!character.IsVisible && !ai.Options.IslandCharacter && !ai.VoiceConnected);
        ai.SetIslandCharacter(true);island.Collapse();await Task.Delay(250);
        for(int retry=0;retry<20 && !character.IsVisible;retry++)await Task.Delay(50);
        check("Re-enabled character returns after the exit transition",character.IsVisible);
        check($"Turning the character back on does not start capture (visible={character.IsVisible}, voice={ai.VoiceConnected}, sharing={ai.Desktop.Sharing}, tucked={island.IsTucked}, peeking={island.IsPeeking})",!ai.VoiceConnected && !ai.Desktop.Sharing && ai.Options.IslandCharacter);
        check("Lifecycle checks used only fixture screen sessions with no capture or native input",desktop.Starts==2 && desktop.Captures==0 && desktop.Inputs==0);
        ai.Clear();
        app.ShowAssistantProviders();await Task.Delay(250);
        var settings=app.Windows.OfType<SettingsWindow>().Single();
        var providerEditor=Diagnostics.Descendants(settings).OfType<AssistantProviderSettings>().Single();
        check("Provider entry opens the AI category in the main Settings app",providerEditor.IsVisible && !app.Windows.OfType<AssistantProvidersWindow>().Any());
        var modelChoice=Field<ComboBox>(providerEditor,"model");providerEditor.RevealSetupTarget(modelChoice);modelChoice.ApplyTemplate();
        var model=(TextBox)modelChoice.Template.FindName("PART_EditableTextBox",modelChoice);model.Text="Unsaved fixture model";model.Select(2,4);
        typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(settings,new object[]{"Application"});settings.OpenAiSettings();await Task.Delay(150);
        check("AI settings preserve provider editing drafts across category navigation",ReferenceEquals(providerEditor,Diagnostics.Descendants(settings).OfType<AssistantProviderSettings>().Single()) && model.Text=="Unsaved fixture model" && model.SelectionStart==2 && model.SelectionLength==4);
        providerEditor.RevealSetupTarget(modelChoice);connectionEditorForTest();
        void connectionEditorForTest()=>Field<Expander>(providerEditor,"connectionEditor").IsExpanded=true;
        foreach(double width in new[]{1100d,600d})
        {
            providerEditor.RevealSetupTarget(modelChoice);connectionEditorForTest();settings.Width=width;settings.Height=650;await Task.Delay(150);settings.UpdateLayout();
            check("Embedded AI settings keep connection fields inside the form at "+width,double.IsNaN(providerEditor.Height) && model.ActualWidth>100 && model.ActualWidth<settings.ActualWidth);
            if(width==600)
            {
                var category=Diagnostics.Descendants(settings).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Settings category");
                typeof(SettingsWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(settings,new object[]{"Application"});bool first=category.SelectedItem?.ToString()=="General";settings.OpenAiSettings();
                check("Programmatic AI navigation keeps the compact category picker synchronized",first && category.SelectedItem?.ToString()=="Naby AI");
            }
            var image=new RenderTargetBitmap((int)settings.ActualWidth,(int)settings.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(settings);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(directory,"settings-ai-"+width+".png"));png.Save(file);
        }
        var password=Field<PasswordBox>(providerEditor,"key");password.Password="fixture-only-unsaved-key";
        // Closing now protects unsaved provider drafts. Exercise Discard on this
        // synthetic fixture instead of leaving the suite waiting in a modal dialog.
        bool discarded=false;
        var discardPrompt=settings.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>
        {
            var prompt=app.Windows.OfType<Window>().Single(w=>w.Title=="Unsaved provider changes");
            Diagnostics.Descendants(prompt).OfType<Button>().Single(b=>b.Content?.ToString()=="Discard").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));discarded=true;
        }));
        bool settingsClosed=false;settings.Closed+=(_,_)=>settingsClosed=true;settings.Close();
        await discardPrompt;
        for(int wait=0;wait<30 && !settingsClosed;wait++)await Task.Delay(50);
        check("Closing Settings confirms discard of the synthetic unsaved provider draft",discarded);
        check("Discarding the synthetic draft actually closes Settings and releases its window",settingsClosed && !app.Windows.OfType<SettingsWindow>().Any());
        check("Closing Settings clears the unsaved key without granting session capture",password.Password.Length==0 && !ai.VoiceConnected && !ai.Desktop.Sharing);
        ai.Options.Providers.Clear();ai.Options.SelectedProvider=ai.Options.SelectedVoiceProvider="";app.ShowAssistantProviders();await Task.Delay(100);
        settings=app.Windows.OfType<SettingsWindow>().Single();providerEditor=Diagnostics.Descendants(settings).OfType<AssistantProviderSettings>().Single();
        var emptyConnection=Field<Expander>(providerEditor,"connectionEditor");var emptyProvider=Field<ComboBox>(providerEditor,"chatProvider");
        check($"First-run AI settings hide empty connection fields until a provider is added (providers={ai.Options.Providers.Count}, visibility={emptyConnection.Visibility}, enabled={emptyProvider.IsEnabled})",emptyConnection.Visibility==Visibility.Collapsed && !emptyProvider.IsEnabled);
        settings.Close();
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Actual WPF island render with synthetic conversation. No provider API, credential vault, microphone/speaker, desktop capture or native input used. A shared runtime drives closed character, hover greeting and inline chat. No separate Companion window is created by the tested entry point.");
        void Capture(string name)
        {
            island.UpdateLayout();var full=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth),(int)Math.Ceiling(island.ActualHeight),96,96,PixelFormats.Pbgra32);full.Render(island);
            int width=Math.Min(full.PixelWidth,(int)Math.Ceiling(island.PaintedWidth)),height=Math.Min(full.PixelHeight,(int)Math.Ceiling(island.PaintedHeight));var crop=new CroppedBitmap(full,new Int32Rect(Math.Max(0,(full.PixelWidth-width)/2),0,width,height));
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(crop));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
    }
}
