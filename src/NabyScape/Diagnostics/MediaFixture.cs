using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Control;
using Windows.Media.Playback;

namespace NabyScape;

// A silent, isolated Windows media publisher used only by --diagnostics.
// It verifies the consumer API without starting/stopping any user's music.
internal static class MediaFixture
{
    public static async Task Verify(App app,string directory,Action<string,bool> check,Action<string> note,Func<Task>? verifyArtwork=null)
    {
        directory=Path.GetFullPath(directory);var original=app.Media.Selected;
        try
        {
            string coverPath=Path.Combine(directory,"fixture-cover.png");
            var visual=new System.Windows.Media.DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(UI.Brush("#FF9F0A"),null,new System.Windows.Rect(0,0,48,48));dc.DrawEllipse(System.Windows.Media.Brushes.Black,null,new System.Windows.Point(24,24),12,12);}
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(48,48,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(visual);
            var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(coverPath))png.Save(stream);
            var thumbnail=Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(await Windows.Storage.StorageFile.GetFileFromPathAsync(coverPath));
            string file=Path.Combine(directory,"silent-fixture.wav");
            using(var writer=new BinaryWriter(File.Create(file)))
            {
                const int samples=8000*60;writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(8000);writer.Write(16000);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);writer.Write(new byte[samples*2]);
            }
            using var player=new MediaPlayer{IsMuted=true};var list=new MediaPlaybackList{AutoRepeatEnabled=true};
            for(int i=1;i<=2;i++)
            {
                var item=new MediaPlaybackItem(MediaSource.CreateFromUri(new Uri(file)));
                var props=item.GetDisplayProperties();props.Type=MediaPlaybackType.Music;props.Thumbnail=thumbnail;props.MusicProperties.Title="NabyScape diagnostic "+i;props.MusicProperties.Artist="Silent test fixture";item.ApplyDisplayProperties(props);list.Items.Add(item);
            }
            player.Source=list;player.Play();
            var manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession? fixture=null;
            for(int attempt=0;attempt<12 && fixture==null;attempt++)
            {
                await Task.Delay(250);
                foreach(var session in manager.GetSessions())
                {try {var props=await session.TryGetMediaPropertiesAsync();if(props.Title.StartsWith("NabyScape diagnostic")) {fixture=session;break;}}catch {}}
            }
            if(fixture==null) {check("Silent Windows media fixture publishes a session",false);return;}
            app.Media.Select(fixture);
            // SMTC may deliver its SessionsChanged/metadata callbacks after Select returns.
            // Wait for this publisher's properties and thumbnail before checking cache identity.
            for(int ready=0;ready<20;ready++)
            {
                await app.Media.Refresh();
                if(app.Media.Selected?.SourceAppUserModelId==fixture.SourceAppUserModelId && app.Media.Title=="NabyScape diagnostic 1" && app.Media.Artist=="Silent test fixture" && app.Media.Artwork!=null)break;
                await Task.Delay(150);
            }
            check("Media metadata is read from a real Windows session",app.Media.Title=="NabyScape diagnostic 1" && app.Media.Artist=="Silent test fixture");
            var artwork=app.Media.Artwork;await app.Media.Refresh();
            check("Repeated metadata refresh reuses the decoded artwork",artwork!=null && ReferenceEquals(artwork,app.Media.Artwork));
            await app.Media.Command("toggle");await Task.Delay(400);await app.Media.Refresh();
            check("Media play/pause pauses the isolated publisher",!app.Media.Playing && player.PlaybackSession.PlaybackState==MediaPlaybackState.Paused);
            check("Playback status updates preserve artwork identity",ReferenceEquals(artwork,app.Media.Artwork));
            await app.Media.Command("toggle");await Task.Delay(400);await app.Media.Refresh();
            check("Media play/pause resumes the isolated publisher",app.Media.Playing);
            if(app.Media.CanSeek)
            {await app.Media.Seek(.5);await Task.Delay(450);check("Seek changes the isolated player's actual position",player.PlaybackSession.Position.TotalSeconds>=29 && player.PlaybackSession.Position.TotalSeconds<34);}
            else note("MANUAL  Seek not exposed by this Windows media fixture; seek UI remains disabled for unsupported sessions.");
            await app.Media.Command("next");await Task.Delay(450);await app.Media.Refresh();
            check("Next skips to another item in the isolated playlist",app.Media.Title=="NabyScape diagnostic 2");
            await app.Media.Command("previous");await Task.Delay(450);await app.Media.Refresh();
            check("Previous returns to the original playlist item",app.Media.Title=="NabyScape diagnostic 1");
            if(verifyArtwork!=null)await verifyArtwork();
            if(original!=null)
            {app.Media.Select(original);await app.Media.Refresh();check("Session selector switches back to the prior media app",app.Media.Selected?.SourceAppUserModelId==original.SourceAppUserModelId);}
            player.Pause();player.Source=null;
            note("INFO  Media commands tested against a muted silent fixture; user playback was not changed.");
        }
        catch(Exception ex) {check("Silent media integration fixture",false);note("INFO  Fixture error: "+ex.Message);app.Store.Log(ex);}
        finally {app.Media.Select(original);await app.Media.Refresh();}
    }
}
