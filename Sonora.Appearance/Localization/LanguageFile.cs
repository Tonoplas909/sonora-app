using System.Globalization;
using System.Text.Json;

namespace Sonora.Appearance.Localization;

/// <summary>One language Sonora can speak.</summary>
/// <param name="Code">Short code, also the file name: "en", "fr", "nb"...</param>
/// <param name="Name">The language's name in itself ("Deutsch"), shown in the language list.</param>
/// <param name="Culture">The .NET culture used to format numbers and dates ("de-DE").</param>
/// <param name="Reviewed">True once a native speaker has read the translation. The language list marks the others.</param>
public sealed record LanguageInfo(string Code, string Name, string Culture, bool Reviewed);

/// <summary>A piece of text: one sentence, or the same sentence in each plural form ("one", "other").</summary>
public sealed record LocEntry(string? Text, IReadOnlyDictionary<string, string>? Forms)
{
    public bool IsPlural => Forms != null;

    /// <summary>Every form of the entry (just the text for a plain one), for checks that look at all of them.</summary>
    public IEnumerable<string> AllTexts => Forms?.Values ?? (Text is { } t ? [t] : []);
}

/// <summary>
/// A language file: who it is and its texts. The file is plain JSON so anyone can edit it:
/// <code>
/// { "language": "fr", "name": "Français", "culture": "fr-FR", "reviewed": true,
///   "strings": { "nav.home": "Accueil", "library.tracks": { "one": "{0} titre", "other": "{0} titres" } } }
/// </code>
/// </summary>
public sealed class LanguageFile
{
    public required LanguageInfo Info { get; init; }
    public required IReadOnlyDictionary<string, LocEntry> Strings { get; init; }

    public static LanguageFile Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : throw new FormatException($"Language file: missing \"{name}\".");

        var strings = new Dictionary<string, LocEntry>(StringComparer.Ordinal);
        if (root.TryGetProperty("strings", out var list) && list.ValueKind == JsonValueKind.Object)
            foreach (var p in list.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                    strings[p.Name] = new LocEntry(p.Value.GetString(), null);
                else if (p.Value.ValueKind == JsonValueKind.Object)
                    strings[p.Name] = new LocEntry(null, p.Value.EnumerateObject()
                        .Where(f => f.Value.ValueKind == JsonValueKind.String)
                        .ToDictionary(f => f.Name, f => f.Value.GetString() ?? "", StringComparer.Ordinal));
                else
                    throw new FormatException($"Language file: \"{p.Name}\" must be a text or an object of plural forms.");
            }

        var reviewed = root.TryGetProperty("reviewed", out var r) && r.ValueKind == JsonValueKind.True;
        return new LanguageFile
        {
            Info = new LanguageInfo(Str("language"), Str("name"), Str("culture"), reviewed),
            Strings = strings,
        };
    }

    /// <summary>The culture to format numbers and dates with, or the invariant one if the name is unknown to this system.</summary>
    public CultureInfo Culture
    {
        get
        {
            try { return CultureInfo.GetCultureInfo(Info.Culture); }
            catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
        }
    }
}
