using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sonora.Appearance.Theming;
using Sonora.Services;
using Sonora.Theming;

namespace Sonora;

public partial class App : Application
{
    /// <summary>Built-in and user themes (created before the main window so its resources exist on first render).</summary>
    public static ThemeCatalog Catalog { get; private set; } = null!;

    /// <summary>Applies themes to the application resources.</summary>
    public static ThemeService Themes { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        Catalog = new ThemeCatalog(Path.Combine(StateStore.Dir, "themes"));
        Catalog.Load();
        Themes = new ThemeService(Resources, Dispatcher);

        // WPF's default wheel scroll (SystemParameters.WheelScrollLines x a fixed line height) is tuned for a
        // mouse notch, not a precision trackpad's much more frequent, smaller deltas: it feels wildly oversensitive.
        // Scale every ScrollViewer's wheel scroll to a fixed, modest distance per event instead.
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnScrollViewerMouseWheel));

        var state = StateStore.LoadState();
        var content = state.Content ?? Sonora.Appearance.Content.ContentSettings.Default();
        content.Repair();
        Themes.SetCardSize(content.CardPixels);
        Themes.SetCoverBackground(state.CoverBackground || ThemeCatalog.WasImmersive(state.ThemeId), state.CoverBackgroundStrength, state.CoverBackgroundBlur);
        if (!string.IsNullOrEmpty(state.BackgroundImage) && File.Exists(state.BackgroundImage))
            Themes.SetCustomBackground(state.BackgroundImage, state.BackgroundImageStrength);
        Themes.Apply(Catalog.Get(state.ThemeId));

        base.OnStartup(e);
    }

    /// <summary>
    /// A bit under WPF's default per-notch distance (48): the first value, 16, was tuned for a trackpad and made
    /// a mouse wheel feel sluggish. The distance still follows the wheel delta, so a trackpad stays smooth.
    /// </summary>
    const double WheelScrollPixels = 40;

    static void OnScrollViewerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv || e.Handled) return;
        if (Sonora.Views.WheelStep.Owns(e.OriginalSource)) return; // a slider under the pointer takes the wheel
        var steps = e.Delta / 120.0; // 120 = one notch (Mouse.MouseWheelDeltaForOneLine)
        Sonora.Views.Motion.ScrollBy(sv, -steps * WheelScrollPixels);
        e.Handled = true;
    }
}
