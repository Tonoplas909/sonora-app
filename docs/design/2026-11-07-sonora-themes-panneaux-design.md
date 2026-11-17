# Sonora — refonte de l'interface : thèmes, personnalisation et panneaux déplaçables

Date : 2026-11-07 · Statut : en attente de validation

## 1. Objectif

Refaire toute l'interface de Sonora avec une nouvelle direction artistique, et rendre l'apparence, la disposition et le contenu affiché entièrement réglables par l'utilisateur, en direct, sans redémarrer.

### Ce que l'utilisateur a demandé
- Trois directions artistiques (Verre, Studio, Immersif) disponibles comme thèmes interchangeables dans les paramètres, plus d'autres thèmes.
- Pouvoir régler : les couleurs, la typo et les formes, la disposition, le contenu affiché.
- Pouvoir déplacer chaque panneau en glisser-déposer et l'ancrer où il veut, à la manière de VS Code (option « zones » retenue, sans fenêtres flottantes).

### Hypothèses (choix faits sans demande explicite)
- Thème par défaut : **Verre**.
- Les thèmes fournis ne sont pas modifiables directement ; on les duplique pour les éditer.
- La page centrale (Accueil, listes, artiste, recherche, paramètres) reste au centre ; seuls les panneaux autour se déplacent.
- Le cœur fonctionnel (Spotify, fichiers locaux, lecteur, connexion obligatoire) n'est pas modifié, sauf pour l'adapter aux nouvelles vues.

