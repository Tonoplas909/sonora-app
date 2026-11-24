using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sonora.Core;
using Sonora.Models;
using Sonora.Services;
using Sonora.ViewModels;
using Sonora.Appearance.Localization;

namespace Sonora.Views;

/// <summary>
/// Lets the user pick, on LRCLIB, the lyrics of a local file by hand: a search prefilled with its title and artist,
/// the entries found with their length to compare with the file's, and a glimpse of each one's text.
/// </summary>
static class LyricsPicker
{
    public static void Show(Window owner, MainViewModel vm, Track track)
    {
        var w = new Window
        {
            Title = Loc.T("lyrics.choose_title"),
            Owner = owner,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        w.SetResourceReference(Window.BackgroundProperty, "Brush.Surface");
        w.SetResourceReference(Window.ForegroundProperty, "Brush.TextPrimary");

        var heading = new TextBlock { Text = Loc.T("lyrics.choose_title"), FontWeight = FontWeights.Bold };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "Size.Text.Section");
        var hint = Muted(Loc.T("lyrics.picker_hint", $"{track.Title} — {track.Artist}", Fmt.Time(track.DurationSec)));
        hint.Margin = new Thickness(0, 4, 0, 12);
        hint.TextWrapping = TextWrapping.Wrap;

        var query = new TextBox { Text = $"{track.Title} {track.Artist.Split(',')[0].Trim()}".Trim(), VerticalContentAlignment = VerticalAlignment.Center };
        var search = new Button { Content = Loc.T("lyrics.search"), Margin = new Thickness(8, 0, 0, 0) };
        search.SetResourceReference(FrameworkElement.StyleProperty, "PillOutline");
        var searchRow = new DockPanel();
        DockPanel.SetDock(search, Dock.Right);
        searchRow.Children.Add(search);
        searchRow.Children.Add(query);

        var status = Muted("");
        status.Margin = new Thickness(0, 10, 0, 6);
        var results = new ListBox { Height = 230, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        // Pixel scrolling, like every list in Sonora: the app's smooth wheel moves by pixels, and with the default
        // item-by-item scrolling each notch jumped 40 entries at once.
        ScrollViewer.SetCanContentScroll(results, false);
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        preview.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        var previewBox = new ScrollViewer { Height = 120, Margin = new Thickness(0, 10, 0, 16), Content = preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        long? chosen = null;
        var use = new Button { Content = Loc.T("lyrics.use_these"), IsDefault = false, IsEnabled = false };
        use.SetResourceReference(FrameworkElement.StyleProperty, "AccentPill");
        use.Click += (_, _) => { chosen = (results.SelectedItem as ListBoxItem)?.Tag is LyricsCandidate c ? c.Id : null; w.DialogResult = chosen != null; };
        var cancel = new Button { Content = Loc.T("common.cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "PillOutline");
        var buttons = new DockPanel { LastChildFill = false };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(cancel);
        right.Children.Add(use);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        bool unlink = false;
        if (vm.LinkedLyrics(track) != null)
        {
            var remove = new Button { Content = Loc.T("lyrics.remove_link") };
            remove.SetResourceReference(FrameworkElement.StyleProperty, "PillOutline");
            remove.Click += (_, _) => { unlink = true; w.DialogResult = true; };
            buttons.Children.Add(remove);
        }

        results.SelectionChanged += (_, _) =>
        {
            var c = (results.SelectedItem as ListBoxItem)?.Tag as LyricsCandidate;
            use.IsEnabled = c != null;
            preview.Text = c == null ? "" : PreviewOf(c.Lyrics);
        };
        results.MouseDoubleClick += (_, _) => { if (use.IsEnabled) use.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };

        int run = 0;
        async void RunSearch()
        {
            var text = query.Text.Trim();
            if (text.Length == 0) return;
            int token = ++run;
            status.Text = Loc.T("lyrics.searching");
            results.Items.Clear();
            List<LyricsCandidate> found;
            try { found = await vm.SearchLyricsAsync(text); }
            catch
            {
                if (token == run) status.Text = Loc.T("lyrics.search_failed");
                return;
            }
            if (token != run) return; // a newer search was started meanwhile
            // Closest length first: the same recording is usually the one that lasts as long as the file.
            foreach (var c in found.OrderBy(c => track.DurationSec > 0 ? Math.Abs(c.DurationSec - track.DurationSec) : 0))
                results.Items.Add(Row(c, c.Id == vm.LinkedLyrics(track)));
            status.Text = found.Count == 0 ? Loc.T("lyrics.no_results") : "";
            results.SelectedIndex = found.Count > 0 ? 0 : -1;
        }
        search.Click += (_, _) => RunSearch();
        query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { RunSearch(); e.Handled = true; } };

        var root = new StackPanel { Margin = new Thickness(24) };
        foreach (var child in new UIElement[] { heading, hint, searchRow, status, results, previewBox, buttons }) root.Children.Add(child);
        w.Content = root;
        w.Loaded += (_, _) => { query.Focus(); RunSearch(); };

        if (w.ShowDialog() != true) return;
        if (unlink) vm.LinkLyrics(track, null);
        else if (chosen != null) vm.LinkLyrics(track, chosen);
    }

    static ListBoxItem Row(LyricsCandidate c, bool current)
    {
        var kind = c.Lyrics.Instrumental ? Loc.T("lyrics.instrumental")
            : c.Lyrics.Synced.Count > 0 ? Loc.T("lyrics.synced")
            : Loc.T("lyrics.plain_only");
        var details = string.Join(" · ", new[] { c.Album, Fmt.Time(c.DurationSec), kind }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (current) details += " · " + Loc.T("lyrics.linked_now");
        var title = new TextBlock { Text = $"{c.Title} — {c.Artist}", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var sub = Muted(details);
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        var panel = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
        panel.Children.Add(title);
        panel.Children.Add(sub);
        return new ListBoxItem { Content = panel, Tag = c };
    }

    /// <summary>The first lines of an entry, enough to recognise the song.</summary>
    static string PreviewOf(LyricsResult r)
    {
        if (r.Instrumental) return Loc.T("lyrics.instrumental");
        var lines = r.Synced.Count > 0 ? r.Synced.Select(l => l.Text) : (r.Plain ?? "").Split('\n').Select(l => l.TrimEnd('\r'));
        return string.Join("\n", lines.Where(l => l.Trim().Length > 0).Take(8));
    }

    static TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        return t;
    }
}
