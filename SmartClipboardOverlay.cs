using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private string? selectedClipId;
    private void BuildSmartClipboard(StackPanel panel)
    {
        var p=app.Productivity;
        panel.Children.Add(UI.Heading("Smart clipboard",18));
        if(!p.ClipboardEnabled)
        {
            selectedClipId=null;
            var intro=UI.Text("History is off. Enable it to save the text and images you copy next, only on this device.",12,UI.Muted);
            intro.Margin=new(0,10,0,12);panel.Children.Add(intro);
            panel.Children.Add(UI.Button("Enable history",()=>{app.Settings.ClipboardHistory=true;app.ChangeSettings();},true));return;
        }
        var pause=new CheckBox{Content="Pause capture",IsChecked=p.ClipboardPaused,Style=(Style)FindResource("SettingsSwitch"),MinHeight=30,Margin=new(0,8,0,4)};UI.Named(pause,"Pause capture");
        pause.Click+=(_,_)=>p.ClipboardPaused=pause.IsChecked==true;panel.Children.Add(pause);
        Button ActionButton(string text,Action action)
        {
            var button=UI.Button(text,action);button.FontSize=10;button.Padding=new(9,5,9,5);button.MinHeight=27;button.Margin=new(0,3,6,3);return button;
        }
        var controls=new WrapPanel();panel.Children.Add(controls);
        controls.Children.Add(ActionButton("Clear history",()=>MutateUtility(p.ClearClips)));
        controls.Children.Add(ActionButton("Turn off",()=>{app.Settings.ClipboardHistory=false;app.ChangeSettings();}));
        var clips=p.Clips.OrderByDescending(c=>c.Pinned).ThenByDescending(c=>c.Timestamp).ToArray();
        if(!clips.Any(c=>c.Id==selectedClipId))selectedClipId=null;
        foreach(var item in clips)
        {
            var classified=SmartClipboard.Classify(item);
            var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(54)});grid.ColumnDefinitions.Add(new());
            FrameworkElement marker=Icons.Create(classified.Kind switch{SmartClipKind.Link=>"link",SmartClipKind.Image=>"image",_=>"file-text"},19,UI.Muted);
            string? dimensions=null;
            if(classified.Kind==SmartClipKind.Color)marker=new Border{Width=32,Height=32,CornerRadius=new(6),Background=UI.Brush(classified.Hex),BorderThickness=new(1)};
            else if(classified.Kind==SmartClipKind.Image && p.LoadClipImage(item,120) is { } bitmap){marker=new Image{Source=bitmap,Width=44,Height=44,Stretch=Stretch.Uniform};dimensions=$"{bitmap.PixelWidth} × {bitmap.PixelHeight} preview";}
            var preview=new Border{Child=marker,Width=44,Height=44,CornerRadius=new(6),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
            if(classified.Kind!=SmartClipKind.Image){preview.SetResourceReference(Border.BackgroundProperty,"ControlFill");preview.SetResourceReference(Border.BorderBrushProperty,"ControlBorder");preview.BorderThickness=new(1);}
            grid.Children.Add(preview);
            var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            var label=UI.Text(classified.Kind==SmartClipKind.Image?"Copied image":item.Label,12,selectedClipId==item.Id?UI.ContrastText(app.Settings.SelectionFill):Brushes.White);label.FontWeight=FontWeights.Medium;label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(label);
            var timestamp=item.Timestamp.ToLocalTime().ToString(item.Timestamp.ToLocalTime().Date==DateTime.Today?"HH:mm":"MMM d · HH:mm");
            var summary=classified.Kind switch{SmartClipKind.Image=>dimensions??"Preview unavailable",SmartClipKind.Link=>"Link",SmartClipKind.Color=>classified.Hex,_=>"Text"};
            var subline=UI.Text((item.Pinned?"Pinned · ":"")+summary+" · "+timestamp,10,selectedClipId==item.Id?UI.ContrastText(app.Settings.SelectionFill):UI.Muted);subline.TextWrapping=TextWrapping.NoWrap;subline.TextTrimming=TextTrimming.CharacterEllipsis;subline.Margin=new(0,3,0,0);labels.Children.Add(subline);Grid.SetColumn(labels,1);grid.Children.Add(labels);
            var row=UI.Button("View "+classified.Kind,()=>{selectedClipId=selectedClipId==item.Id?null:item.Id;RefreshTools();ResizeToolPanel();Dispatcher.BeginInvoke(()=>toolsContent.Children.OfType<StackPanel>().FirstOrDefault(part=>part.Tag as string=="clip-detail")?.BringIntoView());});row.Tag=new CornerRadius(10);row.Content=grid;row.HorizontalContentAlignment=HorizontalAlignment.Stretch;row.MinHeight=56;row.Padding=new(7,6,7,6);row.Margin=new(0,2,0,0);if(selectedClipId==item.Id)row.SetResourceReference(Control.BackgroundProperty,"SelectionFill");else row.Background=Brushes.Transparent;if(selectedClipId==item.Id)row.SetResourceReference(Control.BorderBrushProperty,"ControlBorder");else row.BorderBrush=Brushes.Transparent;row.BorderThickness=new(1);panel.Children.Add(row);
        }
        if(clips.Length==0){var empty=UI.Text("Your next copied item will appear here.",12,UI.Muted);empty.Margin=new(0,12,0,0);panel.Children.Add(empty);return;}
        var selected=clips.FirstOrDefault(c=>c.Id==selectedClipId);if(selected==null)return;
        var detail=SmartClipboard.Classify(selected);
        var section=new StackPanel{Tag="clip-detail",Margin=new(0,12,0,0)};panel.Children.Add(section);
        section.Children.Add(UI.Text(detail.Kind+" · "+selected.Timestamp.ToLocalTime().ToString("g"),10,UI.Muted));
        var status=UI.Text("",10,UI.Muted);var actions=new WrapPanel{Margin=new(0,5,0,0)};
        void Copy(string? variant=null){status.Text=p.TryCopyClip(selected,variant)?"Copied.":"Could not copy. The clipboard may be busy; try again.";ResizeToolPanel();}
        void Preview(string title,string text)
        {
            var preview=UI.Input(title,text);preview.IsReadOnly=true;preview.AcceptsReturn=true;preview.TextWrapping=TextWrapping.Wrap;preview.MaxHeight=100;preview.MinHeight=38;preview.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;preview.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;preview.Margin=new(0,6,0,3);section.Children.Add(preview);
        }
        if(detail.Kind==SmartClipKind.Image)
        {
            if(p.LoadClipImage(selected,400) is { } image)section.Children.Add(new Image{Source=image,MaxHeight=112,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,7,0,4)});
            actions.Children.Add(ActionButton("Copy image",()=>Copy()));
            actions.Children.Add(ActionButton("Save as PNG…",()=>
            {
                try
                {
                    var dialog=new Microsoft.Win32.SaveFileDialog{Title="Save clipboard image",Filter="PNG image (*.png)|*.png",DefaultExt=".png",AddExtension=true,FileName="Clipboard image.png",OverwritePrompt=true};
                    if(dialog.ShowDialog(this)==true)status.Text=p.SaveClipImage(selected,dialog.FileName)?"Image saved.":"Could not save image. Check the location and try again.";
                }
                catch(Exception ex){app.Store.Log(ex);status.Text="Could not save image. Try another location.";}
                ResizeToolPanel();
            }));
        }
        else
        {
            Preview(detail.Kind==SmartClipKind.Link?"Original link":"Original text",selected.Text);
            actions.Children.Add(ActionButton(detail.Kind==SmartClipKind.Link?"Copy original":"Copy",()=>Copy()));
            if(detail.Kind==SmartClipKind.Text)actions.Children.Add(ActionButton("Copy plain text",()=>Copy(selected.Text)));
            if(detail.Kind==SmartClipKind.Link)
            {
                var hint=UI.Text("Cleaned preview · review before copying",10,UI.Muted);hint.Margin=new(0,6,0,0);section.Children.Add(hint);Preview("Cleaned link preview",detail.CleanedLink);
                actions.Children.Add(ActionButton("Copy cleaned link",()=>Copy(detail.CleanedLink)));
            }
            if(detail.Kind==SmartClipKind.Color)
            {
                section.Children.Add(new Border{Height=24,Background=UI.Brush(detail.Hex),CornerRadius=new(4),Margin=new(0,5,0,5)});
                section.Children.Add(UI.Text(detail.Hex+" · "+detail.Rgb,11,UI.Muted));
                actions.Children.Add(ActionButton("Copy HEX",()=>Copy(detail.Hex)));actions.Children.Add(ActionButton("Copy RGB",()=>Copy(detail.Rgb)));
            }
        }
        actions.Children.Add(ActionButton(selected.Pinned?"Unpin":"Pin",()=>MutateUtility(()=>p.ToggleClipPin(selected.Id))));
        actions.Children.Add(ActionButton("Remove",()=>MutateUtility(()=>p.RemoveClip(selected.Id))));section.Children.Add(actions);section.Children.Add(status);
    }
}
