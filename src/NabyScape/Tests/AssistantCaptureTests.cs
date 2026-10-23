using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace NabyScape;

// Synthetic pixels only. Never opens the picker, captures the desktop, or uses a provider.
internal static class AssistantCaptureTests
{
    internal static readonly List<string> Evidence=new();
    private static readonly AssistantScreenTarget Target=new("fixture","Synthetic",new Rectangle(0,0,3840,2160));
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static SoftwareBitmap Fixture(int width,int height,byte alpha=255)
    {
        byte[] pixels=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int at=(y*width+x)*4;bool right=x>=width/2,bottom=y>=height/2;
            pixels[at]=(byte)(bottom?192:32);pixels[at+1]=(byte)(right?160:48);pixels[at+2]=(byte)(right?64:224);pixels[at+3]=255;
            if(alpha!=255){pixels[at]=(byte)(pixels[at]*alpha/255);pixels[at+1]=(byte)(pixels[at+1]*alpha/255);pixels[at+2]=(byte)(pixels[at+2]*alpha/255);pixels[at+3]=alpha;}
        }
        using var buffer=new DataWriter();buffer.WriteBytes(pixels);
        var bitmap=new SoftwareBitmap(BitmapPixelFormat.Bgra8,width,height,BitmapAlphaMode.Premultiplied);bitmap.CopyFromBuffer(buffer.DetachBuffer());return bitmap;
    }
    private static SoftwareBitmap AlphaPattern(bool stripes,bool opaque)
    {
        const int width=2560;int height=stripes?256:8;var pixels=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int at=(y*width+x)*4;bool red=stripes?x%2==1:x>=1281;
            pixels[at+2]=(byte)(red?224:0);pixels[at+3]=(byte)(opaque || red?255:0);
        }
        using var writer=new DataWriter();writer.WriteBytes(pixels);
        var bitmap=new SoftwareBitmap(BitmapPixelFormat.Bgra8,width,height,BitmapAlphaMode.Premultiplied);bitmap.CopyFromBuffer(writer.DetachBuffer());return bitmap;
    }
    private static void CompareAlphaPattern(bool stripes,bool opaque)
    {
        using var fixture=AlphaPattern(stripes,opaque);var previous=Legacy(fixture);var current=AssistantShareCapture.Encode(Target,fixture,default).GetAwaiter().GetResult();
        Assert(previous.Width==current.Width && previous.Height==current.Height,"Alpha pattern dimensions changed");
        using var old=Decode(previous);using var image=Decode(current);
        // Check the whole output, including transparent areas and resampled boundaries.
        int maximum=0;
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
        {var a=old.GetPixel(x,y);var b=image.GetPixel(x,y);maximum=Math.Max(maximum,Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B))));}
        Assert(maximum<=(opaque?12:0),$"Variable alpha {(stripes?"stripes":"boundary")} changed: maximum channel difference={maximum}");
        Evidence.Add($"{(opaque?"Opaque":"Mixed alpha")} {(stripes?"stripes":"boundary")} 2560x{fixture.PixelHeight} -> {image.Width}x{image.Height}: maximum decoded RGB channel difference={maximum}; center old={old.GetPixel(640,image.Height/2)}, new={image.GetPixel(640,image.Height/2)}.");
    }
    // The previous production pipeline, kept only as an independent decoded-output/allocation oracle.
    private static AssistantScreenFrame Legacy(SoftwareBitmap source)
    {
        uint length=checked((uint)(source.PixelWidth*source.PixelHeight*4));var buffer=new Windows.Storage.Streams.Buffer(length){Length=length};source.CopyToBuffer(buffer);byte[] pixels=new byte[length];using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(pixels);
        using var bitmap=new Bitmap(source.PixelWidth,source.PixelHeight,PixelFormat.Format32bppArgb);var locked=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try{for(int row=0;row<bitmap.Height;row++)Marshal.Copy(pixels,row*bitmap.Width*4,locked.Scan0+row*locked.Stride,bitmap.Width*4);}finally{bitmap.UnlockBits(locked);}
        double scale=Math.Min(1,1280d/Math.Max(bitmap.Width,bitmap.Height));using var reduced=new Bitmap(bitmap,Math.Max(1,(int)(bitmap.Width*scale)),Math.Max(1,(int)(bitmap.Height*scale)));using var output=new MemoryStream();reduced.Save(output,ImageFormat.Jpeg);
        return new(Target,reduced.Width,reduced.Height,"data:image/jpeg;base64,"+Convert.ToBase64String(output.GetBuffer(),0,(int)output.Length));
    }
    private static Bitmap Decode(AssistantScreenFrame frame)
    {using var stream=new MemoryStream(Convert.FromBase64String(frame.DataUrl[(frame.DataUrl.IndexOf(',')+1)..]));using var image=Image.FromStream(stream);return new Bitmap(image);}
    private static void Compare(SoftwareBitmap fixture)
    {
        var previous=Legacy(fixture);var current=AssistantShareCapture.Encode(Target,fixture,default).GetAwaiter().GetResult();
        Assert(previous.Width==current.Width && previous.Height==current.Height,"Encoded size changed");
        using var old=Decode(previous);using var image=Decode(current);
        Assert(image.Width==current.Width && image.Height==current.Height && Math.Max(image.Width,image.Height)<=1280,"JPEG dimensions exceed bound");
        foreach(var p in new[]{(.25,.25),(.75,.25),(.25,.75),(.75,.75)})
        {
            int x=Math.Min(image.Width-1,(int)(image.Width*p.Item1)),y=Math.Min(image.Height-1,(int)(image.Height*p.Item2));
            var a=old.GetPixel(x,y);var b=image.GetPixel(x,y);
            Assert(Math.Abs(a.R-b.R)<=12 && Math.Abs(a.G-b.G)<=12 && Math.Abs(a.B-b.B)<=12,$"JPEG color changed at {x},{y}: old={a} new={b}");
        }
    }
    private static string Outcome(Action action)
    {try{action();return "accepted";}catch(TargetInvocationException ex)when(ex.InnerException!=null){return ex.InnerException.Message;}catch(InvalidOperationException ex){return ex.Message;}}
    private static void ValidateBase64()
    {
        var image=typeof(AssistantTransport).GetMethod("Image",BindingFlags.NonPublic|BindingFlags.Static)!;
        var inline=typeof(AssistantTransport).GetMethod("Inline",BindingFlags.NonPublic|BindingFlags.Static)!;
        foreach(string data in new[]{""," ","AQID","AQ==","AQ=", "A===", "AA=A", "====", "AQ==A", "AQID\r\n", "A Q\t=\r=\n", "AQID\u00a0", "AQID\v", "AQID_", "AR==", "AAA=", "AAAA=", "AA==\n",new string('A',3*1024*1024)})
        {
            string url="data:image/jpeg;base64,"+data;
            bool decoded;try{Convert.FromBase64String(data);decoded=true;}catch(FormatException){decoded=false;}
            string expectedImage=url.Length>3*1024*1024?"The screenshot is invalid or too large.":data.Length==0 || Array.Exists(data.ToCharArray(),c=>!(char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '='))?"Use an inline PNG or JPEG screenshot.":decoded?"accepted":"The screenshot encoding is invalid.";
            string actualImage=Outcome(()=>image.Invoke(null,new object[]{url}));Assert(actualImage==expectedImage,$"Screenshot Base64 acceptance/message changed for {data.Length} chars ({(data.Length<30?System.Text.Json.JsonSerializer.Serialize(data):"large")}): expected {expectedImage}; actual {actualImage}");
            string expectedInline=data.Length==0?"Invalid or oversized attachment data.":decoded?"accepted":"Invalid attachment encoding.";
            Assert(Outcome(()=>inline.Invoke(null,new object[]{url,"image/jpeg"}))==expectedInline,"Attachment Base64 acceptance/message changed");
            string expectedVoice=url.Length>3*1024*1024?"Supply a bounded JPEG or PNG image data URL.":decoded?"accepted":"The shared image is invalid.";
            Assert(Outcome(()=>AssistantVoiceSession.ValidateImage(url))==expectedVoice,"Voice Base64 acceptance/message changed");
        }
        string huge="data:application/pdf;base64,"+new string('A',AssistantAttachments.MaximumPayload);
        Assert(Outcome(()=>inline.Invoke(null,new object[]{huge,"application/pdf"}))=="Invalid or oversized attachment data.","Attachment bound changed");
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        foreach(char a in alphabet)foreach(char b in alphabet)
        {
            foreach(string data in new[]{"AAAA"+a+b+"=="," \tAAAA\r\n"+a+" "+b+"=\r=\n","AAAAA"+a+b+"="})
            {bool old;try{Convert.FromBase64String(data);old=true;}catch(FormatException){old=false;}Assert(AssistantBase64.IsValid(data.AsSpan())==old,"Noncanonical pad bits acceptance changed");}
        }
        var random=new Random(221);const string symbols="ABCDab09+/= \t\r\n_\v\u00a0";
        for(int i=0;i<4000;i++)
        {
            var chars=new char[random.Next(0,24)];for(int j=0;j<chars.Length;j++)chars[j]=symbols[random.Next(symbols.Length)];string data=new(chars);
            bool old;try{Convert.FromBase64String(data);old=true;}catch(FormatException){old=false;}
            Assert(AssistantBase64.IsValid(data.AsSpan())==old,"Malformed Base64 acceptance changed");
        }
        // Warm the framework validator before measuring the validation-only operation.
        string large=new('A',2*1024*1024);AssistantBase64.IsValid(large.AsSpan());
        long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<20;i++)Assert(AssistantBase64.IsValid(large.AsSpan()),"Valid large Base64 rejected");long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
        Assert(allocated<1024,"Base64 validation allocated a payload buffer");Evidence.Add($"Base64 validation: 20 x 2 MiB encoded chars, managed allocated bytes={allocated} (payload already resident).");
    }
    internal static void Run(Action<string,Action> test)
    {
        Evidence.Clear();
        test("Capture optimization preserves protocol Base64 acceptance bounds whitespace and messages",ValidateBase64);
        test("Capture optimization preserves native JPEG sizes and representative opaque colors",()=>
        {foreach(var size in new[]{(1,1),(640,360),(1920,1080),(1080,1920),(3840,2160),(1,2048)}){using var fixture=Fixture(size.Item1,size.Item2);Compare(fixture);}});
        test("Capture optimization preserves premultiplied alpha appearance",()=>{foreach(byte alpha in new byte[]{0,64,128,192}){using var fixture=Fixture(640,360,alpha);Compare(fixture);}});
        test("Capture optimization preserves mixed alpha stripes and boundary resampling",()=>{CompareAlphaPattern(true,false);CompareAlphaPattern(false,false);CompareAlphaPattern(true,true);});
        test("Capture optimization cancellation preserves borrowed bitmap ownership",()=>
        {
            foreach(byte alpha in new byte[]{255,128})
            {
            using var fixture=Fixture(1920,1080,alpha);using var cancel=new CancellationTokenSource();cancel.Cancel();
            for(int i=0;i<8;i++){try{AssistantShareCapture.Encode(Target,fixture,cancel.Token).GetAwaiter().GetResult();throw new Exception("Cancelled encode succeeded");}catch(OperationCanceledException){}}
            Assert(fixture.PixelWidth==1920,"Encoder disposed a borrowed bitmap");
            for(int i=0;i<8;i++)
            {
                using var during=new CancellationTokenSource();var pending=AssistantShareCapture.Encode(Target,fixture,during.Token);during.Cancel();
                try{pending.GetAwaiter().GetResult();throw new Exception("In-flight cancelled encode succeeded");}catch(OperationCanceledException){}
            }
            var encoded=AssistantShareCapture.Encode(Target,fixture,default).GetAwaiter().GetResult();Assert(encoded.Width==1280,"Bitmap unusable after cancellation");
            }
        });
        test("Capture optimization retains the 128 MiB source limit",()=>
        {
            using var huge=new SoftwareBitmap(BitmapPixelFormat.Bgra8,8193,4096,BitmapAlphaMode.Ignore);
            Assert(Outcome(()=>AssistantShareCapture.Encode(Target,huge,default).GetAwaiter().GetResult())=="Choose a smaller display or window to share.","Source bound changed");
        });
        test("Capture optimization reduces managed allocation against previous 4K pipeline",()=>
        {
            using var fixture=Fixture(3840,2160);Legacy(fixture);AssistantShareCapture.Encode(Target,fixture,default).GetAwaiter().GetResult();
            // Total allocation spans continuations as well as the initiating thread. This process is an isolated static fixture.
            long before=GC.GetTotalAllocatedBytes(true);for(int i=0;i<3;i++)Legacy(fixture);long legacy=GC.GetTotalAllocatedBytes(true)-before;
            before=GC.GetTotalAllocatedBytes(true);for(int i=0;i<3;i++)AssistantShareCapture.Encode(Target,fixture,default).GetAwaiter().GetResult();long native=GC.GetTotalAllocatedBytes(true)-before;
            Evidence.Add($"Synthetic 3840x2160 capture, 3 warm encodes: previous managed allocated bytes={legacy}; native managed allocated bytes={native}; reduction={100d*(legacy-native)/legacy:F2}%. Native codec/surface memory excluded; no forced GC.");
            Assert(native<legacy/4,$"Native encoder allocation did not fall sufficiently: old={legacy},new={native}");
            AssistantShareCapture.EncodeAlphaIfNeeded(Target,fixture,1280,720,default);
            var watch=new Stopwatch();before=GC.GetTotalAllocatedBytes(true);watch.Start();
            for(int i=0;i<10;i++)Assert(AssistantShareCapture.EncodeAlphaIfNeeded(Target,fixture,1280,720,default)==null,"Opaque frame incorrectly selected alpha fallback");
            watch.Stop();long scan=GC.GetTotalAllocatedBytes(true)-before;
            Evidence.Add($"Opaque 3840x2160 alpha classification, 10 warm scans: managed allocated bytes={scan}; total elapsed={watch.Elapsed.TotalMilliseconds:F2} ms; mean={watch.Elapsed.TotalMilliseconds/10:F2} ms. Bounded 16 KiB pooled scratch; no full-frame arrays or forced GC.");
            Assert(scan<64*1024,"Alpha classification allocated a large buffer");
            using var translucent=Fixture(3840,2160,128);Legacy(translucent);AssistantShareCapture.Encode(Target,translucent,default).GetAwaiter().GetResult();
            before=GC.GetTotalAllocatedBytes(true);for(int i=0;i<3;i++)Legacy(translucent);legacy=GC.GetTotalAllocatedBytes(true)-before;
            before=GC.GetTotalAllocatedBytes(true);for(int i=0;i<3;i++)AssistantShareCapture.Encode(Target,translucent,default).GetAwaiter().GetResult();long fallback=GC.GetTotalAllocatedBytes(true)-before;
            Evidence.Add($"Translucent 3840x2160 capture, 3 warm encodes: previous managed allocated bytes={legacy}; native-view alpha fallback managed allocated bytes={fallback}; reduction={100d*(legacy-fallback)/legacy:F2}%. Native GDI/codec memory excluded.");
            Assert(fallback<legacy/4,$"Alpha fallback allocation did not fall sufficiently: old={legacy},new={fallback}");
        });
    }
}
