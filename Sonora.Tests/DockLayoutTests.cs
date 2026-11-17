using Sonora.Appearance.Layout;
using Sonora.Appearance.Theming;

namespace Sonora.Tests;

public class DockLayoutTests
{
    static IEnumerable<string> AllPanels(DockLayout l) =>
        new[] { l.Left, l.Right, l.Top, l.Bottom }.SelectMany(z => z.Groups).SelectMany(g => g.Panels).Concat(l.Hidden);

    [Fact]
    public void Default_ContainsEveryPanelOnce()
    {
        var l = DockLayout.Default();
        Assert.Equal(PanelIds.All.OrderBy(x => x), AllPanels(l).OrderBy(x => x));
    }

    [Fact]
    public void MoveToZone_LibraryToRight()
    {
        var l = DockLayout.Default();
        DockLayoutOps.MoveToZone(l, "library", DockZone.Right);
        Assert.Equal(DockZone.Right, DockLayoutOps.Find(l, "library")!.Value.Zone);
        Assert.False(l.Right.IsCollapsed);
        Assert.Single(l.Left.Groups);
    }

    [Fact]
    public void MoveToZone_LastPanelLeavesNoEmptyGroup()
    {
        var l = DockLayout.Default();
        DockLayoutOps.MoveToZone(l, "player", DockZone.Top);
        Assert.Empty(l.Bottom.Groups);
        Assert.Equal("player", l.Top.Groups.Single().Panels.Single());
    }

    [Fact]
    public void MoveToGroup_TabMakesItActive()
    {
        var l = DockLayout.Default();
        DockLayoutOps.MoveToGroup(l, "queue", DockZone.Left, 1, DropPosition.Tab);
        var g = l.Left.Groups[1];
        Assert.Equal(new[] { "library", "queue" }, g.Panels);
        Assert.Equal(1, g.ActiveIndex);
        Assert.Empty(l.Right.Groups);
    }

    [Fact]
    public void MoveToGroup_BeforeInsertsNewGroup()
    {
        var l = DockLayout.Default();
        DockLayoutOps.MoveToGroup(l, "queue", DockZone.Left, 0, DropPosition.Before);
        Assert.Equal("queue", l.Left.Groups[0].Panels.Single());
        Assert.Equal(3, l.Left.Groups.Count);
    }

    [Fact]
    public void MoveToGroup_OntoItselfIsNoOp()
    {
        var l = DockLayout.Default();
        var before = l.ToJson();
        DockLayoutOps.MoveToGroup(l, "library", DockZone.Left, 1, DropPosition.Tab);
        Assert.Equal(before, l.ToJson());
    }

    [Fact]
    public void CloseThenShow_ReturnsToLastZone()
    {
        var l = DockLayout.Default();
        DockLayoutOps.MoveToZone(l, "library", DockZone.Right);
        DockLayoutOps.Close(l, "library");
        Assert.Contains("library", l.Hidden);
        DockLayoutOps.Show(l, "library");
        Assert.Equal(DockZone.Right, DockLayoutOps.Find(l, "library")!.Value.Zone);
        Assert.DoesNotContain("library", l.Hidden);
    }

    [Fact]
    public void Repair_RemovesDuplicatesUnknownAndAddsMissing()
    {
        var l = DockLayout.Default();
        l.Left.Groups[0].Panels.Add("library");
        l.Right.Groups[0].Panels.Add("lyrics");
        l.Bottom.Groups.Clear();
        var r = DockLayoutOps.Repair(l, 1400, 900);
        Assert.Equal(PanelIds.All.OrderBy(x => x), AllPanels(r).OrderBy(x => x));
        Assert.Equal(DockZone.Bottom, DockLayoutOps.Find(r, "player")!.Value.Zone);
        Assert.DoesNotContain(r.Left.Groups.Concat(r.Right.Groups), g => g.Panels.Count == 0);
    }

    [Fact]
    public void Repair_ClampsSizesToWindow()
    {
        var l = DockLayout.Default();
        l.Left.Size = 5000;
        l.Bottom.Size = 5;
        var r = DockLayoutOps.Repair(l, 1000, 700);
        Assert.Equal(450, r.Left.Size);
        Assert.Equal(72, r.Bottom.Size);
    }

    [Fact]
    public void Repair_AllHiddenStillUsable()
    {
        var l = DockLayout.Default();
        foreach (var p in PanelIds.All) DockLayoutOps.Close(l, p);
        var r = DockLayoutOps.Repair(l, 1400, 900);
        Assert.NotNull(DockLayoutOps.Find(r, "player"));
    }

    [Fact]
    public void Repair_NullOrGarbageGivesDefault() =>
        Assert.Equal(DockLayout.Default().ToJson(), DockLayoutOps.Repair(DockLayout.Parse("{]"), 1400, 900).ToJson());

    [Fact]
    public void Json_RoundTrip()
    {
        var l = DockLayout.Default();
        DockLayoutOps.MoveToZone(l, "queue", DockZone.Top);
        Assert.Equal(l.ToJson(), DockLayout.Parse(l.ToJson())!.ToJson());
    }

    [Fact]
    public void Repair_DropsAPanelThatNoLongerExistsAndKeepsTheRest()
    {
        // A layout saved before the "now playing" panel was folded into the player must still open.
        const string saved = """
            {"left":{"size":280,"groups":[{"panels":["navigation","library"],"activeIndex":0,"ratio":1}]},
             "right":{"size":420,"groups":[{"panels":["nowPlaying"],"activeIndex":0,"ratio":1}]},
             "top":{"size":0,"groups":[]},
             "bottom":{"size":96,"groups":[{"panels":["player"],"activeIndex":0,"ratio":1}]},
             "hidden":["queue"],"lastZone":{"queue":"right"}}
            """;
        var l = DockLayoutOps.Repair(DockLayout.Parse(saved)!, 1320, 840);

        Assert.DoesNotContain("nowPlaying", AllPanels(l));
        Assert.Equal(PanelIds.All.OrderBy(x => x), AllPanels(l).OrderBy(x => x));
        Assert.Equal(DockZone.Bottom, DockLayoutOps.Find(l, PanelIds.Player)!.Value.Zone);
        Assert.DoesNotContain(l.Right.Groups, g => g.Panels.Count == 0);
    }
}
