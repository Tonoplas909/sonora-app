using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sonora.ViewModels;

namespace Sonora.Views;

/// <summary>Drag-and-drop reordering of the home page sections.</summary>
public partial class AppearancePageView : ResourceDictionary
{
    Point? _start;
    HomeSectionItem? _dragged;

    public AppearancePageView() => InitializeComponent();

    void Sections_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Clicking or selecting text inside a field must not start dragging the shelf.
        if (IsInput(e.OriginalSource as DependencyObject)) { _start = null; _dragged = null; return; }
        _start = e.GetPosition((IInputElement)sender);
        _dragged = ItemAt((ListBox)sender, e.OriginalSource as DependencyObject);
    }

    void Sections_MouseMove(object sender, MouseEventArgs e)
    {
        if (_start is not { } start || _dragged == null || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition((IInputElement)sender) - start;
        if (Math.Abs(d.Y) < 6) return;
        var item = _dragged;
        _start = null;
        DragDrop.DoDragDrop((DependencyObject)sender, item, DragDropEffects.Move);
    }

    void Sections_Drop(object sender, DragEventArgs e)
    {
        var list = (ListBox)sender;
        if (e.Data.GetData(typeof(HomeSectionItem)) is not HomeSectionItem dragged) return;
        var target = ItemAt(list, e.OriginalSource as DependencyObject);
        if (target == null || target == dragged || list.DataContext is not AppearancePage page) return;
        page.Settings.MoveSectionTo(dragged.Id, page.Settings.HomeSections.IndexOf(target));
    }

    static bool IsInput(DependencyObject? source)
    {
        for (; source != null && source is not ListBoxItem; source = source is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.Selector or System.Windows.Controls.Primitives.ScrollBar or TextBox)
                return true;
        return false;
    }

    static HomeSectionItem? ItemAt(ListBox list, DependencyObject? source)
    {
        while (source != null && source is not ListBoxItem) source = VisualTreeHelper.GetParent(source);
        return (source as ListBoxItem)?.DataContext as HomeSectionItem;
    }
}
