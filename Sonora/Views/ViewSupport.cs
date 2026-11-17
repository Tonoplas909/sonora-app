using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sonora.Appearance.Content;
using Sonora.Core;

namespace Sonora.Views;

/// <summary>
/// Track list column widths, shared by every list. Bound with Source={x:Static} because
/// ColumnDefinitions are not part of the visual tree.
/// </summary>
public sealed class ColumnLayout : ObservableObject
{
    public static ColumnLayout Current { get; } = new();

    GridLength _number = new(44), _art = new(48), _album = new(4, GridUnitType.Star), _like = new(44), _duration = new(60);
    public GridLength NumberWidth { get => _number; private set => Set(ref _number, value); }
    public GridLength ArtWidth { get => _art; private set => Set(ref _art, value); }
    public GridLength AlbumWidth { get => _album; private set => Set(ref _album, value); }
    public GridLength LikeWidth { get => _like; private set => Set(ref _like, value); }
    public GridLength DurationWidth { get => _duration; private set => Set(ref _duration, value); }

    public void Update(ContentSettings c)
    {
        NumberWidth = c.ShowNumber ? new GridLength(44) : new GridLength(0);
        ArtWidth = c.ShowArt ? new GridLength(48) : new GridLength(0);
        AlbumWidth = c.ShowAlbum ? new GridLength(4, GridUnitType.Star) : new GridLength(0);
        LikeWidth = c.ShowLike ? new GridLength(44) : new GridLength(0);
        DurationWidth = c.ShowDuration ? new GridLength(60) : new GridLength(0);
    }
}

/// <summary>Finding a named element inside a DataTemplate, which x:Name alone does not reach.</summary>
public static class VisualTree
{
    public static T? Find<T>(DependencyObject? root, string name) where T : FrameworkElement
    {
        if (root == null) return null;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T { } match && match.Name == name) return match;
            if (Find<T>(child, name) is { } found) return found;
        }
        return null;
    }
}

/// <summary>
/// Lets the mouse wheel move a slider: <c>v:WheelStep.Step="5"</c> makes one notch change the value by 5.
/// The app-wide wheel handler (<see cref="App"/>) steps aside for these sliders instead of scrolling the page.
/// </summary>
public static class WheelStep
{
    public static readonly DependencyProperty StepProperty =
        DependencyProperty.RegisterAttached("Step", typeof(double), typeof(WheelStep), new PropertyMetadata(0.0, OnStepChanged));

    public static double GetStep(DependencyObject d) => (double)d.GetValue(StepProperty);
    public static void SetStep(DependencyObject d, double value) => d.SetValue(StepProperty, value);

    static void OnStepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider) return;
        slider.PreviewMouseWheel -= OnWheel;
        if ((double)e.NewValue > 0) slider.PreviewMouseWheel += OnWheel;
    }

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var slider = (Slider)sender;
        slider.Value = Math.Clamp(slider.Value + e.Delta / 120.0 * GetStep(slider), slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    /// <summary>True when the wheel is over a slider that takes the wheel for itself.</summary>
    public static bool Owns(object? source)
    {
        for (var d = source as DependencyObject; d != null;
             d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                 ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is Slider s && GetStep(s) > 0) return true;
        return false;
    }
}

/// <summary>One shelf of the home page.</summary>
public sealed record HomeSectionView(string Id, string Title, IEnumerable Items, string Kind, ICommand? ShowAll)
{
    public bool HasShowAll => ShowAll != null;
}

public sealed class HomeSectionTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Albums { get; set; }
    public DataTemplate? Artists { get; set; }
    public DataTemplate? Playlists { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        (item as HomeSectionView)?.Kind switch
        {
            "artists" => Artists,
            "playlists" => Playlists,
            _ => Albums,
        };
}
