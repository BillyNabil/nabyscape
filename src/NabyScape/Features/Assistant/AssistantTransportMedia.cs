using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal sealed partial class AssistantTransport
{
    internal static IEnumerable<JsonObject> AttachmentParts(AssistantProvider provider,AssistantAttachment file)
    {
        if(file.Text!=null){yield return PartText(provider,$"Attached document: {file.Name}\n{file.Note}\n<attachment>\n{file.Text}\n</attachment>");yield break;}
        if(file.IsImage)
        {
            if(!provider.Vision)throw new InvalidOperationException("Enable Image input in AI settings for a model that supports images.");
            yield return PartImage(provider,file.Source);yield break;
        }
        bool video=file.Mime.StartsWith("video/",StringComparison.Ordinal);
        bool pdf=file.Mime=="application/pdf";
        if(provider.Protocol=="Gemini")
        {
            if((pdf || video) && !provider.Vision)throw new InvalidOperationException("Enable Image input for this multimodal model before sending PDFs or video.");
            yield return new JsonObject{["inlineData"]=new JsonObject{["mimeType"]=file.Mime,["data"]=Inline(file.Source,file.Mime)}};yield break;
        }
        if(video || pdf && provider.Backend=="Hermes")
        {
            if(!provider.Vision)throw new InvalidOperationException("This provider receives PDF pages and video frames as images. Enable Image input for a compatible model.");
            if(file.Frames is not {Count:>0})throw new InvalidOperationException("Windows could not create video frames. Use a Gemini video model, or attach a supported MP4 video.");
            yield return PartText(provider,$"Attached {file.Name}. {file.Note}. These images are ordered pages or evenly sampled frames; do not claim to have read unseen pages, full motion or audio.");
            foreach(string frame in file.Frames)yield return PartImage(provider,frame);yield break;
        }
        if(!pdf)throw new InvalidOperationException("Audio files require a Gemini model with audio input. Choose that provider or attach a transcript.");
        if(!provider.Vision)throw new InvalidOperationException("Enable Image input for a PDF-capable model before sending PDFs.");
        Inline(file.Source,file.Mime);
        yield return provider.Protocol switch
        {
            "Responses"=>new JsonObject{["type"]="input_file",["filename"]=file.Name,["file_data"]=file.Source},
            "Anthropic"=>new JsonObject{["type"]="document",["title"]=file.Name,["source"]=new JsonObject{["type"]="base64",["media_type"]="application/pdf",["data"]=Inline(file.Source,file.Mime)}},
            _=>new JsonObject{["type"]="file",["file"]=new JsonObject{["filename"]=file.Name,["file_data"]=file.Source}}
        };
    }
    private static JsonObject PartText(AssistantProvider provider,string text)=>provider.Protocol=="Gemini"?new JsonObject{["text"]=text}:Text(provider.Protocol=="Responses"?"input_text":"text",text);
    private static JsonObject PartImage(AssistantProvider provider,string source)
    {
        var image=Image(source);
        return provider.Protocol switch
        {
            "Responses"=>new JsonObject{["type"]="input_image",["image_url"]=source},
            "Anthropic"=>new JsonObject{["type"]="image",["source"]=new JsonObject{["type"]="base64",["media_type"]=image.Mime,["data"]=image.Data}},
            "Gemini"=>new JsonObject{["inlineData"]=new JsonObject{["mimeType"]=image.Mime,["data"]=image.Data}},
            _=>new JsonObject{["type"]="image_url",["image_url"]=new JsonObject{["url"]=source}}
        };
    }
    private static string Inline(string source,string mime)
    {
        string prefix="data:"+mime+";base64,";
        if(!source.StartsWith(prefix,StringComparison.Ordinal) || source.Length<=prefix.Length || source.Length>AssistantAttachments.MaximumPayload)throw new InvalidOperationException("Invalid or oversized attachment data.");
        if(!AssistantBase64.IsValid(source.AsSpan(prefix.Length)))throw new InvalidOperationException("Invalid attachment encoding.");return source[prefix.Length..];
    }
    internal static void CheckAttachments(AssistantProvider provider,IReadOnlyList<AssistantAttachment>? files)
    {
        if(files==null)return;
        if(files.Count>AssistantAttachments.MaximumFiles || files.Sum(f=>f.Weight)>AssistantAttachments.MaximumPayload)throw new InvalidOperationException("Attach up to 6 files and 24 MB of prepared content per message.");
        if(files.Sum(f=>f.Text?.Length??0)>100000)throw new InvalidOperationException("Attached documents exceed 100,000 text characters in total. Split them into separate messages.");
        foreach(var file in files)
        {
            if(file.Name.Length>180 || file.Text?.Length>AssistantAttachments.MaximumFileText || file.Name.Any(char.IsControl))throw new InvalidOperationException("The attachment name or text is too long.");
            foreach(var _ in AttachmentParts(provider,file)){ }
        }
    }
    private static readonly Regex mediaMarkdown=new(@"!\[(?<name>[^\]\r\n]{0,180})\]\(<?(?<url>data:image/[a-zA-Z0-9.+-]+;base64,[a-zA-Z0-9+/=]+|https?://[^\s)<>]+)>?(?:\s+""[^""\r\n]*"")?\)",RegexOptions.Compiled,TimeSpan.FromMilliseconds(250));
    private static string ExtractImages(string text,AssistantProvider provider,List<AssistantAttachment> files)
    {
        try
        {
            string result=mediaMarkdown.Replace(text,m=>{int count=files.Count;AddMedia(files,provider,m.Groups["name"].Value,"image/png",m.Groups["url"].Value);return files.Count>count || files.Any(f=>f.Source==m.Groups["url"].Value)?"":"[Image unavailable: the returned address is not accessible from this app.]";});
            result=Regex.Replace(result,@"(?m)^\s*MEDIA:\s*(?<url>https?://[^\s]+)\s*$",m=>{AddMedia(files,provider,"Agent image","image/png",m.Groups["url"].Value);return "";},RegexOptions.None,TimeSpan.FromMilliseconds(100));
            return Regex.Replace(result,@"!\[[^\]\r\n]*\]\((?:/|file:|[A-Za-z]:[\\/])[^)\r\n]+\)|(?m)^\s*MEDIA:\s*(?:/|file:|[A-Za-z]:[\\/])[^\r\n]+", "[Image unavailable: the agent returned a server-local file path. Ask it to return image data or an accessible image URL.]",RegexOptions.None,TimeSpan.FromMilliseconds(100));
        }
        catch(RegexMatchTimeoutException){throw new IOException("The media reply is too complex to display.");}
    }
    private static void AddMedia(List<AssistantAttachment> files,AssistantProvider provider,string name,string mime,string source)
    {
        if(source.Length==0 || files.Any(f=>f.Source==source))return;
        if(files.Count>=12 || source.Length>AssistantAttachments.MaximumPayload || files.Sum(f=>f.Weight)+source.Length>AssistantAttachments.MaximumPayload)throw new JsonException();
        if(source.StartsWith("data:",StringComparison.Ordinal))
        {
            int split=source.IndexOf(';');if(split<5 || split>100)throw new JsonException();mime=source[5..split];
            if(!mime.StartsWith("image/",StringComparison.Ordinal))throw new JsonException();Inline(source,mime);
        }
        else
        {
            try{MediaUri(source,provider.BaseUrl);}catch(InvalidOperationException){return;}
            if(mime.StartsWith("image/",StringComparison.Ordinal)){string extension=Path.GetExtension(new Uri(source).AbsolutePath).ToLowerInvariant();mime=extension switch{".jpg" or ".jpeg"=>"image/jpeg",".gif"=>"image/gif",".webp"=>"image/webp",_=>mime};}
        }
        name=new string(name.Where(c=>!char.IsControl(c)).Take(180).ToArray());if(name.Length==0)name="Image "+(files.Count+1);
        files.Add(new(name,mime,source,Note:"From "+provider.Name,ProviderId:provider.Id,Origin:provider.BaseUrl));
    }
    private static bool ReadMedia(JsonElement part,AssistantProvider provider,List<AssistantAttachment> files)
    {
        if(part.ValueKind==JsonValueKind.String){AddMedia(files,provider,"","image/png",part.GetString()!);return true;}
        if(part.ValueKind!=JsonValueKind.Object)return false;
        string type=Get(part,"type"),name=Get(part,"filename");
        if(part.TryGetProperty("inlineData",out var inline) || part.TryGetProperty("inline_data",out inline))
        {string mime=Get(inline,"mimeType");if(mime.Length==0)mime=Get(inline,"mime_type");if(!mime.StartsWith("image/",StringComparison.Ordinal))return false;AddMedia(files,provider,name,mime,"data:"+mime+";base64,"+Get(inline,"data"));return true;}
        if(type=="image_generation_call")
        {string format=Get(part,"output_format");string mime=format=="jpeg"?"image/jpeg":format=="webp"?"image/webp":"image/png";AddMedia(files,provider,name,mime,"data:"+mime+";base64,"+Get(part,"result"));return true;}
        if(part.TryGetProperty("image_url",out var image))
        {AddMedia(files,provider,name,"image/png",image.ValueKind==JsonValueKind.String?image.GetString()!:Get(image,"url"));return true;}
        if(type is "image" or "output_image" or "image_url")
        {
            string url=Get(part,"url");
            if(part.TryGetProperty("source",out var source)){string mime=Get(source,"media_type");if(Get(source,"type")=="base64")url="data:"+mime+";base64,"+Get(source,"data");else url=Get(source,"url");}
            if(url.Length==0 && Get(part,"b64_json") is {Length:>0} data)url="data:image/png;base64,"+data;
            AddMedia(files,provider,name,"image/png",url);return true;
        }
        return false;
    }
    internal static Uri MediaUri(string source,string origin)
    {
        if(source.Length>8192 || !Uri.TryCreate(source,UriKind.Absolute,out var uri) || uri.UserInfo.Length>0 || uri.Fragment.Length>0)throw new InvalidOperationException("Invalid image address.");
        var clean=new UriBuilder(uri){Query="",Fragment=""};ValidateEndpoint(clean.Uri.AbsoluteUri);
        if(IsLocalHost(uri.Host) && (!Uri.TryCreate(origin,UriKind.Absolute,out var allowed) || allowed.Scheme!=uri.Scheme || allowed.Host!=uri.Host || allowed.Port!=uri.Port))throw new InvalidOperationException("Local media must belong to the connected provider.");
        return uri;
    }
    internal async Task<byte[]> DownloadMedia(AssistantAttachment file,AssistantProvider? provider,string? key,CancellationToken token)
    {
        if(file.Source.StartsWith("data:",StringComparison.Ordinal))return AssistantAttachments.Bytes(file.Source);
        var uri=MediaUri(file.Source,file.Origin);using var request=new HttpRequestMessage(HttpMethod.Get,uri);
        // Never forward a provider credential to a different origin or outside its API path.
        if(provider!=null && provider.Id==file.ProviderId && provider.BaseUrl==file.Origin)
        {var basis=ValidateEndpoint(provider.BaseUrl);if(basis.IsBaseOf(uri))Authenticate(request,provider,key);}
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);response.EnsureSuccessStatusCode();
        return await ReadBounded(response,AssistantAttachments.MaximumFileBytes,timeout.Token);
    }
}
