#!/usr/bin/env python3
"""
Generates the sounds the interface is built out of: `press` on finger-down, `click` on
release, and `win` — the fanfare that plays when a glade is solved, a chest opens, an
ad pays out and a companion arrives.

Why these are synthesised here rather than bought as samples
------------------------------------------------------------
They are the most-heard few seconds in the product — a player hears the button pair
more often than they hear the music, and the fanfare on every single level they
finish — so they are worth being able to *tune* rather than re-shop for. Everything
that decides how they feel is a number in the SOUNDS table below: pitch, decay,
attack, filter sweep. Changing the feel is editing one line and re-running this.

They replace a pack pair that read as metallic, and the analysis says why. The old
`click.wav` reached full level in 5.6 ms and carried its energy at 258 Hz and 1158 Hz
— a ratio of 4.5, which is not a harmonic of anything. An instant attack plus an
inharmonic upper partial is the recipe for "small metal object"; it is how a triangle
and a bell differ from a flute. Both are fixed here on purpose:

  * every partial is a whole-number ratio of the root, so the stack fuses into one
    pitched note instead of a struck object, and
  * the attack is a raised cosine over several milliseconds, which is long enough to
    remove the transient the ear hears as a tick and short enough to still feel
    instant under a finger.

The gesture is two notes, not one sound twice. Down is C4/G4 and release is the same
chord an octave up, so a tap reads as one movement that lifts rather than as the same
blip fired twice. Press is also darker and shorter, which is what stops it competing
with the sound that actually confirms something happened.

Both sit deliberately high for how warm they are meant to feel. A phone speaker has
almost nothing under ~500 Hz, so a genuinely low pad would be inaudible on the device
most players are holding and would only eat headroom off the partials that do come
through. The warmth here comes from harmonic ratios, a soft attack and a falling
filter — not from pitching it into a range the hardware cannot reproduce.

The fanfare, and what was wrong with the old one
------------------------------------------------
`win.wav` replaces a clip that everybody who heard it called a slot machine, and the
measurements agree: 3.42 seconds long, spectral centroid 4773 Hz, and — the telling
number — 27 spectral-flux peaks per second while staying above half level for 70% of
its length. That is not a chord that swells and resolves. It is a rattle that keeps
re-triggering, which is exactly what a coin cascade is, and it is a strange thing to
pay a player with when they finish a puzzle.

The replacement is a shape rather than a texture: four notes of a C major arpeggio
rising 85 ms apart, arriving on a held triad that blooms and decays, over a slow pad
underneath that gives it weight. It ends in about two seconds instead of three and a
half, which matters because it is heard on *every* completed glade — the old one was
still going while the victory panel was assembling itself.

The triad is tuned justly (5/4 and 3/2, not equal temperament). With pure synthesised
partials an equal-tempered major third is 14 cents sharp of 5/4 and beats audibly
against the root; the just one locks. That is most of why this sounds warm and the
old one sounded busy, and it costs nothing.

Usage:  python Tools/audio/ui_sounds.py [--check]

Writes into Assets/Game/Audio/Sfx/ in place — 44.1 kHz, 16-bit stereo, matching what
was there. Writing in place is deliberate: the .meta files stay put, so the asset
GUIDs, the Addressables entries and every call site survive untouched. `--check`
re-analyses what is on disk instead of writing.
"""

import argparse
import math
import os
import sys
import wave

import numpy as np

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "..",
                   "Assets", "Game", "Audio", "Sfx")

# Peak each file is normalised to. Not a mix decision — the balance between the two
# lives in Btn (press at .35, click at .7) so it can be tuned without a re-render.
# This only keeps a little headroom so the encoder never clips.
PEAK = 0.86


class Voice:
    """
    One note: a frequency, a level, and how it fades.

    `harmonics` stacks whole-number multiples of the root, each at 1/k^`rolloff`. At
    the default 2 that is a triangle wave's spectrum — about the softest thing still
    recognisably an instrument rather than a sine. Raising it thins the stack toward a
    bare sine, which is what the button sounds want and the fanfare does not.

    `glide_to` bends the pitch: the note starts at `hz` and settles onto `glide_to`,
    most of the way there within `glide` seconds. This is the whole difference between
    a blip and a bleep, and it is what the button sounds are built on — see the SOUNDS
    table.

    `at` is when the note starts, which is what lets one table describe both a stack
    (everything at zero) and an arpeggio. `attack` overrides the sound's default, for
    the pad that has to swell rather than land.
    """

    def __init__(self, hz, amp, tau, detune_cents=0.0, at=0.0, harmonics=1, attack=None,
                 glide_to=None, glide=0.030, rolloff=2.0, hold=None):
        self.hz = hz
        self.amp = amp
        self.tau = tau
        self.detune = detune_cents
        self.at = at
        self.harmonics = harmonics
        self.attack = attack
        self.glide_to = glide_to
        self.glide = glide
        self.rolloff = rolloff
        self.hold = hold


