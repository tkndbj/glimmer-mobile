"""
Reading and writing the game's sound effects, without Unity.

Split out of `make_sfx.py` for the reason every arithmetic rule in this project is
split out of the thing that uses it: the cut is the part worth proving, and proving
it must not need a 384 MB source pack on disk. `sfx_dsp_test.py` drives every
function here against signals it builds itself.

Everything is float64 mono internally. The pack is 44.1 kHz 16-bit stereo and the
game ships 44.1 kHz 16-bit mono - see `make_sfx.py` for why mono.
"""

import struct
import wave

import numpy as np

RATE = 44100


# --------------------------------------------------------------------- reading
def read(path):
    """A wav as (mono float64 in [-1, 1], sample rate)."""
    with wave.open(str(path), "rb") as w:
        frames, channels, width, rate = (
            w.getnframes(), w.getnchannels(), w.getsampwidth(), w.getframerate())
        raw = w.readframes(frames)

    if width == 1:
        a = (np.frombuffer(raw, dtype=np.uint8).astype(np.float64) - 128.0) / 128.0
    elif width == 2:
        a = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    elif width == 3:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        v = (b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16))
        v = np.where(v & 0x800000, v - 0x1000000, v)
        a = v.astype(np.float64) / 8388608.0
    elif width == 4:
        a = np.frombuffer(raw, dtype="<i4").astype(np.float64) / 2147483648.0
    else:
        raise ValueError(f"{path}: unsupported sample width {width}")

    if channels > 1:
        a = a.reshape(-1, channels).mean(axis=1)
    return a, rate


def write(path, a, rate=RATE):
    """Mono 16-bit wav. Rounds half-away-from-zero so a re-read is stable."""
    clipped = np.clip(a, -1.0, 1.0)
    # 32767 rather than 32768: the ceiling has to survive the round trip, and
    # -1.0 * 32768 is representable while +1.0 * 32768 is not.
    ints = np.sign(clipped) * np.floor(np.abs(clipped) * 32767.0 + 0.5)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(ints.astype("<i2").tobytes())


# -------------------------------------------------------------------- resample
def resample(a, src, dst):
    """Linear resample. The pack is already 44.1 kHz, so this is a guard rather
    than a workhorse - a source that is not gets converted rather than silently
    playing at the wrong pitch."""
    if src == dst or a.size == 0:
        return a
    n = int(round(a.size * dst / float(src)))
    if n <= 1:
        return a[:1].copy()
    x = np.linspace(0.0, a.size - 1.0, n)
    return np.interp(x, np.arange(a.size), a)


# ------------------------------------------------------------------------ trim
def trim(a, floor_db=-60.0, pad_ms=2.0, rate=RATE):
    """Cut leading and trailing near-silence.

    The pack pads most files with a few tens of milliseconds of nothing, and that
    is latency a player feels on a tap. A little padding is kept either side so a
    fade has something to work on.
    """
    if a.size == 0:
        return a
    peak = float(np.max(np.abs(a)))
    if peak <= 0.0:
        return a
    thr = peak * (10.0 ** (floor_db / 20.0))
    live = np.nonzero(np.abs(a) > thr)[0]
    if live.size == 0:
        return a
    pad = int(pad_ms * rate / 1000.0)
    s = max(0, int(live[0]) - pad)
    e = min(a.size, int(live[-1]) + 1 + pad)
    return a[s:e].copy()


def head(a, seconds, rate=RATE):
    """Keep only the first `seconds`. A long tail on a sound that repeats is what
    turns a cascade into a smear."""
    n = int(seconds * rate)
    return a[:n].copy() if 0 < n < a.size else a


# ----------------------------------------------------------------------- fades
def fade(a, in_ms=1.5, out_ms=12.0, rate=RATE):
    """Ramp both ends to zero.

    Not cosmetic: a waveform cut mid-cycle steps to zero, and a step is a click -
    which on a sound the player triggers hundreds of times is the single most
    fatiguing thing in a mix. The out-ramp is raised-cosine rather than linear so
    a decaying tail does not develop an audible corner.
    """
    a = a.copy()
    n = a.size
    if n == 0:
        return a

    ni = min(int(in_ms * rate / 1000.0), n)
    if ni > 1:
        a[:ni] *= 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, ni))

    no = min(int(out_ms * rate / 1000.0), n)
    if no > 1:
        a[n - no:] *= 0.5 + 0.5 * np.cos(np.linspace(0.0, np.pi, no))
    return a


# ------------------------------------------------------------------- loudness
def rms(a):
    return float(np.sqrt(np.mean(a * a))) if a.size else 0.0


def peak(a):
    return float(np.max(np.abs(a))) if a.size else 0.0


