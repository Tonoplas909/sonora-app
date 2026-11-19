using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sonora.Appearance.Content;
using Sonora.Appearance.Features;
using Sonora.Core;
using Sonora.Models;
using Sonora.Views;
using Sonora.Appearance.Localization;

namespace Sonora.ViewModels;

public abstract class PageViewModel(MainViewModel main) : ObservableObject
{
    public MainViewModel Main { get; } = main;
    public abstract string Title { get; }
}

public sealed class HomePage : PageViewModel
{
    public HomePage(MainViewModel main) : base(main)
    {
        var lib = main.Library;
        var sp = main.Spotify;
        Recent = sp.RecentAlbums.Concat(main.State.RecentAlbums.Select(lib.GetAlbum).OfType<Album>()).Take(10).ToList();
        Playlists = sp.Playlists.Take(10).ToList();
        SavedAlbums = sp.SavedAlbums.Take(10).ToList();
        Rediscover = lib.Albums.OrderBy(_ => Random.Shared.Next()).Take(10).ToList();
        Artists = sp.Artists.Concat(lib.Artists.OrderBy(_ => Random.Shared.Next())).Take(10).ToList();
        var f = main.State.Features;
        NewReleases = f.NewReleases ? sp.NewReleases.Take(10).ToList() : [];
        TopArtists = f.Tops ? sp.TopArtists.Take(10).ToList() : [];
        Shows = f.Podcasts ? sp.Shows.Take(10).ToList() : [];
        var h = DateTime.Now.Hour;
        Greeting = Loc.T(h is >= 5 and < 12 ? "greeting.morning" : h is >= 12 and < 18 ? "greeting.afternoon" : "greeting.evening");

        // Shelves in the user's order; hidden or empty ones are left out.
        Sections = [];
        foreach (var s in main.State.Content.HomeSections.Where(s => s.Visible))
        {
            HomeSectionView? v = s.Id switch
            {
                HomeSectionIds.Recent => new(s.Id, Loc.T("home.recently_played"), Recent, "albums", null),
                HomeSectionIds.Playlists => new(s.Id, Loc.T("home.your_playlists"), Playlists, "playlists", main.OpenPlaylistsCommand),
                HomeSectionIds.SavedAlbums => new(s.Id, Loc.T("home.your_albums"), SavedAlbums, "albums", main.OpenAlbumsCommand),
                HomeSectionIds.Rediscover => new(s.Id, Loc.T("home.rediscover"), Rediscover, "albums", main.OpenAlbumsCommand),
                HomeSectionIds.Artists => new(s.Id, Loc.T("pages.your_artists"), Artists, "artists", main.OpenArtistsCommand),
                HomeSectionIds.NewReleases => new(s.Id, Loc.T("pages.new_releases_from_your_artists"), NewReleases, "albums",
                    new RelayCommand(main.OpenNewReleases)),
                HomeSectionIds.TopArtists => new(s.Id, Loc.T("pages.your_most_played_artists"), TopArtists, "artists",
                    new RelayCommand(main.OpenTops)),
                HomeSectionIds.Shows => new(s.Id, Loc.T("home.your_podcasts"), Shows, "albums", new RelayCommand(main.OpenShows)),
                _ => null,
            };
            if (v != null && v.Items.Cast<object>().Any()) Sections.Add(v);
        }
    }

    public override string Title => "Accueil";
    public string Greeting { get; }
    public List<HomeSectionView> Sections { get; }
    public List<Album> Recent { get; }
    public List<SpotifyPlaylist> Playlists { get; }
    public List<Album> SavedAlbums { get; }
    public List<Album> Rediscover { get; }
    public List<Artist> Artists { get; }
    public List<Album> NewReleases { get; }
    public List<Artist> TopArtists { get; }
    public List<Album> Shows { get; }
    public bool HasRecent => Recent.Count > 0;
    public bool HasPlaylists => Playlists.Count > 0;
    public bool HasSavedAlbums => SavedAlbums.Count > 0;
    public bool HasRediscover => Rediscover.Count > 0;
    public bool HasArtists => Artists.Count > 0;
    public bool IsEmpty => Main.Library.Tracks.Count == 0 && !Main.Spotify.IsConnected;
}

/// <summary>One entry of a track list's sort selector.</summary>
public sealed record SortOption(string Key)
{
    public string Label => Loc.T("sort." + Key);
}

