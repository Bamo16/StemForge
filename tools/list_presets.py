#!/usr/bin/env python3
"""
list_presets.py — lightweight standalone tool to list built-in ensemble presets.

Prints the preset dictionary as JSON to stdout.
Exit code 1 if audio-separator is not installed.
"""

import json
import sys

try:
    from audio_separator.separator import Separator
except ImportError as exc:
    print(f"error: audio-separator not installed: {exc}", file=sys.stderr)
    sys.exit(1)

try:
    sep = Separator()
    presets = sep.list_ensemble_presets()
    json.dump(presets, sys.stdout, indent=None)
    print()  # trailing newline
except Exception as exc:
    print(f"error: failed to list presets: {exc}", file=sys.stderr)
    sys.exit(1)
