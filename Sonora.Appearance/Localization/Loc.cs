using System.Globalization;

namespace Sonora.Appearance.Localization;

/// <summary>
/// The language Sonora is speaking, reachable from anywhere: <c>Loc.T("nav.home")</c>. Switching language
/// raises <see cref="LanguageChanged"/>, which the interface listens to so everything is redrawn at once.
/// </summary>
public static class Loc
{
    static Localizer _current = Create(LanguageCatalog.Fallback);

    public static Localizer Current => _current;

    /// <summary>The code of the language in use ("fr").</summary>
    public static string Code => _current.Info.Code;

    /// <summary>How numbers and dates are written in the language in use.</summary>
    public static CultureInfo Culture => _current.Culture;

    /// <summary>Raised after <see cref="SetLanguage"/> has switched, on the thread that called it.</summary>
    public static event Action? LanguageChanged;

    static Localizer Create(string code) =>
        new(LanguageCatalog.Load(code), code == LanguageCatalog.Fallback ? null : LanguageCatalog.Load(LanguageCatalog.Fallback));

    /// <summary>Switches to a language (a code from <see cref="LanguageCatalog.Codes"/>; anything else means English).</summary>
    public static void SetLanguage(string code)
    {
        if (!LanguageCatalog.IsSupported(code)) code = LanguageCatalog.Fallback;
        if (code == _current.Info.Code) return;
        _current = Create(code);
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// True when <paramref name="text"/> is what <paramref name="key"/> says in any language. For a name that was saved
    /// in one language and has to be recognised in another ("All tracks" saved by a user who later switched to French).
    /// </summary>
    public static bool MatchesAnyLanguage(string key, string? text)
    {
        if (text == null) return false;
        foreach (var code in LanguageCatalog.Codes)
            if (LanguageCatalog.Load(code).Strings.TryGetValue(key, out var e) && e.Text == text) return true;
        return false;
    }

    public static string T(string key) => _current.T(key);

    public static string T(string key, params object?[] args) => _current.T(key, args);

    /// <summary>A counted text in the right plural form: <c>Loc.Count("library.tracks", 3)</c>. {0} is the number.</summary>
    public static string Count(string key, long n, params object?[] more) => _current.Plural(key, n, more);
}
