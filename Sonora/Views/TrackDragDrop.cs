using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Sonora.Appearance.Features;
using Sonora.Models;
using Sonora.ViewModels;
using Sonora.Appearance.Localization;

namespace Sonora.Views;

/// <summary>What a track drag carries: the rows picked up and the page they came from.</summary>
public sealed record TrackDrag(List<TrackRow> Rows, PageViewModel? From)
{
    public const string Format = "Sonora.TrackRows";
}

/// <summary>What a library entry shows while tracks are dragged over it (see the <c>LibraryItem</c> style).</summary>
public enum DropState { None, Accepts, Refuses, Landed }

/// <summary>
/// Dragging tracks: onto a playlist in the library panel to add them, or onto another row of the same
/// playlist to move them. Shared by every track list, wherever its panel is docked.
/// </summary>
public static class TrackDragDrop
{
    /// <summary>How far the pointer must travel before a press turns into a drag rather than a click.</summary>
    static readonly double Threshold = SystemParameters.MinimumHorizontalDragDistance;

    public static readonly DependencyProperty DropStateProperty =
        DependencyProperty.RegisterAttached("DropState", typeof(DropState), typeof(TrackDragDrop), new PropertyMetadata(DropState.None));

    public static DropState GetDropState(DependencyObject d) => (DropState)d.GetValue(DropStateProperty);
    public static void SetDropState(DependencyObject d, DropState v) => d.SetValue(DropStateProperty, v);

    static Point _origin;
    static bool _armed;
    static TrackDragGhost? _ghost;
    static ListBoxItem? _hover;

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

        // DoDragDrop returns when the drag ends, so the card lives exactly as long as the drag.
        GiveFeedbackEventHandler follow = (_, _) => _ghost?.Follow();
        _ghost = Application.Current.MainWindow.Content is UIElement root ? TrackDragGhost.Show(root, row.Track.Title, rows.Count) : null;
        item.GiveFeedback += follow;
        try { DragDrop.DoDragDrop(item, new DataObject(TrackDrag.Format, payload), DragDropEffects.Move | DragDropEffects.Copy); }
        finally
        {
            item.GiveFeedback -= follow;
            _ghost?.Close();
            _ghost = null;
            Leave();
        }
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
        e.Handled = true;
        if (PinPayload(e) != null)
        {
            e.Effects = PinTarget(sender) != null ? DragDropEffects.Move : DragDropEffects.None;
            return;
        }
        if (Payload(e) is not { } drag || sender is not ListBoxItem item)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var (target, _, name) = Target(sender);
        var verdict = Judge(drag, target);
        e.Effects = verdict.IsAccepted ? DragDropEffects.Copy : DragDropEffects.None;

        // Anything that is not a playlist stays quiet: it was never a place to drop on.
        if (target == DropTarget.Other) return;
        if (_hover != item) { Leave(); _hover = item; }
        SetDropState(item, verdict.IsAccepted ? DropState.Accepts : DropState.Refuses);
        _ghost?.SetHint(Hint(verdict, name), !verdict.IsAccepted);
    }

    public static void LibraryDragLeave(object sender, DragEventArgs e)
    {
        if (sender == _hover) Leave();
    }

    public static void LibraryDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (PinPayload(e) is { } pinned)
        {
            if (PinTarget(sender) is { } over) Vm.MovePin(pinned, over);
            return;
        }
        if (Payload(e) is not { } drag || sender is not ListBoxItem item) return;
        var (kind, source, _) = Target(sender);
        if (!Judge(drag, kind).IsAccepted) return;

        var list = ItemsControl.ItemsControlFromItemContainer(item); // before the tracks are added: that redraws the list and detaches the row
        var tracks = drag.Rows.Select(r => r.Track).ToList();
        switch (source)
        {
            case Playlist local:
                Vm.AddToPlaylist(local, tracks);
                Vm.ReportError(Loc.T("playlist.added_to", local.Name));
                break;
            case SpotifyPlaylist remote:
                _ = Vm.AddToSpotifyPlaylistAsync(remote, tracks);
                break;
        }
        Land(item, list, source);
    }

    /// <summary>The playlist under the pointer: what it is, the playlist itself, and its name.</summary>
    static (DropTarget Kind, object? Source, string Name) Target(object sender) => sender switch
    {
        ListBoxItem { DataContext: LibraryEntry { Source: Playlist p } } => (DropTarget.LocalPlaylist, p, p.Name),
        ListBoxItem { DataContext: LibraryEntry { Source: SpotifyPlaylist sp } } =>
            (sp.OwnerId == Vm.Spotify.UserId ? DropTarget.OwnSpotifyPlaylist : DropTarget.ForeignSpotifyPlaylist, sp, sp.Name),
        _ => (DropTarget.Other, null, ""),
    };

    static DropVerdict Judge(TrackDrag drag, DropTarget target)
    {
        int spotify = drag.Rows.Count(r => r.Track.IsSpotify);
        return DropRule.Judge(target, drag.Rows.Count - spotify, spotify);
    }

    /// <summary>What the card says over a playlist: what the drop will do, or why it will not.</summary>
    static string Hint(DropVerdict v, string playlist) => v switch
    {
        { IsPartial: true } => Loc.T("drag.add_some_to", v.Accepted, v.Total, playlist),
        { IsAccepted: true } => Loc.T("drag.add_to", playlist),
        { Refusal: DropRefusal.SpotifyIntoLocal } => Loc.T("drag.refuse_spotify_in_local"),
        { Refusal: DropRefusal.LocalIntoSpotify } => Loc.T("drag.refuse_local_in_spotify"),
        _ => Loc.T("drag.refuse_not_yours"),
    };

    static void Leave()
    {
        if (_hover is { } item) SetDropState(item, DropState.None);
        _hover = null;
        _ghost?.SetHint(null, false);
    }

    /// <summary>
    /// A short pulse on the playlist that took the tracks, then back to normal. Adding tracks redraws the library list, so
    /// the row that was dropped on may be replaced: the pulse goes to the row of the same playlist, wherever it is by then.
    /// </summary>
    static void Land(ListBoxItem item, ItemsControl? list, object? playlist)
    {
        if (_hover == item) _hover = null;
        if (list == null) return;

        void Set(DropState state)
        {
            SetDropState(item, state);
            if (RowOf(list, playlist) is { } row) SetDropState(row, state);
        }
        Set(DropState.Landed);
        item.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Set(DropState.Landed)); // after the redraw
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) => { timer.Stop(); Set(DropState.None); };
        timer.Start();
    }

    static ListBoxItem? RowOf(ItemsControl list, object? playlist) =>
        list.Items.OfType<LibraryEntry>().FirstOrDefault(e => ReferenceEquals(e.Source, playlist)) is { } entry
            ? list.ItemContainerGenerator.ContainerFromItem(entry) as ListBoxItem
            : null;

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
