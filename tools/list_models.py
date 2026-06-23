#!/usr/bin/env python3
"""
list_models.py — lightweight standalone tool to list supported separation models.

Reproduces the model catalog that audio_separator.Separator.list_supported_model_files()
builds, but WITHOUT importing the audio_separator package (which pulls in torch at module
load even though no inference happens here). The model registry ships as static JSON data
files inside the package (models.json, models-scores.json) plus a cached remote list
(download_checks.json) under the models directory. We locate those files via
importlib.metadata and json.load them directly.

Usage: list_models.py <models_dir>
  <models_dir>  Directory where audio-separator caches download_checks.json. Optional;
                if omitted or the file is absent, only the bundled lists are used.

Prints the model catalog as JSON to stdout, grouped by architecture, in the same shape
the C# ModelCatalogService parses: { "<arch>": { "<friendly name>": { "filename", "stems",
"scores": { "<stem>": { "SDR": ... } } } } }.

Exit code 1 if the bundled package data cannot be located.
"""

import json
import os
import sys
from importlib import metadata


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


def _load_bundled(name):
    path = _locate(name)
    if path is None or not os.path.exists(path):
        raise FileNotFoundError(f"bundled data file not found: {name}")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def _entry(filename, scores):
    """Build a single model entry matching list_supported_model_files() output."""
    score_data = scores.get(filename, {})
    # median_scores mixes per-stem score objects ({"SDR": ...}) with scalar metrics such as
    # "seconds_per_minute_m3". Keep only the per-stem objects so the emitted contract is a clean
    # map of stem name -> score object.
    median = score_data.get("median_scores", {})
    stem_scores = {k: v for k, v in median.items() if isinstance(v, dict)}
    return {
        "filename": filename,
        "scores": stem_scores,
        "stems": score_data.get("stems", []),
        "target_stem": score_data.get("target_stem"),
    }


def build_catalog(models_dir):
    models = _load_bundled("models.json")
    scores = _load_bundled("models-scores.json")

    # download_checks.json is fetched remotely by audio-separator and cached under the
    # models directory. When present it adds the UVR-hosted model lists; when absent we
    # still emit the bundled audio-separator lists so listing degrades gracefully offline.
    download_checks = {}
    if models_dir:
        dl_path = os.path.join(models_dir, "download_checks.json")
        if os.path.exists(dl_path):
            with open(dl_path, encoding="utf-8") as fh:
                download_checks = json.load(fh)

    def merged(*keys):
        out = {}
        for key in keys:
            out.update(download_checks.get(key, {}))
            out.update(models.get(key, {}))
        return out

    # VR and MDX models map friendly name -> single filename string.
    vr = {
        name: _entry(filename, scores)
        for name, filename in merged("vr_download_list").items()
    }
    mdx = {
        name: _entry(filename, scores)
        for name, filename in merged(
            "mdx_download_list", "mdx_download_vip_list"
        ).items()
    }

    # Demucs v4 models map friendly name -> { filename/url: localname }; the .yaml is the id.
    demucs = {}
    for name, files in download_checks.get("demucs_download_list", {}).items():
        if not name.startswith("Demucs v4"):
            continue
        yaml_file = next(
            (fn for fn in files.keys() if fn.endswith(".yaml")), None
        )
        if yaml_file:
            demucs[name] = _entry(yaml_file, scores)

    # MDXC (MDX23C + RoFormer) models map friendly name -> { filename: config }; first key is the id.
    mdxc = {}
    for name, files in merged(
        "mdx23c_download_list", "mdx23c_download_vip_list", "roformer_download_list"
    ).items():
        first = next(iter(files.keys()), None)
        if first:
            mdxc[name] = _entry(first, scores)

    return {"VR": vr, "MDX": mdx, "Demucs": demucs, "MDXC": mdxc}


def main():
    models_dir = sys.argv[1] if len(sys.argv) > 1 else ""
    try:
        catalog = build_catalog(models_dir)
    except FileNotFoundError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    except Exception as exc:  # noqa: BLE001 — surface any unexpected failure to the caller
        print(f"error: failed to list models: {exc}", file=sys.stderr)
        return 1

    json.dump(catalog, sys.stdout, indent=None)
    print()  # trailing newline
    return 0


if __name__ == "__main__":
    sys.exit(main())
