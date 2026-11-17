using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Sonora.Services;

/// <summary>
/// Gives memory back to Windows while Sonora is out of sight. A minimized player has no use for the pages its
/// interface was drawn with: they leave the working set (Task Manager's figure) and stay in RAM on Windows'
/// standby list, so coming back is a quick soft fault, not a read from disk, and playback is not affected.
/// Nothing is freed that Sonora still needs: this changes what is resident, not what is allocated.
/// </summary>
public sealed class MemoryTrim
{
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);

    /// <summary>Waits a moment after the window is minimized, so a quick minimize-restore does not trim anything.</summary>
    static readonly TimeSpan Delay = TimeSpan.FromSeconds(4);

    readonly DispatcherTimer _timer;

    public MemoryTrim(Dispatcher dispatcher, Func<bool> isHidden)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = Delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (isHidden()) TrimNow();
        };
    }

    /// <summary>Call whenever the window state changes: arms the trim when hidden, cancels it otherwise.</summary>
    public void WindowStateChanged(bool hidden)
    {
        _timer.Stop();
        if (hidden) _timer.Start();
    }

    /// <summary>Compacts the managed heap, then lets go of every page that is not in use right now.</summary>
    public static void TrimNow()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch { /* best effort: memory stays where it was */ }
    }
}
