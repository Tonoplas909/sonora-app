using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Sonora.ViewModels;

namespace Sonora.Views;

/// <summary>
/// The "now playing" bubble: a small window in the corner of the screen whenever the track changes, whether
/// it moved on by itself or you changed it, from Sonora or from outside. One instance is reused, so a quick run
/// of skips just restarts its timer instead of stacking windows.
/// </summary>
public partial class TrackToastWindow : Window
{
    static TrackToastWindow? _instance;

    readonly DispatcherTimer _hide;
    readonly Window _main;
    readonly TranslateTransform _slide = new();
    bool _leaving;

    /// <summary>How far to the right the bubble starts and ends. The window clips it, so it comes out of the screen's edge.</summary>
    const double Travel = 200;

    TrackToastWindow(MainViewModel vm, Window main)
    {
        InitializeComponent();
        DataContext = vm;
        _main = main;
        Card.RenderTransform = _slide;
        if (Motion.Enabled) { _slide.X = Travel; Card.Opacity = 0; } // starts off to the right, unseen
        _hide = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _hide.Tick += (_, _) => Dismiss();
        Closed += (_, _) => { if (ReferenceEquals(_instance, this)) _instance = null; };
        Loaded += (_, _) => Glide(0, 1, 380, new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    /// <summary>Slides and fades from wherever the bubble is now, so an interrupted entrance or exit turns around smoothly.</summary>
    void Glide(double toX, double toOpacity, int ms, IEasingFunction ease, Action? done = null)
    {
        if (!Motion.Enabled)
        {
            _slide.X = toX;
            Card.Opacity = toOpacity;
            done?.Invoke();
            return;
        }
        var slide = new DoubleAnimation(toX, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        if (done != null) slide.Completed += (_, _) => done();
        _slide.BeginAnimation(TranslateTransform.XProperty, slide);
        Card.BeginAnimation(OpacityProperty,
            new DoubleAnimation(toOpacity, TimeSpan.FromMilliseconds(ms * 0.8)) { EasingFunction = ease });
    }

    /// <summary>Goes the way it came, to the right, then closes.</summary>
    void Dismiss()
    {
        _hide.Stop();
        if (_leaving) return;
        _leaving = true;
        Glide(Travel, 0, 300, new CubicEase { EasingMode = EasingMode.EaseIn }, () => { if (_leaving) Close(); });
    }

    /// <summary>Shows (or re-shows) the bubble for the track playing now. Does nothing when switched off.</summary>
    public static void Notify(MainViewModel vm, Window main)
    {
        if (!vm.State.Features.TrackToast || vm.Player.Current == null) return;
        Show(vm, main);
    }

    /// <summary>Shows it whatever the switches say: the diagnostics render uses this.</summary>
    public static void Show(MainViewModel vm, Window main)
    {
        if (_instance == null)
        {
            _instance = new TrackToastWindow(vm, main) { ShowActivated = false };
            _instance.Show();
            _instance.PlaceBottomRight();
        }
        var toast = _instance;
        toast._hide.Stop();
        if (toast._leaving)
        {
            // Skipped again while it was leaving: bring it back rather than let it close under the new track.
            toast._leaving = false;
            toast.Glide(0, 1, 260, new CubicEase { EasingMode = EasingMode.EaseOut });
        }
        toast._hide.Interval = TimeSpan.FromSeconds(Math.Clamp(vm.State.Features.ToastSeconds, 2, 10));
        toast._hide.Start();
    }

    /// <summary>Slides it away, or closes it on the spot when the app is going down and nothing would be left to finish the animation.</summary>
    public static void HideIfOpen(bool animate = true)
    {
        if (animate) _instance?.Dismiss();
        else _instance?.Close();
    }

    void PlaceBottomRight()
    {
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth;
        Top = area.Bottom - ActualHeight;
    }

    void Toast_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_main.IsVisible) FullScreenWindow.CloseOpen(); // the full-screen view has put the main window away: back to it
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        Dismiss();
    }
}
