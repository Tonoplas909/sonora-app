using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Sonora.Appearance.Features;

/// <summary>
/// The moments at which Spotify said each endpoint would answer again, kept in a file. A limited app can be turned
/// away for hours; if the block were only remembered in memory, every restart would ask again, be refused again and
/// push the moment further away. Written down, a restart stays quiet until the reset time has passed.
/// </summary>
public sealed class RateLimitLedger
{
    readonly ConcurrentDictionary<string, DateTime> _until = new();
    readonly string? _file;
    readonly object _fileLock = new();

    /// <param name="file">Where the blocks are kept between runs; null keeps them in memory only.</param>
    public RateLimitLedger(string? file = null, DateTime? nowUtc = null)
    {
        _file = file;
        Load(nowUtc ?? DateTime.UtcNow);
    }

    /// <summary>When this endpoint answers again, or null when it is not blocked.</summary>
    public DateTime? BlockedUntil(string key, DateTime? nowUtc = null) =>
        _until.TryGetValue(key, out var until) && until > (nowUtc ?? DateTime.UtcNow) ? until : null;

    /// <summary>The moment the last of the blocks ends, or null when nothing is blocked.</summary>
    public DateTime? LastOpening(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var open = _until.Values.Where(u => u > now).ToList();
        return open.Count == 0 ? null : open.Max();
    }

    /// <summary>Records a block (the later one wins over an earlier one for the same endpoint) and writes the file.</summary>
    public void Block(string key, DateTime untilUtc, DateTime? nowUtc = null)
    {
        _until.AddOrUpdate(key, untilUtc, (_, old) => old > untilUtc ? old : untilUtc);
        Save(nowUtc ?? DateTime.UtcNow);
    }

    public void Clear()
    {
        _until.Clear();
        Save(DateTime.UtcNow);
    }

    void Load(DateTime now)
    {
        if (_file is null) return;
        try
        {
            if (!File.Exists(_file)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_file));
            if (saved is null) return;
            foreach (var (key, text) in saved)
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until) && until > now)
                    _until[key] = until.ToUniversalTime();
        }
        catch
        {
            // a broken file is the same as no file: the next refusal writes it again
        }
    }

    void Save(DateTime now)
    {
        if (_file is null) return;
        try
        {
            var live = _until.Where(p => p.Value > now).ToDictionary(p => p.Key, p => p.Value.ToString("O", CultureInfo.InvariantCulture));
            lock (_fileLock)
            {
                if (live.Count == 0)
                {
                    if (File.Exists(_file)) File.Delete(_file);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var tmp = _file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(live));
                File.Move(tmp, _file, overwrite: true);
            }
        }
        catch
        {
            // failing to write it only means a restart asks again, as before
        }
    }
}
