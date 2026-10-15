using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace NabyScape;
internal sealed class ButtonCornerRadiusConverter : IMultiValueConverter
{
    public object Convert(object[] values,Type targetType,object parameter,CultureInfo culture)
    {
        var radius=values.Length>0 && values[0] is CornerRadius r?r:new CornerRadius(10);
        double height=values.Length>1 && values[1] is double h && h>0?h:34;
        double width=values.Length>2 && values[2] is double w && w>0?w:height;
        double limit=Math.Min(width,height)/2;
        return new CornerRadius(Math.Min(radius.TopLeft,limit),Math.Min(radius.TopRight,limit),Math.Min(radius.BottomRight,limit),Math.Min(radius.BottomLeft,limit));
    }
    public object[] ConvertBack(object value,Type[] targetTypes,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
