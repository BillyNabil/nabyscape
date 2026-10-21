using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace NabyScape;

// Holds only display thumbnails. Copy/Save always decode the original file separately.
internal sealed class ThumbnailCache
{
    private sealed record Entry(BitmapSource Image,long Bytes,LinkedListNode<string> Node)
    {internal long UsedAt;}
    private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
    private readonly LinkedList<string> order=new();
    private readonly long budget;
    private readonly Func<long> clock;
    private long bytes;
    internal ThumbnailCache(long budget=16*1024*1024,Func<long>? clock=null){this.budget=budget;this.clock=clock??(()=>Environment.TickCount64);}
    internal int Count=>entries.Count;
    internal long Bytes=>bytes;
    private void Touch(Entry entry){entry.UsedAt=clock();order.Remove(entry.Node);order.AddLast(entry.Node);}
    internal BitmapSource? Get(string key)
    {if(!entries.TryGetValue(key,out var item))return null;Touch(item);return item.Image;}
    internal void Add(string key,BitmapSource image)
    {
        if(entries.TryGetValue(key,out var existing)){Touch(existing);return;}
        long size=(long)image.PixelWidth*image.PixelHeight*Math.Max(4,(image.Format.BitsPerPixel+7)/8);
        if(size>budget)return;
        while(entries.Count>=64 || bytes+size>budget)RemoveOldest();
        entries.Add(key,new(image,size,order.AddLast(key)){UsedAt=clock()});bytes+=size;
    }
    private void RemoveOldest(){string key=order.First!.Value;bytes-=entries[key].Bytes;entries.Remove(key);order.RemoveFirst();}
    // Only cache references expire. Visible images and original files remain intact.
    internal void TrimIdle(long idleMilliseconds=30000)
    {long now=clock();while(order.First is {} node && now-entries[node.Value].UsedAt>=idleMilliseconds)RemoveOldest();}
    internal void Clear(){entries.Clear();order.Clear();bytes=0;}
}
