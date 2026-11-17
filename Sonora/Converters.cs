using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sonora.Appearance.Localization;

namespace Sonora;

/// <summary>
/// Whether a library entry, album, artist or playlist is what the current track is played from:
/// 0 no, 1 yes but paused, 2 yes and playing. A Spotify context is matched on its URI; a plain list of tracks
/// (local files, liked songs) only has a name to go by.
/// </summary>
public static class PlayingContext
{
    public static int State(string key, string title, string? contextUri, string contextName, bool isPlaying, bool hasTrack)
    {
        if (!hasTrack) return 0;
        bool match = contextUri != null
            ? key == contextUri
            : contextName.Length > 0 && string.Equals(title, contextName, StringComparison.CurrentCultureIgnoreCase);
        return !match ? 0 : isPlaying ? 2 : 1;
    }

    /// <summary>
    /// Local albums are matched by name, and many share one ("Music"): the playing track has to belong to the
    /// album or artist as well, or every namesake would light up.
    /// </summary>
    public static bool Owns(object? source, Sonora.Models.Track? current) => source switch
    {
        Sonora.Models.Album { Uri: null } a => current != null && a.Tracks.Any(t => t.Path == current.Path),
        Sonora.Models.Artist { Uri: null } a => current != null && a.Tracks.Any(t => t.Path == current.Path),
        Sonora.Models.Playlist p => current != null && p.Paths.Contains(current.Path),
        _ => true,
    };

    public static int StateOf(object? source, string? contextUri, string contextName, bool isPlaying, bool hasTrack, Sonora.Models.Track? current)
    {
        int state = source switch
        {
            Sonora.Models.SpotifyPlaylist p => State(p.Uri, p.Name, contextUri, contextName, isPlaying, hasTrack),
            Sonora.Models.Playlist p => State("playlist:" + p.Id, p.Name, contextUri, contextName, isPlaying, hasTrack),
            Sonora.Models.Album a => State(a.Uri ?? "album:" + a.Key, a.Title, contextUri, contextName, isPlaying, hasTrack),
            Sonora.Models.Artist a => State(a.Uri ?? "artist:" + a.Name, a.Name, contextUri, contextName, isPlaying, hasTrack),
            _ => 0,
        };
        return state > 0 && !Owns(source, current) ? 0 : state;
    }
}

/// <summary>Cards bind the model, the player's context URI and name, and its playing flag; the result is a <see cref="PlayingContext"/> state.</summary>
public sealed class PlayingContextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        PlayingContext.StateOf(
            values.ElementAtOrDefault(0),
            values.ElementAtOrDefault(1) as string,
            values.ElementAtOrDefault(2) as string ?? "",
            values.ElementAtOrDefault(3) is true,
            values.ElementAtOrDefault(4) is true,
            values.ElementAtOrDefault(5) as Sonora.Models.Track);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Path → decoded, frozen, down-sampled bitmap (cached, weakly referenced). The parameter is the width to decode
/// to, in pixels, or "card" for the size of an album/artist/playlist card on this screen.
/// </summary>
public sealed class ArtConverter : IValueConverter
{
    static readonly Dictionary<(string, int), WeakReference<BitmapImage>> Cache = [];

    /// <summary>
    /// What a card needs, no more: its side in device pixels, rounded up to a multiple of 32. A decoded cover costs
    /// width x width x 4 bytes, twice over once drawn, and a home page holds dozens: decoding them at a fixed 400 px
    /// (640 KB each, for a 160 px card) was worth over 100 MB.
    /// </summary>
    static int CardWidth()
    {
        double scale = Application.Current?.MainWindow is { } window ? VisualTreeHelper.GetDpi(window).DpiScaleX : 1.0;
        double side = (App.Themes?.CardPixels ?? 160) * Math.Max(1.0, scale);
        return Math.Clamp((int)Math.Ceiling(side / 32) * 32, 64, 640);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0) return null;
        int width = parameter is "card" ? CardWidth() : parameter is string s && int.TryParse(s, out var w) ? w : 300;

        // A Spotify cover is served from its CDN the first time and from disk ever after, so the library
        // still has its covers on the next start — and without a network.
        if (Services.RemoteArtCache.IsRemote(path))
        {
            if (Services.RemoteArtCache.Cached(path) is { } file) path = file;
            else Services.RemoteArtCache.Prefetch(path);
        }

        var key = (path, width);
        if (Cache.TryGetValue(key, out var weak) && weak.TryGetTarget(out var hit)) return hit;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = width;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            // Remote covers (Spotify CDN) download asynchronously and cannot be frozen until done.
            if (bmp.CanFreeze) bmp.Freeze();
            Cache[key] = new WeakReference<BitmapImage>(bmp);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Playing or not → the taskbar thumbnail's progress state (paused shows a dimmed bar).</summary>
public sealed class TaskbarStateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? System.Windows.Shell.TaskbarItemProgressState.Normal : System.Windows.Shell.TaskbarItemProgressState.Paused;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A 0..1 share → a pixel height, for the listening-hours bars (parameter: the full height).</summary>
public sealed class BarHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var share = value is double d && !double.IsNaN(d) ? Math.Clamp(d, 0, 1) : 0;
        var full = parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : 96;
        // An hour with no plays keeps a hairline, so the axis still reads as twenty-four hours.
        return share <= 0 ? 1.0 : Math.Max(2.0, share * full);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Two-way equality for RadioButton groups bound to a string.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter ?? Binding.DoNothing : Binding.DoNothing;
}

/// <summary>"#RRGGBB" / "#AARRGGBB" → brush (theme previews and colour swatches).</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || !Sonora.Appearance.Theming.Argb.TryParse(s, out var c)) return null;
        var b = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Upper-cases a label (Encre headings and buttons are typed in sentence case and shown in capitals).</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpper(culture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A theme's corner radius scaled down for its small preview (double → CornerRadius).</summary>
public sealed class RadiusPreviewConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new CornerRadius(value is double d ? Math.Min(d / 2, 12) : 4);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Theme option values (density, background mode, card size) shown in the language in use.</summary>
public sealed class OptionLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "compact" => Loc.T("option.compact"),
        "normal" => Loc.T("option.normal"),
        "confort" => Loc.T("option.confort"),
        "solid" => Loc.T("option.solid"),
        "gradient" => Loc.T("option.gradient"),
        "artwork" => Loc.T("option.artwork"),
        "standard" => Loc.T("option.standard"),
        "terminal" => Loc.T("option.terminal"),
        "small" => Loc.T("option.small"),
        "medium" => Loc.T("option.medium"),
        "large" => Loc.T("option.large"),
        _ => value?.ToString() ?? "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The lyrics panel's "left"/"center" feature setting → <see cref="TextAlignment"/>.</summary>
public sealed class LyricsAlignConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as string == "center" ? TextAlignment.Center : TextAlignment.Left;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
