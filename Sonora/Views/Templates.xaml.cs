using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sonora.Models;
using Sonora.ViewModels;

namespace Sonora.Views;

/// <summary>Event handlers for the shared templates (they work in whichever panel the list is docked).</summary>
public partial class Templates : ResourceDictionary
{
    public Templates() => InitializeComponent();

    void TrackItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: TrackRow row } item) return;
        TrackInteractions.PlayRow(ItemsControl.ItemsControlFromItemContainer(item), row);
        e.Handled = true;
    }

    /// <summary>The play button over a cover: starts that track, or pauses / resumes it when it is the current one.</summary>
    void CoverPlay_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: TrackRow row } button) return;
        var player = ((MainViewModel)Application.Current.MainWindow.DataContext).Player;
        if (row.Track.IsCurrent) { player.Toggle(); return; }
        for (DependencyObject? d = button; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is ListBox list) { TrackInteractions.PlayRow(list, row); return; }
    }

    void TrackItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: TrackRow row } item) return;
        TrackInteractions.BuildTrackMenu(item, row).IsOpen = true;
        e.Handled = true;
    }

    void TrackItem_DragArm(object sender, MouseButtonEventArgs e) => TrackDragDrop.ArmDrag(sender, e);
    void TrackItem_DragCancel(object sender, MouseButtonEventArgs e) => TrackDragDrop.CancelDrag(sender, e);
    void TrackItem_DragMove(object sender, MouseEventArgs e) => TrackDragDrop.MaybeStartDrag(sender, e);
    void TrackItem_DragOver(object sender, DragEventArgs e) => TrackDragDrop.RowDragOver(sender, e);
    void TrackItem_Drop(object sender, DragEventArgs e) => TrackDragDrop.RowDrop(sender, e);
    void LibraryItem_DragOver(object sender, DragEventArgs e) => TrackDragDrop.LibraryDragOver(sender, e);
    void LibraryItem_DragLeave(object sender, DragEventArgs e) => TrackDragDrop.LibraryDragLeave(sender, e);
    void LibraryItem_Drop(object sender, DragEventArgs e) => TrackDragDrop.LibraryDrop(sender, e);

    void LibraryItem_DragArm(object sender, MouseButtonEventArgs e) => TrackDragDrop.ArmPinDrag(sender, e);
    void LibraryItem_DragMove(object sender, MouseEventArgs e) => TrackDragDrop.MaybeStartPinDrag(sender, e);

    void LibraryItem_Click(object sender, MouseButtonEventArgs e)
    {
        TrackDragDrop.CancelPinDrag(sender, e);
        if (sender is ListBoxItem { DataContext: LibraryEntry entry }) TrackInteractions.OpenLibraryEntry(entry);
    }

    void LibraryItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: LibraryEntry entry } item) return;
        var menu = TrackInteractions.BuildLibraryEntryMenu(entry, Window.GetWindow(item));
        menu.PlacementTarget = item;
        menu.IsOpen = true;
        e.Handled = true;
    }
}

/// <summary>Library list that switches to covers only when its panel is narrow.</summary>
public sealed class LibraryList : ListBox
{
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(LibraryList));

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public static readonly DependencyProperty ForceCompactProperty =
        DependencyProperty.Register(nameof(ForceCompact), typeof(bool), typeof(LibraryList),
            new PropertyMetadata(false, (d, _) => ((LibraryList)d).UpdateCompact()));

    /// <summary>Covers only, asked for in the settings rather than forced by a narrow panel.</summary>
    public bool ForceCompact
    {
        get => (bool)GetValue(ForceCompactProperty);
        set => SetValue(ForceCompactProperty, value);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        UpdateCompact();
    }

    void UpdateCompact() => IsCompact = ForceCompact || ActualWidth < 240;
}
