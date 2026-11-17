# Sonora — complément : styles structurels et défauts mineurs

Date : 2026-11-07 · Statut : validé · Complète `2026-11-07-sonora-themes-panneaux-design.md`.

## Demande
- Les thèmes Verre, Studio et Immersif doivent correspondre aux maquettes du début, pas seulement à des couleurs. Le « cyber » de l'utilisateur est Studio (maquette noire et verte).
- Corriger les 11 défauts mineurs relevés par la relecture finale.

## Styles
Nouveau champ de thème `style` : `standard` | `glass` | `terminal` | `immersive` (valeur inconnue → `standard`). Choisissable dans l'éditeur (Standard, Verre, Studio, Immersif). Verre → `glass`, Studio → `terminal`, Immersif → `immersive`, les autres → `standard`.

Un style est un dictionnaire de ressources (`Sonora/Styles/<Style>.xaml`) échangé à chaud qui redéfinit des styles et modèles nommés ; au changement de style, les panneaux et la page affichée sont reconstruits.

| Élément | standard | glass (Verre) | terminal (Studio) | immersive (Immersif) |
|---|---|---|---|---|
| Fond de fenêtre | fond du thème | fond + halos colorés flous (accent et teintes voisines) | fond du thème | pochette floutée plein écran |
| Panneaux | surface pleine | surface translucide (≈ 72 %), liseré clair 1 px, ombre douce | surface, cadre 1 px couleur `border`, coins 0 | surface très transparente (≈ 40 %), sans cadre |
| Titres de section | gras | gras | mono, préfixe `// ` en accent | très grands (titre ×1,3) |
| Titre de page | normal | normal | préfixe `> ` en accent | taille « hero » |
| Albums / artistes / playlists | cartes | cartes | lignes compactes (vignette 40 px + texte) | cartes |
| Lignes de titres | unies | unies | une ligne sur deux teintée | unies |
| Boutons lecture / pilules / puces | ronds | ronds | carrés à cadre | ronds |
| Polices (thème Studio) | — | — | Cascadia Mono partout | — |

## Défauts mineurs
1. Thème sans `id` : identifiant tiré du nom de fichier (au chargement et à l'import).
2. Avertissement « police introuvable » affiché dans la barre d'état.
3. Espacement entre panneaux et densité (`Thickness.Panel` = marge intérieure des panneaux : 4 / 8 / 12) appliqués en direct.
4. `Repair` borne les zones à la taille réelle de la fenêtre (mise à jour au redimensionnement).
5. `Apply` n'écrit que les ressources qui changent et ne parcourt la liste des polices qu'une fois.
6. Vignette du thème édité rafraîchie pendant l'édition.
7. Fermer un onglet placé avant l'onglet actif garde l'onglet actif.
8. Chargement des pochettes : seul le dernier titre demandé met à jour l'accent et le fond.
9. Barre de lecture absente du menu « Afficher » et de la liste des panneaux (elle ne peut pas être masquée).
10. Barre de titre Windows : mode clair/sombre et couleurs (fond, texte) du thème.
11. Thème dupliqué : champ `basedOn` ; « Réinitialiser » remet les valeurs du thème d'origine (nom et identifiant conservés) ; sans origine, relit le fichier.

## Tests
Unitaires : style lu/écrit/inconnu → standard ; `basedOn` posé par `Duplicate` ; `ResetToBase` ; id tiré du nom de fichier ; onglet actif conservé. Visuels : 7 thèmes × accueil/liste/apparence ; recherche de couleurs littérales.

## Complément 2 — Verre liquide et thèmes à deux niveaux (validé)
- **Thème** = style complet (formes, effets, polices) : Verre, Verre liquide, Studio, Immersif, Standard (dans cet ordre). Nouveau style `liquid`.
- **Sous-thème** = variante de couleurs d'un thème (un `ThemeDefinition` dont `style` désigne son thème). Fournis : Verre (Nuit=`verre`, Aube=`verre-aube`) ; Verre liquide (Océan=`liquide`, Lumière=`liquide-clair`) ; Studio (Vert=`studio`, Ambre=`studio-ambre`, Cyan=`studio-cyan`) ; Immersif (Prune=`immersif`, Ocre=`immersif-ocre`) ; Standard (`clair`, `oled`, `papier`, `synthwave`).
- Page Apparence : section « Thème » (5 cartes) puis « Sous-thème » (variantes du thème choisi, actions Dupliquer, etc.). Choisir un thème applique son dernier sous-thème utilisé (mémorisé par thème dans `state.json`, champ `PaletteByStyle`), sinon le premier. Le choix « Style » de l'éditeur disparaît : un sous-thème dupliqué reste dans son thème.
- Verre liquide : panneaux très transparents à dégradé bombé (plus clair en haut), arête lumineuse, coins 24, capsules pour boutons/champs/puces, halos plus saturés.
- Tests : ordre et noms des thèmes, sous-thèmes par thème (≥ 2 chacun), 13 sous-thèmes fournis valides et lisibles, style `liquid` lu/écrit.
