# Glisser des titres vers une playlist : un retour visuel clair

Premier des quatre chantiers (ordre convenu : 1 glisser-déposer, 2 onglets de sources, 4 export/import, 3 fichiers locaux dans les playlists Spotify). Petit changement : conception courte.

## Pourquoi

Glisser des titres sur une playlist de la bibliothèque marche déjà (`TrackDragDrop`), mais rien ne montre ce qui est porté ni où il va atterrir. Pire : un titre Spotify lâché sur une playlist locale, ou un fichier local lâché sur une playlist Spotify, est écarté sans un mot (`MainViewModel.AddToPlaylist` et `SpotifyAccount.AddToPlaylistAsync` filtrent en silence).

## Ce qu'on construit

Pendant un glisser de titres :

1. **Un aperçu qui suit le curseur** : une petite carte « ♪ Titre » (+ « +N » si plusieurs lignes sont portées), aux couleurs du thème.
2. **La playlist visée est mise en avant** : bordure et fond d'accent, et l'aperçu affiche « + Ajouter à *X* ».
3. **Une cible refusée se grise** et l'aperçu dit pourquoi. Raisons : titres Spotify vers une playlist locale ; fichiers locaux vers une playlist Spotify ; playlist Spotify qui n'est pas à l'utilisateur. Une sélection mixte dont une partie passe affiche « +2 sur 3 ».
4. **Au lâcher**, la playlist pulse brièvement ; le message « Ajouté à… » existant est conservé.

Le pointeur reste le pointeur Windows habituel (déplacement / interdit).

## Hors périmètre

- Défilement automatique de la bibliothèque quand on approche du bord (à proposer à part).
- Glisser des fichiers depuis l'Explorateur Windows.
- Indicateur d'insertion pour réordonner dans une playlist.
- Autoriser les titres Spotify dans une playlist locale (le modèle local ne stocke que des chemins).

## Approches considérées

| | Principe | Pour | Contre |
|---|---|---|---|
| **A (choisie)** | Aperçu dessiné sur la couche d'ornements de WPF | Aux couleurs du thème, pas de Win32, même mécanisme que `DockDragOverlay` | Reste dans la fenêtre (sans importance : toutes les cibles y sont) |
| B | Fenêtre flottante au-dessus de tout | L'aperçu peut sortir de la fenêtre | Win32 pour suivre le curseur, pour rien ici |
| C | Image de glisser native de Windows | Aucun dessin à faire | Ignore le thème, interop COM |

## Conception

- **Règle de dépôt, testable** : une fonction pure dans `Sonora.Appearance` (par ex. `DropRule`) prend le genre de cible (playlist locale / playlist Spotify à soi / playlist Spotify d'autrui / autre) et le nombre de titres Spotify et locaux portés ; elle rend le verdict (accepté avec le nombre de titres qui passent, refusé avec la raison). Les messages sont des clés de localisation. `TrackDragDrop.LibraryDragOver` et `LibraryDrop` l'appellent toutes deux, pour que l'aperçu promette exactement ce que le dépôt fera.
- **État de la cible** : une propriété attachée sur l'élément de bibliothèque (`Aucun / Accepte / Refuse / Atterri`) que le style `LibraryItem` (`Templates.xaml`) traduit en bordure, fond et opacité. Elle est posée par `LibraryDragOver`, remise à zéro quand le pointeur quitte l'élément et à la fin du glisser.
- **Aperçu** : un `Adorner` (comme `DockDragOverlay`) posé sur la couche de la fenêtre principale, créé avant `DragDrop.DoDragDrop` et retiré dans un `finally` (l'appel est bloquant, donc l'aperçu vit exactement le temps du glisser). Sa position vient de `PreviewDragOver` sur la fenêtre, puisque les événements souris ne sont pas émis pendant un glisser.
- **Impulsion** : à `Atterri`, une brève animation du fond de l'élément (environ 600 ms) puis retour à `Aucun`.
- **Textes** : nouvelles clés dans les 12 fichiers de langue (les tests `LocalizationTests` l'exigent). Les clés existantes sont réutilisées quand elles conviennent.

## Preuve prévue

Tests unitaires de la règle de dépôt (tous les genres de cible, sélections pures et mixtes, sélection vide). Le reste, de l'affichage, se vérifie en lançant l'app : glisser vers chacun des quatre genres de cible, lâcher, annuler avec Échap.

## Laissé au constructeur

Dimensions, durée exacte et courbe de l'impulsion ; formulation des textes (en français d'abord, les autres langues suivent le processus de `docs/TRANSLATING.md`).
