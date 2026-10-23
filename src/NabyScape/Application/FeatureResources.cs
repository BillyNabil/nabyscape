using System.Linq;
namespace NabyScape;
public partial class App
{
    private bool MediaFeaturesWanted=>Settings.MediaIntegrationEnabled &&
        (Settings.Widgets.Any(w=>w.Enabled && w.Id=="media") || HoverPagePolicy.Pages(Settings).Contains("media"));
}
