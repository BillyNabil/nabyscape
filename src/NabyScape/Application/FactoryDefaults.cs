using System;
using System.IO;

namespace NabyScape;

// Applied only to absent/unreadable preferences and explicit preference reset.
// Persisted profiles retain their historical deserialization/migration behavior.
internal static class FactoryDefaults
{
    internal static Settings Create()
    {
        using var stream=typeof(FactoryDefaults).Assembly.GetManifestResourceStream("NabyScape.DefaultPreferences.json")
            ?? throw new InvalidOperationException("The bundled default preferences are missing.");
        var settings=System.Text.Json.JsonSerializer.Deserialize<Settings>(stream)
            ?? throw new InvalidOperationException("The bundled default preferences are invalid.");
        settings.Normalize();return settings;
    }
}
