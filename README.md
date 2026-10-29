# Overboard

Dessiner n'importe où sur l'écran, et présenter sur un tableau blanc ou noir.
Windows, macOS et Linux, en .NET 10 et [Avalonia](https://avaloniaui.net/).

## Lancer

```sh
dotnet run --project src/Overboard
```

Overboard démarre dans la zone de notification (barre des tâches).
Clic sur l'icône : dessiner sur l'écran. Menu de l'icône → **Tableau blanc** : un tableau infini dans une fenêtre.

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

## Plateformes

- **Windows** : complet.
- **macOS** : autoriser Overboard dans Réglages → Confidentialité → Accessibilité (raccourcis globaux).
- **Linux** : complet sous X11. Sous Wayland, les raccourcis globaux et les fenêtres par-dessus les autres applications sont limités par le système.

## Structure

- `Ink/` : moteur de dessin (outils, lissage des traits, contour à épaisseur variable, formes, texte, annuler/rétablir)
- `Overlay/` : une fenêtre transparente par écran, activation et désactivation
- `Board/` : fenêtre du tableau, fond et grille, format `.overboard`, sauvegarde automatique
- `Controls/` : barre d'outils et bandeau d'aide, communs aux deux
- `Input/` : raccourcis clavier globaux via [SharpHook](https://github.com/TolikPylypchuk/SharpHook)
- `App.cs` : icône et menu de la zone de notification
