using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NabyScape;

internal sealed record MediaSourceChoice(string Id,string Name,Action Select);

// An in-surface overlay: Canvas contributes no desired size, so opening a control
// never grows the island or displaces transport/timeline/lyrics.
internal sealed class MediaControls : Grid
{
    private readonly Canvas layer=new(){ClipToBounds=true};
    private readonly Border popover=new(){Tag="media-popover",Background=UI.Brush("#111216"),BorderBrush=UI.Brush("#53565D"),BorderThickness=new(.7),CornerRadius=new(12),Padding=new(10),Visibility=Visibility.Collapsed};
    private readonly Grid footer=new(){Margin=new(0,8,0,0)};
    private readonly Button source,level;
    private readonly TextBlock sourceName=UI.Text("Media",12),levelText=UI.Text("—",11,UI.Muted);
    private readonly Grid sourceIcon=new(){Width=18,Height=18,Margin=new(0,0,8,0)},levelIcon=new(){Width=18,Height=18,Margin=new(0,0,8,0)};
    private readonly Func<IReadOnlyList<MediaSourceChoice>> sources;
    private readonly Func<string?> selected;
    private readonly Func<FrameworkElement> volumeContent;
    private string kind="",sourceSignature="",iconKey="",levelIconKey="";
    private Button? anchor;
    internal bool IsOpen=>kind.Length>0;
    internal string OpenKind=>kind;
    internal Border Popover=>popover;
    internal Button SourceButton=>source;
    internal Button VolumeButton=>level;

