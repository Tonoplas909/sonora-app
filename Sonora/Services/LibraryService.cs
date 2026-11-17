using Sonora.Models;
using Windows.Storage;
using IOPath = System.IO.Path;
using FileAttributes = System.IO.FileAttributes;
using Sonora.Appearance.Localization;

namespace Sonora.Services;

/// <summary>Scans music folders, reads tags through the Windows property system and builds the album/artist index.</summary>
public sealed class LibraryService
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".wav", ".wma", ".ogg", ".opus", ".aif", ".aiff", ".alac"
    };

    // Cloud placeholders (OneDrive "files on demand") would be downloaded if we touched them.
    const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    Dictionary<string, Track> _byPath = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, Album> _albumsByKey = [];

    public List<Track> Tracks { get; private set; } = [];
    public List<Album> Albums { get; private set; } = [];
    public List<Artist> Artists { get; private set; } = [];
    public List<Genre> Genres { get; private set; } = [];

    public Track? Get(string path) => _byPath.GetValueOrDefault(path);
    public Album? GetAlbum(string key) => _albumsByKey.GetValueOrDefault(key);
    public Artist? GetArtist(string name) =>
        Artists.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.CurrentCultureIgnoreCase));

    public async Task<List<Track>> ScanAsync(IReadOnlyList<string> folders, IReadOnlyDictionary<string, Track> cache,
        IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        var files = await Task.Run(() => Enumerate(folders), ct);
        var result = new Track?[files.Count];
        int done = 0;

        await Parallel.ForEachAsync(Enumerable.Range(0, files.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount), CancellationToken = ct },
            async (i, _) =>
            {
                var fi = files[i];
                result[i] = cache.TryGetValue(fi.FullName, out var c)
                            && c.FileTicks == fi.LastWriteTimeUtc.Ticks && c.FileSize == fi.Length
                    ? c
                    : await ReadAsync(fi);
                var d = Interlocked.Increment(ref done);
                if (d % 50 == 0 || d == files.Count) progress?.Report((d, files.Count));
            });

        return result.Where(t => t is not null).Select(t => t!).ToList();
    }

    static List<FileInfo> Enumerate(IEnumerable<string> folders)
    {
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Offline | RecallOnDataAccess,
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<FileInfo>();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var fi in new DirectoryInfo(folder).EnumerateFiles("*", opts))
                if (Extensions.Contains(fi.Extension) && seen.Add(fi.FullName))
                    list.Add(fi);
        }
        return list;
    }

    static async Task<Track> ReadAsync(FileInfo fi)
    {
        var t = new Track
        {
            Path = fi.FullName,
            FileTicks = fi.LastWriteTimeUtc.Ticks,
            FileSize = fi.Length,
            Title = IOPath.GetFileNameWithoutExtension(fi.Name),
        };
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(fi.FullName);
            var mp = await file.Properties.GetMusicPropertiesAsync();
            if (!string.IsNullOrWhiteSpace(mp.Title)) t.Title = mp.Title.Trim();
            t.Artist = mp.Artist?.Trim() ?? "";
            t.AlbumArtist = mp.AlbumArtist?.Trim() ?? "";
            t.Album = mp.Album?.Trim() ?? "";
            t.Genre = mp.Genre?.FirstOrDefault()?.Trim() ?? "";
            t.TrackNumber = mp.TrackNumber;
            t.Year = mp.Year;
            t.DurationSec = mp.Duration.TotalSeconds;
        }
        catch
        {
            // Unreadable tags: keep file-name based metadata.
        }
        if (string.IsNullOrWhiteSpace(t.Artist)) t.Artist = Loc.T("library.unknown_artist");
        if (string.IsNullOrWhiteSpace(t.Album))
            t.Album = IOPath.GetFileName(IOPath.GetDirectoryName(fi.FullName)) is { Length: > 0 } dir ? dir : Loc.T("library.unknown_album");
        return t;
    }

    /// <summary>
    /// "Unknown artist" and "Unknown album" are written into the tracks when they are scanned, in the language of that
    /// moment. After a change of language they are written again in the new one. True when something changed.
    /// </summary>
    public bool Relocalize()
    {
        bool changed = false;
        string artist = Loc.T("library.unknown_artist"), album = Loc.T("library.unknown_album");
        foreach (var t in Tracks)
        {
            if (t.Artist != artist && Loc.MatchesAnyLanguage("library.unknown_artist", t.Artist)) { t.Artist = artist; changed = true; }
            if (t.Album != album && Loc.MatchesAnyLanguage("library.unknown_album", t.Album)) { t.Album = album; changed = true; }
        }
        return changed;
    }

    /// <summary>Rebuilds the in-memory index. Must run on the UI thread.</summary>
    public void Build(List<Track> tracks)
    {
        var cmp = StringComparer.CurrentCultureIgnoreCase;
        tracks.Sort((a, b) =>
        {
            int c = cmp.Compare(a.PrimaryArtist, b.PrimaryArtist);
            if (c != 0) return c;
            c = cmp.Compare(a.Album, b.Album);
            if (c != 0) return c;
            c = a.TrackNumber.CompareTo(b.TrackNumber);
            return c != 0 ? c : cmp.Compare(a.Title, b.Title);
        });

        var oldArt = _albumsByKey.Values.Where(a => a.ArtPath != null).ToDictionary(a => a.Key, a => a.ArtPath!);
        var byPath = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        var albums = new Dictionary<string, Album>();
        var artists = new Dictionary<string, Artist>(cmp);
        var genres = new Dictionary<string, Genre>(cmp);

        foreach (var t in tracks)
        {
            byPath[t.Path] = t;

            var key = t.AlbumKey;
            if (!albums.TryGetValue(key, out var al))
            {
                al = new Album { Key = key, Title = t.Album, Artist = t.PrimaryArtist, ArtPath = oldArt.GetValueOrDefault(key) };
                albums[key] = al;
            }
            al.Tracks.Add(t);
            if (t.Year > al.Year) al.Year = t.Year;
            t.AlbumRef = al;

            if (!artists.TryGetValue(t.PrimaryArtist, out var ar))
                artists[t.PrimaryArtist] = ar = new Artist { Name = t.PrimaryArtist };
            ar.Tracks.Add(t);
            if (ar.Albums.Count == 0 || ar.Albums[^1] != al) ar.Albums.Add(al);

            if (t.Genre.Length > 0)
            {
                if (!genres.TryGetValue(t.Genre, out var g)) genres[t.Genre] = g = new Genre { Name = t.Genre };
                g.Tracks.Add(t);
            }
        }

        foreach (var ar in artists.Values)
        {
            ar.Albums.Sort((a, b) => b.Year.CompareTo(a.Year));
            ar.ArtPath = ar.Albums.Select(a => a.ArtPath).FirstOrDefault(p => p != null);
        }

        _byPath = byPath;
        _albumsByKey = albums;
        Tracks = tracks;
        Albums = albums.Values.OrderBy(a => a.Title, cmp).ToList();
        Artists = artists.Values.OrderBy(a => a.Name, cmp).ToList();
        Genres = genres.Values.OrderByDescending(g => g.Tracks.Count).ToList();
    }
}
