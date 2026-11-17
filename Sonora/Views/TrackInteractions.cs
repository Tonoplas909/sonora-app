using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Sonora.Models;
using Sonora.ViewModels;

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
        Add(menu, "Lire", "", () => PlayRow(list, row));
        if (!spotify) Add(menu, "Lire ensuite", "", () => { foreach (var x in Enumerable.Reverse(tracks)) vm.Player.PlayNext(x); });
        Add(menu, "Ajouter à la file d'attente", "", () => { foreach (var x in tracks) vm.Player.Enqueue(x); });
        menu.Items.Add(new Separator());

        if (!many)
            Add(menu, t.IsLiked ? "Retirer des Titres likés" : "Ajouter aux Titres likés", t.IsLiked ? "" : "", () => vm.ToggleLike(t));
        else
            Add(menu, "Ajouter aux Titres likés", "", () => { foreach (var x in tracks.Where(x => !x.IsLiked)) vm.ToggleLike(x); });

        var sub = new MenuItem { Header = "Ajouter à la playlist", Icon = Glyph("") };
        if (spotify)
        {
            Add(sub, "Nouvelle playlist Spotify", "", () => _ = vm.AddToSpotifyPlaylistAsync(null, tracks));
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
            Add(sub, "Nouvelle playlist", "", () => vm.AddToPlaylist(vm.CreatePlaylist(), tracks));
            if (vm.Playlists.Count > 0) sub.Items.Add(new Separator());
            foreach (var p in vm.Playlists)
            {
                var target = p;
                Add(sub, p.Name, "", () => vm.AddToPlaylist(target, tracks));
            }
        }
        menu.Items.Add(sub);

        if (list?.DataContext is TracksPage { Playlist: { } pl })
            Add(menu, "Retirer de cette playlist", "", () =>
            {
                foreach (var r in selected.OrderByDescending(r => r.Number)) vm.RemoveFromPlaylist(pl, r.Number - 1);
            });

        menu.Items.Add(new Separator());
        if (!many)
        {
            if (t.AlbumRef is { } album) Add(menu, "Accéder à l'album", "", () => vm.OpenAlbum(album));
            Add(menu, "Accéder à l'artiste", "", () => vm.OpenTrackArtist(t));
            if (spotify)
                Add(menu, "Copier le lien du titre", "", () => Clipboard.SetText($"https://open.spotify.com/track/{t.SpotifyId}"));
            else
                Add(menu, "Afficher dans l'Explorateur", "", () =>
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{t.Path}\"") { UseShellExecute = true }));
        }

        menu.PlacementTarget = item;
        return menu;
    }

    public static void OpenLibraryEntry(LibraryEntry e) => Vm.OpenEntry(e);

    /// <summary>Order of the library list. Pinned entries stay on top, in the order they were dragged into, whichever order is chosen.</summary>
    public static ContextMenu BuildLibrarySortMenu()
    {
        var vm = Vm;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Ordre", IsEnabled = false });
        var recent = new MenuItem { Header = "Récents d'abord", IsChecked = vm.LibrarySort == "recent" };
        recent.Click += (_, _) => vm.LibrarySort = "recent";
        menu.Items.Add(recent);
        var alpha = new MenuItem { Header = "Ordre alphabétique", IsChecked = vm.LibrarySort == "alpha" };
        alpha.Click += (_, _) => vm.LibrarySort = "alpha";
        menu.Items.Add(alpha);

        menu.Items.Add(new Separator());
        var compact = new MenuItem { Header = "Pochettes seules", IsChecked = vm.LibraryCompact };
        compact.Click += (_, _) => vm.LibraryCompact = !vm.LibraryCompact;
        menu.Items.Add(compact);
        return menu;
    }

    /// <summary>Right-click menu of a library entry: pin, plus rename/delete for a local playlist.</summary>
    public static ContextMenu BuildLibraryEntryMenu(LibraryEntry entry, Window owner)
    {
        var vm = Vm;
        var menu = new ContextMenu();
        Add(menu, entry.IsPinned ? "Détacher" : "Épingler", "", () => vm.TogglePin(entry));
        if (ShareLink(entry.Source) is { } link)
            Add(menu, "Copier le lien", "", () =>
            {
                Clipboard.SetText(link);
                vm.ReportError("Lien copié dans le presse-papiers.");
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
                Album { IsShow: true } => saved ? "Retirer ce podcast" : "Enregistrer ce podcast",
                Album => saved ? "Retirer cet album" : "Enregistrer cet album",
                Artist => saved ? "Ne plus suivre" : "Suivre cet artiste",
                _ => saved ? "Ne plus suivre cette playlist" : "Suivre cette playlist",
            };
            var target = subject;
            Add(menu, label, saved ? "" : "", () => _ = vm.SetSavedAsync(target, !saved));
        }

        if (ShareLink(subject) is { } link)
            Add(menu, "Copier le lien", "", () =>
            {
                Clipboard.SetText(link);
                vm.ReportError("Lien copié dans le presse-papiers.");
            });

        if (subject is Artist similar && vm.State.Features.Discovery)
            Add(menu, "Artistes similaires", "", () => vm.OpenSimilarArtists(similar));

        if (subject is Playlist p) AddPlaylistActions(menu, p, owner);
        if (subject is SpotifyPlaylist own && own.OwnerId == vm.Spotify.UserId) AddOwnedPlaylistActions(menu, own, owner);

        if (tracks is { Count: > 0 } && tracks.Any(t => !t.IsSpotify))
            Add(menu, "Exporter en M3U", "", () => vm.ExportPlaylist(tracks, title));

        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Aucune action disponible", IsEnabled = false });
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
        menu.Items.Add(new MenuItem { Header = "Trier par", IsEnabled = false });
        foreach (var option in page.SortOptions)
        {
            var chosen = option;
            var mi = new MenuItem { Header = option.Label, IsChecked = option.Key == page.SelectedSort.Key };
            mi.Click += (_, _) => page.ChooseSort(chosen);
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        var up = new MenuItem { Header = "Croissant", IsChecked = !page.SortDescending, IsEnabled = page.IsSorted };
        up.Click += (_, _) => page.SortDescending = false;
        menu.Items.Add(up);
        var down = new MenuItem { Header = "Décroissant", IsChecked = page.SortDescending, IsEnabled = page.IsSorted };
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
        Add(menu, "Renommer", "", () =>
        {
            var name = Prompt.Show(owner, "Renommer la playlist", p.Name);
            if (name is { Length: > 0 }) _ = vm.SetPlaylistDetailsAsync(p, name: name);
        });
        Add(menu, "Modifier la description", "", () =>
        {
            var text = Prompt.Show(owner, "Description de la playlist", "");
            if (text != null) _ = vm.SetPlaylistDetailsAsync(p, description: text);
        });
        Add(menu, "Changer la pochette", "", () => _ = vm.SetPlaylistCoverAsync(p));
        Add(menu, p.IsPublic ? "Rendre privée" : "Rendre publique", "",
            () => _ = vm.SetPlaylistDetailsAsync(p, isPublic: !p.IsPublic));
        Add(menu, p.IsCollaborative ? "Arrêter la collaboration" : "Rendre collaborative", "",
            () => _ = vm.SetPlaylistDetailsAsync(p, collaborative: !p.IsCollaborative));
    }

    /// <summary>Rename and delete for a local playlist, shared by every menu that offers them.</summary>
    static void AddPlaylistActions(ItemsControl menu, Playlist p, Window owner)
    {
        var vm = Vm;
        Add(menu, "Renommer", "", () =>
        {
            var name = Prompt.Show(owner, "Renommer la playlist", p.Name);
            if (name != null) vm.RenamePlaylist(p, name);
        });
        Add(menu, "Supprimer", "", () =>
        {
            var r = MessageBox.Show(owner, $"Supprimer « {p.Name} » de votre bibliothèque ?", "Supprimer la playlist",
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
        menu.Items.Add(new MenuItem { Header = "Lire sur", IsEnabled = false });
        if (devices.Count == 0) menu.Items.Add(new MenuItem { Header = "Aucun appareil disponible", IsEnabled = false });
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
        menu.Items.Add(new MenuItem { Header = "Minuteur de veille", IsEnabled = false });
        foreach (var minutes in new[] { 15, 30, 45, 60 })
        {
            var delay = minutes;
            Add(menu, $"{minutes} minutes", "", () => vm.StartSleep(delay));
        }
        Add(menu, "À la fin du titre", "", vm.StartSleepAtTrackEnd);
        if (vm.IsSleepActive)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = vm.SleepStatus, IsEnabled = false });
            Add(menu, "Annuler", "", vm.CancelSleep);
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
