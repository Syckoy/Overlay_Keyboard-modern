# Yeshua Overlay V2

Overlay clavier + souris pour le live (OBS / TikTok), avec **panneau de réglages** séparé.

> Base = copie de `obs-overlay-rose` (V1). La V1 n’est pas modifiée.

## Nouveau en V2

Panneau simple (`panel.html`) pour régler en live :

- **Couleur** : presets + panneau HSV (teinte / saturation / valeur) + HEX
- **Design** : Classique, Kawaii, Chat cartoon, Gothique, iPhone, Apex, Valorant, Minecraft, Néon
- **Souris** : Ruban, Points, Laser, Comète, Viseur, Anneau (traînée / curseur)
- **Clavier** : AZERTY ou QWERTY
- **Super Glide** : touche jump, touche crouch, FPS

Les réglages sont sauvés dans `settings.json` par le bridge, puis poussés à l’overlay via WebSocket (donc OBS les voit tout de suite).

## Lancement

1. Double-clique **`start-overlay.bat`** (laisse ouvert).
2. Double-clique **`ouvrir-panel.bat`** pour les réglages.
3. OBS : source Navigateur → `http://127.0.0.1:7689/`
4. TikTok : `ouvrir-fenetre-tiktok.bat` comme avant.

URLs utiles :

| Page | URL |
|------|-----|
| Overlay | http://127.0.0.1:7689/ |
| Panneau | http://127.0.0.1:7689/panel |
| Settings JSON | http://127.0.0.1:7689/settings |

## Fichiers

| Fichier | Rôle |
|---------|------|
| `start-overlay.bat` | Démarre le serveur |
| `ouvrir-panel.bat` | Ouvre le panneau de réglages |
| `panel.html` | UI réglages |
| `overlay.html` | Overlay stream |
| `settings.json` | Réglages sauvegardés (créé au 1er lancement) |
| `settings.default.json` | Défauts |
| `bridge.exe` / `bridge.cs` | Capture + HTTP/WS |
| `ouvrir-fenetre-tiktok.bat` | Fenêtre Edge TikTok |

## OBS

1. `start-overlay.bat` lancé.
2. Source **Navigateur**, URL `http://127.0.0.1:7689/`
3. Taille conseillée : **700 × 340**
4. Fond transparent.
5. Change les options via le panneau — pas besoin de recharger (mais « Actualiser » si besoin).

## Note

Si tu changes `bridge.cs`, supprime `bridge.exe` puis relance `start-overlay.bat` pour recompiler.
