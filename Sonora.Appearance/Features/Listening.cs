namespace Sonora.Appearance.Features;

/// <summary>One finished listen, as stored in the local listening log (one JSON object per line).</summary>
public sealed class ListenEntry
{
    /// <summary>When the track finished playing, in UTC.</summary>
    public DateTime AtUtc { get; set; }

    /// <summary>Spotify URI for Spotify tracks, file path for local ones.</summary>
    public string Key { get; set; } = "";

    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";

    /// <summary>How far the track actually got, in seconds.</summary>
    public double Seconds { get; set; }
}

public sealed record ListenCount(string Key, string Title, string Artist, int Plays, double Seconds);
public sealed record ListenTotals(int Plays, double Seconds, int Tracks, int Artists);

/// <summary>
/// The local listening log: what Sonora knows about your own listening, with no API limits and no
/// yearly window. Pure logic — reading and writing the file is the app's job.
/// </summary>
public static class Listening
{
    /// <summary>Periods offered by the statistics page, as a number of days (0 = everything).</summary>
    public static readonly int[] Periods = [7, 30, 365, 0];

    /// <summary>
    /// Whether a play counts, following the usual scrobbling rule: tracks shorter than 30 s never count,
    /// and a longer one counts once it has played half its length (or four minutes, whichever comes first).
    /// </summary>
    public static bool Counts(double playedSeconds, double durationSeconds) =>
        durationSeconds >= 30 && playedSeconds >= Math.Min(durationSeconds / 2, 240);

    /// <summary>The entries of the last <paramref name="days"/> days; 0 or less means every entry.</summary>
    public static List<ListenEntry> Since(IEnumerable<ListenEntry> entries, int days, DateTime nowUtc)
    {
        var all = entries.Where(e => e != null);
        if (days > 0)
        {
            var from = nowUtc.AddDays(-days);
            all = all.Where(e => e.AtUtc >= from);
        }
        return all.ToList();
    }

    /// <summary>Most played tracks, by play count then by time listened.</summary>
    public static List<ListenCount> TopTracks(IEnumerable<ListenEntry> entries, int limit = 50) =>
        entries.Where(e => e.Key.Length > 0)
            .GroupBy(e => e.Key)
            .Select(g => new ListenCount(g.Key, g.First().Title, g.First().Artist, g.Count(), g.Sum(e => e.Seconds)))
            .OrderByDescending(c => c.Plays).ThenByDescending(c => c.Seconds)
            .Take(limit).ToList();

    /// <summary>Most played artists. The key is the artist name, since that is all a log line carries.</summary>
    public static List<ListenCount> TopArtists(IEnumerable<ListenEntry> entries, int limit = 50) =>
        entries.Where(e => e.Artist.Length > 0)
            .GroupBy(e => e.Artist, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new ListenCount(g.Key, g.Key, g.Key, g.Count(), g.Sum(e => e.Seconds)))
            .OrderByDescending(c => c.Plays).ThenByDescending(c => c.Seconds)
            .Take(limit).ToList();

    public static ListenTotals Totals(IEnumerable<ListenEntry> entries)
    {
        var list = entries.ToList();
        return new ListenTotals(
            list.Count,
            list.Sum(e => e.Seconds),
            list.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count(),
            list.Select(e => e.Artist).Where(a => a.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).Count());
    }

    /// <summary>Plays per hour of the day (24 buckets, local time), to show when you actually listen.</summary>
    public static int[] ByHour(IEnumerable<ListenEntry> entries)
    {
        var hours = new int[24];
        foreach (var e in entries) hours[e.AtUtc.ToLocalTime().Hour]++;
        return hours;
    }

    /// <summary>A human label for a period, used by the statistics page's chips.</summary>
    public static string PeriodLabel(int days) => days switch
    {
        7 => "7 jours",
        30 => "30 jours",
        365 => "12 mois",
        _ => "Depuis toujours",
    };

    /// <summary>
    /// "3 h 42 min", "42 min" or "18 s" — how long a pile of listens adds up to. Truncated rather than
    /// rounded, so the figure never claims more listening than actually happened.
    /// </summary>
    public static string Duration(double seconds)
    {
        if (seconds < 0) seconds = 0;
        if (seconds < 60) return $"{(int)seconds} s";
        var minutes = (int)(seconds / 60);
        if (minutes < 60) return $"{minutes} min";
        return $"{minutes / 60} h {minutes % 60:00} min";
    }
}
