#!/usr/bin/env python3
"""
list_presets.py — lightweight standalone tool to list built-in ensemble presets.

The preset catalog ships as a static data file (ensemble_presets.json) inside the
audio_separator package. We locate it via importlib.metadata and json.load it directly,
WITHOUT importing the audio_separator package, which pulls in torch at module load even
though no inference happens here.

Prints the preset dictionary (preset id -> preset data) as JSON to stdout, matching the
shape Separator.list_ensemble_presets() returns.

Exit code 1 if the preset data file cannot be located.
"""

import json
import os
import sys
from importlib import metadata

PRESETS_FILE = "ensemble_presets.json"


def _locate(name):
    """Locate a data file shipped inside the audio_separator package, without importing it."""
    try:
        files = metadata.files("audio-separator") or []
    except metadata.PackageNotFoundError:
        return None
    for f in files:
        if f.name == name and f.parts and f.parts[0] == "audio_separator":
            return f.locate()
    return None


def main():
    path = _locate(PRESETS_FILE)
    if path is None or not os.path.exists(path):
        print(
            f"error: could not locate {PRESETS_FILE} (audio-separator not installed?)",
            file=sys.stderr,
        )
        return 1

    try:
        with open(path, encoding="utf-8") as fh:
            data = json.load(fh)
    except Exception as exc:  # noqa: BLE001 — surface any unexpected failure to the caller
        print(f"error: failed to read presets: {exc}", file=sys.stderr)
        return 1

    # list_ensemble_presets() returns the inner "presets" mapping, not the wrapper object.
    presets = data.get("presets", {})
    json.dump(presets, sys.stdout, indent=None)
    print()  # trailing newline
    return 0


if __name__ == "__main__":
    sys.exit(main())
