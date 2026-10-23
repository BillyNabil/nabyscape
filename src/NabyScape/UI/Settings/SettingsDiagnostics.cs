using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    // Parent integration: call DiagnosticsSection() from General(). No report is written until Export is chosen.
    internal void DiagnosticsSection()
    {
        var preview=new TextBox
        {
            IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=180,MaxHeight=320,
            Text=PrivateDiagnostics.CreateReport(app.Store)
        };
        UI.LocalizedName(preview,"Private diagnostics report preview");
        var notice=UI.Caption("Review the preview before copying or exporting. Nothing is uploaded automatically.",11,UI.Muted);
        Accessibility.Live(notice);
        var refresh=UI.CaptionButton("Refresh diagnostics preview",()=>
        {
            preview.Text=PrivateDiagnostics.CreateReport(app.Store);
            Accessibility.Announce(notice,Localization.Text("Diagnostics preview refreshed."));
        });
        var copy=UI.CaptionButton("Copy diagnostics report",()=>
        {
            try
            {
                Clipboard.SetText(preview.Text);
                Accessibility.Announce(notice,Localization.Text("Diagnostics report copied."));
            }
            catch(Exception ex)
            {
                PrivateDiagnostics.Record(DiagnosticStage.DiagnosticsCopy,PrivateDiagnostics.Classify(ex));
                Accessibility.Announce(notice,Localization.Text("Could not copy the report. Try again."));
            }
        });
        var export=UI.CaptionButton("Export diagnostics report",()=>
        {
            try
            {
                var dialog=new SaveFileDialog
                {
                    Title=Localization.Text("Export diagnostics report"),
                    Filter=Localization.Text("JSON report (*.json)|*.json"),DefaultExt=".json",AddExtension=true,
                    FileName="NabyScape-diagnostics.json",OverwritePrompt=true,CheckPathExists=true
                };
                if(dialog.ShowDialog(this)!=true)return;
                using(var stream=new FileStream(dialog.FileName,FileMode.Create,FileAccess.Write,FileShare.None))
                {
                    var bytes=new UTF8Encoding(false).GetBytes(preview.Text);
                    stream.Write(bytes,0,bytes.Length);stream.Flush(true);
                }
                Accessibility.Announce(notice,Localization.Text("Diagnostics report exported."));
            }
            catch(Exception ex)
            {
                PrivateDiagnostics.Record(DiagnosticStage.DiagnosticsExport,PrivateDiagnostics.Classify(ex));
                Accessibility.Announce(notice,Localization.Text("Could not export the report. Try another location."));
            }
        });
        page.Children.Add(Section("Private diagnostics",
            UI.Caption("Includes app and runtime versions, architecture, coarse storage and service status, and up to 64 controlled diagnostic events. No usernames, paths, endpoints, models, device names, keys, conversations, screenshots, audio or saved profile content are included.",11,UI.Muted),
            UI.Caption("The existing local errors.log may contain private details and is never included in this report.",11,UI.Muted),
            preview,refresh,copy,export,notice,
            UI.Caption("Storage recovery",13),
            UI.Caption("When a document is unreadable or from a newer version, it stays protected from automatic saves. Temporary defaults may be shown. Quit NabyScape and preserve the original JSON file and its matching .schema.json sidecar before recovery. Use a compatible app version, or restore a known-good matching document and schema backup. For a legacy backup without a sidecar, preserve and move aside the current sidecar too. Keep the original and migration backup until recovery is verified. Reset preferences does not unlock protected documents.",11,UI.Muted)));
    }
}
