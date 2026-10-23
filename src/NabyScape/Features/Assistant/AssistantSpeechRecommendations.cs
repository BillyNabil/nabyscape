using System;
using System.Linq;
namespace NabyScape;
internal sealed record SpeechPack(SpeechAsrModel Asr,SpeechTtsModel Tts,bool Gpu,string Reason);
internal sealed record SpeechHardwareProfile(string Name,int RamGb,int VramGb,string AsrId);
internal static partial class SpeechCatalog
{
    private static SpeechAsset ReleaseModel(string name,long bytes,string hash,bool tts=false)=>new("https://github.com/k2-fsa/sherpa-onnx/releases/download/"+(tts?"tts-models/":"asr-models/")+name+".tar.bz2",bytes,hash);
    private static SpeechModelFile[] Qwen17Files()=>new SpeechModelFile[]{
        new("README.md",new("https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/69eb686fd94a4a865bb5340a3d6ac0d7f1fec0d5/README.md",1787,"c67121ba17a3718d059c58c59b2391464a8fc0ebefbfc38444e33fc454517461")),
        new("conv_frontend.onnx",new("https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/69eb686fd94a4a865bb5340a3d6ac0d7f1fec0d5/conv_frontend.onnx",48080441,"3cb27a9fe94d95c938e476f2012b21aba2ec0bfceef33b0e58acd208946bafdd")),
        new("decoder.int8.onnx",new("https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/69eb686fd94a4a865bb5340a3d6ac0d7f1fec0d5/decoder.int8.onnx",2037458645,"c43c853fa6e97d08365cb8a5502b360b595cd43c00dc60e4d8ca7cc18cad460b")),
        new("encoder.int8.onnx",new("https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/69eb686fd94a4a865bb5340a3d6ac0d7f1fec0d5/encoder.int8.onnx",314222162,"a5deedae034ece715de8ed204378d8c77f889af3a60c2566581135e84cced7cd")),
        new("tokenizer/merges.txt",new("https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/69eb686fd94a4a865bb5340a3d6ac0d7f1fec0d5/tokenizer/merges.txt",1671853,"8831e4f1a044471340f7c0a83d7bd71306a5b867e95fd870f74d0c5308a904d5")),
        new("tokenizer/tokenizer_config.json",new("https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/69eb686fd94a4a865bb5340a3d6ac0d7f1fec0d5/tokenizer/tokenizer_config.json",12487,"4942d005604266809309cabc9f4e9cb89ce855d59b14681fdc0e1cc62ea26c4c")),
        new("tokenizer/vocab.json",new("https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8/resolve/69eb686fd94a4a865bb5340a3d6ac0d7f1fec0d5/tokenizer/vocab.json",2776833,"ca10d7e9fb3ed18575dd1e277a2579c16d108e32f27439684afa0e10b1440910")),
    };
    private static SpeechAsrModel[] AdditionalAsr()=>new SpeechAsrModel[]{
        new("base-q5_1","Whisper base Q5 · 2 GB GPU / light CPU",4,1,1.4,Whisper("base-q5_1",59707625,"422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898")),
        new("small-q5_1","Whisper small Q5 · 4 GB GPU",8,2,2.8,Whisper("small-q5_1",190085487,"ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb")),
        new("large-v3-turbo-q5_0","Whisper large-v3-turbo Q5 · 6 GB GPU",8,3,4.5,Whisper("large-v3-turbo-q5_0",574041195,"394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2")),
        new("large-v3-turbo-q8_0","Whisper large-v3-turbo Q8 · 8 GB GPU",12,4,6,Whisper("large-v3-turbo-q8_0",874188075,"317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1")),
        new("moonshine-v2-tiny-en","Moonshine v2 tiny · 2026 English CPU",4,.6,double.PositiveInfinity,ReleaseModel("sherpa-onnx-moonshine-tiny-en-quantized-2026-02-27",29858559,"9ec31b342d8fa3240c3b81b8f82e1cf7e3ac467c93ca5a999b741d5887164f8d"),"moonshine","sherpa-onnx-moonshine-tiny-en-quantized-2026-02-27","en",Info:"English only · compact 2026 native CPU pack"),
        new("moonshine-v2-base-en","Moonshine v2 base · 2026 English CPU",8,1,double.PositiveInfinity,ReleaseModel("sherpa-onnx-moonshine-base-en-quantized-2026-02-27",111266225,"43232c1d13013d37317163baec3135bd771a186a4356f28c889bab453bb0e891"),"moonshine","sherpa-onnx-moonshine-base-en-quantized-2026-02-27","en",Info:"English only · CPU · model loads for each utterance"),
        new("moonshine-v2-base-es","Moonshine v2 base · 2026 Spanish CPU",8,1,double.PositiveInfinity,ReleaseModel("sherpa-onnx-moonshine-base-es-quantized-2026-02-27",50846902,"850c3dcc5dfccc8b1feb10bb221b11d6039b6f5c626241729f46863771016383"),"moonshine","sherpa-onnx-moonshine-base-es-quantized-2026-02-27","es",Info:"Spanish only · CPU · model loads for each utterance"),
        new("qwen3-asr-1.7b-int8","Qwen3-ASR 1.7B INT8 · 2026 large CPU",24,7,double.PositiveInfinity,Qwen17Files()[0].Asset,"qwen","","multi",Qwen17Files(),"Community ONNX conversion by thieunv-asilla of Qwen's model · 2.40 GB download · CPU latency can be high · recommended for 32 GB RAM, 12+ threads")
    };
    private static SpeechTtsModel[] AdditionalTts()=>new SpeechTtsModel[]{
        new("kitten-nano-v0.8-int8-en","KittenTTS nano v0.8 INT8 · light English","",31220690,"","","",ReleaseModel("kitten-nano-en-v0_8-int8",31220690,"6fa5be852612ce761094ba74ee6123b4fc4acfefa79bf64dc63acae4a83af2fd",true),"en",0,.35,"kitten","kitten-nano-en-v0_8-int8","Compact English CPU voice · no reference recording needed"),
        new("supertonic-3-int8","Supertonic 3 INT8 · 2026 multilingual","",128774318,"","","",ReleaseModel("sherpa-onnx-supertonic-3-tts-int8-2026-05-11",128774318,"82fa96f91c4ef8abaae3a14a3f4153facf88bed821d1f7331cec2700f432c427",true),"multi",0,1,"supertonic","sherpa-onnx-supertonic-3-tts-int8-2026-05-11","2026 multilingual CPU voice · preset speaker · OpenRAIL-M model license; read its terms before use")
    };
    internal static readonly SpeechHardwareProfile[] Profiles={
        new("CPU · RAM 8 GB",8,0,"moonshine-v2-base-en"),new("CPU · RAM 16 GB",16,0,"qwen3-asr-0.6b-int8"),new("CPU · RAM 32 GB",32,0,"qwen3-asr-1.7b-int8"),
        new("NVIDIA · VRAM 2 GB",8,2,"base-q5_1"),new("NVIDIA · VRAM 4 GB",8,4,"small-q5_1"),new("NVIDIA · VRAM 6 GB",16,6,"large-v3-turbo-q5_0"),new("NVIDIA · VRAM 8 GB+",16,8,"large-v3-turbo-q8_0")};
    internal static SpeechTtsModel RecommendVoice(SpeechHardware h,SpeechAsrModel a,string language,string preference)
    {
        string[] ids=(preference=="Light" || h.RamGb<12) && language=="en"?new[]{"kitten-nano-v0.8-int8-en","supertonic-3-int8"}:new[]{"supertonic-3-int8","kitten-nano-v0.8-int8-en"};
        return ids.Select(TtsById).Concat(Tts.Where(v=>v.Package==null && v.Id!="windows")).Append(TtsById("windows")).First(v=>v.Supports(language) && h.FitsPack(a,v));
    }
    internal static SpeechPack? ProfilePack(SpeechHardware h,SpeechHardwareProfile profile,string language,string preference)
    {
        string id=profile.AsrId;if(profile.VramGb==0 && profile.RamGb==8)id=language=="en"?"moonshine-v2-base-en":language=="es"?"moonshine-v2-base-es":"base-q5_1";
        var a=AsrById(id);if(!a.Supports(language) || h.RamGb+.5<profile.RamGb || !h.Fits(a) || profile.VramGb>0 && (h.VramGb+.5<profile.VramGb || !h.UseGpu(a)))return null;
        SpeechTtsModel t;try{t=RecommendVoice(h,a,language,preference);}catch(InvalidOperationException){return null;}return new(a,t,profile.VramGb>0,profile.Name+" · TTS stays on CPU to leave VRAM for recognition and other apps.");
    }
    // Conservative budgets, not a benchmark: keep both language and the whole pack within available memory.
    internal static SpeechPack RecommendPack(SpeechHardware h,string language,string preference,bool allowGpu=true)
    {
        var candidates=new System.Collections.Generic.List<string>();
        if(allowGpu && h.Cuda)
        {
            if(preference!="Light")
            {if(h.VramGb>=7.5)candidates.Add("large-v3-turbo-q8_0");if(h.VramGb>=5.5)candidates.Add("large-v3-turbo-q5_0");if(h.VramGb>=3.5)candidates.Add("small-q5_1");}
            candidates.Add("base-q5_1");
        }
        if(preference=="Quality" && h.RamGb>=31.5 && h.Threads>=12)candidates.Add("qwen3-asr-1.7b-int8");
        if(preference!="Light" && h.Threads>=6 && h.RamGb>=15.5)candidates.Add("qwen3-asr-0.6b-int8");
        if(language=="en")candidates.Add(preference=="Light"?"moonshine-v2-tiny-en":"moonshine-v2-base-en");
        if(language=="es")candidates.Add("moonshine-v2-base-es");
        if(preference!="Light" && h.Threads>=8)candidates.Add("small-q5_1");candidates.Add("base-q5_1");candidates.Add("tiny");
        foreach(string id in candidates.Distinct())
        {
            var a=AsrById(id);if(!h.Fits(a) || !a.Supports(language))continue;
            bool gpu=allowGpu && h.UseGpu(a);if(a.Engine=="whisper" && id.StartsWith("large-") && !gpu)continue;
            try{var t=RecommendVoice(h,a,language,preference);return new(a,t,gpu,h.Tier+" detected · "+(gpu?"ASR on NVIDIA GPU, TTS on CPU.":"Native CPU speech; dedicated VRAM is not needed.")+" Based on available memory, language and "+preference.ToLowerInvariant()+" priority.");}catch(InvalidOperationException){}
        }
        return new(AsrById("tiny"),TtsById("windows"),false,"Not enough memory or unsupported CPU. Close other apps or use an external speech service.");
    }
    internal static string AsrDetails(SpeechAsrModel a)=>a.Info.Length>0?a.Info:a.Engine=="qwen"?"2026 multilingual recognition · CPU INT8 · loads for each utterance":"Multilingual Whisper · CPU / supported NVIDIA GPU · model stays loaded during the voice session";
    internal static string VoiceDetails(SpeechTtsModel t)=>t.Info.Length>0?t.Info:t.Id=="windows"?"Default installed Windows voice; its language must match your selection":"Piper native CPU voice for the selected language";
    internal static string ModelLink(SpeechAsrModel a)=>a.Id=="qwen3-asr-1.7b-int8"?"https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8":a.Engine=="moonshine"?"https://k2-fsa.github.io/sherpa/onnx/moonshine/models-v2.html":a.Engine=="qwen"?"https://github.com/QwenLM/Qwen3-ASR":"https://github.com/ggml-org/whisper.cpp";
    internal static string VoiceLink(SpeechTtsModel t)=>t.Engine=="supertonic"?"https://huggingface.co/Supertone/supertonic-3/blob/main/LICENSE":t.Engine=="kitten"?"https://huggingface.co/KittenML/kitten-tts-nano-0.8-int8":t.Package!=null?"https://huggingface.co/hexgrad/Kokoro-82M-v1.1-zh":VoiceUrl(t,"MODEL_CARD").Replace("/resolve/","/blob/");
}
