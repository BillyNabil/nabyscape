namespace NabyScape;

public sealed partial class App
{
    internal AssistantRuntime Assistant {get;private set;}=null!;
    private AssistantWindow? assistantWindow;
    private AssistantProvidersWindow? assistantProviders;
    internal void ShowAssistant()
    {
        if(assistantWindow==null){assistantWindow=new(this);assistantWindow.Closed+=(_,_)=>assistantWindow=null;}
        assistantWindow.Show();if(assistantWindow.WindowState==System.Windows.WindowState.Minimized)assistantWindow.WindowState=System.Windows.WindowState.Normal;assistantWindow.Activate();
    }
    internal void ShowAssistantProviders()
    {
        if(assistantProviders==null){assistantProviders=new(this);assistantProviders.Closed+=(_,_)=>assistantProviders=null;}
        assistantProviders.Show();if(assistantProviders.WindowState==System.Windows.WindowState.Minimized)assistantProviders.WindowState=System.Windows.WindowState.Normal;assistantProviders.Activate();
    }
    internal void HideAssistant(){Assistant?.StopSession();assistantWindow?.Hide();assistantProviders?.Hide();}
}
