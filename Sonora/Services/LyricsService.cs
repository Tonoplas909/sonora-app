using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Sonora.Appearance.Features;
using Sonora.Models;

namespace Sonora.Services;

/// <summary>A track's lyrics as returned by LRCLIB: synced lines when available, otherwise plain text.</summary>
public sealed record LyricsResult(List<LyricLine> Synced, string? Plain, bool Instrumental);

/// <summary>One LRCLIB entry offered when the user picks lyrics by hand.</summary>
public sealed record LyricsCandidate(long Id, string Title, string Artist, string Album, double DurationSec, LyricsResult Lyrics);

/// <summary>
/// Looks up lyrics on LRCLIB (lrclib.net), a free, community-run, open lyrics database (no API key, no account).
/// Tries an exact match first (title, artist, album, duration), then falls back to a search, keeping only an entry for
/// the same recording (see <see cref="LocalMatcher{T}"/>). A local file the user linked to an entry by hand
/// (<see cref="LinkedId"/>) gets that entry instead. Results are cached in memory only, for the lifetime of the app — nothing touches disk.
/// </summary>
public sealed class LyricsService
{
    static readonly HttpClient Http = CreateClient();
    const string Base = "https://lrclib.net/api/";

    readonly Dictionary<string, LyricsResult?> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<long, LyricsResult> _byId = [];

    /// <summary>The LRCLIB entry the user linked to a track by hand, if any.</summary>
    public Func<Track, long?>? LinkedId { get; set; }

    public async Task<LyricsResult?> GetAsync(Track track, CancellationToken ct = default)
    {
        if (LinkedId?.Invoke(track) is { } id) return await GetByIdAsync(id, ct);
        var artist = PrimaryArtist(track);
        var key = $"{artist}{track.Title}{track.Album}{(int)Math.Round(track.DurationSec)}".ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var result = await FetchAsync(track, artist, ct);
        _cache[key] = result;
        return result;
    }

    async Task<LyricsResult?> FetchAsync(Track track, string artist, CancellationToken ct)
    {
        try
        {
            var exact = await GetJsonAsync("get?" + Query(track, artist, exact: true), ct);
            if (exact.HasValue && exact.Value.ValueKind == JsonValueKind.Object) return Map(exact.Value);

            var found = await GetJsonAsync("search?" + Query(track, artist, exact: false), ct);
            if (!found.HasValue || found.Value.ValueKind != JsonValueKind.Array) return null;

            // The search is loose: keep only an entry for the same recording (same title, overlapping artist, length
            // within a few seconds). A remix, a live take or a mashup sharing the title would show lyrics that do not
            // follow the music, and no lyrics beats wrong ones (issue #11).
            var matcher = new LocalMatcher<JsonElement>(found.Value.EnumerateArray(),
                e => new TrackKey(Str(e, "trackName") ?? "", Str(e, "artistName") ?? "", Num(e, "duration")));
            var best = matcher.Find(new TrackKey(track.Title, artist, track.DurationSec));
            return best.ValueKind == JsonValueKind.Object ? Map(best) : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; } // best effort: a failed lookup just means "no lyrics", never a crash
    }

    async Task<LyricsResult?> GetByIdAsync(long id, CancellationToken ct)
    {
        if (_byId.TryGetValue(id, out var known)) return known;
        try
        {
            var found = await GetJsonAsync("get/" + id.ToString(CultureInfo.InvariantCulture), ct);
            if (found is not { ValueKind: JsonValueKind.Object } e) return null;
            return _byId[id] = Map(e);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; } // offline: no lyrics this time, the link stays and is tried again next time
    }

    /// <summary>Free-text search on LRCLIB, for the user to pick the right lyrics themselves. Throws when LRCLIB cannot be reached.</summary>
    public async Task<List<LyricsCandidate>> SearchAsync(string query, CancellationToken ct = default)
    {
        var found = await GetJsonAsync("search?q=" + Uri.EscapeDataString(query.Trim()), ct)
                    ?? throw new HttpRequestException("LRCLIB did not answer.");
        if (found.ValueKind != JsonValueKind.Array) return [];
        var list = new List<LyricsCandidate>();
        foreach (var e in found.EnumerateArray())
        {
            if (!e.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id)) continue;
            var lyrics = _byId[id] = Map(e); // picking one then needs no second request
            list.Add(new LyricsCandidate(id, Str(e, "trackName") ?? "", Str(e, "artistName") ?? "", Str(e, "albumName") ?? "",
                Num(e, "duration"), lyrics));
        }
        return list;
    }

    async Task<JsonElement?> GetJsonAsync(string query, CancellationToken ct)
    {
        using var res = await Http.GetAsync(Base + query, ct);
        if (!res.IsSuccessStatusCode) return null;
        var text = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    static string Query(Track t, string artist, bool exact)
    {
        var parts = new List<string>
        {
            "track_name=" + Uri.EscapeDataString(t.Title),
            "artist_name=" + Uri.EscapeDataString(artist),
        };
        if (exact)
        {
            if (!string.IsNullOrWhiteSpace(t.Album)) parts.Add("album_name=" + Uri.EscapeDataString(t.Album));
            if (t.DurationSec > 0) parts.Add("duration=" + ((int)Math.Round(t.DurationSec)).ToString(CultureInfo.InvariantCulture));
        }
        return string.Join("&", parts);
    }

    static LyricsResult Map(JsonElement e)
    {
        var synced = Str(e, "syncedLyrics");
        var plain = Str(e, "plainLyrics");
        var instrumental = e.TryGetProperty("instrumental", out var i) && i.ValueKind == JsonValueKind.True;
        return new LyricsResult(Lrc.Parse(synced), string.IsNullOrWhiteSpace(plain) ? null : plain, instrumental);
    }

    static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static double Num(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    /// <summary>LRCLIB indexes by the performing artist: for "A, B" credits, the first name matches best.</summary>
    static string PrimaryArtist(Track t) => t.Artist.Split(',')[0].Trim();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Sonora/1.0.0");
        return c;
    }
}
