using System.Windows;

namespace Sonora.Services;

/// <summary>
/// Writes an unexpected error to <c>crash.log</c> in Sonora's data folder. A WPF app has no console, so without
/// this a failure while the window is opening just makes the process vanish.
/// </summary>
public static class CrashLog
{
    public static string Path => System.IO.Path.Combine(StateStore.Dir, "crash.log");

    public static void Write(string where, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(StateStore.Dir);
            File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n{ex}\n\n");
        }
        catch
        {
            // Nothing sensible is left to do when even the log cannot be written.
        }
    }

    /// <summary>Logs the error and tells the user where to find it, for a failure that ends the app.</summary>
    public static void Fatal(string where, Exception ex)
    {
        Write(where, ex);
        try { MessageBox.Show($"{ex.GetType().Name}: {ex.Message}\n\n{Path}", "Sonora", MessageBoxButton.OK, MessageBoxImage.Error); }
        catch { /* the message is also in the log */ }
    }
}
