using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sonora.Services;
using Sonora.Appearance.Localization;

namespace Sonora.Spotify;

/// <summary>
/// Official Spotify sign-in: Authorization Code flow with PKCE (no client secret).
/// The user signs in on accounts.spotify.com in their own browser; Sonora only receives
/// the authorization code on a loopback port and keeps the refresh token encrypted with DPAPI.
/// </summary>
public sealed class SpotifyAuth(HttpClient http)
{
    public const int Port = 5543;
    public static readonly string RedirectUri = $"http://127.0.0.1:{Port}/callback";

    const string Scopes =
        "streaming user-read-email user-read-private user-read-playback-state user-modify-playback-state " +
        "user-read-currently-playing user-library-read user-library-modify playlist-read-private " +
        "playlist-read-collaborative playlist-modify-private playlist-modify-public user-read-recently-played " +
        "user-top-read user-follow-read user-follow-modify user-read-playback-position";

    static readonly string TokenFile = Path.Combine(StateStore.Dir, "spotify.bin");

    readonly SemaphoreSlim _lock = new(1, 1);
    string? _access;
    DateTime _expiresUtc;
    string? _refresh;

    public string ClientId { get; private set; } = "";
    public bool IsSignedIn => _refresh != null;

    public void Load()
    {
        try
        {
            if (!File.Exists(TokenFile)) return;
            var json = ProtectedData.Unprotect(File.ReadAllBytes(TokenFile), null, DataProtectionScope.CurrentUser);
            using var doc = JsonDocument.Parse(json);
            ClientId = doc.RootElement.GetProperty("clientId").GetString() ?? "";
            _refresh = doc.RootElement.GetProperty("refresh").GetString();
        }
        catch
        {
            _refresh = null;
        }
    }

    void Save()
    {
        Directory.CreateDirectory(StateStore.Dir);
        var json = JsonSerializer.SerializeToUtf8Bytes(new { clientId = ClientId, refresh = _refresh });
        File.WriteAllBytes(TokenFile, ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser));
    }

    public void SignOut()
    {
        _access = null;
        _refresh = null;
        try { File.Delete(TokenFile); } catch { /* ignore */ }
    }

    public void Invalidate() => _access = null;

    public async Task LoginAsync(string clientId, CancellationToken ct)
    {
        clientId = clientId.Trim();
        if (clientId.Length == 0) throw new InvalidOperationException(Loc.T("spotify.auth_enter_client_id"));

        var verifier = B64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = B64Url(RandomNumberGenerator.GetBytes(16));

        var listener = new TcpListener(IPAddress.Loopback, Port);
        try { listener.Start(); }
        catch (SocketException) { throw new InvalidOperationException(Loc.T("spotify.auth_port_in_use", Port)); }

        try
        {
            var url = "https://accounts.spotify.com/authorize?" + Query(
                ("response_type", "code"), ("client_id", clientId), ("scope", Scopes), ("redirect_uri", RedirectUri),
                ("code_challenge_method", "S256"), ("code_challenge", challenge), ("state", state));
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));

            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                var line = await ReadRequestLineAsync(stream, timeout.Token);
                var target = line.Split(' ') is { Length: >= 2 } p ? p[1] : "";
                if (!target.StartsWith("/callback", StringComparison.Ordinal))
                {
                    await RespondAsync(stream, "404 Not Found", "");
                    continue;
                }

                var q = ParseQuery(target);
                if (q.GetValueOrDefault("state") != state)
                {
                    await RespondAsync(stream, "400 Bad Request", Page(Loc.T("spotify.auth_page_invalid")));
                    continue;
                }
                if (q.TryGetValue("error", out var error))
                {
                    await RespondAsync(stream, "200 OK", Page(Loc.T("spotify.auth_page_cancelled")));
                    throw new InvalidOperationException(Loc.T("spotify.auth_refused", error));
                }

                await RespondAsync(stream, "200 OK", Page(Loc.T("spotify.auth_page_done")));
                await ExchangeAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = q.GetValueOrDefault("code") ?? "",
                    ["redirect_uri"] = RedirectUri,
                    ["client_id"] = clientId,
                    ["code_verifier"] = verifier,
                }, ct);
                ClientId = clientId;
                Save();
                return;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(Loc.T("spotify.auth_timeout"));
        }
        finally
        {
            listener.Stop();
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_access != null && DateTime.UtcNow < _expiresUtc.AddSeconds(-60)) return _access;
            if (_refresh == null) throw new InvalidOperationException(Loc.T("spotify.auth_not_signed_in"));
            try
            {
                await ExchangeAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = _refresh,
                    ["client_id"] = ClientId,
                }, ct);
            }
            catch (SpotifyAuthException ex) when (ex.Status == 400)
            {
                SignOut(); // refresh token revoked or expired
                throw new InvalidOperationException(Loc.T("spotify.auth_session_expired"));
            }
            Save();
            return _access!;
        }
        finally
        {
            _lock.Release();
        }
    }

    async Task ExchangeAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var res = await http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form), ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new SpotifyAuthException((int)res.StatusCode, Loc.T("spotify.auth_failed", (int)res.StatusCode, body));
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        _access = r.GetProperty("access_token").GetString();
        _expiresUtc = DateTime.UtcNow.AddSeconds(r.GetProperty("expires_in").GetInt32());
        if (r.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { Length: > 0 } refresh) _refresh = refresh;
    }

    // ---------------- helpers ----------------

    static string B64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string Query(params (string Key, string Value)[] pairs) =>
        string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    static Dictionary<string, string> ParseQuery(string target)
    {
        var result = new Dictionary<string, string>();
        var i = target.IndexOf('?');
        if (i < 0) return result;
        foreach (var part in target[(i + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return result;
    }

    static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        while (sb.Length < 16384)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) break;
            sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
            var s = sb.ToString();
            if (s.Contains("\r\n\r\n")) break;
        }
        var text = sb.ToString();
        var end = text.IndexOf("\r\n", StringComparison.Ordinal);
        return end >= 0 ? text[..end] : text;
    }

    static async Task RespondAsync(NetworkStream stream, string status, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    static string Page(string message) =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Sonora</title></head>" +
        "<body style=\"background:#121212;color:#fff;font-family:Segoe UI,sans-serif;display:flex;align-items:center;" +
        "justify-content:center;height:100vh;margin:0\"><div style=\"text-align:center\"><h1 style=\"color:#9b7bff\">Sonora</h1>" +
        $"<p>{WebUtility.HtmlEncode(message)}</p></div></body></html>";
}

public sealed class SpotifyAuthException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
