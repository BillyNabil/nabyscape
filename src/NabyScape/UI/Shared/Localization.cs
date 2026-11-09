using System;
using System.Globalization;

namespace NabyScape;

// English source captions are catalog keys. Call only for application-owned UI text.
// Never apply this to transcripts, user labels, filenames, provider IDs or persisted values.
internal static class Localization
{
    internal static bool CollectMissing { get; set; }
    internal static readonly System.Collections.Generic.HashSet<string> MissingCaptions=new(StringComparer.Ordinal);
    internal static string Language { get; private set; } = "ru";
    internal static CultureInfo Culture => CultureInfo.GetCultureInfo(Language switch { "ru"=>"ru-RU", "es"=>"es-ES", "id"=>"id-ID", _=>"en-US" });
    internal static void Configure(Settings settings)
    {
        settings.NormalizeLanguageAccessibility();
        Language = settings.UiLanguage;
        Accessibility.Configure(settings);
        // Deliberately preserve CurrentCulture: numeric editors retain their existing parsing.
    }

    internal static string Text(string english)
    {
        if (Language == "ru" && RussianCatalog.Entries.TryGetValue(english, out string? russian)) return russian;
        if(Language=="ru" && CollectMissing && english.AnyAsciiLetter())lock(MissingCaptions)MissingCaptions.Add(english);
        if (!LocalizationCatalog.English.TryGetValue(english, out var entry)) return english;
        return Language switch { "es" when entry.Spanish.Length > 0 => entry.Spanish,
            "id" when entry.Indonesian.Length > 0 => entry.Indonesian, _ => english };
    }

    internal static string Format(string english, params object[] values)
        => string.Format(CultureInfo.CurrentCulture, Text(english), values);
}

internal static class CaptionInspection
{
    internal static bool AnyAsciiLetter(this string text)
    {foreach(char c in text)if(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')return true;return false;}
}
