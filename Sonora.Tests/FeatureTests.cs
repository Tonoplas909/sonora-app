using Sonora.Appearance.Content;
using Sonora.Appearance.Features;
using Sonora.Appearance.Layout;

namespace Sonora.Tests;

public class FeatureTests
{
    // ---------------- settings ----------------

    [Fact]
    public void Settings_DefaultsEnableEverything()
    {
        var f = new FeatureSettings();
        Assert.True(f.Lyrics && f.Podcasts && f.Tops && f.History && f.NewReleases && f.TrackToast && f.KeepRecentSearches);
        Assert.Equal(1.0, f.LyricsScale);
        Assert.Equal("short_term", f.TopRange);
    }

    [Fact]
    public void Settings_RepairClampsAndFixesUnknownValues()
    {
        var f = new FeatureSettings { LyricsScale = 9, MiniPlayerOpacity = 0.01, ToastSeconds = 99, LyricsAlign = "zigzag", LibrarySort = "?", TopRange = "century" };
        f.RecentSearches = null!;
        f.Pinned = null!;
        f.Repair();
        Assert.Equal(1.6, f.LyricsScale);
        Assert.Equal(0.4, f.MiniPlayerOpacity);
        Assert.Equal(10, f.ToastSeconds);
        Assert.Equal("left", f.LyricsAlign);
        Assert.Equal("recent", f.LibrarySort);
        Assert.Equal("short_term", f.TopRange);
        Assert.Empty(f.RecentSearches);
        Assert.Empty(f.Pinned);
    }

    [Fact]
    public void RecentSearches_DedupeNewestFirstCappedAtTen()
    {
        var f = new FeatureSettings();
        for (int i = 0; i < 12; i++) f.AddRecentSearch("requête " + i);
        f.AddRecentSearch("  REQUÊTE 5 ");
        Assert.Equal(10, f.RecentSearches.Count);
        Assert.Equal("REQUÊTE 5", f.RecentSearches[0]);
        Assert.Single(f.RecentSearches, s => s.Equals("requête 5", StringComparison.CurrentCultureIgnoreCase));
        f.AddRecentSearch("   ");
        Assert.Equal(10, f.RecentSearches.Count);
    }

    [Fact]
    public void RecentSearches_NotKeptWhenDisabled()
    {
        var f = new FeatureSettings { KeepRecentSearches = false };
        f.AddRecentSearch("jazz");
        Assert.Empty(f.RecentSearches);
    }

    [Fact]
    public void Pins_Toggle()
    {
        var f = new FeatureSettings();
        Assert.True(f.TogglePin("spotify:playlist:abc"));
        Assert.True(f.IsPinned("spotify:playlist:abc"));
        Assert.False(f.TogglePin("spotify:playlist:abc"));
        Assert.Empty(f.Pinned);
    }

    [Fact]
    public void Pins_MoveDownAndUp()
    {
        var f = new FeatureSettings { Pinned = ["a", "b", "c", "d"] };
        Assert.True(f.MovePin("a", "c"));
        Assert.Equal(["b", "c", "a", "d"], f.Pinned);
        Assert.True(f.MovePin("d", "b"));
        Assert.Equal(["d", "b", "c", "a"], f.Pinned);
        Assert.Equal(2, f.PinIndex("c"));
    }

    [Fact]
    public void Pins_MoveIgnoresUnknownAndSame()
    {
        var f = new FeatureSettings { Pinned = ["a", "b"] };
        Assert.False(f.MovePin("a", "a"));
        Assert.False(f.MovePin("a", "zzz"));
        Assert.False(f.MovePin("zzz", "a"));
        Assert.Equal(["a", "b"], f.Pinned);
        Assert.Equal(-1, f.PinIndex("zzz"));
    }

    // ---------------- lyrics ----------------

    const string Sample = "[ar:Artiste inventé]\n[00:12.50]première ligne d'essai\n[00:05.00]ligne d'ouverture\n[01:02.30][00:20.00]refrain d'essai\n\n[00:30.00]\npas d'horodatage ici";

    [Fact]
    public void Lrc_ParsesSortsAndExpandsRepeatedStamps()
    {
        var lines = Lrc.Parse(Sample);
        Assert.Equal(new[] { 5.0, 12.5, 20.0, 30.0, 62.3 }, lines.Select(l => Math.Round(l.Time.TotalSeconds, 2)));
        Assert.Equal("ligne d'ouverture", lines[0].Text);
        Assert.Equal("refrain d'essai", lines[2].Text);
        Assert.Equal("", lines[3].Text);
        Assert.Equal("refrain d'essai", lines[4].Text);
    }

    [Fact]
    public void Lrc_EmptyOrNullGivesNoLines()
    {
        Assert.Empty(Lrc.Parse(null));
        Assert.Empty(Lrc.Parse("juste du texte\nsans horodatage"));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(5, 0)]
    [InlineData(12.4, 0)]
    [InlineData(12.5, 1)]
    [InlineData(500, 4)]
    public void Lrc_IndexAtFindsCurrentLine(double seconds, int expected) =>
        Assert.Equal(expected, Lrc.IndexAt(Lrc.Parse(Sample), TimeSpan.FromSeconds(seconds)));

    // ---------------- list query ----------------

    sealed record Row(string Title, string Artist, string Album, double Duration);

