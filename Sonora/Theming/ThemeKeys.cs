namespace Sonora.Theming;

/// <summary>Resource keys written by <see cref="ThemeService"/>; views read them with DynamicResource only.</summary>
public static class ThemeKeys
{
    public const string Window = "Brush.Window";
    public const string Background = "Brush.Background";
    public const string Surface = "Brush.Surface";
    public const string SurfaceAlt = "Brush.SurfaceAlt";
    public const string Hover = "Brush.Hover";
    public const string Selected = "Brush.Selected";
    public const string Border = "Brush.Border";
    public const string TextPrimary = "Brush.TextPrimary";
    public const string TextSecondary = "Brush.TextSecondary";
    public const string TextMuted = "Brush.TextMuted";
    public const string Accent = "Brush.Accent";
    public const string OnAccent = "Brush.OnAccent";
    public const string Rail = "Brush.Rail";

    // Derived from the palette
    public const string AccentSoft = "Brush.AccentSoft";
    public const string HeaderGradient = "Brush.HeaderGradient";
    public const string SpecialGradient = "Brush.SpecialGradient";

    // Style-dependent (see ThemeDefinition.Style)
    public const string SurfaceGlass = "Brush.SurfaceGlass";
    public const string GlassEdge = "Brush.GlassEdge";
    public const string RowAlt = "Brush.RowAlt";
    public const string Halo1 = "Brush.Halo1";
    public const string Halo2 = "Brush.Halo2";
    public const string Halo3 = "Brush.Halo3";
    public const string LiquidFill = "Brush.LiquidFill";
    public const string GlassHighlight = "Brush.GlassHighlight";

    public const string FontUi = "Font.UI";
    public const string FontDisplay = "Font.Display";
    public const string FontNumbers = "Font.Numbers";

    public const string TextSmall = "Size.Text.Small";
    public const string TextBody = "Size.Text.Body";
    public const string TextLarge = "Size.Text.Large";
    public const string TextSection = "Size.Text.Section";
    public const string TextTitle = "Size.Text.Title";
    public const string TextHero = "Size.Text.Hero";

    public const string RadiusPanel = "Radius.Panel";
    public const string RadiusControl = "Radius.Control";
    public const string RadiusArt = "Radius.Art";
    public const string Gap = "Value.Gap";
    public const string PanelPadding = "Thickness.Panel";
    public const string Row = "Size.Row";
    /// <summary>Cover of a library row: as large as the row allows, so the ring of what is playing is never cut off.</summary>
    public const string RowCover = "Size.RowCover";
    public const string Card = "Size.Card";
    public const string ArtworkImage = "Image.Artwork";
    public const string ArtworkOpacity = "Value.ArtworkOpacity";
    public const string HaloOpacity = "Value.HaloOpacity";
    public const string ArtworkBlur = "Value.ArtworkBlur";
    public const string CustomBackgroundImage = "Image.CustomBackground";
    public const string CustomBackgroundOpacity = "Value.CustomBackgroundOpacity";

    /// <summary>Base sizes (before the theme's font scale).</summary>
    public static readonly (string Key, double Size)[] TextSizes =
    [
        (TextSmall, 12), (TextBody, 14), (TextLarge, 16), (TextSection, 22), (TextTitle, 32), (TextHero, 48),
    ];
}
