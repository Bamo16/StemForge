# StemForge — domain glossary

This file defines the canonical language used across the codebase. Update it when terms are introduced, refined, or retired. Do not put implementation details, decisions, or rationale here — those belong in `docs/adr/`.

## Tools and installation

### Tool
A prerequisite binary StemForge depends on. The set is closed and known at compile time:
`uv`, `audio-separator`, `yt-dlp`, `ffmpeg`, `deno`. Adding a tool is a code change, not a configuration change.

### Tool catalog
The declarative list of every known Tool together with its per-platform metadata (CLI name, version probe, install strategy, asset descriptors). Single source of truth — all other tool-aware code reads from it.

### Install strategy
The mechanism by which a Tool reaches the user's disk. Three strategies exist:
- **Script install** — runs an upstream installer command (`irm | iex` on Windows, `curl | sh` on Unix). Used by `uv`. Result lands on the user's system PATH via the upstream installer's own behaviour.
- **uv-tool install** — invokes `uv tool install <package>`. The result lives in uv's shim directory, which is also on the user's PATH. Used by `audio-separator`.
- **Bundled fetch** — StemForge downloads a pinned archive, verifies its SHA-256, and extracts the binary into the [[Bundled bin dir]]. Not on the user's system PATH; reachable from StemForge children only. Used by `yt-dlp`, `ffmpeg`, `deno`.

A Tool's strategy choice reflects whether a power user is likely to want that binary in their general toolbox: tools they would plausibly use independently (`uv`, `audio-separator`) go on PATH; tools that would shadow a user's existing copy (`ffmpeg`, `yt-dlp`) or are StemForge-internal plumbing (`deno`) stay bundled.

### Variant
A user-selectable install configuration within the `uv-tool install` strategy, encoding pip extras and optional extra-index-urls. Today only `audio-separator` has variants. The variant set is per-platform.

### GPU variant
A [[Variant]] of `audio-separator` selecting the compute backend. On Windows: `Cuda`, `DirectML`, `Cpu`. On Linux (planned): `Cuda`, `Cpu`. On macOS (planned): `Cpu` only until CoreML support is validated.

A GPU variant has two distinct states that can disagree:
- **Chosen variant** — what the user asked for at install time. Persisted in user settings, drives the wizard's variant picker.
- **Detected variant** — what actually works at runtime, determined by probing the installed Python environment for torch CUDA availability and onnxruntime providers. Drives the runtime behaviour and the Settings status display.

Divergence happens when an install silently falls back. Example: the user picks `Cuda` but the CUDA-build of torch fails to install (no matching wheel for the system's CUDA toolkit version), so audio-separator still installs but only the CPU provider is functional. The detected variant is the source of truth for what audio-separator can actually do; the chosen variant is the source of truth for what to attempt on reinstall.

### Bundled bin dir
`%LOCALAPPDATA%\StemForge\bin` on Windows (analogous paths on other OSes). Holds the output of every [[Bundled fetch]]. Not on the user's system PATH; reachable only from StemForge's own child processes, which are pointed at these binaries by explicit path argument (see ADR 0003 for how).

## Separation

### Model
A single audio-separation neural network, identified by its weight file (`.ckpt`, `.onnx`, etc.), e.g. `bs_roformer_vocals_resurrection_unwa.ckpt`. A model takes one audio input and emits two or more [[Stem]]s: a target and its complement at minimum (e.g. a vocals stem and a no-vocals stem), and more for models that split into several parts. (Not every model declares its stem names in the catalog metadata.) Models are the atomic unit; a [[Preset]] names one or more of them.

### Stem
An isolated audio component produced by separation — vocals, instrumental, drums, bass, etc. A separation run writes one file per stem.

### Preset
A named separation recipe selectable in the UI. Carries a category, a human label, and a [[Separation mode]] that determines how its [[Model]]s are run. User-defined presets (custom ensembles and single-model presets) are surfaced in the UI as "User presets".

### Separation mode
How a [[Preset]] is specified to the separator. The modes are not different processes so much as different ways of expressing the same separation: most presets are a set of [[Model]]s run together and combined with an [[Ensemble algorithm]]. Three modes:
- **Built-in preset** — an [[Ensemble]] curated in audio-separator's own catalog, invoked by naming the preset rather than enumerating its models and algorithm. StemForge mirrors the catalog's model list and algorithm for display, but the separator owns the definition. The canonical source is the separator driver's live `list_presets` response; a built-in fallback catalog mirrors it for use before that response arrives.
- **Custom ensemble** — an [[Ensemble]] whose [[Model]]s and [[Ensemble algorithm]] StemForge specifies explicitly to the separator. Defined the same way as a built-in preset; only the point of definition differs. Shown in the UI as a "User preset".
- **Single model** — one [[Model]] run on its own. Exists so a single model can be run without being part of an ensemble, which the other two modes cannot express. Also shown in the UI as a "User preset" (one that uses a single model).

### Ensemble
A [[Preset]] that runs two or more [[Model]]s and combines their per-[[Stem]] outputs into one result. Single-model presets are not ensembles.

### Ensemble algorithm
The method by which an [[Ensemble]] combines its models' outputs. Operates either in the waveform domain (`avg_wave`, `median_wave`, `min_wave`, `max_wave`) or the spectral/FFT-magnitude domain (`avg_fft`/`mean_fft`, `median_fft`, `min_fft`, `max_fft`, and `*_mag` variants). Each algorithm trades off loudness/detail recovery against noise suppression. Built-in presets carry their algorithm in the driver's catalog; custom ensembles let the user pick one. Applies only to ensembles (2+ models).
