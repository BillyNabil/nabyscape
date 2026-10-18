using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

internal sealed class AssistantProvidersWindow : Window
{
    private readonly AssistantRuntime ai;
    private readonly ComboBox list=new(),preset=new(){ItemsSource=AssistantProviderCatalog.Presets,SelectedIndex=0},voiceProvider=new();
    private readonly ComboBox protocol=new(){ItemsSource=new[]{"ChatCompletions","Responses","Anthropic","Gemini"}};
    private readonly ComboBox realtimeProtocol=new(){ItemsSource=AssistantVoiceSession.Protocols};
    private readonly TextBox name=UI.Input("Provider name",""),url=UI.Input("API base URL",""),model=UI.Input("Chat model",""),realtimeUrl=UI.Input("Realtime WebSocket URL",""),realtimeModel=UI.Input("Realtime model",""),voiceName=UI.Input("Voice name",""),instructions=UI.Input("Assistant instructions","");
    private readonly PasswordBox key=new(){MaxLength=2000};
    private readonly TextBlock status=UI.Text("",11,UI.Muted),keyStatus=UI.Text("",11,UI.Muted);
    private readonly Button enable,vision,tools;
    private bool syncing,imageInput,toolInput;
    internal AssistantProvidersWindow(App app)
    {
        ai=app.Assistant;Title="NabyScape · AI providers";AssistantWindowChrome.Place(this,app.Settings,600,740);
        var page=new StackPanel{Margin=new(24,20,24,24)};
        var header=new DockPanel();var orb=new AssistantOrb{Width=Height=54,ReduceMotion=app.Settings.ReduceMotion};header.Children.Add(orb);var intro=new StackPanel{Margin=new(12,0,0,0)};intro.Children.Add(UI.Heading("Your AI, your provider",22));intro.Children.Add(UI.Text("Connect an API. Choose what it can see and do.",12,UI.Muted));header.Children.Add(intro);page.Children.Add(header);
        enable=AssistantWindow.Pill("",()=>{ai.Options.Enabled=!ai.Options.Enabled;SaveConfiguration();RefreshEnabled();});enable.Margin=new(0,16,0,12);page.Children.Add(enable);
        var add=new Grid();add.ColumnDefinitions.Add(new());add.ColumnDefinitions.Add(new(){Width=GridLength.Auto});add.Children.Add(preset);var addButton=AssistantWindow.Pill("Add provider",AddProvider);addButton.Margin=new(8,0,0,0);Grid.SetColumn(addButton,1);add.Children.Add(addButton);page.Children.Add(add);
        Label(page,"Saved providers",list);list.SelectionChanged+=(_,_)=>{if(!syncing)LoadSelected();};
        Label(page,"Name",name);name.MaxLength=60;Label(page,"Chat API protocol",protocol);Label(page,"API base URL",url);url.MaxLength=2048;Label(page,"Chat model ID",model);model.MaxLength=160;
        Label(page,"API key · Windows Credential Manager",key);UI.Named(key,"API key");page.Children.Add(keyStatus);
        page.Children.Add(UI.Text("The key is never written to settings or exported layouts. Changing a server clears its saved key unless you enter a new one. Local providers can leave this blank.",11,UI.Muted));
        vision=AssistantWindow.Pill("",()=>{imageInput=!imageInput;RefreshCapabilities();});tools=AssistantWindow.Pill("",()=>{toolInput=!toolInput;RefreshCapabilities();});var flags=new WrapPanel{Margin=new(0,10,0,8)};flags.Children.Add(vision);flags.Children.Add(tools);page.Children.Add(flags);page.Children.Add(UI.Text("Enable these only if your chosen model supports image input and function tools. A compatible chat API does not imply realtime voice support.",11,UI.Muted));
        var realtime=new StackPanel();Label(realtime,"Voice protocol",realtimeProtocol);Label(realtime,"Realtime WebSocket URL",realtimeUrl);realtimeUrl.MaxLength=2048;realtime.Children.Add(AssistantWindow.Pill("Use protocol URL",()=>realtimeUrl.Text=AssistantVoiceSession.DefaultUrl(realtimeProtocol.SelectedItem?.ToString()??"OpenAIRealtime")));Label(realtime,"Realtime model ID",realtimeModel);realtimeModel.MaxLength=160;Label(realtime,"Provider voice name",voiceName);voiceName.MaxLength=60;realtime.Children.Add(UI.Text("OpenAI Realtime and Gemini Live use different protocols. Model access and charges depend on your provider. No microphone or screen is opened by saving.",11,UI.Muted));page.Children.Add(new Expander{Header="Live voice configuration",Content=realtime,Margin=new(0,14,0,8)});
        var row=new WrapPanel{Margin=new(0,12,0,8)};foreach(var button in new[]{AssistantWindow.Pill("Save provider",SaveProvider,true),AssistantWindow.Pill("Check configuration",Check),AssistantWindow.Pill("Forget key",ForgetKey),AssistantWindow.Pill("Remove provider + key",RemoveProvider)})row.Children.Add(button);page.Children.Add(row);
        Label(page,"Provider used for voice",voiceProvider);voiceProvider.SelectionChanged+=(_,_)=>{if(!syncing && voiceProvider.SelectedItem is AssistantProvider selected){ai.Options.SelectedVoiceProvider=selected.Id;SaveConfiguration();}};
        Label(page,"Assistant instructions",instructions);instructions.AcceptsReturn=true;instructions.TextWrapping=TextWrapping.Wrap;instructions.Height=92;instructions.MaxLength=8000;instructions.Text=ai.Options.Instructions;page.Children.Add(AssistantWindow.Pill("Save instructions",()=>{ai.Options.Instructions=instructions.Text;SaveConfiguration();}));
        status.Margin=new(0,12,0,0);page.Children.Add(status);
        page.Children.Add(UI.Text("Chat lives in memory until Clear or exit. Microphone, screen sharing and computer control are separate session choices. AI-generated text never executes as a shell command.",11,UI.Muted));
        Content=AssistantWindowChrome.Frame(this,"NabyScape · AI providers",new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        Reload();RefreshEnabled();
    }
    private static void Label(Panel page,string title,UIElement control)
    {var label=UI.Text(title,11,UI.Muted);label.Margin=new(0,12,0,5);page.Children.Add(label);if(control is Control c)c.MinHeight=32;page.Children.Add(control);}
    private AssistantProvider? Selected=>list.SelectedItem as AssistantProvider;
    private void RefreshEnabled()=>enable.Content=ai.Options.Enabled?"AI companion on":"Enable AI companion";
    private void RefreshCapabilities(){vision.Content=imageInput?"Image input enabled":"Image input off";tools.Content=toolInput?"Function tools enabled":"Function tools off";}
    private void Reload(string? selectedId=null)
    {
        syncing=true;list.ItemsSource=ai.Options.Providers.ToArray();list.SelectedItem=ai.Options.Providers.FirstOrDefault(p=>p.Id==(selectedId??ai.Options.SelectedProvider))??ai.Options.Providers.FirstOrDefault();voiceProvider.ItemsSource=ai.Options.Providers.ToArray();voiceProvider.SelectedItem=ai.VoiceProvider;syncing=false;LoadSelected();
    }
    private void LoadSelected()
    {
        var selected=Selected;if(selected==null){status.Text="Choose a preset and Add provider to get started.";return;}
        name.Text=selected.Name;url.Text=selected.BaseUrl;model.Text=selected.Model;protocol.SelectedItem=selected.Protocol;realtimeProtocol.SelectedItem=selected.RealtimeProtocol;realtimeUrl.Text=selected.RealtimeUrl;realtimeModel.Text=selected.RealtimeModel;voiceName.Text=selected.Voice;key.Clear();imageInput=selected.Vision;toolInput=selected.Tools;RefreshCapabilities();
        try{keyStatus.Text=ai.Keys.Exists(selected.Id)?"A key is saved for this Windows user. Leave blank to keep it.":"No key saved.";}catch{keyStatus.Text="Windows credential storage is unavailable.";}
    }
    private void AddProvider()
    {
        if(ai.Options.Providers.Count>=12){status.Text="Keep up to 12 providers. Remove one before adding another.";return;}
        var selected=AssistantProviderCatalog.Create(preset.SelectedItem?.ToString()??"OpenAI");
        if(selected.Name is "OpenAI" or "Google Gemini")
        {selected.RealtimeProtocol=selected.Name=="OpenAI"?"OpenAIRealtime":"GeminiLive";selected.RealtimeUrl=AssistantVoiceSession.DefaultUrl(selected.RealtimeProtocol);selected.Voice=selected.Name=="OpenAI"?"marin":"Kore";}
        ai.Options.Providers.Add(selected);if(ai.Options.SelectedProvider.Length==0)ai.Options.SelectedProvider=selected.Id;SaveConfiguration();Reload(selected.Id);
    }
    private AssistantProvider ReadFields()
    {
        var original=Selected??throw new InvalidOperationException("Add a provider first.");
        var candidate=JsonSerializer.Deserialize<AssistantProvider>(JsonSerializer.Serialize(original))!;
        candidate.Name=name.Text.Trim();candidate.BaseUrl=url.Text.Trim();candidate.Model=model.Text.Trim();candidate.Protocol=protocol.SelectedItem?.ToString()??"ChatCompletions";candidate.Vision=imageInput;candidate.Tools=toolInput;candidate.RealtimeUrl=realtimeUrl.Text.Trim();candidate.RealtimeModel=realtimeModel.Text.Trim();candidate.RealtimeProtocol=realtimeProtocol.SelectedItem?.ToString()??"OpenAIRealtime";candidate.Voice=voiceName.Text.Trim();
        if(candidate.Name.Length==0)throw new InvalidOperationException("Enter a provider name.");
        if(candidate.BaseUrl.Length>0)AssistantTransport.ValidateEndpoint(candidate.BaseUrl);
        if(candidate.Model.Length==0 && candidate.RealtimeModel.Length==0)throw new InvalidOperationException("Enter a chat model or realtime model ID.");
        if(candidate.Model.Length>0 && candidate.BaseUrl.Length==0)throw new InvalidOperationException("Enter an API base URL for chat.");
        if(candidate.RealtimeUrl.Length>0)
        {if(!Uri.TryCreate(candidate.RealtimeUrl,UriKind.Absolute,out var endpoint) || endpoint.Scheme is not ("wss" or "ws") || endpoint.UserInfo.Length>0 || endpoint.Fragment.Length>0)throw new InvalidOperationException("Enter a valid realtime WebSocket URL.");var validation=new UriBuilder(endpoint){Scheme=endpoint.Scheme=="wss"?"https":"http"};AssistantTransport.ValidateEndpoint(validation.Uri.AbsoluteUri);}
        return candidate;
    }
    private void SaveProvider()
    {
        try
        {
            var original=Selected!;var candidate=ReadFields();
            string savedId=ai.SaveProvider(original.Id,candidate,key.Password);key.Clear();Reload(savedId);status.Text="Provider saved. Use chat for a live request; configuration checks send nothing.";
        }
        catch(Exception ex){status.Text=ex.Message;}
    }
    private void Check(){try{ReadFields();status.Text="Configuration looks valid. No network request was sent; model support is checked by your provider when you use it.";}catch(Exception ex){status.Text=ex.Message;}}
    private void ForgetKey(){try{if(Selected is { } selected){ai.Keys.Remove(selected.Id);key.Clear();keyStatus.Text="Key removed from Windows Credential Manager.";}}catch(Exception ex){status.Text=ex.Message;}}
    private void RemoveProvider()
    {try{if(Selected is not { } selected)return;ai.Keys.Remove(selected.Id);ai.Options.Providers.Remove(selected);if(ai.Options.SelectedProvider==selected.Id)ai.Options.SelectedProvider=ai.Options.Providers.FirstOrDefault()?.Id??"";if(ai.Options.SelectedVoiceProvider==selected.Id)ai.Options.SelectedVoiceProvider="";SaveConfiguration();Reload();}catch(Exception ex){status.Text=ex.Message;}}
    private bool SaveConfiguration(){try{ai.SaveOptions();status.Text="Saved on this device.";return true;}catch(Exception){status.Text="Could not save provider preferences. Check the local settings folder.";return false;}}
}
