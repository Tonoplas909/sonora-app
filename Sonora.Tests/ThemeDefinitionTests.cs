using Sonora.Appearance.Theming;

namespace Sonora.Tests;

public class ThemeDefinitionTests
{
    [Fact]
    public void Argb_ParsesBothForms()
    {
        Assert.True(Argb.TryParse("#7C9CFF", out var a));
        Assert.Equal(new Argb(255, 0x7C, 0x9C, 0xFF), a);
        Assert.True(Argb.TryParse("#807C9CFF", out var b));
        Assert.Equal(0x80, b.A);
        Assert.False(Argb.TryParse("7C9CFF", out _));
        Assert.False(Argb.TryParse("#12345", out _));
    }

    [Fact]
    public void Argb_ContrastBlackWhiteIs21() =>
        Assert.Equal(21.0, Argb.Contrast(new(255, 0, 0, 0), new(255, 255, 255, 255)), 1);

    [Fact]
    public void Argb_HslRoundTrip()
    {
        var c = new Argb(255, 0x7C, 0x9C, 0xFF);
        var (h, s, l) = c.ToHsl();
        var back = Argb.FromHsl(h, s, l);
        Assert.InRange(Math.Abs(back.R - c.R) + Math.Abs(back.G - c.G) + Math.Abs(back.B - c.B), 0, 3);
    }

    [Fact]
    public void Parse_TolerantToBadFields()
    {
        var fallback = TestThemes.Verre();
        var t = ThemeDefinition.Parse(
            """{"id":"x","name":"X","colors":{"accent":"rouge","surface":"#112233"},"fontScale":"gros","cornerRadius":99,"density":"énorme"}""",
            fallback);
        Assert.Equal(fallback.Colors.Accent, t.Colors.Accent);
        Assert.Equal("#112233", t.Colors.Surface);
        Assert.Equal(fallback.FontScale, t.FontScale);
        Assert.Equal(24, t.CornerRadius);
        Assert.Equal("normal", t.Density);
        Assert.Equal(fallback.Colors.Window, t.Colors.Window);
    }

    [Fact]
    public void Parse_InvalidJsonReturnsFallbackCopy()
    {
        var f = TestThemes.Verre();
        var t = ThemeDefinition.Parse("{oops", f);
        Assert.Equal(f.ToJson(), t.ToJson());
        Assert.NotSame(f, t);
    }

    [Fact]
    public void Json_RoundTrip()
    {
        var f = TestThemes.Verre();
        Assert.Equal(f.ToJson(), ThemeDefinition.Parse(f.ToJson(), f).ToJson());
    }

    [Fact]
    public void Validate_ReportsBadColor()
    {
        var t = TestThemes.Verre();
        t.Colors.Accent = "#GG0000";
        Assert.Contains(t.Validate(), m => m.Contains("accent", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("compact", 40)]
    [InlineData("normal", 56)]
    [InlineData("confort", 64)]
    public void RowHeight_FollowsDensity(string d, double h)
    {
        var t = TestThemes.Verre();
        t.Density = d;
        Assert.Equal(h, t.RowHeight);
    }
}
