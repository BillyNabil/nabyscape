using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NabyScape;

internal static class AssistantSpeechRemovalTests
{
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool value){if(!value)throw new Exception("Speech uninstall invariant failed.");}
        void Put(string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"fixture");}
        void Fixture(string name,Action<AssistantSpeechInstall,string> action)=>test("Speech uninstall · "+name,()=>
        {
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"NabyScape-removal-"+Guid.NewGuid().ToString("N")));
            try{using var speech=new AssistantSpeechInstall(new Store(root));action(speech,Path.Combine(root,"local-speech"));}
            finally{if(!root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe fixture cleanup.");if(Directory.Exists(root))Directory.Delete(root,true);}
        });
        Fixture("every ASR model removes weights and cache while preserving sibling models and engines",(speech,root)=>
        {
            string keep=Path.Combine(root,"engines","keep.txt"),sibling=Path.Combine(root,"models","keep.txt");Put(keep);Put(sibling);
            foreach(var model in SpeechCatalog.Asr)
            {
                string target=model.Native?Path.Combine(root,"models",model.Id):speech.AsrPath(model);
                if(model.Native)Put(Path.Combine(target,"nested","weights.onnx"));else Put(target);
                string cache=Path.Combine(root,"downloads",model.Id);
                if(model.Native){if(model.Files!=null)Put(Path.Combine(cache,"nested","weights.onnx"));else Put(cache+(model.Asset.Url.EndsWith(".tar.bz2")?".tar.bz2":".zip"));}
                Assert(speech.HasManagedModel(model.Id,false));Assert(speech.UninstallModel(model.Id,false).GetAwaiter().GetResult());
                Assert(!speech.HasManagedModel(model.Id,false) && File.Exists(keep) && File.Exists(sibling));
            }
        });
        Fixture("every TTS model removes package or Piper support files and download caches",(speech,root)=>
        {
            string keep=Path.Combine(root,"engines","keep.txt");Put(keep);
            foreach(var model in SpeechCatalog.Tts.Where(m=>m.Id!="windows"))
            {
                if(model.Package!=null)
                {string folder=speech.VoiceRoot(model);Put(Path.Combine(folder,"nested","weights.onnx"));Put(Path.Combine(root,"downloads",Path.GetFileName(folder)+(model.Package.Url.EndsWith(".tar.bz2")?".tar.bz2":".zip")));}
                else{foreach(string suffix in new[]{"",".json",".MODEL_CARD",".partial"})Put(speech.TtsPath(model)+suffix);}
                Assert(speech.HasManagedModel(model.Id,true));Assert(speech.UninstallModel(model.Id,true).GetAwaiter().GetResult());
                Assert(!speech.HasManagedModel(model.Id,true) && File.Exists(keep));
            }
        });
        Fixture("Windows voices and unknown or traversal IDs are rejected without deleting files",(speech,root)=>
        {
            string keep=Path.Combine(root,"models","keep.txt");Put(keep);
            foreach(var choice in new[]{("windows",true),("../models/keep",false),("unknown",true)})
            {bool rejected=false;try{speech.UninstallModel(choice.Item1,choice.Item2).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}Assert(rejected && File.Exists(keep) && !speech.Busy);}
        });
        Fixture("selected managed and imported references are detected without owning external files",(speech,root)=>
        {
            var model=SpeechCatalog.Asr[0];var options=new AssistantLocalVoiceOptions{Enabled=true,Managed=true,ManagedAsr=model.Id,ManagedTts="windows"};
            Assert(speech.ModelInUse(options,model.Id,false));options.OwnModels=true;options.OwnAsrEngine="Whisper GGML file";options.OwnAsrPath=speech.AsrPath(model);
            Assert(speech.ModelInUse(options,model.Id,false));options.OwnAsrPath=Path.Combine(Path.GetDirectoryName(root)!,"user-owned.bin");Put(options.OwnAsrPath);
            Assert(!speech.ModelInUse(options,model.Id,false));Put(speech.AsrPath(model));Assert(speech.UninstallModel(model.Id,false).GetAwaiter().GetResult() && File.Exists(options.OwnAsrPath));
        });
        Fixture("voice shutdown is awaited and concurrent install or uninstall cannot start",(speech,root)=>
        {
            var model=SpeechCatalog.Asr[0];Put(speech.AsrPath(model));var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var resume=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var removal=speech.UninstallModel(model.Id,false,async()=>{entered.SetResult();await resume.Task;});
            try
            {
                entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();Assert(speech.Busy && !speech.CanCancel && File.Exists(speech.AsrPath(model)));
                Assert(!speech.UninstallModel(model.Id,false).GetAwaiter().GetResult());Assert(!speech.Install(model,SpeechCatalog.TtsById("windows"),false).GetAwaiter().GetResult());
            }
            finally{resume.TrySetResult();}
            Assert(removal.GetAwaiter().GetResult() && !speech.Busy && !File.Exists(speech.AsrPath(model)));
        });
        Fixture("a failed release leaves model files intact and reports failure",(speech,root)=>
        {
            var model=SpeechCatalog.Asr[0];Put(speech.AsrPath(model));
            Assert(!speech.UninstallModel(model.Id,false,()=>throw new IOException("fixture preference failure")).GetAwaiter().GetResult());
            Assert(File.Exists(speech.AsrPath(model)) && !speech.Busy && speech.Status.StartsWith("Could not finish"));
        });
        Fixture("directory junctions cannot redirect model removal outside managed storage",(speech,root)=>
        {
            var model=SpeechCatalog.Asr.First(m=>m.Native);string target=Path.Combine(root,"models",model.Id),outside=Path.Combine(Path.GetDirectoryName(root)!,"external");Put(Path.Combine(outside,"keep.txt"));Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var info=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string arg in new[]{"/c","mklink","/J",target,outside})info.ArgumentList.Add(arg);
            using(var junction=Process.Start(info)!){junction.WaitForExit();Assert(junction.ExitCode==0);}
            try{Assert(!speech.UninstallModel(model.Id,false).GetAwaiter().GetResult() && File.Exists(Path.Combine(outside,"keep.txt")) && speech.Status.Contains("link"));}
            finally{Directory.Delete(target);}
        });
    }
}
