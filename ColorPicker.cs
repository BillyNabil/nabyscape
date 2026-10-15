using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;

internal sealed class ColorPicker : StackPanel
{
    private Color color;
    private bool updating;
    private readonly ColorField field=new();
    private readonly Slider hue=new(){Minimum=0,Maximum=359.99,SmallChange=1,Height=24,Margin=new(0,8,0,8)};
    private readonly TextBox hex;
    private readonly TextBox[] rgb=new TextBox[3];
    private readonly Action<string> changed,committed;
    private readonly WrapPanel recent=new();
    private readonly string[] presets;
    private readonly Border sample=new(){Width=28,Height=28,CornerRadius=new(7),Margin=new(0,0,10,0)};
    internal string Value=>Format(color);

    internal ColorPicker(string name,string initial,string[] presets,System.Collections.Generic.IEnumerable<string> colors,Action<string> changed,Action<string> committed)
    {
        this.presets=presets;this.changed=changed;this.committed=committed;color=(Color)ColorConverter.ConvertFromString(initial);
        var top=new DockPanel();DockPanel.SetDock(sample,Dock.Left);top.Children.Add(sample);
        hex=UI.Input(name+" hex",initial,"Enter a six-digit color such as #182028");hex.Margin=new(0);hex.MaxLength=7;top.Children.Add(hex);
        hex.TextChanged+=(_,_)=>{if(updating)return;if(Settings.ValidColor(hex.Text)){Set((Color)ColorConverter.ConvertFromString(hex.Text));hex.BorderBrush=UI.Brush("#393C46");}else hex.BorderBrush=UI.Brush("#F08C85");};
        hex.LostKeyboardFocus+=(_,_)=>Commit();
        var numeric=new Expander{Header="Color values · HEX / RGB",Margin=new(0,8,0,10),Foreground=UI.Muted};
        var detail=new StackPanel{Margin=new(0,0,0,0)};field.Height=136;UI.Named(field,name+" saturation and brightness");field.ToolTip="Drag to choose saturation and brightness. Arrow keys adjust; Shift makes larger changes.";detail.Children.Add(field);
        var gradient=new LinearGradientBrush();foreach(var stop in new[]{(0d,Colors.Red),(.167,Colors.Yellow),(.333,Colors.Lime),(.5,Colors.Cyan),(.667,Colors.Blue),(.833,Colors.Magenta),(1d,Colors.Red)})gradient.GradientStops.Add(new(stop.Item2,stop.Item1));hue.Background=gradient;
        hue.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider"><Grid Height="24"><Border Height="12" CornerRadius="6" Background="{TemplateBinding Background}"/><Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Focusable="False" Opacity="0"/></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width="12" Height="20"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#1B1D22" BorderBrush="White" BorderThickness="2" CornerRadius="6"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Focusable="False" Opacity="0"/></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate>
        """);UI.Named(hue,name+" hue");detail.Children.Add(hue);
        hue.ValueChanged+=(_,_)=>{if(!updating){field.Hue=hue.Value;Set(FromHsv(field.Hue,field.Saturation,field.Value),false);}};
        hue.PreviewMouseLeftButtonUp+=(_,_)=>Commit();hue.LostKeyboardFocus+=(_,_)=>Commit();
        field.Changed+=()=>Set(FromHsv(field.Hue,field.Saturation,field.Value),false);field.Committed+=Commit;
        var channels=new Grid();for(int i=0;i<3;i++)channels.ColumnDefinitions.Add(new());
        for(int i=0;i<3;i++)
        {
            int channel=i;var group=new StackPanel{Margin=new(i==0?0:6,0,0,0)};group.Children.Add(UI.Text(new[]{"Red","Green","Blue"}[i],10,UI.Muted));var input=rgb[i]=UI.Input(name+" "+new[]{"red","green","blue"}[i],"");input.Margin=new(0,4,0,0);input.Padding=new(8,6,8,6);group.Children.Add(input);Grid.SetColumn(group,i);channels.Children.Add(group);
            void Apply(){if(updating)return;if(byte.TryParse(input.Text,out byte value)){Set(Color.FromRgb(channel==0?value:color.R,channel==1?value:color.G,channel==2?value:color.B));Commit();}else Sync();}
            input.LostKeyboardFocus+=(_,_)=>Apply();input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Apply();e.Handled=true;}};
        }
        Children.Add(detail);
        var values=new StackPanel{Margin=new(0,10,0,0)};values.Children.Add(top);channels.Margin=new(0,10,0,0);values.Children.Add(channels);numeric.Content=values;Children.Add(numeric);
        var palette=new WrapPanel();foreach(string value in presets)palette.Children.Add(Swatch(name,value));Children.Add(palette);
        recent.Margin=new(0,5,0,0);Children.Add(recent);SetRecent(name,colors);var initialHsv=ToHsv(color);field.Hue=initialHsv.hue;field.Saturation=initialHsv.saturation;field.Value=initialHsv.value;Sync();
    }
    private Button Swatch(string name,string value)
    {
        var button=UI.Button(name+" "+value,()=>{Set((Color)ColorConverter.ConvertFromString(value));Commit();});button.Content=null;button.Width=26;button.Height=26;button.MinHeight=26;button.Margin=new(0,0,7,5);button.Padding=new(0);button.Tag=new CornerRadius(13);button.Background=UI.Brush(value);button.BorderThickness=new(1);button.BorderBrush=UI.Brush("#747780");button.ToolTip=value;return button;
    }
    internal void SetRecent(string name,System.Collections.Generic.IEnumerable<string> values){recent.Children.Clear();foreach(var value in values.Where(Settings.ValidColor).Where(v=>!presets.Contains(v,StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8))recent.Children.Add(Swatch(name,value));}
    private void Set(Color next,bool updateHsv=true)
    {
        bool different=color!=next;color=next;
        if(updateHsv){var hsv=ToHsv(color);field.Hue=hsv.hue;field.Saturation=hsv.saturation;field.Value=hsv.value;}
        Sync();if(different)changed(Value);
    }
    private void Sync()
    {
        updating=true;try{sample.Background=new SolidColorBrush(color);if(!hex.IsKeyboardFocusWithin)hex.Text=Value;hue.Value=field.Hue;rgb[0].Text=color.R.ToString();rgb[1].Text=color.G.ToString();rgb[2].Text=color.B.ToString();field.InvalidateVisual();}finally{updating=false;}
    }
    private void Commit(){if(!Settings.ValidColor(hex.Text)){hex.Text=Value;hex.BorderBrush=UI.Brush("#393C46");}committed(Value);}
    internal static string Format(Color c)=>$"#{c.R:X2}{c.G:X2}{c.B:X2}";
    internal static (double hue,double saturation,double value) ToHsv(Color c)
    {
        double r=c.R/255d,g=c.G/255d,b=c.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;
        double h=d==0?0:max==r?60*((g-b)/d%6):max==g?60*((b-r)/d+2):60*((r-g)/d+4);return ((h+360)%360,max==0?0:d/max,max);
    }
    internal static Color FromHsv(double hue,double saturation,double value)
    {
        double h=(hue%360+360)%360,s=Math.Clamp(saturation,0,1),v=Math.Clamp(value,0,1),c=v*s,x=c*(1-Math.Abs(h/60%2-1)),m=v-c;
        var (r,g,b)=h<60?(c,x,0d):h<120?(x,c,0d):h<180?(0d,c,x):h<240?(0d,x,c):h<300?(x,0d,c):(c,0d,x);
        return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
    }
}

internal sealed class ColorField : FrameworkElement
{
    internal double Hue,Saturation,Value;
    internal event Action? Changed,Committed;
    internal ColorField()
    {
        Focusable=true;Cursor=Cursors.Cross;
        MouseLeftButtonDown+=(_,e)=>{Focus();CaptureMouse();Pick(e.GetPosition(this));e.Handled=true;};MouseMove+=(_,e)=>{if(IsMouseCaptured)Pick(e.GetPosition(this));};MouseLeftButtonUp+=(_,e)=>{if(IsMouseCaptured){Pick(e.GetPosition(this));ReleaseMouseCapture();Committed?.Invoke();e.Handled=true;}};
        KeyDown+=(_,e)=>{double step=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.05:.01;if(e.Key is Key.Left or Key.Right)Saturation=Math.Clamp(Saturation+(e.Key==Key.Right?step:-step),0,1);else if(e.Key is Key.Up or Key.Down)Value=Math.Clamp(Value+(e.Key==Key.Up?step:-step),0,1);else return;Changed?.Invoke();Committed?.Invoke();InvalidateVisual();e.Handled=true;};
    }
    private void Pick(Point p){Saturation=Math.Clamp(p.X/Math.Max(1,ActualWidth),0,1);Value=1-Math.Clamp(p.Y/Math.Max(1,ActualHeight),0,1);Changed?.Invoke();InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        var rect=new Rect(RenderSize);dc.DrawRoundedRectangle(new SolidColorBrush(ColorPicker.FromHsv(Hue,1,1)),null,rect,8,8);
        dc.PushClip(new RectangleGeometry(rect,8,8));dc.DrawRectangle(new LinearGradientBrush(Colors.White,Colors.Transparent,0),null,rect);dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent,Colors.Black,90),null,rect);dc.Pop();
        var point=new Point(Math.Clamp(Saturation*ActualWidth,6,Math.Max(6,ActualWidth-6)),Math.Clamp((1-Value)*ActualHeight,6,Math.Max(6,ActualHeight-6)));dc.DrawEllipse(null,new Pen(Brushes.Black,4),point,5,5);dc.DrawEllipse(null,new Pen(Brushes.White,2),point,5,5);
        if(IsKeyboardFocused)dc.DrawRoundedRectangle(null,new Pen(Brushes.White,1),new Rect(1,1,Math.Max(0,ActualWidth-2),Math.Max(0,ActualHeight-2)),8,8);
    }
}
