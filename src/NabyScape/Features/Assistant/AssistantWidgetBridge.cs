using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

// Stdio MCP adapts the running app. Only this Windows user's clients can
// connect, and the dispatcher accepts only the two bounded widget tools.
internal sealed class AssistantWidgetBridge : IDisposable
{
    private static string PipeName=>"NabyScape.WidgetPages."+(WindowsIdentity.GetCurrent().User?.Value??throw new IOException("Windows user identity is unavailable."));
    private readonly CancellationTokenSource shutdown=new();
    private readonly Func<JsonElement,CancellationToken,Task<JsonElement>> execute;
    private readonly Task worker;
    internal AssistantWidgetBridge(Func<JsonElement,CancellationToken,Task<JsonElement>> execute){this.execute=execute;worker=Task.Run(Serve);}
    private async Task Serve()
    {
        while(!shutdown.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(shutdown.Token);using var request=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);request.CancelAfter(TimeSpan.FromSeconds(8));
                using var reader=new StreamReader(pipe,new UTF8Encoding(false,true),false,4096,true);using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
                using var json=JsonDocument.Parse(await Line(reader,request.Token),new JsonDocumentOptions{MaxDepth=12});var result=await execute(json.RootElement.Clone(),request.Token);await writer.WriteLineAsync(result.GetRawText().AsMemory(),request.Token);
            }
            catch(OperationCanceledException) when(shutdown.IsCancellationRequested){break;}
            catch{if(!shutdown.IsCancellationRequested)try{await Task.Delay(150,shutdown.Token);}catch(OperationCanceledException){break;}}
        }
    }
    private static async Task<string> Line(StreamReader reader,CancellationToken token)
    {
        var line=new StringBuilder();char[] next=new char[1];
        while(await reader.ReadAsync(next.AsMemory(),token)>0){if(next[0]=='\n')return line.ToString();if(line.Length>=16384)throw new IOException("The tool request is too large.");line.Append(next[0]);}
        if(line.Length>0)return line.ToString();throw new EndOfStreamException();
    }
    private static async Task<JsonElement> Request(object body,CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await pipe.ConnectAsync(2500,deadline.Token);
        using var reader=new StreamReader(pipe,new UTF8Encoding(false,true),false,4096,true);using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
        string payload=JsonSerializer.Serialize(body);if(payload.Length>8192)throw new IOException("The tool arguments are too large.");await writer.WriteLineAsync(payload.AsMemory(),deadline.Token);
        using var result=JsonDocument.Parse(await Line(reader,deadline.Token),new JsonDocumentOptions{MaxDepth=16});return result.RootElement.Clone();
    }
    internal static async Task RunStdio()
    {
        using var input=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false,true));using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
        while(true)
        {
            string line;try{line=await Line(input,CancellationToken.None);}catch(EndOfStreamException){break;}catch{break;}
            JsonElement id=default;
            try
            {
                using var json=JsonDocument.Parse(line,new JsonDocumentOptions{MaxDepth=16});var root=json.RootElement;
                if(root.GetProperty("jsonrpc").GetString()!="2.0")throw new JsonException();
                if(!root.TryGetProperty("id",out id))continue;id=id.Clone();if(id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String) || id.GetRawText().Length>100){id=default;throw new JsonException();}
                string method=root.GetProperty("method").GetString()??"";object result;
                if(method=="initialize")
                {
                    string version=root.TryGetProperty("params",out var init) && init.TryGetProperty("protocolVersion",out var v)?v.GetString()??"":"";if(version is not ("2024-11-05" or "2025-03-26" or "2025-06-18"))version="2024-11-05";
                    result=new{protocolVersion=version,capabilities=new{tools=new{listChanged=false}},serverInfo=new{name="nabyscape",version=typeof(App).Assembly.GetName().Version?.ToString(3)??""}};
                }
                else if(method=="ping")result=new{};
                else if(method=="tools/list")result=await Request(new{operation="tools"},CancellationToken.None);
                else if(method=="tools/call")
                {
                    try{var args=root.GetProperty("params");var reply=await Request(new{operation="call",name=args.GetProperty("name").GetString(),arguments=args.TryGetProperty("arguments",out var a)?a:JsonSerializer.SerializeToElement(new{})},CancellationToken.None);bool error=reply.TryGetProperty("ok",out var ok) && ok.ValueKind==JsonValueKind.False;result=new{content=new[]{new{type="text",text=reply.GetRawText()}},isError=error};}
                    catch{result=new{content=new[]{new{type="text",text="Start NabyScape on this Windows account and enable AI widget navigation in Naby AI settings. The bridge does not launch the app or execute other actions."}},isError=true};}
                }
                else{await output.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id,error=new{code=-32601,message="Method not supported"}}));continue;}
                await output.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id,result}));
            }
            catch{await output.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id=id.ValueKind==JsonValueKind.Undefined?(object?)null:id,error=new{code=-32602,message="Invalid request, or NabyScape is not available. Start the app and enable AI widget navigation."}}));}
        }
    }
    public void Dispose(){shutdown.Cancel();_ =worker.ContinueWith(_=>shutdown.Dispose(),TaskScheduler.Default);}
}
