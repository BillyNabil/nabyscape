using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace NabyScape;

internal readonly record struct DockLayout(double Horizontal,double Top,double Width,double Height,double PanelWidth,double PanelHeight,double HoverWidth,double HoverHeight,string Preset,string Monitor,string Mode,bool AutoHeight,string Edge,double Vertical,double Inset)
{
    internal static DockLayout From(Settings s)=>new(s.Horizontal,s.Top,s.Width,s.Height,s.PanelWidth,s.PanelMaxHeight,s.HoverWidth,s.HoverHeight,s.Preset,s.Monitor,s.DisplayMode,s.PanelAutoHeight,s.DockEdge,s.DockVertical,s.DockInset);
    internal void Restore(Settings s){s.Horizontal=Horizontal;s.Top=Top;s.Width=Width;s.Height=Height;s.PanelWidth=PanelWidth;s.PanelMaxHeight=PanelHeight;s.HoverWidth=HoverWidth;s.HoverHeight=HoverHeight;s.Preset=Preset;s.Monitor=Monitor;s.DisplayMode=Mode;s.PanelAutoHeight=AutoHeight;s.DockEdge=Edge;s.DockVertical=Vertical;s.DockInset=Inset;}
}

public sealed partial class OverlayWindow
{
    private readonly Canvas dockHandles=new(){Visibility=Visibility.Collapsed};
    private Thumb moveGrip=null!,resizeLeft=null!,resizeRight=null!;
    private DockLayout? editingOrigin;
    private Point editingPointer;
    private double editingCenter,editingTop,editingHeight;
    private bool dockEditing;
    internal void ToggleDockEditing(){app.Settings.DockUnlocked=!app.Settings.DockUnlocked;app.ChangeSettings();}
    private void InitializeDockEditing()
    {
        Thumb Grip(string name,Cursor cursor,bool move,bool left=false)
        {
            var handle=new Thumb{Width=move?42:16,Height=move?9:16,Cursor=cursor,Focusable=true,ToolTip=name+" · arrow keys adjust · Esc cancels",Background=Brushes.Transparent};UI.Named(handle,name);
            var shape=new FrameworkElementFactory(typeof(Border));shape.SetValue(Border.BackgroundProperty,UI.Brush("#989DA7"));shape.SetValue(Border.CornerRadiusProperty,new CornerRadius(2));shape.SetValue(WidthProperty,move?26d:6d);shape.SetValue(HeightProperty,move?3d:6d);shape.SetValue(OpacityProperty,.7);
            handle.Template=new ControlTemplate(typeof(Thumb)){VisualTree=shape};dockHandles.Children.Add(handle);
            handle.DragStarted+=(_,_)=>
            {
                if(IsAnimating || IsTucked){handle.CancelDrag();return;}
                dockEditing=true;editingOrigin=DockLayout.From(app.Settings);editingPointer=PointToScreen(Mouse.GetPosition(this));var screen=IslandScreen;double scale=Native.Scale(screen),compact=app.Settings.Width*scale;
                editingCenter=PointToScreen(new Point(ActualWidth/2,0)).X;editingTop=PointToScreen(new Point()).Y;editingHeight=PaintedHeight;StopHoverTimers();leaveDelay.Stop();
            };
            handle.DragDelta+=(_,_)=>
            {
                if(editingOrigin is not { } original)return;
                var pointer=PointToScreen(Mouse.GetPosition(this));var delta=pointer-editingPointer;var s=app.Settings;
                if(move)
                {
                    var screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)(editingCenter+delta.X),(int)pointer.Y));double scale=Native.Scale(screen),compact=s.Width*scale;
                    s.Monitor=screen.DeviceName;s.DisplayMode="Selected";s.Horizontal=Math.Clamp((editingCenter+delta.X-screen.WorkingArea.Left-compact/2)/Math.Max(1,screen.WorkingArea.Width-compact)*100,0,100);
                    double top=Math.Clamp((editingTop+delta.Y-screen.WorkingArea.Top)/scale,0,Math.Max(0,screen.WorkingArea.Height/scale-PaintedHeight));
                    if(Math.Abs(s.Horizontal-50)<.7)s.Horizontal=50;
                    s.DockEdge=original.Preset=="Mac" && top<=8?"Top":"Floating";s.Preset=original.Preset=="Mac" && top<=8?"Mac":"iPhone";s.Top=s.Preset=="Mac"?0:top;surface.Notch=s.Preset=="Mac" && s.DockEdge=="Top";
                }
                else
                {
                    double scale=Native.Scale(IslandScreen),dx=delta.X/scale*(left?-2:2),dy=delta.Y/scale;
                    if(expanded){s.PanelWidth=Math.Clamp(original.PanelWidth+dx,300,960);if(Math.Abs(dy)>2){s.PanelAutoHeight=false;s.PanelMaxHeight=Math.Clamp(editingHeight+dy,240,1100);}}
                    else if(peeking){s.HoverWidth=Math.Clamp(original.HoverWidth+dx,300,760);s.HoverHeight=Math.Clamp(original.HoverHeight+dy,184,440);}
                    else{s.Width=Math.Clamp(original.Width+dx,180,400);s.Height=Math.Clamp(original.Height+dy,28,72);compact.Width=Math.Max(120,s.Width-(surface.Notch?32:12));compact.Height=compact.MinHeight=s.Height;RefreshAssistantCharacter();}
                }
                ReflowDisplay();RefreshDockHandles();
            };
            handle.DragCompleted+=(_,e)=>
            {
                if(editingOrigin is not { } original)return;
                if(e.Canceled)original.Restore(app.Settings);editingOrigin=null;dockEditing=false;app.ChangeSettings();CheckEnvironment();
            };
            handle.PreviewKeyDown+=(_,e)=>
            {
                if(e.Key==Key.Escape && dockEditing){handle.CancelDrag();e.Handled=true;return;}
                if(e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))return;
                double dx=e.Key==Key.Left?-2:e.Key==Key.Right?2:0,dy=e.Key==Key.Up?-2:e.Key==Key.Down?2:0;var s=app.Settings;
                if(move){if(s.DockEdge is "Left" or "Right"){s.DockVertical=Math.Clamp(s.DockVertical+dy/2,0,100);s.DockInset=Math.Clamp(s.DockInset+(s.DockEdge=="Left"?dx:-dx),0,200);}else{s.Horizontal=Math.Clamp(s.Horizontal+dx/2,0,100);if(s.DockEdge=="Bottom")s.DockInset=Math.Clamp(s.DockInset-dy,0,200);else if(s.Preset!="Mac" || s.DockEdge=="Floating")s.Top=Math.Max(0,s.Top+dy);}}
                else if(expanded){s.PanelWidth+=dx;if(dy!=0){s.PanelAutoHeight=false;s.PanelMaxHeight=PaintedHeight+dy;}}
                else if(peeking){s.HoverWidth+=dx;s.HoverHeight+=dy;}
                else{s.Width+=dx;s.Height+=dy;}
                app.ChangeSettings();e.Handled=true;
            };
            return handle;
        }
        moveGrip=Grip("Move island",Cursors.SizeAll,true);resizeLeft=Grip("Resize island from left",Cursors.SizeNESW,false,true);resizeRight=Grip("Resize island from right",Cursors.SizeNWSE,false);
        root.Children.Add(dockHandles);surface.ContourChanged+=RefreshDockHandles;root.SizeChanged+=(_,_)=>RefreshDockHandles();app.SettingsChanged+=RefreshDockHandles;
        PreviewKeyDown+=(_,_)=>RefreshDockHandles();PreviewKeyUp+=(_,_)=>RefreshDockHandles();
        var menu=new ContextMenu();var unlock=new MenuItem{Header="Move & resize island",IsCheckable=true};unlock.Click+=(_,_)=>ToggleDockEditing();menu.Opened+=(_,_)=>unlock.IsChecked=app.Settings.DockUnlocked;menu.Items.Add(unlock);compact.ContextMenu=menu;
        RefreshDockHandles();
    }
    private void RefreshDockHandles()
    {
        if(moveGrip==null)return;
        bool visible=(app.Settings.DockUnlocked || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) && !userHidden && !IsTucked && !assistantPeeking && !noticeShowing;
        dockHandles.Visibility=visible?Visibility.Visible:Visibility.Collapsed;if(!visible)return;
        double left=(root.ActualWidth-PaintedWidth)/2;
        Canvas.SetLeft(moveGrip,left+PaintedWidth/2-21);Canvas.SetTop(moveGrip,1);
        Canvas.SetLeft(resizeLeft,left+2);Canvas.SetLeft(resizeRight,left+PaintedWidth-18);Canvas.SetTop(resizeLeft,Math.Max(0,PaintedHeight-18));Canvas.SetTop(resizeRight,Math.Max(0,PaintedHeight-18));
    }
}
