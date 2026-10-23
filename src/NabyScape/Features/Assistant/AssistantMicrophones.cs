using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;

namespace NabyScape;

internal sealed record AssistantMicrophone(string Id,string Name,int Number)
{
    public override string ToString()=>Name;
}

internal static class AssistantMicrophones
{
    internal static AssistantMicrophone[] List()
    {
        var devices=new List<AssistantMicrophone>{new("","Windows default microphone",-1)};
        for(int i=0;i<WaveIn.DeviceCount;i++)
        {
            var caps=WaveIn.GetCapabilities(i);
            devices.Add(new(caps.NameGuid+"|"+caps.ProductName,caps.ProductName,i));
        }
        return devices.ToArray();
    }
    internal static int Resolve(string? id)=>Resolve(id,List());
    internal static int Resolve(string? id,IEnumerable<AssistantMicrophone> devices)
    {
        if(string.IsNullOrEmpty(id))return -1;
        var matches=devices.Where(d=>d.Id==id).ToArray();
        if(matches.Length!=1)throw new IOException("Your selected microphone is unavailable or has a duplicate name. Reconnect it, or choose another input in Voice settings.");
        return matches[0].Number;
    }
}

internal sealed partial class AssistantRuntime
{
    internal Task WaitForMicrophoneRelease()=>Task.WhenAll(wakeClosing,voiceClosing);
    internal void SaveMicrophone(string id)
    {
        if(SpeechCheckBusy || VoiceConnected || Busy)throw new InvalidOperationException("End voice or the audio test before changing your microphone.");
        AssistantMicrophones.Resolve(id);
        string previous=Options.MicrophoneId;Options.MicrophoneId=id;
        try{app.Store.Save("assistant",Options);}catch{Options.MicrophoneId=previous;throw;}
        bool resume=WakeArmed;PauseWake();wakeWanted=resume;wakeFailed=false;Notify();
    }
}

internal sealed partial class AssistantProviderSettings
{
    private FrameworkElement MicrophoneSettings()
    {
        var body=new StackPanel{Margin=new(0,0,0,18)};
        body.Children.Add(UI.Text("Microphone",14));
        var input=new ComboBox{MinHeight=38,Margin=new(0,8,0,8)};UI.Named(input,"Input microphone");body.Children.Add(input);
        var feedback=UI.Text("",11,UI.Muted);Accessibility.Live(feedback);
        bool loading=false;
        void Refresh()
        {
            loading=true;
            try
            {
                input.ItemsSource=AssistantMicrophones.List();input.SelectedItem=input.Items.Cast<AssistantMicrophone>().FirstOrDefault(d=>d.Id==ai.Options.MicrophoneId);
                feedback.Text=input.SelectedItem==null?"Saved microphone unavailable. Reconnect it or choose an input.":"Selected: "+input.SelectedItem+" · used for voice, wake word and audio tests.";
            }
            catch(Exception){feedback.Text="Could not list microphones. Check Windows sound settings, then refresh.";}
            finally{loading=false;}
        }
        input.SelectionChanged+=(_,_)=>
        {
            if(loading || input.SelectedItem is not AssistantMicrophone selected)return;
            try{ai.SaveMicrophone(selected.Id);feedback.Text="Saved · "+selected.Name;Feedback(feedback.Text);}
            catch(Exception ex){Refresh();feedback.Text=ex.Message;Feedback(feedback.Text,true);}
        };
        var actions=new WrapPanel();actions.Children.Add(AssistantWindow.Pill("Test microphone",()=>{selectVoicePage?.Invoke("Audio");testSelectedMicrophone?.Invoke();}));
        actions.Children.Add(AssistantWindow.Pill("Refresh inputs",Refresh));body.Children.Add(actions);body.Children.Add(feedback);Refresh();
        body.IsVisibleChanged+=(_,_)=>{if(body.IsVisible)Refresh();};return body;
    }
}

// SAPI consumes a blocking PCM stream so wake detection uses the same selected
// WaveIn device as conversation and tests. The bounded queue caps idle memory.
internal sealed class AssistantWakeInput : Stream
{
    private readonly BlockingCollection<byte[]> blocks=new(32);
    private readonly WaveInEvent microphone;
    private byte[]? current;
    private int offset,closed;
    internal AssistantWakeInput(string id)
    {
        microphone=new WaveInEvent{DeviceNumber=AssistantMicrophones.Resolve(id),WaveFormat=new WaveFormat(16000,16,1),BufferMilliseconds=50};
        microphone.DataAvailable+=(_,e)=>
        {
            if(System.Threading.Volatile.Read(ref closed)!=0)return;
            var bytes=new byte[e.BytesRecorded];Buffer.BlockCopy(e.Buffer,0,bytes,0,bytes.Length);
            try{if(!blocks.TryAdd(bytes))blocks.CompleteAdding();}catch(InvalidOperationException){}
        };
        microphone.RecordingStopped+=(_,_)=>blocks.CompleteAdding();
        try{microphone.StartRecording();}catch{Dispose();throw;}
    }
    public override int Read(byte[] buffer,int start,int count)
    {
        if(count==0 || System.Threading.Volatile.Read(ref closed)!=0)return 0;
        if(current==null || offset==current.Length)
        {
            if(!blocks.TryTake(out current,System.Threading.Timeout.Infinite))return 0;
            offset=0;
        }
        int length=Math.Min(count,current.Length-offset);Buffer.BlockCopy(current,offset,buffer,start,length);offset+=length;return length;
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing && System.Threading.Interlocked.Exchange(ref closed,1)==0)
        {
            blocks.CompleteAdding();try{microphone.StopRecording();}finally{microphone.Dispose();}
        }
        base.Dispose(disposing);
    }
    public override bool CanRead=>true;
    public override bool CanSeek=>false;
    public override bool CanWrite=>false;
    public override long Length=>throw new NotSupportedException();
    public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override void Flush(){}
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
