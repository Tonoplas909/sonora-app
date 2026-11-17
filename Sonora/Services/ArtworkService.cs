using System.Security.Cryptography;
using System.Text;
using Sonora.Models;
using Windows.Storage;
using Windows.Storage.FileProperties;
using IOPath = System.IO.Path;

namespace Sonora.Services;

/// <summary>Resolves album covers (folder image or embedded art via the shell thumbnail cache) into a local disk cache.</summary>
public sealed class ArtworkService
{
    static readonly string[] FolderImages = ["cover.jpg", "folder.jpg", "front.jpg", "album.jpg", "cover.png", "folder.png"];
    readonly string _dir = IOPath.Combine(StateStore.Dir, "art");

    public ArtworkService() => Directory.CreateDirectory(_dir);

    public Task LoadAllAsync(IReadOnlyList<Album> albums, CancellationToken ct) =>
        Parallel.ForEachAsync(albums.Where(a => a.ArtPath == null),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (album, _) =>
            {
                var path = await ResolveAsync(album);
                if (path != null) album.ArtPath = path;
            });

    async Task<string?> ResolveAsync(Album album)
    {
        if (album.Tracks.Count == 0) return null;
        var name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(album.Key)))[..20];
        var cached = IOPath.Combine(_dir, name + ".img");
        var none = IOPath.Combine(_dir, name + ".none");
        if (File.Exists(cached)) return cached;
        if (File.Exists(none)) return null;

        var dir = IOPath.GetDirectoryName(album.Tracks[0].Path);
        if (dir != null)
            foreach (var f in FolderImages)
            {
                var p = IOPath.Combine(dir, f);
                if (File.Exists(p)) return p;
            }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(album.Tracks[0].Path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.MusicView, 512, ThumbnailOptions.ResizeThumbnail);
            if (thumb is { Type: ThumbnailType.Image })
            {
                var tmp = cached + "." + Environment.CurrentManagedThreadId + ".tmp";
                await using (var src = thumb.AsStreamForRead())
                await using (var dst = File.Create(tmp))
                    await src.CopyToAsync(dst);
                File.Move(tmp, cached, overwrite: true);
                return cached;
            }
        }
        catch
        {
            // fall through: no art
        }

        try { await File.WriteAllBytesAsync(none, []); } catch { /* ignore */ }
        return null;
    }
}
