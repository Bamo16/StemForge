#!/usr/bin/env python3
"""
separator_driver.py — long-lived JSON-Lines IPC wrapper around audio-separator.

Spawned once by the host application; reads commands from stdin and emits events
on stdout, both as JSON objects, one per line. Human-readable diagnostics go to
stderr.

Why this exists
---------------
Calling `audio-separator` (the CLI) once per preset spins up a new Python
process, redoes the torch/onnxruntime setup, reloads cuDNN, etc. For batch
work (e.g. running 6 presets on one file), the per-process overhead dominates
the small ones. This script keeps one process alive, lets the host queue
jobs, and surfaces a clean structured progress stream.

Protocol
--------
All I/O is JSON Lines (one JSON object per line, '\\n'-terminated, flushed).

Commands accepted on stdin
~~~~~~~~~~~~~~~~~~~~~~~~~~
{"cmd": "ping"}
    -> {"event": "pong"}

{"cmd": "list_presets"}
    -> {"event": "presets", "presets": {...}}

{"cmd": "list_models", "filter": null | "vocals" | "drums" | ...}
    -> {"event": "models", "models": {...}}  // simplified list

{"cmd": "download_models", "models": ["foo.ckpt", "bar.onnx"]}
    Pre-fetches model files. Emits download phase + progress events.
    -> ... -> {"event": "download_completed"}

{"cmd": "run",
 "id": "<caller-chosen string>",
 "audio": "/abs/path/to/input.wav",
 "output_dir": "/abs/path/to/output",
 "output_format": "FLAC",        // optional, default FLAC
 "preset": "vocal_balanced",      // either preset OR (models+algorithm)
 "models": ["m1.ckpt", "m2.ckpt"],
 "algorithm": "avg_wave",
 "weights": [1.0, 0.5],           // optional
 "stems_to_keep": ["Vocals"],     // optional; see "Stem filtering" below
 "custom_names": {"Vocals": "song_vbal"}  // optional, no extension
}
    -> job_started, phase, progress, log, stem_written, stem_discarded,
       job_completed | job_failed

{"cmd": "shutdown"}
    -> {"event": "bye"} then process exits 0.

Events emitted on stdout
~~~~~~~~~~~~~~~~~~~~~~~~
{"event": "ready", "driver_version": "...", "separator_version": "...",
 "device": "cuda" | "mps" | "cpu", "model_dir": "...",
 "preset_ids": ["instrumental_clean", "vocal_balanced", ...]}
    Full preset metadata is available via the "list_presets" command.

{"event": "log", "id": "<id|null>", "level": "info", "message": "..."}

{"event": "phase", "id": "...", "phase": "downloading_model",
 "model": "...", "model_index": 1, "model_count": 3, "cached": false}
    Phases: downloading_model, loading_model, separating, ensembling,
            writing_output, finalizing
    The downloading_model phase always fires once per model in the job,
    even if the model is already on disk (`cached: true`). When cached,
    no `progress` events follow this phase. When `cached: false`, expect
    `progress` events with `unit: "iB"` while bytes are fetched.

{"event": "progress", "id": "...", "phase": "download" | "separate",
 "current": 1234, "total": 5678, "unit": "iB" | "chunks"}

{"event": "stem_written", "id": "...", "stem": "Vocals", "path": "/abs/.../x.flac"}
{"event": "stem_discarded", "id": "...", "stem": "Instrumental", "path": "..."}

{"event": "job_completed", "id": "...", "outputs": [{"stem":..,"path":..}],
 "discarded": [...], "duration_seconds": 123.4}
{"event": "job_failed", "id": "...", "error": "...", "traceback": "..."}

Stem filtering
--------------
`stems_to_keep` is a list of canonical stem names (e.g. ["Vocals"],
["Vocals", "Drums"]). Canonical names match audio-separator's STEM_NAME_MAP:
Vocals, Instrumental, Drums, Bass, Guitar, Piano, Synthesizer, Strings,
Woodwinds, Brass, Other, Lead Vocals, Backing Vocals.

If `stems_to_keep` is missing/empty, default behaviour by preset prefix:
  vocal_*        -> ["Vocals"]
  instrumental_* -> ["Instrumental"]
  karaoke        -> ["Instrumental"]   (lead-vocal removal)
  otherwise      -> keep all

Optimisation: if exactly one stem is to be kept, we set
`output_single_stem` so the library doesn't even write the unwanted
intermediate stems to the temp dir. For 2+ stems we write all and delete
unwanted final outputs.

Filename rules
--------------
If `custom_names` is provided, it's passed through verbatim (keys are stem
names, values are filenames *without* extension; the library appends the
extension based on `output_format`).

If not, the driver synthesises predictable names:
  "{base} ({stem})"   e.g. "Song (Vocals)", "Song (Drums)"

This makes it trivial for the host to predict where each stem will land.
The driver also enforces the expected name after separation — if the model
ignores custom_output_names (e.g. Demucs) the file is renamed in place.
"""

