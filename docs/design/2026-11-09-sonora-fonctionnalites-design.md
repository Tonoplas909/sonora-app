# Sonora — fonctionnalités Spotify manquantes

Date : 2026-11-09 · Statut : validé.

## Demande
Ajouter les fonctionnalités de Spotify qui manquent, toutes personnalisables, dans l'esprit de Sonora (thèmes, panneaux ancrables, réglages en direct).

## Sonde de l'API (lecture seule, 2026-11-09, app en mode développement)
- Répondent : `me`, `me/top/tracks|artists`, `me/player/recently-played`, `me/shows`, `me/episodes`, `me/following`, `me/playlists`, `me/player/queue|devices`, `search` (dont show, episode), `artists/{id}`, `artists/{id}/albums`, `playlists/{id}`, `playlists/{id}/items` (champ `item`), `shows/{id}`, `shows/{id}/episodes`, `me/library/contains?uris=`.
- Refusés (403/404) : `browse/new-releases`, `browse/categories`, `me/tracks/contains`, `me/following/contains`, `artists/{id}/top-tracks`, `artists/{id}/related-artists`, `playlists/{id}/tracks`, `recommendations`.
- Conséquences : nouveautés bâties sur les albums des artistes suivis ; titres d'un artiste via `search` ; écritures via `PUT|DELETE me/library?uris=` puis repli sur les anciens accès ; contenu des playlists via `/items`. Les écritures ne sont pas testées par la sonde (elles modifieraient le compte).

## Lot 1 — Bibliothèque et découverte
- **Découvrir** (nouveau lien de navigation) : tuiles vers Vos tops, Historique, Nouveautés de vos artistes, Podcasts (selon les fonctions actives).
- **Vos tops** : titres et artistes, périodes 4 semaines / 6 mois / depuis toujours (mémorisée).
- **Historique** : 50 derniers titres joués.
- **Nouveautés de vos artistes** : dernières sorties (albums, singles) des 30 premiers artistes suivis, triées par date.
- **Podcasts** : émissions enregistrées (filtre de bibliothèque « Podcasts », section d'accueil), page d'émission (épisodes), lecture d'un épisode dans son émission.
- **Suivre / enregistrer** : artiste, album, playlist, émission (bouton sur la page) ; état tiré des listes chargées.
- **Pages de titres** : filtre par texte et tri (ordre d'origine, titre, artiste, album, durée) ; playlists Spotify possédées : renommer, retirer des titres ; copie du lien (album, playlist, émission, artiste).
- **Bibliothèque** : recherche, tri (récents / alphabétique), épingles (menu contextuel), filtre Podcasts.
- **Recherche** : filtres Tout / Titres / Artistes / Albums / Playlists / Podcasts ; recherches récentes (10, désactivable, effaçable).
- **Accueil** : nouvelles sections réordonnables `newReleases`, `topArtists`, `shows`.
- **Page artiste** : titres via la recherche.
- Correctifs : J'aime et ajout aux playlists passent par les nouveaux accès.

## Lot 2 — Écoute
- **Paroles** : panneau ancrable `lyrics` (masqué par défaut, menu Afficher). Source : LRCLIB (`lrclib.net/api/get`), paroles synchronisées (surlignage de la ligne en cours, défilement, clic = saut) ou simples ; taille (0,8–1,6) et alignement (gauche/centre) réglables. Aucune parole n'est stockée dans le code ; cache en mémoire seulement.
- **Mini-lecteur** : petite fenêtre sans bordure (pochette, titre, précédent / lecture / suivant, retour à la fenêtre principale), toujours au premier plan (réglable), opacité 40–100 %.
- **Plein écran** (F11, Échap) : pochette géante, titre, paroles, commandes.
- **Minuteur de veille** : 15 / 30 / 45 / 60 min ou fin du titre ; met en pause.
- **Bulle de changement de titre** : en bas à droite quand Sonora n'est pas au premier plan, durée 2–10 s, désactivable.

## Personnalisation
Page « Fonctionnalités » (paramètres) : interrupteurs Paroles, Podcasts, Tops, Historique, Nouveautés, Bulle, Recherches récentes ; options paroles, mini-lecteur, bulle. Réglages dans `state.json` (`Features`). Tout lit les ressources de thème.

## Hors périmètre (API officielle)
Hors-ligne, égaliseur et fondu sur les flux Spotify ; radio et recommandations ; activité des amis ; dossiers de playlists ; réordonner ou vider la file Spotify.

## Tests
Unitaires (logique pure) : réglages (bornes, épingles, recherches récentes), analyse LRC et ligne courante, tri/filtre, minuteur, nouvelles sections d'accueil, panneau `lyrics` masqué par défaut. Visuels : captures des nouvelles pages et panneaux sur plusieurs thèmes. Non testable sans le compte : écritures Spotify (signalé à l'utilisateur).
