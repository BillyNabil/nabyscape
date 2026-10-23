using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NabyScape;

// Only model files are user supplied. Executables always come from the pinned engine installer.
internal static class AssistantOwnSpeech
{
    internal static readonly string[] AsrEngines={"Installed pack","Whisper GGML file","Qwen3-ASR INT8 folder","Moonshine v2 folder"};
    internal static readonly string[] TtsEngines={"Installed pack","Windows voice","Piper ONNX file","Kokoro folder","KittenTTS folder","Supertonic folder"};
    internal static SpeechAsrModel Asr(AssistantLocalVoiceOptions options)
    {
        if(!options.OwnModels || options.OwnAsrEngine=="Installed pack")return SpeechCatalog.AsrById(options.ManagedAsr);
        string path=LocalPath(options.OwnAsrPath);string engine;
        switch(options.OwnAsrEngine)
        {
            case "Whisper GGML file":
                RequireFile(path,".bin");using(var input=File.OpenRead(path))using(var reader=new BinaryReader(input))
                {if(input.Length<1024*1024 || reader.ReadUInt32()!=0x67676d6c)throw new IOException("Choose a whisper.cpp GGML .bin model. GGUF chat models and safetensors are not supported here.");}
                engine="whisper";break;
            case "Qwen3-ASR INT8 folder":
                RequireFiles(path,"conv_frontend.onnx","encoder.int8.onnx","decoder.int8.onnx","tokenizer/vocab.json","tokenizer/merges.txt");engine="qwen";break;
            case "Moonshine v2 folder":
                RequireFiles(path,"tokens.txt");MoonshineFile(path,"encoder_model");MoonshineFile(path,"decoder_model_merged");engine="moonshine";break;
            default:throw new IOException("Choose a supported listening model format.");
        }
        return new("own-asr","Your listening model",0,0,engine=="whisper"?1.4:double.PositiveInfinity,new("",engine=="whisper"?new FileInfo(path).Length:0,""),engine,SourcePath:path);
    }
    internal static SpeechTtsModel Tts(AssistantLocalVoiceOptions options)
    {
        if(!options.OwnModels || options.OwnTtsEngine=="Installed pack")return SpeechCatalog.TtsById(options.ManagedTts);
        if(options.OwnTtsEngine=="Windows voice")return SpeechCatalog.TtsById("windows");
        string path=LocalPath(options.OwnTtsPath);string engine;
        switch(options.OwnTtsEngine)
        {
            case "Piper ONNX file":
                RequireFile(path,".onnx");RequireFile(path+".json");
                if(new FileInfo(path+".json").Length>512*1024)throw new IOException("The Piper configuration exceeds 512 KB.");
                using(var config=JsonDocument.Parse(File.ReadAllText(path+".json"),new JsonDocumentOptions{MaxDepth=32}))
                {
                    if(!config.RootElement.TryGetProperty("audio",out var audio) || !audio.TryGetProperty("sample_rate",out var rate) || !rate.TryGetInt32(out int sampleRate) || sampleRate<8000 || sampleRate>48000)throw new IOException("Piper needs a matching .onnx.json with an audio sample_rate from 8000 to 48000 Hz.");
                    if(options.OwnSpeaker>0 && (!config.RootElement.TryGetProperty("num_speakers",out var count) || !count.TryGetInt32(out int speakers) || options.OwnSpeaker>=speakers))throw new IOException("That speaker ID is not available in this Piper voice.");
                }
                engine="piper";break;
            case "Kokoro folder":case "KittenTTS folder":
                if(options.SpeechLanguage!="en")throw new IOException("These Kokoro/Kitten adapters use English voices. Select English, or use an external service for other configurations.");
                Weights(path);RequireFiles(path,"voices.bin","tokens.txt","espeak-ng-data/phontab","espeak-ng-data/phondata","espeak-ng-data/phonindex");engine=options.OwnTtsEngine=="Kokoro folder"?"kokoro":"kitten";break;
            case "Supertonic folder":
                RequireFiles(path,"duration_predictor.int8.onnx","text_encoder.int8.onnx","vector_estimator.int8.onnx","vocoder.int8.onnx","tts.json","unicode_indexer.bin","voice.bin");engine="supertonic";break;
            default:throw new IOException("Choose a supported speaking model format.");
        }
        if(options.OwnSpeaker<0 || options.OwnSpeaker>255)throw new IOException("Speaker ID must be between 0 and 255 and exist in your model.");
        return new("own-tts","Your speaking model","",engine=="piper"?new FileInfo(path).Length:0,"","","",engine=="piper"?null:new("",0,""),options.SpeechLanguage,options.OwnSpeaker,0,engine,SourcePath:path);
    }
    internal static string LocalPath(string value)
    {
        string path=Environment.ExpandEnvironmentVariables((value??"").Trim().Trim('"'));
        if(!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\",StringComparison.Ordinal))throw new IOException("Choose a local file or folder using its full drive path.");
        return Path.GetFullPath(path);
    }
    internal static string MoonshineFile(string folder,string name)
    {
        foreach(string suffix in new[]{".ort",".onnx"}){string path=Path.Combine(folder,name+suffix);if(File.Exists(path) && new FileInfo(path).Length>0)return path;}
        throw new IOException("Missing "+name+".ort or "+name+".onnx in the Moonshine folder.");
    }
    internal static string Weights(string folder)
    {
        if(!Directory.Exists(folder))throw new IOException("The model folder was moved or is unavailable. Choose it again.");
        foreach(string name in new[]{"model.int8.onnx","model.onnx"}){string path=Path.Combine(folder,name);if(File.Exists(path)){RequireFile(path);return path;}}
        var files=Directory.EnumerateFiles(folder,"*.onnx").Take(2).ToArray();if(files.Length!=1)throw new IOException("Choose a voice folder with model.onnx, model.int8.onnx or exactly one ONNX model.");RequireFile(files[0]);return files[0];
    }
    private static void RequireFile(string path,string extension="")
    {
        if(extension.Length>0 && !path.EndsWith(extension,StringComparison.OrdinalIgnoreCase))throw new IOException("Expected a "+extension+" model file.");
        if(!File.Exists(path) || new FileInfo(path).Length==0)throw new IOException("Missing model file: "+Path.GetFileName(path)+". Choose the complete model and its matching support files.");
    }
    private static void RequireFiles(string folder,params string[] files)
    {if(!Directory.Exists(folder))throw new IOException("Choose an existing, extracted model folder.");foreach(string file in files)RequireFile(Path.Combine(folder,file));}
}
