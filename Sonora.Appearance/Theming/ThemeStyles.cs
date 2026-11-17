namespace Sonora.Appearance.Theming;

/// <summary>A theme in the user's sense: a complete structural style (shapes, effects, fonts).</summary>
public sealed record ThemeStyleInfo(string Id, string Name, string Description);

/// <summary>The themes offered at the first level of the Appearance page; their colour variants are sub-themes.</summary>
public static class ThemeStyles
{
    public static IReadOnlyList<ThemeStyleInfo> All { get; } =
    [
        new("liquid", "Verre liquide", "Verre très transparent et bombé, reflets lumineux, formes en capsules. En option : la pochette en cours en fond de fenêtre."),
        new("terminal", "Studio", "Look terminal : police mono, cadres carrés, lignes denses."),
        new("affiche", "Affiche", "Une affiche de concert : aplats de couleur, typographie étendue géante, lignes qui s'inversent au survol."),
        new("soul", "Soul", "Une pochette des années 70 : serif doux et gras, coins en feuille, couleurs de terre et de soleil."),
        new("fonction", "Fonction", "Une fiche technique : gris de bureau, filets fins, grille stricte, une seule touche orange pour ce qui joue."),
        new("encre", "Encre", "Cadre bleu encre, cartes blanches, titres condensés en capitales, pilules."),
        new("standard", "Standard", "Panneaux unis et cartes arrondies, sobre."),
    ];

    public static ThemeStyleInfo Get(string id) => All.FirstOrDefault(s => s.Id == id) ?? All[^1];
}