### Critères de réussite
1. Les 7 thèmes fournis s'appliquent instantanément depuis les paramètres, et le choix persiste au redémarrage.
2. Toute couleur, police, taille, arrondi, densité ou mode de fond d'un thème personnel se modifie avec un aperçu immédiat ; un thème s'exporte et s'importe en fichier.
3. Chaque panneau (Navigation, Bibliothèque, File d'attente, En cours, Barre de lecture) se déplace en glisser-déposer vers les zones gauche, droite, haut ou bas, en pile ou en onglet ; la disposition persiste et se réinitialise.
4. Les sections de l'accueil et les colonnes des listes se masquent et se réordonnent.
5. Aucune couleur n'est écrite en dur dans les vues (vérifié par recherche dans le XAML).
6. Le défilement des longues listes reste virtualisé et fluide.

## 2. Architecture

```
Theming/
  ThemeDefinition.cs     modèle pur (chaînes, nombres) — sérialisé en JSON, testable sans WPF
  ThemeCatalog.cs        thèmes fournis + thèmes utilisateur (chargement, validation, import/export)
  ThemeService.cs        applique un thème aux ressources dynamiques de l'application
  ArtworkAccent.cs       couleur dominante d'une pochette + correction de contraste
Layout/
  DockLayout.cs          modèle pur de la disposition (zones, groupes, panneaux)
  DockLayoutOps.cs       opérations : déplacer, scinder, mettre en onglet, fermer, réinitialiser, réparer
  DockHost.cs (+ XAML)   construit l'arbre visuel à partir du modèle, poignées de redimensionnement
  DockDragOverlay.cs     cibles de dépôt affichées pendant un glisser
Appearance/
  ContentSettings.cs     sections de l'accueil (ordre, visibilité), colonnes des listes
Views/
  vues réécrites (pages et panneaux), une par fichier XAML
```

Les modèles `ThemeDefinition`, `DockLayout` et `ContentSettings` n'utilisent aucun type WPF, pour pouvoir être testés unitairement.

### 2.1 Thèmes

**Format** (`%LOCALAPPDATA%\Sonora\themes\<id>.json`) :

```json
{
  "id": "verre",
  "name": "Verre",
  "builtIn": true,
  "colors": {
    "window": "#0F1117", "surface": "#181B23", "surfaceAlt": "#20242F",
    "hover": "#2A2F3C", "selected": "#343A4A", "border": "#2C3140",
    "textPrimary": "#F4F5F8", "textSecondary": "#A9AEBD", "textMuted": "#6E7383",
    "accent": "#7C9CFF", "onAccent": "#0B0E18", "rail": "#3A4050"
  },
  "fonts": { "ui": "Segoe UI Variable Text", "display": "Segoe UI Variable Display", "numbers": "Segoe UI Variable Text" },
  "fontScale": 1.0,
  "cornerRadius": 12,
  "density": "normal",
  "panelGap": 8,
  "background": { "mode": "gradient", "colors": ["#141826", "#0F1117"], "artworkStrength": 0.0 },
  "accentFromArtwork": true,
  "animations": true,
  "defaultLayout": null
}
```

- `density` : `compact` | `normal` | `confort` → hauteur des lignes (40/56/64 px), marges et espacements.
- `background.mode` : `solid` | `gradient` | `artwork` (fond teinté par la pochette en cours, intensité `artworkStrength` de 0 à 1).
- `defaultLayout` : disposition proposée par le thème (optionnelle), appliquée seulement si l'utilisateur choisit « Réinitialiser la disposition ».

**Ressources dynamiques** : `ThemeService` écrit dans `Application.Resources` des clés stables (`Brush.Window`, `Brush.Surface`, `Brush.Accent`, `Font.UI`, `Font.Numbers`, `Size.Text.Body`, `Size.Text.Title`, `Radius.Panel`, `Radius.Control`, `Radius.Art`, `Space.Gap`, `Size.Row`, etc.). Toutes les vues les lisent en `DynamicResource`. Les pinceaux ne sont pas gelés pour permettre les transitions de couleur (animation de 250 ms si `animations` est actif).

**Accent automatique** : à chaque changement de titre, `ArtworkAccent` réduit la pochette à 32×32, retient la teinte la plus représentée parmi les pixels assez saturés, puis ajuste la luminosité pour garantir un contraste d'au moins 3:1 avec `surface` et calcule `onAccent` (noir ou blanc selon le meilleur contraste). Sans pochette, on revient à l'accent du thème.

**Catalogue** : les thèmes fournis sont embarqués dans l'application et réécrits au démarrage s'ils manquent. Les thèmes utilisateur sont chargés depuis le dossier ; un fichier invalide est ignoré et signalé. Les valeurs manquantes sont complétées avec celles de Verre. Opérations : dupliquer, renommer, supprimer (thèmes utilisateur uniquement), exporter (copie du `.json`), importer (validation puis copie avec un identifiant unique).

### 2.2 Thèmes fournis

| Thème | Esprit | Points clés |
|---|---|---|
| Verre (défaut) | Windows 11 moderne | Panneaux flottants, arrondi 12, dégradé sombre bleuté, accent auto d'après la pochette |
| Studio | Outil pro, clavier | Noir, arrondi 2, densité compacte, chiffres en Cascadia Mono, accent vert citron, séparateurs fins |
| Immersif | Pochette au centre | Fond teinté par la pochette (intensité 0,55), grande typo (échelle 1,15), arrondi 16, disposition « En cours » en grand |
| Clair | Thème lumineux | Fonds blancs et gris clairs, texte sombre, accent violet |
| OLED | Noir pur | Fond #000, panneaux #0A0A0A, accent auto |
| Papier | Chaud, lecture | Beige et brun, police Georgia pour les titres, arrondi 8 |
| Synthwave | Néons de nuit | Dégradé violet nuit, accent rose, textes cyan secondaires |

### 2.3 Panneaux déplaçables

**Modèle** :

```
DockLayout
  Zones: Left, Right, Top, Bottom        (la zone Centre est fixe : page courante)
  Zone   { Size (px), IsCollapsed, Groups: [Group] }        groupes empilés dans la zone
  Group  { Panels: [PanelId], ActiveIndex, Ratio }           plusieurs panneaux = onglets
  Hidden : [PanelId]                                          panneaux fermés
PanelId = navigation | library | queue | nowPlaying | player
```

**Disposition par défaut** : Gauche = [Navigation] au-dessus de [Bibliothèque] ; Droite = [File d'attente] (repliée) ; Bas = [Barre de lecture] ; En cours masqué.

**Interaction** :
- Chaque panneau a un en-tête (titre, icône, bouton fermer). Glisser l'en-tête au-delà de 6 px démarre le déplacement : l'overlay affiche les 4 cibles de bord de fenêtre et, au survol d'un groupe existant, 3 cibles (au-dessus, en dessous, en onglet).
- Au relâchement, `DockLayoutOps` met à jour le modèle, `DockHost` reconstruit la disposition et l'enregistre. Échap annule le glisser.
- Les zones se redimensionnent par poignée ; un double-clic sur la poignée replie ou déplie la zone.
- Un panneau s'adapte à l'orientation de sa zone : la barre de lecture est horizontale en haut ou en bas et verticale (pochette, titre, commandes empilées) à gauche ou à droite ; la Bibliothèque passe en liste compacte quand la zone est étroite.
- Menu « Afficher » dans la barre du haut : cocher un panneau masqué le rouvre dans sa dernière zone ; « Réinitialiser la disposition » restaure celle du thème ou la disposition par défaut.

**Persistance** : `%LOCALAPPDATA%\Sonora\layout.json`, écrit 1 s après chaque changement. Au chargement, `DockLayoutOps.Repair` retire les identifiants inconnus ou en double, ajoute les panneaux absents à leur zone par défaut, supprime les groupes vides et borne les tailles à la fenêtre.

### 2.4 Contenu affiché

- **Accueil** : sections `recent`, `playlists`, `savedAlbums`, `rediscover`, `artists`, chacune avec visible/ordre, réordonnables dans les paramètres (flèches haut/bas et glisser-déposer).
- **Listes de titres** : colonnes `number`, `art`, `album`, `like`, `duration` affichables ou masquables (le titre et l'artiste restent toujours visibles). S'applique à toutes les listes.
- **Taille des pochettes** des cartes : petite (128), moyenne (160), grande (200).
- Stockage : dans `state.json` (section `appearance`), avec l'identifiant du thème actif.

### 2.5 Page « Apparence » (paramètres)

Ordre : vignettes des thèmes (aperçu miniature dessiné avec les couleurs du thème, clic = appliquer) → actions (Dupliquer, Renommer, Supprimer, Exporter, Importer, Réinitialiser) → Couleurs (une ligne par couleur : pastille, code hexadécimal éditable, sélecteur avec teinte/saturation/luminosité ; désactivé sur un thème fourni avec invitation à le dupliquer) → Typo et formes (polices installées, échelle 0,85–1,3, arrondi 0–24, densité, animations) → Fond (mode, couleurs, intensité) → Disposition (Réinitialiser, liste des panneaux avec visibilité) → Contenu (sections de l'accueil, colonnes, taille des pochettes).

### 2.6 Refonte des vues

Toutes les vues sont réécrites dans `Views/` en n'utilisant que les ressources du thème : pages (Accueil, liste de titres, artiste, recherche, albums, artistes, playlists, paramètres, apparence), panneaux (Navigation, Bibliothèque, File d'attente, En cours, Barre de lecture) et écran de connexion. `MainWindow.xaml` ne contient plus que `DockHost` et l'écran de connexion. Les styles génériques (boutons, curseurs, champs, listes) sont redéfinis dans `Theme.xaml` en `DynamicResource`.

## 3. Gestion des erreurs

- Thème illisible ou invalide : ignoré, message dans la barre d'état avec le nom du fichier ; si le thème actif est introuvable, retour à Verre.
- Import : refus explicite si le JSON est invalide, si une couleur n'est pas au format `#RRGGBB`/`#AARRGGBB` ou si une police n'est pas installée (remplacée par Segoe UI avec avertissement).
- Disposition invalide : réparée ; si irréparable, disposition par défaut.
- Police absente du système : repli sur Segoe UI Variable.

## 4. Tests

- Nouveau projet `Sonora.Tests` (xUnit, net10.0) sur les modèles purs :
  - thèmes : lecture, complétion des valeurs manquantes, validation des couleurs, aller-retour export/import, identifiants uniques à l'import ;
  - disposition : déplacer vers une zone, scinder, mettre en onglet, fermer/rouvrir, réinitialiser, réparation des cas invalides ;
  - accent automatique : contraste minimal garanti sur des couleurs limites ;
  - contenu : ordre et visibilité des sections.
- Vérification visuelle avec le mode `--snapshot` étendu (`--theme <id>`, `--layout <fichier>`) : chaque thème rendu sur l'accueil, une liste de titres et la page Apparence, plus deux dispositions non standard (bibliothèque à droite, barre de lecture en haut).
- Recherche automatique de couleurs hexadécimales restantes dans `Views/` et `MainWindow.xaml`.

## 5. Hors périmètre

Fenêtres flottantes détachables, habillages XAML complets, paroles, découpages imbriqués au-delà d'une pile par zone, synchronisation des thèmes entre appareils.
