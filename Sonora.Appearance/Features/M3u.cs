using System.Globalization;
using System.Text;

namespace Sonora.Appearance.Features;

/// <summary>One line of an exported playlist.</summary>
public sealed record M3uEntry(string Path, string Title, string Artist, double Seconds);

/// <summary>
/// Extended M3U, the one playlist format every other player reads. Spotify can neither import nor export
/// a playlist, so this is one place Sonora can do more rather than less — for local files, at least:
/// a Spotify track has no file to point at.
/// </summary>
public static class M3u
{
    public const string Header = "#EXTM3U";

    /// <summary>Writes an extended M3U. Paths are left exactly as given, so absolute paths stay absolute.</summary>
    public static string Write(IEnumerable<M3uEntry> entries)
    {
        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.Path)) continue;
            var seconds = e.Seconds > 0 ? (int)Math.Round(e.Seconds) : -1;
            var label = string.IsNullOrWhiteSpace(e.Artist) ? e.Title : $"{e.Artist} - {e.Title}";
            text.Append("#EXTINF:").Append(seconds.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(Clean(label)).Append('\n');
            text.Append(e.Path).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// Reads the file paths out of an M3U. Comments and directives are skipped, blank lines ignored, and
    /// relative paths resolved against the playlist's own folder — which is how every other player reads them.
    /// </summary>
    public static List<string> Parse(string content, string? folder = null)
    {
        var paths = new List<string>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            paths.Add(Resolve(line, folder));
        }
        return paths;
    }

    static string Resolve(string path, string? folder)
    {
        if (folder == null || folder.Length == 0) return path;
        // A URL or a rooted path is already absolute; anything else hangs off the playlist's folder.
        if (path.Contains("://", StringComparison.Ordinal)) return path;
        try
        {
            return System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, path));
        }
        catch
        {
            return path; // a path the platform rejects is handed back untouched rather than dropped
        }
    }

    /// <summary>A playlist name from a file name: "Mes titres.m3u8" becomes "Mes titres".</summary>
    public static string NameFromFile(string fileName)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(fileName ?? "");
        return name.Length > 0 ? name : "Playlist importée";
    }

    /// <summary>Line breaks in a title would split the entry in two.</summary>
    static string Clean(string text) => (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
}
