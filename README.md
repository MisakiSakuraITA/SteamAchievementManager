# Steam Achievement Manager

[![CI](https://github.com/MisakiSakuraITA/SteamAchievementManager/actions/workflows/ci.yml/badge.svg)](https://github.com/MisakiSakuraITA/SteamAchievementManager/actions/workflows/ci.yml)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0--windows-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Version](https://img.shields.io/badge/version-7.1.2-blue.svg)](CHANGELOG.md)
[![Tests](https://img.shields.io/badge/tests-155%20passing-brightgreen.svg)](https://github.com/MisakiSakuraITA/SteamAchievementManager/actions/workflows/ci.yml)
[![License: zlib](https://img.shields.io/badge/License-zlib-lightgrey.svg)](LICENSE.txt)
[![Steam API](https://img.shields.io/badge/Steam-API%20compatible-1b2838?logo=steam)](https://partner.steamgames.com/)

A tool for viewing and editing your own achievements and statistics for games you own on
Steam, entirely through Steam's own local API — no save-file editing, no memory patching.

This is a complete rewrite of the original 2008-era WinForms tool onto .NET 8 and WPF, with
a redesigned, testable architecture underneath it.

---

## Overview & Architecture

SAM talks to a Steam client already running on the same machine over Steam's local IPC pipe
(`steamclient.dll`), the same interface Steam itself uses to expose achievement and
statistic data to games. Because that pipe is a 32-bit native module, the whole solution —
every project, every published binary — targets **x86**, even on a 64-bit system; there is
no way around this without Steam changing its own ABI.

The application is split into two shells sharing one core:

- **SAM.Picker** lists the games you own and launches **SAM.Game** for whichever one you
  select, passing its App ID on the command line.
- **SAM.Game** connects to Steam for that one App ID and lets you view, unlock, lock, or
  invert individual achievements, and read or edit integer/float statistics.

Both shells are built on the same MVVM foundation: view models in `SAM.Core` hold no
reference to WPF at all, native interop is isolated in `SAM.API` behind interfaces the view
models actually depend on, and the WPF-specific pieces (theme, custom controls, image
decoding) live in `SAM.UI`, shared by both shells. See
[Project Structure](#project-structure) below for the full breakdown.

## Legacy vs. Modern

| | Legacy SAM (2008, WinForms) | SAM 7.1.2 (this project) |
| --- | --- | --- |
| **Framework** | .NET Framework 3.5, WinForms | .NET 8, WPF, C# 12 |
| **UI** | Standard light-themed WinForms controls | Dark-themed WPF UI, virtualized card/row lists, keyboard shortcuts and access keys, screen-reader labels |
| **Performance** | Every list item's full-size art loaded and held; UI controls rebuilt on every keystroke while searching | Virtualized wrap panel with an overscan buffer, an LRU byte-budgeted image cache, off-UI-thread image decoding, a single `Reset` per search/filter pass |
| **Thread Safety** | Native calls could end up running on the GC finalizer thread | Strict UI-thread affinity for native calls, with an off-thread decode path that hands back to the UI thread deliberately |
| **Reliability** | One subscriber throwing could break the callback loop for the rest of the session; a Steam disconnect failed silently | Per-subscriber fault isolation in the callback pump, automatic disconnect detection that gates every command that writes to Steam, and transient-vs-permanent classification for network failures so a blip doesn't lock an asset out for good |

## Features

- **Full achievement management** — unlock, lock, or invert achievements individually or in
  bulk, with a three-step confirmation before anything destructive.
- **Statistic editing** — view and modify integer and float player statistics directly, with
  live validation against each stat's declared range and increment-only rules.
- **Instant search & filtering** — narrow a library of thousands of games, or a game's own
  achievement list, without a frame drop; the status bar reports the live match count.
- **High-performance virtualized scrolling** — a custom `VirtualizingWrapPanel` realizes only
  the rows near the viewport, with a small overscan buffer so fast scrolling doesn't churn
  containers.
- **Keyboard-first navigation** — window-wide shortcuts, access keys on tabs and buttons, and
  Up/Down arrow-key traversal of virtualized lists. See the [table below](#keyboard-shortcuts).
- **Non-blocking notification banners** — transient errors and confirmations surface as a
  dismissible banner with an optional Retry action, instead of a modal dialog that blocks
  everything else until it's clicked away.
- **Disconnection safety** — a persistent banner and automatic command gating the moment
  Steam's pipe goes away, so nothing is attempted against a dead connection.
- **Dark theme throughout**, including a custom-painted title bar and high-DPI-aware
  rendering.

## Keyboard Shortcuts

| Shortcut | Where | Action |
| --- | --- | --- |
| `Ctrl+F` | Picker & Achievement Manager | Focus the search box |
| `Esc` | Picker & Achievement Manager | Clear the search box |
| `F5` | Picker & Achievement Manager | Refresh the game list / reload achievements & stats |
| `Ctrl+S` | Achievement Manager | Store pending changes |
| `Ctrl+Enter` | Picker | Launch the selected game |
| `Enter` | Picker (game list focused) | Launch the selected game |
| `↑` / `↓` | Achievement Manager | Move keyboard focus through the achievement or statistic list, one row at a time |
| `Alt+`&nbsp;*(underlined letter)* | Picker & Achievement Manager | Activate the matching tab or toolbar button |

## System Requirements

- **Operating system:** Windows 10 (1809+) or Windows 11.
- **Runtime:** [.NET Desktop Runtime 8.0 (x86)](https://dotnet.microsoft.com/download/dotnet/8.0).
  The **x86** build specifically — SAM talks to Steam's 32-bit `steamclient.dll` directly,
  so the x64 runtime cannot run it.
- **Steam:** installed, running, and logged into the account that owns the games you want
  to manage.

## Building & Testing

```bash
git clone https://github.com/MisakiSakuraITA/SteamAchievementManager.git
cd SteamAchievementManager
dotnet restore SAM.sln
dotnet build SAM.sln -c Release
dotnet test SAM.sln
```

`dotnet build SAM.sln -t:Rebuild` forces a full rebuild of every project (Debug and Release
are both expected to complete with 0 warnings and 0 errors). Building requires the .NET 8
SDK and, because of the x86 platform requirement above, a Windows machine — the solution
cannot be built on Linux or macOS.

## Project Structure

| Project | Purpose |
| --- | --- |
| **SAM.API** | The native interop layer: `ThisCall`-ABI wrappers around `steamclient.dll`'s vtables, the Steam callback pump, and native string marshaling. |
| **SAM.Core** | Platform-agnostic core: view models, the asset/disk cache, Steam service interfaces, and schema/KeyValue parsing. Deliberately has no reference to WPF or any other UI framework. |
| **SAM.UI** | The shared WPF layer: the dark theme, custom controls (`VirtualizingWrapPanel`, `CachedImage`, `NotificationBanner`), value converters, and the in-memory image cache. |
| **SAM.Picker** | The game-picker shell — lists owned games and launches `SAM.Game` for the one you choose. Application entry point. |
| **SAM.Game** | The achievement & statistics manager shell for a single game. Application entry point. |
| **SAM.Tests** | The xunit test suite — 155 tests covering the view models, caching, native string handling, and the WPF controls. |

## Security

See [SECURITY.md](SECURITY.md) for supported versions and how to report a vulnerability
privately.

## License

Licensed under the [zlib License](LICENSE.txt).
