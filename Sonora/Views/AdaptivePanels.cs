using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sonora.Views;

/// <summary>Attached settings read by <see cref="PriorityRow"/> and <see cref="AdaptiveFlow"/>.</summary>
public static class Adaptive
{
    /// <summary>Higher stays longer. The children sharing the top priority are never dropped.</summary>
    public static readonly DependencyProperty PriorityProperty =
        DependencyProperty.RegisterAttached("Priority", typeof(int), typeof(Adaptive), new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>Share of the leftover room this child takes. 0 keeps it at its own size.</summary>
    public static readonly DependencyProperty WeightProperty =
        DependencyProperty.RegisterAttached("Weight", typeof(double), typeof(Adaptive), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>The width (in <see cref="AdaptiveFlow"/>) a group is comfortable at; its own size when unset.</summary>
    public static readonly DependencyProperty BasisProperty =
        DependencyProperty.RegisterAttached("Basis", typeof(double), typeof(Adaptive), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static int GetPriority(DependencyObject d) => (int)d.GetValue(PriorityProperty);
    public static void SetPriority(DependencyObject d, int v) => d.SetValue(PriorityProperty, v);
    public static double GetWeight(DependencyObject d) => (double)d.GetValue(WeightProperty);
    public static void SetWeight(DependencyObject d, double v) => d.SetValue(WeightProperty, v);
    public static double GetBasis(DependencyObject d) => (double)d.GetValue(BasisProperty);
    public static void SetBasis(DependencyObject d, double v) => d.SetValue(BasisProperty, v);

    /// <summary>Takes a child out of sight without collapsing it: zero size, nothing drawn, nothing to click.</summary>
    internal static void Hide(UIElement child)
    {
        child.Arrange(new Rect(0, 0, 0, 0));
        child.Clip = Geometry.Empty;
        child.IsHitTestVisible = false;
    }

    internal static void Show(UIElement child, Rect slot)
    {
        if (child.Clip == Geometry.Empty) child.ClearValue(UIElement.ClipProperty);
        child.ClearValue(UIElement.IsHitTestVisibleProperty);
        child.Arrange(slot);
    }

    internal static IEnumerable<UIElement> Present(UIElementCollection children) =>
        children.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed);
}

public enum AdaptiveAlign { Start, Center, End }

/// <summary>
/// A row (or column) that drops its least important children, one by one, as the room runs out — the opposite
/// of a toolbar that overflows. Whatever room is left over goes to the children with a weight, or, when none
/// has one, shifts the whole line to the start, centre or end.
/// </summary>
public sealed class PriorityRow : Panel
{
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(PriorityRow),
            new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(PriorityRow),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty AlignProperty =
        DependencyProperty.Register(nameof(Align), typeof(AdaptiveAlign), typeof(PriorityRow),
            new FrameworkPropertyMetadata(AdaptiveAlign.Start, FrameworkPropertyMetadataOptions.AffectsArrange));

    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    public AdaptiveAlign Align { get => (AdaptiveAlign)GetValue(AlignProperty); set => SetValue(AlignProperty, value); }

    readonly Dictionary<UIElement, double> _sizes = [];
    readonly HashSet<UIElement> _shown = [];
    double _used;

    bool Horizontal => Orientation == Orientation.Horizontal;

    /// <summary>What a child needs along the line: its own size, or only its minimum when it can stretch.</summary>
    double Base(UIElement child, double natural)
    {
        if (Adaptive.GetWeight(child) <= 0) return natural;
        var min = child is FrameworkElement f ? (Horizontal ? f.MinWidth : f.MinHeight) : 0;
        return Math.Min(natural, min);
    }

    /// <summary>
    /// The narrowest this row can be and still show what must stay. WPF clamps a measured size to the room it
    /// was offered, so measuring at zero width cannot tell: the row has to say it itself.
    /// </summary>
    public double MinimumWidth()
    {
        var kids = Adaptive.Present(InternalChildren).ToList();
        if (kids.Count == 0) return 0;
        if (!Horizontal) return kids.Max(MinimumWidthOf);
        int top = kids.Max(Adaptive.GetPriority);
        var essentials = kids.Where(k => Adaptive.GetPriority(k) == top).ToList();
        return essentials.Sum(MinimumWidthOf) + Spacing * (essentials.Count - 1);
    }

    /// <summary>The narrowest width an element can take: a row's essentials, a stretching child's minimum, else its own width.</summary>
    internal static double MinimumWidthOf(UIElement el)
    {
        double margin = el is FrameworkElement f ? f.Margin.Left + f.Margin.Right : 0;
        if (el is PriorityRow row) return row.MinimumWidth() + margin;
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (Adaptive.GetWeight(el) > 0 && el is FrameworkElement fe) return Math.Min(el.DesiredSize.Width, fe.MinWidth + margin);
        return el.DesiredSize.Width;
    }

    protected override Size MeasureOverride(Size available)
    {
        var kids = Adaptive.Present(InternalChildren).ToList();
        double room = Horizontal ? available.Width : available.Height;
        double cross = Horizontal ? available.Height : available.Width;
        var open = Horizontal ? new Size(double.PositiveInfinity, cross) : new Size(cross, double.PositiveInfinity);

        _sizes.Clear();
        _shown.Clear();
        foreach (var k in kids)
        {
            k.Measure(open);
            _sizes[k] = Base(k, Horizontal ? k.DesiredSize.Width : k.DesiredSize.Height);
        }
        if (kids.Count == 0) { _used = 0; return default; }

        int top = kids.Max(Adaptive.GetPriority);
        _used = 0;
        foreach (var k in kids.Select((c, i) => (c, i)).OrderByDescending(x => Adaptive.GetPriority(x.c)).ThenBy(x => x.i).Select(x => x.c))
        {
            double need = _sizes[k] + (_shown.Count > 0 ? Spacing : 0);
            if (Adaptive.GetPriority(k) < top && !double.IsInfinity(room) && _used + need > room) break;
            _shown.Add(k);
            _used += need;
        }

        double crossSize = _shown.Max(k => Horizontal ? k.DesiredSize.Height : k.DesiredSize.Width);
        return Horizontal ? new Size(_used, crossSize) : new Size(crossSize, _used);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var kids = Adaptive.Present(InternalChildren).ToList();
        double room = Horizontal ? final.Width : final.Height;
        var shown = kids.Where(_shown.Contains).ToList();

        double leftover = Math.Max(0, room - _used);
        double weights = shown.Sum(Adaptive.GetWeight);
        double offset = weights > 0 ? 0 : Align switch { AdaptiveAlign.Center => leftover / 2, AdaptiveAlign.End => leftover, _ => 0 };

        double at = offset;
        foreach (var k in kids)
        {
            if (!_shown.Contains(k)) { Adaptive.Hide(k); continue; }
            double size = _sizes[k] + (weights > 0 ? leftover * Adaptive.GetWeight(k) / weights : 0);
            // A stretching child was measured at its minimum; give it the size it now gets.
            if (Adaptive.GetWeight(k) > 0)
                k.Measure(Horizontal ? new Size(size, final.Height) : new Size(final.Width, size));
            var slot = Horizontal
                ? new Rect(at, 0, size, final.Height)
                : new Rect(0, at, final.Width, size);
            Adaptive.Show(k, slot);
            at += size + Spacing;
        }
        return final;
    }
}

/// <summary>
/// Lays groups out like a flex row that wraps: side by side when they all fit; stacked one above another when
/// the panel is too narrow but tall enough; otherwise a single row in which the groups give way by priority,
/// each shrinking to its essentials first (see <see cref="PriorityRow"/>).
/// </summary>
public sealed class AdaptiveFlow : Panel
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(AdaptiveFlow),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    readonly Dictionary<UIElement, Rect> _plan = [];

    protected override Size MeasureOverride(Size available)
    {
        _plan.Clear();
        var kids = Adaptive.Present(InternalChildren).ToList();
        if (kids.Count == 0) return default;

        double w = available.Width, h = available.Height;
        var open = new Size(double.PositiveInfinity, h);
        foreach (var k in kids) k.Measure(open);
        double Basis(UIElement k) => double.IsNaN(Adaptive.GetBasis(k)) ? k.DesiredSize.Width : Adaptive.GetBasis(k);

        // 1. Everything side by side.
        double together = kids.Sum(Basis) + Spacing * (kids.Count - 1);
        if (double.IsInfinity(w) || together <= w)
        {
            Row(kids, kids.Select(Basis).ToList(), double.IsInfinity(w) ? together : w, 0, h, h);
            return new Size(double.IsInfinity(w) ? together : w, h);
        }

        // 2. Too narrow: stacked, if the height allows it.
        if (!double.IsInfinity(h) && TryStack(kids, Basis, w, h)) return new Size(w, h);

        // 3. One row, each group giving way: first the groups, then what is inside them.
        var minimum = new Dictionary<UIElement, double>();
        foreach (var k in kids) minimum[k] = PriorityRow.MinimumWidthOf(k);
        int top = kids.Max(Adaptive.GetPriority);
        var keep = new List<UIElement>();
        double used = 0;
        foreach (var k in kids.Select((c, i) => (c, i)).OrderByDescending(x => Adaptive.GetPriority(x.c)).ThenBy(x => x.i).Select(x => x.c))
        {
            double need = minimum[k] + (keep.Count > 0 ? Spacing : 0);
            if (Adaptive.GetPriority(k) < top && used + need > w) break;
            keep.Add(k);
            used += need;
        }
        var ordered = kids.Where(keep.Contains).ToList();
        Row(ordered, ordered.Select(k => minimum[k]).ToList(), w, 0, h, h);
        return new Size(w, h);
    }

    /// <summary>Places the groups on one line, sharing the leftover width out by weight, and measures each at its share.</summary>
    double Row(List<UIElement> kids, List<double> sizes, double width, double top, double height, double measureHeight)
    {
        double leftover = Math.Max(0, width - sizes.Sum() - Spacing * (kids.Count - 1));
        double weights = kids.Sum(Adaptive.GetWeight);
        double x = 0, tallest = 0;
        for (int i = 0; i < kids.Count; i++)
        {
            double size = sizes[i] + (weights > 0 ? leftover * Adaptive.GetWeight(kids[i]) / weights : 0);
            kids[i].Measure(new Size(size, measureHeight));
            _plan[kids[i]] = new Rect(x, top, size, height);
            tallest = Math.Max(tallest, kids[i].DesiredSize.Height);
            x += size + Spacing;
        }
        return tallest;
    }

    bool TryStack(List<UIElement> kids, Func<UIElement, double> basis, double w, double h)
    {
        // Greedy lines by comfortable width; a group wider than the panel gets a line of its own.
        var lines = new List<List<UIElement>> { new() };
        double lineWidth = 0;
        foreach (var k in kids)
        {
            double b = Math.Min(basis(k), w);
            if (lines[^1].Count > 0 && lineWidth + Spacing + b > w) { lines.Add([]); lineWidth = 0; }
            lineWidth += (lines[^1].Count > 0 ? Spacing : 0) + b;
            lines[^1].Add(k);
        }
        if (lines.Count < 2) return false;

        var rects = new Dictionary<UIElement, Rect>();
        double y = 0;
        var heights = new List<double>();
        foreach (var line in lines)
        {
            var sizes = line.Select(k => Math.Min(basis(k), w)).ToList();
            double leftover = Math.Max(0, w - sizes.Sum() - Spacing * (line.Count - 1));
            double weights = line.Sum(Adaptive.GetWeight);
            double lineHeight = 0, x = 0;
            var measured = new List<(UIElement K, double X, double Size)>();
            for (int i = 0; i < line.Count; i++)
            {
                double size = sizes[i] + (weights > 0 ? leftover * Adaptive.GetWeight(line[i]) / weights : 0);
                line[i].Measure(new Size(size, double.PositiveInfinity));
                lineHeight = Math.Max(lineHeight, line[i].DesiredSize.Height);
                measured.Add((line[i], x, size));
                x += size + Spacing;
            }
            foreach (var m in measured) rects[m.K] = new Rect(m.X, y, m.Size, lineHeight);
            heights.Add(lineHeight);
            y += lineHeight + Spacing;
        }
        double total = heights.Sum() + Spacing * (lines.Count - 1);
        if (total > h) return false;

        double drop = (h - total) / 2; // the block sits in the middle of the panel
        foreach (var (k, r) in rects) _plan[k] = new Rect(r.X, r.Y + drop, r.Width, r.Height);
        return true;
    }

    protected override Size ArrangeOverride(Size final)
    {
        foreach (var k in Adaptive.Present(InternalChildren))
        {
            if (_plan.TryGetValue(k, out var slot)) Adaptive.Show(k, slot);
            else Adaptive.Hide(k);
        }
        return final;
    }
}
