using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace NabyScape;

// Holds only display thumbnails. Copy/Save always decode the original file separately.
internal sealed class ThumbnailCache
{
    private readonly Dictionary<string,(BitmapSource Image,long Bytes)> entries=new(StringComparer.Ordinal);
    private readonly Queue<string> order=new();
    private readonly long budget;
    private long bytes;
    internal ThumbnailCache(long budget=16*1024*1024)=>this.budget=budget;
    internal int Count=>entries.Count;
    internal long Bytes=>bytes;
    internal BitmapSource? Get(string key)=>entries.TryGetValue(key,out var item)?item.Image:null;
    internal void Add(string key,BitmapSource image)
    {
        if(entries.ContainsKey(key))return;
        long size=(long)image.PixelWidth*image.PixelHeight*Math.Max(4,(image.Format.BitsPerPixel+7)/8);
        if(size>budget)return;
        while(entries.Count>=64 || bytes+size>budget){var old=order.Dequeue();bytes-=entries[old].Bytes;entries.Remove(old);}
        entries.Add(key,(image,size));order.Enqueue(key);bytes+=size;
    }
    internal void Clear(){entries.Clear();order.Clear();bytes=0;}
}
