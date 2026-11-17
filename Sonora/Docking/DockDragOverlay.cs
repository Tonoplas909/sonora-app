using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Sonora.Docking;

/// <summary>Drop targets drawn over the window while a panel is dragged: edge markers, the landing area and a label.</summary>
public sealed class DockDragOverlay : Adorner
{
    readonly string _title;
    Point _cursor;
    Rect? _preview;

    public DockDragOverlay(UIElement host, string title) : base(host)
    {
        _title = title;
        IsHitTestVisible = false;
    }

    public void Update(Point cursor, Rect? preview)
    {
        _cursor = cursor;
        _preview = preview;
        InvalidateVisual();
    }

    Brush Res(string key, Brush fallback) => (AdornedElement as FrameworkElement)?.TryFindResource(key) as Brush ?? fallback;

    protected override void OnRender(DrawingContext dc)
    {
        var size = AdornedElement.RenderSize;
        var soft = Res("Brush.AccentSoft", Brushes.Transparent);
        var accent = Res("Brush.Accent", Brushes.Gray);
        var pen = new Pen(accent, 2);
        var surface = Res("Brush.SurfaceAlt", Brushes.Black);
        var text = Res("Brush.TextPrimary", Brushes.White);

        // Edge markers: where a panel can be docked.
        const double m = 44, t = 8;
        foreach (var r in new[]
                 {
                     new Rect(t, size.Height / 2 - m, 10, m * 2),
                     new Rect(size.Width - t - 10, size.Height / 2 - m, 10, m * 2),
                     new Rect(size.Width / 2 - m, t, m * 2, 10),
                     new Rect(size.Width / 2 - m, size.Height - t - 10, m * 2, 10),
                 })
            dc.DrawRoundedRectangle(soft, pen, r, 5, 5);

        if (_preview is { } p)
            dc.DrawRoundedRectangle(soft, pen, Inflate(p, -4), 10, 10);

        var ft = new FormattedText(_title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text"), 13, text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var label = new Rect(_cursor.X + 14, _cursor.Y + 14, ft.Width + 20, ft.Height + 10);
        dc.DrawRoundedRectangle(surface, pen, label, 6, 6);
        dc.DrawText(ft, new Point(label.X + 10, label.Y + 5));
    }

    static Rect Inflate(Rect r, double d)
    {
        r.Inflate(d, d);
        return r.Width > 0 && r.Height > 0 ? r : Rect.Empty;
    }
}