    static readonly Row[] Rows =
    [
        new("Zèbre", "Bravo", "Un", 200), new("été indien", "Alpha", "Deux", 100), new("Aube", "Charlie", "Trois", 300),
    ];

    static List<Row> Query(string? filter, string sort, bool desc = false) =>
        ListQuery.Apply(Rows, filter, sort, desc, r => r.Title, r => r.Artist, r => r.Album, r => r.Duration);

    [Fact]
    public void ListQuery_DefaultKeepsOrder() => Assert.Equal(Rows, Query(null, "default"));

    [Fact]
    public void ListQuery_SortsByEachKey()
    {
        Assert.Equal(new[] { "Aube", "été indien", "Zèbre" }, Query("", "title").Select(r => r.Title));
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, Query("", "artist").Select(r => r.Artist));
        Assert.Equal(new[] { 300.0, 200, 100 }, Query("", "duration", desc: true).Select(r => r.Duration));
        Assert.Equal(Rows, Query("", "inconnu"));
    }

    [Fact]
    public void ListQuery_SortsByDateAdded()
    {
        long Added(Row r) => r.Title switch { "Zèbre" => 20, "été indien" => 30, _ => 10 };
        List<string> By(bool desc) => ListQuery.Apply(Rows, "", "added", desc, r => r.Title, r => r.Artist, r => r.Album, r => r.Duration, Added)
            .Select(r => r.Title).ToList();
        Assert.Equal(new[] { "Aube", "Zèbre", "été indien" }, By(false));
        Assert.Equal(new[] { "été indien", "Zèbre", "Aube" }, By(true));
        // no dates known: the key leaves the order alone
        Assert.Equal(Rows, Query("", "added"));
    }

    [Fact]
    public void ListQuery_FilterIgnoresCaseAndAccentsAcrossFields()
    {
        Assert.Equal(new[] { "été indien" }, Query("ETE", "default").Select(r => r.Title));
        Assert.Equal(new[] { "Aube" }, Query("charlie trois", "default").Select(r => r.Title));
        Assert.Empty(Query("introuvable", "default"));
    }

    // ---------------- sleep timer ----------------

    [Fact]
    public void SleepTimer_MinutesCountDownThenStopOnce()
    {
        var t0 = new DateTime(2026, 1, 1, 22, 0, 0);
        var s = new SleepTimer();
        Assert.False(s.IsActive);
        s.StartMinutes(30, t0);
        Assert.True(s.IsActive);
        Assert.Equal(TimeSpan.FromMinutes(18), s.Remaining(t0.AddMinutes(12)));
        Assert.False(s.ShouldStop(t0.AddMinutes(29)));
        Assert.True(s.ShouldStop(t0.AddMinutes(30)));
        Assert.False(s.IsActive);
        Assert.False(s.ShouldStop(t0.AddMinutes(31)));
    }

    [Fact]
    public void SleepTimer_EndOfTrackStopsOnNextTrackChange()
    {
        var s = new SleepTimer();
        s.StartAtTrackEnd();
        Assert.True(s.IsActive);
        Assert.False(s.ShouldStop(DateTime.Now));
        Assert.True(s.ShouldStopOnTrackChange());
        Assert.False(s.ShouldStopOnTrackChange());
    }

    [Fact]
    public void SleepTimer_CancelAndDescribe()
    {
        var t0 = new DateTime(2026, 1, 1, 22, 0, 0);
        var s = new SleepTimer();
        Assert.Equal("", s.Describe(t0));
        s.StartMinutes(15, t0);
        Assert.Equal("Arrêt dans 15 min", s.Describe(t0));
        Assert.Equal("Arrêt dans 1 min", s.Describe(t0.AddMinutes(14).AddSeconds(30)));
        s.StartAtTrackEnd();
        Assert.Equal("Arrêt à la fin du titre", s.Describe(t0));
        s.Cancel();
        Assert.False(s.IsActive);
    }

    // ---------------- home sections and lyrics panel ----------------

    [Fact]
    public void Home_NewSectionsAreAppendedByRepair()
    {
        var c = new ContentSettings { HomeSections = [new HomeSection { Id = "recent" }, new HomeSection { Id = "artists", Visible = false }] };
        c.Repair();
        Assert.Contains(c.HomeSections, s => s.Id == HomeSectionIds.NewReleases);
        Assert.Contains(c.HomeSections, s => s.Id == HomeSectionIds.TopArtists);
        Assert.Contains(c.HomeSections, s => s.Id == HomeSectionIds.Shows);
        Assert.False(c.HomeSections.First(s => s.Id == "artists").Visible);
    }

    [Fact]
    public void LyricsPanel_IsHiddenByDefaultEvenForOldLayouts()
    {
        Assert.Contains(PanelIds.Lyrics, DockLayout.Default().Hidden);
        var old = DockLayout.Default();
        old.Hidden.Remove(PanelIds.Lyrics); // a layout saved before the panel existed
        var repaired = DockLayoutOps.Repair(old, 1400, 900);
        Assert.Contains(PanelIds.Lyrics, repaired.Hidden);
        Assert.Null(DockLayoutOps.Find(repaired, PanelIds.Lyrics));
        DockLayoutOps.Show(repaired, PanelIds.Lyrics);
        Assert.Equal(DockZone.Right, DockLayoutOps.Find(repaired, PanelIds.Lyrics)!.Value.Zone);
    }
}
