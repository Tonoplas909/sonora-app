using System.Text.Json;

namespace Sonora.Appearance.Features;

/// <summary>What ListenBrainz said about a user token.</summary>
public sealed record LbTokenCheck(bool Valid, string UserName);

/// <summary>An artist or a recording with how many times its owner listened to it over a period.</summary>
public sealed record LbRanked(string Name, string Artist, int Listens);

/// <summary>Listens in one slice (a day, a month…) of the period asked for.</summary>
public sealed record LbActivity(DateTime FromUtc, int Listens);

/// <summary>
/// Reading the answers of the ListenBrainz statistics API. Kept apart from the HTTP calls so the parsing,
/// the part that breaks when a field goes missing, can be tested without a network. Every reader treats an
/// empty or odd answer as "nothing": ListenBrainz answers 204 with no body while it has not computed a
/// user's statistics yet, and that is an ordinary state, not an error.
/// </summary>
public static class ListenBrainzApi
{
    /// <summary>The periods the statistics endpoints accept that a page can offer, shortest first.</summary>
    public static readonly string[] Ranges = ["this_week", "this_month", "this_year", "all_time"];

    public static bool IsKnownRange(string? range) => range != null && Ranges.Contains(range);

    /// <summary>The token endpoint answers 200 even for a bad token; only <c>valid</c> says which it was.</summary>
    public static LbTokenCheck ParseToken(string? json)
    {
        if (!TryRoot(json, out var root)) return new(false, "");
        var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        var name = valid ? Text(root, "user_name") : "";
        // A "valid" token with no user name could not be used to read anything, so it counts as unusable.
        return new(valid && name.Length > 0, name);
    }

    public static List<LbRanked> ParseArtists(string? json) =>
        Payload(json, "artists", item => new LbRanked(Text(item, "artist_name"), "", Count(item, "listen_count")));

    public static List<LbRanked> ParseRecordings(string? json) =>
        Payload(json, "recordings", item => new LbRanked(Text(item, "track_name"), Text(item, "artist_name"), Count(item, "listen_count")));

    public static List<LbActivity> ParseActivity(string? json)
    {
        var list = new List<LbActivity>();
        if (!TryRoot(json, out var root) || !Array(root, "listening_activity", out var items)) return list;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("from_ts", out var ts) || ts.ValueKind != JsonValueKind.Number || !ts.TryGetInt64(out var seconds))
                continue;
            list.Add(new LbActivity(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime, Count(item, "listen_count")));
        }
        return list;
    }

    /// <summary>The user's total number of listens, or -1 when the answer carries none.</summary>
    public static long ParseListenCount(string? json)
    {
        if (!TryRoot(json, out var root) || !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            return -1;
        return payload.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var n) ? n : -1;
    }

    static List<LbRanked> Payload(string? json, string array, Func<JsonElement, LbRanked> read)
    {
        var list = new List<LbRanked>();
        if (!TryRoot(json, out var root) || !Array(root, array, out var items)) return list;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var row = read(item);
            if (row.Name.Length > 0) list.Add(row);
        }
        return list;
    }

    static bool Array(JsonElement root, string name, out JsonElement items)
    {
        items = default;
        return root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object
               && payload.TryGetProperty(name, out items) && items.ValueKind == JsonValueKind.Array;
    }

    static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static int Count(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    static bool TryRoot(string? json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            // The document is deliberately not disposed: the elements are read right after, and a cloned
            // root outlives it.
            root = JsonDocument.Parse(json).RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
