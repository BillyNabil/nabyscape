using System;
using System.Linq;
using System.Text.Json;

namespace NabyScape;

internal static class AssistantProviderPersistence
{
    private static string Origin(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)?uri.GetLeftPart(UriPartial.Authority):"";
    // Stage changed credentials under a fresh identity. A failed metadata write
    // can never pair a new server's credential with a previously saved server.
    internal static AssistantOptions Save(AssistantOptions current,string originalId,AssistantProvider candidate,string replacementKey,IAssistantKeys keys,Action<AssistantOptions> persist)
    {
        var original=current.Providers.Single(p=>p.Id==originalId);
        var next=JsonSerializer.Deserialize<AssistantOptions>(JsonSerializer.Serialize(current))!;
        var saved=JsonSerializer.Deserialize<AssistantProvider>(JsonSerializer.Serialize(candidate))!;
        bool rotate=replacementKey.Length>0 || Origin(original.BaseUrl)!=Origin(saved.BaseUrl) || Origin(original.RealtimeUrl)!=Origin(saved.RealtimeUrl) || saved.Backend=="Hermes" && original.BaseUrl.TrimEnd('/')!=saved.BaseUrl.TrimEnd('/');
        saved.Id=rotate?Guid.NewGuid().ToString("N"):original.Id;
        next.Providers[next.Providers.FindIndex(p=>p.Id==originalId)]=saved;next.SelectedProvider=saved.Id;
        if(next.SelectedVoiceProvider==originalId)next.SelectedVoiceProvider=saved.Id;
        try
        {
            if(replacementKey.Length>0)keys.Write(saved.Id,replacementKey);
            persist(next);
        }
        catch
        {
            if(rotate){try{keys.Remove(saved.Id);}catch{/* Unreferenced staged credential; never used by a profile. */}}
            throw;
        }
        if(rotate){try{keys.Remove(originalId);}catch{/* Old unreferenced credential cannot be used by this profile. */}}
        return next;
    }
}
