using System;
using System.Globalization;

namespace NabyScape;

// English source captions are catalog keys. Call only for application-owned UI text.
// Never apply this to transcripts, user labels, filenames, provider IDs or persisted values.
internal static class Localization
{
    internal static string Language { get; private set; } = "en";
    internal static void Configure(Settings settings)
    {
        settings.NormalizeLanguageAccessibility();
        Language = settings.UiLanguage;
        Accessibility.Configure(settings);
        // Deliberately preserve CurrentCulture: numeric editors retain their existing parsing.
    }

    internal static string Text(string english)
    {
        if (!LocalizationCatalog.English.TryGetValue(english, out var entry)) return english;
        return Language switch { "es" when entry.Spanish.Length > 0 => entry.Spanish,
            "id" when entry.Indonesian.Length > 0 => entry.Indonesian, _ => english };
    }

    internal static string Format(string english, params object[] values)
        => string.Format(CultureInfo.CurrentCulture, Text(english), values);
}
