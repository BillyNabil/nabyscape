using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

internal sealed partial class AssistantProviderSettings : UserControl
{
    private readonly AssistantRuntime ai;
    private readonly App app;
    private readonly ComboBox model=new(){IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true},realtimeModel=new(){IsEditable=true,IsTextSearchEnabled=true,StaysOpenOnEdit=true};
    private readonly ComboBox list=new(),preset=new(){ItemsSource=AssistantProviderCatalog.Presets,SelectedIndex=0},voiceProvider=new(),chatProvider=new();
    private readonly ComboBox protocol=new(){ItemsSource=new[]{"ChatCompletions","Responses","Anthropic","Gemini"}};
    private readonly ComboBox realtimeProtocol=new(){ItemsSource=AssistantVoiceSession.Protocols};
    private readonly TextBox name=UI.Input("Provider name",""),url=UI.Input("API base URL",""),realtimeUrl=UI.Input("Realtime WebSocket URL",""),voiceName=UI.Input("Voice name",""),instructions=UI.Input("Assistant instructions","");
    private readonly PasswordBox key=new(){MaxLength=2000};
    private readonly TextBlock status=UI.Text("",11,UI.Muted),keyStatus=UI.Text("",11,UI.Muted);
    private readonly TextBlock hermesHelp=UI.Text("Connect your own Hermes API server. Its tools, skills and memory stay in Hermes. Naby handles chat, voice and the sharing source you select. Use Connect Hermes for the setup guide.",11,UI.Muted);
    private readonly Button enable;
    private readonly CheckBox vision,imageReplies;
    private readonly TextBlock capabilityStatus=UI.Text("",11,UI.Muted);
    internal event Action<string,bool>? FeedbackChanged;
    private void Feedback(string text,bool error=false){status.Foreground=error?UI.Brush("#F0B8B2"):UI.Muted;Accessibility.Announce(status,text);FeedbackChanged?.Invoke(text,error);}
    private bool syncing,imageInput,imageOutput;
    private readonly Expander connectionEditor=new(){Header="Edit connection details",IsExpanded=false,Margin=new(0,14,0,8)};
    internal AssistantProviderSettings(App app,bool embedded=true)
    {
        this.app=app;ai=app.Assistant;Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/NabyScape;component/Features/Assistant/AssistantTheme.xaml",UriKind.Relative)});
        var page=new StackPanel{Margin=embedded?new Thickness(0):new Thickness(24,20,24,24)};
        var header=new DockPanel();var orb=new AssistantOrb{Width=54,Height=54,ReduceMotion=app.Settings.ReduceMotion};header.Children.Add(orb);var intro=new StackPanel{Margin=new(12,0,0,0)};intro.Children.Add(UI.Heading("Your AI, your provider",22));intro.Children.Add(UI.Text("Connect an API. Choose how you chat, speak and share context.",12,UI.Muted));header.Children.Add(intro);if(!embedded)page.Children.Add(header);
        BuildSetupNavigation(page);var chatPage=setupPages["Chat"];var voicePage=setupPages["Voice"];var advancedPage=setupPages["Advanced"];
        enable=AssistantWindow.Pill("",()=>{ai.Options.Enabled=!ai.Options.Enabled;SaveConfiguration("AI companion");RefreshEnabled();});enable.Margin=new(0,16,0,12);advancedPage.Children.Insert(0,enable);
        var character=AssistantWindow.Pill(ai.Options.IslandCharacter?"Idle character visible":"Idle character hidden",()=>{});
        character.Click+=(_,_)=>{try{ai.SetIslandCharacter(!ai.Options.IslandCharacter);character.Content=ai.Options.IslandCharacter?"Idle character visible":"Idle character hidden";Feedback("Idle character saved · "+(ai.Options.IslandCharacter?"On":"Off")+".");}catch{Feedback("Could not save the island character preference.",true);}};
        advancedPage.Children.Add(character);setupPages["Hermes"].Children.Add(HermesSetup());
        chatPage.Children.Add(UI.Heading("Your chat connection",20));chatPage.Children.Add(UI.Text("Choose who answers your messages. Your connection stays saved on this PC.",13,UI.Muted));
        Label(chatPage,"Use for chat",chatProvider);UI.Named(chatProvider,"Chat provider");chatProvider.SelectionChanged+=async(_,_)=>{if(!syncing && chatProvider.SelectedItem is AssistantProvider selected && selected.Id!=ai.Options.SelectedProvider){try{if(!await ConfirmProviderChanges()){syncing=true;chatProvider.SelectedItem=ai.Provider;syncing=false;return;}ai.SelectProvider(selected.Id);Reload(selected.Id);Feedback("Chat provider saved · "+selected.Name+".");}catch(Exception){Feedback("Could not save the chat provider.",true);}}};
        voiceProvider.SelectionChanged+=(_,_)=>{if(!syncing && voiceProvider.SelectedItem is AssistantProvider selected){ai.Options.SelectedVoiceProvider=selected.Id;SaveConfiguration("Voice provider");}};
        var add=new Grid();add.ColumnDefinitions.Add(new());add.ColumnDefinitions.Add(new(){Width=GridLength.Auto});add.Children.Add(preset);var addButton=AssistantWindow.Pill("Add provider",AddProvider);addButton.Margin=new(8,0,0,0);Grid.SetColumn(addButton,1);add.Children.Add(addButton);var providerLibrary=new StackPanel();providerLibrary.Children.Add(UI.Text("Add another provider, or choose which saved connection to edit.",12,UI.Muted));providerLibrary.Children.Add(add);
Label(providerLibrary,"Saved connection to edit",list);chatPage.Children.Add(new Expander{Header="Add or manage connections",Content=providerLibrary,Margin=new(0,16,0,16)});UI.Named(list,"Saved AI providers");list.SelectionChanged+=async(_,_)=>{if(!syncing)await ChangeEditedProvider();};
        var chatStatus=new StackPanel();chatStatus.Children.Add(connectionStatus);chatStatus.Margin=new(0,14,0,14);chatPage.Children.Insert(4,chatStatus);
        var quick=new WrapPanel{Margin=new(0,0,0,12)};quick.Children.Add(AssistantWindow.Pill("Edit connection",()=>{connectionEditor.IsExpanded=true;connectionEditor.BringIntoView();}));quick.Children.Add(AssistantWindow.Pill("Check connection",()=>_=TestConnection()));quick.Children.Add(AssistantWindow.Pill("Open chat",app.ShowAssistant,true));chatPage.Children.Add(quick);
        var connection=new StackPanel();
        var saveActions=new WrapPanel{Margin=new(0,4,0,14)};saveActions.Children.Add(AssistantWindow.Pill("Save provider",()=>_=SaveProvider(),true));saveActions.Children.Add(AssistantWindow.Pill("Test connection",()=>_=TestConnection()));connection.Children.Add(saveActions);
        name.MaxLength=60;var technical=new StackPanel();Label(technical,"Connection name",name);Label(technical,"Chat API protocol",protocol);connection.Children.Add(new Expander{Header="Advanced · API protocol",Content=technical,Margin=new(0,8,0,4)});Label(connection,"API base URL",url);url.MaxLength=2048;Label(connection,"Model · choose one or paste its name",model);UI.Named(model,"Chat model");InitializeModelPicker(connection);
        Label(connection,"API key",key);UI.Named(key,"API key");connection.Children.Add(keyStatus);
        hermesHelp.Margin=new(0,10,0,10);connection.Children.Add(hermesHelp);
        connection.Children.Add(UI.Text("Your key is stored securely in Windows. Leave it blank to keep the saved key.",11,UI.Muted));
        var imageOptions=new StackPanel();
        vision=new CheckBox{Margin=new(0,10,0,8)};UI.Named(vision,"Image input");vision.Click+=(_,_)=>{imageInput=vision.IsChecked==true;RefreshCapabilities();RefreshDraftFeedback();};imageOptions.Children.Add(vision);imageOptions.Children.Add(UI.Text("Enable image input if this model can understand attached images, PDF pages, video frames and shared windows. Agent tools are managed in Hermes; Naby does not execute local actions.",11,UI.Muted));
        imageReplies=new CheckBox();UI.Named(imageReplies,"Image replies");imageReplies.Click+=(_,_)=>{imageOutput=imageReplies.IsChecked==true;RefreshCapabilities();RefreshDraftFeedback();};imageOptions.Children.Add(imageReplies);capabilityStatus.Margin=new(0,6,0,10);imageOptions.Children.Add(capabilityStatus);imageOptions.Children.Add(UI.Text("Enable image replies only for compatible image models: Responses image generation, Gemini image models or Chat Completions endpoints with image output. Hermes uses its own configured image tools. Image links returned in chat can always be displayed.",11,UI.Muted));protocol.SelectionChanged+=(_,_)=>RefreshCapabilities();
        BuildImageCheck(imageOptions);connection.Children.Add(new Expander{Header="Images & files",Content=imageOptions,Margin=new(0,16,0,10)});
        var realtime=new StackPanel();Label(realtime,"Voice protocol",realtimeProtocol);Label(realtime,"Realtime WebSocket URL",realtimeUrl);realtimeUrl.MaxLength=2048;realtime.Children.Add(AssistantWindow.Pill("Use protocol URL",()=>realtimeUrl.Text=AssistantVoiceSession.DefaultUrl(realtimeProtocol.SelectedItem?.ToString()??"OpenAIRealtime")));Label(realtime,"Realtime model ID",realtimeModel);UI.Named(realtimeModel,"Realtime model");Label(realtime,"Provider voice name",voiceName);voiceName.MaxLength=60;realtime.Children.Add(UI.Text("OpenAI Realtime and Gemini Live use different protocols. Model access and charges depend on your provider. No microphone or screen is opened by saving.",11,UI.Muted));connection.Children.Add(new Expander{Header="Advanced · live voice connection",Content=realtime,Margin=new(0,14,0,8)});
        var maintenance=new WrapPanel();maintenance.Children.Add(AssistantWindow.Pill("Forget key",ForgetKey));maintenance.Children.Add(AssistantWindow.Pill("Remove provider + key",RemoveProvider));connection.Children.Add(new Expander{Header="Remove connection or key",Content=maintenance,Margin=new(0,16,0,10)});
        connectionEditor.Content=connection;chatPage.Children.Add(connectionEditor);

        BuildVoiceWorkspace(voicePage);
        advancedPage.Children.Add(WidgetNavigationSettings());
        var prompt=new StackPanel();Label(prompt,"Assistant instructions",instructions);instructions.AcceptsReturn=true;instructions.TextWrapping=TextWrapping.Wrap;instructions.Height=92;instructions.MaxLength=8000;instructions.Text=ai.Options.Instructions;prompt.Children.Add(AssistantWindow.Pill("Save instructions",()=>{ai.Options.Instructions=instructions.Text;SaveConfiguration("Assistant instructions");}));advancedPage.Children.Add(new Expander{Header="Assistant instructions",Content=prompt,Margin=new(0,14,0,8)});
        status.Margin=new(0,12,0,0);page.Children.Add(status);
        page.Children.Add(UI.Text("You choose when to use the microphone or share your screen. Hermes manages its own tools and permissions.",11,UI.Muted));
        Content=page;
        Reload();RefreshEnabled();SelectSetupSection("Overview");InitializeDraftFeedback();RefreshDraftFeedback();
    }
    internal void ClearSecrets(){key.Clear();hermesPassword?.Clear();}
    private static void Label(Panel page,string title,UIElement control)
    {var label=UI.Text(title,13,UI.Muted);label.Margin=new(0,12,0,5);page.Children.Add(label);if(control is Control c){c.MinHeight=40;if(System.Windows.Automation.AutomationProperties.GetName(c).Length==0)UI.Named(c,title);}page.Children.Add(control);}
    private AssistantProvider? editorProvider;
    private AssistantProvider? Selected=>editorProvider;
    private void RefreshEnabled(){RefreshSetupOverview();enable.Content=ai.Options.Enabled?"AI companion on":"Enable AI companion";if(speechEnable!=null)speechEnable.Content=enable.Content;}
    private void RefreshCapabilities()
    {
        vision.IsChecked=imageInput;vision.Content="Image input · "+(imageInput?"On":"Off");
        bool managed=Selected?.Backend=="Hermes",supported=!managed && protocol.SelectedItem?.ToString()!="Anthropic";
        imageReplies.IsEnabled=supported;imageReplies.IsChecked=supported && imageOutput;
        imageReplies.Content=managed?"Image replies · managed in Hermes":!supported?"Image replies · unavailable for this protocol":"Image replies · "+(imageOutput?"On":"Off");
        bool pending=Selected is {} saved && (imageInput!=saved.Vision || (supported && imageOutput!=saved.ImageOutput));
        capabilityStatus.Text=pending?"Not saved yet · choose Save provider to apply these image preferences.":"Saved image input: "+(Selected?.Vision==true?"On":"Off")+". Your selected model must support images.";
    }
    private void Reload(string? selectedId=null)
    {
        syncing=true;chatProvider.ItemsSource=ai.Options.Providers.ToArray();chatProvider.SelectedItem=ai.Provider;list.ItemsSource=ai.Options.Providers.ToArray();list.SelectedItem=ai.Options.Providers.FirstOrDefault(p=>p.Id==(selectedId??ai.Options.SelectedProvider))??ai.Options.Providers.FirstOrDefault();voiceProvider.ItemsSource=ai.Options.Providers.ToArray();voiceProvider.SelectedItem=ai.VoiceProvider;syncing=false;LoadSelected();
    }
    private void LoadSelected()
    {
        CancelProviderRequest();imageCheckStatus.Text="Image support: not tested for this selected connection.";loadingProvider=true;editorProvider=list.SelectedItem as AssistantProvider;draftPending=false;connectionStatus.Text="Not tested yet.";model.ItemsSource=null;realtimeModel.ItemsSource=null;var selected=Selected;key.Clear();connectionEditor.Visibility=selected==null?Visibility.Collapsed:Visibility.Visible;
        list.IsEnabled=chatProvider.IsEnabled=voiceProvider.IsEnabled=ai.Options.Providers.Count>0;
        hermesHelp.Visibility=selected?.Backend=="Hermes"?Visibility.Visible:Visibility.Collapsed;
        if(selected==null){loadingProvider=false;status.Text="Choose a preset and Add provider to get started.";keyStatus.Text="";RefreshCapabilities();return;}
        name.Text=selected.Name;url.Text=selected.BaseUrl;model.Text=selected.Model;protocol.SelectedItem=selected.Protocol;realtimeProtocol.SelectedItem=selected.RealtimeProtocol;realtimeUrl.Text=selected.RealtimeUrl;realtimeModel.Text=selected.RealtimeModel;voiceName.Text=selected.Voice;key.Clear();imageInput=selected.Vision;imageOutput=selected.ImageOutput;RefreshCapabilities();
        try{keyStatus.Text=ai.Keys.Exists(selected.Id)?"A key is saved for this Windows user. Leave blank to keep it.":"No key saved.";}catch{keyStatus.Text="Windows credential storage is unavailable.";}
        connectionEditor.IsExpanded=selected.BaseUrl.Length==0 || selected.Model.Length==0 && selected.RealtimeModel.Length==0;loadingProvider=false;RefreshDraftFeedback();
    }
    private async void AddProvider()
    {
        if(!await ConfirmProviderChanges())return;
        if(ai.Options.Providers.Count>=12){status.Text="Keep up to 12 providers. Remove one before adding another.";return;}
        var selected=AssistantProviderCatalog.Create(preset.SelectedItem?.ToString()??"OpenAI");
        if(selected.Name is "OpenAI" or "Google Gemini")
        {selected.RealtimeProtocol=selected.Name=="OpenAI"?"OpenAIRealtime":"GeminiLive";selected.RealtimeUrl=AssistantVoiceSession.DefaultUrl(selected.RealtimeProtocol);selected.Voice=selected.Name=="OpenAI"?"marin":"Kore";}
        ai.Options.Providers.Add(selected);if(ai.Options.SelectedProvider.Length==0)ai.Options.SelectedProvider=selected.Id;SaveConfiguration();Reload(selected.Id);
    }
    private AssistantProvider ReadFields(bool allowEmptyModel=false)
    {
        var original=Selected??throw new InvalidOperationException("Add a provider first.");
        var candidate=JsonSerializer.Deserialize<AssistantProvider>(JsonSerializer.Serialize(original))!;
        candidate.Name=name.Text.Trim();candidate.BaseUrl=url.Text.Trim();candidate.Model=model.Text.Trim();candidate.Protocol=protocol.SelectedItem?.ToString()??"ChatCompletions";candidate.Vision=imageInput;candidate.Tools=false;candidate.RealtimeUrl=realtimeUrl.Text.Trim();candidate.RealtimeModel=realtimeModel.Text.Trim();candidate.RealtimeProtocol=realtimeProtocol.SelectedItem?.ToString()??"OpenAIRealtime";candidate.Voice=voiceName.Text.Trim();
        candidate.ImageOutput=imageOutput && candidate.Backend!="Hermes" && candidate.Protocol!="Anthropic";
        if(candidate.Name.Length==0)throw new InvalidOperationException("Enter a provider name.");
        if(candidate.Backend=="Hermes"){candidate.Tools=false;if(candidate.Protocol!="ChatCompletions")throw new InvalidOperationException("Select ChatCompletions for Hermes Agent.");}
        if(candidate.BaseUrl.Length>0)AssistantTransport.ValidateEndpoint(candidate.BaseUrl);
        if(!allowEmptyModel && candidate.Model.Length==0 && candidate.RealtimeModel.Length==0)throw new InvalidOperationException("Enter a chat model or realtime model ID.");
        if(candidate.Model.Length>0 && candidate.BaseUrl.Length==0)throw new InvalidOperationException("Enter an API base URL for chat.");
        if(candidate.RealtimeUrl.Length>0)
        {if(!Uri.TryCreate(candidate.RealtimeUrl,UriKind.Absolute,out var endpoint) || endpoint.Scheme is not ("wss" or "ws") || endpoint.UserInfo.Length>0 || endpoint.Fragment.Length>0)throw new InvalidOperationException("Enter a valid realtime WebSocket URL.");var validation=new UriBuilder(endpoint){Scheme=endpoint.Scheme=="wss"?"https":"http"};AssistantTransport.ValidateEndpoint(validation.Uri.AbsoluteUri);}
        return candidate;
    }
    private async System.Threading.Tasks.Task<bool> SaveProvider()
    {
        if(providerBusy){ConnectionResult("Finish or cancel the current check before saving.");return false;}
        try
        {
            if(model.Text.Trim().Length==0 && realtimeModel.Text.Trim().Length==0 && !await EnsureModel())return false;
            var original=Selected!;var candidate=ReadFields();
            string savedId=ai.SaveProvider(original.Id,candidate,key.Password);key.Clear();Reload(savedId);Feedback("Provider saved · image input "+(candidate.Vision?"On":"Off")+" · "+(candidate.Backend=="Hermes"?"image replies managed in Hermes":candidate.ImageOutput?"image replies On":"image replies Off")+". Test connection to check API access.");return true;
        }
        catch(Exception ex){Feedback("Not saved · "+ex.Message,true);return false;}
    }
    private void Check(){try{ReadFields();status.Text="Configuration looks valid. No network request was sent; model support is checked by your provider when you use it.";}catch(Exception ex){Feedback("Not saved · "+ex.Message,true);}}
    private void ForgetKey(){try{if(Selected is { } selected){ai.Keys.Remove(selected.Id);ai.ForgetConnectionCheck(selected.Id);key.Clear();keyStatus.Text="Key removed from Windows Credential Manager.";}}catch(Exception ex){Feedback("Not saved · "+ex.Message,true);}}
    private async void RemoveProvider()
    {if(!await ConfirmProviderChanges())return;try{if(Selected is not { } selected)return;ai.ForgetConnectionCheck(selected.Id);ai.Keys.Remove(selected.Id);ai.Options.Providers.Remove(selected);if(ai.Options.SelectedProvider==selected.Id)ai.Options.SelectedProvider=ai.Options.Providers.FirstOrDefault()?.Id??"";if(ai.Options.SelectedVoiceProvider==selected.Id)ai.Options.SelectedVoiceProvider="";SaveConfiguration();Reload();}catch(Exception ex){Feedback("Not saved · "+ex.Message,true);}}
    private bool SaveConfiguration(string preference="AI preferences"){try{ai.SaveOptions();Feedback(preference+" saved · companion "+(ai.Options.Enabled?"On":"Off")+".");return true;}catch(Exception){Feedback("Not saved · could not save AI preferences. Check the local settings folder.",true);return false;}}
}
