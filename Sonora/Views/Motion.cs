using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Sonora.Views;

/// <summary>
/// The small animations of the app. They only ever touch opacity, scroll offset and transforms, which are cheap
/// to render, they are short, and they stay off when the theme turns animations off or Windows is set to show none.
/// </summary>
public static class Motion
{
    /// <summary>A soft ease-out with a long glide at the end: motion starts at once and settles without a visible stop.</summary>
    static readonly IEasingFunction EaseOut = new QuinticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Decorative motion: off when the theme says so (Affiche and Fonction cut hard) or Windows has animations off.</summary>
    public static bool Enabled => !SoftwareRendering && SystemParameters.ClientAreaAnimation && (App.Themes?.Current.Animations ?? true);

    /// <summary>
    /// True when WPF draws on the CPU (no usable graphics driver, remote desktop, some virtual machines). Every
    /// animation then costs a full redraw, so they all stay off, and the wheel jumps instead of gliding.
    /// </summary>
    public static bool SoftwareRendering { get; } = (RenderCapability.Tier >> 16) == 0;

    /// <summary>
    /// WPF draws its animations at 60 frames a second whatever the screen does. On a faster screen (120, 144 Hz)
    /// 60 does not divide the refresh rate, so frames are held unevenly and motion judders. This lifts the cap to
    /// the fastest screen's rate. It must run before the first animation exists, so it is called from Main.
    /// </summary>
    public static void MatchDisplayRefresh()
    {
        int hz = Sonora.Services.ScreenTools.HighestRefreshRate();
        if (hz <= 60) return; // the default already matches
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = hz });
    }

    // ---------------- fades ----------------

    /// <summary>Fades an element in from a dim start, ending on the opacity it already had.</summary>
    public static void FadeIn(UIElement element, double from = 0.0, int ms = 260)
    {
        if (!Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, element.Opacity, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop,
        });
    }

    const int CascadeStepMs = 40, CascadeFadeMs = 380, CascadeRise = 18, CascadeMax = 10;

    /// <summary>
    /// A page arriving: its blocks (title, header, each shelf or section) fade in and rise into place one after
    /// the other, 40 ms apart, so the whole page is in within about three quarters of a second. A page with a single block
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
        var shift = new TranslateTransform(0, 14);
        page.RenderTransform = shift;
        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop,
        });
        var rise = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = EaseOut };
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
            content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(340))
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
            if (d is UIElement element && !Equals(e.OldValue, e.NewValue)) FadeIn(element, 0.15, 360);
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

    static readonly IEasingFunction SwipeEase = new QuinticEase { EasingMode = EasingMode.EaseOut };

    static void SwipeIn(FrameworkElement element, double fromX = 140)
    {
        if (!Enabled) return;
        var shift = new TranslateTransform(fromX, 0);
        element.RenderTransform = shift;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, element.Opacity, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop,
        });
        var slide = new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(700)) { EasingFunction = SwipeEase };
        slide.Completed += (_, _) => { if (ReferenceEquals(element.RenderTransform, shift)) element.ClearValue(UIElement.RenderTransformProperty); };
        shift.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    static void Rise(FrameworkElement element)
    {
        if (!Enabled) return;
        var shift = new TranslateTransform(0, 16);
        element.RenderTransform = shift;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, element.Opacity, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = EaseOut,
            FillBehavior = FillBehavior.Stop,
        });
        var rise = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = EaseOut };
        rise.Completed += (_, _) => { if (ReferenceEquals(element.RenderTransform, shift)) element.ClearValue(UIElement.RenderTransformProperty); };
        shift.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    /// <summary>A like: the button swells and settles.</summary>
    public static void Pop(FrameworkElement element)
    {
        if (!Enabled || RigOf(element) is not { } rig) return;
        rig.Center(element);
        var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(440) };
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(1.4, KeyTime.FromPercent(0.35), EaseOut));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }));
        frames.Completed += (_, _) => Settle(element, rig);
        rig.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, frames);
        rig.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, frames);
    }

    // ---------------- the transform rig ----------------

    /// <summary>
    /// The scale and shift the motions below share on one element, so a press, a hover lift and a pulse can
    /// overlap without replacing each other's transform. It is put on only while something moves, and taken
    /// off again once everything is back at rest.
    /// </summary>
    sealed class Rig
    {
        public readonly ScaleTransform Scale = new();
        public readonly TranslateTransform Shift = new();
        public readonly TransformGroup Group;
        public bool Pressed, Hovered;
        public Rig() => Group = new TransformGroup { Children = { Scale, Shift } };
        public void Center(FrameworkElement e) { Scale.CenterX = e.ActualWidth / 2; Scale.CenterY = e.ActualHeight / 2; }
    }

    static readonly ConditionalWeakTable<FrameworkElement, Rig> Rigs = new();

    /// <summary>The element's rig, put on now if needed; null when the element has a transform of its own, which is left alone.</summary>
    static Rig? RigOf(FrameworkElement e)
    {
        if (Rigs.TryGetValue(e, out var rig) && ReferenceEquals(e.RenderTransform, rig.Group)) return rig;
        if (e.RenderTransform is not MatrixTransform { Matrix.IsIdentity: true }) return null;
        rig = new Rig();
        Rigs.AddOrUpdate(e, rig);
        e.RenderTransform = rig.Group;
        return rig;
    }

    /// <summary>Takes the rig off once nothing holds it away from rest.</summary>
    static void Settle(FrameworkElement e, Rig rig)
    {
        if (rig.Pressed || rig.Hovered || !ReferenceEquals(e.RenderTransform, rig.Group)) return;
        if (Math.Abs(rig.Scale.ScaleX - 1) > 0.001 || Math.Abs(rig.Shift.Y) > 0.01 || Math.Abs(rig.Shift.X) > 0.01) return;
        e.ClearValue(UIElement.RenderTransformProperty);
        Rigs.Remove(e);
    }

    /// <summary>Glides the rig to where the element's state puts it: pressed in, lifted by the pointer, or at rest.</summary>
    static void Retarget(FrameworkElement e, Rig rig, int ms, IEasingFunction ease)
    {
        rig.Center(e);
        var scale = new DoubleAnimation(rig.Pressed ? PressedScale(e) : 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        scale.Completed += (_, _) => Settle(e, rig);
        rig.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        rig.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        rig.Shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(rig.Hovered && GetHoverLift(e) ? -HoverLiftPx : 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
    }

    /// <summary>
    /// The element arrives: it fades in from <paramref name="fromOpacity"/> while its scale and height ease from
    /// the given start into place. Used for popups opening, panels and blocks appearing, and pulses.
    /// </summary>
    static void Enter(FrameworkElement e, double fromOpacity, double fromScale, double fromY, int ms, IEasingFunction? ease = null)
    {
        if (!Enabled) return;
        ease ??= EaseOut;
        if (fromOpacity < 1)
            e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(fromOpacity, e.Opacity, TimeSpan.FromMilliseconds(Math.Min(ms, 300)))
            {
                EasingFunction = EaseOut,
                FillBehavior = FillBehavior.Stop,
            });
        if ((fromScale == 1 && fromY == 0) || RigOf(e) is not { } rig) return;
        rig.Center(e);
        var scale = new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        scale.Completed += (_, _) => Settle(e, rig);
        rig.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        rig.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        rig.Shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
    }

    /// <summary>A panel or block coming into view: fades in while rising a few pixels.</summary>
    public static void Arrive(FrameworkElement element) => Enter(element, 0, 1, 12, 420);

    // ---------------- pointer feedback, for every button of the app ----------------

    const double HoverLiftPx = 4;
    static readonly IEasingFunction Spring = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 };

    /// <summary>
    /// Wires the feedback every button gets, whatever style draws it: pressed, it sinks a little and springs
    /// back when let go; menus and tooltips open with a short fade and drop instead of popping.
    /// </summary>
    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPress), true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnRelease), true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseLeaveEvent, new MouseEventHandler(OnRelease), true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.LostMouseCaptureEvent, new MouseEventHandler(OnRelease), true); // a dialog took the pointer mid-press
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((s, _) => Enter((FrameworkElement)s, 0, 0.95, -8, 300)));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler((s, _) => Enter((FrameworkElement)s, 0, 1, 6, 260)));
    }

    /// <summary>
    /// How far a pressed element sinks: about four pixels whatever its size, so a small icon visibly gives way
    /// and a wide card only just does.
    /// </summary>
    static double PressedScale(FrameworkElement e) => 1 - Math.Clamp(4 / Math.Max(1, Math.Max(e.ActualWidth, e.ActualHeight)), 0.015, 0.08);

    /// <summary>Sliders' track buttons repeat while held and the switches have their own knob motion: neither sinks.</summary>
    static bool Sinks(object sender, RoutedEventArgs e) =>
        sender is ButtonBase button and not RepeatButton and not CheckBox && InnermostButton(e.OriginalSource) == button;

    /// <summary>The button the pointer is really on: a play button inside a card sinks, not the card around it.</summary>
    static ButtonBase? InnermostButton(object? source)
    {
        var node = source as DependencyObject;
        while (node != null && node is not ButtonBase)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as ButtonBase;
    }

    static void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (!Enabled || !Sinks(sender, e) || RigOf((FrameworkElement)sender) is not { } rig) return;
        rig.Pressed = true;
        Retarget((FrameworkElement)sender, rig, 140, EaseOut);
    }

    static void OnRelease(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element || !Rigs.TryGetValue(element, out var rig) || !rig.Pressed) return;
        rig.Pressed = false;
        Retarget(element, rig, 520, Spring);
    }

    /// <summary>Cards rise a few pixels under the pointer, and settle back when it leaves.</summary>
    public static readonly DependencyProperty HoverLiftProperty =
        DependencyProperty.RegisterAttached("HoverLift", typeof(bool), typeof(Motion), new PropertyMetadata(false, (d, e) =>
        {
            if (d is not FrameworkElement element || e.NewValue is not true || e.OldValue is true) return;
            element.MouseEnter += (_, _) =>
            {
                if (!Enabled || !GetHoverLift(element) || RigOf(element) is not { } rig) return;
                rig.Hovered = true;
                Retarget(element, rig, 320, EaseOut);
            };
            element.MouseLeave += (_, _) =>
            {
                if (!Rigs.TryGetValue(element, out var rig) || !rig.Hovered) return;
                rig.Hovered = false;
                Retarget(element, rig, 420, EaseOut);
            };
        }));

    public static bool GetHoverLift(DependencyObject d) => (bool)d.GetValue(HoverLiftProperty);
    public static void SetHoverLift(DependencyObject d, bool v) => d.SetValue(HoverLiftProperty, v);

    // ---------------- appearing and changing ----------------

    /// <summary>Whenever the bound value changes (not when it is first set), the element shrinks and springs back: play turned to pause, shuffle turned on.</summary>
    public static readonly DependencyProperty PulseOnChangeProperty =
        DependencyProperty.RegisterAttached("PulseOnChange", typeof(object), typeof(Motion), new PropertyMetadata(null, (d, e) =>
        {
            if (d is FrameworkElement { IsLoaded: true } element && e.OldValue != null && !Equals(e.OldValue, e.NewValue))
                Enter(element, 1, 0.85, 0, 560, Spring);
        }));

    public static object? GetPulseOnChange(DependencyObject d) => d.GetValue(PulseOnChangeProperty);
    public static void SetPulseOnChange(DependencyObject d, object? v) => d.SetValue(PulseOnChangeProperty, v);

    /// <summary>Each time the element is shown, it fades in instead of appearing at once (a hover button, a playing marker).</summary>
    public static readonly DependencyProperty FadeOnShowProperty =
        DependencyProperty.RegisterAttached("FadeOnShow", typeof(bool), typeof(Motion), new PropertyMetadata(false, (d, e) =>
        {
            if (d is UIElement element && e.NewValue is true && e.OldValue is not true)
                element.IsVisibleChanged += (_, v) => { if (v.NewValue is true && GetFadeOnShow(element)) FadeIn(element, 0, 220); };
        }));

    public static bool GetFadeOnShow(DependencyObject d) => (bool)d.GetValue(FadeOnShowProperty);
    public static void SetFadeOnShow(DependencyObject d, bool v) => d.SetValue(FadeOnShowProperty, v);

    /// <summary>Each time the element is shown, it fades in while rising into place (a prompt, a popup, a sign-in card).</summary>
    public static readonly DependencyProperty ArriveOnShowProperty =
        DependencyProperty.RegisterAttached("ArriveOnShow", typeof(bool), typeof(Motion), new PropertyMetadata(false, (d, e) =>
        {
            if (d is FrameworkElement element && e.NewValue is true && e.OldValue is not true)
                element.IsVisibleChanged += (_, v) => { if (v.NewValue is true && GetArriveOnShow(element)) Arrive(element); };
        }));

    public static bool GetArriveOnShow(DependencyObject d) => (bool)d.GetValue(ArriveOnShowProperty);
    public static void SetArriveOnShow(DependencyObject d, bool v) => d.SetValue(ArriveOnShowProperty, v);

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
            // Frame-rate independent exponential approach (time constant about 85 ms): a longer, softer glide.
            _viewer.ScrollToVerticalOffset(_viewer.VerticalOffset + diff * (1 - Math.Exp(-dt * 12)));
        }

        void Stop()
        {
            if (!_running) return;
            CompositionTarget.Rendering -= OnFrame;
            _running = false;
        }
    }
}
