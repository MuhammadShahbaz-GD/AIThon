"""Deterministically generates the original Candy Plastic Doll audio library.

The generated WAV files are deliberately source-controlled masters. Unity owns
runtime compression through AudioImporter settings authored by the accompanying
CandyPlasticAudioSetupEditor.
"""

from __future__ import annotations

import math
import wave
from pathlib import Path

import numpy as np


SAMPLE_RATE = 48_000
ROOT = Path(__file__).resolve().parents[1]
RNG = np.random.default_rng(0xCA7D1)


def time_axis(duration: float) -> np.ndarray:
    return np.arange(max(1, int(duration * SAMPLE_RATE)), dtype=np.float64) / SAMPLE_RATE


def decay(duration: float, rate: float = 8.0, attack: float = 0.003) -> np.ndarray:
    t = time_axis(duration)
    return np.minimum(1.0, t / max(attack, 0.0001)) * np.exp(-rate * t)


def oscillator(duration: float, start_hz: float, end_hz: float | None = None,
               harmonic: float = 0.0) -> np.ndarray:
    end_hz = start_hz if end_hz is None else end_hz
    t = time_axis(duration)
    frequency = start_hz + (end_hz - start_hz) * (t / max(duration, 0.0001))
    phase = 2.0 * np.pi * np.cumsum(frequency) / SAMPLE_RATE
    result = np.sin(phase)
    if harmonic:
        result += harmonic * np.sin(phase * 2.0 + 0.35)
    return result


def colored_noise(duration: float, brightness: float = 0.65) -> np.ndarray:
    count = max(1, int(duration * SAMPLE_RATE))
    white = RNG.normal(0.0, 1.0, count)
    smooth = np.convolve(white, np.ones(9) / 9.0, mode="same")
    return white * brightness + smooth * (1.0 - brightness)


def delayed(signal: np.ndarray, delay_seconds: float, gain: float = 1.0) -> np.ndarray:
    offset = int(delay_seconds * SAMPLE_RATE)
    output = np.zeros(len(signal) + offset, dtype=np.float64)
    output[offset:offset + len(signal)] = signal * gain
    return output


def mix(*signals: np.ndarray) -> np.ndarray:
    length = max(len(signal) for signal in signals)
    output = np.zeros(length, dtype=np.float64)
    for signal in signals:
        output[:len(signal)] += signal
    return output


def plastic_shell(duration: float, base: float, strength: float = 1.0) -> np.ndarray:
    """Short, hollow and slightly dull resonances of a lightweight plastic shell."""
    partials = ((1.0, 1.0), (1.43, 0.46), (2.08, 0.25), (2.71, 0.12))
    result = np.zeros(len(time_axis(duration)), dtype=np.float64)
    for index, (ratio, gain) in enumerate(partials):
        result += oscillator(duration, base * ratio) * decay(
            duration, 12.0 + index * 5.5, 0.0012) * gain
    result += colored_noise(duration, 0.34) * decay(duration, 24.0, 0.0005) * 0.12
    return result * strength


def candy_click(pitch: float, variant: int = 0) -> np.ndarray:
    duration = 0.18
    click = oscillator(duration, pitch * (1.08 + variant * 0.01), pitch, 0.25)
    sparkle = delayed(plastic_shell(0.12, pitch * 1.36, 0.19), 0.018)
    return mix(click * decay(duration, 22.0, 0.001), sparkle)


def ui_panel() -> np.ndarray:
    duration = 0.32
    return mix(
        oscillator(duration, 330.0, 510.0, 0.22) * decay(duration, 7.0, 0.008) * 0.55,
        delayed(candy_click(680.0), 0.09, 0.55))


def spring_sound(variant: int = 0, stretched: bool = False) -> np.ndarray:
    duration = 0.52 if stretched else 0.42
    t = time_axis(duration)
    start = 250.0 + variant * 28.0
    wobble = np.sin(2.0 * np.pi * (17.0 + variant) * t) * (72.0 if stretched else 46.0)
    phase = 2.0 * np.pi * np.cumsum(start + wobble) / SAMPLE_RATE
    metallic = np.sin(phase) + 0.38 * np.sin(phase * 2.76)
    return metallic * decay(duration, 6.5 if stretched else 9.0, 0.002)


def candy_rattle(variant: int = 0) -> np.ndarray:
    duration = 0.5
    output = np.zeros(len(time_axis(duration)), dtype=np.float64)
    for index in range(8):
        pitch = 760.0 + RNG.uniform(-170.0, 230.0)
        grain = candy_click(pitch, index & 1) * RNG.uniform(0.18, 0.38)
        offset = int((0.025 + index * 0.048 + RNG.uniform(0.0, 0.02)) * SAMPLE_RATE)
        end = min(len(output), offset + len(grain))
        output[offset:end] += grain[:end - offset]
    return output * decay(duration, 2.1 + variant * 0.25, 0.003)


