using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Sonora.Appearance.Layout;
using Sonora.Views.Panels;
using Sonora.Appearance.Localization;

namespace Sonora.Docking;

/// <summary>
/// Frame around one dock group: a header (icon + title, or tabs when several panels share the group, and a close
/// button) that starts a drag after 6 px, then the active panel. The player shows only a grip handle.
/// </summary>
public sealed class PanelFrame : Border
{
    const double DragThreshold = 6;

    readonly DockHost _host;
    readonly DockGroup _group;
    Point? _pressAt;
    string? _pressPanel;

    public PanelFrame(DockHost host, DockZone zone, int groupIndex, DockGroup group, Orientation orientation)
    {
        _host = host;
        _group = group;
        // Frame colours (e.g. the Encre theme's ink frame) override the main brushes inside panels only.
        if (Application.Current is App) Resources.MergedDictionaries.Add(App.Themes.FrameResources);
        // Background, border, corners and padding come from the current structural style (Styles/*.xaml).
        SetResourceReference(StyleProperty, "PanelFrameStyle");
        ClipToBounds = true;

        var activeId = group.Panels[Math.Clamp(group.ActiveIndex, 0, group.Panels.Count - 1)];
        var view = PanelFactory.Get(activeId);
        PanelFactory.Detach(view);
        view.PanelOrientation = orientation;

        var root = new DockPanel { LastChildFill = true };
        bool gripOnly = group.Panels.Count == 1 && activeId == PanelIds.Player;
        if (gripOnly)
        {
            var grip = Grip(activeId, orientation);
            DockPanel.SetDock(grip, orientation == Orientation.Horizontal ? Dock.Left : Dock.Top);
            root.Children.Add(grip);
        }
        else
        {
            var header = Header(activeId);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
        }
        root.Children.Add(view);
        Child = root;
    }

    FrameworkElement Header(string activeId)
    {
        var bar = new DockPanel { Height = 30, Margin = new Thickness(6, 0, 2, 2), Background = Brushes.Transparent };

        var close = new Button { Content = "", FontSize = 11, Width = 28, Height = 28, ToolTip = Loc.T("dock.close_panel") };
        close.SetResourceReference(StyleProperty, "IconButton");
        close.Click += (_, _) => _host.Model?.Close(activeId);
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var id in _group.Panels)
        {
            var tab = Tab(id, id == activeId, showTitle: true);
            tabs.Children.Add(tab);
        }
        bar.Children.Add(tabs);
        return bar;
    }

    FrameworkElement Tab(string panelId, bool active, bool showTitle)
    {
        var glyph = new TextBlock { Text = PanelFactory.Glyph(panelId), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
        var title = new TextBlock
        {
            Text = PanelFactory.Title(panelId),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "Size.Text.Small");
        var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph } };
        if (showTitle) content.Children.Add(title);

        var tab = new Border
        {
            Child = content,
            Padding = new Thickness(8, 4, 10, 4),
            Margin = new Thickness(0, 0, 4, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            ToolTip = Loc.T("dock.move_panel"),
        };
        tab.SetResourceReference(Border.CornerRadiusProperty, "Radius.Control");
        tab.SetResourceReference(TextElement.ForegroundProperty, active ? "Brush.TextPrimary" : "Brush.TextSecondary");
        if (active && _group.Panels.Count > 1) tab.SetResourceReference(Border.BackgroundProperty, "Brush.Hover");
        Wire(tab, panelId);
        return tab;
    }

    FrameworkElement Grip(string panelId, Orientation orientation)
    {
        var glyph = new TextBlock
        {
            Text = "",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LayoutTransform = orientation == Orientation.Horizontal ? new RotateTransform(90) : Transform.Identity,
        };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var grip = new Border
        {
            Child = glyph,
            Width = orientation == Orientation.Horizontal ? 16 : double.NaN,
            Height = orientation == Orientation.Horizontal ? double.NaN : 16,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            ToolTip = Loc.T("dock.move_player_bar"),
            Opacity = 0.35,
        };
        grip.MouseEnter += (_, _) => grip.Opacity = 1;
        grip.MouseLeave += (_, _) => grip.Opacity = 0.35;
        Wire(grip, panelId);
        return grip;
    }

    void Wire(UIElement handle, string panelId)
    {
        handle.MouseLeftButtonDown += (_, e) =>
        {
            _pressAt = e.GetPosition(_host);
            _pressPanel = panelId;
            handle.CaptureMouse();
            e.Handled = true;
        };
        handle.MouseMove += (_, e) =>
        {
            if (_pressAt is not { } start || _pressPanel == null || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(_host) - start;
            if (Math.Abs(d.X) < DragThreshold && Math.Abs(d.Y) < DragThreshold) return;
            var id = _pressPanel;
            _pressAt = null;
            _pressPanel = null;
            handle.ReleaseMouseCapture();
            _host.BeginDrag(id);
        };
        handle.MouseLeftButtonUp += (_, e) =>
        {
            var clicked = _pressPanel;
            _pressAt = null;
            _pressPanel = null;
            handle.ReleaseMouseCapture();
            if (clicked != null && _group.Panels.Count > 1) _host.Model?.SetActive(clicked);
            e.Handled = true;
        };
    }
}
