using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;

internal sealed partial class AssistantTransport
{
    internal async Task<string[]> Models(AssistantProvider provider,string? apiKey,CancellationToken token)
    {
        var endpoint=ValidateEndpoint(provider.BaseUrl);var result=new HashSet<string>(StringComparer.Ordinal);
        string route="models",cursor="";
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(30));
        for(int page=0;page<20;page++)
        {
            string query=provider.Protocol=="Gemini"?(cursor.Length>0?"?pageSize=1000&pageToken="+Uri.EscapeDataString(cursor):"?pageSize=1000"):
                provider.Protocol=="Anthropic"?(cursor.Length>0?"?limit=100&after_id="+Uri.EscapeDataString(cursor):"?limit=100"):"";
            using var request=new HttpRequestMessage(HttpMethod.Get,new Uri(endpoint,route+query));Authenticate(request,provider,apiKey);
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(!response.IsSuccessStatusCode)throw new IOException(await Failure(response,apiKey,deadline.Token));
            var bytes=await ReadBounded(response,4*1024*1024,deadline.Token);using var document=JsonDocument.Parse(bytes);
            var root=document.RootElement;
            if(!root.TryGetProperty(provider.Protocol=="Gemini"?"models":"data",out var items) || items.ValueKind!=JsonValueKind.Array)
                throw new IOException("This provider does not expose a compatible model list. Paste the model ID manually.");
            foreach(var item in items.EnumerateArray())
            {
                if(!item.TryGetProperty(provider.Protocol=="Gemini"?"name":"id",out var id) || id.ValueKind!=JsonValueKind.String)continue;
                string value=id.GetString()??"";if(provider.Protocol=="Gemini" && value.StartsWith("models/"))value=value[7..];
                if(value.Length is >0 and <=160 && !value.Any(char.IsControl))result.Add(value);
            }
            if(result.Count>5000)break;
            string next=provider.Protocol=="Gemini" && root.TryGetProperty("nextPageToken",out var continuation)?continuation.GetString()??"":
                provider.Protocol=="Anthropic" && root.TryGetProperty("has_more",out var more) && more.ValueKind==JsonValueKind.True && root.TryGetProperty("last_id",out var last)?last.GetString()??"":"";
            if(next.Length==0 || next==cursor)break;cursor=next;
        }
        if(result.Count==0)throw new IOException("The provider returned no models. Check your key and permissions, or enter a model ID manually.");
        return result.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static void Authenticate(HttpRequestMessage request,AssistantProvider provider,string? key)
    {
        if(key?.Length>4096 || key?.Any(char.IsControl)==true)throw new InvalidOperationException("The API key is invalid.");
        if(!string.IsNullOrWhiteSpace(key))
        {
            if(provider.Protocol=="Anthropic")request.Headers.Add("x-api-key",key);
            else if(provider.Protocol=="Gemini")request.Headers.Add("x-goog-api-key",key);
            else request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        }
        if(provider.Protocol=="Anthropic")request.Headers.Add("anthropic-version","2023-06-01");
    }
    internal static async Task<byte[]> ReadBounded(HttpResponseMessage response,int maximum,CancellationToken token)
    {
        if(response.Content.Headers.ContentLength>maximum)throw new IOException("The provider response exceeded the size limit.");
        await using var source=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();byte[] chunk=new byte[16384];int count;
        while((count=await source.ReadAsync(chunk,token))>0){if(output.Length+count>maximum)throw new IOException("The provider response exceeded the size limit.");output.Write(chunk,0,count);}
        return output.ToArray();
    }
    internal static async Task<string> Failure(HttpResponseMessage response,string? key,CancellationToken token)
    {
        int code=(int)response.StatusCode;
        string hint=code switch{401=>"API key missing, invalid or expired.",403=>"Your key does not have permission for this model.",404=>"Check the API base URL and model ID.",429=>"Rate limit or quota reached. Check your provider balance.",>=500=>"The provider is temporarily unavailable.",>=300 and <400=>"Redirects are blocked; enter the final provider URL.",_=>"Check the protocol, model and request support."};
        string detail="";
        try
        {
            using var json=JsonDocument.Parse(await ReadBounded(response,32768,token));var root=json.RootElement;
            if(root.TryGetProperty("error",out var error))detail=error.ValueKind==JsonValueKind.String?error.GetString()??"":error.TryGetProperty("message",out var msg)?msg.GetString()??"":"";
            else if(root.TryGetProperty("detail",out var other) && other.ValueKind==JsonValueKind.String)detail=other.GetString()??"";
        }
        catch(Exception ex) when(ex is JsonException or IOException or InvalidOperationException){}
        if(!string.IsNullOrEmpty(key))detail=detail.Replace(key,"[key hidden]",StringComparison.Ordinal);
        detail=Regex.Replace(detail,@"(?i)(sk-|bearer\s+)[a-z0-9_\-\.]{8,}","[key hidden]",RegexOptions.None,TimeSpan.FromMilliseconds(100));
        detail=new string(detail.Where(c=>!char.IsControl(c) || c==' ').Take(360).ToArray());
        return $"HTTP {code}: {hint}"+(detail.Length>0?" Provider: "+detail:"");
    }
}