class Swish:
    """
    A breath of filtered noise: the honest way to build a whoosh.

    Noise has no partials at all, so it cannot be inharmonic and cannot read as metal
    however it is shaped — which is the whole reason the transition sound is made of
    it rather than of tones. The movement comes from the band sweeping, not from a
    pitch, so it stays a sound of *air* rather than becoming a note that would compete
    with the fanfare and the button blips.

    `band` is (from, to) for the centre of the pass band; `width` is how many octaves
    wide it is. `swell` is the fraction of the sound spent rising, the rest falling.
    """

    def __init__(self, amp, band, width=1.6, swell=0.28, at=0.0, seed=0):
        self.amp = amp
        self.band = band
        self.width = width
        self.swell = swell
        self.at = at
        self.seed = seed


# ------------------------------------------------------------------- the sounds
# Ratios are exact (3/2, 2/1, 1/2) rather than equal-tempered, because the whole
# point is that the partials fuse into a single note. An equal-tempered fifth is
# two cents flat of 3/2, which over a decay this long is audible as a slow waver.
SOUNDS = {
    # CURRENTLY UNUSED — kept so it can be brought back with one line.
    #
    # A button used to make two sounds, this one as the finger landed and `click` as it
    # lifted. The idea was tactile depth; what it actually produced was a stutter, and
    # the second sound always arrived a reaction-time after the squash it was meant to
    # belong to. Btn now plays one sound on the way down and nothing on release. If a
    # two-stage press is ever wanted again, this is what it sounded like.
    "press": dict(
        seconds=0.110,
        attack=0.010,
        shape="blob",
        cutoff=(1800.0, 900.0),
        voices=[
            # An eighth of an octave on top, and only here: it bends down to 294 Hz,
            # which a phone speaker barely reproduces, and the octave is what carries
            # the note on a handset. An octave cannot read as metal — it is the most
            # consonant interval there is.
            Voice(392.00, 1.00, 0.0, harmonics=2, rolloff=3.0,
                  glide_to=293.99, glide=0.026),                       # G4 → D4
        ],
    ),
    # Release. A "boop" that rises a fifth — the one that says the button worked.
    "click": dict(
        seconds=0.170,
        attack=0.012,
        shape="blob",
        cutoff=(2600.0, 1300.0),
        voices=[
            # One sine and nothing else. A single partial has no partials to be
            # inharmonic with, so this cannot read as metal by construction — and it
            # bends up to 785 Hz, which every phone speaker reproduces happily, so it
            # needs no help from an octave the way the press does.
            Voice(523.25, 1.00, 0.0, glide_to=784.88, glide=0.030),    # C5 → G5
        ],
    ),
    # THE GAME HAS NO REFUSAL SOUND, ON PURPOSE. Do not add one back here.
    #
    # `nope` was the universal "no" — twelve call sites: a conduit that will not turn, a
    # padlocked glade, no hearts left, no hints left, a defeat, a link that hit an account
    # somebody already owns, both twice-to-confirm buttons. It was measurably an alarm:
    # 1249 ms long, above half level for 90% of that (it did not decay, it *sustained*),
    # 72 re-onsets, a centroid of 4282 Hz, and a full harmonic stack — 66, 131, 196, 262,
    # 327, 392 Hz, a perfect 1:2:3:4:5:6, which is what a buzzer is made of.
    #
    # Worth separating from the metallic problem the button sounds had: being harmonic is
    # why this one was never metallic, and being long, bright and sustained is why it was
    # alarming. Two different faults with opposite fixes.
    #
    # A gentle replacement was built and measured (two dots falling a fifth, G4 → C4, in
    # under 300 ms) and then cut anyway, because the owner's answer to "what should the
    # refusal sound like" was that there should not be one. Every refusal in the game is
    # already carried visually — a shake, a toast, a relabelled button — so the sound was
    # adding volume rather than information. If it is ever wanted back, the shape above is
    # the one to rebuild: the exact inverse of `click`, which rises a fifth C5 → G5.
    #
    # One coin landing in the bank. Never heard alone — the chest pays out five or six of
    # these in under a second, each a step higher than the last, and the tune they make
    # together is the reward. So the design brief is the opposite of the fanfare's: it has
    # to be short enough to leave a gap before the next one (a tail would smear the run
    # into a chord), plain enough that transposing it a fifth does not change what it is,
    # and quiet enough to be heard six times in a row without becoming an alarm.
    #
    # Bare sine plus an eighth of an octave, 80 ms, no tail. The melody is the pitch the
    # caller passes in; this only has to be a clean dot.
    "coin": dict(
        seconds=0.080,
        attack=0.006,
        shape="blob",
        cutoff=(3000.0, 1500.0),
        voices=[
            Voice(523.25, 1.00, 0.0, harmonics=2, rolloff=3.0),        # C5
        ],
    ),
    # Recognition: the percentile medal on the victory panel, the streak toast, the
    # companion landing. Two notes rising a fourth, G5 → C6, and then it stops.
    #
    # What it replaces was the brightest thing in the game and read as breaking glass:
    # 2085 ms, a spectral centroid of 9265 Hz, 56 re-onsets, and — the giveaway — its
    # partials were a *cluster* rather than a series, 1639 / 1684 / 1727 / 1768 / 1808 Hz,
    # spaced about 42 Hz apart at ratios of 1.03, 1.05, 1.08. Densely-packed inharmonic
    # partials up in the top two octaves is exactly what a shard of glass sounds like;
    # nothing else in nature makes that shape.
    "bell": dict(
        seconds=0.340,
        attack=0.010,
        shape="blob",
        cutoff=(3200.0, 1600.0),
        voices=[
            Voice(783.99, 0.75, 0.0, harmonics=2, rolloff=3.0, hold=0.130),             # G5
            Voice(1046.50, 1.00, 0.0, at=0.075, harmonics=2, rolloff=3.0, hold=0.245),  # C6
        ],
    ),
    # Something opened: a glade unlocked, a streak rung taken, an event bloom collected,
    # a companion arriving. Three notes climbing a C major triad in under half a second.
    #
    # Also inharmonic before this — 1 : 1.28 : 1.69 : 2.38 : 4.0, with 26 re-onsets over
    # 720 ms — and it is the *last* thing the victory panel says, which is why it and the
    # medal above it were the pair worth fixing together. A triad climbing to the octave
    # says "opened" without needing to be bright to do it.
    #
    # Deliberately a compact echo of `win` rather than a different idea: same key, same
    # shape, a fifth of the length and no pad. The fanfare is the event; this is the
    # footnote that says the event had a consequence.
    "unlock": dict(
        seconds=0.420,
        attack=0.009,
        shape="blob",
        cutoff=(3400.0, 1700.0),
        voices=[
            Voice(523.25, 0.80, 0.0, harmonics=2, rolloff=3.0, hold=0.115),             # C5
            Voice(784.88, 0.85, 0.0, at=0.075, harmonics=2, rolloff=3.0, hold=0.120),   # G5 (3/2)
            Voice(1046.50, 1.00, 0.0, at=0.150, harmonics=2, rolloff=3.0, hold=0.265),  # C6 (2/1)
        ],
    ),
    # The fanfare. A rise that arrives somewhere, over a pad that gives it weight.
    #
    # The pad is what makes this read as an event rather than as a longer blip, and it
    # is the one thing here allowed to sit below the phone-speaker floor: it is felt on
    # headphones and simply absent on a handset, where the arpeggio carries the whole
    # sound on its own. That is a deliberate trade the button sounds could not make,
    # because they have nothing else to fall back on.
    "win": dict(
        seconds=2.05,
        attack=0.010,
        tail=0.180,
        cutoff=(9000.0, 2600.0),
        voices=[
            # the pad: a slow swell underneath everything
            Voice(130.81, 0.30, 0.95, at=0.000, harmonics=3, attack=0.22),   # C3
            Voice(196.22, 0.22, 0.95, at=0.000, harmonics=3, attack=0.22),   # G3  (3/2)
            Voice(261.63, 0.20, 0.90, at=0.020, harmonics=2, attack=0.24),   # C4

            # the rise: C major, justly tuned, 85 ms between notes
            Voice(523.25, 0.42, 0.42, -3.0, at=0.000, harmonics=3),          # C5 } detuned
            Voice(523.25, 0.42, 0.42, +3.0, at=0.000, harmonics=3),          # C5 } pair
            Voice(654.06, 0.40, 0.46, at=0.085, harmonics=3),                # E5  (5/4)
            Voice(784.88, 0.40, 0.52, at=0.170, harmonics=3),                # G5  (3/2)

            # the arrival, and the triad blooming behind it
            Voice(1046.50, 0.52, 0.80, at=0.255, harmonics=2),               # C6  (2/1)
            Voice(1308.13, 0.16, 0.55, at=0.255),                            # E6  (5/2)
            Voice(1569.76, 0.14, 0.50, at=0.255),                            # G6  (3/1)
            Voice(2093.00, 0.08, 0.35, at=0.255),                            # C7  air
        ],
    ),
    # The transition. Heard on every screen change in the game, which is why the old
    # one mattered far more than its 0.44 seconds suggested: it rang at 621 / 1076 /
    # 2150 Hz — ratios of 1 : 1.73 : 3.46, and 1.73 is √3, about as textbook an
    # inharmonic ring as exists. Tonal (flatness 0.219) and centred at 5435 Hz, so it
    # was a small bright metal object announcing every navigation, including the two
    # feature boxes on the hub, which play no sound of their own and were only ever
    # heard through this.
    #
    # A whoosh should be air, so this one is air: noise, no partials, nothing to be
    # inharmonic with. The band slides down as the iris closes and the envelope has no
    # onset at all — it arrives without being struck, which is the difference between
    # a transition and a hit.
    "whoosh": dict(
        seconds=0.340,
        attack=0.0,
        tail=0.040,
        cutoff=(7000.0, 7000.0),      # the band does the shaping; this stays out of it
        voices=[
            Swish(1.00, band=(2000.0, 550.0), width=1.5, swell=0.30),
        ],
    ),
}