import argparse
import io
import json
import logging
import os
import re
import sys
import threading
import time
import traceback
import warnings
from contextlib import contextmanager

DRIVER_VERSION = "0.1.0"

# ---------------------------------------------------------------------------
# Dependency warning suppression
# ---------------------------------------------------------------------------
# torch, librosa, and audio-separator emit a steady stream of FutureWarning,
# UserWarning, and DeprecationWarning at import time and during inference.
# The driver redirects native stdout to stderr and the host logs every stderr
# line as Debug, so that noise floods the debug channel. Filter it here, before
# the heavy imports (tqdm/audio_separator and their transitive torch/librosa)
# fire, so the filters are in place when those warnings are raised.
#
# This only silences warnings. Genuine errors are exceptions, not warnings, so
# tracebacks still reach stderr untouched.
#
# Escape hatch: set STEMFORGE_DRIVER_WARNINGS=1 to keep every warning visible
# for debugging a warning that actually matters.
if os.environ.get("STEMFORGE_DRIVER_WARNINGS") != "1":
    for _warn_category in (FutureWarning, UserWarning, DeprecationWarning):
        warnings.filterwarnings("ignore", category=_warn_category)

# ---------------------------------------------------------------------------
# stdout discipline
# ---------------------------------------------------------------------------
# audio-separator and its deps love to print to stdout (model warnings,
# package banners, ONNX provider chatter). All of that has to be diverted
# off stdout or it'll corrupt our JSON-Lines stream.

# Order matters: first save a duplicate of fd 1, *then* redirect fd 1 to
# fd 2. This way our event channel writes via the duplicated fd (which
# still points to the original stdout/pipe), while everything in the
# process that writes to fd 1 (Python prints, C extensions, library
# banners, ONNX provider chatter, even forked subprocesses inheriting
# our fds) lands on stderr and can't corrupt the JSON-Lines stream.
try:
    _real_stdout_fd = os.dup(1)
    _REAL_STDOUT = os.fdopen(_real_stdout_fd, "w",
                             buffering=1, encoding="utf-8")
    os.dup2(sys.stderr.fileno(), 1)
    # sys.stdout still references fd 1 (now redirected) but we replace it
    # so Python-level prints route here without the syscall.
    sys.stdout = sys.stderr
except (OSError, io.UnsupportedOperation):
    # Non-tty / no fileno (e.g. Jupyter). Fallback: just keep stdout.
    _REAL_STDOUT = sys.stdout
    sys.stdout = sys.stderr

_emit_lock = threading.Lock()

# ---------------------------------------------------------------------------
# Cancellation
# ---------------------------------------------------------------------------
# Set by the main thread when the host sends {"cmd": "cancel"}.
# Cleared by the job thread before each new run.
# Checked in EventTqdm.update() (fires on every inference tick) and at
# explicit checkpoints in _run_job so cancellation is prompt.

_job_cancel_event = threading.Event()


class JobCancelledError(Exception):
    pass


def _check_cancelled():
    if _job_cancel_event.is_set():
        raise JobCancelledError("Job cancelled by host")


def emit(event: str, **fields) -> None:
    """Write a single JSON-Lines event to the real stdout, atomically."""
    payload = {"event": event, **fields}
    line = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
    with _emit_lock:
        _REAL_STDOUT.write(line + "\n")
        _REAL_STDOUT.flush()


# ---------------------------------------------------------------------------
# Phase tracking
# ---------------------------------------------------------------------------
# Per-thread "current phase" used by EventTqdm to tag progress events with
# meaningful context (download vs separate vs ensemble).

_phase_local = threading.local()


def _get_phase() -> dict:
    # getattr only returns the default when the attribute is *absent*; if the phase
    # context manager restored prev=None (no outer phase), the attribute exists but
    # is None, so we must guard against that explicitly.
    phase = getattr(_phase_local, "phase", None)
    return phase if phase is not None else {"phase": "unknown"}


@contextmanager
def phase(job_id, phase_name, **extra):
    """Sets current-phase context and emits a phase event on enter."""
    prev = getattr(_phase_local, "phase", None)
    _phase_local.phase = {"phase": phase_name, "id": job_id, **extra}
    emit("phase", id=job_id, phase=phase_name, **extra)
    try:
        yield
    finally:
        _phase_local.phase = prev


