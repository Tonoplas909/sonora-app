using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Sonora.Models;
using Sonora.Appearance.Localization;

namespace Sonora.Spotify;

public sealed class SpotifyException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Thin client for the official Spotify Web API (api.spotify.com/v1).</summary>
public sealed class SpotifyApi(HttpClient http, SpotifyAuth auth)
{
    const string Base = "https://api.spotify.com/v1/";

    // ---------------- rate limits ----------------

    /// <summary>A "come back in a moment" up to this long is waited out; anything longer is a block (see below).</summary>
    static readonly TimeSpan LongWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Endpoints Spotify has told us to stay away from for a long time, with the moment they open again. A development
    /// app that goes over its quota can be turned away for hours (a Retry-After of 16000 s was seen on /me): asking
    /// again meanwhile only renews the sentence, so nothing is sent until then and the caller gets the answer at once.
    /// </summary>
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _blockedUntil = new();

    static string Key(string path) => (path.StartsWith(Base, StringComparison.Ordinal) ? path[Base.Length..] : path).Split('?')[0];

    /// <summary>When this endpoint will answer again, or null when it is not blocked.</summary>
    public DateTime? BlockedUntil(string path) =>
        _blockedUntil.TryGetValue(Key(path), out var until) && until > DateTime.UtcNow ? until : null;

    static SpotifyException Limited(DateTime untilUtc) =>
        new(429, Loc.T("spotify.rate_limited_until", untilUtc.ToLocalTime().ToString("HH:mm")));

    /// <summary>How long to wait before trying again after a 429, or a thrown block when Spotify asked for a long one.</summary>
    TimeSpan WaitAfterTooManyRequests(string path, HttpResponseMessage res)
    {
        var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
        if (wait <= LongWait) return wait;
        var until = DateTime.UtcNow + wait;
        _blockedUntil[Key(path)] = until;
        throw Limited(until);
    }

    public async Task<JsonElement?> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        if (BlockedUntil(path) is { } blocked) throw Limited(blocked);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            using var req = new HttpRequestMessage(method, path.StartsWith("http", StringComparison.Ordinal) ? path : Base + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.GetAccessTokenAsync(ct));
            if (body != null) req.Content = JsonContent.Create(body);
            else if (method != HttpMethod.Get && method != HttpMethod.Delete) req.Content = new ByteArrayContent([]);

            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(WaitAfterTooManyRequests(path, res), ct);
                continue;
            }
            if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                auth.Invalidate();
                continue;
            }

            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw new SpotifyException((int)res.StatusCode, Describe((int)res.StatusCode, text));
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null; // some endpoints answer with a plain snapshot id
            }
        }
        throw new SpotifyException(429, Loc.T("spotify.rate_limited_briefly"));
    }

    /// <summary>
    /// A request whose body is not JSON — a playlist cover goes up as a base64 JPEG. The content is built
    /// per attempt, since an HttpContent cannot be sent twice.
    /// </summary>
    public async Task SendContentAsync(HttpMethod method, string path, Func<HttpContent> content, CancellationToken ct = default)
    {
        if (BlockedUntil(path) is { } blocked) throw Limited(blocked);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using var req = new HttpRequestMessage(method, Base + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.GetAccessTokenAsync(ct));
            req.Content = content();

            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                auth.Invalidate();
                continue;
            }
            if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
            {
                await Task.Delay(WaitAfterTooManyRequests(path, res), ct);
                continue;
            }
            if (res.IsSuccessStatusCode) return;
            throw new SpotifyException((int)res.StatusCode, Describe((int)res.StatusCode, await res.Content.ReadAsStringAsync(ct)));
        }
        throw new SpotifyException(429, Loc.T("spotify.rate_limited_briefly"));
    }

    public async Task<JsonElement> GetAsync(string path, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Get, path, null, ct) ?? default;

    /// <summary>Follows "next" links (offset or cursor paging), including wrapped pages like {"artists": {...}}.</summary>
    public async Task<List<JsonElement>> GetAllAsync(string path, int max = 10_000, CancellationToken ct = default)
    {
        var list = new List<JsonElement>();
        string? next = path;
        while (next != null && list.Count < max)
        {
            var page = await GetAsync(next, ct);
            if (page.ValueKind != JsonValueKind.Object) break;
            var container = page.TryGetProperty("items", out _) ? page : page.EnumerateObject().First().Value;
            if (!container.TryGetProperty("items", out var items)) break;
            foreach (var it in items.EnumerateArray()) list.Add(it);
            next = container.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        }
        return list;
    }

    static string Describe(int status, string body)
    {
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
                message = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() : e.GetString();
        }
        catch { /* not JSON */ }

        return status switch
        {
            403 when message?.Contains("premium", StringComparison.OrdinalIgnoreCase) == true =>
                Loc.T("spotify.error_premium_required"),
            403 => Loc.T("spotify.error_forbidden") + (message != null ? $" ({message})." : "."),
            404 when message?.Contains("device", StringComparison.OrdinalIgnoreCase) == true =>
                Loc.T("spotify.error_no_device"),
            404 => Loc.T("spotify.error_not_found"),
            _ => message != null ? Loc.T("spotify.error_generic_message", status, message) : Loc.T("spotify.error_generic", status),
        };
    }
}

