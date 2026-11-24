using System.Collections.ObjectModel;
using System.Windows.Threading;
using Sonora.Core;
using Sonora.Models;
using Sonora.Spotify;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using WinMediaPlayer = Windows.Media.Playback.MediaPlayer;
using Sonora.Appearance.Localization;

namespace Sonora.Services;

/// <summary>
/// Single player facade over two engines:
/// - local files: Windows Media Foundation (hardware decoding, shared WASAPI output, media keys via SMTC);
///   Sonora owns the context, shuffle order, repeat mode and "add to queue" list;
/// - Spotify: the official Web Playback SDK / Web API (see <see cref="SpotifyEngine"/>); Spotify owns the
///   queue and Sonora mirrors its state.
/// </summary>
public sealed class PlayerService : ObservableObject, IDisposable
{
    readonly WinMediaPlayer _mp;
    readonly SystemMediaTransportControls _smtc;
    readonly Dispatcher _ui;
    readonly DispatcherTimer _timer;
    SpotifyEngine? _spotify;
    int _artToken;

    List<Track> _context = [];
    List<int> _order = [];
    int _pos = -1;
    readonly List<Track> _userQueue = [];
    int _loadToken;
    int _failures;
    bool _wantPlay;
    double _pendingSeek;

    /// <summary>A Spotify track shown again after a restart but not started: pressing play starts it from here.</summary>
    sealed record SpotifyResume(string? ContextUri, string TrackUri, double PositionSec);
    SpotifyResume? _resume;

    // Spotify mode
    bool _spotifyMode;
    string? _spotifyContextUri;

    /// <summary>
    /// A list that holds both local files and Spotify tracks (a Spotify playlist with local files in it). Spotify cannot
    /// play a local file and the local engine cannot play Spotify, so here Sonora owns the order and plays the entries one
    /// at a time, each on the engine it belongs to. <see cref="_spotifyMode"/> then only says what the current track plays on.
    /// </summary>
    bool _mixed;
    int _endedToken = -1; // the load whose end has already been acted on, so one end advances once

    /// <summary>The Spotify URI of what is playing from (album, playlist, artist), or null for a list of tracks.</summary>
    public string? ContextUri => _spotifyContextUri;

    void SetContextUri(string? uri)
    {
        if (_spotifyContextUri == uri) return;
        _spotifyContextUri = uri;
        OnPropertyChanged(nameof(ContextUri));
    }
    double _posBase;
    DateTime _posAtUtc;

    public ObservableCollection<TrackRow> Upcoming { get; } = [];
    public event Action<Track>? TrackStarted;

    /// <summary>The outgoing track and how far it got, raised just before the next one takes over.</summary>
    public event Action<Track, double>? TrackFinished;
    public event Action<string>? Error;

