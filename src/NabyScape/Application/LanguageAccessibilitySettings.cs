using System;

namespace NabyScape;

public sealed partial class Settings
{
    public string UiLanguage { get; set; } = "ru";
    public double AccessibilityTextScale { get; set; } = 1;
    public bool AccessibilityHighContrast { get; set; }

    internal void NormalizeLanguageAccessibility()
    {
        UiLanguage = UiLanguage is "es" or "id" or "ru" ? UiLanguage : "en";
        AccessibilityTextScale = double.IsFinite(AccessibilityTextScale)
            ? Math.Clamp(AccessibilityTextScale, 1, 1.5) : 1;
    }
}
