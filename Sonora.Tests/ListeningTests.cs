using Sonora.Appearance.Features;

namespace Sonora.Tests;

public class ListeningTests
{
    static ListenEntry Entry(string key, string artist, double seconds, DateTime atUtc, string title = "T") =>
        new() { Key = key, Artist = artist, Title = title, Seconds = seconds, AtUtc = atUtc };

    // ---------------- what counts as a play ----------------

    [Fact]
    public void Counts_IgnoresTracksShorterThanThirtySeconds()
    {
        Assert.False(Listening.Counts(playedSeconds: 29, durationSeconds: 29));
        Assert.False(Listening.Counts(playedSeconds: 20, durationSeconds: 20));
    }

    [Fact]
    public void Counts_NeedsHalfTheTrack()
    {
        Assert.False(Listening.Counts(playedSeconds: 59, durationSeconds: 120));
        Assert.True(Listening.Counts(playedSeconds: 60, durationSeconds: 120));
    }

    [Fact]
    public void Counts_LongTrackCountsAfterFourMinutes()
    {
        // A 20-minute track must not need ten minutes to count.
        Assert.True(Listening.Counts(playedSeconds: 240, durationSeconds: 1200));
        Assert.False(Listening.Counts(playedSeconds: 239, durationSeconds: 1200));
    }

    // ---------------- periods ----------------

    [Fact]
    public void Since_KeepsOnlyThePeriodAndEverythingForZero()
    {
        var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        List<ListenEntry> entries =
        [
            Entry("a", "A", 100, now.AddDays(-1)),
            Entry("b", "B", 100, now.AddDays(-10)),
            Entry("c", "C", 100, now.AddDays(-400)),
        ];
        Assert.Single(Listening.Since(entries, 7, now));
        Assert.Equal(2, Listening.Since(entries, 30, now).Count);
        Assert.Equal(3, Listening.Since(entries, 0, now).Count);
    }

    // ---------------- tops ----------------

    [Fact]
    public void TopTracks_RanksByPlaysThenTimeListened()
    {
        var now = DateTime.UtcNow;
        List<ListenEntry> entries =
        [
            Entry("a", "A", 200, now, "Twice"), Entry("a", "A", 200, now, "Twice"),
            Entry("b", "B", 900, now, "Once but long"),
        ];
        var top = Listening.TopTracks(entries);
        Assert.Equal("a", top[0].Key);
        Assert.Equal(2, top[0].Plays);
        Assert.Equal("b", top[1].Key);
    }

    [Fact]
    public void TopArtists_GroupsCaseInsensitively()
    {
        var now = DateTime.UtcNow;
        List<ListenEntry> entries = [Entry("a", "Niska", 100, now), Entry("b", "niska", 100, now)];
        var top = Listening.TopArtists(entries);
        Assert.Single(top);
        Assert.Equal(2, top[0].Plays);
    }

    [Fact]
    public void Totals_CountsDistinctTracksAndArtists()
    {
        var now = DateTime.UtcNow;
        List<ListenEntry> entries =
        [
            Entry("a", "A", 100, now), Entry("a", "A", 100, now), Entry("b", "B", 50, now),
        ];
        var totals = Listening.Totals(entries);
        Assert.Equal(3, totals.Plays);
        Assert.Equal(250, totals.Seconds);
        Assert.Equal(2, totals.Tracks);
        Assert.Equal(2, totals.Artists);
    }

    [Fact]
    public void ByHour_BucketsOnLocalTime()
    {
        var at = new DateTime(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc);
        var hours = Listening.ByHour([Entry("a", "A", 100, at)]);
        Assert.Equal(1, hours[at.ToLocalTime().Hour]);
        Assert.Equal(1, hours.Sum());
    }

    // ---------------- formatting ----------------

    [Theory]
    [InlineData(18, "18 s")]
    [InlineData(150, "2 min")]   // truncated: 2 min 30 s is not yet three minutes of listening
    [InlineData(3600, "1 h 00 min")]
    [InlineData(13320, "3 h 42 min")]
    public void Duration_ReadsAsTimeNotSeconds(double seconds, string expected) =>
        Assert.Equal(expected, Listening.Duration(seconds));

    [Fact]
    public void PeriodLabel_CoversEveryOfferedPeriod()
    {
        Assert.All(Listening.Periods, d => Assert.False(string.IsNullOrWhiteSpace(Listening.PeriodLabel(d))));
        Assert.Equal("Depuis toujours", Listening.PeriodLabel(0));
    }
}
