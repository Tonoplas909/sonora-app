using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Sonora.ViewModels;

namespace Sonora.Views.Panels;

/// <summary>How much of the track the player panel has room to show.</summary>
public enum PlayerPanelMode
{
    /// <summary>A transport bar: cover thumbnail, title, controls on one line.</summary>
    Compact,

    /// <summary>Wide but not deep: a large cover on the left, the track and its controls beside it.</summary>
    Wide,

    /// <summary>Deep enough for the cover to sit above the track and its controls.</summary>
    Tall,
}

public partial class PlayerPanel : PanelView
{
    // What each layout needs to be worth showing. A dock zone at the bottom is capped at 40% of the
    // window, so Wide — not Tall — is what a bottom player grows into.
    const double TallHeight = 360, TallWidth = 200;
    const double WideHeight = 210, WideWidth = 560;

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(PlayerPanelMode), typeof(PlayerPanel),
            new PropertyMetadata(PlayerPanelMode.Compact));

    /// <summary>Which of the three faces the panel is currently wearing.</summary>
    public PlayerPanelMode Mode
    {
        get => (PlayerPanelMode)GetValue(ModeProperty);
        private set => SetValue(ModeProperty, value);
    }

    public PlayerPanel() => InitializeComponent();

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        var (w, h) = (info.NewSize.Width, info.NewSize.Height);
        Mode = h >= TallHeight && w >= TallWidth ? PlayerPanelMode.Tall
            : h >= WideHeight && w >= WideWidth ? PlayerPanelMode.Wide
            : PlayerPanelMode.Compact;
    }

    MainViewModel? Vm => DataContext as MainViewModel;

    void Seek_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (Vm is { } vm) vm.Player.IsSeeking = true;
    }

    void Seek_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (Vm is { } vm) vm.Player.EndSeek(((Slider)sender).Value);
    }

    /// <summary>The like button swells when it is about to turn the heart on.</summary>
    void Like_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement button && Vm?.Player.Current is { IsLiked: false }) Motion.Pop(button);
    }

    void Devices_Click(object sender, RoutedEventArgs e) => TrackInteractions.ShowDevices((UIElement)sender);

    void Sleep_Click(object sender, RoutedEventArgs e) => TrackInteractions.ShowSleepTimer((UIElement)sender);

    void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) FullScreenWindow.Toggle(vm, Window.GetWindow(this));
    }

    void MiniPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) MiniPlayerWindow.Toggle(vm, Window.GetWindow(this));
    }
}
