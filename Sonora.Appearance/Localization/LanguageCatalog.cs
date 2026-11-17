namespace Sonora.Appearance.Localization;

/// <summary>The languages that come with Sonora: JSON files embedded in the assembly (Localization/Languages/*.json).</summary>
public static class LanguageCatalog
{
    /// <summary>The language every other one falls back on for the texts it does not have yet.</summary>
    public const string Fallback = "en";

    /// <summary>The setting that means "the language of Windows".</summary>
    public const string Auto = "auto";

    /// <summary>The languages, in the order the language list shows them.</summary>
    public static readonly string[] Codes = ["en", "fr", "es", "de", "it", "pt", "nl", "sv", "da", "nb", "fi", "is"];

    static readonly Dictionary<string, LanguageFile> Cache = new(StringComparer.Ordinal);

    /// <summary>Reads a language file from the assembly. Throws for a code that is not one of <see cref="Codes"/>.</summary>
    public static LanguageFile Load(string code)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(code, out var cached)) return cached;
            var asm = typeof(LanguageCatalog).Assembly;
            using var s = asm.GetManifestResourceStream($"Sonora.Appearance.Localization.Languages.{code}.json")
                          ?? throw new InvalidOperationException($"Language file not found: {code}");
            using var reader = new StreamReader(s);
            return Cache[code] = LanguageFile.Parse(reader.ReadToEnd());
        }
    }

    public static bool IsSupported(string? code) => code != null && Array.IndexOf(Codes, code) >= 0;

    /// <summary>Every language with its name and whether a native speaker has reviewed it.</summary>
    public static IReadOnlyList<LanguageInfo> All() => Codes.Select(c => Load(c).Info).ToList();
}

/// <summary>Picks the language to start in.</summary>
public static class LanguageResolver
{
    /// <summary>
    /// The language to use: the one the user chose, or when that is "auto" (or unknown) the first language of
    /// Windows that Sonora speaks, or English.
    /// </summary>
    public static string Resolve(string? setting, IEnumerable<string> windowsLanguages)
    {
        if (setting != null && setting != LanguageCatalog.Auto && LanguageCatalog.IsSupported(setting)) return setting;
        return Pick(windowsLanguages);
    }

    /// <summary>The first of the language tags ("fr-CA", "nb-NO", "pt-BR"...) that Sonora speaks, else English.</summary>
    public static string Pick(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            var primary = (tag ?? "").Trim().ToLowerInvariant().Split('-', '_')[0];
            // Norwegian has two written forms; the one translated here is Bokmål, which "no" and "nn" readers also read.
            if (primary is "no" or "nn") primary = "nb";
            if (LanguageCatalog.IsSupported(primary)) return primary;
        }
        return LanguageCatalog.Fallback;
    }
}
