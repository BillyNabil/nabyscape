using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace NabyScape;

internal sealed class AssistantAttachmentView : UserControl
{
    private readonly AssistantRuntime ai;
    private readonly AssistantAttachment file;
    private readonly AssistantPreviewImage preview=new(){Stretch=Stretch.Uniform,MaxHeight=240,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly TextBlock detail=UI.Text("",10,UI.Muted);
    private readonly string previewSource;
    private CancellationTokenSource? loading;
    private readonly List<ScrollViewer> viewports=new();
    private readonly bool draft;
    private bool demandQueued,watching,failed;
    private int requestedPixels;
    internal bool PreviewRealized=>preview.Source!=null;
    internal Image PreviewImage=>preview;
    internal AssistantAttachmentView(AssistantRuntime ai,AssistantAttachment file,bool draft=false)
    {
        this.ai=ai;this.file=file;this.draft=draft;previewSource=file.IsImage?file.Source:file.Frames is {Count:>0}?file.Frames[0]:"";
        if(previewSource.Length>0 && !draft)preview.NaturalSize=AssistantPreviewGeometry.Read(file,previewSource);
        var body=new StackPanel();var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var icon=Icons.Create(file.IsImage?"image":"file-text",15,UI.Muted);icon.Margin=new(0,0,8,0);
        if(draft && previewSource.Length>0){preview.Width=40;preview.Height=40;preview.Margin=new(0,0,8,0);row.Children.Add(preview);}else row.Children.Add(icon);
        var name=UI.Text(file.Name,11);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.ToolTip=file.Name;
        if(draft){var remove=Icons.Button("x","Remove "+file.Name,()=>ai.RemoveAttachment(file),false,26);remove.Padding=new(0);remove.Content=Icons.Create("x",13,UI.Muted);remove.Margin=new(4,0,0,0);remove.Background=Brushes.Transparent;Grid.SetColumn(remove,2);row.Children.Add(remove);}
        detail.Text=draft?AssistantAttachmentSummary.Describe(file,ai.Provider):file.Note;detail.Margin=new(0,4,0,0);detail.MaxHeight=draft?84:44;detail.TextTrimming=TextTrimming.CharacterEllipsis;detail.ToolTip=detail.Text;
        if(draft){var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};labels.Children.Add(name);labels.Children.Add(detail);Grid.SetColumn(labels,1);row.Children.Add(labels);body.Children.Add(row);}
        else{Grid.SetColumn(name,1);row.Children.Add(name);body.Children.Add(row);body.Children.Add(detail);}
        if(previewSource.Length>0 && !draft)
        {
            preview.Margin=new(0,8,0,0);body.Children.Add(preview);UI.Named(preview,file.Name+" preview");
            preview.ToolTip="Click to enlarge";preview.Cursor=System.Windows.Input.Cursors.Hand;preview.MouseLeftButtonUp+=(_,_)=>_=Enlarge();
        }
        if(!draft)
        {
            var actions=new WrapPanel{Margin=new(0,6,0,0)};
            if(previewSource.Length>0){var open=AssistantControls.Action("Open preview","image",()=>_=Enlarge());open.Margin=new(0,0,5,0);actions.Children.Add(open);}
            if(file.IsImage){var copy=AssistantControls.Action("Copy image","copy",()=>_=Copy());copy.Margin=new(0,0,5,0);actions.Children.Add(copy);}
            actions.Children.Add(AssistantControls.Action("Save…","folder",()=>_=Save()));body.Children.Add(actions);
        }
        Content=new Border{Child=body,Background=UI.Brush("#171A20"),BorderBrush=UI.Brush("#2B3039"),BorderThickness=new(1),CornerRadius=new(10),Padding=new(10),Margin=new(0,4,draft?6:0,4)};
        if(draft)Width=240;else MaxWidth=460;
        Loaded+=(_,_)=>Watch();
        Unloaded+=(_,_)=>Unwatch();
        IsVisibleChanged+=(_,_)=>{if(IsVisible && IsLoaded)Watch();else if(!IsVisible)Unwatch();};
        SizeChanged+=(_,_)=>QueueDemand();
    }
    private Task<byte[]> Read(CancellationToken token)
        =>file.Text!=null?Task.FromResult(Encoding.UTF8.GetBytes(file.Text)):ai.Media(file,token);
    private void Watch()
    {
        if(watching || previewSource.Length==0)return;watching=true;failed=false;
        for(DependencyObject? parent=VisualTreeHelper.GetParent(this);parent!=null;parent=VisualTreeHelper.GetParent(parent))
            if(parent is ScrollViewer viewport){viewports.Add(viewport);viewport.ScrollChanged+=ViewportChanged;viewport.SizeChanged+=ViewportResized;}
        QueueDemand();
    }
    private void Unwatch()
    {
        watching=false;
        foreach(var viewport in viewports){viewport.ScrollChanged-=ViewportChanged;viewport.SizeChanged-=ViewportResized;}viewports.Clear();
        ReleasePreview();
    }
    private void ViewportChanged(object sender,ScrollChangedEventArgs e)=>QueueDemand();
    private void ViewportResized(object sender,SizeChangedEventArgs e)=>QueueDemand();
    private void QueueDemand()
    {
        if(!watching || demandQueued)return;demandQueued=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>{demandQueued=false;UpdateDemand();}));
    }
    private void ReleasePreview()
    {
        var previous=loading;loading=null;previous?.Cancel();preview.Source=null;requestedPixels=0;
    }
    private void UpdateDemand()
    {
        if(!watching || !IsLoaded || !IsVisible || ActualWidth<=0 || ActualHeight<=0){ReleasePreview();return;}
        foreach(var viewport in viewports)
        {
            try
            {
                var bounds=TransformToAncestor(viewport).TransformBounds(new Rect(0,0,ActualWidth,ActualHeight));
                if(!bounds.IntersectsWith(new Rect(-32,-160,viewport.ActualWidth+64,viewport.ActualHeight+320))){ReleasePreview();return;}
            }
            catch(InvalidOperationException){ReleasePreview();return;}
        }
        var dpi=VisualTreeHelper.GetDpi(this);double width=preview.ActualWidth,height=preview.ActualHeight;
        if(width<=0 || height<=0){width=draft?40:Math.Min(438,Math.Max(1,ActualWidth-22));height=draft?40:240;}
        // Include parent layout transforms (the island's configurable panel scale).
        if(PresentationSource.FromVisual(this)?.RootVisual is Visual root)
        {
            var pixels=preview.TransformToAncestor(root).TransformBounds(new Rect(0,0,width,height));width=pixels.Width;height=pixels.Height;
        }
        int needed=Math.Max(32,(int)Math.Ceiling(Math.Max(width*dpi.DpiScaleX,height*dpi.DpiScaleY)/32)*32);
        if(failed || requestedPixels>=needed && (loading!=null || preview.Source!=null))return;
        requestedPixels=needed;_=Load(needed);
    }
    private async Task Load(int pixels)
    {
        loading?.Cancel();using var request=new CancellationTokenSource();loading=request;
        try
        {
            bool local=previewSource.StartsWith("data:",StringComparison.Ordinal);
            // Provider settings/ownership are UI-owned. A queued remote read may
            // resume on a decoder worker; evaluate its authorization on the dispatcher.
            Task<byte[]> ReadPreview(CancellationToken token)=>previewSource!=file.Source?Task.FromResult(AssistantAttachments.Bytes(previewSource)):local?ai.Media(file,token):Dispatcher.InvokeAsync(()=>ai.Media(file,token),System.Windows.Threading.DispatcherPriority.Background,token).Task.Unwrap();
            var bitmap=await AssistantPreviewCache.For(ai).Get(file,pixels,ReadPreview,local,request.Token);
            request.Token.ThrowIfCancellationRequested();if(!ReferenceEquals(loading,request) || !watching)return;
            if(!draft)preview.SetNaturalSize(AssistantPreviewGeometry.Read(file,previewSource),viewports);
            preview.Source=bitmap;
        }
        catch(OperationCanceledException){}
        catch{if(ReferenceEquals(loading,request)){failed=true;detail.Text=file.Note+" · Preview unavailable";detail.ToolTip="The image may require a different codec, or its link may have expired. Save the file to view it.";}}
        finally{if(ReferenceEquals(loading,request))loading=null;}
    }
    private async Task Copy()
    {
        try{byte[] bytes=await Read(CancellationToken.None);await AssistantClipboard.CopyImage(bytes);detail.Text="Image copied";}
        catch{detail.Text="Could not copy this image. Try Save instead.";}
    }
    private async Task Save()
    {
        string extension=file.Mime switch{"image/jpeg"=>".jpg","image/webp"=>".webp","image/gif"=>".gif","application/pdf"=>".pdf","video/webm"=>".webm","video/quicktime"=>".mov","video/x-matroska"=>".mkv","video/x-msvideo"=>".avi","video/mp4"=>".mp4","audio/mpeg"=>".mp3","audio/wav"=>".wav","audio/mp4"=>".m4a","audio/ogg"=>".ogg","audio/flac"=>".flac",_=>file.IsImage?".png":file.Text!=null?".txt":".bin"};
        string name=file.Name;foreach(char invalid in Path.GetInvalidFileNameChars())name=name.Replace(invalid,'_');if(name.Length==0)name="Attachment";
        if(!name.EndsWith(extension,StringComparison.OrdinalIgnoreCase))name=Path.GetFileNameWithoutExtension(name)+extension;
        var dialog=new SaveFileDialog{Title="Save attachment",FileName=name,DefaultExt=extension,Filter="Attachment (*"+extension+")|*"+extension,AddExtension=true,OverwritePrompt=true};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{byte[] bytes=await Read(CancellationToken.None);await File.WriteAllBytesAsync(dialog.FileName,bytes);detail.Text="Saved";}
        catch{detail.Text="Could not save the file. Check its link and the save folder.";}
    }
    private async Task Enlarge()
    {
        try
        {
            byte[] bytes=previewSource==file.Source?await Read(CancellationToken.None):AssistantAttachments.Bytes(previewSource);var bitmap=await Task.Run(()=>AssistantAttachments.Thumbnail(bytes,1800));
            var image=new Image{Source=bitmap,Stretch=Stretch.Uniform};var owner=Window.GetWindow(this);var window=new Window{Title=file.Name,Width=Math.Min(960,SystemParameters.WorkArea.Width-60),Height=Math.Min(720,SystemParameters.WorkArea.Height-60),MinWidth=240,MinHeight=200,Background=UI.Brush("#101215"),Content=new ScrollViewer{Content=image,Padding=new(18),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},WindowStartupLocation=WindowStartupLocation.CenterScreen};
            if(owner!=null)window.Owner=owner;window.Show();
        }
        catch{detail.Text="Could not open the image preview. Try Save instead.";}
    }
}
