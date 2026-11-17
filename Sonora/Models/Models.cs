using System.Text.Json.Serialization;
using Sonora.Core;

namespace Sonora.Models;

public sealed class Track : ObservableObject
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Genre { get; set; } = "";
    public uint TrackNumber { get; set; }
    public uint Year { get; set; }
    public double DurationSec { get; set; }
    public long FileTicks { get; set; }
    public long FileSize { get; set; }

    /// <summary>spotify:track:… (or spotify:episode:…) for Spotify items; null for local files.</summary>
    [JsonIgnore] public string? Uri { get; set; }
    [JsonIgnore] public string? ArtistUri { get; set; }
    [JsonIgnore] public bool IsSpotify => Uri != null;
    [JsonIgnore] public string? SpotifyId => Uri?[(Uri.LastIndexOf(':') + 1)..];

    [JsonIgnore] public Album? AlbumRef { get; set; }
    [JsonIgnore] public string PrimaryArtist => string.IsNullOrWhiteSpace(AlbumArtist) ? Artist : AlbumArtist;
    [JsonIgnore] public string AlbumKey => (PrimaryArtist + "\u001f" + Album).ToLowerInvariant();
    [JsonIgnore] public string DurationText => Fmt.Time(DurationSec);

    string? _searchKey;
    [JsonIgnore] public string SearchKey => _searchKey ??= Fmt.Fold($"{Title} {Artist} {Album} {AlbumArtist}");

    bool _isCurrent;
    [JsonIgnore] public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }

    bool _isLiked;
    [JsonIgnore] public bool IsLiked { get => _isLiked; set => Set(ref _isLiked, value); }
}

/// <summary>A numbered row in a track list (allows duplicates inside playlists).</summary>
public sealed record TrackRow(int Number, Track Track);

/// <summary>
/// The entries of a track list, with when each one was added (ticks, in the same order as the tracks) when the
/// source knows. Without dates, the order of the list is taken as the order of addition.
/// </summary>
public sealed record PlaylistContent(List<Track> Tracks, List<long>? Added = null);

public sealed class Album : ObservableObject
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public uint Year { get; set; }
    public List<Track> Tracks { get; } = [];
    public string? Uri { get; init; }
    public string? SpotifyId => Uri?[(Uri.LastIndexOf(':') + 1)..];
    /// <summary>ISO release date (Spotify), used to sort new releases.</summary>
    public string ReleaseDate { get; set; } = "";
    /// <summary>True for a podcast show (shown and played like an album of episodes).</summary>
    public bool IsShow => Uri?.StartsWith("spotify:show:", StringComparison.Ordinal) == true;

    string? _artPath;
    public string? ArtPath { get => _artPath; set => Set(ref _artPath, value); }

    public string Subtitle => Year > 0 ? $"{Year} • {Artist}" : Artist;
}

public sealed class Artist : ObservableObject
{
    public required string Name { get; init; }
    public string? Uri { get; init; }
    public string? SpotifyId => Uri?[(Uri.LastIndexOf(':') + 1)..];
    public List<Album> Albums { get; } = [];
    public List<Track> Tracks { get; } = [];

    string? _artPath;
    public string? ArtPath { get => _artPath; set => Set(ref _artPath, value); }

    public string Subtitle => "Artiste";
}

public sealed class Genre
{
    public required string Name { get; init; }
    public List<Track> Tracks { get; } = [];
}

public sealed class Playlist : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    public List<string> Paths { get; set; } = [];

    string? _artPath;
    [JsonIgnore] public string? ArtPath { get => _artPath; set => Set(ref _artPath, value); }

    [JsonIgnore] public string Subtitle => $"Playlist • {Fmt.Count(Paths.Count, "titre", "titres")}";

    public void Touch() => OnPropertyChanged(nameof(Subtitle));
}

/// <summary>A playlist that lives in the user's Spotify account.</summary>
public sealed class SpotifyPlaylist : ObservableObject
{
    public required string Id { get; init; }
    public required string Uri { get; init; }

