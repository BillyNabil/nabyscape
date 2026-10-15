using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
internal static class MiniEqualizerTests
{
    internal static void Run(Action<string,bool> test)
    {
        var meter=new ActivityVisual{Mini=true};
        for(int i=0;i<40;i++)meter.Sample(i%7<3?.8:.08,1/60d,0,false,false,true,i/60d,i%2==0);
        var levels=Enumerable.Range(0,5).Select(meter.MiniLevel).ToArray();
        test("Inline equalizer has five bounded differentiated level envelopes",levels.All(v=>v>=0 && v<=1) && levels.Max()-levels.Min()>.04);
        meter.Sample(0,.016,0,false,false,false,1,true,true);
        test("Inline equalizer pause or reduced motion immediately returns to baseline",Enumerable.Range(0,5).All(i=>meter.MiniLevel(i)==0) && meter.Envelope==0);
        for(int i=0;i<60;i++)meter.Sample(0,1/60d,0,false,false,true,i/60d,i%2==0,false);
        test("Inline equalizer silence does not invent measured activity",Enumerable.Range(0,5).All(i=>meter.MiniLevel(i)==0));
        var slow=new ActivityVisual{Mini=true};var fast=new ActivityVisual{Mini=true};
        for(int i=0;i<60;i++)slow.Sample(.5,1/30d,0,false,false,true,i/30d,true);
        for(int i=0;i<240;i++)fast.Sample(.5,1/120d,0,false,false,true,i/120d,i%4==0);
        test("Inline equalizer interpolation remains consistent across render rates",Enumerable.Range(0,5).All(i=>Math.Abs(slow.MiniLevel(i)-fast.MiniLevel(i))<.02));
        var text=new TextBlock{Text=new string('W',200),TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};var visual=new ActivityVisual{Mini=true,Width=22,Height=12};var row=new InlineMediaLabel(text,visual);
        row.Measure(new Size(120,30));row.Arrange(new Rect(0,0,120,30));row.UpdateLayout();var rect=visual.TransformToAncestor(row).TransformBounds(new Rect(visual.RenderSize));
        test("Long artist names preserve a bounded readable inline indicator",rect.Width==22 && rect.Left>=0 && rect.Right<=120 && text.ActualWidth<=90);
        text.Text="nonoc";row.Measure(new Size(300,30));row.Arrange(new Rect(0,0,300,30));row.UpdateLayout();rect=visual.TransformToAncestor(row).TransformBounds(new Rect(visual.RenderSize));
        test("Short artist names keep the indicator beside the text",Math.Abs(rect.Left-text.ActualWidth-8)<1);
    }
}
