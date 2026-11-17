using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Sonora.Appearance.Theming;
using Sonora.Appearance.Localization;

namespace Sonora.Theming;

/// <summary>
/// Turns a <see cref="ThemeDefinition"/> into the application's dynamic resources (brushes, fonts, sizes, radii)
/// and swaps the structural style dictionary (Styles/&lt;Style&gt;.xaml) that reshapes panels, cards and titles.
/// Only values that change are written, so dragging a slider stays cheap.
/// </summary>
public sealed class ThemeService(ResourceDictionary resources, Dispatcher ui)
{
    static readonly (string Key, string Color)[] BrushSlots =
    [
        (ThemeKeys.Window, "window"), (ThemeKeys.Surface, "surface"), (ThemeKeys.SurfaceAlt, "surfaceAlt"),
        (ThemeKeys.Hover, "hover"), (ThemeKeys.Selected, "selected"), (ThemeKeys.Border, "border"),
        (ThemeKeys.TextPrimary, "textPrimary"), (ThemeKeys.TextSecondary, "textSecondary"), (ThemeKeys.TextMuted, "textMuted"),
        (ThemeKeys.Accent, "accent"), (ThemeKeys.OnAccent, "onAccent"), (ThemeKeys.Rail, "rail"),
    ];

    static readonly Lazy<HashSet<string>> InstalledFonts = new(() =>
        Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase));

    readonly Dictionary<string, Color> _targets = [];
    readonly Dictionary<string, string> _signatures = [];
    Argb? _artworkAccent;
    double _cardPixels = 160;
    int _artworkToken;
    bool _coverBackground;
    double _coverStrength = 0.55;
    double _coverBlur = 30;
    ImageSource? _customImage;
    double _customStrength = 0.85;
    ResourceDictionary? _styleDictionary;
    string? _style;
    IntPtr _hwnd;

    public ThemeDefinition Current { get; private set; } = ThemeCatalog.BuiltInDefault();

    /// <summary>Raised after a theme is applied, with any warnings (e.g. a missing font).</summary>
    public event Action<IReadOnlyList<string>>? Applied;

    /// <summary>Raised when the structural style changes: views built from the old style must be rebuilt.</summary>
    public event Action? StyleChanged;

    /// <summary>Raised when the gap between panels or the panel padding changes (the dock must be rebuilt).</summary>
    public event Action? SpacingChanged;

    public void Apply(ThemeDefinition theme)
    {
        Current = theme;
        var warnings = new List<string>();
        bool animate = theme.Animations && _targets.Count > 0;

        foreach (var (key, slot) in BrushSlots)
        {
            var color = ToColor(slot == "accent" && _artworkAccent is { } a && theme.AccentFromArtwork
                ? AccentFor(a, theme).ToHex()
                : slot == "onAccent" && _artworkAccent is { } a2 && theme.AccentFromArtwork
                    ? AccentPicker.OnColor(AccentFor(a2, theme)).ToHex()
                    : theme.Colors.Get(slot));
            SetBrush(key, color, animate);
        }

        UpdateDerivedBrushes(theme);

        Set(ThemeKeys.FontUi, Font(theme.Fonts.Ui, warnings), theme.Fonts.Ui);
        Set(ThemeKeys.FontDisplay, Font(theme.Fonts.Display, warnings), theme.Fonts.Display);
        Set(ThemeKeys.FontNumbers, Font(theme.Fonts.Numbers, warnings), theme.Fonts.Numbers);
        foreach (var (key, size) in ThemeKeys.TextSizes) Set(key, Math.Round(size * theme.FontScale, 1));

        var r = theme.CornerRadius;
        Set(ThemeKeys.RadiusPanel, new CornerRadius(r));
        Set(ThemeKeys.RadiusControl, new CornerRadius(Math.Round(r * 0.66)));
        Set(ThemeKeys.RadiusArt, new CornerRadius(Math.Round(r * 0.5)));
        bool spacing = Set(ThemeKeys.Gap, theme.PanelGap);
        spacing |= Set(ThemeKeys.PanelPadding, new Thickness(theme.Density switch { "compact" => 4, "confort" => 12, _ => 8 }));
        Set(ThemeKeys.Row, theme.RowHeight);
        Set(ThemeKeys.RowCover, Math.Min(44, theme.RowHeight - 4));
        Set(ThemeKeys.Card, _cardPixels);
        UpdateCoverResources(theme);
        if (!resources.Contains(ThemeKeys.ArtworkImage)) resources[ThemeKeys.ArtworkImage] = null;
        if (!resources.Contains(ThemeKeys.CustomBackgroundImage)) resources[ThemeKeys.CustomBackgroundImage] = _customImage;
        UpdateCustomBackgroundOpacity();

#pragma warning disable WPF0001
        if (Application.Current is { } app && Argb.TryParse(theme.Colors.Window, out var w))
        {
            var mode = w.RelativeLuminance > 0.4 ? ThemeMode.Light : ThemeMode.Dark;
            if (app.ThemeMode != mode) app.ThemeMode = mode;
        }
#pragma warning restore WPF0001

        UpdateFrame(theme);
        bool styleChanged = ApplyStyle(theme.Style);
        ApplyTitleBar();
        UpdateIcon(theme);
        Applied?.Invoke(warnings);
        if (styleChanged) StyleChanged?.Invoke();
        else if (spacing) SpacingChanged?.Invoke();
    }

    /// <summary>The cover of the current track can replace the halos behind Verre liquide.</summary>
    public bool CoverBackgroundAllowed => Current.Style == "liquid";

    /// <summary>Turns the blurred cover of the playing track into the window's background (Verre liquide only).</summary>
    public void SetCoverBackground(bool on, double strength, double blur)
    {
        _coverBackground = on;
        _coverStrength = Math.Clamp(strength, 0, 1);
        _coverBlur = Math.Clamp(blur, 0, 120);
        UpdateCoverResources(Current);
    }

    void UpdateCoverResources(ThemeDefinition theme)
    {
        bool cover = _coverBackground && theme.Style == "liquid";
        Set(ThemeKeys.ArtworkOpacity, cover ? _coverStrength : theme.Background.Mode == "artwork" ? theme.Background.ArtworkStrength : 0.0);
        Set(ThemeKeys.HaloOpacity, cover ? 0.3 : 1.0); // the halos step back so the cover reads
        Set(ThemeKeys.ArtworkBlur, cover ? _coverBlur : 90.0);
    }

    /// <summary>True when the theme's background is one plain colour, the only case a picture can replace.</summary>
    public bool CustomBackgroundAllowed => Current.Background.Mode == "solid";

    void UpdateCustomBackgroundOpacity() =>
        Set(ThemeKeys.CustomBackgroundOpacity, CustomBackgroundAllowed && _customImage != null ? _customStrength : 0.0);

    /// <summary>
    /// Puts a picture of the user's choosing behind the panels (null or empty clears it). Returns false when the
    /// file cannot be read as an image; the previous picture is kept then.
    /// </summary>
    public bool SetCustomBackground(string? path, double strength)
    {
        _customStrength = Math.Clamp(strength, 0, 1);
        if (string.IsNullOrWhiteSpace(path))
        {
            _customImage = null;
        }
        else
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // the file is not kept open
                bmp.DecodePixelWidth = 2560;                                            // a wallpaper needs no more
                bmp.EndInit();
                bmp.Freeze();
                _customImage = bmp;
            }
            catch
            {
                return false;
            }
        }
        resources[ThemeKeys.CustomBackgroundImage] = _customImage;
        UpdateCustomBackgroundOpacity();
        return true;
    }

    /// <summary>Side of an album, artist or playlist card, in device-independent pixels.</summary>
    public double CardPixels => _cardPixels;

    public void SetCardSize(double px)
    {
        _cardPixels = px;
        Set(ThemeKeys.Card, px);
    }

    // ---- the app icon: the logo in the theme's accent, on a tile shaped like the theme's style ----

    bool _themedIcon = true;
    string? _iconSignature;
    ImageSource? _windowIcon;

    /// <summary>
    /// Whether the icon of the window, the title bar and the login page follows the theme (true) or stays the
    /// original purple (false). The icon of the .exe, the Start menu and the pinned taskbar button is a file Windows
    /// reads once: it never follows.
    /// </summary>
    public bool ThemedIcon
    {
        get => _themedIcon;
        set
        {
            if (_themedIcon == value) return;
            _themedIcon = value;
            UpdateIcon(Current);
        }
    }

    /// <summary>Gives a window the current icon (a window opened after the last theme change would otherwise have the old one).</summary>
    public void ApplyIconTo(Window window)
    {
        if (_windowIcon != null) window.Icon = _windowIcon;
    }

    void UpdateIcon(ThemeDefinition theme)
    {
        try
        {
            string signature;
            ImageSource title, window;
            if (_themedIcon && Argb.TryParse(theme.Colors.Accent, out var accent))
            {
                // The accent of the theme itself, not the one taken from the current cover: the icon must not change every track.
                var spec = AppIconSpec.For(theme.Style, accent);
                signature = spec.Signature;
                if (signature == _iconSignature) return;
                title = AppIconRenderer.Render(spec, 64);
                window = AppIconRenderer.WindowIcon(spec);
            }
            else
            {
                signature = "original";
                if (signature == _iconSignature) return;
                title = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Sonora.png"));
                window = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Sonora.ico"));
            }
            _iconSignature = signature;
            _windowIcon = window;
            resources[ThemeKeys.AppIcon] = title;
            if (Application.Current is { } app)
                foreach (Window w in app.Windows) w.Icon = window;
        }
        catch
        {
            // An icon is not worth a crash: the window keeps the one it had.
        }
    }

    /// <summary>Window whose title bar follows the theme colours.</summary>
    public void AttachWindow(IntPtr hwnd)
    {
        _hwnd = hwnd;
        ApplyTitleBar();
    }

    /// <summary>Follows the playing track: accent from the cover and the artwork background. Only the latest request wins.</summary>
    public async Task OnTrackArtworkAsync(string? artPath)
    {
        int token = Interlocked.Increment(ref _artworkToken);
        Argb? accent = null;
        ImageSource? image = null;
        if (!string.IsNullOrEmpty(artPath)) (accent, image) = await ArtworkAccentSource.LoadAsync(artPath);
        await ui.InvokeAsync(() =>
        {
            if (token != _artworkToken) return; // a newer track asked in the meantime
            _artworkAccent = accent;
            resources[ThemeKeys.ArtworkImage] = image;
            if (Current.AccentFromArtwork)
            {
                var a = accent is { } c ? AccentFor(c, Current) : Parse(Current.Colors.Accent);
                SetBrush(ThemeKeys.Accent, ToColor(a.ToHex()), Current.Animations);
                SetBrush(ThemeKeys.OnAccent, ToColor((accent is { } c2 ? AccentPicker.OnColor(AccentFor(c2, Current)) : Parse(Current.Colors.OnAccent)).ToHex()), Current.Animations);
                UpdateDerivedBrushes(Current);
            }
        });
    }

    // ---------------- frame ----------------

    /// <summary>
    /// Merged into every docked panel: when the theme has frame colours, the panel's own resources override the
    /// main brushes (ground, text, hover, accent) so the frame around the page gets its own colours.
    /// </summary>
    public ResourceDictionary FrameResources { get; } = new();

    void UpdateFrame(ThemeDefinition theme)
    {
        var signature = theme.Frame is { } f ? string.Join("|", FrameColors.Keys.Select(f.Get)) : "";
        if (_signatures.TryGetValue("frame", out var old) && old == signature) return;
        _signatures["frame"] = signature;

        if (theme.Frame is not { } frame)
        {
            FrameResources.Clear();
            return;
        }
        var text = ToColor(frame.Text);
        Brush Solid(Color c) => Frozen(new SolidColorBrush(c));
        Brush TextAt(byte alpha) => Solid(Color.FromArgb(alpha, text.R, text.G, text.B));
        var accent = ToColor(frame.Accent);
        var secondary = ToColor(frame.TextSecondary);
        FrameResources[ThemeKeys.Surface] = Solid(ToColor(frame.Background));
        FrameResources[ThemeKeys.Window] = Solid(ToColor(frame.Background)); // e.g. the play glyph drawn on a text-coloured disc
        FrameResources[ThemeKeys.SurfaceAlt] = TextAt(0x14);
        FrameResources[ThemeKeys.Hover] = TextAt(0x1A);
        FrameResources[ThemeKeys.Selected] = TextAt(0x29);
        FrameResources[ThemeKeys.Border] = TextAt(0x1F);
        FrameResources[ThemeKeys.Rail] = TextAt(0x40);
        FrameResources[ThemeKeys.TextPrimary] = Solid(text);
        FrameResources[ThemeKeys.TextSecondary] = Solid(secondary);
        FrameResources[ThemeKeys.TextMuted] = Solid(Color.FromArgb(0xC0, secondary.R, secondary.G, secondary.B));
        FrameResources[ThemeKeys.Accent] = Solid(accent);
        FrameResources[ThemeKeys.OnAccent] = Solid(ToColor(frame.OnAccent));
        FrameResources[ThemeKeys.AccentSoft] = Solid(Color.FromArgb(0x40, accent.R, accent.G, accent.B));
        FrameResources[ThemeKeys.RowAlt] = Solid(Colors.Transparent);
    }

    // ---------------- style ----------------

    bool ApplyStyle(string style)
    {
        if (!ThemeDefinition.Styles.Contains(style)) style = "standard";
        if (style == _style) return false;
        var name = style switch
        {
            "terminal" => "Terminal", "liquid" => "Liquid", "encre" => "Encre",
            "affiche" => "Affiche", "soul" => "Soul", "fonction" => "Fonction", _ => "Standard",
        };
        var dictionary = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Styles/{name}.xaml") };
        if (_styleDictionary != null) resources.MergedDictionaries.Remove(_styleDictionary);
        resources.MergedDictionaries.Add(dictionary); // last merged dictionary wins
        _styleDictionary = dictionary;
        bool first = _style == null;
        _style = style;
        return !first;
    }

    // ---------------- title bar ----------------

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// The title bar itself is drawn by Sonora (see MainWindow.xaml's WindowChrome), so only the dark-mode flag
    /// (affects the drop shadow and resize frame) and the thin window border still go through DWM.
    /// </summary>
    void ApplyTitleBar()
    {
        if (_hwnd == IntPtr.Zero) return;
        var window = Parse(Current.Colors.Window);
        var accent = Parse(Current.Colors.Accent);
        int dark = window.RelativeLuminance > 0.4 ? 0 : 1;
        int border = accent.R | (accent.G << 8) | (accent.B << 16); // COLORREF 0x00BBGGRR
        DwmSetWindowAttribute(_hwnd, 20, ref dark, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(_hwnd, 34, ref border, sizeof(int)); // DWMWA_BORDER_COLOR (Windows 11)
    }

    // ---------------- helpers ----------------

    static Argb AccentFor(Argb accent, ThemeDefinition theme) => AccentPicker.EnsureContrast(accent, Parse(theme.Colors.Surface));

    static Argb Parse(string hex) => Argb.TryParse(hex, out var c) ? c : new Argb(255, 128, 128, 128);

    static Color ToColor(string hex)
    {
        var c = Parse(hex);
        return Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    /// <summary>Writes a resource only when its value changes. Returns true when it was written.</summary>
    bool Set(string key, object value, string? signature = null)
    {
        var sig = signature ?? value.ToString() ?? "";
        if (_signatures.TryGetValue(key, out var old) && old == sig && resources.Contains(key)) return false;
        _signatures[key] = sig;
        resources[key] = value;
        return true;
    }

    /// <summary>
    /// Publishes a fresh brush for the key. Application-level resources are frozen by WPF on first lookup, so a
    /// brush is never mutated after publication; an animated brush cannot be frozen, which keeps the transition alive.
    /// </summary>
    void SetBrush(string key, Color color, bool animate)
    {
        var from = _targets.TryGetValue(key, out var previous) ? previous : (Color?)null;
        _targets[key] = color;
        if (from == color && resources.Contains(key)) return;

        var brush = new SolidColorBrush(color);
        if (animate && from is { } start)
        {
            brush.Color = start;
            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(start, color, TimeSpan.FromMilliseconds(250)) { EasingFunction = new QuadraticEase() });
        }
        resources[key] = brush;
    }

    void UpdateDerivedBrushes(ThemeDefinition theme)
    {
        // Target accent (not the animated in-between value).
        var accent = theme.AccentFromArtwork && _artworkAccent is { } a
            ? ToColor(AccentFor(a, theme).ToHex())
            : ToColor(theme.Colors.Accent);
        var surface = ToColor(theme.Colors.Surface);
        var surfaceAlt = ToColor(theme.Colors.SurfaceAlt);
        var window = Parse(theme.Colors.Window);
        bool light = window.RelativeLuminance > 0.4;

        var signature = string.Join("|", accent, surface, surfaceAlt, theme.Style, theme.Background.Mode,
            string.Join(",", theme.Background.Colors), theme.Colors.Window);
        if (_signatures.TryGetValue("derived", out var old) && old == signature) return;
        _signatures["derived"] = signature;

        resources[ThemeKeys.AccentSoft] = Frozen(new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)));
        resources[ThemeKeys.HeaderGradient] = Frozen(new LinearGradientBrush(
            Color.FromArgb(0x99, accent.R, accent.G, accent.B), Color.FromArgb(0x00, surface.R, surface.G, surface.B), 90));
        var (h, s, l) = new Argb(255, accent.R, accent.G, accent.B).ToHsl();
        var second = Argb.FromHsl(h + 60, s, Math.Min(0.75, l + 0.1));
        resources[ThemeKeys.SpecialGradient] = Frozen(new LinearGradientBrush(accent, Color.FromRgb(second.R, second.G, second.B), 45));

        // Style-dependent surfaces
        byte glassAlpha = theme.Style switch { "liquid" => 0x5C, _ => surface.A };
        resources[ThemeKeys.SurfaceGlass] = Frozen(new SolidColorBrush(Color.FromArgb(Math.Min(glassAlpha, surface.A), surface.R, surface.G, surface.B)));
        resources[ThemeKeys.GlassEdge] = Frozen(new SolidColorBrush(light ? Color.FromArgb(0x1F, 0, 0, 0) : Color.FromArgb(0x2E, 255, 255, 255)));
        resources[ThemeKeys.RowAlt] = Frozen(new SolidColorBrush(theme.Style == "terminal"
            ? Color.FromArgb(0xFF, surfaceAlt.R, surfaceAlt.G, surfaceAlt.B)
            : Colors.Transparent));
        bool liquid = theme.Style == "liquid";
        double haloSat = liquid ? Math.Max(s, 0.8) : Math.Max(s, 0.5);
        var halo2 = Argb.FromHsl(h + 70, haloSat, 0.55);
        var halo3 = Argb.FromHsl(h - 70, haloSat, 0.5);
        resources[ThemeKeys.Halo1] = Frozen(new SolidColorBrush(Color.FromArgb((byte)(liquid ? 0xD0 : 0xA0), accent.R, accent.G, accent.B)));
        resources[ThemeKeys.Halo2] = Frozen(new SolidColorBrush(Color.FromArgb((byte)(liquid ? 0xC0 : 0x90), halo2.R, halo2.G, halo2.B)));
        resources[ThemeKeys.Halo3] = Frozen(new SolidColorBrush(Color.FromArgb((byte)(liquid ? 0xB0 : 0x80), halo3.R, halo3.G, halo3.B)));

        // Liquid glass: domed fill (lighter at the top) and a bright rim that fades down the edges.
        static Color Mix(Color c, Color with, double t, byte alpha) => Color.FromArgb(alpha,
            (byte)(c.R + (with.R - c.R) * t), (byte)(c.G + (with.G - c.G) * t), (byte)(c.B + (with.B - c.B) * t));
        var white = Colors.White;
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop(Mix(surface, white, light ? 0.6 : 0.22, (byte)(light ? 0x99 : 0x80)), 0));
        fill.GradientStops.Add(new GradientStop(Mix(surface, white, light ? 0.3 : 0.06, (byte)(light ? 0x66 : 0x52)), 0.45));
        fill.GradientStops.Add(new GradientStop(Mix(surface, white, 0, (byte)(light ? 0x59 : 0x47)), 1));
        resources[ThemeKeys.LiquidFill] = Frozen(fill);
        var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0xD9, 255, 255, 255), 0));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, 255, 255, 255), 0.35));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 255, 255, 255), 0.7));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x4D, 255, 255, 255), 1));
        resources[ThemeKeys.GlassHighlight] = Frozen(rim);

        var bg = theme.Background.Colors.Select(ToColor).ToList();
        if (bg.Count == 0) bg.Add(ToColor(theme.Colors.Window));
        resources[ThemeKeys.Background] = theme.Background.Mode == "solid" || bg.Count == 1
            ? Frozen(new SolidColorBrush(bg[0]))
            : Frozen(new LinearGradientBrush(bg[0], bg[^1], 90));
    }

    static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    /// <summary>Fonts shipped inside Sonora (Fonts/*.ttf), usable even when not installed on Windows.</summary>
    static readonly HashSet<string> EmbeddedFonts = new(StringComparer.OrdinalIgnoreCase) { "Barlow Condensed", "Figtree", "Syne", "Fraunces" };

    /// <summary>
    /// Segoe UI covers the text a theme font lacks, and Segoe UI Emoji the emoji: without naming it, a font such as
    /// Cascadia Mono draws an empty box for a playlist called "Road trip 🚗" instead of falling back on its own.
    /// </summary>
    const string Fallbacks = "Segoe UI, Segoe UI Emoji";

    static FontFamily Font(string name, List<string> warnings)
    {
        if (EmbeddedFonts.Contains(name))
            return new FontFamily(new Uri("pack://application:,,,/"), $"./Fonts/#{name}, {Fallbacks}");
        if (!InstalledFonts.Value.Contains(name))
        {
            warnings.Add(Loc.T("theme.font_missing", name));
            return new FontFamily("Segoe UI Variable Text, " + Fallbacks);
        }
        return new FontFamily($"{name}, {Fallbacks}");
    }
}
