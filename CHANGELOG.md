# Changelog

All notable changes to StemForge are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-07-30

### Added

- **Premium shortfall advisory.** When a download lands below the best audio the source actually offers, StemForge now says so and why. A stale or missing cookie is silent otherwise: yt-dlp succeeds and YouTube serves the logged-out format set, so the file arrives at a lower bitrate with nothing reported. The advisory distinguishes the cases you can act on (sign-in or cookies would get you more) from the ones you cannot.
- **Source-format inspection and pinning in the CLI.** `download --list-formats` reports every candidate audio format for an input, and `--format-id` pins a specific one. A pinned id that is not available fails that input at resolve time rather than silently substituting another format; the rest of the batch continues and the run reports exit code 2.
- **`--json` output** on `presets`, `separate`, and `download` for automation callers. Stdout carries only the final payload; warnings and errors go to stderr.
- **The Models tab shows what a model produces.** Models that previously listed no stems now show them, resolved from the model's own config where available and otherwise inferred from its architecture. Inferred stems are marked as such. The resolution is advisory and never blocks a separation.
- **Ensemble stem-overlap guidance.** The ensemble builder shows which stem names will be averaged across the selected models and which will pass through from a single model, updating as you add and remove models. It is informational and never blocks a selection.
- **Drum-extraction model picker.** Drum extraction is no longer pinned to one model; pick any model that produces drums.
- **Clean output names for user presets.** User presets now write the same "title (stem)" names the built-in presets use, instead of the separator's model-name mashup. Two runs in one job that would write the same file name are disambiguated with a stable numeric suffix, which also fixes the existing collision between two built-in presets emitting the same stem.

### Changed

- Reworked how the app talks to the separation engine so an unrecognized message from the engine is reported instead of silently ignored.
- Listing presets and models no longer imports the inference stack, so `presets` and the model list return quickly without loading torch.
- Child processes are terminated with the OS parent-exit facility on Windows and Linux, so a killed StemForge no longer leaves a separator running.
- The Settings footer shows the full four-component version.

### Fixed

- **Source-format selection could pick an AI auto-dubbed track over the original.** YouTube auto-dubbing emits one audio format per language, and a dub can carry a higher bitrate than the original. A dub is the original with its vocal stem replaced by synthesised speech, so separating one meant feeding already-separated audio into the separator. Dubs are now rejected, as are dynamic-range-compressed variants when the plain equivalent is available at the same quality rung.
- **Provenance survives a round trip.** Separating a file that StemForge downloaded earlier lost its source URL, codec, and bitrate, because the provenance fields written into the tag were never read back out. A download, then separate chain now carries them through to the stems.
- The model catalog no longer comes up empty when a model's score map contains a scalar metric.
- The update check compares the full version, so a hotfix build is no longer reported as out of date.
- Removed the bare `python` PATH fallback in the separation driver, which could pick an unrelated interpreter.

## [0.2.1.1] - 2026-06-16

### Fixed

- First-run setup on Windows and Linux. The bundled ffmpeg download URLs pointed at a rotating FFmpeg-Builds auto-build that was removed upstream, so the setup wizard failed with a 404 when fetching ffmpeg on a fresh install. The Windows and Linux downloads are now pinned to a retained release, and a catalog check guards against pinning a rotating build again. macOS, yt-dlp, and deno downloads were unaffected. If you already completed setup on v0.2.1, nothing changes for you.

## [0.2.1] - 2026-06-16

### Added

- **Command-line mode** (`stemforge-cli`): headless `separate` and `download` with built-in presets, batch input, `--keep-source` and `--extract-drums`, live progress, and two-stage Ctrl+C.
- Preset cards show the ensemble algorithm, with a tooltip.
- Richer source display: sample-rate chips, local-file parity, and aligned monospace numeric cells.
- The job feed shows a phase timeline instead of stalling on raw log lines.
- An animated progress-bar shimmer that fades in with fill and stops at completion.
- The project is MIT licensed.

### Changed

- The models list drops duplicate entries and shows full names and stems on hover.
- Dependency warning noise is suppressed from the logs (set `STEMFORGE_DRIVER_WARNINGS=1` to restore it).
- GUI and CLI share a single version number.

### Fixed

- A separation-engine crash now reports the exit code and the last output instead of a bare "terminated unexpectedly".
- The Run button is no longer clipped when the window is narrow.

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

[0.3.0]: https://github.com/Bamo16/StemForge/compare/v0.2.1.1...v0.3.0
[0.2.1.1]: https://github.com/Bamo16/StemForge/compare/v0.2.1...v0.2.1.1
[0.2.1]: https://github.com/Bamo16/StemForge/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/Bamo16/StemForge/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/Bamo16/StemForge/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Bamo16/StemForge/releases/tag/v0.1.0
