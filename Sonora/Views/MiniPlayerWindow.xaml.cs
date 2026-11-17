using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Sonora.ViewModels;

namespace Sonora.Views;

/// <summary>
/// A small always-on-top player for when Sonora should stay out of the way: cover, title, seek bar and
/// transport. Opening it hides the main window; the restore button brings it back.
/// </summary>
public partial class MiniPlayerWindow : Window
{
    static MiniPlayerWindow? _open;

    readonly Window _main;

    MiniPlayerWindow(MainViewModel vm, Window main)
    {
        InitializeComponent();
        DataContext = vm;
        _main = main;
        Motion.FadeInWindow(this);
        Topmost = vm.State.Features.MiniPlayerOnTop;
        Opacity = Math.Clamp(vm.State.Features.MiniPlayerOpacity, 0.4, 1.0);

        // Bottom-right of the work area, like the bubble.
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 8;
        Top = area.Bottom - 160;

        Closed += (_, _) =>
        {
            if (!ReferenceEquals(_open, this)) return;
            _open = null;
            RestoreMain();
        };
    }

    /// <summary>Opens the mini player and hides the main window, or closes it and brings the window back.</summary>
    public static void Toggle(MainViewModel vm, Window main)
    {
        if (_open != null) { _open.Close(); return; }
        _open = new MiniPlayerWindow(vm, main);
        _open.Show();
        main.Hide();
        _open.Activate();
    }

    /// <summary>Re-applies the on-top and opacity settings to an open mini player.</summary>
    public static void ApplySettings(MainViewModel vm)
    {
        if (_open == null) return;
        _open.Topmost = vm.State.Features.MiniPlayerOnTop;
        _open.Opacity = Math.Clamp(vm.State.Features.MiniPlayerOpacity, 0.4, 1.0);
    }

    /// <summary>Closes it on shutdown without bringing the main window back (it is closing too).</summary>
    public static void CloseIfOpen()
    {
        if (_open is not { } w) return;
        _open = null; // the Closed handler checks this, so it skips the restore
        w.Close();
    }

    void RestoreMain()
    {
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    void Restore_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Dragging the frame moves the window, but a press on a control is left to that control.</summary>
    void Drag_Start(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is ButtonBase or Slider or Thumb) return;
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Space: vm.TogglePlayCommand.Execute(null); break;
            case Key.Right: vm.Player.Next(); break;
            case Key.Left: vm.Player.Previous(); break;
            default: return;
        }
        e.Handled = true;
    }

    void Seek_DragStarted(object sender, DragStartedEventArgs e) => ((MainViewModel)DataContext).Player.IsSeeking = true;

    void Seek_DragCompleted(object sender, DragCompletedEventArgs e) =>
        ((MainViewModel)DataContext).Player.EndSeek(((Slider)sender).Value);
}
