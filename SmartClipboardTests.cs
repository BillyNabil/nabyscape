using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

public static class SmartClipboardTests
{
    public static void Run(Action<string,bool> report)
    {
        static void Assert(bool value){if(!value)throw new InvalidOperationException("Smart clipboard assertion failed.");}
        void Check(string name,Action test){try{test();report("Smart clipboard: "+name,true);}catch{report("Smart clipboard: "+name,false);}}
        static SmartClip Classify(string value)=>SmartClipboard.Classify(new(){Text=value});
        Check("whole HEX values normalize without altering source",()=>
        {
            var clip=new ClipItem{Text="#aBc"};var result=SmartClipboard.Classify(clip);
            Assert(result.Kind==SmartClipKind.Color && result.Hex=="#AABBCC" && result.Rgb=="rgb(170, 187, 204)");
            Assert(clip.Kind=="text" && clip.Text=="#aBc");
            Assert(Classify("#0012ff").Hex=="#0012FF");
        });
        Check("RGB accepts only complete integer channels within bounds",()=>
        {
            Assert(Classify("rgb(0, 255, 16)").Hex=="#00FF10");
            Assert(Classify("RGB(255,0,0)").Kind==SmartClipKind.Color);
            foreach(var value in new[]{"rgb(256,0,0)","rgb(-1,0,0)","rgb(1.5,2,3)","rgba(1,2,3,1)","rgb(1%,2%,3%)","rgb(1,2,3)\n"," rgb(1,2,3)","rgb(1,2,3) text","#fff\n"," #fff","#ffff","#12345678","#ggg","color: #fff"})Assert(Classify(value).Kind==SmartClipKind.Text);
        });
        Check("links use existing validation and expose an explicit clean variant",()=>
        {
            var original="https://example.com/a?q=keep&utm_source=test#anchor";var clip=new ClipItem{Text=original};
            var result=SmartClipboard.Classify(clip);Assert(result.Kind==SmartClipKind.Link && result.CleanedLink=="https://example.com/a?q=keep#anchor" && clip.Text==original && clip.Kind=="text");
            foreach(var value in new[]{"file:///C:/private.txt","javascript:alert(1)","ftp://example.com","https://user:password@example.com","example.com","read https://example.com","https://example.com/\nsecret"})Assert(Classify(value).Kind==SmartClipKind.Text);
            var signed="https://example.com/?sig=secret&utm_source=keep";Assert(Classify(signed).CleanedLink==signed);
            Assert(SmartClipboard.Classify(new(){Kind="image",Text="#fff"}).Kind==SmartClipKind.Image);
        });
        Check("native-free service preserves privacy and persists source data",()=>
        {
            var root=Path.Combine(Path.GetTempPath(),"NabyScape-smart-clips-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                using(var service=new ProductivityService(new Store(root),_=>{},false))
                {
                    Assert(!service.IngestText("#fff"));service.SetClipboardEnabled(true);Assert(service.IngestText("#abc"));
                    var item=service.Clips.Single();Assert(!service.TryCopyClip(item,"#AABBCC"));
                    service.ClipboardPaused=true;Assert(!service.IngestText("https://private.example"));service.ClipboardPaused=false;
                    var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{1,2,3,255},4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);
                    Assert(service.IngestImage(stream.ToArray()));var image=service.Clips.First();Assert(service.LoadClipImage(image)!=null);
                    var output=Path.Combine(root,"saved.png");Assert(service.SaveClipImage(image,output) && File.Exists(output));
                    Assert(!service.SaveClipImage(image,root) && service.LastError!=null);
                    service.RemoveClip(image.Id);Assert(service.LoadClipImage(image)==null && !service.SaveClipImage(image,Path.Combine(root,"removed.png")));
                }
                using(var service=new ProductivityService(new Store(root),_=>{},false))
                {
                    Assert(!service.ClipboardEnabled && service.Clips.Single().Kind=="text" && service.Clips[0].Text=="#abc");
                    var stale=service.Clips[0];int changes=0;service.Changed+=()=>changes++;service.SetClipboardEnabled(false);
                    Assert(service.Clips.Count==0 && changes>0 && !service.TryCopyClip(stale) && service.LoadClipImage(stale)==null && !service.IngestText("ignored"));
                }
            }
            finally {Directory.Delete(root,true);}
        });
    }
}
