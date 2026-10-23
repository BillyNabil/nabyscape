using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    internal (string Id,string Name,string Widget)[] AssistantWidgetPages=>app.Settings.Widgets.Where(w=>w.Enabled).GroupBy(w=>PageFor(w.Id)).Select(g=>(g.Key,PageName(g.Key),g.First().Id)).ToArray();
    internal string AssistantNavigate(string requested)
    {
        var pages=AssistantWidgetPages;if(pages.Length==0)return JsonSerializer.Serialize(new{ok=false,error="No widget pages are enabled. Enable widgets in Settings first."});
        string page=requested.Trim().ToLowerInvariant();int index=Array.FindIndex(pages,p=>p.Id==activePage);
        if(page is "next" or "previous")index=index<0?(page=="next"?0:pages.Length-1):(index+(page=="next"?1:-1)+pages.Length)%pages.Length;
        else
        {
            string? widget=app.Settings.Widgets.FirstOrDefault(w=>w.Enabled && w.Id==page)?.Id;
            index=Array.FindIndex(pages,p=>p.Id==page || widget!=null && p.Id==PageFor(widget));
        }
        if(index<0)return JsonSerializer.Serialize(new{ok=false,error="That page is disabled or unavailable. Use naby_list_widget_pages for enabled pages."});
        var chosen=pages[index];
        if(!app.Settings.CompactLayout){LeaveAssistantPanel();FlushNotes();hoverPanelWidget=chosen.Widget;activePage=chosen.Id;BuildBody();Reveal();scroll.ScrollToTop();}
        else OpenFeaturePage(chosen.Widget);
        return JsonSerializer.Serialize(new{ok=expanded && !assistantOpen && activePage==chosen.Id,page=chosen.Id,title=chosen.Name,message="Opened the widget page. Its contents were not changed."});
    }
}

public sealed partial class App
{
    internal object[] AssistantWidgetPages()=>overlay?.AssistantWidgetPages.Select(p=>(object)new{id=p.Id,title=p.Name}).ToArray()??Array.Empty<object>();
    internal string[] AssistantWidgetPageIds()=>overlay?.AssistantWidgetPages.Select(p=>p.Id).Concat(Settings.Widgets.Where(w=>w.Enabled).Select(w=>w.Id)).Append("next").Append("previous").Distinct().ToArray()??new[]{"next","previous"};
    internal string AssistantCurrentPage=>overlay?.AssistantOpen==true?"chat":overlay?.ActivePage??"unavailable";
    internal string NavigateAssistantWidget(string page)=>overlay?.AssistantNavigate(page)??JsonSerializer.Serialize(new{ok=false,error="The island is unavailable."});
}

internal sealed partial class AssistantRuntime
{
    internal IReadOnlyList<AssistantTool> WidgetTools()
    {
        if(disposed || !Options.Enabled || !Options.WidgetNavigation)return Array.Empty<AssistantTool>();
        return new[]{new AssistantTool("naby_list_widget_pages","List enabled NabyScape widget pages and the current page. Read-only; does not inspect widget contents.",JsonSerializer.SerializeToElement(new{type="object",properties=new{},additionalProperties=false})),
            new AssistantTool("naby_navigate_widget","Open an enabled NabyScape widget page, or move to the next/previous page. Only changes the visible page; never starts a timer, recording, playback, app or computer action.",JsonSerializer.SerializeToElement(new{type="object",properties=new{page=new{type="string",@enum=app.AssistantWidgetPageIds()}},required=new[]{"page"},additionalProperties=false}))};
    }
    private IReadOnlyList<AssistantTool> ChatWidgetTools(AssistantProvider provider)
    {var tools=provider.Backend=="Hermes"?Array.Empty<AssistantTool>():WidgetTools();provider.Tools=tools.Count>0;return tools;}
    internal async Task<string> ExecuteWidgetTool(AssistantCall call,CancellationToken token)
    {
        if(!app.Dispatcher.CheckAccess())return await app.Dispatcher.InvokeAsync(()=>ExecuteWidgetTool(call,token),System.Windows.Threading.DispatcherPriority.Normal,token).Task.Unwrap();
        token.ThrowIfCancellationRequested();if(disposed || !Options.Enabled || !Options.WidgetNavigation)return JsonSerializer.Serialize(new{ok=false,error="AI widget navigation is off. Enable it in Naby AI settings."});
        if(call.Arguments.Length>4096)return JsonSerializer.Serialize(new{ok=false,error="Tool arguments are too large."});
        try
        {
            using var json=JsonDocument.Parse(call.Arguments,new JsonDocumentOptions{MaxDepth=4});var args=json.RootElement;
            if(args.ValueKind!=JsonValueKind.Object)throw new JsonException();
            if(call.Name=="naby_list_widget_pages" && !args.EnumerateObject().Any())return JsonSerializer.Serialize(new{ok=true,current=app.AssistantCurrentPage,pages=app.AssistantWidgetPages()});
            if(call.Name!="naby_navigate_widget" || args.EnumerateObject().Count()!=1 || !args.TryGetProperty("page",out var page) || page.ValueKind!=JsonValueKind.String || page.GetString() is not {Length:>0 and <=64} target)throw new JsonException();
            token.ThrowIfCancellationRequested();string result=app.NavigateAssistantWidget(target);Status="Widget navigation · "+target;Notify();return result;
        }
        catch(JsonException){return JsonSerializer.Serialize(new{ok=false,error="Use an offered widget tool with the documented arguments."});}
    }
    private async Task RealtimeWidgetTool(IAssistantVoice session,AssistantCall call)
    {
        try{if(!ReferenceEquals(voice,session))return;var token=session.CallToken(call.Id);string result=await ExecuteWidgetTool(call,token);if(!ReferenceEquals(voice,session))return;await session.SendToolResult(call,result,token);}
        catch(OperationCanceledException){}
        catch(Exception){if(ReferenceEquals(voice,session)){Status="Widget navigation could not return its result. Check the visible page before retrying.";Notify();}}
    }
}
