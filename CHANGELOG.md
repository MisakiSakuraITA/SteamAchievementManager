# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [7.2.2] - 2026-08-29

### Changed

- **Redesigned "More Actions" Menu**: Transformed the utility dropdown into a sleek, modern
  popup panel with dark-theme styling, rounded corners, hover feedback, and clear visual
  hierarchy.

### Removed

- **URI Protocol Handler (`sam://`)**: Completely removed the registry-backed URI scheme,
  fast-path CLI arguments, and related UI toggles to simplify the user experience and reduce
  codebase surface area.

## [7.2.1] - 2026-08-29

### Added

- **Active Steam Profile Badge**: Displays the current Steam account's persona name and
  SteamID64 in the header of both windows.
- **Custom Protocol Handler (`sam://`)**: An opt-in, per-user `sam://game/{appid}` URI
  handler for launching SAM directly from Playnite, LaunchBox, or the Windows Run dialog.

### Changed

- **UI Simplification & Polish**: Redesigned toolbars with clean iconography, a unified
  visual hierarchy, decluttered headers, and improved dark theme spacing.

## [7.2.0] - 2026-08-28

### Added

- **Secret Achievement Revealer**: Hover peek and global toolbar toggle to reveal hidden
  achievement details.
- **Global Rarity Percentages**: Real-time Steam rarity data with gold highlights for
  ultra-rare achievements (< 5%).
- **Advanced Filtering & Sorting**: Sort by A-Z, unlock status, rarity, or hidden status;
  quick-filter by hidden or ultra-rare.
- **Queued Batch Store**: Sequential unlock store queue with live status-bar progress and
  cancellation support.
- **Snapshot Export/Import**: Export and restore achievements/statistics state to/from JSON
  or CSV backup files.

## [7.1.2] - UX Polish, Keyboard Shortcuts & Accessibility Pass

### Added

- Keyboard shortcuts: `Ctrl+F` focuses search, `Esc` clears it, `F5` refreshes/reloads,
  `Ctrl+S` stores pending changes (achievement manager), and `Ctrl+Enter` launches the
  selected game (picker). Handled window-wide so they work regardless of which control
  currently has focus.
- Access keys (`Alt+`) on the achievement manager's tab headers and on the primary,
  clearly-labeled toolbar buttons in both windows.
- Non-blocking, dismissible notification banners with an optional Retry action, replacing
  blocking `MessageBox` dialogs for transient errors and store/info confirmations. Dialogs
  that gate a decision (unsaved-changes-on-close, the three-step stat reset confirmation)
  are unchanged.
- A search/filter match count in the achievement manager's status bar ("Showing 12 of 45
  achievements"), matching the game picker's existing behavior.
- Up/Down arrow-key traversal of the achievement and statistic lists, including across
  virtualized-container recycling boundaries.
- `AutomationProperties.Name` on icon-only controls and the per-row unlock checkbox, and
  `AutomationProperties.LiveSetting` on both status bars and the notification banner, for
  screen reader users.
- Root `Directory.Build.props` centralizing the MSBuild properties shared by all six
  projects (target framework, language version, platform, versioning, repository metadata).
- Root `.editorconfig` codifying the project's existing code style.

### Changed

- `ListBoxItem.Row` is keyboard-focusable again, with a focus indicator that is deliberately
  distinct from a selection highlight, since these rows are browsed rather than picked from.

### Removed

- The obsolete, FxCop-era `GlobalSuppressions.cs` — no analyzer in the current toolchain
  ever read it.
- The unused `PercentageToWidthConverter`.

### Fixed

- `NativeStrings` is now declared `static` (it only ever held static members).
- `VirtualizingWrapPanel.SetHorizontalOffset` is now documented to explain why its
  parameter is intentionally ignored, rather than reading as an oversight.

## [7.1.1] - Performance Offloading & Cache Resilience

### Added

- The asset cache now distinguishes a transient network failure (timeout, dropped
  connection, a 5xx status) from a permanent one (404), retrying the former automatically
  after a short delay instead of blocking that asset for the rest of the session.
- Cache folder resolution now proves a candidate directory is genuinely writable with an
  actual temporary-file write, rather than trusting `Directory.Exists`/`CreateDirectory`
  alone, which can succeed against a directory the process cannot actually write into.

### Changed

- Image decoding now runs with `ConfigureAwait(false)`, avoiding an unnecessary hop back to
  the UI thread partway through a cache miss.
- `VirtualizingWrapPanel` realizes a one-row overscan buffer above and below the visible
  viewport, reducing container churn during fast scrolling.
- `ImageSourceCache.Store` now returns whichever instance actually won a race between two
  concurrent decodes of the same identity, so every caller ends up sharing one bitmap
  instead of each holding an uncounted copy.
- Reloading the achievement manager now diffs the incoming schema against the existing view
  models by id and updates them in place, rather than rebuilding the achievement and
  statistic lists from scratch on every reload.

## [7.1.0] - Critical Data Safety & Parser Hardening

### Fixed

- An unsolicited `UserStatsReceived` callback — Steam can redeliver this on its own
  schedule, not only in response to a request the app made — no longer silently discards a
  pending, unstored achievement or statistic edit.
- Achievement callbacks are now filtered strictly by the reported AppId.
- The stat schema parser skips a stat of an unrecognized type instead of failing the whole
  parse over it.
- Binary KeyValue parsing now caps recursion depth, preventing a stack overflow from a
  malformed or adversarial file.
- The Steam callback pump now runs at input priority, so it stays responsive rather than
  being starved by lower-priority work.

## [7.0.0] - Complete .NET 8 / WPF Architecture Rewrite

### Added

- Full rewrite onto .NET 8 and C# 12, replacing the legacy WinForms / .NET Framework 3.5
  codebase.
- Clean MVVM separation across six projects: `SAM.API`, `SAM.Core`, `SAM.UI`,
  `SAM.Picker`, `SAM.Game`, and `SAM.Tests`.
- Native interop with Steam's 32-bit `steamclient.dll` matching its `ThisCall` calling
  convention.
- A dark-themed WPF interface with a custom title bar.
- An LRU, byte-budget-bounded image cache for game capsule art and achievement icons.
- An unkillable Steam callback pump: a fault raised by one subscriber no longer breaks
  callback delivery for the rest of the session.
- Automatic Steam pipe disconnection detection that gates destructive commands once the
  connection is lost.

[Unreleased]: https://github.com/MisakiSakuraITA/SteamAchievementManager/compare/v7.2.2...HEAD
[7.2.2]: https://github.com/MisakiSakuraITA/SteamAchievementManager/compare/v7.2.1...v7.2.2
[7.2.1]: https://github.com/MisakiSakuraITA/SteamAchievementManager/compare/v7.2.0...v7.2.1
[7.2.0]: https://github.com/MisakiSakuraITA/SteamAchievementManager/compare/v7.1.2...v7.2.0
[7.1.2]: https://github.com/MisakiSakuraITA/SteamAchievementManager/compare/v7.1.1...v7.1.2
[7.1.1]: https://github.com/MisakiSakuraITA/SteamAchievementManager/compare/v7.1.0...v7.1.1
[7.1.0]: https://github.com/MisakiSakuraITA/SteamAchievementManager/compare/v7.0.0...v7.1.0
[7.0.0]: https://github.com/MisakiSakuraITA/SteamAchievementManager/releases/tag/v7.0.0
