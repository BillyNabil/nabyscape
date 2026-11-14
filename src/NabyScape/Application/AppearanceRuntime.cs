using System.Windows;

namespace NabyScape;

public sealed partial class App
{
    private bool appearanceInitialized,applyingAppearance;
    // Control tokens, light/dark ink scopes and island colors follow the chosen theme
    // ("Follow Windows" also follows the Windows app mode while NabyScape runs).
    private void ApplyAppearance()
    {
        if(!appearanceInitialized){appearanceInitialized=true;ThemeInk.Register();ThemeInk.Changed+=AppearanceChanged;}
        applyingAppearance=true;
        try{ThemeInk.Configure(Settings);}finally{applyingAppearance=false;}
        UI.ApplyControlTheme(Settings);
    }
    private void AppearanceChanged()
    {
        UI.ApplyControlTheme(Settings);
        foreach(Window window in Windows)ThemeInk.Refresh(window);
        // A Windows light/dark switch arrives outside ChangeSettings; surfaces re-resolve their colors.
        if(!applyingAppearance){SettingsChanged?.Invoke();PublishUpdate(RuntimeChange.Settings);}
    }
}
