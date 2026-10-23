using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
namespace NabyScape;
internal static class UxCompletionTests
{
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool condition){if(!condition)throw new Exception("UX completion invariant failed.");}
        test("UX attachment processing summaries match provider and prepared page counts",()=>
        {
            var pdf=new AssistantAttachment("long.pdf","application/pdf","data:application/pdf;base64,AQID",Frames:new[]{"frame","frame"});var p=new AssistantProvider{Vision=true,Backend="Hermes"};
            Assert(AssistantAttachmentSummary.Describe(pdf,p).Contains("first 2 pages") && !AssistantAttachmentSummary.Describe(pdf,p).Contains("Off"));p.Protocol="Gemini";p.Backend="";Assert(AssistantAttachmentSummary.Describe(pdf,p).Contains("original document"));
            var video=pdf with{Mime="video/mp4",Frames=new[]{"frame"}};p.Protocol="ChatCompletions";Assert(AssistantAttachmentSummary.Describe(video,p).Contains("1 sampled frames") && AssistantAttachmentSummary.Describe(video,p).Contains("audio is not included"));
            p.Vision=false;Assert(AssistantAttachmentSummary.Describe(video,p).Contains("Image input is Off"));
        });
        test("UX synthetic image capability sample is an actual red PNG",()=>
        {
            string url=AssistantProviderSettings.ImageTestData();using var stream=new MemoryStream(Convert.FromBase64String(url[(url.IndexOf(',')+1)..]));var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);var image=new FormatConvertedBitmap(decoder.Frames[0],System.Windows.Media.PixelFormats.Bgra32,null,0);byte[] pixel=new byte[4];image.CopyPixels(new System.Windows.Int32Rect(32,32,1,1),pixel,4,0);Assert(pixel[0]==0 && pixel[1]==0 && pixel[2]==255 && pixel[3]==255);
        });
        test("UX storage measurement includes actual model and sidecar bytes",()=>
        {
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"NabyScape-ux-"+Guid.NewGuid().ToString("N")));
            try
            {
                using var speech=new AssistantSpeechInstall(new Store(root));var voice=SpeechCatalog.Tts.First(t=>t.Id!="windows" && t.Package==null);string path=speech.TtsPath(voice);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,new byte[123]);File.WriteAllBytes(path+".json",new byte[11]);File.WriteAllBytes(path+".partial",new byte[7]);Assert(speech.ManagedModelBytes(voice.Id,true)==141 && AssistantSpeechInstall.StorageSize(141)=="141 B");
            }
            finally{if(root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))Directory.Delete(root,true);}
        });
    }
}
