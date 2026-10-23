using System;
using System.Collections.Generic;
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

// Synthetic runtime state and actual WPF measure/arrange/render. No devices,
// provider, credentials, desktop capture or user conversation are used.
internal static class VoiceLayoutDiagnostics
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var ai=app.Assistant;var settings=app.Settings;var evidence=new List<string>();
        void Set(string name,object? value)=>typeof(AssistantRuntime).GetField(name,Fields)!.SetValue(ai,value);
        T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Fields)!.GetValue(target)!;
        void Invoke(object target,string method)=>target.GetType().GetMethod(method,Fields)!.Invoke(target,null);
        int Listeners(string name)=>((MulticastDelegate?)typeof(AssistantRuntime).GetField(name,Fields)!.GetValue(ai))?.GetInvocationList().Length??0;
        Button Button(AssistantVoiceView view,string name)=>Diagnostics.Descendants(view).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name);
        bool Inside(FrameworkElement element,FrameworkElement viewport)
        {
            var bounds=element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
            if(!element.IsVisible || bounds.Width<=0 || bounds.Height<=0 || bounds.Left<-.75 || bounds.Top<-.75 || bounds.Right>viewport.ActualWidth+.75 || bounds.Bottom>viewport.ActualHeight+.75)return false;
            for(var ancestor=VisualTreeHelper.GetParent(element);ancestor!=null && ancestor!=viewport;ancestor=VisualTreeHelper.GetParent(ancestor))
                if(ancestor is ScrollContentPresenter presenter)
                {
                    var clipped=element.TransformToAncestor(presenter).TransformBounds(new Rect(element.RenderSize));
                    if(clipped.Left<-.75 || clipped.Top<-.75 || clipped.Right>presenter.ActualWidth+.75 || clipped.Bottom>presenter.ActualHeight+.75)return false;
                }
            return true;
        }
        void Capture(FrameworkElement element,string name)
        {
            element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
        }
        var voice=new AssistantDiagnostics.FixtureVoice();
        void State(AssistantState state,bool live)
        {
            Set("voice",live?voice:null);Set("state",state);Set("<Status>k__BackingField",state==AssistantState.Thinking?"Waiting for Hermes · preparing a detailed synthetic response with additional status text":"Ready to talk");
            Set("<VoiceCaption>k__BackingField","Here is a deliberately long synthetic spoken response. The caption should stay bounded while every voice control remains reachable and the animated character keeps its normal proportions.");
            Set("<VoiceTiming>k__BackingField",live?new AssistantVoiceTiming(1,1200,4200,1800,7200,10000,true):null);
            Set("wakeWanted",live);Set("backgroundReady",false);
        }
        settings.Width=327;settings.Height=36;settings.PanelWidth=526;settings.PanelMaxHeight=417;settings.PanelScale=1.04;settings.PanelPadding=32;settings.SectionGap=18;settings.CategoryStyle="Dots";settings.CompactLayout=true;settings.HideMaximized=settings.HideFullscreen=false;
        var voiceWidget=settings.Widgets.FirstOrDefault(w=>w.Id=="voice");if(voiceWidget!=null)voiceWidget.Enabled=true;else settings.Widgets.Add(new(){Id="voice",Enabled=true});
        ai.Options.Enabled=true;ai.Options.WakeWord.Enabled=true;ai.Options.LocalVoice.Enabled=false;
        var provider=new AssistantProvider{Name="Synthetic fixture",Model="fixture",RealtimeModel="fixture",RealtimeUrl="wss://voice-fixture.invalid/"};ai.Options.Providers.Add(provider);ai.Options.SelectedProvider=ai.Options.SelectedVoiceProvider=provider.Id;Set("setupCache",null);
        ai.Conversation.Record("user","Open Chrome");ai.Conversation.Record("assistant","Speak naturally. Naby will reply.");
        State(AssistantState.Thinking,true);app.ChangeSettings();island.Show();island.Reveal(false);island.SelectPage("voice",false);await Settle(island);
        try
        {
            foreach(var sample in new[]{(Name:"current-padding32",Width:526d,Height:417d,Scale:1.04,Padding:32d,Style:"Dots"),(Name:"reported-padding11",Width:526d,Height:417d,Scale:1.04,Padding:11d,Style:"Dots"),(Name:"legacy-icons",Width:546d,Height:504d,Scale:1d,Padding:40d,Style:"Icons")})
            {
                settings.PanelWidth=sample.Width;settings.PanelMaxHeight=sample.Height;settings.PanelScale=sample.Scale;settings.PanelPadding=sample.Padding;settings.CategoryStyle=sample.Style;app.ChangeSettings();island.SelectPage("voice",false);await Settle(island);
                var view=Diagnostics.Descendants(island).OfType<AssistantVoiceView>().Single(v=>v.IsVisible);Invoke(view,"Refresh");await Settle(island);var viewport=Field<ScrollViewer>(island,"scroll");viewport.ScrollToTop();await Settle(island);
                bool controls=new[]{"End conversation","Chat","Voice","Options"}.All(name=>Inside(Button(view,name),viewport));
                check("Voice "+sample.Name+" essential controls fit painted viewport without scrolling",controls && viewport.ScrollableHeight<1 && Diagnostics.Descendants(view).OfType<ScrollViewer>().All(s=>s.ScrollableHeight<1));
                evidence.Add($"{sample.Name}: surface={island.PaintedWidth:0.##}x{island.PaintedHeight:0.##}; viewport={viewport.ActualWidth:0.##}x{viewport.ActualHeight:0.##}; extent={viewport.ExtentHeight:0.##}; voice={view.ActualWidth:0.##}x{view.ActualHeight:0.##}; controlsInside={controls}.");
                evidence.Add("  Voice scrollers: "+string.Join("; ",Diagnostics.Descendants(view).OfType<ScrollViewer>().Select(s=>$"{s.ViewportWidth:0.###}x{s.ViewportHeight:0.###}/{s.ExtentHeight:0.###}")));
                foreach(var name in new[]{"End conversation","Chat","Voice","Options"})
                {
                    var control=Button(view,name);evidence.Add("  "+name+" inside="+Inside(control,viewport)+" bounds="+control.TransformToAncestor(viewport).TransformBounds(new Rect(control.RenderSize)));
                }
                if(sample.Name=="current-padding32")Capture(island,"voice-default-live");
                foreach(var state in new[]{AssistantState.Idle,AssistantState.Speaking})
                {
                    State(state,state==AssistantState.Speaking);Invoke(view,"Refresh");await Settle(island);
                    check("Voice "+sample.Name+" "+state+" footer and session action stay painted",new[]{state==AssistantState.Speaking?"End conversation":"Start conversation","Chat","Voice","Options"}.All(name=>Inside(Button(view,name),viewport)) && viewport.ScrollableHeight<1 && Diagnostics.Descendants(view).OfType<ScrollViewer>().All(s=>s.ScrollableHeight<1));
                }
                State(AssistantState.Thinking,true);
            }
            island.Hide();
            foreach(var sample in new[]{(Name:"default-ready",Width:526d,Height:417d,Scale:1.04,Text:1d,Live:false),(Name:"narrow-live",Width:300d,Height:417d,Scale:1.04,Text:1d,Live:true),(Name:"short-live",Width:526d,Height:300d,Scale:1.04,Text:1d,Live:true),(Name:"large-text",Width:526d,Height:540d,Scale:1.5,Text:1.5,Live:true),(Name:"narrow-large-text",Width:300d,Height:300d,Scale:1.5,Text:1.5,Live:true),(Name:"extreme-height",Width:300d,Height:190d,Scale:1.5,Text:1.5,Live:true)})
            {
                settings.PanelScale=sample.Scale;settings.AccessibilityTextScale=sample.Text;Accessibility.Configure(settings);State(sample.Live?AssistantState.Speaking:AssistantState.Idle,sample.Live);
                var view=new AssistantVoiceView(app){LayoutTransform=new ScaleTransform(sample.Scale,sample.Scale)};
                var host=new Grid{Background=UI.Brush("#0B0C0E"),Margin=new(11)};host.Children.Add(view);
                var window=new Window{Content=host,Width=sample.Width,Height=sample.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowActivated=false,ShowInTaskbar=false};
                try
                {
                    window.Show();await Settle(window);Invoke(view,"Refresh");await Settle(window);
                    bool normal=sample.Name is "default-ready" or "narrow-live" or "short-live" or "large-text";
                    var primary=Button(view,sample.Live?"End conversation":"Start conversation");var options=Button(view,"Options");
                    var scrollers=Diagnostics.Descendants(view).OfType<ScrollViewer>().ToArray();
                    if(normal)check("Voice "+sample.Name+" controls fully visible without scrolling",new[]{primary,Button(view,"Chat"),Button(view,"Voice"),options}.All(b=>Inside(b,host)) && (sample.Name is not ("default-ready" or "narrow-live") || scrollers.All(s=>s.ScrollableHeight<1)));
                    else
                    {
                        primary.BringIntoView();await Settle(window);check("Voice "+sample.Name+" stop action is reachable through bounded fallback",Inside(primary,host) && view.ActualHeight*sample.Scale<=host.ActualHeight+.75);
                        options.BringIntoView();await Settle(window);
                        check("Voice "+sample.Name+" footer stays inside finite viewport",new[]{Button(view,"Chat"),Button(view,"Voice"),options}.All(b=>Inside(b,host)));
                    }
                    bool textFits=Diagnostics.Descendants(view).OfType<TextBlock>().Where(t=>t.IsVisible && t.ActualWidth>0).All(t=>{var bounds=t.TransformToAncestor(host).TransformBounds(new Rect(t.RenderSize));return bounds.Left>=-.75 && bounds.Right<=host.ActualWidth+.75;});
                    check("Voice "+sample.Name+" text and footer labels stay within horizontal bounds",textFits);
                    var orb=Diagnostics.Descendants(view).OfType<AssistantOrb>().Single();
                    check("Voice "+sample.Name+" retains proportional character and active animation",Math.Abs(orb.ActualWidth-orb.ActualHeight)<.1 && (!sample.Live || orb.Rendering));
                    evidence.Add($"{sample.Name}: host={host.ActualWidth:0.##}x{host.ActualHeight:0.##}; view={view.ActualWidth:0.##}x{view.ActualHeight:0.##}; scale={sample.Scale}; text={sample.Text}; scrolls={string.Join(",",scrollers.Select(s=>$"{s.ViewportHeight:0.##}/{s.ExtentHeight:0.##}"))}.");
                    if(sample.Name is "narrow-live" or "large-text" or "narrow-large-text")Capture(window,"voice-"+sample.Name);
                    bool contentRetained=true;
                    foreach(var state in new[]{AssistantState.Idle,AssistantState.Thinking,AssistantState.Speaking,AssistantState.Error})
                    {
                        State(state,state is AssistantState.Thinking or AssistantState.Speaking);Invoke(view,"Refresh");await Task.Delay(20);
                        var expected=state is AssistantState.Thinking or AssistantState.Speaking?"End conversation":state==AssistantState.Error?"Try again":"Start conversation";
                        contentRetained&=primary.Content is Grid action && action.Children.Count==2 && action.Children.OfType<TextBlock>().Single().Text==Localization.Text(expected) && (state is AssistantState.Thinking or AssistantState.Speaking?action.Children[0] is System.Windows.Shapes.Path filled && filled.Fill!=null:action.Children[0] is Viewbox);
                    }
                    check("Voice "+sample.Name+" state changes retain wrapped action label and icon",contentRetained);State(sample.Live?AssistantState.Speaking:AssistantState.Idle,sample.Live);Invoke(view,"Refresh");
                    int changed=Listeners("Changed"),visual=Listeners("VisualChanged"),layouts=0;SizeChangedEventHandler resized=(_,_)=>layouts++;view.SizeChanged+=resized;
                    for(int i=0;i<5;i++){view.Visibility=Visibility.Collapsed;await Task.Delay(10);view.Visibility=Visibility.Visible;await Task.Delay(10);}await Settle(window);
                    check("Voice "+sample.Name+" repeated visibility releases and restores one set of listeners",Listeners("Changed")==changed && Listeners("VisualChanged")==visual);
                    int stable=layouts;for(int i=0;i<5;i++){Invoke(view,"Refresh");window.UpdateLayout();await Task.Delay(20);}
                    check("Voice "+sample.Name+" repeated refresh does not feed back into layout",layouts==stable);view.SizeChanged-=resized;
                    window.Close();await Task.Delay(30);check("Voice "+sample.Name+" unload releases runtime and character scheduling",Listeners("Changed")==changed-1 && Listeners("VisualChanged")==visual-1 && !orb.Rendering && !orb.IdleAnimating);
                }
                finally{window.Close();}
            }
            check("Voice geometry fixtures preserve the synthetic live session",voice.Connected && !voice.Stopped && !voice.Disposed && !ai.Desktop.Sharing);
            // Exercise the actual asynchronous end path without opening a microphone.
            Set("voice",new AssistantDiagnostics.FixtureVoice());Set("wakeWanted",false);
            await ai.EndVoice();
            check("Ending manually started voice rearms enabled wake word after a setup pause",ai.WakeArmed && !ai.VoiceConnected && ai.WakeStatus.Contains("will resume"));
            ai.StopSession();check("Stop all microphones keeps wake word paused",!ai.WakeArmed);
            var closing=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Set("voiceClosing",closing.Task);
            var ending=ai.EndVoice();ai.StopSession();closing.SetResult();await ending;
            check("Stop all microphones during voice teardown cancels pending wake resume",!ai.WakeArmed);
            Set("voiceClosing",Task.CompletedTask);ai.Options.WakeWord.Enabled=false;await ai.EndVoice();
            check("Ending voice never enables a disabled wake word",!ai.WakeArmed && !ai.WakeListening);
            var feedbackWindow=new SettingsWindow(app){ShowActivated=false};
            try
            {
                var window=feedbackWindow;window.Show();await Settle(window);
                var saved=Field<TextBlock>(window,"status");
                settings.CoverAmbient=!settings.CoverAmbient;Invoke(window,"Save");await Task.Delay(650);
                check("Settings confirms the actual saved preference and active state",app.SettingsSaveSucceeded && saved.Text.Contains("Ambient light: ") && saved.Text.StartsWith("Saved · "));
                Capture(window,"settings-save-confirmation");
                using(var locked=new FileStream(Path.Combine(app.Store.DirectoryPath,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.None))
                {
                    settings.CoverAmbient=!settings.CoverAmbient;Invoke(window,"Save");await Task.Delay(650);
                    check("Settings disk failure never displays a saved confirmation",!app.SettingsSaveSucceeded && saved.Text.StartsWith("Not saved"));
                }
                Invoke(window,"Save");await Task.Delay(650);
                check("Settings can retry and confirm persistence after a disk failure",app.SettingsSaveSucceeded && saved.Text.StartsWith("Saved · ") && app.Store.Load<Settings>("settings").CoverAmbient==settings.CoverAmbient);
                Diagnostics.NavigateSettings(window,"AI");await Settle(window);var editor=Field<AssistantProviderSettings>(window,"aiSettings");
                editor.GetType().GetMethod("SelectSetupSection",Fields)!.Invoke(editor,new object[]{"Chat"});await Settle(window);
                Field<Expander>(editor,"connectionEditor").IsExpanded=true;foreach(var expander in Diagnostics.LogicalDescendants(editor).OfType<Expander>())expander.IsExpanded=true;await Settle(window);
                var input=Field<CheckBox>(editor,"vision");var output=Field<CheckBox>(editor,"imageReplies");
                check("Provider image controls are real switches with explicit saved Off states",input.IsChecked==false && input.Content.ToString()=="Image input · Off" && output.IsChecked==false);
                input.IsChecked=true;input.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(40);
                check("Image input draft is On without prematurely changing saved provider",input.Content.ToString()=="Image input · On" && !ai.Provider!.Vision && saved.Text.StartsWith("Not saved yet"));
                var save=Diagnostics.Descendants(editor).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Save provider");
                save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle(window);
                check("Saving image input persists it and reports the new On state",ai.Provider!.Vision && app.Store.Load<AssistantOptions>("assistant").Providers.Single(p=>p.Id==ai.Provider.Id).Vision && saved.Text.Contains("image input On"));
                input.BringIntoView();await Settle(window);Capture(window,"provider-image-input-saved");
                window.Width=520;window.Height=640;await Settle(window);input.BringIntoView();await Settle(window);Capture(window,"provider-image-input-narrow");
                check("Settings feedback stays visible in a narrow window",saved.IsVisible && saved.ActualWidth>0 && saved.TransformToAncestor(window).TransformBounds(new Rect(saved.RenderSize)).Bottom<=window.ActualHeight);
            }
            finally{if(Field<AssistantProviderSettings>(feedbackWindow,"aiSettings") is {} pending)pending.GetType().GetField("loadingProvider",Fields)?.SetValue(pending,true);feedbackWindow.Close();}
        }
        finally
        {
            Set("voice",null);Set("wakeWanted",false);ai.Options.WakeWord.Enabled=false;settings.AccessibilityTextScale=1;Accessibility.Configure(settings);ai.Conversation.Clear();
            File.WriteAllLines(Path.Combine(directory,"voice-layout-evidence.txt"),evidence);
            File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Synthetic voice state, captions, transcript, timings and wake privacy only. Real WPF geometry and rendering. No provider calls, credentials, microphones, speakers, desktop capture or native input. Current526/417/1.04/padding32 and reportedpadding11 tested separately. Short-screen geometry uses a finite native Window viewport rather than changing the user's display.");
        }
    }
    private static async Task Settle(Window window){window.UpdateLayout();await Task.Delay(650);window.UpdateLayout();await Task.Delay(80);}
}
