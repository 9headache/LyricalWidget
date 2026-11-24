# LyricalWidget

A lightweight, high-performance Windows desktop lyrics overlay and fullscreen kinetic visualizer built with C# and WPF (.NET 10).

## Download

Grab the latest standalone `LyricalWidget.exe` from the [**Releases**](https://github.com/9headache/LyricalWidget/releases/latest) page—no installer or .NET runtime required.

> **Note:** Because the app isn't code-signed yet, Windows SmartScreen may show a warning on first launch. Click **More info → Run anyway** to start.

## Features

- **Real-Time Sync:** Hooks directly into Windows Media Controls (SMTC) to stay synced with Spotify and other desktop players with millisecond interpolation.
- **Word-Synced Fluid Wave:** Smooth spring-physics lyric animation with dynamic palette extraction pulled directly from the current album cover.
- **Desktop Overlay Modes:** Switch between clean **Transparent** floating text and **Glassify** frosted cards, with click-through locking, Gaming Mode, and a minimal **Idle Clock** when paused.
- **Fullscreen Vibe Mode:**
  - **Split Mode:** Swirling multi-color album art backdrop with synchronized lyrics and hover media/volume controls.
  - **Kinetic Mode (Beta):** 60 FPS procedural typography featuring **Editorial** (serif/glass) and **Poster** (night-safe muted album palette, brutalist layouts, semantic triggers, and Dim/Vivid toggle).
- **Fast Offline Caching:** Races LRCLIB and NetEase in parallel and caches synced lyrics locally for 30 days.
- **Built-In Auto-Updater:** Checks GitHub Releases automatically and updates in place without touching your saved settings or lyric cache.

## Building From Source

Requires Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/).

```powershell
git clone https://github.com/9headache/LyricalWidget.git
cd LyricalWidget
dotnet run
```

To build a single-file standalone executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "./Release"
```

## Bug Reports & Feedback

Found a bug or have a feature idea? Open an issue on the [Issues page](https://github.com/9headache/LyricalWidget/issues/new).