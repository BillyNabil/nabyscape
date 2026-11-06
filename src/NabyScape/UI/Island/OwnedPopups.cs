using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;

// Drop-down lists, menus and popovers render in their own popup windows, which may extend
// beyond the island contour. Pointer-leave, outside-click and deactivation rules must treat
// them as part of the island; otherwise choosing an item that hangs outside the panel closes it.
internal static class OwnedPopupPolicy
{
    internal static bool CaptureOwnedBy(Window owner)=>Mouse.Captured is DependencyObject captured && OwnedBy(captured,owner);
    internal static bool OwnedBy(DependencyObject? node,Window owner)
    {
        for(int depth=0;node!=null && depth<256;depth++)
        {
            if(ReferenceEquals(node,owner))return true;
            if(node is ComboBox {IsDropDownOpen:true} box)return ReferenceEquals(Window.GetWindow(box),owner);
            if(node is Popup popup && popup.PlacementTarget is {} target)return OwnedBy(target,owner);
            if(node is ContextMenu {IsOpen:true} menu && menu.PlacementTarget is {} menuTarget)return OwnedBy(menuTarget,owner);
            // Popup content is a separate visual root; climb through templates and logical owners.
            node=node switch
            {
                FrameworkElement {TemplatedParent:{} templated}=>templated,
                FrameworkElement {Parent:{} parent}=>parent,
                Visual or System.Windows.Media.Media3D.Visual3D=>VisualTreeHelper.GetParent(node)??LogicalTreeHelper.GetParent(node),
                _=>LogicalTreeHelper.GetParent(node)
            };
        }
        return false;
    }
}

public sealed partial class OverlayWindow
{
    private bool OwnedPopupOpen=>sessions.IsDropDownOpen || audioMenu?.IsOpen==true || MinimalWidgets.MenuOpen(this) || LyricsSourcesControl.MenuOpen || OwnedPopupPolicy.CaptureOwnedBy(this);
}

internal static class OwnedPopupTests
{
    internal static void Run(Action<string,bool> test)
    {
        var owner=new Window();var host=new StackPanel();owner.Content=host;
        var box=new ComboBox();host.Children.Add(box);
        var outside=new Window();var other=new ComboBox();outside.Content=other;
        test("Elements inside the island belong to it",OwnedPopupPolicy.OwnedBy(box,owner));
        test("Another window's control never holds the island open",!OwnedPopupPolicy.OwnedBy(other,owner));
        var popupChild=new Border();var popup=new Popup{Child=popupChild,PlacementTarget=box};
        test("Popup content resolves to its placement owner",OwnedPopupPolicy.OwnedBy(popupChild,owner));
        var stray=new Border();var strayPopup=new Popup{Child=stray,PlacementTarget=other};
        test("Popup content placed on another window is not owned",!OwnedPopupPolicy.OwnedBy(stray,owner));
        test("Unparented elements are not owned",!OwnedPopupPolicy.OwnedBy(new Border(),owner) && !OwnedPopupPolicy.OwnedBy(null,owner));
        owner.Close();outside.Close();GC.KeepAlive(popup);GC.KeepAlive(strayPopup);
    }
}