# ---------------------------------------------------------------------------
# tqdm interception (MUST happen before importing audio_separator)
# ---------------------------------------------------------------------------
# Strategy: subclass tqdm and override update/close to emit progress events.
# We also disable the terminal rendering. We must patch BEFORE any
# audio_separator module is imported, because they do `from tqdm import tqdm`
# at module load time (binding the name).

import tqdm as _tqdm_pkg  # noqa: E402

_OriginalTqdm = _tqdm_pkg.tqdm

# Min interval between emitted progress events per bar, to avoid
# flooding the IPC stream during fast loops.
_PROGRESS_THROTTLE_SEC = 0.1

# tqdm needs *somewhere* to render to. We don't want it on stdout (would
# corrupt our JSON-Lines stream) or stderr (would mix carriage-return bar
# scribbles in with diagnostic output). Send it to the void. One handle,
# reused for every bar.
_DEVNULL = open(os.devnull, "w", buffering=1)


class EventTqdm(_OriginalTqdm):
    """tqdm subclass that mirrors updates as JSON events on our stream.

    Important: do NOT pass disable=True to tqdm. When disabled, tqdm's
    __iter__ short-circuits and never calls self.update(), so our hook
    would never fire and `self.n` would stay at 0 for the bar's life.
    Instead we leave tqdm enabled and redirect its rendering to devnull.
    """

    def __init__(self, *args, **kwargs):
        # Render into the void; we have our own progress event stream.
        kwargs["file"] = _DEVNULL
        kwargs["disable"] = False
        # Encourage tqdm to call self.update() on (nearly) every iteration.
        kwargs.setdefault("miniters", 1)
        kwargs.setdefault("mininterval", 0.05)
        super().__init__(*args, **kwargs)
        self._last_emit = 0.0
        self._closed_once = False
        # tqdm sets self.unit to "it" by default; downloads pass unit="iB",
        # demucs uses "seconds". Expose whatever the call site chose so the
        # host can pick an appropriate label.
        self._unit = self.unit or ""

    def update(self, n=1):
        _check_cancelled()
        ret = super().update(n)
        now = time.monotonic()
        if now - self._last_emit >= _PROGRESS_THROTTLE_SEC or (
            self.total and self.n >= self.total
        ):
            self._last_emit = now
            self._emit()
        return ret

    def close(self):
        # tqdm.__iter__'s finally calls close(), then Python may GC the
        # iterator and call close() again via __del__. Guard so the host
        # only sees one final tick per bar.
        if not self._closed_once:
            self._closed_once = True
            self._emit(final=True)
        return super().close()

    def _emit(self, final=False):
        ctx = _get_phase()
        # Heuristic phase tagging when no explicit phase is active:
        # download bars use unit="iB"; inference bars don't.
        if ctx.get("phase") in (None, "unknown"):
            phase_name = "download" if self._unit == "iB" else "separate"
        else:
            phase_name = ctx.get("phase")
        emit(
            "progress",
            id=ctx.get("id"),
            phase=phase_name,
            current=int(self.n),
            total=int(self.total) if self.total else None,
            unit=self._unit or None,
            final=final or None,
        )


# Patch the class on the package. Modules importing `from tqdm import tqdm`
# AFTER this point will pick up our subclass.
_tqdm_pkg.tqdm = EventTqdm

# ---------------------------------------------------------------------------
# Now safe to import audio_separator
# ---------------------------------------------------------------------------
from audio_separator.separator import Separator  # noqa: E402

# Re-patch any module that already had `from tqdm import tqdm` bound at the
# time audio_separator was first imported (separator.py imports it eagerly).
import audio_separator.separator.separator as _sep_mod  # noqa: E402
_sep_mod.tqdm = EventTqdm

# Architecture modules are imported lazily inside Separator.load_model
# (via importlib), so they'll naturally pick up our patched tqdm. But pre-
# import-and-patch them anyway to be safe — costs almost nothing.
try:
    import audio_separator.separator.architectures.mdx_separator as _mdx  # noqa: E402
    _mdx.tqdm = EventTqdm
except Exception:
    pass
try:
    import audio_separator.separator.architectures.mdxc_separator as _mdxc  # noqa: E402
    _mdxc.tqdm = EventTqdm
except Exception:
    pass
try:
    import audio_separator.separator.architectures.vr_separator as _vr  # noqa: E402
    _vr.tqdm = EventTqdm
except Exception:
    pass


