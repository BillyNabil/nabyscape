using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace NabyScape;
internal sealed record SettingsSearchItem(string Title,string Category,string Section,string Control,string Keywords);
public sealed partial class SettingsWindow
{
    private TextBox? settingsSearch;
    private Border? settingsSearchHeader;
    private Border? searchResultsHost;
    private readonly StackPanel settingsResults=new();
    private SettingsSearchItem[] settingsSearchIndex=Array.Empty<SettingsSearchItem>();
    private static string[] CategorySections(string id)=>Category(id).Sections??(id switch
    {
        "Layout"=>new[]{"Display","Dock placement","Move & resize","Closed island","Open panel","Hover preview","Layout profile"},
        "Content"=>new[]{"Feature usage","Enabled widgets & order","Individual tools","Hover pages","Focus sessions","Stopwatch"},
        "Controls"=>new[]{"Hover & swipe","Panel navigation","Drag to Island","Detachable previews","Closed cover gestures","Two live activities","Notch gestures","Global hotkey"},
        "Audio"=>new[]{"Visualizer","Lyrics","Closed media display","Volume mixer","Waveform source","Live signal","Live feedback","Audio behavior","Header wheel"},
        "System"=>new[]{"Clock","Monitor details"},"Launch"=>new[]{"Launch display","Shortcuts","Activity actions"},"AI"=>new[]{"AI companion"},
        "Application"=>new[]{"Language & accessibility","Updates","Behavior","Local storage","Private diagnostics","Reset preferences","About NabyScape"},_=>Array.Empty<string>()
    });
    private static SettingsSearchItem[] BuildSettingsSearchIndex()
    {
        var items=new List<SettingsSearchItem>();
        foreach(var category in SettingsCategories)
        {
            items.Add(new(category.Title,category.Id,"","",category.Terms+" "+category.Description));
            foreach(string section in CategorySections(category.Id))items.Add(new(FriendlySection(section),category.Id,section,"",section+" "+category.Title));
        }
        void Add(string title,string category,string section,string control,string terms)=>items.Add(new(title,category,section,control,terms));
        Add("Live lyrics on closed dock","Audio","Closed media display","Closed media content","lyrics lirik youtube apple song title none waveform");
        Add("Feature usage","Content","Feature usage","Connect to music apps","resource ram essentials background disable features music download");
        Add("Light on the closed dock","Ambience","Ambient light","Show ambience on closed dock","closed compact dock ambience tertutup hitam tuck glow");
        Add("Ambient brightness","Ambience","Ambient light","Ambient intensity","intensity brightness terang cahaya warna");
        Add("Light color & cover palette","Ambience","Ambient light","Ambient color source","single satu warna color cover blend spotify album custom");
        Add("Light spread","Ambience","Ambient light","Ambient spread","spread lebar luas sebar cahaya");
        Add("Light reach","Ambience","Ambient light","Light reach","height tinggi reach full panel");
        Add("Light direction","Ambience","Ambient light","Light direction","direction arah glow atas bawah kiri kanan surround");
        Add("Light softness","Ambience","Ambient light","Ambient softness","softness lembut fade gradient");
        Add("Color balance","Ambience","Ambient light","Color balance","balance campur gabungan warna blend");
        Add("Light reacts to music","Ambience","Ambient light","Ambient audio response","audio reactive react beat lagu musik responsive punchy");
        Add("Ambient movement","Ambience","Ambient light","Ambient movement","motion breathe drift gerak animasi");
        Add("Dock screen edge","Layout","Dock placement","Dock side","position posisi dock pindah kanan kiri atas bawah floating");
        Add("Precise dock position & center","Layout","Move & resize","Horizontal position percent","center tengah pixel piksel presisi precise drag drop position posisi alignment");
        Add("Smooth monitor follow","Layout","Display","Slide smoothly between monitors","monitor screen layar follow cursor kursor smooth animation slide");
        Add("Drag & reorder widgets","Content","Enabled widgets & order","","drag drop urutan widget reorder susun");
        Add("Hover shows active widgets","Content","Hover pages","Show all active widgets in hover","hover semua widget aktif active preview swipe slide");
        Add("Dock size","Layout","Closed island","Closed width","width height ukuran lebar tinggi compact");
        Add("Panel size","Layout","Open panel","Panel width","resize size ukuran besar panel");
        Add("Automatic hide & tuck","Visibility","Automatic tucking","","tuck hide fullscreen fullscreen otomatis sembunyi dock");
        Add("YouTube previews","Visibility","Automatic activity","Automatic browser media previews","youtube browser hover popup preview chrome ganggu");
        Add("Keyboard shortcut","Controls","Global hotkey","","keyboard hotkey shortcut tombol pintasan buka dock");
        Add("Lyrics","Audio","Lyrics","","lyrics lirik spotify karaoke lagu");
        Add("Audio waveform","Audio","Visualizer","","waveform visualizer spectrum gelombang audio reaktif");
        Add("Speaker & volume","Audio","Volume mixer","","speaker volume suara mixer microphone output device spotify");
        Add("Clipboard capture","Clipboard","Clipboard history","Save text & image history locally","clipboard history salin copy paste riwayat gambar capture");
        Add("Weather city","Weather","","Weather city search","cuaca kota city location lokasi weather");
        Add("Weather temperature units","Weather","","Weather temperature units","suhu temperature celsius fahrenheit derajat");
        Add("AI connection & API key","AI","AI companion","Chat provider","naby hermes gemini provider api key endpoint model voice chat");
        Add("Choose a chat model","AI","AI companion","Chat model","discover models paste manual ai model");
        Add("Voice setup & local models","AI","AI companion","Speech connection","asr tts microphone voice speech local hardware install model");
        Add("Wake word · hands-free voice","AI","AI companion","Wake word settings","wake word hands free hey naby microphone paused resume listening");
        Add("Uninstall ASR & TTS models","AI","AI companion","Installed speech models","uninstall remove model free space storage delete tts asr download cache");
        Add("Image input & support test","AI","AI companion","Image input","image input vision send pictures photo support test");
        Add("Test microphone and speech","AI","AI companion","Speech checks","microphone not working test transcription asr tts play sample speaker voice");
        Add("Timer & focus duration","Content","Focus sessions","","focus timer pomodoro menit durasi break istirahat");
        Add("Notification sounds","Notifications","Completion sound","","notifikasi notif suara sound bunyi timer fokus");
        Add("Quiet hours","Notifications","Notification presentation","","quiet hours mute silent senyap diam jadwal malam");
        Add("Launch at Windows startup","Application","Behavior","Start automatically when signing in to Windows","startup autostart windows mulai otomatis login");
        return items.ToArray();
    }
    private void InitializeSettingsSearch(Grid main,Grid workspace)
    {
        settingsSearchIndex=BuildSettingsSearchIndex();settingsSearch=UI.Input("Search settings","");settingsSearch.Background=Brushes.Transparent;settingsSearch.BorderThickness=new(0);settingsSearch.Margin=new(0);settingsSearch.Padding=new(0,6,0,6);settingsSearch.MinHeight=34;
        var row=new DockPanel();var glyph=Icons.Create("search",15,UI.Muted);glyph.Margin=new(0,0,10,0);DockPanel.SetDock(glyph,Dock.Left);row.Children.Add(glyph);
        var clear=Icons.Button("x","Clear settings search",()=>settingsSearch.Clear(),false,28);clear.Background=Brushes.Transparent;clear.Margin=new(5,0,0,0);clear.Visibility=Visibility.Collapsed;DockPanel.SetDock(clear,Dock.Right);row.Children.Add(clear);
        var inputHost=new Grid();inputHost.Children.Add(settingsSearch);var hint=UI.Caption("Search settings…  Ctrl+F",12,UI.Muted);hint.IsHitTestVisible=false;inputHost.Children.Add(hint);row.Children.Add(inputHost);
        var field=new Border{Child=row,Background=UI.Brush("#1A1D23"),BorderBrush=UI.Brush("#30353F"),BorderThickness=new(.7),CornerRadius=new(10),Padding=new(12,0,6,0),Margin=new(20,13,20,9)};settingsSearchHeader=field;main.Children.Add(field);Grid.SetRow(field,0);
        var resultsScroll=UI.Scroll(settingsResults);resultsScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        searchResultsHost=new Border{Child=resultsScroll,Background=UI.Brush("#181B21"),BorderBrush=UI.Brush("#3C4350"),BorderThickness=new(.8),CornerRadius=new(12),Padding=new(8),Margin=new(20,0,20,10),VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Collapsed};UI.LocalizedName(searchResultsHost,"Settings search results");Grid.SetRow(searchResultsHost,1);Panel.SetZIndex(searchResultsHost,40);main.Children.Add(searchResultsHost);
        workspace.SizeChanged+=(_,_)=>resultsScroll.MaxHeight=Math.Max(80,Math.Min(430,workspace.ActualHeight-28));
        settingsSearch.TextChanged+=(_,_)=>{bool empty=string.IsNullOrWhiteSpace(settingsSearch.Text);hint.Visibility=empty?Visibility.Visible:Visibility.Collapsed;clear.Visibility=empty?Visibility.Collapsed:Visibility.Visible;ShowSettingsSearch();};
        settingsSearch.GotKeyboardFocus+=(_,_)=>ShowSettingsSearch();
        settingsSearch.PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Escape){settingsSearch.Clear();e.Handled=true;}
            else if(e.Key is Key.Down or Key.Enter && settingsResults.Children.OfType<Button>().FirstOrDefault() is {} first){if(e.Key==Key.Down)first.Focus();else first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));e.Handled=true;}
        };
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.F && Keyboard.Modifiers==ModifierKeys.Control){settingsSearch.Focus();settingsSearch.SelectAll();e.Handled=true;}else if(e.Key==Key.Escape && searchResultsHost.Visibility==Visibility.Visible){settingsSearch.Clear();settingsSearch.Focus();e.Handled=true;}};
        PreviewMouseDown+=(_,e)=>{if(searchResultsHost.Visibility!=Visibility.Visible)return;if(e.OriginalSource is DependencyObject source && !Inside(source,field) && !Inside(source,searchResultsHost))searchResultsHost.Visibility=Visibility.Collapsed;};
    }
    private static bool Inside(DependencyObject source,DependencyObject ancestor)
    {for(var node=source;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node==ancestor)return true;return false;}
    private void ShowSettingsSearch()
    {
        if(settingsSearch==null || searchResultsHost==null)return;settingsResults.Children.Clear();string query=settingsSearch.Text.Trim();
        if(query.Length==0){searchResultsHost.Visibility=Visibility.Collapsed;return;}
        var tokens=query.Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(t=>t is not ("settings" or "setting" or "pengaturan" or "ubah" or "change" or "cara")).ToArray();if(tokens.Length==0)tokens=new[]{query};
        var matches=settingsSearchIndex.Where(item=>tokens.All(t=>(Localization.Text(item.Title)+" "+item.Title+" "+item.Keywords).Contains(t,StringComparison.OrdinalIgnoreCase))).OrderByDescending(item=>item.Title.StartsWith(query,StringComparison.OrdinalIgnoreCase)).ThenByDescending(item=>item.Title.Contains(query,StringComparison.OrdinalIgnoreCase)).ThenByDescending(item=>item.Control.Length>0).Take(12).ToArray();
        if(matches.Length==0){var empty=UI.Caption("No settings found. Try color, dock, lyrics, weather or notifications.",12,UI.Muted);empty.Margin=new(12);settingsResults.Children.Add(empty);}
        foreach(var item in matches)
        {
            var result=item;var button=UI.Button("Open "+result.Title,()=>OpenSettingsSearchResult(result));button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.Padding=new(12,9,12,9);button.Margin=new(0,1,0,1);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var labels=new StackPanel();labels.Children.Add(UI.Caption(result.Title,12));var path=UI.Text(CategoryTitle(result.Category)+(result.Section.Length>0?"  /  "+FriendlySection(result.Section):""),10,UI.Muted);path.Margin=new(0,3,0,0);labels.Children.Add(path);button.Content=labels;settingsResults.Children.Add(button);
        }
        searchResultsHost.Visibility=Visibility.Visible;
    }
    private void OpenSettingsSearchResult(SettingsSearchItem item)
    {
        settingsSearch?.Clear();Navigate(item.Category);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>
        {
            if(current!=item.Category)return;if(item.Category=="Weather")SettingTree(page).OfType<WeatherView>().FirstOrDefault()?.OpenLocationEditor();
            var section=SettingTree(page).OfType<Border>().FirstOrDefault(b=>b.Tag as string==item.Section);
            var target=SettingTree((DependencyObject?)section??page).OfType<Control>().FirstOrDefault(c=>item.Control.Length>0 && UI.Identity(c)==item.Control);
            if(target!=null)
            {
                if(item.Category=="AI")aiSettings?.RevealSetupTarget(target);
                for(DependencyObject? node=target;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))if(node is Expander expander)expander.IsExpanded=true;
                // Collapsed Expander content is logical before it is realized visually.
                foreach(var expander in SettingTree((DependencyObject?)section??page).OfType<Expander>().Where(x=>SettingTree(x).Contains(target)))expander.IsExpanded=true;
                page.UpdateLayout();target.Focus();target.BringIntoView();SetSaveFeedback("Opened · "+item.Title);
            }
            else if(section!=null){foreach(var parent in SettingTree(page).OfType<Expander>().Where(e=>SettingTree(e).Contains(section)))parent.IsExpanded=true;page.UpdateLayout();section.BringIntoView();ConfigurePreviewForSection(item.Section);RefreshPreview();}
        });
    }
}