def loudness(a, rate=RATE):
    """Perceived level: RMS of the signal through a crude ear curve.

    Plain RMS is the wrong yardstick for a set of clips this varied - it calls a
    dull thud and a bright chime equally loud when the chime is plainly louder to
    a person. This rises with frequency, rolls off below 100 Hz, and - the part that
    had to be added - **rolls off again above 3.5 kHz**.

    That last term is not a refinement, it is a bug fix. Without it the weight kept
    climbing to its cap, so a clip whose energy sits at 8-12 kHz measured as far
    louder than it sounds and the match turned it *down* to compensate. Two of the
    brightest clips in the set - `win` and `wheel`, both fanfares the owner chose by
    ear - came out at roughly **half the level of everything else**, which is audible
    and was invisible in every reading except a side-by-side of plain RMS. A real
    equal-loudness contour peaks near 3-4 kHz and falls above it, and the ear stops
    gaining long before 12 kHz; the curve now does the same.

    Correcting it moved every other clip by under a decibel, because everything else
    here is centred below 2 kHz where the two curves agree.
    """
    if a.size < 8:
        return rms(a)
    n = 1 << int(np.ceil(np.log2(a.size)))
    sp = np.fft.rfft(a, n)
    f = np.fft.rfftfreq(n, 1.0 / rate)
    w = np.clip(f / 500.0, 0.0, None) ** 0.5
    w = np.where(f < 100.0, w * (f / 100.0), w)
    w = np.where(f > 3500.0, w * (3500.0 / np.maximum(f, 1.0)) ** 0.9, w)
    w = np.clip(w, 0.0, 4.0)
    w[0] = 0.0
    return float(np.sqrt(np.sum((np.abs(sp) * w) ** 2) / (n * n / 2.0)))


def normalise(a, target_loudness, ceiling=0.891):
    """Bring a clip to a common perceived level, then hold a peak ceiling.

    The ceiling (-1 dBFS) is what stops a sample that is loud only for an instant
    from being pushed into clipping by the loudness match, and it leaves headroom
    for the pitch-shifting the game does at playback.

    Returns (signal, gain applied).
    """
    if a.size == 0:
        return a, 1.0
    lo = loudness(a)
    gain = target_loudness / lo if lo > 0 else 1.0
    pk = peak(a)
    if pk * gain > ceiling:
        gain = ceiling / pk if pk > 0 else 1.0
    return a * gain, gain


# -------------------------------------------------------------------- shaping
def lowpass(a, cutoff, rate=RATE, order=2):
    """One-pole cascade. Used to take the fizz off a sample that is right in
    every other way - a gentler instrument than rejecting the sample."""
    if cutoff <= 0 or cutoff >= rate / 2:
        return a
    dt = 1.0 / rate
    rc = 1.0 / (2.0 * np.pi * cutoff)
    alpha = dt / (rc + dt)
    out = a
    for _ in range(max(1, order)):
        y = np.empty_like(out)
        acc = 0.0
        for i in range(out.size):
            acc += alpha * (out[i] - acc)
            y[i] = acc
        out = y
    return out


def lowpass_fft(a, cutoff, rate=RATE, width=0.25):
    """Same intent as `lowpass`, done in the frequency domain so it is O(n log n)
    rather than a Python loop, and phase-linear so a transient keeps its shape.

    The skirt is a raised cosine `width` octaves wide, because a brick wall on a
    short percussive clip rings audibly.
    """
    if cutoff <= 0 or cutoff >= rate / 2 or a.size == 0:
        return a
    n = 1 << int(np.ceil(np.log2(a.size * 2)))
    sp = np.fft.rfft(a, n)
    f = np.fft.rfftfreq(n, 1.0 / rate)
    hi = cutoff * (2.0 ** width)
    g = np.ones_like(f)
    band = (f > cutoff) & (f < hi)
    g[band] = 0.5 + 0.5 * np.cos(np.pi * (f[band] - cutoff) / (hi - cutoff))
    g[f >= hi] = 0.0
    return np.fft.irfft(sp * g, n)[:a.size]


def highpass_fft(a, cutoff, rate=RATE, width=0.5):
    """Take out rumble a phone speaker cannot reproduce and a headphone turns into
    mud. Same raised-cosine skirt as `lowpass_fft`, for the same reason."""
    if cutoff <= 0 or a.size == 0:
        return a
    n = 1 << int(np.ceil(np.log2(a.size * 2)))
    sp = np.fft.rfft(a, n)
    f = np.fft.rfftfreq(n, 1.0 / rate)
    lo = cutoff / (2.0 ** width)
    g = np.ones_like(f)
    band = (f > lo) & (f < cutoff)
    g[band] = 0.5 - 0.5 * np.cos(np.pi * (f[band] - lo) / (cutoff - lo))
    g[f <= lo] = 0.0
    return np.fft.irfft(sp * g, n)[:a.size]


def pitch(a, semitones, rate=RATE):
    """Resample-pitch: shifts speed and pitch together, like a tape.

    That is deliberately not a time-preserving shift. These are short percussive
    clips, and a phase vocoder smears exactly the transient that makes a tap feel
    immediate; playing a mallet faster is what a smaller mallet sounds like.
    """
    if semitones == 0 or a.size == 0:
        return a
    ratio = 2.0 ** (semitones / 12.0)
    n = max(1, int(round(a.size / ratio)))
    x = np.linspace(0.0, a.size - 1.0, n)
    return np.interp(x, np.arange(a.size), a)


