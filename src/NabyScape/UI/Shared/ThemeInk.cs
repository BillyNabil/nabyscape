using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace NabyScape;

// Where an element sits: on a dark surface (the original design), on a light surface, or
// "Fixed" for content whose colors are data (swatches, artwork, the user's own colors).
internal enum Tone { Auto, Dark, Light, Fixed }
// Custom-drawn elements that pick colors from ThemeInk.LightAt while rendering.
internal interface IInkAware {}

// Light/dark appearance. Most of the app was drawn for dark surfaces with locally set brushes,
// so instead of re-coloring hundreds of call sites, a light tone re-maps those dark-design brushes
// when an element loads (and again when the tone changes): pale ink becomes dark ink, dark fills
// become light fills and saturated colors keep their hue. Control templates use the resource
// tokens from UI.ApplyControlTheme. Dark is the original look and is never re-mapped.
internal static class ThemeInk
{
    // Tone inherits down the tree. Every window carries the theme's tone, so an element attached to a
    // window (or to a surface with its own tone) sees its effective tone change and re-maps itself.
    // WPF raises Loaded only in subtrees that have instance Loaded handlers, so Loaded alone misses
    // most dock content; the inherited-tone callback reaches every element.
    internal static readonly DependencyProperty ToneProperty=DependencyProperty.RegisterAttached("Tone",typeof(Tone),typeof(ThemeInk),new FrameworkPropertyMetadata(Tone.Auto,FrameworkPropertyMetadataOptions.Inherits,ToneChanged));
    private static void ToneChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        if(d is not FrameworkElement element)return;
        Apply(element);if(element is IInkAware)element.InvalidateVisual();
    }
    private static Tone WindowTone=>IsLight?Tone.Light:Tone.Dark;
    private static void StampWindows(){if(Application.Current==null)return;foreach(Window window in Application.Current.Windows)SetTone(window,WindowTone);}
    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("InkState",typeof(Dictionary<DependencyProperty,(Brush Original,Brush Applied)>),typeof(ThemeInk),new PropertyMetadata(null));
    internal static Tone GetTone(DependencyObject d)=>(Tone)d.GetValue(ToneProperty);
    internal static void SetTone(DependencyObject d,Tone value){if(GetTone(d)!=value || d.ReadLocalValue(ToneProperty)==DependencyProperty.UnsetValue)d.SetValue(ToneProperty,value);}

    internal static readonly Color LightBase=Color.FromRgb(0xF5,0xF5,0xF7);
    internal static string Choice {get;private set;}="Dark";
    internal static bool IsLight {get;private set;}
    internal static event Action? Changed;
    private static bool? systemLight;
    private static bool registered,watching;
    internal static bool SystemLight=>systemLight??=ReadSystemLight();

    internal static bool Resolve(string choice,bool systemPrefersLight)=>choice=="Light" || choice=="System" && systemPrefersLight;
    // Windows exposes the app mode (not the taskbar mode) as AppsUseLightTheme; missing means dark.
    internal static bool ReadSystemLight()
    {
        try{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");return key?.GetValue("AppsUseLightTheme") is int value && value!=0;}
        catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}catch(System.IO.IOException){}
        return false;
    }
    internal static void Configure(Settings settings)
    {
        Choice=settings.ColorTheme;
        if(Application.Current is App && !watching){watching=true;SystemEvents.UserPreferenceChanged+=SystemPreferenceChanged;}
        Recompute();
    }
    private static void SystemPreferenceChanged(object? sender,UserPreferenceChangedEventArgs e)
    {
        if(e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))return;
        Application.Current?.Dispatcher.BeginInvoke(()=>{bool now=ReadSystemLight();if(systemLight==now)return;systemLight=now;Recompute();});
    }
    private static void Recompute()
    {
        bool light=!Accessibility.HighContrast && Resolve(Choice,Choice=="System" && SystemLight);
        if(light==IsLight)return;IsLight=light;StampWindows();Changed?.Invoke();
    }
    internal static void Register()
    {
        if(registered)return;registered=true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is FrameworkElement element)Apply(element);}),true);
        // A window's first layout always raises SizeChanged, unlike Loaded; new windows take the tone there.
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.SizeChangedEvent,new SizeChangedEventHandler((sender,_)=>{if(sender is Window window)SetTone(window,WindowTone);}),true);
        StampWindows();
    }

    // Re-applies the current tone below root, restoring originals when it returns to dark.
    internal static void Refresh(DependencyObject? root)
    {
        if(root==null)return;
        var pending=new Stack<DependencyObject>();pending.Push(root);var seen=new HashSet<DependencyObject>();
        while(pending.Count>0)
        {
            var node=pending.Pop();if(!seen.Add(node))continue;
            if(node is FrameworkElement element){Apply(element);if(element is IInkAware)element.InvalidateVisual();}
            if(node is Visual || node is System.Windows.Media.Media3D.Visual3D){int count=VisualTreeHelper.GetChildrenCount(node);for(int i=0;i<count;i++)pending.Push(VisualTreeHelper.GetChild(node,i));}
            // Collapsed or not-yet-templated content and popups are only in the logical tree.
            foreach(var child in LogicalTreeHelper.GetChildren(node))if(child is DependencyObject logical)pending.Push(logical);
            if(node is Popup {Child:{} popup})pending.Push(popup);
        }
    }
    // Call after code changes a brush on an already loaded element.
    internal static void Reapply(FrameworkElement element)=>Apply(element);
    internal static bool LightAt(DependencyObject d)=>!Accessibility.HighContrast && GetTone(d) switch{Tone.Light=>true,Tone.Auto=>IsLight,_=>false};
    private static void Apply(FrameworkElement e)
    {
        if(Accessibility.HighContrast)return;
        var tone=GetTone(e);if(tone==Tone.Fixed)return;
        bool light=tone==Tone.Light || tone==Tone.Auto && IsLight;
        if(!light && e.ReadLocalValue(StateProperty)==DependencyProperty.UnsetValue)return;
        // Template parts follow their control's resources and triggers.
        if(e.TemplatedParent!=null)return;
        switch(e)
        {
            case TextBlock t:Map(t,TextBlock.ForegroundProperty,Role.Ink,light);Map(t,TextBlock.BackgroundProperty,Role.Surface,light);break;
            case Control c:Map(c,Control.ForegroundProperty,Role.Ink,light);Map(c,Control.BackgroundProperty,Role.Surface,light);Map(c,Control.BorderBrushProperty,Role.Surface,light);break;
            case Border b:Map(b,Border.BackgroundProperty,Role.Surface,light);Map(b,Border.BorderBrushProperty,Role.Surface,light);break;
            case Panel p:Map(p,Panel.BackgroundProperty,Role.Surface,light);break;
            case Shape s:Map(s,Shape.FillProperty,Role.Auto,light);Map(s,Shape.StrokeProperty,Role.Ink,light);break;
        }
    }
    internal enum Role { Ink, Surface, Auto }
    private static void Map(FrameworkElement e,DependencyProperty property,Role role,bool light)
    {
        var source=DependencyPropertyHelper.GetValueSource(e,property);
        if(source.BaseValueSource!=BaseValueSource.Local || source.IsExpression || source.IsAnimated)return;
        if(e.GetValue(property) is not Brush current)return;
        var state=e.GetValue(StateProperty) as Dictionary<DependencyProperty,(Brush Original,Brush Applied)>;
        Brush original=state!=null && state.TryGetValue(property,out var entry) && ReferenceEquals(entry.Applied,current)?entry.Original:current;
        Brush target=light?Remap(original,role):original;
        if(!ReferenceEquals(target,current))e.SetCurrentValue(property,target);
        if(ReferenceEquals(target,original)){state?.Remove(property);return;}
        if(state==null){state=new();e.SetValue(StateProperty,state);}
        state[property]=(original,target);
    }

    private static readonly Dictionary<(Color,Role),Brush> remapped=new();
    internal static Brush Remap(Brush brush,Role role)
    {
        // Live (unfrozen) brushes are theme-managed by their owner, such as the Settings palette.
        if(brush is not SolidColorBrush solid || !brush.IsFrozen || brush.Opacity!=1)return brush;
        var key=(solid.Color,role);
        lock(remapped)
        {
            if(remapped.TryGetValue(key,out var cached))return cached;
            var color=role switch{Role.Ink=>OnLight(solid.Color),Role.Surface=>SurfaceOnLight(solid.Color),_=>Luminance(solid.Color)>.2 && solid.Color.A>=0x80?OnLight(solid.Color):SurfaceOnLight(solid.Color)};
            Brush result=color==solid.Color?brush:Frozen(color);
            if(remapped.Count>512)remapped.Clear();remapped[key]=result;return result;
        }
    }
    private static Brush Frozen(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}

    // Hand-tuned counterparts for the most common dark-design text colors.
    private static readonly Dictionary<Color,Color> DesignedInk=new(){[Color.FromRgb(0xF2,0xF3,0xF5)]=Color.FromRgb(0x1B,0x1D,0x22),[Color.FromRgb(0xA6,0xAB,0xB4)]=Color.FromRgb(0x5B,0x61,0x6B),[Colors.White]=Color.FromRgb(0x1D,0x1D,0x1F)};
    // Ink drawn for a dark surface → ink readable on a light surface (WCAG AA against LightBase).
    internal static Color OnLight(Color c)
    {
        if(c.A==0 || Luminance(c)<.2)return c;
        if(DesignedInk.TryGetValue(Opaque(c),out var designed))return Color.FromArgb(c.A,designed.R,designed.G,designed.B);
        var (h,s,l)=ToHsl(c);l=1-l;s=Math.Min(s,.85);
        var result=FromHsl(h,s,l,c.A);
        while(Contrast(Opaque(result),LightBase)<4.5 && l>0){l=Math.Max(0,l-.02);result=FromHsl(h,s,l,c.A);}
        return result;
    }
    // Fill drawn for a dark surface → its light counterpart. White washes become soft shade.
    internal static Color SurfaceOnLight(Color c)
    {
        if(c.A==0)return c;
        double lum=Luminance(c);
        if(c.A<0xA0 && lum>.5)return Color.FromArgb(c.A,0,0,0);
        if(lum>=.12)return c;
        var (h,s,l)=ToHsl(c);return FromHsl(h,Math.Min(s,.6),1-l,c.A);
    }
    private static Color Opaque(Color c)=>Color.FromRgb(c.R,c.G,c.B);
    internal static double Luminance(Color c)
    {
        static double Channel(byte v){double x=v/255d;return x<=.03928?x/12.92:Math.Pow((x+.055)/1.055,2.4);}
        return .2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B);
    }
    internal static double Contrast(Color a,Color b){double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
    // Same rule as UI.ContrastText: surfaces above it carry dark ink.
    internal static bool IsLightSurface(Color c)=>c.R*.2126+c.G*.7152+c.B*.0722>150;
    internal static bool IsLightSurface(string hex)=>Settings.ValidColor(hex) && IsLightSurface(((SolidColorBrush)UI.Brush(hex)).Color);
    private static (double H,double S,double L) ToHsl(Color c)
    {
        double r=c.R/255d,g=c.G/255d,b=c.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),l=(max+min)/2,d=max-min;
        if(d<1e-9)return(0,0,l);
        double s=l>.5?d/(2-max-min):d/(max+min);
        double h=max==r?(g-b)/d+(g<b?6:0):max==g?(b-r)/d+2:(r-g)/d+4;
        return(h/6,s,l);
    }
    private static Color FromHsl(double h,double s,double l,byte a)
    {
        l=Math.Clamp(l,0,1);s=Math.Clamp(s,0,1);
        if(s<1e-9){byte v=(byte)Math.Round(l*255);return Color.FromArgb(a,v,v,v);}
        double q=l<.5?l*(1+s):l+s-l*s,p=2*l-q;
        static double Hue(double p,double q,double t){if(t<0)t+=1;if(t>1)t-=1;return t<1/6d?p+(q-p)*6*t:t<.5?q:t<2/3d?p+(q-p)*(2/3d-t)*6:p;}
        return Color.FromArgb(a,(byte)Math.Round(Hue(p,q,h+1/3d)*255),(byte)Math.Round(Hue(p,q,h)*255),(byte)Math.Round(Hue(p,q,h-1/3d)*255));
    }
}
