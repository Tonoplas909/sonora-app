using System.ComponentModel;
using Sonora.Appearance.Features;
using Sonora.Core;
using Sonora.Models;
using Sonora.Services;

namespace Sonora.ViewModels;

/// <summary>One lyric line, with its own highlight flag so the list need not re-template on every tick.</summary>
public sealed class LyricLineVm(TimeSpan time, string text) : ObservableObject
{
    public TimeSpan Time { get; } = time;
    public string Text { get; } = text;

    bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
}

/// <summary>
/// Lyrics for the track currently playing in <see cref="PlayerService"/>: fetched from LRCLIB on demand
/// (see <see cref="LyricsService"/>) and kept in step with playback position for line-by-line highlighting.
/// Created lazily by <see cref="MainViewModel.Lyrics"/>, the first time the lyrics panel is shown.
/// </summary>
public sealed class LyricsViewModel : ObservableObject
{
    readonly PlayerService _player;
    readonly LyricsService _service;
    int _loadToken;
    List<LyricLine> _raw = [];
    List<LyricLineVm> _lines = [];

    public LyricsViewModel(PlayerService player, LyricsService service)
    {
        _player = player;
        _service = service;
        _player.PropertyChanged += OnPlayerPropertyChanged;
        _player.TrackStarted += t => _ = LoadAsync(t);
        if (_player.Current is { } current) _ = LoadAsync(current);
    }

    public IReadOnlyList<LyricLineVm> Lines => _lines;

    int _currentIndex = -1;
    public int CurrentIndex
    {
        get => _currentIndex;
        private set
        {
            if (_currentIndex == value) return;
            if (_currentIndex >= 0 && _currentIndex < _lines.Count) _lines[_currentIndex].IsCurrent = false;
            _currentIndex = value;
            if (_currentIndex >= 0 && _currentIndex < _lines.Count) _lines[_currentIndex].IsCurrent = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentLine));
            OnPropertyChanged(nameof(NextLine));
        }
    }

    bool _isLoading;
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    bool _instrumental;
    public bool Instrumental { get => _instrumental; private set => Set(ref _instrumental, value); }

    string? _plain;
    public string? Plain { get => _plain; private set => Set(ref _plain, value); }

    /// <summary>Text of the line playing now, for views that show one line at a time (full screen).</summary>
    public string? CurrentLine => Line(_currentIndex);
    public string? NextLine => Line(_currentIndex + 1);

    string? Line(int i) => i >= 0 && i < _lines.Count ? _lines[i].Text : null;

    public bool HasSynced => _lines.Count > 0;
    public bool ShowPlainOnly => !HasSynced && !string.IsNullOrWhiteSpace(Plain);
    public bool NotFound => !IsLoading && !Instrumental && !HasSynced && string.IsNullOrWhiteSpace(Plain);

    async Task LoadAsync(Track track)
    {
        int token = ++_loadToken;
        CurrentIndex = -1; // clears the highlight on the previous track's lines, if any
        _raw = [];
        _lines = [];
        Plain = null;
        Instrumental = false;
        IsLoading = true;
        Refresh();

        LyricsResult? result;
        try { result = await _service.GetAsync(track); }
        catch { result = null; } // best effort: a lookup failure never blocks playback
        if (token != _loadToken) return; // a newer track started while this lookup was in flight

        IsLoading = false;
        if (result != null)
        {
            _raw = result.Synced;
            _lines = result.Synced.Select(l => new LyricLineVm(l.Time, l.Text)).ToList();
            Plain = result.Plain;
            Instrumental = result.Instrumental;
            if (_raw.Count > 0) CurrentIndex = Lrc.IndexAt(_raw, TimeSpan.FromSeconds(_player.SeekValue));
        }
        Refresh();
    }

    void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerService.SeekValue) || _raw.Count == 0) return;
        CurrentIndex = Lrc.IndexAt(_raw, TimeSpan.FromSeconds(_player.SeekValue));
    }

    void Refresh()
    {
        OnPropertyChanged(nameof(Lines));
        OnPropertyChanged(nameof(HasSynced));
        OnPropertyChanged(nameof(ShowPlainOnly));
        OnPropertyChanged(nameof(NotFound));
        OnPropertyChanged(nameof(CurrentLine));
        OnPropertyChanged(nameof(NextLine));
    }
}