/// <summary>Maps Web API / Playback SDK JSON to Sonora models, keeping one instance per Spotify URI.</summary>
public sealed class SpotifyCatalog
{
    readonly Dictionary<string, Track> _tracks = [];
    readonly Dictionary<string, Album> _albums = [];
    readonly Dictionary<string, Artist> _artists = [];

    public HashSet<string> Liked { get; } = [];

    public Track? MapTrack(JsonElement t, Album? album = null)
    {
        if (t.ValueKind != JsonValueKind.Object) return null;
        // Track relinking: the SDK may report a market-specific copy; keep the URI the user picked.
        var uri = t.TryGetProperty("linked_from", out var lf) && lf.ValueKind == JsonValueKind.Object && Str(lf, "uri") is { } linked
            ? linked
            : Str(t, "uri");
        if (uri == null || uri.StartsWith("spotify:local:", StringComparison.Ordinal)) return null;
        if (_tracks.TryGetValue(uri, out var existing)) return existing;

        var artists = new List<(string Name, string? Uri)>();
        if (t.TryGetProperty("artists", out var ar) && ar.ValueKind == JsonValueKind.Array)
            foreach (var a in ar.EnumerateArray())
                if (Str(a, "name") is { } n) artists.Add((n, Str(a, "uri")));

        if (album == null && t.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object) album = MapAlbum(al);
        if (t.TryGetProperty("show", out var show) && show.ValueKind == JsonValueKind.Object)
        {
            album ??= MapAlbum(show);
            if (artists.Count == 0 && Str(show, "publisher") is { } pub) artists.Add((pub, null));
        }

        var track = new Track
        {
            Path = uri,
            Uri = uri,
            Title = Str(t, "name") ?? "",
            Artist = string.Join(", ", artists.Select(a => a.Name)),
            ArtistUri = artists.FirstOrDefault().Uri,
            Album = album?.Title ?? "",
            AlbumArtist = album?.Artist ?? "",
            DurationSec = Num(t, "duration_ms") / 1000.0,
            TrackNumber = (uint)Math.Max(0, Num(t, "track_number")),
            AlbumRef = album,
            IsLiked = Liked.Contains(uri),
        };
        _tracks[uri] = track;
        return track;
    }

    public Album MapAlbum(JsonElement a)
    {
        var uri = Str(a, "uri") ?? "";
        if (_albums.TryGetValue(uri, out var existing))
        {
            existing.ArtPath ??= Image(a);
            return existing;
        }
        var artists = a.TryGetProperty("artists", out var ar) && ar.ValueKind == JsonValueKind.Array
            ? string.Join(", ", ar.EnumerateArray().Select(x => Str(x, "name")).OfType<string>())
            : Str(a, "publisher") ?? (Str(a, "type") == "show" ? Loc.T("kind.podcast") : "");
        var date = Str(a, "release_date") ?? "";
        var album = new Album
        {
            Key = uri,
            Uri = uri.Length > 0 ? uri : null,
            Title = Str(a, "name") ?? "",
            Artist = artists,
            Year = date.Length >= 4 && uint.TryParse(date[..4], out var y) ? y : 0,
            ReleaseDate = date,
            ArtPath = Image(a),
        };
        if (uri.Length > 0) _albums[uri] = album;
        return album;
    }

