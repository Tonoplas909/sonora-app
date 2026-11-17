using System.Text;
using System.Text.Json;
using Sonora.Appearance.Features;
using IOPath = System.IO.Path;

namespace Sonora.Services;

/// <summary>
/// Sonora's own listening history, kept on this machine only: one JSON object per line, appended as each
/// track finishes. Spotify's API gives a third-party app no play counts at all and only the last 50 plays,
/// so this is the only way Sonora can answer "what did I actually listen to" over any period.
/// Append-only and line-based on purpose: a torn write costs one line, never the whole history.
/// </summary>
public sealed class ListeningLogService
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>
    /// Where the log lives. Diagnostics renders point it at a throwaway folder (<c>--log-dir</c>) so they
    /// never read or write the real history; everything else leaves it alone.
    /// </summary>
    public static string Folder
    {
        get => _folder;
        set { _folder = value; IsSandboxed = true; }
    }
    static string _folder = StateStore.Dir;

    /// <summary>True once <see cref="Folder"/> points somewhere other than the real history.</summary>
    public static bool IsSandboxed { get; private set; }

    static string LogFile => IOPath.Combine(Folder, "listening.jsonl");

    readonly List<ListenEntry> _entries = [];
    readonly SemaphoreSlim _writing = new(1, 1);

    public IReadOnlyList<ListenEntry> Entries => _entries;

    /// <summary>Reads the log. Unreadable lines are skipped rather than failing the whole history.</summary>
    public async Task LoadAsync()
    {
        _entries.Clear();
        try
        {
            if (!File.Exists(LogFile)) return;
            foreach (var line in await File.ReadAllLinesAsync(LogFile))
            {
                if (line.Length == 0) continue;
                try
                {
                    if (JsonSerializer.Deserialize<ListenEntry>(line, Options) is { } entry) _entries.Add(entry);
                }
                catch (JsonException)
                {
                    // a half-written line from a previous crash: skip it
                }
            }
        }
        catch
        {
            // no history is better than no app
        }
    }

    /// <summary>Records one finished listen, in memory at once and on disk shortly after.</summary>
    public void Append(ListenEntry entry)
    {
        _entries.Add(entry);
        _ = WriteAsync(entry);
    }

    async Task WriteAsync(ListenEntry entry)
    {
        await _writing.WaitAsync();
        try
        {
            Directory.CreateDirectory(Folder);
            await File.AppendAllTextAsync(LogFile, JsonSerializer.Serialize(entry, Options) + "\n", Encoding.UTF8);
        }
        catch
        {
            // the entry stays in memory for this session; losing a line is not worth an error to the user
        }
        finally
        {
            _writing.Release();
        }
    }

    public async Task ClearAsync()
    {
        _entries.Clear();
        await _writing.WaitAsync();
        try { if (File.Exists(LogFile)) File.Delete(LogFile); }
        catch { /* nothing useful to do */ }
        finally { _writing.Release(); }
    }
}
