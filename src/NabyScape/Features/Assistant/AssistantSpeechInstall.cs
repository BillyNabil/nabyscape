using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NabyScape;

// Optional assets live outside the application/installer and are never loaded at startup.
internal sealed partial class AssistantSpeechInstall : IDisposable
{
    private readonly HttpClient http=new(new HttpClientHandler{MaxAutomaticRedirections=8}){Timeout=Timeout.InfiniteTimeSpan};
    private readonly string root;
    private CancellationTokenSource? installation;
    private long lastProgress;
    private bool disposed;
    private bool installingPrerequisite;
    internal bool Testing {get;set;}
    internal bool Busy=>installation!=null || Testing;
    internal bool CanCancel=>installation!=null && !installingPrerequisite && !removing;
    internal string Status {get;private set;}="Scan your PC to find a suitable listening model.";
    internal double Progress {get;private set;}
    internal SpeechHardware? Hardware {get;private set;}
    internal event Action? Changed;
    internal AssistantSpeechInstall(Store store){root=Path.Combine(store.DirectoryPath,"local-speech");http.DefaultRequestHeaders.UserAgent.ParseAdd("NabyScape/2.20.0");}
    internal string EngineFolder(bool gpu)=>Path.Combine(root,"engines",gpu?"whisper-1.8.7-cuda12":"whisper-1.8.7-cpu");
    internal string WhisperExe(bool gpu)=>Path.Combine(EngineFolder(gpu),"Release","whisper-server.exe");
    internal string PiperFolder=>Path.Combine(root,"engines","piper-2023.11.14-2");
    internal string PiperExe=>Path.Combine(PiperFolder,"piper","piper.exe");
    internal string AsrPath(SpeechAsrModel model)=>model.SourcePath.Length>0?model.SourcePath:Path.Combine(root,"models","ggml-"+model.Id+".bin");
    internal string TtsPath(SpeechTtsModel model)=>model.SourcePath.Length>0?model.SourcePath:Path.Combine(root,"voices",model.Id+".onnx");
    internal string SherpaRoot=>Path.Combine(root,"engines","sherpa-1.13.8");
    internal string SherpaExe(bool tts)=>Path.Combine(SherpaRoot,SpeechCatalog.SherpaFolder,"bin",tts?"sherpa-onnx-offline-tts.exe":"sherpa-onnx-offline.exe");
    internal string QwenRoot(SpeechAsrModel model)=>model.SourcePath.Length>0?model.SourcePath:Path.Combine(root,"models",model.Id,model.Folder);
    internal string KokoroRoot=>Path.Combine(root,"voices","kokoro-v1.1-int8");
    internal string KokoroDirectory=>Directory.Exists(KokoroRoot)?Path.GetDirectoryName(Directory.EnumerateFiles(KokoroRoot,"voices.bin",SearchOption.AllDirectories).FirstOrDefault())??"":"";
    internal string VoiceRoot(SpeechTtsModel model)=>model.Engine=="kokoro"?KokoroRoot:Path.Combine(root,"voices",model.Id);
    internal string VoiceDirectory(SpeechTtsModel model)=>model.SourcePath.Length>0?model.SourcePath:model.Engine=="kokoro"?KokoroDirectory:Path.Combine(VoiceRoot(model),model.Folder);
    internal string AsrMarker(SpeechAsrModel model)=>Path.Combine(QwenRoot(model),model.Engine=="moonshine"?(File.Exists(Path.Combine(QwenRoot(model),"encoder_model.onnx"))?"encoder_model.onnx":"encoder_model.ort"):"encoder.int8.onnx");
    internal bool HasAsrWeights(SpeechAsrModel model)=>model.Native?(model.SourcePath.Length>0?File.Exists(AsrMarker(model)):Ready(Path.Combine(root,"models",model.Id),AsrMarker(model))) && (model.Files==null || model.Files.All(f=>HasFile(Path.Combine(QwenRoot(model),f.Path),f.Asset.Bytes))):HasFile(AsrPath(model),model.Asset.Bytes);
    internal bool HasTtsWeights(SpeechTtsModel model)=>model.Id=="windows" || (model.Package!=null?(model.SourcePath.Length>0 || Ready(VoiceRoot(model),"")) && Directory.Exists(VoiceDirectory(model)):HasFile(TtsPath(model),model.Bytes) && HasFile(TtsPath(model)+".json",0) && (model.SourcePath.Length>0 || File.Exists(TtsPath(model)+".MODEL_CARD")));
    private bool NativeAsrInstalled(SpeechAsrModel model)=>Ready(SherpaRoot,SherpaExe(false)) && HasAsrWeights(model);
    private bool NativeVoiceInstalled(SpeechTtsModel model)=>Ready(SherpaRoot,SherpaExe(true)) && HasTtsWeights(model);
    private static bool Ready(string folder,string exe)
    {
        try{return (exe.Length==0 || File.Exists(exe)) && File.Exists(Path.Combine(folder,"installed.txt")) && File.ReadLines(Path.Combine(folder,"installed.txt")).All(path=>File.Exists(Path.Combine(folder,path)));}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
    internal bool Installed(SpeechAsrModel asr,SpeechTtsModel tts,bool gpu=false)
        =>AssistantSpeechPlatform.NativePacksSupported && CppRuntimePresent() && (asr.Native?NativeAsrInstalled(asr):Ready(EngineFolder(false),WhisperExe(false)) && (!gpu || Ready(EngineFolder(true),WhisperExe(true))) && HasFile(AsrPath(asr),asr.Asset.Bytes)) && (tts.Id=="windows" || (tts.Package!=null?NativeVoiceInstalled(tts):Ready(PiperFolder,PiperExe) && HasFile(TtsPath(tts),tts.Bytes) && HasFile(TtsPath(tts)+".json",0) && (tts.SourcePath.Length>0 || File.Exists(TtsPath(tts)+".MODEL_CARD"))));
    private static bool HasFile(string path,long size){try{var file=new FileInfo(path);return file.Exists && (size==0?file.Length>0:file.Length==size);}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}}
    internal async Task<SpeechHardware> Scan(CancellationToken token=default)
    {
        Hardware=await SpeechHardware.Scan(token);return Hardware;
    }
    private void Report(string message,double progress=0)
    {Status=message;Progress=Math.Clamp(progress,0,100);if(!disposed)Changed?.Invoke();}
    internal void Cancel()=>installation?.Cancel();
    internal async Task<bool> Install(SpeechAsrModel asr,SpeechTtsModel tts,bool preferGpu)
    {
        if(Busy || disposed)return false;
        installation=new();var token=installation.Token;
        try
        {
            AssistantSpeechPlatform.RequireNativePacks();
            Report("Checking available memory and disk space…");var hardware=await Scan(token);
            if(!hardware.Supported)throw new IOException(AssistantSpeechPlatform.NativePackUnavailable);
            if(!hardware.FitsPack(asr,tts))throw new IOException($"This pack needs a budget of {asr.RamGb:0.#} GB total RAM and {SpeechHardware.PackFreeGb(asr,tts):0.#} GB available. Close other apps or choose a smaller model/voice.");
            bool gpu=preferGpu && hardware.UseGpu(asr);
            long need=asr.DownloadBytes*(asr.Files!=null?2:asr.Native?4:1)+SpeechCatalog.Cpu.Bytes*3+(gpu?SpeechCatalog.Gpu.Bytes*4:0)+(tts.Package!=null?tts.Bytes*4+SpeechCatalog.Sherpa.Bytes*5:tts.Id!="windows"?tts.Bytes+SpeechCatalog.Piper.Bytes*4:0)+(asr.Native?SpeechCatalog.Sherpa.Bytes*5:0)+SpeechCatalog.VisualCpp.Bytes+512L*1024*1024;
            Directory.CreateDirectory(root);var drive=new DriveInfo(Path.GetPathRoot(root)!);if(drive.AvailableFreeSpace<need)throw new IOException($"Free at least {need/1024d/1024/1024:0.0} GB on {drive.Name} for this pack and installation space.");
            await EnsureCppRuntime(token);
            if(asr.Native)
            {
                await Runtime(SpeechCatalog.Sherpa,SherpaRoot,SherpaExe(false),"Sherpa native speech engine",token);
                if(asr.Files!=null)await ModelFiles(asr,token);
                else await Runtime(asr.Asset,Path.Combine(root,"models",asr.Id),AsrMarker(asr),asr.Name,token);
            }
            else
            {
                await Runtime(SpeechCatalog.Cpu,EngineFolder(false),WhisperExe(false),"Whisper CPU engine",token);
                if(gpu)await Runtime(SpeechCatalog.Gpu,EngineFolder(true),WhisperExe(true),"Whisper GPU engine",token);
                await Download(asr.Asset,AsrPath(asr),asr.Name,token);
            }
            if(tts.Package!=null)
            {
                await Runtime(SpeechCatalog.Sherpa,SherpaRoot,SherpaExe(true),"Sherpa native speech engine",token);
                await Runtime(tts.Package,VoiceRoot(tts),"",tts.Name,token);
            }
            else if(tts.Id!="windows")
            {
                await Runtime(SpeechCatalog.Piper,PiperFolder,PiperExe,"Piper speech engine",token);
                await Download(new(SpeechCatalog.VoiceUrl(tts,".onnx"),tts.Bytes,tts.Hash),TtsPath(tts),tts.Name,token);
                await Download(new(SpeechCatalog.VoiceUrl(tts,".onnx.json"),0,tts.ConfigHash),TtsPath(tts)+".json","Voice configuration",token);
                await Download(new(SpeechCatalog.VoiceUrl(tts,"MODEL_CARD"),0,tts.CardHash),TtsPath(tts)+".MODEL_CARD","Voice attribution and model card",token);
            }
            await WriteSources(token);
            Report("Installed and verified. Choose Use installed pack to enable it.",100);return true;
        }
        catch(OperationCanceledException){Report("Installation cancelled. Completed verified downloads are kept; choose Install & use to retry.");return false;}
        catch(Exception ex){Report("Installation failed: "+(ex is HttpRequestException?"Check your internet connection and retry. "+ex.Message:ex.Message));return false;}
        finally{installation.Dispose();installation=null;if(!disposed)Changed?.Invoke();}
    }
    private Task WriteSources(CancellationToken token)=>File.WriteAllTextAsync(Path.Combine(root,"SOURCES.txt"),"Optional speech components downloaded by NabyScape:\nWhisper.cpp v1.8.7 (MIT): https://github.com/ggml-org/whisper.cpp/tree/v1.8.7\nWhisper weights (MIT): https://huggingface.co/ggerganov/whisper.cpp\nPiper native engine 2023.11.14-2 (MIT): https://github.com/rhasspy/piper/tree/2023.11.14-2\nPiper uses eSpeak NG (GPL): https://github.com/espeak-ng/espeak-ng and ONNX Runtime (MIT): https://github.com/microsoft/onnxruntime\nPiper voice terms and dataset attribution: see each .MODEL_CARD beside the voice file.\nSherpa-ONNX v1.13.8 (Apache-2.0): https://github.com/k2-fsa/sherpa-onnx/tree/v1.13.8\nQwen3-ASR (Apache-2.0): https://github.com/QwenLM/Qwen3-ASR\nKokoro v1.1 (Apache-2.0): https://huggingface.co/hexgrad/Kokoro-82M-v1.1-zh\nMoonshine v2: https://huggingface.co/UsefulSensors (see bundled LICENSE)\nQwen3-ASR 1.7B community ONNX conversion: https://huggingface.co/thieunv-asilla/sherpa-onnx-qwen3-asr-1.7B-int8\nKittenTTS nano v0.8 (Apache-2.0): https://huggingface.co/KittenML/kitten-tts-nano-0.8-int8\nSupertonic 3 (OpenRAIL-M model): https://huggingface.co/Supertone/supertonic-3/blob/main/LICENSE\nSherpa packages include ONNX Runtime and eSpeak NG components; see their bundled license notices.\nNVIDIA CUDA runtime terms: https://docs.nvidia.com/cuda/eula/index.html\nMicrosoft Visual C++ runtime terms: https://aka.ms/VisualStudioRedistribution\n",token);
    internal async Task<bool> PrepareOwn(AssistantLocalVoiceOptions options)
    {
        if(Busy || disposed)return false;installation=new();var token=installation.Token;
        try
        {
            AssistantSpeechPlatform.RequireNativePacks();
            var asr=AssistantOwnSpeech.Asr(options);var tts=AssistantOwnSpeech.Tts(options);
            string managedEngines=Path.GetFullPath(Path.Combine(root,"engines"))+Path.DirectorySeparatorChar;
            foreach(string source in new[]{asr.SourcePath,tts.SourcePath})
                if(source.Length>0 && (source.StartsWith(managedEngines,StringComparison.OrdinalIgnoreCase) || string.Equals(source,managedEngines.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)))throw new IOException("Keep your model files outside Naby's managed engines folder. Move them to a separate models folder and select them again.");
            if(!HasAsrWeights(asr))throw new IOException("The selected listening pack is not installed. Install it in Automatic setup, or choose your own model file/folder.");
            if(!HasTtsWeights(tts))throw new IOException("The selected speaking pack is not installed. Install it first, or choose your own speaking model.");
            Report("Checking hardware for your model setup…");var hardware=await Scan(token);if(!hardware.Supported)throw new IOException(AssistantSpeechPlatform.NativePackUnavailable);
            bool gpu=options.ManagedGpu && hardware.UseGpu(asr);
            if(Installed(asr,tts,gpu)){Report("Your files and required engines are ready. No downloads needed.",100);return true;}
            Directory.CreateDirectory(root);if(new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace<1024L*1024*1024)throw new IOException("Keep at least 1 GB free for engine installation and temporary audio.");
            await EnsureCppRuntime(token);
            if(asr.Native)await Runtime(SpeechCatalog.Sherpa,SherpaRoot,SherpaExe(false),"Native ASR engine",token);
            else
            {
                await Runtime(SpeechCatalog.Cpu,EngineFolder(false),WhisperExe(false),"Whisper CPU engine",token);
                if(gpu)await Runtime(SpeechCatalog.Gpu,EngineFolder(true),WhisperExe(true),"Whisper GPU engine",token);
            }
            if(tts.Package!=null)await Runtime(SpeechCatalog.Sherpa,SherpaRoot,SherpaExe(true),"Native TTS engine",token);
            else if(tts.Id!="windows")await Runtime(SpeechCatalog.Piper,PiperFolder,PiperExe,"Piper engine",token);
            await WriteSources(token);token.ThrowIfCancellationRequested();Report("Engines ready. Your model files remain in their original location.",100);return true;
        }
        catch(OperationCanceledException){Report("Setup cancelled. Your model files were not changed.");return false;}
        catch(Exception ex){Report("Model setup failed: "+ex.Message);return false;}
        finally{installation.Dispose();installation=null;if(!disposed)Changed?.Invoke();}
    }
    internal static bool CppRuntimePresent()
    {
        try
        {
            using var registry=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var runtime=registry.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            return runtime?.GetValue("Installed") is int installed && installed==1 && Version.TryParse(runtime.GetValue("Version")?.ToString()?.TrimStart('v','V'),out var version) && version>=new Version(14,44,35211);
        }
        catch(System.Security.SecurityException){return false;}catch(UnauthorizedAccessException){return false;}catch(IOException){return false;}
    }
    private async Task EnsureCppRuntime(CancellationToken token)
    {
        if(CppRuntimePresent())return;
        string setup=Path.Combine(root,"downloads","VC_redist.x64.exe");await Download(SpeechCatalog.VisualCpp,setup,"Microsoft Visual C++ runtime",token);token.ThrowIfCancellationRequested();
        await Task.Run(()=>UpdateSignature.VerifyIfPresent(setup),token);
        installingPrerequisite=true;Report("Installing Microsoft Visual C++ runtime. Complete the Windows administrator prompt. Cancellation is available again when this component finishes.");
        try
        {
            using var installer=Process.Start(new ProcessStartInfo(setup){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,Arguments="/install /quiet /norestart"})??throw new IOException("Could not start Microsoft runtime setup.");
            // Do not kill Windows Installer halfway through a shared runtime update.
            await installer.WaitForExitAsync();
            if(installer.ExitCode==3010)throw new IOException("Microsoft runtime installed. Restart Windows, then choose Install & use again.");
            if(installer.ExitCode!=0 || !CppRuntimePresent())throw new IOException("Microsoft runtime setup did not finish successfully. Complete its Windows administrator prompt, then retry.");
            token.ThrowIfCancellationRequested();
        }
        catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223){throw new IOException("Windows runtime installation was cancelled. Choose Install & use when you are ready to approve it.");}
        finally{installingPrerequisite=false;}
    }
    private async Task Download(SpeechAsset asset,string target,string title,CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if(HasFile(target,asset.Bytes))
        {
            Report("Verifying "+title+"…");await using var existing=File.OpenRead(target);var hash=await SHA256.HashDataAsync(existing,token);if(Convert.ToHexString(hash).Equals(asset.Sha256,StringComparison.OrdinalIgnoreCase))return;
        }
        string partial=target+".partial";
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromHours(2));
            using var response=await http.GetAsync(asset.Url,HttpCompletionOption.ResponseHeadersRead,deadline.Token);response.EnsureSuccessStatusCode();
            if(response.RequestMessage?.RequestUri?.Scheme!="https")throw new IOException("The download redirected outside HTTPS.");
            if(asset.Bytes>0 && response.Content.Headers.ContentLength is long declared && declared!=asset.Bytes)throw new IOException("Download size differs from the pinned model catalog.");
            long cap=asset.Bytes>0?asset.Bytes:128*1024,total=0;byte[] buffer=new byte[128*1024];
            await using(var source=await response.Content.ReadAsStreamAsync(deadline.Token))
            await using(var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,buffer.Length,true))
            using(var checksum=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                int count;while((count=await source.ReadAsync(buffer,deadline.Token))>0)
                {
                    total+=count;if(total>cap)throw new IOException("Download exceeds its expected size.");await output.WriteAsync(buffer.AsMemory(0,count),deadline.Token);checksum.AppendData(buffer,0,count);
                    long now=Environment.TickCount64;if(now-lastProgress>150){lastProgress=now;Report($"Downloading {title} · {total/1024d/1024:0.0}"+(asset.Bytes>0?$" / {asset.Bytes/1024d/1024:0.0} MB":" MB"),asset.Bytes>0?total*100d/asset.Bytes:0);}
                }
                if(asset.Bytes>0 && total!=asset.Bytes || !Convert.ToHexString(checksum.GetHashAndReset()).Equals(asset.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Checksum verification failed. Retry the download.");
            }
            File.Move(partial,target,true);
        }
        finally{if(File.Exists(partial))File.Delete(partial);}
    }
    private async Task ModelFiles(SpeechAsrModel model,CancellationToken token)
    {
        string folder=Path.Combine(root,"models",model.Id);if(Ready(folder,AsrMarker(model)))return;
        string staging=folder+".staging-"+Guid.NewGuid().ToString("N");
        try
        {
            foreach(var file in model.Files!)
            {
                string cached=Path.Combine(root,"downloads",model.Id,file.Path);
                await Download(file.Asset,cached,model.Name+" · "+file.Path,token);
                string target=Path.Combine(staging,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var source=new FileStream(cached,FileMode.Open,FileAccess.Read,FileShare.Read,128*1024,true);
                await using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,128*1024,true);
                await source.CopyToAsync(output,token);
            }
            token.ThrowIfCancellationRequested();File.WriteAllLines(Path.Combine(staging,"installed.txt"),model.Files!.Select(f=>f.Path));
            if(Directory.Exists(folder))Directory.Delete(folder,true);Directory.Move(staging,folder);
        }
        finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    private async Task Runtime(SpeechAsset asset,string folder,string executable,string title,CancellationToken token)
    {
        if(Ready(folder,executable))return;
        bool tar=asset.Url.EndsWith(".tar.bz2",StringComparison.OrdinalIgnoreCase);
        string archive=Path.Combine(root,"downloads",Path.GetFileName(folder)+(tar?".tar.bz2":".zip"));await Download(asset,archive,title,token);
        Report("Installing "+title+"…");string staging=folder+".staging-"+Guid.NewGuid().ToString("N");
        try
        {
            await Task.Run(()=>
            {
                Directory.CreateDirectory(staging);string prefix=Path.GetFullPath(staging)+Path.DirectorySeparatorChar;
                long expanded=0;
                if(tar)ExtractTar(archive,staging,token).GetAwaiter().GetResult();
                else
                {
                    using var zip=ZipFile.OpenRead(archive);if(zip.Entries.Count>10000)throw new IOException("Runtime archive has too many entries.");
                    foreach(var entry in zip.Entries)
                    {
                        token.ThrowIfCancellationRequested();if(entry.Length>3L*1024*1024*1024-expanded)throw new IOException("Runtime archive is too large.");expanded+=entry.Length;
                        string path=Path.GetFullPath(Path.Combine(staging,entry.FullName));if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) || ((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new IOException("Unsafe runtime archive path.");
                        if(entry.Name.Length==0){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path);
                    }
                }
                if(executable.Length>0 && !File.Exists(Path.Combine(staging,Path.GetRelativePath(folder,executable))))throw new IOException("The package does not contain its expected model/engine file.");
                File.WriteAllLines(Path.Combine(staging,"installed.txt"),Directory.EnumerateFiles(staging,"*",SearchOption.AllDirectories).Select(path=>Path.GetRelativePath(staging,path)));
                if(Directory.Exists(folder))Directory.Delete(folder,true);Directory.Move(staging,folder);
            },token);
        }
        finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    private static async Task ExtractTar(string archive,string staging,CancellationToken token)
    {
        string tar=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tar.exe");if(!File.Exists(tar))throw new IOException("These new model packs require the tar component included with current Windows. Update Windows, or use a Whisper/Piper pack.");
        async Task<string> Run(params string[] arguments)
        {
            var info=new ProcessStartInfo(tar){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string arg in arguments)info.ArgumentList.Add(arg);
            using var p=Process.Start(info)??throw new IOException("Could not unpack the speech model.");using var registration=token.Register(()=>{try{p.Kill(true);}catch{}});
            async Task<string> Read(StreamReader reader)
            {
                var text=new System.Text.StringBuilder();char[] chunk=new char[4096];int count;
                while((count=await reader.ReadAsync(chunk,token))>0){if(text.Length+count>2*1024*1024){try{p.Kill(true);}catch{}throw new IOException("The package listing is too large.");}text.Append(chunk,0,count);}return text.ToString();
            }
            var output=Read(p.StandardOutput);var errors=Read(p.StandardError);try{await p.WaitForExitAsync(token);await Task.WhenAll(output,errors);if(p.ExitCode!=0)throw new IOException("Windows could not unpack the verified model. Check available disk space and retry.");return await output;}
            finally{try{if(!p.HasExited)p.Kill(true);}catch{}try{await Task.WhenAll(output,errors);}catch{}}
        }
        string prefix=Path.GetFullPath(staging)+Path.DirectorySeparatorChar;string listing=await Run("-tf",archive);var names=listing.Split('\n',StringSplitOptions.RemoveEmptyEntries);if(names.Length>20000)throw new IOException("Too many files in the model package.");
        foreach(string value in names){string name=value.TrimEnd('\r');if(name.Contains(':') || !Path.GetFullPath(Path.Combine(staging,name)).StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe speech-package path.");}
        string details=await Run("-tvf",archive);if(details.Split('\n',StringSplitOptions.RemoveEmptyEntries).Any(line=>line[0]!='-' && line[0]!='d'))throw new IOException("Model packages containing links or devices are not accepted.");
        await Run("-xf",archive,"-C",staging);
        long size=0;foreach(string file in Directory.EnumerateFiles(staging,"*",SearchOption.AllDirectories)){token.ThrowIfCancellationRequested();if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Unsafe link in speech package.");size+=new FileInfo(file).Length;if(size>3L*1024*1024*1024)throw new IOException("The unpacked model is too large.");}
    }
    internal void OpenFolder(){Directory.CreateDirectory(root);Process.Start(new ProcessStartInfo(root){UseShellExecute=true});}
    public void Dispose(){disposed=true;installation?.Cancel();http.Dispose();}
}
