using Sonora.Appearance.Features;

namespace Sonora.Tests;

public class LibrarySourcesTests
{
    [Fact]
    public void Resolve_KeepsASelectedSourceThatIsStillAvailable()
        => Assert.Equal("spotify", LibrarySources.Resolve("spotify", ["local", "spotify"]));

    [Fact]
    public void Resolve_FallsBackToAllWhenTheSourceWentAway()
    {
        Assert.Equal(LibrarySources.All, LibrarySources.Resolve("spotify", ["local"]));
        Assert.Equal(LibrarySources.All, LibrarySources.Resolve(null, ["local"]));
        Assert.Equal(LibrarySources.All, LibrarySources.Resolve("nonsense", ["local", "spotify"]));
    }

    [Fact]
    public void Shows_AllShowsEverythingAndASourceOnlyItsOwn()
    {
        Assert.True(LibrarySources.Shows(LibrarySources.All, "spotify"));
        Assert.True(LibrarySources.Shows("local", "local"));
        Assert.False(LibrarySources.Shows("local", "spotify"));
    }
}

public class LocalMatcherTests
{
    sealed record Song(string Title, string Artist, double Seconds, string Path);

    static LocalMatcher<Song> Matcher(params Song[] songs) => new(songs, s => new TrackKey(s.Title, s.Artist, s.Seconds));

    [Fact]
    public void Find_MatchesOnTitleArtistAndLengthIgnoringCaseAndAccents()
    {
        var m = Matcher(new Song("Freestyle Skyrock", "Beendo Z", 85.8, "a.mp3"), new Song("Autre", "X", 60, "b.mp3"));
        Assert.Equal("a.mp3", m.Find(new TrackKey("FREESTYLE SKYRÓCK", "beendo z", 86))?.Path);
    }

    /// <summary>
    /// Issue #11: the lyrics search fallback took the closest length among LRCLIB's results however far off it was,
    /// so a 4:07 mashup named "4 Raws" got the 2:27 song's timed lyrics. These are LRCLIB's real entries for it.
    /// </summary>
    [Fact]
    public void Find_LyricsCandidates_RefusesAnotherRecordingAndPicksTheSameOne()
    {
        var m = Matcher(new Song("4 Raws", "EsDeeKid", 155, "155"), new Song("4 Raws", "EsDeeKid", 180, "180"),
                        new Song("4 Raws", "EsDeeKid", 220, "220"), new Song("4 Raws", "EsDeeKid", 146.666, "146.666"),
                        new Song("4 Raws", "Esdeekid", 147, "147"));
        Assert.Null(m.Find(new TrackKey("4 Raws", "EsDeeKid", 246.9)));                // a mashup: no lyrics beats wrong ones
        Assert.Null(m.Find(new TrackKey("4 Raws x Monëy so big", "EsDeeKid", 147)));   // another title
        Assert.Equal("146.666", m.Find(new TrackKey("4 Raws", "EsDeeKid", 146.6))?.Path); // the closest of those that agree
    }

    [Fact]
    public void Find_RefusesALengthThatIsTooFarOff()
    {
        var m = Matcher(new Song("Titre", "Artiste", 200, "radio-edit.mp3"));
        Assert.Null(m.Find(new TrackKey("Titre", "Artiste", 240)));
        Assert.NotNull(m.Find(new TrackKey("Titre", "Artiste", 202)));
    }

    [Fact]
    public void Find_PrefersTheClosestLengthWhenSeveralFilesQualify()
    {
        var m = Matcher(new Song("Titre", "A", 100, "far.mp3"), new Song("Titre", "A", 101.5, "near.mp3"));
        Assert.Equal("near.mp3", m.Find(new TrackKey("Titre", "A", 101.4))?.Path);
    }

    [Fact]
    public void Find_ArtistsOverlapButMustNotContradict()
    {
        var m = Matcher(new Song("Titre", "A, B", 100, "feat.mp3"));
        Assert.NotNull(m.Find(new TrackKey("Titre", "A", 100)));
        Assert.Null(m.Find(new TrackKey("Titre", "Z", 100)));
    }

    [Fact]
    public void Find_UnknownArtistOrLengthDoesNotBlockAMatchButAnotherTitleDoes()
    {
        var m = Matcher(new Song("Titre", "A", 100, "a.mp3"));
        Assert.NotNull(m.Find(new TrackKey("Titre", "", 0)));
        Assert.Null(m.Find(new TrackKey("Titre 2", "A", 100)));
        Assert.Null(m.Find(new TrackKey("", "A", 100)));
    }
}

public class PlaylistFileTests
{
    [Fact]
    public void RoundTrip_KeepsSpotifyTracksAndLocalFilesTogether()
    {
        var json = PlaylistFile.Write("Road trip",
        [
            new PlaylistFileEntry("Champion", "KXTSU", "Remixes", 124.08, Spotify: "spotify:track:abc"),
            new PlaylistFileEntry("Freestyle", "Beendo Z", "Music", 85.7, Path: @"C:\Music\a.mp3"),
        ]);

        var file = PlaylistFile.Parse(json);
        Assert.Equal("Road trip", file.Name);
        Assert.Equal(2, file.Tracks.Count);
        Assert.True(file.Tracks[0].IsSpotify);
        Assert.Equal("spotify:track:abc", file.Tracks[0].Spotify);
        Assert.Null(file.Tracks[0].Path);
        Assert.Equal(@"C:\Music\a.mp3", file.Tracks[1].Path);
        Assert.Equal(85.7, file.Tracks[1].Seconds);
    }

    [Theory]
    [InlineData("spotify:track:1sNcgBCWbf0WtoOONCORWO", true)]
    [InlineData("spotify:episode:1sNcgBCWbf0WtoOONCORWO", true)]
    [InlineData("spotify:track:short", false)]
    [InlineData("spotify:local:artist:album:title:123", false)]
    [InlineData("spotify:playlist:1sNcgBCWbf0WtoOONCORWO", false)]
    [InlineData("spotify:track:1sNcgBCWbf0WtoOONCORWO\n", false)]
    [InlineData("https://open.spotify.com/track/1sNcgBCWbf0WtoOONCORWO", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSpotifyItemUri_AcceptsOnlyWellFormedTrackAndEpisodeIds(string? uri, bool expected)
        => Assert.Equal(expected, PlaylistFile.IsSpotifyItemUri(uri));

    [Fact]
    public void Parse_RejectsWhatIsNotAPlaylistFile()
    {
        Assert.Throws<FormatException>(() => PlaylistFile.Parse("not json"));
        Assert.Throws<FormatException>(() => PlaylistFile.Parse("{\"tracks\":[]}"));
        Assert.Throws<FormatException>(() => PlaylistFile.Parse("null"));
    }

    [Fact]
    public void Parse_RejectsAFileFromANewerVersion()
        => Assert.Throws<FormatException>(() => PlaylistFile.Parse("{\"format\":\"sonora-playlist\",\"version\":99,\"name\":\"x\",\"tracks\":[]}"));

    [Fact]
    public void Parse_DropsEmptyEntriesAndToleratesAMissingName()
    {
        var file = PlaylistFile.Parse("{\"format\":\"sonora-playlist\",\"version\":1,\"tracks\":[{\"title\":\"\"},{\"title\":\"Ok\"}]}");
        Assert.Equal("", file.Name);
        Assert.Single(file.Tracks);
        Assert.Equal("Ok", file.Tracks[0].Title);
    }
}
