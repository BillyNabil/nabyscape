using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NabyScape;
public sealed partial class OverlayWindow
{
    private ViewportThumbnails? shelfViewportThumbnails;
    private async Task<BitmapSource?> ShelfThumbnail(string path,int pixels)
    {
        try
        {
            var info=new FileInfo(path);string key=path+"|"+(info.Exists?info.LastWriteTimeUtc.Ticks:0)+"|"+pixels;
            if(shelfThumbnails.Get(key) is {} cached)return cached;
            BitmapSource? source=null;
            if(new[]{".png",".jpg",".jpeg",".bmp",".gif",".tif",".tiff"}.Contains(Path.GetExtension(path).ToLowerInvariant()) && info.Exists && info.Length<10*1024*1024)
            {
                try{source=await Task.Run(()=>{using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return DisplayThumbnail.Decode(stream,pixels);});}catch{}
            }
            source??=pixels>64?await AppIconCache.Thumbnail(path,pixels):await AppIconCache.GetAsync(new(){Target=path});
            if(source!=null)shelfThumbnails.Add(key,source);return source;
        }
        catch{return null;}
    }
    private async Task LoadShelfThumbnail(string path,Image image)
    {image.Source=await ShelfThumbnail(path,(int)Math.Ceiling(44*VisualTreeHelper.GetDpi(this).DpiScaleX));}
    private void BuildFileShelf()
    {
        var p=app.Productivity;shelfSelection.RemoveWhere(path=>!p.Shelf.Any(x=>x.Path.Equals(path,StringComparison.OrdinalIgnoreCase)));
        var heading=UI.Row(UI.Heading("File shelf",18),UI.Text($"{p.Shelf.Count} / 40",11,UI.Muted));((FrameworkElement)heading.Children[1]).Margin=new(12,0,0,0);
        var add=UI.Button("Add files…",()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Multiselect=true,Title="Add to file shelf"};if(dialog.ShowDialog(this)==true)MutateUtility(()=>p.AddFiles(dialog.FileNames));});
        add.MinHeight=30;add.Padding=new(10,5,10,5);add.Margin=new(0);add.IsEnabled=p.Shelf.Count<40;
        var more=MinimalWidgets.More("File shelf actions",("Select all",()=>{foreach(var file in p.Shelf.Where(x=>x.Exists))shelfSelection.Add(file.Path);RefreshTools();}),("Clear selection",()=>{shelfSelection.Clear();RefreshTools();}));
        toolsContent.Children.Add(MinimalWidgets.Header(heading,UI.Row(add,more)));
        var gallery=new TileFlow{MinimumTileWidth=150,Gap=18};var viewport=UI.Scroll(gallery);viewport.Tag="panel-content-scroll";viewport.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;viewport.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;viewport.PanningMode=PanningMode.VerticalOnly;viewport.MaxHeight=Math.Clamp((app.Settings.PanelMaxHeight-160)/app.Settings.PanelScale,84,380);UI.Named(viewport,"Shelf files");toolsContent.Children.Add(viewport);
        shelfViewportThumbnails=new(viewport);
        var count=UI.Text("",10,UI.Muted);void Count()=>count.Text=shelfSelection.Count>0?$"{shelfSelection.Count} selected":"";Count();
        foreach(var item in p.Shelf.ToArray())
        {
            var content=new StackPanel();var image=new Image{Height=142,Stretch=Stretch.Uniform,IsHitTestVisible=false};
            var preview=new Grid{Height=142,Background=Brushes.Transparent};var fallback=Icons.Create(Directory.Exists(item.Path)?"folder":"file-text",34,UI.Muted);image.Tag=fallback;preview.Children.Add(fallback);preview.Children.Add(image);
            var frame=new Border{Child=preview,CornerRadius=new(9),ClipToBounds=true,Margin=new(0,0,0,10)};
            preview.SizeChanged+=(_,_)=>frame.Clip=new RectangleGeometry(new Rect(0,0,preview.ActualWidth,preview.ActualHeight),9,9);content.Children.Add(frame);
            var name=UI.Text(item.Name,12,item.Exists?Brushes.White:UI.Muted);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;content.Children.Add(name);
            var extension=Path.GetExtension(item.Path).TrimStart('.').ToUpperInvariant();var sub=UI.Text(item.Exists?(extension.Length>0?extension:Directory.Exists(item.Path)?"Folder":"File"):"File not found",10,UI.Muted);sub.Margin=new(0,4,0,0);content.Children.Add(sub);
            var tile=new Border{Tag="file-drag",Child=content,Background=Brushes.Transparent,ToolTip=item.Path+"\nDrag out · double-click to open"};UI.Named(tile,"Shelf file "+item.Name);
            var selected=new CheckBox{IsChecked=shelfSelection.Contains(item.Path),IsEnabled=item.Exists,Style=(Style)FindResource("TaskToggle"),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(6),MinHeight=28};UI.Named(selected,"Select "+item.Name);preview.Children.Add(selected);
            selected.Checked+=(_,_)=>{shelfSelection.Add(item.Path);Count();};selected.Unchecked+=(_,_)=>{shelfSelection.Remove(item.Path);Count();};
            var remove=Icons.Button("x","Unstage "+item.Name,()=>MutateUtility(()=>p.RemoveFile(item.Path)),false,28);remove.Margin=new(6);remove.HorizontalAlignment=HorizontalAlignment.Right;remove.VerticalAlignment=VerticalAlignment.Top;remove.Background=UI.Brush("#E61A1D22");remove.ToolTip="Unstage "+item.Name;preview.Children.Add(remove);MinimalWidgets.Reveal(remove,tile);
            Point? start=null;
            tile.PreviewMouseLeftButtonDown+=(_,e)=>{if(PeekGestureBlocked(e.OriginalSource as DependencyObject))return;start=e.GetPosition(tile);if(e.ClickCount==2){try{ShortcutService.Open(new(){Target=item.Path});}catch(Exception ex){app.Notify("Could not open file",ex.Message);}start=null;e.Handled=true;}};
            tile.PreviewMouseMove+=(_,e)=>
            {
                if(start==null || e.LeftButton!=MouseButtonState.Pressed || !item.Exists)return;var delta=e.GetPosition(tile)-start.Value;
                if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;start=null;
                var paths=(shelfSelection.Contains(item.Path)?p.Shelf.Where(x=>x.Exists && shelfSelection.Contains(x.Path)).Select(x=>x.Path):new[]{item.Path}).ToArray();
                try{if(paths.Length>0)DragDrop.DoDragDrop(tile,new DataObject(DataFormats.FileDrop,paths),DragDropEffects.Copy|DragDropEffects.Link);}finally{FinishFileDrop(false);}e.Handled=true;
            };
            gallery.Children.Add(tile);shelfViewportThumbnails.Add(tile,image,pixels=>ShelfThumbnail(item.Path,pixels));
        }
        if(p.Shelf.Count==0){var empty=new StackPanel{Margin=new(0,18,0,22)};var icon=ToolIcon("files");icon.Width=icon.Height=34;icon.Opacity=.6;empty.Children.Add(icon);var text=UI.Text("Drop files here or add files above.",12,UI.Muted);text.HorizontalAlignment=HorizontalAlignment.Center;text.Margin=new(0,12,0,0);empty.Children.Add(text);gallery.Children.Add(empty);}
        var footer=new DockPanel{Margin=new(0,18,0,0)};DockPanel.SetDock(count,Dock.Right);footer.Children.Add(count);footer.Children.Add(UI.Text("↗  Drop files or drag out",10,UI.Muted));toolsContent.Children.Add(footer);
    }
}
