using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class OverlayWindow
{
    private ViewportThumbnails? clipboardThumbnails;
    private FrameworkElement ClipboardTile(ClipItem item,ViewportThumbnails thumbnails)
    {
        var p=app.Productivity;var kind=SmartClipboard.Classify(item);
        var layout=new Grid{Height=142};layout.RowDefinitions.Add(new());layout.RowDefinitions.Add(new(){Height=new GridLength(28)});
        var preview=new Grid{Background=Brushes.Transparent,ClipToBounds=true};layout.Children.Add(preview);
        Image? image=null;
        if(kind.Kind==SmartClipKind.Image)
        {
            image=new Image{Stretch=Stretch.Uniform};preview.Children.Add(image);
            preview.SizeChanged+=(_,_)=>preview.Clip=new RectangleGeometry(new Rect(0,0,preview.ActualWidth,preview.ActualHeight),8,8);
        }
        else if(kind.Kind==SmartClipKind.Color)
        {
            preview.Children.Add(new Border{Background=UI.Brush(kind.Hex),CornerRadius=new(8),Margin=new(0,0,0,2)});
            var hex=UI.Text(kind.Hex,15,UI.ContrastText(kind.Hex));hex.HorizontalAlignment=HorizontalAlignment.Center;preview.Children.Add(hex);
        }
        else
        {
            string value=item.Text.Length>170?item.Text[..170]+"…":item.Text;
            var text=UI.Text(value,12,Brushes.White);text.Margin=new(12);text.VerticalAlignment=VerticalAlignment.Top;text.TextWrapping=TextWrapping.Wrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.MaxHeight=86;
            preview.Children.Add(new Border{Child=text,CornerRadius=new(8),BorderThickness=new(1),BorderBrush=UI.Brush("#24282F"),Background=UI.Brush("#15171B")});
        }
        var metadata=new DockPanel{Margin=new(6,4,6,0)};
        var icon=Icons.Create(kind.Kind switch{SmartClipKind.Image=>"image",SmartClipKind.Link=>"link",_=>"file-text"},13,UI.Muted);icon.Margin=new(0,0,8,0);metadata.Children.Add(icon);
        string timestamp=item.Timestamp.ToLocalTime().ToString(item.Timestamp.LocalDateTime.Date==DateTime.Today?"HH:mm":"MMM d · HH:mm");
        metadata.Children.Add(UI.Text((item.Pinned?"Pinned · ":"")+timestamp,10,UI.Muted));Grid.SetRow(metadata,1);layout.Children.Add(metadata);
        var view=UI.Button("View "+kind.Kind,()=>
        {
            selectedClipId=selectedClipId==item.Id?null:item.Id;RefreshTools();ResizeToolPanel();
            Dispatcher.BeginInvoke(()=>{if(clipboardScroll?.Content is Panel content)foreach(FrameworkElement part in content.Children)if(part.Tag as string=="clip-detail"){part.BringIntoView();break;}});
        });
        view.Content=layout;view.Padding=new(0);view.Margin=new(0);view.MinHeight=0;view.HorizontalContentAlignment=HorizontalAlignment.Stretch;view.Background=Brushes.Transparent;view.BorderThickness=new(selectedClipId==item.Id?1:0);view.BorderBrush=UI.Brush("#59616F");view.ToolTip="Open details · copy, pin or remove";
        var tile=new Grid();tile.Children.Add(view);
        Button? copy=null;copy=Icons.Button("copy","Quick copy "+kind.Kind.ToString().ToLowerInvariant(),()=>{copy!.ToolTip=p.TryCopyClip(item)?"Copied":"Clipboard is busy. Try again.";},false,27);
        copy.Margin=new(7);copy.HorizontalAlignment=HorizontalAlignment.Right;copy.VerticalAlignment=VerticalAlignment.Top;copy.Background=UI.Brush("#DA1A1D23");copy.ToolTip="Copy "+kind.Kind.ToString().ToLowerInvariant();tile.Children.Add(copy);MinimalWidgets.Reveal(copy,tile);
        if(image!=null)thumbnails.Add(tile,image,pixels=>Task.FromResult(p.LoadClipImage(item,pixels)));
        return tile;
    }
}
