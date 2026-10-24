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
    private readonly TextBlock status=UI.Caption("Changes save automatically on this device.",11,UI.Muted);
    private readonly Border preview;
    private readonly Border previewStage;
    private readonly DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private readonly System.Collections.Generic.Dictionary<string,Button> navigationButtons=new();
    private readonly Border sectionIndexHost=new(){Margin=new(28,12,28,8)};
    private int navigationRevision,previewRevision;
    private int? previewBuildKey;
    private bool applyingSettings;
    private readonly Button retrySave=UI.CaptionButton("Retry save",()=>{});
    private System.Text.Json.JsonElement savedSettings;
    private Action? refreshSettingsLayout;
    private readonly System.Collections.Generic.List<SettingsScene> settingExamples=new();
    private string current="Home";
    private readonly TextBlock categoryHeading=UI.Caption("",26),categoryDescription=UI.Caption("",13,UI.Muted);
    private readonly TextBlock previewHelp=UI.Caption("",11,UI.Muted);
    private ComboBox? categorySelector;
    private AssistantProviderSettings? aiSettings;
    internal void OpenAiSettings(){Navigate("AI");aiSettings?.OpenSetupOverview();}
    internal void OpenAiHermesSettings(){Navigate("AI");Dispatcher.BeginInvoke(()=>aiSettings?.OpenHermesSetup());}
    internal void OpenAiVoiceSettings(){Navigate("AI");Dispatcher.BeginInvoke(()=>aiSettings?.OpenVoiceSetup());}
    private void AiSettings(){if(aiSettings==null){aiSettings=new(app);aiSettings.FeedbackChanged+=SetSaveFeedback;}page.Children.Add(aiSettings);}

    private readonly TextBlock frameStatus=UI.Caption("",11,UI.Muted);
    private readonly TextBlock hotkeyState=UI.Caption("",11,UI.Muted);
    public SettingsWindow(App app)
    {
        this.app=app;savedSettings=System.Text.Json.JsonSerializer.SerializeToElement(app.Settings);UI.LocalizedName(status,"Settings status");Accessibility.Live(status);Title=Localization.Text("NabyScape · Settings");Width=1100;Height=780;MinWidth=480;MinHeight=300;WindowStartupLocation=WindowStartupLocation.Manual;
        Background=Accessibility.HighContrast?SystemColors.WindowBrush:UI.Brush("#111214");Foreground=Accessibility.HighContrast?SystemColors.WindowTextBrush:UI.Brush("#F3F4F6");FontFamily=new FontFamily("Segoe UI Variable, Segoe UI");FontSize=13;
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/island.ico"));
        Resources[typeof(CheckBox)]=FindResource("SettingsSwitch");
        InitializeSettingsBounds();InitializeMaterial();
        var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.ColumnDefinitions.Add(new(){Width=new GridLength(228)});root.ColumnDefinitions.Add(new());
        var navigation=new StackPanel{Orientation=Orientation.Vertical,Margin=new(14,26,12,18)};var brand=UI.Caption("NabyScape",20);brand.TextWrapping=TextWrapping.NoWrap;brand.FontWeight=FontWeights.SemiBold;brand.Margin=new(12,0,0,28);navigation.Children.Add(brand);
        string lastGroup="";
        foreach(var category in SettingsCategories)
        {
            if(category.Group!=lastGroup){var label=UI.Caption(category.Group,10,UI.Muted);label.Margin=new(12,category.Group=="Personalize"?0:18,0,6);navigation.Children.Add(label);lastGroup=category.Group;}
            var item=category;var button=UI.CaptionButton(item.Title,()=>Navigate(item.Id));button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Padding=new(12,8,12,8);button.Margin=new(0,1,0,1);button.Tag=new CornerRadius(8);button.FontSize=13;button.MinHeight=40;button.ToolTip=Localization.Text(item.Description);navigationButtons[item.Id]=button;navigation.Children.Add(button);
        }
        var navigationScroll=UI.Scroll(navigation);UI.LocalizedName(navigationScroll,"Settings category viewport");
        var navBorder=new Border{Background=UI.Brush("#141619"),BorderBrush=UI.Brush("#24262B"),BorderThickness=new(0,0,1,0),Child=navigationScroll};root.Children.Add(navBorder);
        var categories=new ComboBox{Margin=new(16,7,16,7),ItemsSource=SettingsCategories.Select(c=>Localization.Text(c.Title)).ToArray(),SelectedItem=CategoryTitle(current),Visibility=Visibility.Collapsed};categorySelector=categories;UI.LocalizedName(categories,"Settings category");
        categories.SelectionChanged+=(_,_)=>{if(categories.SelectedItem is string name && CategoryId(name)!=current)Navigate(CategoryId(name));};Grid.SetColumnSpan(categories,2);root.Children.Add(categories);
        var main=new Grid();Grid.SetColumn(main,1);Grid.SetRowSpan(main,2);Grid.SetRowSpan(navBorder,2);root.Children.Add(main);main.RowDefinitions.Add(new(){Height=GridLength.Auto});main.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});main.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var workspace=new Grid();workspace.RowDefinitions.Add(new(){Height=GridLength.Auto});workspace.RowDefinitions.Add(new(){Height=GridLength.Auto});workspace.RowDefinitions.Add(new());workspace.ColumnDefinitions.Add(new());workspace.ColumnDefinitions.Add(new(){Width=new GridLength(300)});Grid.SetRow(workspace,1);main.Children.Add(workspace);InitializeSettingsSearch(main,workspace);
        var pageHeader=new StackPanel{Margin=new(30,18,30,16)};categoryHeading.FontWeight=FontWeights.SemiBold;pageHeader.Children.Add(categoryHeading);categoryDescription.Margin=new(0,6,0,0);pageHeader.Children.Add(categoryDescription);Grid.SetRow(pageHeader,0);workspace.Children.Add(pageHeader);
        var form=new Grid();form.RowDefinitions.Add(new(){Height=GridLength.Auto});form.RowDefinitions.Add(new());Grid.SetRow(form,2);workspace.Children.Add(form);
        var scroll=UI.Scroll(page);scroll.Padding=new(0,0,8,0);form.Children.Add(scroll);Grid.SetRow(scroll,1);settingsScroll=scroll;InitializePinnedAmbientPreview(form,scroll);Grid.SetRow(sectionIndexHost,1);workspace.Children.Add(sectionIndexHost);
        preview=new Border{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top};
        var previewColumn=new StackPanel{Margin=new(20,28,20,20)};var previewTitle=UI.Caption("Live preview",14);previewTitle.FontWeight=FontWeights.SemiBold;previewColumn.Children.Add(previewTitle);previewFocus.Margin=new(0,7,0,0);previewColumn.Children.Add(previewFocus);
        var modes=new Grid{Margin=new(0,14,0,12)};for(int i=0;i<3;i++)modes.ColumnDefinitions.Add(new());int column=0;foreach(string mode in new[]{"Closed","Hover","Panel"}){var button=UI.CaptionButton(mode,()=>{previewMode=mode;RefreshPreview();});button.FontSize=10;button.Padding=new(3,7,3,7);button.Margin=new(0,0,4,0);button.Tag=new CornerRadius(7);UI.Named(button,"Preview "+mode);Grid.SetColumn(button,column++);modes.Children.Add(button);previewModeButtons[mode]=button;}previewColumn.Children.Add(modes);
        previewStage=new Border{Height=238,Background=UI.Brush("#202226"),CornerRadius=new(10),ClipToBounds=true,Child=new System.Windows.Controls.Viewbox{Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Child=preview,VerticalAlignment=VerticalAlignment.Top}};previewColumn.Children.Add(previewStage);
        previewDimensions.Margin=new(0,12,0,18);previewColumn.Children.Add(previewDimensions);var sample=UI.Caption("Preview content",11,UI.Muted);sample.Margin=new(0,0,0,7);previewColumn.Children.Add(sample);var examples=new ComboBox();previewExamples=examples;foreach(string item in new[]{"Music","Timer","Focus","Stopwatch","Calendar","Note","Shelf","Tools","System","Launch","Navigation","Volume feedback","Drop zone","Detached preview","Cover gestures","Smart clipboard","Control colors","Typography","Panel navigation","Visibility","Tasks","Startup","AI"})examples.Items.Add(item);examples.SelectedIndex=0;UI.LocalizedName(examples,"Preview content");examples.SelectionChanged+=(_,_)=>{previewContent=examples.SelectedItem?.ToString()??"Music";RefreshPreview();};previewColumn.Children.Add(examples);var hint=previewHelp;hint.Margin=new(0,15,0,0);previewColumn.Children.Add(hint);
        var previewExpander=new Expander{Header=UI.Caption("Live preview",12,UI.Muted),Foreground=UI.Muted,Margin=new(8,0,0,0),Content=UI.Scroll(previewColumn),IsExpanded=true};
        var rail=new Border{BorderBrush=UI.Brush("#24262B"),BorderThickness=new(1,0,0,0),Child=previewExpander};Grid.SetColumn(rail,1);Grid.SetRowSpan(rail,3);workspace.Children.Add(rail);
        var saveRow=new DockPanel();undoSettings.Visibility=Visibility.Collapsed;undoSettings.Click+=(_,_)=>UndoSettings();DockPanel.SetDock(undoSettings,Dock.Right);saveRow.Children.Add(undoSettings);retrySave.Visibility=Visibility.Collapsed;retrySave.HorizontalAlignment=HorizontalAlignment.Right;retrySave.Click+=(_,_)=>CommitSettings();DockPanel.SetDock(retrySave,Dock.Right);saveRow.Children.Add(retrySave);saveRow.Children.Add(status);
        var footer=new Border{Padding=new(26,12,26,14),BorderBrush=UI.Brush("#24262B"),BorderThickness=new(0,1,0,0),Background=Accessibility.HighContrast?SystemColors.WindowBrush:UI.Brush("#181B20"),Child=saveRow};
        bool? previousNarrow=null;
        bool? previousShort=null;
        void Responsive()
        {
            bool noPreview=current is not ("Appearance" or "Ambience" or "Layout" or "Motion" or "Audio");
            bool narrow=ActualWidth<1100,compactNav=ActualWidth<760,shortScreen=ActualHeight<600,veryShort=ActualHeight<340;
            categories.Margin=new Thickness(16,veryShort?3:7,16,veryShort?3:7);
            if(settingsSearchHeader!=null)settingsSearchHeader.Margin=veryShort?new Thickness(20,4,20,4):new Thickness(20,13,20,9);
            footer.Padding=veryShort?new Thickness(20,4,20,6):new Thickness(26,12,26,14);
            root.ColumnDefinitions[0].Width=compactNav?new GridLength(0):new GridLength(228);
            navBorder.Visibility=compactNav?Visibility.Collapsed:Visibility.Visible;categories.Visibility=compactNav?Visibility.Visible:Visibility.Collapsed;
            if(categories.SelectedItem?.ToString()!=CategoryTitle(current))categories.SelectedItem=CategoryTitle(current);
            Grid.SetRow(main,compactNav?1:0);Grid.SetRowSpan(main,compactNav?1:2);Grid.SetColumn(main,compactNav?0:1);Grid.SetColumnSpan(main,compactNav?2:1);
            rail.Visibility=noPreview?Visibility.Collapsed:Visibility.Visible;
            workspace.ColumnDefinitions[1].Width=narrow || noPreview?new GridLength(0):new GridLength(284);Grid.SetColumn(rail,narrow?0:1);Grid.SetRowSpan(rail,narrow?1:3);
            if(previousNarrow!=narrow || previousShort!=shortScreen){previewExpander.IsExpanded=!narrow && !shortScreen;previousNarrow=narrow;previousShort=shortScreen;}
            previewTitle.Visibility=Visibility.Collapsed;
            double previewHeight=Math.Clamp(workspace.ActualHeight-sectionIndexHost.ActualHeight-100,50,210);
            previewExpander.MaxHeight=narrow?previewHeight:double.PositiveInfinity;
            rail.Height=double.NaN;
            modes.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
            previewStage.Height=narrow?Math.Max(40,previewHeight-65):238;
            previewColumn.Margin=narrow?new Thickness(18,6,18,8):new Thickness(18,18,18,18);hint.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;previewDimensions.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;sample.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;examples.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
            sectionIndexHost.Margin=narrow?(veryShort?new Thickness(20,3,20,2):new Thickness(20,8,20,4)):new Thickness(28,14,28,4);
            categoryDescription.Visibility=shortScreen?Visibility.Collapsed:Visibility.Visible;
            categoryHeading.FontSize=shortScreen?18:26;
            pageHeader.Margin=new Thickness(narrow?20:30,shortScreen?3:16,narrow?20:30,shortScreen?3:16);
            page.Margin=narrow?new Thickness(20,8,20,24):new Thickness(30,8,30,32);
            double available=workspace.ActualWidth-(narrow || noPreview?0:284)-80;
            page.Width=Math.Max(260,Math.Min(900,available));
        }
        refreshSettingsLayout=Responsive;
        UI.LocalizedName(previewExpander,"Settings live preview");UI.LocalizedName(scroll,"Settings form viewport");UI.LocalizedName(rail,"Settings preview rail");UI.LocalizedName(navBorder,"Settings navigation");
        SizeChanged+=(_,_)=>Responsive();workspace.SizeChanged+=(_,_)=>Responsive();Loaded+=(_,_)=>Responsive();
        page.MaxWidth=900;page.HorizontalAlignment=HorizontalAlignment.Left;
        Grid.SetRow(footer,2);main.Children.Add(footer);Content=SettingsFrame(root);InitializePreviewMotion(previewColumn);InitializeAmbientPreview();
        saveTimer.Tick+=(_,_)=>CommitSettings();
        app.Updates.Changed+=UpdateControls;
        app.SettingsChanged+=SettingsApplied;app.Updated+=UpdateFrameStatus;
        bool closeApproved=false,closePrompt=false;
        Closing+=async(_,e)=>{if(closeApproved || aiSettings?.HasProviderDraft!=true)return;e.Cancel=true;if(closePrompt)return;closePrompt=true;try{if(await aiSettings.ConfirmProviderChanges()){closeApproved=true;_ =Dispatcher.BeginInvoke(new Action(()=>{if(IsLoaded)Close();}));}}finally{closePrompt=false;}};
        Closed+=(_,_)=>{aiSettings?.ClearSecrets();if(saveTimer.IsEnabled)CommitSettings();app.Updates.Changed-=UpdateControls;app.SettingsChanged-=SettingsApplied;app.Updated-=UpdateFrameStatus;};InitializeHomeStatuses();Navigate("Home");RefreshPreview();
    }
    private void CommitSettings()
    {
        saveTimer.Stop();
        try
        {
            applyingSettings=true;app.ChangeSettings();
            if(!app.SettingsSaveSucceeded){SetSaveFeedback("Not saved · changes are active for this session only. Choose Retry save.",true);return;}
            CaptureSettingsUndo(savedSettings);
            string summary=SettingsChangeFeedback.Describe(savedSettings,app.Settings);
            savedSettings=System.Text.Json.JsonSerializer.SerializeToElement(app.Settings);
            SetSaveFeedback(summary);
        }
        catch(Exception ex){SetSaveFeedback("Could not save changes. Choose Retry save.",true);app.Store.Log(ex);}
        finally{applyingSettings=false;}
    }
    private void SetSaveFeedback(string text,bool error=false)
    {
        retrySave.Visibility=error && current!="AI"?Visibility.Visible:Visibility.Collapsed;
        status.Foreground=Accessibility.HighContrast?SystemColors.WindowTextBrush:error?UI.Brush("#F0B8B2"):UI.Brush("#F2F3F5");
        Accessibility.Announce(status,Localization.Text(text));status.ToolTip=text;
    }
    private void Save() {previewRevision++;InvalidateExamples(System.Windows.Input.Keyboard.FocusedElement as DependencyObject??System.Windows.Input.Mouse.DirectlyOver as DependencyObject);RefreshPreview();Accessibility.Announce(status,Localization.Text("Saving changes…"));saveTimer.Stop();saveTimer.Start();}
    private void SettingsApplied(){ApplyMaterial();if(applyingSettings)return;if(app.SettingsSaveSucceeded){CaptureSettingsUndo(savedSettings);SetSaveFeedback(SettingsChangeFeedback.Describe(savedSettings,app.Settings));savedSettings=System.Text.Json.JsonSerializer.SerializeToElement(app.Settings);}else SetSaveFeedback("Not saved · changes are active for this session only. Choose Retry save.",true);previewRevision++;InvalidateExamples(null);RefreshPreview();}
    private void InvalidateExamples(DependencyObject? source)
    {
        for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
            if(node is Border {Tag:string title}){var example=settingExamples.FirstOrDefault(e=>e.Section==title);if(example!=null){example.InvalidateVisual();return;}}
        foreach(var example in settingExamples)example.InvalidateVisual();
    }
    private void RefreshPreview()
    {
        pinnedAmbientScene?.InvalidateVisual();
        RefreshAmbientPreview();
        int key=HashCode.Combine(HashCode.Combine(current,previewMode,previewContent,previewRevision),app.Media.Title,app.Media.Artist,app.Media.Playing,app.Media.Artwork,app.Audio.Volume,app.Audio.Muted);
        if(previewBuildKey!=key){previewBuildKey=key;RenderSettingsPreview();}SyncPreviewRendering();
    }
    private void Navigate(string name)
    {
        if(searchResultsHost!=null)searchResultsHost.Visibility=Visibility.Collapsed;
        Accessibility.Announce(status,Localization.Text(name=="AI"?"Connection fields use Save provider. Voice and wake word changes use their Save buttons.":"Changes save automatically on this device."));previewHelp.Text=Localization.Text(name=="AI"?"Preview only. Apply connection edits with Save provider.":"Changes apply automatically. The preview uses your current shape, size and colors.");
        bool same=name==current && page.Children.Count>0;double offset=settingsScroll.VerticalOffset;int revision=++navigationRevision;
        var expanded=SettingTree(page).OfType<Expander>().Where(e=>e.IsExpanded).Select(e=>e.Header?.ToString()).ToHashSet();
        string focused=System.Windows.Input.Keyboard.FocusedElement is DependencyObject oldFocus?UI.Identity(oldFocus):"";
        previewRevision++;settingExamples.Clear();HidePinnedAmbientPreview();current=name;categoryHeading.Text=CategoryTitle(name);categoryDescription.Text=Localization.Text(Category(name).Description);if(categorySelector!=null && categorySelector.SelectedItem?.ToString()!=CategoryTitle(name))categorySelector.SelectedItem=CategoryTitle(name);previewFocus.Text=name+" overview";previewContent="Music";previewMode="Closed";if(name=="Launch"){previewContent="Launch";previewMode="Panel";}if(name=="Content"){previewContent="Navigation";previewMode="Panel";}if(name=="Controls"){previewContent="Music";previewMode="Hover";}if(name=="System"){previewContent="System";previewMode="Panel";}if(name=="Audio"){previewContent="Music";previewMode="Panel";}if(name=="AI"){previewContent="AI";previewMode="Panel";}if(name=="Application"){previewContent="Startup";previewMode="Panel";}if(!same)settingsScroll.ScrollToTop();page.Children.Clear();
        foreach(var entry in navigationButtons){entry.Value.FontWeight=entry.Key==name?FontWeights.SemiBold:FontWeights.Normal;entry.Value.Background=entry.Key==name?UI.Brush(app.Settings.SelectionFill):Brushes.Transparent;entry.Value.Foreground=entry.Key==name?Brushes.White:UI.Muted;}
        switch(SettingsRoot(name)){case "Home":SettingsHome();break;case "Weather":WeatherSettingsPage();break;case "Appearance":Appearance();break;case "Layout":LayoutSettings();break;case "Content":ContentSettings();break;case "Launch":LaunchSettings();break;case "Controls":Interactions();break;case "Audio":AudioSettings();break;case "System":SystemSettings();break;case "AI":AiSettings();break;default:General();break;}FilterSettingsCategory(name);if(PreferenceReset.Categories.TryGetValue(current,out var resetKeys) && resetKeys.Length>0)page.Children.Add(UI.CaptionButton("Reset this category",()=>ResetPreferences(current)));OrganizeSettings();if(same){foreach(var group in SettingTree(page).OfType<Expander>())group.IsExpanded=expanded.Contains(group.Header?.ToString());page.UpdateLayout();settingsScroll.ScrollToVerticalOffset(offset);Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>{if(revision!=navigationRevision)return;var replacement=SettingTree(page).OfType<Control>().FirstOrDefault(c=>focused.Length>0 && UI.Identity(c)==focused);replacement?.Focus();settingsScroll.ScrollToVerticalOffset(offset);});}refreshSettingsLayout?.Invoke();RefreshPreview();
    }
    private Border Section(string title,params UIElement[] children)
    {
        var content=new StackPanel();var label=UI.Caption(FriendlySection(title),16);label.FontWeight=FontWeights.SemiBold;label.Margin=new(0,0,0,12);content.Children.Add(label);
        // The dedicated preview rail already shows visual changes; avoid duplicate preview drawers.
        var details=new StackPanel();
        foreach(var child in children)
        {
            if(child is Button action){action.HorizontalAlignment=HorizontalAlignment.Left;action.Margin=new(0,8,0,0);}
            if(child is FrameworkElement element && element.Parent is Panel old)old.Children.Remove(child);else if(child is FrameworkElement owned && owned.Parent is Expander expander)expander.Content=null;
            if(child is TextBlock paragraph && paragraph.Text.Length>220 && title!="Clipboard history")details.Children.Add(child);else content.Children.Add(child);
        }
        if(details.Children.Count>0)content.Children.Add(new Expander{Header=Localization.Text("More details")+" · "+Localization.Text(FriendlySection(title)),Content=details,Foreground=UI.Muted,IsExpanded=false,FontSize=11,Margin=new(0,10,0,0)});
        return new Border{Tag=title,Background=UI.Brush("#17191D"),BorderBrush=UI.Brush("#2A2E36"),BorderThickness=new(.7),CornerRadius=new(12),Padding=new(16),Margin=new(0,0,0,15),Child=content};
    }
    private static Expander Advanced(string title,UIElement content)
    {
        if(content is FrameworkElement element && element.Parent is Expander previous)previous.Content=null;
        return new(){Header=Localization.Text("Advanced")+" · "+Localization.Text(title),Content=content,Foreground=Brushes.White,Margin=new(0,8,0,0)};
    }
    private void AddSlider(StackPanel target,string label,double value,double min,double max,Action<double> change,string unit="",double step=1)
    {
        var row=new Grid{Margin=new(0,2,0,0)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(72)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(UI.Caption(label,12));var number=UI.Input(label+" value",value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture));number.Padding=new(6,4,6,4);number.Margin=new(0);number.HorizontalContentAlignment=HorizontalAlignment.Right;number.ToolTip=$"{min:0.##} to {max:0.##} {unit}";Grid.SetColumn(number,1);row.Children.Add(number);var suffix=UI.Text(unit.Trim(),10,UI.Muted);suffix.Margin=new(7,0,0,0);Grid.SetColumn(suffix,2);row.Children.Add(suffix);target.Children.Add(row);
        var slider=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=step,IsSnapToTickEnabled=true,Margin=new(0,6,0,14)};UI.LocalizedName(slider,label);slider.ValueChanged+=(_,_)=>{if(!number.IsKeyboardFocusWithin)number.Text=slider.Value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);change(slider.Value);ConfigurePreviewForSetting(label);if(label is "Panel width" or "Maximum panel height" or "Content inset" or "Content size" or "Section spacing")previewMode="Panel";else if(label.StartsWith("Preview"))previewMode="Hover";else if(label.StartsWith("Closed") || label=="Corner radius")previewMode="Closed";Save();};
        WireSettingPreview(slider,label);WireSettingPreview(number,label);
        void Commit(){if(double.TryParse(number.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double n) && double.IsFinite(n)){slider.Value=Math.Clamp(Math.Round(n/step)*step,min,max);number.Text=slider.Value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);}else{number.Text=slider.Value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);Accessibility.Announce(status,Localization.Format("Enter a number from {0} to {1} {2}.",min.ToString("0.##"),max.ToString("0.##"),unit));}}
        number.LostKeyboardFocus+=(_,_)=>Commit();number.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit();e.Handled=true;}};target.Children.Add(slider);string help=SettingDescription(label);if(help.Length>0){var hint=UI.Caption(help,11,UI.Muted);hint.Margin=new(0,-6,0,16);target.Children.Add(hint);}
    }
    private CheckBox Check(string name,bool value,Action<bool> change)
    {
        var label=new StackPanel{Margin=new(0,7,0,7)};var stateLabel=UI.Text(Localization.Text(name)+" · "+Localization.Text(value?"On":"Off"),12);label.Children.Add(stateLabel);
        string help=SettingDescription(name);if(help.Length>0){var description=UI.Caption(help,11,UI.Muted);description.Margin=new(0,4,0,0);label.Children.Add(description);}
        var check=new CheckBox{Content=label,IsChecked=value};UI.LocalizedName(check,name);check.ToolTip=help.Length>0?help:null;
        check.Checked+=(_,_)=>stateLabel.Text=Localization.Text(name)+" · "+Localization.Text("On");check.Unchecked+=(_,_)=>stateLabel.Text=Localization.Text(name)+" · "+Localization.Text("Off");
        WireSettingPreview(check,name);check.Click+=(_,_)=> {change(check.IsChecked==true);ConfigurePreviewForSetting(name);Save();};return check;
    }
    private void Appearance()
    {
        var s=app.Settings;
        void Preset(string name){s.Preset=name;s.Top=name=="Mac"?0:Math.Max(8,s.Top);Save();Navigate(current);}
        page.Children.Add(Section("Shape",ShapeChoices(Preset)));
        MaterialControls();var colors=new StackPanel();ColorControl(colors,"Island color",s.Background,v=>s.Background=v,new[]{"#000000","#101114","#182028","#25221E"});ColorControl(colors,"Accent color",s.Accent,v=>s.Accent=v,new[]{"#FFFFFF","#E3D2AF","#A8C6F2","#B7D6BC"});AddSlider(colors,"Opacity",s.Opacity*100,65,100,v=>s.Opacity=v/100,"%",1);page.Children.Add(Section("Color & material",colors,Check("Black glass",s.GlassStyle,v=>s.GlassStyle=v),Check("Color from album artwork",s.AlbumAccent,v=>s.AlbumAccent=v)));
        AmbientSettings();ControlColorSettings();
        var material=new StackPanel();AddSlider(material,"Glass intensity",s.GlassStrength*100,0,100,v=>s.GlassStrength=v/100,"%");page.Children.Add(Section("Surface",material));
        page.Children.Add(Section("Text and navigation",UI.Caption("Dots keep your panels easy to reach. Hover over a dot to see its name.",11,UI.Muted)));
        page.Children.Add(Section("Typography",Check("Bold media titles",s.BoldMediaTitle,v=>s.BoldMediaTitle=v),Check("Bold panel and tool headings",s.BoldHeadings,v=>s.BoldHeadings=v),UI.Caption("Use stronger titles without changing body text, timestamps or counters. Each option is independent; the adjacent preview shows both weights.",11,UI.Muted)));
        var motion=new StackPanel();motion.Children.Add(PremiumChoice("Interaction feedback",s.InteractionMotion,new[]{"Off","Subtle","Expressive"},v=>s.InteractionMotion=v));AddSlider(motion,"Animation duration",s.AnimationMs,0,600,v=>s.AnimationMs=v," ms",10);
        motion.Children.Add(UI.Caption("Animations follow the Windows compositor. Actual FPS depends on your display and GPU load.",11,UI.Muted));
        motion.Children.Add(Check("Live activity · audio, progress & focus breathing",s.LiveActivity,v=>s.LiveActivity=v));


        motion.Children.Add(Check("Reduce motion",s.ReduceMotion,v=>s.ReduceMotion=v));page.Children.Add(Section("Motion",motion));
        // Keep saved profile IDs compatible while showing English labels.
        var profiles=new[]{("Auto (recommended)","Auto"),("Save power","Hemat"),("Smoother motion","Maksimal")};
        var performance=new ComboBox();foreach(var profile in profiles)performance.Items.Add(profile.Item1);performance.SelectedIndex=Math.Max(0,Array.FindIndex(profiles,p=>p.Item2==s.Performance));UI.LocalizedName(performance,"Performance profile");performance.SelectionChanged+=(_,_)=>{if(performance.SelectedIndex>=0){s.Performance=profiles[performance.SelectedIndex].Item2;Save();}};UpdateFrameStatus();page.Children.Add(Section("Animation performance",UI.Caption("Auto follows your display. Save power reduces update frequency; Smoother motion allows more frequent frames.",11,UI.Muted),performance,Advanced("Render statistics",frameStatus)));
    }
    private void UpdateFrameStatus(){if(!IsVisible || WindowState==WindowState.Minimized)return;if(frameStatus.IsVisible)Motion.Text(frameStatus,FrameTelemetry.Status);if((app.CurrentUpdate&RuntimeChange.Settings)!=0){Motion.Text(hotkeyState,app.HotkeyStatus+"\n"+app.VisibilityHotkeyStatus+"\n"+app.TuckHotkeyStatus);Motion.Text(brightnessState,app.BrightnessStatus);}if((app.CurrentUpdate&(RuntimeChange.Media|RuntimeChange.Audio))!=0)RefreshLivePreviewData();if(IsVisible && WindowState!=WindowState.Minimized && (app.CurrentUpdate&(RuntimeChange.Clock|RuntimeChange.Audio|RuntimeChange.System|RuntimeChange.Countdown))!=0)foreach(var frame in previewFrames)frame(previewClock.Elapsed.TotalSeconds);SyncPreviewRendering();}
    private void Interactions()
    {
        var s=app.Settings;
        HoverControls();InteractionFeatureControls();
        ComboBox Choice(string label,string[] labels,string[] values,string currentValue,Action<string> changed)
        {var box=new ComboBox();foreach(var item in labels)box.Items.Add(item);box.SelectedIndex=Math.Max(0,Array.IndexOf(values,currentValue));UI.Named(box,label);box.SelectionChanged+=(_,_)=>{if(box.SelectedIndex>=0){changed(values[box.SelectedIndex]);Save();}};return box;}
        page.Children.Add(Section("Automatic activity",Check("Preview new tracks & sessions ending soon",s.SmartActivities,v=>s.SmartActivities=v),Check("Automatic browser media previews",s.BrowserMediaPreviews,v=>s.BrowserMediaPreviews=v),UI.Caption("Turn off to suppress automatic browser previews, including YouTube hover playback. Unknown Windows media identities are also suppressed. Manual island hover and playback controls still work.",11,UI.Muted),UI.Caption("Brief previews appear without taking focus or replacing an open panel.",12,UI.Muted)));
        var wheelStep=new StackPanel();AddSlider(wheelStep,"Volume step",s.VolumeStep,1,10,v=>s.VolumeStep=(int)v,"%");
        page.Children.Add(Section("Notch gestures",UI.Caption("Mouse wheel",12,UI.Muted),Choice("Wheel action",new[]{"Volume","Switch tabs","Off"},new[]{"volume","pages","none"},s.WheelAction,v=>s.WheelAction=v),UI.Caption("Middle click",12,UI.Muted),Choice("Middle-click action",new[]{"Play / pause","Mute microphone","Off"},new[]{"toggle","microphone","none"},s.MiddleAction,v=>s.MiddleAction=v),wheelStep));
        var modifier=Choice("Hotkey modifiers",new[]{"Ctrl+Alt","Ctrl+Shift","Alt+Shift"},new[]{"Ctrl+Alt","Ctrl+Shift","Alt+Shift"},s.HotkeyModifiers,v=>s.HotkeyModifiers=v);var keys=Enumerable.Range('A',26).Select(c=>((char)c).ToString()).ToArray();
        UpdateFrameStatus();page.Children.Add(Section("Global hotkey",Check("Enable open / close hotkey",s.HotkeyEnabled,v=>s.HotkeyEnabled=v),modifier,Choice("Hotkey key",keys,keys,s.HotkeyKey,v=>s.HotkeyKey=v),hotkeyState,UI.Caption("Default: Ctrl+Alt+I. Use Ctrl+K for commands, Ctrl+Tab to switch tabs, and Esc to go back or close. Choose another hotkey if an app already uses it.",12,UI.Muted)));
        VisibilityControls();

    }
    private void ContentSettings()
    {
        var s=app.Settings;FeatureResourceSettings();var widgets=new StackPanel();
        widgets.Children.Add(UI.Caption("Drag the grip to arrange your widgets. Switch any widget on or off.",11,UI.Muted));
        widgets.Children.Add(WidgetOrder(s.Widgets,"widgets",RefreshFeatureUsage));
        page.Children.Add(Section("Enabled widgets & order",widgets));
        page.Children.Add(UI.Caption("Each tool has its own panel above. Enable and reorder Notes, Tasks, Shelf, Clipboard, Calendar, Keep awake and Clean links alongside your other widgets. Saved content is preserved.",11,UI.Muted));
        var focus=new StackPanel();AddSlider(focus,"Focus duration",s.FocusMinutes,1,240,v=>s.FocusMinutes=(int)v," min");AddSlider(focus,"Break duration",s.BreakMinutes,1,120,v=>s.BreakMinutes=(int)v," min");
        focus.Children.Add(UI.Caption("New durations apply to the next session.",11,UI.Muted));page.Children.Add(Section("Focus sessions",focus));
        page.Children.Add(Section("Stopwatch",new StopwatchView(app),UI.Caption("Count elapsed time with Start, Pause, Resume and Reset. Sleep is counted, and the session survives restart. Tab to a button and press Space or Enter. Enable Stopwatch above to show it in Focus.",11,UI.Muted)));
        HoverPageSettings();
        page.Children.Add(Section("Clipboard history",Check("Save text & image history locally",s.ClipboardHistory,v=>s.ClipboardHistory=v),UI.Caption("Off by default. Capture starts only when enabled. Turning it off clears saved history and images. Pause and pin items in Tools → Clipboard.",12,UI.Muted)));
    }
    private void Move<T>(System.Collections.Generic.List<T> list,int index,int delta)
    {int to=index+delta;if(to<0 || to>=list.Count)return;(list[index],list[to])=(list[to],list[index]);Save();Navigate(current);}
    private void LaunchSettings()
    {
        var s=app.Settings;
        page.Children.Add(Section("Launch display",StyleChoice("Launch appearance",s.LaunchStyle,v=>s.LaunchStyle=v)));
        var shortcuts=new StackPanel();shortcuts.Children.Add(UI.CaptionButton("Add installed app",()=>new AppPicker(this,shortcut=>
        {
            if(s.Shortcuts.Count>=40){Accessibility.Announce(status,Localization.Text("You can add up to 40 shortcuts."));return;}
            s.Shortcuts.Add(shortcut);Save();Navigate(current);
        }).ShowDialog()));
        for(int i=0;i<s.Shortcuts.Count;i++)
        {
            var index=i;var shortcut=s.Shortcuts[i];
            var row=new DockPanel{Margin=new(0,0,0,8)};
            var up=UI.CaptionButton("↑",()=>Move(s.Shortcuts,index,-1));UI.LocalizedName(up,"Move up");var down=UI.CaptionButton("↓",()=>Move(s.Shortcuts,index,1));UI.LocalizedName(down,"Move down");var buttons=UI.Row(up,down,UI.CaptionButton("Remove",()=> {s.Shortcuts.RemoveAt(index);Save();Navigate(current);}));
            ((Button)buttons.Children[0]).IsEnabled=i>0;((Button)buttons.Children[1]).IsEnabled=i<s.Shortcuts.Count-1;
            DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
            var detail=new StackPanel();detail.Children.Add(UI.Text(shortcut.Name));var target=UI.Text(shortcut.Target,10,UI.Muted);target.TextWrapping=TextWrapping.NoWrap;target.TextTrimming=TextTrimming.CharacterEllipsis;detail.Children.Add(target);row.Children.Add(detail);shortcuts.Children.Add(row);
        }
        var name=UI.Input("Shortcut name","","e.g. Browser");var targetInput=UI.Input("Shortcut destination","","Application path, folder, or https:// URL");
        shortcuts.Children.Add(UI.Caption("Shortcut name",12,UI.Muted));shortcuts.Children.Add(name);shortcuts.Children.Add(UI.Caption("Application, folder, or URL",12,UI.Muted));shortcuts.Children.Add(targetInput);
        shortcuts.Children.Add(UI.Row(UI.CaptionButton("Choose file",()=> {var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Applications & files|*.*"};if(dialog.ShowDialog(this)==true){targetInput.Text=dialog.FileName;if(string.IsNullOrWhiteSpace(name.Text))name.Text=System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);}}),UI.CaptionButton("Choose folder",()=> {var dialog=new Microsoft.Win32.OpenFolderDialog();if(dialog.ShowDialog(this)==true){targetInput.Text=dialog.FolderName;if(string.IsNullOrWhiteSpace(name.Text))name.Text=System.IO.Path.GetFileName(dialog.FolderName);}})));
        shortcuts.Children.Add(UI.CaptionButton("Add shortcut",()=>
        {
            if(string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(targetInput.Text)){Accessibility.Announce(status,Localization.Text("Enter a name and destination first."));return;}
            if(s.Shortcuts.Count>=40){Accessibility.Announce(status,Localization.Text("You can add up to 40 shortcuts."));return;}
            s.Shortcuts.Add(new(){Name=name.Text.Trim(),Target=targetInput.Text.Trim()});Save();Navigate(current);
        },true));page.Children.Add(Section("Shortcuts",shortcuts));
    }
    private void MediaAppearanceSettings()
    {
        var s=app.Settings;
        var mediaIcon=new ComboBox();mediaIcon.Items.Add("Waveform");mediaIcon.Items.Add("Album artwork");mediaIcon.Items.Add("None");mediaIcon.SelectedIndex=s.ClosedMediaIcon=="None"?2:s.ClosedMediaIcon=="Artwork"?1:0;UI.LocalizedName(mediaIcon,"Closed media icon");mediaIcon.SelectionChanged+=(_,_)=>{s.ClosedMediaIcon=mediaIcon.SelectedIndex==2?"None":mediaIcon.SelectedIndex==1?"Artwork":"Waveform";previewMode="Closed";previewContent="Music";Save();};

        var choices=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new(0,8,0,8)};
        string[] ids={"Waveform","Lyrics","Title","None"};string[] labels={"Waveform","Live lyrics","Song title","None"};
        string[] hints={"Follow the music","One line, in sync","Keep it simple","Show idle or your timer"};
        var buttons=new System.Collections.Generic.List<Button>();
        void RefreshChoices(){for(int i=0;i<buttons.Count;i++){bool selected=s.ClosedMediaContent==ids[i];buttons[i].Background=UI.Brush(selected?s.SelectionFill:s.ControlFill);if(buttons[i].Content is StackPanel body && body.Children[0] is TextBlock title)title.Text=labels[i]+(selected?" · Selected":"");}}
        for(int i=0;i<ids.Length;i++)
        {
            int index=i;var content=new StackPanel();content.Children.Add(UI.Text(labels[i],14));var hint=UI.Text(hints[i],12,UI.Muted);hint.Margin=new(0,5,0,0);content.Children.Add(hint);
            var button=UI.Button(labels[i],()=>{s.ClosedMediaContent=ids[index];RefreshChoices();previewMode="Closed";previewContent="Music";Save();});button.Content=content;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Padding=new(14);button.Margin=new(0,0,8,8);button.MinHeight=76;choices.Children.Add(button);buttons.Add(button);
        }
        RefreshChoices();UI.LocalizedName(choices,"Closed media content");
        page.Children.Add(Section("Closed media display",choices,UI.Caption("Live lyrics switches to a waveform when lyrics are unavailable or during marked instrumental breaks. Track details are sent to LRCLIB while visible. None shows idle or your timer without stopping music.",12,UI.Muted),UI.Caption("Media icon",12,UI.Muted),mediaIcon));
    }
    private void DisplaySettings()
    {
        var s=app.Settings;
        var mode=new ComboBox{ItemsSource=new[]{"Primary","Selected","Follow cursor"},SelectedIndex=s.DisplayMode=="Follow"?2:s.DisplayMode=="Selected"?1:0};UI.LocalizedName(mode,"Display mode");
        mode.SelectionChanged+=(_,_)=>{s.DisplayMode=mode.SelectedIndex==2?"Follow":mode.SelectedIndex==1?"Selected":"Primary";Save();};
        var monitors=new ComboBox();var screens=Forms.Screen.AllScreens;
        monitors.Items.Add("Primary display (automatic)");foreach(var screen in screens) monitors.Items.Add(screen.DeviceName+" · "+screen.Bounds.Width+" × "+screen.Bounds.Height);
        monitors.SelectedIndex=Array.FindIndex(screens,m=>m.DeviceName==s.Monitor)+1;UI.LocalizedName(monitors,"Island display");
        monitors.SelectionChanged+=(_,_)=> {s.Monitor=monitors.SelectedIndex>0?screens[monitors.SelectedIndex-1].DeviceName:"";s.DisplayMode=monitors.SelectedIndex>0?"Selected":"Primary";mode.SelectedIndex=s.DisplayMode=="Selected"?1:0;Save();};
        var follow=new StackPanel();follow.Children.Add(Check("Slide smoothly between monitors",s.FollowMonitorAnimation,v=>s.FollowMonitorAnimation=v));AddSlider(follow,"Monitor slide duration",s.FollowMonitorDuration,180,700,v=>s.FollowMonitorDuration=v,"ms",10);
        page.Children.Add(Section("Display",mode,monitors,follow,UI.Text(s.Monitor.Length>0 && !screens.Any(screen=>screen.DeviceName==s.Monitor)?"Saved display unavailable: "+s.Monitor+" · using Primary fallback":"Selected display is saved; cursor moves never rewrite this preference.",11,UI.Muted),UI.Caption("Primary uses the main display. Selected uses the display below, falling back to Primary if disconnected. Follow cursor slides after 250 ms on another display; it waits during interaction and manual hide. Detached previews stay where placed. Example: move to a second display, pause, then continue working.",11,UI.Muted)));
    }
    private void General()
    {
        LanguageAndAccessibilitySettings();
        var s=app.Settings;
        UpdatesSection();DiagnosticsSection();NotificationSettings();
        var startup=new CheckBox{Content=Localization.Text("Start automatically when signing in to Windows"),IsChecked=s.AutoStart};
        UI.LocalizedName(startup,"Start automatically when signing in to Windows");        startup.Click+=(_,_)=>
        {
            try {StartupService.Set(startup.IsChecked==true);s.AutoStart=startup.IsChecked==true;Save();}
            catch(Exception ex) {startup.IsChecked=StartupService.Enabled();s.AutoStart=startup.IsChecked==true;status.Text="Could not change startup: "+ex.Message;app.Store.Log(ex);}
        };
        page.Children.Add(Section("Behavior",UI.CaptionButton("Open setup guide",app.ShowWelcome),startup,UI.Caption("Launch NabyScape for this Windows user when you sign in. You can turn this off at any time.",11,UI.Muted),UI.Caption("NabyScape keeps running when you close Settings. Quit from the system tray.",12,UI.Muted)));
        page.Children.Add(Section("Local storage",UI.Caption("Preferences and timers stay on this device. No account required.",12,UI.Muted),UI.CaptionButton("Open settings folder",()=>ShortcutService.Open(new(){Target=app.Store.DirectoryPath})),UI.CaptionButton("Uninstall NabyScape…",()=>{try{Installer.OpenUninstaller();}catch(Exception ex){app.Notify("Uninstall NabyScape",ex.Message);}}),UI.Caption("Uninstall can remove downloaded speech models, caches, history and saved connections too.",11,UI.Muted),UI.CaptionButton("Quit NabyScape",app.Quit)));
        var resetCategory=new ComboBox{ItemsSource=PreferenceReset.Categories.Where(pair=>pair.Value.Length>0).Select(pair=>CategoryTitle(pair.Key)).ToArray(),SelectedIndex=0};UI.LocalizedName(resetCategory,"Preference reset category");
        page.Children.Add(Section("Reset preferences",UI.Text(PreferenceReset.Summary(null),11,UI.Muted),UI.Caption("Choose a category to reset only its preferences; for example Appearance keeps your current layout.",11,UI.Muted),resetCategory,UI.CaptionButton("Reset this category",()=>ResetPreferences(CategoryId(resetCategory.SelectedItem?.ToString()??"Look & colors"))),UI.CaptionButton("Reset all preferences",()=>ResetPreferences(null)),UI.CaptionButton("Restore last preference backup",RestorePreferences)));
        page.Children.Add(Section("About NabyScape",UI.Text("Version "+typeof(App).Assembly.GetName().Version!.ToString(3)+" · Windows · "+RuntimePlatform.Rid,14),UI.Caption("Media controls depend on support from your media app. Battery appears on devices with a battery. Transparency controls opacity; glass uses a dark gradient and reflections.",12,UI.Muted),UI.Caption("Focus sessions run in NabyScape and do not change Windows Do Not Disturb.",12,UI.Muted)));
    }
}
