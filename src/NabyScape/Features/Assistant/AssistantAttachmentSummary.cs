using System;
namespace NabyScape;
internal static class AssistantAttachmentSummary
{
    internal static string DisplayNote(AssistantAttachment file)
    {
        string note=file.Note;
        if(note.StartsWith("From ",StringComparison.Ordinal))return Localization.Format("From {0}",note[5..]);
        var pdf=System.Text.RegularExpressions.Regex.Match(note,@"^PDF · (\d+) pages · Hermes sees first (\d+) pages$");
        if(pdf.Success)return Localization.Format("PDF · {0} pages · Hermes sees first {1} pages",pdf.Groups[1].Value,pdf.Groups[2].Value);
        var video=System.Text.RegularExpressions.Regex.Match(note,@"^Video · ([\d.,]+) seconds · 8 sampled frames, no audio outside Gemini$");
        if(video.Success)return Localization.Format("Video · {0} seconds · 8 sampled frames, no audio outside Gemini",video.Groups[1].Value);
        return note switch
        {
            "Image · optimized for chat" or "Pasted image · optimized for chat" or "Video · original file for Gemini" or "Video · original file for Gemini; Windows could not create preview frames" or "Audio · requires Gemini audio input" or "Document · extracted text; embedded media and formatting are not included"=>Localization.Text(note),
            _=>note
        };
    }
    internal static string Describe(AssistantAttachment file,AssistantProvider? provider)
    {
        bool native=provider?.Protocol=="Gemini";string note=DisplayNote(file);
        if(file.Mime=="application/pdf")note=provider?.Backend=="Hermes"?Localization.Text("PDF · first ")+(file.Frames?.Count??0)+Localization.Text(" pages sent as images; later pages are not included."):Localization.Text("PDF · original document sent to the provider; PDF support depends on the selected model.");
        else if(file.Mime.StartsWith("video/",StringComparison.Ordinal))note=native?Localization.Text("Video · original video sent to Gemini"):Localization.Text("Video · ")+(file.Frames?.Count??0)+Localization.Text(" sampled frames · audio is not included.");
        if((file.IsImage || file.Frames is {Count:>0}) && provider?.Vision!=true)note+=Localization.Text(" Image input is Off. Enable it and save your provider before sending visual content.");
        return note;
    }
}
