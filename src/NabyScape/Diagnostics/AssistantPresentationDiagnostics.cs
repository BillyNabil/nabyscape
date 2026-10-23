using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

// Synthetic pixels and session-only attachments. No provider, clipboard, dialogs,
// microphone, desktop capture, input injection, or credential access.
internal static class AssistantPresentationDiagnostics
{
    private static byte[] Fixture(int width,int height,double dpi=96)
    {
        var pixels=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {int p=(y*width+x)*4;pixels[p]=(byte)(x%251);pixels[p+1]=(byte)(y%239);pixels[p+2]=(byte)((x/32+y/32)%2==0?230:30);pixels[p+3]=255;}
        var image=BitmapSource.Create(width,height,dpi,dpi,PixelFormats.Bgra32,null,pixels,width*4);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    internal static async Task Run(App app,OverlayWindow overlay,string directory,Action<string,bool> check)
    {
        overlay.Hide();var ai=app.Assistant;var evidence=new List<string>();
        var landscape=Fixture(1600,900);var portrait=Fixture(900,1600);var highDpi=Fixture(1600,900,192);
        var file=new AssistantAttachment("Synthetic.png","image/png",AssistantAttachments.Data("image/png",landscape));
        var portraitFile=new AssistantAttachment("Portrait.png","image/png",AssistantAttachments.Data("image/png",portrait));
        var hdpiFile=new AssistantAttachment("DPI.png","image/png",AssistantAttachments.Data("image/png",highDpi));
        var wrapped=new AssistantAttachment("Wrapped.png","image/png",file.Source.Insert(100,"\r\n"));
        foreach(var fixture in new[]{file,portraitFile,hdpiFile,wrapped})
        {
            var metadata=AssistantPreviewGeometry.Read(fixture,fixture.Source);var legacy=AssistantAttachments.Thumbnail(AssistantAttachments.Bytes(fixture.Source));
            check("Presentation header matches previous preview geometry: "+fixture.Name,Math.Abs(metadata.Width-legacy.Width)<1 && Math.Abs(metadata.Height-legacy.Height)<1);
        }
        var cache=new AssistantPreviewCache();int reads=0;var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<byte[]> Read(CancellationToken token){Interlocked.Increment(ref reads);await ready.Task.WaitAsync(token);return landscape;}
        using var cancel=new CancellationTokenSource();var canceled=cache.Get(file,448,Read,true,cancel.Token);var survivor=cache.Get(file,448,Read,true,default);
        cancel.Cancel();ready.SetResult();try{await canceled;check("Presentation consumer cancellation is honored",false);}catch(OperationCanceledException){check("Presentation consumer cancellation is honored",true);}
        var shared=await survivor;var hit=await cache.Get(file,448,Read,true,default);
        check("Presentation concurrent consumers share one frozen decode",reads==1 && ReferenceEquals(shared,hit) && hit.IsFrozen);
        check("Presentation cached pixels have no encoded StreamSource",hit is WriteableBitmap);
        var expected=AssistantAttachments.Thumbnail(landscape,448);int stride=(expected.PixelWidth*expected.Format.BitsPerPixel+7)/8;
        var expectedPixels=new byte[stride*expected.PixelHeight];var cachedPixels=new byte[expectedPixels.Length];expected.CopyPixels(expectedPixels,stride,0);hit.CopyPixels(cachedPixels,stride,0);
        check("Presentation detached cache preserves every decoded pixel and DPI",expectedPixels.SequenceEqual(cachedPixels) && expected.DpiX==hit.DpiX && expected.DpiY==hit.DpiY);
        int remoteReads=0;
        Task<byte[]> Remote(CancellationToken token){Interlocked.Increment(ref remoteReads);return Task.FromResult(landscape);}
        await cache.Get(file,448,Remote,false,default);await cache.Get(file,448,Remote,false,default);
        check("Presentation remote requests always re-enter provider-owned read",remoteReads==2);
        var abandoned=new AssistantAttachment("Abandoned.png","image/png",file.Source);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var ended=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<byte[]> Abandon(CancellationToken token){entered.SetResult();try{await Task.Delay(Timeout.Infinite,token);return landscape;}finally{ended.SetResult();}}
        using(var stop=new CancellationTokenSource())
        {
            var orphan=cache.Get(abandoned,448,Abandon,true,stop.Token);await entered.Task;stop.Cancel();try{await orphan;}catch(OperationCanceledException){}await ended.Task;
            check("Presentation orphan read cancels and leaves no pending cache entry",cache.Snapshot.Pending==0);
        }
        for(int i=0;i<30;i++)await cache.Get(new AssistantAttachment("Bound "+i,"image/png",file.Source),1600,_=>Task.FromResult(landscape),true,default);
        var bounded=cache.Snapshot;check("Presentation cache enforces both count and byte limits",bounded.Count<=AssistantPreviewCache.MaximumCount && bounded.Bytes<=AssistantPreviewCache.MaximumBytes);
        evidence.Add($"Large-preview cache after30unique1600px requests: entries={bounded.Count}; retained pixel bytes={bounded.Bytes}; maximum={AssistantPreviewCache.MaximumBytes}.");
        cache.ExpireForTest(DateTime.UtcNow+AssistantPreviewCache.IdleAge+TimeSpan.FromSeconds(1));check("Presentation idle expiration drops all cached pixels",cache.Snapshot.Count==0 && cache.Snapshot.Bytes==0);
        for(int i=0;i<30;i++)await cache.Get(new AssistantAttachment("Small "+i,"image/png",file.Source),64,_=>Task.FromResult(landscape),true,default);
        check("Presentation small previews enforce count limit independently",cache.Snapshot.Count==AssistantPreviewCache.MaximumCount);
        ai.Options.Enabled=true;ai.Clear();await ai.AttachEncodedImage(landscape);
        var add=typeof(AssistantConversation).GetMethod("Add",BindingFlags.NonPublic|BindingFlags.Instance)!;
        for(int i=0;i<48;i++)
        {
            var image=i%2==0?file:portraitFile;var attachment=image with{Name=$"Fixture {i:00}.png"};
            add.Invoke(ai.Conversation,new object[]{new AssistantMessage(i%2==0?"user":"assistant",$"Synthetic message {i:00}\n\n**Selectable rich text** and `code` remain available.",Attachments:new[]{attachment})});
        }
        var construction=Stopwatch.StartNew();var window=new AssistantWindow(app){ShowActivated=false,Width=720,Height=740};window.Show();construction.Stop();evidence.Add($"48attachment layout/header construction: {construction.Elapsed.TotalMilliseconds:0.00}ms (includes window creation). No full original Base64 buffer is allocated by metadata reader.");await Settle(window);
        try
        {
            var view=Diagnostics.Descendants(window).OfType<AssistantView>().Single();view.Editor.Text="Unsent fixture draft";view.Editor.Select(3,7);
            var transcript=Diagnostics.Descendants(window).OfType<ScrollViewer>().Single(s=>AutomationProperties.GetName(s)=="AI conversation");
            var all=Diagnostics.Descendants(transcript).OfType<AssistantAttachmentView>().ToArray();
            var draft=Diagnostics.Descendants(view).OfType<AssistantAttachmentView>().Single(v=>!all.Contains(v));
            check("Presentation draft uses small display-size thumbnail",draft.PreviewImage.Source is BitmapSource draftBitmap && Math.Max(draftBitmap.PixelWidth,draftBitmap.PixelHeight)<=64);
            long OriginalPixels()=>all.Sum(v=>v.PreviewImage.Source is BitmapSource b?(long)b.PixelWidth*b.PixelHeight*4:0);
            long legacyBytes=48L*480*270*4;
            var bottom=all.Count(v=>v.PreviewRealized);long bottomBytes=OriginalPixels();
            check("Presentation preserves all48attachment controls and message menus",all.Length==48 && Diagnostics.Descendants(window).OfType<StackPanel>().Count(p=>p.ContextMenu?.Items.OfType<MenuItem>().Any(m=>(string)m.Header=="Copy message")==true)==48);
            check("Presentation only viewport and overscan previews are realized",bottom is >0 and <8);
            evidence.Add($"48alternating1600x900/900x1600 attachments: legacy retained bitmap bytes={legacyBytes}; bottom realized={bottom}; realized bitmap bytes={bottomBytes}; shared cache={AssistantPreviewCache.For(ai).Snapshot}.");
            double extent=transcript.ExtentHeight;transcript.ScrollToVerticalOffset(transcript.ScrollableHeight*.48);await Settle(window);double middleOffset=transcript.VerticalOffset;
            check("Presentation middle scrolling preserves extent while releasing old pixels",Math.Abs(transcript.ExtentHeight-extent)<1 && all.Count(v=>v.PreviewRealized) is >0 and <9 && !all[^1].PreviewRealized);
            var middle=all.Count(v=>v.PreviewRealized);evidence.Add($"Middle realized={middle}; realized bitmap bytes={OriginalPixels()}; extent={extent:0.00}; offset={middleOffset:0.00}.");
            var screenshot=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);screenshot.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(screenshot));using(var stream=File.Create(Path.Combine(directory,"presentation-middle.png")))png.Save(stream);
            view.Visibility=Visibility.Collapsed;await Task.Delay(100);
            check("Presentation hidden view releases every decoded Image.Source",all.All(v=>!v.PreviewRealized) && !draft.PreviewRealized);
            check("Presentation hidden transcript clears visuals and preserves draft selection",view.Editor.Text=="Unsent fixture draft" && view.Editor.SelectionStart==3 && view.Editor.SelectionLength==7 && !Diagnostics.Descendants(transcript).OfType<AssistantAttachmentView>().Any() && ai.DraftFiles.Count==1);
            view.Visibility=Visibility.Visible;await Settle(window);
            check("Presentation reopen restores exact scroll position and draft",Math.Abs(transcript.VerticalOffset-middleOffset)<1 && view.Editor.Text=="Unsent fixture draft" && view.Editor.SelectionStart==3 && view.Editor.SelectionLength==7);
            var reopened=Diagnostics.Descendants(transcript).OfType<AssistantAttachmentView>().ToArray();
            check("Presentation reopen realizes bounded previews without losing history",reopened.Length==48 && reopened.Count(v=>v.PreviewRealized) is >0 and <9 && draft.PreviewRealized);
            transcript.ScrollToTop();await Settle(window);
            var first=Diagnostics.Descendants(transcript).OfType<AssistantAttachmentView>().First();
            check("Presentation offscreen originals remain exact for original actions",(await ai.Media(portraitFile,default)).SequenceEqual(portrait) && ai.Conversation.Messages.Count==48);
            var original=AssistantAttachments.Thumbnail(await ai.Media(file,default),1800);
            check("Presentation original enlarge decode retains1600x900pixels",original.PixelWidth==1600 && original.PixelHeight==900);
            check("Presentation copy/save/open actions remain available",Diagnostics.Descendants(first).OfType<Button>().Count(b=>new[]{"Open preview","Copy image","Save…"}.Contains(AutomationProperties.GetName(b)))==3);
            var preview=first.PreviewImage;var before=preview.Source as BitmapSource;window.Width=1100;view.LayoutTransform=new ScaleTransform(1.5,1.5);await Settle(window);var after=preview.Source as BitmapSource;
            check("Presentation panel scaling requests enough pixels without reducing sharpness",before!=null && after!=null && after.PixelWidth>before.PixelWidth);
            view.LayoutTransform=Transform.Identity;window.Width=720;await Settle(window);
            for(int page=0;page<=12;page++){transcript.ScrollToVerticalOffset(transcript.ScrollableHeight*page/12);window.UpdateLayout();await Task.Delay(90);}
            await Settle(window);var traversed=AssistantPreviewCache.For(ai).Snapshot;
            check("Presentation traversing history keeps cache bounded and no pending reads",traversed.Count<=AssistantPreviewCache.MaximumCount && traversed.Bytes<=AssistantPreviewCache.MaximumBytes && traversed.Pending==0);
            evidence.Add($"After13scroll positions: cache entries={traversed.Count}; retained cache pixel bytes={traversed.Bytes}; active sources={Diagnostics.Descendants(transcript).OfType<AssistantAttachmentView>().Count(v=>v.PreviewRealized)}; pending={traversed.Pending}.");
            transcript.ScrollToTop();window.UpdateLayout();view.Visibility=Visibility.Collapsed;await Task.Delay(200);
            check("Presentation rapid scroll then hide cannot reattach late pixels",Diagnostics.Descendants(view).OfType<AssistantAttachmentView>().All(v=>!v.PreviewRealized) && AssistantPreviewCache.For(ai).Snapshot.Pending==0);
            var process=Process.GetCurrentProcess();process.Refresh();evidence.Add($"Context only, no forcedGC or working-set trim: process private bytes={process.PrivateMemorySize64}; total working set={process.WorkingSet64}; process counters include fixture creation and previous tests, not an isolated before/after comparison.");
        }
        finally{window.Close();ai.Clear();}
        await RemotePresentation(app,directory,evidence,check);
        File.WriteAllLines(Path.Combine(directory,"presentation-evidence.txt"),evidence);
    }
    private sealed class RemoteImages(Dictionary<string,byte[]> images):HttpMessageHandler
    {
        internal TaskCompletionSource? Release;
        internal int Reads,Canceled,Authorized;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Interlocked.Increment(ref Reads);
            if(request.Headers.Authorization?.Parameter=="synthetic-preview-key")Interlocked.Increment(ref Authorized);
            try
            {
                var release=Release;if(release!=null)await release.Task.WaitAsync(token);
                return new(HttpStatusCode.OK){Content=new ByteArrayContent(images[request.RequestUri!.AbsolutePath])};
            }
            catch(OperationCanceledException){Interlocked.Increment(ref Canceled);throw;}
        }
    }
    private sealed class PreviewKeys(System.Windows.Threading.Dispatcher dispatcher):IAssistantKeys
    {
        internal int Reads;internal bool OnDispatcher=true;
        public string? Read(string id){Reads++;OnDispatcher&=dispatcher.CheckAccess();return "synthetic-preview-key";}
        public bool Exists(string id)=>true;
        public void Write(string id,string value)=>throw new NotSupportedException();
        public void Remove(string id)=>throw new NotSupportedException();
    }
    private static async Task RemotePresentation(App app,string directory,List<string> evidence,Action<string,bool> check)
    {
        var ai=app.Assistant;
        var fixtures=new[]{Fixture(1600,900,192),Fixture(900,1600),Fixture(1600,900),Fixture(150,90),Fixture(900,1600,192)};
        var provider=new AssistantProvider{Id=Guid.NewGuid().ToString("N"),Name="Synthetic preview provider",BaseUrl="https://preview-fixture.invalid/v1/"};
        var files=Enumerable.Range(0,15).Select(i=>new AssistantAttachment($"Remote {i:00}.png","image/png",provider.BaseUrl+"image/"+i,ProviderId:provider.Id,Origin:provider.BaseUrl)).ToArray();
        var payloads=files.Select((file,i)=>(Path:new Uri(file.Source).AbsolutePath,Bytes:fixtures[i%fixtures.Length])).ToDictionary(p=>p.Path,p=>p.Bytes);
        payloads.Add("/v1/image/appended",fixtures[0]);
        using var handler=new RemoteImages(payloads);using var fakeTransport=new AssistantTransport(handler);var keys=new PreviewKeys(app.Dispatcher);
        var transportField=typeof(AssistantRuntime).GetField("transport",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var keysField=typeof(AssistantRuntime).GetField("Keys",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var previousTransport=transportField.GetValue(ai);var previousKeys=keysField.GetValue(ai);var selected=ai.Options.SelectedProvider;
        // Replace only this isolated diagnostic runtime's transport; production Media
        // still performs provider matching and authorization on every actual request.
        transportField.SetValue(ai,fakeTransport);keysField.SetValue(ai,keys);ai.Options.Providers.Add(provider);ai.Options.SelectedProvider=provider.Id;
        AssistantWindow? window=null;
        try
        {
            ai.Clear();await ai.AttachEncodedImage(fixtures[2]);
            var add=typeof(AssistantConversation).GetMethod("Add",BindingFlags.NonPublic|BindingFlags.Instance)!;
            foreach(var file in files)add.Invoke(ai.Conversation,new object[]{new AssistantMessage("assistant","Synthetic remote message "+file.Name,Attachments:new[]{file})});
            handler.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
            window=new AssistantWindow(app){ShowActivated=false,Width=360,Height=740};window.Show();await Settle(window);
            var view=Diagnostics.Descendants(window).OfType<AssistantView>().Single();view.Editor.Text="Remote draft remains";view.Editor.Select(2,5);
            var transcript=Diagnostics.Descendants(view).OfType<ScrollViewer>().Single(s=>AutomationProperties.GetName(s)=="AI conversation");
            var panel=(Panel)transcript.Content;
            AssistantAttachmentView[] Cards()=>Diagnostics.Descendants(transcript).OfType<AssistantAttachmentView>().ToArray();
            double Top(FrameworkElement element)=>element.TransformToAncestor(transcript).Transform(new Point()).Y;
            (FrameworkElement Element,double Top) Anchor(){var element=panel.Children.OfType<FrameworkElement>().First(e=>Top(e)+e.ActualHeight>0);return(element,Top(element));}
            void ToCard(int index,double extra=0){transcript.ScrollToVerticalOffset(transcript.VerticalOffset+Top(Cards()[index])+extra);window.UpdateLayout();}
            check("Presentation unknown remote slots reserve geometry without offscreen prefetch",handler.Reads is >0 and <=2 && Cards().All(c=>c.PreviewImage.DesiredSize.Height>0) && !Cards()[0].PreviewRealized);
            handler.Release.SetResult();handler.Release=null;await Settle(window);
            check("Presentation delayed remote first load preserves follow latest",transcript.ScrollableHeight-transcript.VerticalOffset<1);

            // A never-requested 192-DPI image finishes in overscan above the viewport.
            handler.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);ToCard(0,Cards()[0].ActualHeight+35);await Settle(window);
            var above=Anchor();int beforeAbove=handler.Reads;handler.Release.SetResult();handler.Release=null;await Settle(window);
            check("Presentation delayed remote geometry above viewport preserves visible message anchor",beforeAbove>2 && Math.Abs(Top(above.Element)-above.Top)<1);
            handler.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);ToCard(6);await Settle(window);
            var inside=Anchor();handler.Release.SetResult();handler.Release=null;await Settle(window);
            check("Presentation delayed remote geometry inside viewport preserves visible message anchor",Math.Abs(Top(inside.Element)-inside.Top)<1);

            // Load every source at a narrow width before expanding. This includes a
            // small original, both orientations and 96/192-DPI metadata.
            for(int i=0;i<files.Length;i++){ToCard(i);await Settle(window);}
            bool narrow=Cards().Where(c=>c.PreviewRealized).All(c=>c.PreviewImage.Source is BitmapSource b && Math.Max(b.PixelWidth,b.PixelHeight)<=256);
            window.Width=900;await Settle(window);bool geometry=true,sharp=true;
            for(int i=0;i<files.Length;i++)
            {
                ToCard(i);await Settle(window);var image=Cards()[i].PreviewImage;var legacy=AssistantAttachments.Thumbnail(fixtures[i%fixtures.Length]);
                var natural=((AssistantPreviewImage)image).NaturalSize;
                geometry&=Math.Abs(natural.Width-legacy.Width)<1 && Math.Abs(natural.Height-legacy.Height)<1;
                var oracle=new AssistantPreviewImage{NaturalSize=new Size(legacy.Width,legacy.Height),MaxHeight=240};oracle.Measure(new Size(Cards()[i].ActualWidth-22,double.PositiveInfinity));
                geometry&=Math.Abs(image.ActualWidth-oracle.DesiredSize.Width)<1 && Math.Abs(image.ActualHeight-oracle.DesiredSize.Height)<1;
                sharp&=image.Source is BitmapSource bitmap && Math.Max(bitmap.PixelWidth,bitmap.PixelHeight)>=Math.Min(Math.Max(legacy.PixelWidth,legacy.PixelHeight),Math.Max(image.ActualWidth,image.ActualHeight))-1;
            }
            check("Presentation remote narrow-to-wide layout matches original landscape portrait DPI and small-image geometry",narrow && geometry);
            check("Presentation remote wider display receives sufficient decoded pixels",sharp);
            transcript.ScrollToVerticalOffset(transcript.ScrollableHeight*.48);await Settle(window);double extent=transcript.ExtentHeight,offset=transcript.VerticalOffset;var savedAnchor=Anchor();int anchorIndex=panel.Children.IndexOf(savedAnchor.Element);
            var screenshot=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);screenshot.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(screenshot));using(var stream=File.Create(Path.Combine(directory,"presentation-remote-middle.png")))png.Save(stream);
            var old=Cards();view.Visibility=Visibility.Collapsed;await Task.Delay(180);int hiddenReads=handler.Reads;await Task.Delay(180);
            check("Presentation hidden remote transcript releases pixels and makes no new requests",old.All(c=>!c.PreviewRealized) && handler.Reads==hiddenReads && !Cards().Any());
            view.Visibility=Visibility.Visible;await Settle(window);
            check("Presentation remote reopen preserves learned extent offset anchor and draft selection",Math.Abs(transcript.ExtentHeight-extent)<1 && Math.Abs(transcript.VerticalOffset-offset)<1 && Math.Abs(Top((FrameworkElement)panel.Children[anchorIndex])-savedAnchor.Top)<1 && view.Editor.Text=="Remote draft remains" && view.Editor.SelectionStart==2 && view.Editor.SelectionLength==5);
            check("Presentation remote reopen reauthorizes each read on UI dispatcher",handler.Reads>hiddenReads && handler.Authorized==handler.Reads && keys.Reads==handler.Reads && keys.OnDispatcher);
            int withProvider=handler.Authorized;ai.Options.Providers.Remove(provider);transcript.ScrollToTop();await Settle(window);
            check("Presentation remote preview cannot reuse previous provider authorization",handler.Authorized==withProvider && handler.Reads>handler.Authorized);
            ai.Options.Providers.Add(provider);
            transcript.ScrollToEnd();await Settle(window);handler.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var appended=files[0] with{Name="Remote appended.png",Source=provider.BaseUrl+"image/appended"};
            add.Invoke(ai.Conversation,new object[]{new AssistantMessage("assistant","Newly received remote message",Attachments:new[]{appended})});await Settle(window);
            handler.Release.SetResult();handler.Release=null;await Settle(window);
            check("Presentation newly appended remote geometry preserves bottom follow and history",transcript.ScrollableHeight-transcript.VerticalOffset<1 && Cards().Length==files.Length+1 && Cards()[^1].PreviewRealized);
            var allBytes=await ai.Media(files[0],default);check("Presentation remote original read preserves full payload",allBytes.SequenceEqual(fixtures[0]));
            transcript.ScrollToTop();await Settle(window);
            handler.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);int canceled=handler.Canceled;transcript.ScrollToEnd();await Settle(window);var pending=Cards();view.Visibility=Visibility.Collapsed;await Task.Delay(180);int stopped=handler.Reads;handler.Release.SetResult();handler.Release=null;await Task.Delay(180);
            check("Presentation remote hide cancels pending reads and rejects late pixels without hidden network",handler.Canceled>canceled && handler.Reads==stopped && pending.All(c=>!c.PreviewRealized));
            // Several attachments share one message top. Anchor the visible later
            // attachment when the first image resolves in overscan above it.
            ai.Clear();var grouped=files.Take(5).Select(f=>f with{Name="Grouped "+f.Name}).ToArray();
            add.Invoke(ai.Conversation,new object[]{new AssistantMessage("assistant","Multiple delayed remote images in one message",Attachments:grouped)});
            handler.Release=new(TaskCreationOptions.RunContinuationsAsynchronously);view.Visibility=Visibility.Visible;await Settle(window);
            ToCard(0,Cards()[0].ActualHeight+35);await Settle(window);var later=Diagnostics.Descendants(Cards()[1]).OfType<TextBlock>().First(t=>t.Text==grouped[1].Name);double laterTop=Top(later),unknownHeight=Cards()[0].ActualHeight;
            var leaf=(FrameworkElement?)typeof(AssistantPreviewImage).GetMethod("VisibleAnchor",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{panel,transcript,Cards()[0].PreviewImage});
            evidence.Add($"Grouped before: card0top={Top(Cards()[0]):0.00}; card0height={unknownHeight:0.00}; image0top={Top(Cards()[0].PreviewImage):0.00}; image0height={Cards()[0].PreviewImage.ActualHeight:0.00}; card1top={Top(Cards()[1]):0.00}; laterTop={laterTop:0.00}; offset={transcript.VerticalOffset:0.00}; anchor={leaf?.GetType().Name}; anchorTop={(leaf==null?0:Top(leaf)):0.00}; anchorName={(leaf==null?"":AutomationProperties.GetName(leaf))}.");
            handler.Release.SetResult();handler.Release=null;await Settle(window);
            evidence.Add($"Grouped after: card0top={Top(Cards()[0]):0.00}; card0height={Cards()[0].ActualHeight:0.00}; image0top={Top(Cards()[0].PreviewImage):0.00}; card1top={Top(Cards()[1]):0.00}; laterTop={Top(later):0.00}; offset={transcript.VerticalOffset:0.00}; anchorTop={(leaf==null?0:Top(leaf)):0.00}.");
            check($"Presentation delayed earlier attachment preserves later visible content in same message (height {unknownHeight:0.00}->{Cards()[0].ActualHeight:0.00}; top {laterTop:0.00}->{Top(later):0.00})",unknownHeight-Cards()[0].ActualHeight>20 && Math.Abs(Top(later)-laterTop)<1);
            evidence.Add($"Remote fake HTTP UI: 15 mixed orientation/96/192-DPI/small originals; learned extent={extent:0.00}; restored middle offset={offset:0.00}; requests={handler.Reads}; authenticated={handler.Authorized}; canceled={handler.Canceled}; no real network. Metadata retained only numeric original dimensions and DPI.");
        }
        finally
        {
            window?.Close();ai.Clear();transportField.SetValue(ai,previousTransport);keysField.SetValue(ai,previousKeys);ai.Options.Providers.Remove(provider);ai.Options.SelectedProvider=selected;
        }
    }
    private static async Task Settle(Window window)
    {window.UpdateLayout();await Task.Delay(450);window.UpdateLayout();await Task.Delay(150);}
}
