# Overboard

Dessiner n'importe où sur l'écran, et présenter sur un tableau blanc ou noir.
Windows, macOS et Linux, en .NET 10 et [Avalonia](https://avaloniaui.net/).

## Lancer

```sh
dotnet run --project src/Overboard
```

Overboard démarre dans la zone de notification (barre des tâches).

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

## Plateformes

- **Windows** : complet.
- **macOS** : autoriser Overboard dans Réglages → Confidentialité → Accessibilité (raccourcis globaux).
- **Linux** : complet sous X11. Sous Wayland, les raccourcis globaux et les fenêtres par-dessus les autres applications sont limités par le système.

## Structure

- `Ink/` : moteur de dessin (outils, lissage des traits, contour à épaisseur variable, formes, texte, annuler/rétablir)
- `Overlay/` : une fenêtre transparente par écran, barre d'outils, activation et désactivation
- `Input/` : raccourcis clavier globaux via [SharpHook](https://github.com/TolikPylypchuk/SharpHook)
- `App.cs` : icône et menu de la zone de notification
