using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly List<UIElement> monitorLayout=new();
    private readonly List<Grid> monitorValueRows=new();
    private int monitorRowIndex;
    private readonly CpuHistoryVisual monitorHistory=new();
    private readonly Grid monitorMemoryTrack=new(){Height=3,Margin=new(0,4,0,8),Background=UI.Brush("#28FFFFFF")};
    private void BeginMonitorRender(){monitorLayout.Clear();monitorRowIndex=0;}
    private void AddMonitorElement(UIElement element)=>monitorLayout.Add(element);
    private Grid GetMonitorRow()
    {
        if(monitorRowIndex<monitorValueRows.Count)return monitorValueRows[monitorRowIndex++];
        var row=new Grid{Margin=new(0,3,0,3)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var name=UI.Text("",11,UI.Muted);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.Margin=new(0,0,8,0);row.Children.Add(name);
        var text=UI.Text("");text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(text,1);row.Children.Add(text);monitorValueRows.Add(row);monitorRowIndex++;return row;
    }
    private void CommitMonitorRender()
    {
        for(int i=0;i<monitorLayout.Count;i++)
        {
            var next=monitorLayout[i];
            if(i<monitorRows.Children.Count)
            {
                var old=monitorRows.Children[i];if(ReferenceEquals(old,next))continue;
                if(old is TextBlock previous && next is TextBlock text)
                {Motion.Text(previous,text.Text);previous.FontSize=text.FontSize;previous.FontWeight=text.FontWeight;previous.Foreground=text.Foreground;previous.Margin=text.Margin;previous.TextWrapping=text.TextWrapping;previous.TextTrimming=text.TextTrimming;previous.ToolTip=text.ToolTip;previous.Tag=text.Tag;continue;}
            }
            if(next is FrameworkElement {Parent:Panel owner})owner.Children.Remove(next);
            if(i<monitorRows.Children.Count)monitorRows.Children.RemoveAt(i);
            monitorRows.Children.Insert(i,next);
        }
        while(monitorRows.Children.Count>monitorLayout.Count)monitorRows.Children.RemoveAt(monitorRows.Children.Count-1);
        // A process/device list may shrink. Do not retain detached historical rows indefinitely.
        if(monitorValueRows.Count>monitorRowIndex)monitorValueRows.RemoveRange(monitorRowIndex,monitorValueRows.Count-monitorRowIndex);
        monitorLayout.Clear();
    }
    private void UpdateMonitorMemory(ulong total,ulong available)
    {
        if(monitorMemoryTrack.ColumnDefinitions.Count==0){monitorMemoryTrack.ColumnDefinitions.Add(new());monitorMemoryTrack.ColumnDefinitions.Add(new());monitorMemoryTrack.Children.Add(new Border());}
        monitorMemoryTrack.ColumnDefinitions[0].Width=new GridLength(Math.Max(1,total-available),GridUnitType.Star);monitorMemoryTrack.ColumnDefinitions[1].Width=new GridLength(Math.Max(1,available),GridUnitType.Star);((Border)monitorMemoryTrack.Children[0]).Background=UI.Brush(app.Settings.Accent);
    }
}

internal sealed class CpuHistoryVisual : FrameworkElement
{
    private readonly double?[] values=new double?[30];
    private int count;
    private Brush color=Brushes.White;
    private static readonly Pen baseline=new(UI.Brush("#28FFFFFF"),1);
    internal CpuHistoryVisual(){Height=42;ClipToBounds=true;Margin=new(0,4,0,2);}
    internal void SetValues(IEnumerable<double?> history,Brush accent){count=0;foreach(var value in history)values[count++]=value;color=accent;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawLine(baseline,new Point(0,41),new Point(ActualWidth,41));var geometry=new StreamGeometry();
        using(var path=geometry.Open())
        {
            bool started=false;
            for(int i=0;i<count;i++){if(values[i] is not double value){started=false;continue;}var point=new Point(ActualWidth*(30-count+i)/29,40-value*.38);if(!started){path.BeginFigure(point,false,false);started=true;}else path.LineTo(point,true,false);}
        }
        geometry.Freeze();dc.DrawGeometry(null,new Pen(color,1.6),geometry);
    }
}
