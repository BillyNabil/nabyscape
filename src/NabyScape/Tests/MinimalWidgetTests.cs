using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
internal static class MinimalWidgetTests
{
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool value){if(!value)throw new Exception("Minimal widget fixture failed.");}
        test("Week navigation crosses month and leap-day boundaries with seven real dates",()=>
        {
            foreach(var date in new[]{new DateTime(2024,2,29),new DateTime(2026,12,31),new DateTime(2026,10,21),DateTime.MinValue,DateTime.MaxValue})
            {var week=MinimalWidgets.Week(date);Assert(week.Length==7 && week.Distinct().Count()==7 && week.Contains(date.Date) && week.Zip(week.Skip(1),(a,b)=>(b-a).TotalDays).All(days=>days==1));}
        });
        test("Visual galleries reflow to one column without shrinking readable tiles",()=>
        {
            var panel=new TileFlow{MinimumTileWidth=152,Gap=16};for(int i=0;i<4;i++)panel.Children.Add(new Border{Height=140});
            panel.Measure(new Size(460,double.PositiveInfinity));panel.Arrange(new Rect(0,0,460,panel.DesiredSize.Height));Assert(panel.Columns==2 && panel.Children.OfType<Border>().All(c=>c.ActualWidth==222));
            panel.Measure(new Size(240,double.PositiveInfinity));panel.Arrange(new Rect(0,0,240,panel.DesiredSize.Height));Assert(panel.Columns==1 && panel.Children.OfType<Border>().All(c=>c.ActualWidth==240) && panel.DesiredSize.Height==608);
        });
        test("Clipboard details span the gallery and remain below all previews",()=>
        {
            var panel=new TileFlow{MinimumTileWidth=152,Gap=16};var first=new Border{Height=100};var second=new Border{Height=140};var detail=new Border{Tag="clip-detail",Height=60};panel.Children.Add(first);panel.Children.Add(second);panel.Children.Add(detail);
            panel.Measure(new Size(460,double.PositiveInfinity));panel.Arrange(new Rect(0,0,460,panel.DesiredSize.Height));Assert(detail.ActualWidth==460 && detail.TranslatePoint(new Point(),panel).Y>=156 && panel.DesiredSize.Height==216);
        });
        test("File previews do not resolve network URLs, UNC paths or remote drives",()=>
        {
            foreach(string path in new[]{"https://example.com/file.pdf",@"\\server\share\file.pdf","file:///C:/sample.pdf","relative.pdf"})Assert(AppIconCache.Thumbnail(path,192).GetAwaiter().GetResult()==null);
        });
    }
}
