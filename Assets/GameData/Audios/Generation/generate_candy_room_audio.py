"""Deterministic candy-room SFX plus 48 kHz normalization for offline SAPI voice masters."""
from __future__ import annotations

import math
import random
import wave
from pathlib import Path

import numpy as np

RATE = 48_000
ROOT = Path(__file__).resolve().parents[1]
SFX = ROOT / "SFX" / "CandyRoom"
VOICE = ROOT / "SFX" / "Character" / "CandyRoomVoice"
RNG = random.Random(20260718)


def envelope(length: int, attack: float, release: float) -> np.ndarray:
    result = np.ones(length, dtype=np.float64)
    attack_count = max(1, min(length, int(RATE * attack)))
    release_count = max(1, min(length, int(RATE * release)))
    result[:attack_count] = np.linspace(0.0, 1.0, attack_count)
    result[-release_count:] *= np.linspace(1.0, 0.0, release_count)
    return result


def write(name: str, samples: np.ndarray) -> None:
    SFX.mkdir(parents=True, exist_ok=True)
    peak = max(1e-9, float(np.max(np.abs(samples))))
    pcm = np.int16(np.clip(samples / peak * 0.88, -1.0, 1.0) * 32767)
    with wave.open(str(SFX / name), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(pcm.tobytes())


def noise(count: int, seed: int) -> np.ndarray:
    local = np.random.default_rng(seed)
    return local.uniform(-1.0, 1.0, count)


def plastic_hit(seed: int, heavy: float) -> np.ndarray:
    duration = 0.22 + 0.1 * heavy
    count = int(RATE * duration)
    t = np.arange(count) / RATE
    shell = np.sin(2 * math.pi * (245 - 55 * heavy) * t) * np.exp(-t * (14 - 4 * heavy))
    hollow = np.sin(2 * math.pi * (510 - 90 * heavy) * t + 0.35) * np.exp(-t * 23)
    click = noise(count, seed) * np.exp(-t * 55)
    candy = np.sin(2 * math.pi * 1150 * t) * np.exp(-t * 31)
    return (shell * 0.55 + hollow * 0.25 + click * 0.18 + candy * 0.11) * envelope(count, .002, .06)


def gummy(seed: int, impact: bool) -> np.ndarray:
    duration = 0.28 if impact else 0.2
    count = int(RATE * duration)
    t = np.arange(count) / RATE
    start = 310 if impact else 470
    end = 115 if impact else 330
    phase = 2 * math.pi * (start * t + (end - start) * t * t / (2 * duration))
    rubber = np.sin(phase) * np.exp(-t * (9 if impact else 14))
    squish = noise(count, seed)
    squish = np.convolve(squish, np.ones(32) / 32, mode="same") * np.exp(-t * 18)
    return (rubber * 0.72 + squish * 0.38) * envelope(count, .004, .08)


def gun(seed: int, impact: bool) -> np.ndarray:
    duration = 0.3 if impact else 0.24
    count = int(RATE * duration)
    t = np.arange(count) / RATE
    pop = noise(count, seed) * np.exp(-t * (46 if impact else 62))
    toy = np.sin(2 * math.pi * (190 if impact else 280) * t) * np.exp(-t * 15)
    sparkle = np.sin(2 * math.pi * (920 if impact else 1250) * t) * np.exp(-t * 31)
    return (pop * .42 + toy * .58 + sparkle * .18) * envelope(count, .001, .07)


def swing(seed: int) -> np.ndarray:
    duration = .24
    count = int(RATE * duration)
    t = np.arange(count) / RATE
    whoosh = noise(count, seed)
    whoosh = np.convolve(whoosh, np.ones(18) / 18, mode="same")
    shape = np.sin(math.pi * np.clip(t / duration, 0, 1)) ** 2
    whistle = np.sin(2 * math.pi * (640 * t + 720 * t * t)) * shape
    return (whoosh * .6 + whistle * .18) * shape * envelope(count, .01, .04)


def normalize_voice(path: Path) -> None:
    with wave.open(str(path), "rb") as source:
        channels = source.getnchannels()
        width = source.getsampwidth()
        rate = source.getframerate()
        frames = source.readframes(source.getnframes())
    if width != 2:
        raise RuntimeError(f"Expected 16-bit SAPI wave: {path}")
    signal = np.frombuffer(frames, dtype=np.int16).astype(np.float64)
    if channels > 1:
        signal = signal.reshape(-1, channels).mean(axis=1)
    if rate != RATE:
        source_x = np.linspace(0.0, 1.0, len(signal), endpoint=False)
        target_count = max(1, round(len(signal) * RATE / rate))
        target_x = np.linspace(0.0, 1.0, target_count, endpoint=False)
        signal = np.interp(target_x, source_x, signal)
    peak = max(1.0, float(np.max(np.abs(signal))))
    pcm = np.int16(np.clip(signal / peak * 0.9, -1.0, 1.0) * 32767)
    with wave.open(str(path), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(pcm.tobytes())


def main() -> None:
    write("Candy_Tool_Swing_01.wav", swing(101))
    write("Candy_Tool_Swing_02.wav", swing(102))
    write("Candy_Tool_Hit_01.wav", plastic_hit(111, .55))
    write("Candy_Tool_Hit_02.wav", plastic_hit(112, .72))
    write("Gummy_Throw.wav", gummy(121, False))
    write("Gummy_Hit_01.wav", gummy(122, True))
    write("Gummy_Hit_02.wav", gummy(123, True) * .92)
    write("Candy_Jar_Hit_01.wav", plastic_hit(131, .8))
    write("Candy_Jar_Hit_02.wav", plastic_hit(132, 1.0))
    write("Candy_Gun_Fire_01.wav", gun(141, False))
    write("Candy_Gun_Fire_02.wav", gun(142, False) * .94)
    write("Candy_Gun_Impact_01.wav", gun(151, True))
    write("Candy_Gun_Impact_02.wav", gun(152, True) * .96)
    if VOICE.exists():
        for path in VOICE.glob("*.wav"):
            normalize_voice(path)
    print(f"Candy-room audio generated at {SFX}; voice masters normalized at {VOICE}.")


if __name__ == "__main__":
    main()
