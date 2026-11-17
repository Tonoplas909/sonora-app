using Sonora.Appearance.Localization;
namespace Sonora.Appearance.Features;

/// <summary>What kind of thing a search result is, in the order used to break ties.</summary>
public enum ResultKind
{
    Artist,
    Album,
    Track,
    Playlist,
    Show,
}

/// <summary>A candidate for the "top result" slot: what it is, what it is called, and where it came from.</summary>
public sealed record RankedResult(ResultKind Kind, string Name, object Item);

/// <summary>
/// Picks the one result to put above the rest. Spotify computes its "top result" server-side and does not
/// expose it, so Sonora scores the candidates itself: how closely the name matches what was typed, then
/// the kind, so that typing an artist's name puts the artist first rather than one of their tracks.
/// </summary>
public static class SearchRank
{
    /// <summary>
    /// 0 when the name does not match at all, higher the closer it is: an exact name wins, then a name
    /// starting with the query, then one merely containing it. Accents and case are ignored throughout.
    /// </summary>
    public static int Score(string? query, string? name)
    {
        var q = ListQuery.Fold(query).Trim();
        var n = ListQuery.Fold(name).Trim();
        if (q.Length == 0 || n.Length == 0) return 0;

        if (n == q) return 1000;
        if (n.StartsWith(q, StringComparison.Ordinal)) return 700 - Math.Min(n.Length - q.Length, 300);
        if (n.Contains(q, StringComparison.Ordinal)) return 400 - Math.Min(n.Length - q.Length, 300);

        // Every word typed appears somewhere in the name: a weak match, but still a match.
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 && words.All(w => n.Contains(w, StringComparison.Ordinal)) ? 100 : 0;
    }

    /// <summary>
    /// The best candidate, or null when nothing matches well enough to deserve the slot. Ties go to the
    /// kind people mean most often: an artist before an album, an album before a track.
    /// </summary>
    public static RankedResult? Best(string? query, IEnumerable<RankedResult> candidates)
    {
        RankedResult? best = null;
        var bestScore = 0;
        foreach (var candidate in candidates)
        {
            var score = Score(query, candidate.Name);
            if (score < 400) continue; // a weak match is not worth putting above everything else
            if (best != null && (score < bestScore || (score == bestScore && candidate.Kind >= best.Kind))) continue;
            best = candidate;
            bestScore = score;
        }
        return best;
    }

    /// <summary>The label shown under the top result's name.</summary>
    public static string KindLabel(ResultKind kind) => kind switch
    {
        ResultKind.Artist => Loc.T("kind.artist"),
        ResultKind.Album => Loc.T("kind.album"),
        ResultKind.Track => Loc.T("kind.track"),
        ResultKind.Playlist => Loc.T("kind.playlist"),
        _ => Loc.T("kind.podcast"),
    };
}
