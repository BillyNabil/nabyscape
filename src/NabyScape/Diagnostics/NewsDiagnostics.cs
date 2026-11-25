using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class NewsDiagnostics
{
    private sealed class Fixture : HttpMessageHandler
    {
        internal int SourceRequests,Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests++;
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--news-fixture");
            string? dir=at>=0 && at+1<args.Length?args[at+1]:null;
            if(request.RequestUri!.AbsolutePath.EndsWith("/sources",StringComparison.Ordinal))
            {
                SourceRequests++;string[] ids=Uri.UnescapeDataString(request.RequestUri.Query[5..]).Split(',');
                object[] sources=Array.Empty<object>();
                if(dir!=null)
                {
                    using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir,"news-sources.json")));
                    sources=doc.RootElement.EnumerateArray().Where(e=>ids.Contains(e.GetProperty("id").ToString())).Select(e=>{
                        string photo=e.GetProperty("avatar").GetString()??"";
                        return (object)new{id=e.GetProperty("id").ToString(),name=e.GetProperty("name").GetString(),avatar=photo.StartsWith('/')?photo:"/media/"+photo};
                    }).ToArray();
                }
                else sources=new object[]{new{id="1",name="Sample source",avatar="/cover.jpg"}};
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(new{version=1,sources}),Encoding.UTF8,"application/json")});
            }
            if(dir!=null && request.RequestUri!.AbsolutePath.StartsWith("/media/avatars/",StringComparison.Ordinal))
            {
                string avatar=Path.Combine(dir,"avatars",Path.GetFileName(request.RequestUri.AbsolutePath));if(!File.Exists(avatar))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(avatar))};response.Content.Headers.ContentType=new("image/png");return Task.FromResult(response);
            }
            if(request.RequestUri!.AbsolutePath=="/cover.jpg")
            {
                var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(dir!=null?File.ReadAllBytes(Path.Combine(dir,"cover.jpg")):Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="))};response.Content.Headers.ContentType=new("image/png");return Task.FromResult(response);
            }
            if(dir!=null)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(File.ReadAllText(Path.Combine(dir,"digest.json")),Encoding.UTF8,"application/json")});
            var titles=new[]{"City libraries open new reading rooms — sample story","A new night train connects two cities — sample story","The riverside park welcomes visitors — sample story","A science museum opens its new gallery — sample story","Community gardens grow across the city — sample story"};
            var items=titles.Select((title,index)=>new{postId=(index+1).ToString(),head=title,nsrc=index+2,srcs=new[]{"1"},cover="/cover.jpg",url=index==1?null:"https://example.org/story/"+(index+1),notes=Array.Empty<string>()});
            string body=System.Text.Json.JsonSerializer.Serialize(new{title="Sample news summary",items});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});
        }
    }
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var settings=app.Settings;settings.ReduceMotion=true;settings.NewsConnected=false;settings.GlassStyle=false;settings.Background="#000000";
        bool live=Environment.GetCommandLineArgs().Contains("--news-live");
        settings.NewsApiBaseUrl=NewsService.DefaultApi;
        var fixture=new Fixture();if(!live){app.News.Dispose();app.News=new NewsService(fixture);}
        var command=Environment.GetCommandLineArgs();int fixtureAt=Array.IndexOf(command,"--news-fixture");
        settings.Widgets.Single(w=>w.Id=="news").Enabled=true;settings.CompactLayout=true;settings.PanelWidth=420;settings.PanelMaxHeight=650;settings.PanelAutoHeight=true;
        app.ChangeSettings();island.Show();island.Reveal(false);island.SelectPage("news",false);await Task.Delay(150);
        var view=Diagnostics.Descendants(island).OfType<NewsView>().Single();
        async Task Wait()
        {var start=DateTime.UtcNow;while(view.Busy && DateTime.UtcNow-start<TimeSpan.FromSeconds(25))await Task.Delay(50);await Task.Delay(350);island.UpdateLayout();}
        void Capture(string name)
        {
            island.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth),(int)Math.Ceiling(island.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(island);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(directory,name+".png"));encoder.Save(stream);
        }
        await Wait();if(!live)check("Disabled news makes no HTTP requests before explicit connection",fixture.Requests==0);
        Capture("news-disconnected");
        Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="Connect").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
        view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();
        check("News digest loads through configured HTTP API",view.Shown?.Items.Length>0);Capture("news-digest-dark");
        if(!live)check("Source avatars arrive through the API without a profile catalog",fixture.SourceRequests>0 && !File.Exists(Path.Combine(app.Store.DirectoryPath,"news-sources.json")));
        var links=new System.Collections.Generic.List<Uri>();view.LinkObserver=links.Add;
        var brand=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="Open Anyway News");
        brand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check("Anyway logo and name open the mini app",links.Single()==NewsService.AppLink);
        check("Digest contains the original Anyway logo",Diagnostics.Descendants(brand).OfType<Image>().Any(i=>i.Source!=null));
        check("Digest has no weather tab",!Diagnostics.Descendants(view).OfType<Button>().Any(b=>System.Windows.Automation.AutomationProperties.GetAutomationId(b).StartsWith("Weather",StringComparison.Ordinal)));
        var firstStory=Diagnostics.Descendants(view).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)==view.Shown!.Items[0].Title);firstStory.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check("Digest story opens its card in the mini app",links.Last()==view.Shown!.Items[0].Url);
        check("Digest poster cover decodes",Diagnostics.Descendants(view).OfType<Image>().Count(i=>i.Source!=null)>=2);
        var headline=Diagnostics.Descendants(firstStory).OfType<TextBlock>().First(t=>t.Text==view.Shown!.Items[0].Title);
        var photo=Diagnostics.Descendants(firstStory).OfType<Image>().First();
        // UniformToFill may extend the image beyond its clipped viewport; compare the visible frame.
        var photoFrame=(FrameworkElement)photo.Parent;
        check("Headline sits below the clipped photograph without overlapping it",photoFrame.Clip!=null && headline.TranslatePoint(new Point(),firstStory).Y>=photoFrame.TranslatePoint(new Point(0,photoFrame.ActualHeight),firstStory).Y);
        var feedLink=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="Open all news in the mini app");
        feedLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check("All news opens the working mini app",links.Last()==NewsService.AppLink);
        await view.Reload();await Wait();
        check("Cached refresh preserves the story and decoded images",ReferenceEquals(firstStory,Diagnostics.Descendants(view).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)==view.Shown!.Items[0].Title)) && photo.Source!=null);
        check("Source avatars keep square bounds and circular clipping",Diagnostics.Descendants(view).OfType<Image>().Where(i=>Equals(i.Tag,"news-source-avatar")).All(i=>i.Width==i.Height && i.Clip is EllipseGeometry));
        check("News hover does not create duplicate pop-out tooltips",!Diagnostics.Descendants(view).OfType<FrameworkElement>().Any(e=>e.IsVisible && e.ToolTip!=null));
        if(fixtureAt>=0 && fixtureAt+1<command.Length && File.Exists(Path.Combine(command[fixtureAt+1],"news-sources.json")))check("Real source avatars decode in circular headline groups",Diagnostics.Descendants(view).OfType<Image>().Count(i=>Equals(i.Tag,"news-source-avatar") && i.Source!=null)>=4);
        var connection=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="News connection");
        var connectionBounds=connection.TransformToAncestor(island).TransformBounds(new Rect(connection.RenderSize));
        check("News footer remains inside the island contour",connectionBounds.Bottom<=island.PaintedHeight-8);
        if(!live)
        {
            settings.NewsApiBaseUrl="https://example.org/api";app.ChangeSettings();island.SelectPage("news",false);await Task.Delay(100);view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();
            view.LinkObserver=links.Add;
            var customActions=Diagnostics.Descendants(view).OfType<Button>().ToArray();
            var customTexts=Diagnostics.Descendants(view).OfType<TextBlock>().Select(t=>t.Text).ToArray();
            var customAll=customActions.Single(b=>UI.Identity(b)=="Open all news in the mini app");
            check("Custom API shows its host without Anyway branding or mini-app actions",customTexts.Contains("example.org") && !customTexts.Contains("by Anyway News") && !customActions.Any(b=>UI.Identity(b)=="Open Anyway News") && !customAll.IsVisible && !Diagnostics.Descendants(view).OfType<Image>().Any(i=>i.Source is BitmapImage bitmap && bitmap.UriSource?.OriginalString.Contains("anyway-logo.jpg")==true));
            int beforeCustomAction=links.Count;customAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("Custom API cannot trigger the default mini-app action",links.Count==beforeCustomAction);
            var customFirst=customActions.Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)==view.Shown!.Items[0].Title);customFirst.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("Custom numeric story IDs follow only their explicit URL",links.Last()==new Uri("https://example.org/story/1"));
            check("Custom story without a URL remains readable and non-clickable",customTexts.Contains(view.Shown!.Items[1].Title) && !customActions.Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)==view.Shown.Items[1].Title));
            Capture("news-custom-provider");
            settings.NewsApiBaseUrl=NewsService.DefaultApi;app.ChangeSettings();island.SelectPage("news",false);await Task.Delay(100);view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();
            check("Returning to the default API restores the provider credit",Diagnostics.Descendants(view).OfType<Button>().Any(b=>UI.Identity(b)=="Open Anyway News"));
            connection=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="News connection");
        }
        connection.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
        int beforeDisconnect=fixture.Requests;
        Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="Disconnect").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
        view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();
        check("Disconnect clears the summary and stops news requests",!settings.NewsConnected && view.Shown==null && (live || fixture.Requests==beforeDisconnect));
        Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="Connect").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
        view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();
        foreach(double width in new[]{300d,526d})
        {
            settings.PanelWidth=width;app.ChangeSettings();island.SelectPage("news",false);await Task.Delay(100);
            view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();
            check("News fits panel width "+width,view.ActualWidth>0 && view.ActualWidth<=island.PaintedWidth);
            var footer=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="News connection");
            check("News footer fits at width "+width,footer.TransformToAncestor(island).TransformBounds(new Rect(footer.RenderSize)).Bottom<=island.PaintedHeight-8);
            Capture("news-width-"+width);
        }
        settings.PanelWidth=300;settings.PanelMaxHeight=360;settings.PanelScale=1.25;app.ChangeSettings();island.SelectPage("news",false);await Task.Delay(120);view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();
        var smallScroll=Diagnostics.Descendants(view).OfType<ScrollViewer>().Single();
        var lastStory=Diagnostics.Descendants(view).OfType<Button>().Last(b=>view.Shown!.Items.Any(i=>i.Title==System.Windows.Automation.AutomationProperties.GetName(b)));
        smallScroll.ScrollToBottom();await Task.Delay(100);island.UpdateLayout();
        check("Last story can be reached inside the small scaled viewport",smallScroll.ScrollableHeight>0 && lastStory.TranslatePoint(new Point(0,lastStory.ActualHeight),smallScroll).Y<=smallScroll.ActualHeight+1);
        var smallSettings=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="News connection");smallSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);island.UpdateLayout();Capture("news-connection-small");
        check("Connection form scrolls inside the card and keeps the footer visible",view.TransformToAncestor(island).TransformBounds(new Rect(view.RenderSize)).Bottom<=island.PaintedHeight-8 && smallSettings.TransformToAncestor(island).TransformBounds(new Rect(smallSettings.RenderSize)).Bottom<=island.PaintedHeight-8 && smallScroll.VerticalOffset==0);
        var smallBrand=Diagnostics.Descendants(view).OfType<TextBlock>().Single(t=>t.Text=="by Anyway News");
        check("Brand signature does not collide with actions at narrow scaled widths",smallBrand.TranslatePoint(new Point(smallBrand.ActualWidth,0),view).X<=smallSettings.TranslatePoint(new Point(),view).X);
        settings.PanelScale=1;settings.PanelMaxHeight=650;settings.PanelWidth=526;
        string originalHeadline=view.Shown!.Items[0].Title;
        foreach(string language in new[]{"en","es","id","ru","uk","ko"})
        {
            // Language/accessibility changes apply on restart in the product; configure
            // before constructing each fresh view to exercise the same startup state.
            settings.UiLanguage=language;Localization.Configure(settings);app.ChangeSettings();island.SelectPage("news",false);await Task.Delay(100);view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();
            var texts=Diagnostics.Descendants(view).OfType<TextBlock>().Select(t=>t.Text).ToArray();
            check("News controls localize in "+language+" without translating source headlines",Localization.Language==language && texts.Contains(Localization.Text("News summary")) && texts.Contains(originalHeadline) && texts.Contains(Localization.Format("Sources: {0}",view.Shown!.Items[0].SourceCount)));
            Capture("news-language-"+language);
            var original=view.Shown!;view.Render(original with{Items=new[]{original.Items[0] with{Title="Connect",Notes=new[]{"Disconnect"}}}});island.UpdateLayout();
            var probe=Diagnostics.Descendants(view).OfType<TextBlock>().Select(t=>t.Text).ToArray();
            check("Source text matching UI captions stays untranslated in "+language,probe.Contains("Connect") && probe.Contains("Disconnect"));
            view.Render(original);await view.Reload();await Wait();
        }
        settings.UiLanguage="en";Localization.Configure(settings);
        settings.ColorTheme="Light";app.ChangeSettings();island.SelectPage("news",false);await Task.Delay(120);view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();Capture("news-digest-light");
        check("News renders with the native Light theme",ThemeInk.IsLight && view.Shown?.Items.Length>0);
        settings.AccessibilityTextScale=1.5;Localization.Configure(settings);settings.PanelWidth=360;settings.ColorTheme="Dark";app.ChangeSettings();island.SelectPage("news",false);await Task.Delay(120);view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();Capture("news-large-text");
        var largeHeadline=Diagnostics.Descendants(view).OfType<TextBlock>().First(t=>t.Text==view.Shown!.Items[0].Title);
        var largeBrand=Diagnostics.Descendants(view).OfType<TextBlock>().Single(t=>t.Text=="by Anyway News");var largeSettings=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="News connection");
        check("Large news text uses expanding line boxes and keeps footer actions separate",largeHeadline.FontSize>=22 && largeHeadline.LineStackingStrategy==LineStackingStrategy.MaxHeight && largeBrand.TranslatePoint(new Point(largeBrand.ActualWidth,0),view).X<=largeSettings.TranslatePoint(new Point(),view).X);
        settings.AccessibilityHighContrast=true;Localization.Configure(settings);app.ChangeSettings();island.SelectPage("news",false);await Task.Delay(120);view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();Capture("news-high-contrast");
        var contrastButton=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="Refresh news");
        check("News action icons follow the native high contrast foreground",Diagnostics.Descendants(contrastButton).OfType<System.Windows.Shapes.Path>().All(p=>Equals(p.Fill,contrastButton.Foreground)));
        settings.AccessibilityHighContrast=false;settings.AccessibilityTextScale=1;Localization.Configure(settings);settings.ColorTheme="Light";
        settings.PanelMaxHeight=417;settings.PanelWidth=420;app.ChangeSettings();island.SelectPage("news",false);await Task.Delay(120);view=Diagnostics.Descendants(island).OfType<NewsView>().Single();await Wait();Capture("news-default-height");
        var brandDefault=Diagnostics.Descendants(view).OfType<Button>().Single(b=>UI.Identity(b)=="Open Anyway News");
        check("Anyway brand stays visible at the standard 417 DIP height",brandDefault.TransformToAncestor(island).TransformBounds(new Rect(brandDefault.RenderSize)).Bottom<=island.PaintedHeight-8);
        var heading=Diagnostics.Descendants(view).OfType<TextBlock>().First(t=>t.Text==view.Shown!.Items[0].Title);var bounds=heading.TransformToAncestor(island).TransformBounds(new Rect(heading.RenderSize));
        check("Main headline stays visible at the standard height",bounds.Top>=50 && bounds.Bottom<brandDefault.TransformToAncestor(island).Transform(new Point()).Y);
        check("News diagnostics did not enable microphone or capture",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing);
    }
}
