# Changelog

All notable changes to StemForge are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.1] - 2026-10-15

Bug-fix release.

### Fixed

- **A tool installed during first-run setup could still show as missing until StemForge was restarted.** The URL box said "Install yt-dlp to enable URL downloads" right after setup had installed it. A slow check of every tool, started when setup reached its Install step, could finish after a tool was installed and overwrite its newer result. Checks now run one at a time.

## [0.5.0] - 2026-10-15

### Added

- **Notes on drum models.** The drum-extraction picker in Settings says what each model is good for. BS Roformer SW gives the cleanest drums for using in a song; htdemucs_ft is the lighter choice for analysis such as BPM detection.
- **A warning for browsers yt-dlp can't read.** On Windows, naming Chrome or another Chromium-based browser as the cookie source shows a warning in Settings, since their cookies can't be read from outside the browser. Firefox works.

### Changed

- **yt-dlp keeps itself current.** StemForge runs yt-dlp's own update once a day, at launch and before resolving a URL, so YouTube changes no longer leave downloads broken until the next StemForge release. New installs start from yt-dlp 2026.08.19. A yt-dlp you set under Settings → Tool paths is left alone.
- **StemForge always uses its own ffmpeg, yt-dlp and deno.** A copy on your PATH is no longer used in their place, so an old one can't break downloads or separation. Settings → Tool paths still points StemForge at another binary. If a required tool is missing at launch, setup opens again.
- **Setup installs everything with one button.** The Install step's **Install** button installs every ticked tool, optional ones included, and moves on; untick an optional tool to skip it.
- **URL downloads go through yt-dlp.** yt-dlp fetches the audio and streams it into ffmpeg, which encodes it as it arrives. There is still a single encode and nothing written to disk before the output.
- **The queue shows the name the file will get**, such as "TLC - No Scrubs (Official Audio)", rather than the bare title. A job queued before its URL was resolved switches from the URL to that name once it is.
- **Roformer models list their real stems.** The Models view reads a model's stems from its config instead of guessing them from its file name, and BS Roformer SW now appears in the drum-extraction picker.

### Fixed

- **YouTube downloads failing on a new install.** The yt-dlp a new install shipped with was months old, and YouTube refused its download links, so a URL failed with "ffmpeg.exe exited with code -858797304".
- **Unreadable browser cookies no longer fail a download.** When yt-dlp can't read the cookies (Chrome on Windows, for one), StemForge downloads without them and says so.
- **Errors from yt-dlp and ffmpeg include the tool's own message**, not only an exit code.
- **Files that appear in the output folder during a run are no longer treated as stems.** Ableton's `.asd` analysis files, written while a batch ran into a folder Ableton was watching, were renamed and tagged as stems.
- **A Demucs model's size and status count all of its files.** htdemucs_ft showed as 149 B, and deleting it left its 321 MB of weights behind.
- Long stem lists in the Models view are cut short instead of running into the Status column.

## [0.4.0] - 2026-09-28

### Added

- **Drums-only and source-only runs in the app.** A run now writes any combination of preset stems, a drum stem and the source audio, so a drum stem or a plain download no longer needs a preset beside it. "Keep source file" moved to the footer as **Source audio** and is remembered between sessions. The footer says what the run will write and how many model runs it costs before you press Run, and a source-only run's button reads **Download**. Dropping several files with only Drum stem ticked queues a drums-only job for each.
- **Single-model runs from the CLI.** `separate --model <file>` runs any model audio-separator knows by its file name, alongside any presets, which reaches models no preset uses. `--keep <stem>` keeps only the named stems and discards the rest, for example `--model MDX23C-DrumSep-aufr33-jarredou.ckpt --keep kick --keep snare`. An input fails if none of the kept stems is written.
- **`--extract-drums` runs on its own**, without a `--preset`, for a source that only needs its drum stem.
- **Update audio-separator from Settings.** Settings shows the installed version against the latest release and offers to update when a newer one exists, keeping your CUDA, DirectML or CPU variant.
- `download --list-formats --json` reports the name the downloaded file will get, and `presets --json` reports each preset's display name, category and description.

### Changed

- **The bundled ffmpeg now comes from [nomercy-ffmpeg](https://github.com/NoMercy-Entertainment/nomercy-ffmpeg)** (ffmpeg 9.0), on every platform. It carries the `beatdetect` and `keydetect` audio filters. The first-run download is larger: about 265 MB on Windows, against about 100 MB before. An existing install keeps the ffmpeg it already has.
- **macOS gets native ffmpeg builds** for Apple Silicon and Intel, with `ffprobe`, in place of an Intel-only binary without it.
- Fresh installs get audio-separator 0.47.0. Every built-in preset resolves to the same models and stems as on 0.44.
- A drums-only run writes its drum stem beside the outputs even when drum stems are set to go to the cache, since the stem is the whole result.
- The queue's column reads **Output**, with summaries such as "Drums only" and "Balanced + Source".
- Checked models show as separate chips instead of one comma-joined line.

### Fixed

- **Artists no longer repeat in file names.** YouTube Music credits a contributor once per role, and every repeat landed in the name. An artist the title already credits as featured is also left out of the name (the ARTIST tag keeps every credit).
- Two-stem models could be shown as producing only one stem when that stem was inferred from the file name.
- A drums-only run whose drum step fails is reported as failed, rather than as done with nothing written.
- On Linux and macOS, bundled ffmpeg, yt-dlp and deno are now installed executable.
- File names leave out the same characters on every OS. On Linux and macOS a name could keep `:` or `?`, so the same video got a different name than on Windows.

## [0.3.1] - 2026-07-31

Bug-fix release. No new features.

### Fixed

- **Output files are no longer overwritten.** The clean output names introduced in 0.3.0 could collide in two ways, and in both the existing file was silently replaced rather than renamed: a second run into an output directory that already held stems from an earlier run, and drum extraction writing its stem alongside a preset that also produced one. Names now account for what is already in the directory, and a collision gets a numbered suffix instead of destroying the earlier file.
- **A Premium format you chose not to use is no longer reported as an account problem.** Picking a non-Premium format in the format picker, or pinning one with `--format-id`, previously produced the advisory that says your account does not have YouTube Premium. It now says a Premium format is available and a different one was selected. With `--json`, the outcome is also reported as a new `premiumStatus` field alongside the existing `premiumShortfall` flag.
- **The format picker updates the Premium indicators.** Choosing a different source format left the Premium wordmark and advisory describing the automatically selected one.
- **Built-in presets load after first-run setup.** On a first run the preset list is read before setup has installed the toolchain, and the failed read was cached, so the session stayed on the built-in fallback list until the app was restarted.
- **The job log reports the paths files were actually written to**, instead of the separation engine's temporary names from before they are renamed.
- **The source builds on Linux again.** A missing `using` made 0.3.0 fail to compile on some .NET SDK builds, including the one CI uses. This affects building from source only; the published Windows download was unaffected.

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

[0.5.1]: https://github.com/Bamo16/StemForge/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/Bamo16/StemForge/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/Bamo16/StemForge/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/Bamo16/StemForge/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/Bamo16/StemForge/compare/v0.2.1.1...v0.3.0
[0.2.1.1]: https://github.com/Bamo16/StemForge/compare/v0.2.1...v0.2.1.1
[0.2.1]: https://github.com/Bamo16/StemForge/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/Bamo16/StemForge/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/Bamo16/StemForge/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Bamo16/StemForge/releases/tag/v0.1.0
