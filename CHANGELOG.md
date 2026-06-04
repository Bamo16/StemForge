# Changelog

All notable changes to StemForge are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-06-04

### Added

- **Cross-platform support.** Per-OS path resolution and bundled ffmpeg, yt-dlp, and deno for Linux and macOS, plus per-OS GPU variants (Windows: CUDA / DirectML / CPU; Linux: CUDA / CPU; macOS: CPU). A Linux CI job builds the app, runs the test suite, and downloads and verifies the bundled binaries on every push. The published download is still Windows; see the README for the current state on other platforms.
- **Source provenance in tags.** Output stems now embed the source URL, codec, bitrate, and format id alongside title/artist/cover, and each file is tagged with the specific preset that produced it.
- The app version is shown in the Settings footer.
- The setup wizard install log is selectable and copyable (in the wizard and Settings), and bundled-download log lines name the tool they belong to.
- Progress feedback during the long audio-separator install.

### Changed

- The URL audio-format picker is ordered best-first by bitrate, with the recommended format flagged AUTO (it prefers a 44.1 kHz source to avoid an extra resampling step).
- Drum extraction is modeled as a first-class preset, so drum stems carry a proper preset name in their provenance.
- User presets are stored alongside settings and migrated from the old location automatically.
- Bundled the DM Sans and JetBrains Mono fonts for consistent typography across platforms.

### Fixed

- Restored the URL audio-format picker's sort order, which regressed in v0.1.1.

## [0.1.1] - 2026-06-01

### Changed

- Reworked how StemForge manages its external tools (uv, audio-separator, ffmpeg, yt-dlp, deno) under the hood for more reliable detection, install, and path handling.
- yt-dlp is now a bundled binary instead of a uv-installed tool, so it no longer shadows a yt-dlp you already have on your PATH. Self-update it in place with `yt-dlp.exe --update-to master`.
- Settings page polish: clearer per-tool status, a sticky action footer, and a smoother detection refresh (the spinner no longer resizes the card).

### Fixed

- The setup wizard's install log now shows the full cumulative log instead of clearing between tools.
- uv is found immediately after install, with no app restart needed.

## [0.1.0] - 2026-05-27

Initial release.

[0.2.0]: https://github.com/Bamo16/StemForge/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/Bamo16/StemForge/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Bamo16/StemForge/releases/tag/v0.1.0