def plastic_impact(tier: int, variant: int) -> np.ndarray:
    duration = (0.2, 0.32, 0.5)[tier]
    base = (520.0, 325.0, 205.0)[tier] * (1.0 + (variant - 1) * 0.06)
    shell = plastic_shell(duration, base, (0.66, 0.86, 1.0)[tier])
    noise = colored_noise(duration, 0.88) * decay(
        duration, (38.0, 27.0, 18.0)[tier], 0.0005)
    hollow = oscillator(duration, (260.0, 175.0, 108.0)[tier],
                        (190.0, 118.0, 64.0)[tier], 0.18) * decay(
        duration, (22.0, 15.0, 10.0)[tier], 0.001)
    return mix(shell, noise * (0.08 + tier * 0.07), hollow * (0.16 + tier * 0.2))


def plastic_stress(severe: bool = False) -> np.ndarray:
    duration = 0.72 if severe else 0.46
    t = time_axis(duration)
    creak_frequency = 118.0 + 42.0 * np.sin(2.0 * np.pi * 5.5 * t)
    creak_phase = 2.0 * np.pi * np.cumsum(creak_frequency) / SAMPLE_RATE
    creak = np.sin(creak_phase) * np.sin(np.pi * t / duration) ** 1.4
    snap_time = 0.36 if severe else 0.25
    snap = delayed(
        mix(
            plastic_shell(0.22, 245.0 if severe else 340.0, .88),
            colored_noise(0.16, .75) * decay(.16, 31.0, .0003) * .28),
        snap_time)
    if severe:
        second_snap = delayed(plastic_shell(.2, 190.0, .62), .51)
        return mix(creak * .34, snap, second_snap)
    return mix(creak * .24, snap)


def limb_break(variant: int) -> np.ndarray:
    return mix(
        plastic_impact(2, variant),
        plastic_stress(True),
        delayed(spring_sound(variant), 0.04, 0.62),
        delayed(candy_rattle(variant), 0.075, 0.56))


def whoosh(duration: float, high: bool = False) -> np.ndarray:
    noise = colored_noise(duration, 0.78)
    t = time_axis(duration)
    envelope = np.sin(np.pi * np.clip(t / duration, 0.0, 1.0)) ** 1.7
    carrier = oscillator(duration, 620.0 if high else 240.0,
                         980.0 if high else 420.0)
    return (noise * 0.47 + carrier * 0.18) * envelope


def jelly_splat(variant: int = 0) -> np.ndarray:
    duration = 0.58
    low = oscillator(duration, 118.0 + variant * 13.0, 48.0, 0.28)
    bubbles = np.zeros(len(time_axis(duration)), dtype=np.float64)
    for index in range(5):
        bubble = oscillator(0.12, 270.0 + index * 85.0, 110.0) * decay(0.12, 17.0)
        bubbles = mix(bubbles, delayed(bubble, 0.055 + index * 0.07, 0.24))
    return mix(low * decay(duration, 8.0, 0.004), bubbles,
               colored_noise(duration, 0.2) * decay(duration, 13.0) * 0.12)


def cannon_fire(variant: int = 0, charged: bool = False) -> np.ndarray:
    duration = 0.9 if charged else 0.56
    boom = oscillator(duration, 105.0 - variant * 4.0, 39.0, 0.22) * decay(
        duration, 6.0 if charged else 9.0, 0.0007)
    pop = colored_noise(duration, 0.9) * decay(duration, 24.0, 0.0002) * 0.44
    candy = delayed(candy_rattle(variant), 0.035, 0.42 if charged else 0.27)
    sparkle = delayed(plastic_shell(0.28, 430.0 + variant * 28.0, 0.38), 0.05)
    charge = oscillator(duration, 290.0, 940.0) * (
        np.linspace(0.0, 1.0, len(time_axis(duration))) ** 2) * 0.22 if charged else np.zeros(1)
    return mix(boom, pop, candy, sparkle, charge)


def cartoon_voice(kind: str, variant: int = 0) -> np.ndarray:
    specs = {
        "smile": (0.42, 460.0, 690.0, 6.5),
        "gasp": (0.36, 330.0 + variant * 24.0, 760.0, 7.5),
        "annoyed": (0.54, 360.0, 245.0, 4.8),
        "cry": (0.86, 510.0, 290.0, 3.8),
        "ko": (0.72, 280.0, 105.0, 4.2),
        "relief": (0.58, 330.0, 520.0, 5.0),
    }
    duration, start, end, rate = specs[kind]
    t = time_axis(duration)
    vibrato = np.sin(2.0 * np.pi * (7.0 if kind == "cry" else 5.2) * t) * (
        26.0 if kind == "cry" else 12.0)
    frequency = start + (end - start) * (t / duration) + vibrato
    phase = 2.0 * np.pi * np.cumsum(frequency) / SAMPLE_RATE
    voice = np.sin(phase) + 0.28 * np.sin(phase * 2.0) + 0.1 * np.sin(phase * 3.0)
    if kind == "annoyed":
        voice *= 0.72 + 0.28 * np.sign(np.sin(2.0 * np.pi * 8.0 * t))
    return voice * decay(duration, rate, 0.02)


