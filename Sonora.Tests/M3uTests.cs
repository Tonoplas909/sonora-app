using Sonora.Appearance.Features;

namespace Sonora.Tests;

public class M3uTests
{
    [Fact]
    public void Write_StartsWithTheHeaderAndPairsEachPathWithItsInfoLine()
    {
        var text = M3u.Write([new M3uEntry(@"C:\Music\a.mp3", "Titre", "Artiste", 85.4)]);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(M3u.Header, lines[0]);
        Assert.Equal("#EXTINF:85,Artiste - Titre", lines[1]);
        Assert.Equal(@"C:\Music\a.mp3", lines[2]);
    }

    [Fact]
    public void Write_UnknownDurationIsMinusOneAndAnEmptyArtistLeavesJustTheTitle()
    {
        var text = M3u.Write([new M3uEntry(@"C:\Music\a.mp3", "Titre", "", 0)]);
        Assert.Contains("#EXTINF:-1,Titre\n", text);
    }

    [Fact]
    public void Write_SkipsEntriesWithNoPath()
    {
        var text = M3u.Write([new M3uEntry("", "Titre", "Artiste", 10), new M3uEntry(@"C:\b.mp3", "B", "A", 10)]);
        Assert.Single(M3u.Parse(text));
    }

    [Fact]
    public void Write_TitleSpanningLinesWouldSplitTheEntry()
    {
        var text = M3u.Write([new M3uEntry(@"C:\a.mp3", "Deux\nlignes", "Artiste", 10)]);
        Assert.Contains("#EXTINF:10,Artiste - Deux lignes\n", text);
        Assert.Single(M3u.Parse(text));
    }

    [Fact]
    public void Parse_IgnoresCommentsDirectivesAndBlankLines()
    {
        var content = "#EXTM3U\n\n#EXTINF:12,A - B\nC:\\Music\\a.mp3\r\n# a comment\nC:\\Music\\b.mp3\n";
        Assert.Equal([@"C:\Music\a.mp3", @"C:\Music\b.mp3"], M3u.Parse(content));
    }

    [Fact]
    public void Parse_ResolvesRelativePathsAgainstThePlaylistFolder()
    {
        var paths = M3u.Parse("sub/a.mp3\n", @"C:\Music");
        Assert.Equal(@"C:\Music\sub\a.mp3", paths[0]);
    }

    [Fact]
    public void Parse_LeavesRootedPathsAndUrlsAlone()
    {
        var paths = M3u.Parse("C:\\Elsewhere\\a.mp3\nhttps://example.com/b.mp3\n", @"C:\Music");
        Assert.Equal(@"C:\Elsewhere\a.mp3", paths[0]);
        Assert.Equal("https://example.com/b.mp3", paths[1]);
    }

    [Fact]
    public void RoundTrip_KeepsEveryPathInOrder()
    {
        List<M3uEntry> entries =
        [
            new(@"C:\Music\a.mp3", "A", "Artiste", 100),
            new(@"C:\Music\b.mp3", "B", "Artiste", 200),
        ];
        Assert.Equal(entries.Select(e => e.Path), M3u.Parse(M3u.Write(entries)));
    }

    [Theory]
    [InlineData(@"C:\Music\Mes titres.m3u8", "Mes titres")]
    [InlineData("rap.m3u", "rap")]
    [InlineData("", "Imported playlist")]
    public void NameFromFile_UsesTheFileNameWithoutItsExtension(string file, string expected) =>
        Assert.Equal(expected, M3u.NameFromFile(file));
}
