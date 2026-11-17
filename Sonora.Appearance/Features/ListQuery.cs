using System.Globalization;
using System.Text;

namespace Sonora.Appearance.Features;

/// <summary>Text filter and sort shared by every track list.</summary>
public static class ListQuery
{
    public static readonly string[] SortKeys = ["default", "title", "artist", "album", "duration", "added"];

    /// <summary>Lower-case, accent-free form used for matching.</summary>
    public static string Fold(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    /// Keeps the items whose title, artist or album contain every word of <paramref name="filter"/>, then sorts them.
    /// "default" (or an unknown key) keeps the original order. "added" orders by <paramref name="added"/>, any
    /// increasing number (a date's ticks, or a position); without it the key does nothing.
    /// </summary>
    public static List<T> Apply<T>(IReadOnlyList<T> items, string? filter, string sortKey, bool descending,
        Func<T, string> title, Func<T, string> artist, Func<T, string> album, Func<T, double> duration,
        Func<T, long>? added = null)
    {
        var tokens = Fold(filter).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<T> query = items;
        if (tokens.Length > 0)
            query = query.Where(i =>
            {
                var key = Fold($"{title(i)} {artist(i)} {album(i)}");
                return tokens.All(t => key.Contains(t, StringComparison.Ordinal));
            });

        var cmp = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
        IOrderedEnumerable<T>? ordered = sortKey switch
        {
            "title" => descending ? query.OrderByDescending(title, cmp) : query.OrderBy(title, cmp),
            "artist" => descending ? query.OrderByDescending(artist, cmp) : query.OrderBy(artist, cmp),
            "album" => descending ? query.OrderByDescending(album, cmp) : query.OrderBy(album, cmp),
            "duration" => descending ? query.OrderByDescending(duration) : query.OrderBy(duration),
            "added" when added != null => descending ? query.OrderByDescending(added) : query.OrderBy(added),
            _ => null,
        };
        return (ordered ?? query).ToList();
    }
}