def combo(level: int) -> np.ndarray:
    root = 520.0 * (2.0 ** ((level - 1) * 2.0 / 12.0))
    return mix(
        candy_click(root),
        delayed(candy_click(root * 1.25), 0.07, 0.8),
        delayed(candy_click(root * 1.5), 0.14, 0.72))


def death_blast() -> np.ndarray:
    duration = 1.85
    low = oscillator(duration, 92.0, 31.0, 0.2) * decay(duration, 3.9, 0.0006)
    blast = colored_noise(duration, 0.93) * decay(duration, 6.0, 0.0002) * 0.56
    shell_burst = mix(plastic_impact(2, 0), plastic_stress(True),
                      delayed(plastic_impact(2, 2), .11, .72))
    layers = [low, blast, shell_burst, limb_break(1),
              delayed(candy_rattle(0), 0.12, 0.8),
              delayed(candy_rattle(1), 0.3, 0.68),
              delayed(spring_sound(1), 0.16, 0.72),
              delayed(combo(3), 0.62, 0.54)]
    return mix(*layers)


def seamless_music(gameplay: bool) -> np.ndarray:
    bpm = 128.0 if gameplay else 104.0
    beats = 32
    duration = beats * 60.0 / bpm
    t = time_axis(duration)
    output = np.zeros(len(t), dtype=np.float64)
    scale = [261.63, 329.63, 392.0, 523.25, 440.0, 392.0, 329.63, 293.66]
    if gameplay:
        scale = [293.66, 369.99, 440.0, 587.33, 493.88, 440.0, 369.99, 329.63]
    beat_duration = 60.0 / bpm
    for beat in range(beats):
        start = int(beat * beat_duration * SAMPLE_RATE)
        note = scale[beat % len(scale)]
        length = int(beat_duration * SAMPLE_RATE)
        local_t = np.arange(length) / SAMPLE_RATE
        pluck = (np.sin(2.0 * np.pi * note * local_t) +
                 0.2 * np.sin(2.0 * np.pi * note * 2.0 * local_t)) * np.exp(-5.2 * local_t)
        end = min(len(output), start + length)
        output[start:end] += pluck[:end - start] * 0.26
        if gameplay and beat % 2 == 0:
            kick = oscillator(0.22, 108.0, 43.0) * decay(0.22, 15.0, 0.0003)
            end = min(len(output), start + len(kick))
            output[start:end] += kick[:end - start] * 0.18
    # Quiet factory air and candy-machine shimmer; both are periodic for a clean loop.
    phase = 2.0 * np.pi * t / duration
    output += np.sin(phase * 8.0) * 0.012 + np.sin(phase * 13.0) * 0.008
    fade = int(0.02 * SAMPLE_RATE)
    output[:fade] *= np.linspace(0.0, 1.0, fade)
    output[-fade:] *= np.linspace(1.0, 0.0, fade)
    return np.column_stack((output, np.roll(output, int(0.009 * SAMPLE_RATE))))


def master(signal: np.ndarray, peak: float = 0.92) -> np.ndarray:
    signal = np.nan_to_num(signal)
    signal = np.tanh(signal * 1.18)
    maximum = float(np.max(np.abs(signal))) if signal.size else 1.0
    return signal * (peak / max(maximum, 0.0001))


def save(relative_path: str, signal: np.ndarray, peak: float = 0.92) -> None:
    path = ROOT / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    normalized = master(signal, peak)
    pcm = np.clip(normalized * 32767.0, -32768.0, 32767.0).astype("<i2")
    channels = 1 if pcm.ndim == 1 else pcm.shape[1]
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(channels)
        wav.setsampwidth(2)
        wav.setframerate(SAMPLE_RATE)
        wav.writeframes(pcm.tobytes())