/// <summary>Album, playlist, liked songs, genre or "all tracks" view (local or Spotify).</summary>
public sealed class TracksPage : PageViewModel
{
    /// <summary>Sort choices offered by every track list, in <see cref="ListQuery.SortKeys"/> order.</summary>
    public static readonly List<SortOption> Sorts =
    [
        new("default"),
        new("title"),
        new("artist"),
        new("album"),
        new("duration"),
    ];

    static readonly SortOption AddedSort = new("added");

    readonly Func<Task<PlaylistContent>> _load;
    readonly string _subtitlePrefix;
    List<Track> _all = [];
    List<long>? _added; // when each of _all was added, where the source says; null: the order of the list is the order of addition
    int _loadVersion;

    public TracksPage(MainViewModel main, string kind, string header, object? artSource, string glyph,
        string subtitlePrefix, Func<IEnumerable<Track>> load) : this(main, kind, header, artSource, glyph, subtitlePrefix,
        () => Task.FromResult(load().ToList()))
    { }

    public TracksPage(MainViewModel main, string kind, string header, object? artSource, string glyph,
        string subtitlePrefix, Func<Task<List<Track>>> loadAsync) : this(main, kind, header, artSource, glyph, subtitlePrefix,
        async () => new PlaylistContent(await loadAsync()))
    { }

    public TracksPage(MainViewModel main, string kind, string header, object? artSource, string glyph,
        string subtitlePrefix, Func<Task<PlaylistContent>> loadAsync) : base(main)
    {
        Kind = kind;
        _header = header;
        ArtSource = artSource;
        Glyph = glyph;
        _subtitlePrefix = subtitlePrefix;
        _load = loadAsync;
        // A filtered or re-sorted list plays what is shown, so the Spotify context is dropped while it is.
        PlayCommand = new RelayCommand(() => Main.PlayTracks(Tracks, 0, Header, contextUri: PlaybackContextUri));
        ShuffleCommand = new RelayCommand(() => Main.PlayTracks(Tracks, -1, Header, shuffle: true, contextUri: PlaybackContextUri));
        ToggleSortDirectionCommand = new RelayCommand(() => SortDescending = !SortDescending);
        ClearFilterCommand = new RelayCommand(() => Filter = "");
        _ = Reload();
    }

    /// <summary>What sort of page this is. An identifier, also compared in code: the words shown are in <see cref="KindLabel"/>.</summary>
    public string Kind { get; }

    public string KindLabel => Kind switch
    {
        "Playlist" => Loc.T("kind.playlist"),
        "Album" => Loc.T("kind.album"),
        "Bibliothèque" => Loc.T("kind.library"),
        "Genre" => Loc.T("kind.genre"),
        "Podcast" => Loc.T("kind.podcast"),
        "Découvrir" => Loc.T("common.discover"),
        _ => Kind,
    };

    public string Glyph { get; }
    public object? ArtSource { get; }
    public bool IsSpecial { get; init; }
    public Playlist? Playlist { get; init; }
    public SpotifyPlaylist? SpotifyPlaylist { get; init; }
    /// <summary>Spotify context (album / playlist / collection URI) used to start playback.</summary>
    public string? ContextUri { get; init; }
    public bool IsPlaylist => Playlist != null;

    string _header;
    public string Header { get => _header; set { if (Set(ref _header, value)) OnPropertyChanged(nameof(Title)); } }
    public override string Title => Header;

    List<TrackRow> _rows = [];
    public List<TrackRow> Rows { get => _rows; private set => Set(ref _rows, value); }

    string _subtitle = "";
    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

