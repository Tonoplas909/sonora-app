using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sonora.Appearance.Features;
using Sonora.ViewModels;

namespace Sonora.Views;

public partial class Pages : ResourceDictionary
{
    public Pages() => InitializeComponent();

    /// <summary>"More options" for whatever the page is about: an album, a show, an artist or a playlist.</summary>
    void PageMore_Click(object sender, RoutedEventArgs e)
    {
        var anchor = (FrameworkElement)sender;
        object? subject = anchor.DataContext switch
        {
            TracksPage { Playlist: { } p } => p,
            TracksPage { SpotifyPlaylist: { } sp } => sp,
            TracksPage t => t.ArtSource,
            ArtistPage a => a.Artist,
            _ => null,
        };
        var tracks = (anchor.DataContext as TracksPage)?.Tracks;
        var title = (anchor.DataContext as PageViewModel)?.Title ?? "playlist";
        var menu = TrackInteractions.BuildPageMenu(subject, Window.GetWindow(anchor), tracks, title);
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    void Sort_Click(object sender, RoutedEventArgs e)
    {
        var anchor = (FrameworkElement)sender;
        if (anchor.DataContext is not TracksPage page) return;
        var menu = TrackInteractions.BuildSortMenu(page);
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Swaps the big header (cover + kind/title/subtitle + play/shuffle row) for a single compact line
    /// (title plus small play/shuffle buttons) once the track list — its own, inner scroll region — has
    /// scrolled a little, so more of the list is visible without losing the header entirely. Each piece
    /// uses a LayoutTransform (not just Opacity), so the collapsed row's height actually shrinks away.
    /// </summary>
    void TracksHeader_Loaded(object sender, RoutedEventArgs e)
    {
        var root = (FrameworkElement)sender;
        if (root.FindName("TrackListBox") is not ListBox list) return;
        if (root.FindName("HeaderBand") is not UIElement headerBand) return;
        if (root.FindName("HeaderScale") is not ScaleTransform headerScale) return;
        if (root.FindName("BigButtonsRow") is not UIElement buttonsRow) return;
        if (root.FindName("ButtonsScale") is not ScaleTransform buttonsScale) return;
        if (root.FindName("CompactBar") is not UIElement compactBar) return;
        if (root.FindName("CompactScale") is not ScaleTransform compactScale) return;

        bool collapsed = false;
        double headerHeight = 0; // the tallest the header rows have been: what collapsing gives back to the list
        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, args) =>
        {
            if (root is Grid grid)
                headerHeight = Math.Max(headerHeight, grid.RowDefinitions.Take(3).Sum(r => r.ActualHeight));

            bool shouldCollapse = HeaderCollapse.ShouldCollapse(collapsed, args.VerticalOffset, args.ExtentHeight, args.ViewportHeight, headerHeight);
            if (shouldCollapse == collapsed) return;
            collapsed = shouldCollapse;

            var duration = TimeSpan.FromMilliseconds(300);
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            SetVisible(headerBand, headerScale, !collapsed, duration, ease);
            SetVisible(buttonsRow, buttonsScale, !collapsed, duration, ease);
            SetVisible(compactBar, compactScale, collapsed, duration, ease);
        }));
    }

    static void SetVisible(UIElement element, ScaleTransform scale, bool visible, Duration duration, IEasingFunction ease)
    {
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(visible ? 1 : 0, duration) { EasingFunction = ease });
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(visible ? 1 : 0, duration));
    }
}
