using Sonora.Appearance.Theming;

namespace Sonora.Tests;

static class TestThemes
{
    /// <summary>The Verre theme exactly as written in the spec (§2.1).</summary>
    public static ThemeDefinition Verre() => new()
    {
        Id = "verre",
        Name = "Verre",
        BuiltIn = true,
        Colors = new ThemeColors
        {
            Window = "#0F1117", Surface = "#181B23", SurfaceAlt = "#20242F",
            Hover = "#2A2F3C", Selected = "#343A4A", Border = "#2C3140",
            TextPrimary = "#F4F5F8", TextSecondary = "#A9AEBD", TextMuted = "#6E7383",
            Accent = "#7C9CFF", OnAccent = "#0B0E18", Rail = "#3A4050",
        },
        Fonts = new ThemeFonts { Ui = "Segoe UI Variable Text", Display = "Segoe UI Variable Display", Numbers = "Segoe UI Variable Text" },
        FontScale = 1.0,
        CornerRadius = 12,
        Density = "normal",
        PanelGap = 8,
        Background = new ThemeBackground { Mode = "gradient", Colors = ["#141826", "#0F1117"], ArtworkStrength = 0 },
        AccentFromArtwork = true,
        Animations = true,
    };

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sonora-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
