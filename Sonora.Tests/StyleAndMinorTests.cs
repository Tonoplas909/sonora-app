using Sonora.Appearance.Layout;
using Sonora.Appearance.Theming;

namespace Sonora.Tests;

public class StyleAndMinorTests
{
    [Fact]
    public void Style_RoundTripAndUnknownIsStandard()
    {
        var t = TestThemes.Verre();
        t.Style = "terminal";
        Assert.Equal("terminal", ThemeDefinition.Parse(t.ToJson(), TestThemes.Verre()).Style);
        var unknown = ThemeDefinition.Parse("""{"id":"x","name":"X","style":"baroque"}""", TestThemes.Verre());
        Assert.Equal("standard", unknown.Style);
    }

    [Fact]
    public void BuiltIns_HaveTheirStyles()
    {
        var c = new ThemeCatalog(TestThemes.TempDir());
        c.Load();
        Assert.Equal("liquid", c.Get("liquide").Style);
        Assert.Equal("terminal", c.Get("studio").Style);
        Assert.Equal("standard", c.Get("clair").Style);
        Assert.Equal("Cascadia Mono", c.Get("studio").Fonts.Ui);
    }

    [Fact]
    public void Duplicate_SetsBasedOn()
    {
        var c = new ThemeCatalog(TestThemes.TempDir());
        c.Load();
        var a = c.Duplicate("studio");
        var b = c.Duplicate(a.Id);
        Assert.Equal("studio", a.BasedOn);
        Assert.Equal("studio", b.BasedOn);
    }

    [Fact]
    public void ResetToBase_RestoresSourceKeepsIdentity()
    {
        var c = new ThemeCatalog(TestThemes.TempDir());
        c.Load();
        var d = c.Duplicate("papier");
        c.Rename(d.Id, "Mon papier");
        d.Colors.Accent = "#00FF00";
        d.CornerRadius = 3;
        c.Save(d);
        var r = c.ResetToBase(d.Id);
        Assert.Equal(d.Id, r.Id);
        Assert.Equal("Mon papier", r.Name);
        Assert.False(r.BuiltIn);
        Assert.Equal(c.Get("papier").Colors.Accent, r.Colors.Accent);
        Assert.Equal(c.Get("papier").CornerRadius, r.CornerRadius);
    }

    [Fact]
    public void Load_MissingIdUsesFileName()
    {
        var dir = TestThemes.TempDir();
        var json = new ThemeCatalog(TestThemes.TempDir()).Also(x => x.Load()).Get("liquide").ToJson()
            .Replace("\"id\": \"liquide\",", "").Replace("\"builtIn\": true", "\"builtIn\": false");
        File.WriteAllText(Path.Combine(dir, "Mon Thème.json"), json);
        var c = new ThemeCatalog(dir);
        c.Load();
        Assert.Empty(c.LoadErrors);
        Assert.Contains(c.Themes, t => t.Id == "mon-th-me" && !t.BuiltIn);
    }

    [Fact]
    public void Close_TabBeforeActiveKeepsActive()
    {
        var l = DockLayout.Default();
        DockLayoutOps.MoveToGroup(l, "queue", DockZone.Left, 1, DropPosition.Tab);   // [library, queue], active queue
        DockLayoutOps.MoveToGroup(l, "nowPlaying", DockZone.Left, 1, DropPosition.Tab); // [library, queue, nowPlaying]
        DockLayoutOps.SetActive(l, "queue");
        DockLayoutOps.Close(l, "library");
        var g = l.Left.Groups[1];
        Assert.Equal("queue", g.Panels[g.ActiveIndex]);
    }
}

static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