def render(spec, spread=1.0):
    """
    One channel of a sound. `spread` nudges every detune, which is the only thing
    that differs between left and right.

    Stereo width is done with detune rather than with delay or polarity because a
    phone speaker sums to mono: a Haas delay would comb-filter and an inverted side
    would cancel outright. Two slightly different tunings just stay two slightly
    different tunings. A Swish gets a different noise seed per channel instead, which
    is the same idea: uncorrelated, so it widens, and it sums without cancelling.
    """
    n = int(SR * spec["seconds"])
    out = np.zeros(n)

    shape = spec.get("shape", "bell")

    for v in spec["voices"]:
        if isinstance(v, Swish):
            start = int(SR * v.at)
            if start < n:
                out[start:] += swish(n - start, v, spread)
            continue

        start = int(SR * v.at)
        if start >= n:
            continue
        t = np.arange(n - start) / SR

        if shape == "blob":
            # Rounded, and reaching exactly zero at the end of the note. An
            # exponential decay never actually arrives, so it always leaves a ring —
            # and a ringing tone is a bell whatever its partials are. This one stops.
            #
            # `hold` is how long *this note* lasts, as opposed to how much file is left
            # after it starts. Without it a two-note phrase can only ever be written one
            # way round — the first note runs to the end of the file and rings under the
            # second, so a short-then-long figure is unwriteable.
            decay = np.clip(1.0 - t / (v.hold if v.hold else t[-1]), 0.0, 1.0) ** 1.8
        else:
            decay = np.exp(-t / v.tau)

        # Pitch, in Hz per sample. A bend settles onto its target rather than sliding
        # linearly into it, which is how a voice or a spring moves and why it reads as
        # playful instead of as a slide whistle.
        detune = 2.0 ** (v.detune * spread / 1200.0)
        if v.glide_to is None:
            freq = np.full(len(t), v.hz * detune)
        else:
            settle = np.exp(-t / max(v.glide, 1e-6))
            freq = (v.glide_to + (v.hz - v.glide_to) * settle) * detune

        # Integrated, never sin(2*pi*f(t)*t) — that expression is only correct for a
        # constant f, and with a bend it warps the phase and buzzes.
        phase = 2.0 * math.pi * np.cumsum(freq) / SR

        note = np.zeros(len(t))
        for k in range(1, v.harmonics + 1):
            note += np.sin(phase * k) / (k ** v.rolloff)

        # Attack: a raised cosine, not a ramp. A linear ramp still has a corner at
        # both ends, and a corner is a click — the exact artefact being removed.
        # Applied per note rather than to the mix, because in an arpeggio each note
        # has its own onset and only the first one starts at the top of the file.
        a = min(len(t), max(1, int(SR * (v.attack if v.attack is not None
                                         else spec["attack"]))))
        note[:a] *= 0.5 - 0.5 * np.cos(np.linspace(0.0, math.pi, a))

        out[start:] += v.amp * note * decay

    out = lowpass_sweep(out, *spec["cutoff"])

    # And a fade at the tail, for the same reason as the attack: an exponential decay
    # is small at the end but never zero, and cutting a non-zero sample is a click.
    f = max(1, int(SR * spec.get("tail", 0.020)))
    out[-f:] *= np.linspace(1.0, 0.0, f) ** 2
    return out