    bool _isLoading;
    public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) OnPropertyChanged(nameof(IsEmpty)); } }

    string? _error;
    public string? Error { get => _error; private set { if (Set(ref _error, value)) { OnPropertyChanged(nameof(EmptyText)); OnPropertyChanged(nameof(IsEmpty)); } } }

    public bool IsEmpty => !_isLoading && _rows.Count == 0;
    public string EmptyText => _error ?? (IsFiltered
        ? Loc.T("tracks.empty_filtered")
        : IsPlaylist || IsSpecial
            ? Loc.T("tracks.empty_playlist")
            : Loc.T("tracks.empty"));

    public List<Track> Tracks => _rows.Select(r => r.Track).ToList();

    public ICommand PlayCommand { get; }
    public ICommand ShuffleCommand { get; }
    public ICommand ToggleSortDirectionCommand { get; }
    public ICommand ClearFilterCommand { get; }

    // ---------------- filter & sort (shared logic in ListQuery) ----------------

    string _filter = "";
    /// <summary>Free-text filter over title, artist and album; every word must match.</summary>
    public string Filter
    {
        get => _filter;
        set { if (Set(ref _filter, value ?? "")) { OnPropertyChanged(nameof(IsFiltered)); Apply(); } }
    }

    public bool IsFiltered => _filter.Trim().Length > 0;

    /// <summary>"Date d'ajout" only makes sense in a playlist, where entries are added over time.</summary>
    public List<SortOption> SortOptions => IsPlaylist || SpotifyPlaylist != null ? [.. Sorts, AddedSort] : Sorts;

    bool _remembersSort;

    /// <summary>
    /// Playlist screens share one sort order for the whole app: this one starts with the order last chosen on
    /// any of them, and what is chosen here is what the next one starts with.
    /// </summary>
    public TracksPage RememberSort()
    {
        _remembersSort = false; // applying the saved order must not save it again
        var saved = SortOptions.FirstOrDefault(o => o.Key == Main.State.PlaylistSort) ?? Sorts[0];
        _sort = saved;
        _sortDescending = Main.State.PlaylistSortDescending;
        _remembersSort = true;
        OnPropertyChanged(nameof(SelectedSort));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(IsSorted));
        OnPropertyChanged(nameof(SortDirectionGlyph));
        Apply();
        return this;
    }

    void SortChanged()
    {
        OnPropertyChanged(nameof(IsSorted));
        OnPropertyChanged(nameof(SortDirectionGlyph));
        Apply();
        if (!_remembersSort) return;
        Main.State.PlaylistSort = _sort.Key;
        Main.State.PlaylistSortDescending = _sortDescending;
        Main.ScheduleSave();
    }

    SortOption _sort = Sorts[0];
    public SortOption SelectedSort
    {
        get => _sort;
        set { if (Set(ref _sort, value ?? Sorts[0])) SortChanged(); }
    }

    bool _sortDescending;
    public bool SortDescending
    {
        get => _sortDescending;
        set { if (Set(ref _sortDescending, value)) SortChanged(); }
    }

    /// <summary>Picks a sort key from the menu. The most recent addition comes first, which is what "by date added" means to everyone.</summary>
    public void ChooseSort(SortOption option)
    {
        if (option.Key == _sort.Key) return;
        bool added = option.Key == AddedSort.Key;
        if (added != (_sort.Key == AddedSort.Key)) _sortDescending = added;
        SelectedSort = option;
        OnPropertyChanged(nameof(SortDescending));
    }

    public bool IsSorted => _sort.Key != "default";
    public string SortDirectionGlyph => _sortDescending ? "" : "";

    /// <summary>Null once the list is filtered or re-sorted: playback then follows what is shown.</summary>
    public string? PlaybackContextUri => IsFiltered || IsSorted ? null : ContextUri;

    void Apply()
    {
        // Sorted by position, so the same track twice in a list stays two rows with their own date.
        var shown = ListQuery.Apply(Enumerable.Range(0, _all.Count).ToList(), _filter, _sort.Key, _sortDescending,
            i => _all[i].Title, i => _all[i].Artist, i => _all[i].Album, i => _all[i].DurationSec,
            i => _added != null && i < _added.Count ? _added[i] : i);
        Rows = shown.Select((i, n) => new TrackRow(n + 1, _all[i])).ToList();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    public async Task Reload()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var content = await _load();
            if (version != _loadVersion) return;
            var tracks = _all = content.Tracks;
            _added = content.Added;
            Subtitle = $"{_subtitlePrefix}{Loc.Count("count.tracks", tracks.Count)}, {Fmt.Total(tracks)}";
            Error = null;
            Apply();
        }
        catch (Exception ex)
        {
            if (version == _loadVersion) Error = ex.Message;
        }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}

public sealed class ArtistPage : PageViewModel
{
    const int TopCount = 10;
    List<Track> _all = [];

    public ArtistPage(MainViewModel main, Artist artist, Func<Task<(List<Track> Tracks, List<Album> Albums)>>? loadAsync = null) : base(main)
    {
        Artist = artist;
        PlayCommand = new RelayCommand(() =>
        {
            if (artist.Uri != null) Main.Player.PlaySpotifyContext(artist.Uri, artist.Name);
            else Main.PlayTracks(_all, 0, artist.Name);
        });
        ShuffleCommand = new RelayCommand(() => Main.PlayTracks(_all, -1, artist.Name, shuffle: true));
        ShowMoreCommand = new RelayCommand(() =>
        {
            ShowingAll = !ShowingAll;
            TopRows = Rows(ShowingAll ? int.MaxValue : TopCount);
        });

        if (loadAsync == null) Apply(artist.Tracks, artist.Albums);
        else _ = LoadAsync(loadAsync);
    }

