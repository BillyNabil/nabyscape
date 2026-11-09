using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

// Compatibility fixture host. Application navigation uses Settings > AI.
internal sealed class AssistantProvidersWindow : Window
{
    internal AssistantProvidersWindow(App app)
    {
        Title=Localization.Text("NabyScape · AI providers");AssistantWindowChrome.Place(this,app.Settings,600,740);
        var editor=new AssistantProviderSettings(app,false);
        Content=AssistantWindowChrome.Frame(this,Localization.Text("NabyScape · AI providers"),new ScrollViewer{Content=editor,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        Closed+=(_,_)=>editor.ClearSecrets();
    }
}
