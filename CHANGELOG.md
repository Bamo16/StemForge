# Changelog

All notable changes to StemForge are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[0.1.1]: https://github.com/Bamo16/StemForge/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Bamo16/StemForge/releases/tag/v0.1.0
