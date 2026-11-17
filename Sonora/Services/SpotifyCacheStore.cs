using System.Text.Json;
using IOPath = System.IO.Path;

namespace Sonora.Services;

/// <summary>A cached Spotify item: just enough to draw it before the API answers.</summary>
public sealed class CachedItem
{
    public string Uri { get; set; } = "";
    public string Name { get; set; } = "";
    public string Secondary { get; set; } = "";
    public string? Art { get; set; }

    /// <summary>Number of tracks, for a playlist.</summary>
    public int Count { get; set; }

    /// <summary>Release year and ISO release date, for an album or a show.</summary>
    public uint Year { get; set; }
    public string ReleaseDate { get; set; } = "";

    /// <summary>Owner id, for a playlist.</summary>
    public string OwnerId { get; set; } = "";
}

/// <summary>A cached track of the liked-songs collection.</summary>
public sealed class CachedTrack
{
    public string Uri { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string? ArtistUri { get; set; }
    public string Album { get; set; } = "";
    public string? AlbumUri { get; set; }
    public string? Art { get; set; }
    public double Seconds { get; set; }
}

/// <summary>Everything Sonora draws from the Spotify library, as last seen.</summary>
public sealed class SpotifyCache
{
    public DateTime SavedAtUtc { get; set; }
    public string UserId { get; set; } = "";
    public List<CachedItem> Playlists { get; set; } = [];
    public List<CachedItem> Albums { get; set; } = [];
    public List<CachedItem> Shows { get; set; } = [];
    public List<CachedItem> Artists { get; set; } = [];
    public List<CachedItem> TopArtists { get; set; } = [];
    public List<CachedItem> NewReleases { get; set; } = [];
    public List<CachedTrack> Liked { get; set; } = [];
}

/// <summary>
/// Reads and writes the Spotify library cache. Without it, every start shows an empty library until six
/// API calls come back — and shows nothing at all with no network, even though the account's contents
/// have not changed. The cache is only ever a first draft: the API refresh replaces it.
/// </summary>
public static class SpotifyCacheStore
{
    static string File => IOPath.Combine(StateStore.Dir, "spotify-cache.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static SpotifyCache? Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return null;
            using var stream = System.IO.File.OpenRead(File);
            return JsonSerializer.Deserialize<SpotifyCache>(stream, Options);
        }
        catch
        {
            return null; // a stale or broken cache is simply ignored
        }
    }

    public static void Save(SpotifyCache cache)
    {
        try
        {
            Directory.CreateDirectory(StateStore.Dir);
            var tmp = File + ".tmp";
            using (var stream = System.IO.File.Create(tmp)) JsonSerializer.Serialize(stream, cache, Options);
            System.IO.File.Move(tmp, File, overwrite: true);
        }
        catch
        {
            // the cache is an optimisation; failing to write it changes nothing for the user
        }
    }

    public static void Clear()
    {
        try { if (System.IO.File.Exists(File)) System.IO.File.Delete(File); }
        catch { /* nothing useful to do */ }
    }
}
