using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace NabyScape;

// Private worker only. Layouts follow the pinned sherpa-onnx v1.13.8 c-api.h.
// The native engine is never loaded in the UI process.
internal sealed class AssistantSpeechNative : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Vits {public IntPtr Model,Lexicon,Tokens,Data;public float Noise,NoiseW,Length;public IntPtr Dict;}
    [StructLayout(LayoutKind.Sequential)] private struct Matcha {public IntPtr Acoustic,Vocoder,Lexicon,Tokens,Data;public float Noise,Length;public IntPtr Dict;}
    [StructLayout(LayoutKind.Sequential)] private struct Kokoro {public IntPtr Model,Voices,Tokens,Data;public float Length;public IntPtr Dict,Lexicon,Lang;}
    [StructLayout(LayoutKind.Sequential)] private struct Kitten {public IntPtr Model,Voices,Tokens,Data;public float Length;}
    [StructLayout(LayoutKind.Sequential)] private struct Zipvoice {public IntPtr Tokens,Encoder,Decoder,Vocoder,Data,Lexicon;public float Feat,Shift,Rms,Guidance;}
    [StructLayout(LayoutKind.Sequential)] private struct Pocket {public IntPtr Flow,Main,Encoder,Decoder,Conditioner,Vocab,Scores;public int Cache;}
    [StructLayout(LayoutKind.Sequential)] private struct Supertonic {public IntPtr Duration,Text,Vector,Vocoder,Json,Indexer,Voice;}
    [StructLayout(LayoutKind.Sequential)] private struct Model {public Vits Vits;public int Threads,Debug;public IntPtr Provider;public Matcha Matcha;public Kokoro Kokoro;public Kitten Kitten;public Zipvoice Zipvoice;public Pocket Pocket;public Supertonic Supertonic;}
    [StructLayout(LayoutKind.Sequential)] private struct Config {public Model Model;public IntPtr Rules;public int Sentences;public IntPtr Fars;public float Silence;}
    [StructLayout(LayoutKind.Sequential)] private struct Generation {public float Silence,Speed;public int Speaker;public IntPtr Reference;public int ReferenceLength,ReferenceRate;public IntPtr ReferenceText;public int Steps;public IntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] private struct Audio {public IntPtr Samples;public int Count,Rate;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create(ref Config config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Generate(IntPtr engine,[MarshalAs(UnmanagedType.LPUTF8Str)] string text,ref Generation config,IntPtr callback,IntPtr arg);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Version();
    private readonly List<IntPtr> strings=new();
    private IntPtr library,engine,searchPath;
    private Destroy? destroyEngine,destroyAudio;
    private Generate? generate;
    private readonly AssistantSpeechWorker.Setup setup;
    internal AssistantSpeechNative(AssistantSpeechWorker.Setup setup)
    {
        AssistantSpeechPlatform.RequireNativePacks();
        this.setup=setup;
        try
        {
            string libraryPath=Path.GetFullPath(setup.Library);
            if(Path.GetFileName(libraryPath)!="sherpa-onnx-c-api.dll" || !File.Exists(libraryPath))throw new IOException("Prepare the pinned speech engine again.");
            searchPath=AddDllDirectory(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(libraryPath)!,"..","bin")));
            if(searchPath==IntPtr.Zero)throw new Win32Exception();
            library=LoadLibraryEx(libraryPath,IntPtr.Zero,0x100|0x1000);
            if(library==IntPtr.Zero)throw new Win32Exception();
            T Export<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
            string version=Marshal.PtrToStringUTF8(Export<Version>("SherpaOnnxGetVersionStr")())??"";
            if(version.TrimStart('v')!="1.13.8")throw new IOException("The speech engine version differs from the pinned catalog. Prepare it again.");
            destroyEngine=Export<Destroy>("SherpaOnnxDestroyOfflineTts");destroyAudio=Export<Destroy>("SherpaOnnxDestroyOfflineTtsGeneratedAudio");generate=Export<Generate>("SherpaOnnxOfflineTtsGenerateWithConfig");
            IntPtr Text(string text){var p=Marshal.StringToCoTaskMemUTF8(text);strings.Add(p);return p;}
            IntPtr FilePath(string name)=>Text(Path.Combine(setup.Folder,name));
            var config=new Config{Sentences=1,Silence=.2f,Model=new Model{Threads=Math.Clamp(Environment.ProcessorCount/2,1,4),Provider=Text("cpu")}};
            if(setup.Engine=="supertonic")config.Model.Supertonic=new(){Duration=FilePath("duration_predictor.int8.onnx"),Text=FilePath("text_encoder.int8.onnx"),Vector=FilePath("vector_estimator.int8.onnx"),Vocoder=FilePath("vocoder.int8.onnx"),Json=FilePath("tts.json"),Indexer=FilePath("unicode_indexer.bin"),Voice=FilePath("voice.bin")};
            else
            {
                string weights=File.Exists(Path.Combine(setup.Folder,"model.int8.onnx"))?"model.int8.onnx":File.Exists(Path.Combine(setup.Folder,"model.onnx"))?"model.onnx":FindWeights(setup.Folder);
                if(setup.Engine=="kitten")config.Model.Kitten=new(){Model=FilePath(weights),Voices=FilePath("voices.bin"),Tokens=FilePath("tokens.txt"),Data=FilePath("espeak-ng-data"),Length=1};
                else if(setup.Engine=="kokoro")
                {
                    var lexicons=new List<string>();foreach(string name in new[]{"lexicon-us-en.txt","lexicon-zh.txt"})if(File.Exists(Path.Combine(setup.Folder,name)))lexicons.Add(Path.Combine(setup.Folder,name));
                    config.Model.Kokoro=new(){Model=FilePath(weights),Voices=FilePath("voices.bin"),Tokens=FilePath("tokens.txt"),Data=FilePath("espeak-ng-data"),Length=1,Lexicon=Text(string.Join(",",lexicons)),Lang=Text(setup.Language=="zh"?"zh":"en-us"),Dict=Directory.Exists(Path.Combine(setup.Folder,"dict"))?FilePath("dict"):IntPtr.Zero};
                }
                else throw new IOException("This voice does not support the persistent native engine.");
            }
            engine=Export<Create>("SherpaOnnxCreateOfflineTts")(ref config);
            if(engine==IntPtr.Zero)throw new IOException("The voice model could not be loaded. Check the files or choose another voice.");
        }
        catch{Dispose();throw;}
    }
    private static string FindWeights(string folder)
    {
        string? result=null;foreach(string file in Directory.EnumerateFiles(folder,"*.onnx")){if(result!=null)throw new IOException("Choose a voice folder with one model file.");result=Path.GetFileName(file);}return result??throw new IOException("The voice model file is missing.");
    }
    internal byte[] Speak(string text)
    {
        if(string.IsNullOrWhiteSpace(text) || text.Length>2000)throw new IOException("The voice segment is empty or too long.");
        IntPtr extra=Marshal.StringToCoTaskMemUTF8(System.Text.Json.JsonSerializer.Serialize(new{lang=setup.Language}));IntPtr audio=IntPtr.Zero;
        try
        {
            var config=new Generation{Silence=.2f,Speed=1,Speaker=setup.Speaker,Steps=setup.Steps,Extra=extra};
            audio=generate!(engine,text,ref config,IntPtr.Zero,IntPtr.Zero);if(audio==IntPtr.Zero)throw new IOException("The voice returned no audio.");
            var data=Marshal.PtrToStructure<Audio>(audio);
            if(data.Samples==IntPtr.Zero || data.Rate is <8000 or >48000 || data.Count<=0 || data.Count>(long)data.Rate*120)throw new IOException("The generated voice exceeds its audio limit.");
            using var buffer=new MemoryStream();using var writer=new BinaryWriter(buffer);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+data.Count*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(data.Rate);writer.Write(data.Rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(data.Count*2);
            var samples=new float[2048];for(int offset=0;offset<data.Count;offset+=samples.Length){int count=Math.Min(samples.Length,data.Count-offset);Marshal.Copy(IntPtr.Add(data.Samples,offset*4),samples,0,count);for(int i=0;i<count;i++)writer.Write((short)Math.Clamp(float.IsFinite(samples[i])?samples[i]*32767:0,-32768,32767));}
            return buffer.ToArray();
        }
        finally{if(audio!=IntPtr.Zero)destroyAudio!(audio);Marshal.FreeCoTaskMem(extra);}
    }
    public void Dispose()
    {
        if(engine!=IntPtr.Zero){destroyEngine?.Invoke(engine);engine=IntPtr.Zero;}
        foreach(var p in strings)Marshal.FreeCoTaskMem(p);strings.Clear();
        if(library!=IntPtr.Zero){NativeLibrary.Free(library);library=IntPtr.Zero;}if(searchPath!=IntPtr.Zero){RemoveDllDirectory(searchPath);searchPath=IntPtr.Zero;}
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr LoadLibraryEx(string path,IntPtr reserved,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr AddDllDirectory(string path);
    [DllImport("kernel32.dll")]private static extern bool RemoveDllDirectory(IntPtr cookie);
}
