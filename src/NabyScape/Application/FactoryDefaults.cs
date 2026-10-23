using System;
using System.IO;

namespace NabyScape;

// Applied only to absent/unreadable preferences and explicit preference reset.
// Persisted profiles retain their historical deserialization/migration behavior.
internal static class FactoryDefaults
{
    internal static Settings Create()
    {
        using var stream=typeof(FactoryDefaults).Assembly.GetManifestResourceStream("NabyScape.DefaultLayout.json")
            ?? throw new InvalidOperationException("The bundled default layout is missing.");
        using var reader=new StreamReader(stream);
        // New profiles opt into downloads; existing saved/legacy choices retain their own value.
        var settings=new Settings{AutoDownloadUpdates=true};
        CustomizationProfile.Import(reader.ReadToEnd(),settings);
        return settings;
    }
}
