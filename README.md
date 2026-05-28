<p align="center">
  <img src="docs/images/stemforge.svg" alt="StemForge logo" width="120" />
</p>

<h1 align="center">StemForge</h1>

<p align="center">
  Cross-platform desktop app for AI-powered audio stem separation. A polished Avalonia GUI on top of the <a href="https://github.com/nomadkaraoke/python-audio-separator"><code>audio-separator</code></a> Python library with a preset-first workflow, built-in setup wizard, queueable jobs, and YouTube URL ingestion.
</p>

---

## Hear it

Vocal and instrumental stems pulled from [Karyuu &amp; Jaylenn - Another Life](https://music.youtube.com/watch?v=G52OPfQUiZ0) ([NCS](https://ncs.io), royalty-free), separated with StemForge.

**Vocal (Full) preset**

https://github.com/user-attachments/assets/0cca8ecc-d786-4aba-8998-6ebe9ccf793b

**Instrumental (Full) preset**

https://github.com/user-attachments/assets/660daf67-0953-405d-a6ef-1c71e59f6a7b

---

## What it does

Drop in an audio file (or paste a YouTube URL) and StemForge runs one of dozens of separation models, or an ensemble of them, to split the track into stems: vocals, instrumentals, drums, bass, and so on. Pick from the curated built-in presets, or browse the full model catalogue and roll your own.

<p align="center">
  <img src="docs/images/screenshot-separate-presets.png" alt="Separate page with presets" width="900" />
</p>

### URL ingestion with format selection

Paste a YouTube link and StemForge resolves the available audio formats via `yt-dlp`, picks the best one automatically, and surfaces a picker if you want to override. Any URL `yt-dlp` supports works in principle; YouTube is the most common case, but the same flow handles other sources. Premium audio formats (higher-bitrate opus and AAC) show up when available. See [YouTube authentication](#youtube-authentication-cookies-premium-formats) below if you want StemForge to use them.

<p align="center">
  <img src="docs/images/screenshot-separate-url.png" alt="URL pasted with format picker expanded" width="900" />
</p>

### Job queue

Queue multiple jobs and watch them progress one at a time. Failed jobs surface their error inline.

<p align="center">
  <img src="docs/images/screenshot-queue.png" alt="Queue page mid-job" width="900" />
</p>

### Model catalogue

Browse hundreds of community models from the audio-separator catalogue. Save any combination as a custom preset.

<p align="center">
  <img src="docs/images/screenshot-models.png" alt="Models page" width="900" />
</p>

### Settings

Configure the output directory, default audio format, tool-path overrides, YouTube cookie source, and the GPU variant audio-separator runs on.

<p align="center">
  <img src="docs/images/screenshot-settings.png" alt="Settings page" width="900" />
</p>

---

## Getting started (Windows)

1. Open the [Releases](../../releases) page and download the latest `StemForge-vX.Y.Z-win-x64.zip`.
2. Extract anywhere. You'll get a `StemForge/` folder; double-click `StemForge.exe` inside it. No .NET install required.
3. **First-run wizard** offers to install everything you need:
   - `uv`: Python tool manager, ~15 MB, installed via [Astral's official installer](https://astral.sh/uv).
   - `audio-separator`: the separation engine, installed as a uv tool.
   - `ffmpeg`: ~100 MB, bundled binary from [`yt-dlp/FFmpeg-Builds`](https://github.com/yt-dlp/FFmpeg-Builds), dropped into `%LOCALAPPDATA%\StemForge\bin`.
   - `yt-dlp` *(optional)*: only needed for URL downloads.
   - `deno` *(optional, ~42 MB)*: JS runtime, needed for some YouTube URL workflows. See [YouTube authentication](#youtube-authentication-cookies-premium-formats) for when this matters.
4. Pick your GPU variant: **CPU**, **CUDA** (NVIDIA), or **DirectML** (any modern Windows GPU). The wizard auto-detects what you have.

> **Windows SmartScreen on first launch.** Because `StemForge.exe` isn't code-signed yet, Windows will probably show a *"Windows protected your PC"* prompt the first time you run it. Click **More info** and then **Run anyway** to proceed. This is the standard warning for any unsigned executable; nothing in StemForge needs admin rights or alters system settings.

If you already have any of these tools on your PATH, the wizard detects them and skips re-installing.

### Where StemForge puts things

| What | Where |
|---|---|
| Stem outputs | `~/Music/Stems` (configurable in Settings) |
| Downloaded models | `%LOCALAPPDATA%\audio-separator\models` |
| Bundled ffmpeg + deno | `%LOCALAPPDATA%\StemForge\bin` |
| App settings | `%APPDATA%\StemForge\settings.json` |
| Drum-stem cache | `%LOCALAPPDATA%\StemForge\drum-cache` |

---

## YouTube authentication, cookies, premium formats

YouTube's audio-only formats fall into two tiers:

- **Public**: ~128 kbps is the ceiling without authentication.
- **Premium-only**: ~256 kbps opus or AAC, requires a YouTube Premium account *and* authenticated requests.

To unlock the premium tier, StemForge passes browser cookies to `yt-dlp`. Open **Settings → yt-dlp → Cookies from browser** and put your browser name there (`firefox`, `chrome`, `edge`, etc.). yt-dlp reads the cookies straight from the browser's storage at extraction time, so you stay signed in normally with no separate export step. [yt-dlp's cookie documentation](https://github.com/yt-dlp/yt-dlp/wiki/FAQ#how-do-i-pass-cookies-to-yt-dlp) covers caveats and alternative cookie sources.

### Why deno

YouTube serves dynamic "n-parameter" challenges that yt-dlp needs to evaluate in a JS runtime before it can construct the final download URL. Without one, extraction may fall back to image-only formats and report `Requested format is not available`. The exact triggers vary by yt-dlp version and YouTube's current behaviour; authenticated and premium requests appear to hit them more often, but they happen on plain public extraction too.

Bundling deno via the setup wizard is the safe default. If you already have deno (or node, or bun) on your PATH, yt-dlp picks it up automatically and StemForge's bundled copy isn't strictly required.

---

## Status

StemForge is pre-v0 and shared mostly with friends and early testers. **User presets work but aren't fully fleshed-out yet.** Expect rough edges in the editor and minimal validation. The built-in preset library is the recommended starting point.

**Windows-only in practice.** The codebase is Avalonia and was written with cross-platform in mind, but only the Windows path is regularly tested. Building and running on macOS or Linux will likely surface bugs around path resolution, bundled-binary fetching (the ffmpeg and deno fetchers currently pin Windows assets), and shell-out invocations. Patches welcome.

Reports of any rough edge (wizard, separation results, UI papercuts) welcome on the [issues page](../../issues).

---

## For developers

### Stack

- C# / .NET 11 (preview)
- [Avalonia UI](https://avaloniaui.net/) 12 (Windows, macOS, Linux)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) source-generated observables and commands
- [CSharpier](https://csharpier.com/) for formatting (enforced; run `dotnet csharpier format .` before committing)

### Build and run

```pwsh
dotnet build
dotnet run --project src/StemForge
dotnet test
```

### Publish + package a Windows release

Two VS Code tasks together produce the shippable zip:

1. **"publish: win-x64 (self-contained)"** runs `dotnet publish` with the right flags into `publish/win-x64/`.
2. **"package: win-x64"** depends on the publish task. It stages `StemForge.exe` and `tools/` under a `StemForge/` subfolder and produces `publish/StemForge-vX.Y.Z-win-x64.zip`, with the version read from `<Version>` in the csproj.

Bumping the release version is one edit in `src/StemForge/StemForge.csproj`:

```xml
<Version>0.1.0</Version>
```

The next package run picks up the new version automatically.

Native debug symbols from Skia and HarfBuzz are removed by an `AfterTargets="Publish"` MSBuild target so they don't pollute the artifact.

### Cutting a release

1. Bump `<Version>` in `src/StemForge/StemForge.csproj` and commit.
2. Tag main with the same version: `git tag v0.1.0 && git push origin v0.1.0`.
3. Run the **"package: win-x64"** task to build `publish/StemForge-v0.1.0-win-x64.zip`.
4. Create a GitHub Release from the tag, attach the zip, write notes.

### Project layout

```
docs/
  adr/                     architectural decision records
  images/                  README screenshots + logo
src/StemForge/
  App.axaml                application entry + theme resources
  Assets/                  embedded resources (app icon, etc.)
  Styles/                  design tokens (colors, typography, spacing)
  Views/                   XAML views + code-behind
  ViewModels/              view models (CommunityToolkit.Mvvm)
  Models/                  domain models + serialisable settings
  Services/                stateful services: process runner, setup detection,
                           job queue, model catalogue, ffmpeg/deno fetchers
  Helpers/                 pure static utilities (no DI, no state)
  Extensions/              extension methods on Avalonia / framework types
  tools/                   Python driver script for audio-separator
src/StemForge.Tests/       xUnit test project
```
