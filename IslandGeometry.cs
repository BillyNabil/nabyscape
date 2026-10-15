using System;
using System.Windows;
using System.Windows.Media;

namespace NabyScape;

internal static class IslandGeometry
{
    // The shoulders are concave: the screen edge flows into the sides of the notch.
    // A rounded rectangle with two square corners cannot produce this contour.
    public static Geometry Create(double width,double height,double radius,bool notch,double offset=0)
    {
        width=Math.Max(1,width);height=Math.Max(1,height);
        if(!notch) {var pill=new RectangleGeometry(new Rect(offset,0,width,height),Math.Min(radius,height/2),Math.Min(radius,height/2));pill.Freeze();return pill;}
        double shoulder=Math.Min(16,height*.36),r=Math.Min(radius,Math.Min(height-shoulder,(width-2*shoulder)/2));
        var geometry=new StreamGeometry();
        using(var c=geometry.Open())
        {
            double left=offset+shoulder,right=offset+width-shoulder;
            c.BeginFigure(new Point(offset,0),true,true);
            c.LineTo(new Point(offset+width,0),true,false);
            c.BezierTo(new Point(right,0),new Point(right,shoulder*.38),new Point(right,shoulder),true,false);
            c.LineTo(new Point(right,height-r),true,false);
            c.BezierTo(new Point(right,height-r*.448),new Point(right-r*.448,height),new Point(right-r,height),true,false);
            c.LineTo(new Point(left+r,height),true,false);
            c.BezierTo(new Point(left+r*.448,height),new Point(left,height-r*.448),new Point(left,height-r),true,false);
            c.LineTo(new Point(left,shoulder),true,false);
            c.BezierTo(new Point(left,shoulder*.38),new Point(left,0),new Point(offset,0),true,false);
        }
        geometry.Freeze();return geometry;
    }
}

internal sealed class IslandSurface : FrameworkElement
{
    public static readonly DependencyProperty GlassLiftProperty=DependencyProperty.Register(nameof(GlassLift),typeof(double),typeof(IslandSurface),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
    public double GlassLift {get=>(double)GetValue(GlassLiftProperty);set=>SetValue(GlassLiftProperty,value);}
    public bool GlassStyle {get;set;}=true;
    private Brush? albumBrush;
    private Color albumTint;
    public Color AlbumTint {get=>albumTint;set{if(albumTint==value)return;albumTint=value;albumBrush=new RadialGradientBrush(value,Colors.Transparent){Center=new(.1,.35),GradientOrigin=new(.1,.35),RadiusX=1,RadiusY=1};albumBrush.Freeze();InvalidateVisual();}}
    private double lightX=.5,lightY=.25;
    private static readonly Brush glass=Frozen(new LinearGradientBrush(new GradientStopCollection{new(Color.FromRgb(18,19,23),0),new(Color.FromRgb(8,9,12),.46),new(Color.FromRgb(1,2,3),1)},new Point(0,0),new Point(.65,1)));
    internal static Brush GlassMaterial=>glass;
    public double GlassStrength {get;set;}=1;
    private Brush material=glass;
    internal static Brush Material(string color)
    {
        var c=((SolidColorBrush)UI.Brush(color)).Color;if(c==Colors.Black)return glass;
        Color Lift(byte amount)=>Color.FromRgb((byte)Math.Min(255,c.R+amount),(byte)Math.Min(255,c.G+amount),(byte)Math.Min(255,c.B+amount));
        return Frozen(new LinearGradientBrush(new GradientStopCollection{new(Lift(18),0),new(Lift(8),.46),new(Lift(1),1)},new Point(0,0),new Point(.65,1)));
    }
    public void SetMaterial(string color,double strength){material=Material(color);GlassStrength=strength;InvalidateVisual();}
    private static readonly Pen edge=new(Frozen(new LinearGradientBrush(new GradientStopCollection{new(Color.FromArgb(66,255,255,255),0),new(Color.FromArgb(9,255,255,255),.4),new(Color.FromArgb(34,255,255,255),1)},new Point(0,0),new Point(1,1))),.75);
    private readonly RadialGradientBrush reflection=new(){GradientStops=new(){new(Color.FromArgb(35,232,239,255),0),new(Color.FromArgb(0,232,239,255),1)},RadiusX=.85,RadiusY=.8};
    private static Brush Frozen(Brush brush){brush.Freeze();return brush;}
    public void Light(double x,double y){if(Math.Abs(lightX-x)+Math.Abs(lightY-y)<.001)return;lightX=x;lightY=y;InvalidateVisual();}
    public static readonly DependencyProperty SurfaceWidthProperty=DependencyProperty.Register(nameof(SurfaceWidth),typeof(double),typeof(IslandSurface),new FrameworkPropertyMetadata(300d,FrameworkPropertyMetadataOptions.AffectsRender,Changed));
    public static readonly DependencyProperty SurfaceHeightProperty=DependencyProperty.Register(nameof(SurfaceHeight),typeof(double),typeof(IslandSurface),new FrameworkPropertyMetadata(44d,FrameworkPropertyMetadataOptions.AffectsRender,Changed));
    public double SurfaceWidth {get=>(double)GetValue(SurfaceWidthProperty);set=>SetValue(SurfaceWidthProperty,value);}
    public double SurfaceHeight {get=>(double)GetValue(SurfaceHeightProperty);set=>SetValue(SurfaceHeightProperty,value);}
    public double Radius {get;set;}=22;
    public bool Notch {get;set;}=true;
    public Brush Fill {get;set;}=Brushes.Black;
    public Geometry Contour {get;private set;}=Geometry.Empty;
    public event Action? ContourChanged;
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)=>((IslandSurface)d).Refresh();
    protected override void OnRenderSizeChanged(SizeChangedInfo info) {base.OnRenderSizeChanged(info);Refresh();}
    public void Refresh()
    {
        Contour=IslandGeometry.Create(SurfaceWidth,SurfaceHeight,Radius,Notch,Math.Max(0,(ActualWidth-SurfaceWidth)/2));
        ContourChanged?.Invoke();InvalidateVisual();
    }
    protected override void OnRender(DrawingContext context)
    {
        context.DrawGeometry(Fill,null,Contour);
        if(!GlassStyle)return;
        context.PushClip(Contour);context.PushOpacity((.45+.55*GlassLift)*GlassStrength);
        context.DrawGeometry(material,null,Contour);
        if(albumBrush!=null)context.DrawRectangle(albumBrush,null,new Rect(Math.Max(0,(ActualWidth-SurfaceWidth)/2),0,SurfaceWidth,SurfaceHeight));
        reflection.Center=reflection.GradientOrigin=new Point(lightX,lightY);
        context.DrawRectangle(reflection,null,new Rect(Math.Max(0,(ActualWidth-SurfaceWidth)/2),0,SurfaceWidth,SurfaceHeight));
        context.DrawGeometry(null,edge,Contour);context.Pop();context.Pop();
    }
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
        =>Contour.FillContains(parameters.HitPoint)?new PointHitTestResult(this,parameters.HitPoint):null;
}
