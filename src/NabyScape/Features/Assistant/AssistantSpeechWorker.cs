using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NabyScape;

// One private, current-user pipe and native worker per active voice session.
internal sealed class AssistantSpeechWorker : IDisposable
{
    internal sealed record Setup(string Library,string Folder,string Engine,string Language,int Speaker,int Steps);
    private readonly string pipeName="NabyScape-tts-"+Guid.NewGuid().ToString("N");
    private readonly NamedPipeServerStream pipe;
    private readonly SemaphoreSlim gate=new(1,1);
    private Process? process;
    private int disposed;
    internal AssistantSpeechWorker()=>pipe=new(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
    internal async Task Start(Setup setup,Func<ProcessStartInfo,Process> launch,CancellationToken token)
    {
        AssistantSpeechPlatform.RequireNativePacks();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed)!=0,this);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            string executable=Environment.ProcessPath??throw new IOException("The app executable is unavailable.");
            var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};
            if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"NabyScape.dll"));
            info.ArgumentList.Add("--speech-worker");info.ArgumentList.Add(pipeName);timeout.Token.ThrowIfCancellationRequested();process=launch(info);
            await pipe.WaitForConnectionAsync(timeout.Token);await WriteFrame(pipe,JsonSerializer.SerializeToUtf8Bytes(setup),timeout.Token);await ReadReply(timeout.Token);
        }
        catch(OperationCanceledException){Dispose();throw;}
        catch(Exception ex){Dispose();throw new IOException("Could not load the persistent voice engine. Prepare the selected pack again, or choose Windows voice.",ex);}
    }
    internal async Task<byte[]> Speak(string text,CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed)!=0,this);await gate.WaitAsync(token);
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(2));
            await WriteFrame(pipe,Encoding.UTF8.GetBytes(text),timeout.Token);return await ReadReply(timeout.Token);
        }
        catch{Dispose();throw;}
        finally{gate.Release();}
    }
    private async Task<byte[]> ReadReply(CancellationToken token)
    {
        var response=await ReadFrame(pipe,12*1024*1024+1,token);
        if(response.Length==0)throw new IOException("The voice worker returned an empty reply.");
        if(response[0]!=0)throw new IOException("Local voice generation failed. Check your voice pack or choose Windows voice.");
        return response[1..];
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        pipe.Dispose();var child=Interlocked.Exchange(ref process,null);
        if(child!=null){try{if(!child.HasExited)child.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}finally{child.Dispose();}}
    }
    internal static async Task Run(string name)
    {
        if(!AssistantSpeechPlatform.NativePacksSupported)return;
        if(!name.StartsWith("NabyScape-tts-",StringComparison.Ordinal) || name.Length!=46)return;
        using var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        try
        {
            using(var connect=new CancellationTokenSource(TimeSpan.FromSeconds(15)))await client.ConnectAsync(connect.Token);
            using var initTimeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var bytes=await ReadFrame(client,32768,initTimeout.Token);
            var setup=JsonSerializer.Deserialize<Setup>(bytes)??throw new IOException("Missing speech configuration.");
            using var engine=new AssistantSpeechNative(setup);
            await WriteFrame(client,new byte[]{0},initTimeout.Token);
            while(true)
            {
                var input=await ReadFrame(client,8000,CancellationToken.None);string text=new UTF8Encoding(false,true).GetString(input);
                var wav=engine.Speak(text);if(wav.Length>12*1024*1024)throw new IOException("Voice segment exceeds its limit.");
                var response=new byte[wav.Length+1];Buffer.BlockCopy(wav,0,response,1,wav.Length);await WriteFrame(client,response,CancellationToken.None);
            }
        }
        catch(EndOfStreamException){}
        catch(Exception)
        {
            // Never send native errors, local model paths or speech content to logs.
            try{using var end=new CancellationTokenSource(TimeSpan.FromSeconds(1));await WriteFrame(client,new byte[]{1},end.Token);}catch{}
        }
    }
    private static async Task WriteFrame(Stream stream,byte[] bytes,CancellationToken token)
    {
        byte[] size=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(size,bytes.Length);await stream.WriteAsync(size,token);await stream.WriteAsync(bytes,token);await stream.FlushAsync(token);
    }
    private static async Task<byte[]> ReadFrame(Stream stream,int maximum,CancellationToken token)
    {
        byte[] size=new byte[4];await stream.ReadExactlyAsync(size,token);int length=BinaryPrimitives.ReadInt32LittleEndian(size);
        if(length<0 || length>maximum)throw new IOException("Invalid voice-worker frame size.");
        byte[] bytes=new byte[length];await stream.ReadExactlyAsync(bytes,token);return bytes;
    }
}
