using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Sonora.Appearance.Features;
using Sonora.Services;
using Sonora.ViewModels;

namespace Sonora.Views;

/// <summary>
/// Full-screen "now playing": giant cover, the current lyric line, and the transport. Shares the main
/// view model, so everything it shows and every button it offers is the same playback state.
/// </summary>
public partial class FullScreenWindow : Window
{
    static FullScreenWindow? _open;

    readonly Display _firstMonitor;
    readonly MainWindow? _main;
    IntPtr _hwnd;

    FullScreenWindow(MainViewModel vm, Window owner, Display? monitor)
    {
        InitializeComponent();
        DataContext = vm;
        // Not owned by the main window: an owned window is hidden along with its owner, and the main window is
        // put away while this view is open (below).
        Motion.FadeInWindow(this);
        // Opens on the screen Sonora is on (or the one it was last left on), not wherever Windows drops it.
        _firstMonitor = monitor ?? ScreenTools.MonitorOf(new WindowInteropHelper(owner).Handle);
        ScreenButton.Visibility = ScreenTools.Monitors().Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(ScreenTools.FillMonitorHook); // maximized = exactly this monitor
            ScreenTools.MaximizeOn(_hwnd, _firstMonitor);
        };

        // The player is not in the windows but in the view model, so the music carries on while the main window,
        // which this view replaces, gives back everything its interface holds (see MainWindow.SuspendUi).
        // A diagnostics snapshot keeps the main window as it is.
        if (owner is MainWindow main && !vm.SnapshotMode)
        {
            _main = main;
            main.SuspendUi();
        }
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, this)) _open = null;
            else return; // closed with the application: the main window is going too
            _main?.ResumeUi();
        };
    }

    /// <summary>Closes the open view, which brings the main window back (a click on the track bubble does this).</summary>
    public static void CloseOpen() => _open?.Close();

    /// <summary>Closes the view on shutdown without bringing the main window back (it is closing too).</summary>
    public static void CloseIfOpen()
    {
        if (_open is not { } w) return;
        _open = null; // the Closed handler checks this, so it skips the resume
        w.Close();
    }

    /// <summary>The screen the open full-screen view is on, or null when it is closed.</summary>
    public static Display? OpenOn => _open is { _hwnd: not 0 } w ? ScreenTools.MonitorOf(w._hwnd) : null;

    /// <summary>Opens it, or closes the one already open (F11 both ways).</summary>
    public static void Toggle(MainViewModel vm, Window owner, Display? monitor = null)
    {
        if (_open != null) { _open.Close(); return; }
        _open = new FullScreenWindow(vm, owner, monitor);
        _open.Show();
        _open.Activate();
    }

    // ---------------- changing screen ----------------

    /// <summary>Sends the view to the next screen, left to right, and round again.</summary>
    void NextScreen() => ScreenTools.MaximizeOn(_hwnd, ScreenTools.NextMonitor(ScreenTools.MonitorOf(_hwnd)));

    void Screen_Click(object sender, RoutedEventArgs e) => NextScreen();

    Point _dragOrigin;
    bool _dragArmed;

    /// <summary>Anything that is not a control can be grabbed and dragged to another screen.</summary>
    void Background_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragArmed = e.ChangedButton == MouseButton.Left && !IsControl(e.OriginalSource);
        _dragOrigin = e.GetPosition(this);
    }

    void Background_MouseUp(object sender, MouseButtonEventArgs e) => _dragArmed = false;

    void Background_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var moved = e.GetPosition(this) - _dragOrigin;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragArmed = false;

        // Like a title bar: let go of the maximized state, follow the pointer, then fill whichever screen it lands on.
        ScreenTools.RestoreUnderCursor(_hwnd);
        try { DragMove(); } catch (InvalidOperationException) { /* the button was released already */ }
        ScreenTools.MaximizeOn(_hwnd, ScreenTools.MonitorUnderCursor());
    }

    static bool IsControl(object source)
    {
        for (var d = source as DependencyObject; d != null; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                 ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase or System.Windows.Controls.Slider or Thumb) return true;
        return false;
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        switch (e.Key)
        {
            case Key.Escape:
            case Key.F11:
                Close();
                break;
            case Key.S:
                NextScreen();
                break;
            case Key.Space:
                vm.TogglePlayCommand.Execute(null);
                break;
            case Key.Right:
                vm.Player.Next();
                break;
            case Key.Left:
                vm.Player.Previous();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Seek_DragStarted(object sender, DragStartedEventArgs e) => ((MainViewModel)DataContext).Player.IsSeeking = true;

    void Seek_DragCompleted(object sender, DragCompletedEventArgs e) =>
        ((MainViewModel)DataContext).Player.EndSeek(((System.Windows.Controls.Slider)sender).Value);
}
