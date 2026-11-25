using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Sonora.Appearance.Features;

namespace Sonora.Services;

/// <summary>What the statistics page shows for one period.</summary>
public sealed record ListenBrainzStats(long TotalListens, List<LbRanked> Tracks, List<LbRanked> Artists, List<LbActivity> Activity);

/// <summary>
/// Reads a user's own statistics from ListenBrainz. The token is what proves who the user is, so the
/// user name always comes from a validated token, never from something typed.
/// </summary>
public sealed class ListenBrainzStatsService(HttpClient? http = null)
{
    const string Api = "https://api.listenbrainz.org/1/";

    readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// Asks ListenBrainz whether the token is good. Null means the question could not be asked (offline,
    /// ListenBrainz down), which must not be taken for "invalid": a user without a network keeps their token.
    /// </summary>
    public async Task<LbTokenCheck?> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return new(false, "");
        try
        {
            var body = await GetAsync("validate-token", token, ct);
            // A server error says nothing about the token.
            return body == null || (int)body.Value.Status >= 500 ? null : ListenBrainzApi.ParseToken(body.Value.Text);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Everything the page shows for a period, fetched together. Throws when ListenBrainz cannot be reached.</summary>
    public async Task<ListenBrainzStats> LoadAsync(string token, string user, string range, CancellationToken ct = default)
    {
        if (!ListenBrainzApi.IsKnownRange(range)) range = "this_month";
        var name = Uri.EscapeDataString(user);
        var query = $"?range={range}";
        var count = GetAsync($"user/{name}/listen-count", token, ct);
        var tracks = GetAsync($"stats/user/{name}/recordings{query}&count=25", token, ct);
        var artists = GetAsync($"stats/user/{name}/artists{query}&count=25", token, ct);
        var activity = GetAsync($"stats/user/{name}/listening-activity{query}", token, ct);
        await Task.WhenAll(count, tracks, artists, activity);

        // 204 (no body): ListenBrainz has not computed this period yet; the parsers read that as empty.
        return new ListenBrainzStats(
            ListenBrainzApi.ParseListenCount(count.Result?.Text),
            ListenBrainzApi.ParseRecordings(tracks.Result?.Text),
            ListenBrainzApi.ParseArtists(artists.Result?.Text),
            ListenBrainzApi.ParseActivity(activity.Result?.Text));
    }

    /// <summary>One GET. Null for a 204; throws for any other failure so the caller can say it could not reach ListenBrainz.</summary>
    async Task<(string Text, HttpStatusCode Status)?> GetAsync(string path, string? token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api + path);
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Token", token.Trim());
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        // A refused token still comes back as readable JSON for validate-token; every other path needs a success.
        if (!response.IsSuccessStatusCode && path != "validate-token") response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync(ct), response.StatusCode);
    }
}