    /// <summary>A podcast episode, shown as a track of its show.</summary>
    public Track? MapEpisode(JsonElement e, Album show)
    {
        var uri = Str(e, "uri");
        if (uri == null) return null;
        if (_tracks.TryGetValue(uri, out var existing)) return existing;
        var track = new Track
        {
            Path = uri,
            Uri = uri,
            Title = Str(e, "name") ?? "",
            Artist = show.Title,
            Album = show.Title,
            AlbumArtist = show.Title,
            DurationSec = Num(e, "duration_ms") / 1000.0,
            AlbumRef = show,
        };
        _tracks[uri] = track;
        return track;
    }

    /// <summary>
    /// An episode straight from a search result. Unlike a show's own listing, it carries no programme,
    /// so its own name and cover are all there is to show.
    /// </summary>
    public Track? MapSearchEpisode(JsonElement e)
    {
        var uri = Str(e, "uri");
        if (uri == null) return null;
        if (_tracks.TryGetValue(uri, out var existing)) return existing;
        var name = Str(e, "name") ?? "";
        var track = new Track
        {
            Path = uri,
            Uri = uri,
            Title = name,
            Artist = Loc.T("spotify.episode"),
            DurationSec = Num(e, "duration_ms") / 1000.0,
            AlbumRef = AlbumFor(uri, name, Loc.T("spotify.episode"), Image(e), 0, Str(e, "release_date") ?? ""),
        };
        _tracks[uri] = track;
        return track;
    }

    public Artist MapArtist(JsonElement a)
    {
        var uri = Str(a, "uri") ?? "";
        if (_artists.TryGetValue(uri, out var existing))
        {
            existing.ArtPath ??= Image(a);
            return existing;
        }
        var artist = new Artist { Name = Str(a, "name") ?? "", Uri = uri, ArtPath = Image(a) };
        if (uri.Length > 0) _artists[uri] = artist;
        return artist;
    }

    /// <summary>
    /// The track for a URI, built from cached fields when the API has not described it yet; the instance
    /// is kept, so a later API mapping of the same URI returns this very object.
    /// </summary>
    public Track TrackFor(string uri, string title, string artist, string? artistUri, Album? album, double seconds)
    {
        if (_tracks.TryGetValue(uri, out var existing)) return existing;
        var track = new Track
        {
            Path = uri,
            Uri = uri,
            Title = title,
            Artist = artist,
            ArtistUri = artistUri,
            Album = album?.Title ?? "",
            AlbumArtist = album?.Artist ?? "",
            DurationSec = seconds,
            AlbumRef = album,
            IsLiked = Liked.Contains(uri),
        };
        _tracks[uri] = track;
        return track;
    }

    /// <summary>
    /// The album for a URI, built from cached fields when the API has not described it yet. Reuses the
    /// instance already known, so a cached album and the one the API returns are the same object.
    /// </summary>
    public Album AlbumFor(string uri, string title, string artist, string? art, uint year, string releaseDate)
    {
        if (_albums.TryGetValue(uri, out var existing))
        {
            existing.ArtPath ??= art;
            return existing;
        }
        var album = new Album
        {
            Key = uri,
            Uri = uri.Length > 0 ? uri : null,
            Title = title,
            Artist = artist,
            Year = year,
            ReleaseDate = releaseDate,
            ArtPath = art,
        };
        if (uri.Length > 0) _albums[uri] = album;
        return album;
    }

    public Artist ArtistFor(string uri, string name) =>
        _artists.TryGetValue(uri, out var a) ? a : _artists[uri] = new Artist { Name = name, Uri = uri };

