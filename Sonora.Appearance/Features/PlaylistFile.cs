using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sonora.Appearance.Features;

/// <summary>
/// One track of a playlist file. A Spotify track carries its URI, a local file its path; either way the title, artist,
/// album and length are kept, so a file that is not on the machine reading the playlist can still be recognised.
/// </summary>
public sealed record PlaylistFileEntry(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("artist")] string Artist = "",
    [property: JsonPropertyName("album")] string Album = "",
    [property: JsonPropertyName("seconds")] double Seconds = 0,
    [property: JsonPropertyName("spotify")] string? Spotify = null,
    [property: JsonPropertyName("path")] string? Path = null)
{
    [JsonIgnore] public bool IsSpotify => !string.IsNullOrEmpty(Spotify);
}

/// <summary>
/// Sonora's own playlist file (<c>.sonoraplaylist</c>, plain JSON). Unlike an M3U, which can only point at files, it can
/// carry Spotify tracks too, and it keeps enough about each track to find it again elsewhere. M3U stays available for
/// other players.
/// </summary>
public sealed class PlaylistFile
{
    public const string Extension = ".sonoraplaylist";
    const string FormatId = "sonora-playlist";
    const int Version = 1;

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public required string Name { get; init; }
    public required IReadOnlyList<PlaylistFileEntry> Tracks { get; init; }

    /// <summary>True for a well-formed <c>spotify:track:</c> or <c>spotify:episode:</c> URI (a 22-character base-62 id).</summary>
    public static bool IsSpotifyItemUri(string? uri) =>
        uri is not null && System.Text.RegularExpressions.Regex.IsMatch(uri, @"^spotify:(track|episode):[0-9A-Za-z]{22}\z");

    public static string Write(string name, IEnumerable<PlaylistFileEntry> tracks) =>
        JsonSerializer.Serialize(new Dto { Format = FormatId, Version = Version, Name = name, Tracks = tracks.ToList() }, Options);

    /// <summary>Reads a playlist file. Throws <see cref="FormatException"/> when the text is not one (or is from a newer Sonora).</summary>
    public static PlaylistFile Parse(string json)
    {
        Dto? dto;
        try { dto = JsonSerializer.Deserialize<Dto>(json, Options); }
        catch (JsonException ex) { throw new FormatException(ex.Message, ex); }

        if (dto is null || dto.Format != FormatId) throw new FormatException("Not a Sonora playlist file.");
        if (dto.Version > Version) throw new FormatException($"Playlist file version {dto.Version} is newer than this Sonora understands.");
        return new PlaylistFile
        {
            Name = dto.Name ?? "",
            Tracks = (dto.Tracks ?? []).Where(t => t is not null && (!string.IsNullOrWhiteSpace(t.Title) || t.IsSpotify || !string.IsNullOrEmpty(t.Path))).ToList(),
        };
    }

    sealed class Dto
    {
        [JsonPropertyName("format")] public string? Format { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("tracks")] public List<PlaylistFileEntry>? Tracks { get; set; }
    }
}
