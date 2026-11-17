namespace Sonora.Appearance.Theming;

/// <summary>One bar of the logo, in fractions of the icon's side (0..1).</summary>
public readonly record struct IconBar(double X, double Y, double Width, double Height);

/// <summary>
/// What the app's icon looks like for a theme: the sound-wave logo on a tile that takes its colour from the theme's
/// accent and its corner shape from the theme's style (a square tile for Affiche, leaf-shaped corners for Soul...).
/// Plain numbers, no drawing: the renderer in the app turns it into pixels, and the tests can check it.
/// </summary>
public sealed record AppIconSpec(
    Argb TopLeft, Argb BottomRight, Argb Bars,
    double RadiusTopLeft, double RadiusTopRight, double RadiusBottomRight, double RadiusBottomLeft)
{
    /// <summary>Space around the tile, so a rounded tile does not touch the edge of the icon.</summary>
    public const double Margin = 4.0 / 256;

    /// <summary>The five bars of the logo (measured on the original 256 px artwork).</summary>
    public static readonly IReadOnlyList<IconBar> LogoBars =
    [
        new(52 / 256.0, 80 / 256.0, 21 / 256.0, 97 / 256.0),
        new(85 / 256.0, 50 / 256.0, 21 / 256.0, 157 / 256.0),
        new(118 / 256.0, 68 / 256.0, 21 / 256.0, 121 / 256.0),
        new(151 / 256.0, 37 / 256.0, 21 / 256.0, 183 / 256.0),
        new(184 / 256.0, 91 / 256.0, 21 / 256.0, 75 / 256.0),
    ];

    /// <summary>The corner radius, in the units of the style's own icon shape (the <c>Shape.Icon</c> of its style file), per corner.</summary>
    static (double Tl, double Tr, double Br, double Bl) Corners(string style) => style switch
    {
        "affiche" => (0, 0, 0, 0),
        "terminal" => (4, 4, 4, 4),
        "fonction" => (3, 3, 3, 3),
        "soul" => (18, 5, 18, 5),
        _ => (18, 18, 18, 18), // liquid, encre, standard, and any style added later
    };

    /// <summary>A radius of 18 is the usual rounded tile: about a fifth of the side.</summary>
    const double RoundedFraction = 0.22;

    /// <summary>The icon for a style and an accent colour.</summary>
    public static AppIconSpec For(string? style, Argb accent)
    {
        var (tl, tr, br, bl) = Corners(style ?? "");
        double F(double r) => r / 18.0 * RoundedFraction;
        var opaque = accent with { A = 255 };
        return new AppIconSpec(
            TopLeft: Mix(opaque, new Argb(255, 0, 0, 0), 0.28),
            BottomRight: Mix(opaque, new Argb(255, 255, 255, 255), 0.30),
            Bars: AccentPicker.OnColor(opaque),
            F(tl), F(tr), F(br), F(bl));
    }

    /// <summary>Colour <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static Argb Mix(Argb a, Argb b, double t)
    {
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return new Argb(255, L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    /// <summary>A key for what the icon looks like: two themes that give the same icon give the same key, so it is drawn once.</summary>
    public string Signature => $"{TopLeft.ToHex()}|{BottomRight.ToHex()}|{Bars.ToHex()}|{RadiusTopLeft:0.###},{RadiusTopRight:0.###},{RadiusBottomRight:0.###},{RadiusBottomLeft:0.###}";
}
