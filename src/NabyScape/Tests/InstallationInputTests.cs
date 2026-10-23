using System;
using System.IO;
using System.Text.Json;

namespace NabyScape;

internal static class InstallationInputTests
{
    internal static void Run(Action<string,Action> test)
    {
        static void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        static void Reject(Action action){try{action();}catch(IOException){return;}catch(InvalidDataException){return;}throw new Exception("Unsafe input accepted");}
        test("Installer rollback copies the verified legacy binary in place without accepting corrupt bytes",()=>
        {
            string root=Path.Combine(Path.GetTempPath(),"NabyScape-restore-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string source=Path.Combine(root,"previous.exe"),target=Path.Combine(root,"NabyScape.exe");File.WriteAllText(source,"saved executable");File.WriteAllText(target,"current executable");
                Reject(()=>UpdateRecovery.RestoreLegacyBinary(source,target,new string('A',64)));
                Check(File.ReadAllText(target)=="current executable" && Directory.GetFiles(root).Length==2);
                string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
                UpdateRecovery.RestoreLegacyBinary(source,target,hash);Check(File.ReadAllText(target)=="saved executable" && Directory.GetFiles(root).Length==2);
            }
            finally{foreach(string file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);}
        });
        test("Panel navigation migrates every legacy style and imported layout to dots",()=>
        {
            foreach(string style in new[]{"Icons","IconsLabels","Text","Dots","invalid"})
            {
                var settings=new Settings{CategoryStyle=style,LaunchStyle="Icons"};settings.Normalize();Check(settings.CategoryStyle=="Dots" && settings.LaunchStyle=="Icons");
                var imported=new Settings();CustomizationProfile.Import(JsonSerializer.Serialize(new{format="luma.layout.v1",values=new{CategoryStyle=style}}),imported);Check(imported.CategoryStyle=="Dots");
            }
            Check(new Settings().CategoryStyle=="Dots" && FactoryDefaults.Create().CategoryStyle=="Dots");
        });
        test("Microphone selection survives device reordering and rejects missing or ambiguous inputs",()=>
        {
            var devices=new[]{new AssistantMicrophone("","Default",-1),new AssistantMicrophone("usb-a","Desk microphone",3),new AssistantMicrophone("usb-b","Headset",0)};
            Check(AssistantMicrophones.Resolve("",devices)==-1 && AssistantMicrophones.Resolve("usb-a",devices)==3);
            Reject(()=>AssistantMicrophones.Resolve("unplugged",devices));
            Reject(()=>AssistantMicrophones.Resolve("usb-a",new[]{devices[1],devices[1] with{Number=1}}));
            var saved=JsonSerializer.Deserialize<AssistantOptions>(JsonSerializer.Serialize(new AssistantOptions{MicrophoneId="usb-a"}))!;
            Check(saved.MicrophoneId=="usb-a" && new AssistantOptions().MicrophoneId=="");
        });
        test("Installer accepts dedicated custom folders and rejects root data relative and network paths",()=>
        {
            string custom=Path.Combine(Path.GetTempPath(),"NabyScape-install-fixture","NabyScape");
            Check(Path.GetFileName(InstallPaths.Default)=="NabyScape" && InstallPaths.Validate(custom)==custom);
            Check(InstallPaths.FromArguments(new[]{"--silent","--install-dir",custom},InstallPaths.Default)==custom);
            Check(InstallPaths.FromArguments(Array.Empty<string>(),InstallPaths.Legacy)==InstallPaths.Legacy);
            foreach(string path in new[]{@"C:\",@"C:\Users",@"NabyScape",@"\\server\Apps\NabyScape",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NabyScape")})Reject(()=>InstallPaths.Validate(path));
            Reject(()=>InstallPaths.FromArguments(new[]{"--install-dir"},custom));
            Reject(()=>InstallPaths.FromArguments(new[]{"--install-dir",custom,"--install-dir",custom},custom));
        });
    }
}
