#!/usr/bin/env python3
"""Synthesises AVAAmidar's sound effects and tunes into ../src/AVAAmidar/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices. The title plays
In the Hall of the Mountain King (Edvard Grieg, 1875), quickening as it goes; the jungle tune in play is a new
composition. The one word is spoken by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAAmidar", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def line(bpm, notes, kind="pulse", mult=1.0, gain=0.55, release=0.6):
    """A tune as (note, beats), '-' a rest; mult transposes it."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note) * mult, kind=kind, release=release, gain=gain))
    return out


def drum(seconds, gain=1.0):
    return cat(tone(0.06, 160, 50, "sine", attack=0.001, release=1.0, gain=gain), silence(max(0, seconds - 0.06)))


def mountain_king():
    """In the Hall of the Mountain King (Grieg, 1875): the theme, again and again, louder, higher and faster."""
    theme = [("B3", 1), ("C#4", 1), ("D4", 1), ("E4", 1), ("F#4", 1), ("D4", 1), ("F#4", 2),
             ("F4", 1), ("C#4", 1), ("F4", 2), ("E4", 1), ("C4", 1), ("E4", 2),
             ("B3", 1), ("C#4", 1), ("D4", 1), ("E4", 1), ("F#4", 1), ("D4", 1), ("F#4", 1), ("B4", 1),
             ("A4", 1), ("F#4", 1), ("D4", 1), ("F#4", 1), ("A4", 4)]
    low = [("B2", 0.5), ("-", 0.5)] * 8 + [("C#3", 0.5), ("-", 0.5)] * 4 + [("C3", 0.5), ("-", 0.5)] * 4 + \
          [("B2", 0.5), ("-", 0.5)] * 8 + [("D3", 0.5), ("-", 0.5)] * 4 + [("A2", 0.5), ("-", 0.5)] * 4
    out = []
    for k, (bpm, mult, gain) in enumerate([(132, 1, 0.4), (150, 1, 0.5), (172, 2, 0.55), (200, 2, 0.6)]):
        out += mix(line(bpm, theme, "pulse", mult, gain, 0.4), line(bpm, low, "tri", 1, 0.6, 0.3))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The brush on the line; a box filling in; the four corners lit; a warrior caught.
    write("Paint.wav", tone(0.03, 900, 1100, "pulse", attack=0.002, release=0.8, gain=0.5), 0.08)
    write("Fill.wav", cat(*[tone(0.045, hz(n), kind="pulse", release=0.5) for n in ["C5", "E5", "G5", "C6", "E6"]]), 0.24)
    write("Corners.wav", cat(*[tone(0.06, hz(n), kind="square", release=0.4) for n in ["G4", "C5", "E5", "G5", "C6", "E6", "G6"]], tone(0.3, hz("C7"), kind="pulse", vibrato=0.02, release=0.8)), 0.28)
    write("Catch.wav", cat(tone(0.08, 300, 1200, "square", release=0.6), tone(0.12, 1200, 1800, "pulse", release=0.9)), 0.26)
    # The jump: the warriors in the air.
    write("Jump.wav", cat(tone(0.18, 200, 900, "square", attack=0.002, release=0.3, gain=0.6), tone(0.18, 900, 300, "square", release=0.9, gain=0.5)), 0.24)
    write("Die.wav", cat(*[tone(0.07, 800 - i * 45, 700 - i * 45, "square", release=0.5, gain=0.6) for i in range(14)], tone(0.4, 90, 40, "sine", release=1.0)), 0.34)
    write("Clear.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.4) for n in ["C5", "D5", "E5", "G5", "A5", "C6"]] * 2), 0.26)
    write("ExtraLife.wav", cat(*[tone(0.06, hz(n), kind="square", release=0.5) for n in ["E5", "G5", "E6", "C6", "D6", "G6"]]), 0.24)
    write("SayGo.wav", mix(say("G OW1 !", rate=RATE, pitch=120, speed=1.0, size=1.0), [s * 0.3 for s in say("G OW1 !", rate=RATE, pitch=180, speed=1.0, size=1.0, seed=11)]), 0.5)

    # In play: a jungle tune of its own, over drums.
    q, e = 1, 0.5
    tune = [("E5", e), ("G5", e), ("A5", q), ("G5", e), ("E5", e), ("D5", q), ("E5", e), ("G5", e), ("A5", e), ("C6", e), ("B5", 2),
            ("A5", e), ("G5", e), ("E5", q), ("D5", e), ("C5", e), ("D5", q), ("E5", e), ("D5", e), ("C5", e), ("A4", e), ("A4", 2)]
    bass = [("A2", e), ("E3", e)] * 4 + [("G2", e), ("D3", e)] * 4 + [("F2", e), ("C3", e)] * 4 + [("E2", e), ("B2", e)] * 4
    beat = 60.0 / 138
    drums = []
    for _ in range(16):
        drums += cat(drum(beat * 0.5), drum(beat * 0.5, 0.4), drum(beat * 0.5, 0.7), drum(beat * 0.5, 0.4))
    write("Music.wav", mix(line(138, tune + tune, "pulse", 1, 0.5, 0.5), line(138, bass + bass, "tri", 1, 0.6, 0.3), drums), 0.24)
    write("TitleTune.wav", cat(mountain_king(), silence(1.5)), 0.3)
    write("Victory.wav", mix(line(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("G5", 0.5), ("C6", 2)], "pulse"), line(150, [("C3", 1), ("G2", 1), ("C3", 3)], "tri")), 0.4)
    write("GameOver.wav", mix(line(70, [("E5", 1), ("C5", 1), ("A4", 1), ("E4", 1), ("A4", 3)], "tri"), line(70, [("A2", 2), ("F2", 2), ("A1", 3)], "tri")), 0.42)


if __name__ == "__main__":
    main()
