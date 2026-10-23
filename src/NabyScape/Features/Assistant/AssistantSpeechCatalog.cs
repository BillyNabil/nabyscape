using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal sealed record SpeechAsset(string Url,long Bytes,string Sha256);
internal sealed record SpeechModelFile(string Path,SpeechAsset Asset);
internal sealed record SpeechAsrModel(string Id,string Name,double RamGb,double FreeGb,double GpuGb,SpeechAsset Asset,string Engine="whisper",string Folder="",string Language="multi",SpeechModelFile[]? Files=null,string Info="",string SourcePath="")
{
    public override string ToString()=>Name;
    internal bool Native=>Engine!="whisper";
    internal long DownloadBytes=>Files?.Sum(f=>f.Asset.Bytes)??Asset.Bytes;
    internal bool Supports(string language)=>Language=="multi" || Language==language;
}
internal sealed record SpeechTtsModel(string Id,string Name,string Path,long Bytes,string Hash,string ConfigHash,string CardHash,SpeechAsset? Package=null,string Locale="",int Speaker=0,double FreeGb=.4,string Engine="piper",string Folder="",string Info="",string SourcePath="")
{
    public override string ToString()=>Name;
    internal bool Supports(string language)=>Language is "any" or "multi" || Language==language;
    internal string Language=>Locale.Length>0?Locale:Id.StartsWith("id_")?"id":Id.StartsWith("es_")?"es":Id.StartsWith("en_")?"en":"any";
}
internal sealed record SpeechHardware(string Cpu,int Threads,double RamGb,double FreeGb,string Gpu,double VramGb,double FreeVramGb,bool Cuda,bool Supported)
{
    internal bool Fits(SpeechAsrModel model)=>Supported && RamGb+.5>=model.RamGb && FreeGb>=model.FreeGb;
    internal bool UseGpu(SpeechAsrModel model)=>model.Engine=="whisper" && Cuda && FreeVramGb>=model.GpuGb;
    internal bool FitsPack(SpeechAsrModel asr,SpeechTtsModel tts)=>Fits(asr) && FreeGb>=PackFreeGb(asr,tts);
    internal static double PackFreeGb(SpeechAsrModel asr,SpeechTtsModel tts)=>asr.Native?Math.Max(asr.FreeGb,tts.FreeGb):asr.FreeGb+tts.FreeGb;
    internal SpeechAsrModel Recommended=>SpeechCatalog.RecommendPack(this,"en","Balanced").Asr;
    internal SpeechAsrModel Recommend(string preference)=>SpeechCatalog.RecommendPack(this,"en",preference).Asr;
    internal string Tier=>Cuda && VramGb>=1.5?"NVIDIA "+(VramGb>=7.5?"8+":VramGb>=5.5?"6":VramGb>=3.5?"4":"2")+" GB VRAM":"CPU · "+(RamGb>=31.5?"32+":RamGb>=15.5?"16":RamGb>=7.5?"8":"under 8")+" GB RAM";
    internal string Summary=>$"{Cpu} · {Threads} threads\nRAM {RamGb:0.#} GB · {FreeGb:0.#} GB available\n{Gpu}"+(VramGb>0?$" · VRAM {VramGb:0.#} GB ({FreeVramGb:0.#} GB free)":" · dedicated VRAM not confirmed");
    internal static async Task<SpeechHardware> Scan(CancellationToken token)
    {
        return await Task.Run(async()=>
        {
            var memory=SystemMonitor.Capture(false,false,false,false,false);string cpu="CPU",gpu="GPU unavailable";
            try
            {
                using var query=new ManagementObjectSearcher(new ManagementScope("root\\cimv2"),new ObjectQuery("SELECT Name FROM Win32_Processor"),new System.Management.EnumerationOptions{Timeout=TimeSpan.FromSeconds(2)});
                using var rows=query.Get();foreach(ManagementObject row in rows){using(row)cpu=row["Name"]?.ToString()?.Trim()??cpu;}
                using var graphics=new ManagementObjectSearcher(new ManagementScope("root\\cimv2"),new ObjectQuery("SELECT Name FROM Win32_VideoController"),new System.Management.EnumerationOptions{Timeout=TimeSpan.FromSeconds(2)});
                using var adapters=graphics.Get();var names=new System.Collections.Generic.List<string>();foreach(ManagementObject row in adapters){using(row){if(row["Name"] is string name)names.Add(name);}}if(names.Count>0)gpu=string.Join(" / ",names);
            }
            catch(ManagementException){}catch(System.Runtime.InteropServices.COMException){}
            double vram=0,free=0;bool cuda=false;
            string nvidia=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvidia-smi.exe");
            if(!File.Exists(nvidia))nvidia=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","NVSMI","nvidia-smi.exe");
            if(File.Exists(nvidia))
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    var info=new ProcessStartInfo(nvidia){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,ArgumentList={"--query-gpu=memory.total,memory.free,driver_version","--format=csv,noheader,nounits"}};
                    using var p=Process.Start(info);if(p!=null)
                    {
                        using var registration=timeout.Token.Register(()=>{try{p.Kill(true);}catch{}});var output=p.StandardOutput.ReadToEndAsync(timeout.Token);var errors=p.StandardError.ReadToEndAsync(timeout.Token);await p.WaitForExitAsync(timeout.Token);string text=await output;await errors;
                        foreach(string line in text.Split('\n'))
                        {
                            var parts=line.Split(',');if(parts.Length!=3 || !double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var total) || !double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var available))continue;
                            bool driver=Version.TryParse(parts[2].Trim(),out var version) && version>=new Version(551,61);
                            if(driver && available/1024>free){vram=total/1024;free=available/1024;cuda=true;}
                            else if(!cuda && total/1024>vram){vram=total/1024;free=available/1024;}
                            break; // Native Whisper uses GPU 0; do not recommend from another adapter's memory.
                        }
                    }
                }
                catch(OperationCanceledException){token.ThrowIfCancellationRequested();}catch(System.ComponentModel.Win32Exception){}catch(IOException){}
            }
            token.ThrowIfCancellationRequested();const double gb=1024d*1024*1024;
            return new SpeechHardware(cpu,Environment.ProcessorCount,(memory.Total??0)/gb,(memory.Available??0)/gb,gpu,vram,free,cuda,AssistantSpeechPlatform.NativePacksSupported);
        },token);
    }
}

