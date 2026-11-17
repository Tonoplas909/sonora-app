using System.Windows;
using System.Windows.Controls.Primitives;

namespace Sonora.Views.Panels;

public partial class LibraryPanel : PanelView
{
    public LibraryPanel() => InitializeComponent();

    void Sort_Click(object sender, RoutedEventArgs e)
    {
        var anchor = (FrameworkElement)sender;
        var menu = TrackInteractions.BuildLibrarySortMenu();
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
