using System.Text.Json;
using Sonora.Models;
using IOPath = System.IO.Path;

namespace Sonora.Services;

public static class StateStore
{
    /// <summary>
    /// Where everything Sonora keeps lives (settings, library, themes, caches, the Spotify session):
    /// %LOCALAPPDATA%\Sonora, or the folder named by the SONORA_DATA_DIR environment variable. The variable lets
    /// a developer try the app on a clean or demo profile without touching the real one.
    /// </summary>
    public static readonly string Dir =
        Environment.GetEnvironmentVariable("SONORA_DATA_DIR") is { Length: > 0 } custom
            ? IOPath.GetFullPath(custom)
            : IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sonora");

    static readonly string StateFile = IOPath.Combine(Dir, "state.json");
    static readonly string LibraryFile = IOPath.Combine(Dir, "library.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static AppState LoadState() => Load<AppState>(StateFile) ?? new AppState();
    public static void SaveState(AppState state) => Save(StateFile, state);
    public static List<Track> LoadLibrary() => Load<List<Track>>(LibraryFile) ?? [];
    public static void SaveLibrary(List<Track> tracks) => Save(LibraryFile, tracks);

    static readonly string LayoutFile = IOPath.Combine(Dir, "layout.json");

    public static Sonora.Appearance.Layout.DockLayout? LoadLayout()
    {
        try { return File.Exists(LayoutFile) ? Sonora.Appearance.Layout.DockLayout.Parse(File.ReadAllText(LayoutFile)) : null; }
        catch { return null; }
    }

    public static void SaveLayout(Sonora.Appearance.Layout.DockLayout layout)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = LayoutFile + ".tmp";
            File.WriteAllText(tmp, layout.ToJson());
            File.Move(tmp, LayoutFile, overwrite: true);
        }
        catch { /* best effort */ }
    }

    static T? Load<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            using var s = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(s, Options);
        }
        catch
        {
            return default;
        }
    }

    static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = path + ".tmp";
            using (var s = File.Create(tmp))
                JsonSerializer.Serialize(s, value, Options);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Persisting is best effort.
        }
    }
}
