using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Sonora.Models;
using Sonora.ViewModels;
using Sonora.Appearance.Localization;

namespace Sonora.Views;

/// <summary>Behaviour shared by every track list and library list, wherever their panel is docked.</summary>
public static class TrackInteractions
{
    static MainViewModel Vm => (MainViewModel)Application.Current.MainWindow.DataContext;

    public static void PlayRow(ItemsControl? list, TrackRow row)
    {
        if (list?.DataContext is MainViewModel) // the queue panel
        {
            Vm.Player.JumpTo(row);
            return;
        }
        if (row.Track.IsMissing)
        {
            Vm.LocateLocalEntry(row.Track); // nothing to play yet: ask for the file instead
            return;
        }
        var rows = (list?.ItemsSource as IEnumerable<TrackRow>)?.ToList() ?? [row];
        var index = rows.FindIndex(r => ReferenceEquals(r, row));
        var page = list?.DataContext as PageViewModel;
        var contextUri = (page as TracksPage)?.PlaybackContextUri;
        Vm.PlayTracks(rows.Select(r => r.Track).ToList(), Math.Max(0, index), page?.Title ?? "", contextUri: contextUri);
    }

    public static ContextMenu BuildTrackMenu(ListBoxItem item, TrackRow row)
    {
        var vm = Vm;
        var list = ItemsControl.ItemsControlFromItemContainer(item) as ListBox;
        if (!item.IsSelected)
        {
            list?.SelectedItems.Clear();
            item.IsSelected = true;
        }
        var selected = list?.SelectedItems.OfType<TrackRow>().ToList() is { Count: > 0 } s ? s : [row];
        var tracks = selected.Select(r => r.Track).ToList();
        var t = row.Track;
        var many = tracks.Count > 1;
        var spotify = t.IsSpotify;

        var menu = new ContextMenu();
        if (t.IsMissing)
        {
            // A placeholder for a file that is not here: the only useful thing to do with it is to point to the file.
            Add(menu, Loc.T("menu.locate_file"), "", () => vm.LocateLocalEntry(t));
            menu.PlacementTarget = item;
            return menu;
        }
        Add(menu, Loc.T("menu.play"), "", () => PlayRow(list, row));
        if (!spotify) Add(menu, Loc.T("menu.play_next"), "", () => { foreach (var x in Enumerable.Reverse(tracks)) vm.Player.PlayNext(x); });
        Add(menu, Loc.T("menu.add_to_queue"), "", () => { foreach (var x in tracks) vm.Player.Enqueue(x); });
        menu.Items.Add(new Separator());

        if (!many)
            Add(menu, t.IsLiked ? Loc.T("menu.remove_from_liked") : Loc.T("common.add_to_liked_songs"), t.IsLiked ? "" : "", () => vm.ToggleLike(t));
        else
            Add(menu, Loc.T("common.add_to_liked_songs"), "", () => { foreach (var x in tracks.Where(x => !x.IsLiked)) vm.ToggleLike(x); });

        var sub = new MenuItem { Header = Loc.T("menu.add_to_playlist"), Icon = Glyph("") };
        if (spotify)
        {
            Add(sub, Loc.T("menu.new_spotify_playlist"), "", () => _ = vm.AddToSpotifyPlaylistAsync(null, tracks));
            var owned = vm.Spotify.OwnedPlaylists;
            if (owned.Count > 0) sub.Items.Add(new Separator());
            foreach (var p in owned)
            {
                var target = p;
                Add(sub, p.Name, "", () => _ = vm.AddToSpotifyPlaylistAsync(target, tracks));
            }
        }
        else
        {
            Add(sub, Loc.T("menu.new_playlist"), "", () => vm.AddToPlaylist(vm.CreatePlaylist(), tracks));
            if (vm.Playlists.Count > 0) sub.Items.Add(new Separator());
            foreach (var p in vm.Playlists)
            {
                var target = p;
                Add(sub, p.Name, "", () => vm.AddToPlaylist(target, tracks));
            }
        }
        menu.Items.Add(sub);

        if (list?.DataContext is TracksPage { Playlist: { } pl })
            Add(menu, Loc.T("menu.remove_from_playlist"), "", () =>
            {
                foreach (var r in selected.OrderByDescending(r => r.Number)) vm.RemoveFromPlaylist(pl, r.Number - 1);
            });

        menu.Items.Add(new Separator());
        if (!many)
        {
            if (t.AlbumRef is { } album) Add(menu, Loc.T("menu.go_to_album"), "", () => vm.OpenAlbum(album));
            Add(menu, Loc.T("menu.go_to_artist"), "", () => vm.OpenTrackArtist(t));
            if (spotify)
                Add(menu, Loc.T("menu.copy_track_link"), "", () => Clipboard.SetText($"https://open.spotify.com/track/{t.SpotifyId}"));
            else
            {
                Add(menu, Loc.T("menu.show_in_explorer"), "", () =>
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{t.Path}\"") { UseShellExecute = true }));
                AddLocalFileActions(menu, t, Window.GetWindow(item) ?? Application.Current.MainWindow);
            }
        }

        menu.PlacementTarget = item;
        return menu;
    }

    /// <summary>What only a local file can have: its own cover, and lyrics picked by hand on LRCLIB.</summary>
    public static void AddLocalFileActions(ItemsControl menu, Models.Track t, Window owner)
    {
        var vm = Vm;
        menu.Items.Add(new Separator());
        Add(menu, Loc.T("menu.change_cover"), "", () => vm.ChooseTrackCover(t));
        if (t.CustomArt != null) Add(menu, Loc.T("menu.remove_track_cover"), "", () => vm.ClearTrackCover(t));
        if (vm.State.Features.Lyrics) Add(menu, Loc.T("menu.choose_lyrics"), "", () => LyricsPicker.Show(owner, vm, t));
    }

    public static void OpenLibraryEntry(LibraryEntry e) => Vm.OpenEntry(e);

    /// <summary>Order of the library list. Pinned entries stay on top, in the order they were dragged into, whichever order is chosen.</summary>
    public static ContextMenu BuildLibrarySortMenu()
    {
        var vm = Vm;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = Loc.T("menu.order"), IsEnabled = false });
        var recent = new MenuItem { Header = Loc.T("menu.recent_first"), IsChecked = vm.LibrarySort == "recent" };
        recent.Click += (_, _) => vm.LibrarySort = "recent";
        menu.Items.Add(recent);
        var alpha = new MenuItem { Header = Loc.T("menu.alphabetical"), IsChecked = vm.LibrarySort == "alpha" };
        alpha.Click += (_, _) => vm.LibrarySort = "alpha";
        menu.Items.Add(alpha);

        menu.Items.Add(new Separator());
        var compact = new MenuItem { Header = Loc.T("menu.covers_only"), IsChecked = vm.LibraryCompact };
        compact.Click += (_, _) => vm.LibraryCompact = !vm.LibraryCompact;
        menu.Items.Add(compact);
        return menu;
    }

    /// <summary>Right-click menu of a library entry: pin, plus rename/delete for a local playlist.</summary>
    public static ContextMenu BuildLibraryEntryMenu(LibraryEntry entry, Window owner)
    {
        var vm = Vm;
        var menu = new ContextMenu();
        Add(menu, entry.IsPinned ? Loc.T("menu.unpin") : Loc.T("menu.pin"), "", () => vm.TogglePin(entry));
        if (ShareLink(entry.Source) is { } link)
            Add(menu, Loc.T("menu.copy_link"), "", () =>
            {
                Clipboard.SetText(link);
                vm.ReportError(Loc.T("menu.link_copied"));
            });
        if (entry.Source is Playlist p)
        {
            menu.Items.Add(new Separator());
            AddPlaylistActions(menu, p, owner);
        }
        return menu;
    }

    /// <summary>"More options" menu of a page header: follow/save, share, and a local playlist's own actions.</summary>
    public static ContextMenu BuildPageMenu(object? subject, Window owner,
        IReadOnlyList<Models.Track>? tracks = null, string title = "playlist")
    {
        var vm = Vm;
        var menu = new ContextMenu();

        if (subject is Album or Artist or SpotifyPlaylist && ShareLink(subject) != null)
        {
            var saved = vm.IsSaved(subject);
            var label = subject switch
            {
                Album { IsShow: true } => saved ? Loc.T("menu.remove_podcast") : Loc.T("menu.save_podcast"),
                Album => saved ? Loc.T("menu.remove_album") : Loc.T("menu.save_album"),
                Artist => saved ? Loc.T("menu.unfollow") : Loc.T("menu.follow_artist"),
                _ => saved ? Loc.T("menu.unfollow_playlist") : Loc.T("menu.follow_playlist"),
            };
            var target = subject;
            Add(menu, label, saved ? "" : "", () => _ = vm.SetSavedAsync(target, !saved));
        }

        if (ShareLink(subject) is { } link)
            Add(menu, Loc.T("menu.copy_link"), "", () =>
            {
                Clipboard.SetText(link);
                vm.ReportError(Loc.T("menu.link_copied"));
            });

        if (subject is Artist similar && vm.State.Features.Discovery)
            Add(menu, Loc.T("menu.similar_artists"), "", () => vm.OpenSimilarArtists(similar));

        if (subject is Playlist p) AddPlaylistActions(menu, p, owner);
        if (subject is SpotifyPlaylist own && own.OwnerId == vm.Spotify.UserId) AddOwnedPlaylistActions(menu, own, owner);

        if (tracks is { Count: > 0 })
            Add(menu, Loc.T("menu.export_playlist"), "", () => vm.ExportPlaylist(tracks, title));

        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("menu.no_action"), IsEnabled = false });
        return menu;
    }

    /// <summary>open.spotify.com link for anything that has a Spotify URI; null for local-only entries.</summary>
    public static string? ShareLink(object? source)
    {
        var uri = source switch
        {
            SpotifyPlaylist p => p.Uri,
            Album a => a.Uri,
            Artist a => a.Uri,
            Models.Track t => t.Uri, // Models.Track, not the slider part of the same name
            _ => null,
        };
        if (uri == null) return null;
        var parts = uri.Split(':');
        return parts.Length >= 3 ? $"https://open.spotify.com/{parts[^2]}/{parts[^1]}" : null;
    }

    /// <summary>Sort choices for a track list, the current one ticked, plus the ascending/descending pair.</summary>
    public static ContextMenu BuildSortMenu(TracksPage page)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = Loc.T("menu.sort_by"), IsEnabled = false });
        foreach (var option in page.SortOptions)
        {
            var chosen = option;
            var mi = new MenuItem { Header = option.Label, IsChecked = option.Key == page.SelectedSort.Key };
            mi.Click += (_, _) => page.ChooseSort(chosen);
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        var up = new MenuItem { Header = Loc.T("menu.ascending"), IsChecked = !page.SortDescending, IsEnabled = page.IsSorted };
        up.Click += (_, _) => page.SortDescending = false;
        menu.Items.Add(up);
        var down = new MenuItem { Header = Loc.T("menu.descending"), IsChecked = page.SortDescending, IsEnabled = page.IsSorted };
        down.Click += (_, _) => page.SortDescending = true;
        menu.Items.Add(down);
        return menu;
    }

    public static ContextMenu BuildPlaylistMenu(Playlist p, Window owner)
    {
        var menu = new ContextMenu();
        AddPlaylistActions(menu, p, owner);
        return menu;
    }

    /// <summary>Name, description, visibility and collaboration for a Spotify playlist you own.</summary>
    static void AddOwnedPlaylistActions(ItemsControl menu, SpotifyPlaylist p, Window owner)
    {
        var vm = Vm;
        menu.Items.Add(new Separator());
        Add(menu, Loc.T("appearance.rename"), "", () =>
        {
            var name = Prompt.Show(owner, Loc.T("menu.rename_playlist"), p.Name);
            if (name is { Length: > 0 }) _ = vm.SetPlaylistDetailsAsync(p, name: name);
        });
        Add(menu, Loc.T("menu.edit_description"), "", () =>
        {
            var text = Prompt.Show(owner, Loc.T("menu.playlist_description"), "");
            if (text != null) _ = vm.SetPlaylistDetailsAsync(p, description: text);
        });
        Add(menu, Loc.T("menu.change_cover"), "", () => _ = vm.SetPlaylistCoverAsync(p));
        Add(menu, p.IsPublic ? Loc.T("menu.make_private") : Loc.T("menu.make_public"), "",
            () => _ = vm.SetPlaylistDetailsAsync(p, isPublic: !p.IsPublic));
        Add(menu, p.IsCollaborative ? Loc.T("menu.stop_collaborating") : Loc.T("menu.make_collaborative"), "",
            () => _ = vm.SetPlaylistDetailsAsync(p, collaborative: !p.IsCollaborative));
    }

    /// <summary>Rename and delete for a local playlist, shared by every menu that offers them.</summary>
    static void AddPlaylistActions(ItemsControl menu, Playlist p, Window owner)
    {
        var vm = Vm;
        Add(menu, Loc.T("appearance.rename"), "", () =>
        {
            var name = Prompt.Show(owner, Loc.T("menu.rename_playlist"), p.Name);
            if (name != null) vm.RenamePlaylist(p, name);
        });
        Add(menu, Loc.T("appearance.delete"), "", () =>
        {
            var r = MessageBox.Show(owner, Loc.T("menu.delete_playlist_confirm", p.Name), Loc.T("menu.delete_playlist"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) vm.DeletePlaylist(p);
        });
    }

    public static async void ShowDevices(UIElement anchor)
    {
        var vm = Vm;
        List<SpotifyDevice> devices;
        try { devices = await vm.GetDevicesAsync(); }
        catch (Exception ex) { vm.ReportError(ex.Message); return; }

        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Top };
        menu.Items.Add(new MenuItem { Header = Loc.T("menu.play_on"), IsEnabled = false });
        if (devices.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("menu.no_device"), IsEnabled = false });
        foreach (var d in devices)
        {
            var device = d;
            var glyph = d.Type switch { "Computer" => "", "Smartphone" => "", "Speaker" => "", "TV" => "", _ => "" };
            var mi = new MenuItem { Header = d.Name, Icon = Glyph(glyph), IsChecked = d.IsActive };
            mi.Click += async (_, _) => await vm.TransferToAsync(device);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    /// <summary>Sleep timer: a delay, the end of the current track, or off.</summary>
    public static void ShowSleepTimer(UIElement anchor)
    {
        var vm = Vm;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Top };
        menu.Items.Add(new MenuItem { Header = Loc.T("menu.sleep_timer"), IsEnabled = false });
        foreach (var minutes in new[] { 15, 30, 45, 60 })
        {
            var delay = minutes;
            Add(menu, Loc.T("menu.sleep_minutes", minutes), "", () => vm.StartSleep(delay));
        }
        Add(menu, Loc.T("menu.sleep_end_of_track"), "", vm.StartSleepAtTrackEnd);
        if (vm.IsSleepActive)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = vm.SleepStatus, IsEnabled = false });
            Add(menu, Loc.T("common.cancel"), "", vm.CancelSleep);
        }
        menu.IsOpen = true;
    }

    public static TextBlock Glyph(string g) => new()
    {
        Text = g,
        FontFamily = (FontFamily)Application.Current.Resources["Icons"],
        FontSize = 14,
    };

    public static void Add(ItemsControl parent, string header, string glyph, Action action)
    {
        var mi = new MenuItem { Header = header, Icon = Glyph(glyph) };
        mi.Click += (_, _) => action();
        parent.Items.Add(mi);
    }
}
