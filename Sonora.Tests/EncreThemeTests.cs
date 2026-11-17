using Sonora.Appearance.Theming;

namespace Sonora.Tests;

/// <summary>The Encre theme, and the ink frame it introduces.</summary>
public class EncreThemeTests
{
    static ThemeCatalog Loaded()
    {
        var c = new ThemeCatalog(TestThemes.TempDir());
        c.Load();
        return c;
    }

    [Fact]
    public void EncreTheme_HasLightAndDarkSubThemes()
    {
        var c = Loaded();
        Assert.Equal(new[] { "encre", "encre-sombre" }, c.PalettesFor("encre").Select(t => t.Id));
        Assert.Equal("Encre", ThemeStyles.Get("encre").Name);
        Assert.Equal(new[] { "Clair", "Sombre" }, c.PalettesFor("encre").Select(t => t.Name));
    }

    [Fact]
    public void EncreColours_AreThoseOfThePalette()
    {
        var c = Loaded();
        var light = c.Get("encre");
        Assert.Equal("#E8EEF3", light.Colors.Window);     // color-bg
        Assert.Equal("#FFFFFF", light.Colors.Surface);    // color-surface
        Assert.Equal("#0E1A2B", light.Colors.TextPrimary); // color-text
        Assert.Equal("#1E3350", light.Colors.Accent);     // color-accent
        Assert.Equal("#0E1A2B", light.Frame!.Background); // color-ink
        Assert.Equal("#3BB5CB", light.Frame.Accent);      // color-accent-2
        var dark = c.Get("encre-sombre");
        Assert.Equal("#0B1522", dark.Colors.Window);
        Assert.Equal("#15243A", dark.Colors.Surface);
        Assert.Equal("#B9C6D6", dark.Colors.Accent);
        Assert.Equal("#060D17", dark.Frame!.Background);
        Assert.Equal(new[] { "Barlow Condensed", "Figtree" }, new[] { light.Fonts.Display, light.Fonts.Ui });
    }

    [Fact]
    public void Frame_RoundTripsAndIsOptional()
    {
        var t = TestThemes.Verre();
        Assert.Null(ThemeDefinition.Parse(t.ToJson(), TestThemes.Verre()).Frame);
        t.Frame = new FrameColors { Background = "#0E1A2B", Text = "#FFFFFF", TextSecondary = "#B3C1CE", Accent = "#3BB5CB", OnAccent = "#0E1A2B" };
        var back = ThemeDefinition.Parse(t.ToJson(), TestThemes.Verre());
        Assert.Equal("#3BB5CB", back.Frame!.Accent);
        Assert.Equal("#B3C1CE", back.Frame.TextSecondary);
    }

    [Fact]
    public void Validate_ReportsBadFrameColour()
    {
        var t = TestThemes.Verre();
        t.Frame = new FrameColors { Background = "encre", Text = "#FFFFFF", TextSecondary = "#B3C1CE", Accent = "#3BB5CB", OnAccent = "#0E1A2B" };
        Assert.Contains(t.Validate(), m => m.Contains("cadre", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FrameText_IsReadableOnFrame()
    {
        static Argb P(string h) => Argb.TryParse(h, out var c) ? c : throw new FormatException(h);
        foreach (var t in Loaded().Themes.Where(t => t.Frame != null))
        {
            Assert.True(Argb.Contrast(P(t.Frame!.Text), P(t.Frame.Background)) >= 4.5, t.Id);
            Assert.True(Argb.Contrast(P(t.Frame.TextSecondary), P(t.Frame.Background)) >= 4.5, t.Id);
        }
    }
}
