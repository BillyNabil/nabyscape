using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NabyScape;

// A separate process is necessary: automatic hiding deliberately excludes NabyScape's own windows.
internal static class WindowFixture
{
    internal static bool TryRun(string[] args)
    {
        int at=Array.IndexOf(args,"--window-fixture");if(at<0)return false;
        if(args.Length<at+6)return true;
        double[] values=new double[4];for(int i=0;i<4;i++)if(!double.TryParse(args[at+2+i],NumberStyles.Float,CultureInfo.InvariantCulture,out values[i]))return true;
        var window=new Window{Title="NabyScape diagnostic window fixture",ShowActivated=false,ShowInTaskbar=true,WindowStartupLocation=WindowStartupLocation.Manual,Left=values[0],Top=values[1],Width=values[2],Height=values[3],Background=Brushes.DimGray};
        if(args[at+1]=="fullscreen"){window.WindowStyle=WindowStyle.None;window.ResizeMode=ResizeMode.NoResize;}else window.Loaded+=(_,_)=>window.WindowState=WindowState.Maximized;
        var timeout=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(12)};timeout.Tick+=(_,_)=>window.Close();window.Loaded+=(_,_)=>timeout.Start();new Application().Run(window);return true;
    }
}
