using Sonora.Appearance.Localization;
namespace Sonora.Appearance.Theming;

/// <summary>A theme in the user's sense: a complete structural style (shapes, effects, fonts).</summary>
public sealed record ThemeStyleInfo(string Id, string Name)
{
    /// <summary>A sentence about the design, in the language in use.</summary>
    public string Description => Loc.T("style." + Id + ".description");
}

/// <summary>The themes offered at the first level of the Appearance page; their colour variants are sub-themes.</summary>
public static class ThemeStyles
{
    public static IReadOnlyList<ThemeStyleInfo> All { get; } =
    [
        new("liquid", "Verre liquide"),
        new("terminal", "Studio"),
        new("affiche", "Affiche"),
        new("soul", "Soul"),
        new("fonction", "Fonction"),
        new("encre", "Encre"),
        new("standard", "Standard"),
    ];

    public static ThemeStyleInfo Get(string id) => All.FirstOrDefault(s => s.Id == id) ?? All[^1];
}
