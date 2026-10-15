using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace NabyScape;

public sealed class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class ShelfItem
{
    public string Path { get; set; } = "";
    [JsonIgnore] public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar));
    [JsonIgnore] public bool Exists => File.Exists(Path) || Directory.Exists(Path);
}

public sealed class ClipItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "text";
    public string Text { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public bool Pinned { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public string Label => Kind=="image" ? "Image" : Text.Replace('\r',' ').Replace('\n',' ');
}

public sealed class ProductivityData
{
    public string Note { get; set; } = "";
    public List<TaskItem> Tasks { get; set; } = new();
    public List<ShelfItem> Shelf { get; set; } = new();
    public List<ClipItem> Clips { get; set; } = new();
}

/// <summary>UI-thread service. Clipboard capture is opt-in on each process start and notification-driven.</summary>
public sealed class ProductivityService : IDisposable
{
    private const int MaxImageBytes=10*1024*1024;
    private readonly Store _store;
    private readonly Action<Exception> _log;
    private readonly bool _nativeClipboard;
    private readonly string _cache;
    private readonly Dictionary<string,string> _imageHashes=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,(long Length,DateTime Modified)> _validatedImages=new(StringComparer.OrdinalIgnoreCase);
    private HwndSource? _source;
    private bool _disposed, _writingClipboard;
    private uint _lastWriteSequence;
    private string _note="";
    private bool _paused;
    private readonly uint[] _excludedFormats;
    public event Action? Changed;
    public List<TaskItem> Tasks { get; } = new();
    public List<ShelfItem> Shelf { get; } = new();
    public List<ClipItem> Clips { get; } = new();
    public bool ClipboardEnabled { get; private set; }
    public string? LastError { get; private set; }
    public bool ClipboardPaused
    {
        get => _paused;
        set { if(_paused==value || _disposed)return; _paused=value; Changed?.Invoke(); }
    }
    public string Note
    {
        get => _note;
        // The editor owns its debounce and calls Save; avoid rebuilding it on every keystroke.
        set { if(_disposed)return;var next=Bound(value,50000);if(_note==next)return;_note=next;Changed?.Invoke(); }
    }
    public ProductivityService(Store store,Action<Exception> log) : this(store,log,true) { }

    // Tests opt out of every native clipboard call, including registration and writes.
    internal ProductivityService(Store store,Action<Exception> log,bool nativeClipboard)
    {
        _store=store; _log=log; _nativeClipboard=nativeClipboard;
        _cache=System.IO.Path.GetFullPath(System.IO.Path.Combine(store.DirectoryPath,"clipboard-images"));
        _excludedFormats=nativeClipboard ? new[]{RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"),RegisterClipboardFormat("CanIncludeInClipboardHistory"),RegisterClipboardFormat("CanUploadToCloudClipboard")} : Array.Empty<uint>();
        var data=store.Load<ProductivityData>("productivity");
        _note=Bound(data.Note,50000);
        Tasks.AddRange(data.Tasks ?? new()); Shelf.AddRange(data.Shelf ?? new()); Clips.AddRange(data.Clips ?? new());
        Normalize();
        PurgeOrphanImages();
    }

    public bool Save()
    {
        if(_disposed)return false;
        try { Normalize(); _store.Save("productivity",new ProductivityData{Note=_note,Tasks=Tasks,Shelf=Shelf,Clips=Clips}); return true; }
        catch(Exception ex) { ReportError(ex); return false; }
    }

    public bool AddTask(string text)
    {
        if(_disposed || string.IsNullOrWhiteSpace(text) || Tasks.Count>=100)return false;
        Tasks.Add(new(){Text=Bound(text.Trim(),20000)}); Commit(); return true;
    }
    public void RemoveTask(string id) { if(!_disposed && Tasks.RemoveAll(t=>t.Id==id)>0)Commit(); }
    public int AddFiles(IEnumerable<string> paths)
    {
        if(_disposed || paths==null)return 0;
        int added=0;
        foreach(var path in paths)
        {
            if(Shelf.Count>=40)break;
            var full=CanonicalPath(path);
            if(full==null || Shelf.Any(s=>string.Equals(s.Path,full,StringComparison.OrdinalIgnoreCase)))continue;
            Shelf.Add(new(){Path=full}); added++;
        }
        if(added>0)Commit(); return added;
    }
    public void RemoveFile(string path)
    {
        if(_disposed)return;
        var full=CanonicalPath(path);
        if(full!=null && Shelf.RemoveAll(s=>string.Equals(s.Path,full,StringComparison.OrdinalIgnoreCase))>0)Commit();
    }

    public void SetClipboardEnabled(bool enabled)
    {
        if(_disposed)return;
        if(!enabled)
        {
            DetachListener(); ClipboardEnabled=false; _paused=false; ClearClips(); Changed?.Invoke(); return;
        }
        if(ClipboardEnabled)return;
        try
        {
            if(_nativeClipboard)
            {
                var parameters=new HwndSourceParameters("NabyScapeClipboard") { ParentWindow=new IntPtr(-3),WindowStyle=0,Width=0,Height=0 };
                _source=new HwndSource(parameters); _source.AddHook(ClipboardMessage);
                if(!AddClipboardFormatListener(_source.Handle))throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            ClipboardEnabled=true; LastError=null; Changed?.Invoke();
        }
        catch(Exception ex) { DetachListener(); ReportError(ex); Changed?.Invoke(); }
    }

    public void CopyClip(ClipItem item) => TryCopyClip(item);

    public bool TryCopyClip(ClipItem item,string? textVariant=null)
    {
        if(_disposed || !_nativeClipboard || !ClipboardEnabled || item==null)return false;
        var clip=Clips.FirstOrDefault(c=>c.Id==item.Id);
        if(clip==null)return false;
        try
        {
            _writingClipboard=true;
            if(clip.Kind=="text")
            {
                var classified=SmartClipboard.Classify(clip);
                if(textVariant!=null && textVariant!=clip.Text &&
                    !(classified.Kind==SmartClipKind.Link && textVariant==classified.CleanedLink) &&
                    !(classified.Kind==SmartClipKind.Color && (textVariant==classified.Hex || textVariant==classified.Rgb)))return false;
                // SetText exports only Unicode text, without HTML/RTF or simulated paste.
                System.Windows.Clipboard.SetText(textVariant ?? clip.Text);
            }
            else
            {
                var bitmap=LoadClipImage(clip);
                if(bitmap==null)return false;
                System.Windows.Clipboard.SetImage(bitmap);
            }
            _lastWriteSequence=GetClipboardSequenceNumber(); LastError=null; return true;
        }
        catch(Exception ex) { ReportError(ex); return false; }
        finally { _writingClipboard=false; }
    }

    public BitmapSource? LoadClipImage(ClipItem item,int decodeWidth=0)
    {
        if(_disposed || !ClipboardEnabled || item==null)return null;
        var clip=Clips.FirstOrDefault(c=>c.Id==item.Id && c.Kind=="image");
        if(clip==null || !ValidImageFile(clip.ImagePath))return null;
        try
        {
            using var stream=File.OpenRead(clip.ImagePath);
            if(decodeWidth>0)
            {
                var thumbnail=new BitmapImage();thumbnail.BeginInit();thumbnail.CacheOption=BitmapCacheOption.OnLoad;
                thumbnail.DecodePixelWidth=decodeWidth;thumbnail.StreamSource=stream;thumbnail.EndInit();thumbnail.Freeze();return thumbnail;
            }
            var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            var bitmap=decoder.Frames[0]; bitmap.Freeze(); return bitmap;
        }
        catch(Exception ex) { ReportError(ex); return null; }
    }

    public bool SaveClipImage(ClipItem item,string destination)
    {
        var bitmap=LoadClipImage(item);
        if(bitmap==null)return false;
        try
        {
            // Encode before opening the destination so a decoding failure cannot truncate it.
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var encoded=new MemoryStream();encoder.Save(encoded);
            using var output=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.None);
            encoded.Position=0;encoded.CopyTo(output); LastError=null; return true;
        }
        catch(Exception ex) { ReportError(ex); return false; }
    }

    public void ToggleClipPin(string id)
    {
        if(_disposed)return;
        var clip=Clips.FirstOrDefault(c=>c.Id==id);
        if(clip==null || (!clip.Pinned && Clips.Count(c=>c.Pinned)>=20))return;
        clip.Pinned=!clip.Pinned; TrimClips(); Commit();
    }
    public void RemoveClip(string id)
    {
        if(_disposed)return;
        var clip=Clips.FirstOrDefault(c=>c.Id==id);
        if(clip==null)return;
        Clips.Remove(clip); DeleteImage(clip.ImagePath); Commit();
    }
    public void ClearClips()
    {
        if(_disposed)return;
        foreach(var clip in Clips)DeleteImage(clip.ImagePath);
        Clips.Clear(); _imageHashes.Clear(); PurgeOrphanImages(); Commit();
    }

    internal bool IngestText(string? text)
    {
        if(_disposed || !ClipboardEnabled || _paused || string.IsNullOrWhiteSpace(text))return false;
        var value=Bound(text,20000);
        var duplicate=Clips.FirstOrDefault(c=>c.Kind=="text" && c.Text==value);
        if(duplicate!=null) { Touch(duplicate); return false; }
        Clips.Insert(0,new(){Kind="text",Text=value}); TrimClips(); Commit(); return true;
    }
    internal bool IngestImage(byte[]? png)
    {
        if(_disposed || !ClipboardEnabled || _paused || png==null || png.Length==0 || png.Length>MaxImageBytes)return false;
        string? path=null;
        try
        {
            using(var input=new MemoryStream(png,false))
            {
                var decoder=new PngBitmapDecoder(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                if(decoder.Frames.Count!=1 || (long)decoder.Frames[0].PixelWidth*decoder.Frames[0].PixelHeight>25000000)return false;
            }
            var hash=Convert.ToHexString(SHA256.HashData(png));
            foreach(var clip in Clips.Where(c=>c.Kind=="image"))
            {
                if(!_imageHashes.TryGetValue(clip.ImagePath,out var existing) && ValidImageFile(clip.ImagePath))
                    _imageHashes[clip.ImagePath]=existing=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(clip.ImagePath)));
                if(existing==hash) { Touch(clip); return false; }
            }
            EnsureCache();
            path=System.IO.Path.Combine(_cache,Guid.NewGuid().ToString("N")+".png");
            using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))file.Write(png);
            _imageHashes[path]=hash;
            Clips.Insert(0,new(){Kind="image",ImagePath=path}); TrimClips(); Commit(); return true;
        }
        catch(Exception ex) { if(path!=null)DeleteImage(path); ReportError(ex); return false; }
    }

    private IntPtr ClipboardMessage(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam,ref bool handled)
    {
        if(message!=0x031D || !ClipboardEnabled || _paused || _writingClipboard || _disposed)return IntPtr.Zero;
        try
        {
            var sequence=GetClipboardSequenceNumber();
            if(sequence!=0 && sequence==_lastWriteSequence)return IntPtr.Zero;
            if(_excludedFormats.Any(f=>f!=0 && IsClipboardFormatAvailable(f)))return IntPtr.Zero;
            var data=System.Windows.Clipboard.GetDataObject();
            if(data==null)return IntPtr.Zero;
            string? text=null; byte[]? png=null;
            if(data.GetDataPresent(DataFormats.UnicodeText))text=data.GetData(DataFormats.UnicodeText) as string;
            else if(data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                if((long)bitmap.PixelWidth*bitmap.PixelHeight>25000000)return IntPtr.Zero;
                var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream=new MemoryStream(); encoder.Save(stream);
                if(stream.Length<=MaxImageBytes)png=stream.ToArray();
            }
            // Delayed-rendering clipboard owners can change while data is fetched.
            // Reject that notification instead of recording a new owner's protected data.
            if(sequence!=GetClipboardSequenceNumber() || _excludedFormats.Any(f=>f!=0 && IsClipboardFormatAvailable(f)))return IntPtr.Zero;
            if(text!=null)IngestText(text);
            else if(png!=null)IngestImage(png);
        }
        catch(Exception ex) { ReportError(ex); }
        return IntPtr.Zero;
    }

    private void Touch(ClipItem item) { item.Timestamp=DateTimeOffset.UtcNow; Clips.Remove(item); Clips.Insert(0,item); Commit(); }
    private void Commit() { Save(); Changed?.Invoke(); }
    private void ReportError(Exception error) { LastError=error.Message; _log(error); }
    private static string Bound(string? text,int limit) => text==null ? "" : text.Length>limit ? text[..limit] : text;
    private static string? CanonicalPath(string? path)
    {
        if(string.IsNullOrWhiteSpace(path))return null;
        try { return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)); }
        catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
    private void Normalize()
    {
        _note=Bound(_note,50000);
        var tasks=Tasks.Where(t=>t!=null && !string.IsNullOrWhiteSpace(t.Text)).Take(100).ToList();
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var task in tasks) { if(string.IsNullOrWhiteSpace(task.Id) || !ids.Add(task.Id)) { task.Id=Guid.NewGuid().ToString("N"); ids.Add(task.Id); } task.Text=Bound(task.Text,20000); }
        Tasks.Clear(); Tasks.AddRange(tasks);
        var shelf=Shelf.Where(s=>s!=null).Select(s=>CanonicalPath(s.Path)).Where(p=>p!=null).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).Select(p=>new ShelfItem{Path=p!}).ToList();
        Shelf.Clear(); Shelf.AddRange(shelf);
        var clips=Clips.Where(c=>c!=null && (c.Kind=="text" && !string.IsNullOrWhiteSpace(c.Text) || c.Kind=="image" && ValidImageFile(c.ImagePath))).OrderByDescending(c=>c.Timestamp).ToList();
        Clips.Clear(); ids.Clear(); var texts=new HashSet<string>(StringComparer.Ordinal); var images=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var clip in clips)
        {
            clip.Text=clip.Kind=="text" ? Bound(clip.Text,20000) : "";
            if(clip.Kind=="text") { clip.ImagePath=""; if(!texts.Add(clip.Text))continue; }
            else { clip.ImagePath=System.IO.Path.GetFullPath(clip.ImagePath); if(!images.Add(clip.ImagePath))continue; }
            if(string.IsNullOrWhiteSpace(clip.Id) || !ids.Add(clip.Id)) { clip.Id=Guid.NewGuid().ToString("N"); ids.Add(clip.Id); }
            Clips.Add(clip);
        }
        TrimClips();
    }
    private void TrimClips()
    {
        int pinned=0,regular=0;
        foreach(var clip in Clips.ToArray())
        {
            if(clip.Pinned ? ++pinned<=20 : ++regular<=30)continue;
            Clips.Remove(clip); DeleteImage(clip.ImagePath);
        }
    }
    private void EnsureCache()
    {
        Directory.CreateDirectory(_cache);
        if((File.GetAttributes(_cache)&FileAttributes.ReparsePoint)!=0)throw new IOException("The clipboard cache folder cannot be a link.");
    }
    private bool ConfinedImage(string? path)
    {
        if(string.IsNullOrEmpty(path))return false;
        try
        {
            var full=System.IO.Path.GetFullPath(path);
            return string.Equals(System.IO.Path.GetDirectoryName(full),_cache,StringComparison.OrdinalIgnoreCase)
                && string.Equals(System.IO.Path.GetExtension(full),".png",StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(full),"N",out _)
                && (!Directory.Exists(_cache) || (File.GetAttributes(_cache)&FileAttributes.ReparsePoint)==0);
        }
        catch(Exception ex) when(ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }
    private bool ValidImageFile(string? path)
    {
        if(!ConfinedImage(path))return false;
        try
        {
            var file=new FileInfo(path!);
            if(!file.Exists || file.Length<=0 || file.Length>MaxImageBytes || (file.Attributes&FileAttributes.ReparsePoint)!=0)return false;
            var version=(file.Length,file.LastWriteTimeUtc);
            if(_validatedImages.TryGetValue(path!,out var validated) && validated==version)return true;
            using var stream=file.OpenRead();
            var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            if(decoder.Frames.Count!=1 || (long)decoder.Frames[0].PixelWidth*decoder.Frames[0].PixelHeight>25000000)return false;
            _validatedImages[path!]=version; return true;
        }
        catch(Exception ex) { ReportError(ex); return false; }
    }
    private void DeleteImage(string? path)
    {
        if(!ConfinedImage(path))return;
        try { File.Delete(path!); _imageHashes.Remove(path!); _validatedImages.Remove(path!); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { ReportError(ex); }
    }
    private void PurgeOrphanImages()
    {
        if(!Directory.Exists(_cache))return;
        try
        {
            if((File.GetAttributes(_cache)&FileAttributes.ReparsePoint)!=0)return;
            var retained=Clips.Where(c=>c.Kind=="image").Select(c=>c.ImagePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var path in Directory.EnumerateFiles(_cache,"*.png"))if(!retained.Contains(path))DeleteImage(path);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { ReportError(ex); }
    }
    private void DetachListener()
    {
        if(_source==null)return;
        try { RemoveClipboardFormatListener(_source.Handle); _source.RemoveHook(ClipboardMessage); _source.Dispose(); }
        catch(Exception ex) { ReportError(ex); }
        finally { _source=null; }
    }
    public void Dispose()
    {
        if(_disposed)return;
        Save(); DetachListener(); ClipboardEnabled=false; _disposed=true;
    }
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
}
