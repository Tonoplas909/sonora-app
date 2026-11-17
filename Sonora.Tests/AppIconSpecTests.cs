using Sonora.Appearance.Theming;

namespace Sonora.Tests;

public class AppIconSpecTests
{
    static Argb C(string hex) { Assert.True(Argb.TryParse(hex, out var c)); return c; }

    [Theory]
    [InlineData("#FFD166")] // a light accent: the bars must go dark
    [InlineData("#5B40C7")] // a dark accent: the bars must go light
    [InlineData("#FF4F9A")]
    [InlineData("#00FF00")]
    [InlineData("#101010")]
    [InlineData("#FFFFFF")]
    public void Bars_AlwaysReadOnTheTile(string accent)
    {
        var a = C(accent);
        var spec = AppIconSpec.For("liquid", a);
        Assert.True(Argb.Contrast(spec.Bars, a) >= 4.5, $"{accent}: bars {spec.Bars.ToHex()}");
    }

    [Fact]
    public void Tile_GoesFromDarkerToLighterThanTheAccent()
    {
        var accent = C("#7B9DFF");
        var spec = AppIconSpec.For("liquid", accent);
        Assert.True(spec.TopLeft.RelativeLuminance < accent.RelativeLuminance);
        Assert.True(spec.BottomRight.RelativeLuminance > accent.RelativeLuminance);
    }

    [Fact]
    public void Accent_AlphaIsIgnored()
    {
        var a = AppIconSpec.For("liquid", new Argb(80, 0x7B, 0x9D, 0xFF));
        var b = AppIconSpec.For("liquid", new Argb(255, 0x7B, 0x9D, 0xFF));
        Assert.Equal(b, a);
        Assert.Equal(255, a.TopLeft.A);
    }

    [Theory]
    [InlineData("liquid")]
    [InlineData("terminal")]
    [InlineData("affiche")]
    [InlineData("soul")]
    [InlineData("fonction")]
    [InlineData("encre")]
    [InlineData("standard")]
    [InlineData("not-a-style")]
    [InlineData(null)]
    public void Corners_StayInsideTheTile(string? style)
    {
        var s = AppIconSpec.For(style, C("#FF8A3D"));
        foreach (var r in new[] { s.RadiusTopLeft, s.RadiusTopRight, s.RadiusBottomRight, s.RadiusBottomLeft })
            Assert.InRange(r, 0, 0.5);
    }

    [Fact]
    public void EachStyleHasItsOwnTileShape()
    {
        var accent = C("#FF8A3D");
        var shapes = new[] { "liquid", "terminal", "affiche", "soul", "fonction" }
            .Select(s => AppIconSpec.For(s, accent)).Select(s => (s.RadiusTopLeft, s.RadiusTopRight)).ToList();
        Assert.Equal(shapes.Count, shapes.Distinct().Count());

        Assert.Equal(0, AppIconSpec.For("affiche", accent).RadiusTopLeft);              // a poster is square
        var soul = AppIconSpec.For("soul", accent);
        Assert.True(soul.RadiusTopLeft > soul.RadiusTopRight);                          // leaf-shaped: opposite corners alike
        Assert.Equal(soul.RadiusTopLeft, soul.RadiusBottomRight);
        Assert.Equal(AppIconSpec.For("liquid", accent).RadiusTopLeft, AppIconSpec.For("standard", accent).RadiusTopLeft);
        Assert.Equal(AppIconSpec.For("liquid", accent).RadiusTopLeft, AppIconSpec.For("something-new", accent).RadiusTopLeft);
    }

    [Fact]
    public void LogoBars_FitInsideTheTileAndDoNotOverlap()
    {
        var bars = AppIconSpec.LogoBars;
        Assert.Equal(5, bars.Count);
        foreach (var b in bars)
        {
            Assert.True(b.X >= AppIconSpec.Margin && b.X + b.Width <= 1 - AppIconSpec.Margin);
            Assert.True(b.Y >= AppIconSpec.Margin && b.Y + b.Height <= 1 - AppIconSpec.Margin);
        }
        for (int i = 1; i < bars.Count; i++) Assert.True(bars[i].X > bars[i - 1].X + bars[i - 1].Width);
    }

    [Fact]
    public void Signature_IsTheSameForTheSameIconAndDiffersOtherwise()
    {
        var a = AppIconSpec.For("liquid", C("#7B9DFF")).Signature;
        Assert.Equal(a, AppIconSpec.For("standard", C("#7B9DFF")).Signature); // same colours, same corners: one drawing
        Assert.NotEqual(a, AppIconSpec.For("liquid", C("#FF4F9A")).Signature);
        Assert.NotEqual(a, AppIconSpec.For("soul", C("#7B9DFF")).Signature);
    }
}