# ---------------------------------------------------------------------------
# Logging interception
# ---------------------------------------------------------------------------
# audio-separator logs lots of useful state through Python's logging.
# We mirror INFO+ messages as `log` events and also pattern-match a few
# distinctive INFO lines to emit `phase` events (model load, ensemble start).

# Log patterns we promote to structured phase events.
# These depend on the exact wording used by audio-separator's INFO logs;
# if upstream changes them, the structured phase events stop firing
# (everything still works, but per-model context disappears).
_MODEL_LOAD_RE = re.compile(r"^Loading model (.+?)\.\.\.")
_ENSEMBLE_STEM_RE = re.compile(r"^Ensembling (\d+) stems for type: (.+)$")

# Per-job state used by IpcLogHandler to attach model_index/model_count
# to loading_model phase events. Mutated by _run_job before each job.
_current_job_state: dict = {
    "id": None,
    "models": [],
    "model_idx": 0,
}


def _reset_job_state(job_id, models):
    _current_job_state["id"] = job_id
    _current_job_state["models"] = list(models)
    _current_job_state["model_idx"] = 0


class IpcLogHandler(logging.Handler):
    """Forwards audio-separator log records as `log` events AND promotes
    selected INFO patterns to structured `phase` events.

    Important: pattern matching runs regardless of the user's chosen log
    level filter, so phase events fire even with --log-level=warning.
    The user_log_level only controls which records produce `log` events.
    """

    def __init__(self, user_log_level: int):
        super().__init__(level=logging.DEBUG)
        self.user_log_level = user_log_level

    def emit(self, record):
        try:
            msg = record.getMessage()
        except Exception:
            msg = str(record.msg)

        # --- Always promote certain INFO patterns to phase events ----------
        # This runs regardless of user_log_level so the host gets a clean
        # state machine even when log events are silenced.
        if record.levelno >= logging.INFO:
            m = _MODEL_LOAD_RE.match(msg)
            if m:
                _current_job_state["model_idx"] += 1
                total = len(_current_job_state["models"]) or None
                emit(
                    "phase",
                    id=_current_job_state["id"],
                    phase="loading_model",
                    model=m.group(1),
                    model_index=_current_job_state["model_idx"],
                    model_count=total,
                )
            else:
                m = _ENSEMBLE_STEM_RE.match(msg)
                if m:
                    emit(
                        "phase",
                        id=_current_job_state["id"],
                        phase="ensembling",
                        stem=m.group(2),
                        source_count=int(m.group(1)),
                    )

        # --- Forward as a `log` event only if at/above user's filter -------
        if record.levelno >= self.user_log_level:
            ctx = _get_phase()
            emit(
                "log",
                id=ctx.get("id"),
                level=record.levelname.lower(),
                module=record.module,
                message=msg,
            )


def install_logging(user_log_level: int) -> logging.Formatter:
    """Replace the package's log handler with our IPC handler.

    Note: the package logger and the handler are both set to DEBUG so all
    records reach IpcLogHandler.emit(). The user-chosen filter is applied
    *inside* emit() — this guarantees phase-promotion patterns still fire
    even when the user asks for a quieter log stream.
    """
    fmt = logging.Formatter(
        "%(asctime)s - %(levelname)s - %(module)s - %(message)s"
    )
    pkg_logger = logging.getLogger("audio_separator")
    # Wipe existing handlers (the Separator adds its own; we replace).
    pkg_logger.handlers = []
    handler = IpcLogHandler(user_log_level)
    pkg_logger.addHandler(handler)
    pkg_logger.setLevel(logging.DEBUG)
    pkg_logger.propagate = False
    # Also catch the separator submodule loggers
    for name in (
        "audio_separator.separator",
        "audio_separator.separator.separator",
    ):
        sub = logging.getLogger(name)
        sub.handlers = []
        sub.propagate = True
    return fmt


# ---------------------------------------------------------------------------
# Stem filtering helpers
# ---------------------------------------------------------------------------

# Stem name canonicalisation (matches audio_separator.separator.separator
# STEM_NAME_MAP for the values we expose; we use *output* canonical names)
CANONICAL_STEMS = {
    "vocals", "instrumental", "other", "drums", "bass", "guitar",
    "piano", "synthesizer", "strings", "woodwinds", "brass",
    "lead vocals", "backing vocals", "wind inst",
}


def _canon(name: str) -> str:
    """Canonical stem name as used by audio-separator post-normalisation."""
    return name.strip().title()


