using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
namespace NabyScape;
internal static class StabilityDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        await Stage2Diagnostics.Run(app,island,directory,check);
        var window=new SettingsWindow(app){ShowActivated=false};
        try
        {
            window.Show();Diagnostics.NavigateSettings(window,"Content");await Task.Delay(60);
            // Reproduce another preference save replacing the list while this editor stays mounted.
            app.ChangeSettings();await Task.Delay(60);
            var section=Diagnostics.LogicalDescendants(window).OfType<Border>().Single(b=>b.Tag as string=="Enabled widgets & order");
            var items=app.Settings.Widgets;var before=items.Select(w=>(w.Id,w.Enabled)).ToArray();var moving=items[^1];var target=items[0];
            var grip=Diagnostics.Descendants(section).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Drag to reorder "+target.Id);
            grip.BringIntoView();window.UpdateLayout();var card=(Border)((DockPanel)grip.Parent).Parent;
            var data=new DataObject("NabyScape.Order.widgets",moving);
            var point=card.TranslatePoint(new Point(2,2),(UIElement)PresentationSource.FromVisual(card).RootVisual);
            var drop=(DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{data,DragDropKeyStates.None,DragDropEffects.Move,card,point},null)!;
            int expected=drop.GetPosition(card).Y>card.ActualHeight/2?1:0;drop.RoutedEvent=DragDrop.DropEvent;card.RaiseEvent(drop);await Task.Delay(500);
            var saved=app.Store.Load<Settings>("settings");
            check("Stability drag after preference save updates live and persisted order",drop.Handled && app.Settings.Widgets[expected].Id==moving.Id && saved.Widgets.Select(w=>w.Id).SequenceEqual(app.Settings.Widgets.Select(w=>w.Id)));
            check("Stability reorder preserves all switches and other relative order",saved.Widgets.All(w=>before.Single(b=>b.Id==w.Id).Enabled==w.Enabled) && saved.Widgets.Where(w=>w.Id!=moving.Id).Select(w=>w.Id).SequenceEqual(before.Where(w=>w.Id!=moving.Id).Select(w=>w.Id)));
            window.Close();
            var restarted=new Store(app.Store.DirectoryPath).Load<Settings>("settings");restarted.Normalize();
            check("Stability fresh profile reload preserves ordered hover widgets",restarted.Widgets.Select(w=>w.Id).SequenceEqual(saved.Widgets.Select(w=>w.Id)) && HoverPagePolicy.Pages(restarted).SequenceEqual(saved.Widgets.Where(w=>w.Enabled && HoverPagePolicy.Ids.Contains(w.Id)).Select(w=>w.Id)));
        }
        finally{window.Close();}
        await InteractionDiagnostics.RunReviewRegressions(app,island,directory,check);
        await MediaExperienceDiagnostics.Run(app,island,directory,check);
    }
}
