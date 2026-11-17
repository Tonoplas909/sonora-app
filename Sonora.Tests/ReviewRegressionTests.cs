using Sonora.Appearance.Layout;
using Sonora.Appearance.Theming;

namespace Sonora.Tests;

/// <summary>Regressions found by the final review.</summary>
public class ReviewRegressionTests
{
    [Fact]
    public void Import_TraversalIdStaysInsideThemesFolder()
    {
        var themes = Path.Combine(TestThemes.TempDir(), "themes"); // unique parent: nothing may land there
        var c = new ThemeCatalog(themes);
        c.Load();
        var json = c.Get("liquide").ToJson().Replace("\"id\": \"liquide\"", "\"id\": \"..\\\\state\"");
        var src = Path.Combine(TestThemes.TempDir(), "evil.json");
        File.WriteAllText(src, json);
        var t = c.Import(src);
        Assert.DoesNotContain("..", t.Id);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(themes)!, "state.json")));
        Assert.True(File.Exists(Path.Combine(themes, t.Id + ".json")));
    }

    [Fact]
    public void Import_RootedIdStaysInsideThemesFolder()
    {
        var themes = TestThemes.TempDir();
        var c = new ThemeCatalog(themes);
        c.Load();
        var outside = Path.Combine(TestThemes.TempDir(), "x");
        var json = c.Get("liquide").ToJson().Replace("\"id\": \"liquide\"", "\"id\": \"" + outside.Replace("\\", "\\\\") + "\"");
        var src = Path.Combine(TestThemes.TempDir(), "rooted.json");
        File.WriteAllText(src, json);
        var t = c.Import(src);
        Assert.False(File.Exists(outside + ".json"));
        Assert.True(File.Exists(Path.Combine(themes, t.Id + ".json")));
    }

    [Fact]
    public void Repair_NullZonesAndGroupsDoNotThrow()
    {
        var l = DockLayout.Parse("""{"left":null,"right":{"size":300,"groups":[null,{"panels":null}]},"hidden":null}""");
        var r = DockLayoutOps.Repair(l, 1400, 900);
        Assert.NotNull(DockLayoutOps.Find(r, "player"));
        Assert.NotNull(DockLayoutOps.Find(r, "library"));
    }

    [Fact]
    public void EnsureContrast_MidGraySurfaceReachesRatio()
    {
        var surface = new Argb(255, 0xBA, 0xBA, 0xBA);
        var r = AccentPicker.EnsureContrast(new Argb(255, 0xC0, 0xA0, 0x60), surface);
        Assert.True(Argb.Contrast(r, surface) >= 3.0, $"contrast {Argb.Contrast(r, surface):F2}");
    }
}

