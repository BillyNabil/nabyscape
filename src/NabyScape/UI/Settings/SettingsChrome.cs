using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private FrameworkElement SettingsFrame(UIElement content)
    {
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;UseLayoutRounding=true;
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=42,ResizeBorderThickness=new(6),CornerRadius=new(10),GlassFrameThickness=new(0),UseAeroCaptionButtons=false});
        var frame=new Grid();frame.RowDefinitions.Add(new(){Height=new GridLength(42)});frame.RowDefinitions.Add(new());
        // The caption bar floats over the shell so the sidebar runs to the top edge.
        var bar=new Grid{Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Top,Height=42};bar.ColumnDefinitions.Add(new());bar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);bar.Children.Add(actions);
        Button Caption(string name,string data,Action action)
        {
            var button=UI.CaptionButton(name,action);button.Width=46;button.Height=42;button.MinHeight=42;button.Padding=new(0);button.Margin=new(0);button.Tag=new CornerRadius(0);button.Background=Brushes.Transparent;button.ToolTip=Localization.Text(name);
            button.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse(data),Stroke=SettingsTheme.Quiet,StrokeThickness=1.15,Width=11,Height=11,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            WindowChrome.SetIsHitTestVisibleInChrome(button,true);button.MouseEnter+=(_,_)=>button.Background=UI.Brush(name=="Close Settings"?"#B32632":"#1A211B");button.MouseLeave+=(_,_)=>button.Background=Brushes.Transparent;actions.Children.Add(button);return button;
        }
        Caption("Minimize Settings","M0,5 H10",()=>SystemCommands.MinimizeWindow(this));
        var maximize=Caption("Maximize Settings","M0,0 H10 V10 H0 Z",()=>{if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);});
        StateChanged+=(_,_)=>{frame.Margin=WindowState==WindowState.Maximized?new Thickness(7):new Thickness(0);UI.LocalizedName(maximize,WindowState==WindowState.Maximized?"Restore Settings":"Maximize Settings");};
        Caption("Close Settings","M0,0 L10,10 M10,0 L0,10",()=>SystemCommands.CloseWindow(this));
        Grid.SetRowSpan(content,2);frame.Children.Add(content);frame.Children.Add(bar);return frame;
    }
}
