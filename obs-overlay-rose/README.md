# Yeshua Overlay

Overlay clavier + souris pour le live (OBS / TikTok).  
Touches AZERTY en rose, pad souris avec point et traînée, compteur de molette, détection de **Super Glide** (Espace → C).

> Description GitHub (courte) :  
> *Overlay HTML temps réel pour stream : clavier AZERTY, souris, molette et Super Glide. Capture globale Windows, conçu pour OBS et TikTok Live Studio.*

## Pourquoi

OBS et TikTok ne voient pas tes inputs si tu colles juste un fichier HTML : la page n’a pas le focus pendant que tu joues.

Ce projet sépare deux rôles :

1. **Un petit serveur Windows** qui lit clavier et souris **même en jeu** (Raw Input).
2. **Une page HTML** affichée comme overlay, qui reçoit ces events en WebSocket.

Résultat : l’overlay réagit en live, sans plugin Input Overlay.

## Fonctionnalités

- Cluster AZERTY (1–5, Tab, AZER, Caps, QSDF, Shift + WXCVBN, Ctrl, Alt, Espace)
- Souris : M1, molette haut/bas, M2, pad de mouvement
- Point rose (blanc au clic gauche), wrap sur les bords, traînée
- Compteur de molette style score, reset si tu changes de sens
- **SUPERGLIDE** si Espace puis C à **1 frame** (réglé sur 138 fps)
- Overlay transparent pour OBS, fond vert pour TikTok (chroma key)

## Comment ça marche

```
Jeu / bureau
     │  Raw Input (clavier + souris)
     ▼
bridge.exe  ──WebSocket /ws──►  overlay.html
     │                              │
     └── HTTP :7689 ───────────────┘
                    │
         OBS Browser Source
         ou fenêtre Edge (TikTok)
```

- `bridge.exe` écoute le port **7689** et envoie du JSON (`key_pressed`, `mouse_moved`, `mouse_wheel`…).
- `overlay.html` dessine les touches et le pad.
- Tant que `start-overlay.bat` est ouvert, la capture continue.

## Prérequis

- Windows 10 / 11
- Microsoft Edge (pour TikTok)
- OBS Studio (pour Twitch / YouTube / TikTok via OBS)
- .NET Framework 4.x (déjà sur Windows) pour recompiler `bridge.exe` si besoin

## Installation

1. Clone ou copie le dossier :

```text
obs-overlay-rose/
  start-overlay.bat
  overlay.html
  bridge.exe
  bridge.cs
  yeshua.ps1
  ouvrir-fenetre-tiktok.bat
```

2. Double-clique **`start-overlay.bat`**.
3. Laisse cette fenêtre ouverte pendant tout le stream.

## OBS

1. `start-overlay.bat` lancé.
2. Source **Navigateur**.
3. Décoche **Fichier local**.
4. URL :

```text
http://127.0.0.1:7689/
```

5. Taille conseillée : **700 × 340**.
6. Fond transparent.
7. Ne pas « arrêter quand non visible ».

## TikTok Live Studio

TikTok **refuse** `127.0.0.1` et les tunnels ngrok gratuits (page « Visit Site »).

1. `start-overlay.bat` lancé.
2. Lance **`ouvrir-fenetre-tiktok.bat`** (petite fenêtre Edge, fond vert).
3. Dans TikTok : source **Fenêtre**, capture cette fenêtre Edge.
4. Active le **fond vert / chroma key**.

## Réglages (`overlay.html` → `CONFIG`)

| Clé | Défaut | Rôle |
|-----|--------|------|
| `sgFps` | `138` | FPS pour le timing Super Glide (1 frame) |
| `travelCm` | `2.1` | Sensibilité souris (plus petit = plus vif) |
| `mouseDecay` | `0.991` | Rappel vers le centre (plus proche de `1` = plus doux) |
| `padWidthCm` / `padHeightCm` | `45` / `39` | Proportions du tapis |
| `mouseDpi` | `800` | DPI souris pour le calcul |

Après une modif HTML : dans OBS, clic droit sur la source → **Actualiser**.

## Super Glide

Ordre : **Espace (jump)** puis **C (crouch)** une frame plus tard.

À 138 fps, 1 frame ≈ **7 ms**. Si le timing est bon, **SUPERGLIDE** s’affiche entre C et Espace.

## Fichiers

| Fichier | Rôle |
|---------|------|
| `start-overlay.bat` | Démarre le serveur + bannière |
| `overlay.html` | Interface overlay |
| `bridge.exe` | Capture globale + serveur HTTP/WS |
| `bridge.cs` | Source C# (recompilé si `bridge.exe` manque) |
| `ouvrir-fenetre-tiktok.bat` | Fenêtre Edge pour TikTok |
| `yeshua.ps1` | ASCII Yeshua au lancement |

## Licence

Usage personnel / stream. Adapte et partage librement.
