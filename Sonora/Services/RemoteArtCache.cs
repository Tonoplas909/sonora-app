using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using IOPath = System.IO.Path;

namespace Sonora.Services;

/// <summary>
/// Keeps Spotify's covers on disk. Without this they are fetched from the CDN on every start and vanish
/// entirely without a network — which made the library cache show rows of empty squares. A cover is
/// written once and read from the file ever after.
/// </summary>
public static class RemoteArtCache
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly HashSet<string> InFlight = [];

    static string Folder => IOPath.Combine(StateStore.Dir, "art", "remote");

    public static bool IsRemote(string? path) =>
        path is { Length: > 0 } && path.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    /// <summary>The cached file for a cover URL, or null when it has not been downloaded yet.</summary>
    public static string? Cached(string url)
    {
        try
        {
            var file = PathFor(url);
            return File.Exists(file) ? file : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads a cover into the cache, once. Fire-and-forget on purpose: the caller already has the
    /// remote URL to display now, and the file is only there to make the next start faster.
    /// </summary>
    public static void Prefetch(string url)
    {
        lock (InFlight)
        {
            if (!InFlight.Add(url)) return;
        }
        _ = DownloadAsync(url);
    }

    static async Task DownloadAsync(string url)
    {
        try
        {
            var file = PathFor(url);
            if (File.Exists(file)) return;
            Directory.CreateDirectory(Folder);
            var bytes = await Http.GetByteArrayAsync(url);
            if (bytes.Length == 0) return;
            // Written beside, then moved: a half-downloaded file never gets read as a cover.
            var tmp = file + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, file, overwrite: true);
        }
        catch
        {
            // the cover is still shown from the network; the cache simply misses this one
        }
        finally
        {
            lock (InFlight) InFlight.Remove(url);
        }
    }

    static string PathFor(string url)
    {
        var name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..24];
        return IOPath.Combine(Folder, name + ".img");
    }

    /// <summary>Removes every cached cover. Used when signing out, with the rest of the account's data.</summary>
    public static void Clear()
    {
        try { if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true); }
        catch { /* nothing useful to do */ }
    }
}
