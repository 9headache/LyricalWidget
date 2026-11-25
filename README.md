<div align="center">

# LyricalWidget

**A lightweight Windows desktop lyrics overlay and 60 FPS fullscreen visualizer for Spotify and SMTC.**

[![Latest Release](https://img.shields.io/github/v/release/9headache/LyricalWidget?style=for-the-badge&labelColor=0E1016&color=E4E4E7)](https://github.com/9headache/LyricalWidget/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/9headache/LyricalWidget/total?style=for-the-badge&labelColor=0E1016&color=E4E4E7)](https://github.com/9headache/LyricalWidget/releases)
[![Stars](https://img.shields.io/github/stars/9headache/LyricalWidget?style=for-the-badge&labelColor=0E1016&color=A1A1AA)](https://github.com/9headache/LyricalWidget/stargazers)
![Platform](https://img.shields.io/badge/Windows-10%20%2F%2011-A1A1AA?style=for-the-badge&logo=windows&logoColor=E4E4E7&labelColor=0E1016)
![.NET 10](https://img.shields.io/badge/.NET%2010-WPF-A1A1AA?style=for-the-badge&logo=dotnet&logoColor=E4E4E7&labelColor=0E1016)

[**Download Latest `.exe`**](https://github.com/9headache/LyricalWidget/releases/latest) · [**Report a Bug**](https://github.com/9headache/LyricalWidget/issues/new) · [**Request a Feature**](https://github.com/9headache/LyricalWidget/issues/new)

</div>

---

## Live Demos

### Fullscreen Kinetic (`LiveCode` & `SpotlightFog`)

https://github.com/user-attachments/assets/a8525f2e-b613-4b09-bdd5-730abd115832

### In-Game Overlay (Click-Through & Gaming Mode)

https://github.com/user-attachments/assets/9a0483ba-2bb4-4608-8637-0eae3b832288

### Fullscreen Vibe Mode

https://github.com/user-attachments/assets/63cfff21-4ce7-4c4f-bc40-f8ed4ea03e6d

---

## Showcase

### Desktop Overlay & Idle Clock

| Transparent Lyrics | Glassify Lyrics |
| :---: | :---: |
| <img src="https://github.com/user-attachments/assets/f2f4efe7-2511-474d-a2de-077cf4a9eae0" width="420" alt="Transparent Lyrics Overlay" /> | <img src="https://github.com/user-attachments/assets/1ba71b31-c70d-40bb-a1ba-efadc59a9cc5" width="420" alt="Glassify Lyrics Overlay" /> |

| Transparent Idle Clock | Glassify Idle Clock |
| :---: | :---: |
| <img src="https://github.com/user-attachments/assets/7da7f988-dbf8-4858-b42a-de082c71b260" width="420" alt="Transparent Idle Clock" /> | <img src="https://github.com/user-attachments/assets/4c2f7c63-88a8-4257-bbe3-f056dfda7a7d" width="420" alt="Glassify Idle Clock" /> |

### Fullscreen Split Mode

<div align="center">
  <img src="https://github.com/user-attachments/assets/b8e4e8e0-975b-47c1-b9cd-44548b2acf0b" width="850" alt="Fullscreen Split Mode" />
</div>

---

## Features

* **SMTC sync:** reads playback straight from Windows Media Controls and interpolates to the millisecond. Works with Spotify and other desktop players.
* **Browser media:** pick the idle clock, synced lyrics, or the full player for YouTube and other browser playback.
* **Gaming overlay:** click-through lock, Gaming Mode, and **Transparent** or **Glassify** styles tinted by the album art.
* **Fullscreen Vibe Mode (`v2.5.0`):** **Split Mode** plus the **Moonlight Bloom**, **Live Code**, **Spotlight Fog**, **Editorial** and **Poster** styles, switchable while the song plays.
* **1200×1200 album art:** fetched automatically, with a color backdrop when there is no high-resolution cover.
* **Idle clock:** takes over when the music pauses.
* **Lyric cache:** asks LRCLIB and NetEase in parallel and keeps synced lyrics on disk for 30 days.
* **Auto-updater:** checks GitHub Releases and updates in place without touching your settings.

---

## Quick Start

1. Download **`LyricalWidget.exe`** from the [**Latest Release**](https://github.com/9headache/LyricalWidget/releases/latest) page. It is standalone, so you do not need to install .NET.
2. Double-click to launch.
   > *The binary is not code-signed, so Windows SmartScreen may warn on first launch. Click **More info → Run anyway**.*
3. Play a song on Spotify and right-click the system tray icon to switch themes, lock position, or open Fullscreen Mode.

---

<details>
<summary><b>Building From Source (.NET 10 SDK)</b></summary>

```powershell
git clone https://github.com/9headache/LyricalWidget.git
cd LyricalWidget
dotnet run
```

</details>