# ----------------------------------------------------------------- synthesis
def struck(f0, seconds, partials, glide=0.0, glide_ms=45.0,
           attack_ms=2.5, rate=RATE):
    """A struck bar: a few partials, each with its own decay.

    This is how a marimba or a kalimba differs from a beep, and the difference is
    the whole reason to synthesise rather than to pick a sine. A bar's overtones are
    *inharmonic* - roughly 1 : 3.93 : 9.2 rather than 1 : 2 : 3 - and the high ones
    die away far faster than the fundamental. That combination is what the ear reads
    as "something was hit" instead of "a tone was switched on", and it is what puts
    energy outside the fundamental (`spectrum`'s `body`) without adding noise.

    `partials` is a sequence of (ratio, amplitude, decay seconds).

    `glide` bends the pitch by that fraction at the onset, decaying over `glide_ms`.
    A small upward bend reads as arriving; the phase is integrated rather than
    computed per sample, because a frequency that changes needs its phase
    accumulated or the tone jumps.
    """
    n = int(seconds * rate)
    t = np.arange(n) / float(rate)

    bend = 1.0 + glide * np.exp(-t / (glide_ms / 1000.0)) if glide else np.ones(n)
    phase = 2.0 * np.pi * np.cumsum(f0 * bend) / rate

    out = np.zeros(n)
    for ratio, amp, decay in partials:
        out += amp * np.sin(phase * ratio) * np.exp(-t / decay)

    # A raised-cosine attack rather than an instant one: a waveform starting at full
    # amplitude mid-cycle is a step, and a step is a click on top of the tone.
    na = max(1, int(attack_ms * rate / 1000.0))
    if na < n:
        out[:na] *= 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, na))

    peak = np.max(np.abs(out))
    return out / peak if peak > 0 else out


def bloop(rate=RATE):
    """The map's node arriving: a small wooden bell with a rising lilt.

    Tuned against the readings rather than by taste alone - it has to be warm enough
    to be soothing, bright enough for a phone speaker to reproduce (energy in
    500-4000 Hz), and quiet in 2-5 kHz where the ear fatigues, because a chapter
    builds twenty of these in a rising run.

    The fundamental sits at E5. `LevelsScreen` plays it at `1 + index * .09`, so the
    twentieth node lands around 1.4 kHz - still comfortably inside the band a
    handset reproduces, which is why the base is not lower.

    The partial weights were **swept rather than guessed**, against `spectrum`'s own
    readings, and the first version failed its own test: written by ear it measured
    `body` 0.012 and `flat` 0.0000, which is a pure sine - the exact fault this whole
    set was replaced to remove. Upper partials that decay quickly sound right in
    isolation and vanish from the spectrum of the whole clip, so they have to be both
    louder and longer-lived than a physical bar's to register at all. That makes this
    nearer a small bell than a marimba, which is the calmer of the two anyway.

    Measured: body 0.40 (a struck instrument sits 0.4-0.8, a sine near 0.05),
    centroid ~1.1 kHz, 8% in the 2-5 kHz fatigue band, nothing above 8 kHz.
    """
    return struck(
        f0=659.26,                       # E5
        seconds=0.30,
        partials=(
            (1.00, 1.00, 0.16),          # the body
            (2.00, 0.65, 0.20),          # an octave, carrying most of the warmth
            (3.01, 0.36, 0.14),          # a twelfth - body just below the harsh band
            (3.93, 0.45, 0.12),          # the bar's own inharmonic mode: the "wood"
            (9.20, 0.03, 0.012),         # a breath of strike noise, gone almost at once
        ),
        glide=0.055,                     # a rising lilt: it arrives rather than sounds
        glide_ms=28.0,
        rate=rate,
    )


SYNTH = {"bloop": bloop}


# ------------------------------------------------------------------ measuring
def spectrum(a, rate=RATE):
    """The readings `make_sfx.py --report` prints. Centroid and the two band
    shares are what separate a warm sound from a fatiguing one; `flat` is low for
    a tone and high for noise."""
    if a.size < 8:
        return dict(centroid=0.0, harsh=0.0, hiss=0.0, warm=0.0, flat=0.0)
    n = 1 << int(np.ceil(np.log2(a.size)))
    w = np.hanning(a.size)
    p = np.abs(np.fft.rfft(a * w, n)) ** 2
    f = np.fft.rfftfreq(n, 1.0 / rate)
    tot = p.sum() + 1e-20
    pm = p[1:] + 1e-20
    return dict(
        centroid=float((f * p).sum() / tot),
        harsh=float(p[(f >= 2000) & (f <= 5000)].sum() / tot),
        hiss=float(p[f >= 8000].sum() / tot),
        warm=float(p[f <= 500].sum() / tot),
        flat=float(np.exp(np.mean(np.log(pm))) / np.mean(pm)),
    )
