using System;
using System.Collections.Generic;
using System.Linq;

namespace NabyScape;

// UI string catalog. The exact English caption is the key; every language lives in its own file,
// LocalizationCatalog.<code>.cs, as one ordinal dictionary of ["English caption"] = "translation".
// A missing key or blank translation deliberately falls back to the English caption.
// New string: add the same key to every language file. New language: one file plus one row in Languages.
// Self-tests require every language to cover every key with the same {0}, {1}… placeholders. See docs/translating.md.
internal static partial class LocalizationCatalog
{
    // Picker order. Native names stay untranslated so every reader can find their own language.
    internal static readonly IReadOnlyList<(string Code, string Name, IReadOnlyDictionary<string, string> Translations)> Languages;
    // Every English key that any language translates.
    internal static readonly IReadOnlySet<string> Keys;

    // Built here because field initializers from all partial files run before a static constructor body.
    static LocalizationCatalog()
    {
        Languages = new (string, string, IReadOnlyDictionary<string, string>)[]
        {
            ("en", "English", new Dictionary<string, string>(StringComparer.Ordinal)),
            ("es", "Español", Spanish),
            ("id", "Bahasa Indonesia", Indonesian),
            ("ru", "Русский", Russian),
            ("uk", "Українська", Ukrainian),
            ("ko", "한국어", Korean),
        };
        Keys = Languages.SelectMany(language => language.Translations.Keys).ToHashSet(StringComparer.Ordinal);
    }

    internal static bool IsSupported(string? code) => Languages.Any(language => language.Code == code);
    internal static IReadOnlyDictionary<string, string> Translations(string code) => Find(code).Translations;
    internal static string NativeName(string code) => Find(code).Name;
    private static (string Code, string Name, IReadOnlyDictionary<string, string> Translations) Find(string code)
    {
        foreach (var language in Languages) if (language.Code == code) return language;
        return Languages[0];
    }
}
