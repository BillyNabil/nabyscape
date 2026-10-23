using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
internal sealed partial class AssistantProviderSettings
{
    private CancellationTokenSource? providerRequest;
    private bool providerBusy,loadingProvider,draftPending;
    private Button? cancelConnection;
    private readonly TextBlock connectionStatus=UI.Text("Not tested yet.",11,UI.Muted);
    private void InitializeDraftFeedback()
    {
        foreach(var input in new[]{name,url,realtimeUrl,voiceName})input.TextChanged+=(_,_)=>RefreshDraftFeedback();
        model.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,_)=>RefreshDraftFeedback()));
        realtimeModel.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,_)=>RefreshDraftFeedback()));
        realtimeProtocol.SelectionChanged+=(_,_)=>RefreshDraftFeedback();protocol.SelectionChanged+=(_,_)=>RefreshDraftFeedback();key.PasswordChanged+=(_,_)=>RefreshDraftFeedback();
    }
    private void RefreshDraftFeedback()
    {
        if(loadingProvider || providerBusy || Selected is not {} saved)return;
        var draft=ReadDraftIdentity();bool changed=key.Password.Length>0 || draft!=ReadSavedIdentity(saved);
        if(changed)imageCheckStatus.Text="Image support: connection changed. Save and test again.";
        if(changed){draftPending=true;connectionStatus.Text="Not saved yet · choose Save provider to apply your connection changes. Test connection checks this draft only.";connectionStatus.Foreground=UI.Muted;Feedback(connectionStatus.Text);return;}
        if(draftPending){draftPending=false;Feedback("No unsaved connection changes · image input "+(saved.Vision?"On":"Off")+".");}
        var check=ai.ChatSetupFor(saved);connectionStatus.Text=check.Title+"\n"+check.Detail;connectionStatus.Foreground=check.Verified?UI.Brush("#A9CDBD"):UI.Muted;
    }
    private (string,string,string,string,bool,bool,string,string,string,string) ReadDraftIdentity()
        =>(name.Text.Trim(),url.Text.Trim(),model.Text.Trim(),protocol.SelectedItem?.ToString()??"",imageInput,imageOutput,realtimeUrl.Text.Trim(),realtimeModel.Text.Trim(),realtimeProtocol.SelectedItem?.ToString()??"",voiceName.Text.Trim());
    private static (string,string,string,string,bool,bool,string,string,string,string) ReadSavedIdentity(AssistantProvider p)
        =>(p.Name,p.BaseUrl,p.Model,p.Protocol,p.Vision,p.ImageOutput,p.RealtimeUrl,p.RealtimeModel,p.RealtimeProtocol,p.Voice);
    private void InitializeModelPicker(Panel page)
    {
        VirtualizingPanel.SetIsVirtualizing(model,true);VirtualizingPanel.SetVirtualizationMode(model,VirtualizationMode.Recycling);model.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        model.ToolTip="Paste an exact model ID, or leave empty and open the list to load models from your API. Listing is metadata only; Test connection sends one short request.";
        model.DropDownOpened+=async(_,_)=>{if(model.Items.Count==0 && !providerBusy){bool open=await LoadModels();if(open)model.IsDropDownOpen=true;}};
        var actions=new WrapPanel();actions.Children.Add(AssistantWindow.Pill("Load model list",()=>_=LoadModels()));
        cancelConnection=AssistantWindow.Pill("Cancel check",CancelProviderRequest);cancelConnection.Visibility=Visibility.Collapsed;actions.Children.Add(cancelConnection);page.Children.Add(actions);

        page.Children.Add(UI.Text("Checking the connection sends one short message and may use provider credits. Your chat history, microphone and screen are not sent.",12,UI.Muted));
        Unloaded+=(_,_)=>CancelProviderRequest();
    }
    private void SetProviderBusy(bool busy)
    {
        providerBusy=busy;
        foreach(var control in new Control[]{name,url,model,key,protocol,realtimeModel,realtimeUrl,realtimeProtocol,voiceName,list,preset,chatProvider,voiceProvider})control.IsEnabled=!busy;
        list.IsEnabled=chatProvider.IsEnabled=voiceProvider.IsEnabled=!busy && ai.Options.Providers.Count>0;
        if(cancelConnection!=null)cancelConnection.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        RefreshSetupOverview();
    }
    private void CancelProviderRequest()
    {
        var request=providerRequest;providerRequest=null;request?.Cancel();SetProviderBusy(false);
        if(request!=null)ConnectionResult("Check cancelled. No retry was sent.");
    }
    private string? DraftKey(AssistantProvider candidate)
    {
        if(key.Password.Length>0)return key.Password;
        var saved=Selected;
        if(saved==null || !Uri.TryCreate(saved.BaseUrl,UriKind.Absolute,out var old) || !Uri.TryCreate(candidate.BaseUrl,UriKind.Absolute,out var next) || old.GetLeftPart(UriPartial.Authority)!=next.GetLeftPart(UriPartial.Authority))return null;
        if(candidate.Backend=="Hermes" && old.AbsolutePath.TrimEnd('/')!=next.AbsolutePath.TrimEnd('/'))return null;
        return ai.Keys.Read(saved.Id);
    }
    private void ConnectionResult(string text,bool error=false,bool success=false)
    {connectionStatus.Text=text;connectionStatus.Foreground=error?UI.Brush("#EAB3AD"):success?UI.Brush("#A9CDBD"):UI.Muted;Feedback(text,error);status.Foreground=connectionStatus.Foreground;}
    private async Task<bool> LoadModels()
    {
        if(providerBusy)return false;
        CancellationTokenSource? request=null;
        try
        {
            var candidate=ReadFields(true);if(candidate.BaseUrl.Length==0)throw new InvalidOperationException("Enter the API base URL first.");
            request=providerRequest=new();request.CancelAfter(TimeSpan.FromSeconds(25));SetProviderBusy(true);ConnectionResult("Loading model IDs from the provider…");
            using var api=new AssistantTransport();var models=await api.Models(candidate,DraftKey(candidate),request.Token);
            if(request!=providerRequest || Selected?.Id!=candidate.Id || url.Text.Trim()!=candidate.BaseUrl)return false;
            string typed=model.Text,voiceTyped=realtimeModel.Text;model.ItemsSource=models;model.Text=typed;realtimeModel.ItemsSource=models;realtimeModel.Text=voiceTyped;ConnectionResult($"{models.Length} models loaded. Paste an ID or choose one; capabilities depend on the model.");return true;
        }
        catch(OperationCanceledException){if(request==providerRequest)ConnectionResult("Model loading cancelled. You can still paste a model ID.",true);return false;}
        catch(Exception ex){if(request!=providerRequest)return false;ConnectionResult("Could not load models. "+ex.Message+" You can paste the model ID manually.",true);return false;}
        finally{if(request==providerRequest){providerRequest=null;SetProviderBusy(false);}request?.Dispose();}
    }
    private async Task<bool> EnsureModel()
    {
        if(model.Text.Trim().Length>0)return true;if(!await LoadModels())return false;
        model.IsDropDownOpen=true;ConnectionResult("Models loaded. Choose a model from the list, then Save provider or Test connection.");return false;
    }
    private async Task TestConnection()
    {
        if(providerBusy)return;
        CancellationTokenSource? request=null;
        AssistantProvider? candidate=null;bool savedConnection=false;
        try
        {
            if(!await EnsureModel())return;candidate=ReadFields();
            savedConnection=Selected!=null && key.Password.Length==0 && AssistantRuntime.ConnectionIdentity(Selected)==AssistantRuntime.ConnectionIdentity(candidate);
            if(candidate.Model.Length==0)throw new InvalidOperationException("Choose a chat model before testing. Realtime voice has separate settings.");
            request=providerRequest=new();request.CancelAfter(TimeSpan.FromSeconds(45));SetProviderBusy(true);ConnectionResult("Checking API access and the selected chat model…");
            candidate.Tools=false;candidate.Vision=false;candidate.ImageOutput=false;
            using var api=new AssistantTransport();var reply=await api.Complete(candidate,DraftKey(candidate),new[]{new AssistantMessage("user","Reply with OK only. This is a connection check.")},Array.Empty<AssistantTool>(),request.Token);
            if(request!=providerRequest || Selected?.Id!=candidate.Id || url.Text.Trim()!=candidate.BaseUrl)return;
            if(string.IsNullOrWhiteSpace(reply.Text))throw new InvalidOperationException("The API responded but returned no text. Select a text/chat model.");
            if(savedConnection)ai.RecordConnectionCheck(candidate,true,candidate.Name+" · "+candidate.Model+" returned a chat response.");
            ConnectionResult("Connected · "+candidate.Model+" returned a chat response. "+(savedConnection?"Saved connection checked.":"These are draft settings. Save the provider, then check its saved connection.")+" Voice and tools were not tested.",success:true);app.Notify("Naby AI connected",candidate.Name+" · Chat model responded successfully.");
        }
        catch(OperationCanceledException){if(request==providerRequest){if(savedConnection && candidate!=null)ai.RecordConnectionCheck(candidate,false,"Connection check timed out. Confirm the server is running and check its address and model.");ConnectionResult("Connection check timed out. Check the endpoint, model and server availability.",true);}}
        catch(Exception ex){if(request!=providerRequest)return;if(savedConnection && candidate!=null)ai.RecordConnectionCheck(candidate,false,ex.Message);ConnectionResult("Connection failed · "+ex.Message,true);app.Notify("Naby AI connection failed",ex.Message);}
        finally{if(request==providerRequest){providerRequest=null;SetProviderBusy(false);}request?.Dispose();}
    }
}
