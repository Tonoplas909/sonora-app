namespace Sonora.Appearance.Features;

/// <summary>Which optional features are on, and their options. Stored in state.json.</summary>
public sealed class FeatureSettings
{
    public static readonly string[] Alignments = ["left", "center"];
    public static readonly string[] LibrarySorts = ["recent", "alpha"];
    public static readonly string[] TopRanges = ["short_term", "medium_term", "long_term"];
    public const int MaxRecentSearches = 10;

    // Discovery
    public bool Podcasts { get; set; } = true;
    public bool Tops { get; set; } = true;
    public bool History { get; set; } = true;
    public bool NewReleases { get; set; } = true;
    /// <summary>Sonora's own play history, written to listening.jsonl; off means nothing is recorded.</summary>
    public bool ListeningLog { get; set; } = true;

    /// <summary>
    /// Similar-artist recommendations built on MusicBrainz and ListenBrainz, since Spotify refuses its
    /// own to third-party apps. Off means those services are never contacted.
    /// </summary>
    public bool Discovery { get; set; } = true;

    /// <summary>The keyboard map; anything the user has not changed keeps its default.</summary>
    public Shortcuts Shortcuts { get; set; } = new();
    public string TopRange { get; set; } = "short_term";

    /// <summary>Look for a new version of Sonora each time it opens (on by default; the settings page also has a manual button).</summary>
    public bool UpdateCheck { get; set; } = true;

    /// <summary>When the last check ran, so opening the app again soon after does not ask again (see <see cref="UpdateSchedule"/>).</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    // Lyrics
    public bool Lyrics { get; set; } = true;
    public double LyricsScale { get; set; } = 1.0;
    public string LyricsAlign { get; set; } = "left";

    // Mini player and track-change bubble
    public bool MiniPlayerOnTop { get; set; } = true;
    public double MiniPlayerOpacity { get; set; } = 1.0;
    public bool TrackToast { get; set; } = true;
    public int ToastSeconds { get; set; } = 4;

    // Search and library
    public bool KeepRecentSearches { get; set; } = true;
    public List<string> RecentSearches { get; set; } = [];
    public string LibrarySort { get; set; } = "recent";
    public List<string> Pinned { get; set; } = [];

    public void Repair()
    {
        LyricsScale = double.IsNaN(LyricsScale) ? 1.0 : Math.Clamp(LyricsScale, 0.8, 1.6);
        MiniPlayerOpacity = double.IsNaN(MiniPlayerOpacity) ? 1.0 : Math.Clamp(MiniPlayerOpacity, 0.4, 1.0);
        ToastSeconds = Math.Clamp(ToastSeconds, 2, 10);
        if (!Alignments.Contains(LyricsAlign)) LyricsAlign = "left";
        if (!LibrarySorts.Contains(LibrarySort)) LibrarySort = "recent";
        if (!TopRanges.Contains(TopRange)) TopRange = "short_term";
        RecentSearches = (RecentSearches ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Take(MaxRecentSearches).ToList();
        Pinned = (Pinned ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        Shortcuts ??= new Shortcuts();
        Shortcuts.Repair();
    }

    /// <summary>Remembers a search: newest first, no duplicates (case-insensitive), at most ten.</summary>
    public void AddRecentSearch(string? query)
    {
        var q = query?.Trim() ?? "";
        if (q.Length == 0 || !KeepRecentSearches) return;
        RecentSearches.RemoveAll(s => string.Equals(s, q, StringComparison.CurrentCultureIgnoreCase));
        RecentSearches.Insert(0, q);
        if (RecentSearches.Count > MaxRecentSearches) RecentSearches.RemoveRange(MaxRecentSearches, RecentSearches.Count - MaxRecentSearches);
    }

    public bool IsPinned(string key) => Pinned.Contains(key);

    /// <summary>Where a pin sits in the user's own order (0 = first); -1 when the key is not pinned.</summary>
    public int PinIndex(string key) => Pinned.IndexOf(key);

    /// <summary>
    /// Moves a pin so it ends up where <paramref name="target"/> is: before it when dragged up, after it when
    /// dragged down. Returns false when either key is not pinned or they are the same.
    /// </summary>
    public bool MovePin(string key, string target)
    {
        int from = Pinned.IndexOf(key), to = Pinned.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return false;
        Pinned.RemoveAt(from);
        Pinned.Insert(to, key);
        return true;
    }

    /// <summary>Pins or unpins a library entry. Returns true when it is now pinned.</summary>
    public bool TogglePin(string key)
    {
        if (Pinned.Remove(key)) return false;
        Pinned.Add(key);
        return true;
    }
}
