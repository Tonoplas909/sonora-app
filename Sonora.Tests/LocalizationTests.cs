using System.Text.RegularExpressions;
using Sonora.Appearance.Localization;

namespace Sonora.Tests;

/// <summary>
/// The language files are data that humans edit, so a typo can break the interface in ways the compiler never sees:
/// a missing key, a lost "{0}", a plural form gone. These tests keep the twelve files consistent with each other and
/// with the keys the sources use. They never change the global language (tests run in parallel): they build their own
/// <see cref="Localizer"/>.
/// </summary>
public class LocalizationTests
{
    static readonly Regex Placeholder = new(@"\{\d+(?::[^}]*)?\}", RegexOptions.Compiled);

    public static IEnumerable<object[]> Codes => LanguageCatalog.Codes.Select(c => new object[] { c });

    static LanguageFile English => LanguageCatalog.Load("en");

    static string[] Placeholders(string text) => Placeholder.Matches(text).Select(m => m.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();

    // ---------------- the files themselves

    [Theory, MemberData(nameof(Codes))]
    public void EveryLanguage_HasExactlyTheKeysOfEnglish(string code)
    {
        var file = LanguageCatalog.Load(code);
        var missing = English.Strings.Keys.Except(file.Strings.Keys).OrderBy(k => k).ToList();
        var extra = file.Strings.Keys.Except(English.Strings.Keys).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, $"{code}: missing keys: {string.Join(", ", missing.Take(10))}");
        Assert.True(extra.Count == 0, $"{code}: keys English does not have: {string.Join(", ", extra.Take(10))}");
    }

    [Theory, MemberData(nameof(Codes))]
    public void EveryLanguage_KeepsThePlaceholdersOfEnglish(string code)
    {
        var file = LanguageCatalog.Load(code);
        var bad = new List<string>();
        foreach (var (key, reference) in English.Strings)
        {
            if (!file.Strings.TryGetValue(key, out var entry)) continue;
            var want = Placeholders(reference.AllTexts.Last()); // "other" form of a plural, or the text itself
            foreach (var text in entry.AllTexts)
                if (!Placeholders(text).SequenceEqual(want))
                    bad.Add($"{key}: «{text}» should contain {string.Join(" ", want)}");
        }
        Assert.True(bad.Count == 0, $"{code}:\n" + string.Join("\n", bad.Take(10)));
    }

    [Theory, MemberData(nameof(Codes))]
    public void EveryLanguage_HasPluralFormsExactlyWhereEnglishHasThem(string code)
    {
        var file = LanguageCatalog.Load(code);
        var bad = new List<string>();
        foreach (var (key, reference) in English.Strings)
        {
            if (!file.Strings.TryGetValue(key, out var entry)) continue;
            if (reference.IsPlural != entry.IsPlural) { bad.Add($"{key}: plural-ness differs from English"); continue; }
            if (entry.IsPlural && (!entry.Forms!.ContainsKey(PluralRules.One) || !entry.Forms.ContainsKey(PluralRules.Other)))
                bad.Add($"{key}: needs both \"one\" and \"other\"");
        }
        Assert.True(bad.Count == 0, $"{code}:\n" + string.Join("\n", bad.Take(10)));
    }

    [Theory, MemberData(nameof(Codes))]
    public void NoTextIsEmpty_ExceptWhereEnglishIsEmpty(string code)
    {
        var file = LanguageCatalog.Load(code);
        var empty = file.Strings.Where(p => English.Strings.TryGetValue(p.Key, out var e)
                                            && e.AllTexts.All(t => t.Trim().Length > 0)
                                            && p.Value.AllTexts.Any(t => t.Trim().Length == 0))
                                .Select(p => p.Key).ToList();
        Assert.True(empty.Count == 0, $"{code}: empty texts: {string.Join(", ", empty.Take(10))}");
    }

    [Theory, MemberData(nameof(Codes))]
    public void TheFileDescribesItself(string code)
    {
        var info = LanguageCatalog.Load(code).Info;
        Assert.Equal(code, info.Code);
        Assert.False(string.IsNullOrWhiteSpace(info.Name));
        Assert.Contains("-", info.Culture);
        Assert.NotEqual(System.Globalization.CultureInfo.InvariantCulture, new Localizer(LanguageCatalog.Load(code)).Culture);
    }

