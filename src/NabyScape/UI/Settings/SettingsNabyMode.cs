using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

// Naby's chat and voice panels open inside the dock but size themselves (AssistantView.PanelSize),
// so most dock layout options never reach them. The Naby AI page gathers only the options that
// do; the widget panel options say where Naby differs.
public sealed partial class SettingsWindow
{
    private TextBlock? nabyPanelSize;
    private void NabyDockSettings()
    {
        var s=app.Settings;var sizes=new StackPanel();
        nabyPanelSize=UI.Text("",12);nabyPanelSize.Margin=new(0,0,0,8);RefreshNabyPanelSize();sizes.Children.Add(nabyPanelSize);
        AddSlider(sizes,"Content size",s.PanelScale*100,85,125,v=>{s.PanelScale=v/100;RefreshNabyPanelSize();},"%",1);
        AddSlider(sizes,"Content inset",s.PanelPadding,16,40,v=>{s.PanelPadding=v;RefreshNabyPanelSize();},"px");
        var links=UI.Row(UI.CaptionButton("Theme & colors",()=>Navigate("Appearance")),UI.CaptionButton("Animation speed",()=>Navigate("Motion")));links.Margin=new(0,6,0,4);
        page.Children.Add(Section("Naby in the dock",
            UI.Caption("Naby's chat and voice open inside the dock with their own size. Only these dock settings change them.",11,UI.Muted),
            sizes,links,
            UI.Caption("Theme, island colors and opacity, and animation speed apply too. Panel dots, widget order, the closed media display and hover sizes are for widgets only.",11,UI.Muted)));
    }
    private void RefreshNabyPanelSize()
    {
        if(nabyPanelSize==null)return;var chat=AssistantView.PanelSize(app.Settings);var voice=AssistantView.PanelSize(app.Settings,true);
        nabyPanelSize.Text=Localization.Format("Chat panel {0} × {1} px · Voice panel {2} × {3} px",chat.Width.ToString("0"),chat.Height.ToString("0"),voice.Width.ToString("0"),voice.Height.ToString("0"));
    }
    // Shown with the widget panel sizes, where Naby's minimum size would otherwise be a surprise.
    private UIElement NabyPanelNote()
    {
        var note=UI.Caption("Naby AI chat and voice keep their own panel size. Adjust them on the Naby AI page.",11,UI.Muted);
        note.Visibility=app.Assistant?.Options.Enabled==true?Visibility.Visible:Visibility.Collapsed;return note;
    }
}
