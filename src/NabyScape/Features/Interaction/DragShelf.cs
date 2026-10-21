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
    private bool fileDragActive,dragWasExpanded,dragWasPeeking,dragWasNotice;
    private bool dragWasAssistantPeek;
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
                FinishFileDrop(true);
            }
            e.Handled=true;
        };
    }
    private void BeginFileDrop()
    {
        if(userHidden || fileDragActive)return;
        fileDragActive=true;dragWasExpanded=expanded;dragWasPeeking=peeking;dragPreviousPeek=peekId;
        dragWasAssistantPeek=assistantPeeking;
        if(assistantPeeking){HideAssistantPeek();assistantPeek.Visibility=Visibility.Collapsed;peeking=false;}
        dragWasNotice=noticeShowing;
        StopHoverTimers();EndPeekGesture();
        if(dragWasNotice){HideNotice();peeking=false;}
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
        if(accepted){dragWasAssistantPeek=false;dragWasNotice=false;shelfDropBrowsing=true;if(peeking){SelectPeek("files");UpdatePeekDeck();}else ShowTools("files");return;}
        if(dragWasNotice){dragWasNotice=false;Collapse();OpenNotice();return;}
        if(dragWasExpanded)return;
        if(dragWasAssistantPeek){dragWasAssistantPeek=false;Collapse();PreviewAssistant();return;}
        else if(dragWasPeeking){SelectPeek(dragPreviousPeek);}
        else Collapse();
    }
    private ThumbnailCache shelfThumbnails=>app.Productivity.DisplayThumbnails;
}
