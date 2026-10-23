using System;

namespace NabyScape;

public sealed partial class Settings
{
    public string UiLanguage { get; set; } = "en";
    public double AccessibilityTextScale { get; set; } = 1;
    public bool AccessibilityHighContrast { get; set; }

    internal void NormalizeLanguageAccessibility()
    {
        UiLanguage = UiLanguage is "es" or "id" ? UiLanguage : "en";
        AccessibilityTextScale = double.IsFinite(AccessibilityTextScale)
            ? Math.Clamp(AccessibilityTextScale, 1, 1.5) : 1;
    }
}
