using System.Text.Json;
using System.Text.Json.Nodes;
using Sonora.Appearance.Layout;
using Sonora.Appearance.Localization;

namespace Sonora.Appearance.Theming;

public sealed class ThemeColors
{
    public string Window { get; set; } = "";
    public string Surface { get; set; } = "";
    public string SurfaceAlt { get; set; } = "";
    public string Hover { get; set; } = "";
    public string Selected { get; set; } = "";
    public string Border { get; set; } = "";
    public string TextPrimary { get; set; } = "";
    public string TextSecondary { get; set; } = "";
    public string TextMuted { get; set; } = "";
    public string Accent { get; set; } = "";
    public string OnAccent { get; set; } = "";
    public string Rail { get; set; } = "";

    /// <summary>JSON names in file order.</summary>
    public static readonly string[] Keys =
        ["window", "surface", "surfaceAlt", "hover", "selected", "border", "textPrimary", "textSecondary", "textMuted", "accent", "onAccent", "rail"];

    public string Get(string key) => key switch
    {
        "window" => Window, "surface" => Surface, "surfaceAlt" => SurfaceAlt, "hover" => Hover,
        "selected" => Selected, "border" => Border, "textPrimary" => TextPrimary, "textSecondary" => TextSecondary,
        "textMuted" => TextMuted, "accent" => Accent, "onAccent" => OnAccent, "rail" => Rail,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    public void Set(string key, string value)
    {
        switch (key)
        {
            case "window": Window = value; break;
            case "surface": Surface = value; break;
            case "surfaceAlt": SurfaceAlt = value; break;
            case "hover": Hover = value; break;
            case "selected": Selected = value; break;
            case "border": Border = value; break;
            case "textPrimary": TextPrimary = value; break;
            case "textSecondary": TextSecondary = value; break;
            case "textMuted": TextMuted = value; break;
            case "accent": Accent = value; break;
            case "onAccent": OnAccent = value; break;
            case "rail": Rail = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(key));
        }
    }
}

/// <summary>
/// Optional "frame" colours: when set, the docked panels (navigation, library, queue, player) use their own
/// darker ground and text, around the page that keeps the main colours (e.g. the ink frame of the Encre theme).
/// </summary>
public sealed class FrameColors
{
    public string Background { get; set; } = "";
    public string Text { get; set; } = "";
    public string TextSecondary { get; set; } = "";
    public string Accent { get; set; } = "";
    public string OnAccent { get; set; } = "";

    public static readonly string[] Keys = ["background", "text", "textSecondary", "accent", "onAccent"];

    public string Get(string key) => key switch
    {
        "background" => Background, "text" => Text, "textSecondary" => TextSecondary, "accent" => Accent, "onAccent" => OnAccent,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    public void Set(string key, string value)
    {
        switch (key)
        {
            case "background": Background = value; break;
            case "text": Text = value; break;
            case "textSecondary": TextSecondary = value; break;
            case "accent": Accent = value; break;
            case "onAccent": OnAccent = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(key));
        }
    }

    public FrameColors Clone() => new() { Background = Background, Text = Text, TextSecondary = TextSecondary, Accent = Accent, OnAccent = OnAccent };
}

public sealed class ThemeFonts
{
    public string Ui { get; set; } = "";
    public string Display { get; set; } = "";
    public string Numbers { get; set; } = "";
}

public sealed class ThemeBackground
{
    public string Mode { get; set; } = "solid";
    public List<string> Colors { get; set; } = [];
    public double ArtworkStrength { get; set; }
}

/// <summary>A Sonora theme: colours, fonts, shapes, density and background. Pure model, serialised as JSON.</summary>
public sealed partial class ThemeDefinition
{
    public static readonly string[] Densities = ["compact", "normal", "confort"];
    public static readonly string[] BackgroundModes = ["solid", "gradient", "artwork"];
    public static readonly string[] Styles = ["standard", "terminal", "liquid", "encre", "affiche", "soul", "fonction"];

    /// <summary>Optional colours of the panels around the page (null: panels use the main colours).</summary>
    public FrameColors? Frame { get; set; }

