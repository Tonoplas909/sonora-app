using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Sonora.Core;
using Sonora.Models;
using Sonora.Services;
using Sonora.Spotify;
using Sonora.Appearance.Features;
using Sonora.Appearance.Localization;

namespace Sonora.ViewModels;

public sealed class LibraryEntry : ObservableObject
{
    public required object Source { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public string Glyph { get; init; } = "";
    public bool IsRound { get; init; }
    public bool IsSpecial { get; init; }

    /// <summary>Stable identity used to pin the entry; survives restarts and library reloads.</summary>
    public string Key => Source switch
    {
        LikedSource => "liked",
        LocalLikedSource => "localLiked",
        AllTracksSource => "allTracks",
        SpotifyPlaylist p => p.Uri,
        Playlist p => "playlist:" + p.Id,
        Album a => a.Uri ?? "album:" + a.Key,
        Artist a => a.Uri ?? "artist:" + a.Name,
        _ => Title,
    };

    public bool IsPinned { get; set; }

    /// <summary>Where the entry comes from, a <see cref="LibrarySources"/> id: what the library panel's tabs filter on.</summary>
    public string Provider { get; init; } = LibrarySources.Local;

    int _playState;
    /// <summary>0: not what is playing; 1: what the current track comes from, paused; 2: the same, playing.</summary>
    public int PlayState { get => _playState; set => Set(ref _playState, value); }
}

/// <summary>Art source for the "Liked songs" collection (gradient tile, no image).</summary>
public sealed class LikedSource
{
    public string? ArtPath => null;
}

public sealed class LocalLikedSource
{
    public string? ArtPath => null;
}

public sealed class AllTracksSource
{
    public string? ArtPath => null;
}

public sealed class MainViewModel : ObservableObject
{
    public static readonly LikedSource Liked = new();
    public static readonly LocalLikedSource LocalLiked = new();
    public static readonly AllTracksSource AllTracks = new();

    readonly ArtworkService _art = new();
    readonly LyricsService _lyricsService = new();
    readonly ListeningLogService _listening = new();
    readonly ScrobbleService _scrobble = new();
    readonly DiscoveryService _discovery = new();
    readonly HashSet<string> _liked = new(StringComparer.OrdinalIgnoreCase);
    readonly List<PageViewModel> _back = [];
    readonly List<PageViewModel> _forward = [];
    readonly DispatcherTimer _saveTimer;
    readonly DispatcherTimer _statusTimer;
    readonly DispatcherTimer _sleepTimer;
    CancellationTokenSource? _scanCts;

    /// <summary>Sonora's own listening history, kept on this machine (Spotify gives third parties none).</summary>
    public ListeningLogService Listening => _listening;

    public LibraryService Library { get; } = new();
    public PlayerService Player { get; }
    public SpotifyAccount Spotify { get; }
    public AppState State { get; private set; } = new();
    public ObservableCollection<Playlist> Playlists { get; } = [];
    public ObservableCollection<LibraryEntry> LibraryEntries { get; } = [];
    public ObservableCollection<string> Folders { get; } = [];

    /// <summary>Window handle hosting the (invisible) Spotify player; set by the window.</summary>
    public IntPtr WindowHandle { get; set; }

    /// <summary>Diagnostics renders (--snapshot): never touch the user's Spotify account or show the sign-in gate.</summary>
    public bool SnapshotMode
    {
        get => _snapshotMode;
        set { _snapshotMode = value; if (value) ShowLoginGate = false; }
    }
    bool _snapshotMode;

    public ICommand OpenAppearanceCommand => _openAppearance ??= new RelayCommand(() => Navigate(new AppearancePage(this)));
    ICommand? _openAppearance;

    /// <summary>Themes, colours, type, layout and content settings (created on first use).</summary>
    public AppearanceViewModel Appearance => _appearance ??= new AppearanceViewModel(this);
    AppearanceViewModel? _appearance;

    /// <summary>Looking for a new version of Sonora, and installing it (created on first use).</summary>
    public UpdatesViewModel Updates => _updates ??= new UpdatesViewModel(this);
    UpdatesViewModel? _updates;

    /// <summary>Live lyrics for the track playing in <see cref="Player"/> (created on first use, i.e. when its panel is shown).</summary>
    public LyricsViewModel Lyrics => _lyrics ??= new LyricsViewModel(Player, _lyricsService);
    LyricsViewModel? _lyrics;

