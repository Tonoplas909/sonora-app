# Sonora — trois thèmes originaux : Affiche, Soul, Fonction

Date : 2026-11-15 · Demande : « les thèmes se ressemblent trop, j'aime Studio parce qu'il est original ». Démarche reprise de Hallmark (une idée par thème, une matière propre, rien de ce qu'on fait par défaut), adaptée à WPF.

## Constat
Verre, Verre liquide et Standard sont les mêmes panneaux sombres arrondis avec une autre couleur. Studio se distingue parce qu'il change la matière : police, formes, titres, lignes. Un thème doit donc changer ce qu'on voit, pas seulement la palette.

## Les trois thèmes
| | Affiche | Soul | Fonction |
|---|---|---|---|
| Idée | une affiche de concert | une pochette des années 70 | une fiche technique |
| Police | Syne 800, très étendue | Fraunces 800, serif doux | Bahnschrift, petite et exacte |
| Formes | aucun arrondi, aplats, filet d'encre de 6 px entre les panneaux | coins en feuille (deux coins ronds, deux presque droits) | feuilles claires dans un châssis sombre, filets de 1 px |
| Titres | géants ; page : barre d'accent au-dessus ; section : filet épais dessous | page : trois bandes d'accent dessous ; section : serif | page : filet de 2 px ; section : filet au-dessus |
| Lignes de titres | hautes, numéro en grand, inversées en encre au survol | numéros et titres en serif | tableau à filets, barre orange sur le titre qui joue |
| Cartes | couverture carrée, carte entière inversée au survol | tuiles en feuille | cadre de spécimen à filet |
| Couleur | aplat cobalt (ou tomate) autour d'une page papier (ou encre) | cacao + moutarde (ou lagune + corail) | gris + orange (ou noir + orange), l'orange ne marque que ce qui joue |
| Animations | aucune | oui | aucune |

Sous-thèmes : Cobalt / Tomate, Tournesol / Lagune, Gris / Nuit.

## Ce qui a changé dans le code
- **Jetons de forme** `Shape.Icon|Nav|Play|Big|Pill|Chip` (Theme.xaml) : un style les redéfinit pour arrondir ou carrer toutes les commandes, y compris dans le lecteur.
- **Pièces redéfinissables** : `IconButton`, `ThinSlider`, `NavItem`, `RoundNav`, `CardButton`, `CardTitle`, les modèles de ligne de titre, d'en-tête de liste, de file d'attente et d'entrée de bibliothèque passent en `DynamicResource`.
- **En-tête de page** : `HeaderBandStyle`, `HeaderCoverStyle`, `HeaderTitle` (le dégradé, l'ombre et le titre tronqué n'étaient pas modifiables).
- **`TrackCover`** : la pochette d'une ligne de titre et son bouton lecture, partagés pour qu'un style qui redessine les lignes garde le comportement.
- Le texte du bouton lecture, des pilules et des puces actives utilise la couleur de surface (et non de fenêtre) : sur Fonction, la fenêtre est le châssis sombre.
- Polices embarquées : Syne et Fraunces (SIL OFL), instanciées en statique depuis les polices variables.

## Vérifié
Les 21 sous-thèmes se rendent (`--snapshot … play --theme <id>`), Studio, Encre, Verre et les autres sont inchangés, les 6 nouvelles palettes passent le test de contraste texte/surface, et les textes du cadre sur le cadre.

## Simplification : moins de thèmes qui se ressemblent
- **Verre** supprimé : Verre liquide en est la version aboutie. Il devient le thème par défaut et la base dont héritent les champs non renseignés des autres thèmes (`ThemeCatalog.BuiltInDefault`). Les 17 thèmes restants se résolvent exactement comme avant.
- **Immersif** supprimé : sa pochette en fond devient une option de Verre liquide (page Apparence, « Pochette en fond » et son intensité). C'est une préférence enregistrée dans `state.json` (`CoverBackground`, `CoverBackgroundStrength`) : elle marche sur les deux palettes sans les dupliquer. Les halos colorés s'estompent pour laisser lire la pochette.
- **Migration** : un thème enregistré `verre` / `verre-aube` / `immersif` / `immersif-ocre` devient `liquide` / `liquide-clair` ; quitter Immersif active la pochette en fond. Un thème perso dont le style était `glass` ou `immersive` passe en `liquid`.
- Thèmes restants : Verre liquide (Océan, Lumière), Studio, Affiche, Soul, Fonction, Encre, Standard.
