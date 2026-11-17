using Sonora.Appearance.Theming;

namespace Sonora.Tests;

public class AccentPickerTests
{
    static IEnumerable<uint> Fill(int n, uint argb) => Enumerable.Repeat(argb, n);

    [Fact]
    public void Pick_DominantSaturatedHueWins()
    {
        var px = Fill(900, 0xFFE0402A).Concat(Fill(100, 0xFF2A40E0)).ToArray();
        var c = AccentPicker.Pick(px)!.Value;
        Assert.True(c.R > 200 && c.B < 80);
    }

    [Fact]
    public void Pick_GrayscaleReturnsNull() => Assert.Null(AccentPicker.Pick(Fill(1024, 0xFF808080).ToArray()));

    [Fact]
    public void Pick_TooDarkReturnsNull() => Assert.Null(AccentPicker.Pick(Fill(1024, 0xFF100505).ToArray()));

    [Fact]
    public void EnsureContrast_LiftsDarkAccentOnDarkSurface()
    {
        var s = new Argb(255, 0x18, 0x1B, 0x23);
        var r = AccentPicker.EnsureContrast(new Argb(255, 0x30, 0x10, 0x40), s);
        Assert.True(Argb.Contrast(r, s) >= 3.0);
    }

    [Fact]
    public void EnsureContrast_DarkensOnLightSurface()
    {
        var s = new Argb(255, 255, 255, 255);
        var r = AccentPicker.EnsureContrast(new Argb(255, 0xFF, 0xF0, 0x80), s);
        Assert.True(Argb.Contrast(r, s) >= 3.0);
    }

    [Fact]
    public void EnsureContrast_KeepsAlreadyGoodColor()
    {
        var s = new Argb(255, 0x18, 0x1B, 0x23);
        var a = new Argb(255, 0x7C, 0x9C, 0xFF);
        Assert.Equal(a, AccentPicker.EnsureContrast(a, s));
    }

    [Fact]
    public void OnColor_PicksReadableText()
    {
        Assert.Equal(new Argb(255, 0x0B, 0x0B, 0x0B), AccentPicker.OnColor(new Argb(255, 0xC6, 0xF1, 0x35)));
        Assert.Equal(new Argb(255, 255, 255, 255), AccentPicker.OnColor(new Argb(255, 0x6B, 0x4E, 0xFF)));
    }
}
