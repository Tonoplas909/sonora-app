namespace Sonora.Appearance.Features;

/// <summary>
/// When a track page's big header gives way to the compact line as its list scrolls. Collapsing hands the header's
/// height to the list, which shortens how far the list can scroll by that much: a list that overflows by less than
/// the header would stop scrolling, expand the header again, overflow again, and flicker for as long as it is held.
/// </summary>
public static class HeaderCollapse
{
    /// <summary>How far the list has scrolled before the header collapses (and below which it expands again), in pixels.</summary>
    public const double ScrollThreshold = 24;

    /// <param name="collapsed">Whether the header is collapsed now.</param>
    /// <param name="offset">How far the list has scrolled.</param>
    /// <param name="extent">The height of all the rows.</param>
    /// <param name="viewport">The height of the list as it is displayed now.</param>
    /// <param name="headerHeight">The most the header can give back to the list.</param>
    public static bool ShouldCollapse(bool collapsed, double offset, double extent, double viewport, double headerHeight)
    {
        if (offset <= ScrollThreshold) return false;
        if (collapsed) return true; // the list overflowed enough to collapse; expanding back happens only at the top
        return extent - viewport > headerHeight + ScrollThreshold; // it still scrolls once the header is gone
    }
}
