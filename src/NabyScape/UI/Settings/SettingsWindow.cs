using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace NabyScape;

public sealed partial class SettingsWindow : Window
{
    private readonly App app;
    private readonly StackPanel page=new(){Margin=new(28,20,28,28)};
    private readonly TextBlock status=UI.Text("Changes save automatically on this device.",11,UI.Muted);
    private readonly Border preview;
    private readonly Border previewStage;
    private readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private readonly System.Collections.Generic.Dictionary<string,Button> navigationButtons=new();
    private readonly Border sectionIndexHost=new(){Margin=new(28,12,28,8)};
    private int navigationRevision,previewRevision;
    private int? previewBuildKey;
    private bool applyingSettings;
    private readonly System.Collections.Generic.List<SettingsScene> settingExamples=new();
    private string current="Appearance";
    private readonly TextBlock frameStatus=UI.Text("",11,UI.Muted);
    private readonly TextBlock hotkeyState=UI.Text("",11,UI.Muted);
    public SettingsWindow(App app)
    {
        this.app=app;Title="NabyScape · Settings";Width=1100;Height=780;MinWidth=480;MinHeight=300;WindowStartupLocation=WindowStartupLocation.Manual;
        Background=UI.Brush("#111214");Foreground=UI.Brush("#F3F4F6");FontFamily=new FontFamily("Segoe UI Variable, Segoe UI");FontSize=13;
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/island.ico"));
        Resources[typeof(CheckBox)]=FindResource("SettingsSwitch");
        InitializeSettingsBounds();InitializeMaterial();
        var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.ColumnDefinitions.Add(new(){Width=new GridLength(168)});root.ColumnDefinitions.Add(new());
        var navigation=new StackPanel{Orientation=Orientation.Vertical,Margin=new(14,26,12,18)};var brand=UI.Text("NabyScape",20);brand.TextWrapping=TextWrapping.NoWrap;brand.FontWeight=FontWeights.SemiBold;brand.Margin=new(12,0,0,28);navigation.Children.Add(brand);
        foreach(var name in new[]{"Appearance","Layout","Content","Controls","Audio","System","Launch","Application"}){var button=UI.Button(name,()=>Navigate(name));button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Padding=new(12,9,12,9);button.Margin=new(0,2,0,2);button.Tag=new CornerRadius(8);button.FontSize=12;navigationButtons[name]=button;navigation.Children.Add(button);}
        var navigationScroll=UI.Scroll(navigation);UI.Named(navigationScroll,"Settings category viewport");
        var navBorder=new Border{Background=UI.Brush("#17181B"),BorderBrush=UI.Brush("#24262B"),BorderThickness=new(0,0,1,0),Child=navigationScroll};root.Children.Add(navBorder);
        var categories=new ComboBox{Margin=new(16,7,16,7),ItemsSource=navigationButtons.Keys,SelectedItem=current,Visibility=Visibility.Collapsed};UI.Named(categories,"Settings category");
        categories.SelectionChanged+=(_,_)=>{if(categories.SelectedItem is string name && name!=current)Navigate(name);};Grid.SetColumnSpan(categories,2);root.Children.Add(categories);
        var main=new Grid();Grid.SetColumn(main,1);Grid.SetRowSpan(main,2);Grid.SetRowSpan(navBorder,2);root.Children.Add(main);main.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});main.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var workspace=new Grid();workspace.RowDefinitions.Add(new(){Height=GridLength.Auto});workspace.RowDefinitions.Add(new(){Height=GridLength.Auto});workspace.RowDefinitions.Add(new());workspace.ColumnDefinitions.Add(new());workspace.ColumnDefinitions.Add(new(){Width=new GridLength(300)});main.Children.Add(workspace);
        var scroll=UI.Scroll(page);scroll.Padding=new(0,0,8,0);workspace.Children.Add(scroll);Grid.SetRow(scroll,2);settingsScroll=scroll;Grid.SetRow(sectionIndexHost,1);workspace.Children.Add(sectionIndexHost);
        preview=new Border{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top};
        var previewColumn=new StackPanel{Margin=new(20,28,20,20)};var previewTitle=UI.Text("Live preview",14);previewTitle.FontWeight=FontWeights.SemiBold;previewColumn.Children.Add(previewTitle);previewFocus.Margin=new(0,7,0,0);previewColumn.Children.Add(previewFocus);
        var modes=new Grid{Margin=new(0,14,0,12)};for(int i=0;i<3;i++)modes.ColumnDefinitions.Add(new());int column=0;foreach(string mode in new[]{"Closed","Hover","Panel"}){var button=UI.Button(mode,()=>{previewMode=mode;RefreshPreview();});button.FontSize=10;button.Padding=new(3,7,3,7);button.Margin=new(0,0,4,0);button.Tag=new CornerRadius(7);UI.Named(button,"Preview "+mode);Grid.SetColumn(button,column++);modes.Children.Add(button);previewModeButtons[mode]=button;}previewColumn.Children.Add(modes);
        previewStage=new Border{Height=238,Background=UI.Brush("#202226"),CornerRadius=new(10),ClipToBounds=true,Child=new System.Windows.Controls.Viewbox{Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Child=preview,VerticalAlignment=VerticalAlignment.Top}};previewColumn.Children.Add(previewStage);
        previewDimensions.Margin=new(0,12,0,18);previewColumn.Children.Add(previewDimensions);var sample=UI.Text("Preview content",11,UI.Muted);sample.Margin=new(0,0,0,7);previewColumn.Children.Add(sample);var examples=new ComboBox();previewExamples=examples;foreach(string item in new[]{"Music","Timer","Focus","Stopwatch","Calendar","Note","Shelf","Tools","System","Launch","Navigation","Volume feedback","Drop zone","Detached preview","Cover gestures","Two activities","Activities","Smart clipboard","Control colors","Typography","Panel navigation","Visibility","Tasks","Startup"})examples.Items.Add(item);examples.SelectedIndex=0;UI.Named(examples,"Preview content");examples.SelectionChanged+=(_,_)=>{previewContent=examples.SelectedItem?.ToString()??"Music";RefreshPreview();};previewColumn.Children.Add(examples);var hint=UI.Text("Changes apply automatically. The preview uses your current shape, size and colors.",11,UI.Muted);hint.Margin=new(0,15,0,0);previewColumn.Children.Add(hint);
        var previewExpander=new Expander{Header=UI.Text("Live preview",12,UI.Muted),Foreground=UI.Muted,Margin=new(8,0,0,0),Content=UI.Scroll(previewColumn),IsExpanded=true};
        var rail=new Border{BorderBrush=UI.Brush("#24262B"),BorderThickness=new(1,0,0,0),Child=previewExpander};Grid.SetColumn(rail,1);Grid.SetRowSpan(rail,3);workspace.Children.Add(rail);
        var footer=new Border{Padding=new(26,12,26,14),BorderBrush=UI.Brush("#24262B"),BorderThickness=new(0,1,0,0),Child=status};
        bool? previousNarrow=null;
        bool? previousShort=null;
        void Responsive()
        {
            bool narrow=ActualWidth<1100,compactNav=ActualWidth<760,shortScreen=ActualHeight<600,veryShort=ActualHeight<340;
            categories.Margin=new Thickness(16,veryShort?3:7,16,veryShort?3:7);
            footer.Padding=veryShort?new Thickness(20,4,20,6):new Thickness(26,12,26,14);
            root.ColumnDefinitions[0].Width=compactNav?new GridLength(0):new GridLength(152);
            navBorder.Visibility=compactNav?Visibility.Collapsed:Visibility.Visible;categories.Visibility=compactNav?Visibility.Visible:Visibility.Collapsed;
            if(categories.SelectedItem?.ToString()!=current)categories.SelectedItem=current;
            Grid.SetRow(main,compactNav?1:0);Grid.SetRowSpan(main,compactNav?1:2);Grid.SetColumn(main,compactNav?0:1);Grid.SetColumnSpan(main,compactNav?2:1);
            workspace.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(284);Grid.SetColumn(rail,narrow?0:1);Grid.SetRowSpan(rail,narrow?1:3);
            if(previousNarrow!=narrow || previousShort!=shortScreen){previewExpander.IsExpanded=!narrow && !shortScreen;previousNarrow=narrow;previousShort=shortScreen;}
            previewTitle.Visibility=Visibility.Collapsed;
            double previewHeight=Math.Clamp(workspace.ActualHeight-sectionIndexHost.ActualHeight-100,50,210);
            previewExpander.MaxHeight=narrow?previewHeight:double.PositiveInfinity;
            rail.Height=double.NaN;
            modes.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
            previewStage.Height=narrow?Math.Max(40,previewHeight-65):238;
            previewColumn.Margin=narrow?new Thickness(18,6,18,8):new Thickness(18,18,18,18);hint.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;previewDimensions.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;sample.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;examples.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
            sectionIndexHost.Margin=narrow?(veryShort?new Thickness(20,3,20,2):new Thickness(20,8,20,4)):new Thickness(28,14,28,4);
            page.Margin=narrow?new Thickness(20,veryShort?8:18,20,24):new Thickness(28,20,28,28);
        }
        UI.Named(previewExpander,"Settings live preview");UI.Named(scroll,"Settings form viewport");UI.Named(rail,"Settings preview rail");UI.Named(navBorder,"Settings navigation");
        SizeChanged+=(_,_)=>Responsive();workspace.SizeChanged+=(_,_)=>Responsive();Loaded+=(_,_)=>Responsive();
        page.MaxWidth=660;page.HorizontalAlignment=HorizontalAlignment.Stretch;
        Grid.SetRow(footer,1);main.Children.Add(footer);Content=SettingsFrame(root);InitializePreviewMotion(previewColumn);
        saveTimer.Tick+=(_,_)=>{saveTimer.Stop();try{applyingSettings=true;app.ChangeSettings();status.Text="Saved on this device";}catch(Exception ex){status.Text="Could not save changes. Try again.";app.Store.Log(ex);}finally{applyingSettings=false;}};
        app.Updates.Changed+=UpdateControls;
        app.SettingsChanged+=SettingsApplied;app.Updated+=UpdateFrameStatus;
        Closed+=(_,_)=>{if(saveTimer.IsEnabled){saveTimer.Stop();app.ChangeSettings();}app.Updates.Changed-=UpdateControls;app.SettingsChanged-=SettingsApplied;app.Updated-=UpdateFrameStatus;};Navigate("Appearance");RefreshPreview();
    }
    private void Save() {previewRevision++;InvalidateExamples(System.Windows.Input.Keyboard.FocusedElement as DependencyObject??System.Windows.Input.Mouse.DirectlyOver as DependencyObject);RefreshPreview();status.Text="Saving changes…";saveTimer.Stop();saveTimer.Start();}
    private void SettingsApplied(){ApplyMaterial();if(applyingSettings)return;previewRevision++;InvalidateExamples(null);RefreshPreview();}
    private void InvalidateExamples(DependencyObject? source)
    {
        for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
            if(node is Border {Tag:string title}){var example=settingExamples.FirstOrDefault(e=>e.Section==title);if(example!=null){example.InvalidateVisual();return;}}
        foreach(var example in settingExamples)example.InvalidateVisual();
    }
    private void RefreshPreview()
    {
        int key=HashCode.Combine(HashCode.Combine(current,previewMode,previewContent,previewRevision),app.Media.Title,app.Media.Artist,app.Media.Playing,app.Media.Artwork,app.Audio.Volume,app.Audio.Muted);
        if(previewBuildKey!=key){previewBuildKey=key;RenderSettingsPreview();}SyncPreviewRendering();
    }
    private void Navigate(string name)
    {
        bool same=name==current && page.Children.Count>0;double offset=settingsScroll.VerticalOffset;int revision=++navigationRevision;
        var expanded=SettingTree(page).OfType<Expander>().Where(e=>e.IsExpanded).Select(e=>e.Header?.ToString()).ToHashSet();
        string focused=System.Windows.Input.Keyboard.FocusedElement is DependencyObject oldFocus?System.Windows.Automation.AutomationProperties.GetName(oldFocus):"";
        previewRevision++;settingExamples.Clear();current=name;previewFocus.Text=name+" overview";previewContent="Music";previewMode="Closed";if(name=="Launch"){previewContent="Launch";previewMode="Panel";}if(name=="Content"){previewContent="Navigation";previewMode="Panel";}if(name=="Controls"){previewContent="Music";previewMode="Hover";}if(name=="System"){previewContent="System";previewMode="Panel";}if(name=="Audio"){previewContent="Music";previewMode="Panel";}if(name=="Application"){previewContent="Startup";previewMode="Panel";}if(!same)settingsScroll.ScrollToTop();page.Children.Clear();var heading=UI.Text(name,24);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new(0,0,0,26);page.Children.Add(heading);
        foreach(var entry in navigationButtons){entry.Value.Background=entry.Key==name?UI.Brush(app.Settings.SelectionFill):Brushes.Transparent;entry.Value.Foreground=entry.Key==name?Brushes.White:UI.Muted;}
        switch(name){case "Appearance":Appearance();break;case "Layout":LayoutSettings();break;case "Content":ContentSettings();break;case "Launch":LaunchSettings();break;case "Controls":Interactions();break;case "Audio":AudioSettings();break;case "System":SystemSettings();break;default:General();break;}if(PreferenceReset.Categories[current].Length>0)page.Children.Add(UI.Button("Reset this category",()=>ResetPreferences(current)));OrganizeSettings();if(same){foreach(var group in SettingTree(page).OfType<Expander>())group.IsExpanded=expanded.Contains(group.Header?.ToString());page.UpdateLayout();settingsScroll.ScrollToVerticalOffset(offset);Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>{if(revision!=navigationRevision)return;var replacement=SettingTree(page).OfType<Control>().FirstOrDefault(c=>focused.Length>0 && System.Windows.Automation.AutomationProperties.GetName(c)==focused);replacement?.Focus();settingsScroll.ScrollToVerticalOffset(offset);});}RefreshPreview();
    }
    private Border Section(string title,params UIElement[] children)
    {
        var content=new StackPanel();var label=UI.Text(title,13);label.FontWeight=FontWeights.SemiBold;label.Margin=new(0,0,0,12);content.Children.Add(label);var example=new SettingsScene(title,app.Settings){Margin=new(0,0,0,16)};settingExamples.Add(example);content.Children.Add(example);foreach(var child in children){if(child is Button action){action.HorizontalAlignment=HorizontalAlignment.Left;action.Margin=new(0,8,0,0);}if(child is FrameworkElement element && element.Parent is Panel old)old.Children.Remove(child);else if(child is FrameworkElement owned && owned.Parent is Expander expander)expander.Content=null;content.Children.Add(child);}
        return new Border{Tag=title,BorderBrush=UI.Brush("#24262B"),BorderThickness=new(0,0,0,1),Padding=new(0,0,0,22),Margin=new(0,0,0,24),Child=content};
    }
    private static Expander Advanced(string title,UIElement content)
    {
        if(content is FrameworkElement element && element.Parent is Expander previous)previous.Content=null;
        return new(){Header="Advanced · "+title,Content=content,Foreground=Brushes.White,Margin=new(0,8,0,0)};
    }
    private void AddSlider(StackPanel target,string label,double value,double min,double max,Action<double> change,string unit="",double step=1)
    {
        var row=new Grid{Margin=new(0,2,0,0)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(72)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(UI.Text(label,12));var number=UI.Input(label+" value",value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture));number.Padding=new(6,4,6,4);number.Margin=new(0);number.HorizontalContentAlignment=HorizontalAlignment.Right;number.ToolTip=$"{min:0.##} to {max:0.##} {unit}";Grid.SetColumn(number,1);row.Children.Add(number);var suffix=UI.Text(unit.Trim(),10,UI.Muted);suffix.Margin=new(7,0,0,0);Grid.SetColumn(suffix,2);row.Children.Add(suffix);target.Children.Add(row);
        var slider=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=step,IsSnapToTickEnabled=true,Margin=new(0,6,0,14)};UI.Named(slider,label);slider.ValueChanged+=(_,_)=>{if(!number.IsKeyboardFocusWithin)number.Text=slider.Value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);change(slider.Value);ConfigurePreviewForSetting(label);if(label is "Panel width" or "Maximum panel height" or "Content inset" or "Content size" or "Section spacing")previewMode="Panel";else if(label.StartsWith("Preview"))previewMode="Hover";else if(label.StartsWith("Closed") || label=="Corner radius")previewMode="Closed";Save();};
        WireSettingPreview(slider,label);WireSettingPreview(number,label);
        void Commit(){if(double.TryParse(number.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double n) && double.IsFinite(n)){slider.Value=Math.Clamp(Math.Round(n/step)*step,min,max);number.Text=slider.Value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);}else{number.Text=slider.Value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);status.Text=$"Enter a number from {min:0.##} to {max:0.##} {unit}.";}}
        number.LostKeyboardFocus+=(_,_)=>Commit();number.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit();e.Handled=true;}};target.Children.Add(slider);string help=SettingDescription(label);if(help.Length>0){var hint=UI.Text(help,11,UI.Muted);hint.Margin=new(0,-6,0,16);target.Children.Add(hint);}
    }
    private CheckBox Check(string name,bool value,Action<bool> change)
    {
        var label=new StackPanel{Margin=new(0,7,0,7)};label.Children.Add(UI.Text(name,12));
        string help=SettingDescription(name);if(help.Length>0){var description=UI.Text(help,11,UI.Muted);description.Margin=new(0,4,0,0);label.Children.Add(description);}
        var check=new CheckBox{Content=label,IsChecked=value};UI.Named(check,name);check.ToolTip=help.Length>0?help:null;
        WireSettingPreview(check,name);check.Click+=(_,_)=> {change(check.IsChecked==true);ConfigurePreviewForSetting(name);Save();};return check;
    }
    private void Appearance()
    {
        var s=app.Settings;
        void Preset(string name){s.Preset=name;s.Top=name=="Mac"?0:Math.Max(8,s.Top);Save();Navigate(current);}
        page.Children.Add(Section("Shape",ShapeChoices(Preset)));
        MaterialControls();var colors=new StackPanel();ColorControl(colors,"Island color",s.Background,v=>s.Background=v,new[]{"#000000","#101114","#182028","#25221E"});ColorControl(colors,"Accent color",s.Accent,v=>s.Accent=v,new[]{"#FFFFFF","#E3D2AF","#A8C6F2","#B7D6BC"});AddSlider(colors,"Opacity",s.Opacity*100,65,100,v=>s.Opacity=v/100,"%",1);page.Children.Add(Section("Color & material",colors,Check("Black glass",s.GlassStyle,v=>s.GlassStyle=v),Check("Color from album artwork",s.AlbumAccent,v=>s.AlbumAccent=v)));
        ControlColorSettings();
        var material=new StackPanel();AddSlider(material,"Glass intensity",s.GlassStrength*100,0,100,v=>s.GlassStrength=v/100,"%");page.Children.Add(Section("Surface",material));
        page.Children.Add(Section("Text and navigation",StyleChoice("Category appearance",s.CategoryStyle,v=>s.CategoryStyle=v),UI.Text("Choose how panel navigation is labeled.",11,UI.Muted)));
        page.Children.Add(Section("Typography",Check("Bold media titles",s.BoldMediaTitle,v=>s.BoldMediaTitle=v),Check("Bold panel and tool headings",s.BoldHeadings,v=>s.BoldHeadings=v),UI.Text("Use stronger titles without changing body text, timestamps or counters. Each option is independent; the adjacent preview shows both weights.",11,UI.Muted)));
        var motion=new StackPanel();AddSlider(motion,"Animation duration",s.AnimationMs,0,600,v=>s.AnimationMs=v," ms",10);
        motion.Children.Add(UI.Text("Animations follow the Windows compositor. Actual FPS depends on your display and GPU load.",11,UI.Muted));
        motion.Children.Add(Check("Live activity · audio, progress & focus breathing",s.LiveActivity,v=>s.LiveActivity=v));


        motion.Children.Add(Check("Reduce motion",s.ReduceMotion,v=>s.ReduceMotion=v));page.Children.Add(Section("Motion",motion));
        // Keep saved profile IDs compatible while showing English labels.
        var profiles=new[]{("Auto","Auto"),("Eco","Hemat"),("Maximum","Maksimal")};
        var performance=new ComboBox();foreach(var profile in profiles)performance.Items.Add(profile.Item1);performance.SelectedIndex=Math.Max(0,Array.FindIndex(profiles,p=>p.Item2==s.Performance));UI.Named(performance,"Performance profile");performance.SelectionChanged+=(_,_)=>{if(performance.SelectedIndex>=0){s.Performance=profiles[performance.SelectedIndex].Item2;Save();}};UpdateFrameStatus();page.Children.Add(Section("Animation performance",UI.Text("Auto follows your screen refresh rate. Eco limits updates to save power; Maximum allows more frequent frames. Your display and GPU still determine visible FPS.",11,UI.Muted),performance,Advanced("Render statistics",frameStatus)));
    }
    private void UpdateFrameStatus(){if(!IsVisible || WindowState==WindowState.Minimized)return;if(frameStatus.IsVisible)Motion.Text(frameStatus,FrameTelemetry.Status);if((app.CurrentUpdate&RuntimeChange.Settings)!=0){Motion.Text(hotkeyState,app.HotkeyStatus+"\n"+app.VisibilityHotkeyStatus+"\n"+app.TuckHotkeyStatus);Motion.Text(brightnessState,app.BrightnessStatus);}if((app.CurrentUpdate&(RuntimeChange.Media|RuntimeChange.Audio))!=0)RefreshLivePreviewData();if(IsVisible && WindowState!=WindowState.Minimized && (app.CurrentUpdate&(RuntimeChange.Clock|RuntimeChange.Audio|RuntimeChange.System|RuntimeChange.Countdown))!=0)foreach(var frame in previewFrames)frame(previewClock.Elapsed.TotalSeconds);SyncPreviewRendering();}
    private void Interactions()
    {
        var s=app.Settings;
        HoverControls();InteractionFeatureControls();
        ComboBox Choice(string label,string[] labels,string[] values,string currentValue,Action<string> changed)
        {var box=new ComboBox();foreach(var item in labels)box.Items.Add(item);box.SelectedIndex=Math.Max(0,Array.IndexOf(values,currentValue));UI.Named(box,label);box.SelectionChanged+=(_,_)=>{if(box.SelectedIndex>=0){changed(values[box.SelectedIndex]);Save();}};return box;}
        page.Children.Add(Section("Automatic activity",Check("Preview new tracks & sessions ending soon",s.SmartActivities,v=>s.SmartActivities=v),UI.Text("Brief previews appear without taking focus or replacing an open panel.",12,UI.Muted)));
        var wheelStep=new StackPanel();AddSlider(wheelStep,"Volume step",s.VolumeStep,1,10,v=>s.VolumeStep=(int)v,"%");
        page.Children.Add(Section("Notch gestures",UI.Text("Mouse wheel",12,UI.Muted),Choice("Wheel action",new[]{"Volume","Switch tabs","Off"},new[]{"volume","pages","none"},s.WheelAction,v=>s.WheelAction=v),UI.Text("Middle click",12,UI.Muted),Choice("Middle-click action",new[]{"Play / pause","Mute microphone","Off"},new[]{"toggle","microphone","none"},s.MiddleAction,v=>s.MiddleAction=v),wheelStep));
        var modifier=Choice("Hotkey modifiers",new[]{"Ctrl+Alt","Ctrl+Shift","Alt+Shift"},new[]{"Ctrl+Alt","Ctrl+Shift","Alt+Shift"},s.HotkeyModifiers,v=>s.HotkeyModifiers=v);var keys=Enumerable.Range('A',26).Select(c=>((char)c).ToString()).ToArray();
        UpdateFrameStatus();page.Children.Add(Section("Global hotkey",Check("Enable open / close hotkey",s.HotkeyEnabled,v=>s.HotkeyEnabled=v),modifier,Choice("Hotkey key",keys,keys,s.HotkeyKey,v=>s.HotkeyKey=v),hotkeyState,UI.Text("Default: Ctrl+Alt+I. Use Ctrl+K for commands, Ctrl+Tab to switch tabs, and Esc to go back or close. Choose another hotkey if an app already uses it.",12,UI.Muted)));
        VisibilityControls();

    }
    private void ContentSettings()
    {
        var s=app.Settings;var widgets=new StackPanel();
        for(int i=0;i<s.Widgets.Count;i++)
        {
            var index=i;var widget=s.Widgets[i];
            var grid=new DockPanel{Margin=new(0,2,0,2)};
            var controls=UI.Row(UI.Button("↑",()=>Move(s.Widgets,index,-1)),UI.Button("↓",()=>Move(s.Widgets,index,1)));
            for(int k=0;k<controls.Children.Count;k++){var button=(Button)controls.Children[k];var icon=Icons.Create("chevron",10);icon.RenderTransformOrigin=new(.5,.5);icon.RenderTransform=new RotateTransform(k==0?180:0);button.Content=icon;button.Width=28;button.MinHeight=28;button.Padding=new(0);button.Margin=new(4,0,0,0);button.Tag=new CornerRadius(6);}
            ((Button)controls.Children[0]).IsEnabled=i>0;((Button)controls.Children[1]).IsEnabled=i<s.Widgets.Count-1;
            UI.Named(controls.Children[0],"Move up "+Settings.Names[widget.Id]);UI.Named(controls.Children[1],"Move down "+Settings.Names[widget.Id]);
            DockPanel.SetDock(controls,Dock.Right);grid.Children.Add(controls);grid.Children.Add(Check(Settings.Names[widget.Id],widget.Enabled,v=>widget.Enabled=v));widgets.Children.Add(grid);
        }
        page.Children.Add(Section("Enabled widgets & order",widgets));
        var tools=new StackPanel();foreach(var tool in s.Tools){var id=tool.Id;tools.Children.Add(Check(Settings.ToolNames[id]+" tool",tool.Enabled,v=>tool.Enabled=v));}
        page.Children.Add(Section("Individual tools",UI.Text("Hide tools you do not use. Saved files, notes, tasks and clipboard history are kept. Clipboard capture is controlled below.",11,UI.Muted),tools));
        var focus=new StackPanel();AddSlider(focus,"Focus duration",s.FocusMinutes,1,240,v=>s.FocusMinutes=(int)v," min");AddSlider(focus,"Break duration",s.BreakMinutes,1,120,v=>s.BreakMinutes=(int)v," min");
        focus.Children.Add(UI.Text("New durations apply to the next session.",11,UI.Muted));page.Children.Add(Section("Focus sessions",focus));
        page.Children.Add(Section("Stopwatch",new StopwatchView(app),UI.Text("Count elapsed time with Start, Pause, Resume and Reset. Sleep is counted, and the session survives restart. Tab to a button and press Space or Enter. Enable Stopwatch above to show it in Focus.",11,UI.Muted)));
        HoverPageSettings();
        page.Children.Add(Section("Clipboard history",Check("Save text & image history locally",s.ClipboardHistory,v=>s.ClipboardHistory=v),UI.Text("Off by default. Capture starts only when enabled. Turning it off clears saved history and images. Pause and pin items in Tools → Clipboard.",12,UI.Muted)));
    }
    private void Move<T>(System.Collections.Generic.List<T> list,int index,int delta)
    {int to=index+delta;if(to<0 || to>=list.Count)return;(list[index],list[to])=(list[to],list[index]);Save();Navigate(current);}
    private void LaunchSettings()
    {
        var s=app.Settings;
        page.Children.Add(Section("Launch display",StyleChoice("Launch appearance",s.LaunchStyle,v=>s.LaunchStyle=v)));
        page.Children.Add(Section("Activity actions",Check("Enable Activities",s.ActivitiesEnabled,v=>s.ActivitiesEnabled=v),UI.Text("Show Activities in Launch and command search. Turning this off stops the routine's owned Focus/Keep awake; apps already opened remain running.",11,UI.Muted)));
        var shortcuts=new StackPanel();shortcuts.Children.Add(UI.Button("Add installed app",()=>new AppPicker(this,shortcut=>
        {
            if(s.Shortcuts.Count>=40){status.Text="You can add up to 40 shortcuts.";return;}
            s.Shortcuts.Add(shortcut);Save();Navigate(current);
        }).ShowDialog()));
        for(int i=0;i<s.Shortcuts.Count;i++)
        {
            var index=i;var shortcut=s.Shortcuts[i];
            var row=new DockPanel{Margin=new(0,0,0,8)};
            var buttons=UI.Row(UI.Button("↑",()=>Move(s.Shortcuts,index,-1)),UI.Button("↓",()=>Move(s.Shortcuts,index,1)),UI.Button("Remove",()=> {s.Shortcuts.RemoveAt(index);Save();Navigate(current);}));
            ((Button)buttons.Children[0]).IsEnabled=i>0;((Button)buttons.Children[1]).IsEnabled=i<s.Shortcuts.Count-1;
            DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
            var detail=new StackPanel();detail.Children.Add(UI.Text(shortcut.Name));var target=UI.Text(shortcut.Target,10,UI.Muted);target.TextWrapping=TextWrapping.NoWrap;target.TextTrimming=TextTrimming.CharacterEllipsis;detail.Children.Add(target);row.Children.Add(detail);shortcuts.Children.Add(row);
        }
        var name=UI.Input("Shortcut name","","e.g. Browser");var targetInput=UI.Input("Shortcut destination","","Application path, folder, or https:// URL");
        shortcuts.Children.Add(UI.Text("Shortcut name",12,UI.Muted));shortcuts.Children.Add(name);shortcuts.Children.Add(UI.Text("Application, folder, or URL",12,UI.Muted));shortcuts.Children.Add(targetInput);
        shortcuts.Children.Add(UI.Row(UI.Button("Choose file",()=> {var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Applications & files|*.*"};if(dialog.ShowDialog(this)==true){targetInput.Text=dialog.FileName;if(string.IsNullOrWhiteSpace(name.Text))name.Text=System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);}}),UI.Button("Choose folder",()=> {var dialog=new Microsoft.Win32.OpenFolderDialog();if(dialog.ShowDialog(this)==true){targetInput.Text=dialog.FolderName;if(string.IsNullOrWhiteSpace(name.Text))name.Text=System.IO.Path.GetFileName(dialog.FolderName);}})));
        shortcuts.Children.Add(UI.Button("Add shortcut",()=>
        {
            if(string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(targetInput.Text)){status.Text="Enter a name and destination first.";return;}
            if(s.Shortcuts.Count>=40){status.Text="You can add up to 40 shortcuts.";return;}
            s.Shortcuts.Add(new(){Name=name.Text.Trim(),Target=targetInput.Text.Trim()});Save();Navigate(current);
        },true));page.Children.Add(Section("Shortcuts",shortcuts));
    }
    private void MediaAppearanceSettings()
    {
        var s=app.Settings;
        var mediaIcon=new ComboBox();mediaIcon.Items.Add("Waveform");mediaIcon.Items.Add("Album artwork");mediaIcon.SelectedIndex=s.ClosedMediaIcon=="Artwork"?1:0;UI.Named(mediaIcon,"Closed media icon");mediaIcon.SelectionChanged+=(_,_)=>{s.ClosedMediaIcon=mediaIcon.SelectedIndex==1?"Artwork":"Waveform";previewMode="Closed";previewContent="Music";Save();};

        var mediaContent=new ComboBox();mediaContent.Items.Add("Song title");mediaContent.Items.Add("Waveform");mediaContent.SelectedIndex=s.ClosedMediaContent=="Waveform"?1:0;UI.Named(mediaContent,"Closed media content");mediaContent.SelectionChanged+=(_,_)=>{s.ClosedMediaContent=mediaContent.SelectedIndex==1?"Waveform":"Title";previewMode="Closed";previewContent="Music";Save();};
        page.Children.Add(Section("Closed media display",UI.Text("Icon",12,UI.Muted),mediaIcon,UI.Text("Content",12,UI.Muted),mediaContent,UI.Text("Artwork falls back to the audio indicator when no cover is available. Hover shows the song title.",11,UI.Muted)));
    }
    private void DisplaySettings()
    {
        var s=app.Settings;
        var mode=new ComboBox{ItemsSource=new[]{"Primary","Selected","Follow cursor"},SelectedIndex=s.DisplayMode=="Follow"?2:s.DisplayMode=="Selected"?1:0};UI.Named(mode,"Display mode");
        mode.SelectionChanged+=(_,_)=>{s.DisplayMode=mode.SelectedIndex==2?"Follow":mode.SelectedIndex==1?"Selected":"Primary";Save();};
        var monitors=new ComboBox();var screens=Forms.Screen.AllScreens;
        monitors.Items.Add("Primary display (automatic)");foreach(var screen in screens) monitors.Items.Add(screen.DeviceName+" · "+screen.Bounds.Width+" × "+screen.Bounds.Height);
        monitors.SelectedIndex=Array.FindIndex(screens,m=>m.DeviceName==s.Monitor)+1;UI.Named(monitors,"Island display");
        monitors.SelectionChanged+=(_,_)=> {s.Monitor=monitors.SelectedIndex>0?screens[monitors.SelectedIndex-1].DeviceName:"";s.DisplayMode=monitors.SelectedIndex>0?"Selected":"Primary";mode.SelectedIndex=s.DisplayMode=="Selected"?1:0;Save();};
        page.Children.Add(Section("Display",mode,monitors,UI.Text(s.Monitor.Length>0 && !screens.Any(screen=>screen.DeviceName==s.Monitor)?"Saved display unavailable: "+s.Monitor+" · using Primary fallback":"Selected display is saved; cursor moves never rewrite this preference.",11,UI.Muted),UI.Text("Primary uses the main display. Selected uses the display below, falling back to Primary if disconnected. Follow cursor moves after 250 ms on another display; it waits during interaction and manual hide. Detached previews stay where placed. Example: move to a second display, pause, then continue working.",11,UI.Muted)));
    }
    private void General()
    {
        var s=app.Settings;
        UpdatesSection();NotificationSettings();
        var startup=new CheckBox{Content="Start automatically when signing in to Windows",IsChecked=s.AutoStart};
        UI.Named(startup,"Start automatically when signing in to Windows");        startup.Click+=(_,_)=>
        {
            try {StartupService.Set(startup.IsChecked==true);s.AutoStart=startup.IsChecked==true;Save();}
            catch(Exception ex) {startup.IsChecked=StartupService.Enabled();s.AutoStart=startup.IsChecked==true;status.Text="Could not change startup: "+ex.Message;app.Store.Log(ex);}
        };
        page.Children.Add(Section("Behavior",startup,UI.Text("Launch NabyScape for this Windows user when you sign in. You can turn this off at any time.",11,UI.Muted),UI.Text("NabyScape keeps running when you close Settings. Quit from the system tray.",12,UI.Muted)));
        page.Children.Add(Section("Local storage",UI.Text("Preferences and timers stay on this device. No account required.",12,UI.Muted),UI.Button("Open settings folder",()=>ShortcutService.Open(new(){Target=app.Store.DirectoryPath})),UI.Button("Quit NabyScape",app.Quit)));
        var resetCategory=new ComboBox{ItemsSource=PreferenceReset.Categories.Where(pair=>pair.Value.Length>0).Select(pair=>pair.Key).ToArray(),SelectedIndex=0};UI.Named(resetCategory,"Preference reset category");
        page.Children.Add(Section("Reset preferences",UI.Text(PreferenceReset.Summary(null),11,UI.Muted),UI.Text("Choose a category to reset only its preferences; for example Appearance keeps your current layout.",11,UI.Muted),resetCategory,UI.Button("Reset this category",()=>ResetPreferences(resetCategory.SelectedItem?.ToString()??"Appearance")),UI.Button("Reset all preferences",()=>ResetPreferences(null)),UI.Button("Restore last preference backup",RestorePreferences)));
        page.Children.Add(Section("About NabyScape",UI.Text("Version "+typeof(App).Assembly.GetName().Version!.ToString(3)+" · Windows 11 · x64",14),UI.Text("Media controls depend on support from your media app. Battery appears on devices with a battery. Transparency controls opacity; glass uses a dark gradient and reflections.",12,UI.Muted),UI.Text("Focus sessions run in NabyScape and do not change Windows Do Not Disturb.",12,UI.Muted)));
    }
}
