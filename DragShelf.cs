using System;
using System.Collections.Generic;
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
    private readonly HashSet<string> shelfSelection=new(StringComparer.OrdinalIgnoreCase);
    private Border? shelfDropPreview;
    private string dropSignature="";
    private bool shelfDropBrowsing;
    private bool fileDragActive,dragWasExpanded,dragWasPeeking;
    private string dragPreviousPeek="media";
    private void InitializeFileDrop()
    {
        AllowDrop=true;
        DragEnter+=(_,e)=>
        {
            if(!CanShelfDrop(e)){e.Effects=DragDropEffects.None;return;}
            if(!fileDragActive)
            {
                BeginFileDrop();
            }
            ShowShelfDropPreview(e.Data.GetData(DataFormats.FileDrop) as string[]??Array.Empty<string>());e.Effects=DragDropEffects.Copy;e.Handled=true;
        };
        DragOver+=(_,e)=>{if(CanShelfDrop(e)){e.Effects=DragDropEffects.Copy;e.Handled=true;}};
        DragLeave+=(_,e)=>{var p=e.GetPosition(this);if(!VisiblePoint(p))FinishFileDrop(false);};
        Drop+=(_,e)=>
        {
            if(!CanShelfDrop(e))return;
            if(e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                utilityMutating=true;try{app.Productivity.AddFiles(paths);}catch(Exception ex){app.Store.Log(ex);app.Notify("Could not stage files",ex.Message);}finally{utilityMutating=false;}
                FinishFileDrop(true);if(peeking){SelectPeek("files");UpdatePeekDeck();}else ShowTools("files");
            }
            e.Handled=true;
        };
    }
    private void BeginFileDrop()
    {
        if(userHidden || fileDragActive)return;
        fileDragActive=true;dragWasExpanded=expanded;dragWasPeeking=peeking;dragPreviousPeek=peekId;
        StopHoverTimers();EndPeekGesture();
        // An expanded page stays mounted, including command/calendar editor state.
        if(!expanded){if(IsTucked)RevealTuckedPreview();if(!peeking)PreviewActivity();SelectPeek("files");PromotePeek();}
    }
    private bool CanShelfDrop(DragEventArgs e)=>!userHidden && ToolOn("files") && e.Data.GetDataPresent(DataFormats.FileDrop) && (app.Settings.DropReveal || expanded && toolsMode=="files" || peeking && peekId=="files");
    private void ShowShelfDropPreview(string[] paths)
    {
        if(!fileDragActive)return;var signature=string.Join("\n",paths);if(shelfDropPreview!=null && dropSignature==signature)return;dropSignature=signature;
        var stack=new Grid{Width=144,Height=66,HorizontalAlignment=HorizontalAlignment.Center};
        for(int i=0;i<Math.Min(3,paths.Length);i++)
        {
            var tile=new Border{Width=54,Height=58,CornerRadius=new(7),Background=UI.Brush("#34363C"),Padding=new(5),Margin=new Thickness(i*32-64,0,0,0),HorizontalAlignment=HorizontalAlignment.Center,RenderTransform=new RotateTransform((i-1)*7)};
            var image=new Image{Stretch=Stretch.Uniform};tile.Child=image;stack.Children.Add(tile);_=LoadShelfThumbnail(paths[i],image);
        }
        var content=new StackPanel();content.Children.Add(stack);var text=UI.Text($"Drop {paths.Length} {(paths.Length==1?"item":"items")} onto your shelf",12);text.HorizontalAlignment=HorizontalAlignment.Center;text.Margin=new(0,6,0,0);content.Children.Add(text);
        RemoveShelfDropPreview();
        shelfDropPreview=new Border{Background=Brushes.Black,CornerRadius=new(10),Child=content,IsHitTestVisible=false,Width=expanded?Math.Max(144,scroll.Width):PeekWidth,Height=124,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=expanded?new Thickness(0,app.Settings.Height+10,0,0):new Thickness(0)};
        if(expanded)visuals.Children.Add(shelfDropPreview);else peekViewport.Children.Add(shelfDropPreview);
    }
    private void RemoveShelfDropPreview()
    {
        if(shelfDropPreview?.Parent is Panel parent)parent.Children.Remove(shelfDropPreview);
        shelfDropPreview=null;
    }
    private void FinishFileDrop(bool accepted)
    {
        RemoveShelfDropPreview();dropSignature="";
        if(!fileDragActive)return;fileDragActive=false;
        if(accepted){shelfDropBrowsing=true;return;}
        if(dragWasExpanded)return;
        else if(dragWasPeeking){SelectPeek(dragPreviousPeek);}
        else Collapse();
    }
    private readonly ThumbnailCache shelfThumbnails=new();
    private async Task LoadShelfThumbnail(string path,Image image)
    {
        try
        {
            int pixels=(int)Math.Ceiling(44*VisualTreeHelper.GetDpi(this).DpiScaleX);
            var info=new FileInfo(path);string key=path+"|"+(info.Exists?info.LastWriteTimeUtc.Ticks:0)+"|"+pixels;
            BitmapSource? source=shelfThumbnails.Get(key);if(source!=null){image.Source=source;return;}
            try
            {
            if(new[]{".png",".jpg",".jpeg",".bmp",".gif",".tif",".tiff"}.Contains(Path.GetExtension(path).ToLowerInvariant()) && File.Exists(path) && new FileInfo(path).Length<10*1024*1024)
                source=await Task.Run(()=>{using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=pixels;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;});
            }
            catch { /* An unreadable preview can still use the file's shell icon. */ }
            source??=await AppIconCache.GetAsync(new(){Target=path});if(source!=null)shelfThumbnails.Add(key,source);image.Source=source;
        }catch{image.Source=null;}
    }
    private void BuildFileShelf()
    {
        var p=app.Productivity;shelfSelection.RemoveWhere(path=>!p.Shelf.Any(x=>x.Path.Equals(path,StringComparison.OrdinalIgnoreCase)));
        toolsContent.Children.Add(UI.Heading("File shelf",18));
        var hint=UI.Text("Drop files here. Select several to drag them out together. Originals stay in place.",11,UI.Muted);hint.Margin=new(0,5,0,10);toolsContent.Children.Add(hint);
        var toolbar=new Grid{Margin=new(0,0,0,6)};toolbar.ColumnDefinitions.Add(new());toolbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var count=UI.Text($"{p.Shelf.Count}/40 items",11,UI.Muted);toolbar.Children.Add(count);
        var add=UI.Button($"Add files…  {p.Shelf.Count}/40",()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Multiselect=true,Title="Add to file shelf"};if(dialog.ShowDialog(this)==true)MutateUtility(()=>p.AddFiles(dialog.FileNames));});add.Content="Add files…";add.Padding=new(10,6,10,6);add.MinHeight=30;add.Margin=new(0);add.IsEnabled=p.Shelf.Count<40;Grid.SetColumn(add,1);toolbar.Children.Add(add);toolsContent.Children.Add(toolbar);
        var selectionCount=UI.Text("",10,UI.Muted);Button? selectionAction=null;
        void UpdateSelectionCount()
        {
            selectionCount.Text=shelfSelection.Count>0?$"{shelfSelection.Count} selected":"Select files to drag together";
            if(selectionAction!=null){var label=shelfSelection.Count>0?"Clear selection":"Select all";selectionAction.Content=label;UI.Named(selectionAction,label);}
        }
        UpdateSelectionCount();
        if(p.Shelf.Count>1)
        {
            var actions=new Grid{Margin=new(0,0,0,5)};actions.ColumnDefinitions.Add(new());actions.ColumnDefinitions.Add(new(){Width=GridLength.Auto});actions.Children.Add(selectionCount);
            var all=UI.Button(shelfSelection.Count>0?"Clear selection":"Select all",()=>{if(shelfSelection.Count>0)shelfSelection.Clear();else foreach(var item in p.Shelf.Where(x=>x.Exists))shelfSelection.Add(item.Path);RefreshTools();});selectionAction=all;all.FontSize=10;all.Padding=new(7,4,7,4);all.MinHeight=26;all.Margin=new(0);Grid.SetColumn(all,1);actions.Children.Add(all);toolsContent.Children.Add(actions);
        }
        foreach(var item in p.Shelf.ToArray())
        {
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(28)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(30)});
            var surface=new Border{Child=row,CornerRadius=new(8),Padding=new(5,5,3,5),Margin=new(0,2,0,0)};
            TextBlock? selectedName=null,selectedDetail=null;
            void UpdateSelectionSurface(){if(selectedName!=null)selectedName.Foreground=shelfSelection.Contains(item.Path)?UI.ContrastText(app.Settings.SelectionFill):item.Exists?Brushes.White:UI.Muted;if(selectedDetail!=null)selectedDetail.Foreground=shelfSelection.Contains(item.Path)?UI.ContrastText(app.Settings.SelectionFill):UI.Muted;if(shelfSelection.Contains(item.Path))surface.SetResourceReference(Border.BackgroundProperty,"SelectionFill");else surface.Background=Brushes.Transparent;UpdateSelectionCount();}
            UpdateSelectionSurface();
            var selected=new CheckBox{IsChecked=shelfSelection.Contains(item.Path),IsEnabled=item.Exists,VerticalAlignment=VerticalAlignment.Center,Style=(Style)FindResource("TaskToggle"),MinHeight=32,ToolTip="Select "+item.Name};UI.Named(selected,"Select "+item.Name);selected.Checked+=(_,_)=>{shelfSelection.Add(item.Path);UpdateSelectionSurface();};selected.Unchecked+=(_,_)=>{shelfSelection.Remove(item.Path);UpdateSelectionSurface();};row.Children.Add(selected);
            var contents=new Grid();contents.ColumnDefinitions.Add(new(){Width=new GridLength(58)});contents.ColumnDefinitions.Add(new());var image=new Image{Width=48,Height=48,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};contents.Children.Add(image);_=LoadShelfThumbnail(item.Path,image);
            var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,7,0)};var name=UI.Text(item.Name,12,item.Exists?Brushes.White:UI.Muted);selectedName=name;name.FontWeight=FontWeights.Medium;name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(name);
            var extension=Path.GetExtension(item.Path).TrimStart('.').ToUpperInvariant();var location=Path.GetDirectoryName(item.Path)??item.Path;
            var subline=UI.Text(item.Exists?(string.IsNullOrEmpty(extension)?location:extension+" · "+location):"File not found",10,UI.Muted);selectedDetail=subline;subline.TextWrapping=TextWrapping.NoWrap;subline.TextTrimming=TextTrimming.CharacterEllipsis;subline.Margin=new(0,3,0,0);labels.Children.Add(subline);Grid.SetColumn(labels,1);contents.Children.Add(labels);
            var tile=new Border{Tag="file-drag",Child=contents,Padding=new(0),Background=Brushes.Transparent,ToolTip=item.Path+"\nDrag out · double-click to open"};Point? start=null;
            tile.PreviewMouseLeftButtonDown+=(_,e)=>{start=e.GetPosition(tile);if(e.ClickCount==2){try{ShortcutService.Open(new(){Target=item.Path});}catch(Exception ex){app.Notify("Could not open file",ex.Message);}start=null;e.Handled=true;}};
            tile.PreviewMouseMove+=(_,e)=>
            {
                if(start==null || e.LeftButton!=MouseButtonState.Pressed || !item.Exists)return;var delta=e.GetPosition(tile)-start.Value;
                if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;start=null;
                var paths=(shelfSelection.Contains(item.Path)?p.Shelf.Where(x=>x.Exists && shelfSelection.Contains(x.Path)).Select(x=>x.Path):new[]{item.Path}).ToArray();
                try{if(paths.Length>0)DragDrop.DoDragDrop(tile,new DataObject(DataFormats.FileDrop,paths),DragDropEffects.Copy|DragDropEffects.Link);}finally{FinishFileDrop(false);}
                e.Handled=true;
            };
            Grid.SetColumn(tile,1);row.Children.Add(tile);var remove=UI.Button("Unstage "+item.Name,()=>MutateUtility(()=>p.RemoveFile(item.Path)));remove.Content=Icons.Create("x",14,UI.Muted);remove.ToolTip="Unstage "+item.Name;remove.Width=28;remove.Height=28;remove.MinHeight=28;remove.Margin=new(0);remove.Padding=new(6);remove.Background=Brushes.Transparent;remove.BorderBrush=Brushes.Transparent;remove.BorderThickness=new(1);Grid.SetColumn(remove,2);row.Children.Add(remove);UpdateSelectionSurface();toolsContent.Children.Add(surface);
        }
        if(p.Shelf.Count==0)toolsContent.Children.Add(UI.Text("Your shelf is ready.",12,UI.Muted));
    }
}
