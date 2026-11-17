namespace Sonora.Appearance.Features;

/// <summary>
/// The places the library panel can show music from. A source is only an id the entries carry: the panel offers a tab
/// for each one that is available, and filters its entries by it. A new provider (Apple Music…) adds its id here, tags
/// its own entries with it and says when it is available; nothing else in the tabs changes.
/// </summary>
public static class LibrarySources
{
    /// <summary>Not a source: the tab that shows every one of them.</summary>
    public const string All = "all";
    public const string Local = "local";
    public const string Spotify = "spotify";

    /// <summary>The tab to show: <paramref name="selected"/> if it is still on offer (a provider can be signed out), else "all".</summary>
    public static string Resolve(string? selected, IEnumerable<string> available) =>
        selected != null && selected != All && available.Contains(selected) ? selected : All;

    /// <summary>True when an entry of <paramref name="source"/> belongs on the <paramref name="selected"/> tab.</summary>
    public static bool Shows(string selected, string source) => selected == All || selected == source;
}
