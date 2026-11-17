using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sonora.Views;

/// <summary>
/// The small animations of the app. They only ever touch opacity, scroll offset and transforms, which are cheap
/// to render, they are short, and they stay off when the theme turns animations off or Windows is set to show none.
/// </summary>
public static class Motion
{
    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Decorative motion: off when the theme says so (Affiche and Fonction cut hard) or Windows has animations off.</summary>
    public static bool Enabled => !SoftwareRendering && SystemParameters.ClientAreaAnimation && (App.Themes?.Current.Animations ?? true);

    /// <summary>
    /// True when WPF draws on the CPU (no usable graphics driver, remote desktop, some virtual machines). Every
    /// animation then costs a full redraw, so they all stay off, and the wheel jumps instead of gliding.
    /// </summary>
    public static bool SoftwareRendering { get; } = (RenderCapability.Tier >> 16) == 0;

    // ---------------- fades ----------------

    /// <summary>Fades an element in from a dim start, ending on the opacity it already had.</summary>
    public static void FadeIn(UIElement element, double from = 0.0, int ms = 180)
    {
        if (!Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, element.Opacity, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop,
        });
    }

    const int CascadeStepMs = 35, CascadeFadeMs = 200, CascadeRise = 14, CascadeMax = 10;

    /// <summary>
    /// A page arriving: its blocks (title, header, each shelf or section) fade in and rise into place one after
    /// the other, 35 ms apart, so the whole page is in within about half a second. A page with a single block
    /// arrives as a whole instead. <paramref name="host"/> is the control that shows the current page.
    /// </summary>
    public static void PageEnter(FrameworkElement host)
    {
        if (!Enabled) return;
        host.UpdateLayout(); // the new page is built now, so its blocks can be found before the first frame is drawn
        var blocks = PageBlocks(host);
        if (blocks.Count < 2) { PageEnterWhole(host); return; }
        for (int i = 0; i < blocks.Count; i++) CascadeIn(blocks[i], i * CascadeStepMs);
    }

    /// <summary>The blocks a page is made of: the children of the first panel that holds several, shelves of a home page included.</summary>
    static List<FrameworkElement> PageBlocks(FrameworkElement host)
    {
        DependencyObject? node = VisualTreeHelper.GetChildrenCount(host) > 0 ? VisualTreeHelper.GetChild(host, 0) : null;
        for (int depth = 0; node != null && depth < 10; depth++)
        {
            switch (node)
            {
                case ContentPresenter cp: node = VisualTreeHelper.GetChildrenCount(cp) > 0 ? VisualTreeHelper.GetChild(cp, 0) : null; break;
                case ScrollViewer sv: node = sv.Content as DependencyObject; break; // not its visual tree: that is the scroll bars
                case Decorator d: node = d.Child; break;
                case ContentControl cc: node = cc.Content as DependencyObject; break;
                case Panel p:
                    var kids = p.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible).ToList();
                    if (kids.Count >= 2) return ExpandLists(kids);
                    node = kids.Count == 1 ? kids[0] : null;
                    break;
                default: return [];
            }
        }
        return [];
    }

    /// <summary>A plain list of a few items (the shelves of the home page) arrives item by item, not as one block.</summary>
    static List<FrameworkElement> ExpandLists(List<FrameworkElement> kids)
    {
        var blocks = new List<FrameworkElement>();
        foreach (var kid in kids)
        {
            if (kid is ItemsControl { Items.Count: > 0 and <= CascadeMax } list && !VirtualizingPanel.GetIsVirtualizing(list))
            {
                for (int i = 0; i < list.Items.Count; i++)
                    if (list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement { Visibility: Visibility.Visible } item) blocks.Add(item);
            }
            else blocks.Add(kid);
            if (blocks.Count >= CascadeMax) break;
        }
        return blocks;
    }

    /// <summary>Invisible until its turn, then fades in while rising into place.</summary>
    static void CascadeIn(FrameworkElement block, int delayMs)
    {
        var end = block.Opacity;
        var start = TimeSpan.FromMilliseconds(delayMs);
        var stop = TimeSpan.FromMilliseconds(delayMs + CascadeFadeMs);

        var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delayMs > 0) fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(start)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(end, KeyTime.FromTimeSpan(stop), EaseOut));
        block.BeginAnimation(UIElement.OpacityProperty, fade);

        if (block.RenderTransform is not MatrixTransform { Matrix.IsIdentity: true }) return; // it has a transform of its own: only the fade
        var shift = new TranslateTransform(0, CascadeRise);
        block.RenderTransform = shift;
        var rise = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        rise.KeyFrames.Add(new LinearDoubleKeyFrame(CascadeRise, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delayMs > 0) rise.KeyFrames.Add(new LinearDoubleKeyFrame(CascadeRise, KeyTime.FromTimeSpan(start)));
        rise.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(stop), EaseOut));
        rise.Completed += (_, _) => { if (ReferenceEquals(block.RenderTransform, shift)) block.ClearValue(UIElement.RenderTransformProperty); };
        shift.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    /// <summary>A page with a single block: it fades in while rising a few pixels into place.</summary>
    static void PageEnterWhole(FrameworkElement page)
    {
        var shift = new TranslateTransform(0, 10);
        page.RenderTransform = shift;
        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop,
        });
        var rise = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut };
        rise.Completed += (_, _) => { if (ReferenceEquals(page.RenderTransform, shift)) page.ClearValue(UIElement.RenderTransformProperty); };
        shift.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    /// <summary>A window opening: its content fades in rather than popping. Call once the content is set.</summary>
    public static void FadeInWindow(Window window)
    {
        if (!Enabled || window.Content is not UIElement content) return;
        content.Opacity = 0;
        window.Loaded += (_, _) =>
        {
            content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = EaseOut,
                FillBehavior = FillBehavior.Stop,
            });
            content.Opacity = 1; // what the element goes back to once the animation has ended
        };
    }

    // ---------------- change feedback ----------------

    /// <summary>Whenever the bound value changes, the element dips and fades back: the track changed, the text is not stale.</summary>
    public static readonly DependencyProperty OnChangeProperty =
        DependencyProperty.RegisterAttached("OnChange", typeof(object), typeof(Motion), new PropertyMetadata(null, (d, e) =>
        {
            if (d is UIElement element && !Equals(e.OldValue, e.NewValue)) FadeIn(element, 0.15, 240);
        }));

    public static object? GetOnChange(DependencyObject d) => d.GetValue(OnChangeProperty);
    public static void SetOnChange(DependencyObject d, object? v) => d.SetValue(OnChangeProperty, v);

    /// <summary>
    /// Whenever the bound text changes, the element rises a little into place while fading in: a lyric line
    /// that takes over from the last one, rather than one that is swapped in place.
    /// </summary>
    public static readonly DependencyProperty RiseOnChangeProperty =
        DependencyProperty.RegisterAttached("RiseOnChange", typeof(object), typeof(Motion), new PropertyMetadata(null, (d, e) =>
        {
            if (d is FrameworkElement element && e.NewValue != null && !Equals(e.OldValue, e.NewValue)) Rise(element);
        }));

    public static object? GetRiseOnChange(DependencyObject d) => d.GetValue(RiseOnChangeProperty);
    public static void SetRiseOnChange(DependencyObject d, object? v) => d.SetValue(RiseOnChangeProperty, v);

    /// <summary>
    /// Whenever the bound value changes, the element sweeps in from the right to its place while fading in: the
    /// new track coming on from the right, as the old one leaves.
    /// </summary>
    public static readonly DependencyProperty SwipeOnChangeProperty =
        DependencyProperty.RegisterAttached("SwipeOnChange", typeof(object), typeof(Motion), new PropertyMetadata(null, (d, e) =>
        {
            if (d is FrameworkElement element && e.NewValue != null && !Equals(e.OldValue, e.NewValue)) SwipeIn(element);
        }));

    public static object? GetSwipeOnChange(DependencyObject d) => d.GetValue(SwipeOnChangeProperty);
    public static void SetSwipeOnChange(DependencyObject d, object? v) => d.SetValue(SwipeOnChangeProperty, v);

    static readonly IEasingFunction SwipeEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    static void SwipeIn(FrameworkElement element, double fromX = 140)
    {
        if (!Enabled) return;
        var shift = new TranslateTransform(fromX, 0);
        element.RenderTransform = shift;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, element.Opacity, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop,
        });
        var slide = new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(560)) { EasingFunction = SwipeEase };
        slide.Completed += (_, _) => { if (ReferenceEquals(element.RenderTransform, shift)) element.ClearValue(UIElement.RenderTransformProperty); };
        shift.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    static void Rise(FrameworkElement element)
    {
        if (!Enabled) return;
        var shift = new TranslateTransform(0, 16);
        element.RenderTransform = shift;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, element.Opacity, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop,
        });
        var rise = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(340)) { EasingFunction = EaseOut };
        rise.Completed += (_, _) => { if (ReferenceEquals(element.RenderTransform, shift)) element.ClearValue(UIElement.RenderTransformProperty); };
        shift.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    /// <summary>A like: the button swells and settles.</summary>
    public static void Pop(FrameworkElement element)
    {
        if (!Enabled) return;
        var scale = new ScaleTransform(1, 1);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(300) };
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(1.4, KeyTime.FromPercent(0.35), EaseOut));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }));
        frames.Completed += (_, _) => { if (ReferenceEquals(element.RenderTransform, scale)) element.ClearValue(UIElement.RenderTransformProperty); };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, frames);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, frames);
    }

    // ---------------- smooth wheel scrolling ----------------

    static readonly ConditionalWeakTable<ScrollViewer, SmoothScroll> Scrollers = new();

    /// <summary>
    /// Moves a scroll viewer by <paramref name="delta"/> pixels, easing towards the target over a few frames so
    /// the wheel glides instead of stepping. Successive notches add up. This is not decorative, so only
    /// Windows' own animation setting turns it off.
    /// </summary>
    public static void ScrollBy(ScrollViewer viewer, double delta)
    {
        if (SoftwareRendering || !SystemParameters.ClientAreaAnimation)
        {
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset + delta);
            return;
        }
        Scrollers.GetValue(viewer, v => new SmoothScroll(v)).By(delta);
    }

    sealed class SmoothScroll
    {
        readonly ScrollViewer _viewer;
        double _target;
        bool _running;
        long _last;

        public SmoothScroll(ScrollViewer viewer)
        {
            _viewer = viewer;
            // The user dragged the scroll bar or used the keyboard: follow, do not fight.
            viewer.ScrollChanged += (_, e) => { if (!_running && e.VerticalChange != 0) _target = viewer.VerticalOffset; };
            viewer.Unloaded += (_, _) => Stop();
        }

        public void By(double delta)
        {
            if (!_running) _target = _viewer.VerticalOffset;
            _target = Math.Clamp(_target + delta, 0, Math.Max(0, _viewer.ScrollableHeight));
            if (_running) return;
            _running = true;
            _last = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnFrame; // only attached while a scroll is gliding
        }

        void OnFrame(object? sender, EventArgs e)
        {
            long now = Stopwatch.GetTimestamp();
            double dt = Math.Min(0.05, (now - _last) / (double)Stopwatch.Frequency);
            _last = now;

            double diff = _target - _viewer.VerticalOffset;
            if (Math.Abs(diff) < 0.5)
            {
                _viewer.ScrollToVerticalOffset(_target);
                Stop();
                return;
            }
            // Frame-rate independent exponential approach (time constant about 55 ms).
            _viewer.ScrollToVerticalOffset(_viewer.VerticalOffset + diff * (1 - Math.Exp(-dt * 18)));
        }

        void Stop()
        {
            if (!_running) return;
            CompositionTarget.Rendering -= OnFrame;
            _running = false;
        }
    }
}