    /// <summary>Structural style: which shapes and templates the interface uses (not only colours).</summary>
    public string Style { get; set; } = "standard";

    /// <summary>Theme this one was duplicated from, used by "Réinitialiser".</summary>
    public string? BasedOn { get; set; }

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>The name to show: a built-in palette is called what the language in use calls it, a user's theme keeps its own name.</summary>
    public string DisplayName => BuiltIn && Loc.Current.Has("palette." + Id) ? Loc.T("palette." + Id) : Name;
    public bool BuiltIn { get; set; }
    public ThemeColors Colors { get; set; } = new();
    public ThemeFonts Fonts { get; set; } = new();
    public double FontScale { get; set; } = 1.0;
    public double CornerRadius { get; set; } = 12;
    public string Density { get; set; } = "normal";
    public double PanelGap { get; set; } = 8;
    public ThemeBackground Background { get; set; } = new();
    public bool AccentFromArtwork { get; set; }
    public bool Animations { get; set; } = true;

    /// <summary>Panel arrangement proposed by the theme, applied on "Réinitialiser la disposition".</summary>
    public DockLayout? DefaultLayout { get; set; }

    public double RowHeight => Density switch { "compact" => 40, "confort" => 64, _ => 56 };

    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public ThemeDefinition Clone() => Parse(ToJson(), this);

    public string ToJson()
    {
        var colors = new JsonObject();
        foreach (var k in ThemeColors.Keys) colors[k] = Colors.Get(k);
        var root = new JsonObject
        {
            ["id"] = Id,
            ["name"] = Name,
            ["builtIn"] = BuiltIn,
            ["style"] = Style,
            ["colors"] = colors,
            ["fonts"] = new JsonObject { ["ui"] = Fonts.Ui, ["display"] = Fonts.Display, ["numbers"] = Fonts.Numbers },
            ["fontScale"] = FontScale,
            ["cornerRadius"] = CornerRadius,
            ["density"] = Density,
            ["panelGap"] = PanelGap,
            ["background"] = new JsonObject
            {
                ["mode"] = Background.Mode,
                ["colors"] = new JsonArray(Background.Colors.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
                ["artworkStrength"] = Background.ArtworkStrength,
            },
            ["accentFromArtwork"] = AccentFromArtwork,
            ["animations"] = Animations,
        };
        if (BasedOn != null) root["basedOn"] = BasedOn;
        if (Frame != null)
        {
            var frame = new JsonObject();
            foreach (var k in FrameColors.Keys) frame[k] = Frame.Get(k);
            root["frame"] = frame;
        }
        if (DefaultLayout != null) root["defaultLayout"] = JsonNode.Parse(DefaultLayout.ToJson());
        return root.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Tolerant read: any missing, mistyped or invalid value keeps the value from <paramref name="fallback"/>;
    /// numbers are clamped to the ranges allowed by the spec.
    /// </summary>
    public static ThemeDefinition Parse(string json, ThemeDefinition fallback)
    {
        var t = Copy(fallback);
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return t;
        }
        if (root.ValueKind != JsonValueKind.Object) return t;

        t.Id = Str(root, "id") ?? t.Id;
        t.Name = Str(root, "name") ?? t.Name;
        if (Bool(root, "builtIn") is { } bi) t.BuiltIn = bi;
        if (Str(root, "style") is { } style)
        {
            if (style is "glass" or "immersive") style = "liquid"; // the Verre and Immersif styles were folded into Verre liquide
            t.Style = Styles.Contains(style) ? style : "standard";
        }
        if (Str(root, "basedOn") is { } basedOn) t.BasedOn = basedOn;
        if (root.TryGetProperty("frame", out var fr) && fr.ValueKind == JsonValueKind.Object)
        {
            var frame = t.Frame?.Clone() ?? new FrameColors();
            foreach (var k in FrameColors.Keys)
                if (Str(fr, k) is { } hex && Argb.TryParse(hex, out _)) frame.Set(k, hex);
            // A frame is only kept when every colour is usable.
            t.Frame = FrameColors.Keys.All(k => Argb.TryParse(frame.Get(k), out _)) ? frame : null;
        }

        if (root.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Object)
            foreach (var k in ThemeColors.Keys)
                if (Str(colors, k) is { } hex && Argb.TryParse(hex, out _))
                    t.Colors.Set(k, hex);

        if (root.TryGetProperty("fonts", out var fonts) && fonts.ValueKind == JsonValueKind.Object)
        {
            t.Fonts.Ui = NonEmpty(Str(fonts, "ui")) ?? t.Fonts.Ui;
            t.Fonts.Display = NonEmpty(Str(fonts, "display")) ?? t.Fonts.Display;
            t.Fonts.Numbers = NonEmpty(Str(fonts, "numbers")) ?? t.Fonts.Numbers;
        }

        if (Num(root, "fontScale") is { } fs) t.FontScale = Math.Clamp(fs, 0.85, 1.3);
        if (Num(root, "cornerRadius") is { } cr) t.CornerRadius = Math.Clamp(cr, 0, 24);
        if (Str(root, "density") is { } d) t.Density = Densities.Contains(d) ? d : "normal";
        if (Num(root, "panelGap") is { } gap) t.PanelGap = Math.Clamp(gap, 0, 32);

        if (root.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.Object)
        {
            if (Str(bg, "mode") is { } mode && BackgroundModes.Contains(mode)) t.Background.Mode = mode;
            if (bg.TryGetProperty("colors", out var bc) && bc.ValueKind == JsonValueKind.Array)
            {
                var list = bc.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String && Argb.TryParse(x.GetString(), out _))
                    .Select(x => x.GetString()!).ToList();
                if (list.Count > 0) t.Background.Colors = list;
            }
            if (Num(bg, "artworkStrength") is { } st) t.Background.ArtworkStrength = Math.Clamp(st, 0, 1);
        }

        if (Bool(root, "accentFromArtwork") is { } afa) t.AccentFromArtwork = afa;
        if (Bool(root, "animations") is { } an) t.Animations = an;
        if (root.TryGetProperty("defaultLayout", out var dl) && dl.ValueKind == JsonValueKind.Object
            && DockLayout.Parse(dl.GetRawText()) is { } layout)
            t.DefaultLayout = layout;
        return t;
    }

