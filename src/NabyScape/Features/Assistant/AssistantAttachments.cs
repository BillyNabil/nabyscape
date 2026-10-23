using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using Windows.Data.Pdf;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace NabyScape;

// Attachments stay in session memory. No file is executed or uploaded before Send.
internal static class AssistantAttachments
{
    internal const int MaximumFiles=6,MaximumFileBytes=16*1024*1024,MaximumPayload=24*1024*1024,MaximumFileText=60000;
    internal static string Data(string mime,byte[] bytes)=>"data:"+mime+";base64,"+Convert.ToBase64String(bytes);
    internal static byte[] Bytes(string source)
    {
        int comma=source.IndexOf(',');
        if(!source.StartsWith("data:",StringComparison.Ordinal) || comma<0 || comma>150 || source.Length>MaximumPayload)throw new IOException("Invalid or oversized inline file.");
        return Convert.FromBase64String(source[(comma+1)..]);
    }
    internal static BitmapSource Thumbnail(byte[] bytes,int width=480)
    {
        using var stream=new MemoryStream(bytes);var header=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnDemand).Frames[0];
        int w=header.PixelWidth,h=header.PixelHeight;if(w<=0 || h<=0 || (long)w*h>80000000 || Math.Max(w,h)>Math.Min(w,h)*30L)throw new IOException("Image dimensions are too large to preview safely.");stream.Position=0;
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;if(w>=h)bitmap.DecodePixelWidth=Math.Min(width,w);else bitmap.DecodePixelHeight=Math.Min(width,h);bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    private static string Picture(byte[] bytes,int width=1400)
    {
        var image=Thumbnail(bytes,width);var encoder=new JpegBitmapEncoder{QualityLevel=82};encoder.Frames.Add(BitmapFrame.Create(image));using var output=new MemoryStream();encoder.Save(output);
        if(output.Length>2*1024*1024)throw new IOException("This image is too complex. Resize it before attaching.");
        return Data("image/jpeg",output.ToArray());
    }
    internal static AssistantAttachment Pasted(BitmapSource bitmap)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);if(output.Length>MaximumFileBytes)throw new IOException("The pasted image exceeds 16 MB.");
        return new("Pasted image.jpg","image/jpeg",Picture(output.ToArray()),Note:"Image · optimized for chat");
    }
    internal static AssistantAttachment EncodedImage(byte[] bytes)
    {if(bytes.Length is <=0 or >MaximumFileBytes)throw new IOException("Choose a clipboard image up to 16 MB.");return new("Pasted image.jpg","image/jpeg",Picture(bytes),Note:"Pasted image · optimized for chat");}
    internal static Task<AssistantAttachment> Read(string path,CancellationToken token)=>Task.Run(async()=>
    {
        var info=new FileInfo(path);if(!info.Exists || info.Length is <=0 or >MaximumFileBytes)throw new IOException("Choose a nonempty file up to 16 MB.");
        string name=Path.GetFileName(path);if(name.Length>160)name=name[..160];name=new string(name.Where(c=>!char.IsControl(c)).ToArray());
        byte[] bytes;
        await using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true))
        {
            using var output=new MemoryStream();var buffer=new byte[81920];int count;
            while((count=await input.ReadAsync(buffer,token))>0){if(output.Length+count>MaximumFileBytes)throw new IOException("The file exceeds 16 MB.");output.Write(buffer,0,count);}bytes=output.ToArray();
        }
        token.ThrowIfCancellationRequested();string extension=Path.GetExtension(path).ToLowerInvariant();
        if(new[]{".png",".jpg",".jpeg",".gif",".bmp",".tif",".tiff",".webp"}.Contains(extension))
            return new AssistantAttachment(Path.GetFileNameWithoutExtension(name)+".jpg","image/jpeg",Picture(bytes),Note:"Image · optimized for chat");
        if(extension==".pdf")
        {
            using var source=new MemoryStream(bytes);using var random=source.AsRandomAccessStream();var pdf=await PdfDocument.LoadFromStreamAsync(random).AsTask(token);var pages=new List<string>();
            for(uint i=0;i<Math.Min(pdf.PageCount,12u);i++)
            {
                token.ThrowIfCancellationRequested();using var page=pdf.GetPage(i);using var stream=new InMemoryRandomAccessStream();
                double w=page.Size.Width,h=page.Size.Height;if(!double.IsFinite(w) || !double.IsFinite(h) || w<=0 || h<=0 || Math.Max(w,h)>Math.Min(w,h)*30)throw new IOException("The PDF contains an unsupported page size.");
                double scale=1600/Math.Max(w,h);await page.RenderToStreamAsync(stream,new PdfPageRenderOptions{DestinationWidth=(uint)Math.Max(1,w*scale),DestinationHeight=(uint)Math.Max(1,h*scale)}).AsTask(token);stream.Seek(0);using var reader=stream.AsStreamForRead();using var png=new MemoryStream();await reader.CopyToAsync(png,token);pages.Add(Picture(png.ToArray(),1600));
            }
            return new AssistantAttachment(name,"application/pdf",Data("application/pdf",bytes),Frames:pages,Note:$"PDF · {pdf.PageCount} pages · Hermes sees first {pages.Count} pages");
        }
        string video=extension switch{".mp4"=>"video/mp4",".webm"=>"video/webm",".mov"=>"video/quicktime",".avi"=>"video/x-msvideo",".mkv"=>"video/x-matroska",_=>""};
        if(video.Length>0)
        {
            var frames=new List<string>();string note="Video · original file for Gemini";
            try
            {
                var file=await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask(token);var clip=await MediaClip.CreateFromFileAsync(file).AsTask(token);var composition=new MediaComposition();composition.Clips.Add(clip);
                double seconds=clip.OriginalDuration.TotalSeconds;if(!double.IsFinite(seconds) || seconds<=0)throw new IOException("Invalid video duration.");
                var encoding=clip.GetVideoEncodingProperties();double w=encoding.Width,h=encoding.Height;if(w<=0 || h<=0 || Math.Max(w,h)>Math.Min(w,h)*30)throw new IOException("Unsupported video dimensions.");double scale=960/Math.Max(w,h);
                for(int i=0;i<8;i++){token.ThrowIfCancellationRequested();using var frame=await composition.GetThumbnailAsync(TimeSpan.FromSeconds(seconds*(i+.5)/8),Math.Max(1,(int)(w*scale)),Math.Max(1,(int)(h*scale)),VideoFramePrecision.NearestFrame).AsTask(token);using var stream=frame.AsStreamForRead();using var image=new MemoryStream();await stream.CopyToAsync(image,token);frames.Add(Picture(image.ToArray(),960));}composition.Clips.Clear();
                note=$"Video · {Math.Ceiling(seconds)} seconds · 8 sampled frames, no audio outside Gemini";
            }
            catch(OperationCanceledException){throw;}
            catch{frames.Clear();note="Video · original file for Gemini; Windows could not create preview frames";}
            return new AssistantAttachment(name,video,Data(video,bytes),Frames:frames,Note:note);
        }
        string audio=extension switch{".mp3"=>"audio/mpeg",".wav"=>"audio/wav",".m4a"=>"audio/mp4",".ogg"=>"audio/ogg",".flac"=>"audio/flac",_=>""};
        if(audio.Length>0)return new AssistantAttachment(name,audio,Data(audio,bytes),Note:"Audio · requires Gemini audio input");
        string? text=null;
        if(new[]{".docx",".pptx",".xlsx",".odt"}.Contains(extension))text=DocumentText(bytes,extension);
        else if(new[]{".txt",".md",".csv",".tsv",".json",".jsonl",".xml",".html",".htm",".css",".js",".ts",".tsx",".jsx",".py",".cs",".cpp",".c",".h",".java",".rs",".go",".sql",".yaml",".yml",".toml",".ini",".log",".srt",".vtt",".sh",".ps1"}.Contains(extension))
        {if(bytes.Length>256*1024)throw new IOException("This text file is too large. Split it into smaller files.");using var stream=new MemoryStream(bytes);using var reader=new StreamReader(stream,new UTF8Encoding(false,true),true);text=reader.ReadToEnd();}
        if(text==null)throw new IOException("This file type is not supported. Attach an image, PDF, video, audio, text, code, DOCX, PPTX, XLSX or ODT file.");
        if(text.Length>MaximumFileText)throw new IOException("This document exceeds 60,000 text characters. Split it into smaller files.");
        if(text.Any(c=>c=='\0'))throw new IOException("This file does not contain supported text.");
        return new AssistantAttachment(name,"text/plain","",text,Note:"Document · extracted text; embedded media and formatting are not included");
    },token);
    private static string DocumentText(byte[] bytes,string extension)
    {
        using var input=new MemoryStream(bytes);using var zip=new ZipArchive(input,ZipArchiveMode.Read);if(zip.Entries.Count>2000)throw new IOException("This document contains too many embedded items.");var result=new StringBuilder();long expanded=0;
        var ordering=Comparer<string>.Create((a,b)=>System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare(a,b,System.Globalization.CompareOptions.NumericOrdering));
        var entries=zip.Entries.Where(e=>extension switch{".docx"=>e.FullName=="word/document.xml",".pptx"=>e.FullName.StartsWith("ppt/slides/slide",StringComparison.Ordinal) && e.FullName.EndsWith(".xml",StringComparison.Ordinal),".xlsx"=>e.FullName.StartsWith("xl/worksheets/sheet",StringComparison.Ordinal) && e.FullName.EndsWith(".xml",StringComparison.Ordinal),_=>e.FullName=="content.xml"}).OrderBy(e=>e.FullName,ordering).ToArray();
        if(entries.Length>200)throw new IOException("The document contains too many sections. Split it into smaller files.");
        XDocument Xml(ZipArchiveEntry entry)
        {expanded+=entry.Length;if(expanded>4*1024*1024)throw new IOException("The expanded document is too large.");using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4*1024*1024});return XDocument.Load(reader);}
        string[] shared=Array.Empty<string>();if(extension==".xlsx" && zip.GetEntry("xl/sharedStrings.xml") is { } strings)shared=Xml(strings).Descendants().Where(n=>n.Name.LocalName=="si").Select(n=>string.Concat(n.Descendants().Where(t=>t.Name.LocalName=="t").Select(t=>t.Value))).ToArray();
        foreach(var entry in entries)
        {
            var xml=Xml(entry);
            if(extension==".xlsx")
            {
                result.AppendLine(entry.FullName);
                foreach(var row in xml.Descendants().Where(n=>n.Name.LocalName=="row"))
                {
                    foreach(var cell in row.Elements().Where(n=>n.Name.LocalName=="c")){string value=cell.Descendants().FirstOrDefault(n=>n.Name.LocalName=="v")?.Value??string.Concat(cell.Descendants().Where(n=>n.Name.LocalName=="t").Select(n=>n.Value));if((string?)cell.Attribute("t")=="s" && int.TryParse(value,out int index) && index>=0 && index<shared.Length)value=shared[index];result.Append((string?)cell.Attribute("r")).Append(": ").Append(value).Append('\t');}result.AppendLine();
                }
            }
            else foreach(var paragraph in xml.Descendants().Where(n=>n.Name.LocalName=="p" || extension==".odt" && n.Name.LocalName=="h")){result.AppendLine(string.Concat(paragraph.DescendantNodes().OfType<XText>().Select(n=>n.Value)));}
            if(result.Length>MaximumFileText)throw new IOException("This document exceeds 60,000 text characters.");
            result.AppendLine();
        }
        if(result.Length==0)throw new IOException("No readable text was found in this document.");return result.ToString();
    }
}
