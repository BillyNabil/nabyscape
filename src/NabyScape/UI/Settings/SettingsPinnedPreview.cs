using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;

public sealed partial class SettingsWindow
{
    private Border? pinnedAmbientHost;
    private SettingsScene? pinnedAmbientScene;
    private Viewbox? pinnedAmbientView;
    private void InitializePinnedAmbientPreview(Grid form,ScrollViewer scroll)
    {
        pinnedAmbientScene=new SettingsScene("Ambient light",app.Settings){Width=420,Height=126};
        pinnedAmbientView=new Viewbox{Child=pinnedAmbientScene,Stretch=System.Windows.Media.Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Height=112};
        pinnedAmbientHost=new Border{Child=pinnedAmbientView,Visibility=Visibility.Collapsed,Background=UI.Brush("#191C20"),BorderBrush=UI.Brush("#30343B"),BorderThickness=new(.7),CornerRadius=new(10),Padding=new(6),MaxWidth=660,ToolTip="Ambient preview stays visible while you adjust this section."};
        UI.Named(pinnedAmbientHost,"Pinned ambient preview");form.Children.Add(pinnedAmbientHost);Grid.SetRow(pinnedAmbientHost,0);
        scroll.ScrollChanged+=(_,e)=>{if(ReferenceEquals(e.Source,scroll))UpdatePinnedAmbientPreview(form);};
        form.SizeChanged+=(_,_)=>UpdatePinnedAmbientPreview(form);
        page.SizeChanged+=(_,_)=>UpdatePinnedAmbientPreview(form);
    }
    private void HidePinnedAmbientPreview()
    {if(pinnedAmbientHost!=null)pinnedAmbientHost.Visibility=Visibility.Collapsed;}
    private void UpdatePinnedAmbientPreview(Grid form)
    {
        if(pinnedAmbientHost==null || pinnedAmbientView==null)return;
        var section=page.Children.OfType<Border>().FirstOrDefault(b=>b.Tag as string=="Ambient light");
        var original=settingExamples.FirstOrDefault(scene=>scene.Section=="Ambient light");
        if(current is not ("Appearance" or "Ambience") || section==null || original==null || !original.IsDescendantOf(settingsScroll) || original.ActualHeight<=0 || settingsScroll.ViewportHeight<=0){HidePinnedAmbientPreview();return;}
        // Compare in viewport coordinates. The pinned copy never reparents an editor,
        // changes the form's content height, or scrolls the user's focused control.
        double previewBottom=original.TransformToAncestor(settingsScroll).Transform(new Point(0,original.ActualHeight)).Y;
        double sectionBottom=section.TransformToAncestor(settingsScroll).Transform(new Point(0,section.ActualHeight)).Y;
        bool pin=previewBottom<=2 && (current=="Ambience" || sectionBottom>16);
        pinnedAmbientView.Height=Math.Clamp(form.ActualHeight*.22,42,112);
        pinnedAmbientHost.Margin=new(page.Margin.Left,4,page.Margin.Right+8,8);
        var visibility=pin?Visibility.Visible:Visibility.Collapsed;
        if(pinnedAmbientHost.Visibility!=visibility){pinnedAmbientHost.Visibility=visibility;if(pin)pinnedAmbientScene?.InvalidateVisual();}
    }
}