    async Task LoadAsync(Func<Task<(List<Track>, List<Album>)>> load)
    {
        IsLoading = true;
        try
        {
            var (tracks, albums) = await load();
            Apply(tracks, albums);
        }
        catch (Exception ex) { Main.ReportError(ex.Message); }
        finally { IsLoading = false; }
    }

    void Apply(List<Track> tracks, List<Album> albums)
    {
        _all = tracks;
        Albums = albums;
        TopRows = Rows(TopCount);
        OnPropertyChanged(nameof(Albums));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasMore));
    }

    List<TrackRow> Rows(int max) => _all.Take(max).Select((t, i) => new TrackRow(i + 1, t)).ToList();

    public Artist Artist { get; }
    public override string Title => Artist.Name;
    public string Subtitle => Artist.Uri != null
        ? Loc.T("artist.spotify_artist")
        : $"{Loc.Count("count.tracks", _all.Count)} • {Loc.Count("count.albums", Albums.Count)}";

    public List<Album> Albums { get; private set; } = [];

    List<TrackRow> _rows = [];
    public List<TrackRow> TopRows { get => _rows; private set => Set(ref _rows, value); }

    bool _isLoading;
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    bool _showingAll;
    public bool ShowingAll
    {
        get => _showingAll;
        private set { if (Set(ref _showingAll, value)) OnPropertyChanged(nameof(ShowMoreText)); }
    }

    public bool HasMore => _all.Count > TopCount;
    public string ShowMoreText => Loc.T(ShowingAll ? "common.show_less" : "common.show_more");

    public ICommand PlayCommand { get; }
    public ICommand ShuffleCommand { get; }
    public ICommand ShowMoreCommand { get; }
}

public sealed record GenreTile(Genre Genre, Brush Brush);

/// <summary>The search page's "top result" card.</summary>
public sealed record TopResultView(string Name, string Kind, object? ArtSource, bool IsRound, ICommand Open);

public sealed class SearchPage : PageViewModel
{
    static readonly string[] Palette =
        ["#E13300", "#7358FF", "#1E3264", "#E8115B", "#148A08", "#BC5900", "#8D67AB", "#509BF5", "#AF2896", "#27856A", "#E91429", "#477D95"];

    readonly DispatcherTimer _debounce;
    CancellationTokenSource? _cts;