    string _name = "";
    public required string Name { get => _name; set => Set(ref _name, value); }
    public string Owner { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public int TrackCount { get; init; }

    bool _isPublic;
    /// <summary>Whether the playlist is listed on the owner's profile.</summary>
    public bool IsPublic { get => _isPublic; set => Set(ref _isPublic, value); }

    bool _isCollaborative;
    /// <summary>Whether other people can add to it (Spotify only allows this on a private playlist).</summary>
    public bool IsCollaborative { get => _isCollaborative; set => Set(ref _isCollaborative, value); }

    string? _artPath;
    public string? ArtPath { get => _artPath; set => Set(ref _artPath, value); }

    public string Subtitle => string.IsNullOrEmpty(Owner) ? "Playlist" : $"Playlist • {Owner}";
}

public sealed record SpotifyDevice(string Id, string Name, string Type, bool IsActive);

public enum RepeatMode { Off, All, One }

/// <summary>What was playing when Sonora closed, enough to show it again and carry on from the same second.</summary>
public sealed class ResumePoint
{
    /// <summary>Local file path; empty for a Spotify item.</summary>
    public string Path { get; set; } = "";
    /// <summary>spotify:track:… or spotify:episode:…; empty for a local file.</summary>
    public string Uri { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string? ArtUrl { get; set; }
    public double DurationSec { get; set; }
    public double PositionSec { get; set; }
    /// <summary>Where it was played from: a Spotify context URI, and the name shown for it (also how a local list is found again).</summary>
    public string? ContextUri { get; set; }
    public string ContextName { get; set; } = "";
}

public sealed class AppState
{
    public List<string> Folders { get; set; } = [];
    public List<string> Liked { get; set; } = [];
    public List<Playlist> Playlists { get; set; } = [];
    public List<string> RecentAlbums { get; set; } = [];
    public double Volume { get; set; } = 70;
    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; }
    public string? LastTrack { get; set; }
    /// <summary>The track, position and context to pick up again at the next launch. Null until something has played.</summary>
    public ResumePoint? Resume { get; set; }
    /// <summary>Where each podcast episode was left, in seconds, so it can be resumed.</summary>
    public Dictionary<string, double> EpisodePositions { get; set; } = [];
    public string SpotifyClientId { get; set; } = "";
    /// <summary>ListenBrainz user token. Empty means nothing is ever sent there.</summary>
    public string ListenBrainzToken { get; set; } = "";
    /// <summary>Order last chosen on a playlist screen (a key of <c>ListQuery.SortKeys</c>), applied to every playlist opened next.</summary>
    public string PlaylistSort { get; set; } = "default";
    public bool PlaylistSortDescending { get; set; }
    public string ThemeId { get; set; } = "liquide";
    /// <summary>Verre liquide only: the blurred cover of the current track as the window's background.</summary>
    public bool CoverBackground { get; set; }
    public double CoverBackgroundStrength { get; set; } = 0.55;
    /// <summary>Blur radius of that cover, 0 (sharp) to 120.</summary>
    public double CoverBackgroundBlur { get; set; } = 30;
    /// <summary>Picture shown behind the panels of themes with a one-colour background. Empty: none.</summary>
    public string BackgroundImage { get; set; } = "";
    /// <summary>How much of that picture shows through (the rest is the theme's colour).</summary>
    public double BackgroundImageStrength { get; set; } = 0.85;
    /// <summary>Last sub-theme (colour variant) chosen for each theme (style).</summary>
    public Dictionary<string, string> PaletteByStyle { get; set; } = [];
    /// <summary>Optional features (lyrics, podcasts, tops…) and their options.</summary>
    public Sonora.Appearance.Features.FeatureSettings Features { get; set; } = new();
    public Sonora.Appearance.Content.ContentSettings Content { get; set; } = Sonora.Appearance.Content.ContentSettings.Default();
}