    public PlayerService(Dispatcher ui)
    {
        _ui = ui;
        _mp = new WinMediaPlayer { AutoPlay = false, AudioCategory = MediaPlayerAudioCategory.Media, Volume = 0.7 };

        // Sonora drives the Windows media controls itself rather than letting MediaPlayer do it: the automatic
        // integration only knows about MediaPlayer's own source, so the overlay and the media keys stayed dead
        // for every Spotify track. Manual control covers both engines with the same code.
        _mp.CommandManager.IsEnabled = false;
        _smtc = _mp.SystemMediaTransportControls;
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        _smtc.ButtonPressed += OnSystemMediaButton;

        _mp.MediaEnded += (_, _) => _ui.InvokeAsync(() => { if (!_spotifyMode) Advance(auto: true); });
        _mp.MediaFailed += (_, e) => _ui.InvokeAsync(() => OnFailed(e.ErrorMessage));
        _mp.MediaOpened += (_, _) => _ui.InvokeAsync(() =>
        {
            _failures = 0;
            var d = _mp.PlaybackSession.NaturalDuration.TotalSeconds;
            if (d > 0 && !_spotifyMode) DurationSec = d;
            if (_pendingSeek > 0 && !_spotifyMode)
            {
                // A track restored after a restart: the source is open now, so the position can be set.
                _mp.PlaybackSession.Position = TimeSpan.FromSeconds(d > 0 ? Math.Min(_pendingSeek, d) : _pendingSeek);
                _pendingSeek = 0;
            }
        });
        _mp.PlaybackSession.PlaybackStateChanged += (s, _) =>
        {
            var st = s.PlaybackState;
            _ui.InvokeAsync(() =>
            {
                if (_spotifyMode) return;
                IsPlaying = st == MediaPlaybackState.Playing
                            || (_wantPlay && st is MediaPlaybackState.Opening or MediaPlaybackState.Buffering);
            });
        };

        _timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();

        // Spotify's queue changes from the phone, the official app, or the shuffle switch, none of which reach
        // us as an event: while Spotify drives, ask for it every few seconds.
        _queueTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(4) };
        _queueTimer.Tick += (_, _) => _ = Run(RefreshSpotifyQueueAsync());
    }

    readonly DispatcherTimer _queueTimer;
    bool _queueBusy;

    // ---------------- Windows media controls (overlay, media keys, headset buttons) ----------------

    void OnSystemMediaButton(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var button = args.Button;
        _ui.InvokeAsync(() =>
        {
            switch (button)
            {
                case SystemMediaTransportControlsButton.Play when !IsPlaying: Toggle(); break;
                case SystemMediaTransportControlsButton.Pause when IsPlaying: Toggle(); break;
                case SystemMediaTransportControlsButton.Next: Next(); break;
                case SystemMediaTransportControlsButton.Previous: Previous(); break;
            }
        });
    }

    /// <summary>Publishes the current track to Windows. Cosmetic, so every failure here is swallowed.</summary>
    void UpdateSystemMedia()
    {
        try
        {
            _smtc.PlaybackStatus = _current == null
                ? MediaPlaybackStatus.Closed
                : IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

            var updater = _smtc.DisplayUpdater;
            if (_current is not { } t)
            {
                updater.ClearAll();
                updater.Update();
                return;
            }
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = t.Title;
            updater.MusicProperties.Artist = t.Artist;
            updater.MusicProperties.AlbumTitle = t.Album;
            updater.Thumbnail = null; // the old cover must not linger while the new one loads
            updater.Update();
            UpdateSystemMediaTimeline();
            _ = ApplyThumbnailAsync(t, ++_artToken);
        }
        catch
        {
            // the overlay is never worth breaking playback for
        }
    }

    /// <summary>Covers arrive late (a Spotify URL, a cached file): a token drops the ones that lost the race.</summary>
    async Task ApplyThumbnailAsync(Track t, int token)
    {
        try
        {
            if (t.AlbumRef?.ArtPath is not { Length: > 0 } art) return;
            RandomAccessStreamReference? thumbnail;
            if (art.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(art));
            else if (File.Exists(art))
                thumbnail = RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(art));
            else
                return;

            if (token != _artToken || !ReferenceEquals(t, _current)) return;
            _smtc.DisplayUpdater.Thumbnail = thumbnail;
            _smtc.DisplayUpdater.Update();
        }
        catch
        {
            // no cover: the overlay just shows the text
        }
    }

    void UpdateSystemMediaTimeline()
    {
        try
        {
            var end = TimeSpan.FromSeconds(Math.Max(0, _duration));
            _smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                Position = TimeSpan.FromSeconds(Math.Clamp(_position, 0, Math.Max(0, _duration))),
                MaxSeekTime = end,
                EndTime = end,
            });
        }
        catch
        {
            // same: cosmetic only
        }
    }

    public void AttachSpotify(SpotifyEngine engine)
    {
        _spotify = engine;
        engine.StateChanged += s => _ui.InvokeAsync(() => OnSpotifyState(s));
        engine.Ready += () => _ui.InvokeAsync(() => _ = Run(engine.SetVolumeAsync(_muted ? 0 : _volume)));
    }

    // ---------------- state ----------------

    Track? _current;
    public Track? Current
    {
        get => _current;
        private set
        {
            if (_current == value) return;
            if (_current != null) _current.IsCurrent = false;
            Set(ref _current, value);
            if (value != null) value.IsCurrent = true;
            OnPropertyChanged(nameof(HasTrack));
            OnPropertyChanged(nameof(ContextLabel));
            UpdateSystemMedia();
        }
    }

    public bool HasTrack => _current != null;

    bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (!Set(ref _isPlaying, value)) return;
            if (value) _timer.Start(); else { _timer.Stop(); Tick(); }
            UpdateSystemMedia();
        }
    }

    double _duration;
    public double DurationSec
    {
        get => _duration;
        private set
        {
            if (!Set(ref _duration, value)) return;
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(SliderMax));
            UpdateSystemMediaTimeline();
        }
    }

    /// <summary>Never 0, so an empty player shows an empty (not full) progress bar.</summary>
    public double SliderMax => Math.Max(1, _duration);

    /// <summary>Position as a 0..1 share of the track, for the taskbar thumbnail's progress bar.</summary>
    public double Progress => _duration > 0 ? Math.Clamp(_position / _duration, 0, 1) : 0;

    double _position;
    double _pending;
    public bool IsSeeking { get; set; }

    /// <summary>Bound two-way to the seek slider.</summary>
    public double SeekValue
    {
        get => IsSeeking ? _pending : _position;
        set
        {
            if (IsSeeking) { _pending = value; OnPropertyChanged(nameof(PositionText)); return; }
            if (Math.Abs(value - _position) > 0.4) Seek(value);
        }
    }

    public string PositionText => Fmt.Time(IsSeeking ? _pending : _position);
    public string DurationText => Fmt.Time(_duration);

    double _volume = 70;
    public double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (!Set(ref _volume, value)) return;
            _mp.Volume = value / 100.0;
            if (value > 0 && _muted) { _muted = false; _mp.IsMuted = false; OnPropertyChanged(nameof(IsMuted)); }
            OnPropertyChanged(nameof(VolumeGlyph));
            PushSpotifyVolume();
        }
    }

    DispatcherTimer? _volumeDebounce;

    /// <summary>Instant for Sonora's own device; debounced when it means a Web API call to a remote device.</summary>
    void PushSpotifyVolume()
    {
        if (_spotify == null || !(_spotifyMode || _spotify.IsTargetSonora)) return;
        if (_spotify.IsTargetSonora) { _ = Run(_spotify.SetVolumeAsync(_muted ? 0 : _volume)); return; }
        if (_volumeDebounce == null)
        {
            _volumeDebounce = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromMilliseconds(250) };
            _volumeDebounce.Tick += (_, _) => { _volumeDebounce.Stop(); _ = Run(_spotify.SetVolumeAsync(_muted ? 0 : _volume)); };
        }
        _volumeDebounce.Stop();
        _volumeDebounce.Start();
    }

    bool _muted;
    public bool IsMuted
    {
        get => _muted;
        set
        {
            if (!Set(ref _muted, value)) return;
            _mp.IsMuted = value;
            OnPropertyChanged(nameof(VolumeGlyph));
            PushSpotifyVolume();
        }
    }

    public string VolumeGlyph => _muted || _volume <= 0 ? "" : _volume < 33 ? "" : _volume < 66 ? "" : "";

    bool _shuffle;
    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (!Set(ref _shuffle, value)) return;
            if (_spotifyMode && !_mixed && _spotify != null) { _ = Run(ChangeAndRefreshQueueAsync(_spotify.SetShuffleAsync(value))); return; }
            if (_pos >= 0 && _pos < _order.Count) BuildOrder(_order[_pos]);
            RefreshUpcoming();
        }
    }

    RepeatMode _repeat;
    public RepeatMode Repeat
    {
        get => _repeat;
        set
        {
            if (!Set(ref _repeat, value)) return;
            OnPropertyChanged(nameof(RepeatGlyph));
            OnPropertyChanged(nameof(RepeatActive));
            if (_spotifyMode && !_mixed && _spotify != null) _ = Run(ChangeAndRefreshQueueAsync(_spotify.SetRepeatAsync(value)));
        }
    }

    public string RepeatGlyph => _repeat == RepeatMode.One ? "" : "";
    public bool RepeatActive => _repeat != RepeatMode.Off;

    string _contextName = "";
    public string ContextName
    {
        get => _contextName;
        private set { if (Set(ref _contextName, value)) OnPropertyChanged(nameof(ContextLabel)); }
    }

    /// <summary>
    /// Where the track is being played from, unless that just repeats the album shown beside it — playing
    /// an album makes the two identical, and a view should not say the same thing twice.
    /// </summary>
    public string ContextLabel =>
        string.Equals(_contextName, _current?.Album, StringComparison.CurrentCultureIgnoreCase) ? "" : _contextName;

    string _deviceName = "";
    /// <summary>Name of the remote Spotify Connect device being controlled; empty when playing in Sonora.</summary>
    public string RemoteDeviceName { get => _deviceName; private set { if (Set(ref _deviceName, value)) OnPropertyChanged(nameof(IsRemote)); } }
    public bool IsRemote => _deviceName.Length > 0;

    // ---------------- commands ----------------

    public void PlayContext(IReadOnlyList<Track> tracks, int start, string name, bool autoplay = true, string? contextUri = null, double startAt = 0)
    {
        if (tracks.Count == 0) return;
        if (start < 0) start = _shuffle ? Random.Shared.Next(tracks.Count) : 0;
        start = Math.Clamp(start, 0, tracks.Count - 1);

        // Local-file entries that have not been located are listed but cannot play: carry on with the next one that can.
        if (tracks.Any(t => t.IsMissing))
        {
            var playable = tracks.Where(t => !t.IsMissing).ToList();
            var first = tracks.Skip(start).FirstOrDefault(t => !t.IsMissing) ?? playable.FirstOrDefault();
            if (first == null) return;
            start = playable.IndexOf(first);
            tracks = playable;
        }

        bool mixed = tracks.Any(t => t.IsSpotify) && tracks.Any(t => !t.IsSpotify);
        if (mixed && _spotify == null)
        {
            // No Spotify account here: play the files of the list.
            var startTrack = tracks[start];
            var files = tracks.Where(t => !t.IsSpotify).ToList();
            start = Math.Max(0, files.IndexOf(startTrack));
            tracks = files;
            mixed = false;
        }
        _mixed = mixed;

        if (mixed)
        {
            _resume = null;
            _context = tracks.ToList();
            SetContextUri(contextUri); // only so the library shows where this comes from; Spotify is never asked to play it
            ContextName = name;
            BuildOrder(start);
            _ = Run(SpotifyPlainPlaybackAsync());
            _ = LoadAsync(_context[_order[_pos]], autoplay, startAt);
            return;
        }

        if (tracks[start].IsSpotify)
        {
            if (_spotify == null) { Error?.Invoke(Loc.T("player.connect_spotify")); return; }
            EnterSpotifyMode();
            _resume = null;
            _context = tracks.ToList();
            SetContextUri(contextUri);
            ContextName = name;
            ShowPending(tracks[start]);
            var uris = tracks.Where(t => t.IsSpotify).Select(t => t.Uri!).ToList();
            var index = uris.IndexOf(tracks[start].Uri!);
            _ = Run(PlaySpotifyAsync(contextUri, uris, index));
            return;
        }

        LeaveSpotifyMode();
        SetContextUri(null);
        _context = tracks.Where(t => !t.IsSpotify).ToList();
        ContextName = name;
        BuildOrder(Math.Max(0, _context.IndexOf(tracks[start])));
        _ = LoadAsync(_context[_order[_pos]], autoplay, startAt);
    }

    /// <summary>
    /// Shows a Spotify track again after a restart, paused, at the position it was left. Spotify cannot load a
    /// track without playing it, so the track starts for real the first time play is pressed.
    /// </summary>
    public bool RestoreSpotify(Track t, string? contextUri, string contextName, double positionSec)
    {
        if (_spotify == null || t.Uri == null || Current != null) return false;
        _mixed = false;
        EnterSpotifyMode();
        _context = [t];
        SetContextUri(contextUri);
        ContextName = contextName;
        ShowPending(t);
        _position = _posBase = positionSec;
        NotifyPosition();
        _resume = new SpotifyResume(contextUri, t.Uri, positionSec);
        return true;
    }

    async Task StartResumeAsync(SpotifyResume r)
    {
        await _spotify!.SetShuffleAsync(_shuffle).ContinueWith(_ => { }); // best effort before starting
        await _spotify.PlayAsync(r.ContextUri, [r.TrackUri], 0, (long)(r.PositionSec * 1000));
    }

    /// <summary>Plays a Spotify context URI (album, playlist, artist) from its start.</summary>
    public void PlaySpotifyContext(string contextUri, string name)
    {
        if (_spotify == null) { Error?.Invoke(Loc.T("player.connect_spotify")); return; }
        _mixed = false;
        EnterSpotifyMode();
        _resume = null;
        _context = [];
        SetContextUri(contextUri);
        ContextName = name;
        _ = Run(PlaySpotifyAsync(contextUri, [], -1));
    }

    /// <summary>Before a mixed list starts: Spotify must neither shuffle nor repeat the one track it is given, Sonora does that.</summary>
    async Task SpotifyPlainPlaybackAsync()
    {
        try
        {
            await _spotify!.SetShuffleAsync(false);
            await _spotify.SetRepeatAsync(RepeatMode.Off);
        }
        catch { /* best effort: no device yet is fine, the next play sets it up */ }
    }

    async Task PlaySpotifyAsync(string? contextUri, IReadOnlyList<string> uris, int index)
    {
        await _spotify!.SetShuffleAsync(_shuffle).ContinueWith(_ => { }); // best effort before starting
        await _spotify.PlayAsync(contextUri, uris, index);
    }

    public void Toggle()
    {
        if (_spotifyMode && _spotify != null)
        {
            if (_resume is { } resume)
            {
                _resume = null;
                IsPlaying = true;
                _posBase = resume.PositionSec;
                _posAtUtc = DateTime.UtcNow;
                _ = Run(StartResumeAsync(resume));
                return;
            }
            IsPlaying = !IsPlaying; // optimistic, corrected by the next state event
            _posBase = _position;
            _posAtUtc = DateTime.UtcNow;
            _ = Run(IsPlaying ? _spotify.ResumeAsync() : _spotify.PauseAsync());
            return;
        }
        if (_current == null) return;
        if (IsPlaying) { _wantPlay = false; _mp.Pause(); }
        else { _wantPlay = true; _mp.Play(); }
    }

    public void Next()
    {
        if (_spotifyMode && !_mixed && _spotify != null) { _ = Run(SpotifyStepAsync(next: true)); return; }
        Advance(auto: false);
    }

    public void Previous()
    {
        if (_spotifyMode && !_mixed && _spotify != null) { _ = Run(SpotifyStepAsync(next: false)); return; }
        if (_position > 3 || _context.Count == 0) { Seek(0); return; }
        if (_pos > 0) _pos--;
        else if (_repeat == RepeatMode.All) _pos = _order.Count - 1;
        else { Seek(0); return; }
        _ = LoadAsync(_context[_order[_pos]], true);
    }

    /// <summary>Next / previous on Spotify. A track restored but not started has to be started first.</summary>
    async Task SpotifyStepAsync(bool next)
    {
        if (_resume is { } resume)
        {
            _resume = null;
            await StartResumeAsync(resume);
            await Task.Delay(900); // the device needs a moment before it accepts another command
        }
        await (next ? _spotify!.NextAsync() : _spotify!.PreviousAsync());
    }

    public void Seek(double seconds)
    {
        if (_current == null) return;
        seconds = Math.Clamp(seconds, 0, Math.Max(0, _duration));
        if (_spotifyMode && _resume is { } pending)
        {
            _resume = pending with { PositionSec = seconds }; // nothing is playing yet: only the start point moves
            _position = _posBase = seconds;
            NotifyPosition();
            return;
        }
        if (_spotifyMode && _spotify != null)
        {
            _posBase = seconds;
            _posAtUtc = DateTime.UtcNow;
            _ = Run(_spotify.SeekAsync(seconds));
        }
        else
        {
            _mp.PlaybackSession.Position = TimeSpan.FromSeconds(seconds);
        }
        _position = seconds;
        NotifyPosition();
        UpdateSystemMediaTimeline();
    }

    public void EndSeek(double seconds)
    {
        IsSeeking = false;
        Seek(seconds);
    }

    public void CycleRepeat() => Repeat = (RepeatMode)(((int)_repeat + 1) % 3);

    public void Enqueue(Track t)
    {
        if (t.IsSpotify && !_mixed)
        {
            if (_spotify == null) return;
            _ = Run(EnqueueSpotifyAsync(t));
            return;
        }
        _userQueue.Add(t);
        RefreshUpcoming();
    }

    async Task EnqueueSpotifyAsync(Track t)
    {
        await _spotify!.EnqueueAsync(t.Uri!);
        await RefreshSpotifyQueueAsync();
    }

    public void PlayNext(Track t)
    {
        if (t.IsSpotify && !_mixed) { Enqueue(t); return; } // the Web API only appends to Spotify's queue
        _userQueue.Insert(0, t);
        RefreshUpcoming();
    }

    /// <summary>Jump to a row of the "Next up" list.</summary>
    public void JumpTo(TrackRow row)
    {
        if (_spotifyMode && !_mixed)
        {
            var idx = _context.IndexOf(row.Track);
            if (idx >= 0) PlayContext(_context, idx, ContextName, true, _spotifyContextUri);
            else PlayContext([row.Track], 0, ContextName);
            return;
        }
        int n = row.Number;
        if (n <= _userQueue.Count)
        {
            var t = _userQueue[n - 1];
            _userQueue.RemoveRange(0, n);
            _ = LoadAsync(t, true);
            return;
        }
        int skip = n - _userQueue.Count;
        _userQueue.Clear();
        if (_pos + skip < _order.Count)
        {
            _pos += skip;
            _ = LoadAsync(_context[_order[_pos]], true);
        }
    }

    // ---------------- Spotify mirroring ----------------

    void EnterSpotifyMode()
    {
        if (_spotifyMode) return;
        _spotifyMode = true;
        _loadToken++; // cancel pending local loads
        _wantPlay = false;
        _mp.Pause();
        _userQueue.Clear();
        _queueTimer.Start();
    }

    void LeaveSpotifyMode()
    {
        if (!_spotifyMode) return;
        _spotifyMode = false;
        _queueTimer.Stop();
        _resume = null;
        RemoteDeviceName = "";
        if (_spotify != null && IsPlaying) _ = Run(_spotify.PauseAsync());
        IsPlaying = false;
    }

    /// <summary>Tells the listening log how far the track leaving the player got.</summary>
    void FinishCurrent()
    {
        if (_current is { } t && _position > 0) TrackFinished?.Invoke(t, _position);
    }

    void ShowPending(Track t)
    {
        FinishCurrent();
        _position = 0;
        _posBase = 0;
        _posAtUtc = DateTime.UtcNow;
        NotifyPosition();
        Current = t;
        DurationSec = t.DurationSec;
    }

    void OnSpotifyState(SpotifyState s)
    {
        if (!_spotifyMode)
        {
            // Playback started elsewhere (phone, official app) while Sonora plays nothing: follow it.
            if (_mixed || !s.IsPlaying || (IsPlaying && !_spotifyMode)) return;
            EnterSpotifyMode();
        }

        // A mixed list moves from one engine to the other: a late report about a track that is no longer current is stale.
        if (_mixed && s.Track != null && s.Track.Uri != _current?.Uri) return;
        // Spotify said it stopped at the start of a track that was nearly over: that track ended, so the next entry is due.
        bool ended = _mixed && IsPlaying && !s.IsPlaying && _duration > 0 && s.PositionSec < 0.5 && _position >= _duration - 3;

        _resume = null; // Spotify has said what is playing: that is the truth now
        RemoteDeviceName = s.IsSonoraDevice ? "" : s.DeviceName;
        var changed = s.Track != null && s.Track != _current;
        if (changed) FinishCurrent(); // _position still holds the outgoing track's last sampled position
        if (s.Track != null) Current = s.Track;
        if (s.DurationSec > 0) DurationSec = s.DurationSec;
        _posBase = s.PositionSec;
        _posAtUtc = s.AtUtc;
        if (!IsSeeking) { _position = s.PositionSec; NotifyPosition(); }
        IsPlaying = s.IsPlaying;

        if (_mixed)
        {
            // Sonora owns shuffle, repeat and the queue of a mixed list: Spotify's own flags describe a single track.
            if (ended && _endedToken != _loadToken) { _endedToken = _loadToken; Advance(auto: true); }
            return;
        }

        bool shuffleChanged = _shuffle != s.Shuffle;
        if (shuffleChanged) { _shuffle = s.Shuffle; OnPropertyChanged(nameof(Shuffle)); }
        if (_repeat != s.Repeat)
        {
            _repeat = s.Repeat;
            OnPropertyChanged(nameof(Repeat));
            OnPropertyChanged(nameof(RepeatGlyph));
            OnPropertyChanged(nameof(RepeatActive));
        }

        if (changed && s.Track != null && PlaysFromElsewhere(s)) FollowRemoteContext(s.Track, s.ContextUri);
        if (changed && s.Track != null) TrackStarted?.Invoke(s.Track);
        if ((changed && s.Track != null) || shuffleChanged) _ = Run(RefreshSpotifyQueueAsync());
    }

    /// <summary>
    /// Whether Spotify plays from somewhere else than Sonora's list. When Spotify names its source, a different one
    /// settles it, even if the track also belongs to Sonora's list (a liked song played from a playlist on the phone,
    /// after Sonora played the liked songs). Without a source (a bare list of tracks), only a track outside the list tells.
    /// </summary>
    bool PlaysFromElsewhere(SpotifyState s) => s.ContextUri != null
        ? s.ContextUri != _spotifyContextUri
        : !_context.Any(t => t.Uri == s.Track!.Uri);

    /// <summary>Gives the name of a Spotify context (playlist, album, artist, liked songs) from its URI, or null when unknown.</summary>
    public Func<string, Task<string?>>? ContextNameResolver { get; set; }

    /// <summary>
    /// A track Sonora did not start is playing (picked on a phone or in the official app, or Spotify carried on past
    /// the list): what it plays from is no longer Sonora's list, so take Spotify's word for it instead of showing the
    /// last list Sonora played, local files included (issue #14). Tracks Sonora started keep the name Sonora gave.
    /// </summary>
    void FollowRemoteContext(Track track, string? contextUri)
    {
        _context = [track]; // as for a restored track: the old list is not what plays any more
        if (contextUri != null && contextUri == _spotifyContextUri && ContextName.Length > 0) return; // same source, already named
        SetContextUri(contextUri);
        ContextName = "";
        if (contextUri != null && ContextNameResolver is { } resolve) _ = Run(NameContextAsync(resolve, contextUri));
    }

    async Task NameContextAsync(Func<string, Task<string?>> resolve, string contextUri)
    {
        var name = await resolve(contextUri);
        if (name != null && _spotifyContextUri == contextUri) ContextName = name; // unless playback moved on meanwhile
    }

    /// <summary>Spotify applies a shuffle or repeat change a moment after the request returns: ask for the queue after that.</summary>
    async Task ChangeAndRefreshQueueAsync(Task change)
    {
        await change;
        await Task.Delay(700);
        await RefreshSpotifyQueueAsync();
    }

    async Task RefreshSpotifyQueueAsync()
    {
        if (_spotify == null || _queueBusy || _mixed) return;
        _queueBusy = true;
        try
        {
            var queue = await _spotify.GetQueueAsync();
            if (!_spotifyMode) return; // Sonora took over while the request was out
            var next = queue.Take(80).ToList();
            // Same queue as before: leave the list alone, so it neither flickers nor jumps back to the top.
            if (next.Count == Upcoming.Count && next.Select((t, i) => t == Upcoming[i].Track).All(same => same)) return;
            Upcoming.Clear();
            int n = 1;
            foreach (var t in next) Upcoming.Add(new TrackRow(n++, t));
        }
        finally { _queueBusy = false; }
    }

    async Task Run(Task task)
    {
        try { await task; }
        catch (Exception ex) { Error?.Invoke(ex.Message); }
    }

    // ---------------- local engine internals ----------------

    void BuildOrder(int startIndex)
    {
        _order = Enumerable.Range(0, _context.Count).ToList();
        if (_shuffle)
        {
            _order.RemoveAt(startIndex);
            var arr = _order.ToArray();
            Random.Shared.Shuffle(arr);
            _order = [startIndex, .. arr];
            _pos = 0;
        }
        else _pos = startIndex;
    }

    void Advance(bool auto)
    {
        if (auto && _repeat == RepeatMode.One && _current != null)
        {
            if (_mixed && _current.IsSpotify) { _ = LoadAsync(_current, true); return; }
            Seek(0);
            _mp.Play();
            return;
        }
        if (_userQueue.Count > 0)
        {
            var t = _userQueue[0];
            _userQueue.RemoveAt(0);
            _ = LoadAsync(t, true);
            return;
        }
        if (_context.Count == 0) return;

        if (_pos + 1 < _order.Count) _pos++;
        else
        {
            // End of the context: wrap around, and keep playing only when "repeat all" is on.
            if (_shuffle) BuildOrder(Random.Shared.Next(_context.Count));
            _pos = 0;
            _ = LoadAsync(_context[_order[_pos]], play: _repeat == RepeatMode.All);
            return;
        }
        _ = LoadAsync(_context[_order[_pos]], true);
    }

    void OnFailed(string message)
    {
        if (_spotifyMode) return;
        System.Diagnostics.Debug.WriteLine("Playback failed: " + message);
        if (++_failures < 5) Advance(auto: true);
        else { _failures = 0; _wantPlay = false; IsPlaying = false; }
    }

    async Task LoadAsync(Track t, bool play, double startAt = 0)
    {
        int token = ++_loadToken;
        _wantPlay = play;
        _pendingSeek = startAt;
        FinishCurrent();

        // Reset position before the duration so the slider never coerces a stale position onto the new track.
        _position = startAt;
        NotifyPosition();
        Current = t;
        DurationSec = t.DurationSec;
        RefreshUpcoming();
        TrackStarted?.Invoke(t);

        if (t.IsSpotify)
        {
            await LoadSpotifyEntryAsync(t, play, startAt, token);
            return;
        }
        if (_spotifyMode) LeaveSpotifyMode(); // a local file of a mixed list follows a Spotify track

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(t.Path);
            if (token != _loadToken) return;

            // No display properties on the item: Sonora publishes them to Windows itself, in both engines.
            if (token != _loadToken) return;
            _mp.Source = new MediaPlaybackItem(MediaSource.CreateFromStorageFile(file));
            if (play) _mp.Play();
        }
        catch (Exception ex)
        {
            if (token == _loadToken) OnFailed(ex.Message);
        }
    }

    /// <summary>A Spotify track of a mixed list: played alone, on Spotify, while the local engine stays quiet.</summary>
    async Task LoadSpotifyEntryAsync(Track t, bool play, double startAt, int token)
    {
        if (_spotify == null) { Error?.Invoke(Loc.T("player.connect_spotify")); return; }
        if (!_spotifyMode)
        {
            // Not EnterSpotifyMode: that would drop the user's queue and hand the order to Spotify.
            _spotifyMode = true;
            _wantPlay = false;
            _mp.Pause();
        }
        if (!play)
        {
            _resume = new SpotifyResume(null, t.Uri!, startAt); // shown, and started by the play button
            return;
        }
        _resume = null;
        try { await _spotify.PlayAsync(null, [t.Uri!], 0, (long)(startAt * 1000)); }
        catch (Exception ex) { if (token == _loadToken) Error?.Invoke(ex.Message); }
    }

    void Tick()
    {
        if (_current == null) return;
        double p;
        if (_spotifyMode)
        {
            p = IsPlaying ? _posBase + (DateTime.UtcNow - _posAtUtc).TotalSeconds : _posBase;
            // Safety net for a mixed list: Spotify normally reports the end of a track, but if that report never came the
            // track has been "playing" for two seconds past its length, and the list moves on.
            if (_mixed && IsPlaying && _duration > 0 && p >= _duration + 2 && _endedToken != _loadToken)
            {
                _endedToken = _loadToken;
                Advance(auto: true);
                return;
            }
            if (_duration > 0) p = Math.Min(p, _duration);
        }
        else p = _mp.PlaybackSession.Position.TotalSeconds;

        if (Math.Abs(p - _position) < 0.05) return;
        _position = p;
        if (!IsSeeking) NotifyPosition();
    }

    void NotifyPosition()
    {
        OnPropertyChanged(nameof(SeekValue));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(Progress));
    }

    void RefreshUpcoming()
    {
        if (_spotifyMode && !_mixed) return;
        Upcoming.Clear();
        int n = 1;
        foreach (var t in _userQueue) Upcoming.Add(new TrackRow(n++, t));
        for (int i = _pos + 1; i < _order.Count && n <= 80; i++)
            Upcoming.Add(new TrackRow(n++, _context[_order[i]]));
    }

    public void Dispose()
    {
        _timer.Stop();
        try
        {
            _smtc.ButtonPressed -= OnSystemMediaButton;
            _smtc.PlaybackStatus = MediaPlaybackStatus.Closed; // leaves no stale tile in the Windows overlay
        }
        catch { /* already torn down */ }
        _mp.Dispose();
    }
}