    public MainViewModel(Dispatcher dispatcher)
    {
        Player = new PlayerService(dispatcher);
        Layout = new LayoutViewModel(dispatcher);
        Layout.Changed += () => OnPropertyChanged(nameof(IsQueueVisible));
        Layout.Changed += () => OnPropertyChanged(nameof(IsLyricsVisible));
        Player.TrackStarted += OnTrackStarted;
        Player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PlayerService.ContextName) or nameof(PlayerService.ContextUri) or nameof(PlayerService.IsPlaying)
                or nameof(PlayerService.Current))
                UpdatePlayingContext();
        };
        Player.TrackFinished += OnTrackFinished;
        Player.TrackStarted += t => _ = App.Themes?.OnTrackArtworkAsync(t.AlbumRef?.ArtPath);
        if (App.Themes != null)
            App.Themes.Applied += warnings => { foreach (var w in warnings) ReportError(w); }; // e.g. missing font
        Player.Error += ReportError;
        _scrobble.Error += ReportError;
        Spotify = new SpotifyAccount(dispatcher);
        Spotify.Engine.Error += ReportError;
        Spotify.Notice += ReportError;
        Loc.LanguageChanged += OnLanguageChanged;
        Spotify.LibraryChanged += () => dispatcher.InvokeAsync(OnSpotifyLibraryChanged);
        Player.AttachSpotify(Spotify.Engine);
        _showLoginGate = !Spotify.Auth.IsSignedIn && !StateStore.LoadState().LocalOnly; // known synchronously: no flash of the main UI

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1.5) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveState(); };
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(8) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); if (!IsScanning) Status = ""; };
        _sleepTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(5) };
        _sleepTimer.Tick += (_, _) => OnSleepTick();
        _page = new HomePage(this);

        GoHomeCommand = new RelayCommand(() => Navigate(new HomePage(this)));
        GoSearchCommand = new RelayCommand(() => Navigate(new SearchPage(this, SearchText)));
        GoBackCommand = new RelayCommand(GoBack, () => _back.Count > 0);
        GoForwardCommand = new RelayCommand(GoForward, () => _forward.Count > 0);
        OpenSettingsCommand = new RelayCommand(() => Navigate(new SettingsPage(this)));
        OpenDiscoverCommand = new RelayCommand(() => Navigate(new DiscoverPage(this)));
        OpenAlbumsCommand = new RelayCommand(() => Navigate(new AlbumsPage(this)));
        OpenArtistsCommand = new RelayCommand(() => Navigate(new ArtistsPage(this)));
        OpenPlaylistsCommand = new RelayCommand(() => Navigate(new PlaylistsPage(this)));
        OpenAlbumCommand = new RelayCommand(p => { if (p is Album a) OpenAlbum(a); });
        OpenArtistCommand = new RelayCommand(p => { if (p is Artist a) OpenArtist(a); });
        OpenGenreCommand = new RelayCommand(p => { if (p is Genre g) OpenGenre(g); });
        OpenSpotifyPlaylistCommand = new RelayCommand(p => { if (p is SpotifyPlaylist sp) OpenSpotifyPlaylist(sp); });
        PlayAlbumCommand = new RelayCommand(p => { if (p is Album a) PlayAlbum(a); });
        PlayArtistCommand = new RelayCommand(p => { if (p is Artist a) PlayArtist(a); });
        PlaySpotifyPlaylistCommand = new RelayCommand(p => { if (p is SpotifyPlaylist sp) Player.PlaySpotifyContext(sp.Uri, sp.Name); });
        OpenCurrentAlbumCommand = new RelayCommand(() => { if (Player.Current?.AlbumRef is { } a) OpenAlbum(a); });
        OpenCurrentArtistCommand = new RelayCommand(() => { if (Player.Current is { } t) OpenTrackArtist(t); });

        TogglePlayCommand = new RelayCommand(TogglePlay);
        NextCommand = new RelayCommand(Player.Next);
        PreviousCommand = new RelayCommand(Player.Previous);
        ToggleShuffleCommand = new RelayCommand(() => { Player.Shuffle = !Player.Shuffle; ScheduleSave(); });
        CycleRepeatCommand = new RelayCommand(() => { Player.CycleRepeat(); ScheduleSave(); });
        ToggleMuteCommand = new RelayCommand(() => Player.IsMuted = !Player.IsMuted);
        ToggleQueueCommand = new RelayCommand(() => Layout.Toggle(Sonora.Appearance.Layout.PanelIds.Queue));
        ToggleLyricsCommand = new RelayCommand(() => Layout.Toggle(Sonora.Appearance.Layout.PanelIds.Lyrics));
        ToggleLikeCommand = new RelayCommand(p => { if (p is Track t) ToggleLike(t); });
        ToggleLikeCurrentCommand = new RelayCommand(() => { if (Player.Current is { } t) ToggleLike(t); });

        NewPlaylistCommand = new RelayCommand(() => OpenPlaylist(CreatePlaylist()));
        ImportPlaylistCommand = new RelayCommand(() => _ = ImportPlaylistAsync());
        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveFolderCommand = new RelayCommand(p => { if (p is string f) RemoveFolder(f); });
        RescanCommand = new RelayCommand(() => _ = RescanAsync());

        SpotifyLoginCommand = new RelayCommand(() => _ = SpotifyLoginAsync(), () => !Spotify.IsBusy);
        SpotifyLogoutCommand = new RelayCommand(SpotifyLogout);
        UseLocalFilesOnlyCommand = new RelayCommand(UseLocalFilesOnly);
        SpotifyRefreshCommand = new RelayCommand(() => _ = Guard(Spotify.LoadLibraryAsync()));
        OpenDeveloperDashboardCommand = new RelayCommand(() => OpenUrl("https://developer.spotify.com/dashboard"));
        CopyRedirectUriCommand = new RelayCommand(() =>
        {
            Clipboard.SetText(SpotifyAuth.RedirectUri);
            ReportError(Loc.T("spotify.redirect_copied"));
        });
    }

    // ---------------- commands ----------------

    public ICommand GoHomeCommand { get; }
    public ICommand GoSearchCommand { get; }
    public ICommand GoBackCommand { get; }
    public ICommand GoForwardCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenDiscoverCommand { get; }
    public ICommand OpenAlbumsCommand { get; }
    public ICommand OpenArtistsCommand { get; }
    public ICommand OpenPlaylistsCommand { get; }
    public ICommand OpenAlbumCommand { get; }
    public ICommand OpenArtistCommand { get; }
    public ICommand OpenGenreCommand { get; }
    public ICommand OpenSpotifyPlaylistCommand { get; }
    public ICommand PlayAlbumCommand { get; }
    public ICommand PlayArtistCommand { get; }
    public ICommand PlaySpotifyPlaylistCommand { get; }
    public ICommand OpenCurrentAlbumCommand { get; }
    public ICommand OpenCurrentArtistCommand { get; }
    public ICommand TogglePlayCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand ToggleShuffleCommand { get; }
    public ICommand CycleRepeatCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleQueueCommand { get; }
    public ICommand ToggleLyricsCommand { get; }
    public ICommand ToggleLikeCommand { get; }
    public ICommand ToggleLikeCurrentCommand { get; }
    public ICommand NewPlaylistCommand { get; }
    public ICommand ImportPlaylistCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand RemoveFolderCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand SpotifyLoginCommand { get; }
    public ICommand SpotifyLogoutCommand { get; }
    public ICommand UseLocalFilesOnlyCommand { get; }    public ICommand SpotifyRefreshCommand { get; }
    public ICommand OpenDeveloperDashboardCommand { get; }
    public ICommand CopyRedirectUriCommand { get; }

    // ---------------- bindable state ----------------

    PageViewModel _page;
    public PageViewModel CurrentPage
    {
        get => _page;
        private set
        {
            if (!Set(ref _page, value)) return;
            if (value is not SearchPage && _searchText.Length > 0)
            {
                _searchText = "";
                OnPropertyChanged(nameof(SearchText));
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }

    string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value ?? "")) return;
            if (CurrentPage is SearchPage sp) sp.Query = _searchText;
            else Navigate(new SearchPage(this, _searchText));
        }
    }

    /// <summary>A tab of the library panel: one per source of music that is available, plus "all".</summary>
    public sealed class LibrarySourceTab(string id, string title, bool selected, Action<string> select) : IEquatable<LibrarySourceTab>
    {
        public string Id { get; } = id;
        public string Title { get; } = title;

        /// <summary>Two-way so a click, the keyboard and a screen reader all select the tab the same way.</summary>
        public bool IsSelected
        {
            get => selected;
            set { if (value && !selected) select(Id); }
        }

        public bool Equals(LibrarySourceTab? other) => other != null && Id == other.Id && Title == other.Title && IsSelected == other.IsSelected;
        public override bool Equals(object? obj) => Equals(obj as LibrarySourceTab);
        public override int GetHashCode() => HashCode.Combine(Id, Title, selected);
    }

    public ObservableCollection<LibrarySourceTab> LibrarySourceTabs { get; } = [];

    /// <summary>The tabs only make sense when there is more than one source to choose between.</summary>
    public bool ShowLibrarySourceTabs => LibrarySourceTabs.Count > 0;

    void SelectLibrarySource(string id)
    {
        if (State.Features.LibrarySource == id) return;
        State.Features.LibrarySource = id;
        ScheduleSave();
        RefreshLibraryEntries();
    }

    string _libraryFilter = "Playlists";
    public string LibraryFilter
    {
        get => _libraryFilter;
        set { if (Set(ref _libraryFilter, value)) RefreshLibraryEntries(); }
    }

    string _librarySearch = "";
    /// <summary>Free-text filter over the library list (title and subtitle).</summary>
    public string LibrarySearch
    {
        get => _librarySearch;
        set { if (Set(ref _librarySearch, value ?? "")) { OnPropertyChanged(nameof(IsLibrarySearching)); RefreshLibraryEntries(); } }
    }

    public bool IsLibrarySearching => _librarySearch.Trim().Length > 0;

    /// <summary>"recent" keeps the order the library was built in; "alpha" sorts by name. Pins always come first.</summary>
    public string LibrarySort
    {
        get => State.Features.LibrarySort;
        set
        {
            if (!Sonora.Appearance.Features.FeatureSettings.LibrarySorts.Contains(value) || State.Features.LibrarySort == value) return;
            State.Features.LibrarySort = value;
            OnPropertyChanged();
            ScheduleSave();
            RefreshLibraryEntries();
        }
    }

    /// <summary>Library panel showing covers only, whatever its width.</summary>
    public bool LibraryCompact
    {
        get => State.Content.LibraryCompact;
        set
        {
            if (State.Content.LibraryCompact == value) return;
            State.Content.LibraryCompact = value;
            OnPropertyChanged();
            NotifyContentChanged();
        }
    }

    /// <summary>Pins or unpins a library entry; pinned entries are hoisted to the top of the list.</summary>
    public void TogglePin(LibraryEntry entry)
    {
        var pinned = State.Features.TogglePin(entry.Key);
        ScheduleSave();
        RefreshLibraryEntries();
        ReportError(Loc.T(pinned ? "library.pinned" : "library.unpinned", entry.Title));
    }

    /// <summary>Drops a pinned entry where another pinned entry sits (the pin order is the user's own).</summary>
    public void MovePin(LibraryEntry entry, LibraryEntry target)
    {
        if (!State.Features.MovePin(entry.Key, target.Key)) return;
        ScheduleSave();
        RefreshLibraryEntries();
    }

    bool _showLoginGate;
    /// <summary>Full-window sign-in screen, shown until a Spotify account is connected or local files only are chosen.</summary>
    public bool ShowLoginGate { get => _showLoginGate; private set => Set(ref _showLoginGate, value); }

    void UpdateLoginGate() => ShowLoginGate = !Spotify.Auth.IsSignedIn && !State.LocalOnly && !SnapshotMode;

    void UseLocalFilesOnly()
    {
        State.LocalOnly = true;
        ScheduleSave();
        UpdateLoginGate();
    }

    /// <summary>Arrangement of the dockable panels.</summary>
    public LayoutViewModel Layout { get; }

    public bool IsQueueVisible => Layout.IsVisible(Sonora.Appearance.Layout.PanelIds.Queue);
    public bool IsLyricsVisible => Layout.IsVisible(Sonora.Appearance.Layout.PanelIds.Lyrics);

    bool _isScanning;
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }

    string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public string SpotifyClientId
    {
        get => State.SpotifyClientId;
        set { if (State.SpotifyClientId == value) return; State.SpotifyClientId = value.Trim(); OnPropertyChanged(); ScheduleSave(); }
    }

    public string RedirectUri => SpotifyAuth.RedirectUri;

    /// <summary>ListenBrainz user token; while it is empty, no listen ever leaves this machine.</summary>
    public string ListenBrainzToken
    {
        get => State.ListenBrainzToken;
        set
        {
            var token = (value ?? "").Trim();
            if (State.ListenBrainzToken == token) return;
            State.ListenBrainzToken = token;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsScrobbling));
            ScheduleSave();
        }
    }

    public bool IsScrobbling => ScrobbleService.HasToken(State.ListenBrainzToken);

    public ICommand TestScrobbleCommand => _testScrobble ??= new RelayCommand(() => _ = TestScrobbleAsync());
    ICommand? _testScrobble;

    async Task TestScrobbleAsync() => ReportError(await _scrobble.TestAsync(State.ListenBrainzToken));

    /// <summary>What the user chose to show (home sections, list columns, card size).</summary>
    public Sonora.Appearance.Content.ContentSettings Content => State.Content;

    /// <summary>Re-applies content settings everywhere after a change.</summary>
    public void NotifyContentChanged()
    {
        Sonora.Views.ColumnLayout.Current.Update(State.Content);
        App.Themes?.SetCardSize(State.Content.CardPixels);
        OnPropertyChanged(nameof(Content));
        if (CurrentPage is HomePage) CurrentPage = new HomePage(this);
        ScheduleSave();
    }

    public string Stats =>
        $"{Loc.Count("count.tracks", Library.Tracks.Count)} • {Loc.Count("count.albums", Library.Albums.Count)} • {Loc.Count("count.artists", Library.Artists.Count)}";

    /// <summary>What the one-line status of the window can show without cutting it (about 360 px of small text).</summary>
    const int StatusLineFits = 48;

    /// <summary>
    /// Tells the user something. A short message goes in the status line; a longer one would be cut there, so it
    /// goes in a notification card that closes by itself instead (a snapshot, which must stay still, keeps the line).
    /// </summary>
    public void ReportError(string message)
    {
        if (message.Length > StatusLineFits && !SnapshotMode)
        {
            Sonora.Views.NoticeToastWindow.Show(message);
            return;
        }
        Status = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    async Task Guard(Task task)
    {
        try { await task; }
        catch (Exception ex) { ReportError(ex.Message); }
    }

    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // ---------------- lifecycle ----------------

    public async Task InitializeAsync()
    {
        State = await Task.Run(StateStore.LoadState);
        State.Content ??= Sonora.Appearance.Content.ContentSettings.Default();
        State.Content.Repair();
        State.Features ??= new Sonora.Appearance.Features.FeatureSettings();
        State.Features.Repair();
        NotifyFeatures(); // the switches and chips read straight off the loaded settings
        OnPropertyChanged(nameof(ListenBrainzToken));
        OnPropertyChanged(nameof(IsScrobbling));
        NotifyContentChanged();
        if (App.Catalog != null)
        {
            // A retired theme becomes its replacement; leaving Immersif keeps its cover background as an option.
            if (Sonora.Appearance.Theming.ThemeCatalog.WasImmersive(State.ThemeId)) State.CoverBackground = true;
            State.ThemeId = App.Catalog.Get(State.ThemeId).Id;
            foreach (var err in App.Catalog.LoadErrors) ReportError(Loc.T("theme.ignored", err));
        }
        if (State.SpotifyClientId.Length == 0 && Spotify.Auth.ClientId.Length > 0) State.SpotifyClientId = Spotify.Auth.ClientId;
        OnPropertyChanged(nameof(SpotifyClientId));
        if (State.Folders.Count == 0)
        {
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (Directory.Exists(music)) State.Folders.Add(music);
        }
        foreach (var f in State.Folders) Folders.Add(f);
        foreach (var p in State.Liked) _liked.Add(p);
        foreach (var p in State.Playlists) Playlists.Add(p);
        Player.Volume = State.Volume;
        Player.Shuffle = State.Shuffle;
        Player.Repeat = State.Repeat;

        await _listening.LoadAsync(); // reading is harmless in snapshot mode; only writing is blocked

        var cached = await Task.Run(StateStore.LoadLibrary);
        if (cached.Count > 0)
        {
            ApplyLibrary(cached);
            RestoreLastTrack();
            _ = LoadArtAsync(CancellationToken.None);
        }
        else
        {
            RefreshLibraryEntries();
            RestoreLastTrack(); // nothing local to play, but a Spotify track may be waiting to be resumed
        }

        // The cached Spotify library draws at once; ConnectSpotifyAsync replaces it as soon as Spotify answers.
        if (Spotify.Auth.IsSignedIn && !SnapshotMode && await Task.Run(SpotifyCacheStore.Load) is { } cache)
            Spotify.ApplyCache(cache);

        CurrentPage = new HomePage(this);
        if (Spotify.Auth.IsSignedIn && !SnapshotMode) _ = ConnectSpotifyAsync();
        if (!SnapshotMode) _ = Updates.CheckAtStartupAsync(); // opening Sonora looks for a new version, unless that is switched off
        await RescanAsync();
    }

    public async Task RescanAsync()
    {
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        IsScanning = true;
        Status = Loc.T("library.scanning");
        try
        {
            var cache = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in Library.Tracks) cache[t.Path] = t;
            var progress = new Progress<(int Done, int Total)>(p => Status = Loc.T("library.scanning_progress", p.Done, p.Total));
            var tracks = await Library.ScanAsync(Folders.ToList(), cache, progress, cts.Token);

            ApplyLibrary(tracks);
            var snapshot = tracks.ToList();
            if (!SnapshotMode) _ = Task.Run(() => StateStore.SaveLibrary(snapshot));
            if (Player.Current == null) RestoreLastTrack();
            RefreshCurrentPage();
            Status = "";
            await LoadArtAsync(cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReportError(Loc.T("library.scan_error", ex.Message)); }
        finally
        {
            if (_scanCts == cts) IsScanning = false;
            if (CurrentPage is HomePage) CurrentPage = new HomePage(this);
        }
    }

    public void Shutdown()
    {
        _scanCts?.Cancel();
        _appearance?.Flush(); // a theme edited less than 500 ms ago is still written
        if (!SnapshotMode) SaveState(); // diagnostics renders must not overwrite the user's settings
        Spotify.Dispose();
        Player.Dispose();
    }

    void ApplyLibrary(List<Track> tracks)
    {
        Library.Build(tracks);
        foreach (var t in Library.Tracks) t.IsLiked = _liked.Contains(t.Path);
        RefreshPlaylistArt();
        RefreshLibraryEntries();
        OnPropertyChanged(nameof(Stats));
    }

    async Task LoadArtAsync(CancellationToken ct)
    {
        try
        {
            await _art.LoadAllAsync(Library.Albums, ct);
        }
        catch (OperationCanceledException) { return; }
        foreach (var ar in Library.Artists)
            ar.ArtPath ??= ar.Albums.Select(a => a.ArtPath).FirstOrDefault(p => p != null);
        RefreshPlaylistArt();
    }

    /// <summary>
    /// Brings back what was playing when Sonora closed, paused, at the second it was left: a local file in the list
    /// it came from, or a Spotify track (once signed in). Falls back to the last local track of older versions.
    /// </summary>
    void RestoreLastTrack()
    {
        if (Player.Current != null) return;
        if (State.Resume is { } r)
        {
            if (r.Uri.Length > 0) { RestoreSpotifyTrack(r); return; }
            if (r.Path.Length > 0 && Library.Get(r.Path) is { } local)
            {
                var ctx = LocalContext(r.ContextName, local);
                Player.PlayContext(ctx, Math.Max(0, ctx.IndexOf(local)), r.ContextName.Length > 0 ? r.ContextName : local.Album,
                    autoplay: false, startAt: ResumeStart(r.PositionSec, local.DurationSec));
                return;
            }
        }
        if (State.LastTrack is not { } path || Library.Get(path) is not { } t) return;
        var album = t.AlbumRef?.Tracks ?? [t];
        Player.PlayContext(album, album.IndexOf(t), t.Album, autoplay: false);
    }

    void RestoreSpotifyTrack(ResumePoint r)
    {
        if (SnapshotMode || !Spotify.Auth.IsSignedIn) return; // a render never touches Spotify; no account, nothing to resume
        var album = new Album { Key = "resume:" + r.Uri, Title = r.Album, Artist = r.Artist, ArtPath = r.ArtUrl };
        var track = new Track { Uri = r.Uri, Title = r.Title, Artist = r.Artist, Album = r.Album, DurationSec = r.DurationSec, AlbumRef = album };
        Player.RestoreSpotify(track, r.ContextUri, r.ContextName, ResumeStart(r.PositionSec, r.DurationSec));
    }

    /// <summary>The list a local track was played from, found again by name; its album when that list is gone.</summary>
    List<Track> LocalContext(string name, Track track)
    {
        List<Track>? list = null;
        if (Loc.MatchesAnyLanguage("library.all_tracks", name)) list = Library.Tracks.ToList();
        else if (Playlists.FirstOrDefault(p => p.Name == name) is { } playlist)
            list = playlist.Paths.Select(Library.Get).OfType<Track>().ToList();
        return list != null && list.Contains(track) ? list : track.AlbumRef?.Tracks ?? [track];
    }

    /// <summary>Starting again from the top beats resuming the last few seconds of a track.</summary>
    static double ResumeStart(double position, double duration) =>
        position <= 1 || (duration > 0 && position >= duration - 5) ? 0 : position;

    /// <summary>What to remember about the current track, or null when nothing has played (the previous one is kept then).</summary>
    ResumePoint? CaptureResume()
    {
        if (Player.Current is not { } t) return null;
        return new ResumePoint
        {
            Path = t.IsSpotify ? "" : t.Path,
            Uri = t.Uri ?? "",
            Title = t.Title,
            Artist = t.Artist,
            Album = t.Album,
            ArtUrl = t.AlbumRef?.ArtPath is { } art && art.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? art : null,
            DurationSec = t.DurationSec,
            PositionSec = Player.SeekValue,
            ContextUri = Player.ContextUri,
            ContextName = Player.ContextName,
        };
    }

    void OnTrackStarted(Track t)
    {
        // "Stop at the end of this track": the next track starting is the signal to pause.
        if (Sleep.ShouldStopOnTrackChange()) StopForSleep();
        if (IsEpisode(t)) _ = ResumeEpisodeAsync(t);
        if (t.IsSpotify) { ScheduleSave(); return; }
        State.LastTrack = t.Path;
        var key = t.AlbumKey;
        State.RecentAlbums.Remove(key);
        State.RecentAlbums.Insert(0, key);
        if (State.RecentAlbums.Count > 20) State.RecentAlbums.RemoveRange(20, State.RecentAlbums.Count - 20);
        ScheduleSave();
    }

    /// <summary>Minimum progress worth resuming: below this, starting over is what you want anyway.</summary>
    const double ResumeThresholdSeconds = 30;

    static bool IsEpisode(Track t) => t.Uri?.StartsWith("spotify:episode:", StringComparison.Ordinal) == true;

    /// <summary>Remembers where a podcast episode was left, and forgets it once the episode is finished.</summary>
    void RememberEpisodePosition(Track t, double playedSeconds)
    {
        if (!IsEpisode(t) || t.Uri is not { } uri) return;
        var nearTheEnd = t.DurationSec > 0 && playedSeconds >= t.DurationSec - ResumeThresholdSeconds;
        if (playedSeconds < ResumeThresholdSeconds || nearTheEnd) State.EpisodePositions.Remove(uri);
        else State.EpisodePositions[uri] = playedSeconds;
        ScheduleSave();
    }

    /// <summary>Picks an episode up where it was left, once playback has actually started.</summary>
    async Task ResumeEpisodeAsync(Track t)
    {
        if (!IsEpisode(t) || t.Uri is not { } uri) return;
        if (!State.EpisodePositions.TryGetValue(uri, out var seconds) || seconds < ResumeThresholdSeconds) return;
        await Task.Delay(1200); // the Spotify engine ignores a seek sent before the stream is running
        if (!ReferenceEquals(Player.Current, t) || Player.SeekValue > ResumeThresholdSeconds) return;
        Player.Seek(seconds);
        ReportError(Loc.T("player.resumed_at", Fmt.Time(seconds)));
    }

    /// <summary>Writes a play to the local log once it has been listened to long enough to count.</summary>
    void OnTrackFinished(Track t, double playedSeconds)
    {
        RememberEpisodePosition(t, playedSeconds);
        // A diagnostics render must never touch the real history, but may write to a sandboxed one.
        if ((SnapshotMode && !ListeningLogService.IsSandboxed) || !State.Features.ListeningLog) return;
        if (!Sonora.Appearance.Features.Listening.Counts(playedSeconds, t.DurationSec)) return;
        var entry = new Sonora.Appearance.Features.ListenEntry
        {
            AtUtc = DateTime.UtcNow,
            Key = t.Uri ?? t.Path,
            Title = t.Title,
            Artist = t.PrimaryArtist,
            Album = t.Album,
            Seconds = Math.Min(playedSeconds, t.DurationSec > 0 ? t.DurationSec : playedSeconds),
        };
        _listening.Append(entry);
        if (IsScrobbling) _ = _scrobble.SubmitAsync(State.ListenBrainzToken, entry);
    }

    public void OpenStats() => Navigate(new StatsPage(this));

    public void ScheduleSave()
    {
        if (SnapshotMode) return; // diagnostics renders never write the user's files
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void SaveState()
    {
        if (SnapshotMode) return;
        State.Folders = Folders.ToList();
        State.Playlists = Playlists.ToList();
        State.Volume = Player.Volume;
        State.Shuffle = Player.Shuffle;
        State.Repeat = Player.Repeat;
        if (CaptureResume() is { } resume) State.Resume = resume;
        StateStore.SaveState(State);
    }

    // ---------------- Spotify ----------------

    async Task SpotifyLoginAsync()
    {
        try
        {
            await Spotify.LoginAsync(SpotifyClientId);
            State.LocalOnly = false; // signing out later brings the sign-in screen back, as before
            ScheduleSave();
            UpdateLoginGate();
            await ConnectSpotifyAsync();
        }
        catch (Exception ex)
        {
            Spotify.Status = ex.Message;
            ReportError(ex.Message);
        }
        finally
        {
            UpdateLoginGate();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    async Task ConnectSpotifyAsync()
    {
        try
        {
            await Spotify.ConnectAsync(WindowHandle);
        }
        catch (Exception ex)
        {
            Spotify.Status = Loc.T("spotify.connect_failed", ex.Message);
            ReportError(Spotify.Status);
        }
        UpdateLoginGate(); // an expired/revoked session signs out and brings the gate back
    }

    void SpotifyLogout()
    {
        if (Player.IsPlaying) Player.Toggle(); // pause while the token is still valid
        Spotify.Logout();
        CurrentPage = new HomePage(this);
        UpdateLoginGate();
    }

    void OnSpotifyLibraryChanged()
    {
        RefreshLibraryEntries();
        switch (CurrentPage)
        {
            case HomePage: CurrentPage = new HomePage(this); break;
            case TracksPage { Kind: "Playlist", IsSpecial: true } tp: _ = tp.Reload(); break;
        }
    }

    // ---------------- navigation ----------------

    public void Navigate(PageViewModel page)
    {
        _back.Add(CurrentPage);
        if (_back.Count > 50) _back.RemoveAt(0);
        _forward.Clear();
        CurrentPage = page;
    }

    void GoBack()
    {
        if (_back.Count == 0) return;
        _forward.Add(CurrentPage);
        CurrentPage = _back[^1];
        _back.RemoveAt(_back.Count - 1);
    }

    void GoForward()
    {
        if (_forward.Count == 0) return;
        _back.Add(CurrentPage);
        CurrentPage = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
    }

    public void NavigateBack() => GoBack();
    public void NavigateForward() => GoForward();

    void RefreshCurrentPage()
    {
        switch (CurrentPage)
        {
            case HomePage: CurrentPage = new HomePage(this); break;
            case AlbumsPage: CurrentPage = new AlbumsPage(this); break;
            case ArtistsPage: CurrentPage = new ArtistsPage(this); break;
            case TracksPage { ContextUri: null } tp: _ = tp.Reload(); break;
            case SearchPage sp: sp.Run(); break;
            case DiscoverPage: CurrentPage = new DiscoverPage(this); break;
        }
    }

    public void OpenAlbum(Album a)
    {
        var prefix = a.Year > 0 ? $"{a.Artist} • {a.Year} • " : $"{a.Artist} • ";
        var kind = a.IsShow ? "Podcast" : "Album";
        if (a.Uri != null)
            Navigate(new TracksPage(this, kind, a.Title, a, "", prefix, () => Spotify.LoadAlbumTracksAsync(a)) { ContextUri = a.Uri });
        else
            Navigate(new TracksPage(this, kind, a.Title, a, "", prefix, () => a.Tracks));
    }

    public void OpenArtist(Artist a)
    {
        if (a.Uri != null) Navigate(new ArtistPage(this, a, () => Spotify.LoadArtistAsync(a)));
        else Navigate(new ArtistPage(this, a));
    }

    public void OpenTrackArtist(Track t)
    {
        if (t.IsSpotify && t.ArtistUri != null)
            OpenArtist(Spotify.Catalog.ArtistFor(t.ArtistUri, t.Artist.Split(", ")[0]));
        else if (Library.GetArtist(t.PrimaryArtist) is { } a)
            OpenArtist(a);
    }

    public void OpenGenre(Genre g) => Navigate(new TracksPage(this, "Genre", g.Name, null, "", "", () => g.Tracks));

    public void OpenLiked()
    {
        if (Spotify.IsConnected)
            Navigate(new TracksPage(this, "Playlist", Loc.T("library.liked_songs"), Liked, "", "", () => State.Liked.Select(Library.Get).OfType<Track>().Concat(Spotify.LikedTracks)) // files liked in Sonora first: Spotify keeps none among its own
            { IsSpecial = true, ContextUri = Spotify.LikedContextUri });
        else
            OpenLocalLiked();
    }

    public void OpenLocalLiked() => Navigate(new TracksPage(this, "Playlist", Loc.T("library.liked_songs"),
        LocalLiked, "", "", () => State.Liked.Select(Library.Get).OfType<Track>()) { IsSpecial = true });

    public void OpenAllTracks() => Navigate(new TracksPage(this, "Bibliothèque", Loc.T("library.all_tracks"), AllTracks, "", "",
        () => Library.Tracks));

    public void OpenPlaylist(Playlist p) => Navigate(new TracksPage(this, "Playlist", p.Name, p, "", "",
        () => p.Paths.Select(Library.Get).OfType<Track>()) { Playlist = p }.RememberSort());

    public void OpenSpotifyPlaylist(SpotifyPlaylist p) => Navigate(new TracksPage(this, "Playlist", p.Name, p, "",
        string.IsNullOrEmpty(p.Owner) ? "" : p.Owner + " • ", async () => await ResolveLocalEntriesAsync(await Spotify.LoadPlaylistTracksAsync(p)))
    { ContextUri = p.Uri, SpotifyPlaylist = p }.RememberSort());

    // ---------------- local files inside Spotify playlists ----------------

    readonly Dictionary<string, Track> _linkedFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Spotify lists the local files somebody added to a playlist but cannot say where they are. Each one is looked for here,
    /// first among the files the user already pointed to, then in the library by title, artist and length. What is not found
    /// stays a placeholder: shown greyed, skipped when playing, until the user locates the file.
    /// </summary>
    async Task<PlaylistContent> ResolveLocalEntriesAsync(PlaylistContent content)
    {
        if (!content.Tracks.Any(t => t.LocalRef != null)) return content;
        var matcher = new LocalMatcher<Track>(Library.Tracks, t => new TrackKey(t.Title, t.Artist, t.DurationSec));
        var resolved = new List<Track>(content.Tracks.Count);
        foreach (var t in content.Tracks)
            resolved.Add(t.LocalRef == null ? t : await FindLocalFileAsync(t, matcher) ?? t);
        return content with { Tracks = resolved };
    }

    async Task<Track?> FindLocalFileAsync(Track entry, LocalMatcher<Track> matcher)
    {
        if (State.LocalLinks.TryGetValue(entry.LocalRef!, out var path) && File.Exists(path))
        {
            if (Library.Get(path) is { } known) return known;
            if (_linkedFiles.TryGetValue(path, out var cached)) return cached;
            try { return _linkedFiles[path] = await Library.ReadFileAsync(path); }
            catch { /* unreadable: fall through to the library */ }
        }
        return matcher.Find(new TrackKey(entry.Title, entry.Artist, entry.DurationSec));
    }

    /// <summary>Asks for the file a local-file entry stands for, remembers the choice, and shows the playlist again with it playable.</summary>
    public void LocateLocalEntry(Track entry)
    {
        if (entry.LocalRef == null) return;
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("dialog.locate_file", entry.Title),
            Filter = Loc.T("dialog.audio_files") + " (" + LibraryService.AudioFilter + ")|" + LibraryService.AudioFilter + "|" + Loc.T("dialog.all_files") + "|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        State.LocalLinks[entry.LocalRef] = dialog.FileName;
        ScheduleSave();
        if (CurrentPage is TracksPage page) _ = page.Reload();
        ReportError(Loc.T("playlist.file_located", entry.Title));
    }

    // ---------------- discover ----------------

    public void OpenTops() => Navigate(new TopsPage(this));

    public void OpenHistory() => Navigate(new TracksPage(this, "Découvrir", Loc.T("pages.listening_history"), null, "", "",
        Spotify.GetHistoryAsync));

    /// <summary>Latest releases of the followed artists; built once at sign-in, rebuilt if that first pass found none.</summary>
    public void OpenNewReleases() => Navigate(new AlbumGridPage(this, Loc.T("discover.new_releases"),
        Loc.T("discover.new_releases_empty"),
        async () => Spotify.NewReleases.Count > 0 ? Spotify.NewReleases : await Spotify.LoadNewReleasesAsync()));

    /// <summary>
    /// Artists listened to alongside this one, from MusicBrainz and ListenBrainz, then matched back to
    /// Spotify so they are playable. Spotify's own "related artists" is refused to third-party apps.
    /// </summary>
    public void OpenSimilarArtists(Artist artist) => Navigate(new ArtistGridPage(this,
        Loc.T("discover.similar_to", artist.Name),
        Loc.T("discover.similar_subtitle"),
        Loc.T("discover.similar_empty"),
        () => SimilarArtistsAsync(artist)));

    async Task<List<Artist>> SimilarArtistsAsync(Artist artist)
    {
        if (!State.Features.Discovery)
        {
            ReportError(Loc.T("discover.disabled"));
            return [];
        }
        var similar = await _discovery.SimilarArtistsAsync(artist.Name);
        if (similar.Count == 0 || !Spotify.IsConnected) return [];

        // Match the names back to Spotify, a few at a time, so they are playable rather than just listed.
        var found = new List<Artist>();
        foreach (var batch in similar.Take(18).Chunk(4))
        {
            var lookups = batch.Select(s => MatchArtistAsync(s.Name)).ToList();
            foreach (var match in await Task.WhenAll(lookups))
                if (match != null && found.All(a => a.Uri != match.Uri)) found.Add(match);
        }
        return found;
    }

    /// <summary>The Spotify artist whose name really is the one ListenBrainz named, or null.</summary>
    async Task<Artist?> MatchArtistAsync(string name)
    {
        try
        {
            var results = await Spotify.SearchAsync(name, CancellationToken.None);
            return results.Artists.FirstOrDefault(a => Sonora.Appearance.Features.SearchRank.Score(name, a.Name) >= 700);
        }
        catch
        {
            return null; // one name that will not resolve must not lose the rest
        }
    }

    public void OpenSavedEpisodes() => Navigate(new TracksPage(this, "Podcast", Loc.T("discover.saved_episodes"), null, "", "",
        Spotify.GetSavedEpisodesAsync));

    public void OpenShows() => Navigate(new AlbumGridPage(this, Loc.T("pages.podcasts"),
        Loc.T("discover.shows_empty"),
        () => Task.FromResult(Spotify.Shows)));

    public void OpenEntry(LibraryEntry e)
    {
        switch (e.Source)
        {
            case LikedSource: OpenLiked(); break;
            case LocalLikedSource: OpenLocalLiked(); break;
            case AllTracksSource: OpenAllTracks(); break;
            case Playlist p: OpenPlaylist(p); break;
            case SpotifyPlaylist sp: OpenSpotifyPlaylist(sp); break;
            case Album a: OpenAlbum(a); break;
            case Artist a: OpenArtist(a); break;
        }
    }

    // ---------------- playback ----------------

    public void PlayTracks(IReadOnlyList<Track> tracks, int index, string context, bool shuffle = false, string? contextUri = null)
    {
        if (tracks.Count == 0)
        {
            if (contextUri != null) Player.PlaySpotifyContext(contextUri, context);
            return;
        }
        if (shuffle) Player.Shuffle = true;
        Player.PlayContext(tracks, index, context, contextUri: contextUri);
    }

    void PlayAlbum(Album a)
    {
        if (a.Uri != null) Player.PlaySpotifyContext(a.Uri, a.Title);
        else PlayTracks(a.Tracks, 0, a.Title);
    }

    void PlayArtist(Artist a)
    {
        if (a.Uri != null) Player.PlaySpotifyContext(a.Uri, a.Name);
        else PlayTracks(a.Tracks, 0, a.Name);
    }

    void TogglePlay()
    {
        if (Player.Current == null)
        {
            if (Spotify.IsConnected && Spotify.LikedTracks.Count > 0)
                PlayTracks(Spotify.LikedTracks, -1, Loc.T("library.liked_songs"), contextUri: Spotify.LikedContextUri);
            else
                PlayTracks(Library.Tracks, -1, Loc.T("library.all_tracks"));
            return;
        }
        Player.Toggle();
    }

    // ---------------- optional features ----------------

    /// <summary>
    /// Applies a change to <see cref="AppState.Features"/>. FeatureSettings is a plain settings record with no
    /// change notification, so re-raising State invalidates every binding that walks through it.
    /// </summary>
    void ApplyFeature(Action change, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        change();
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(State));
        ScheduleSave();
        RefreshCurrentPage();
    }

    public bool FeatureLyrics
    {
        get => State.Features.Lyrics;
        set { if (State.Features.Lyrics != value) ApplyFeature(() => State.Features.Lyrics = value); }
    }

    public bool FeaturePodcasts
    {
        get => State.Features.Podcasts;
        set { if (State.Features.Podcasts != value) ApplyFeature(() => State.Features.Podcasts = value); }
    }

    public bool FeatureTops
    {
        get => State.Features.Tops;
        set { if (State.Features.Tops != value) ApplyFeature(() => State.Features.Tops = value); }
    }

    public bool FeatureHistory
    {
        get => State.Features.History;
        set { if (State.Features.History != value) ApplyFeature(() => State.Features.History = value); }
    }

    public bool FeatureNewReleases
    {
        get => State.Features.NewReleases;
        set { if (State.Features.NewReleases != value) ApplyFeature(() => State.Features.NewReleases = value); }
    }

    public bool FeatureListeningLog
    {
        get => State.Features.ListeningLog;
        set { if (State.Features.ListeningLog != value) ApplyFeature(() => State.Features.ListeningLog = value); }
    }

    // ---------------- language ----------------

    /// <summary>A choice in the language list. <paramref name="ShowBadge"/> marks a translation no native speaker has reviewed yet.</summary>
    public sealed record LanguageOption(string Code, string Name, bool ShowBadge, bool IsSelected);

    /// <summary>"Windows language" first, then every language Sonora speaks, with the chosen one marked.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions
    {
        get
        {
            var chosen = State.Language is { Length: > 0 } c ? c : LanguageCatalog.Auto;
            var list = new List<LanguageOption> { new(LanguageCatalog.Auto, Loc.T("settings.language_auto"), false, chosen == LanguageCatalog.Auto) };
            list.AddRange(LanguageCatalog.All().Select(l => new LanguageOption(l.Code, l.Name, !l.Reviewed, l.Code == chosen)));
            return list;
        }
    }

    public ICommand OpenTranslatingGuideCommand => new RelayCommand(() => OpenUrl(UpdatesViewModel.RepositoryUrl + "/blob/main/docs/TRANSLATING.md"));

    public ICommand SetLanguageCommand => _setLanguage ??= new RelayCommand(p => { if (p is string code) SetLanguage(code); });
    ICommand? _setLanguage;

    /// <summary>Switches the interface to a language (a code, or "auto" for the language of Windows) and remembers it.</summary>
    public void SetLanguage(string setting)
    {
        State.Language = setting;
        Loc.SetLanguage(LanguageResolver.Resolve(setting, App.WindowsLanguages())); // OnLanguageChanged runs from its event
        ScheduleSave();
        OnPropertyChanged(nameof(LanguageOptions));
    }

    /// <summary>Everything the interface says in words that is not bound straight to a text: written again in the new language.</summary>
    void OnLanguageChanged()
    {
        if (Library.Relocalize()) ApplyLibrary(Library.Tracks.ToList());
        Spotify.RefreshLanguage();
        Updates.RefreshLanguage();
        Layout.Refresh(); // the panels' titles
        RefreshLibraryEntries();
        OnPropertyChanged(string.Empty); // every text computed by this view model
        // The pages that are built once from texts are built again; the settings page stays as it is (its texts are bound live).
        switch (CurrentPage)
        {
            case HomePage: CurrentPage = new HomePage(this); break;
            case AlbumsPage: CurrentPage = new AlbumsPage(this); break;
            case ArtistsPage: CurrentPage = new ArtistsPage(this); break;
            case PlaylistsPage: CurrentPage = new PlaylistsPage(this); break;
            case DiscoverPage: CurrentPage = new DiscoverPage(this); break;
            case AppearancePage: _appearance = null; CurrentPage = new AppearancePage(this); break;
        }
    }

    public bool FeatureUpdateCheck
    {
        get => State.Features.UpdateCheck;
        set { if (State.Features.UpdateCheck != value) ApplyFeature(() => State.Features.UpdateCheck = value); }
    }

    public bool FeatureDiscovery
    {
        get => State.Features.Discovery;
        set { if (State.Features.Discovery != value) ApplyFeature(() => State.Features.Discovery = value); }
    }

    public bool FeatureRecentSearches
    {
        get => State.Features.KeepRecentSearches;
        set
        {
            if (State.Features.KeepRecentSearches == value) return;
            ApplyFeature(() =>
            {
                State.Features.KeepRecentSearches = value;
                if (!value) State.Features.RecentSearches.Clear(); // turning it off forgets what was kept
            });
        }
    }

    public bool FeatureTrackToast
    {
        get => State.Features.TrackToast;
        set { if (State.Features.TrackToast != value) ApplyFeature(() => State.Features.TrackToast = value); }
    }

    public int ToastSeconds
    {
        get => State.Features.ToastSeconds;
        set
        {
            var clamped = Math.Clamp(value, 2, 10);
            if (State.Features.ToastSeconds == clamped) return;
            ApplyFeature(() => State.Features.ToastSeconds = clamped);
        }
    }

    public bool MiniPlayerOnTop
    {
        get => State.Features.MiniPlayerOnTop;
        set
        {
            if (State.Features.MiniPlayerOnTop == value) return;
            ApplyFeature(() => State.Features.MiniPlayerOnTop = value);
            Sonora.Views.MiniPlayerWindow.ApplySettings(this);
        }
    }

    public double MiniPlayerOpacity
    {
        get => State.Features.MiniPlayerOpacity;
        set
        {
            var clamped = Math.Clamp(double.IsNaN(value) ? 1.0 : value, 0.4, 1.0);
            if (Math.Abs(State.Features.MiniPlayerOpacity - clamped) < 0.001) return;
            ApplyFeature(() => State.Features.MiniPlayerOpacity = clamped);
            Sonora.Views.MiniPlayerWindow.ApplySettings(this);
        }
    }

    public double LyricsScale
    {
        get => State.Features.LyricsScale;
        set
        {
            var clamped = Math.Clamp(double.IsNaN(value) ? 1.0 : value, 0.8, 1.6);
            if (Math.Abs(State.Features.LyricsScale - clamped) < 0.001) return;
            ApplyFeature(() => State.Features.LyricsScale = clamped);
        }
    }

    public string LyricsAlign
    {
        get => State.Features.LyricsAlign;
        set
        {
            if (!Sonora.Appearance.Features.FeatureSettings.Alignments.Contains(value) || State.Features.LyricsAlign == value) return;
            ApplyFeature(() => State.Features.LyricsAlign = value);
        }
    }

    /// <summary>Re-reads every feature switch after the settings are loaded from disk.</summary>
    void NotifyFeatures()
    {
        foreach (var p in new[] { nameof(FeatureLyrics), nameof(FeaturePodcasts), nameof(FeatureTops), nameof(FeatureHistory),
                     nameof(FeatureNewReleases), nameof(FeatureRecentSearches), nameof(FeatureTrackToast), nameof(FeatureListeningLog), nameof(FeatureDiscovery), nameof(FeatureUpdateCheck),
                     nameof(ToastSeconds), nameof(MiniPlayerOnTop), nameof(MiniPlayerOpacity),
                     nameof(LyricsScale), nameof(LyricsAlign),
                     nameof(LibrarySort) })
            OnPropertyChanged(p);
    }

    // ---------------- keyboard map ----------------

    /// <summary>One row of the shortcuts list in the settings.</summary>
    public sealed class ShortcutRow(string id, string label, string gesture, bool isDefault)
    {
        public string Id { get; } = id;
        public string Label { get; } = label;
        public string Gesture { get; } = gesture.Length > 0 ? gesture : Loc.T("shortcut.none");
        public bool IsCustom { get; } = !isDefault;
    }

    public List<ShortcutRow> ShortcutRows => Sonora.Appearance.Features.Shortcuts.Actions.Select(a =>
    {
        var gesture = State.Features.Shortcuts.Gesture(a.Id);
        return new ShortcutRow(a.Id, a.Label, gesture, gesture == a.Default);
    }).ToList();

    /// <summary>The action waiting for a key press, or null when nothing is being rebound.</summary>
    public string? CapturingShortcut
    {
        get => _capturing;
        private set { if (Set(ref _capturing, value)) OnPropertyChanged(nameof(IsCapturingShortcut)); }
    }
    string? _capturing;

    public bool IsCapturingShortcut => _capturing != null;

    public ICommand CaptureShortcutCommand => _captureShortcut ??= new RelayCommand(p =>
    {
        CapturingShortcut = p as string;
        if (_capturing != null) ReportError(Loc.T("shortcut.capture_prompt"));
    });
    ICommand? _captureShortcut;

    public ICommand ResetShortcutsCommand => _resetShortcuts ??= new RelayCommand(() =>
    {
        State.Features.Shortcuts.ResetAll();
        CapturingShortcut = null;
        OnPropertyChanged(nameof(ShortcutRows));
        ScheduleSave();
        ReportError(Loc.T("shortcut.reset_done"));
    });
    ICommand? _resetShortcuts;

    /// <summary>Takes the pressed combination for the action being rebound. Returns true when it did.</summary>
    public bool ApplyCapturedShortcut(string? gesture)
    {
        if (_capturing is not { } action) return false;
        CapturingShortcut = null;
        if (gesture is { Length: > 0 })
        {
            State.Features.Shortcuts.Bind(action, gesture);
            ScheduleSave();
            ReportError(Loc.T("shortcut.set_done", gesture));
        }
        else ReportError(Loc.T("shortcut.cancelled"));
        OnPropertyChanged(nameof(ShortcutRows));
        return true;
    }

    // ---------------- sleep timer ----------------

    /// <summary>Stops playback after a delay, or at the end of the current track.</summary>
    public Sonora.Appearance.Features.SleepTimer Sleep { get; } = new();

    public bool IsSleepActive => Sleep.IsActive;
    public string SleepStatus => Sleep.Describe(DateTime.Now);

    public void StartSleep(int minutes)
    {
        Sleep.StartMinutes(minutes, DateTime.Now);
        _sleepTimer.Start();
        NotifySleep();
        ReportError(SleepStatus);
    }

    public void StartSleepAtTrackEnd()
    {
        Sleep.StartAtTrackEnd();
        _sleepTimer.Start();
        NotifySleep();
        ReportError(SleepStatus);
    }

    public void CancelSleep()
    {
        Sleep.Cancel();
        _sleepTimer.Stop();
        NotifySleep();
        ReportError(Loc.T("sleep.cancelled"));
    }

    void OnSleepTick()
    {
        if (!Sleep.IsActive) { _sleepTimer.Stop(); NotifySleep(); return; }
        if (Sleep.ShouldStop(DateTime.Now)) StopForSleep();
        else NotifySleep(); // keeps the remaining minutes fresh in the menu and the player bar
    }

    void StopForSleep()
    {
        _sleepTimer.Stop();
        if (Player.IsPlaying) Player.Toggle();
        NotifySleep();
        ReportError(Loc.T("sleep.paused"));
    }

    void NotifySleep()
    {
        OnPropertyChanged(nameof(IsSleepActive));
        OnPropertyChanged(nameof(SleepStatus));
    }

    // ---------------- follow / save ----------------

    /// <summary>True when the album, show, artist or playlist is already in the Spotify library.</summary>
    public bool IsSaved(object? subject) => Spotify.IsSaved(subject switch
    {
        Album a => a.Uri,
        Artist a => a.Uri,
        SpotifyPlaylist p => p.Uri,
        _ => null,
    });

    public async Task SetSavedAsync(object subject, bool save)
    {
        try
        {
            switch (subject)
            {
                case Album a: await Spotify.SetAlbumSavedAsync(a, save); break;
                case Artist a: await Spotify.SetArtistFollowedAsync(a, save); break;
                case SpotifyPlaylist p: await Spotify.SetPlaylistFollowedAsync(p, save); break;
                default: return;
            }
            ReportError(Loc.T(save ? "library.added_to_library" : "library.removed_from_library"));
        }
        catch (Exception ex) { ReportError(ex.Message); }
    }

    public Task<List<SpotifyDevice>> GetDevicesAsync() => Spotify.Engine.GetDevicesAsync();

    public async Task TransferToAsync(SpotifyDevice device)
    {
        try { await Spotify.Engine.TransferAsync(device.Id, Player.IsPlaying); }
        catch (Exception ex) { ReportError(ex.Message); }
    }

    // ---------------- likes & playlists ----------------

    public void ToggleLike(Track t)
    {
        if (t.IsSpotify)
        {
            _ = Guard(Spotify.SetLikedAsync(t, !t.IsLiked));
            return;
        }
        if (_liked.Remove(t.Path))
        {
            State.Liked.Remove(t.Path);
            t.IsLiked = false;
        }
        else
        {
            _liked.Add(t.Path);
            State.Liked.Insert(0, t.Path);
            t.IsLiked = true;
        }
        RefreshLibraryEntries();
        if (CurrentPage is TracksPage { IsSpecial: true } tp && (tp.ArtSource == Liked || tp.ArtSource == LocalLiked)) _ = tp.Reload();
        ScheduleSave();
    }

    public Playlist CreatePlaylist(Track? first = null)
    {
        var p = new Playlist { Name = Loc.T("playlist.default_name", Playlists.Count + 1) };
        if (first != null) p.Paths.Add(first.Path);
        Playlists.Add(p);
        RefreshPlaylistArt();
        RefreshLibraryEntries();
        ScheduleSave();
        return p;
    }

    public void AddToPlaylist(Playlist p, IEnumerable<Track> tracks)
    {
        p.Paths.AddRange(tracks.Where(t => !t.IsSpotify).Select(t => t.Path));
        PlaylistChanged(p);
    }

    /// <summary>Adds Spotify tracks to a playlist of the user's, creating one (called <paramref name="newName"/>) when none is given. Null on failure.</summary>
    public async Task<SpotifyPlaylist?> AddToSpotifyPlaylistAsync(SpotifyPlaylist? p, IReadOnlyList<Track> tracks, string? newName = null)
    {
        try
        {
            p ??= await Spotify.CreatePlaylistAsync(newName ?? Loc.T("playlist.default_name", Spotify.OwnedPlaylists.Count + 1));
            await Spotify.AddToPlaylistAsync(p, tracks);
            ReportError(Loc.T("playlist.added_to", p.Name));
            if (CurrentPage is TracksPage { SpotifyPlaylist: { } shown } tp && shown.Id == p.Id) _ = tp.Reload();
            return p;
        }
        catch (Exception ex) { ReportError(ex.Message); return null; }
    }

    /// <summary>Moves one track of a local playlist so it lands at <paramref name="to"/>.</summary>
    public void ReorderPlaylist(Playlist p, int from, int to)
    {
        if (from < 0 || from >= p.Paths.Count) return;
        to = Math.Clamp(to, 0, p.Paths.Count - 1);
        if (from == to) return;
        var path = p.Paths[from];
        p.Paths.RemoveAt(from);
        p.Paths.Insert(to, path);
        PlaylistChanged(p);
    }

    /// <summary>Moves one track of a Spotify playlist. Spotify may refuse it for a third-party app.</summary>
    public async Task ReorderSpotifyPlaylistAsync(SpotifyPlaylist p, int from, int to)
    {
        try
        {
            // Spotify inserts *before* a position, so moving down has to account for the gap left behind.
            await Spotify.ReorderPlaylistAsync(p, from, to > from ? to + 1 : to);
            if (CurrentPage is TracksPage { SpotifyPlaylist: { } shown } tp && shown.Id == p.Id) await tp.Reload();
        }
        catch (Exception ex) { ReportError(ex.Message); }
    }

    /// <summary>Changes a Spotify playlist's name, description, visibility or collaboration.</summary>
    public async Task SetPlaylistDetailsAsync(SpotifyPlaylist p, string? name = null, string? description = null,
        bool? isPublic = null, bool? collaborative = null)
    {
        try
        {
            await Spotify.SetPlaylistDetailsAsync(p, name, description, isPublic, collaborative);
            if (CurrentPage is TracksPage tp && tp.SpotifyPlaylist == p && name != null) tp.Header = name;
            ReportError(Loc.T("playlist.updated"));
        }
        catch (Exception ex) { ReportError(ex.Message); }
    }

    /// <summary>Picks an image and sets it as a Spotify playlist's cover.</summary>
    public async Task SetPlaylistCoverAsync(SpotifyPlaylist p)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("dialog.choose_cover"),
            Filter = Loc.T("dialog.images") + " (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp|" + Loc.T("dialog.all_files") + "|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        var jpeg = CoverImage.ToSquareJpeg(dialog.FileName);
        if (jpeg == null)
        {
            ReportError(Loc.T("playlist.cover_unreadable"));
            return;
        }
        try
        {
            await Spotify.SetPlaylistCoverAsync(p, jpeg);
            ReportError(Loc.T("playlist.cover_updated"));
        }
        catch (Exception ex) { ReportError(ex.Message); }
    }

    public void RemoveFromPlaylist(Playlist p, int index)
    {
        if (index < 0 || index >= p.Paths.Count) return;
        p.Paths.RemoveAt(index);
        PlaylistChanged(p);
    }

    public void RenamePlaylist(Playlist p, string name)
    {
        p.Name = name;
        if (CurrentPage is TracksPage tp && tp.Playlist == p) tp.Header = name;
        RefreshLibraryEntries();
        ScheduleSave();
    }

    // ---------------- import / export ----------------

    /// <summary>
    /// Writes a list to a file the user names. A Sonora playlist file (the default) keeps every track, Spotify ones included;
    /// an M3U, for other players, can only point at files, so the Spotify tracks are left out of it.
    /// </summary>
    public void ExportPlaylist(IReadOnlyList<Track> tracks, string suggestedName)
    {
        if (tracks.Count == 0)
        {
            ReportError(Loc.T("playlist.nothing_to_export"));
            return;
        }
        var ext = PlaylistFile.Extension;
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("dialog.export_playlist"),
            FileName = Sanitize(suggestedName) + ext,
            DefaultExt = ext,
            Filter = Loc.T("dialog.sonora_playlist") + " (*" + ext + ")|*" + ext + "|" + Loc.T("dialog.m3u_playlist") + " (*.m3u8;*.m3u)|*.m3u8;*.m3u",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (Path.GetExtension(dialog.FileName).StartsWith(".m3u", StringComparison.OrdinalIgnoreCase))
                ExportM3u(tracks, dialog.FileName);
            else
            {
                var entries = tracks.Select(t => t.IsSpotify
                    ? new PlaylistFileEntry(t.Title, t.Artist, t.Album, t.DurationSec, Spotify: t.Uri)
                    : new PlaylistFileEntry(t.Title, t.Artist, t.Album, t.DurationSec, Path: t.Path.Length > 0 ? t.Path : null));
                File.WriteAllText(dialog.FileName, PlaylistFile.Write(suggestedName, entries), new System.Text.UTF8Encoding(false));
                ReportError(Loc.Count("count.tracks_exported", tracks.Count));
            }
        }
        catch (Exception ex) { ReportError(Loc.T("m3u.export_failed", ex.Message)); }
    }

    void ExportM3u(IReadOnlyList<Track> tracks, string file)
    {
        var local = tracks.Where(t => !t.IsSpotify && t.Path.Length > 0).ToList();
        if (local.Count == 0)
        {
            ReportError(Loc.T("m3u.nothing_to_export"));
            return;
        }
        var text = M3u.Write(local.Select(t => new M3uEntry(t.Path, t.Title, t.Artist, t.DurationSec)));
        File.WriteAllText(file, text, new System.Text.UTF8Encoding(false));
        var skipped = tracks.Count - local.Count;
        ReportError(skipped > 0
            ? Loc.Count("m3u.exported_with_skipped", local.Count, skipped)
            : Loc.Count("count.tracks_exported", local.Count));
    }

    /// <summary>Reads a Sonora playlist file or an M3U into a new playlist.</summary>
    public async Task ImportPlaylistAsync()
    {
        var all = "*" + PlaylistFile.Extension + ";*.m3u8;*.m3u";
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("dialog.import_playlist"),
            Filter = Loc.T("dialog.playlists") + " (" + all + ")|" + all + "|" + Loc.T("dialog.all_files") + "|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (Path.GetExtension(dialog.FileName).Equals(PlaylistFile.Extension, StringComparison.OrdinalIgnoreCase))
                await ImportSonoraFileAsync(dialog.FileName);
            else
                ImportM3u(dialog.FileName);
        }
        catch (Exception ex) { ReportError(Loc.T("m3u.import_failed", ex.Message)); }
    }

    /// <summary>
    /// A file's local tracks go to a new local playlist (found by path, else by title, artist and length in the library),
    /// its Spotify tracks to a new playlist in the user's account. Each of the two is only made when it has something in it.
    /// </summary>
    async Task ImportSonoraFileAsync(string path)
    {
        var file = PlaylistFile.Parse(File.ReadAllText(path));
        var name = file.Name.Trim().Length > 0 ? file.Name.Trim() : M3u.NameFromFile(path);
        var matcher = new LocalMatcher<Track>(Library.Tracks, t => new TrackKey(t.Title, t.Artist, t.DurationSec));

        var local = new List<Track>();
        var spotify = new List<Track>();
        int notFound = 0;
        foreach (var e in file.Tracks)
        {
            if (e.IsSpotify)
            {
                // The file is not trusted: only well-formed track and episode ids may reach Spotify.
                if (PlaylistFile.IsSpotifyItemUri(e.Spotify)) spotify.Add(new Track { Uri = e.Spotify, Path = e.Spotify!, Title = e.Title, Artist = e.Artist });
                else notFound++;
                continue;
            }
            var found = (e.Path is { Length: > 0 } ? Library.Get(e.Path) : null) ?? matcher.Find(new TrackKey(e.Title, e.Artist, e.Seconds));
            if (found != null) local.Add(found); else notFound++;
        }

        if (local.Count == 0 && spotify.Count == 0)
        {
            ReportError(Loc.T("m3u.none_found"));
            return;
        }

        Playlist? created = null;
        if (local.Count > 0)
        {
            created = CreatePlaylist();
            RenamePlaylist(created, name);
            AddToPlaylist(created, local);
        }

        SpotifyPlaylist? remote = null;
        bool needsSpotify = spotify.Count > 0 && !Spotify.IsConnected;
        if (spotify.Count > 0 && !needsSpotify) remote = await AddToSpotifyPlaylistAsync(null, spotify, name);

        int imported = local.Count + (remote != null ? spotify.Count : 0);
        int left = notFound + (remote == null ? spotify.Count : 0);
        ReportError(needsSpotify
            ? Loc.T("playlist.imported_needs_spotify", name, imported, spotify.Count)
            : Loc.T("playlist.imported_summary", name, imported, left));

        if (created != null) OpenPlaylist(created);
        else if (remote != null) OpenSpotifyPlaylist(remote);
    }

    /// <summary>Reads an .m3u/.m3u8 into a new local playlist, keeping the tracks the library knows.</summary>
    void ImportM3u(string file)
    {
        var content = File.ReadAllText(file);
        var paths = M3u.Parse(content, Path.GetDirectoryName(file));
        var found = paths.Select(Library.Get).OfType<Track>().ToList();
        if (found.Count == 0)
        {
            ReportError(Loc.T("m3u.none_found"));
            return;
        }
        var playlist = CreatePlaylist();
        RenamePlaylist(playlist, M3u.NameFromFile(file));
        AddToPlaylist(playlist, found);
        var missing = paths.Count - found.Count;
        ReportError(missing > 0
            ? Loc.Count("m3u.imported_with_missing", found.Count, missing)
            : Loc.Count("count.tracks_imported", found.Count));
        OpenPlaylist(playlist);
    }

    /// <summary>Strips what Windows will not accept in a file name.</summary>
    static string Sanitize(string name)
    {
        var clean = new string((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return clean.Length > 0 ? clean : "playlist";
    }

    public void DeletePlaylist(Playlist p)
    {
        Playlists.Remove(p);
        if (CurrentPage is TracksPage tp && tp.Playlist == p) CurrentPage = new HomePage(this);
        _back.RemoveAll(x => x is TracksPage t && t.Playlist == p);
        _forward.RemoveAll(x => x is TracksPage t && t.Playlist == p);
        RefreshLibraryEntries();
        ScheduleSave();
    }

    void PlaylistChanged(Playlist p)
    {
        p.Touch();
        RefreshPlaylistArt();
        if (CurrentPage is TracksPage tp && tp.Playlist == p) _ = tp.Reload();
        RefreshLibraryEntries();
        ScheduleSave();
    }

    void RefreshPlaylistArt()
    {
        foreach (var p in Playlists)
            p.ArtPath = p.Paths.Select(Library.Get).Select(t => t?.AlbumRef?.ArtPath).FirstOrDefault(a => a != null);
    }

    void RefreshLibraryEntries()
    {
        var entries = new List<LibraryEntry>();
        var connected = Spotify.IsConnected;
        entries.Add(new LibraryEntry
        {
            Provider = connected ? LibrarySources.Spotify : LibrarySources.Local,
            Source = Liked, Title = Loc.T("library.liked_songs"), IsSpecial = true, Glyph = "",
            Subtitle = Loc.T("library.subtitle_playlist", Loc.Count("count.tracks", connected ? Spotify.LikedTracks.Count + _liked.Count : _liked.Count)),
        });
        switch (LibraryFilter)
        {
            case "Albums":
                foreach (var a in Spotify.SavedAlbums.Concat(Library.Albums))
                    entries.Add(new LibraryEntry { Provider = a.Uri != null ? LibrarySources.Spotify : LibrarySources.Local, Source = a, Title = a.Title, Subtitle = Loc.T("library.subtitle_album", a.Artist), Glyph = "" });
                break;
            case "Artists":
                foreach (var a in Spotify.Artists.Concat(Library.Artists))
                    entries.Add(new LibraryEntry { Provider = a.Uri != null ? LibrarySources.Spotify : LibrarySources.Local, Source = a, Title = a.Name, Subtitle = Loc.T("kind.artist"), Glyph = "", IsRound = true });
                break;
            default:
                foreach (var p in Spotify.Playlists)
                    entries.Add(new LibraryEntry { Provider = LibrarySources.Spotify, Source = p, Title = p.Name, Subtitle = p.Subtitle, Glyph = "" });
                if (Library.Tracks.Count > 0)
                    entries.Add(new LibraryEntry
                    {
                        Source = AllTracks, Title = Loc.T("library.all_tracks"), Glyph = "",
                        Subtitle = Loc.T("library.subtitle_local_files", Loc.Count("count.tracks", Library.Tracks.Count)),
                    });
                foreach (var p in Playlists)
                    entries.Add(new LibraryEntry { Source = p, Title = p.Name, Subtitle = Loc.T("library.subtitle_local", p.Subtitle), Glyph = "" });
                break;
        }
    
        // The source tab first, then search, pins, and the chosen order. OrderBy is stable, so "recent" keeps the order built above.
        var selectedSource = RefreshLibrarySourceTabs(connected);
        var tokens = Sonora.Appearance.Features.ListQuery.Fold(_librarySearch)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // "Liked songs" holds files and Spotify tracks together, so it stays on every tab.
        IEnumerable<LibraryEntry> shown = entries.Where(e => e.Source is LikedSource || LibrarySources.Shows(selectedSource, e.Provider));
        if (tokens.Length > 0)
            shown = shown.Where(e =>
            {
                var key = Sonora.Appearance.Features.ListQuery.Fold(e.Title + " " + e.Subtitle);
                return tokens.All(t => key.Contains(t, StringComparison.Ordinal));
            });

        var list = shown.ToList();
        foreach (var e in list) e.IsPinned = State.Features.IsPinned(e.Key);
        // Pins keep the order the user gave them by dragging, whichever sort the rest follows.
        IEnumerable<LibraryEntry> rest = list.Where(e => !e.IsPinned);
        if (State.Features.LibrarySort == "alpha")
            rest = rest.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase);
        var ordered = list.Where(e => e.IsPinned).OrderBy(e => State.Features.PinIndex(e.Key)).Concat(rest);

        LibraryEntries.Clear();
        foreach (var e in ordered) LibraryEntries.Add(e);
        UpdatePlayingContext();
    }

    /// <summary>
    /// Offers a tab for each source that is available (and "all" in front) when there is more than one, and returns the one
    /// selected. A source that went away (Spotify signed out) drops its tab and the panel falls back to "all".
    /// </summary>
    string RefreshLibrarySourceTabs(bool spotifyConnected)
    {
        var available = new List<(string Id, string Title)> { (LibrarySources.Local, Loc.T("library.source_local")) };
        if (spotifyConnected) available.Add((LibrarySources.Spotify, "Spotify"));

        var selected = LibrarySources.Resolve(State.Features.LibrarySource, available.Select(a => a.Id));
        var tabs = available.Count < 2
            ? []
            : new List<LibrarySourceTab> { new(LibrarySources.All, Loc.T("library.source_all"), selected == LibrarySources.All, SelectLibrarySource) }
                .Concat(available.Select(a => new LibrarySourceTab(a.Id, a.Title, selected == a.Id, SelectLibrarySource))).ToList();

        if (!tabs.SequenceEqual(LibrarySourceTabs))
        {
            LibrarySourceTabs.Clear();
            foreach (var t in tabs) LibrarySourceTabs.Add(t);
            OnPropertyChanged(nameof(ShowLibrarySourceTabs));
        }
        return selected;
    }

    /// <summary>Marks the library entry the current track is played from, so it stands out wherever the library is shown.</summary>
    void UpdatePlayingContext()
    {
        var uri = Player.ContextUri;
        var name = Player.ContextName;
        foreach (var e in LibraryEntries)
        {
            int state = PlayingContext.State(e.Key, e.Title, uri, name, Player.IsPlaying, Player.HasTrack);
            e.PlayState = state > 0 && !PlayingContext.Owns(e.Source, Player.Current) ? 0 : state;
        }
    }

    // ---------------- folders ----------------

    void AddFolder()
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("dialog.choose_music_folder"), Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        bool changed = false;
        foreach (var f in dlg.FolderNames)
            if (!Folders.Contains(f, StringComparer.OrdinalIgnoreCase)) { Folders.Add(f); changed = true; }
        if (!changed) return;
        ScheduleSave();
        _ = RescanAsync();
    }

    void RemoveFolder(string folder)
    {
        if (!Folders.Remove(folder)) return;
        ScheduleSave();
        _ = RescanAsync();
    }
}
