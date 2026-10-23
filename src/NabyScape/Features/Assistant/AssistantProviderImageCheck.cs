using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NabyScape;
internal sealed partial class AssistantProviderSettings
{
    private readonly TextBlock imageCheckStatus=UI.Text("Image support: not tested. Turning Image input on does not verify model support.",11,UI.Muted);
    private void BuildImageCheck(Panel panel)
    {
        var test=AssistantWindow.Pill("Test image support",()=>_=TestImageSupport());UI.Named(test,"Test image support");panel.Children.Add(test);panel.Children.Add(imageCheckStatus);
        panel.Children.Add(UI.Text("Sends a generated color sample to the saved provider. No personal image is sent. This may use provider credits; Hermes may run an agent task. Image replies are a separate capability.",10,UI.Muted));
    }
    internal static string ImageTestData()
    {
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())dc.DrawRectangle(Brushes.Red,null,new Rect(0,0,64,64));
        var bitmap=new RenderTargetBitmap(64,64,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();png.Save(stream);return "data:image/png;base64,"+Convert.ToBase64String(stream.ToArray());
    }
    private async Task TestImageSupport()
    {
        if(providerBusy)return;
        if(HasProviderDraft){Feedback("Save provider before testing its image support.",true);return;}
        if(Selected is not {} saved || !saved.Vision){Feedback("Enable Image input and Save provider first.",true);return;}
        CancellationTokenSource? request=null;
        try
        {
            var candidate=ReadFields();candidate.Tools=false;candidate.ImageOutput=false;
            request=providerRequest=new();request.CancelAfter(TimeSpan.FromSeconds(45));SetProviderBusy(true);imageCheckStatus.Text="Checking image understanding…";
            using var api=new AssistantTransport();var reply=await api.Complete(candidate,ai.Keys.Read(saved.Id),new[]{new AssistantMessage("user","Name the dominant color of the attached image. Reply with one color word only.",ImageTestData())},Array.Empty<AssistantTool>(),request.Token);
            if(request!=providerRequest)return;
            bool recognized=reply.Text.Trim().Trim('.','!',' ').Equals("red",StringComparison.OrdinalIgnoreCase);
            imageCheckStatus.Text=recognized?"Image support checked · the model identified the red sample. Image replies and other formats were not tested.":"Image support not confirmed · the provider replied but did not identify the sample. Check the model's vision support.";
            Feedback(imageCheckStatus.Text,!recognized);
        }
        catch(Exception ex){if(request==providerRequest){imageCheckStatus.Text=ex is OperationCanceledException?"Image check timed out or was cancelled. No automatic retry was sent.":"Image check failed · "+ex.Message;Feedback(imageCheckStatus.Text,true);}}
        finally{if(request==providerRequest){providerRequest=null;SetProviderBusy(false);}request?.Dispose();}
    }
}
