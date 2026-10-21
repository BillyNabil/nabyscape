using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace NabyScape;

// A retained screen map; redraws only on a geometry change or a user gesture.
internal sealed class DockPositionEditor : UserControl
{
    private readonly App app;
    private readonly Action save;
    private readonly Canvas map=new(){Height=132,Background=Brushes.Transparent,ClipToBounds=true};
    private readonly TextBlock coordinates=UI.Text("",11,UI.Muted),alignment=UI.Text("",11);
    private readonly Slider horizontal=new(){Minimum=0,Maximum=100,TickFrequency=.1,IsSnapToTickEnabled=true},vertical=new();
    private readonly TextBox x=UI.Input("Horizontal position percent",""),y=UI.Input("Vertical dock position","");
    private readonly TextBlock verticalLabel=UI.Text("",11,UI.Muted);
    private bool syncing,dragging;
    internal DockPositionEditor(App app,Action save)
    {
        this.app=app;this.save=save;var p=new StackPanel();
        p.Children.Add(new Border{Child=map,CornerRadius=new(10),BorderBrush=UI.Brush("#343C48"),BorderThickness=new(.7),Background=UI.Brush("#10151D"),Margin=new(0,0,0,10)});
        var info=new DockPanel();var center=UI.Button("Center",()=>{app.Settings.Horizontal=50;app.Settings.DockVertical=50;Changed();});center.FontSize=11;center.Padding=new(12,5,12,5);DockPanel.SetDock(center,Dock.Right);info.Children.Add(center);info.Children.Add(alignment);p.Children.Add(info);coordinates.Margin=new(0,3,0,12);p.Children.Add(coordinates);
        void Field(string name,Slider slider,TextBox input,TextBlock label)
        {var row=new DockPanel();input.Width=82;input.Margin=new(8,0,0,0);input.Padding=new(6,4,6,4);DockPanel.SetDock(input,Dock.Right);row.Children.Add(input);row.Children.Add(label);p.Children.Add(row);slider.Margin=new(0,8,0,14);UI.Named(slider,name);p.Children.Add(slider);}
        Field("Horizontal position",horizontal,x,UI.Text("Horizontal · %",11,UI.Muted));Field("Vertical dock position",vertical,y,verticalLabel);
        horizontal.ValueChanged+=(_,_)=>{if(syncing)return;app.Settings.Horizontal=horizontal.Value;Changed();};
        vertical.ValueChanged+=(_,_)=>{if(syncing)return;if(app.Settings.DockEdge is "Left" or "Right")app.Settings.DockVertical=vertical.Value;else if(app.Settings.DockEdge=="Bottom")app.Settings.DockInset=vertical.Value;else app.Settings.Top=vertical.Value;Changed();};
        void Commit(TextBox input,Slider slider){if(double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value))slider.Value=Math.Clamp(value,slider.Minimum,slider.Maximum);Sync();}
        x.LostKeyboardFocus+=(_,_)=>Commit(x,horizontal);y.LostKeyboardFocus+=(_,_)=>Commit(y,vertical);x.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit(x,horizontal);e.Handled=true;}};y.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit(y,vertical);e.Handled=true;}};
        map.SizeChanged+=(_,_)=>Draw();map.MouseLeftButtonDown+=(_,e)=>{dragging=true;map.CaptureMouse();Drag(e.GetPosition(map));e.Handled=true;};map.MouseMove+=(_,e)=>{if(dragging && e.LeftButton==MouseButtonState.Pressed)Drag(e.GetPosition(map));};map.MouseLeftButtonUp+=(_,_)=>{dragging=false;map.ReleaseMouseCapture();};map.LostMouseCapture+=(_,_)=>dragging=false;
        map.Cursor=Cursors.SizeAll;map.ToolTip="Drag the dock marker. Center guides snap within 1%. Values are Windows layout pixels (DIPs).";
        Content=p;Loaded+=(_,_)=>{app.SettingsChanged+=Sync;Sync();};Unloaded+=(_,_)=>app.SettingsChanged-=Sync;Sync();
    }
    private void Changed(){Sync();save();}
    private void Sync()
    {
        syncing=true;var s=app.Settings;var screen=Native.ChooseScreen(s);double scale=Native.Scale(screen),height=screen.WorkingArea.Height/scale;
        horizontal.Value=s.Horizontal;x.Text=s.Horizontal.ToString("0.0",CultureInfo.InvariantCulture);
        bool side=s.DockEdge is "Left" or "Right",bottom=s.DockEdge=="Bottom";vertical.Minimum=0;vertical.Maximum=side?100:bottom?200:Math.Min(3000,Math.Max(0,height-s.Height));vertical.TickFrequency=side?.1:1;vertical.IsSnapToTickEnabled=true;vertical.Value=side?s.DockVertical:bottom?s.DockInset:s.Top;y.Text=vertical.Value.ToString("0.#",CultureInfo.InvariantCulture);verticalLabel.Text=side?"Vertical · %":bottom?"Bottom inset · px":"Top offset · px";
        vertical.IsEnabled=!(s.Preset=="Mac" && s.DockEdge=="Top");
        double left=(screen.WorkingArea.Width/scale-s.Width)*s.Horizontal/100,top=side?(height-s.Height)*s.DockVertical/100:bottom?height-s.Height-s.DockInset:s.Top;
        if(side)left=s.DockEdge=="Left"?s.DockInset:screen.WorkingArea.Width/scale-s.Width-s.DockInset;
        double off=side?s.DockVertical-50:s.Horizontal-50;alignment.Text=Math.Abs(off)<.05?"Centered on this screen":$"{Math.Abs(off):0.#}% {(side?(off<0?"above":"below"):(off<0?"left of":"right of"))} center";alignment.Foreground=Math.Abs(off)<.05?UI.Brush("#A4CEC0"):UI.Muted;
        coordinates.Text=$"Left {left:0} px · Top {top:0} px · {scale*100:0}% display scaling";syncing=false;Draw();
    }
    private void Drag(Point point)
    {
        double w=Math.Max(1,map.ActualWidth-32),h=map.ActualHeight-24;var s=app.Settings;double px=Math.Clamp((point.X-45)/Math.Max(1,w-58)*100,0,100),py=Math.Clamp((point.Y-19.5)/Math.Max(1,h-15)*100,0,100);
        if(Math.Abs(px-50)<1)px=50;if(Math.Abs(py-50)<1)py=50;
        if(s.DockEdge is "Left" or "Right")s.DockVertical=py;else s.Horizontal=px;
        if(s.DockEdge=="Floating" || s.DockEdge=="Top" && s.Preset!="Mac")s.Top=Math.Round(py/100*vertical.Maximum);
        Changed();
    }
    private void Draw()
    {
        if(map.ActualWidth<=32)return;map.Children.Clear();double w=map.ActualWidth-32,h=map.Height-24;var s=app.Settings;
        void Line(double a,double b,double c,double d){map.Children.Add(new Line{X1=a,Y1=b,X2=c,Y2=d,Stroke=UI.Brush("#415064"),StrokeThickness=.7,StrokeDashArray=new DoubleCollection{3,4},IsHitTestVisible=false});}
        Line(16+w/2,8,16+w/2,h+16);Line(12,12+h/2,w+20,12+h/2);
        double dx=s.Horizontal/100,dy=s.DockEdge=="Bottom"?1:s.DockEdge is "Left" or "Right"?s.DockVertical/100:s.DockEdge=="Floating" || s.Preset!="Mac"?s.Top/Math.Max(1,vertical.Maximum):0;
        if(s.DockEdge=="Left")dx=0;if(s.DockEdge=="Right")dx=1;
        var marker=new Border{Width=58,Height=15,CornerRadius=new(6),Background=Brushes.Black,BorderBrush=UI.Brush("#CAD9EA"),BorderThickness=new(1),IsHitTestVisible=false};Canvas.SetLeft(marker,16+dx*(w-58));Canvas.SetTop(marker,12+dy*(h-15));map.Children.Add(marker);
    }
}
