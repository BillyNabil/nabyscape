using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    // Donation buttons: Saweria for QRIS/e-wallets in Indonesia, Ko-fi for cards and PayPal elsewhere.
    private StackPanel SupportButtons()
    {
        var saweria=UI.CaptionButton("Donate with Saweria",()=>SupportLinks.Open(SupportLinks.Saweria,app.Store.Log),true);saweria.ToolTip=Localization.Text("Opens saweria.co · QRIS, e-wallets and Indonesian banks");saweria.Padding=new(16,8,16,8);saweria.Tag=new CornerRadius(16);
        var kofi=UI.CaptionButton("Donate with Ko-fi",()=>SupportLinks.Open(SupportLinks.KoFi,app.Store.Log));kofi.ToolTip=Localization.Text("Opens ko-fi.com · cards and PayPal");kofi.Padding=new(16,8,16,8);kofi.Tag=new CornerRadius(16);kofi.Background=Brushes.Transparent;kofi.BorderBrush=SettingsTheme.Line;kofi.BorderThickness=new(1);kofi.Margin=new(8,0,0,0);
        var row=UI.Row(saweria,kofi);row.Margin=new(0,10,0,0);UI.LocalizedName(row,"Support NabyScape");return row;
    }
    private UIElement AboutSupport()
    {
        var panel=new StackPanel{Margin=new(0,12,0,0)};
        var title=UI.Caption("Support NabyScape",13);title.FontWeight=FontWeights.SemiBold;title.Foreground=SettingsTheme.Bone;panel.Children.Add(title);
        var note=UI.Caption("NabyScape is free and open source. If it helps you every day, a small donation keeps updates coming.",12,UI.Muted);note.Margin=new(0,3,0,0);panel.Children.Add(note);
        panel.Children.Add(SupportButtons());return panel;
    }
    // A quiet card at the end of Start here. It never pops up, blocks or repeats elsewhere.
    private Border HomeSupportCard()
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.ColumnDefinitions.Add(new());
        var glyph=new Border{Width=36,Height=36,CornerRadius=new(11),Background=SettingsTheme.LimeWash,Margin=new(0,0,12,0),VerticalAlignment=VerticalAlignment.Top,Child=Icons.Create("coffee",17,SettingsTheme.Lime)};grid.Children.Add(glyph);
        var text=new StackPanel();var name=UI.Caption("Enjoying NabyScape?",14);name.FontFamily=SettingsTheme.Display;name.FontWeight=FontWeights.Bold;text.Children.Add(name);
        var hint=UI.Caption("It's free and made by one developer. Support future updates with a donation, any amount helps.",12,UI.Muted);hint.Margin=new(0,3,0,0);text.Children.Add(hint);text.Children.Add(SupportButtons());
        Grid.SetColumn(text,1);grid.Children.Add(text);
        var card=new Border{Background=SettingsTheme.Card,BorderBrush=SettingsTheme.LineSoft,BorderThickness=new(1),CornerRadius=new(14),Padding=new(16,14,16,14),Margin=new(0,10,0,10),Child=grid};
        UI.LocalizedName(card,"Support NabyScape");return card;
    }
}
