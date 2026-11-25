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

    // ---------------- shelves the user adds ----------------

    [Fact]
    public void AddSection_AppendsAShelfWithItsOwnId()
    {
        var c = ContentSettings.Default();
        var a = c.AddSection(HomeSectionKinds.Custom, "  Chill  ");
        var b = c.AddSection(HomeSectionKinds.Custom);
        Assert.Equal("Chill", a.Title);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(a.Id, c.HomeSections[HomeSectionIds.All.Count].Id);
        Assert.False(a.IsBuiltIn);
        Assert.Equal(HomeSectionKinds.Custom, a.EffectiveKind);
    }

    [Fact]
    public void AddSection_RefusesAKindThatIsNotOffered()
    {
        Assert.Throws<ArgumentException>(() => ContentSettings.Default().AddSection(HomeSectionIds.Recent));
        Assert.Throws<ArgumentException>(() => ContentSettings.Default().AddSection("nonsense"));
    }

    [Fact]
    public void RemoveSection_DropsAddedShelvesButNeverBuiltInOnes()
    {
        var c = ContentSettings.Default();
        var added = c.AddSection(HomeSectionKinds.LocalRecent);
        Assert.False(c.RemoveSection(HomeSectionIds.Recent));
        Assert.True(c.RemoveSection(added.Id));
        Assert.False(c.RemoveSection(added.Id));
        Assert.Equal(HomeSectionIds.All, c.HomeSections.Select(s => s.Id));
    }

    [Fact]
    public void Repair_KeepsAddedShelvesAndTheirPlaylists()
    {
        var c = ContentSettings.Default();
        var shelf = c.AddSection(HomeSectionKinds.Custom, "Mine");
        shelf.Items = ["s:abc", "l:def", "s:abc"];
        shelf.Count = 12;
        c.Repair();
        var back = c.HomeSections.Single(s => s.Id == shelf.Id);
        Assert.Equal(["s:abc", "l:def"], back.Items);
        Assert.Equal("Mine", back.Title);
        Assert.Equal(12, back.Limit);
    }

    [Fact]
    public void Repair_DropsBrokenShelvesAndStrayItems()
    {
        var c = ContentSettings.Default();
        c.HomeSections.Add(new HomeSection { Id = "x1", Kind = "mystery" });
        c.HomeSections.Add(new HomeSection { Id = "", Kind = HomeSectionKinds.Custom });
        c.HomeSections.Add(new HomeSection { Id = "x2", Kind = HomeSectionKinds.Custom, Items = ["s:ok", "junk", "", "l:"], Count = 999, Title = new string('t', 500) });
        c.HomeSections.Add(new HomeSection { Id = "x3", Kind = HomeSectionKinds.LocalAlbums, Items = ["s:left-over"] });
        c.Repair();
        Assert.DoesNotContain(c.HomeSections, s => s.Id is "x1" or "");
        var x2 = c.HomeSections.Single(s => s.Id == "x2");
        Assert.Equal(["s:ok"], x2.Items);
        Assert.Equal(HomeSection.MaxCount, x2.Count);
        Assert.Equal(HomeSection.MaxTitle, x2.Title.Length);
        Assert.Empty(c.HomeSections.Single(s => s.Id == "x3").Items); // only a custom shelf holds playlists
    }

    [Fact]
    public void Limit_DefaultsToTenWhenNoCountIsSet()
    {
        Assert.Equal(10, new HomeSection().Limit);
        Assert.Equal(5, new HomeSection { Count = 5 }.Limit);
    }

    [Fact]
    public void Json_RoundTripKeepsAddedShelves()
    {
        var c = ContentSettings.Default();
        var shelf = c.AddSection(HomeSectionKinds.Custom, "Road trip");
        shelf.Items = ["l:1", "s:2"];
        var back = JsonSerializer.Deserialize<ContentSettings>(JsonSerializer.Serialize(c))!;
        back.Repair();
        var again = back.HomeSections.Single(s => s.Id == shelf.Id);
        Assert.Equal("Road trip", again.Title);
        Assert.Equal(["l:1", "s:2"], again.Items);
        Assert.Equal(HomeSectionKinds.Custom, again.Kind);
    }

    [Fact]
    public void OldSettingsFiles_StillLoad()
    {
        // A file written before shelves could be added has only an id and a visibility per shelf.
        var back = JsonSerializer.Deserialize<ContentSettings>("""{"HomeSections":[{"Id":"artists","Visible":false},{"Id":"recent","Visible":true}]}""")!;
        back.Repair();
        Assert.Equal("artists", back.HomeSections[0].Id);
        Assert.False(back.HomeSections[0].Visible);
        Assert.All(back.HomeSections, s => Assert.True(s.IsBuiltIn));
        Assert.Equal(HomeSectionIds.All.Count, back.HomeSections.Count);
    }
}
