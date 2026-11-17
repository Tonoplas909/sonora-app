using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Sonora.Appearance.Layout;
using Sonora.ViewModels;
using Sonora.Views.Panels;
using Sonora.Appearance.Localization;

namespace Sonora.Docking;

/// <summary>
/// Builds the window from a <see cref="DockLayout"/>: top and bottom zones span the full width, left and right
/// zones sit beside the fixed centre page. Zones and groups are resized with splitters; panels are moved by
/// dragging their header onto the targets drawn by <see cref="DockDragOverlay"/>.
/// </summary>
public sealed class DockHost : Grid
{
    public static readonly DependencyProperty CenterProperty =
        DependencyProperty.Register(nameof(Center), typeof(UIElement), typeof(DockHost),
            new PropertyMetadata(null, (d, _) => ((DockHost)d).Rebuild()));

    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(nameof(Model), typeof(LayoutViewModel), typeof(DockHost),
            new PropertyMetadata(null, OnModelChanged));

    readonly List<(PanelFrame Frame, DockZone Zone, int Group)> _frames = [];
    DockDragOverlay? _overlay;
    string? _dragPanel;
    DropTarget? _target;

    public DockHost()
    {
        Focusable = true;
        Loaded += (_, _) => Rebuild();
    }

    public UIElement? Center
    {
        get => (UIElement?)GetValue(CenterProperty);
        set => SetValue(CenterProperty, value);
    }