def swish(n, v, spread):
    """
    Band-passed noise with the band sliding, under a swell-and-fall envelope.

    The noise is drawn from a *seeded* generator, and it has to be: a build that
    produced a different file every time it ran would make this tool a source of
    churn rather than a source of truth, and there would be no way to tell a retune
    from a re-roll in a diff.
    """
    rng = np.random.default_rng(9001 + v.seed + (0 if spread > 0 else 1))
    x = rng.standard_normal(n)

    centre = np.geomspace(v.band[0], v.band[1], n)
    half = 2.0 ** (v.width * 0.5)

    # Three poles on the way down, one on the way up. That asymmetry is the whole
    # character: a single-pole roll-off leaves 6 dB an octave of noise above the band,
    # and noise has so much energy up there that the result reads as hiss however low
    # the band is set — the first attempt measured *brighter* than the metallic clip
    # it replaced. Cascading three makes the top of the band an actual edge. Below it
    # one pole is plenty, since there is only rumble to remove.
    for _ in range(3):
        x = lowpass_sweep(x, *(centre[[0, -1]] * half))
    x = x - lowpass_sweep(x, *(centre[[0, -1]] / half))

    # Rise then fall, both raised cosines, meeting at the peak. No corners anywhere,
    # so there is nothing for the ear to hear as an onset — a whoosh should arrive
    # without ever being struck.
    s = max(1, int(n * v.swell))
    env = np.empty(n)
    env[:s] = 0.5 - 0.5 * np.cos(np.linspace(0.0, math.pi, s))
    env[s:] = (0.5 + 0.5 * np.cos(np.linspace(0.0, math.pi, n - s))) ** 1.6

    return v.amp * x * env


