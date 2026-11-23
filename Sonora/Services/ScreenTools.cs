using System.Runtime.InteropServices;
using System.Text.Json;
using IOPath = System.IO.Path;

namespace Sonora.Services;

/// <summary>Where the main window was left, in the pixels Windows itself uses.</summary>
public sealed class SavedWindow
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public bool Maximized { get; set; }

    /// <summary>The full-screen "now playing" view was open when Sonora closed, on the monitor whose top-left is below.</summary>
    public bool FullScreen { get; set; }
    public int FullScreenX { get; set; }
    public int FullScreenY { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSane => Right - Left >= 200 && Bottom - Top >= 150;
}

/// <summary>One display, in virtual-screen pixels.</summary>
public readonly record struct Display(int Left, int Top, int Right, int Bottom, bool Primary)
{
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>
/// Window placement and monitor handling that WPF does not offer. Everything is in physical pixels, so it
/// holds across monitors with different scaling.
/// </summary>
public static class ScreenTools
{
    const int SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_SHOWMAXIMIZED = 3, SW_RESTORE = 9;
    const int WPF_RESTORETOMAXIMIZED = 2;
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPLACEMENT
    {
        public int Length, Flags, ShowCmd;
        public POINT MinPosition, MaxPosition;
        public RECT NormalPosition;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);

    static Display ToMonitor(MONITORINFO info) =>
        new(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom, (info.Flags & 1) != 0);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string? device, int index, ref DISPLAY_DEVICE info, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE info);

    /// <summary>
    /// The highest refresh rate among the screens on the desktop (60 when it cannot be read): how many frames a
    /// second the window can actually show, wherever it is moved.
    /// </summary>
    public static int HighestRefreshRate()
    {
        const int AttachedToDesktop = 1, CurrentSettings = -1;
        int best = 0;
        try
        {
            var device = new DISPLAY_DEVICE { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (int i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device.Size = Marshal.SizeOf<DISPLAY_DEVICE>())
            {
                if ((device.StateFlags & AttachedToDesktop) == 0) continue;
                var mode = new DEVMODE { Size = (short)Marshal.SizeOf<DEVMODE>() };
                // 0 and 1 mean "the hardware default": no usable figure.
                if (EnumDisplaySettings(device.DeviceName, CurrentSettings, ref mode) && mode.DisplayFrequency > 1)
                    best = Math.Max(best, mode.DisplayFrequency);
            }
        }
        catch { /* a driver without these answers: keep the default */ }
        return best > 0 ? best : 60;
    }

    /// <summary>All connected displays, left to right, so "next screen" reads naturally.</summary>
    public static List<Display> Monitors()
    {
        var list = new List<Display>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, _, _, _) =>
        {
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(m, ref info)) list.Add(ToMonitor(info));
            return true;
        }, IntPtr.Zero);
        return list.OrderBy(m => m.Left).ThenBy(m => m.Top).ToList();
    }

    /// <summary>The monitor a window mostly sits on.</summary>
    public static Display MonitorOf(IntPtr hwnd)
    {
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        var handle = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (handle != IntPtr.Zero && GetMonitorInfo(handle, ref info)) return ToMonitor(info);
        return Monitors().FirstOrDefault(m => m.Primary);
    }

    /// <summary>The monitor under the mouse pointer.</summary>
    public static Display MonitorUnderCursor()
    {
        GetCursorPos(out var p);
        var all = Monitors();
        foreach (var m in all) if (m.Contains(p.X, p.Y)) return m;
        return all.FirstOrDefault(m => m.Primary);
    }

    /// <summary>The monitor after the given one, wrapping round. The same monitor when there is only one.</summary>
    public static Display NextMonitor(Display current)
    {
        var all = Monitors();
        if (all.Count == 0) return current;
        var i = all.FindIndex(m => m.Left == current.Left && m.Top == current.Top);
        return all[(i + 1) % all.Count];
    }

    /// <summary>The connected monitor whose top-left corner is exactly here, if there still is one.</summary>
    public static Display? MonitorAt(int x, int y)
    {
        foreach (var m in Monitors()) if (m.Left == x && m.Top == y) return m;
        return null;
    }

    // ---------------- main window placement ----------------

    public static SavedWindow? Capture(IntPtr hwnd)
    {
        var wp = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref wp)) return null;
        bool maximized = wp.ShowCmd == SW_SHOWMAXIMIZED
                         || (wp.ShowCmd == SW_SHOWMINIMIZED && (wp.Flags & WPF_RESTORETOMAXIMIZED) != 0);
        var r = wp.NormalPosition;
        return new SavedWindow { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom, Maximized = maximized };
    }

    /// <summary>
    /// Puts the window back. A monitor that is gone since would leave it out of reach, so a position that
    /// touches no connected screen is ignored and the window keeps its default.
    /// </summary>
    public static bool Restore(IntPtr hwnd, SavedWindow saved)
    {
        if (!saved.IsSane) return false;
        bool reachable = Monitors().Any(m => saved.Left < m.Right && saved.Right > m.Left && saved.Top < m.Bottom && saved.Bottom > m.Top);
        if (!reachable) return false;

        var wp = new WINDOWPLACEMENT
        {
            Length = Marshal.SizeOf<WINDOWPLACEMENT>(),
            ShowCmd = saved.Maximized ? SW_SHOWMAXIMIZED : SW_SHOWNORMAL,
            NormalPosition = new RECT { Left = saved.Left, Top = saved.Top, Right = saved.Right, Bottom = saved.Bottom },
        };
        return SetWindowPlacement(hwnd, ref wp);
    }

    // ---------------- full-screen windows ----------------

    const int WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    /// <summary>
    /// Window hook for a borderless full-screen window: when it is maximized it fills exactly the monitor it is on,
    /// never more. Left to WPF, the size is worked out with another screen's scale on mixed-scale setups and the
    /// window spills over its neighbours.
    /// </summary>
    public static IntPtr FillMonitorHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.MaxPosition = new POINT { X = 0, Y = 0 }; // relative to the monitor's own top-left, as Windows expects
        mmi.MaxSize = new POINT { X = info.Monitor.Right - info.Monitor.Left, Y = info.Monitor.Bottom - info.Monitor.Top };
        mmi.MaxTrackSize = mmi.MaxSize;
        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    /// <summary>Makes a window fill the given monitor: back to normal, onto that monitor, then maximized there.</summary>
    public static void MaximizeOn(IntPtr hwnd, Display monitor)
    {
        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, IntPtr.Zero, monitor.Left + 40, monitor.Top + 40, 800, 500, SWP_NOZORDER | SWP_NOACTIVATE);
        ShowWindow(hwnd, SW_SHOWMAXIMIZED);
    }

    /// <summary>Restores a maximized window to a normal size centred on the pointer, ready to be dragged.</summary>
    public static void RestoreUnderCursor(IntPtr hwnd, int width = 960, int height = 600)
    {
        GetCursorPos(out var p);
        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, IntPtr.Zero, p.X - width / 2, p.Y - 24, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // ---------------- persistence ----------------

    static readonly string WindowFile = IOPath.Combine(StateStore.Dir, "window.json");

    public static SavedWindow? Load()
    {
        try
        {
            return File.Exists(WindowFile) ? JsonSerializer.Deserialize<SavedWindow>(File.ReadAllText(WindowFile)) : null;
        }
        catch { return null; }
    }

    public static void Save(SavedWindow window)
    {
        try
        {
            Directory.CreateDirectory(StateStore.Dir);
            var tmp = WindowFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(window));
            File.Move(tmp, WindowFile, overwrite: true);
        }
        catch { /* best effort */ }
    }
}
