using System;
using System.IO;

namespace NabyScape;

internal static class RussianBuildProfile
{
    // One-time language transition. All other settings and every content document are preserved.
    internal static void Initialize(Store store,Settings settings)
    {
        string marker=Path.Combine(store.DirectoryPath,"russian-build-initialized.txt");
        if(File.Exists(marker))return;
        string original=Path.Combine(store.DirectoryPath,"settings.json");
        string backup=Path.Combine(store.DirectoryPath,"settings.before-russian-build.json");
        try
        {
            if(File.Exists(original) && !File.Exists(backup))File.Copy(original,backup,false);
            string previous=settings.UiLanguage;settings.UiLanguage="ru";
            try{store.Save("settings",settings);}catch{settings.UiLanguage=previous;throw;}
            File.WriteAllText(marker,"Russian interface initialized. Later language choices are preserved.");
        }
        catch(Exception ex){store.Log(ex);}
    }
}
