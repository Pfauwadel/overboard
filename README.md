# Overboard

Dessiner n'importe où sur l'écran, et présenter sur un tableau blanc ou noir.
Windows, macOS et Linux, en .NET 10 et [Avalonia](https://avaloniaui.net/).

## Installer

Télécharger le fichier de sa plateforme dans les [Releases](https://github.com/Pfauwadel/overboard/releases) :
un exécutable autonome, sans installation ni .NET à installer.

- **Windows** : `Overboard-x.y.z-win-x64.zip` (ou `win-arm64`), extraire le dossier où l'on veut et lancer `Overboard.exe` (garder `uiohook.dll` à côté).
  L'exécutable n'est pas encore signé (voir [Code signing policy](#code-signing-policy)) : au premier lancement,
  SmartScreen demande de confirmer (« Informations complémentaires » → « Exécuter quand même »).
- **macOS** : `Overboard-x.y.z-macos-arm64.zip` (Apple Silicon) ou `macos-x64` (Intel), glisser `Overboard.app` dans Applications.
  L'application n'est pas notarisée : la première fois, clic droit → Ouvrir, ou `xattr -dr com.apple.quarantine /Applications/Overboard.app`.
- **Linux** : `Overboard-x.y.z-linux-x64.tar.gz`, extraire le dossier et lancer `./Overboard`.

Dans le menu de l'icône : **Lancer au démarrage de la session**, **Rechercher les mises à jour**,
et la version installée (lien vers les Releases). Quand une version plus récente existe, elle est proposée en haut de ce menu.

## Désinstaller

Overboard ne s'installe pas : il suffit d'effacer ce qu'il a créé.

1. Dans le menu de l'icône, décocher **Lancer au démarrage de la session** (si cochée), puis **Quitter Overboard**.
2. Supprimer le dossier extrait (Windows, Linux) ou `Overboard.app` (macOS).
3. Supprimer le dossier de données (tableau sauvegardé automatiquement et préférences) :
   - Windows : `%LocalAppData%\Overboard`
   - macOS : `~/Library/Application Support/Overboard`
   - Linux : `~/.local/share/Overboard`

Les fichiers `.overboard` enregistrés ailleurs restent à l'utilisateur.

## Lancer depuis les sources

```sh
dotnet run --project src/Overboard
```

## Utiliser

Overboard démarre dans la zone de notification (barre des tâches).
Clic sur l'icône : dessiner sur l'écran. Menu de l'icône → **Tableau blanc** : un tableau infini dans une fenêtre.
Le bouton latéral « avant » de la souris active aussi le dessin, de n'importe où ; il se change ou se désactive
dans le menu de l'icône (**Bouton de souris pour dessiner**).

## Raccourcis

| Action | Raccourci |
|---|---|
| Activer / désactiver le dessin | `Ctrl+Shift+D` ou clic sur l'icône |
| Quitter le dessin (efface tout) | `Échap` |
| Sélection | `V` ; clic, `Maj`+clic pour ajouter ou retirer, ou rectangle autour ; glisser pour déplacer |
| Sélectionner tout, copier, couper, coller, dupliquer | `Ctrl+A`, `Ctrl+C`, `Ctrl+X`, `Ctrl+V`, `Ctrl+D` |
| Déplacer ou supprimer la sélection | flèches (`Maj` : ×10), `Suppr` |
| Stylo, surligneur, gomme | `P`, `S`, `G` |
| Texte | `T`, puis clic ; `Entrée` ou `Échap` pour valider, `Maj+Entrée` pour aller à la ligne |
| Ligne, flèche, rectangle, ellipse | `L`, `F`, `R`, `E` ; `Maj` pour angles à 45°, carrés et cercles |
| Couleurs | `1` à `6` |
| Taille du trait | molette, `+` / `-` |
| Annuler / rétablir | `Ctrl+Z` / `Ctrl+Y` |
| Tout effacer | `Suppr` (sans sélection) |
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
| Désélectionner | `Échap` |
| Exporter en PNG (la sélection, sinon tout le tableau) | `Ctrl+E` |
| Grille | `Ctrl+G` |
| Nouveau, ouvrir, enregistrer, enregistrer sous | `Ctrl+N`, `Ctrl+O`, `Ctrl+S`, `Ctrl+Maj+S` |

Les tailles de trait et de texte sont celles affichées à l'écran : zoomer permet d'écrire plus fin.
Le tableau est sauvegardé en continu et rouvert tel quel ; `Ctrl+S` l'enregistre en plus dans un fichier `.overboard` (JSON).
Un écran envoyé au tableau s'ajoute à droite du contenu : la capture en image, les annotations par-dessus, toujours modifiables.
La gomme n'efface que les annotations, jamais les captures : pour en retirer une, la sélectionner (`V`) puis `Suppr`.
Copier sur l'écran puis coller sur le tableau fonctionne aussi.

`Ctrl+C` place aussi la sélection en image dans le presse-papiers du système, à coller dans un document ou un message
(tant qu'Overboard tourne). `Ctrl+V` colle une image ou des fichiers image copiés ailleurs, et on peut glisser-déposer
des images sur le tableau. L'export PNG reprend le fond du tableau, en double résolution.

## Plateformes

- **Windows** : complet.
- **macOS** : autoriser Overboard dans Réglages → Confidentialité → Accessibilité (raccourcis globaux)
  et → Enregistrement de l'écran (envoi au tableau).
- **Linux** : complet sous X11. Sous Wayland, les raccourcis globaux et les fenêtres par-dessus les autres applications sont limités par le système.
  L'envoi au tableau capture l'écran avec `grim` (Wayland) ou `import` d'ImageMagick (X11) ; sans eux, seules les annotations sont envoyées.

L'envoi au tableau n'a été essayé que sous Windows.

## Structure

- `Ink/` : moteur de dessin (outils, lissage des traits, contour à épaisseur variable, formes, texte, annuler/rétablir)
- `Overlay/` : une fenêtre transparente par écran, activation et désactivation, capture d'écran
- `Board/` : fenêtre du tableau, fond et grille, format `.overboard`, sauvegarde automatique
- `Controls/` : barre d'outils et bandeau d'aide, communs aux deux
- `Input/` : raccourcis clavier globaux via [SharpHook](https://github.com/TolikPylypchuk/SharpHook)
- `App.cs` : icône et menu de la zone de notification
- `Distribution/` : version, lancement au démarrage de la session, recherche de nouvelle version

## Publier une version

Pousser un tag de version : le workflow **Release** construit les exécutables des cinq plateformes
et crée la Release GitHub correspondante, avec les notes générées depuis les commits.

```sh
git tag v0.2.0
git push origin v0.2.0
```

Pour obtenir les fichiers sans rien publier : Actions → Release → *Run workflow* ; ils sont alors joints à l'exécution.
En local : `dotnet publish src/Overboard -c Release -r win-x64` (ou `linux-x64`, `osx-arm64`…).

## Confidentialité

Overboard fonctionne entièrement sur l'ordinateur : pas de compte, pas de statistiques d'usage, rien n'est envoyé ailleurs,
à une exception près.

- **Recherche de mises à jour** : au démarrage puis une fois par jour, Overboard demande à GitHub
  (`api.github.com`) quelle est la dernière version publiée. Cette requête ne contient que ce que tout accès web transmet
  (adresse IP, version d'Overboard dans l'en-tête `User-Agent`), et relève de la
  [déclaration de confidentialité de GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).
  Elle se désactive dans le menu de l'icône (**Rechercher les mises à jour**) : plus aucune connexion n'est alors établie.
- **Clavier et souris** : pour les raccourcis globaux, Overboard observe les touches et boutons pressés dans tout le système.
  Il ne réagit qu'à ses propres raccourcis ; rien n'est enregistré ni transmis.
- **Captures d'écran** : faites uniquement à la demande (envoi au tableau) et conservées localement, dans le tableau.
- **Données locales** : tableau sauvegardé automatiquement et préférences, dans le dossier indiqué à [Désinstaller](#désinstaller).

## Code signing policy

Les exécutables Windows des [Releases](https://github.com/Pfauwadel/overboard/releases) sont construits par
[GitHub Actions](.github/workflows/release.yml) à partir de ce dépôt, sur des machines hébergées par GitHub.

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).
*Demande en cours : les versions publiées jusqu'ici ne sont pas encore signées.*

Seul `Overboard.exe`, construit depuis ce dépôt, est signé ; les bibliothèques tierces livrées à côté
(voir [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)) ne le sont pas par ce projet.

Rôles :

- Auteurs (committers) : [Pfauwadel](https://github.com/Pfauwadel)
- Relecteurs (contributions extérieures) : [Pfauwadel](https://github.com/Pfauwadel)
- Approbateurs (demandes de signature) : [Pfauwadel](https://github.com/Pfauwadel)

Chaque demande de signature est approuvée manuellement par un approbateur. Le projet n'utilise aucun composant propriétaire.

## Licence

[MIT](LICENSE) : chacun peut réutiliser le code, en citant l'auteur. Les modifications de ce dépôt restent réservées à son propriétaire.
Les composants tiers inclus dans les versions publiées et leurs licences sont listés dans [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md),
dont libuiohook (LGPL-3.0), livrée comme bibliothèque séparée et remplaçable.
