using Sonora.Appearance.Features;

namespace Sonora.Tests;

public class DropRuleTests
{
    [Fact]
    public void LocalPlaylist_TakesLocalFiles()
    {
        var v = DropRule.Judge(DropTarget.LocalPlaylist, localTracks: 3, spotifyTracks: 0);
        Assert.Equal(new DropVerdict(3, 3, DropRefusal.None), v);
        Assert.True(v.IsAccepted);
        Assert.False(v.IsPartial);
    }

    [Fact]
    public void LocalPlaylist_RefusesSpotifyTracks()
    {
        var v = DropRule.Judge(DropTarget.LocalPlaylist, 0, 2);
        Assert.False(v.IsAccepted);
        Assert.Equal(DropRefusal.SpotifyIntoLocal, v.Refusal);
    }

    [Fact]
    public void OwnSpotifyPlaylist_TakesSpotifyTracksAndRefusesFiles()
    {
        Assert.Equal(new DropVerdict(2, 2, DropRefusal.None), DropRule.Judge(DropTarget.OwnSpotifyPlaylist, 0, 2));
        Assert.Equal(DropRefusal.LocalIntoSpotify, DropRule.Judge(DropTarget.OwnSpotifyPlaylist, 4, 0).Refusal);
    }

    [Fact]
    public void MixedSelection_IsPartialAndCountsOnlyWhatTheTargetHolds()
    {
        var local = DropRule.Judge(DropTarget.LocalPlaylist, 2, 1);
        Assert.Equal(2, local.Accepted);
        Assert.Equal(3, local.Total);
        Assert.True(local.IsPartial);

        var spotify = DropRule.Judge(DropTarget.OwnSpotifyPlaylist, 2, 1);
        Assert.Equal(1, spotify.Accepted);
        Assert.True(spotify.IsPartial);
    }

    [Fact]
    public void ForeignSpotifyPlaylist_RefusesEverything()
    {
        var v = DropRule.Judge(DropTarget.ForeignSpotifyPlaylist, 1, 1);
        Assert.False(v.IsAccepted);
        Assert.Equal(DropRefusal.NotYours, v.Refusal);
    }

    [Fact]
    public void AnythingThatIsNotAPlaylist_TakesNothing()
    {
        var v = DropRule.Judge(DropTarget.Other, 1, 1);
        Assert.False(v.IsAccepted);
        Assert.Equal(DropRefusal.NotAPlaylist, v.Refusal);
    }

    [Fact]
    public void EmptySelection_IsNeverAccepted()
    {
        foreach (var target in Enum.GetValues<DropTarget>())
            Assert.False(DropRule.Judge(target, 0, 0).IsAccepted);
    }
}
