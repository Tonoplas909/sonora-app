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

/// <summary>
/// The kinds of shelf a user can add to the home page on top of the built-in ones. The built-in shelves keep
/// their own id as their kind; an added shelf has a generated id and one of these kinds.
/// </summary>
public static class HomeSectionKinds
{
    /// <summary>Playlists picked by hand, Spotify and local ones mixed.</summary>
    public const string Custom = "custom";
    public const string LocalRecent = "localRecent";
    public const string LocalAlbums = "localAlbums";
    public const string LocalArtists = "localArtists";
    public const string LocalPlaylists = "localPlaylists";

    /// <summary>What the "add a shelf" menu offers. Everything but <see cref="Custom"/> reads the local library only.</summary>
    public static IReadOnlyList<string> Addable { get; } = [Custom, LocalRecent, LocalAlbums, LocalArtists, LocalPlaylists];

    public static bool IsLocal(string kind) => kind is LocalRecent or LocalAlbums or LocalArtists or LocalPlaylists;
}

public sealed class HomeSection
{
    public const int DefaultCount = 10;
    public const int MaxCount = 50;
    public const int MaxTitle = 80;

    public string Id { get; set; } = "";
    public bool Visible { get; set; } = true;

    /// <summary>The kind of an added shelf; empty for a built-in one, whose id is its kind.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The user's own title; empty keeps the default one of the kind.</summary>
    public string Title { get; set; } = "";

    /// <summary>How many cards the shelf shows; 0 keeps <see cref="DefaultCount"/>.</summary>
    public int Count { get; set; }

    /// <summary>For a <see cref="HomeSectionKinds.Custom"/> shelf: the playlists, in order, as "s:&lt;id&gt;" (Spotify) or "l:&lt;id&gt;" (local).</summary>
    public List<string> Items { get; set; } = [];

    public bool IsBuiltIn => HomeSectionIds.All.Contains(Id);

    /// <summary>What the shelf shows: its own id for a built-in shelf, otherwise its kind.</summary>
    public string EffectiveKind => IsBuiltIn ? Id : Kind;

    public int Limit => Count > 0 ? Math.Min(Count, MaxCount) : DefaultCount;
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

    /// <summary>Adds a shelf of an addable kind at the end of the home page.</summary>
    public HomeSection AddSection(string kind, string title = "")
    {
        if (!HomeSectionKinds.Addable.Contains(kind)) throw new ArgumentException("Unknown shelf kind: " + kind, nameof(kind));
        var section = new HomeSection { Id = Guid.NewGuid().ToString("N"), Kind = kind, Title = title.Trim() };
        HomeSections.Add(section);
        return section;
    }

    /// <summary>Removes an added shelf. The built-in ones can only be hidden, so a settings file can never lose them.</summary>
    public bool RemoveSection(string id)
    {
        var section = HomeSections.FirstOrDefault(s => s.Id == id);
        return section is { IsBuiltIn: false } && HomeSections.Remove(section);
    }

    public void Repair()
    {
        var seen = new HashSet<string>();
        HomeSections = (HomeSections ?? []).Where(s => s != null && !string.IsNullOrEmpty(s.Id) && seen.Add(s.Id)
                                                       && (s.IsBuiltIn || HomeSectionKinds.Addable.Contains(s.Kind ?? ""))).ToList();
        foreach (var s in HomeSections)
        {
            s.Title = (s.Title ?? "").Trim();
            if (s.Title.Length > HomeSection.MaxTitle) s.Title = s.Title[..HomeSection.MaxTitle];
            s.Count = Math.Clamp(s.Count, 0, HomeSection.MaxCount);
            s.Kind ??= "";
            s.Items = (s.Items ?? []).Where(i => i is { Length: > 2 } && (i.StartsWith("s:") || i.StartsWith("l:"))).Distinct().ToList();
            if (s.EffectiveKind != HomeSectionKinds.Custom) s.Items = [];
        }
        foreach (var id in HomeSectionIds.All)
            if (!seen.Contains(id)) HomeSections.Add(new HomeSection { Id = id });
        if (!CardSizes.Contains(CardSize)) CardSize = "medium";
    }
}
