using System;
using System.IO;
using System.Windows.Threading;
using NAudio.Wave;

namespace NabyScape;

// Plays independently of Windows notification volume/routing. No background polling.
internal sealed class CountdownAudio : IDisposable
{
    private WaveOutEvent? player;
    private WaveStream? stream;
    private readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;
    internal string? LastError {get;private set;}
    internal void Play(Settings settings)
    {
        Dispose();LastError=null;if(settings.CountdownSound=="Off" || settings.CountdownSoundVolume<=0)return;
        try
        {
            if(settings.CountdownSound=="Custom")
            {
                var file=new FileInfo(settings.CountdownSoundFile);
                if(!file.Exists || file.Length>10*1024*1024 || !file.Extension.Equals(".wav",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Choose a WAV file under 10 MB.");
                stream=new WaveFileReader(file.FullName);
                if(stream.TotalTime.TotalSeconds>15)throw new InvalidDataException("Choose a sound no longer than 15 seconds.");
            }
            else stream=new RawSourceWaveStream(new MemoryStream(Tone(settings.CountdownSound)),new WaveFormat(22050,16,1));
            var output=new WaveOutEvent();player=output;output.Volume=(float)settings.CountdownSoundVolume;
            output.PlaybackStopped+=(_,_)=>dispatcher.BeginInvoke(()=>{if(ReferenceEquals(player,output))Dispose();});
            output.Init(stream);output.Play();
        }
        catch(Exception ex)
        {
            string error=ex is IOException or UnauthorizedAccessException?ex.Message:"Could not play the completion sound on the current output.";
            Dispose();
            if(settings.CountdownSound=="Custom"){Play(new Settings{CountdownSound="Chime",CountdownSoundVolume=settings.CountdownSoundVolume});LastError=error+" Used the built-in chime instead.";}else LastError=error;
        }
    }
    internal static byte[] Tone(string kind)
    {
        const int rate=22050;int length=rate*2;var data=new byte[length*2];var tones=new[]{(0d,659.25),(.16,830.61),(.32,987.77)};
        for(int i=0;i<length;i++)
        {
            double t=i/(double)rate,value=0;
            if(kind=="Bell")value=(Math.Sin(2*Math.PI*660*t)+.35*Math.Sin(2*Math.PI*1323*t))*Math.Exp(-t*3.4);
            else
            {
                foreach(var tone in tones)
                {double elapsed=t-tone.Item1;if(elapsed>=0)value+=.42*Math.Sin(2*Math.PI*tone.Item2*elapsed)*Math.Min(1,elapsed/.008)*Math.Exp(-elapsed*4.1);}
            }
            short sample=(short)(Math.Clamp(value*.45,-.95,.95)*short.MaxValue);data[i*2]=(byte)sample;data[i*2+1]=(byte)(sample>>8);
        }
        return data;
    }
    public void Dispose(){var old=player;player=null;try{old?.Stop();}catch{}old?.Dispose();stream?.Dispose();stream=null;}
}

public sealed partial class OverlayWindow
{
    private System.Windows.FrameworkElement FocusDurationEditor()
    {
        var row=new System.Windows.Controls.Grid{Margin=new(0,2,0,9)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new());
        void Number(string label,int value,int maximum,int column,Action<int> changed)
        {
            var group=new System.Windows.Controls.StackPanel{Margin=new(column==0?0:6,0,column==0?6:0,0)};group.Children.Add(UI.Text(label+" · min",10,UI.Muted));
            var input=UI.Input(label+" minutes",value.ToString());input.Margin=new(0,5,0,0);input.MinWidth=0;input.MinHeight=30;input.Padding=new(8,5,8,5);input.MaxLength=3;group.Children.Add(input);System.Windows.Controls.Grid.SetColumn(group,column);row.Children.Add(group);
            void Commit(){if(int.TryParse(input.Text,out int number) && number>=1 && number<=maximum){changed(number);app.ChangeSettings();}else{input.Text=(column==0?app.Settings.FocusMinutes:app.Settings.BreakMinutes).ToString();input.ToolTip="Enter 1–"+maximum+" minutes.";}}
            input.LostKeyboardFocus+=(_,_)=>Commit();input.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit();e.Handled=true;}};
        }
        Number("Focus",app.Settings.FocusMinutes,240,0,value=>app.Settings.FocusMinutes=value);Number("Break",app.Settings.BreakMinutes,120,1,value=>app.Settings.BreakMinutes=value);return row;
    }
}
