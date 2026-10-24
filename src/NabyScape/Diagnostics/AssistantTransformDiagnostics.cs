using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class AssistantTransformDiagnostics
{
    internal static Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        island.Hide();app.Settings.ReduceMotion=false;Motion.Configure(app.Settings);
        var orb=new AssistantOrb();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string name)=>(T)typeof(AssistantOrb).GetField(name,flags)!.GetValue(orb)!;
        void Set(string name,object value)=>typeof(AssistantOrb).GetField(name,flags)!.SetValue(orb,value);
        byte[] Capture(bool reference,double size,double dpi)
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())orb.DrawCharacter(dc,reference);
            var image=new RenderTargetBitmap((int)Math.Ceiling(size*dpi),(int)Math.Ceiling(size*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);image.Render(visual);
            var pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);return pixels;
        }
        var evidence=new List<object>();int comparisons=0,worst=0;long differences=0;
        foreach(double size in new[]{28d,32d,52d,72d,144d})foreach(double dpi in new[]{1d,1.25,1.5,1.75,2})
        foreach(var state in Enum.GetValues<AssistantState>())
        {
            int groupWorst=0;long groupDifferences=0;
            for(int pose=0;pose<5;pose++)
            {
                orb.State=state;orb.Width=orb.Height=size;orb.Measure(new Size(size,size));orb.Arrange(new Rect(0,0,size,size));
                double t=.371+pose*.613;Set("reduceMotion",false);Set("rendering",true);Set("phase",t);Set("displayLevel",.2+.15*pose);
                Field<MatrixTransform>("idlePose").BeginAnimation(MatrixTransform.MatrixProperty,null);Field<MatrixTransform>("idlePose").Matrix=AssistantIdleMotion.Pose(t,pose%2==0);
                var lift=Field<TranslateTransform>("lift");lift.BeginAnimation(TranslateTransform.YProperty,null);lift.Y=-2*Math.Sin(t);
                var lean=Field<RotateTransform>("lean");lean.BeginAnimation(RotateTransform.AngleProperty,null);lean.Angle=4*Math.Cos(t);
                var squash=Field<ScaleTransform>("squash");squash.BeginAnimation(ScaleTransform.ScaleXProperty,null);squash.BeginAnimation(ScaleTransform.ScaleYProperty,null);squash.ScaleX=1+.04*Math.Sin(t);squash.ScaleY=1-.03*Math.Sin(t);
                var gaze=Field<TranslateTransform>("gaze");gaze.BeginAnimation(TranslateTransform.XProperty,null);gaze.BeginAnimation(TranslateTransform.YProperty,null);gaze.X=6*Math.Sin(t);gaze.Y=4*Math.Cos(t);
                var expression=Field<ScaleTransform>("expression");expression.BeginAnimation(ScaleTransform.ScaleYProperty,null);expression.ScaleY=.6+.12*pose;
                foreach(string name in new[]{"leftLid","rightLid"}){var lid=Field<ScaleTransform>(name);lid.BeginAnimation(ScaleTransform.ScaleYProperty,null);lid.ScaleY=pose==2?.1:1;}
                var headphones=Field<ScaleTransform>("headphonesReveal");headphones.BeginAnimation(ScaleTransform.ScaleXProperty,null);headphones.BeginAnimation(ScaleTransform.ScaleYProperty,null);headphones.ScaleX=headphones.ScaleY=pose%2;
                orb.Live=pose%2==0;
                var before=Capture(true,size,dpi);var after=Capture(false,size,dpi);
                for(int i=0;i<before.Length;i++){int delta=Math.Abs(before[i]-after[i]);groupWorst=Math.Max(groupWorst,delta);if(delta!=0)groupDifferences++;}
                comparisons++;
            }
            worst=Math.Max(worst,groupWorst);differences+=groupDifferences;
            evidence.Add(new{size,dpi,state=state.ToString(),poses=5,maxChannelDelta=groupWorst,differentChannels=groupDifferences});
            check($"Character transforms: original raster preserved at {size} DIP, {dpi} scale, {state} (max delta {groupWorst})",groupWorst==0);
        }
        Set("rendering",false);orb.ReduceMotion=true;
        File.WriteAllText(Path.Combine(directory,"transform-parity.json"),JsonSerializer.Serialize(new{comparisons,maxChannelDelta=worst,differentChannels=differences,evidence},new JsonSerializerOptions{WriteIndented=true}));
        return Task.CompletedTask;
    }
}
