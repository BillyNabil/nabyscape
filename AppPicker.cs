using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;

internal sealed class ShortcutIcon:Grid
{
    internal event Action<bool>? AvailabilityChanged;
    internal ShortcutIcon(Shortcut shortcut)
    {
        Width=Height=24;Visibility=Visibility.Collapsed;
        Loaded+=async(_,_)=>{var source=await AppIconCache.GetAsync(shortcut);if(!IsLoaded || Window.GetWindow(this)?.IsVisible!=true)return;AvailabilityChanged?.Invoke(source!=null);if(source==null)return;Children.Clear();Children.Add(new Image{Source=source,Width=24,Height=24});Visibility=Visibility.Visible;};
    }
}
internal sealed class AppPicker:Window
{
    private readonly IAppCatalog catalog;
    private readonly CancellationTokenSource cancellation=new();
    private readonly TextBox search=UI.Input("Search installed apps","","Search by application name");
    private readonly ListBox list=new(){BorderThickness=new(0),Background=Brushes.Transparent,Foreground=Brushes.White};
    private readonly TextBlock state=UI.Text("Loading installed apps…",12,UI.Muted);
    private IReadOnlyList<InstalledApp> apps=Array.Empty<InstalledApp>();
    private bool closed,loading=true,failed;
    internal AppPicker(Window owner,Action<Shortcut> add,IAppCatalog? catalog=null)
    {
        this.catalog=catalog??new WindowsAppCatalog();Owner=owner;Title="Add installed app";Width=480;Height=570;MinWidth=320;MinHeight=340;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=UI.Brush("#161618");Foreground=Brushes.White;
        var panel=new DockPanel{Margin=new(20)};Content=panel;
        var title=UI.Text("Installed apps",20);title.Margin=new(0,0,0,14);DockPanel.SetDock(title,Dock.Top);panel.Children.Add(title);
        search.Margin=new(0,0,0,12);DockPanel.SetDock(search,Dock.Top);panel.Children.Add(search);
        state.Margin=new(0,0,0,8);DockPanel.SetDock(state,Dock.Top);panel.Children.Add(state);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var accept=UI.Button("Add selected app",()=>Accept(),true);accept.IsEnabled=false;buttons.Children.Add(UI.Button("Cancel",Close));buttons.Children.Add(accept);DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);panel.Children.Add(list);
        UI.Named(list,"Installed applications");ScrollViewer.SetCanContentScroll(list,true);VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
        var template=new DataTemplate(typeof(InstalledApp));var factory=new FrameworkElementFactory(typeof(AppPickerRow));template.VisualTree=factory;list.ItemTemplate=template;list.DisplayMemberPath="";
        list.SelectionChanged+=(_,_)=>accept.IsEnabled=list.SelectedItem is InstalledApp;
        search.TextChanged+=(_,_)=>Filter();search.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Down && list.Items.Count>0){list.SelectedIndex=0;list.Focus();e.Handled=true;}};
        list.MouseDoubleClick+=(_,_)=>Accept();list.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){Accept();e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
        Closed+=(_,_)=>{closed=true;cancellation.Cancel();};Loaded+=async(_,_)=>{search.Focus();try{apps=await this.catalog.DiscoverAsync(cancellation.Token);loading=false;if(!closed)Filter();}catch(OperationCanceledException){}catch{loading=false;failed=true;if(!closed)state.Text="Installed apps could not be loaded. Close and try again, or add a shortcut manually.";}};
        void Accept(){if(list.SelectedItem is InstalledApp selected){add(selected.ToShortcut());Close();}}
    }
    internal static InstalledApp[] Search(IEnumerable<InstalledApp> source,string query)=>source.Where(a=>a.Name.Contains(query.Trim(),StringComparison.CurrentCultureIgnoreCase)).ToArray();
    private void Filter(){if(loading || failed)return;var filtered=Search(apps,search.Text);list.ItemsSource=filtered;state.Text=filtered.Length==0?(apps.Count==0?"No installed apps were found. You can add a shortcut manually in Settings.":"No installed apps match your search."):filtered.Length+" applications";if(filtered.Length>0)list.SelectedIndex=0;}
}
public sealed class AppPickerRow:StackPanel
{
    public AppPickerRow()
    {
        Orientation=Orientation.Horizontal;Margin=new(4,7,4,7);
        DataContextChanged+=(_,_)=>{Children.Clear();if(DataContext is InstalledApp app){var icon=new ShortcutIcon(app.ToShortcut()){Margin=new(0,0,12,0)};Children.Add(icon);Children.Add(UI.Text(app.Name,13));ToolTip=app.Name;UI.Named(this,app.Name);}};
    }
}
