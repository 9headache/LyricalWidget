<div align="center">

<img src="Assets/logo.svg" width="84" alt="LyricalWidget Logo" />

# LyricalWidget

**A lightweight Windows desktop lyrics overlay and 60 FPS fullscreen visualizer for Spotify and SMTC.**

[![Latest Release](https://img.shields.io/github/v/release/9headache/LyricalWidget?style=for-the-badge&labelColor=0E1016&color=E4E4E7)](https://github.com/9headache/LyricalWidget/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/9headache/LyricalWidget/total?style=for-the-badge&labelColor=0E1016&color=E4E4E7)](https://github.com/9headache/LyricalWidget/releases)
[![Stars](https://img.shields.io/github/stars/9headache/LyricalWidget?style=for-the-badge&labelColor=0E1016&color=A1A1AA)](https://github.com/9headache/LyricalWidget/stargazers)
![Platform](https://img.shields.io/badge/Windows-10%20%2F%2011-A1A1AA?style=for-the-badge&logo=windows&logoColor=E4E4E7&labelColor=0E1016)
![.NET 10](https://img.shields.io/badge/.NET%2010-WPF-A1A1AA?style=for-the-badge&logo=dotnet&logoColor=E4E4E7&labelColor=0E1016)

[**Website**](https://9headache.github.io/) · [**Download Latest `.exe`**](https://github.com/9headache/LyricalWidget/releases/latest) · [**Report a Bug**](https://github.com/9headache/LyricalWidget/issues/new) · [**Request a Feature**](https://github.com/9headache/LyricalWidget/issues/new)

</div>

---

## Live Demos

### Fullscreen Vibe Mode (`Moonlight Bloom`, `Fisheye` & `Ambient Idle`)

https://github.com/user-attachments/assets/7f95c43a-cab8-44f3-9e3a-1ea7145f7861

### Desktop Widget (Quick-Access Bars & Album Art Resizer)

https://github.com/user-attachments/assets/9fb7336d-8aee-4abd-bf71-808c141dafab

### In-Game Overlay (Click-Through & Gaming Mode)

https://github.com/user-attachments/assets/e03d3bb2-3998-4b7f-84cf-8a2fea876b71

---

## Showcase

### Desktop Overlay & Idle Clock

| Midnight Bloom Lyrics | Glassify Lyrics | Transparent Lyrics |
| :---: | :---: | :---: |
| <img src="https://github.com/user-attachments/assets/370944c2-ef6f-4776-8fe8-203bda4a1009" width="320" alt="Midnight Bloom Lyrics Overlay" /> | <img src="https://github.com/user-attachments/assets/dd284b98-f8b3-41e6-81bd-f44565c333a6" width="320" alt="Glassify Lyrics Overlay" /> | <img src="https://github.com/user-attachments/assets/2047be45-187b-40a8-8400-1f02271dcd87" width="320" alt="Transparent Lyrics Overlay" /> |

| Transparent Idle Clock | Glassify Idle Clock |
| :---: | :---: |
| <img src="https://github.com/user-attachments/assets/f31d2ca6-1c87-4d2f-911d-25e8951b5d64" width="420" alt="Transparent Idle Clock" /> | <img src="https://github.com/user-attachments/assets/d4e1497d-ebc1-444f-b896-2f01e150d836" width="420" alt="Glassify Idle Clock" /> |


---

## Features

* **SMTC sync:** reads playback straight from Windows Media Controls and interpolates to the millisecond. Works with Spotify and other desktop players.
* **Browser media:** pick the idle clock, synced lyrics, or the full player for YouTube and other browser playback.
* **Gaming overlay:** click-through lock (`Alt + L`), Gaming Mode, quick-access bars, and **Midnight Bloom**, **Glassify**, or **Transparent** styles tinted by the album art.
* **Resizable album art:** scale the widget cover between `36px` and `180px` (`150px` default) with presets or a live slider and zero corner clipping.
* **Fullscreen & Windowed Vibe Mode (`v2.5.0`):** runs as a resizable, Windows-Snappable borderless window (`F11` / `Esc` for fullscreen) with multi-monitor selection, **Split Mode**, **Moonlight Bloom** (with optional 60 FPS GPU **Fisheye** lens), **Live Code**, **Spotlight Fog**, **Editorial**, and **Poster** styles.
* **1200×1200 album art:** fetched automatically, with a color backdrop when there is no high-resolution cover.
* **Ambient Idle clock:** takes over smoothly with subtle particle drift when the music pauses.
* **Lyric cache:** asks LRCLIB and NetEase in parallel and keeps synced lyrics on disk for 30 days.
* **Auto-updater:** checks GitHub Releases and updates in place without touching your settings.

---

## Quick Start

1. Download **`LyricalWidget.exe`** from the [**Latest Release**](https://github.com/9headache/LyricalWidget/releases/latest) page. It is standalone, so you do not need to install .NET.
2. Double-click to launch.
   > *The binary is not code-signed, so Windows SmartScreen may warn on first launch. Click **More info → Run anyway**.*
3. Play a song on Spotify and right-click the system tray icon to switch themes, lock position, or open Vibe Mode.

---

<details>
<summary><b>Building From Source (.NET 10 SDK)</b></summary>

```powershell
git clone [https://github.com/9headache/LyricalWidget.git](https://github.com/9headache/LyricalWidget.git)
cd LyricalWidget
dotnet run
