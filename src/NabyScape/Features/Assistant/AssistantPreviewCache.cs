using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NabyScape;

// Session-scoped, frozen display pixels only. Original attachments never enter this cache.
internal sealed class AssistantPreviewCache
{
    internal const int MaximumCount=12;
    internal const long MaximumBytes=8*1024*1024;
    internal static readonly TimeSpan IdleAge=TimeSpan.FromSeconds(30);
    private static readonly ConditionalWeakTable<AssistantRuntime,AssistantPreviewCache> sessions=new();
    private readonly ConditionalWeakTable<AssistantAttachment,Identity> identities=new();
    private readonly Dictionary<(long,int),Entry> entries=new();
    private readonly Dictionary<(long,int),Flight> flights=new();
    private readonly SemaphoreSlim decoders=new(2);
    private readonly object gate=new();
    private long nextId,bytes;
    private sealed class Identity {internal long Id;}
    private sealed record Entry(BitmapSource Image,long Bytes,DateTime Used);
    private sealed class Flight
    {
        internal readonly CancellationTokenSource Cancellation=new();
        internal Task<BitmapSource> Task=null!;
        internal int Waiters;
    }
    internal static AssistantPreviewCache For(AssistantRuntime runtime)=>sessions.GetValue(runtime,_=>new());
    internal (int Count,long Bytes,int Pending) Snapshot {get{lock(gate)return(entries.Count,bytes,flights.Count);}}
    internal async Task<BitmapSource> Get(AssistantAttachment file,int pixels,Func<CancellationToken,Task<byte[]>> read,bool local,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Remote reads always go through Runtime.Media's current provider/endpoint checks.
        // Never satisfy a remote request from a retained cache or an earlier authorization.
        if(!local)return await Decode(file,read,pixels,token).ConfigureAwait(false);
        (long,int) key;Flight flight;
        lock(gate)
        {
            key=(identities.GetValue(file,_=>new(){Id=Interlocked.Increment(ref nextId)}).Id,pixels);
            if(entries.TryGetValue(key,out var hit)){entries[key]=hit with{Used=DateTime.UtcNow};return hit.Image;}
            if(!flights.TryGetValue(key,out flight!))
            {
                flight=new();flights.Add(key,flight);
                var work=flight;
                flight.Task=Task.Run(async()=>
                {
                    try
                    {
                        var image=await Decode(file,read,pixels,work.Cancellation.Token).ConfigureAwait(false);
                        lock(gate)
                        {
                            work.Cancellation.Token.ThrowIfCancellationRequested();
                            long weight=(long)image.PixelWidth*image.PixelHeight*Math.Max(4,(image.Format.BitsPerPixel+7)/8);
                            if(weight<=MaximumBytes)
                            {
                                entries[key]=new(image,weight,DateTime.UtcNow);bytes+=weight;Trim(DateTime.UtcNow);
                            }
                        }
                        return image;
                    }
                    finally
                    {
                        lock(gate)
                        {
                            if(flights.TryGetValue(key,out var current) && ReferenceEquals(current,work))flights.Remove(key);
                            if(work.Waiters==0)work.Cancellation.Dispose();
                        }
                    }
                });
                // Observe cancellation/failure even if all visual consumers have gone away.
                _=flight.Task.ContinueWith(t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            }
            flight.Waiters++;
        }
        try{return await flight.Task.WaitAsync(token).ConfigureAwait(false);}
        finally
        {
            lock(gate)
            {
                if(--flight.Waiters==0)
                {
                    if(flight.Task.IsCompleted)flight.Cancellation.Dispose();
                    else
                    {
                        if(flights.TryGetValue(key,out var current) && ReferenceEquals(current,flight))flights.Remove(key);
                        flight.Cancellation.Cancel();
                    }
                }
            }
        }
    }
    private async Task<BitmapSource> Decode(AssistantAttachment file,Func<CancellationToken,Task<byte[]>> read,int pixels,CancellationToken token)
    {
        await decoders.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();var data=await read(token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
            return await Task.Run(()=>{token.ThrowIfCancellationRequested();var geometry=AssistantPreviewGeometry.Inspect(data);var image=AssistantAttachments.Thumbnail(data,pixels);token.ThrowIfCancellationRequested();
                // BitmapImage.StreamSource can retain its disposed MemoryStream and
                // original encoded bytes. A frozen pixel copy owns only display pixels.
                var detached=new System.Windows.Media.Imaging.WriteableBitmap(image);detached.Freeze();token.ThrowIfCancellationRequested();AssistantPreviewGeometry.Remember(file,geometry);return (BitmapSource)detached;},token).ConfigureAwait(false);
        }
        finally{decoders.Release();}
    }
    private void Trim(DateTime now)
    {
        foreach(var item in entries.OrderBy(p=>p.Value.Used).ToArray())
            if(entries.Count>MaximumCount || bytes>MaximumBytes || now-item.Value.Used>=IdleAge){entries.Remove(item.Key);bytes-=item.Value.Bytes;}
    }
    internal static void TrimIdle(AssistantRuntime runtime){if(sessions.TryGetValue(runtime,out var cache)){lock(cache.gate)cache.Trim(DateTime.UtcNow);}}
    internal void ExpireForTest(DateTime now){lock(gate)Trim(now);}
}

// Image metadata is weakly associated with the attachment, not its large data URI.
// A seekable Base64 stream lets WIC read headers without allocating the entire original.
internal static class AssistantPreviewGeometry
{
    internal sealed record Dimensions(int Width,int Height,double DpiX,double DpiY)
    {
        internal Size Size {get{double scale=Math.Min(1,480d/Math.Max(Width,Height));return new(Width*scale*96/DpiX,Height*scale*96/DpiY);}}
    }
    private sealed class Geometry {internal Dimensions? Value;}
    private static readonly ConditionalWeakTable<AssistantAttachment,Geometry> sizes=new();
    // Unknown remote images reserve a landscape-sized slot, without a network read.
    // Learned original metadata survives visual recreation; first-load changes are anchored.
    internal static Size Read(AssistantAttachment file,string source)
    {
        var geometry=sizes.GetValue(file,_=>new(){Value=Read(source)});
        lock(geometry)return geometry.Value?.Size??new Size(480,270);
    }
    internal static void Remember(AssistantAttachment file,Dimensions dimensions)
    {var geometry=sizes.GetValue(file,_=>new());lock(geometry)geometry.Value=dimensions;}
    internal static Dimensions Inspect(byte[] data)
    {using var stream=new MemoryStream(data,false);return Inspect(stream);}
    private static Dimensions Inspect(Stream stream)
    {
        var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation|BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.None).Frames[0];
        int w=frame.PixelWidth,h=frame.PixelHeight;
        if(w<=0 || h<=0 || (long)w*h>80000000 || Math.Max(w,h)>Math.Min(w,h)*30L)throw new IOException("Image dimensions are too large to preview safely.");
        return new(w,h,frame.DpiX>0?frame.DpiX:96,frame.DpiY>0?frame.DpiY:96);
    }
    private static Dimensions? Read(string source)
    {
        if(!source.StartsWith("data:",StringComparison.Ordinal))return null;
        try
        {
            // Provider-supplied Base64 may contain legal whitespace. Preserve that
            // compatibility using a transient buffer only for this uncommon form.
            using Stream stream=source.AsSpan().IndexOfAny(" \r\n\t")>=0?new MemoryStream(AssistantAttachments.Bytes(source)):new InlineStream(source);
            return Inspect(stream);
        }
        catch{return default;}
    }
    private sealed class InlineStream : Stream
    {
        private readonly string source;
        private readonly int start;
        private long position;
        internal InlineStream(string source)
        {
            this.source=source;start=source.IndexOf(',')+1;
            int count=source.Length-start;
            if(start<=0 || start>151 || source.Length>AssistantAttachments.MaximumPayload || count<4 || count%4!=0)throw new IOException("Invalid inline image.");
            Length=(long)count/4*3-(source[^1]=='='?1:0)-(source[^2]=='='?1:0);
        }
        public override bool CanRead=>true;
        public override bool CanSeek=>true;
        public override bool CanWrite=>false;
        public override long Length {get;}
        public override long Position {get=>position;set{if(value<0)throw new ArgumentOutOfRangeException(nameof(value));position=value;}}
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override int Read(Span<byte> buffer)
        {
            int total=0;Span<byte> decoded=stackalloc byte[3072];
            while(buffer.Length>0 && position<Length)
            {
                int skip=(int)(position%3),chars=(int)Math.Min(4096,source.Length-start-position/3*4);
                chars=Math.Min(chars,((buffer.Length+skip+2)/3)*4);
                if(!Convert.TryFromBase64Chars(source.AsSpan(start+(int)(position/3*4),chars),decoded,out int written))throw new IOException("Invalid inline image.");
                int take=Math.Min(buffer.Length,written-skip);decoded.Slice(skip,take).CopyTo(buffer);buffer=buffer[take..];position+=take;total+=take;
            }
            return total;
        }
        public override long Seek(long offset,SeekOrigin origin){Position=origin switch{SeekOrigin.Begin=>offset,SeekOrigin.Current=>position+offset,SeekOrigin.End=>Length+offset,_=>throw new ArgumentOutOfRangeException(nameof(origin))};return position;}
        public override void Flush(){}
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
