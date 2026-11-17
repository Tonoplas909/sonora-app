using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Sonora.Appearance.Features;

namespace Sonora.Services;

/// <summary>
/// Submits finished listens to ListenBrainz, the open listening-history service. Spotify offers a
/// third-party app no way to do this, and Sonora already knows every finished listen, so forwarding
/// them is a few lines. Entirely opt-in: nothing leaves the machine until the user pastes their own
/// user token in the settings.
/// </summary>
public sealed class ScrobbleService(HttpClient? http = null)
{
    const string Endpoint = "https://api.listenbrainz.org/1/submit-listens";

    readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Raised with a short message when a submission fails, so the user is not left guessing.</summary>
    public event Action<string>? Error;

    public static bool HasToken(string? token) => !string.IsNullOrWhiteSpace(token);

    /// <summary>Submits one finished listen. Does nothing without a token.</summary>
    public async Task SubmitAsync(string? token, ListenEntry entry, CancellationToken ct = default)
    {
        if (!HasToken(token) || entry.Artist.Length == 0 || entry.Title.Length == 0) return;
        var body = new
        {
            listen_type = "single",
            payload = new[]
            {
                new
                {
                    listened_at = new DateTimeOffset(DateTime.SpecifyKind(entry.AtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds(),
                    track_metadata = new
                    {
                        artist_name = entry.Artist,
                        track_name = entry.Title,
                        release_name = entry.Album.Length > 0 ? entry.Album : null,
                    },
                },
            },
        };
        await PostAsync(token!, body, ct);
    }

    /// <summary>
    /// Checks a token against ListenBrainz. The endpoint answers 200 even for a bad token, saying so in
    /// its body ("valid": false), so the status code alone must not be trusted here.
    /// </summary>
    public async Task<string> TestAsync(string? token, CancellationToken ct = default)
    {
        if (!HasToken(token)) return "Aucun jeton ListenBrainz renseigné.";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.listenbrainz.org/1/validate-token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", token!.Trim());
            using var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) return $"ListenBrainz a refusé le jeton ({(int)response.StatusCode}).";

            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
            if (!valid)
            {
                var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                return "Jeton refusé par ListenBrainz" + (message is { Length: > 0 } ? $" : {message}" : ".");
            }
            var name = root.TryGetProperty("user_name", out var user) ? user.GetString() : null;
            return name is { Length: > 0 } ? $"Jeton valide : {name}." : "Jeton valide.";
        }
        catch (Exception ex)
        {
            return "ListenBrainz injoignable : " + ex.Message;
        }
    }

    async Task PostAsync(string token, object body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", token.Trim());
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                Error?.Invoke($"Envoi à ListenBrainz refusé ({(int)response.StatusCode}).");
        }
        catch (Exception ex)
        {
            // A listen that fails to leave is already safe in the local log; it is not worth a retry queue.
            Error?.Invoke("Envoi à ListenBrainz impossible : " + ex.Message);
        }
    }
}
