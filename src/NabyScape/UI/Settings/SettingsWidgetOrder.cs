using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private FrameworkElement WidgetOrder(List<WidgetSetting> items,string scope,Action? changed=null)
    {
        var list=new StackPanel();string format="NabyScape.Order."+scope;
        void MoveTo(WidgetSetting item,int index)
        {
            // Normalize replaces the list on save. Always mutate the live collection,
            // including after another preference has already been saved in this page.
            items=scope=="widgets"?app.Settings.Widgets:app.Settings.HoverViews;
            int old=items.FindIndex(current=>current.Id==item.Id);if(old<0)return;
            var current=items[old];items.RemoveAt(old);items.Insert(Math.Clamp(index,0,items.Count),current);Save();changed?.Invoke();Build();
        }
        void Build()
        {
            list.Children.Clear();foreach(var item in items)
            {
                var row=new DockPanel{LastChildFill=true};
                var grip=UI.Button("Drag to reorder "+item.Id,()=>{});grip.Content="⠿";grip.FontSize=19;grip.Width=28;grip.Padding=new(0);grip.Background=Brushes.Transparent;grip.Foreground=UI.Muted;grip.Cursor=Cursors.SizeNS;grip.Margin=new(0,0,8,0);DockPanel.SetDock(grip,Dock.Left);row.Children.Add(grip);
                var enabled=new CheckBox{IsChecked=item.Enabled,Style=(Style)FindResource("SettingsSwitch"),VerticalAlignment=VerticalAlignment.Center,Margin=new(12,0,0,0)};UI.Named(enabled,"Enable "+(Settings.Names.GetValueOrDefault(item.Id)??item.Id));enabled.Click+=(_,_)=>{item.Enabled=enabled.IsChecked==true;Save();changed?.Invoke();};DockPanel.SetDock(enabled,Dock.Right);row.Children.Add(enabled);
                var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};labels.Children.Add(UI.Text(Settings.Names.GetValueOrDefault(item.Id)??item.Id,12));string help=SettingDescription(Settings.Names.GetValueOrDefault(item.Id)??item.Id);if(help.Length>0){var sub=UI.Text(help,10,UI.Muted);sub.Margin=new(0,4,0,0);labels.Children.Add(sub);}row.Children.Add(labels);
                var card=new Border{Child=row,Padding=new(5,10,8,10),BorderBrush=Brushes.Transparent,BorderThickness=new(0,2,0,2),CornerRadius=new(7),Background=Brushes.Transparent,AllowDrop=true};list.Children.Add(card);Point start=default;
                grip.PreviewMouseLeftButtonDown+=(_,e)=>start=e.GetPosition(grip);grip.PreviewMouseMove+=(_,e)=>{if(e.LeftButton!=MouseButtonState.Pressed || (e.GetPosition(grip)-start).Length<SystemParameters.MinimumVerticalDragDistance)return;card.Opacity=.45;try{DragDrop.DoDragDrop(grip,new DataObject(format,item),DragDropEffects.Move);}finally{card.Opacity=1;}};
                card.DragOver+=(_,e)=>{if(!e.Data.GetDataPresent(format)){e.Effects=DragDropEffects.None;return;}e.Effects=DragDropEffects.Move;card.BorderBrush=UI.Brush("#8099B7");double edge=e.GetPosition(settingsScroll).Y;if(edge<40)settingsScroll.LineUp();else if(edge>settingsScroll.ActualHeight-40)settingsScroll.LineDown();e.Handled=true;};card.DragLeave+=(_,_)=>card.BorderBrush=Brushes.Transparent;
                card.Drop+=(_,e)=>{card.BorderBrush=Brushes.Transparent;if(e.Data.GetData(format) is not WidgetSetting dragged)return;int index=items.IndexOf(item)+(e.GetPosition(card).Y>card.ActualHeight/2?1:0);if(items.IndexOf(dragged)<index)index--;MoveTo(dragged,index);e.Handled=true;};
                grip.PreviewKeyDown+=(_,e)=>{if(Keyboard.Modifiers!=ModifierKeys.Alt || e.Key is not (Key.Up or Key.Down))return;MoveTo(item,items.IndexOf(item)+(e.Key==Key.Up?-1:1));e.Handled=true;};grip.ToolTip="Drag to reorder · Alt+↑ / Alt+↓ with keyboard";
            }
        }
        Build();return list;
    }
}
