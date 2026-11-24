<div align="center">

# LyricalWidget

**A lightweight Windows desktop lyrics overlay and 60 FPS fullscreen visualizer for Spotify and SMTC.**

[![Latest Release](https://img.shields.io/github/v/release/9headache/LyricalWidget?style=for-the-badge&color=ff4b6e)](https://github.com/9headache/LyricalWidget/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/9headache/LyricalWidget/total?style=for-the-badge&color=6e56cf)](https://github.com/9headache/LyricalWidget/releases)
[![Stars](https://img.shields.io/github/stars/9headache/LyricalWidget?style=for-the-badge&color=f5a623)](https://github.com/9headache/LyricalWidget/stargazers)
![Platform](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6?style=for-the-badge&logo=windows)
![.NET 10](https://img.shields.io/badge/.NET%2010-WPF-512BD4?style=for-the-badge&logo=dotnet)

[**Download Latest `.exe`**](https://github.com/9headache/LyricalWidget/releases/latest) · [**Report a Bug**](https://github.com/9headache/LyricalWidget/issues/new) · [**Request a Feature**](https://github.com/9headache/LyricalWidget/issues/new)

</div>

---

## Live Demos

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

* **Real-Time SMTC Sync** — Hooks directly into Windows Media Controls for millisecond-interpolated sync with Spotify and desktop players.
* **Gaming-Friendly Overlay** — Click-through locking, dedicated Gaming Mode, and **Transparent** or **Glassify** frosted card styles with dynamic album art colors.
* **Minimal Idle Clock** — Automatically transitions to a floating or glass clock when music pauses.
* **Fullscreen Vibe Mode** — Features **Split Mode** (swirling multi-color album art backdrop with hover media and volume controls) plus an experimental **60 FPS Kinetic Mode (Beta)** with Editorial and night-safe Poster presets.
* **Fast Offline Caching** — Races LRCLIB and NetEase in parallel and caches synced lyrics locally for 30 days.
* **Built-In Auto-Updater** — Checks GitHub Releases automatically and updates in place without touching your saved settings.

---

## Quick Start

1. Download **`LyricalWidget.exe`** from the [**Latest Release**](https://github.com/9headache/LyricalWidget/releases/latest) page (standalone—no .NET install required).
2. Double-click to launch.
   > *Windows SmartScreen may show a prompt on first launch since the binary is not code-signed—click **More info → Run anyway**.*
3. Play a song on Spotify and right-click the system tray icon to switch themes, lock position, or open Fullscreen Mode.

---

<details>
<summary><b>Building From Source (.NET 10 SDK)</b></summary>

```powershell
git clone [https://github.com/9headache/LyricalWidget.git](https://github.com/9headache/LyricalWidget.git)
cd LyricalWidget
dotnet run
```

Build a single-file standalone `.exe`:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "./Release"
```

</details>

---

<div align="center">

*If LyricalWidget upgrades your desktop or gaming setup, consider starring the repository.*

</div>