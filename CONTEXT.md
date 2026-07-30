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

## Acquisition

### Resolve
Determining everything knowable about a URL source *without fetching its audio*: title, artist, duration, the candidate [[Source format]]s, and which one the selection policy would pick. Every URL [[Job]] resolves before it downloads, so the facts a download will produce are known before any audio bytes are paid for.

### Source format
One audio-only candidate a URL source offers, identified by a format id assigned by the extractor and described by codec, bitrate, and sample rate. A [[Resolve]] returns the full candidate set; exactly one is selected to fetch. Selection is StemForge's policy, not a user choice, unless the user explicitly pins one.

Two kinds of candidate are excluded before any of that, because neither is the source audio as recorded: an **AI auto-dub** (a synthesised-speech translation, which YouTube produces by separating the original's vocal stem and remixing, so it is already-separated audio) and a **DRC variant** (a dynamic-range-compressed copy of another candidate, retained only when it is the sole route to that quality rung). A candidate's format id is not a stable name on such sources: an id may carry a per-video suffix, so only the leading **itag** may be compared against a known set.
_Avoid_: "format" unqualified, and "audio format" — both collide with [[Output format]], which sits at the opposite end of the pipeline. A source format is what StemForge fetches **from**; an output format is what it writes **to**.

### Output format
The container and codec StemForge encodes an acquired or separated file into (`flac`, `mp3`, `wav`). Independent of the [[Source format]]: any source format may be transcoded to any output format, and every URL acquisition is normalised to 44.1 kHz on the way, because separation [[Model]]s operate at that rate.

### Auth-gated format
A [[Source format]] YouTube withholds from an unauthenticated request, identified by its itag against a known set. An authentication signal, not a quality one: the gated set includes formats *worse* than what the same source gives away freely. Like a [[Model profile]], this is **advisory, never authoritative** (see ADR 0010): the set is empirically derived and will drift as YouTube changes its formats.

Gating has two tiers, and the difference is what makes an unmet expectation diagnosable:
- **Session-gated** — shown to any signed-in account, paying or not. Presence proves a live session and nothing more.
- **Premium-gated** — withheld even from a signed-in account without a subscription. Presence proves a Premium entitlement.

### Premium format
An [[Auth-gated format]] whose bitrate also exceeds the best ungated format the same source offers, i.e. audio the user could not have obtained without authenticating. Determined per source rather than from a fixed list, since the ungated candidates in a resolve are exactly the baseline an unauthenticated request would have received. Gating alone is not enough to be premium.

### Premium ladder
The set of [[Auth-gated format]]s a source is provisioned with. Provisioned in tiers rather than per format: a source has the full ladder, a low-quality-only ladder, or none at all, and the high-bitrate formats never appear without the low one. The ladder attaches to the [[Music track entity]], not to the video, so two YouTube entries for the same recording can differ.

### Music track entity
A URL YouTube surfaces as a music track rather than an ordinary upload, marked by populated artist, track, and album metadata (typically served from a `- Topic` channel). Distinct from a video upload of the same recording, **including an official one from the artist's or label's own channel**, which is not a music track entity and carries no [[Premium ladder]]. The distinction is what makes a [[Premium shortfall]] diagnosable.

### Premium expectation
A user's declared expectation that acquisitions should yield a [[Premium format]], inferred from their having configured a cookie source rather than from a separate setting. A user with no cookie source configured holds no premium expectation, and premium-ness is not surfaced to them.

### Premium shortfall
A [[Resolve]] in which a [[Premium expectation]] holds but no [[Premium format]] was selected. An umbrella for four outcomes, distinguished by whether the source is a [[Music track entity]] and which tier of [[Auth-gated format]] survived, because each calls for a different response:

- **Not signed in** — a music track with no gated format at all. A signed-in free account would still have been shown the session-gated rungs, so their total absence means the browser session is no longer authenticated. This is the outcome the whole signal exists to catch, and the only one that points at the session.
- **Account not Premium** — a music track with session-gated formats but no Premium-gated ones. The ladder exists; this account cannot reach it. Most often cookies read from a browser profile signed into a different account than intended.
- **Source has no Premium audio** — not a music track, signed in, and nothing on offer beats the source's own free formats. A property of the source.
- **No Premium ladder** — not a music track and nothing gated at all, so none was ever provisioned. The ordinary outcome for a video upload, and not a fault.

None of these asserts a cause it cannot observe. A dead session is silent: it yields a normal, successful resolve with fewer formats and no error.

### Provenance
The record of where an audio file came from and how it was obtained — source URL, acquisition date, and the codec, bitrate, and id of the selected [[Source format]] — written into the file itself so it survives being moved, re-ingested, or used as the input to a later [[Job]].

## Separation

### Model
A single audio-separation neural network, identified by its weight file (`.ckpt`, `.onnx`, etc.), e.g. `bs_roformer_vocals_resurrection_unwa.ckpt`. A model takes one audio input and emits two or more [[Stem]]s: a target and its complement at minimum (e.g. a vocals stem and a no-vocals stem), and more for models that split into several parts. (Not every model declares its stem names in the catalog metadata.) Models are the atomic unit; a [[Preset]] names one or more of them.

### Stem
An isolated audio component produced by separation — vocals, instrumental, drums, bass, etc. A separation run writes one file per stem.

### Model profile
The set of facts StemForge derives about a [[Model]] *without running it*: its architecture, its output [[Stem]]s with a confidence/source (exact names from the model config or benchmark data; a target stem inferred from the filename; or unknown), and whether it is a composite/bag model — one weight file that is internally several sub-models, e.g. `htdemucs_ft`. Drives stem-aware UI: the [[Keep set]] picker, ensemble overlap hints, and the drum-extraction model list. Advisory, never authoritative — audio-separator remains the source of truth for what a model actually emits.

### Keep set
The output [[Stem]]s StemForge retains; the rest are discarded. For a [[Preset]] (one [[Separation run]]) it is the subset of that run's stems: built-in presets carry an opinionated keep set (a vocal preset keeps only Vocals), user presets default to keeping all, narrowed by the user via a stem picker when the [[Model profile]] can name the stems. For a [[Workflow]] it is a single decision made once, at the end of the workflow, over the pooled outputs of every [[Step]] — so a workflow may keep stems from any step, not only the last. The stems each step must actually emit follow from this (a stem is produced only if a later step consumes it or the keep set retains it); they are not chosen per step.

A keep set is applied when a separation is **run**, not baked into a [[Preset]]'s identity. A built-in's curated keep is the default applied when the preset is run on its own; inside a [[Workflow]] that curated keep is only a suggestion — the workflow's end-of-run keep decision governs, so a workflow can keep (or feed onward) a stem the preset would normally hide.

### Preset
A named recipe for a single [[Separation run]]: a category, a human label, and a [[Separation mode]] that determines how its [[Model]]s are run. Built-in, custom-ensemble, and single-model presets are all one separation each. User-defined presets (custom ensembles and single-model presets) are surfaced in the UI as "User presets". A preset is **atomic**: it is the reusable building block a [[Workflow]] composes. Presets never contain [[Step]]s or other presets.

### Workflow
A user-defined, multi-step recipe: an ordered list of [[Step]]s that produces output [[Stem]]s the user chooses to keep and name. Each step runs a separation — a built-in [[Preset]] or an inline [[Model]]-or-[[Ensemble]] — on a chosen input (the source audio, or a [[Stem]] produced by an earlier step). A workflow **composes** [[Preset]]s but does not **nest**: a workflow references presets, and presets never reference workflows or other presets, so composition is one level deep with no cycles. Where a [[Preset]] is one [[Separation run]], a workflow is several. (Mixing operations, and steps that reference another user workflow, are deferred extensions.)

### Step
A single stage of a [[Workflow]]: an input (the source audio, or a named [[Stem]] produced by an earlier step) and a separation to run on it (a built-in [[Preset]], or an inline [[Model]]-or-[[Ensemble]]), producing that separation's [[Stem]]s. A workflow is an ordered list of steps; when executed, each step is one [[Separation run]]. A step's input may reference **any** earlier step's output, so the linear ordered list expresses a tree without a tree-shaped UI. _Not to be confused_ with the loose "step" the pipeline uses for progress, which counts [[Separation run]]s within a [[Job]].

### Separation mode
How a [[Preset]] is specified to the separator. The modes are not different processes so much as different ways of expressing the same separation: most presets are a set of [[Model]]s run together and combined with an [[Ensemble algorithm]]. Three modes:
- **Built-in preset** — an [[Ensemble]] curated in audio-separator's own catalog, invoked by naming the preset rather than enumerating its models and algorithm. StemForge mirrors the catalog's model list and algorithm for display, but the separator owns the definition. The canonical source is audio-separator's own `ensemble_presets.json`, read at startup by the torch-free `list_presets.py` one-shot; a built-in fallback catalog mirrors it for use before that response arrives.
- **Custom ensemble** — an [[Ensemble]] whose [[Model]]s and [[Ensemble algorithm]] StemForge specifies explicitly to the separator. Defined the same way as a built-in preset; only the point of definition differs. Shown in the UI as a "User preset".
- **Single model** — one [[Model]] run on its own. Exists so a single model can be run without being part of an ensemble, which the other two modes cannot express. Also shown in the UI as a "User preset" (one that uses a single model).

### Ensemble
A [[Preset]] that runs two or more [[Model]]s and groups their outputs by [[Stem]] name. A stem name produced by two or more of the models is blended into a single stem via the [[Ensemble algorithm]]; a stem name produced by only one model passes through unchanged. The result is the union of every stem name the models produce, with only the overlapping names actually combined. So ensembling models that share a target stem improves that stem, while ensembling models with disjoint stems just collects their stems side by side (no quality benefit). Single-model presets are not ensembles.

### Ensemble algorithm
The method by which an [[Ensemble]] combines its models' outputs. Operates either in the waveform domain (`avg_wave`, `median_wave`, `min_wave`, `max_wave`) or the spectral/FFT-magnitude domain (`avg_fft`/`mean_fft`, `median_fft`, `min_fft`, `max_fft`, and the UVR spectral blends `uvr_max_spec` and `uvr_min_spec`). Each algorithm trades off loudness/detail recovery against noise suppression. Built-in presets carry their algorithm in the driver's catalog; custom ensembles let the user pick one. Applies only to ensembles (2+ models).

## Jobs and orchestration

### Job
All the work StemForge performs on a single input source: one local file or one URL. Carried as a `JobRecord` (source, one or more [[Preset]]s, output directory and format, optional drum extraction and keep-source). A job expands into one [[Separation run]] per preset, plus one more if drum extraction is enabled; keep-source and provenance tagging are job steps but not separation runs. A batch invocation produces one job per input.

### Separation run
One [[Preset]] (or the drum-extraction preset) executed as a single invocation of the separator driver (`JobRequest` → `JobResult`). The unit a job's progress is divided into. Takes one audio input plus the preset's [[Model]]s and produces that preset's [[Stem]] outputs.
_Avoid_: "pass" (reserved intuition for a single model running, which is a [[Model run]]).

### Model run
One execution of a single [[Model]] within a [[Separation run]]. A single-model run contains one; an [[Ensemble]] run contains several, combined by the [[Ensemble algorithm]].

### Phase
A sub-state of a single [[Separation run]] as it progresses, reported by the separator driver: `downloading_model`, `loading_model`, `separating`, `ensembling`, `writing_output`, `finalizing`. A phase says *what kind of work* is happening; progress (a percentage) says *how far along* it is. Distinct from the run's lifecycle outcome (accepted, completed, failed, cancelled).
_Avoid_: using "phase" for a bare progress percentage, for a [[Separation run]] itself, or (historically) for the coarse download/separate distinction the progress event used to carry.

### Separation pipeline
The UI-agnostic orchestrator that takes one [[Job]] and drives it to completion: download (for URL sources), each [[Separation run]] in order, optional drum extraction, optional keep-source, and provenance tagging. Reports progress as a single update stream consumed by both the GUI (via an adapter that maps updates onto the queue view) and the CLI. Knows nothing about view-models or the UI thread.
