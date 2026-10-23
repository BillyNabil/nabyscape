using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;
internal sealed partial class AssistantProviderSettings
{
    private Expander? hermesSetup;
    private PasswordBox? hermesPassword;
    internal void OpenHermesSetup()
    {
        if(hermesSetup==null)return;SelectSetupSection("Hermes");connectionEditor.IsExpanded=false;hermesSetup.IsExpanded=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>hermesSetup.BringIntoView(new Rect(0,0,Math.Max(1,hermesSetup.ActualWidth),40))));
    }
    private FrameworkElement HermesSetup()
    {
        var saved=ai.Options.Providers.FirstOrDefault(p=>p.Backend=="Hermes");
        var p=new StackPanel();p.Children.Add(UI.Heading("Your Hermes, inside Naby",16));p.Children.Add(UI.Text("Naby handles chat, voice and the window/display you choose to share. Your Hermes agent keeps its own skills, memory, tools and permissions.",11,UI.Muted));
        Label(p,"1 · Enable API server in Hermes",UI.Text("Open Hermes → API server, enable it, and set an API_SERVER_KEY for your profile. Start or restart the gateway with hermes gateway. Your model/provider must already be configured in Hermes. Webhooks are for external events.",11,UI.Muted));
        p.Children.Add(AssistantWindow.Pill("Copy gateway setup template",()=>Clipboard.SetText("API_SERVER_ENABLED=true\nAPI_SERVER_KEY=<your-own-long-random-key>\n\n# Start or restart after saving the profile .env:\nhermes gateway")));
        p.Children.Add(AssistantWindow.Pill("Open Hermes API setup guide",()=>Process.Start(new ProcessStartInfo("https://hermes-agent.nousresearch.com/docs/user-guide/features/api-server/"){UseShellExecute=true})));
        var address=UI.Input("Hermes API address",saved?.BaseUrl??"http://127.0.0.1:8642/v1/");address.MaxLength=2048;Label(p,"2 · Paste your Hermes API address",address);
        p.Children.Add(UI.Text("Same PC: http://127.0.0.1:8642/v1/. Remote server: use its public HTTPS address, or forward its port to localhost. Naby does not install or start Hermes for you.",10,UI.Muted));
        var profile=UI.Input("Optional Hermes profile name","");profile.MaxLength=64;Label(p,"Profile · optional",profile);p.Children.Add(UI.Text("Leave empty for the default profile or a complete profile URL. With shared multi-profile routing enabled in Hermes, a name adds /p/name/v1/. Use that profile’s own key. Separate profile ports can also be pasted directly.",10,UI.Muted));
        var secret=new PasswordBox{MaxLength=2000};hermesPassword=secret;Label(p,"API_SERVER_KEY · from Hermes, not the underlying LLM provider",secret);
        p.Children.Add(UI.Text("An existing saved key is kept only for the exact same Hermes address/profile. Keys stay in Windows Credential Manager; they are not put in chat or copied by the setup template.",10,UI.Muted));
        var agent=new ComboBox{IsEditable=true,IsTextSearchEnabled=true,Text=saved?.Model??"hermes-agent"};Label(p,"3 · Check the server and choose its advertised agent",agent);
        var images=new CheckBox{Content=UI.Text("Allow the window/display images I choose to share",11),IsChecked=saved?.Vision==true,Margin=new(0,12,0,8)};p.Children.Add(images);
        p.Children.Add(UI.Text("Image understanding depends on the model configured in Hermes. The agent/model list is not a list of all underlying LLMs; change the agent’s LLM in Hermes.",10,UI.Muted));
        var result=UI.Text("Not connected yet. Check server reads metadata only; it does not start an agent task.",11,UI.Muted);result.Margin=new(0,8,0,8);p.Children.Add(result);
        var buttons=new WrapPanel();var check=AssistantWindow.Pill("Check server",()=>{});var connect=AssistantWindow.Pill("Connect & use Hermes",()=>{},true);buttons.Children.Add(check);buttons.Children.Add(connect);p.Children.Add(buttons);
        CancellationTokenSource? pending=null;
        string BaseUrl()
        {
            string value=address.Text.Trim().TrimEnd('/');string name=profile.Text.Trim();
            if(name.Length>0)
            {
                if(name.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-' && c!='_'))throw new InvalidOperationException("Use letters, numbers, hyphens or underscores for the profile name.");
                if(new Uri(value,UriKind.Absolute).AbsolutePath.Contains("/p/",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("The address already includes a profile. Leave Profile empty.");
                if(value.EndsWith("/v1",StringComparison.OrdinalIgnoreCase))value=value[..^3];value+="/p/"+Uri.EscapeDataString(name)+"/v1";
            }
            else if(!value.EndsWith("/v1",StringComparison.OrdinalIgnoreCase))value+="/v1";
            return AssistantTransport.ValidateEndpoint(value).AbsoluteUri;
        }
        AssistantProvider? Existing(string endpoint)=>ai.Options.Providers.FirstOrDefault(v=>v.Backend=="Hermes" && string.Equals(v.BaseUrl.TrimEnd('/'),endpoint.TrimEnd('/'),StringComparison.Ordinal));
        async Task<bool> Check()
        {
            if(pending!=null)return false;pending=new();pending.CancelAfter(TimeSpan.FromSeconds(25));check.IsEnabled=connect.IsEnabled=address.IsEnabled=profile.IsEnabled=secret.IsEnabled=agent.IsEnabled=false;
            try
            {
                string endpoint=BaseUrl();var old=Existing(endpoint);string? token=secret.Password.Length>0?secret.Password:old==null?null:ai.Keys.Read(old.Id);
                if(string.IsNullOrWhiteSpace(token))throw new InvalidOperationException("Enter the API_SERVER_KEY from this Hermes profile.");
                result.Text="Checking Hermes address, key and advertised agents…";var candidate=AssistantProviderCatalog.Create("Hermes Agent");candidate.BaseUrl=endpoint;
                using var api=new AssistantTransport();string[] models=await api.Models(candidate,token,pending.Token);if(models.Length==0)throw new InvalidOperationException("Server returned no agent/model IDs. Check its API server setup.");
                string typed=agent.Text.Trim();agent.ItemsSource=models;agent.Text=models.Contains(typed)?typed:models.Length==1?models[0]:"";result.Text=$"Server available · {models.Length} agent(s). Choose one, then Connect & use Hermes. No chat or tool task was started.";return true;
            }
            catch(OperationCanceledException){result.Text="Connection check stopped or timed out. Check the gateway address and whether Hermes is running.";return false;}
            catch(Exception ex){result.Text="Could not connect: "+ex.Message;return false;}
            finally{pending?.Dispose();pending=null;check.IsEnabled=connect.IsEnabled=address.IsEnabled=profile.IsEnabled=secret.IsEnabled=agent.IsEnabled=true;}
        }
        check.Click+=async(_,_)=>await Check();connect.Click+=async(_,_)=>
        {
            if(!await ConfirmProviderChanges() || !await Check())return;
            try
            {
                string endpoint=BaseUrl();string modelId=agent.Text.Trim();if(modelId.Length==0 || modelId.Length>160)throw new InvalidOperationException("Choose or paste an advertised agent ID first.");
                var old=Existing(endpoint);var candidate=AssistantProviderCatalog.Create("Hermes Agent");candidate.BaseUrl=endpoint;candidate.Model=modelId;candidate.Name=profile.Text.Trim().Length>0?"Hermes · "+profile.Text.Trim():"Hermes Agent";candidate.Vision=images.IsChecked==true;
                if(old!=null){candidate.Id=old.Id;ai.SaveProvider(old.Id,candidate,secret.Password);}
                else
                {
                    if(ai.Options.Providers.Count>=12)throw new InvalidOperationException("Remove an unused provider before adding Hermes.");
                    string previous=ai.Options.SelectedProvider;ai.Keys.Write(candidate.Id,secret.Password);
                    try{ai.Options.Providers.Add(candidate);ai.Options.SelectedProvider=candidate.Id;app.Store.Save("assistant",ai.Options);}
                    catch{ai.Options.Providers.Remove(candidate);ai.Options.SelectedProvider=previous;ai.Keys.Remove(candidate.Id);throw;}
                    ai.SelectProvider(candidate.Id);
                }
                ai.Options.Enabled=true;app.Store.Save("assistant",ai.Options);secret.Clear();Reload(candidate.Id);RefreshEnabled();Feedback("Hermes connection saved · selected for chat.");result.Text="Hermes selected for chat. Return to Naby to start. For spoken replies, choose a local speech pack in Voice setup.";app.Notify("Hermes connected","Your agent is selected for Naby chat.");
            }
            catch(Exception ex){result.Text="Could not save Hermes connection: "+ex.Message;Feedback(result.Text,true);}
        };
        p.Unloaded+=(_,_)=>{pending?.Cancel();secret.Clear();};hermesSetup=new Expander{Header="Connect Hermes · guided setup",Content=p,Margin=new(0,12,0,10)};return hermesSetup;
    }
}
