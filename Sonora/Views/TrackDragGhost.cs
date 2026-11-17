using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Sonora.Views;

/// <summary>
/// The card that follows the pointer while tracks are dragged: what is carried ("♪ Title", "+2" when there are more)
/// and, once the pointer is over a playlist, what dropping would do ("Add to …") or why it would do nothing.
/// Drawn on the window's adorner layer like <see cref="Docking.DockDragOverlay"/>, so it takes the theme's colours.
/// </summary>
public sealed class TrackDragGhost : Adorner
{
    readonly string _title;
    readonly int _count;
    string? _hint;
    bool _refused;
    Point _cursor;

    TrackDragGhost(UIElement host, string title, int count) : base(host)
    {
        _title = title;
        _count = count;
        IsHitTestVisible = false;
    }

    /// <summary>Puts the card on the layer of <paramref name="host"/>; <see cref="Close"/> takes it off.</summary>
    public static TrackDragGhost? Show(UIElement host, string title, int count)
    {
        if (AdornerLayer.GetAdornerLayer(host) is not { } layer) return null;
        var ghost = new TrackDragGhost(host, title, count);
        layer.Add(ghost);
        ghost.Follow();
        return ghost;
    }

    public void Close() => AdornerLayer.GetAdornerLayer(AdornedElement)?.Remove(this);

    /// <summary>What the pointer is over: a line under the title, in the accent when the drop would work, muted when it would not.</summary>
    public void SetHint(string? hint, bool refused)
    {
        if (_hint == hint && _refused == refused) return;
        _hint = hint;
        _refused = refused;
        InvalidateVisual();
    }

    /// <summary>Moves the card to the pointer. Mouse events are not sent during a drag, so the position is read from Windows.</summary>
    public void Follow()
    {
        if (!GetCursorPos(out var p)) return;
        _cursor = AdornedElement.PointFromScreen(new Point(p.X, p.Y));
        InvalidateVisual();
    }

    Brush Res(string key, Brush fallback) => (AdornedElement as FrameworkElement)?.TryFindResource(key) as Brush ?? fallback;

    FormattedText Text(string text, double size, Brush brush, double maxWidth, FontWeight? weight = null)
    {
        var font = new Typeface(TextElement.GetFontFamily(AdornedElement), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, font, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        const double pad = 10, maxText = 380, badgeGap = 8;
        var primary = Res("Brush.TextPrimary", Brushes.White);
        var secondary = Res("Brush.TextSecondary", Brushes.Gray);
        var accent = Res("Brush.Accent", Brushes.DodgerBlue);

        var title = Text("♪  " + _title, 13, primary, maxText, FontWeights.SemiBold);
        var badge = _count > 1 ? Text("+" + (_count - 1).ToString(CultureInfo.CurrentUICulture), 12, accent, 40, FontWeights.SemiBold) : null;
        var hint = _hint is null ? null : Text(_hint, 12, _refused ? secondary : accent, maxText);

        double width = Math.Max(title.Width + (badge is null ? 0 : badge.Width + badgeGap), hint?.Width ?? 0) + pad * 2;
        double height = pad * 2 + title.Height + (hint is null ? 0 : hint.Height + 2);

        // Below and to the right of the pointer so the card does not hide what it is over; kept inside the window.
        var size = AdornedElement.RenderSize;
        double x = Math.Clamp(_cursor.X + 16, 0, Math.Max(0, size.Width - width));
        double y = Math.Clamp(_cursor.Y + 18, 0, Math.Max(0, size.Height - height));

        var border = new Pen(_refused ? Res("Brush.Border", Brushes.Gray) : accent, 1.5);
        dc.PushOpacity(0.96);
        dc.DrawRoundedRectangle(Res("Brush.SurfaceAlt", Brushes.Black), border, new Rect(x, y, width, height), 8, 8);
        dc.DrawText(title, new Point(x + pad, y + pad));
        if (badge is not null) dc.DrawText(badge, new Point(x + pad + title.Width + badgeGap, y + pad + (title.Height - badge.Height) / 2));
        if (hint is not null) dc.DrawText(hint, new Point(x + pad, y + pad + title.Height + 2));
        dc.Pop();
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
}
