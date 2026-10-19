#!/usr/bin/env python3
"""Synthesises AVAIkariWarriors's sound effects and tunes into ../src/AVAIkariWarriors/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle, sine and noise
voices (and, for the enemy's death cry, a buzz through two vowel resonances). The tunes: the title plays
"Yankee Doodle" (traditional, 18th century, public domain) as a fife and drum march; game over is the bugle call
"Taps" (1862, public domain); the rest are new. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAIkariWarriors", "Sounds")
RNG = random.Random(1986)


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
    if kind == "thin":
        return 1.0 if p < 0.125 else -1.0
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


def gain(samples, k):
    return [s * k for s in samples]


def lowpass(samples, cutoff):
    """A one-pole low-pass: takes the hiss off noise, for rumbles and splashes."""
    a = 1 - math.exp(-2 * math.pi * cutoff / RATE)
    out, y = [], 0.0
    for s in samples:
        y += a * (s - y)
        out.append(y)
    return out


def resonate(samples, freq, width):
    """A two-pole resonance (a band-pass) at freq, of about the given width in Hz: a formant of a voice."""
    r = math.exp(-math.pi * width / RATE)
    c1, c2 = 2 * r * math.cos(2 * math.pi * freq / RATE), -r * r
    out, y1, y2 = [], 0.0, 0.0
    for s in samples:
        y = (1 - r) * s + c1 * y1 + c2 * y2
        out.append(y)
        y1, y2 = y, y1
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def seq(bpm, notes, kind="pulse", gain_=0.55, release=0.6, vibrato=0.0):
    """A line of (note, beats); '-' is a rest."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain_, vibrato=vibrato, vib_rate=5.5))
    return out


def snare(seconds, g=0.6):
    return mix(tone(seconds, 2400, 900, "noise", attack=0.001, release=2.2, gain=g), tone(min(seconds, 0.05), 330, 180, "tri", attack=0.001, release=1.5, gain=g * 0.5))


