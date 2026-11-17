using Sonora.Appearance.Theming;

namespace Sonora.Tests;

public class ThemeCatalogTests
{
    readonly string dir = TestThemes.TempDir();

    ThemeCatalog New() => new(dir);

    string Temp(string name, string content)
    {
        var p = Path.Combine(TestThemes.TempDir(), name);
        File.WriteAllText(p, content);
        return p;
    }

    static Argb P(string hex) => Argb.TryParse(hex, out var c) ? c : throw new FormatException(hex);

    [Fact]
    public void Load_HasBuiltInsInOrder()
    {
        var c = New();
        c.Load();
        Assert.Equal(new[] { "liquide", "liquide-clair", "studio", "studio-ambre", "studio-cyan", "affiche", "affiche-tomate", "soul", "soul-lagune", "fonction", "fonction-nuit", "encre", "encre-sombre", "clair", "oled", "papier", "synthwave", "gta6" },
            c.Themes.Where(t => t.BuiltIn).Select(t => t.Id));
        Assert.Empty(c.LoadErrors);
    }

    [Fact]
    public void BuiltIns_AreValidAndReadable()
    {
        var c = New();
        c.Load();
        foreach (var t in c.Themes)
        {
            Assert.Empty(t.Validate());
            Assert.True(Argb.Contrast(P(t.Colors.TextPrimary), P(t.Colors.Surface)) >= 4.5, t.Id);
        }
    }

    [Fact]
    public void Duplicate_CreatesEditableCopyWithUniqueId()
    {
        var c = New();
        c.Load();
        var a = c.Duplicate("liquide");
        var b = c.Duplicate("liquide");
        Assert.Equal("liquide-copy", a.Id);
        Assert.Equal("liquide-copy-2", b.Id);
        Assert.False(a.BuiltIn);
        Assert.Equal("Ocean (copy)", a.Name);
        Assert.True(File.Exists(Path.Combine(dir, "liquide-copy.json")));
    }

    [Fact]
    public void Delete_BuiltInThrows()
    {
        var c = New();
        c.Load();
        Assert.Throws<InvalidOperationException>(() => c.Delete("studio"));
    }

    [Fact]
    public void Import_BuiltInIdGetsUniqueId()
    {
        var c = New();
        c.Load();
        var path = Temp("x.json", c.Get("studio").ToJson());
        var t = c.Import(path);
        Assert.Equal("studio-2", t.Id);
        Assert.False(t.BuiltIn);
        Assert.True(c.Get("studio").BuiltIn);
    }

    [Fact]
    public void Import_InvalidColorRejected()
    {
        var c = New();
        c.Load();
        var json = c.Get("liquide").ToJson().Replace("#7FD4FF", "bleu");
        Assert.Throws<InvalidDataException>(() => c.Import(Temp("y.json", json)));
    }

    [Fact]
    public void Load_BadUserFileIsReportedNotFatal()
    {
        File.WriteAllText(Path.Combine(dir, "cassé.json"), "{");
        var c = New();
        c.Load();
        Assert.Single(c.LoadErrors);
        Assert.Equal(18, c.Themes.Count);
    }

    [Fact]
    public void ExportImport_RoundTrip()
    {
        var c = New();
        c.Load();
        var d = c.Duplicate("papier");
        var p = Path.Combine(dir, "out.json");
        c.Export(d.Id, p);
        c.Delete(d.Id);
        var back = c.Import(p);
        Assert.Equal(d.Colors.Accent, back.Colors.Accent);
    }

    [Fact]
    public void Get_UnknownFallsBackToTheDefault()
    {
        var c = New();
        c.Load();
        Assert.Equal("liquide", c.Get("nope").Id);
    }
}


