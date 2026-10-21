using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class OverlayWindow
{
    private FrameworkElement CalendarToolbar()
    {
        var month=UI.Button("Toggle calendar month",()=>{calendarMonthExpanded=!calendarMonthExpanded;RefreshTools();});var monthLabel=UI.Row(UI.Heading(calendarMonth.ToString("MMMM yyyy"),17),Icons.Create("chevron",8,UI.Muted));((FrameworkElement)monthLabel.Children[1]).Margin=new(8,0,0,0);month.Content=monthLabel;month.Padding=new(0);month.Margin=new(0);month.Background=Brushes.Transparent;month.MinHeight=30;
        Button Arrow(bool forward)
        {
            var button=UI.Button(forward?"Next calendar month":"Previous calendar month",()=>{if(forward && calendarMonth.Year==9999 && calendarMonth.Month==12 || !forward && calendarMonth==DateTime.MinValue)return;calendarMonth=calendarMonth.AddMonths(forward?1:-1);calendarMonthExpanded=true;RefreshTools();});
            var icon=Icons.Create("chevron",9,UI.Muted);icon.RenderTransformOrigin=new(.5,.5);icon.RenderTransform=new RotateTransform(forward?-90:90);button.Content=icon;button.Width=26;button.MinHeight=30;button.Padding=new(6);button.Margin=new(0,0,4,0);button.Background=Brushes.Transparent;button.IsEnabled=forward?calendarMonth.Year<9999 || calendarMonth.Month<12:calendarMonth>DateTime.MinValue;return button;
        }
        var today=UI.Button("Today",()=>{calendarDay=DateTime.Today;calendarMonth=new(calendarDay.Year,calendarDay.Month,1);calendarMonthExpanded=false;RefreshTools();});today.Padding=new(8,4,8,4);today.Margin=new(2,0,6,0);today.Background=Brushes.Transparent;today.MinHeight=30;today.FontSize=11;
        var add=UI.Button("Add local event",()=>{calendarEditing=true;RefreshTools();});add.Content="Add event";add.MinHeight=30;add.Padding=new(10,4,10,4);add.Margin=new(0);add.FontSize=11;
        return MinimalWidgets.Header(month,UI.Row(Arrow(false),Arrow(true),today,add));
    }
    private FrameworkElement CalendarWeek()
    {
        var week=new System.Windows.Controls.Primitives.UniformGrid{Columns=7,Margin=new(0,6,0,18)};UI.Named(week,"Calendar week");
        foreach(var day in MinimalWidgets.Week(calendarDay))
        {
            var content=new StackPanel();var name=UI.Text(day.ToString("ddd"),9,UI.Muted);name.HorizontalAlignment=HorizontalAlignment.Center;content.Children.Add(name);
            var number=UI.Text(day.Day.ToString(),15);number.HorizontalAlignment=HorizontalAlignment.Center;
            var selected=new Border{Child=number,Width=30,Height=30,CornerRadius=new(15),Background=day==calendarDay?UI.Brush("#343745"):Brushes.Transparent,Margin=new(0,5,0,4)};content.Children.Add(selected);
            var dot=new Border{Width=3,Height=3,CornerRadius=new(2),Background=UI.Brush("#9AA7C2"),Opacity=DayEvents(day).Any()?1:0,HorizontalAlignment=HorizontalAlignment.Center};content.Children.Add(dot);
            var button=UI.Button(day.ToString("dddd, MMMM d, yyyy"),()=>{calendarDay=day;calendarMonth=new(day.Year,day.Month,1);calendarMonthExpanded=false;RefreshTools();});button.Content=content;button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.Padding=new(0);button.Margin=new(0);button.ToolTip=day.ToString("D");week.Children.Add(button);
        }
        return week;
    }
    private FrameworkElement CalendarAgendaEvent(CalendarEvent item)
    {
        var row=new Grid{Margin=new(0,0,0,14)};row.ColumnDefinitions.Add(new(){Width=new GridLength(54)});row.ColumnDefinitions.Add(new(){Width=new GridLength(14)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(30)});
        var time=UI.Text(item.Start.ToLocalTime().ToString("HH:mm"),10,UI.Muted);time.VerticalAlignment=VerticalAlignment.Top;time.Margin=new(0,4,0,0);row.Children.Add(time);
        var rail=new Border{Width=2,MinHeight=35,CornerRadius=new(1),Background=UI.Brush("#99A6C3"),Margin=new(0,3,10,3)};Grid.SetColumn(rail,1);row.Children.Add(rail);
        var labels=new StackPanel();var title=UI.Text(item.Title,12);title.TextWrapping=TextWrapping.Wrap;labels.Children.Add(title);
        string range=item.Start.LocalDateTime.Date==item.End.LocalDateTime.Date?item.Start.ToLocalTime().ToString("HH:mm")+" – "+item.End.ToLocalTime().ToString("HH:mm"):item.Start.ToLocalTime().ToString("MMM d, HH:mm")+" – "+item.End.ToLocalTime().ToString("MMM d, HH:mm");
        var duration=UI.Text(range,10,UI.Muted);duration.Margin=new(0,5,0,0);labels.Children.Add(duration);
        if(item.MeetingUrl.Length>0){var join=UI.Button("Open meeting",()=>{try{ShortcutService.Open(new(){Target=item.MeetingUrl});}catch(Exception ex){app.Notify("Could not open meeting",ex.Message);}});join.FontSize=10;join.MinHeight=26;join.Padding=new(0,5,8,5);join.Background=Brushes.Transparent;join.HorizontalAlignment=HorizontalAlignment.Left;labels.Children.Add(join);}
        Grid.SetColumn(labels,2);row.Children.Add(labels);
        var remove=MinimalWidgets.More("Event actions · "+item.Title,("Delete event "+item.Title,()=>MutateUtility(()=>app.Calendar.Remove(item.Id))));remove.Margin=new(0);Grid.SetColumn(remove,3);row.Children.Add(remove);MinimalWidgets.Reveal(remove,row);return row;
    }
}