def drums(bpm, bars, pattern="march"):
    """A field drum: rolls and flams on a snare, a bass drum on the strong beats."""
    beat = 60.0 / bpm
    out = []
    for bar in range(bars):
        for b in range(4):
            if pattern == "march":
                if b == 3 and bar % 2 == 1:
                    hit = cat(*[snare(beat / 8, 0.35 + 0.05 * k) for k in range(8)])
                elif b in (1, 3):
                    hit = cat(snare(beat / 2), snare(beat / 4, 0.4), snare(beat / 4, 0.5))
                else:
                    hit = cat(snare(beat / 2, 0.7), silence(beat / 2))
            else:
                # The fighting cadence: busier, a flam on every off beat.
                hit = cat(snare(beat / 4, 0.55), snare(beat / 4, 0.3), snare(beat / 4, 0.5), snare(beat / 4, 0.3)) if b % 2 else cat(snare(beat / 2, 0.7), snare(beat / 4, 0.35), snare(beat / 4, 0.35))
            kick = cat(tone(beat / 2, 110, 42, "sine", attack=0.002, release=1.4, gain=1.0), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out


def death_cry(f0, f1, seconds, vowels=((750, 1150), (600, 1000))):
    """A man's cry: a buzzing voice falling in pitch, through the resonances of an 'ah' going to 'oh'."""
    buzz = tone(seconds, f0, f1, "saw", attack=0.02, release=0.6, vibrato=0.04, vib_rate=11)
    breath = tone(seconds, 3000, 1500, "noise", attack=0.01, release=1.0, gain=0.15)
    src = mix(buzz, breath)
    half = len(src) // 2
    a = mix(gain(resonate(src[:half], vowels[0][0], 120), 1.0), gain(resonate(src[:half], vowels[0][1], 150), 0.6))
    b = mix(gain(resonate(src[half:], vowels[1][0], 120), 1.0), gain(resonate(src[half:], vowels[1][1], 150), 0.6))
    return a + b


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))

    # The commandos' rifles: a short burst, two cracks; the enemy's a duller pop; an empty gun's click.
    crack = mix(tone(0.05, 3200, 900, "noise", attack=0.001, release=1.4), tone(0.045, 420, 150, "square", release=1, gain=0.5))
    write("Fire.wav", cat(crack, silence(0.012), gain(crack, 0.75)), 0.26)
    write("EnemyFire.wav", mix(tone(0.1, 1500, 450, "noise", attack=0.001, release=1.0), tone(0.08, 240, 110, "square", release=1, gain=0.4)), 0.22)
    write("Empty.wav", cat(tone(0.012, 5000, 3000, "noise", attack=0.0005, release=1), silence(0.03), tone(0.01, 4200, 2500, "noise", attack=0.0005, release=1)), 0.3)
    write("Clank.wav", mix(tone(0.12, 1900, 1700, "square", attack=0.001, release=2.5, gain=0.4), tone(0.12, 2650, 2500, "tri", attack=0.001, release=2.5), tone(0.03, 6000, 3000, "noise", attack=0.0005, release=1)), 0.2)

    # A grenade: the whoosh of the throw, and the blast; a tank's cannon, and a tank (or a gunship, or a pillbox) going up.
    write("Throw.wav", lowpass(tone(0.32, 500, 1600, "noise", attack=0.06, release=0.8), 2500), 0.2)
    write("Blast.wav", mix(lowpass(tone(0.8, 1200, 60, "noise", attack=0.002, release=0.35), 1800), tone(0.6, 110, 32, "sine", release=0.4, gain=1.0)), 0.55)
    write("Cannon.wav", mix(tone(0.07, 4000, 1000, "noise", attack=0.0008, release=1.2, gain=0.8), lowpass(tone(0.5, 900, 80, "noise", attack=0.002, release=0.5), 1000), tone(0.45, 80, 36, "sine", release=0.6, gain=1.2)), 0.6)
    write("TankBoom.wav", cat(mix(lowpass(tone(1.3, 900, 30, "noise", release=0.3), 1200), tone(1.1, 75, 26, "saw", release=0.35, gain=0.5), tone(1.0, 60, 25, "sine", release=0.4, gain=1.0)),
                              lowpass(tone(0.6, 500, 100, "noise", release=0.9, gain=0.35), 900)), 0.62)
    # The tank's engine: a diesel's chug, half a second, to repeat while it moves.
    chug = []
    for k in range(8):
        chug.extend(mix(tone(0.0625, 70 + (k % 2) * 6, 55, "saw", attack=0.003, release=0.7, gain=0.9), lowpass(tone(0.0625, 400, 200, "noise", attack=0.002, release=1, gain=0.6), 600)))
    write("TankEngine.wav", lowpass(chug, 900), 0.4)
    # A gunship's rotor: thumps of air, eleven a second, over the turbine's whine.
    rotor = []
    for k in range(11):
        rotor.extend(mix(lowpass(tone(1 / 11, 600, 120, "noise", attack=0.004, release=1.6), 700), tone(1 / 11, 85, 60, "sine", attack=0.003, release=1.5, gain=0.6)))
    write("Helicopter.wav", mix(rotor, tone(1.0, 2300, 2350, "sine", attack=0.1, release=0.2, gain=0.04)), 0.4)
    # A step in the river: a splash, and the water settling.
    write("Wade.wav", cat(lowpass(tone(0.09, 2500, 900, "noise", attack=0.008, release=1.2), 2200), lowpass(tone(0.12, 900, 400, "noise", attack=0.01, release=1.5, gain=0.4), 1200)), 0.28)

    # The enemy's death cry (two of them, picked between by pitch would need two files; one cry, varied by the voice).
    write("EnemyDown.wav", cat(death_cry(260, 150, 0.32), silence(0.02)), 0.3)
    write("Death.wav", cat(mix(lowpass(tone(0.5, 1200, 60, "noise", release=0.4), 1500), death_cry(200, 95, 0.55, ((700, 1100), (500, 850)))),
                           *[tone(0.15, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4", "E4"]]), 0.48)
    write("Pickup.wav", cat(*[tone(0.05, hz(n), kind="pulse", release=0.3) for n in ["E5", "G5", "C6", "E6"]], tone(0.12, hz("G6"), kind="pulse", release=0.5)), 0.3)
    write("Warning.wav", cat(tone(0.09, 1400, kind="square", release=0.2), silence(0.05), tone(0.09, 1050, kind="square", release=0.2)), 0.2)
    write("Join.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G4", "C5", "E5", "G5"]], tone(0.2, hz("C6"), kind="pulse", release=0.5)), 0.32)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "G6", "C6", "G6", "C7"]]), 0.36)

    # The bugles: a stage begins, a stage is taken.
    write("StageStart.wav", seq(150, [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 0.5), ("G5", 2)], kind="pulse", release=0.25, vibrato=0.006), 0.34)
    write("StageClear.wav", mix(seq(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("G5", 0.5), ("C6", 2)], kind="pulse", release=0.3),
                                seq(150, [("C3", 1), ("G2", 1), ("C3", 1), ("G2", 0.5), ("C3", 1.5)], kind="tri", gain_=0.6, release=0.3), drums(150, 1)[: int(RATE * 2.2)]), 0.42)
    # The gate: the creak of the great doors, their rumble, and a chord of victory.
    creak = mix(tone(1.6, 55, 42, "saw", attack=0.2, release=0.3, vibrato=0.08, vib_rate=13, gain=0.6), lowpass(tone(1.6, 300, 150, "noise", attack=0.3, release=0.5, gain=0.5), 400))
    chord = mix(*[tone(1.6, hz(n), kind="pulse", attack=0.02, release=1.2, gain=0.35) for n in ["C5", "E5", "G5", "C6"]], tone(1.6, hz("C3"), kind="tri", release=1.0, gain=0.6))
    write("GateOpen.wav", cat(creak, chord), 0.5)

    # The fighting while it's on: a busy cadence on the field drum, with a low ostinato (an original), exactly four
    # bars, so it loops cleanly.
    bass = [("C3", 0.5), ("C3", 0.5), ("G2", 0.5), ("C3", 0.5)] * 2 + [("Bb2", 0.5), ("Bb2", 0.5), ("F2", 0.5), ("Bb2", 0.5)] + [("G2", 0.5), ("G2", 0.5), ("D3", 0.5), ("G2", 0.5)]
    write("Music.wav", mix(drums(132, 4, "cadence"), gain(seq(132, bass * 2, kind="tri", gain_=0.7, release=0.4), 0.8)), 0.32)

    # The title: "Yankee Doodle" (traditional, public domain) on a fife, with the drum and a bass under it.
    verse = [("C5", .5), ("C5", .5), ("D5", .5), ("E5", .5), ("C5", .5), ("E5", .5), ("D5", .5), ("G4", .5),
             ("C5", .5), ("C5", .5), ("D5", .5), ("E5", .5), ("C5", 1), ("B4", 1),
             ("C5", .5), ("C5", .5), ("D5", .5), ("E5", .5), ("F5", .5), ("E5", .5), ("D5", .5), ("C5", .5),
             ("B4", .5), ("G4", .5), ("A4", .5), ("B4", .5), ("C5", 1), ("C5", 1)]
    chorus = [("A4", .5), ("B4", .5), ("A4", .5), ("G4", .5), ("A4", .5), ("B4", .5), ("C5", 1),
              ("G4", .5), ("A4", .5), ("G4", .5), ("F4", .5), ("E4", 1), ("G4", 1),
              ("A4", .5), ("B4", .5), ("A4", .5), ("G4", .5), ("A4", .5), ("B4", .5), ("C5", .5), ("A4", .5),
              ("G4", .5), ("C5", .5), ("B4", .5), ("D5", .5), ("C5", 1), ("C5", 1)]
    fife = [(n[:-1] + str(int(n[-1]) + 1), b) for n, b in verse + chorus]
    under = [("C3", 1), ("G2", 1), ("C3", 1), ("G2", 1), ("C3", 1), ("G2", 1), ("G2", 1), ("D3", 1),
             ("C3", 1), ("G2", 1), ("F2", 1), ("C3", 1), ("G2", 1), ("D3", 1), ("C3", 1), ("C3", 1),
             ("F2", 1), ("C3", 1), ("F2", 1), ("C3", 1), ("C3", 1), ("G2", 1), ("C3", 1), ("G2", 1),
             ("F2", 1), ("C3", 1), ("F2", 1), ("C3", 1), ("G2", 1), ("G2", 1), ("C3", 1), ("C3", 1)]
    bpm = 116
    tune = mix(seq(bpm, fife, kind="thin", gain_=0.5, release=0.25, vibrato=0.008), seq(bpm, under, kind="tri", gain_=0.55, release=0.4), gain(drums(bpm, 8, "march"), 0.8))
    write("TitleTune.wav", cat(tune, silence(1.2)), 0.42)
    write("StartTune.wav", mix(seq(160, [("G4", 0.5), ("G4", 0.5), ("C5", 0.5), ("E5", 1), ("C5", 0.5), ("E5", 0.5), ("G5", 2)], kind="pulse", release=0.3), drums(160, 2)[: int(RATE * 2.2)]), 0.45)
    # Game over: "Taps" (1862, public domain), on a bugle.
    taps = [("G4", .75), ("G4", .25), ("C5", 2.5), ("-", .5), ("G4", .75), ("C5", .25), ("E5", 2.5), ("-", .5),
            ("G4", .5), ("C5", .5), ("E5", 1), ("G4", .5), ("C5", .5), ("E5", 1), ("G4", .5), ("C5", .5), ("E5", 2), ("C5", 1), ("G4", 2)]
    write("GameOver.wav", seq(92, taps, kind="pulse", gain_=0.55, release=0.35, vibrato=0.01), 0.42)


if __name__ == "__main__":
    main()