    public SpotifyPlaylist MapPlaylist(JsonElement p)
    {
        var owner = p.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
        int count = 0;
        foreach (var key in new[] { "tracks", "items" })
            if (p.TryGetProperty(key, out var tr) && tr.ValueKind == JsonValueKind.Object && tr.TryGetProperty("total", out var tot))
                count = tot.GetInt32();
        return new SpotifyPlaylist
        {
            Id = Str(p, "id") ?? "",
            Uri = Str(p, "uri") ?? "",
            Name = Str(p, "name") ?? "",
            Owner = owner.ValueKind == JsonValueKind.Object ? Str(owner, "display_name") ?? Str(owner, "id") ?? "" : "",
            OwnerId = owner.ValueKind == JsonValueKind.Object ? Str(owner, "id") ?? "" : "",
            TrackCount = count,
            IsPublic = p.TryGetProperty("public", out var pub) && pub.ValueKind == JsonValueKind.True,
            IsCollaborative = p.TryGetProperty("collaborative", out var col) && col.ValueKind == JsonValueKind.True,
            ArtPath = Image(p),
        };
    }

    /// <summary>Playlist entries are {"track": {...}} (or {"item": {...}} in newer payloads).</summary>
    public Track? MapPlaylistItem(JsonElement item)
    {
        if (item.TryGetProperty("track", out var t) && t.ValueKind == JsonValueKind.Object) return MapPlaylistTrack(t);
        if (item.TryGetProperty("item", out var i) && i.ValueKind == JsonValueKind.Object) return MapPlaylistTrack(i);
        return null;
    }

    Track? MapPlaylistTrack(JsonElement t) => IsLocalEntry(t) ? MapLocalEntry(t) : MapTrack(t);

    static bool IsLocalEntry(JsonElement t) =>
        (t.TryGetProperty("is_local", out var l) && l.ValueKind == JsonValueKind.True) || Str(t, "uri")?.StartsWith("spotify:local:", StringComparison.Ordinal) == true;

    /// <summary>
    /// A local file somebody added to the playlist in Spotify's app. The API gives its tags (and a spotify:local: URI that
    /// is only a name), never the file, so the entry comes back as a placeholder for the app to resolve.
    /// </summary>
    static Track? MapLocalEntry(JsonElement t)
    {
        var uri = Str(t, "uri");
        if (uri == null) return null;
        // spotify:local:<artist>:<album>:<title>:<seconds>, each part URL-encoded: the fallback when the tags are missing.
        var parts = uri.Split(':');
        string Part(int i) => i < parts.Length ? Uri.UnescapeDataString(parts[i].Replace('+', ' ')) : "";

        var artists = new List<string>();
        if (t.TryGetProperty("artists", out var ar) && ar.ValueKind == JsonValueKind.Array)
            foreach (var a in ar.EnumerateArray())
                if (Str(a, "name") is { Length: > 0 } n) artists.Add(n);

        var album = t.TryGetProperty("album", out var al) ? Str(al, "name") : null;
        var seconds = Num(t, "duration_ms") / 1000.0;
        if (seconds <= 0 && double.TryParse(Part(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fromUri)) seconds = fromUri;

        return new Track
        {
            Path = "",
            LocalRef = uri,
            IsMissing = true,
            Title = Str(t, "name") is { Length: > 0 } name ? name : Part(4),
            Artist = artists.Count > 0 ? string.Join(", ", artists) : Part(2),
            Album = !string.IsNullOrEmpty(album) ? album : Part(3),
            DurationSec = seconds,
        };
    }

    /// <summary>When a playlist entry was added, as ticks (UTC); 0 when Spotify does not say.</summary>
    public static long AddedTicks(JsonElement item) =>
        DateTimeOffset.TryParse(Str(item, "added_at"), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var at) ? at.UtcTicks : 0;

    public static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static double Num(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    /// <summary>Picks the image closest to 300 px wide.</summary>
    public static string? Image(JsonElement o)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("images", out var imgs) || imgs.ValueKind != JsonValueKind.Array)
            return null;
        string? best = null;
        double bestScore = double.MaxValue;
        foreach (var img in imgs.EnumerateArray())
        {
            var url = Str(img, "url");
            if (url == null) continue;
            var w = Num(img, "width");
            var score = w <= 0 ? 1000 : Math.Abs(w - 300);
            if (score < bestScore) { bestScore = score; best = url; }
        }
        return best;
    }
}