def _trim_stems_to_source_length(
    source_path: str, stem_paths: list[str], job_id: str | None
):
    """Trim or pad each stem so its sample count exactly matches the source file.

    audio-separator pads the last model chunk to fill the window, so outputs
    are typically a handful of samples longer than the input. That sub-ms
    discrepancy prevents DAWs like Ableton from multi-clip warping across
    source + stems. We fix it here, before reporting job_completed.
    """
    try:
        import soundfile as sf
        import numpy as np
    except ImportError:
        return  # shouldn't happen — audio-separator depends on both

    try:
        src_info = sf.info(source_path)
        source_frames = src_info.frames
        source_sr = src_info.samplerate
    except Exception as e:
        emit("log", id=job_id, level="warning", module="driver",
             message=f"trim: could not read source info: {e}")
        return

    for stem_path in stem_paths:
        if not os.path.isfile(stem_path):
            continue
        try:
            info = sf.info(stem_path)
            # Compute the target frame count in the stem's own sample rate.
            # Source and stem may differ (e.g. 48 kHz opus download → 44.1 kHz
            # separator output), so compare durations, not raw frame counts.
            target_frames = round(source_frames * info.samplerate / source_sr)
            diff = info.frames - target_frames
            if diff == 0:
                continue  # already exact

            data, sr = sf.read(stem_path, always_2d=True)
            if diff > 0:
                data = data[:target_frames]
            else:
                data = np.pad(data, ((0, -diff), (0, 0)))

            sf.write(stem_path, data, sr, subtype=info.subtype)
            direction = "trimmed" if diff > 0 else "padded"
            emit("log", id=job_id, level="debug", module="driver",
                 message=(
                     f"trim: {direction} {os.path.basename(stem_path)}"
                     f" by {abs(diff)} samples → {target_frames} total"
                     f" (src {source_sr} Hz → stem {info.samplerate} Hz)"
                 ))
        except Exception as e:
            emit("log", id=job_id, level="warning", module="driver",
                 message=f"trim: could not adjust {os.path.basename(stem_path)}: {e}")


def default_stems_to_keep(preset_id: str | None) -> list[str] | None:
    """Default keep-set for the curated presets, based on preset prefix."""
    if not preset_id:
        return None
    pid = preset_id.lower()
    if pid.startswith("vocal_"):
        return ["Vocals"]
    if pid.startswith("instrumental_"):
        return ["Instrumental"]
    if pid == "karaoke":
        # Karaoke preset removes lead vocals; the "Instrumental" stem is the
        # backing track (vocals + everything except lead) — that's what users
        # actually want from a karaoke preset.
        return ["Instrumental"]
    return None


# ---------------------------------------------------------------------------
# Driver core
# ---------------------------------------------------------------------------