    public SearchPage(MainViewModel main, string query) : base(main)
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Run(); };
        Genres = main.Library.Genres.Take(24).Select((g, i) =>
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Palette[i % Palette.Length]));
            b.Freeze();
            return new GenreTile(g, b);
        }).ToList();
        _query = query;
        UseRecentCommand = new RelayCommand(p => { if (p is string s) Query = s; });
        ClearRecentCommand = new RelayCommand(() =>
        {
            Features.RecentSearches.Clear();
            Main.ScheduleSave();
            OnPropertyChanged(nameof(RecentSearches));
            OnPropertyChanged(nameof(HasRecentSearches));
        });
        Run();
    }

    FeatureSettings Features => Main.State.Features;

    public override string Title => "Rechercher";
    public List<GenreTile> Genres { get; }
    public bool NoGenres => Genres.Count == 0;

    public ICommand UseRecentCommand { get; }
    public ICommand ClearRecentCommand { get; }

    /// <summary>The last ten searches, newest first (can be turned off in the settings).</summary>
    public List<string> RecentSearches => Features.RecentSearches.ToList();
    public bool HasRecentSearches => Features.KeepRecentSearches && Features.RecentSearches.Count > 0;

    string _query;
    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value)) { _debounce.Stop(); _debounce.Start(); } }
    }

    string _filter = "all";
    /// <summary>Type filter: all, tracks, artists, albums, playlists or shows.</summary>
    public string TypeFilter
    {
        get => _filter;
        set { if (Set(ref _filter, string.IsNullOrEmpty(value) ? "all" : value)) Notify(); }
    }

    /// <summary>True when the type filter lets a section of this kind through.</summary>
    bool Allows(string kind) => _filter == "all" || _filter == kind;

    public List<TrackRow> Tracks { get; private set; } = [];
    public List<TrackRow> LocalTracks { get; private set; } = [];
    public List<Album> Albums { get; private set; } = [];
    public List<Artist> Artists { get; private set; } = [];
    public List<SpotifyPlaylist> Playlists { get; private set; } = [];
    public List<Album> Shows { get; private set; } = [];
    public List<TrackRow> Episodes { get; private set; } = [];

    /// <summary>The single result worth putting above the rest, or null when nothing stands out.</summary>
    public TopResultView? TopResult { get; private set; }
    public bool HasTopResult => TopResult != null && _filter == "all";

    public bool HasQuery => !string.IsNullOrWhiteSpace(_query);
    public bool HasTracks => Allows("tracks") && Tracks.Count > 0;
    public bool HasLocalTracks => Allows("tracks") && LocalTracks.Count > 0;
    public bool HasAlbums => Allows("albums") && Albums.Count > 0;
    public bool HasArtists => Allows("artists") && Artists.Count > 0;
    public bool HasPlaylists => Allows("playlists") && Playlists.Count > 0;
    public bool HasShows => Allows("shows") && Features.Podcasts && Shows.Count > 0;
    public bool HasEpisodes => Allows("shows") && Features.Podcasts && Episodes.Count > 0;
    public bool NoResults => HasQuery && !_searching && !HasTracks && !HasLocalTracks && !HasAlbums && !HasArtists
                             && !HasPlaylists && !HasShows && !HasEpisodes;

    bool _searching;
    public bool IsSearching { get => _searching; private set { if (Set(ref _searching, value)) OnPropertyChanged(nameof(NoResults)); } }

    public void Run()
    {
        _cts?.Cancel();
        var tokens = Fmt.Fold(_query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<Album> localAlbums = [];
        List<Artist> localArtists = [];
        if (tokens.Length == 0)
        {
            Tracks = []; LocalTracks = []; Albums = []; Artists = []; Playlists = []; Shows = []; Episodes = [];
            TopResult = null;
        }
        else
        {
            bool Match(string key) => tokens.All(tok => key.Contains(tok, StringComparison.Ordinal));
            var lib = Main.Library;
            var first = tokens[0];
            LocalTracks = lib.Tracks.Where(t => Match(t.SearchKey))
                .OrderByDescending(t => Fmt.Fold(t.Title).StartsWith(first, StringComparison.Ordinal))
                .Take(8).Select((t, i) => new TrackRow(i + 1, t)).ToList();
            localAlbums = lib.Albums.Where(a => Match(Fmt.Fold(a.Title + " " + a.Artist))).Take(10).ToList();
            localArtists = lib.Artists.Where(a => Match(Fmt.Fold(a.Name))).Take(10).ToList();
            Albums = localAlbums;
            Artists = localArtists;
            if (!Main.Spotify.IsConnected) { Tracks = []; Playlists = []; Shows = []; Episodes = []; }
            PickTopResult();
        }
        Notify();

        if (tokens.Length > 0 && Main.Spotify.IsConnected)
        {
            var cts = _cts = new CancellationTokenSource();
            _ = SearchSpotifyAsync((_query ?? "").Trim(), localAlbums, localArtists, cts.Token);
        }
    }

    async Task SearchSpotifyAsync(string q, List<Album> localAlbums, List<Artist> localArtists, CancellationToken ct)
    {
        IsSearching = true;
        try
        {
            var r = await Main.Spotify.SearchAsync(q, ct);
            if (ct.IsCancellationRequested) return;
            Tracks = r.Tracks.Select((t, i) => new TrackRow(i + 1, t)).ToList();
            Artists = r.Artists.Concat(localArtists).ToList();
            Albums = r.Albums.Concat(localAlbums).ToList();
            Playlists = r.Playlists;
            Shows = r.Shows;
            Episodes = r.Episodes.Select((t, i) => new TrackRow(i + 1, t)).ToList();
            PickTopResult();
            // Only a search that actually answered is worth remembering.
            Features.AddRecentSearch(q);
            Main.ScheduleSave();
            Notify();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Main.ReportError(ex.Message); }
        finally { if (!ct.IsCancellationRequested) IsSearching = false; }
    }

    /// <summary>
    /// Scores every result against what was typed and keeps the best, the way Spotify's "top result" does.
    /// Spotify computes its own server-side and does not expose it, so this is Sonora's reading of it.
    /// </summary>
    void PickTopResult()
    {
        var candidates = new List<RankedResult>();
        candidates.AddRange(Artists.Select(a => new RankedResult(ResultKind.Artist, a.Name, a)));
        candidates.AddRange(Albums.Select(a => new RankedResult(ResultKind.Album, a.Title, a)));
        candidates.AddRange(Tracks.Select(r => new RankedResult(ResultKind.Track, r.Track.Title, r.Track)));
        candidates.AddRange(LocalTracks.Select(r => new RankedResult(ResultKind.Track, r.Track.Title, r.Track)));
        candidates.AddRange(Playlists.Select(p => new RankedResult(ResultKind.Playlist, p.Name, p)));
        candidates.AddRange(Shows.Select(a => new RankedResult(ResultKind.Show, a.Title, a)));

        var best = SearchRank.Best(_query, candidates);
        TopResult = best == null ? null : new TopResultView(
            best.Name,
            SearchRank.KindLabel(best.Kind),
            best.Item,
            best.Kind == ResultKind.Artist,
            new RelayCommand(() => Open(best.Item)));
    }

    void Open(object item)
    {
        switch (item)
        {
            case Artist a: Main.OpenArtist(a); break;
            case Album a: Main.OpenAlbum(a); break;
            case SpotifyPlaylist p: Main.OpenSpotifyPlaylist(p); break;
            case Track t: Main.PlayTracks([t], 0, t.Title); break;
        }
    }

    void Notify()
    {
        foreach (var p in new[] { nameof(Tracks), nameof(LocalTracks), nameof(Albums), nameof(Artists), nameof(Playlists),
                     nameof(Shows), nameof(Episodes), nameof(HasQuery), nameof(HasTracks), nameof(HasLocalTracks),
                     nameof(HasAlbums), nameof(HasArtists), nameof(HasPlaylists), nameof(HasShows), nameof(HasEpisodes),
                     nameof(NoResults), nameof(TopResult), nameof(HasTopResult),
                     nameof(RecentSearches), nameof(HasRecentSearches) })
            OnPropertyChanged(p);
    }
}

