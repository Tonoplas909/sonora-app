using System.Windows;
using System.Windows.Controls;

namespace Sonora.Views;

/// <summary>
/// A wrapping panel where every item in a row is stretched to that row's tallest item — so cards with
/// differing content (e.g. a description that wraps to a different number of lines) still line up with
/// even, non-ragged bottoms, without forcing every row in the whole list to share one fixed height.
/// </summary>
public sealed class RowStretchWrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double maxWidth = double.IsPositiveInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        foreach (UIElement child in InternalChildren) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double totalWidth = 0, totalHeight = 0;
        foreach (var row in BuildRows(maxWidth))
        {
            totalWidth = Math.Max(totalWidth, row.Sum(c => c.DesiredSize.Width));
            totalHeight += row.Max(c => c.DesiredSize.Height);
        }
        return new Size(Math.Min(totalWidth, maxWidth), totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var row in BuildRows(finalSize.Width))
        {
            double rowHeight = row.Max(c => c.DesiredSize.Height);
            double x = 0;
            foreach (var child in row)
            {
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, rowHeight));
                x += child.DesiredSize.Width;
            }
            y += rowHeight;
        }
        return finalSize;
    }

    /// <summary>Greedily packs already-measured children into rows, wrapping once a child would overflow the width.</summary>
    IEnumerable<List<UIElement>> BuildRows(double maxWidth)
    {
        var row = new List<UIElement>();
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            var width = child.DesiredSize.Width;
            if (row.Count > 0 && x + width > maxWidth)
            {
                yield return row;
                row = [];
                x = 0;
            }
            row.Add(child);
            x += width;
        }
        if (row.Count > 0) yield return row;
    }
}
