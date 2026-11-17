using Sonora.Appearance.Theming;

namespace Sonora.Tests;

/// <summary>Themes (structural styles) and their sub-themes (colour palettes).</summary>
public class TwoLevelThemeTests
{
    static ThemeCatalog Loaded()
    {
        var c = new ThemeCatalog(TestThemes.TempDir());
        c.Load();
        return c;
    }

    [Fact]
    public void Styles_InOrderWithFrenchNames()
    {
        Assert.Equal(new[] { "liquid", "terminal", "affiche", "soul", "fonction", "encre", "standard" }, ThemeStyles.All.Select(s => s.Id));
        Assert.Equal(new[] { "Verre liquide", "Studio", "Affiche", "Soul", "Fonction", "Encre", "Standard" }, ThemeStyles.All.Select(s => s.Name));
        Assert.All(ThemeStyles.All, s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));
        Assert.Contains("liquid", ThemeDefinition.Styles);
    }

    [Fact]
    public void EveryOfferedStyle_IsAKnownStyleWithBuiltInPalettes()
    {
        var c = Loaded();
        foreach (var style in ThemeStyles.All)
        {
            Assert.Contains(style.Id, ThemeDefinition.Styles);
            Assert.All(c.PalettesFor(style.Id), t => Assert.Equal(style.Id, t.Style));
        }
    }

    [Theory]
    [InlineData("affiche")]
    [InlineData("soul")]
    [InlineData("fonction")]
    public void NewStyles_RoundTrip(string style)
    {
        var t = TestThemes.Verre();
        t.Style = style;
        Assert.Equal(style, ThemeDefinition.Parse(t.ToJson(), TestThemes.Verre()).Style);
    }

    [Theory]
    [InlineData("verre", "liquide")]
    [InlineData("verre-aube", "liquide-clair")]
    [InlineData("immersif", "liquide")]
    [InlineData("immersif-ocre", "liquide-clair")]
    public void RetiredThemes_MapToTheirReplacement(string retired, string replacement)
    {
        var c = Loaded();
        Assert.Equal(replacement, ThemeCatalog.Resolve(retired));
        Assert.Equal(replacement, c.Get(retired).Id);
    }

    [Fact]
    public void RetiredThemeId_YieldsToAUserThemeWithThatId()
    {
        var c = Loaded();
        var copy = c.Duplicate("liquide");
        Assert.Equal("liquide", ThemeCatalog.Resolve("liquide"));
        Assert.Equal(copy.Id, c.Get(copy.Id).Id);
        Assert.True(ThemeCatalog.WasImmersive("immersif"));
        Assert.False(ThemeCatalog.WasImmersive("verre"));
    }

    [Theory]
    [InlineData("glass")]
    [InlineData("immersive")]
    public void RetiredStyles_BecomeLiquid(string style)
    {
        var parsed = ThemeDefinition.Parse("{\"id\":\"x\",\"name\":\"X\",\"style\":\"" + style + "\"}", TestThemes.Verre());
        Assert.Equal("liquid", parsed.Style);
        Assert.DoesNotContain(style, ThemeDefinition.Styles);
    }

    [Fact]
    public void LiquidStyle_RoundTrips()
    {
        var t = TestThemes.Verre();
        t.Style = "liquid";
        Assert.Equal("liquid", ThemeDefinition.Parse(t.ToJson(), TestThemes.Verre()).Style);
    }

    [Fact]
    public void PalettesFor_GroupsBuiltInsByStyle()
    {
        var c = Loaded();
        Assert.Equal(new[] { "liquide", "liquide-clair" }, c.PalettesFor("liquid").Select(t => t.Id));
        Assert.Equal(new[] { "studio", "studio-ambre", "studio-cyan" }, c.PalettesFor("terminal").Select(t => t.Id));
        Assert.Equal(new[] { "affiche", "affiche-tomate" }, c.PalettesFor("affiche").Select(t => t.Id));
        Assert.Equal(new[] { "soul", "soul-lagune" }, c.PalettesFor("soul").Select(t => t.Id));
        Assert.Equal(new[] { "fonction", "fonction-nuit" }, c.PalettesFor("fonction").Select(t => t.Id));
        Assert.Equal(new[] { "clair", "oled", "papier", "synthwave", "gta6" }, c.PalettesFor("standard").Select(t => t.Id));
        Assert.All(ThemeStyles.All, s => Assert.True(c.PalettesFor(s.Id).Count >= 2, s.Id));
    }

    [Fact]
    public void PalettesFor_IncludesDuplicatesInTheirTheme()
    {
        var c = Loaded();
        var d = c.Duplicate("studio-ambre");
        Assert.Contains(c.PalettesFor("terminal"), t => t.Id == d.Id);
        Assert.DoesNotContain(c.PalettesFor("standard"), t => t.Id == d.Id);
    }

    [Fact]
    public void PaletteNames_AreColourVariants()
    {
        var c = Loaded();
        Assert.Equal("Vert", c.Get("studio").Name);
        Assert.Equal("Océan", c.Get("liquide").Name);
        Assert.Equal("Lumière", c.Get("liquide-clair").Name);
        Assert.Equal(new[] { "Cobalt", "Tomate" }, c.PalettesFor("affiche").Select(t => t.Name));
        Assert.Equal(new[] { "Tournesol", "Lagune" }, c.PalettesFor("soul").Select(t => t.Name));
        Assert.Equal(new[] { "Gris", "Nuit" }, c.PalettesFor("fonction").Select(t => t.Name));
    }
}

