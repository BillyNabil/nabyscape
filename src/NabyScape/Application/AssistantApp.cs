namespace NabyScape;

public sealed partial class App
{
    internal AssistantRuntime Assistant {get;private set;}=null!;
    private AssistantWindow? assistantWindow;
    internal void ShowAssistant()
    {
        if(overlay!=null){overlay.OpenAssistant();return;}
        ShowAssistantWindow();
    }
    internal void ShowAssistantWindow()
    {
        if(assistantWindow==null){assistantWindow=new(this);assistantWindow.Closed+=(_,_)=>assistantWindow=null;}
        assistantWindow.Show();if(assistantWindow.WindowState==System.Windows.WindowState.Minimized)assistantWindow.WindowState=System.Windows.WindowState.Normal;assistantWindow.Activate();
    }
    internal void ShowAssistantProviders()
    {
        ShowSettings();settingsWindow?.OpenAiSettings();
    }
    internal void HideAssistant(){Assistant?.StopSession();assistantWindow?.Hide();}
}
