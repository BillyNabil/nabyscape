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
    private bool CanShelfDrop(DragEventArgs e)=>!userHidden && WidgetOn("tools") && e.Data.GetDataPresent(DataFormats.FileDrop) && (app.Settings.DropReveal || expanded && toolsMode=="files" || peeking && peekId=="files");
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
    private static async Task LoadShelfThumbnail(string path,Image image)
    {
        try
        {
            BitmapSource? source=null;
            if(new[]{".png",".jpg",".jpeg",".bmp"}.Contains(Path.GetExtension(path).ToLowerInvariant()) && File.Exists(path) && new FileInfo(path).Length<10*1024*1024)
                source=await Task.Run(()=>{using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=80;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;});
            source??=await AppIconCache.GetAsync(new(){Target=path});image.Source=source;
        }catch{image.Source=null;}
    }
    private void BuildFileShelf()
    {
        var p=app.Productivity;shelfSelection.RemoveWhere(path=>!p.Shelf.Any(x=>x.Path.Equals(path,StringComparison.OrdinalIgnoreCase)));
        toolsContent.Children.Add(UI.Heading("File shelf",18));toolsContent.Children.Add(UI.Text("Drop files here. Select several to drag them out together. Originals stay in place.",11,UI.Muted));
        var add=UI.Button($"Add files…  {p.Shelf.Count}/40",()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Multiselect=true,Title="Add to file shelf"};if(dialog.ShowDialog(this)==true)MutateUtility(()=>p.AddFiles(dialog.FileNames));});add.IsEnabled=p.Shelf.Count<40;toolsContent.Children.Add(add);
        var actions=UI.Row(UI.Button("Select all",()=>{foreach(var item in p.Shelf.Where(x=>x.Exists))shelfSelection.Add(item.Path);RefreshTools();}),UI.Button("Clear selection",()=>{shelfSelection.Clear();RefreshTools();}));toolsContent.Children.Add(actions);
        foreach(var item in p.Shelf.ToArray())
        {
            var row=new Grid{Margin=new(0,9,0,0)};row.ColumnDefinitions.Add(new(){Width=new GridLength(26)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(32)});
            var selected=new CheckBox{IsChecked=shelfSelection.Contains(item.Path),IsEnabled=item.Exists,VerticalAlignment=VerticalAlignment.Center};UI.Named(selected,"Select "+item.Name);selected.Checked+=(_,_)=>shelfSelection.Add(item.Path);selected.Unchecked+=(_,_)=>shelfSelection.Remove(item.Path);row.Children.Add(selected);
            var contents=new Grid();contents.ColumnDefinitions.Add(new(){Width=new GridLength(34)});contents.ColumnDefinitions.Add(new());var image=new Image{Width=26,Height=26,Stretch=Stretch.Uniform};contents.Children.Add(image);_=LoadShelfThumbnail(item.Path,image);
            var labels=new StackPanel();var name=UI.Text(item.Name,12,item.Exists?Brushes.White:UI.Muted);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(name);labels.Children.Add(UI.Text(item.Exists?"Drag out · double-click to open":"File not found",10,UI.Muted));Grid.SetColumn(labels,1);contents.Children.Add(labels);
            var tile=new Border{Child=contents,Padding=new(7,5,7,5),Background=Brushes.Transparent,ToolTip=item.Path};Point? start=null;
            tile.PreviewMouseLeftButtonDown+=(_,e)=>{start=e.GetPosition(tile);if(e.ClickCount==2){try{ShortcutService.Open(new(){Target=item.Path});}catch(Exception ex){app.Notify("Could not open file",ex.Message);}start=null;e.Handled=true;}};
            tile.PreviewMouseMove+=(_,e)=>
            {
                if(start==null || e.LeftButton!=MouseButtonState.Pressed || !item.Exists)return;var delta=e.GetPosition(tile)-start.Value;
                if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;start=null;
                var paths=(shelfSelection.Contains(item.Path)?p.Shelf.Where(x=>x.Exists && shelfSelection.Contains(x.Path)).Select(x=>x.Path):new[]{item.Path}).ToArray();
                try{if(paths.Length>0)DragDrop.DoDragDrop(tile,new DataObject(DataFormats.FileDrop,paths),DragDropEffects.Copy|DragDropEffects.Link);}finally{FinishFileDrop(false);}
                e.Handled=true;
            };
            Grid.SetColumn(tile,1);row.Children.Add(tile);var remove=UI.Button("×",()=>MutateUtility(()=>p.RemoveFile(item.Path)));UI.Named(remove,"Unstage "+item.Name);remove.Padding=new(5);Grid.SetColumn(remove,2);row.Children.Add(remove);toolsContent.Children.Add(row);
        }
        if(p.Shelf.Count==0)toolsContent.Children.Add(UI.Text("Your shelf is ready.",12,UI.Muted));
    }
}
