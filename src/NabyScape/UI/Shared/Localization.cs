using System;
using System.Collections.Generic;
using System.Globalization;

namespace NabyScape;

// English source captions are catalog keys. Call only for application-owned UI text.
// Never apply this to transcripts, user labels, filenames, provider IDs or persisted values.
internal static class Localization
{
    internal static string Language { get; private set; } = "en";
    private static IReadOnlyDictionary<string, string> translations = LocalizationCatalog.Translations("en");
    internal static void Configure(Settings settings)
    {
        settings.NormalizeLanguageAccessibility();
        Language = settings.UiLanguage;
        translations = LocalizationCatalog.Translations(Language);
        Accessibility.Configure(settings);
        // Deliberately preserve CurrentCulture: numeric editors retain their existing parsing.
    }

    // One ordinal lookup in the selected language; a missing or blank translation returns the English key.
    internal static string Text(string english)
        => translations.TryGetValue(english, out var text) && text.Length > 0 ? text : english;

    internal static string Format(string english, params object[] values)
        => string.Format(CultureInfo.CurrentCulture, Text(english), values);
}
