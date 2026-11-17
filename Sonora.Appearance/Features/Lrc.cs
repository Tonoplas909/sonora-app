using System.Globalization;
using System.Text.RegularExpressions;

namespace Sonora.Appearance.Features;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Reads time-stamped lyrics in the LRC format ("[mm:ss.xx] text").</summary>
public static partial class Lrc
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]")]
    private static partial Regex Stamp();

    /// <summary>Lines sorted by time. A line with several stamps is repeated; lines without a stamp are ignored.</summary>
    public static List<LyricLine> Parse(string? lrc)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrc)) return result;
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var matches = Stamp().Matches(line);
            if (matches.Count == 0) continue;
            var last = matches[^1];
            var text = line[(last.Index + last.Length)..].Trim();
            foreach (Match m in matches)
            {
                var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                result.Add(new LyricLine(TimeSpan.FromSeconds(minutes * 60 + seconds), text));
            }
        }
        return result.OrderBy(l => l.Time).ToList();
    }

    /// <summary>Index of the line being sung at <paramref name="position"/>, or -1 before the first one.</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Time <= position) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }
}
