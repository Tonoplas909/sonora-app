using System.Globalization;

namespace Sonora.Appearance.Localization;

/// <summary>
/// Looks texts up in one language, and in English when that language does not have them (yet): a half-finished
/// translation shows English where it has gaps, never an empty label. A key found nowhere comes back as itself,
/// which makes a missing text easy to spot on screen.
/// </summary>
public sealed class Localizer
{
    readonly LanguageFile _file;
    readonly LanguageFile? _fallback;

    public Localizer(LanguageFile file, LanguageFile? fallback = null)
    {
        _file = file;
        _fallback = ReferenceEquals(file, fallback) ? null : fallback;
        Culture = file.Culture;
    }

    public LanguageInfo Info => _file.Info;

    /// <summary>How numbers and dates are written in this language.</summary>
    public CultureInfo Culture { get; }

    LocEntry? Find(string key) =>
        _file.Strings.TryGetValue(key, out var e) ? e
        : _fallback != null && _fallback.Strings.TryGetValue(key, out var f) ? f
        : null;

    public bool Has(string key) => Find(key) != null;

    /// <summary>A text. For a counted text, use <see cref="Plural"/>.</summary>
    public string T(string key)
    {
        var entry = Find(key);
        if (entry == null) return key;
        return entry.Text ?? entry.Forms!.GetValueOrDefault(PluralRules.Other) ?? key;
    }

    /// <summary>A text with {0}, {1}... filled in with <paramref name="args"/>, formatted the way this language writes numbers.</summary>
    public string T(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>
    /// A counted text in the right plural form for <paramref name="n"/>; {0} is the number and {1}... are
    /// <paramref name="more"/>. ("library.tracks", 1) is "1 track" and 2 is "2 tracks".
    /// </summary>
    public string Plural(string key, long n, params object?[] more)
    {
        var entry = Find(key);
        string template;
        if (entry == null) template = key;
        else if (entry.Forms == null) template = entry.Text ?? key;
        else
        {
            var category = PluralRules.Category(_file.Info.Code, n);
            template = entry.Forms.GetValueOrDefault(category) ?? entry.Forms.GetValueOrDefault(PluralRules.Other) ?? key;
        }
        var args = new object?[more.Length + 1];
        args[0] = n;
        more.CopyTo(args, 1);
        return string.Format(Culture, template, args);
    }
}
