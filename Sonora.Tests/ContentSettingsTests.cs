using System.Text.Json;
using Sonora.Appearance.Content;

namespace Sonora.Tests;

public class ContentSettingsTests
{
    [Fact]
    public void Default_HasAllSectionsVisibleInOrder()
    {
        var c = ContentSettings.Default();
        Assert.Equal(HomeSectionIds.All, c.HomeSections.Select(s => s.Id));
        Assert.All(c.HomeSections, s => Assert.True(s.Visible));
        Assert.Equal(160, c.CardPixels);
    }

    [Fact]
    public void MoveSection_UpAndDownClamped()
    {
        var c = ContentSettings.Default();
        c.MoveSection("artists", -1);
        Assert.Equal("artists", c.HomeSections[3].Id);
        c.MoveSection("recent", -1);
        Assert.Equal("recent", c.HomeSections[0].Id);
    }

    [Fact]
    public void Repair_FixesUnknownDuplicatesAndMissing()
    {
        var c = ContentSettings.Default();
        c.HomeSections.Add(new HomeSection { Id = "recent" });
        c.HomeSections.Add(new HomeSection { Id = "podcasts" });
        c.HomeSections.RemoveAll(s => s.Id == "artists");
        c.CardSize = "xxl";
        c.Repair();
        Assert.Equal(HomeSectionIds.All.OrderBy(x => x), c.HomeSections.Select(s => s.Id).OrderBy(x => x));
        Assert.Equal("medium", c.CardSize);
    }

    [Fact]
    public void Json_RoundTripThroughSystemTextJson()
    {
        var c = ContentSettings.Default();
        c.ShowAlbum = false;
        c.MoveSection("artists", -2);
        var back = JsonSerializer.Deserialize<ContentSettings>(JsonSerializer.Serialize(c))!;
        Assert.False(back.ShowAlbum);
        Assert.Equal(c.HomeSections.Select(s => s.Id), back.HomeSections.Select(s => s.Id));
    }
}
