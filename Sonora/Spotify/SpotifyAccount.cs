using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Sonora.Core;
using Sonora.Models;
using static Sonora.Spotify.SpotifyCatalog;

namespace Sonora.Spotify;

public sealed record SpotifySearchResult(List<Track> Tracks, List<Album> Albums, List<Artist> Artists,
    List<SpotifyPlaylist> Playlists, List<Album> Shows, List<Track> Episodes);

/// <summary>The user's Spotify connection: sign-in, library, discovery, search, likes, playlists and devices.</summary>
public sealed class SpotifyAccount : ObservableObject, IDisposable
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public SpotifyAuth Auth { get; }
    public SpotifyApi Api { get; }
    public SpotifyCatalog Catalog { get; } = new();
    public SpotifyEngine Engine { get; }

    public SpotifyAccount(Dispatcher ui)
    {
        Auth = new SpotifyAuth(Http);
        Auth.Load();
        Api = new SpotifyApi(Http, Auth);
        Engine = new SpotifyEngine(Api, Auth, Catalog, ui);
    }

    // ---------------- state ----------------

    bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }

    bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    string _userName = "";
    public string UserName { get => _userName; private set => Set(ref _userName, value); }

    string _status = "Non connecté";
    public string Status { get => _status; set => Set(ref _status, value); }

    public string UserId { get; private set; } = "";
    public bool IsPremium { get; private set; }
    public string LikedContextUri => $"spotify:user:{UserId}:collection";

    public List<SpotifyPlaylist> Playlists { get; private set; } = [];
    public List<Album> SavedAlbums { get; private set; } = [];
    public List<Artist> Artists { get; private set; } = [];
    public List<Album> RecentAlbums { get; private set; } = [];
    public List<Track> LikedTracks { get; private set; } = [];
    /// <summary>Saved podcast shows (modelled as albums of episodes).</summary>
    public List<Album> Shows { get; private set; } = [];
    /// <summary>The user's most played artists over the last weeks (home shelf).</summary>
    public List<Artist> TopArtists { get; private set; } = [];
    /// <summary>Latest releases of the followed artists.</summary>
    public List<Album> NewReleases { get; private set; } = [];
    public List<SpotifyPlaylist> OwnedPlaylists => Playlists.Where(p => p.OwnerId == UserId).ToList();

    public event Action? LibraryChanged;

    /// <summary>A message for the user that is worth more than the status line (shown in a notification card).</summary>
    public event Action<string>? Notice;

    // ---------------- connection ----------------

    public async Task LoginAsync(string clientId, CancellationToken ct = default)
    {
        IsBusy = true;
        Status = "Connexion dans votre navigateur…";
        try
        {
            await Auth.LoginAsync(clientId, ct);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Validates the session, starts the Spotify Connect device and loads the library.</summary>
    public async Task ConnectAsync(IntPtr hwnd)
    {
        if (!Auth.IsSignedIn) return;
        IsBusy = true;
        try
        {
            // The profile only gives the account's name and plan (its id is also in the saved library). If Spotify
            // has put it out of reach for a while, everything else still answers: the player, the list of devices and
            // the remote control must not stay off for that, so carry on and ask for the profile again when it opens.
            bool profile = await TryLoadProfileAsync();
            IsConnected = true;

            await Engine.StartAsync(hwnd);
            await LoadLibraryAsync();
            if (!profile) _ = LoadProfileWhenAllowedAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Reads the profile. False when Spotify is limiting this app on it (any other failure is thrown).</summary>
    async Task<bool> TryLoadProfileAsync()
    {
        try
        {
            var me = await Api.GetAsync("me");
            UserId = Str(me, "id") ?? UserId;
            UserName = Str(me, "display_name") ?? UserId;
            IsPremium = Str(me, "product") == "premium";
            Status = IsPremium
                ? $"Connecté en tant que {UserName}"
                : $"Connecté en tant que {UserName} — Premium requis pour la lecture";
            return true;
        }
        catch (SpotifyException ex) when (ex.Status == 429)
        {
            Status = "Connecté, profil indisponible : " + ex.Message;
            Notice?.Invoke(ex.Message + " Le lecteur et la télécommande restent disponibles ; la bibliothèque se mettra à jour ensuite.");
            return false;
        }
    }

    /// <summary>Waits for the profile to be allowed again, then reads it.</summary>
    async Task LoadProfileWhenAllowedAsync()
    {
        if (Api.BlockedUntil("me") is not { } until) return;
        await Task.Delay(until - DateTime.UtcNow + TimeSpan.FromSeconds(5));
        if (!Auth.IsSignedIn) return;
        try { await TryLoadProfileAsync(); }
        catch { /* next start will try again */ }
    }

    public void Logout()
    {
        Auth.SignOut();
        IsConnected = false;
        UserName = "";
        UserId = "";
        Playlists = []; SavedAlbums = []; Artists = []; RecentAlbums = []; LikedTracks = [];
        Shows = []; TopArtists = []; NewReleases = [];
        Catalog.Liked.Clear();
        IsFromCache = false;
        Services.SpotifyCacheStore.Clear(); // signing out must not leave the account's library on disk
        Services.RemoteArtCache.Clear();
        Status = "Non connecté";
        LibraryChanged?.Invoke();
    }

    public async Task LoadLibraryAsync()
    {
        var liked = Api.GetAllAsync("me/tracks?limit=50");
        var playlists = Api.GetAllAsync("me/playlists?limit=50");
        var albums = Api.GetAllAsync("me/albums?limit=50");
        var artists = Api.GetAllAsync("me/following?type=artist&limit=50");
        var shows = Api.GetAllAsync("me/shows?limit=50");
        var top = Api.GetAllAsync("me/top/artists?limit=20&time_range=short_term", max: 20);
        var recent = Api.GetAsync("me/player/recently-played?limit=50");

        // Each list is optional: one refused endpoint must not hide the rest of the library. And a list that could
        // not be read (null) is not an empty one: it keeps what was there, from the saved library, instead of being
        // replaced by nothing and then saved over the cache (Spotify can refuse these for hours when it limits an app).
        bool complete = true;

        if (await Safe(liked) is { } likedItems)
        {
            Catalog.Liked.Clear();
            var likedTracks = new List<Track>();
            foreach (var it in likedItems)
                if (it.TryGetProperty("track", out var t) && Str(t, "uri") is { } uri)
                {
                    Catalog.Liked.Add(uri);
                    if (Catalog.MapTrack(t) is { } track) { track.IsLiked = true; likedTracks.Add(track); }
                }
            LikedTracks = likedTracks;
        }
        else complete = false;

        if (await Safe(playlists) is { } playlistItems) Playlists = playlistItems.Where(p => p.ValueKind == JsonValueKind.Object).Select(Catalog.MapPlaylist).ToList();
        else complete = false;
        if (await Safe(albums) is { } albumItems) SavedAlbums = albumItems.Where(a => a.TryGetProperty("album", out _)).Select(a => Catalog.MapAlbum(a.GetProperty("album"))).ToList();
        else complete = false;
        if (await Safe(artists) is { } artistItems) Artists = artistItems.Select(Catalog.MapArtist).ToList();
        else complete = false;
        if (await Safe(shows) is { } showItems) Shows = showItems.Where(s => s.TryGetProperty("show", out _)).Select(s => Catalog.MapAlbum(s.GetProperty("show"))).ToList();
        else complete = false;
        if (await Safe(top) is { } topItems) TopArtists = topItems.Select(Catalog.MapArtist).ToList();
        else complete = false;

        try
        {
            var r = await recent;
            var seen = new HashSet<string>();
            var list = new List<Album>();
            if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("items", out var items))
                foreach (var it in items.EnumerateArray())
                    if (it.TryGetProperty("track", out var t) && Catalog.MapTrack(t)?.AlbumRef is { } al && seen.Add(al.Key))
                        list.Add(al);
            RecentAlbums = list;
        }
        catch { complete = false; }

        IsFromCache = !complete; // what could not be refreshed is still the saved copy
        LibraryChanged?.Invoke();
        Services.SpotifyCacheStore.Save(BuildCache());
        _ = LoadNewReleasesAsync(); // many small requests: done after the library is shown
    }

    /// <summary>The list, or null when it could not be read.</summary>
    static async Task<List<JsonElement>?> Safe(Task<List<JsonElement>> task)
    {
        try { return await task; }
        catch { return null; }
    }

    // ---------------- cache ----------------

    /// <summary>What to write to disk so the next start can draw the library before the API answers.</summary>
    public Services.SpotifyCache BuildCache() => new()
    {
        SavedAtUtc = DateTime.UtcNow,
        UserId = UserId,
        Playlists = Playlists.Select(p => new Services.CachedItem
        {
            Uri = p.Uri, Name = p.Name, Secondary = p.Owner, Art = p.ArtPath, Count = p.TrackCount, OwnerId = p.OwnerId,
        }).ToList(),
        Albums = SavedAlbums.Select(CacheAlbum).ToList(),
        Shows = Shows.Select(CacheAlbum).ToList(),
        NewReleases = NewReleases.Select(CacheAlbum).ToList(),
        Artists = Artists.Select(CacheArtist).ToList(),
        TopArtists = TopArtists.Select(CacheArtist).ToList(),
        Liked = LikedTracks.Select(t => new Services.CachedTrack
        {
            Uri = t.Uri ?? "", Title = t.Title, Artist = t.Artist, ArtistUri = t.ArtistUri,
            Album = t.Album, AlbumUri = t.AlbumRef?.Uri, Art = t.AlbumRef?.ArtPath, Seconds = t.DurationSec,
        }).ToList(),
    };

    static Services.CachedItem CacheAlbum(Album a) => new()
    {
        Uri = a.Uri ?? "", Name = a.Title, Secondary = a.Artist, Art = a.ArtPath,
        Year = a.Year, ReleaseDate = a.ReleaseDate,
    };

    static Services.CachedItem CacheArtist(Artist a) => new() { Uri = a.Uri ?? "", Name = a.Name, Art = a.ArtPath };

    /// <summary>
    /// Fills the library from the cache so the window has something to draw at once. Everything here is
    /// replaced by <see cref="LoadLibraryAsync"/> as soon as Spotify answers.
    /// </summary>
    public void ApplyCache(Services.SpotifyCache cache)
    {
        if (cache.UserId.Length > 0) UserId = cache.UserId;
        Playlists = cache.Playlists.Where(p => p.Uri.Length > 0).Select(p => new SpotifyPlaylist
        {
            Id = p.Uri[(p.Uri.LastIndexOf(':') + 1)..],
            Uri = p.Uri,
            Name = p.Name,
            Owner = p.Secondary,
            OwnerId = p.OwnerId,
            TrackCount = p.Count,
            ArtPath = p.Art,
        }).ToList();
        SavedAlbums = cache.Albums.Select(RestoreAlbum).ToList();
        Shows = cache.Shows.Select(RestoreAlbum).ToList();
        NewReleases = cache.NewReleases.Select(RestoreAlbum).ToList();
        Artists = cache.Artists.Select(RestoreArtist).ToList();
        TopArtists = cache.TopArtists.Select(RestoreArtist).ToList();

        Catalog.Liked.Clear();
        LikedTracks = cache.Liked.Where(t => t.Uri.Length > 0).Select(t =>
        {
            Catalog.Liked.Add(t.Uri);
            Album? album = t.AlbumUri is { Length: > 0 } albumUri
                ? RestoreAlbum(new Services.CachedItem { Uri = albumUri, Name = t.Album, Secondary = t.Artist, Art = t.Art })
                : null;
            // Through the catalog, so the API refresh later hands back this same instance rather than a
            // second one — otherwise the row playing would stop matching the row listed.
            return Catalog.TrackFor(t.Uri, t.Title, t.Artist, t.ArtistUri, album, t.Seconds);
        }).ToList();

        IsFromCache = true;
        LibraryChanged?.Invoke();
    }

    /// <summary>True while the library shown comes from the cache rather than from Spotify.</summary>
    public bool IsFromCache { get; private set; }

    Album RestoreAlbum(Services.CachedItem item) =>
        Catalog.AlbumFor(item.Uri, item.Name, item.Secondary, item.Art, item.Year, item.ReleaseDate);

    Artist RestoreArtist(Services.CachedItem item)
    {
        var artist = Catalog.ArtistFor(item.Uri, item.Name);
        artist.ArtPath ??= item.Art;
        return artist;
    }

    // ---------------- discovery ----------------

    /// <summary>
    /// Latest albums and singles of the followed artists, newest first. (Spotify's general "new releases"
    /// listing is refused to personal developer apps, so this is built from the artists the user follows.)
    /// </summary>
    public async Task<List<Album>> LoadNewReleasesAsync()
    {
        var found = new Dictionary<string, Album>();
        var artists = Artists.Take(30).ToList();
        bool anyFailed = false;
        for (int i = 0; i < artists.Count; i += 5)
        {
            var batch = artists.Skip(i).Take(5)
                .Select(a => Safe(Api.GetAllAsync($"artists/{a.SpotifyId}/albums?include_groups=album,single&limit=4", max: 4)));
            foreach (var albums in await Task.WhenAll(batch))
            {
                if (albums == null) { anyFailed = true; continue; }
                foreach (var album in albums.Select(Catalog.MapAlbum))
                    if (album.Uri != null) found.TryAdd(album.Uri, album);
            }
        }
        if (found.Count == 0 && anyFailed) return NewReleases; // nothing could be read: keep the saved ones
        NewReleases = found.Values.Where(a => a.ReleaseDate.Length > 0)
            .OrderByDescending(a => a.ReleaseDate, StringComparer.Ordinal).Take(40).ToList();
        LibraryChanged?.Invoke();
        Services.SpotifyCacheStore.Save(BuildCache());
        return NewReleases;
    }

    public async Task<List<Track>> GetTopTracksAsync(string range) =>
        (await Api.GetAllAsync($"me/top/tracks?limit=50&time_range={range}", max: 50))
        .Select(t => Catalog.MapTrack(t)).OfType<Track>().ToList();

    public async Task<List<Artist>> GetTopArtistsAsync(string range) =>
        (await Api.GetAllAsync($"me/top/artists?limit=30&time_range={range}", max: 30)).Select(Catalog.MapArtist).ToList();

    /// <summary>
    /// Episodes saved for later ("Your episodes"), newest first. Each item carries its own show, so the
    /// episode can be shown with its programme's name and cover without a second request.
    /// </summary>
    public async Task<List<Track>> GetSavedEpisodesAsync()
    {
        var items = await Api.GetAllAsync("me/episodes?limit=50", max: 200);
        var episodes = new List<Track>();
        foreach (var item in items)
        {
            if (!item.TryGetProperty("episode", out var e) || e.ValueKind != JsonValueKind.Object) continue;
            var show = e.TryGetProperty("show", out var sh) && sh.ValueKind == JsonValueKind.Object
                ? Catalog.MapAlbum(sh)
                : Catalog.MapAlbum(e);
            if (Catalog.MapEpisode(e, show) is { } episode) episodes.Add(episode);
        }
        return episodes;
    }

    /// <summary>The 50 most recently played tracks, newest first (a track can appear several times).</summary>
    public async Task<List<Track>> GetHistoryAsync()
    {
        var r = await Api.GetAsync("me/player/recently-played?limit=50");
        var list = new List<Track>();
        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("items", out var items))
            foreach (var it in items.EnumerateArray())
                if (it.TryGetProperty("track", out var t) && Catalog.MapTrack(t) is { } track)
                    list.Add(track);
        return list;
    }

    // ---------------- catalog ----------------

    public async Task<PlaylistContent> LoadPlaylistTracksAsync(SpotifyPlaylist p)
    {
        List<JsonElement> items;
        try
        {
            items = await Api.GetAllAsync($"playlists/{p.Id}/items?limit=100");
        }
        catch (SpotifyException ex) when (ex.Status is 403 or 404)
        {
            // Older API revisions expose playlist contents as /tracks.
            try { items = await Api.GetAllAsync($"playlists/{p.Id}/tracks?limit=100"); }
            catch (SpotifyException) { throw new InvalidOperationException("Spotify ne permet pas aux applications tierces de lire le contenu de cette playlist (playlists éditoriales ou générées par Spotify)."); }
        }
        var tracks = new List<Track>();
        var added = new List<long>();
        foreach (var item in items)
        {
            if (Catalog.MapPlaylistItem(item) is not { } track) continue;
            tracks.Add(track);
            added.Add(SpotifyCatalog.AddedTicks(item));
        }
        return new PlaylistContent(tracks, added);
    }

    public async Task<List<Track>> LoadAlbumTracksAsync(Album album)
    {
        if (album.IsShow) return await LoadShowEpisodesAsync(album);
        var items = await Api.GetAllAsync($"albums/{album.SpotifyId}/tracks?limit=50");
        var tracks = items.Select(t => Catalog.MapTrack(t, album)).OfType<Track>().ToList();
        album.Tracks.Clear();
        album.Tracks.AddRange(tracks);
        return tracks;
    }

    /// <summary>Episodes of a podcast show, newest first (up to 200).</summary>
    public async Task<List<Track>> LoadShowEpisodesAsync(Album show)
    {
        var items = await Api.GetAllAsync($"shows/{show.SpotifyId}/episodes?limit=50", max: 200);
        var episodes = items.Where(e => e.ValueKind == JsonValueKind.Object).Select(e => Catalog.MapEpisode(e, show)).OfType<Track>().ToList();
        show.Tracks.Clear();
        show.Tracks.AddRange(episodes);
        return episodes;
    }

    public async Task<(List<Track>, List<Album>)> LoadArtistAsync(Artist artist)
    {
        var id = artist.SpotifyId;
        // Spotify refuses "top tracks" to personal developer apps: the artist's tracks come from search instead.
        var tracksTask = Api.GetAsync($"search?q={Uri.EscapeDataString($"artist:\"{artist.Name}\"")}&type=track&limit=10");
        var albumsTask = Api.GetAllAsync($"artists/{id}/albums?include_groups=album,single&limit=10", max: 60);
        var tracks = new List<Track>();
        try
        {
            var found = await tracksTask;
            if (found.ValueKind == JsonValueKind.Object && found.TryGetProperty("tracks", out var tr) && tr.TryGetProperty("items", out var arr))
                tracks.AddRange(arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
                    .Select(t => Catalog.MapTrack(t)).OfType<Track>()
                    .Where(t => t.ArtistUri == artist.Uri || t.Artist.Contains(artist.Name, StringComparison.CurrentCultureIgnoreCase)));
        }
        catch { /* tracks are optional on the artist page */ }
        var albums = (await Safe(albumsTask) ?? []).Select(Catalog.MapAlbum)
            .GroupBy(a => a.Key).Select(g => g.First()).ToList();
        return (tracks, albums);
    }

    public async Task<SpotifySearchResult> SearchAsync(string query, CancellationToken ct)
    {
        var r = await Api.GetAsync($"search?q={Uri.EscapeDataString(query)}&type=track,artist,album,playlist,show,episode&limit=10", ct);
        List<JsonElement> Items(string key) =>
            r.ValueKind == JsonValueKind.Object && r.TryGetProperty(key, out var o) && o.TryGetProperty("items", out var items)
                ? items.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList()
                : [];
        return new SpotifySearchResult(
            Items("tracks").Select(t => Catalog.MapTrack(t)).OfType<Track>().ToList(),
            Items("albums").Select(Catalog.MapAlbum).ToList(),
            Items("artists").Select(Catalog.MapArtist).ToList(),
            Items("playlists").Select(Catalog.MapPlaylist).ToList(),
            Items("shows").Select(Catalog.MapAlbum).ToList(),
            Items("episodes").Select(Catalog.MapSearchEpisode).OfType<Track>().ToList());
    }

    // ---------------- library (save / follow) ----------------

    /// <summary>True when the album, show, artist or playlist is in the user's library (from the loaded lists).</summary>
    public bool IsSaved(string? uri) => uri != null &&
        (SavedAlbums.Any(a => a.Uri == uri) || Shows.Any(a => a.Uri == uri) || Artists.Any(a => a.Uri == uri) || Playlists.Any(p => p.Uri == uri));

    /// <summary>
    /// Saves or removes an item through the unified library endpoint, falling back to the older per-type
    /// endpoints when Spotify refuses it.
    /// </summary>
    async Task SetSavedAsync(string uri, bool save)
    {
        var method = save ? HttpMethod.Put : HttpMethod.Delete;
        try
        {
            await Api.SendAsync(method, $"me/library?uris={Uri.EscapeDataString(uri)}");
        }
        catch (SpotifyException ex) when (ex.Status is 400 or 403 or 404)
        {
            var legacy = LegacyPath(uri) ?? throw new InvalidOperationException(ex.Message);
            try { await Api.SendAsync(method, legacy); }
            catch (SpotifyException)
            {
                throw new InvalidOperationException("Spotify a refusé cette action. Déconnectez-vous puis reconnectez-vous dans les paramètres pour accorder les nouvelles autorisations.");
            }
        }
    }

    static string? LegacyPath(string uri)
    {
        var parts = uri.Split(':');
        if (parts.Length < 3) return null;
        var id = parts[^1];
        return parts[1] switch
        {
            "track" => $"me/tracks?ids={id}",
            "album" => $"me/albums?ids={id}",
            "artist" => $"me/following?type=artist&ids={id}",
            "show" => $"me/shows?ids={id}",
            "episode" => $"me/episodes?ids={id}",
            "playlist" => $"playlists/{id}/followers",
            _ => null,
        };
    }

    public async Task SetLikedAsync(Track t, bool liked)
    {
        await SetSavedAsync(t.Uri!, liked);
        t.IsLiked = liked;
        if (liked)
        {
            Catalog.Liked.Add(t.Uri!);
            if (!LikedTracks.Contains(t)) LikedTracks = [t, .. LikedTracks];
        }
        else
        {
            Catalog.Liked.Remove(t.Uri!);
            LikedTracks = LikedTracks.Where(x => x != t).ToList();
        }
        LibraryChanged?.Invoke();
    }

    /// <summary>Saves or removes an album or a podcast show.</summary>
    public async Task SetAlbumSavedAsync(Album album, bool save)
    {
        if (album.Uri == null) return;
        await SetSavedAsync(album.Uri, save);
        if (album.IsShow) Shows = save ? [album, .. Shows.Where(a => a != album)] : Shows.Where(a => a != album).ToList();
        else SavedAlbums = save ? [album, .. SavedAlbums.Where(a => a != album)] : SavedAlbums.Where(a => a != album).ToList();
        LibraryChanged?.Invoke();
    }

    public async Task SetArtistFollowedAsync(Artist artist, bool follow)
    {
        if (artist.Uri == null) return;
        await SetSavedAsync(artist.Uri, follow);
        Artists = follow ? [artist, .. Artists.Where(a => a != artist)] : Artists.Where(a => a != artist).ToList();
        LibraryChanged?.Invoke();
    }

    public async Task SetPlaylistFollowedAsync(SpotifyPlaylist playlist, bool follow)
    {
        await SetSavedAsync(playlist.Uri, follow);
        Playlists = follow ? [playlist, .. Playlists.Where(p => p.Id != playlist.Id)] : Playlists.Where(p => p.Id != playlist.Id).ToList();
        LibraryChanged?.Invoke();
    }

    // ---------------- playlists ----------------

    /// <summary>Runs a playlist write on /items, then on the older /tracks path when Spotify refuses the first.</summary>
    async Task PlaylistWriteAsync(HttpMethod method, string playlistId, object body)
    {
        try { await Api.SendAsync(method, $"playlists/{playlistId}/items", body); }
        catch (SpotifyException ex) when (ex.Status is 403 or 404 or 405)
        {
            await Api.SendAsync(method, $"playlists/{playlistId}/tracks", body);
        }
    }

    public async Task AddToPlaylistAsync(SpotifyPlaylist p, IEnumerable<Track> tracks)
    {
        var uris = tracks.Where(t => t.IsSpotify).Select(t => t.Uri!).ToList();
        for (int i = 0; i < uris.Count; i += 100)
            await PlaylistWriteAsync(HttpMethod.Post, p.Id, new { uris = uris.Skip(i).Take(100).ToList() });
    }

    public async Task RemoveFromPlaylistAsync(SpotifyPlaylist p, IEnumerable<Track> tracks)
    {
        var refs = tracks.Where(t => t.IsSpotify).Select(t => new { uri = t.Uri! }).ToList();
        for (int i = 0; i < refs.Count; i += 100)
        {
            var chunk = refs.Skip(i).Take(100).ToList();
            // Both spellings are sent: the newer API reads "items", the older one "tracks".
            await PlaylistWriteAsync(HttpMethod.Delete, p.Id, new { items = chunk, tracks = chunk });
        }
    }

    /// <summary>
    /// Moves <paramref name="count"/> items starting at <paramref name="from"/> so they land before
    /// <paramref name="insertBefore"/> (both are positions in the playlist as Spotify stores it).
    /// </summary>
    public Task ReorderPlaylistAsync(SpotifyPlaylist p, int from, int insertBefore, int count = 1) =>
        PlaylistWriteAsync(HttpMethod.Put, p.Id, new
        {
            range_start = from,
            insert_before = insertBefore,
            range_length = Math.Max(1, count),
        });

    /// <summary>Name, description, public/private and collaborative, in one call. Nulls are left alone.</summary>
    public async Task SetPlaylistDetailsAsync(SpotifyPlaylist p, string? name = null, string? description = null,
        bool? isPublic = null, bool? collaborative = null)
    {
        var body = new Dictionary<string, object>();
        if (name != null) body["name"] = name;
        if (description != null) body["description"] = description;
        if (isPublic != null) body["public"] = isPublic.Value;
        // Spotify only accepts a collaborative playlist that is private, so the two travel together.
        if (collaborative != null)
        {
            body["collaborative"] = collaborative.Value;
            if (collaborative.Value) body["public"] = false;
        }
        if (body.Count == 0) return;
        await Api.SendAsync(HttpMethod.Put, $"playlists/{p.Id}", body);
        if (name != null) p.Name = name;
        if (body.TryGetValue("public", out var newPublic)) p.IsPublic = (bool)newPublic;
        if (collaborative != null) p.IsCollaborative = collaborative.Value;
        LibraryChanged?.Invoke();
    }

    /// <summary>
    /// Replaces a playlist's cover. Spotify wants the JPEG base64-encoded in the body, not a multipart
    /// upload, with the content type still image/jpeg.
    /// </summary>
    public async Task SetPlaylistCoverAsync(SpotifyPlaylist p, byte[] jpeg)
    {
        var base64 = Convert.ToBase64String(jpeg);
        await Api.SendContentAsync(HttpMethod.Put, $"playlists/{p.Id}/images", () =>
        {
            var content = new StringContent(base64);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return content;
        });
    }

    public async Task RenamePlaylistAsync(SpotifyPlaylist p, string name)
    {
        await Api.SendAsync(HttpMethod.Put, $"playlists/{p.Id}", new { name });
        p.Name = name;
        LibraryChanged?.Invoke();
    }

    public async Task<SpotifyPlaylist> CreatePlaylistAsync(string name)
    {
        var body = new { name, @public = false, description = "Créée avec Sonora" };
        JsonElement? json;
        try { json = await Api.SendAsync(HttpMethod.Post, "me/playlists", body); }
        catch (SpotifyException ex) when (ex.Status is 403 or 404 or 405)
        {
            json = await Api.SendAsync(HttpMethod.Post, $"users/{Uri.EscapeDataString(UserId)}/playlists", body);
        }
        var p = Catalog.MapPlaylist(json ?? default);
        Playlists = [p, .. Playlists];
        LibraryChanged?.Invoke();
        return p;
    }

    public void Dispose() => Engine.Dispose();
}