    public LayoutViewModel? Model
    {
        get => (LayoutViewModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (DockHost)d;
        if (e.OldValue is LayoutViewModel old) old.Changed -= host.Rebuild;
        if (e.NewValue is LayoutViewModel model) model.Changed += host.Rebuild;
        host.Rebuild();
    }

    double Gap => Math.Max(4, TryFindResource("Value.Gap") is double g ? g : 8);

    // ---------------- layout ----------------

    bool _suspended;

    /// <summary>
    /// While true the host holds no panel at all: the window is out of sight (the full-screen view has taken its
    /// place) and what its panels hold, covers and lists included, is let go. Back to false, it is built again.
    /// </summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            Rebuild();
        }
    }

    public void Rebuild()
    {
        if (Model is not { } model || !IsLoaded) return;
        var layout = model.Layout;
        var gap = Gap;

        if (Center is FrameworkElement { Parent: Panel oldParent } c) oldParent.Children.Remove(c);
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        _frames.Clear();
        if (_suspended) return;

        var middle = new Grid();
        RowDefinitions.Add(new RowDefinition { Height = ZoneLength(layout, DockZone.Top) });
        RowDefinitions.Add(new RowDefinition { Height = SplitterLength(layout, DockZone.Top, gap) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = SplitterLength(layout, DockZone.Bottom, gap) });
        RowDefinitions.Add(new RowDefinition { Height = ZoneLength(layout, DockZone.Bottom) });

        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = ZoneLength(layout, DockZone.Left) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = SplitterLength(layout, DockZone.Left, gap) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = SplitterLength(layout, DockZone.Right, gap) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = ZoneLength(layout, DockZone.Right) });
        SetRow(middle, 2);
        Children.Add(middle);

        if (Center != null)
        {
            SetColumn(Center, 2);
            middle.Children.Add(Center);
        }

        AddZone(this, layout, DockZone.Top, row: 0, col: -1);
        AddZone(this, layout, DockZone.Bottom, row: 4, col: -1);
        AddZone(middle, layout, DockZone.Left, row: -1, col: 0);
        AddZone(middle, layout, DockZone.Right, row: -1, col: 4);

        AddZoneSplitter(this, layout, DockZone.Top, 1, gap);
        AddZoneSplitter(this, layout, DockZone.Bottom, 3, gap);
        AddZoneSplitter(middle, layout, DockZone.Left, 1, gap);
        AddZoneSplitter(middle, layout, DockZone.Right, 3, gap);
    }

    static GridLength ZoneLength(DockLayout l, DockZone z)
    {
        var zone = l.Zone(z);
        return zone.Groups.Count == 0 || zone.IsCollapsed ? new GridLength(0) : new GridLength(zone.Size);
    }

    static GridLength SplitterLength(DockLayout l, DockZone z, double gap) =>
        l.Zone(z).Groups.Count == 0 ? new GridLength(0) : new GridLength(gap);

    void AddZone(Grid parent, DockLayout layout, DockZone zone, int row, int col)
    {
        var state = layout.Zone(zone);
        if (state.Groups.Count == 0) return;
        bool vertical = zone is DockZone.Left or DockZone.Right;
        var grid = new Grid { Visibility = state.IsCollapsed ? Visibility.Collapsed : Visibility.Visible };
        if (row >= 0) SetRow(grid, row);
        if (col >= 0) SetColumn(grid, col);

        var gap = Gap;
        for (int i = 0; i < state.Groups.Count; i++)
        {
            var star = new GridLength(state.Groups[i].Ratio, GridUnitType.Star);
            if (vertical)
            {
                if (i > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(gap) });
                grid.RowDefinitions.Add(new RowDefinition { Height = star, MinHeight = 60 });
            }
            else
            {
                if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = star, MinWidth = 120 });
            }

            var frame = new PanelFrame(this, zone, i, state.Groups[i], vertical ? Orientation.Vertical : Orientation.Horizontal);
            if (vertical) SetRow(frame, i * 2); else SetColumn(frame, i * 2);
            grid.Children.Add(frame);
            _frames.Add((frame, zone, i));

            if (i > 0)
            {
                var split = new GridSplitter
                {
                    Background = System.Windows.Media.Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    Focusable = false,
                };
                if (vertical) SetRow(split, i * 2 - 1); else SetColumn(split, i * 2 - 1);
                var z = zone;
                split.DragCompleted += (_, _) => Model?.SetGroupRatios(z, vertical
                    ? grid.RowDefinitions.Where((_, k) => k % 2 == 0).Select(d => d.ActualHeight).ToList()
                    : grid.ColumnDefinitions.Where((_, k) => k % 2 == 0).Select(d => d.ActualWidth).ToList());
                grid.Children.Add(split);
            }
        }
        parent.Children.Add(grid);
    }

    void AddZoneSplitter(Grid parent, DockLayout layout, DockZone zone, int index, double gap)
    {
        if (layout.Zone(zone).Groups.Count == 0) return;
        bool columns = zone is DockZone.Left or DockZone.Right;
        var split = new GridSplitter
        {
            Background = System.Windows.Media.Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = columns ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            Focusable = false,
            ToolTip = Loc.T("dock.resize_hint"),
        };
        if (columns) SetColumn(split, index); else SetRow(split, index);
        split.DragCompleted += (_, _) =>
        {
            double size = zone switch
            {
                DockZone.Left => parent.ColumnDefinitions[0].ActualWidth,
                DockZone.Right => parent.ColumnDefinitions[4].ActualWidth,
                DockZone.Top => parent.RowDefinitions[0].ActualHeight,
                _ => parent.RowDefinitions[4].ActualHeight,
            };
            if (size > 0) Model?.SetZoneSize(zone, size);
        };
        split.MouseDoubleClick += (_, e) => { Model?.ToggleCollapsed(zone); e.Handled = true; };
        parent.Children.Add(split);
    }

    // ---------------- drag and drop ----------------

    public void BeginDrag(string panelId)
    {
        if (_overlay != null) return;
        _dragPanel = panelId;
        _overlay = new DockDragOverlay(this, PanelFactory.Title(panelId));
        AdornerLayer.GetAdornerLayer(this)?.Add(_overlay);
        Keyboard.Focus(this);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_overlay == null) return;
        var p = e.GetPosition(this);
        _target = ComputeTarget(p);
        _overlay.Update(p, _target?.Preview);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_overlay == null) return;
        var target = _target;
        var panel = _dragPanel;
        EndDrag();
        if (target == null || panel == null || Model == null) return;
        if (target.Group is { } g) Model.MoveToGroup(panel, target.Zone, g, target.Position);
        else Model.Move(panel, target.Zone);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_overlay != null && e.Key == Key.Escape) { EndDrag(); e.Handled = true; }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_overlay != null) EndDrag();
    }

    void EndDrag()
    {
        var overlay = _overlay;
        _overlay = null;
        if (overlay != null) AdornerLayer.GetAdornerLayer(this)?.Remove(overlay);
        _target = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    sealed record DropTarget(DockZone Zone, int? Group, DropPosition Position, Rect Preview);

    const double EdgeBand = 56;

    DropTarget? ComputeTarget(Point p)
    {
        double w = ActualWidth, h = ActualHeight;
        if (p.X < EdgeBand) return new DropTarget(DockZone.Left, null, DropPosition.After, new Rect(0, 0, w * 0.25, h));
        if (p.X > w - EdgeBand) return new DropTarget(DockZone.Right, null, DropPosition.After, new Rect(w * 0.75, 0, w * 0.25, h));
        if (p.Y < EdgeBand) return new DropTarget(DockZone.Top, null, DropPosition.After, new Rect(0, 0, w, h * 0.2));
        if (p.Y > h - EdgeBand) return new DropTarget(DockZone.Bottom, null, DropPosition.After, new Rect(0, h * 0.8, w, h * 0.2));

        foreach (var (frame, zone, group) in _frames)
        {
            if (!frame.IsVisible) continue;
            var r = frame.TransformToAncestor(this).TransformBounds(new Rect(frame.RenderSize));
            if (!r.Contains(p)) continue;
            bool vertical = zone is DockZone.Left or DockZone.Right;
            double rel = vertical ? (p.Y - r.Top) / r.Height : (p.X - r.Left) / r.Width;
            if (rel < 0.3)
                return new DropTarget(zone, group, DropPosition.Before,
                    vertical ? new Rect(r.Left, r.Top, r.Width, r.Height / 2) : new Rect(r.Left, r.Top, r.Width / 2, r.Height));
            if (rel > 0.7)
                return new DropTarget(zone, group, DropPosition.After,
                    vertical ? new Rect(r.Left, r.Top + r.Height / 2, r.Width, r.Height / 2) : new Rect(r.Left + r.Width / 2, r.Top, r.Width / 2, r.Height));
            return new DropTarget(zone, group, DropPosition.Tab, r);
        }
        return null;
    }
}
