using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Sonora.Views;

/// <summary>
/// A message that does not fit the one-line status of the window (a refusal from Spotify, an error with its
/// reason): a card in the top right corner of the screen that slides in, stays long enough to be read, and leaves
/// by itself. One instance is reused, so a second message replaces the first instead of stacking windows. It sits
/// at the top so it never covers the "now playing" bubble in the bottom corner.
/// </summary>
public partial class NoticeToastWindow : Window
{
    static NoticeToastWindow? _instance;

    /// <summary>How far to the right the card starts and ends; the window clips it, so it comes out of the screen's edge.</summary>
    const double Travel = 200;

    readonly DispatcherTimer _hide;
    readonly TranslateTransform _slide = new();
    bool _leaving;

    NoticeToastWindow()
    {
        InitializeComponent();
        Card.RenderTransform = _slide;
        if (Motion.Enabled) { _slide.X = Travel; Card.Opacity = 0; }
        _hide = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _hide.Tick += (_, _) => Dismiss();
        Closed += (_, _) => { if (ReferenceEquals(_instance, this)) _instance = null; };
        Loaded += (_, _) => Glide(0, 1, 320, new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    /// <summary>Shows the message (or replaces the one on show) and restarts its countdown.</summary>
    public static void Show(string message)
    {
        if (_instance == null)
        {
            _instance = new NoticeToastWindow { ShowActivated = false };
            _instance.MessageText.Text = message;
            _instance.Show();
            _instance.PlaceTopRight();
        }
        var toast = _instance;
        toast.MessageText.Text = message;
        toast._hide.Stop();
        if (toast._leaving)
        {
            // A new message while it was leaving: bring it back rather than let it close over the new text.
            toast._leaving = false;
            toast.Glide(0, 1, 240, new CubicEase { EasingMode = EasingMode.EaseOut });
        }
        toast.PlaceTopRight(); // the text may have changed its height
        toast._hide.Interval = ReadingTime(message);
        toast._hide.Start();
    }

    /// <summary>Long enough to read it: two seconds and a little more for each line's worth of text, within 4 and 10 s.</summary>
    static TimeSpan ReadingTime(string message) => TimeSpan.FromSeconds(Math.Clamp(2 + message.Length / 18.0, 4, 10));

    /// <summary>Closes it on the spot (the application is going down and nothing would be left to finish the animation).</summary>
    public static void CloseIfOpen() => _instance?.Close();

    void PlaceTopRight()
    {
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth;
        Top = area.Top;
    }

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

    void Dismiss()
    {
        _hide.Stop();
        if (_leaving) return;
        _leaving = true;
        Glide(Travel, 0, 260, new CubicEase { EasingMode = EasingMode.EaseIn }, () => { if (_leaving) Close(); });
    }

    void Notice_Click(object sender, MouseButtonEventArgs e) => Dismiss();
}