    [Fact]
    public void KeysUseOnlyLettersDigitsDotsUnderscoresAndHyphens()
    {
        // The {loc:T key} markup extension reads the key up to the closing brace, so it must not need escaping.
        var bad = English.Strings.Keys.Where(k => !Regex.IsMatch(k, @"^[A-Za-z0-9_.\-]+$")).ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void FrenchIsTheOnlyLanguageMarkedAsReviewed()
    {
        // Honest labels: "reviewed" is set by hand, only once a native speaker has read the whole file.
        var reviewed = LanguageCatalog.All().Where(l => l.Reviewed).Select(l => l.Code).ToList();
        Assert.Equal(["fr"], reviewed);
    }

    [Fact]
    public void TheCatalogListsEveryFileAndOnlyThose()
    {
        Assert.Equal(12, LanguageCatalog.Codes.Length);
        Assert.Equal(LanguageCatalog.Codes.Length, LanguageCatalog.Codes.Distinct().Count());
        Assert.Equal(LanguageCatalog.Codes, LanguageCatalog.All().Select(l => l.Code));
        var asm = typeof(LanguageCatalog).Assembly;
        var resources = asm.GetManifestResourceNames().Where(n => n.StartsWith("Sonora.Appearance.Localization.Languages.")).Count();
        Assert.Equal(LanguageCatalog.Codes.Length, resources);
    }

    // ---------------- the Localizer

    [Fact]
    public void Localizer_FallsBackOnEnglish_ThenOnTheKey()
    {
        var partial = LanguageFile.Parse("""{"language":"xx","name":"Test","culture":"fr-FR","strings":{"a":"A in xx"}}""");
        var loc = new Localizer(partial, English);
        Assert.Equal("A in xx", loc.T("a"));
        Assert.Equal(English.Strings["common.save"].Text, loc.T("common.save"));
        Assert.Equal("no.such.key", loc.T("no.such.key"));
        Assert.True(loc.Has("common.save"));
        Assert.False(loc.Has("no.such.key"));
    }

    [Fact]
    public void Localizer_FormatsPluralsAndArguments()
    {
        var en = new Localizer(English);
        Assert.Equal("1 track", en.Plural("count.tracks", 1));
        Assert.Equal("2 tracks", en.Plural("count.tracks", 2));
        var fr = new Localizer(LanguageCatalog.Load("fr"), English);
        Assert.Equal("0 titre", fr.Plural("count.tracks", 0));
        Assert.Equal("1 titre", fr.Plural("count.tracks", 1));
        Assert.Equal("2 titres", fr.Plural("count.tracks", 2));
        Assert.Equal("Signed in as Bob", en.T("spotify.connected_as", "Bob"));
    }

    [Theory]
    [InlineData("en", 1, "one")]
    [InlineData("en", 0, "other")]
    [InlineData("en", 2, "other")]
    [InlineData("fr", 0, "one")]
    [InlineData("fr", 1, "one")]
    [InlineData("fr", 2, "other")]
    [InlineData("pt", 0, "one")]
    [InlineData("pt", 5, "other")]
    [InlineData("is", 1, "one")]
    [InlineData("is", 21, "one")]
    [InlineData("is", 11, "other")]
    [InlineData("is", 12, "other")]
    [InlineData("de", 1, "one")]
    [InlineData("fi", 1, "one")]
    [InlineData("fi", 2, "other")]
    [InlineData("nb", -1, "one")]
    public void PluralRules_FollowTheLanguage(string language, long n, string expected) =>
        Assert.Equal(expected, PluralRules.Category(language, n));

    // ---------------- picking the language

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("de", "de")]
    [InlineData("auto", "en")]
    [InlineData(null, "en")]
    [InlineData("klingon", "en")]
    public void Resolver_ChosenLanguageWinsOverWindows(string? setting, string expected) =>
        Assert.Equal(expected, LanguageResolver.Resolve(setting, ["en-US"]));

