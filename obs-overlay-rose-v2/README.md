# Overlay Keyboard Modern (V2)

Overlay clavier + souris pour le live (**OBS** / **TikTok**), avec panneau de config live, éditeur de touches type Figma, images/GIF perso, Super Glide et mise à jour automatique depuis GitHub.

> Repo : [Syckoy/Overlay_Keyboard-modern](https://github.com/Syckoy/Overlay_Keyboard-modern)  
> Base = évolution de `obs-overlay-rose` (V1). La V1 n’est pas modifiée.

---

## Lancement

1. Double-clique **`start-overlay.bat`** (laisse la fenêtre ouverte).
2. Au démarrage : vérif auto des mises à jour GitHub (voir plus bas).
3. Double-clique **`ouvrir-panel.bat`** pour la config.
4. **OBS** : source Navigateur → `http://127.0.0.1:7689/`
5. **TikTok** : `ouvrir-fenetre-tiktok.bat` (fenêtre Edge + chroma key).

| Page | URL |
|------|-----|
| Overlay | http://127.0.0.1:7689/ |
| Panneau | http://127.0.0.1:7689/panel |
| Settings | http://127.0.0.1:7689/settings |

Taille OBS conseillée : **~700 × 340** (adapte si tu custom le layout).

---

## Fonctionnalités

### Apparence
- **Thèmes** : Classique, Kawaii, Chat cartoon, Gothique, iPhone, Apex, Valorant, Minecraft, Néon
- **Couleur** : presets + picker HSV + HEX, appliqué en live sur l’overlay

### Souris
- Styles de traînée : Ruban, Points, Laser, Comète, Viseur, Anneau
- **Sensibilité** réglable de **5 % à 1000 %** (100 % = base)

### Clavier
- **AZERTY** / **QWERTY**
- Mode **custom** : placement libre des touches (1 px = overlay)
- Éditeur type **Figma** dans le panneau :
  - Drag & drop, resize, grille **SHIFT** (8 px)
  - Guides roses + **espacement égal** (ex. 3 px) avec labels en pixels
  - **Lasso** (clic gauche + glisser) pour sélection multiple
  - **SHIFT + clic** / **SHIFT + lasso** pour ajouter à la sélection
  - Déplacement / suppression groupés
  - Palette complète + « Ajouter clavier complet » / « Recaler défaut »
- Désactiver le mode custom → disposition par défaut (les images custom sont retirées de l’overlay)

### Images & GIF perso
- Ajout illimité (PNG, JPG, GIF, WEBP) depuis le panneau
- Place, agrandis (coins type Figma, ratio gardé, **SHIFT** = libre), tourne (poignée orange)
- **Masque touches** : l’image ne s’affiche que sur la forme des touches
- **Couvrir les lettres** : option pour passer l’image au-dessus ou **sous** le texte des touches
- **Opacité** 0–100 %
- Calques à droite avec **▲▼** pour l’ordre
- Upload via le bridge (`/media/upload`), fichiers dans `themeperso/assets/user-*`

### Super Glide
- Activation / désactivation
- Jump & crouch : n’importe quelle touche (même hors overlay) + bouton **Capturer**
- FPS du jeu configurable

### Mise à jour auto
- À chaque `start-overlay.bat` : vérif GitHub
- Si nouveauté → message + demande **O/N**
- Mise à jour sans écraser le dossier `themeperso/` (réglages + images perso)
- Suivi : `version.json` + `.update-state.json`

### Panneau de config
- UI plus large / lisible (moins besoin de zoomer)
- Look tool (Syne + IBM Plex), moins « dashboard IA »
- Thème + couleur en 2 colonnes sur grand écran

---

## Changelog (ajouts & correctifs)

### Ajouts
- Panneau de réglages live (`panel.html`) + sync WebSocket
- Sensibilité souris 5–1000 %
- Éditeur clavier custom (absolute, fidèle à l’overlay)
- Guides d’alignement + snap d’espacement égal (style Figma) + labels px
- Sélection multiple + lasso
- Images / GIF : upload, resize, rotation, masque, opacité, calques, sous/sur lettres
- Super Glide : toggle, capture de touches globales, FPS
- Vérification / mise à jour automatique depuis GitHub (`check-update.ps1`)
- Recompile `bridge.exe` à chaque lancement (toujours à jour avec `bridge.cs`)

### Correctifs
- Mode custom + thèmes : touches bien en `position: absolute` (plus de layout cassé type Chat/Kawaii)
- Layout custom vide : plus de désactivation forcée du mode custom → on peut réajouter touches / clavier complet
- Masque image : rendu SVG fiable (overlay + aperçu éditeur)
- Resize / rotation images en mode masque (cadre de contrôle type Figma)
- Lettres des touches lisibles avec option « ne pas couvrir les lettres »
- Désactivation du mode custom → images retirées de l’overlay
- Premier check update : baseline sans écraser une version locale plus récente

---

## Fichiers

| Fichier | Rôle |
|---------|------|
| `start-overlay.bat` | Bannière + check update + compile bridge + lance le serveur |
| `check-update.ps1` | Vérif / maj GitHub |
| `version.json` | Version locale + infos repo |
| `.update-state.json` | Dernier commit GitHub suivi (créé au 1er lancement) |
| `ouvrir-panel.bat` | Ouvre le panneau |
| `ouvrir-fenetre-tiktok.bat` | Fenêtre Edge fond vert |
| `panel.html` | UI config + éditeur |
| `overlay.html` | Overlay stream |
| `bridge.cs` / `bridge.exe` | Capture Raw Input + HTTP/WS (port **7689**) |
| `themeperso/` | **Données perso** (jamais écrasées à la maj) |
| `themeperso/settings.json` | Thèmes / design / réglages utilisateur |
| `themeperso/assets/` | Images / GIF perso (`user-*`) |
| `settings.default.json` | Défauts (template) |
| `assets/` | Stickers des thèmes intégrés (repo) |
| `yeshua.ps1` | Bannière ASCII au lancement |

---

## OBS

1. Lance `start-overlay.bat`.
2. Source **Navigateur** → `http://127.0.0.1:7689/`
3. Fond transparent, ne pas « arrêter quand non visible ».
4. Régle via le panneau — les changements partent en live (Actualiser si besoin).

---

## Notes

- Laisse `start-overlay.bat` ouvert pendant tout le stream.
- Les images perso et le placement custom ne s’appliquent qu’en **mode custom**.
- Pour publier une nouvelle version aux utilisateurs : push sur `main` (ou une Release GitHub) — le script de maj la détectera.
- Usage personnel / stream. Adapte et partage librement.
