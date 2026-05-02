# StemForge

Cross-platform desktop app for AI-powered audio stem separation. Wraps the [`audio-separator`](https://github.com/nomadkaraoke/python-audio-separator) Python library in a polished GUI, with a preset-first workflow and a built-in setup wizard.

## Features

- Browse and search hundreds of separation models
- Built-in curated presets (vocals, instrumentals, karaoke, …)
- Create and save custom presets from any model or ensemble
- Job queue with per-job progress tracking
- Session logging with file rollover

## Stack

- C# / .NET 10
- [Avalonia UI](https://avaloniaui.net/) 12 (Windows, macOS, Linux)
- CommunityToolkit.Mvvm

## For non-technical users (Windows)

1. Download the latest `StemForge-win-x64.zip` from the [Releases](../../releases) page.
2. Extract the ZIP anywhere and run `StemForge.exe`.
3. The setup wizard will offer to install [uv](https://github.com/astral-sh/uv) and `audio-separator` automatically — no manual Python setup needed.

> **GPU note:** for best performance you need an NVIDIA GPU. The wizard will ask which variant to install (CPU, CUDA, or ROCm).

## For developers

### Build

```
dotnet build
dotnet run --project src/StemForge
```

### Publish a self-contained Windows EXE

Run the **"publish: win-x64 (self-contained)"** task in VS Code (`Ctrl+Shift+B` → select task), or from the terminal:

```
dotnet publish src/StemForge --runtime win-x64 --self-contained -c Release -o publish/win-x64
```

Output lands in `publish/win-x64/`. Zip that folder and share it — no .NET installation required on the target machine.

### Project layout

```
src/StemForge/
  App.axaml              application entry + theme resources
  Styles/                design tokens (colors, typography, spacing)
  Views/                 XAML views
  ViewModels/            view models (CommunityToolkit.Mvvm)
  Models/                domain models
  Services/              separation engine, queue, settings, logging
```
