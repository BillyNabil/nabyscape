using System;
using System.IO;

namespace NabyScape;

public sealed class WelcomeState
{
    public bool Pending {get;set;}
    public bool Completed {get;set;}
}

internal static class WelcomeSetup
{
    internal static bool ShouldOffer(Store store,bool existingProfile)
    {
        bool hasState=File.Exists(Path.Combine(store.DirectoryPath,"welcome.json"));
        var state=store.Load<WelcomeState>("welcome");
        if(state.Completed)return false;
        if(state.Pending)return true;
        if(hasState || existingProfile)return false;
        store.Save("welcome",new WelcomeState{Pending=true});
        return true;
    }

    internal static void Complete(Store store)=>store.Save("welcome",new WelcomeState{Completed=true});
}

public sealed partial class App
{
    private WelcomeWindow? welcomeWindow;
    internal void ShowWelcome()
    {
        if(welcomeWindow==null)
        {
            welcomeWindow=new WelcomeWindow(this);
            welcomeWindow.Closed+=(_,_)=>welcomeWindow=null;
        }
        welcomeWindow.Show();welcomeWindow.Activate();
    }
}
