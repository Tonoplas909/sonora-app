namespace Sonora.Appearance.Content;

public static class HomeSectionIds
{
    public const string Recent = "recent";
    public const string Playlists = "playlists";
    public const string SavedAlbums = "savedAlbums";
    public const string Rediscover = "rediscover";
    public const string Artists = "artists";
    public const string NewReleases = "newReleases";
    public const string TopArtists = "topArtists";
    public const string Shows = "shows";

    public static IReadOnlyList<string> All { get; } = [Recent, Playlists, SavedAlbums, Rediscover, Artists, NewReleases, TopArtists, Shows];
}

public sealed class HomeSection
{
    public string Id { get; set; } = "";
    public bool Visible { get; set; } = true;
}

/// <summary>What the user chose to show: home sections (order, visibility), track list columns, card size.</summary>
public sealed class ContentSettings
{
    public static readonly string[] CardSizes = ["small", "medium", "large"];

    public List<HomeSection> HomeSections { get; set; } = [];
    public bool ShowNumber { get; set; } = true;
    public bool ShowArt { get; set; } = true;
    public bool ShowAlbum { get; set; } = true;
    public bool ShowLike { get; set; } = true;
    public bool ShowDuration { get; set; } = true;
    public string CardSize { get; set; } = "medium";

    /// <summary>Library panel showing covers only, whatever its width.</summary>
    public bool LibraryCompact { get; set; }

    public double CardPixels => CardSize switch { "small" => 128, "large" => 200, _ => 160 };

    public static ContentSettings Default() => new()
    {
        HomeSections = HomeSectionIds.All.Select(id => new HomeSection { Id = id }).ToList(),
    };

    public void MoveSection(string id, int delta)
    {
        var i = HomeSections.FindIndex(s => s.Id == id);
        if (i < 0) return;
        var j = Math.Clamp(i + delta, 0, HomeSections.Count - 1);
        if (i == j) return;
        var s = HomeSections[i];
        HomeSections.RemoveAt(i);
        HomeSections.Insert(j, s);
    }

    public void Repair()
    {
        var seen = new HashSet<string>();
        HomeSections = (HomeSections ?? []).Where(s => s != null && HomeSectionIds.All.Contains(s.Id) && seen.Add(s.Id)).ToList();
        foreach (var id in HomeSectionIds.All)
            if (!seen.Contains(id)) HomeSections.Add(new HomeSection { Id = id });
        if (!CardSizes.Contains(CardSize)) CardSize = "medium";
    }
}