/// <summary>One tile of the Discover page.</summary>
public sealed record DiscoverTile(string Title, string Subtitle, string Glyph, ICommand Open);

/// <summary>Entry point for everything Spotify's API still allows: your tops, your history, new releases, podcasts.</summary>
public sealed class DiscoverPage : PageViewModel
{
    public DiscoverPage(MainViewModel main) : base(main)
    {
        var f = main.State.Features;
        Func<bool> spotify = () => main.Spotify.IsConnected; // these tiles need the account: they are greyed out without it
        Tiles = [];
        if (f.Tops)
            Tiles.Add(new DiscoverTile(Loc.T("pages.your_tops"), Loc.T("discover.tops_desc"), "",
                new RelayCommand(main.OpenTops, spotify)));
        if (f.History)
            Tiles.Add(new DiscoverTile(Loc.T("discover.history"), Loc.T("discover.history_desc"), "",
                new RelayCommand(main.OpenHistory, spotify)));
        if (f.NewReleases)
            Tiles.Add(new DiscoverTile(Loc.T("discover.new_releases"), Loc.T("discover.new_releases_desc"), "",
                new RelayCommand(main.OpenNewReleases, spotify)));
        if (f.Podcasts)
            Tiles.Add(new DiscoverTile(Loc.T("pages.podcasts"), Loc.T("discover.shows_desc"), "",
                new RelayCommand(main.OpenShows, spotify)));
        if (f.Podcasts)
            Tiles.Add(new DiscoverTile(Loc.T("discover.saved_episodes"), Loc.T("discover.episodes_desc"), "",
                new RelayCommand(main.OpenSavedEpisodes, spotify)));
        if (f.ListeningLog) // Sonora's own log: no account needed
            Tiles.Add(new DiscoverTile(Loc.T("pages.your_statistics"), Loc.T("discover.stats_desc"), "",
                new RelayCommand(main.OpenStats)));
        if (f.Discovery && main.Spotify.TopArtists.Count > 0)
        {
            var seed = main.Spotify.TopArtists[0];
            Tiles.Add(new DiscoverTile(Loc.T("discover.similar_tile"), Loc.T("discover.similar_tile_desc", seed.Name), "",
                new RelayCommand(() => main.OpenSimilarArtists(seed))));
        }
    }

    public override string Title => Loc.T("common.discover");
    public List<DiscoverTile> Tiles { get; }
    public bool IsEmpty => Tiles.Count == 0;