class Driver:
    def __init__(self, model_file_dir: str, log_level: int):
        self.model_file_dir = model_file_dir
        self.log_level = log_level
        self.log_formatter = install_logging(log_level)
        self._separator: Separator | None = None

    # -- lazy Separator construction (defers heavy GPU/ONNX init) -----------

    def _get_separator(self, **overrides) -> Separator:
        """Returns a Separator instance, creating it on first use.

        Overridable per-job attributes are applied to the existing instance
        rather than re-creating it (would re-run torch device setup).
        """
        if self._separator is None:
            # Force the Separator's logger to DEBUG so ALL records reach
            # our IpcLogHandler. Our handler does the user-level filtering
            # itself — this guarantees the phase-promotion regexes fire on
            # INFO records even when the user asks for --log-level warning.
            self._separator = Separator(
                log_level=logging.DEBUG,
                log_formatter=self.log_formatter,
                model_file_dir=self.model_file_dir,
                # output_dir, output_format are set per-job before use
            )
        sep = self._separator
        for k, v in overrides.items():
            setattr(sep, k, v)
        return sep

    # -- command handlers ---------------------------------------------------

    def cmd_ping(self, _cmd):
        emit("pong")

    def cmd_list_presets(self, _cmd):
        sep = self._get_separator()
        emit("presets", presets=sep.list_ensemble_presets())

    def cmd_list_models(self, cmd):
        sep = self._get_separator()
        filt = cmd.get("filter")
        emit("models", models=sep.get_simplified_model_list(filter_sort_by=filt))

    def cmd_download_models(self, cmd):
        models = cmd.get("models") or []
        if not models:
            emit("download_completed", downloaded=[])
            return
        sep = self._get_separator()
        downloaded = []
        for i, m in enumerate(models, 1):
            cached = self._is_model_cached(sep, m)
            with phase(None, "downloading_model",
                       model=m, model_index=i, model_count=len(models),
                       cached=cached):
                sep.download_model_and_data(m)
                downloaded.append(m)
        emit("download_completed", downloaded=downloaded)

    @staticmethod
    def _is_model_cached(sep: Separator, model_filename: str) -> bool:
        """Quick heuristic: is the primary model file present in model_file_dir?

        Roformer models also have a YAML config; if that's missing but the
        ckpt exists, the library will fetch just the YAML (small, fast),
        so this check is occasionally optimistic. Good enough for UI hinting.
        """
        return os.path.isfile(os.path.join(sep.model_file_dir, model_filename))

    def cmd_cancel(self, _cmd):
        # Sets the event; the job thread checks it on every tqdm tick and at
        # explicit checkpoints. No-op if no job is running.
        _job_cancel_event.set()

    def cmd_shutdown(self, _cmd):
        emit("bye")
        sys.exit(0)

    # ---- the main event: run a separation job -----------------------------

    def cmd_run(self, cmd):
        job_id = cmd.get("id") or f"job_{int(time.time() * 1000)}"
        _job_cancel_event.clear()
        try:
            self._run_job(job_id, cmd)
        except JobCancelledError:
            emit("job_cancelled", id=job_id)
        except Exception as e:
            emit(
                "job_failed",
                id=job_id,
                error=f"{type(e).__name__}: {e}",
                traceback=traceback.format_exc(),
            )

    def _run_job(self, job_id: str, cmd: dict):
        # ---- validate inputs ---------------------------------------------
        audio = cmd.get("audio")
        if not audio or not os.path.isfile(audio):
            raise ValueError(
                f"audio must be an existing file path, got: {audio!r}")

        output_dir = cmd.get("output_dir")
        if not output_dir:
            raise ValueError("output_dir is required")
        os.makedirs(output_dir, exist_ok=True)

        output_format = (cmd.get("output_format") or "FLAC").upper()

        preset_id = cmd.get("preset")
        explicit_models = cmd.get("models")
        algorithm = cmd.get("algorithm")
        weights = cmd.get("weights")

        if preset_id and explicit_models:
            raise ValueError("specify either 'preset' or 'models', not both")
        if not preset_id and not explicit_models:
            raise ValueError("must specify 'preset' or 'models'")

        # Resolve stems_to_keep
        stems_to_keep = cmd.get("stems_to_keep")
        if not stems_to_keep:
            stems_to_keep = default_stems_to_keep(preset_id)
        # Normalise to canonical case if provided
        if stems_to_keep:
            stems_to_keep = [_canon(s) for s in stems_to_keep]

        # ---- prep Separator instance state -------------------------------
        sep = self._get_separator(
            output_dir=output_dir,
            output_format=output_format,
            ensemble_preset=preset_id,
            ensemble_algorithm=algorithm,
            ensemble_weights=weights,
        )

        # Resolve model list (preset or explicit). For presets, load the
        # preset metadata so we know the model list for download progress.
        if preset_id:
            preset_data = sep._load_ensemble_preset(preset_id)
            models = preset_data["models"]
            # _load_ensemble_preset stores _ensemble_preset_models internally;
            # we don't need to re-set it but make it explicit for readability.
            sep._ensemble_preset_models = models
            if algorithm is None:
                sep.ensemble_algorithm = preset_data["algorithm"]
            if weights is None and preset_data.get("weights") is not None:
                sep.ensemble_weights = preset_data["weights"]
        else:
            models = list(explicit_models or [])
            if not algorithm:
                sep.ensemble_algorithm = "avg_wave"

        # Single-stem optimisation: if exactly one stem is kept, the library
        # can skip writing the unwanted ones during per-model separation.
        if stems_to_keep and len(stems_to_keep) == 1:
            sep.output_single_stem = stems_to_keep[0]
        else:
            sep.output_single_stem = None

        # ---- build custom_output_names so filenames are predictable ------
        base = os.path.splitext(os.path.basename(audio))[0]
        provided_names = cmd.get("custom_names") or {}
        # Canonicalise keys
        provided_names = {_canon(k): v for k, v in provided_names.items()}

        def synth_name(stem: str) -> str:
            return f"{base} ({stem})"

        # We don't know all stems each model produces without loading them,
        # but for filename prediction we only need names for stems that
        # actually get written. We pre-populate for stems_to_keep and let
        # any extras fall through to defaults (which we'll delete anyway).
        custom_output_names = {}
        target_stems = stems_to_keep or []  # may grow if we discover others
        for stem in target_stems:
            custom_output_names[stem] = provided_names.get(
                stem, synth_name(stem))
        # Also pass through any provided_names not in target_stems (caller
        # explicitly chose to name them; trust them)
        for stem, name in provided_names.items():
            custom_output_names.setdefault(stem, name)

        emit(
            "job_started",
            id=job_id,
            audio=audio,
            output_dir=output_dir,
            output_format=output_format,
            preset=preset_id,
            models=models,
            algorithm=sep.ensemble_algorithm,
            weights=sep.ensemble_weights,
            stems_to_keep=stems_to_keep,
            single_stem_mode=sep.output_single_stem,
        )

        # Reset per-job counters used by IpcLogHandler to emit
        # loading_model phase events with model_index/model_count.
        _reset_job_state(job_id, models)

        # ---- preflight: download all models so download progress is
        #      cleanly separated from inference progress -------------------
        for i, m in enumerate(models, 1):
            cached = self._is_model_cached(sep, m)
            with phase(job_id, "downloading_model",
                       model=m, model_index=i, model_count=len(models),
                       cached=cached):
                # download_model_and_data is idempotent (skips if file exists)
                sep.download_model_and_data(m)

        # ---- run the ensemble (single or multi-model) --------------------
        _check_cancelled()  # between download and load phases

        # Snapshot files already present so we can detect orphans later.
        # Some models (e.g. Demucs with output_single_stem) write all stems
        # to disk but only return the single stem in `outputs`, leaving the
        # others untracked and un-deletable without this snapshot.
        try:
            _before_files = {
                os.path.normcase(os.path.abspath(os.path.join(output_dir, f)))
                for f in os.listdir(output_dir)
                if os.path.isfile(os.path.join(output_dir, f))
            }
        except OSError:
            _before_files = set()

        t0 = time.perf_counter()
        with phase(job_id, "separating", model_count=len(models)):
            # load_model with a list (or a single name) sets up state for
            # _separate_ensemble; with a single model it just loads it.
            if len(models) == 1:
                sep.load_model(model_filename=models[0])
            else:
                sep.load_model(model_filename=list(models))  # type:ignore

            _check_cancelled()  # between load and inference
            outputs = sep.separate(
                audio, custom_output_names=custom_output_names or None)

        # Detect files the library wrote that it didn't include in outputs.
        try:
            _after_files = {
                os.path.normcase(os.path.abspath(os.path.join(output_dir, f)))
                for f in os.listdir(output_dir)
                if os.path.isfile(os.path.join(output_dir, f))
            }
        except OSError:
            _after_files = set()

        _known_paths = {
            os.path.normcase(
                os.path.abspath(p if os.path.isabs(p) else os.path.join(output_dir, p))
            )
            for p in outputs
        }
        _orphans = _after_files - _before_files - _known_paths
        if _orphans:
            outputs = list(outputs) + sorted(_orphans)
            emit("log", id=job_id, level="debug", module="driver",
                 message=f"detected {len(_orphans)} untracked output(s): "
                         + ", ".join(os.path.basename(p) for p in sorted(_orphans)))

        # ---- post-process: keep only the wanted stems --------------------
        # Build a reverse map: filename_without_ext -> canonical_stem. Values
        # in custom_output_names are already extension-free; using splitext
        # would corrupt names that contain dots (e.g. "HUMBLE. (Instrumental)").
        name_to_stem = {
            os.path.basename(v): k
            for k, v in custom_output_names.items()
        }

        def identify_stem(out_path):
            fname = os.path.basename(out_path)
            stem_part = os.path.splitext(fname)[0]
            # First try: exact match against names we sent in
            if stem_part in name_to_stem:
                return name_to_stem[stem_part]
            # Fallback: regex against library default convention
            # "..._(StemName)..." or "..._(StemName)"
            m = re.search(r"_\(([^)]+)\)(?:\.[^.]+)?$", fname)
            if not m:
                m = re.search(r"_\(([^)]+)\)", fname)
            return _canon(m.group(1)) if m else "Unknown"

        kept, discarded = [], []
        for out_path in outputs:
            # Single-model separators return just the filename; ensemble
            # paths are already absolute. Normalize to absolute against
            # output_dir so post-processing actually finds the file.
            if not os.path.isabs(out_path):
                out_path = os.path.join(output_dir, out_path)
            stem = identify_stem(out_path)

            if stems_to_keep and stem not in stems_to_keep:
                try:
                    os.remove(out_path)
                except OSError as e:
                    emit("log", id=job_id, level="warning", module="driver",
                         message=f"failed to delete unwanted stem {out_path}: {e}")
                discarded.append({"stem": stem, "path": out_path})
                emit("stem_discarded", id=job_id, stem=stem, path=out_path)
            else:
                # Enforce expected name if the library didn't honour custom_output_names.
                expected = custom_output_names.get(stem)
                if expected:
                    ext = os.path.splitext(out_path)[1].lower()
                    target = os.path.join(output_dir, expected + ext)
                    if os.path.normcase(out_path) != os.path.normcase(target):
                        try:
                            os.replace(out_path, target)
                            out_path = target
                        except OSError as e:
                            emit("log", id=job_id, level="warning", module="driver",
                                 message=f"rename {os.path.basename(out_path)!r} → "
                                         f"{os.path.basename(target)!r} failed: {e}")
                kept.append({"stem": stem, "path": out_path})
                emit("stem_written", id=job_id, stem=stem, path=out_path)

        # ---- trim/pad stems to source length --------------------------------
        _trim_stems_to_source_length(audio, [item["path"] for item in kept], job_id)

        emit(
            "job_completed",
            id=job_id,
            outputs=kept,
            discarded=discarded,
            duration_seconds=round(time.perf_counter() - t0, 3),
        )

    # -- main loop ----------------------------------------------------------

    DISPATCH = {
        "ping": cmd_ping,
        "list_presets": cmd_list_presets,
        "list_models": cmd_list_models,
        "download_models": cmd_download_models,
        "cancel": cmd_cancel,
        "shutdown": cmd_shutdown,
    }

    def run(self):
        # Emit ready event with environment info
        sep = self._get_separator()
        device = "cpu"
        try:
            if sep.torch_device is not None:
                device = str(sep.torch_device)
        except Exception:
            pass

        sep_version = "unknown"
        try:
            from importlib import metadata
            sep_version = metadata.distribution("audio-separator").version
        except Exception:
            pass

        emit(
            "ready",
            driver_version=DRIVER_VERSION,
            separator_version=sep_version,
            device=device,
            model_dir=self.model_file_dir,
            preset_ids=sorted(sep.list_ensemble_presets().keys()),
        )

        # Read commands line-by-line from stdin.
        # "run" is dispatched to a background thread so the main loop can
        # receive "cancel" while a job is in progress. All other commands
        # run on the main thread (they are fast and non-blocking).
        self._job_thread: threading.Thread | None = None

        for raw in sys.stdin:
            raw = raw.strip()
            if not raw:
                continue
            try:
                cmd = json.loads(raw)
            except json.JSONDecodeError as e:
                emit(
                    "error", error=f"invalid JSON command: {e}", line=raw[:200])
                continue

            name = cmd.get("cmd")

            if name == "run":
                if self._job_thread and self._job_thread.is_alive():
                    emit("error", error="a job is already running; send cancel first")
                    continue
                self._job_thread = threading.Thread(
                    target=self.cmd_run, args=(cmd,), daemon=True
                )
                self._job_thread.start()
                continue

            if name == "shutdown":
                # Cancel any running job and wait briefly before exiting.
                if self._job_thread and self._job_thread.is_alive():
                    _job_cancel_event.set()
                    self._job_thread.join(timeout=10.0)
                self.cmd_shutdown(cmd)
                continue

            handler = self.DISPATCH.get(name)
            if handler is None:
                emit("error", error=f"unknown command: {name!r}")
                continue
            try:
                handler(self, cmd)
            except SystemExit:
                raise
            except Exception as e:
                emit(
                    "error",
                    error=f"{type(e).__name__}: {e}",
                    traceback=traceback.format_exc(),
                    cmd=name,
                )

        # stdin closed -> graceful exit
        emit("bye")


def main():
    p = argparse.ArgumentParser(
        description="audio-separator JSON-Lines driver")
    p.add_argument("--model-dir", default=os.environ.get(
        "AUDIO_SEPARATOR_MODEL_DIR", "/tmp/audio-separator-models/"))
    p.add_argument("--log-level", default="info",
                   choices=["debug", "info", "warning", "error"],
                   help="Minimum log level to forward as `log` events (default: info).")
    p.add_argument("--debug", action="store_true",
                   help="Shortcut for --log-level=debug.")
    p.add_argument("--ffmpeg-path", default=None,
                   help="Absolute path to ffmpeg. Prepends its parent directory to PATH "
                        "so audio-separator's subprocess calls find the right binary.")
    args = p.parse_args()

    if args.ffmpeg_path:
        ffmpeg_dir = os.path.dirname(os.path.abspath(args.ffmpeg_path))
        if ffmpeg_dir:
            os.environ["PATH"] = ffmpeg_dir + os.pathsep + os.environ.get("PATH", "")

    if args.debug:
        log_level = logging.DEBUG
    else:
        log_level = getattr(logging, args.log_level.upper())
    driver = Driver(model_file_dir=args.model_dir, log_level=log_level)
    driver.run()


if __name__ == "__main__":
    main()