def lowpass_sweep(x, start_hz, end_hz):
    """
    One-pole lowpass whose cutoff falls across the sound — a synth filter envelope.

    Sine partials have nothing above themselves to remove, so this is not about
    taming harshness; it is about the top of the stack dying before the bottom does,
    which is what every real resonant body does and what makes a decay sound like it
    is settling rather than merely getting quieter.
    """
    n = len(x)
    cutoff = np.geomspace(start_hz, end_hz, n)
    alpha = 1.0 - np.exp(-2.0 * math.pi * cutoff / SR)
    y = np.empty(n)
    acc = 0.0
    for i in range(n):
        acc += alpha[i] * (x[i] - acc)
        y[i] = acc
    return y


def write(name, spec):
    left = render(spec, spread=1.0)
    right = render(spec, spread=-1.0)

    stereo = np.stack([left, right], axis=1)
    stereo *= PEAK / np.max(np.abs(stereo))

    pcm = np.clip(np.rint(stereo * 32767.0), -32768, 32767).astype("<i2")

    path = os.path.abspath(os.path.join(OUT, name + ".wav"))
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    return path, pcm


def describe(name):
    """Re-reads a file and reports the two numbers that made the old pair metallic."""
    path = os.path.abspath(os.path.join(OUT, name + ".wav"))
    with wave.open(path, "rb") as w:
        d = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2")
        d = d.reshape(-1, 2).mean(axis=1) / 32768.0
        frames, sr = w.getnframes(), w.getframerate()

    peak = int(np.argmax(np.abs(d)))
    seg = d[peak:peak + int(sr * 0.12)]
    sp = np.abs(np.fft.rfft(seg * np.hanning(len(seg))))
    f = np.fft.rfftfreq(len(seg), 1.0 / sr)
    centroid = float((sp * f).sum() / sp.sum())
    partials = sorted(round(float(f[i])) for i in np.argsort(sp)[::-1][:6])

    print(f"  {name+'.wav':<12} {frames/sr*1000:6.0f} ms   "
          f"attack {peak/sr*1000:5.1f} ms   centroid {centroid:5.0f} Hz   {partials}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="analyse what is on disk instead of writing")
    args = ap.parse_args()

    if args.check:
        print("on disk:")
        for name in SOUNDS:
            describe(name)
        return 0

    print("writing:")
    for name, spec in SOUNDS.items():
        path, pcm = write(name, spec)
        print(f"  {path}  ({len(pcm)} frames)")
    print("verifying:")
    for name in SOUNDS:
        describe(name)
    return 0


if __name__ == "__main__":
    sys.exit(main())
