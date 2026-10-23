using System;
namespace NabyScape;
internal static class AssistantAttachmentSummary
{
    internal static string Describe(AssistantAttachment file,AssistantProvider? provider)
    {
        bool native=provider?.Protocol=="Gemini";string note=file.Note;
        if(file.Mime=="application/pdf")note=provider?.Backend=="Hermes"?"PDF · first "+(file.Frames?.Count??0)+" pages sent as images; later pages are not included.":"PDF · original document sent to the provider; PDF support depends on the selected model.";
        else if(file.Mime.StartsWith("video/",StringComparison.Ordinal))note=native?"Video · original video sent to Gemini":"Video · "+(file.Frames?.Count??0)+" sampled frames · audio is not included.";
        if((file.IsImage || file.Frames is {Count:>0}) && provider?.Vision!=true)note+=" Image input is Off. Enable it and save your provider before sending visual content.";
        return note;
    }
}
