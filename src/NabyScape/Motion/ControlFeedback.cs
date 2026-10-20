using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace NabyScape;

internal static class ControlFeedback
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(ControlFeedback),new PropertyMetadata(false,Changed));
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    private static bool Active=>Motion.Enabled && Motion.Interaction!="Off";
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        if(e.NewValue is not true)return;
        if(d is Slider slider)
        {
            slider.Loaded+=(_,_)=>
            {
                if(slider.Template?.FindName("PART_Track",slider) is not Track {Thumb:{} thumb} || thumb.Tag is ScaleTransform)return;
                var scale=new ScaleTransform();thumb.Tag=scale;thumb.RenderTransformOrigin=new(.5,.5);thumb.RenderTransform=scale;
                void Size(double value){if(!Active)value=1;Motion.To(scale,ScaleTransform.ScaleXProperty,value,150);Motion.To(scale,ScaleTransform.ScaleYProperty,value,150);}
                thumb.MouseEnter+=(_,_)=>Size(1.16);thumb.MouseLeave+=(_,_)=>Size(thumb.IsDragging?1.23:1);thumb.DragStarted+=(_,_)=>Size(1.23);thumb.DragCompleted+=(_,_)=>Size(thumb.IsMouseOver?1.16:1);
            };
        }
        else if(d is CheckBox check)
        {
            void Pulse()
            {
                if(check.Template?.FindName("Knob",check) is FrameworkElement knob)
                {
                    if(!Active){knob.RenderTransform=Transform.Identity;return;}
                    double previous=(knob.RenderTransform as TranslateTransform)?.X??0;
                    var slide=new TranslateTransform((check.IsChecked==true?-12:12)+previous,0);knob.RenderTransform=slide;Motion.To(slide,TranslateTransform.XProperty,0,220);return;
                }
                if(!Active || check.Template?.FindName("Ring",check) is not FrameworkElement ring)return;var scale=new ScaleTransform(.84,.84);ring.RenderTransformOrigin=new(.5,.5);ring.RenderTransform=scale;Motion.To(scale,ScaleTransform.ScaleXProperty,1,220);Motion.To(scale,ScaleTransform.ScaleYProperty,1,220);
            }
            check.Checked+=(_,_)=>Pulse();check.Unchecked+=(_,_)=>Pulse();
        }
        else if(d is Expander expander)expander.Expanded+=(_,e)=>{if(Active && ReferenceEquals(e.OriginalSource,expander) && expander.Content is FrameworkElement content)Motion.Enter(content,3);};
    }
    internal static void Artwork(FrameworkElement image)
    {
        var scale=new ScaleTransform();image.RenderTransformOrigin=new(.5,.5);image.RenderTransform=scale;
        void Size(double value){if(!Active)value=1;Motion.To(scale,ScaleTransform.ScaleXProperty,value,280);Motion.To(scale,ScaleTransform.ScaleYProperty,value,280);}
        image.MouseEnter+=(_,_)=>Size(1.045);image.MouseLeave+=(_,_)=>Size(1);image.PreviewMouseLeftButtonDown+=(_,_)=>Size(.98);image.PreviewMouseLeftButtonUp+=(_,_)=>Size(image.IsMouseOver?1.045:1);
    }
    internal static void Notice(FrameworkElement card,string mode)
    {
        if(!Motion.Enabled){card.Opacity=1;return;}
        if(mode=="Lift")Motion.Enter(card,9);
        else if(mode=="Fade"){card.Opacity=0;Motion.Fade(card,1,260);}
        else {var scale=new ScaleTransform(.965,.965);card.RenderTransform=scale;card.RenderTransformOrigin=new(.5,0);card.Opacity=0;Motion.Fade(card,1,240);Motion.To(scale,ScaleTransform.ScaleXProperty,1,350);Motion.To(scale,ScaleTransform.ScaleYProperty,1,350);}
    }
}
