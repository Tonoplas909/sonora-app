using Sonora.Appearance.Features;

namespace Sonora.Tests;

public class SearchRankTests
{
    static RankedResult R(ResultKind kind, string name) => new(kind, name, name);

    [Fact]
    public void Score_ExactNameBeatsPrefixBeatsContains()
    {
        var exact = SearchRank.Score("niska", "Niska");
        var prefix = SearchRank.Score("niska", "Niska Deluxe");
        var contains = SearchRank.Score("niska", "Best of Niska Deluxe");
        Assert.True(exact > prefix);
        Assert.True(prefix > contains);
        Assert.True(contains > 0);
    }

    [Fact]
    public void Score_IgnoresAccentsAndCase()
    {
        Assert.Equal(SearchRank.Score("niska", "Niska"), SearchRank.Score("NÏSKA", "niska"));
        Assert.Equal(1000, SearchRank.Score("eté", "Été"));
    }

    [Fact]
    public void Score_ShorterNameWinsAtEqualPrefix()
    {
        Assert.True(SearchRank.Score("free", "Freestyle") > SearchRank.Score("free", "Freestyle Skyrock vol. 2"));
    }

    [Fact]
    public void Score_UnrelatedNameScoresZero()
    {
        Assert.Equal(0, SearchRank.Score("niska", "Booba"));
        Assert.Equal(0, SearchRank.Score("", "Niska"));
        Assert.Equal(0, SearchRank.Score("niska", ""));
    }

    [Fact]
    public void Score_EveryWordPresentIsAWeakMatch()
    {
        var scattered = SearchRank.Score("skyrock freestyle", "Freestyle au Skyrock");
        Assert.True(scattered > 0);
        Assert.True(scattered < SearchRank.Score("freestyle", "Freestyle"));
    }

    [Fact]
    public void Best_PrefersTheArtistWhenNamesTie()
    {
        var best = SearchRank.Best("niska", [R(ResultKind.Track, "Niska"), R(ResultKind.Artist, "Niska"), R(ResultKind.Album, "Niska")]);
        Assert.Equal(ResultKind.Artist, best!.Kind);
    }

    [Fact]
    public void Best_ACloserNameBeatsAPreferredKind()
    {
        var best = SearchRank.Best("medicament", [R(ResultKind.Artist, "Medicament Remix Collection"), R(ResultKind.Track, "Medicament")]);
        Assert.Equal(ResultKind.Track, best!.Kind);
    }

    [Fact]
    public void Best_IsNullWhenNothingMatchesWellEnough()
    {
        Assert.Null(SearchRank.Best("niska", [R(ResultKind.Track, "Booba")]));
        Assert.Null(SearchRank.Best("skyrock freestyle", [R(ResultKind.Track, "Freestyle au Skyrock")]));
        Assert.Null(SearchRank.Best("niska", []));
    }

    [Fact]
    public void KindLabel_NamesEveryKind()
    {
        foreach (ResultKind kind in Enum.GetValues<ResultKind>())
            Assert.False(string.IsNullOrWhiteSpace(SearchRank.KindLabel(kind)));
    }
}
