"""Probe which GPU variant of audio-separator is actually functional in this environment.

Run with the Python interpreter from the audio-separator uv tool environment. Prints exactly
one of: Cuda, DirectML, Cpu. Checks functional support (can torch see CUDA, which providers
does onnxruntime expose) rather than merely which packages are installed, because a CUDA-extras
install can silently fall back to CPU when the matching torch wheel is missing.
"""

try:
    import torch

    torch_gpu = torch.cuda.is_available()
except ImportError:
    torch_gpu = False

import onnxruntime as ort

providers = ort.get_available_providers()

if torch_gpu and "CUDAExecutionProvider" in providers:
    print("Cuda")
elif "DMLExecutionProvider" in providers:
    print("DirectML")
else:
    print("Cpu")
