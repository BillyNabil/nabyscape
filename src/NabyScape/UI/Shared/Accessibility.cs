using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace NabyScape;

internal static class Accessibility
{
    private static bool initialized;
    private static bool forceHighContrast;
    private static double textScale = 1;
    private static readonly DependencyProperty SizedProperty = DependencyProperty.RegisterAttached(
        "Sized", typeof(bool), typeof(Accessibility), new PropertyMetadata(false));
    internal static double TextScale => textScale;
    internal static bool HighContrast => forceHighContrast || SystemParameters.HighContrast;

    internal static void Configure(Settings settings)
    {
        forceHighContrast = settings.AccessibilityHighContrast;
        textScale = settings.AccessibilityTextScale;
        if (initialized) return;
        initialized = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
    }

    internal static void SetFontSize(TextBlock text, double baseSize)
    {
        // Explicit sizes use base units and share the Loaded pass's once-only marker.
        text.SetCurrentValue(TextBlock.FontSizeProperty, baseSize * textScale);
        text.SetValue(SizedProperty, true);
    }
    internal static void ApplyCaption(TextBlock text)=>OnLoaded(text,new RoutedEventArgs());

    private static void OnLoaded(object sender, RoutedEventArgs args)
    {
        // Loaded is a direct event. TextBlock can report an internal content source;
        // the class-handler sender is the element whose properties we must apply.
        if (sender is not FrameworkElement element) return;
        DependencyProperty? font = element is TextBlock ? TextBlock.FontSizeProperty : element is Control ? Control.FontSizeProperty : null;
        if (font != null && !(bool)element.GetValue(SizedProperty))
        {
            element.SetValue(SizedProperty, true);
            var source = DependencyPropertyHelper.GetValueSource(element, font).BaseValueSource;
            // Inherited sizes are already scaled by their parent. Preserve bindings.
            if (textScale != 1 && source is BaseValueSource.Local or BaseValueSource.Style or BaseValueSource.DefaultStyle)
                element.SetCurrentValue(font, (double)element.GetValue(font) * textScale);
        }
        if (!HighContrast) return;
        // Template parts retain semantic brushes and triggers (checked, selected, progress).
        if (element.TemplatedParent != null) return;
        if (element is TextBlock text && !BindingOperations.IsDataBound(text, TextBlock.ForegroundProperty))
            FollowContentForeground(text, TextBlock.ForegroundProperty);
        if (element is Control control && control is not MenuItem && control is not ComboBoxItem)
        {
            bool selected = control.Background is SolidColorBrush background && background.Color == SystemColors.HighlightColor;
            control.SetCurrentValue(Control.ForegroundProperty, selected ? SystemColors.HighlightTextBrush : SystemColors.WindowTextBrush);
            if (control.Background is SolidColorBrush fill && fill.Color.A > 0 && !selected)
                control.SetCurrentValue(Control.BackgroundProperty, SystemColors.WindowBrush);
            if (control is ProgressBar)
            {
                control.SetResourceReference(Control.ForegroundProperty, "ProgressFill");
                control.SetResourceReference(Control.BackgroundProperty, "ProgressTrack");
            }
            control.SetCurrentValue(Control.BorderBrushProperty, SystemColors.WindowTextBrush);
            if (control is ButtonBase && control.BorderThickness == new Thickness(0))
                control.SetCurrentValue(Control.BorderThicknessProperty, new Thickness(1));
        }
        if (element is Border border)
        {
            if (border.Background is SolidColorBrush fill && fill.Color.A > 0 && fill.Color != SystemColors.HighlightColor)
                border.SetCurrentValue(Border.BackgroundProperty, SystemColors.WindowBrush);
            if (border.BorderBrush != null) border.SetCurrentValue(Border.BorderBrushProperty, SystemColors.WindowTextBrush);
        }
    }

    internal static void FollowContentForeground(FrameworkElement element, DependencyProperty property)
    {
        // Content may be a label/icon inside a selected menu item or primary button.
        for (DependencyObject? parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ButtonBase or MenuItem or ComboBoxItem)
            {
                element.SetBinding(property, new Binding("Foreground") { Source = parent });
                return;
            }
        element.SetCurrentValue(property, SystemColors.WindowTextBrush);
    }

    internal static void Live(TextBlock text)
        => AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);

    internal static void Announce(TextBlock text, string message)
    {
        Live(text);
        if (text.Text == message) return;
        text.Text = message;
        // Announce status only; transcripts and rapidly changing meters stay ordinary text.
        if (text.IsLoaded && text.IsVisible && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            (UIElementAutomationPeer.FromElement(text) ?? UIElementAutomationPeer.CreatePeerForElement(text))?
                .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
