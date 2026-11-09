using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

// Circular sender avatars: the app's own logo for alerts from other apps, Naby for
// NabyScape's alerts, and a coloured initial when Windows provides no logo.
internal static class NoticeAvatar
{
    private static readonly string[] Palette={"#3B6EA8","#7A4FA3","#2F8F6B","#B5643A","#A33F5F","#4F7A2E","#2F7C8F","#8A6D2F"};
    private static ImageSource? naby;
    internal static string Initial(string name)=>name.Trim().FirstOrDefault(char.IsLetterOrDigit) is var c && c!='\0'?char.ToUpperInvariant(c).ToString():"?";
    internal static string Color(string name){int hash=0;foreach(char c in name.ToLowerInvariant())hash=unchecked(hash*31+c);return Palette[(int)((uint)hash%(uint)Palette.Length)];}
    internal static FrameworkElement Create(InternalNotice notice,double size)
    {
        var host=new Grid{Width=size,Height=size,ClipToBounds=false,VerticalAlignment=VerticalAlignment.Top};
        host.Clip=new EllipseGeometry(new Point(size/2,size/2),size/2,size/2);
        if(notice.Kind=="external")
        {
            var logo=ExternalAppIcons.Get(notice.AppId);
            if(logo!=null)
            {
                // Full-bleed icons fill the circle; transparent glyph logos sit inside a soft plate.
                bool fullBleed=ExternalAppIcons.FullBleed(notice.AppId);
                host.Children.Add(new System.Windows.Shapes.Ellipse{Fill=UI.Brush("#2A2D33")});
                var image=new Image{Source=logo,Stretch=fullBleed?Stretch.UniformToFill:Stretch.Uniform,Margin=new(fullBleed?0:size*.2)};
                RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);host.Children.Add(image);
                return host;
            }
            string name=notice.Source??Localization.Text("App");
            host.Children.Add(new System.Windows.Shapes.Ellipse{Fill=UI.Brush(Color(name))});
            var letter=UI.Text(Initial(name),size*.42);letter.FontWeight=FontWeights.SemiBold;letter.Foreground=Brushes.White;letter.HorizontalAlignment=HorizontalAlignment.Center;letter.VerticalAlignment=VerticalAlignment.Center;letter.TextAlignment=TextAlignment.Center;
            host.Children.Add(letter);return host;
        }
        host.Children.Add(new System.Windows.Shapes.Ellipse{Fill=UI.Brush("#16181B")});
        try{naby??=Load();}catch{naby=null;}
        if(naby!=null){var image=new Image{Source=naby,Stretch=Stretch.UniformToFill,Margin=new(-size*.06)};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);host.Children.Add(image);}
        else host.Children.Add(Icons.Create(NotificationCenterText.Icon(notice.Kind),size*.5));
        return host;
    }
    private static ImageSource Load()
    {
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=new Uri("pack://application:,,,/Assets/naby-chrome.png");bitmap.DecodePixelWidth=96;bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
}
