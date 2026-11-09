using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace NabyScape;

internal static partial class LocalizedOptionPresentation
{
    internal sealed class CaptionConverter : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
            => value is string text ? Localization.Text(text) : value;
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)
            => Binding.DoNothing;
    }
    internal static DataTemplate Template()
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty,new Binding{Converter=new CaptionConverter()});
        text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        return new DataTemplate{VisualTree=text};
    }
}
