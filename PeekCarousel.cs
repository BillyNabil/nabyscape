using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private double PeekExpandedHeight=>app.Settings.HoverHeight;
    private double PeekWidth=>Math.Min(app.Settings.HoverWidth,app.Settings.PanelWidth)-64;
    private readonly Grid peekMedia=new(),peekViewport=new(){ClipToBounds=true,Height=124,Background=Brushes.Transparent};
    private readonly Dictionary<string,FrameworkElement> peekViews=new();
    private readonly Dictionary<string,TranslateTransform> peekMoves=new();
    private readonly Dictionary<string,Button> peekDots=new();
    private readonly TextBlock peekPageName=UI.Text("Music",10,UI.Muted);
    private readonly Dictionary<string,(TextBlock time,TextBlock detail,Button toggle,ProgressBar progress)> peekClocks=new();
    private readonly TextBlock peekEventTitle=UI.Text("",16),peekEventTime=UI.Text("",11,UI.Muted),peekShelfName=UI.Text("",14),peekShelfCount=UI.Text("",11,UI.Muted);
    private TextBox peekNote=null!;
    private Button peekPrevious=null!,peekNext=null!,peekJoin=null!,peekShelfFile=null!,peekMute=null!,peekTrackPrevious=null!,peekTrackNext=null!,peekOutput=null!;
    private Slider peekVolume=null!;
    private string peekId="media";
    private StackPanel peekDotStrip=null!;
    private Point? peekDown;
    private bool peekDragging,peekTouching,peekTouchBlocked;
    private double peekOffset,peekDragOrigin;
    private readonly PeekWheelAccumulator peekWheel=new();
    private Grid peekFooter=null!;
    private bool peekSuppressClick;
    internal bool PeekDetailed=>peekDetailed;
    internal Rect PeekContentBounds=>new((ActualWidth-PeekWidth)/2,peekPanel.Margin.Top,PeekWidth,peekPanel.Height);
    private CalendarEvent? peekEvent;
    private string? peekFilePath;
    private string? peekShelfArtPath;
    private readonly Image peekShelfArt=new(){Width=44,Height=44,Stretch=Stretch.Uniform,Margin=new(0,0,12,0),Visibility=Visibility.Collapsed};
    private bool PeekInteractionActive=>peekDragging || peekTouching || peekNote?.IsKeyboardFocusWithin==true || audioMenu?.IsOpen==true;
    internal string PeekPage=>peekId;
    internal string[] HoverDeckIds=>PeekPages();
    internal bool PeekTabSafe=>peekViews.Where(pair=>pair.Key!=peekId).All(pair=>KeyboardNavigation.GetTabNavigation(pair.Value)==KeyboardNavigationMode.None);
    internal bool PeekMoving=>peekMoves.Values.Any(move=>move.HasAnimatedProperties);
    private string[] PeekPages()=>app.Settings.HoverViews.Where(view=>(view.Enabled || satellitePreview && view.Id==SatellitePeekKind || view.Id=="files" && (fileDragActive || shelfDropBrowsing)) && (view.Id=="system" && satellitePreview && satelliteKind=="clock" || (view.Id=="media"?(WidgetOn("media") || WidgetOn("volume")):WidgetOn(view.Id is "calendar" or "notes" or "files"?"tools":view.Id)))).Select(view=>view.Id).ToArray();
    private static string PeekName(string id)=>id switch{"media"=>"Music","timer"=>"Timer","focus"=>"Focus","calendar"=>"Calendar","notes"=>"Note","system"=>"System",_=>"Shelf"};
    private void InitializePeekDeck()
    {
        peekPanel.HorizontalAlignment=HorizontalAlignment.Center;peekPanel.VerticalAlignment=VerticalAlignment.Top;peekPanel.Width=PeekWidth;peekPanel.Height=148;peekPanel.Margin=new(0,20,0,0);peekPanel.Visibility=Visibility.Collapsed;
        peekPanel.RowDefinitions.Add(new(){Height=new GridLength(124)});peekPanel.RowDefinitions.Add(new(){Height=new GridLength(24)});peekPanel.Children.Add(peekViewport);
        peekMedia.Height=124;peekMedia.Width=PeekWidth;peekMedia.Visibility=Visibility.Visible;peekMedia.RowDefinitions.Add(new(){Height=new GridLength(32)});
        var controls=new Grid{Margin=new(0,2,0,0)};controls.ColumnDefinitions.Add(new(){Width=new GridLength(34)});controls.ColumnDefinitions.Add(new(){Width=new GridLength(34)});controls.ColumnDefinitions.Add(new(){Width=new GridLength(34)});controls.ColumnDefinitions.Add(new());controls.ColumnDefinitions.Add(new(){Width=new GridLength(28)});
        controls.ColumnDefinitions.Add(new(){Width=new GridLength(28)});
        var previous=PeekIcon("previous","Previous track in preview",()=>_=app.Media.Command("previous"));var next=PeekIcon("next","Next track in preview",()=>_=app.Media.Command("next"));var mute=PeekIcon("volume","Mute preview audio",()=>{app.Audio.ToggleMute();app.Audio.Read();Update();});var output=PeekIcon("chevron","Choose preview audio output",OpenAudioMenu);
        peekTrackPrevious=previous;peekTrackNext=next;peekMute=mute;peekOutput=output;controls.Children.Add(previous);Grid.SetColumn(next,1);controls.Children.Add(next);Grid.SetColumn(mute,2);controls.Children.Add(mute);
        peekVolume=new Slider{Minimum=0,Maximum=100,Height=26,Margin=new(7,0,9,0),VerticalAlignment=VerticalAlignment.Center};UI.Named(peekVolume,"Preview volume");peekVolume.ValueChanged+=(_,_)=>{if(!updating){app.Audio.SetVolume(peekVolume.Value/100);app.Audio.Read();}};Grid.SetColumn(peekVolume,3);controls.Children.Add(peekVolume);Grid.SetColumn(output,4);controls.Children.Add(output);var mixerButton=PeekIcon("mixer","Open mixer from preview",OpenMixer);Grid.SetColumn(mixerButton,5);controls.Children.Add(mixerButton);Grid.SetRow(controls,3);Grid.SetColumnSpan(controls,3);peekMedia.Children.Add(controls);AddPeekView("media",peekMedia);
        AddPeekClock("timer");AddPeekClock("focus");AddPeekView("calendar",BuildPeekCalendar());AddPeekView("notes",BuildPeekNote());AddPeekView("files",BuildPeekShelf());AddPeekView("system",BuildPeekSystem());
        var footer=new Grid{Margin=new(0,2,0,0)};peekFooter=footer;footer.ColumnDefinitions.Add(new(){Width=new GridLength(50)});footer.ColumnDefinitions.Add(new());footer.ColumnDefinitions.Add(new(){Width=new GridLength(48)});footer.ColumnDefinitions.Add(new(){Width=new GridLength(74)});footer.Children.Add(peekPageName);
        var dots=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};peekDotStrip=dots;Grid.SetColumn(dots,1);footer.Children.Add(dots);
        foreach(var id in new[]{"media","timer","focus","calendar","notes","files","system"})
        {
            var dot=PlainPeekButton("Show "+PeekName(id)+" preview",()=>SelectPeek(id));dot.Width=14;dot.Height=22;dot.Content=new Border{Width=4,Height=4,CornerRadius=new(2),Background=Brushes.White,Opacity=.3};peekDots[id]=dot;dots.Children.Add(dot);
        }
        var arrows=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};peekPrevious=PeekArrow(false,()=>NavigatePeek(-1));peekNext=PeekArrow(true,()=>NavigatePeek(1));arrows.Children.Add(peekPrevious);arrows.Children.Add(peekNext);Grid.SetColumn(arrows,2);footer.Children.Add(arrows);var detachTools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var handle=new Thumb{Width=18,Height=22,Cursor=Cursors.SizeAll,Background=Brushes.Transparent,ToolTip="Drag to detach this preview"};var grip=new FrameworkElementFactory(typeof(Border));grip.SetValue(WidthProperty,12d);grip.SetValue(HeightProperty,3d);grip.SetValue(Border.CornerRadiusProperty,new CornerRadius(1.5));grip.SetValue(Border.BackgroundProperty,UI.Brush("#8B8D94"));handle.Template=new ControlTemplate(typeof(Thumb)){VisualTree=grip};UI.Named(handle,"Drag preview out");Point detachStart=default;bool detached=false;handle.DragStarted+=(_,_)=>{detachStart=Mouse.GetPosition(this);detached=false;};handle.DragDelta+=(_,_)=>{if(!detached && (Mouse.GetPosition(this)-detachStart).Length>10){detached=true;var location=PointToScreen(Mouse.GetPosition(this));var kind=peekId;handle.CancelDrag();Collapse();app.Previews?.OpenAt(kind,location,true);}};detachTools.Children.Add(handle);
        var detach=PlainPeekButton("Detach preview",()=>{var location=PointToScreen(new Point((ActualWidth-PeekWidth)/2+24,app.Settings.Height+24));var kind=peekId;Collapse();app.Previews?.OpenAt(kind,location,false);});detach.Content="Detach";detach.FontSize=10;detach.Width=52;detachTools.Children.Add(detach);Grid.SetColumn(detachTools,3);footer.Children.Add(detachTools);detachTools.Tag="detach";
        Grid.SetRow(footer,1);peekPanel.Children.Add(footer);visuals.Children.Add(peekPanel);
        peekPanel.Focusable=true;peekPanel.PreviewMouseLeftButtonDown+=(_,e)=>{if(peeking){Activate();if(!PeekGestureBlocked(e.OriginalSource as DependencyObject))peekPanel.Focus();}};
        peekViewport.PreviewTouchDown+=(_,e)=>peekTouchBlocked=PeekGestureBlocked(e.OriginalSource as DependencyObject);
        peekViewport.PreviewMouseLeftButtonDown+=(_,e)=>{if(PeekGestureBlocked(e.OriginalSource as DependencyObject))return;peekSuppressClick=false;peekDown=e.GetPosition(peekViewport);peekDragOrigin=peekMoves[peekId].X;};
        peekViewport.PreviewMouseMove+=(_,e)=>
        {
            if(!peekDown.HasValue || e.LeftButton!=MouseButtonState.Pressed)return;var delta=e.GetPosition(peekViewport)-peekDown.Value;
            if(!peekDragging){if(Math.Abs(delta.Y)>10 && Math.Abs(delta.Y)>Math.Abs(delta.X)){peekDown=null;return;}if(Math.Abs(delta.X)<8)return;peekDragging=true;peekSuppressClick=true;peekViewport.CaptureMouse();PromotePeek();}
            DragPeek(delta.X+peekDragOrigin);e.Handled=true;
        };
        peekViewport.PreviewMouseLeftButtonUp+=(_,e)=>{if(!peekDragging){peekDown=null;return;}double offset=peekOffset;peekDragging=false;peekDown=null;peekViewport.ReleaseMouseCapture();FinishPeekSwipe(offset);e.Handled=true;};
        peekViewport.LostMouseCapture+=(_,_)=>{if(peekDragging){peekDragging=false;peekDown=null;LayoutPeek(0,true);}};
        peekViewport.IsManipulationEnabled=true;
        peekViewport.ManipulationStarting+=(_,e)=>{if(peekTouchBlocked || PeekGestureBlocked(e.OriginalSource as DependencyObject)){e.Cancel();return;}peekTouching=true;peekSuppressClick=true;peekOffset=0;e.ManipulationContainer=peekViewport;e.Mode=ManipulationModes.TranslateX;PromotePeek();e.Handled=true;};
        peekViewport.ManipulationDelta+=(_,e)=>{if(peekTouching){DragPeek(e.CumulativeManipulation.Translation.X);e.Handled=true;}};
        peekViewport.ManipulationCompleted+=(_,e)=>{if(peekTouching){peekTouching=false;FinishPeekSwipe(peekOffset);e.Handled=true;}};
        peekViewport.MouseLeftButtonUp+=(_,e)=>{if(peekSuppressClick){peekSuppressClick=false;e.Handled=true;return;}if(!PeekGestureBlocked(e.OriginalSource as DependencyObject)){OpenPeekPage();e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>{if(!peeking || Keyboard.FocusedElement is TextBox or Slider)return;if(e.Key is Key.Left or Key.Right){NavigatePeek(e.Key==Key.Right?1:-1);e.Handled=true;}};
    }
    internal static bool PeekGestureBlocked(DependencyObject? source)
    {
        for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node is ButtonBase or TextBoxBase or Slider)return true;return false;
    }
    private Button PlainPeekButton(string name,Action action)
    {var button=UI.Button(name,action);button.Template=(ControlTemplate)FindResource("ToolRowTemplate");button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.Padding=new(3);button.Margin=new(0);button.MinHeight=22;button.HorizontalContentAlignment=HorizontalAlignment.Center;button.MouseEnter+=(_,_)=>PeekButtonLight(button,.075);button.MouseLeave+=(_,_)=>PeekButtonLight(button,button.IsKeyboardFocusWithin?.075:0);button.GotKeyboardFocus+=(_,_)=>PeekButtonLight(button,.075);button.LostKeyboardFocus+=(_,_)=>PeekButtonLight(button,button.IsMouseOver?.075:0);return button;}
    private static void PeekButtonLight(Button button,double opacity){if(button.Template.FindName("RowLight",button) is Border light)Motion.Fade(light,opacity);}
    private Button PeekIcon(string icon,string name,Action action)
    {var button=PlainPeekButton(name,action);button.Content=Icons.Create(icon,14);button.Width=28;button.Height=28;button.ToolTip=name;return button;}
    private Button PeekArrow(bool right,Action action)
    {var button=PlainPeekButton(right?"Next preview":"Previous preview",action);button.Width=24;button.Height=22;button.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse(right?"M1,1 L5,5 1,9":"M5,1 L1,5 5,9"),Stroke=UI.Brush("#BFC2CA"),StrokeThickness=1.3,Width=5,Height=9};button.ToolTip=right?"Next · drag left":"Previous · drag right";return button;}
    private void AddPeekView(string id,FrameworkElement view)
    {view.Width=PeekWidth;view.Height=124;view.Visibility=Visibility.Collapsed;var move=new TranslateTransform();view.RenderTransform=move;peekMoves[id]=move;peekViews[id]=view;peekViewport.Children.Add(view);}
    private void PreparePeekDeck()
    {
        EndPeekGesture();var ids=PeekPages();if(ids.Length==0)return;
        peekPanel.Width=peekViewport.Width=PeekWidth;foreach(var view in peekViews.Values)view.Width=PeekWidth;ApplyPeekGeometry();hoverDelay.Interval=TimeSpan.FromMilliseconds(app.Settings.HoverDelayMs);detailDelay.Interval=TimeSpan.FromMilliseconds(app.Settings.HoverDetailDelayMs);
        if(automaticPreview && ids.Contains(previewKind))peekId=previewKind;
        else if(!ids.Contains(peekId))peekId=ids[0];
        peekDotStrip.Children.Clear();foreach(var view in app.Settings.HoverViews)peekDotStrip.Children.Add(peekDots[view.Id]);
        LayoutPeek(0,false);UpdatePeekDeck();
        outside?.Dispose();outside=DiagnosticRun?null:new OutsideClick(this,Collapse,p=>surface.Contour.FillContains(p) || audioMenu?.IsOpen==true);
    }
    private void PromotePeek()
    {if(!peeking)return;detailDelay.Stop();peekDetailed=true;automaticPreview=false;activityDismiss.Stop();ApplyPeekGeometry();motion.Go(surface,IslandSurface.SurfaceHeightProperty,PeekExpandedHeight,app.Settings.AnimationMs);}
    internal void SelectPeek(string id)
    {if(!peeking || !PeekPages().Contains(id))return;PromotePeek();var ids=PeekPages();int old=Array.IndexOf(ids,peekId),next=Array.IndexOf(ids,id);double start=(next-old)*PeekWidth;if(Math.Abs(next-old)>1){peekId=id;LayoutPeek(0,false);peekViews[id].Opacity=.65;Motion.Fade(peekViews[id],1,160);UpdatePeekDeck();return;}if(next==old){LayoutPeek(0,true);return;}peekId=id;LayoutPeek(start,false);UpdatePeekDeck();LayoutPeek(0,true);WakeLiveRendering();}
    internal void NavigatePeek(int direction)
    {var ids=PeekPages();int index=Array.IndexOf(ids,peekId);if(ids.Length>0)SelectPeek(ids[Math.Clamp(index+Math.Sign(direction),0,ids.Length-1)]);}
    private void LayoutPeek(double offset,bool animate)
    {
        var ids=PeekPages();int index=Array.IndexOf(ids,peekId);
        foreach(var pair in peekViews)
        {
            int i=Array.IndexOf(ids,pair.Key);double x=(i-index)*PeekWidth+offset;pair.Value.Visibility=i>=0 && (Math.Abs(x)<PeekWidth-.1 || (animate && Math.Abs(peekMoves[pair.Key].X)<PeekWidth-.1))?Visibility.Visible:Visibility.Collapsed;pair.Value.IsHitTestVisible=pair.Key==peekId;KeyboardNavigation.SetTabNavigation(pair.Value,pair.Key==peekId?KeyboardNavigationMode.Continue:KeyboardNavigationMode.None);if(pair.Key!=peekId && pair.Value.IsKeyboardFocusWithin)peekPanel.Focus();
            if(animate)Motion.To(peekMoves[pair.Key],TranslateTransform.XProperty,x,300);else{peekMoves[pair.Key].BeginAnimation(TranslateTransform.XProperty,null);peekMoves[pair.Key].X=x;}
        }
        foreach(var pair in peekDots){pair.Value.Visibility=ids.Contains(pair.Key)?Visibility.Visible:Visibility.Collapsed;if(pair.Value.Content is Border dot){dot.Width=pair.Key==peekId?13:4;Motion.Fade(dot,pair.Key==peekId?.9:.25,180);}}
        peekPageName.Text=PeekName(peekId);peekPrevious.IsEnabled=index>0;peekNext.IsEnabled=index<ids.Length-1;peekPrevious.Opacity=peekPrevious.IsEnabled?1:.25;peekNext.Opacity=peekNext.IsEnabled?1:.25;
    }
    internal void DragPeek(double distance)
    {var ids=PeekPages();int index=Array.IndexOf(ids,peekId);peekOffset=Math.Clamp(distance,-PeekWidth,PeekWidth);if((index==0 && distance>0)||(index==ids.Length-1 && distance<0))peekOffset*=.22;LayoutPeek(Motion.Enabled?peekOffset:0,false);}
    internal void FinishPeekSwipe(double offset)
    {var ids=PeekPages();int index=Array.IndexOf(ids,peekId);int next=Math.Clamp(index+(offset < -app.Settings.SwipeDistance?1:offset>app.Settings.SwipeDistance?-1:0),0,Math.Max(0,ids.Length-1));if(ids.Length==0)return;peekId=ids[next];LayoutPeek(offset+(next-index)*PeekWidth,false);UpdatePeekDeck();LayoutPeek(0,true);peekOffset=0;WakeLiveRendering();if(!DiagnosticRun && !PointerInside() && !PeekInteractionActive)leaveDelay.Start();}
    private void EndPeekGesture(){peekDown=null;peekDragging=peekTouching=false;peekOffset=0;peekWheel.Reset();if(peekViewport.IsMouseCaptured)peekViewport.ReleaseMouseCapture();}
    internal bool RoutePeekWheel(int delta,bool horizontal,DependencyObject? source)
    {
        if(!peeking)return false;
        // Only the focused multiline editor keeps its native vertical scrolling.
        if(!VisibilityPolicy.CarouselOwnsWheel(peeking,horizontal,peekNote.IsKeyboardFocusWithin,IsPeekNoteSource(source)))return false;
        int direction=peekWheel.Push(horizontal?delta:-delta,Environment.TickCount64);
        if(direction!=0)NavigatePeek(direction);
        return true;
    }
    private bool IsPeekNoteSource(DependencyObject? source)
    {for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node==peekNote)return true;return false;}
    private void ApplyPeekGeometry()
    {
        double height=peekDetailed?PeekExpandedHeight:100;
        var safe=PeekLayout.SafeBounds(Math.Min(app.Settings.HoverWidth,expandedWidth),height,app.Settings.Radius,surface.Notch);
        peekPanel.Width=peekViewport.Width=safe.Width;peekPanel.Height=safe.Height;
        peekPanel.Margin=new(0,safe.Top,0,0);
        double contentHeight=peekDetailed?124:Math.Min(64,safe.Height);
        peekFooter.ColumnDefinitions[0].Width=new(peekDetailed && safe.Width>=270?50:0);peekPageName.Visibility=safe.Width>=270?Visibility.Visible:Visibility.Collapsed;
        peekFooter.ColumnDefinitions[3].Width=new(app.Settings.DetachablePreviews?74:0);foreach(FrameworkElement child in peekFooter.Children)if(child.Tag as string=="detach")child.Visibility=app.Settings.DetachablePreviews?Visibility.Visible:Visibility.Collapsed;
        peekViewport.Height=contentHeight;peekPanel.RowDefinitions[0].Height=new(contentHeight);
        peekPanel.RowDefinitions[1].Height=new(peekDetailed?24:0);peekFooter.Visibility=peekDetailed?Visibility.Visible:Visibility.Collapsed;
        foreach(var pair in peekViews)
        {
            pair.Value.Width=safe.Width;pair.Value.Height=contentHeight;
            if(pair.Key is "timer" or "focus" && pair.Value is Grid clock){clock.RowDefinitions[0].Height=new(peekDetailed?24:18);clock.RowDefinitions[1].Height=new(peekDetailed?48:46);}
            if(pair.Value is Panel page)foreach(FrameworkElement child in page.Children)
            {
                bool primary=pair.Key=="media"?Grid.GetRow(child)==0:pair.Key is "timer" or "focus"?Grid.GetRow(child)<2:page.Children.IndexOf(child)<(pair.Key=="notes"?1:2);
                if(!primary){if(!peekDetailed)child.Visibility=Visibility.Hidden;else if(child.Visibility==Visibility.Hidden)child.Visibility=Visibility.Visible;}
            }
        }
        // Media metadata keeps a flexible title column even for very long tracks.
        peekTrackPrevious.Visibility=peekTrackNext.Visibility=Visibility.Visible;
        if(peekViews["media"] is Grid media && media.Children.OfType<Grid>().LastOrDefault() is Grid controls)
        {bool narrow=safe.Width<260;controls.ColumnDefinitions[0].Width=new(narrow?0:34);controls.ColumnDefinitions[1].Width=new(narrow?0:34);peekTrackPrevious.Visibility=peekTrackNext.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;}
    }
    private void OpenPeekPage()
    {if(peekId is "notes" or "calendar" or "files"){if(peekId=="notes")notesMode="write";ShowTools(peekId);}else{Reveal();SelectPage(peekId=="system"?"status":peekId=="media"?"media":"focus");}}
    private void AddPeekClock(string id)
    {
        var page=new Grid();page.RowDefinitions.Add(new(){Height=new GridLength(24)});page.RowDefinitions.Add(new(){Height=new GridLength(48)});page.RowDefinitions.Add(new(){Height=new GridLength(12)});page.RowDefinitions.Add(new(){Height=new GridLength(32)});
        var detail=UI.Text(id=="timer"?"Timer":"Focus",11,UI.Muted);page.Children.Add(detail);var time=UI.Text("",32);time.FontWeight=FontWeights.Medium;Typography.SetNumeralAlignment(time,FontNumeralAlignment.Tabular);Grid.SetRow(time,1);page.Children.Add(time);
        var bar=new ProgressBar{Minimum=0,Maximum=1,Height=2,Background=UI.Brush("#35FFFFFF"),Foreground=UI.Brush("#DCDAD5"),BorderThickness=new(0)};Grid.SetRow(bar,2);page.Children.Add(bar);
        var actions=new DockPanel{Margin=new(0,4,0,0)};var open=PlainPeekButton(id=="timer"?"Edit timer":"Edit focus",OpenPeekPage);open.Content="Edit";open.FontSize=11;open.Foreground=UI.Muted;DockPanel.SetDock(open,Dock.Right);actions.Children.Add(open);
        var toggle=PeekIcon("play",id=="timer"?"Start or pause preview timer":"Start or pause preview focus",()=>TogglePeekClock(id));toggle.Background=Brushes.Transparent;actions.Children.Add(toggle);
        var more=PlainPeekButton(id=="timer"?"Add five minutes to preview timer":"Add five minutes to preview focus",()=>ExtendPeekClock(id));more.Content="+5 min";more.FontSize=11;more.Margin=new(8,0,0,0);actions.Children.Add(more);var reset=PlainPeekButton(id=="timer"?"Reset preview timer":"Reset preview focus",()=>{PeekClock(id).Reset();app.SaveSession();});reset.Content="Reset";reset.FontSize=11;reset.Foreground=UI.Muted;reset.Margin=new(10,0,0,0);actions.Children.Add(reset);Grid.SetRow(actions,3);page.Children.Add(actions);peekClocks[id]=(time,detail,toggle,bar);AddPeekView(id,page);
    }
    private Countdown PeekClock(string id)=>id=="timer"?app.Session.Timer:app.Session.Focus;
    private void TogglePeekClock(string id)
    {var clock=PeekClock(id);var now=DateTimeOffset.UtcNow;if(clock.Running)clock.Pause(now);else if(clock.Remaining(now)>0)clock.Resume(now);else clock.Start((id=="timer"?5:app.Settings.FocusMinutes)*60,now,id=="focus"?"work":"");app.SaveSession();}
    private void ExtendPeekClock(string id)
    {var clock=PeekClock(id);var now=DateTimeOffset.UtcNow;double remaining=clock.Remaining(now);if(remaining<=0)clock.Start(300,now,id=="focus"?"work":"");else{clock.DurationSeconds+=300;clock.RemainingSeconds=remaining+300;if(clock.Running)clock.Deadline=now.AddSeconds(remaining+300);}app.SaveSession();}
    private FrameworkElement BuildPeekSystem()
    {var page=new StackPanel();page.Children.Add(UI.Text("System",11,UI.Muted));peekSystem.Margin=new(0,9,0,0);page.Children.Add(peekSystem);return page;}
    private FrameworkElement BuildPeekCalendar()
    {
        var page=new StackPanel();peekEventTime.Margin=new(0,3,0,9);page.Children.Add(peekEventTime);peekEventTitle.MaxHeight=43;peekEventTitle.TextWrapping=TextWrapping.NoWrap;peekEventTime.TextWrapping=TextWrapping.NoWrap;peekEventTime.TextTrimming=TextTrimming.CharacterEllipsis;peekShelfCount.TextWrapping=TextWrapping.NoWrap;peekShelfCount.TextTrimming=TextTrimming.CharacterEllipsis;peekEventTitle.TextTrimming=TextTrimming.CharacterEllipsis;page.Children.Add(peekEventTitle);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,15,0,0)};peekJoin=PlainPeekButton("Join preview meeting",()=>{if(peekEvent?.MeetingUrl.Length>0)try{ShortcutService.Open(new(){Target=peekEvent.MeetingUrl});}catch(Exception ex){app.Notify("Could not open meeting",ex.Message);}});peekJoin.Content="Join meeting";peekJoin.FontSize=12;peekJoin.Margin=new(0,0,18,0);actions.Children.Add(peekJoin);var open=PlainPeekButton("Open local calendar",()=>ShowTools("calendar"));open.Content="Calendar";open.FontSize=12;open.Foreground=UI.Muted;actions.Children.Add(open);page.Children.Add(actions);return page;
    }
    private FrameworkElement BuildPeekNote()
    {
        var page=new StackPanel();var label=UI.Text("Quick note",11,UI.Muted);label.Margin=new(0,3,0,10);page.Children.Add(label);
        peekNote=UI.Input("Preview note draft",app.Productivity.Note);peekNote.TextChanged+=(_,_)=>{if(syncingNote)return;app.Productivity.Note=peekNote.Text;noteSave.Stop();noteSave.Start();};peekNote.Height=53;peekNote.MaxLength=50000;peekNote.Padding=new(9,7,9,7);peekNote.Margin=new(0);peekNote.AcceptsReturn=true;peekNote.TextWrapping=TextWrapping.Wrap;peekNote.VerticalContentAlignment=VerticalAlignment.Top;peekNote.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;peekNote.PreviewMouseLeftButtonDown+=(_,_)=>{Activate();PromotePeek();};page.Children.Add(peekNote);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,9,0,0)};var save=PlainPeekButton("Save preview note",SavePeekNote);save.Content="Save note";save.FontSize=12;actions.Children.Add(save);var open=PlainPeekButton("Open all notes",()=>{notesMode="write";ShowTools("notes");});open.Content="All notes";open.Foreground=UI.Muted;open.FontSize=12;open.Margin=new(20,0,0,0);actions.Children.Add(open);page.Children.Add(actions);return page;
    }
    private void SavePeekNote()
    {
        FlushNotes();
    }

    private FrameworkElement BuildPeekShelf()
    {
        var page=new StackPanel();peekShelfCount.Margin=new(0,3,0,9);page.Children.Add(peekShelfCount);
        peekShelfFile=PlainPeekButton("Open or drag preview shelf file",()=>{if(peekFilePath!=null)try{ShortcutService.Open(new(){Target=peekFilePath});}catch(Exception ex){app.Notify("Could not open file",ex.Message);}});peekShelfName.TextTrimming=TextTrimming.CharacterEllipsis;peekShelfName.TextWrapping=TextWrapping.NoWrap;var fileRow=new DockPanel();fileRow.Children.Add(peekShelfArt);fileRow.Children.Add(peekShelfName);peekShelfFile.Content=fileRow;peekShelfFile.HorizontalContentAlignment=HorizontalAlignment.Left;peekShelfFile.Padding=new(0);peekShelfFile.Height=48;page.Children.Add(peekShelfFile);Point start=default;
        peekShelfFile.PreviewMouseLeftButtonDown+=(_,e)=>start=e.GetPosition(peekShelfFile);peekShelfFile.PreviewMouseMove+=(_,e)=>{var delta=e.GetPosition(peekShelfFile)-start;if(e.LeftButton==MouseButtonState.Pressed && peekFilePath!=null && (System.IO.File.Exists(peekFilePath) || System.IO.Directory.Exists(peekFilePath)) && (Math.Abs(delta.X)>SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y)>SystemParameters.MinimumVerticalDragDistance))DragDrop.DoDragDrop(peekShelfFile,new DataObject(DataFormats.FileDrop,new[]{peekFilePath}),DragDropEffects.Copy|DragDropEffects.Link);};
        var open=PlainPeekButton("Open full file shelf",()=>ShowTools("files"));open.Content="Open shelf";open.Foreground=UI.Muted;open.FontSize=12;open.HorizontalAlignment=HorizontalAlignment.Left;open.Margin=new(0,8,0,0);page.Children.Add(open);return page;
    }
    private async System.Threading.Tasks.Task LoadPeekShelfArtwork(string path)
    {var target=new Image();await LoadShelfThumbnail(path,target);if(peekShelfArtPath==path)peekShelfArt.Source=target.Source;}
    private void UpdatePeekDeck()
    {
        if(peekVolume!=null && !peekVolume.IsMouseCaptureWithin && !peekVolume.IsKeyboardFocusWithin){bool previous=updating;updating=true;try{peekVolume.Value=app.Audio.Volume*100;}finally{updating=previous;}}
        peekMute.IsEnabled=peekOutput.IsEnabled=peekVolume!.IsEnabled=app.Audio.Available;Motion.Icon(peekMute,app.Audio.Muted?"muted":"volume",14);peekTrackPrevious.IsEnabled=app.Media.CanPrevious;peekTrackNext.IsEnabled=app.Media.CanNext;peekTrackPrevious.Opacity=peekTrackPrevious.IsEnabled?1:.3;peekTrackNext.Opacity=peekTrackNext.IsEnabled?1:.3;
        var now=DateTimeOffset.UtcNow;peekSystem.Text=$"CPU  {app.Usage.cpu:0}%\nRAM  {app.Usage.used:0.0} / {app.Usage.total:0.0} GB";
        foreach(var pair in peekClocks){var c=PeekClock(pair.Key);var fields=pair.Value;double remaining=c.Remaining(now);fields.time.Text=remaining>0?c.Display(now):new Countdown{RemainingSeconds=(pair.Key=="timer"?5:app.Settings.FocusMinutes)*60}.Display(now);fields.detail.Text=pair.Key=="focus"?(c.Phase=="break"?"Break":"Focus"):("Timer");if(remaining>0 && !c.Running)fields.detail.Text+=" · paused";if(automaticPreview && previewKind==pair.Key && activityTitle.Length>0){fields.detail.Text=activityTitle;fields.time.Text=remaining>0?c.Display(now):"00:00";}fields.progress.Value=remaining>0?c.Progress(now):0;Motion.Icon(fields.toggle,c.Running?"pause":"play",14);}
        peekEvent=app.Calendar.Events.Where(e=>e.End>now).OrderBy(e=>e.Start).FirstOrDefault();peekEventTitle.Text=peekEvent?.Title??"No upcoming events";peekEventTime.Text=peekEvent==null?DateTime.Now.ToString("ddd, d MMM"):peekEvent.Start<=now?"Now · until "+peekEvent.End.ToLocalTime().ToString("HH:mm"):peekEvent.Start.ToLocalTime().ToString("ddd, d MMM · HH:mm");peekJoin.Visibility=peekEvent?.MeetingUrl.Length>0?Visibility.Visible:Visibility.Collapsed;
        var file=app.Productivity.Shelf.LastOrDefault();peekFilePath=file?.Exists==true?file.Path:null;if(peekShelfArtPath!=peekFilePath){peekShelfArtPath=peekFilePath;peekShelfArt.Source=null;peekShelfArt.Visibility=peekFilePath==null?Visibility.Collapsed:Visibility.Visible;if(peekFilePath!=null)_=LoadPeekShelfArtwork(peekFilePath);}peekShelfName.Text=file?.Name??"Drop files onto the notch";peekShelfCount.Text=app.Productivity.Shelf.Count==0?"File shelf":$"{app.Productivity.Shelf.Count} staged · drag a file out";peekShelfFile.IsEnabled=peekFilePath!=null;
    }
}
