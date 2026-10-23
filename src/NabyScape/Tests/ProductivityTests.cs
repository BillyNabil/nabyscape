using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

/// <summary>All checks use isolated temporary storage and disable all native clipboard access.</summary>
public static class ProductivityTests
{
    public static void Run(Action<string,bool> report)
    {
        void Check(string name,Action<string> check)
        {
            var root=Path.Combine(Path.GetTempPath(),"NabyScape-productivity-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            bool passed=false;
            try { check(root); passed=true; }
            catch { passed=false; }
            finally
            {
                try { Directory.Delete(root,true); }
                catch { passed=false; }
            }
            report(name,passed);
        }
        static void Assert(bool condition) { if(!condition)throw new InvalidOperationException("Productivity assertion failed."); }
        static ProductivityService Service(string path) => new(new Store(path),_=>{},false);
        static byte[] Png()
        {
            var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{22,33,44,255},4);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=new MemoryStream(); encoder.Save(stream); return stream.ToArray();
        }

        Check("Productivity: clipboard is disabled by default and ingest cannot read it",root=>
        {
            using var service=Service(root);
            Assert(!service.ClipboardEnabled && !service.IngestText("private") && !service.IngestImage(Png()) && service.Clips.Count==0);
            service.CopyClip(new(){Text="ignored test copy"}); // Native seam also blocks all writes.
        });
        Check("Productivity: notes, tasks and completion survive restart",root=>
        {
            using(var service=Service(root))
            {
                service.Note=new string('n',51000); Assert(service.Note.Length==50000);
                Assert(!service.AddTask(" ") && service.AddTask("  tugas  "));
                service.Tasks[0].Done=true; service.Save();
            }
            using var restored=Service(root);
            Assert(restored.Note.Length==50000 && restored.Tasks.Single().Text=="tugas" && restored.Tasks[0].Done);
            restored.RemoveTask(restored.Tasks[0].Id); Assert(restored.Tasks.Count==0);
        });
        Check("Productivity: tasks are capped at 100 and text at 20000",root=>
        {
            using var service=Service(root);
            Assert(service.AddTask(new string('t',21000)) && service.Tasks[0].Text.Length==20000);
            for(int i=1;i<100;i++)Assert(service.AddTask("Task "+i));
            Assert(!service.AddTask("overflow") && service.Tasks.Count==100);
        });
        Check("Productivity: shelf handles missing paths, deduplication and 40-file cap",root=>
        {
            using var service=Service(root);
            var path=Path.Combine(root,"missing.txt");
            Assert(service.AddFiles(new[]{path,path.ToUpperInvariant(),"\0"})==1);
            Assert(!service.Shelf[0].Exists && service.Shelf[0].Name=="missing.txt");
            File.WriteAllText(path,"file"); Assert(service.Shelf[0].Exists);
            service.AddFiles(Enumerable.Range(0,50).Select(i=>Path.Combine(root,"file"+i)));
            Assert(service.Shelf.Count==40);
            service.RemoveFile(path); Assert(service.Shelf.Count==39 && File.Exists(path));
        });
        Check("Productivity: shelf survives restart without deleting broken references",root=>
        {
            var path=Path.Combine(root,"gone.txt");
            using(var service=Service(root))Assert(service.AddFiles(new[]{path})==1);
            using var restored=Service(root); Assert(restored.Shelf.Single().Path==path && !restored.Shelf[0].Exists);
        });
        Check("Productivity: shelf deduplicates trailing directory separators and preserves filesystem roots",root=>
        {
            var directory=Path.Combine(root,"folder");Directory.CreateDirectory(directory);
            var filesystemRoot=Path.GetPathRoot(Path.GetFullPath(directory))!;
            using(var service=Service(root))
            {
                Assert(service.AddFiles(new[]{directory,directory+Path.DirectorySeparatorChar,directory+Path.AltDirectorySeparatorChar})==1);
                Assert(service.Shelf.Single().Path==directory && service.Shelf[0].Exists);
                Assert(service.AddFiles(new[]{filesystemRoot})==1 && service.Shelf[1].Path==filesystemRoot);
            }
            using var restored=Service(root);
            Assert(restored.Shelf.Count==2 && restored.Shelf[0].Path==directory && restored.Shelf[1].Path==filesystemRoot);
            restored.RemoveFile(directory+Path.DirectorySeparatorChar);
            Assert(restored.Shelf.Count==1 && Directory.Exists(directory));
        });
        Check("Productivity: clipboard deduplication, empty exclusion and text cap",root=>
        {
            using var service=Service(root); service.SetClipboardEnabled(true);
            Assert(!service.IngestText(null) && !service.IngestText("\r\n "));
            Assert(service.IngestText("first") && service.IngestText("second") && !service.IngestText("first"));
            Assert(service.Clips.Count==2 && service.Clips[0].Text=="first");
            Assert(service.IngestText(new string('a',21000)) && service.Clips[0].Text.Length==20000);
        });
        Check("Productivity: pause stops text and image capture",root=>
        {
            using var service=Service(root); service.SetClipboardEnabled(true); service.ClipboardPaused=true;
            Assert(!service.IngestText("paused") && !service.IngestImage(Png()) && service.Clips.Count==0);
            service.ClipboardPaused=false; Assert(service.IngestText("resumed"));
        });
        Check("Productivity: separate pinned and unpinned limits retain newest entries",root=>
        {
            using var service=Service(root); service.SetClipboardEnabled(true);
            for(int i=0;i<20;i++) { service.IngestText("pin"+i); service.ToggleClipPin(service.Clips[0].Id); }
            for(int i=0;i<40;i++)service.IngestText("item"+i);
            Assert(service.Clips.Count==50 && service.Clips.Count(c=>c.Pinned)==20 && service.Clips.Count(c=>!c.Pinned)==30);
            Assert(!service.Clips.Any(c=>c.Text=="item0") && service.Clips.Any(c=>c.Text=="pin0"));
            service.ToggleClipPin(service.Clips.First(c=>!c.Pinned).Id); Assert(service.Clips.Count(c=>c.Pinned)==20);
            service.ToggleClipPin(service.Clips.First(c=>c.Pinned).Id);
            Assert(service.Clips.Count==49 && service.Clips.Count(c=>!c.Pinned)==30);
        });
        Check("Productivity: image deduplication, removal and 10 MB cap",root=>
        {
            using var service=Service(root); service.SetClipboardEnabled(true);
            var png=Png(); Assert(service.IngestImage(png) && !service.IngestImage(png) && service.Clips.Count==1);
            Assert(!service.IngestImage(new byte[10*1024*1024+1]) && !service.IngestImage(new byte[]{1,2,3}));
            var image=service.Clips[0]; Assert(File.Exists(image.ImagePath));
            service.RemoveClip(image.Id); Assert(service.Clips.Count==0 && !File.Exists(image.ImagePath));
        });
        Check("Productivity: clear purges pinned images and leaves source files alone",root=>
        {
            using var service=Service(root); service.SetClipboardEnabled(true);
            var source=Path.Combine(root,"source.png"); File.WriteAllBytes(source,Png());
            service.IngestImage(File.ReadAllBytes(source)); var image=service.Clips[0]; service.ToggleClipPin(image.Id);
            service.IngestText("clear"); service.ClearClips();
            Assert(service.Clips.Count==0 && !File.Exists(image.ImagePath) && File.Exists(source));
        });
        Check("Productivity: evicting an unpinned image removes its cached PNG",root=>
        {
            using var service=Service(root); service.SetClipboardEnabled(true); service.IngestImage(Png());
            var image=service.Clips[0];
            for(int i=0;i<30;i++)service.IngestText("newer "+i);
            Assert(service.Clips.Count==30 && !service.Clips.Any(c=>c.Id==image.Id) && !File.Exists(image.ImagePath));
        });
        Check("Productivity: disabling purges history and cache without altering notes",root=>
        {
            using(var service=Service(root))
            {
                service.Note="keep"; service.SetClipboardEnabled(true); service.IngestImage(Png()); service.IngestText("secret");
                service.ClipboardPaused=true; service.SetClipboardEnabled(false);
                Assert(!service.ClipboardEnabled && !service.ClipboardPaused && service.Clips.Count==0 && !service.IngestText("ignored"));
            }
            using var restored=Service(root);
            Assert(restored.Note=="keep" && restored.Clips.Count==0 && Directory.GetFiles(Path.Combine(root,"clipboard-images")).Length==0);
        });
        Check("Productivity: restart loads history without enabling capture; disabled cleanup still works",root=>
        {
            using(var service=Service(root)) { service.SetClipboardEnabled(true); service.IngestText("persist"); service.ToggleClipPin(service.Clips[0].Id); service.IngestImage(Png()); }
            using var restored=Service(root);
            Assert(!restored.ClipboardEnabled && restored.Clips.Count==2 && restored.Clips.Count(c=>c.Pinned)==1);
            Assert(!restored.IngestText("no capture")); restored.SetClipboardEnabled(false);
            Assert(restored.Clips.Count==0 && Directory.GetFiles(Path.Combine(root,"clipboard-images")).Length==0);
        });
        Check("Productivity: malformed JSON recovers through Store with a backup",root=>
        {
            File.WriteAllText(Path.Combine(root,"productivity.json"),"{bad json");
            var store=new Store(root); using var service=new ProductivityService(store,_=>{},false);
            Assert(service.Note=="" && service.Tasks.Count==0 && store.Warning!=null && Directory.GetFiles(root,"productivity.json.backup-*").Length==1);
        });
        Check("Productivity: corrupt collections normalize and cannot delete outside cache",root=>
        {
            var outside=Path.Combine(root,"outside.png"); File.WriteAllBytes(outside,Png());
            var store=new Store(root);
            store.Save("productivity",new ProductivityData
            {
                Note=null!, Tasks=new(){null!,new(){Id="same",Text="first"},new(){Id="same",Text="second"},new(){Text=""}},
                Shelf=new(){null!,new(){Path="\0"},new(){Path=outside},new(){Path=outside}},
                Clips=new(){null!,new(){Kind="image",ImagePath=outside},new(){Kind="invalid"},new(){Text="okay"},new(){Text="okay"}}
            });
            using var service=Service(root);
            Assert(service.Note=="" && service.Tasks.Count==2 && service.Tasks.Select(t=>t.Id).Distinct().Count()==2 && service.Shelf.Count==1 && service.Clips.Count==1);
            service.Clips.Add(new(){Kind="image",ImagePath=outside}); service.ClearClips(); Assert(File.Exists(outside));
        });
        Check("Productivity: orphan cache files are purged on restart",root=>
        {
            var cache=Path.Combine(root,"clipboard-images"); Directory.CreateDirectory(cache);
            var orphan=Path.Combine(cache,Guid.NewGuid().ToString("N")+".png"); File.WriteAllBytes(orphan,Png());
            var unrelated=Path.Combine(cache,"unrelated.png"); File.WriteAllBytes(unrelated,Png());
            using var service=Service(root); Assert(!File.Exists(orphan) && File.Exists(unrelated));
        });
        Check("Productivity: corrupt cached images are discarded on restart",root=>
        {
            string path;
            using(var service=Service(root)) { service.SetClipboardEnabled(true); service.IngestImage(Png()); path=service.Clips[0].ImagePath; }
            File.WriteAllText(path,"not a PNG");
            using var restored=Service(root); Assert(restored.Clips.Count==0 && !File.Exists(path));
        });
        Check("Productivity: persistence failures are logged; note typing stays in memory",root=>
        {
            int errors=0,changes=0;
            using var service=new ProductivityService(new Store(root),_=>errors++,false);
            service.Changed+=()=>changes++;
            Assert(service.Save());using var locked=new FileStream(Path.Combine(root,"productivity.json"),FileMode.Open,FileAccess.Read,FileShare.Read);
            service.Note="editable despite disk error";
            Assert(errors==0 && changes==1); service.Note="editable despite disk error"; Assert(changes==1); Assert(!service.Save());
            Assert(errors==1 && changes==1 && service.Note=="editable despite disk error" && !string.IsNullOrEmpty(service.LastError));
        });
    }
}