    /// <summary>Errors that make a theme unusable as written (in French, for the user).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id)) errors.Add(Loc.T("theme.error_empty_id"));
        if (string.IsNullOrWhiteSpace(Name)) errors.Add(Loc.T("theme.error_empty_name"));
        foreach (var k in ThemeColors.Keys)
            if (!Argb.TryParse(Colors.Get(k), out _))
                errors.Add(Loc.T("theme.error_bad_color", k, Colors.Get(k)));
        foreach (var c in Background.Colors)
            if (!Argb.TryParse(c, out _))
                errors.Add(Loc.T("theme.error_bad_background_color", c));
        if (Frame != null)
            foreach (var k in FrameColors.Keys)
                if (!Argb.TryParse(Frame.Get(k), out _))
                    errors.Add(Loc.T("theme.error_bad_frame_color", k, Frame.Get(k)));
        return errors;
    }

    static ThemeDefinition Copy(ThemeDefinition f)
    {
        var t = new ThemeDefinition
        {
            Id = f.Id, Name = f.Name, BuiltIn = f.BuiltIn,
            Fonts = new ThemeFonts { Ui = f.Fonts.Ui, Display = f.Fonts.Display, Numbers = f.Fonts.Numbers },
            FontScale = f.FontScale, CornerRadius = f.CornerRadius, Density = f.Density, PanelGap = f.PanelGap,
            Background = new ThemeBackground { Mode = f.Background.Mode, Colors = [.. f.Background.Colors], ArtworkStrength = f.Background.ArtworkStrength },
            AccentFromArtwork = f.AccentFromArtwork, Animations = f.Animations,
            Style = f.Style, BasedOn = f.BasedOn, Frame = f.Frame?.Clone(),
        };
        foreach (var k in ThemeColors.Keys) t.Colors.Set(k, f.Colors.Get(k));
        t.DefaultLayout = f.DefaultLayout?.Clone();
        return t;
    }

    static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    internal static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static double? Num(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static bool? Bool(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
}
