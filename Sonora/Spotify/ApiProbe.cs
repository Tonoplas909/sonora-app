using System.Text;
using System.Text.Json;

namespace Sonora.Spotify;

/// <summary>
/// Diagnostics (<c>--snapshot out apicheck</c>): read-only GET requests that report which Web API endpoints answer
/// for this developer app. Only status codes and field names are written, never the account's data.
/// </summary>
public static class ApiProbe
{
    public static async Task<string> RunAsync(SpotifyApi api, SpotifyAuth auth)
    {
        if (!auth.IsSignedIn) return "not signed in";
        var sb = new StringBuilder();

        async Task<JsonElement> Try(string label, string path)
        {
            try
            {
                var r = await api.GetAsync(path);
                sb.AppendLine($"200 {label} {Shape(r)}");
                return r;
            }
            catch (SpotifyException ex) { sb.AppendLine($"{ex.Status} {label}"); }
            catch (Exception ex) { sb.AppendLine($"ERR {label} {ex.GetType().Name}"); }
            return default;
        }

        var me = await Try("me", "me");
        var topTracks = await Try("top-tracks", "me/top/tracks?limit=5&time_range=short_term");
        var topArtists = await Try("top-artists", "me/top/artists?limit=5&time_range=medium_term");
        await Try("recently-played", "me/player/recently-played?limit=5");
        var releases = await Try("new-releases", "browse/new-releases?limit=5");
        await Try("categories", "browse/categories?limit=5");
        var shows = await Try("saved-shows", "me/shows?limit=5");
        await Try("saved-episodes", "me/episodes?limit=5");
        var following = await Try("following", "me/following?type=artist&limit=5");
        var playlists = await Try("playlists", "me/playlists?limit=5");
        await Try("queue", "me/player/queue");
        await Try("devices", "me/player/devices");
        var search = await Try("search-shows", "search?q=musique&type=show,episode&limit=5");

        var trackId = FirstId(topTracks, ["items"]);
        var artistId = FirstId(topArtists, ["items"]) ?? FirstId(following, ["artists", "items"]);
        var albumId = FirstId(releases, ["albums", "items"]);
        var playlistId = FirstId(playlists, ["items"]);
        var showId = FirstId(shows, ["items"], nested: "show") ?? FirstId(search, ["shows", "items"]);

        if (trackId != null)
        {
            await Try("tracks-contains", $"me/tracks/contains?ids={trackId}");
            await Try("library-contains", $"me/library/contains?uris=spotify:track:{trackId}");
        }
        if (artistId != null)
        {
            await Try("following-contains", $"me/following/contains?type=artist&ids={artistId}");
            await Try("artist", $"artists/{artistId}");
            await Try("artist-top-tracks", $"artists/{artistId}/top-tracks?market=from_token");
            await Try("artist-albums", $"artists/{artistId}/albums?include_groups=album,single&limit=5");
            await Try("related-artists", $"artists/{artistId}/related-artists");
            await Try("recommendations", $"recommendations?seed_artists={artistId}&limit=5");
        }
        if (albumId != null)
        {
            await Try("album", $"albums/{albumId}");
            await Try("albums-contains", $"me/albums/contains?ids={albumId}");
        }
        if (playlistId != null)
        {
            await Try("playlist", $"playlists/{playlistId}?fields=name,description,owner(id),snapshot_id");
            await Try("playlist-tracks", $"playlists/{playlistId}/tracks?limit=2");
            await Try("playlist-items", $"playlists/{playlistId}/items?limit=2");
        }
        if (showId != null)
        {
            await Try("show", $"shows/{showId}");
            await Try("show-episodes", $"shows/{showId}/episodes?limit=2");
        }
        sb.AppendLine($"product={(me.ValueKind == JsonValueKind.Object && me.TryGetProperty("product", out var p) ? p.GetString() : "?")}");
        return sb.ToString();
    }

    /// <summary>Field names only (two levels), plus item counts.</summary>
    static string Shape(JsonElement e, int depth = 0)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            var first = e.GetArrayLength() > 0 ? e[0] : default;
            return $"[{e.GetArrayLength()}]" + (depth < 2 && first.ValueKind == JsonValueKind.Object ? Shape(first, depth + 1) : "");
        }
        if (e.ValueKind != JsonValueKind.Object) return "";
        var parts = e.EnumerateObject().Select(p =>
            depth < 2 && p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? p.Name + Shape(p.Value, depth + 1) : p.Name);
        return "{" + string.Join(",", parts) + "}";
    }

    static string? FirstId(JsonElement root, string[] path, string? nested = null)
    {
        var e = root;
        foreach (var key in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out e)) return null;
        }
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() == 0) return null;
        var item = e[0];
        if (nested != null && item.ValueKind == JsonValueKind.Object && item.TryGetProperty(nested, out var n)) item = n;
        return item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) ? id.GetString() : null;
    }
}