    /// <summary>Shown when Spotify is not connected: its tiles need the account and are greyed out.</summary>
    public bool NeedsSpotify => !Main.Spotify.IsConnected;
}

/// <summary>Your most played tracks and artists, over a period you choose.</summary>
public sealed class TopsPage : PageViewModel
{
    int _version;

    public TopsPage(MainViewModel main) : base(main)
    {
        _range = main.State.Features.TopRange;
        _ = Reload();
    }

    public override string Title => Loc.T("pages.your_tops");

    string _range;
    /// <summary>A Spotify time range: short_term (4 weeks), medium_term (6 months) or long_term.</summary>
    public string Range
    {
        get => _range;
        set
        {
            if (!FeatureSettings.TopRanges.Contains(value) || !Set(ref _range, value)) return;
            Main.State.Features.TopRange = value;
            Main.ScheduleSave();
            _ = Reload();
        }
    }

    List<TrackRow> _tracks = [];
    public List<TrackRow> Tracks { get => _tracks; private set => Set(ref _tracks, value); }

    List<Artist> _artists = [];
    public List<Artist> Artists { get => _artists; private set => Set(ref _artists, value); }

    bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) OnPropertyChanged(nameof(IsEmpty)); } }

    string? _error;
    public string? Error { get => _error; private set { if (Set(ref _error, value)) { OnPropertyChanged(nameof(EmptyText)); OnPropertyChanged(nameof(IsEmpty)); } } }

    public bool IsEmpty => !_isLoading && _tracks.Count == 0 && _artists.Count == 0;
    public string EmptyText => _error ?? Loc.T("tops.empty");

    public ICommand PlayCommand => _play ??= new RelayCommand(() =>
        Main.PlayTracks(_tracks.Select(r => r.Track).ToList(), 0, Loc.T("pages.your_tops")));
    ICommand? _play;

    public async Task Reload()
    {
        var version = ++_version;
        IsLoading = true;
        try
        {
            var tracks = Main.Spotify.GetTopTracksAsync(_range);
            var artists = Main.Spotify.GetTopArtistsAsync(_range);
            var rows = (await tracks).Select((t, i) => new TrackRow(i + 1, t)).ToList();
            var found = await artists;
            if (version != _version) return;
            Tracks = rows;
            Artists = found;
            Error = null;
        }
        catch (Exception ex)
        {
            if (version == _version) Error = ex.Message;
        }
        finally
        {
            if (version == _version) IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}

/// <summary>One bar of the "when do you listen" histogram.</summary>
public sealed record HourBar(string Label, int Plays, double Share);

/// <summary>One line of a statistics list, with its count already written out.</summary>
public sealed record StatRow(string Title, string Subtitle, string Plays, string Listened);

/// <summary>
/// What Sonora's own listening log says: plays, time, tops and listening hours over any period.
/// Spotify's API offers none of this to a third-party app, so every figure here is Sonora's own.
/// </summary>
public sealed class StatsPage : PageViewModel
{
    public StatsPage(MainViewModel main) : base(main)
    {
        ClearCommand = new RelayCommand(() => _ = ClearAsync());
        Apply();
    }

    public override string Title => Loc.T("pages.your_statistics");

    public List<int> PeriodDays => [.. Listening.Periods];

    int _days = 30;
    /// <summary>Period in days; 0 means the whole log.</summary>
    public int Days
    {
        get => _days;
        set { if (Set(ref _days, value)) Apply(); }
    }

    public ICommand ClearCommand { get; }

    public string Totals { get; private set; } = "";
    public List<StatRow> TopTracks { get; private set; } = [];
    public List<StatRow> TopArtists { get; private set; } = [];
    public List<HourBar> Hours { get; private set; } = [];

    public bool IsEmpty => TopTracks.Count == 0;
    public string EmptyText => Main.State.Features.ListeningLog
        ? Loc.T("stats.empty")
        : Loc.T("stats.log_disabled");

    void Apply()
    {
        var entries = Listening.Since(Main.Listening.Entries, _days, DateTime.UtcNow);
        var totals = Listening.Totals(entries);
        Totals = $"{Loc.Count("count.plays", totals.Plays)} • {Listening.Duration(totals.Seconds)} • " +
                 $"{Loc.Count("count.tracks", totals.Tracks)} • {Loc.Count("count.artists", totals.Artists)}";
        TopTracks = Listening.TopTracks(entries, 25)
            .Select(c => new StatRow(c.Title, c.Artist, Loc.Count("count.plays", c.Plays), Listening.Duration(c.Seconds)))
            .ToList();
        TopArtists = Listening.TopArtists(entries, 25)
            .Select(c => new StatRow(c.Artist, "", Loc.Count("count.plays", c.Plays), Listening.Duration(c.Seconds)))
            .ToList();

        var byHour = Listening.ByHour(entries);
        var peak = byHour.Length > 0 ? byHour.Max() : 0;
        Hours = byHour.Select((plays, hour) => new HourBar($"{hour}h", plays, peak > 0 ? (double)plays / peak : 0)).ToList();

        foreach (var p in new[] { nameof(Totals), nameof(TopTracks), nameof(TopArtists), nameof(Hours),
                     nameof(IsEmpty), nameof(EmptyText) })
            OnPropertyChanged(p);
    }

    async Task ClearAsync()
    {
        await Main.Listening.ClearAsync();
        Apply();
    }
}

/// <summary>A grid of artist cards fetched on demand (similar artists).</summary>
public sealed class ArtistGridPage : PageViewModel
{
    readonly Func<Task<List<Artist>>> _load;
    readonly string _empty;
    int _version;

    public ArtistGridPage(MainViewModel main, string title, string subtitle, string empty, Func<Task<List<Artist>>> load) : base(main)
    {
        Title = title;
        Subtitle = subtitle;
        _empty = empty;
        _load = load;
        _ = Reload();
    }

    public override string Title { get; }
    public string Subtitle { get; }

    List<Artist> _items = [];
    public List<Artist> Items { get => _items; private set => Set(ref _items, value); }

    bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) OnPropertyChanged(nameof(IsEmpty)); } }

    public bool IsEmpty => !_isLoading && _items.Count == 0;
    public string EmptyText => _empty;

    public async Task Reload()
    {
        var version = ++_version;
        IsLoading = true;
        try
        {
            var found = await _load();
            if (version != _version) return;
            Items = found;
        }
        catch
        {
            if (version == _version) Items = [];
        }
        finally
        {
            if (version == _version) IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}

/// <summary>A grid of album cards fetched on demand (new releases, saved podcast shows).</summary>
public sealed class AlbumGridPage : PageViewModel
{
    readonly Func<Task<List<Album>>> _load;
    readonly string _empty;
    int _version;

    public AlbumGridPage(MainViewModel main, string title, string empty, Func<Task<List<Album>>> load) : base(main)
    {
        Title = title;
        _empty = empty;
        _load = load;
        _ = Reload();
    }

    public override string Title { get; }

    List<Album> _items = [];
    public List<Album> Items { get => _items; private set => Set(ref _items, value); }

    bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) OnPropertyChanged(nameof(IsEmpty)); } }

    string? _error;
    public string? Error { get => _error; private set { if (Set(ref _error, value)) { OnPropertyChanged(nameof(EmptyText)); OnPropertyChanged(nameof(IsEmpty)); } } }

    public bool IsEmpty => !_isLoading && _items.Count == 0;
    public string EmptyText => _error ?? _empty;

    public async Task Reload()
    {
        var version = ++_version;
        IsLoading = true;
        try
        {
            var found = await _load();
            if (version != _version) return;
            Items = found;
            Error = null;
        }
        catch (Exception ex)
        {
            if (version == _version) Error = ex.Message;
        }
        finally
        {
            if (version == _version) IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}

public sealed class AlbumsPage(MainViewModel main) : PageViewModel(main)
{
    public override string Title => Loc.T("common.albums");
    public List<Album> Items { get; } = main.Spotify.SavedAlbums.Concat(main.Library.Albums).ToList();
}

public sealed class ArtistsPage(MainViewModel main) : PageViewModel(main)
{
    public override string Title => Loc.T("common.artists");
    public List<Artist> Items { get; } = main.Spotify.Artists.Concat(main.Library.Artists).ToList();
}

public sealed class PlaylistsPage(MainViewModel main) : PageViewModel(main)
{
    public override string Title => Loc.T("common.playlists");
    public List<SpotifyPlaylist> Items { get; } = main.Spotify.Playlists;
}

public sealed class AppearancePage(MainViewModel main) : PageViewModel(main)
{
    public override string Title => Loc.T("common.appearance");
    public AppearanceViewModel Settings => Main.Appearance;
}

public sealed class SettingsPage(MainViewModel main) : PageViewModel(main)
{
    public override string Title => Loc.T("common.settings");
}
