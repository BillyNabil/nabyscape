using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NabyScape;

internal readonly record struct CoverPalette(Color Primary,Color Secondary)
{
    internal static CoverPalette Extract(ImageSource? source)
    {
        if(source==null)return new(Colors.Transparent,Colors.Transparent);
        // A bounded thumbnail supports bitmap and vector artwork without copying a full cover.
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())dc.DrawImage(source,new Rect(0,0,32,32));
        var thumbnail=new RenderTargetBitmap(32,32,96,96,PixelFormats.Pbgra32);thumbnail.Render(drawing);
        var pixels=new byte[32*32*4];thumbnail.CopyPixels(pixels,128,0);
        var weight=new double[24];var red=new double[24];var green=new double[24];var blue=new double[24];
        for(int i=0;i<pixels.Length;i+=4)
        {
            double alpha=pixels[i+3]/255d;if(alpha<.65)continue;
            double r=pixels[i+2]/alpha,g=pixels[i+1]/alpha,b=pixels[i]/alpha;
            double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
            if(max<28 || min>238 || delta<18)continue;
            double hue=delta<1?0:max==r?((g-b)/delta+6)%6:max==g?(b-r)/delta+2:(r-g)/delta+4;
            int bin=(int)(hue*4)%24;double w=alpha*Math.Pow(delta/255d,1.5)*(.3+max/255d);
            weight[bin]+=w;red[bin]+=r*w;green[bin]+=g*w;blue[bin]+=b*w;
        }
        int main=0;for(int i=1;i<24;i++)if(weight[i]>weight[main])main=i;
        if(weight[main]<.01)return new(Color.FromRgb(96,96,96),Color.FromRgb(64,64,64));
        Color Pick(int i)
        {
            double r=red[i]/weight[i],g=green[i]/weight[i],b=blue[i]/weight[i],max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
            if(delta<1)return Color.FromRgb((byte)r,(byte)g,(byte)b);
            double saturation=Math.Clamp(delta/max*1.15,.72,.96),value=Math.Clamp(max,190,245);
            byte Channel(double channel)=>(byte)Math.Clamp(Math.Round(value*(1-saturation+saturation*(channel-min)/delta)),0,255);
            return Color.FromRgb(Channel(r),Channel(g),Channel(b));
        }
        int other=main;double best=0;
        for(int i=0;i<24;i++){int distance=Math.Min(Math.Abs(main-i),24-Math.Abs(main-i));double score=weight[i]*distance;if(distance>=4 && score>best){other=i;best=score;}}
        return new(Pick(main),Pick(other));
    }
}
