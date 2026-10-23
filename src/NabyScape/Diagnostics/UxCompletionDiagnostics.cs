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
using System.Windows.Threading;
namespace NabyScape;
internal static class UxCompletionDiagnostics
{
    private const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        island.Hide();var ai=app.Assistant;ai.Options.Enabled=true;
        var first=new AssistantProvider{Name="Fixture first",Model="fixture",BaseUrl="https://fixture.invalid/v1/"};var second=new AssistantProvider{Name="Fixture second",Model="fixture",BaseUrl="https://fixture.invalid/v1/"};
        ai.Options.Providers.Add(first);ai.Options.Providers.Add(second);ai.Options.SelectedProvider=first.Id;app.Store.Save("assistant",ai.Options);
        var window=new SettingsWindow(app){ShowActivated=false};
        T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,Hidden)!.GetValue(instance)!;
        Button Button(DependencyObject owner,string name)=>Diagnostics.Descendants(owner).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name);
        async Task Settle(){await Task.Delay(100);window.UpdateLayout();}
        try
        {
            window.Show();Diagnostics.NavigateSettings(window,"AI");await Settle();var editor=Field<AssistantProviderSettings>(window,"aiSettings");
            check("UX goal setup offers chat, talk and Hermes with an explicit pipeline",Diagnostics.Descendants(editor).OfType<Button>().Any(b=>b.Content?.ToString()=="Set up voice") && ai.SetupSummary.Contains("AI replies:") && ai.SetupSummary.Contains("Listening:") && ai.SetupSummary.Contains("Speaking:"));
            editor.GetType().GetMethod("SelectSetupSection",Hidden)!.Invoke(editor,new object[]{"Chat"});await Settle();
            var name=Field<TextBox>(editor,"name");var list=Field<ComboBox>(editor,"list");name.Text="Draft name";check("UX provider draft is detected without saving",editor.HasProviderDraft && first.Name=="Fixture first");
            async Task Pick(string action)
            {
                var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};timer.Tick+=(_,_)=>{var prompt=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.Title=="Unsaved provider changes");if(prompt==null)return;timer.Stop();Button(prompt,action).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));};timer.Start();list.SelectedItem=second;await Task.Delay(180);timer.Stop();
            }
            await Pick("Keep editing");check("UX keep editing preserves draft and original provider selection",list.SelectedItem==first && name.Text=="Draft name" && editor.HasProviderDraft);
            await Pick("Discard");check("UX discard switches provider without persisting draft",list.SelectedItem==second && first.Name=="Fixture first" && !editor.HasProviderDraft);
            list.SelectedItem=first;await Settle();name.Text="Saved fixture";await Pick("Save");check("UX save persists draft before switching provider",first.Name=="Saved fixture" || ai.Options.Providers.Single(p=>p.Id==first.Id).Name=="Saved fixture");
            check("UX provider switch ends with the requested provider and no pending draft",(list.SelectedItem as AssistantProvider)?.Id==second.Id && !editor.HasProviderDraft);
            var vision=Field<CheckBox>(editor,"vision");vision.IsChecked=true;vision.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check("UX image capability distinguishes unsaved from verified",Field<TextBlock>(editor,"imageCheckStatus").Text.Contains("changed") && Field<TextBlock>(editor,"capabilityStatus").Text.Contains("Not saved"));
            vision.IsChecked=false;vision.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check("UX reverting draft clears pending state",!editor.HasProviderDraft);
            check("UX image test sends a generated PNG without user content",AssistantProviderSettings.ImageTestData().StartsWith("data:image/png;base64,"));
            editor.OpenVoiceChecks();await Settle();
            Diagnostics.LogicalDescendants(editor).OfType<Expander>().Single(e=>AutomationProperties.GetName(e)=="Speech checks").IsExpanded=true;await Settle();
            check("UX speech checks are explicit and do not start capture on page load",Button(editor,"Test microphone").IsEnabled && Button(editor,"Test transcription").IsEnabled && Button(editor,"Play sample").IsEnabled && !ai.SpeechCheckBusy && !ai.VoiceConnected);
            var search=Field<TextBox>(window,"settingsSearch");search.Text="uninstall model";await Settle();var results=Field<StackPanel>(window,"settingsResults");
            check("UX search finds uninstall by intent",results.Children.OfType<Button>().Any());results.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
            var storage=Diagnostics.Descendants(editor).OfType<Expander>().Single(e=>AutomationProperties.GetName(e)=="Installed speech models");check("UX search opens the nested storage section",storage.IsExpanded && storage.IsVisible);
            search.Text="wake word";await Settle();results.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();check("UX search reveals nested wake settings",Diagnostics.Descendants(editor).OfType<Expander>().Any(e=>AutomationProperties.GetName(e)=="Wake word settings" && e.IsExpanded && e.IsVisible));
            check("UX capture status distinguishes wake, microphone and screen",ai.CaptureSummary.Contains("Wake off") && ai.CaptureSummary.Contains("Mic off") && ai.CaptureSummary.Contains("Screen not shared"));
            ai.BeginSpeechCheck(()=>{});check("UX speech test reserves model lifecycle",ai.SpeechCheckBusy && ai.Speech.Busy);ai.EndSpeechCheck();check("UX speech test releases model lifecycle",!ai.SpeechCheckBusy && !ai.Speech.Busy);
            ai.Options.WakeWord.Enabled=true;typeof(AssistantRuntime).GetField("wakeWanted",Hidden)!.SetValue(ai,true);ai.BeginSpeechCheck(()=>{});ai.EndSpeechCheck();check("UX speech check restores previously armed wake listening",ai.WakeArmed);
            ai.BeginSpeechCheck(()=>{});ai.StopSession();ai.EndSpeechCheck();check("UX explicit Stop during speech check prevents wake resume",!ai.WakeArmed);ai.Options.WakeWord.Enabled=false;
            search.Text="image input";await Settle();results.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();window.Width=520;window.Height=640;await Settle();vision.BringIntoView();await Settle();Capture(window,Path.Combine(directory,"ux-image-settings-narrow.png"));
            var file=new AssistantAttachment("fixture.pdf","application/pdf","data:application/pdf;base64,AQID",Frames:new[]{"data:image/png;base64,AQID"},Note:"Fixture");
            second.Backend="Hermes";
            check("UX attachment summary names exactly the prepared pages",AssistantAttachmentSummary.Describe(file,second).Contains("first 1 pages") && AssistantAttachmentSummary.Describe(file,second).Contains("Image input is Off"));
        }
        finally{window.Close();ai.StopSession();}
    }
    private static void Capture(FrameworkElement element,string path){var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(element);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);}
}
