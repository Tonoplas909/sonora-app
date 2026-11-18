using Sonora.Appearance.Features;

namespace Sonora.Tests;

public class HeaderCollapseTests
{
    const double Header = 260;   // what collapsing gives back to the list
    const double Viewport = 500; // the list's height with the header expanded

    /// <summary>
    /// The page as WPF lays it out: the list scrolls by (extent - viewport), the viewport is taller once the header is
    /// collapsed, and the offset is clamped to what the layout allows. Returns each state the header goes through.
    /// </summary>
    static List<bool> Settle(double extent, double wantedOffset, bool startCollapsed)
    {
        bool collapsed = startCollapsed;
        var states = new List<bool>();
        for (int step = 0; step < 20; step++)
        {
            double viewport = Viewport + (collapsed ? Header : 0);
            double offset = Math.Clamp(wantedOffset, 0, Math.Max(0, extent - viewport));
            bool next = HeaderCollapse.ShouldCollapse(collapsed, offset, extent, viewport, Header);
            states.Add(next);
            if (next == collapsed) break;
            collapsed = next;
        }
        return states;
    }

    [Theory]
    [InlineData(Viewport + 30)]   // overflows by less than the header: the case that flickered
    [InlineData(Viewport + 150)]
    [InlineData(Viewport + Header)]
    [InlineData(Viewport + Header + 20)]
    [InlineData(Viewport + Header + 24 + 1)]
    [InlineData(Viewport + 2000)]
    public void TheHeaderSettlesWhateverTheListHeightAndHoweverFarItIsScrolled(double extent)
    {
        foreach (var wanted in new[] { 0.0, 10, 25, 40, 100, 5000 })
            foreach (var start in new[] { false, true })
                Assert.True(Settle(extent, wanted, start).Count <= 2, $"extent {extent}, offset {wanted}, start collapsed {start}");
    }

    [Fact]
    public void AListThatWouldStopScrollingOnceCollapsedKeepsTheHeader()
        => Assert.False(HeaderCollapse.ShouldCollapse(false, offset: 30, extent: Viewport + 40, viewport: Viewport, headerHeight: Header));

    [Fact]
    public void ALongListCollapsesPastTheThresholdAndExpandsAtTheTop()
    {
        double extent = Viewport + 3000;
        Assert.False(HeaderCollapse.ShouldCollapse(false, 24, extent, Viewport, Header));
        Assert.True(HeaderCollapse.ShouldCollapse(false, 25, extent, Viewport, Header));
        Assert.True(HeaderCollapse.ShouldCollapse(true, 25, extent, Viewport + Header, Header));
        Assert.False(HeaderCollapse.ShouldCollapse(true, 24, extent, Viewport + Header, Header));
    }

    [Fact]
    public void TheOldRuleFlickeredOnAShortOverflow()
    {
        // offset > 24 alone, with the list 40 px taller than its viewport: collapse -> list no longer scrolls
        // (offset 0) -> expand -> offset 40 > 24 -> collapse ... never settles.
        double extent = Viewport + 40;
        bool collapsed = false;
        int changes = 0;
        for (int i = 0; i < 10; i++)
        {
            double viewport = Viewport + (collapsed ? Header : 0);
            double offset = Math.Clamp(40, 0, Math.Max(0, extent - viewport));
            bool next = offset > HeaderCollapse.ScrollThreshold;
            if (next != collapsed) changes++;
            collapsed = next;
        }
        Assert.Equal(10, changes); // it changes state on every single step
    }
}
