using System.Net.Http;
using System.Text.Json;

namespace Sonora.Services;

/// <summary>An artist ListenBrainz considers close to another, with its own confidence score.</summary>
public sealed record SimilarArtist(string Name, string Mbid, int Score);

/// <summary>
/// Recommendations built outside Spotify. Spotify refuses its recommendation endpoints to third-party
/// apps, so Sonora asks the open stack instead: MusicBrainz resolves an artist's name to an identifier,
/// and ListenBrainz answers which artists people listen to alongside them.
/// Only artist names are ever sent, and only for an artist the user has opened.
/// </summary>
public sealed class DiscoveryService
{
    // MusicBrainz asks every client to identify itself and to stay under one request a second.
    const string UserAgent = "Sonora/1.0 ( https://github.com/Tonoplas909/sonora-app )";

    /// <summary>One of the algorithms the ListenBrainz labs endpoint accepts; it rejects anything else.</summary>
    const string Algorithm = "session_based_days_1800_session_300_contribution_3_threshold_10_limit_100_filter_True_skip_30";

    static readonly HttpClient Http = CreateClient();

    readonly Dictionary<string, string?> _mbids = new(StringComparer.CurrentCultureIgnoreCase);
    readonly SemaphoreSlim _oneAtATime = new(1, 1);
    DateTime _lastMusicBrainzCall = DateTime.MinValue;

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        return http;
    }

    /// <summary>
    /// Artists listened to alongside this one, best first. Empty when the artist is unknown to
    /// MusicBrainz or the service is unreachable — a failed lookup is never worth an error to the user.
    /// </summary>
    public async Task<List<SimilarArtist>> SimilarArtistsAsync(string artistName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return [];
        var mbid = await ResolveMbidAsync(artistName, ct);
        if (mbid == null) return [];

        try
        {
            var url = $"https://labs.api.listenbrainz.org/similar-artists/json?artist_mbids={mbid}&algorithm={Algorithm}";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var found = new List<SimilarArtist>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var name = Str(item, "name");
                var id = Str(item, "artist_mbid");
                if (name == null || id == null) continue;
                if (string.Equals(name, artistName, StringComparison.CurrentCultureIgnoreCase)) continue;
                found.Add(new SimilarArtist(name, id, (int)Num(item, "score")));
            }
            return found.OrderByDescending(a => a.Score).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The MusicBrainz identifier for an artist's name. A real answer — found or confidently not found —
    /// is remembered for the session; a service that is merely busy is not, so a rate-limited moment does
    /// not blank that artist for as long as Sonora runs.
    /// </summary>
    async Task<string?> ResolveMbidAsync(string name, CancellationToken ct)
    {
        if (_mbids.TryGetValue(name, out var known)) return known;

        await _oneAtATime.WaitAsync(ct);
        try
        {
            if (_mbids.TryGetValue(name, out known)) return known;

            var query = Uri.EscapeDataString($"artist:\"{name}\"");
            var url = $"https://musicbrainz.org/ws/2/artist?query={query}&fmt=json&limit=1";

            // MusicBrainz allows one request a second and answers 503 when that is exceeded; one retry
            // covers the usual case of two lookups landing together.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await WaitForRateLimitAsync(ct);
                using var response = await Http.GetAsync(url, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    if (attempt == 0) continue;
                    return null; // busy, not absent: leave the name uncached so it can be tried again
                }
                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                string? mbid = null;
                if (doc.RootElement.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                    foreach (var a in artists.EnumerateArray())
                    {
                        // Only a confident match: a weak one sends the recommendations somewhere else entirely.
                        if (Num(a, "score") >= 90) mbid = Str(a, "id");
                        break;
                    }
                _mbids[name] = mbid;
                return mbid;
            }
            return null;
        }
        catch
        {
            return null; // unreachable rather than absent: nothing worth remembering
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    async Task WaitForRateLimitAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow - _lastMusicBrainzCall;
        var gap = TimeSpan.FromSeconds(1.1);
        if (since < gap) await Task.Delay(gap - since, ct);
        _lastMusicBrainzCall = DateTime.UtcNow;
    }

    static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    static double Num(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : 0;
}