internal static partial class SpeechCatalog
{
    internal const string WhisperRevision="5359861c739e955e79d9a303bcbc70fb988958b1",PiperRevision="5041a7a49ae5b3a0f30e6837769eb03fdbb231bd";
    internal static readonly SpeechAsset Cpu=new("https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.7/whisper-bin-x64.zip",4386743,"d9627486e1c34a03745880485593473e047294260ce9a3cb0aa8deaf15b99af6");
    internal static readonly SpeechAsset Gpu=new("https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.7/whisper-cublas-12.4.0-bin-x64.zip",460076656,"ce5e66c7e45985f497e3a81c9d33723f19b3118993b47f53b8351df5cbb20402");
    internal static readonly SpeechAsset Piper=new("https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip",22477236,"f3c58906402b24f3a96d92145f58acba6d86c9b5db896d207f78dc80811efcea");
    internal static readonly SpeechAsset VisualCpp=new("https://download.visualstudio.microsoft.com/download/pr/990c720a-46ce-4dc7-87b9-c2eb615d34b9/CC0FF0EB1DC3F5188AE6300FAEF32BF5BEEBA4BDD6E8E445A9184072096B713B/VC_redist.x64.exe",25635768,"cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b");
    internal const string SherpaFolder="sherpa-onnx-v1.13.8-win-x64-shared-MT-Release",KokoroFolder="kokoro-int8-multi-lang-v1_1";
    internal static readonly SpeechAsset Sherpa=new("https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.8/"+SherpaFolder+".tar.bz2",24805859,"6dffdc715a4465b989446a6105265d2cb345e7101591a17d35534b6758f6e8df");
    internal static readonly SpeechAsset Kokoro=new("https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-int8-multi-lang-v1_1.tar.bz2",147031220,"a1e94694776049035c4f2c6529f003aaece993c76aae9a78995831c3c4dcafc6");
    private static SpeechAsset Whisper(string id,long bytes,string hash)=>new($"https://huggingface.co/ggerganov/whisper.cpp/resolve/{WhisperRevision}/ggml-{id}.bin",bytes,hash);
    private static readonly SpeechAsrModel[] LegacyAsr={
        new("tiny","Whisper tiny · light",4,.8,1,Whisper("tiny",77691713,"be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21")),
        new("base","Whisper base · everyday",6,1.3,1.5,Whisper("base",147951465,"60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe")),
        new("small","Whisper small · clearer recognition",8,2.5,3,Whisper("small",487601967,"1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b")),
        new("large-v3-turbo","Whisper large-v3-turbo · GPU quality",16,4,6,Whisper("large-v3-turbo",1624555275,"1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69")),
        new("qwen3-asr-0.6b-int8","Qwen3-ASR 0.6B INT8 · 2026 CPU",8,3.5,double.PositiveInfinity,new("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2",878702423,"393f8a14e2f5fb96746aaab342997a40641001fbd5bf9592a080a8329178ee96"),"qwen","sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25")};
    private static readonly SpeechTtsModel[] LegacyTts={
        new("windows","Windows voice · already on this PC","",0,"","","",FreeGb:.1),
        new("kokoro-v1.1-int8-en","Kokoro 82M v1.1 INT8 · English","",147031220,"","","",Kokoro,"en",0,1.2,"kokoro","", "English voice · native CPU · loads only while speaking"),
        new("id_ID-news_tts-medium","Piper · Indonesian","id/id_ID/news_tts/medium",62950044,"ed8f02aa593f7af6b19acbdb8142e0da0dd72f46194eb33d38e0eb10a52597e8","1ef677072668a5e172e0759b1d3871f129009d1167f093325a17607f7add5ad7","cc134d8c8fce95d2486a0e5ae60b40c17451a876cf7d36df0f4110589fc40538"),
        new("en_US-ljspeech-high","Piper · English (US)","en/en_US/ljspeech/high",114199011,"5d4f08ba6a2a48c44592eed3ce56bf85e9de3dd4e20df90541ae68a8310c029a","7e1f4634af596d83cca997fb7a931ba80b70f8a316a2655ee69c55365e0ace14","289f7421072d689a3d91f6c632f486af46f9b8b04417536104a4ea667b8a394b"),
        new("es_ES-sharvard-medium","Piper · Spanish","es/es_ES/sharvard/medium",76733615,"40febfb1679c69a4505ff311dc136e121e3419a13a290ef264fdf43ddedd0fb1","7438c9b699c72b0c3388dae1b68d3f364dc66a2150fe554a1c11f03372957b2c","37655314d08fe8a0900abc628d3bcbee48ffbdd8fad9306b89525a0ada20fefc")};
    internal static readonly SpeechAsrModel[] Asr=LegacyAsr.Concat(AdditionalAsr()).ToArray();
    internal static readonly SpeechTtsModel[] Tts=LegacyTts.Concat(AdditionalTts()).ToArray();
    internal static SpeechAsrModel AsrById(string id)=>Asr.FirstOrDefault(m=>m.Id==id)??throw new InvalidOperationException("Choose a supported listening model.");
    internal static SpeechTtsModel TtsById(string id)=>Tts.FirstOrDefault(m=>m.Id==id)??throw new InvalidOperationException("Choose a supported speaking voice.");
    internal static string VoiceUrl(SpeechTtsModel model,string suffix)=>$"https://huggingface.co/rhasspy/piper-voices/resolve/{PiperRevision}/{model.Path}/"+(suffix=="MODEL_CARD"?suffix:model.Id+suffix);
}
