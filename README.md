# Steam Achievement Manager (v7.0 Modern Rewrite)

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0--windows-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Architecture](https://img.shields.io/badge/Architecture-x86-blue)](#system-requirements)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?logo=windows)](https://microsoft.com)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.txt)

A complete modern rewrite of the classic **Steam Achievement Manager (SAM)**. Built from the ground up on modern C# 12 and .NET 8.0, featuring a sleek dark-themed WPF interface, asynchronous architecture, bulletproof Steam IPC stability, and high-performance list virtualization.

---

## 🚀 Key Improvements & Architecture (Legacy vs Modern)

| Feature | Legacy SAM (2008–2013) | Modern SAM 7.0 (Rewrite) |
| :--- | :--- | :--- |
| **Framework** | .NET Framework 3.5 / WinForms | **.NET 8.0 Desktop Runtime (WPF)** |
| **UI Theme** | Standard Windows Forms Light UI | **Modern WPF Dark Theme** with Custom Title Bar |
| **List Performance** | High memory usage & UI lag on large libraries | **Custom `VirtualizingWrapPanel`** & LRU Byte-Bounded Image Cache |
| **Search Filtering** | Rebuilt UI controls per keystroke | **`BulkObservableCollection`** (Single `Reset` event per search pass) |
| **Steam Pump Stability** | Single exception breaks callback loop permanently | **Unkillable Callback Pump** with `try/finally` re-entrancy protection |
| **Thread Safety** | Native C++ pipe calls executed on GC Finalizer thread | **Strict Thread Affinity** (Deterministic UI thread cleanup only) |
| **Pipe Disconnection** | Silent failures on Steam disconnect | **Automatic Pipe Health Monitoring** & UI Command Gating |
| **Architecture** | Coupled procedural code | **Clean MVVM Pattern** (`SAM.API`, `SAM.Core`, `SAM.UI`, `SAM.Picker`, `SAM.Game`) |

---

## ✨ Features

* **Instant Library Search:** Filter through 5,000+ owned Steam games smoothly without frame drops.
* **Full Achievement Management:** Unlock, lock, or invert achievements individually or in bulk.
* **Stat Editing Support:** View and modify integer/float player statistics directly.
* **Disconnection Safety Banner:** Real-time IPC health check automatically disables dangerous commands if Steam closes mid-session.
* **3-Step Destructive Action Confirmation:** Integrated safety confirmations (`IDialogService`) prevent accidental full-achievement resets.
* **High-DPI Awareness:** Native sharp rendering on 4K and scaled displays.

---

## 💻 System Requirements

* **Operating System:** Windows 10 (Build 1809+) or Windows 11.
* **Runtime:** [.NET Desktop Runtime 8.0 (x86)](https://dotnet.microsoft.com/download/dotnet/8.0).
  > **Note:** The **x86 (32-bit)** runtime is strictly required because SAM interacts directly with Steam's 32-bit `steamclient.dll` native pipe.
* **Steam Client:** Steam must be installed, running, and logged into an active account.

---

## 🏗️ Solution Architecture

The repository is organized into five modular projects following clean architecture guidelines:

```text
SteamAchievementManager/
├── SAM.API/      # Native Interop wrappers & robust Steam IPC Callback Pump
├── SAM.Core/     # ViewModels, Asset Caching, Interfaces & Pure Business Logic
├── SAM.UI/       # WPF Shared Controls, Dark Styles, Converters & Virtualizing Panel
├── SAM.Picker/   # Game Library Selection Window & Application Entry Point
└── SAM.Game/     # Achievement & Statistics Management Window
