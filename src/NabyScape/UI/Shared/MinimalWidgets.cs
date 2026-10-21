using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
namespace NabyScape;

internal static class MinimalWidgets
{
    private static readonly HashSet<ContextMenu> openMenus=new();
    internal static bool MenuOpen(Window owner)=>openMenus.Any(menu=>menu.IsOpen && Window.GetWindow(menu.PlacementTarget)==owner);
    internal static Grid Header(FrameworkElement heading,FrameworkElement actions)
    {
        var row=new Grid{Margin=new(0,0,0,18)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.RowDefinitions.Add(new(){Height=GridLength.Auto});row.RowDefinitions.Add(new(){Height=GridLength.Auto});
        row.Children.Add(heading);Grid.SetColumn(actions,1);row.Children.Add(actions);
        row.SizeChanged+=(_,_)=>{bool narrow=row.ActualWidth<330;Grid.SetColumn(actions,narrow?0:1);Grid.SetRow(actions,narrow?1:0);Grid.SetColumnSpan(heading,narrow?2:1);actions.HorizontalAlignment=narrow?HorizontalAlignment.Left:HorizontalAlignment.Right;actions.Margin=narrow?new Thickness(0,12,0,0):new Thickness(0);};return row;
    }
    internal static void Underline(TextBox input)
    {
        input.Background=Brushes.Transparent;input.BorderThickness=new(0,0,0,1);input.BorderBrush=UI.Brush("#3C4048");
        input.Padding=new(5,4,5,4);input.Margin=new(0);input.MinWidth=0;input.MinHeight=32;
    }
    internal static Button More(string name,params (string Label,Action Run)[] actions)
    {
        Button? button=null;
        button=UI.Button(name,()=>
        {
            var menu=new ContextMenu{PlacementTarget=button};
            menu.Opened+=(_,_)=>openMenus.Add(menu);menu.Closed+=(_,_)=>openMenus.Remove(menu);
            foreach(var action in actions){var item=new MenuItem{Header=action.Label};UI.Named(item,action.Label);item.Click+=(_,_)=>action.Run();menu.Items.Add(item);}
            button!.ContextMenu=menu;menu.IsOpen=true;
        });
        button.Content=UI.Text("⋯",21);button.MinHeight=30;button.Width=30;button.Padding=new(0);button.Margin=new(8,0,0,0);button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.ToolTip=name;
        button.Unloaded+=(_,_)=>{if(button.ContextMenu is {} menu){menu.IsOpen=false;openMenus.Remove(menu);}};
        return button;
    }
    internal static void Reveal(Button button,FrameworkElement owner)
    {
        void Update()=>Motion.Fade(button,owner.IsMouseOver || owner.IsKeyboardFocusWithin?1:.12,130);
        button.Opacity=.12;owner.MouseEnter+=(_,_)=>Update();owner.MouseLeave+=(_,_)=>Update();owner.GotKeyboardFocus+=(_,_)=>Update();owner.LostKeyboardFocus+=(_,_)=>Update();
    }
    internal static DateTime[] Week(DateTime selected)
    {
        int offset=((int)selected.DayOfWeek+6)%7;
        long start=Math.Clamp(selected.Date.Ticks-offset*TimeSpan.TicksPerDay,0,DateTime.MaxValue.Date.Ticks-6*TimeSpan.TicksPerDay);
        return Enumerable.Range(0,7).Select(i=>new DateTime(start+i*TimeSpan.TicksPerDay)).ToArray();
    }
}

// A measured progress instrument. No timer and no perpetual rendering subscription.
internal sealed class ProgressRing : ProgressBar
{
    private static readonly DependencyProperty VisualValueProperty=DependencyProperty.Register("VisualValue",typeof(double),typeof(ProgressRing),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Pen track=new(UI.Brush("#343840"),2.3);
    private Pen active=new(Brushes.White,2.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
    internal ProgressRing()
    {
        Minimum=0;Maximum=1;MinHeight=112;MaxHeight=154;Height=128;IsHitTestVisible=false;
        Template=new ControlTemplate(typeof(ProgressBar)){VisualTree=new FrameworkElementFactory(typeof(Grid))};
        ValueChanged+=(_,_)=>
        {
            double before=(double)GetValue(VisualValueProperty);BeginAnimation(VisualValueProperty,null);SetValue(VisualValueProperty,Value);
            if(!Motion.Enabled || !IsVisible || !IsLoaded || Math.Abs(before-Value)<.00001)return;
            var animation=new DoubleAnimation(before,Value,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};Timeline.SetDesiredFrameRate(animation,Math.Clamp(Motion.FrameLimit,30,240));BeginAnimation(VisualValueProperty,animation);
        };
        Unloaded+=(_,_)=>{BeginAnimation(VisualValueProperty,null);SetValue(VisualValueProperty,Value);};
    }
    internal void Accent(bool focus){var brush=focus?UI.Brush("#DCC7AE"):Brushes.White;if(active.Brush==brush)return;active=new(brush,2.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        double radius=Math.Max(0,Math.Min(ActualWidth,ActualHeight)/2-3);if(radius<2)return;
        var center=new Point(ActualWidth/2,ActualHeight/2);dc.DrawEllipse(null,track,center,radius,radius);
        double fraction=Math.Clamp((double)GetValue(VisualValueProperty),0,1);if(fraction<.0001)return;
        if(fraction>.9999){dc.DrawEllipse(null,active,center,radius,radius);return;}
        double angle=fraction*Math.PI*2;var geometry=new StreamGeometry();
        using(var path=geometry.Open()){path.BeginFigure(new(center.X,center.Y-radius),false,false);path.ArcTo(new(center.X+radius*Math.Sin(angle),center.Y-radius*Math.Cos(angle)),new(radius,radius),0,fraction>.5,SweepDirection.Clockwise,true,false);}
        geometry.Freeze();dc.DrawGeometry(null,active,geometry);
    }
}

internal sealed class TileFlow : Panel
{
    internal double MinimumTileWidth {get;set;}=150;
    internal double Gap {get;set;}=16;
    internal int Columns {get;private set;}=1;
    private readonly List<(UIElement Child,Rect Bounds)> slots=new();
    protected override Size MeasureOverride(Size available)
    {
        double width=double.IsFinite(available.Width)?available.Width:MinimumTileWidth;
        Columns=Math.Clamp((int)Math.Floor((width+Gap)/(MinimumTileWidth+Gap)),1,2);
        double tile=Math.Max(0,(width-Gap*(Columns-1))/Columns),y=0,rowHeight=0;int column=0;slots.Clear();
        foreach(UIElement child in InternalChildren)
        {
            bool full=child is FrameworkElement {Tag:"clip-detail"};
            if(full && column>0){y+=rowHeight+Gap;rowHeight=0;column=0;}
            child.Measure(new(full?width:tile,double.PositiveInfinity));
            double height=child.DesiredSize.Height;
            slots.Add((child,new(full?0:column*(tile+Gap),y,full?width:tile,height)));
            rowHeight=Math.Max(rowHeight,height);column++;
            if(full || column==Columns){y+=rowHeight+Gap;rowHeight=0;column=0;}
        }
        return new(width,Math.Max(0,column>0?y+rowHeight:y-Gap));
    }
    protected override Size ArrangeOverride(Size final){foreach(var slot in slots)slot.Child.Arrange(slot.Bounds);return final;}
}

// Keep decoded textures near the viewport. Original files are retained for Copy/Save.
internal sealed class ViewportThumbnails
{
    private sealed class Entry
    {
        internal FrameworkElement Tile=null!;internal Image Image=null!;internal Func<int,Task<BitmapSource?>> Load=null!;
        internal bool Active;internal int Epoch,Pixels;
    }
    private readonly ScrollViewer viewport;
    private readonly List<Entry> entries=new();
    internal int ActiveCount=>entries.Count(e=>e.Active);
    internal ViewportThumbnails(ScrollViewer viewport)
    {
        this.viewport=viewport;viewport.Loaded+=(_,_)=>Refresh();viewport.SizeChanged+=(_,_)=>Refresh();viewport.ScrollChanged+=(_,_)=>Refresh();
        viewport.Unloaded+=(_,_)=>{foreach(var e in entries){e.Active=false;e.Epoch++;SetSource(e.Image,null);}};
        viewport.IsVisibleChanged+=(_,_)=>{if(viewport.IsVisible)Refresh();else foreach(var e in entries){e.Active=false;e.Epoch++;SetSource(e.Image,null);}};
    }
    internal void Add(FrameworkElement tile,Image image,Func<int,Task<BitmapSource?>> load)=>entries.Add(new(){Tile=tile,Image=image,Load=load});
    internal void Refresh()
    {
        if(!viewport.IsLoaded || !viewport.IsVisible || viewport.ViewportHeight<=0)return;
        var visible=new Rect(0,-viewport.ViewportHeight*.5,Math.Max(1,viewport.ActualWidth),viewport.ViewportHeight*2);
        foreach(var entry in entries)
        {
            Rect bounds;
            try{bounds=entry.Tile.TransformToAncestor(viewport).TransformBounds(new Rect(entry.Tile.RenderSize));}catch(InvalidOperationException){continue;}
            bool active=bounds.Height>0 && visible.IntersectsWith(bounds);
            int pixels=(int)Math.Clamp(Math.Ceiling(Math.Max(48,entry.Tile.ActualWidth)*VisualTreeHelper.GetDpi(viewport).DpiScaleX/32)*32,64,768);
            if(active==entry.Active && (!active || entry.Pixels==pixels))continue;
            entry.Active=active;entry.Pixels=pixels;int epoch=++entry.Epoch;
            if(!active){SetSource(entry.Image,null);continue;}
            _=Load(entry,pixels,epoch);
        }
    }
    private static async Task Load(Entry entry,int pixels,int epoch)
    {
        try{var source=await entry.Load(pixels);if(entry.Active && entry.Epoch==epoch)SetSource(entry.Image,source);}catch{if(entry.Epoch==epoch)SetSource(entry.Image,null);}
    }
    private static void SetSource(Image image,BitmapSource? source){image.Source=source;if(image.Tag is FrameworkElement fallback)fallback.Visibility=source==null?Visibility.Visible:Visibility.Collapsed;}
}