    [Theory]
    [InlineData("fr-CA", "fr")]
    [InlineData("pt-BR", "pt")]
    [InlineData("nb-NO", "nb")]
    [InlineData("no", "nb")]
    [InlineData("nn-NO", "nb")]
    [InlineData("is-IS", "is")]
    [InlineData("ja-JP", "en")]
    [InlineData("", "en")]
    public void Resolver_AutoPicksTheFirstLanguageSonoraSpeaks(string tag, string expected) =>
        Assert.Equal(expected, LanguageResolver.Resolve("auto", [tag]));

    [Fact]
    public void Resolver_SkipsUnsupportedWindowsLanguagesInOrder() =>
        Assert.Equal("sv", LanguageResolver.Resolve("auto", ["ja-JP", "sv-SE", "fr-FR"]));

    // ---------------- the keys the sources use

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Sonora.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Sonora.slnx not found above " + AppContext.BaseDirectory);
    }

    static IEnumerable<string> SourceFiles(params string[] patterns)
    {
        var root = RepoRoot();
        foreach (var project in new[] { "Sonora", "Sonora.Appearance" })
            foreach (var pattern in patterns)
                foreach (var f in Directory.EnumerateFiles(Path.Combine(root, project), pattern, SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, f);
                    if (rel.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                        rel.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
                    yield return f;
                }
    }

    [Fact]
    public void EveryKeyUsedInXamlExists()
    {
        var rx = new Regex(@"\{loc:T\s+([^}\s]+)\s*\}");
        var missing = new List<string>();
        foreach (var file in SourceFiles("*.xaml"))
            foreach (Match m in rx.Matches(File.ReadAllText(file)))
                if (!English.Strings.ContainsKey(m.Groups[1].Value))
                    missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
        Assert.True(missing.Count == 0, "Keys used in XAML but not in en.json:\n" + string.Join("\n", missing.Distinct().Take(20)));
    }

    [Fact]
    public void EveryKeyUsedInCodeExists()
    {
        // A literal key, followed by "," or ")": a key built with "+" is checked by its own test below. Doc comments are skipped.
        var rx = new Regex(@"Loc\.(?:T|Count|Plural)\(\s*""([^""]+)""\s*[,)]");
        var missing = new List<string>();
        foreach (var file in SourceFiles("*.cs"))
            foreach (var line in File.ReadLines(file))
            {
                if (line.TrimStart().StartsWith("//")) continue;
                foreach (Match m in rx.Matches(line))
                    if (!English.Strings.ContainsKey(m.Groups[1].Value))
                        missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
            }
        Assert.True(missing.Count == 0, "Keys used in code but not in en.json:\n" + string.Join("\n", missing.Distinct().Take(20)));
    }

    [Fact]
    public void EveryKeyBuiltFromAnIdExists()
    {
        var missing = new List<string>();
        foreach (var a in Sonora.Appearance.Features.Shortcuts.Actions) Need("shortcut." + a.Id, missing);
        foreach (var s in Sonora.Appearance.Theming.ThemeStyles.All) Need("style." + s.Id + ".description", missing);
        foreach (var id in Sonora.Appearance.Theming.ThemeCatalog.BuiltInIds) Need("palette." + id, missing);
        foreach (var k in new[] { "default", "title", "artist", "album", "duration", "added" }) Need("sort." + k, missing);
        Assert.True(missing.Count == 0, "Missing keys: " + string.Join(", ", missing));

        static void Need(string key, List<string> missing)
        {
            if (!English.Strings.ContainsKey(key)) missing.Add(key);
        }
    }

    [Fact]
    public void TheAppsOwnSourcesAreWritten_NotLeftInFrench()
    {
        // The French texts live in fr.json now. A French sentence left in a XAML attribute would show up in every language.
        var accented = new Regex(@"(Text|Content|Header|ToolTip|Title)=""[^""{]*[éèêàùçôî][^""]*""");
        var found = new List<string>();
        foreach (var file in SourceFiles("*.xaml"))
            foreach (Match m in accented.Matches(File.ReadAllText(file)))
                found.Add($"{Path.GetFileName(file)}: {m.Value}");
        Assert.True(found.Count == 0, "Literal accented text left in XAML:\n" + string.Join("\n", found.Take(10)));
    }
}