def generate() -> None:
    save("SFX/UI/UI_Click_01.wav", candy_click(720.0, 0))
    save("SFX/UI/UI_Click_02.wav", candy_click(790.0, 1))
    save("SFX/UI/UI_Toggle.wav", mix(candy_click(560.0), delayed(candy_click(840.0), 0.07)))
    save("SFX/UI/UI_Panel.wav", ui_panel())
    save("SFX/UI/UI_Confirm.wav", combo(1))

    save("SFX/Ragdoll/Ragdoll_Grab.wav", oscillator(0.18, 260.0, 390.0) * decay(0.18, 13.0))
    save("SFX/Ragdoll/Ragdoll_Release.wav", oscillator(0.2, 430.0, 220.0) * decay(0.2, 12.0))
    save("SFX/Ragdoll/Ragdoll_Stretch.wav", spring_sound(0, True))
    save("SFX/Ragdoll/Spring_Recoil_01.wav", spring_sound(0))
    save("SFX/Ragdoll/Spring_Recoil_02.wav", spring_sound(1))
    save("SFX/Ragdoll/Candy_Rattle_01.wav", candy_rattle(0))
    save("SFX/Ragdoll/Candy_Rattle_02.wav", candy_rattle(1))

    tiers = ("Light", "Medium", "Heavy")
    for tier in range(3):
        for variant in range(3):
            save(f"SFX/Impacts/Plastic_{tiers[tier]}_{variant + 1:02d}.wav",
                 plastic_impact(tier, variant))
    save("SFX/Impacts/Plastic_Stress_New.wav", plastic_stress(False))
    save("SFX/Impacts/Plastic_Stress_Severe.wav", plastic_stress(True))
    save("SFX/Impacts/Spring_Detach.wav", spring_sound(1, True))
    save("SFX/Impacts/Limb_Break_01.wav", limb_break(0))
    save("SFX/Impacts/Limb_Break_02.wav", limb_break(1))

    save("SFX/Tools/Lollipop_Swing.wav", whoosh(0.34, True))
    save("SFX/Tools/Lollipop_Hit_01.wav", mix(plastic_impact(1, 0), candy_click(410.0)))
    save("SFX/Tools/Lollipop_Hit_02.wav", mix(plastic_impact(1, 2), candy_click(455.0)))
    save("SFX/Tools/Jelly_Throw.wav", whoosh(0.3, False) * 0.74)
    save("SFX/Tools/Jelly_Splat_01.wav", jelly_splat(0))
    save("SFX/Tools/Jelly_Splat_02.wav", jelly_splat(1))
    save("SFX/Tools/Jelly_Stick.wav", jelly_splat(0)[:int(0.3 * SAMPLE_RATE)])
    save("SFX/Tools/Jelly_Slide.wav",
         colored_noise(0.72, 0.12) * decay(0.72, 3.2, 0.018) * 0.65)

    for variant in range(3):
        save(f"SFX/Cannon/Cannon_Fire_{variant + 1:02d}.wav", cannon_fire(variant))
    save("SFX/Cannon/Cannon_Charged.wav", cannon_fire(1, True))
    save("SFX/Cannon/Cannon_Impact_01.wav", mix(plastic_impact(2, 0), candy_click(330.0)))
    save("SFX/Cannon/Cannon_Impact_02.wav", mix(plastic_impact(2, 2), candy_click(370.0)))
    save("SFX/Cannon/Cannon_Miss.wav", whoosh(0.48, True) * 0.7)

    save("SFX/Character/Character_Smile.wav", cartoon_voice("smile"))
    save("SFX/Character/Character_Gasp_01.wav", cartoon_voice("gasp", 0))
    save("SFX/Character/Character_Gasp_02.wav", cartoon_voice("gasp", 1))
    save("SFX/Character/Character_Annoyed.wav", cartoon_voice("annoyed"))
    save("SFX/Character/Character_Cry.wav", cartoon_voice("cry"))
    save("SFX/Character/Character_KO.wav", cartoon_voice("ko"))
    save("SFX/Character/Character_Relief.wav", cartoon_voice("relief"))

    for level in range(1, 4):
        save(f"SFX/Flow/Combo_{level:02d}.wav", combo(level))
    save("SFX/Flow/Coin_01.wav", candy_click(940.0, 0))
    save("SFX/Flow/Coin_02.wav", candy_click(1050.0, 1))
    save("SFX/Flow/Level_Start.wav", combo(2))
    save("SFX/Flow/Level_Complete.wav",
         mix(combo(3), delayed(combo(3), 0.24, 0.72)))
    save("SFX/Flow/Level_Failed.wav",
         oscillator(0.8, 410.0, 155.0, 0.2) * decay(0.8, 3.2, 0.012))
    save("SFX/Death/Plastic_Doll_Death_Burst.wav", death_blast(), 0.96)

    save("Music/Menu_Candy_Factory_Loop.wav", seamless_music(False), 0.72)
    save("Music/Gameplay_Candy_Action_Loop.wav", seamless_music(True), 0.76)
    print(f"Generated {len(list(ROOT.rglob('*.wav')))} WAV masters in {ROOT}")


if __name__ == "__main__":
    generate()
