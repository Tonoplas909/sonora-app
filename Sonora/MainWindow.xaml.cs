using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Sonora.Appearance.Features;
using Sonora.Appearance.Layout;
using Sonora.Services;
using Sonora.ViewModels;
using Sonora.Views;
using Sonora.Views.Panels;

namespace Sonora;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        bool snapshot = args.Contains("--snapshot");

        // Diagnostics options: --theme <id>, --layout <file.json>, --log-dir <folder>
        if (Option(args, "--theme") is { } themeId) App.Themes.Apply(App.Catalog.Get(themeId));
        if (Option(args, "--log-dir") is { } logDir) ListeningLogService.Folder = logDir;
        DockLayout? stored = Option(args, "--layout") is { } layoutFile && File.Exists(layoutFile)
            ? DockLayout.Parse(File.ReadAllText(layoutFile))
            : snapshot ? null : StateStore.LoadLayout();

        _vm = new MainViewModel(Dispatcher) { SnapshotMode = snapshot };
        _vm.Layout.SaveDisabled = snapshot;
        _vm.Layout.Load(stored, Width, Height);
        DataContext = _vm;

        // Where the window was left: restored once the native window exists, in the pixels Windows uses.
        var savedWindow = snapshot ? null : ScreenTools.Load();
        SourceInitialized += (_, _) =>
        {
            _vm.WindowHandle = new WindowInteropHelper(this).Handle;
            // WindowStyle="None" windows maximize to the full monitor by default, covering the taskbar;
            // handling WM_GETMINMAXINFO ourselves restores the normal "maximize to the work area" behaviour.
            // The hook goes in first: a window left maximized is maximized again just below, and without it
            // that one maximize covered the taskbar (and overhung the screen by the resize border).
            HwndSource.FromHwnd(_vm.WindowHandle)?.AddHook(WndProc);
            if (savedWindow != null && ScreenTools.Restore(_vm.WindowHandle, savedWindow) && savedWindow.Maximized)
                WindowState = WindowState.Maximized;
            App.Themes.AttachWindow(_vm.WindowHandle); // title bar follows the theme colours
        };
        // Spacing and structural style changes need the dock rebuilt; zone sizes are bounded by the real window size.
        App.Themes.SpacingChanged += () => Host.Rebuild();
        App.Themes.StyleChanged += () => Host.Rebuild();
        SizeChanged += (_, _) => _vm.Layout.UpdateWindowSize(ActualWidth, ActualHeight);
        // Minimized, Sonora hands the pages it is not using back to Windows (see MemoryTrim).
        var trim = new MemoryTrim(Dispatcher, () => WindowState == WindowState.Minimized);
        StateChanged += (_, _) =>
        {
            UpdateMaxRestoreGlyph();
            trim.WindowStateChanged(WindowState == WindowState.Minimized);
        };
        UpdateMaxRestoreGlyph();
        string? shownPage = null;
        _vm.PropertyChanged += (_, e) =>
        {
            // A new page glides in; a refresh of the one on screen (same title) does not replay it.
            if (e.PropertyName == nameof(MainViewModel.CurrentPage) && _vm.CurrentPage?.Title is { } title && title != shownPage)
            {
                // Every page arrives with the cascade, the first one too (but not in a snapshot, which must be still).
                if (shownPage != null || !_vm.SnapshotMode) Views.Motion.PageEnter(PageHost);
                shownPage = title;
            }
            // Back from the browser sign-in: bring Sonora to the front.
            if (e.PropertyName == nameof(MainViewModel.ShowLoginGate) && !_vm.ShowLoginGate) Activate();
        };
        // "Now playing" bubble: only when Sonora is not the window in front, and never in snapshot mode.
        if (!snapshot)
        {
            _vm.Player.TrackStarted += _ => Dispatcher.InvokeAsync(() => TrackToastWindow.Notify(_vm, this));
            Activated += (_, _) => TrackToastWindow.HideIfOpen();
        }
        Loaded += async (_, _) =>
        {
            await _vm.InitializeAsync();
            // Closed on the full-screen "now playing" view: back to it, on the screen it was on.
            if (savedWindow is { FullScreen: true } && !_vm.ShowLoginGate && !snapshot)
                FullScreenWindow.Toggle(_vm, this, ScreenTools.MonitorAt(savedWindow.FullScreenX, savedWindow.FullScreenY));
            await SnapshotIfRequestedAsync();
        };
    }

    static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>
    /// Diagnostics: <c>Sonora.exe --snapshot out.png [view] [--theme id] [--layout file.json]</c>
    /// renders the window to a PNG, then exits. Views: home, albums, artists, settings, settings-bottom, appearance, liked, all, album,
    /// play, search:&lt;query&gt;, discover, tops, stats, fullscreen, toast, miniplayer, drm, mediacontrols, apicheck.
    /// </summary>
    async Task SnapshotIfRequestedAsync()
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, "--snapshot");
        if (i < 0 || i + 1 >= args.Length) return;
        var output = args[i + 1];
        var view = i + 2 < args.Length && !args[i + 2].StartsWith("--") ? args[i + 2] : "home";
        if (view == "apicheck")
        {
            // Read-only check of which Web API endpoints answer for this developer app (status codes and field names only).
            await File.WriteAllTextAsync(output + ".txt", await Spotify.ApiProbe.RunAsync(_vm.Spotify.Api, _vm.Spotify.Auth));
            Close();
            return;
        }
        if (view == "mediacontrols")
        {
            // Plays a local track muted, then asks Windows what it sees: proves the media overlay and the
            // media keys are actually wired, which a screenshot of Sonora itself cannot show.
            var report = "no local track to play";
            if (_vm.Library.Tracks.Count > 0)
            {
                _vm.Player.IsMuted = true;
                _vm.PlayTracks(_vm.Library.Tracks, 0, "Tous les titres");
                await Task.Delay(2500);
                try
                {
                    var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    var mine = manager.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId.Contains("Sonora", StringComparison.OrdinalIgnoreCase))
                               ?? manager.GetCurrentSession();
                    if (mine == null) report = "no session reported by Windows";
                    else
                    {
                        var media = await mine.TryGetMediaPropertiesAsync();
                        var info = mine.GetPlaybackInfo();
                        var timeline = mine.GetTimelineProperties();
                        report = $"app={mine.SourceAppUserModelId}\nstatus={info.PlaybackStatus}\n" +
                                 $"title={media?.Title}\nartist={media?.Artist}\nalbum={media?.AlbumTitle}\n" +
                                 $"next_enabled={info.Controls.IsNextEnabled}\nprevious_enabled={info.Controls.IsPreviousEnabled}\n" +
                                 $"pause_enabled={info.Controls.IsPauseEnabled}\nend={timeline.EndTime}";
                    }
                }
                catch (Exception ex) { report = "error=" + ex.Message; }
            }
            await File.WriteAllTextAsync(output + ".txt", report);
            Close();
            return;
        }
        if (view == "drm")
        {
            // Probes the WebView2 + Widevine + Web Playback SDK stack without signing in.
            var http = new System.Net.Http.HttpClient();
            var auth = new Spotify.SpotifyAuth(http);
            var engine = new Spotify.SpotifyEngine(new Spotify.SpotifyApi(http, auth), auth, new Spotify.SpotifyCatalog(), Dispatcher);
            string report;
            try
            {
                await engine.StartAsync(new WindowInteropHelper(this).Handle);
                for (int n = 0; n < 60 && (engine.DrmSupported == null || !engine.SdkLoaded); n++) await Task.Delay(250);
                report = $"webview2={CoreWebView2VersionInfo()}\nwidevine={engine.DrmSupported?.ToString() ?? "unknown"}\n" +
                         $"drm_error={engine.DrmError}\nsdk_loaded={engine.SdkLoaded}";
            }
            catch (Exception ex) { report = "error=" + ex; }
            await File.WriteAllTextAsync(output + ".txt", report);
            engine.Dispose();
            Close();
            return;
        }
        Window shot = this; // the full-screen view renders its own window instead of the main one
        switch (view)
        {
            case "albums": _vm.OpenAlbumsCommand.Execute(null); break;
            case "artists": _vm.OpenArtistsCommand.Execute(null); break;
            case "settings": _vm.OpenSettingsCommand.Execute(null); break;
            case "settings-bottom": // the end of the long settings page: the sections that do not fit the first screen
                _vm.OpenSettingsCommand.Execute(null);
                await Task.Delay(800);
                FirstScrollViewer(PageHost)?.ScrollToEnd();
                await Task.Delay(400);
                break;
            case "discover": _vm.OpenDiscoverCommand.Execute(null); break;
            case "tops": _vm.OpenTops(); break;
            case "stats": _vm.OpenStats(); break;
            case "appearance": _vm.OpenAppearanceCommand.Execute(null); break;
            case "liked": _vm.OpenLiked(); break;
            case "all": _vm.OpenAllTracks(); _vm.Layout.Show(PanelIds.Queue); break;
            case "album" when _vm.Library.Albums.Count > 0: _vm.OpenAlbum(_vm.Library.Albums[0]); break;
            case var s when s.StartsWith("search:"): _vm.SearchText = s[7..]; break;
            case "play" when _vm.Library.Tracks.Count > 0:
                _vm.Player.IsMuted = true; // silent self-test
                _vm.OpenAllTracks();
                _vm.Layout.Show(PanelIds.Queue);
                _vm.PlayTracks(_vm.Library.Tracks, 0, "Tous les titres");
                await Task.Delay(3000);
                // With a sandboxed listening log (--log-dir), run the track to its end and skip on, so the
                // render also proves a finished listen reaches the log.
                if (Services.ListeningLogService.IsSandboxed && _vm.Player.DurationSec > 5)
                {
                    _vm.Player.Seek(_vm.Player.DurationSec - 2);
                    await Task.Delay(1200);
                    _vm.Player.Next();
                    await Task.Delay(1200);
                }
                break;
            case "miniplayer" when _vm.Library.Tracks.Count > 0:
                _vm.Player.IsMuted = true;
                _vm.PlayTracks(_vm.Library.Tracks, 0, "Tous les titres");
                await Task.Delay(1500);
                MiniPlayerWindow.Toggle(_vm, this);
                Show(); // the render needs the main window up; the mini player hid it
                shot = Application.Current.Windows.OfType<MiniPlayerWindow>().First();
                break;
            case "toast" when _vm.Library.Tracks.Count > 0:
                _vm.Player.IsMuted = true;
                _vm.PlayTracks(_vm.Library.Tracks, 0, "Tous les titres");
                await Task.Delay(1500);
                TrackToastWindow.Show(_vm, this);
                shot = Application.Current.Windows.OfType<TrackToastWindow>().First();
                break;
            case "fullscreen" when _vm.Library.Tracks.Count > 0:
                _vm.Player.IsMuted = true;
                _vm.PlayTracks(_vm.Library.Tracks, 0, "Tous les titres");
                await Task.Delay(1500);
                FullScreenWindow.Toggle(_vm, this);
                shot = Application.Current.Windows.OfType<FullScreenWindow>().First();
                break;
        }
        await Task.Delay(1500);
        shot.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(shot);
        var root = (FrameworkElement)shot.Content;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(shot.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        rtb.Render(bg);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        await using (var fs = File.Create(output)) enc.Save(fs);
        Close();
    }

    /// <summary>The first scroll viewer under <paramref name="root"/>, depth first: the one of the page on show.</summary>
    static ScrollViewer? FirstScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FirstScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    static string CoreWebView2VersionInfo()
    {
        try { return Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (Exception ex) { return "absent (" + ex.Message + ")"; }
    }

    // ---------------- maximize bounds (keep the taskbar visible) ----------------

    const int WM_GETMINMAXINFO = 0x0024;
    const int MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        // Position and size relative to the monitor's own top-left, which Windows expects here.
        mmi.MaxPosition = new POINT { X = info.Work.Left - info.Monitor.Left, Y = info.Work.Top - info.Monitor.Top };
        mmi.MaxSize = new POINT { X = info.Work.Right - info.Work.Left, Y = info.Work.Bottom - info.Work.Top };
        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    // ---------------- custom title bar ----------------

    void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    void UpdateMaxRestoreGlyph()
    {
        bool max = WindowState == WindowState.Maximized;
        MaxButton.Content = max ? "" : "";
        MaxButton.ToolTip = max ? "Restaurer" : "Agrandir";
    }

    // ---------------- "Afficher" menu ----------------

    void Display_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        foreach (var id in PanelIds.All.Where(p => p != PanelIds.Player)) // the player can't be hidden
        {
            var panel = id;
            bool shown = DockLayoutOps.Find(_vm.Layout.Layout, panel) is { } at && !_vm.Layout.Layout.Zone(at.Zone).IsCollapsed;
            var item = new MenuItem { Header = PanelFactory.Title(panel), IsCheckable = false, IsChecked = shown, Icon = TrackInteractions.Glyph(PanelFactory.Glyph(panel)) };
            item.Click += (_, _) => { if (shown) _vm.Layout.Close(panel); else _vm.Layout.Show(panel); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        TrackInteractions.Add(menu, "Réinitialiser la disposition", "", () => _vm.Layout.Reset(App.Themes.Current));
        menu.IsOpen = true;
    }

    // ---------------- keyboard & mouse ----------------

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.ShowLoginGate) return; // the sign-in screen blocks the player until connected
        bool inText = Keyboard.FocusedElement is TextBox;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // While the settings page waits for a combination, every key belongs to it.
        if (_vm.IsCapturingShortcut)
        {
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.System or Key.LWin or Key.RWin)
                return; // a modifier on its own is not a shortcut yet
            var captured = key == Key.Escape
                ? null
                : Shortcuts.Build(
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
                    key.ToString());
            if (_vm.ApplyCapturedShortcut(captured)) e.Handled = true;
            return;
        }

        // Navigation and the media keys are fixed; everything else goes through the user's keyboard map.
        if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Left) _vm.NavigateBack();
        else if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Right) _vm.NavigateForward();
        else if (key == Key.MediaPlayPause) _vm.TogglePlayCommand.Execute(null);
        else if (key == Key.Escape && inText) Keyboard.ClearFocus();
        else if (RunShortcut(key, inText)) { /* handled by the map */ }
        else return;
        e.Handled = true;
    }

    /// <summary>Runs whatever the user has bound this key to, if anything. Returns true when it did.</summary>
    bool RunShortcut(Key key, bool inText)
    {
        var gesture = Shortcuts.Build(
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
            key.ToString());

        // A bare letter or Space belongs to whatever is being typed into, not to the player.
        if (inText && Keyboard.Modifiers == ModifierKeys.None) return false;

        var p = _vm.Player;
        switch (_vm.State.Features.Shortcuts.ActionFor(gesture))
        {
            case "playPause": _vm.TogglePlayCommand.Execute(null); return true;
            case "next": p.Next(); return true;
            case "previous": p.Previous(); return true;
            case "volumeUp": p.Volume += 10; return true;
            case "volumeDown": p.Volume -= 10; return true;
            case "shuffle": _vm.ToggleShuffleCommand.Execute(null); return true;
            case "repeat": _vm.CycleRepeatCommand.Execute(null); return true;
            case "queue": _vm.ToggleQueueCommand.Execute(null); return true;
            case "lyrics": _vm.ToggleLyricsCommand.Execute(null); return true;
            case "search": SearchBox.Focus(); SearchBox.SelectAll(); return true;
            case "fullScreen": FullScreenWindow.Toggle(_vm, this); return true;
            case "miniPlayer": MiniPlayerWindow.Toggle(_vm, this); return true;
            case "filter":
                if (VisualTree.Find<TextBox>(this, "ListFilterBox") is not { } filter) return false;
                filter.Focus();
                filter.SelectAll();
                return true;
            default: return false;
        }
    }

    // ---------------- put away while the full-screen view is open ----------------

    bool _uiSuspended, _showOnResume;

    /// <summary>
    /// The full-screen view replaces this window while it is open, so the window is hidden and lets go of what its
    /// interface holds: the panels with their lists and covers, and the page on show. Playback does not depend on any
    /// of it (the player lives in the view model, and the window itself stays, as it does behind the mini player).
    /// </summary>
    public void SuspendUi()
    {
        if (_uiSuspended) return;
        _uiSuspended = true;
        _showOnResume = IsVisible;
        Host.Suspended = true;
        BindingOperations.ClearBinding(PageHost, ContentControl.ContentProperty);
        PageHost.Content = null;
        Hide();
        // Give the freed objects a moment to become garbage, then let Windows have the memory back.
        _ = Task.Delay(1500).ContinueWith(_ => { if (_uiSuspended) MemoryTrim.TrimNow(); });
    }

    /// <summary>Builds the interface again and brings the window back (unless it was already hidden, behind the mini player).</summary>
    public void ResumeUi()
    {
        if (!_uiSuspended) return;
        _uiSuspended = false;
        PageHost.SetBinding(ContentControl.ContentProperty, new Binding(nameof(MainViewModel.CurrentPage)));
        Host.Suspended = false;
        if (!_showOnResume) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.ShowLoginGate) return;
        if (e.ChangedButton == MouseButton.XButton1) { _vm.NavigateBack(); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.XButton2) { _vm.NavigateForward(); e.Handled = true; }
    }

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_vm.SnapshotMode && ScreenTools.Capture(new WindowInteropHelper(this).Handle) is { } placement)
        {
            if (FullScreenWindow.OpenOn is { } screen)
                (placement.FullScreen, placement.FullScreenX, placement.FullScreenY) = (true, screen.Left, screen.Top);
            ScreenTools.Save(placement);
        }
        TrackToastWindow.HideIfOpen(animate: false); // a leftover window would keep the process alive
        NoticeToastWindow.CloseIfOpen();
        MiniPlayerWindow.CloseIfOpen();
        FullScreenWindow.CloseIfOpen(); // no longer owned by this window, so it would not close with it
        _vm.Shutdown();
    }

}

/// <summary>Minimal single-line text prompt, styled by the current theme.</summary>
static class Prompt
{
    public static string? Show(Window owner, string title, string initial)
    {
        var w = new Window
        {
            Title = title,
            Owner = owner,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        w.SetResourceReference(Window.BackgroundProperty, "Brush.Surface");
        w.SetResourceReference(Window.ForegroundProperty, "Brush.TextPrimary");
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 12, 0, 20) };
        var ok = new Button { Content = "Enregistrer", IsDefault = true };
        ok.SetResourceReference(FrameworkElement.StyleProperty, "AccentPill");
        ok.Click += (_, _) => w.DialogResult = true;
        var cancel = new Button { Content = "Annuler", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "PillOutline");
        cancel.Click += (_, _) => w.DialogResult = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var root = new StackPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = title, FontWeight = FontWeights.Bold };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "Size.Text.Section");
        root.Children.Add(heading);
        root.Children.Add(box);
        root.Children.Add(buttons);
        w.Content = root;
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return w.ShowDialog() == true && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }
}
