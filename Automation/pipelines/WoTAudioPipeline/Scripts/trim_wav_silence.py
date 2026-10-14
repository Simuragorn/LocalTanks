#!/usr/bin/env python3
"""Trim reproducible trailing near-silence from PCM WAV clips."""

from __future__ import annotations

import argparse
import array
import math
import os
from pathlib import Path
import tempfile
import wave


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("path", type=Path)
    parser.add_argument("--threshold-db", type=float, required=True)
    parser.add_argument("--tail-ms", type=int, default=150)
    parser.add_argument("--window-ms", type=int, default=20)
    return parser.parse_args()


def trim(path: Path, threshold_db: float, tail_ms: int, window_ms: int) -> bool:
    with wave.open(str(path), "rb") as source:
        params = source.getparams()
        frames = source.readframes(params.nframes)

    if params.sampwidth != 2:
        raise ValueError(f"Only 16-bit PCM WAV is supported, got {params.sampwidth * 8}-bit: {path}")

    samples = array.array("h")
    samples.frombytes(frames)
    if not samples:
        return False

    samples_per_frame = params.nchannels
    window_frames = max(1, round(params.framerate * window_ms / 1000))
    window_samples = window_frames * samples_per_frame
    threshold = 32767.0 * math.pow(10.0, threshold_db / 20.0)
    last_active_sample = -1

    for start in range(0, len(samples), window_samples):
        block = samples[start : start + window_samples]
        if not block:
            continue
        rms = math.sqrt(sum(value * value for value in block) / len(block))
        if rms >= threshold:
            last_active_sample = min(len(samples), start + len(block))

    if last_active_sample < 0:
        return False

    tail_samples = round(params.framerate * tail_ms / 1000) * samples_per_frame
    keep_samples = min(len(samples), last_active_sample + tail_samples)
    keep_samples -= keep_samples % samples_per_frame
    if keep_samples >= len(samples):
        return False

    trimmed = array.array("h", samples[:keep_samples]).tobytes()
    handle, temporary_name = tempfile.mkstemp(prefix=path.stem + "-", suffix=".wav", dir=path.parent)
    os.close(handle)
    temporary_path = Path(temporary_name)
    try:
        with wave.open(str(temporary_path), "wb") as target:
            target.setparams(params._replace(nframes=keep_samples // samples_per_frame))
            target.writeframes(trimmed)
        os.replace(temporary_path, path)
    finally:
        temporary_path.unlink(missing_ok=True)
    return True


def main() -> int:
    args = parse_args()
    changed = trim(args.path, args.threshold_db, args.tail_ms, args.window_ms)
    print(f"{'trimmed' if changed else 'unchanged'}: {args.path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
