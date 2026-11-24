using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sonora.ViewModels;
using Sonora.Appearance.Localization;

namespace Sonora.Views.Panels;

public partial class LyricsPanel : PanelView
{
    ScrollViewer? _scroll;
    EventHandler? _scrollAnim;
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

    /// <summary>Right-click: wrong or missing lyrics on a local file can be picked by hand on LRCLIB.</summary>
    void Panel_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { } vm || vm.Player.Current is not { IsSpotify: false } track || Window.GetWindow(this) is not { } owner) return;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        TrackInteractions.Add(menu, Loc.T("menu.choose_lyrics"), "", () => LyricsPicker.Show(owner, vm, track));
        menu.IsOpen = true;
        e.Handled = true;
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
        StopScrollAnim(); // a running follow-animation would drag the list back under the pointer
    }

    void AnimateScrollTo(double target)
    {
        StopScrollAnim();
        var scroll = _scroll!;
        var start = scroll.VerticalOffset;
        var distance = target - start;
        if (Math.Abs(distance) < 1) return;

        // Driven by the render loop, so each step lands on a drawn frame: no judder from a timer drifting against it.
        var startTime = DateTime.UtcNow;
        const double durationMs = 650;
        EventHandler? frame = null;
        frame = (_, _) =>
        {
            var t = (DateTime.UtcNow - startTime).TotalMilliseconds / durationMs;
            if (t >= 1) { scroll.ScrollToVerticalOffset(target); StopScrollAnim(); return; }
            var eased = 1 - Math.Pow(1 - t, 5); // ease-out quintic: a long, soft landing on the line
            scroll.ScrollToVerticalOffset(start + distance * eased);
        };
        _scrollAnim = frame;
        CompositionTarget.Rendering += frame;
    }

    void StopScrollAnim()
    {
        if (_scrollAnim == null) return;
        CompositionTarget.Rendering -= _scrollAnim;
        _scrollAnim = null;
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
