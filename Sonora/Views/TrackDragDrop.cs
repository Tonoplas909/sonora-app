using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sonora.Models;
using Sonora.ViewModels;
using Sonora.Appearance.Localization;

namespace Sonora.Views;

/// <summary>What a track drag carries: the rows picked up and the page they came from.</summary>
public sealed record TrackDrag(List<TrackRow> Rows, PageViewModel? From)
{
    public const string Format = "Sonora.TrackRows";
}

/// <summary>
/// Dragging tracks: onto a playlist in the library panel to add them, or onto another row of the same
/// playlist to move them. Shared by every track list, wherever its panel is docked.
/// </summary>
public static class TrackDragDrop
{
    /// <summary>How far the pointer must travel before a press turns into a drag rather than a click.</summary>
    static readonly double Threshold = SystemParameters.MinimumHorizontalDragDistance;

    static Point _origin;
    static bool _armed;

    static MainViewModel Vm => (MainViewModel)Application.Current.MainWindow.DataContext;

    public static void ArmDrag(object sender, MouseButtonEventArgs e)
    {
        _armed = sender is ListBoxItem { DataContext: TrackRow };
        _origin = e.GetPosition(null);
    }

    public static void MaybeStartDrag(object sender, MouseEventArgs e)
    {
        if (!_armed || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ListBoxItem { DataContext: TrackRow row } item) return;

        var moved = e.GetPosition(null) - _origin;
        if (Math.Abs(moved.X) < Threshold && Math.Abs(moved.Y) < Threshold) return;
        _armed = false;

        var list = ItemsControl.ItemsControlFromItemContainer(item) as ListBox;
        var rows = list?.SelectedItems.OfType<TrackRow>().ToList() is { Count: > 0 } picked && picked.Contains(row)
            ? picked
            : [row];
        var payload = new TrackDrag(rows, list?.DataContext as PageViewModel);
        DragDrop.DoDragDrop(item, new DataObject(TrackDrag.Format, payload), DragDropEffects.Move | DragDropEffects.Copy);
    }

    public static void CancelDrag(object sender, MouseButtonEventArgs e) => _armed = false;

    // ---------------- drop on another row: reorder inside a playlist ----------------

    public static void RowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Payload(e) != null && TargetPlaylist(sender, e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    public static void RowDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Payload(e) is not { } drag || sender is not ListBoxItem { DataContext: TrackRow target } item) return;
        if (!TargetPlaylist(sender, e)) return;
        if (drag.Rows.Count != 1) return; // one at a time: Spotify moves a contiguous range only

        var from = drag.Rows[0].Number - 1;
        var to = target.Number - 1;
        if (from == to) return;

        switch ((item.DataContext, ItemsControl.ItemsControlFromItemContainer(item) as ListBox))
        {
            case (_, { DataContext: TracksPage { Playlist: { } local } }):
                Vm.ReorderPlaylist(local, from, to);
                break;
            case (_, { DataContext: TracksPage { SpotifyPlaylist: { } remote } }):
                _ = Vm.ReorderSpotifyPlaylistAsync(remote, from, to);
                break;
        }
    }

    /// <summary>True when the drop target is a playlist page the user can edit, and the drag came from it.</summary>
    static bool TargetPlaylist(object sender, DragEventArgs e)
    {
        if (sender is not ListBoxItem item) return false;
        if (ItemsControl.ItemsControlFromItemContainer(item) is not ListBox { DataContext: TracksPage page }) return false;
        if (Payload(e)?.From != page) return false; // reordering only within the same list
        return page.Playlist != null
               || (page.SpotifyPlaylist is { } sp && sp.OwnerId == Vm.Spotify.UserId);
    }

    // ---------------- drop on a library entry: add to that playlist ----------------

    public static void LibraryDragOver(object sender, DragEventArgs e)
    {
        e.Effects = PinPayload(e) != null
            ? (PinTarget(sender) != null ? DragDropEffects.Move : DragDropEffects.None)
            : Payload(e) != null && LibraryTarget(sender) != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    public static void LibraryDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (PinPayload(e) is { } pinned)
        {
            if (PinTarget(sender) is { } over) Vm.MovePin(pinned, over);
            return;
        }
        if (Payload(e) is not { } drag || LibraryTarget(sender) is not { } target) return;
        var tracks = drag.Rows.Select(r => r.Track).ToList();
        switch (target)
        {
            case Playlist local:
                Vm.AddToPlaylist(local, tracks);
                Vm.ReportError(Loc.T("playlist.added_to", local.Name));
                break;
            case SpotifyPlaylist remote:
                _ = Vm.AddToSpotifyPlaylistAsync(remote, tracks);
                break;
        }
    }

    /// <summary>The playlist under the pointer, if it is one the user can add to.</summary>
    static object? LibraryTarget(object sender) => sender switch
    {
        ListBoxItem { DataContext: LibraryEntry { Source: Playlist p } } => p,
        ListBoxItem { DataContext: LibraryEntry { Source: SpotifyPlaylist sp } } when sp.OwnerId == Vm.Spotify.UserId => sp,
        _ => null,
    };

    // ---------------- pinned library entries: reorder them by hand ----------------

    const string PinFormat = "Sonora.PinnedEntry";
    static Point _pinOrigin;
    static bool _pinArmed;

    public static void ArmPinDrag(object sender, MouseButtonEventArgs e)
    {
        _pinArmed = sender is ListBoxItem { DataContext: LibraryEntry { IsPinned: true } };
        _pinOrigin = e.GetPosition(null);
    }

    public static void CancelPinDrag(object sender, MouseButtonEventArgs e) => _pinArmed = false;

    public static void MaybeStartPinDrag(object sender, MouseEventArgs e)
    {
        if (!_pinArmed || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ListBoxItem { DataContext: LibraryEntry { IsPinned: true } entry } item) return;

        var moved = e.GetPosition(null) - _pinOrigin;
        if (Math.Abs(moved.X) < Threshold && Math.Abs(moved.Y) < Threshold) return;
        _pinArmed = false;
        DragDrop.DoDragDrop(item, new DataObject(PinFormat, entry), DragDropEffects.Move);
    }

    /// <summary>A pinned entry can only land on another pinned entry: the pins stay above everything else.</summary>
    static LibraryEntry? PinTarget(object sender) =>
        sender is ListBoxItem { DataContext: LibraryEntry { IsPinned: true } entry } ? entry : null;

    static LibraryEntry? PinPayload(DragEventArgs e) =>
        e.Data.GetDataPresent(PinFormat) ? e.Data.GetData(PinFormat) as LibraryEntry : null;

    static TrackDrag? Payload(DragEventArgs e) =>
        e.Data.GetDataPresent(TrackDrag.Format) ? e.Data.GetData(TrackDrag.Format) as TrackDrag : null;
}
