using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal sealed partial class AssistantTransport
{
    public async Task<AssistantReply> CompleteStreaming(AssistantProvider provider,string? apiKey,IReadOnlyList<AssistantMessage> messages,IReadOnlyList<AssistantTool> tools,Func<string,CancellationToken,Task> text,Action<string>? progress,CancellationToken cancellationToken)
    {
        if(provider.Protocol!="ChatCompletions" || provider.ImageOutput)
        {
            var reply=await Complete(provider,apiKey,messages,tools,cancellationToken);
            if(reply.Calls.Count==0 && reply.Text.Length>0)await text(reply.Text,cancellationToken);
            return reply;
        }
        cancellationToken.ThrowIfCancellationRequested();var endpoint=ValidateEndpoint(provider.BaseUrl);ValidateInput(provider,messages,tools);
        if(apiKey is {Length:>4096} || apiKey?.Any(char.IsControl)==true)throw new InvalidOperationException("The API key is invalid.");
        var activeTools=provider.Tools && provider.Backend!="Hermes"?tools:Array.Empty<AssistantTool>();var body=Build(provider,messages,activeTools);body["stream"]=true;
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(body);if(bytes.Length>MaximumBodyBytes)throw new IOException("The assistant request is too large.");
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(endpoint,"chat/completions")){Content=new ByteArrayContent(bytes)};
        request.Content.Headers.ContentType=new("application/json");request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));Authenticate(request,provider,apiKey);
        if(provider.Backend=="Hermes")
        {
            if(provider.HermesSessionId.Length>0){if(!Guid.TryParseExact(provider.HermesSessionId,"N",out _))throw new IOException("Clear chat to start a new Hermes session.");request.Headers.Add("X-Hermes-Session-Id","nabyscape-"+provider.HermesSessionId);}
            request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));
        }
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(provider.Backend=="Hermes" && timeout==TimeSpan.FromSeconds(90)?TimeSpan.FromMinutes(3):timeout);
        try
        {
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode)throw new IOException(await Failure(response,apiKey,deadline.Token).ConfigureAwait(false));
            if(response.Content.Headers.ContentLength>MaximumBodyBytes)throw new IOException("The provider response exceeded its limit.");
            if(!string.Equals(response.Content.Headers.ContentType?.MediaType,"text/event-stream",StringComparison.OrdinalIgnoreCase))
            {
                // Some compatible servers return JSON even when stream=true. Consume this
                // same response; never resend an agent request as an automatic fallback.
                var data=await ReadBounded(response,MaximumBodyBytes,deadline.Token);using var json=JsonDocument.Parse(data);var reply=Parse(provider,json.RootElement,activeTools);
                if(reply.Calls.Count==0 && reply.Text.Length>0)await text(reply.Text,deadline.Token);return reply;
            }
            await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var content=new StringBuilder();var calls=new SortedDictionary<int,(StringBuilder Id,StringBuilder Name,StringBuilder Args)>();string finish="";
            // Local tool turns can contain an introductory sentence followed by a tool
            // call. Buffer those turns until the final message is known to be text-only.
            bool early=activeTools.Count==0;long lastProgress=0;
            await foreach(var frame in SseFrames(stream,deadline.Token))
            {
                if(frame.Data=="[DONE]")break;
                if(frame.Event=="hermes.tool.progress"){long now=Environment.TickCount64;if(now-lastProgress>1000){lastProgress=now;progress?.Invoke("Hermes is working with tools…");}continue;}
                if(frame.Event=="error")throw new IOException("The provider stopped the streamed reply. Check its session before retrying.");
                if(frame.Event.Length>0 && frame.Event!="message")continue;
                using var json=JsonDocument.Parse(frame.Data,new JsonDocumentOptions{MaxDepth=48});var root=json.RootElement;
                if(root.TryGetProperty("error",out _) || root.TryGetProperty("failed",out var failed) && failed.ValueKind==JsonValueKind.True)throw new IOException("The provider stopped the streamed reply. Check its session before retrying.");
                if(!root.TryGetProperty("choices",out var choices) || choices.ValueKind!=JsonValueKind.Array)throw new IOException("Invalid voice stream.");
                if(choices.GetArrayLength()==0)continue;
                var choice=choices[0];string reason=Get(choice,"finish_reason");
                if(reason.Length>0){finish=reason;if(reason is not ("stop" or "tool_calls"))throw new IOException("The streamed reply was interrupted or incomplete. Check the provider session before retrying.");}
                if(!choice.TryGetProperty("delta",out var delta))continue;
                if(delta.TryGetProperty("tool_calls",out var toolCalls) && toolCalls.ValueKind==JsonValueKind.Array)
                {
                    foreach(var call in toolCalls.EnumerateArray())
                    {
                        if(!call.TryGetProperty("index",out var indexElement) || !indexElement.TryGetInt32(out int index) || index is <0 or >=8 || early)throw new IOException("Unexpected tool call in the voice stream.");
                        if(!calls.TryGetValue(index,out var part)){part=(new(),new(),new());calls.Add(index,part);}
                        part.Id.Append(Get(call,"id"));if(call.TryGetProperty("function",out var function)){part.Name.Append(Get(function,"name"));part.Args.Append(Get(function,"arguments"));}
                        if(part.Id.Length>128 || part.Name.Length>128 || part.Args.Length>16000)throw new IOException("The streamed tool call exceeded its limit.");
                    }
                }
                string chunk=Get(delta,"content");if(chunk.Length==0)continue;
                if(content.Length+chunk.Length>64000)throw new IOException("The spoken reply exceeded its text limit.");content.Append(chunk);
                // reasoning_content, progress frames and model internals are never spoken.
                if(early)await text(chunk,deadline.Token).ConfigureAwait(false);
            }
            if(finish.Length==0)throw new IOException("The voice stream ended before its completion status.");
            var message=new JsonObject{["content"]=content.ToString()};if(calls.Count>0){var values=new JsonArray();foreach(var part in calls.Values)values.Add(new JsonObject{["id"]=part.Id.ToString(),["type"]="function",["function"]=new JsonObject{["name"]=part.Name.ToString(),["arguments"]=part.Args.ToString()}});message["tool_calls"]=values;}
            var complete=new JsonObject{["choices"]=new JsonArray(new JsonObject{["finish_reason"]=finish,["message"]=message})};using var document=JsonDocument.Parse(complete.ToJsonString());var final=Parse(provider,document.RootElement,activeTools);
            if(!early && final.Calls.Count==0 && final.Text.Length>0)await text(final.Text,deadline.Token);return final;
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){throw new TimeoutException("The voice reply timed out. Check the provider session before retrying.");}
        catch(HttpRequestException){throw new IOException("The voice provider connection was interrupted.");}
        catch(JsonException){throw new IOException("The provider returned an invalid voice stream. Disable streaming in Voice settings for an incompatible endpoint.");}
    }

    private static async IAsyncEnumerable<(string Event,string Data)> SseFrames(Stream stream,[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        using var reader=new StreamReader(stream,new UTF8Encoding(false,true),false,2048,leaveOpen:true);char[] buffer=new char[2048];var line=new StringBuilder();var data=new StringBuilder();string kind="";int total=0,frames=0,count;
        while((count=await reader.ReadAsync(buffer,token))>0)
        {
            total+=count;if(total>4*1024*1024)throw new IOException("The voice stream exceeded its size limit.");
            for(int i=0;i<count;i++)
            {
                char c=buffer[i];if(c!='\n'){line.Append(c);if(line.Length>128*1024)throw new IOException("The voice stream line exceeded its limit.");continue;}
                string value=line.ToString().TrimEnd('\r');line.Clear();
                if(value.Length==0)
                {
                    if(data.Length>0){if(++frames>32768)throw new IOException("Too many voice stream events.");yield return(kind,data.ToString());data.Clear();}kind="";
                }
                else if(value.StartsWith("data:",StringComparison.Ordinal)){if(data.Length>0)data.Append('\n');data.Append(value.AsSpan(5).TrimStart(' '));if(data.Length>256*1024)throw new IOException("The voice stream event exceeded its limit.");}
                else if(value.StartsWith("event:",StringComparison.Ordinal)){kind=value[6..].Trim();if(kind.Length>128)throw new IOException("Invalid voice stream event.");}
            }
        }
        // A frame is only valid after its blank-line boundary. Truncated JSON is not
        // promoted into a completed answer on a dropped connection.
    }
}
