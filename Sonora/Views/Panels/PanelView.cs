using System.Windows;
using System.Windows.Controls;

namespace Sonora.Views.Panels;

/// <summary>Base of every dockable panel: knows whether its zone is horizontal (top/bottom) or vertical (left/right).</summary>
public class PanelView : UserControl
{
    public static readonly DependencyProperty PanelOrientationProperty =
        DependencyProperty.Register(nameof(PanelOrientation), typeof(Orientation), typeof(PanelView),
            new PropertyMetadata(Orientation.Vertical));

    public Orientation PanelOrientation
    {
        get => (Orientation)GetValue(PanelOrientationProperty);
        set => SetValue(PanelOrientationProperty, value);
    }
}
