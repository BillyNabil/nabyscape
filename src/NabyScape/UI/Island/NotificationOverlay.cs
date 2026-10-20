using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;
public sealed partial class OverlayWindow
{
    private Border? noticeCard;
    private bool noticeShowing;
    private Border? noticeEdge;
    private TextBlock? noticeQueue;
    private Button? noticeNext;
    private void RefreshNoticeEdge()
    {
        if(noticeEdge==null){noticeEdge=new(){Width=18,Height=2,CornerRadius=new(1),Background=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};visuals.Children.Add(noticeEdge);}
        noticeEdge.Margin=new(0,app.Settings.Height-3,0,0);noticeEdge.Visibility=IsTucked && app.Notifications.Current!=null?Visibility.Visible:Visibility.Collapsed;
    }
    internal bool ManuallyHidden=>userHidden;
    internal bool NotificationBusy=>expanded || peeking || FeedbackVisible || fileDragActive || PeekInteractionActive || PointerInside();
    internal void RefreshNotice()
    {
        if(!IsLoaded)return;
        RefreshNoticeEdge();
        if(noticeQueue!=null)noticeQueue.Text=app.Notifications.Count>1?app.Notifications.Count+" queued":"";
        if(noticeNext!=null)noticeNext.IsEnabled=app.Notifications.Count>1;
        if(app.Notifications.Current==null && noticeShowing)Collapse();
        // Never replace controls under a pointer, keyboard editor, or active drag.
        app.PublishUpdate(RuntimeChange.Clock);
    }
    private void HideNotice(){noticeShowing=false;if(noticeCard!=null)noticeCard.Visibility=Visibility.Collapsed;}
    internal void OpenNotice(bool requested=false)
    {
        var notice=app.Notifications.Current;
        if(requested && expanded)Collapse();
        if(notice==null || userHidden || expanded || fileDragActive || PeekInteractionActive || FeedbackVisible)return;
        if(peeking && !noticeShowing)return;
        if(noticeCard!=null)visuals.Children.Remove(noticeCard);
        app.Notifications.MarkDisplayed();
        StopHoverTimers();SetTucked(false);peeking=true;noticeShowing=true;peekPanel.Visibility=Visibility.Collapsed;SyncAssistantVisibility();
        var content=new StackPanel();
        var header=new DockPanel{Margin=new(0,0,0,12)};
        noticeQueue=UI.Text(app.Notifications.Count>1?app.Notifications.Count+" queued":"",10,UI.Muted);DockPanel.SetDock(noticeQueue,Dock.Right);header.Children.Add(noticeQueue);
        var kind=UI.Text(notice.Kind switch{"timer"=>"TIMER","focus"=>"FOCUS","downloads"=>"DOWNLOADS","updates"=>"UPDATE",_=>"NABYSCAPE"},10,UI.Muted);
        var icon=new Border{Width=28,Height=28,CornerRadius=new(9),Background=UI.Brush("#19FFFFFF"),Margin=new(0,0,9,0),Child=Icons.Create(notice.Kind switch{"timer"=>"timer","focus"=>"focus","downloads"=>"shelf","updates"=>"settings",_=>"nav-status"},14)};
        header.Children.Add(icon);header.Children.Add(kind);content.Children.Add(header);
        var noticeTitle=UI.Heading(notice.Title,18);noticeTitle.FontWeight=FontWeights.SemiBold;noticeTitle.Margin=new(0,0,0,6);content.Children.Add(noticeTitle);
        var detail=UI.Text(notice.Detail,12,UI.Muted);detail.LineHeight=18;content.Children.Add(detail);
        var accent=new LinearGradientBrush(UI.Brush("#C4C9D2").Color,UI.Brush("#30343C").Color,0);accent.Freeze();content.Children.Add(new Border{Height=2,CornerRadius=new(1),Background=accent,Margin=new(0,15,0,4)});
        string[] paths=(notice.Files??Array.Empty<string>()).Where(File.Exists).Take(40).ToArray();
        if(paths.Length>0)
        {
            var row=new DockPanel{Margin=new(0,10,0,8)};var image=new Image{Width=32,Height=32,Margin=new(0,0,10,0)};row.Children.Add(image);_=LoadShelfThumbnail(paths[0],image);
            var drag=UI.Button(Path.GetFileName(paths[0])+(paths.Length>1?$" +{paths.Length-1}":""),()=>{});drag.Content=new TextBlock{Text=Path.GetFileName(paths[0])+(paths.Length>1?$" +{paths.Length-1}":""),TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};drag.ToolTip="Drag original files · use Show in folder or Add to shelf with keyboard";row.Children.Add(drag);content.Children.Add(row);
            Point? down=null;drag.PreviewMouseLeftButtonDown+=(_,e)=>down=e.GetPosition(drag);
            drag.PreviewMouseMove+=(_,e)=>
            {
                if(down==null || e.LeftButton!=MouseButtonState.Pressed)return;var delta=e.GetPosition(drag)-down.Value;
                if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                down=null;e.Handled=true;fileDragActive=true;peekSuppressClick=true;
                try{DragDrop.DoDragDrop(drag,new DataObject(DataFormats.FileDrop,paths.Where(File.Exists).ToArray()),DragDropEffects.Copy|DragDropEffects.Link);}
                finally{fileDragActive=false;down=null;}
            };
            drag.PreviewMouseLeftButtonUp+=(_,e)=>{down=null;e.Handled=true;};
            content.Children.Add(UI.Row(UI.Button("Show in folder",()=>
            {
                try{if(!File.Exists(paths[0]))return;var info=new System.Diagnostics.ProcessStartInfo("explorer.exe"){UseShellExecute=false};info.ArgumentList.Add("/select,");info.ArgumentList.Add(paths[0]);System.Diagnostics.Process.Start(info);}
                catch(Exception ex){app.Store.Log(ex);app.Notify("Could not show file",ex.Message);}
            }),UI.Button("Add to shelf",()=>{try{app.Productivity.AddFiles(paths.Where(File.Exists));}catch(Exception ex){app.Store.Log(ex);app.Notify("Could not stage files",ex.Message);}})));
        }
        else if(notice.Kind=="downloads")content.Children.Add(UI.Text("These files are no longer available.",11,UI.Muted));
        if(notice.Kind=="updates")content.Children.Add(UI.Button("Open Settings",app.ShowSettings));
        var actions=new WrapPanel{Margin=new(0,10,0,0)};
        var dismiss=UI.Button("Dismiss",()=>{Collapse();app.Notifications.Dismiss();});
        noticeNext=UI.Button("Next",()=>{Collapse();app.Notifications.Dismiss();OpenNotice(true);});noticeNext.IsEnabled=app.Notifications.Count>1;
        var clear=UI.Button("Clear all",()=>{Collapse();app.Notifications.Clear();});
        foreach(var action in new[]{dismiss,noticeNext,clear}){action.FontSize=11;action.MinHeight=30;action.Padding=new(9,4,9,4);action.Margin=new(0,0,6,0);actions.Children.Add(action);}content.Children.Add(actions);
        var screen=Native.ChooseScreen(app.Settings);double scale=Native.Scale(screen);
        double width=Math.Min(Math.Min(370,app.Settings.HoverWidth),Math.Max(180,screen.WorkingArea.Width/scale-24));
        var viewport=new ScrollViewer{Content=content,MaxHeight=Math.Max(100,Math.Min(370,screen.WorkingArea.Height/scale-48)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        noticeCard=new(){Child=viewport,Width=width-(surface.Notch?64:32),Margin=new(0,16,0,0),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};
        visuals.Children.Add(noticeCard);compact.IsHitTestVisible=false;compact.Opacity=0;Show();Dimensions();
        viewport.Measure(new Size(noticeCard.Width,double.PositiveInfinity));
        motion.Go(surface,IslandSurface.SurfaceWidthProperty,width,app.Settings.AnimationMs);
        motion.Go(surface,IslandSurface.SurfaceHeightProperty,viewport.DesiredSize.Height+32,app.Settings.AnimationMs);
        ControlFeedback.Notice(noticeCard,app.Settings.NotificationAnimation);
    }
}
