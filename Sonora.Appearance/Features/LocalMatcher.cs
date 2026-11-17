namespace Sonora.Appearance.Features;

/// <summary>What identifies a piece of music when no file path or Spotify id is shared: its title, artist and length.</summary>
public readonly record struct TrackKey(string Title, string Artist, double Seconds);

/// <summary>
/// Finds, among the files a person has, the one a playlist entry from somewhere else stands for (a local file listed in a
/// Spotify playlist, a track of an imported playlist). Matching is by title, artist and length, and only a title that
/// agrees exactly (ignoring case and accents) counts: a wrong file playing is worse than a "locate" prompt.
/// </summary>
public sealed class LocalMatcher<T>
{
    /// <summary>How far apart two lengths may be and still be the same recording (tags, encoders and Spotify round differently).</summary>
    public const double ToleranceSeconds = 3;

    readonly ILookup<string, (T Item, TrackKey Key, string Artist)> _byTitle;

    public LocalMatcher(IEnumerable<T> items, Func<T, TrackKey> key)
    {
        _byTitle = items.Select(i => { var k = key(i); return (Item: i, Key: k, Artist: ListQuery.Fold(k.Artist).Trim()); })
            .ToLookup(x => ListQuery.Fold(x.Key.Title).Trim());
    }

    /// <summary>The best file for <paramref name="wanted"/>, or null when none is convincing.</summary>
    public T? Find(TrackKey wanted)
    {
        var title = ListQuery.Fold(wanted.Title).Trim();
        if (title.Length == 0) return default;
        var artist = ListQuery.Fold(wanted.Artist).Trim();

        (T Item, double Distance)? best = null;
        foreach (var c in _byTitle[title])
        {
            if (!SameArtist(artist, c.Artist)) continue;
            double distance = wanted.Seconds > 0 && c.Key.Seconds > 0 ? Math.Abs(wanted.Seconds - c.Key.Seconds) : 0;
            if (distance > ToleranceSeconds) continue;
            if (best is null || distance < best.Value.Distance) best = (c.Item, distance);
        }
        return best is { } b ? b.Item : default;
    }

    /// <summary>An unknown artist on either side matches; otherwise the names must overlap ("A, B" against "A").</summary>
    static bool SameArtist(string wanted, string candidate)
    {
        if (wanted.Length == 0 || candidate.Length == 0) return true;
        return wanted.Contains(candidate, StringComparison.Ordinal) || candidate.Contains(wanted, StringComparison.Ordinal);
    }
}
