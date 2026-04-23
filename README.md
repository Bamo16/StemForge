# StemForge

Cross-platform desktop app for AI-powered audio stem separation. Wraps the [`audio-separator`](https://github.com/nomadkaraoke/python-audio-separator) Python library in a polished GUI, with a preset-first workflow.

## Stack

- C# / .NET 10
- [Avalonia UI](https://avaloniaui.net/) 12 (Windows, macOS, Linux)
- CommunityToolkit.Mvvm

## Structure

```
src/StemForge/
  App.axaml              application entry + theme resources
  Styles/                design tokens (colors, typography, spacing)
  Views/                 XAML views
  ViewModels/            view models (CommunityToolkit.Mvvm)
  Models/                domain models
```

## Build

```
dotnet build
dotnet run --project src/StemForge
```
