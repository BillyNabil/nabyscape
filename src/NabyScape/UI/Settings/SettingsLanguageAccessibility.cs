using System;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    internal void LanguageAndAccessibilitySettings()
    {
        var settings = app.Settings;
        settings.NormalizeLanguageAccessibility();
        var codes = new[] { "en", "es", "id" };
        var language = new ComboBox { ItemsSource = new[] { "English", "Español", "Bahasa Indonesia" },
            SelectedIndex = Array.IndexOf(codes, settings.UiLanguage) };
        UI.LocalizedName(language, "Interface language");
        var scale = new ComboBox { ItemsSource = new[] { "100%", "110%", "125%", "150%" } };
        var sizes = new[] { 1d, 1.1, 1.25, 1.5 };
        scale.SelectedIndex = Array.FindIndex(sizes, value => value >= settings.AccessibilityTextScale);
        UI.LocalizedName(scale, "Text size");
        var contrast = new CheckBox { Content = UI.Caption("High contrast"), IsChecked = settings.AccessibilityHighContrast };
        UI.LocalizedName(contrast, "High contrast");
        var notice = UI.Caption("Save these preferences, then quit and reopen NabyScape to apply them.", 12, UI.Muted);
        Accessibility.Live(notice);
        var save = UI.CaptionButton("Save language and accessibility", () =>
        {
            var previous = (settings.UiLanguage, settings.AccessibilityTextScale, settings.AccessibilityHighContrast);
            settings.UiLanguage = codes[Math.Max(0, language.SelectedIndex)];
            settings.AccessibilityTextScale = sizes[Math.Max(0, scale.SelectedIndex)];
            settings.AccessibilityHighContrast = contrast.IsChecked == true;
            settings.NormalizeLanguageAccessibility();
            try
            {
                // ChangeSettings logs persistence failures internally; this explicit write must succeed before announcing success.
                app.Store.Save("settings", settings);
                app.ChangeSettings();
                Accessibility.Announce(notice, Localization.Text("Saved. Quit and reopen NabyScape to apply language, text size and contrast."));
            }
            catch (Exception ex)
            {
                (settings.UiLanguage, settings.AccessibilityTextScale, settings.AccessibilityHighContrast) = previous;
                app.Store.Log(ex);
                Accessibility.Announce(notice, Localization.Text("Could not save changes. Try again."));
            }
        }, true);
        page.Children.Add(Section("Language & accessibility", UI.Caption("Interface language", 12), language,
            UI.Caption("English is used when a translation is unavailable. Your content stays in its original language.", 11, UI.Muted),
            UI.Caption("Text size", 12), scale, contrast,
            UI.Caption("Windows high contrast is always respected. Larger text may require scrolling in compact widgets.", 11, UI.Muted),
            Check("Reduce motion", settings.ReduceMotion, value => settings.ReduceMotion = value),
            UI.Caption("Motion also follows the Windows animation setting.", 11, UI.Muted), save, notice));
    }
}
