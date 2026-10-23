using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

internal sealed partial class AssistantManagedSpeech
{
    internal async Task<string> Recognize(byte[] wav,CancellationToken token)
    {
        var model=AssistantOwnSpeech.Asr(options);string directory=Path.Combine(Path.GetTempPath(),"NabyScape-speech-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string file=Path.Combine(directory,"utterance.wav");await File.WriteAllBytesAsync(file,wav,token);var info=NativeInfo(assets.SherpaExe(false));string folder=assets.QwenRoot(model);
            if(model.Engine=="moonshine")
            {
                info.ArgumentList.Add("--moonshine-encoder="+AssistantOwnSpeech.MoonshineFile(folder,"encoder_model"));info.ArgumentList.Add("--moonshine-merged-decoder="+AssistantOwnSpeech.MoonshineFile(folder,"decoder_model_merged"));info.ArgumentList.Add("--tokens="+Path.Combine(folder,"tokens.txt"));
            }
            else
            {
                info.ArgumentList.Add("--qwen3-asr-conv-frontend="+Path.Combine(folder,"conv_frontend.onnx"));info.ArgumentList.Add("--qwen3-asr-encoder="+Path.Combine(folder,"encoder.int8.onnx"));info.ArgumentList.Add("--qwen3-asr-decoder="+Path.Combine(folder,"decoder.int8.onnx"));info.ArgumentList.Add("--qwen3-asr-tokenizer="+Path.Combine(folder,"tokenizer"));
            }
            info.ArgumentList.Add("--num-threads="+Math.Clamp(Environment.ProcessorCount/2,1,8));info.ArgumentList.Add("--");info.ArgumentList.Add(file);
            string output=await RunSherpa(info,model.Name,token);int first=output.IndexOf('{'),last=output.LastIndexOf('}');if(first<0 || last<first)throw new IOException(model.Name+" returned no transcript. Try speaking again or choose another pack.");
            using var json=JsonDocument.Parse(output[first..(last+1)]);return json.RootElement.TryGetProperty("text",out var text)?text.GetString()?.Trim()??"":"";
        }
        finally{try{Directory.Delete(directory,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private async Task<string> RunSherpa(ProcessStartInfo info,string engine,CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var child=Launch(info);using var registration=deadline.Token.Register(()=>{try{child.Kill(true);}catch{}});
        async Task<string> Read(StreamReader reader)
        {
            var text=new StringBuilder();char[] buffer=new char[4096];int count;
            while((count=await reader.ReadAsync(buffer,deadline.Token))>0)
            {
                if(text.Length+count>128*1024){try{child.Kill(true);}catch{}throw new IOException(engine+" output exceeded its limit.");}text.Append(buffer,0,count);
            }
            return text.ToString();
        }
        var output=Read(child.StandardOutput);var errors=Read(child.StandardError);
        try
        {
            await child.WaitForExitAsync(deadline.Token);await Task.WhenAll(output,errors);if(child.ExitCode!=0)throw new IOException(engine+" could not complete inference (exit "+child.ExitCode+"). Reinstall the pack or choose a smaller model.");return await output;
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested && !lifetime.IsCancellationRequested){throw new IOException(engine+" took too long. Try a smaller model or a shorter utterance.");}
        finally{try{if(!child.HasExited)child.Kill(true);}catch{}try{await Task.WhenAll(output,errors);}catch{}}
    }
}
