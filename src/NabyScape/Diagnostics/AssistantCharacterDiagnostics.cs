using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class AssistantCharacterDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        island.Hide();
        var orb=new AssistantOrb{Width=144,Height=144,Margin=new(0,10,0,0)};
        var caption=UI.Text("Hello",17);caption.HorizontalAlignment=HorizontalAlignment.Center;caption.FontWeight=FontWeights.SemiBold;
        var canvas=new StackPanel{Width=320,Height=218,Background=UI.Brush("#0B0C0E")};canvas.Children.Add(orb);canvas.Children.Add(caption);
        var window=new Window{Content=canvas,Width=320,Height=218,WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false,ResizeMode=ResizeMode.NoResize};
        try
        {
            window.Show();await Task.Delay(350);
            check("Idle character uses sparse blinking without continuous rendering",!orb.Rendering && orb.BlinkScheduled);
            orb.LookAt(10,-10);await Task.Delay(230);
            check("Pointer tracking eases to bounded eye coordinates",Math.Abs(orb.Gaze.X-6)<.1 && Math.Abs(orb.Gaze.Y+4)<.1 && !orb.Rendering);
            orb.LookAt(double.NaN,double.PositiveInfinity);await Task.Delay(230);
            check("Nonfinite pointer coordinates safely return the face to center",Math.Abs(orb.Gaze.X)<.1 && Math.Abs(orb.Gaze.Y)<.1);
            orb.Press(true);check("Press feedback preserves the click gesture without starting a session",orb.Pressed && !app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing);
            orb.Press(false);await Task.Delay(320);check("Release settles and clears the pressed state",!orb.Pressed);
            var gif=new GifBitmapEncoder();
            var states=new[]{AssistantState.Idle,AssistantState.Listening,AssistantState.Thinking,AssistantState.Speaking,AssistantState.Approval,AssistantState.Error};
            var sheet=new DrawingVisual();
            using(var sheetContext=sheet.RenderOpen())
            {
                sheetContext.DrawRectangle(UI.Brush("#0B0C0E"),null,new Rect(0,0,960,436));
                for(int i=0;i<states.Length;i++)
                {
                    orb.State=states[i];caption.Text=states[i].ToString();orb.Live=states[i] is AssistantState.Listening or AssistantState.Speaking;
                    for(int frame=0;frame<16;frame++)
                    {
                        orb.Level=.15+.75*Math.Abs(Math.Sin(frame*.37));
                        if(i==0){orb.LookAt(Math.Sin(frame*.4),Math.Cos(frame*.4)*.5);if(frame==4)orb.Blink(true);}
                        await Task.Delay(40);canvas.UpdateLayout();
                        var image=Capture(canvas);
                        var frameMetadata=new BitmapMetadata("gif");frameMetadata.SetQuery("/grctlext/Delay",(ushort)4);frameMetadata.SetQuery("/grctlext/Disposal",(byte)2);
                        gif.Frames.Add(BitmapFrame.Create(image,null,frameMetadata,null));
                        if(frame==12)
                        {
                            Write(image,Path.Combine(directory,"character-"+states[i].ToString().ToLowerInvariant()+".png"));
                            sheetContext.DrawImage(image,new Rect((i%3)*320,(i/3)*218,320,218));
                        }
                    }
                }
            }
            using(var encoded=new MemoryStream())
            {
                gif.Save(encoded);var bytes=encoded.ToArray();
                int header=13+((bytes[10]&128)!=0?3*(1<<((bytes[10]&7)+1)):0);
                // WPF supports frame metadata but not GIF global metadata. Add the
                // standard loop extension after its logical screen/global palette.
                bytes[4]=(byte)'9';using var looped=new MemoryStream();looped.Write(bytes,0,header);
                looped.Write(new byte[]{0x21,0xff,0x0b});looped.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));looped.Write(new byte[]{3,1,0,0,0});looped.Write(bytes,header,bytes.Length-header);
                File.WriteAllBytes(Path.Combine(directory,"character-motion.gif"),looped.ToArray());
            }
            var sheetImage=new RenderTargetBitmap(960,436,96,96,PixelFormats.Pbgra32);sheetImage.Render(sheet);Write(sheetImage,Path.Combine(directory,"character-expressions.png"));
            orb.State=AssistantState.Listening;await Task.Delay(60);check("Listening character wakes the existing paced render callback",orb.Rendering);
            orb.ReduceMotion=true;check("Reduce Motion stops both continuous rendering and scheduled blinks",!orb.Rendering && !orb.BlinkScheduled);
            orb.Press(true);orb.Press(false);orb.LookAt(1,1);check("Reduced-motion pointer feedback stays finite and static",!orb.Rendering && double.IsFinite(orb.Gaze.X));
            bool motionEnabled=Motion.Enabled;
            try{Motion.Enabled=false;orb.ReduceMotion=false;check("Disabling app animations also stops character frames and sparse blinks",!orb.Rendering && !orb.BlinkScheduled);}
            finally{Motion.Enabled=motionEnabled;orb.ReduceMotion=false;}
            orb.ReduceMotion=false;window.Hide();check("Hidden character releases render callbacks and blink scheduling",!orb.Rendering && !orb.BlinkScheduled && !orb.Pressed);
            window.Show();orb.State=AssistantState.Idle;await Task.Delay(40);check("Visible idle character resumes sparse blinking only",!orb.Rendering && orb.BlinkScheduled);
            window.Close();await Task.Delay(40);check("Closing character view releases all visual scheduling",!orb.Rendering && !orb.BlinkScheduled);
            check("Character interaction never enables microphone, sharing or actions",!app.Assistant.VoiceConnected && !app.Assistant.Desktop.Sharing && !app.Assistant.ActionsAllowed);
            File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Actual retained WPF character, synthetic listening/speaking levels and state fixtures. GIF sampled at 25 fps; application uses its existing FramePacer. No provider, credential, microphone, speaker, desktop capture or native input used.");
        }
        finally{if(window.IsLoaded)window.Close();}
    }
    private static RenderTargetBitmap Capture(FrameworkElement element){var bitmap=new RenderTargetBitmap(320,218,96,96,PixelFormats.Pbgra32);bitmap.Render(element);return bitmap;}
    private static void Write(BitmapSource image,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);}
}
