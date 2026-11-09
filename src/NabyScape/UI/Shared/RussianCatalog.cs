using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NabyScape;

// Captions are explicit English keys. User data must never pass through this catalog.
internal static class RussianCatalog
{
    internal static readonly IReadOnlyDictionary<string,string> Entries=Load();
    private static IReadOnlyDictionary<string,string> Load()
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        var assembly=typeof(RussianCatalog).Assembly;
        foreach(string resource in assembly.GetManifestResourceNames().Where(n=>n.Contains(".Russian.",StringComparison.Ordinal) && n.EndsWith(".json",StringComparison.Ordinal)).OrderBy(n=>n.EndsWith(".Russian.Base.json",StringComparison.Ordinal)?0:1).ThenBy(n=>n,StringComparer.Ordinal))
        {
            using var stream=assembly.GetManifestResourceStream(resource)!;
            var entries=JsonSerializer.Deserialize<Dictionary<string,string>>(stream) ?? throw new InvalidOperationException("Empty translation resource: "+resource);
            foreach(var entry in entries)
            {
                if(string.IsNullOrWhiteSpace(entry.Value))throw new InvalidOperationException("Empty Russian caption: "+entry.Key);
                // Base terminology takes priority; domain catalogs supply additional captions.
                result.TryAdd(entry.Key,entry.Value);
            }
        }
        return result;
    }
}
