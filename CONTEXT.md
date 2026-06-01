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