    internal MediaControls(StackPanel content,bool media,bool volume,Func<IReadOnlyList<MediaSourceChoice>> sources,Func<string?> selected,Func<FrameworkElement> volumeContent)
    {
        this.sources=sources;this.selected=selected;this.volumeContent=volumeContent;
        footer.ColumnDefinitions.Add(new());footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        source=FooterButton("Choose media source",()=>Toggle("source"));
        var chevron=Icons.Create("chevron",8,UI.Muted);chevron.Margin=new(7,0,0,0);source.Content=UI.Row(sourceIcon,sourceName,chevron);source.HorizontalAlignment=HorizontalAlignment.Left;source.Visibility=media?Visibility.Visible:Visibility.Collapsed;source.ToolTip="Choose media source · Enter or Space to open";footer.Children.Add(source);
        level=FooterButton("Volume controls",()=>Toggle("volume"));level.Content=UI.Row(levelIcon,levelText);level.HorizontalAlignment=HorizontalAlignment.Right;level.Visibility=volume?Visibility.Visible:Visibility.Collapsed;level.ToolTip="Volume, mute, mixer and audio devices · Enter or Space to open";Grid.SetColumn(level,1);footer.Children.Add(level);
        if(!media){content.MinHeight=144;var heading=UI.Heading("Volume",18);content.Children.Add(heading);}
        if(media)content.Children.Add(footer);Children.Add(content);if(!media){footer.VerticalAlignment=VerticalAlignment.Bottom;Children.Add(footer);}Children.Add(layer);layer.Children.Add(popover);Panel.SetZIndex(layer,10);
        KeyboardNavigation.SetTabNavigation(popover,KeyboardNavigationMode.Cycle);
        SizeChanged+=(_,_)=>Place();popover.SizeChanged+=(_,_)=>Place();
        Unloaded+=(_,_)=>Close(false);IsVisibleChanged+=(_,_)=>{if(!IsVisible)Close(false);};
        // Source lists own wheel scrolling; do not advance the widget carousel.
        popover.PreviewMouseWheel+=(_,e)=>{if(popover.Child is ScrollViewer viewer)viewer.ScrollToVerticalOffset(viewer.VerticalOffset-e.Delta*.35);e.Handled=true;};
        Refresh(0,false,false);
    }
    private static Button FooterButton(string name,Action run)
    {
        var button=UI.Button(name,run);button.MinHeight=32;button.Padding=new(7,4,7,4);button.Margin=new(0);button.Background=Brushes.Transparent;button.BorderThickness=new(0);return button;
    }
    internal void Refresh(double value,bool muted,bool available,bool refreshSources=true)
    {
        levelText.Text=available?$"{value:0}%":"—";string levelKey=muted?"muted":"volume";
        if(levelKey!=levelIconKey){levelIconKey=levelKey;levelIcon.Children.Clear();levelIcon.Children.Add(Icons.Create(levelKey,18));}
        if(!refreshSources)return;
        var choices=sources();string? id=selected();var current=choices.FirstOrDefault(item=>item.Id==id);
        string signature=string.Join("\n",choices.Select(item=>item.Id+"\t"+item.Name))+"\nselected:"+id;
        if(IsOpen && kind=="source" && signature!=sourceSignature)Close(popover.IsKeyboardFocusWithin);
        sourceSignature=signature;sourceName.Text=current?.Name??"Media";sourceName.MaxWidth=Math.Max(48,ActualWidth-145);sourceName.TextTrimming=TextTrimming.CharacterEllipsis;
        source.ToolTip=current!=null?"Media source · "+current.Name:"Play media in an app to select its session";
        string key=current?.Id??"";if(key!=iconKey || sourceIcon.Children.Count==0){iconKey=key;sourceIcon.Children.Clear();sourceIcon.Children.Add(SourceGlyph(key));}
    }
    internal void Toggle(string next)
    {
        if(IsOpen && kind==next){Close();return;}
        Close(false);kind=next;anchor=next=="source"?source:level;
        anchor.Background=UI.Brush("#25272B");AutomationProperties.SetItemStatus(anchor,"Expanded");
        if(next=="volume")popover.Child=volumeContent();
        else
        {
            var list=new StackPanel();var heading=UI.Text("Media source",11,UI.Muted);heading.Margin=new(3,0,0,8);list.Children.Add(heading);
            var choices=sources();
            if(choices.Count==0){var empty=UI.Text("No active media sessions.\nPlay something in Spotify or your browser.",11,UI.Muted);empty.Margin=new(3,6,3,6);list.Children.Add(empty);}
            foreach(var item in choices)
            {
                var button=UI.Button("Use media source "+item.Name,()=>{Close(false);item.Select();source.Focus();});button.Padding=new(6);button.Margin=new(0);button.Background=item.Id==selected()?UI.Brush("#24262B"):Brushes.Transparent;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                var row=new DockPanel{LastChildFill=true};var check=UI.Text(item.Id==selected()?"✓":"",14);check.Width=22;check.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(check,Dock.Right);row.Children.Add(check);
                var glyph=SourceGlyph(item.Id);glyph.Margin=new(0,0,10,0);DockPanel.SetDock(glyph,Dock.Left);row.Children.Add(glyph);var name=UI.Text(item.Name,12);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;row.Children.Add(name);button.Content=row;AutomationProperties.SetItemStatus(button,item.Id==selected()?"Selected":"Available");list.Children.Add(button);
            }
            popover.Child=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        }
        UI.Named(popover,next=="source"?"Media source popover":"Volume popover");popover.Visibility=Visibility.Visible;Place();
        var move=new TranslateTransform(0,Motion.Enabled?5:0);popover.RenderTransform=move;Motion.To(move,TranslateTransform.YProperty,0,180);popover.Opacity=Motion.Enabled?0:1;
        Motion.Fade(popover,1,220,completed:()=>{if(popover.RenderTransform==move){move.BeginAnimation(TranslateTransform.YProperty,null);popover.BeginAnimation(OpacityProperty,null);}});
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>{if(IsOpen && popover.IsVisible)popover.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));});
    }
    internal bool Close(bool restoreFocus=true)
    {
        if(!IsOpen)return false;kind="";popover.Visibility=Visibility.Collapsed;popover.Child=null;
        popover.BeginAnimation(OpacityProperty,null);popover.Opacity=1;if(popover.RenderTransform is TranslateTransform move)move.BeginAnimation(TranslateTransform.YProperty,null);
        if(anchor!=null){anchor.Background=Brushes.Transparent;AutomationProperties.SetItemStatus(anchor,"Collapsed");if(restoreFocus && anchor.IsVisible)anchor.Focus();}anchor=null;return true;
    }
    internal bool ContainsControl(DependencyObject? target)
    {
        for(var node=target;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node==popover || node==source || node==level)return true;
        return false;
    }
    private void Place()
    {
        sourceName.MaxWidth=Math.Max(48,ActualWidth-145);if(!IsOpen || ActualWidth<=0 || ActualHeight<=0)return;
        popover.Width=Math.Min(kind=="volume"?280:260,ActualWidth);popover.MaxHeight=Math.Max(40,ActualHeight-footer.ActualHeight-12);
        popover.Measure(new Size(popover.Width,popover.MaxHeight));double height=Math.Min(popover.DesiredSize.Height,popover.MaxHeight);
        Canvas.SetLeft(popover,kind=="volume"?Math.Max(0,ActualWidth-popover.Width):0);Canvas.SetTop(popover,Math.Max(0,ActualHeight-footer.ActualHeight-height-10));
    }
    private static FrameworkElement SourceGlyph(string id)
    {
        if(id.Contains("spotify",StringComparison.OrdinalIgnoreCase))
        {
            var grid=new Grid{Width=18,Height=18};grid.Children.Add(new Ellipse{Fill=UI.Brush("#1ED760")});
            grid.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M4,6 Q9,4 14,7 M5,9 Q9,7.5 13,10 M6,12 Q9,11 12,13"),Stroke=UI.Brush("#101312"),StrokeThickness=1.3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});return grid;
        }
        if(id.Contains("chrome",StringComparison.OrdinalIgnoreCase))
        {
            var grid=new Grid{Width=18,Height=18};grid.Children.Add(new Ellipse{Fill=UI.Brush("#EAC446")});grid.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M9,9 L1.2,4.5 A9,9 0 0 1 16.8,4.5 L9,4.5 Z"),Fill=UI.Brush("#E25950")});grid.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M9,9 L9,18 A9,9 0 0 1 1.2,4.5 L5.1,11.2 Z"),Fill=UI.Brush("#5BA76F")});grid.Children.Add(new Ellipse{Width=8,Height=8,Fill=UI.Brush("#4E9BDD"),Stroke=UI.Brush("#E7E9EA"),StrokeThickness=1});return grid;
        }
        return Icons.Create("music",18,UI.Muted);
    }
}
