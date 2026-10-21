using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private void DockPlacementSettings()
    {
        var s=app.Settings;var p=new StackPanel();
        var floating=new StackPanel{Visibility=s.DockEdge=="Floating"?Visibility.Visible:Visibility.Collapsed};
        AddSlider(floating,"Floating top offset",s.Top,0,3000,v=>s.Top=v,"px",1);
        p.Children.Add(PremiumChoice("Dock side",s.DockEdge,new[]{"Top","Bottom","Left","Right","Floating"},v=>{s.DockEdge=v;floating.Visibility=v=="Floating"?Visibility.Visible:Visibility.Collapsed;}));p.Children.Add(floating);
        AddSlider(p,"Vertical alignment",s.DockVertical,0,100,v=>s.DockVertical=v,"%",1);
        AddSlider(p,"Side / bottom inset",s.DockInset,0,200,v=>s.DockInset=v,"px",1);
        p.Children.Add(UI.Text("Left and Right use vertical alignment. Top and Bottom use horizontal position below. Floating uses horizontal position and its top offset. The top notch stays attached to the screen edge. Panels stay upright; side and bottom docks use the available work area.",11,UI.Muted));
        page.Children.Add(Section("Dock placement",p));
    }
}
