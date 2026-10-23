using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NabyScape;
internal static class AssistantControls
{
    private static FrameworkElement Modes(App app,bool voice)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(var item in new[]{("Chat",false),("Voice",true)})
        {
            bool selected=item.Item2==voice;var button=Action(item.Item1,item.Item2?"mic":"chat",()=>{if(!selected){if(item.Item2)app.OpenVoice();else app.ShowAssistant();}});
            button.MinWidth=72;if(selected){button.SetResourceReference(Control.BackgroundProperty,"SelectionFill");button.SetResourceReference(Control.ForegroundProperty,"SelectionText");}else button.Foreground=UI.Muted;button.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;button.BorderThickness=new(0);button.ToolTip=item.Item2?"Voice conversation · the microphone starts only when you choose Start":"Text conversation";
            if(button.Content is StackPanel content && content.Children[1] is TextBlock label)label.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetHelpText(button,selected?"Current mode":"Switch conversation mode");row.Children.Add(button);
        }
        return row;
    }
    internal static Button Action(string text,string icon,Action action,bool primary=false)
    {
        var button=UI.CaptionButton(text,action,primary);var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var glyph=Icons.Create(icon,14,primary?Brushes.Black:UI.Muted);glyph.Margin=new(0,0,7,0);row.Children.Add(glyph);var label=UI.Caption(text,12,primary?Brushes.Black:null);row.Children.Add(label);
        label.SetBinding(TextBlock.ForegroundProperty,new Binding("Foreground"){Source=button});FollowForeground(glyph,button);
        label.TextWrapping=TextWrapping.NoWrap;button.Content=row;button.Tag=new CornerRadius(8);button.MinHeight=34;button.Margin=new(0);button.Padding=new(12,7,12,7);button.HorizontalContentAlignment=HorizontalAlignment.Center;button.VerticalContentAlignment=VerticalAlignment.Center;if(!primary)button.Background=Brushes.Transparent;button.BorderThickness=new(0);button.FontSize=12;UI.LocalizedName(button,text);return button;
    }
    private static void FollowForeground(FrameworkElement icon,Button button)
    {
        if(icon is Viewbox {Child:Canvas canvas})foreach(var element in canvas.Children)if(element is Shape path)path.SetBinding(Shape.StrokeProperty,new Binding("Foreground"){Source=button});
        if(icon is Shape filled)filled.SetBinding(Shape.FillProperty,new Binding("Foreground"){Source=button});
    }
    internal static void SetActionText(Button button,string text)
    {
        if(button.Content is Grid content && content.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock wrapped)
        {
            if(wrapped.Text!=Localization.Text(text)){wrapped.Text=Localization.Text(text);var glyph=Icons.Create(text.StartsWith("End",StringComparison.Ordinal)?"stop-filled":"mic",14,Brushes.Black);FollowForeground(glyph,button);glyph.Margin=new(0,0,7,0);content.Children.RemoveAt(0);content.Children.Insert(0,glyph);}
        }
        else if(button.Content is StackPanel row && row.Children.Count>1 && row.Children[1] is TextBlock label)
        {
            if(label.Text!=Localization.Text(text)){label.Text=Localization.Text(text);var glyph=Icons.Create(text.StartsWith("End",StringComparison.Ordinal)?"stop-filled":"mic",14,Brushes.Black);FollowForeground(glyph,button);glyph.Margin=new(0,0,7,0);row.Children.RemoveAt(0);row.Children.Insert(0,glyph);}
        }
        else button.Content=Localization.Text(text);
        UI.LocalizedName(button,text);
    }
    internal static FrameworkElement Connection(App app,out TextBlock description,bool voice=false)
    {
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(Modes(app,voice));
        // Keep connection details in the menu so the footer stays one compact row.
        description=UI.Caption("",11,UI.Muted);description.MaxWidth=260;
        var menu=new ContextMenu{Placement=System.Windows.Controls.Primitives.PlacementMode.Top,MinWidth=230};
        UI.LocalizedName(menu,"Naby options");menu.Items.Add(new MenuItem{Header=description,IsEnabled=false});menu.Items.Add(new Separator());
        menu.Items.Add(Option("AI settings","Providers & API keys","settings",app.ShowAssistantProviders));
        menu.Items.Add(Option("Voice settings","Models & microphone","mic",app.ShowVoiceSettings));
        menu.Items.Add(Option("Connect Hermes","Connect or manage your own agent","link",app.ShowHermesSettings));
        var wake=Option("Wake word","Pause or resume local wake listening","mic",()=>{if(app.Assistant.Options.WakeWord.Enabled)app.Assistant.ToggleWakeListening();else app.ShowVoiceSettings();});menu.Items.Add(new Separator());menu.Items.Add(wake);
        menu.Opened+=(_,_)=>{string label=app.Assistant.Options.WakeWord.Enabled?(app.Assistant.WakeArmed?"Pause wake word":"Resume wake word"):"Set up wake word…";if(wake.Header is StackPanel header && header.Children[1] is TextBlock text)text.Text=Localization.Text(label);UI.LocalizedName(wake,label);wake.ToolTip=app.Assistant.WakeStatus;};
        var options=Action("Options","settings",()=>menu.IsOpen=true);options.ToolTip=Localization.Text("AI settings, voice setup and Hermes");options.ContextMenu=menu;menu.PlacementTarget=options;Grid.SetColumn(options,2);row.Children.Add(options);
        FrameworkElement footer=row;
        if(voice && row.Children[0] is StackPanel modes)
        {
            // Wrap each mode independently so a narrow large-text viewport does
            // not inherit the combined minimum width of Chat and Voice.
            row.Children.Clear();var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};
            foreach(var button in modes.Children.OfType<Button>().ToArray()){modes.Children.Remove(button);actions.Children.Add(button);}
            actions.Children.Add(options);footer=actions;
        }
        return new Border{Child=footer,BorderBrush=UI.Brush("#24282E"),BorderThickness=new(0,1,0,0),Padding=new(0,7,0,0),Margin=new(0,7,0,0)};
    }
    private static MenuItem Option(string title,string subtitle,string icon,Action action)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};var glyph=Icons.Create(icon,15);glyph.Margin=new(0,0,10,0);row.Children.Add(glyph);row.Children.Add(UI.Caption(title,12));
        var item=new MenuItem{Header=row,ToolTip=Localization.Text(subtitle)};UI.LocalizedName(item,title);item.Click+=(_,_)=>action();return item;
    }
    internal static void RefreshConnection(TextBlock label,AssistantRuntime ai,bool voice=false)
    {
        string text;
        if(voice && ai.Options.LocalVoice.Enabled)
        {
            var options=ai.Options.LocalVoice;text=Localization.Text(options.OwnModels?"Your local model files":options.Managed?"Installed local speech": "Local ASR & TTS service");
        }
        else
        {
            var provider=voice?ai.VoiceProvider:ai.Provider;text=provider==null?"Choose a provider to get started":provider.Name+(provider.Model.Length>0?" · "+provider.Model:"");
        }
        bool configured=voice && ai.Options.LocalVoice.Enabled || (voice?ai.VoiceProvider:ai.Provider)!=null;label.Text=configured?Localization.Format("Selected: {0}",text):Localization.Text("Choose a provider to get started");label.ToolTip=text;
    }
}
