# Overboard

Dessiner n'importe où sur l'écran, et présenter sur un tableau blanc ou noir.
Windows, macOS et Linux, en .NET 10 et [Avalonia](https://avaloniaui.net/).

## Lancer

```sh
dotnet run --project src/Overboard
```

Overboard démarre dans la zone de notification (barre des tâches).
Clic sur l'icône : dessiner sur l'écran. Menu de l'icône → **Tableau blanc** : un tableau infini dans une fenêtre.
Le bouton latéral « avant » de la souris active aussi le dessin, de n'importe où ; il se change ou se désactive
dans le menu de l'icône (**Bouton de souris pour dessiner**).

## Raccourcis

| Action | Raccourci |
|---|---|
| Activer / désactiver le dessin | `Ctrl+Shift+D` ou clic sur l'icône |
| Quitter le dessin (efface tout) | `Échap` |
| Stylo, surligneur, gomme | `P`, `S`, `G` |
| Texte | `T`, puis clic ; `Entrée` ou `Échap` pour valider, `Maj+Entrée` pour aller à la ligne |
| Ligne, flèche, rectangle, ellipse | `L`, `F`, `R`, `E` ; `Maj` pour angles à 45°, carrés et cercles |
| Couleurs | `1` à `6` |
| Taille du trait | molette, `+` / `-` |
| Annuler / rétablir | `Ctrl+Z` / `Ctrl+Y` |
| Tout effacer | `Suppr` |
| Pointeur laser (trace qui s'efface) | `K` |
| Projecteur (assombrit tout sauf autour du curseur ; molette pour la taille) | `O` |
| Envoyer l'écran annoté au tableau | `Ctrl+B` (en mode dessin) |

Tout est aussi accessible depuis la barre d'outils, déplaçable en la faisant glisser.
Au stylet, l'épaisseur du trait suit la pression.

### Tableau blanc

| Action | Raccourci |
|---|---|
| Se déplacer | `Espace` + glisser, ou glisser avec le bouton du milieu ou le bouton droit |
| Zoomer | `Ctrl` + molette (pincer sur un pavé tactile), `Ctrl` + `+` / `-` |
| Tout voir | `Ctrl+0` |
| Fond clair ou sombre | `B` (le noir et le blanc s'échangent) |
| Grille | `Ctrl+G` |
| Nouveau, ouvrir, enregistrer, enregistrer sous | `Ctrl+N`, `Ctrl+O`, `Ctrl+S`, `Ctrl+Maj+S` |

Les tailles de trait et de texte sont celles affichées à l'écran : zoomer permet d'écrire plus fin.
Le tableau est sauvegardé en continu et rouvert tel quel ; `Ctrl+S` l'enregistre en plus dans un fichier `.overboard` (JSON).
Un écran envoyé au tableau s'ajoute à droite du contenu : la capture en image, les annotations par-dessus, toujours modifiables.
La gomme n'efface que les annotations, jamais les captures (`Ctrl+Z` retire un envoi entier).

## Plateformes

- **Windows** : complet.
- **macOS** : autoriser Overboard dans Réglages → Confidentialité → Accessibilité (raccourcis globaux)
  et → Enregistrement de l'écran (envoi au tableau).
- **Linux** : complet sous X11. Sous Wayland, les raccourcis globaux et les fenêtres par-dessus les autres applications sont limités par le système.
  L'envoi au tableau capture l'écran avec `grim` (Wayland) ou `import` d'ImageMagick (X11) ; sans eux, seules les annotations sont envoyées.

L'envoi au tableau n'a été essayé que sous Windows.

## Structure

- `Ink/` : moteur de dessin (outils, lissage des traits, contour à épaisseur variable, formes, texte, annuler/rétablir)
- `Overlay/` : une fenêtre transparente par écran, activation et désactivation
- `Board/` : fenêtre du tableau, fond et grille, format `.overboard`, sauvegarde automatique
- `Controls/` : barre d'outils et bandeau d'aide, communs aux deux
- `Input/` : raccourcis clavier globaux via [SharpHook](https://github.com/TolikPylypchuk/SharpHook)
- `App.cs` : icône et menu de la zone de notification
