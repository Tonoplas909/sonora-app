namespace Sonora.Appearance.Localization;

/// <summary>
/// Which plural form a number takes. Every language Sonora ships needs only "one" and "other" (the rules are
/// the CLDR ones, for whole numbers), so a language file gives those two forms of a counted text.
/// </summary>
public static class PluralRules
{
    public const string One = "one";
    public const string Other = "other";

    public static string Category(string language, long n)
    {
        n = Math.Abs(n);
        return language switch
        {
            // French and Portuguese: zero counts as singular too ("0 titre").
            "fr" or "pt" => n is 0 or 1 ? One : Other,
            // Icelandic: what ends in 1 is singular, except 11 ("21 lag", "11 lög").
            "is" => n % 10 == 1 && n % 100 != 11 ? One : Other,
            // English, Spanish, German, Italian, Dutch, Swedish, Danish, Norwegian, Finnish.
            _ => n == 1 ? One : Other,
        };
    }
}
