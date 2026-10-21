using System.IO;
using System.Windows.Media.Imaging;
namespace NabyScape;

internal static class DisplayThumbnail
{
    // All callers display a Uniform preview inside a box no taller than its width.
    // Bound the long edge: decoding a thin portrait by width can otherwise upscale
    // its height by orders of magnitude. Original Copy/Save never uses this helper.
    internal static BitmapSource Decode(Stream stream,int pixels)
    {
        long start=stream.Position;
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
        bool portrait=decoder.Frames[0].PixelHeight>decoder.Frames[0].PixelWidth;
        stream.Position=start;
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
        if(portrait)image.DecodePixelHeight=pixels;else image.DecodePixelWidth=pixels;
        image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
}
