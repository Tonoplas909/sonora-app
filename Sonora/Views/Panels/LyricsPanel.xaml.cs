using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sonora.ViewModels;

namespace Sonora.Views.Panels;

public partial class LyricsPanel : PanelView
{
    ScrollViewer? _scroll;
    DispatcherTimer? _scrollAnim;
    DateTime _manualUntil = DateTime.MinValue;

    /// <summary>How long the list leaves the reader alone after they scroll it by hand.</summary>
    static readonly TimeSpan ManualScrollPause = TimeSpan.FromSeconds(4);

    public LyricsPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    MainViewModel? Vm => DataContext as MainViewModel;

    void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel old) old.Lyrics.PropertyChanged -= OnLyricsChanged;
        if (e.NewValue is MainViewModel vm) vm.Lyrics.PropertyChanged += OnLyricsChanged;
    }

    void OnLyricsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LyricsViewModel.CurrentIndex)) return;
        var idx = Vm?.Lyrics.CurrentIndex ?? -1;
        if (idx < 0 || idx >= LinesList.Items.Count) return;
        FollowCurrentLine(idx);
    }

    /// <summary>Smoothly scrolls so the current line settles a little above the panel's vertical centre,
    /// karaoke-style, instead of the jump-only-when-it-would-leave-the-viewport of ScrollIntoView.</summary>
    void FollowCurrentLine(int idx)
    {
        if (DateTime.UtcNow < _manualUntil) return; // the reader is looking elsewhere in the lyrics
        _scroll ??= FindScrollViewer(LinesList);
        if (_scroll == null) { LinesList.ScrollIntoView(LinesList.Items[idx]); return; }

        LinesList.UpdateLayout(); // the new current line may have just grown (bold/larger): measure it after that
        if (LinesList.ItemContainerGenerator.ContainerFromIndex(idx) is not FrameworkElement container)
        {
            LinesList.ScrollIntoView(LinesList.Items[idx]);
            return;
        }

        var top = container.TranslatePoint(new Point(0, 0), LinesList).Y + _scroll.VerticalOffset;
        var target = Math.Max(0, top - _scroll.ViewportHeight * 0.4);
        AnimateScrollTo(target);
    }

    void LinesList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _manualUntil = DateTime.UtcNow + ManualScrollPause;
        _scrollAnim?.Stop(); // a running follow-animation would drag the list back under the pointer
    }

    void AnimateScrollTo(double target)
    {
        _scrollAnim?.Stop();
        var scroll = _scroll!;
        var start = scroll.VerticalOffset;
        var distance = target - start;
        if (Math.Abs(distance) < 1) return;

        var startTime = DateTime.UtcNow;
        var duration = TimeSpan.FromMilliseconds(400);
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - startTime).TotalMilliseconds / duration.TotalMilliseconds;
            if (t >= 1) { scroll.ScrollToVerticalOffset(target); timer.Stop(); return; }
            var eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
            scroll.ScrollToVerticalOffset(start + distance * eased);
        };
        _scrollAnim = timer;
        timer.Start();
    }

    static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    void Line_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LyricLineVm line } && Vm is { } vm)
            vm.Player.Seek(line.Time.TotalSeconds);
    }
}
