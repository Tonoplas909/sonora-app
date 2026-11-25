using Sonora.Appearance.Features;

namespace Sonora.Tests;

public class ListenBrainzApiTests
{
    [Fact]
    public void ParseToken_ValidTokenGivesTheUser()
    {
        var check = ListenBrainzApi.ParseToken("""{"code":200,"message":"Token valid.","valid":true,"user_name":"mr_monkey"}""");
        Assert.True(check.Valid);
        Assert.Equal("mr_monkey", check.UserName);
    }

    [Fact]
    public void ParseToken_TheEndpointAnswers200ForABadToken_SoOnlyValidCounts()
    {
        var check = ListenBrainzApi.ParseToken("""{"code":200,"message":"Token invalid.","valid":false}""");
        Assert.False(check.Valid);
        Assert.Equal("", check.UserName);
    }

    [Fact]
    public void ParseToken_ValidWithoutAUserNameIsUnusable()
    {
        Assert.False(ListenBrainzApi.ParseToken("""{"valid":true}""").Valid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("[1,2]")]
    public void EveryReader_TreatsNonsenseAsNothing(string? body)
    {
        Assert.False(ListenBrainzApi.ParseToken(body).Valid);
        Assert.Empty(ListenBrainzApi.ParseArtists(body));
        Assert.Empty(ListenBrainzApi.ParseRecordings(body));
        Assert.Empty(ListenBrainzApi.ParseActivity(body));
        Assert.Equal(-1, ListenBrainzApi.ParseListenCount(body));
    }

    [Fact]
    public void ParseArtists_ReadsNamesAndCounts()
    {
        var rows = ListenBrainzApi.ParseArtists("""
            {"payload":{"artists":[{"artist_name":"Daft Punk","listen_count":42},{"artist_name":"Air","listen_count":7}]}}
            """);
        Assert.Equal(["Daft Punk", "Air"], rows.Select(r => r.Name));
        Assert.Equal([42, 7], rows.Select(r => r.Listens));
    }

    [Fact]
    public void ParseArtists_SkipsRowsWithoutAName_AndToleratesAMissingCount()
    {
        var rows = ListenBrainzApi.ParseArtists("""
            {"payload":{"artists":[{"listen_count":3},{"artist_name":"Air"},"oops",{"artist_name":"Moby","listen_count":"x"}]}}
            """);
        Assert.Equal(["Air", "Moby"], rows.Select(r => r.Name));
        Assert.All(rows, r => Assert.Equal(0, r.Listens));
    }

    [Fact]
    public void ParseRecordings_KeepsTheArtistOfEachTrack()
    {
        var rows = ListenBrainzApi.ParseRecordings("""
            {"payload":{"recordings":[{"track_name":"One More Time","artist_name":"Daft Punk","release_name":"Discovery","listen_count":12}]}}
            """);
        var row = Assert.Single(rows);
        Assert.Equal("One More Time", row.Name);
        Assert.Equal("Daft Punk", row.Artist);
        Assert.Equal(12, row.Listens);
    }

    [Fact]
    public void ParseActivity_ReadsTheStartOfEachSlice()
    {
        var rows = ListenBrainzApi.ParseActivity("""
            {"payload":{"listening_activity":[{"from_ts":1700000000,"to_ts":1700086399,"time_range":"Tuesday","listen_count":5},{"to_ts":1}]}}
            """);
        var row = Assert.Single(rows);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime, row.FromUtc);
        Assert.Equal(5, row.Listens);
    }

    [Fact]
    public void ParseListenCount_ReadsThePayload()
    {
        Assert.Equal(12345, ListenBrainzApi.ParseListenCount("""{"payload":{"count":12345}}"""));
        Assert.Equal(-1, ListenBrainzApi.ParseListenCount("""{"payload":{}}"""));
    }

    [Fact]
    public void Ranges_AreTheOnesThePageOffers()
    {
        Assert.True(ListenBrainzApi.IsKnownRange("this_week"));
        Assert.True(ListenBrainzApi.IsKnownRange("all_time"));
        Assert.False(ListenBrainzApi.IsKnownRange("../../x"));
        Assert.False(ListenBrainzApi.IsKnownRange(null));
    }
}
