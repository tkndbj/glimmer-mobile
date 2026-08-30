# -*- coding: utf-8 -*-
"""Cuts the game's sound effects out of the licensed GameBurp pack.

    python Tools/make_sfx.py                      # write them
    python Tools/make_sfx.py --check              # prove the shipped wavs are what this writes
    python Tools/make_sfx.py --report             # what each one measures
    python Tools/make_sfx.py --contact sfx.html   # a page that plays them: scenes, ladders, runs

`Tools/sfx.tsv` is the table. One row per name `Audio.Sfx("...")` asks for, and the
slot names are a fixed vocabulary - `AssetManifest.Sfxs` preloads exactly these and
`Tools/verify/sfxnames.py` proves the code asks for no others.

**Why the set was replaced wholesale.** The sounds this replaces were synthesised sine
tones - measured spectral flatness of 0.0000 on nine of the twenty, which is one partial
and no body - plus a few noise bursts. Two of those noise bursts were the *most repeated
sounds in the game*: `pop` carried 98% of its energy in the 2-5 kHz band and `rotate_a`
68%, and 2-5 kHz is where the ear fatigues first. So the two things a player did most -
turning a conduit, watching a cascade pay out - were the two most tiring things in the
mix. That is not a taste, it is a measurement, and it is the whole reason for this tool.

**What replaced it is three materials and one scale.** Wood for the interface, stone for
movement, bells and a mallet for reward - the three things a grove is actually made of.
Every clip is transposed onto a pentatonic relationship (see `semis` in the table), which
matters here more than it would in most games because this one *overlaps sound
constantly*: `lit` climbs twelve notes over two octaves on one turn, `coin` runs dozens of
tokens in two seconds, and three modes fire cascade voices on a rising ladder. A
pentatonic set has no semitone in it, so no two of those can ever collide into a beat.

**Every clip is loudness-matched, and that is what makes the call sites honest.** The old
set ranged from 0.57 to 0.92 peak with RMS varying six-fold, so the volumes authored at
the 115 call sites (.28, .34, .42, .5, .62, .9) were partly compensating for samples that
were not the same size to begin with. `sfx_dsp.normalise` puts them all at one perceived
level under a -1 dBFS ceiling, so `volume: .5` now means the same thing wherever it is
written. `trim_db` in the table is the deliberate exception, and it is used once.

**The ceiling is not decoration.** The game pitches these at playback - up to 3x on `lit`
- and `AudioSource.pitch` resamples, which can overshoot a signal that already sits at
full scale. A dB of headroom is what stops the loudest, happiest moment in the game being
the one that clips.

**Mono, deliberately.** `Audio.cs` sets `spatialBlend = 0` on all twelve of its sources,
so the game never positions a sound and the second channel is decoded, held in memory and
mixed to no purpose. Mono halves the clip memory of the whole preloaded set.

**Short where repeated, and that is measured against the voice pool, not by ear.**
`Audio.PlayOne` is a **ten-voice round-robin that calls `Stop()` on reuse**, so the
eleventh overlapping one-shot cuts the first off mid-tail - an audible click, on the
busiest moment in the game. The old `lit` was 1.06 s against a ladder that fires twelve
notes at 70 ms apart: fifteen overlapping copies, so it was cutting itself off every time
a player solved anything. `head` in the table is what holds each clip under its own worst
case, and `--report` prints the worst case beside it.

**`--check` proves reproducibility and says nothing about quality.** That is
`make_shop_art.py`'s bargain and the reason `--contact` exists here too - except that a
contact sheet for sound cannot be looked at, so it is a page that *plays*. It gives every
clip a button, and beside it the two things a static audition would miss: **the ladder**
(the same clip fired at the pitches the game really uses, so `lit`'s two octaves can be
heard as a phrase rather than as one note) and **the run** (the clip at the rate the game
really repeats it, which is how `coin` at nine a second is judged). Those are the two
cases every wrong choice here has been wrong in.

**One clip is chosen against its measurements, and it is `reward`.** It carries 61% of its
energy in the 2-5 kHz band - by some distance the most fatiguing thing here, where nothing
else exceeds 17%. It is in anyway, because the owner picked it by ear and because *how often
a sound plays is part of whether harshness matters*: `reward` fires only on coming back from
a watched ad, which is opt-in, capped at a handful a day, and is the one moment in the game
that is allowed to be brash. The same sample on `click` or `rotate_a` would be indefensible.
Keep that distinction if this table grows: the fatigue readings are a budget spent against
repetition, not a pass mark every clip has to clear.

**The source is outside the repo** - see the audio-source-pack note. It is a 384 MB
licensed pack (GameBurp 2000 Game Sound FX Collection, royalty-free, EULA in the pack),
of which this uses a couple of dozen.
"""
from __future__ import annotations

import argparse
import base64
import html
import io
import json
import sys
import wave
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import numpy as np  # noqa: E402

import sfx_dsp as dsp  # noqa: E402

REPO = Path(__file__).resolve().parent.parent
TABLE = REPO / "Tools" / "sfx.tsv"
OUT = REPO / "Assets" / "Game" / "Audio" / "Sfx"

DEFAULT_SOURCE = Path(r"C:\Users\Digikey\Downloads\AUDIO\_extracted\gameburp-2000-sfx")
WAVS = "GameBurp - 2000 Game Sound FX Collection (WAV)"

# One perceived level for the whole set. Chosen by sweeping it: at 0.14 the median
# clip peaks at 0.63 and three of the twenty are held by the ceiling, which is the
# most level that can be had without the match itself becoming meaningless - a clip
# the ceiling caught is no longer at the matched loudness, so a set that is all
# ceiling is a set that was never matched. `--report` names the ones it caught.
TARGET_LOUDNESS = 0.14
CEILING = 0.891  # -1 dBFS

# What the game actually does with each clip, for `--report` and the audition page.
# (pitches it is played at, fastest repeat interval in seconds, what drives it)
USE = {
    "click":    ((0.96, 1.04), 0.12, "every button, on pointer-down"),
    "back":     ((0.94, 1.06), 0.15, "undo, and every modal close"),
    "menu":     ((1.00, 1.10), 0.00, "every panel opening - settings, pause, all 25 modals"),
    "tip":      ((1.00, 1.05), 0.00, "every tip box and info bubble"),
    "enter":    ((1.00, 1.00), 0.00, "committing to a level from the map"),
    "poke":     ((0.82, 1.18), 0.18, "poking the critter on the home screen"),
    "wheel":    ((1.00, 1.00), 0.00, "the bonus wheel landing on a slice"),
    "collect":  ((1.00, 1.06), 0.00, "a reward landing on a counter - the streak, First Bloom"),
    "reward":   ((1.00, 1.00), 0.00, "coming back from a watched ad"),
    "tick":     ((0.92, 1.62), 0.05, "wheel pegs, 9-tick rolls, the XP token stream"),
    "tock":     ((0.82, 1.38), 0.11, "chest thumps, the par bar, a grove's rings rising"),
    "pop":      ((0.55, 1.56), 0.11, "the credits token stream; a placed tile; a dropped stone"),
    "pop2":     ((0.60, 0.95), 0.03, "each lamp that goes dark, staggered by depth"),
    "rotate_a": ((0.92, 1.08), 0.20, "every conduit turn"),
    "rotate_b": ((0.80, 1.08), 0.20, "the other half of the turn, and the refusal nudge"),
    "whoosh":   ((0.74, 1.35), 0.30, "every mote falling; every ripple beat"),
    "lit":      ((0.92, 3.00), 0.07, "each lamp waking - twelve notes over two octaves"),
    "chime":    ((0.92, 1.90), 0.07, "cascade voice in three modes; every panel entrance"),
    "chime2":   ((1.00, 1.30), 0.05, "confirmation, and the capstone on a reward cascade"),
    "bell":     ((0.92, 1.70), 0.07, "a dewling waking, once per waking in a cascade"),
    "coin":     ((0.92, 1.88), 0.05, "every token landing in a balance - dozens in two seconds"),
    "star":     ((0.92, 1.66), 0.13, "the star row; the hint beckon; rarity stars"),
    "unlock":   ((1.00, 1.06), 0.30, "a plot claimed, a chapter opened, a streak night paid"),
    "chest":    ((1.00, 1.00), 0.00, "the lid gives"),
    "win":      ((1.00, 1.30), 0.00, "the fanfare"),
    "shatter":  ((1.00, 1.25), 0.00, "brittle stone breaking; a hint spent"),
    "blocked":  ((1.00, 1.00), 0.00, "a refused keeper name"),
}


# ---------------------------------------------------------------------- table
class Row:
    __slots__ = ("slot", "source", "semis", "head", "trim_db", "lowpass", "family", "note")

    def __init__(self, cells):
        self.slot = cells[0]
        self.source = cells[1]
        self.semis = float(cells[2]) if cells[2] else 0.0
        self.head = float(cells[3]) if cells[3] else 0.0
        self.trim_db = float(cells[4]) if cells[4] else 0.0
        self.lowpass = float(cells[5]) if cells[5] else 0.0
        self.family = cells[6] if len(cells) > 6 else ""
        self.note = cells[7] if len(cells) > 7 else ""


def load_table(path=TABLE):
    rows = []
    seen = set()
    for n, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        cells = line.split("\t")
        if len(cells) < 6:
            raise SystemExit(f"{path.name}:{n}: expected at least 6 tab-separated columns, got {len(cells)}")
        row = Row([c.strip() for c in cells])
        if row.slot in seen:
            raise SystemExit(f"{path.name}:{n}: duplicate slot {row.slot!r}")
        seen.add(row.slot)
        rows.append(row)
    return rows


# ----------------------------------------------------------------------- cut
def cut(row, source_root):
    """One row of the table, from the pack (or a generator) to the bytes that ship.

    A `synth:` source is built rather than read - see `sfx_dsp.SYNTH`. It goes through
    exactly the same trim, cap, fade and loudness match as a sampled clip, so a
    generated sound sits at the same level as the rest of the set and is held to the
    same readings. It also needs no pack on disk, which is why `--check` still passes
    on a machine that has never downloaded one.
    """
    if row.source.startswith("synth:"):
        name = row.source[len("synth:"):]
        make = dsp.SYNTH.get(name)
        if make is None:
            raise SystemExit(f"{row.slot}: no such generator {name!r} - "
                             f"sfx_dsp.SYNTH has {', '.join(sorted(dsp.SYNTH))}")
        a = make()
    else:
        src = source_root / WAVS / row.source
        if not src.exists():
            raise SystemExit(
                f"{row.slot}: source not found\n  {src}\n"
                f"Pass --source, or see the audio-source-pack note for where the pack lives.")
        a, rate = dsp.read(str(src))
        a = dsp.resample(a, rate, dsp.RATE)

    # Trim first: the pack pads most files, and that padding is latency on a tap.
    a = dsp.trim(a)

    if row.semis:
        a = dsp.pitch(a, row.semis)

    if row.lowpass:
        a = dsp.lowpass_fft(a, row.lowpass)

    # Below 40 Hz a phone reproduces nothing and a headphone reproduces mud.
    a = dsp.highpass_fft(a, 40.0)

    if row.head:
        a = dsp.head(a, row.head)

    # Fade after the cut, or the cut leaves a step - which is a click, on the
    # sounds that repeat most.
    a = dsp.fade(a)

    a, _ = dsp.normalise(a, TARGET_LOUDNESS, CEILING)
    if row.trim_db:
        a = a * (10.0 ** (row.trim_db / 20.0))

    # The trim can only ever lower, but a positive one is legal in the table, so
    # hold the ceiling regardless rather than trusting the author.
    pk = dsp.peak(a)
    if pk > CEILING:
        a = a * (CEILING / pk)
    return a


# -------------------------------------------------------------------- report
def report(rows, source_root):
    """What each clip measures, and the one reading only this game can produce.

    `voices` is the clip's own length at its slowest authored pitch divided by the
    fastest interval the game repeats it at - that is, how many copies of itself a
    sound has to stand alongside at its worst moment. Over ten and `Audio.PlayOne`
    reuses a voice that is still sounding and cuts it off, so the number is a
    property of this mix rather than of the clip, and no sound library can tell you
    it.
    """
    print(f"{'slot':10} {'family':7} {'dur':>6} {'peak':>5} {'loud':>7} {'atk':>6} "
          f"{'centr':>6} {'2-5k':>6} {'>8k':>6} {'<500':>6} {'voices':>7}")
    over, held = [], []
    for row in rows:
        a = cut(row, source_root)
        s = dsp.spectrum(a)
        dur = a.size / dsp.RATE
        (lo, hi), gap, _ = USE.get(row.slot, ((1.0, 1.0), 0.0, ""))
        longest = dur / min(lo, hi)
        voices = int(np.ceil(longest / gap)) if gap > 0 else 1
        if voices > 10:
            over.append(row.slot)
        atk = int(np.argmax(np.abs(a))) / dsp.RATE * 1000.0
        flag = "  <-- over the pool" if voices > 10 else ""
        if dsp.peak(a) >= CEILING - 1e-6 and not row.trim_db:
            held.append(row.slot)
            flag += "  (held by the ceiling)"
        print(f"{row.slot:10} {row.family:7} {dur:6.2f} {dsp.peak(a):5.2f} "
              f"{dsp.loudness(a):7.4f} {atk:6.1f} {s['centroid']:6.0f} "
              f"{s['harsh']:6.1%} {s['hiss']:6.1%} {s['warm']:6.1%} {voices:7d}{flag}")

    if held:
        print(f"\n{len(held)} clip(s) sit below the matched loudness because the peak")
        print(f"ceiling caught them first: {', '.join(held)}. That is expected of a short")
        print("dry transient and is not a fault - but if the list grows past a third of")
        print("the set, TARGET_LOUDNESS is too high and the match has stopped meaning much.")

    if over:
        print(f"\n{len(over)} clip(s) can outrun the ten-voice pool: {', '.join(over)}")
        print("A voice reused mid-tail is cut off, which is an audible click on the")
        print("busiest moment in the game. Shorten `head` in Tools/sfx.tsv.")
        return 1
    return 0


# ------------------------------------------------------------------- contact
# ------------------------------------------------------------------- contact
# Four moments from the real game, as (label, blurb, [(slot, at, rate, gain)]).
#
# These are the point of the page. A clip auditioned alone is judged on whether it
# is pleasant; a clip is *used* alongside three others at a pitch it was never
# recorded at, and that is what a player actually hears. Every mistake this set has
# made was inaudible one clip at a time - the old `pop` was a perfectly reasonable
# noise until nine of them ran past in a second.
SCENES = [
    ("a turn", "One conduit turned, then another. The most-repeated second in the game.",
     [("rotate_a", 0.00, 1.02, 1.0), ("rotate_b", 0.42, 0.97, 1.0),
      ("rotate_a", 0.84, 1.05, 1.0), ("back", 1.30, 1.00, 0.7)]),

    ("a solve", "A turn, the lamps waking up the ladder, and the grove settling.",
     [("rotate_a", 0.00, 1.00, 0.9)]
     + [("lit", 0.18 + i * 0.07, 0.92 * (2 ** (s / 12.0)), 0.60 + min(i, 4) * 0.045)
        for i, s in enumerate((0, 2, 4, 7, 9, 12, 14, 16, 19, 21))]
     + [("win", 1.15, 1.00, 0.95)]),

    ("a payout", "The victory panel: the credits counting into the purse.",
     [("coin", 0.10 + i * 0.055, 0.94 + (1.56 - 0.94) * i / 17.0, 0.34) for i in range(18)]),

    ("a victory", "The whole win, panel and all: the grove settles, three stars, the "
                  "credits counting in, and a chapter opening.",
     [("win", 0.00, 1.00, 0.85)]
     + [("star", 1.10 + i * 0.42, 1.0 + i * 0.16, 0.55) for i in range(3)]
     + [("coin", 2.60 + i * 0.055, 0.92 + (1.88 - 0.92) * i / 13.0, 0.46) for i in range(14)]
     + [("unlock", 3.75, 1.00, 0.75)]),

    ("the wheel", "The bonus wheel winding down, and landing.",
     [("whoosh", 0.00, 0.85, 0.40)]
     + [("tick", 0.20 + 0.055 * i + 0.0042 * i * i, 1.0 + 0.35 * min(i / 22.0, 1.0), 0.34)
        for i in range(22)]
     + [("wheel", 2.15, 1.00, 0.62)]),
]

PAGE = r"""<title>Grove Sound Audition</title>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Literata:opsz,wght@7..72,400;7..72,600&family=Archivo:wght@400;500;600&family=JetBrains+Mono:wght@400;500&display=swap">
<style>
:root{
  --ground:#f973;--x:0;
  --bg:#f1f3ec; --panel:#ffffff; --sunk:#e7ebe1;
  --ink:#1c2620; --ink-2:#55635a; --ink-3:#8a978d;
  --line:#d8ded2; --line-2:#c3ccbb;
  --lamp:#c07a2e; --lamp-soft:#eddcc4;
  --moss:#5f8a5c; --alarm:#b04f35;
  --shadow:0 1px 2px rgba(28,38,32,.06), 0 6px 18px rgba(28,38,32,.05);
}
@media (prefers-color-scheme: dark){
  :root:not([data-theme="light"]){
    --bg:#11150f; --panel:#191e16; --sunk:#0c0f0a;
    --ink:#e7ede1; --ink-2:#a0ac9c; --ink-3:#6d786a;
    --line:#28301f; --line-2:#38412e;
    --lamp:#e2a45c; --lamp-soft:#3a2d19;
    --moss:#7faa78; --alarm:#d4795c;
    --shadow:0 1px 2px rgba(0,0,0,.4), 0 8px 22px rgba(0,0,0,.3);
  }
}
:root[data-theme="dark"]{
  --bg:#11150f; --panel:#191e16; --sunk:#0c0f0a;
  --ink:#e7ede1; --ink-2:#a0ac9c; --ink-3:#6d786a;
  --line:#28301f; --line-2:#38412e;
  --lamp:#e2a45c; --lamp-soft:#3a2d19;
  --moss:#7faa78; --alarm:#d4795c;
  --shadow:0 1px 2px rgba(0,0,0,.4), 0 8px 22px rgba(0,0,0,.3);
}

*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);
     font:400 15px/1.55 Archivo,"Segoe UI",system-ui,sans-serif;
     -webkit-font-smoothing:antialiased}
.wrap{max-width:1120px;margin:0 auto;padding:44px 26px 80px}

header{margin-bottom:34px}
h1{font:600 clamp(30px,4.4vw,44px)/1.08 Literata,Georgia,serif;
   margin:0 0 12px;letter-spacing:-.015em;text-wrap:balance}
.lede{margin:0;max-width:66ch;color:var(--ink-2);font-size:16px}
.lede b{color:var(--ink);font-weight:600}
.lede code{font:500 13px JetBrains Mono,ui-monospace,monospace;
  background:var(--sunk);border:1px solid var(--line);border-radius:4px;padding:1px 5px}

h2{font:600 12px/1 Archivo,sans-serif;letter-spacing:.16em;text-transform:uppercase;
   color:var(--ink-3);margin:38px 0 12px;display:flex;align-items:center;gap:12px}
h2::after{content:"";flex:1;height:1px;background:var(--line)}
h2 .of{letter-spacing:.02em;text-transform:none;font-weight:400;color:var(--ink-3)}

/* --- scenes --------------------------------------------------------- */
.scenes{display:grid;grid-template-columns:repeat(auto-fit,minmax(232px,1fr));gap:12px}
.scene{background:var(--panel);border:1px solid var(--line);border-radius:12px;
  padding:15px 16px 14px;box-shadow:var(--shadow);text-align:left;cursor:pointer;
  font:inherit;color:inherit;display:flex;flex-direction:column;gap:6px;
  transition:border-color .15s, transform .15s}
.scene:hover{border-color:var(--lamp)}
.scene:active{transform:translateY(1px)}
.scene:focus-visible{outline:2px solid var(--lamp);outline-offset:2px}
.scene .n{font:600 17px/1.2 Literata,Georgia,serif}
.scene .d{color:var(--ink-2);font-size:13.5px;line-height:1.45}
.scene[data-on="1"]{border-color:var(--lamp);background:var(--lamp-soft)}

/* --- clip rows ------------------------------------------------------- */
.rows{border:1px solid var(--line);border-radius:12px;overflow:hidden;
  background:var(--panel);box-shadow:var(--shadow)}
.row{display:grid;grid-template-columns:92px 104px minmax(0,1fr) 214px 168px;gap:18px;
  align-items:center;padding:10px 16px;border-top:1px solid var(--line)}
.row:first-child{border-top:0}
.row[data-on="1"]{background:var(--lamp-soft)}
.slot{font:500 14px JetBrains Mono,ui-monospace,monospace;color:var(--ink)}
.wave{display:block;width:96px;height:26px}

.tp{display:flex;gap:5px;flex-wrap:wrap}
.tp button{font:500 12px Archivo,sans-serif;color:var(--ink-2);cursor:pointer;
  background:var(--sunk);border:1px solid var(--line);border-radius:999px;padding:4px 11px;
  transition:color .12s,border-color .12s,background .12s}
.tp button:hover{color:var(--ink);border-color:var(--lamp);background:var(--panel)}
.tp button:focus-visible{outline:2px solid var(--lamp);outline-offset:2px}

.what{color:var(--ink-2);font-size:13.5px;line-height:1.45}
.what .why{color:var(--ink-3);display:block;margin-top:2px}

.reads{display:grid;grid-template-columns:52px 58px 1fr 40px;gap:10px;align-items:center;
  font:500 12.5px JetBrains Mono,ui-monospace,monospace;
  font-variant-numeric:tabular-nums;color:var(--ink-2)}
.reads span{text-align:right}
.bar{height:6px;border-radius:3px;background:var(--sunk);
  border:1px solid var(--line);overflow:hidden;position:relative}
.bar i{display:block;height:100%;background:var(--moss)}
.bar.hot i{background:var(--alarm)}
.vc{text-align:right}
.vc.over{color:var(--alarm)}

.legend{margin-top:12px;color:var(--ink-3);font-size:12.5px;max-width:74ch}
.legend b{color:var(--ink-2);font-weight:500}

@media (max-width:900px){
  .row{grid-template-columns:92px minmax(0,1fr);gap:8px 14px}
  .what{grid-column:2}
  .reads{grid-column:1/-1}
  .tp{grid-column:1/-1}
}
@media (prefers-reduced-motion:reduce){*{transition:none!important}}
</style>

<div class="wrap">
<header>
  <h1>Grove Sound Audition</h1>
  <p class="lede">__COUNT__ clips, cut from the pack by <code>Tools/make_sfx.py</code>.
  Start with the <b>scenes</b> &mdash; a clip judged alone is judged on whether it is
  pleasant, and what a player hears is four of them at once at pitches none of them
  were recorded at. Then work down the rows: <b>one</b> is the clip as it ships,
  <b>ladder</b> is the pitches the game really uses, <b>run</b> is the rate it really
  repeats at. Anything that grates on <i>ladder</i> or <i>run</i> is wrong however
  good it sounds once &mdash; change that row in <code>Tools/sfx.tsv</code> and re-run
  the tool.</p>
</header>

<h2>Scenes <span class="of">__SCENECOUNT__ moments from the real game</span></h2>
<div class="scenes">__SCENES__</div>

__FAMILIES__

<p class="legend"><b>The four readings</b> are duration, spectral centroid in hertz
(where the sound sits &mdash; under about 500 a phone speaker cannot reproduce it),
the share of energy in 2&ndash;5&nbsp;kHz (where the ear fatigues first, so the bar is
one to keep short), and <b>voices</b> &mdash; how many copies of itself the clip has to
stand alongside at its busiest moment. Past ten, Unity's pool reuses a voice that is
still sounding and cuts it off.</p>
</div>

<script>
const AC = new (window.AudioContext || window.webkitAudioContext)();
const B64 = __AUDIO__;
const SCENES = __SCENEDATA__;
const buffers = {};

function decode(name){
  if (buffers[name]) return buffers[name];
  const bin = atob(B64[name]);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  buffers[name] = AC.decodeAudioData(bytes.buffer);
  return buffers[name];
}

async function shot(name, when, rate, gain){
  const buf = await decode(name);
  const src = AC.createBufferSource();
  src.buffer = buf;
  src.playbackRate.value = rate;
  const g = AC.createGain();
  g.gain.value = gain * 0.9;          // Audio.PlayOne's own 0.9
  src.connect(g).connect(AC.destination);
  src.start(Math.max(when, AC.currentTime));
}

function lit(el, ms){
  if (!el) return;
  el.dataset.on = "1";
  clearTimeout(el._t);
  el._t = setTimeout(() => { el.dataset.on = "0"; }, ms);
}

function one(name, el){
  AC.resume(); shot(name, AC.currentTime, 1, 1); lit(el, 260);
}
function ladder(name, lo, hi, steps, el){
  AC.resume();
  const t0 = AC.currentTime + 0.05;
  for (let i = 0; i < steps; i++){
    const k = steps > 1 ? i / (steps - 1) : 0;
    shot(name, t0 + i * 0.09, lo + (hi - lo) * k, 1);
  }
  lit(el, steps * 90 + 300);
}
function run(name, gap, count, lo, hi, el){
  AC.resume();
  const t0 = AC.currentTime + 0.05;
  for (let i = 0; i < count; i++){
    const k = count > 1 ? i / (count - 1) : 0;
    shot(name, t0 + i * gap, lo + (hi - lo) * k, 1);
  }
  lit(el, count * gap * 1000 + 300);
}
function scene(i, el){
  AC.resume();
  const t0 = AC.currentTime + 0.06;
  let last = 0;
  for (const [name, at, rate, gain] of SCENES[i]){
    shot(name, t0 + at, rate, gain);
    if (at > last) last = at;
  }
  lit(el, last * 1000 + 900);
}
</script>
"""


def sparkline(a, cells=48):
    """A peak envelope, as an inline SVG path. It encodes attack and decay, which is
    exactly what separates a tap from a chime and is invisible in the numbers."""
    if a.size == 0:
        return ""
    step = max(1, a.size // cells)
    env = [float(np.max(np.abs(a[i:i + step]))) for i in range(0, a.size, step)][:cells]
    top = max(env) or 1.0
    n = len(env)
    up, down = [], []
    for i, v in enumerate(env):
        x = 1 + i * (94.0 / max(n - 1, 1))
        h = (v / top) * 11.0
        up.append(f"{x:.1f},{13 - h:.1f}")
        down.append(f"{x:.1f},{13 + h:.1f}")
    pts = " ".join(up + list(reversed(down)))
    return (f'<svg class="wave" viewBox="0 0 96 26" aria-hidden="true">'
            f'<polygon points="{pts}" fill="currentColor" opacity=".42"/></svg>')


FAMILY_OF = {
    "wood": "Wood &mdash; the interface",
    "water": "Water &mdash; the turn",
    "stone": "Stone &mdash; movement",
    "mallet": "Mallet &mdash; the ladder",
    "bell": "Bells &mdash; reward",
    "tune": "Tune &mdash; the finish",
}


def contact(rows, source_root, out_file):
    if out_file.suffix.lower() not in (".html", ".htm"):
        out_file = out_file / "index.html"
    out_file.parent.mkdir(parents=True, exist_ok=True)

    audio, sections = {}, []
    for family in dict.fromkeys(r.family for r in rows):
        cells = []
        for row in (r for r in rows if r.family == family):
            a = cut(row, source_root)
            buf = io.BytesIO()
            with wave.open(buf, "wb") as w:
                w.setnchannels(1)
                w.setsampwidth(2)
                w.setframerate(dsp.RATE)
                w.writeframes((np.sign(a) * np.floor(np.abs(np.clip(a, -1, 1)) * 32767 + .5))
                              .astype("<i2").tobytes())
            audio[row.slot] = base64.b64encode(buf.getvalue()).decode("ascii")

            s = dsp.spectrum(a)
            dur = a.size / dsp.RATE
            (lo, hi), gap, what = USE.get(row.slot, ((1.0, 1.0), 0.0, ""))
            voices = int(np.ceil((dur / min(lo, hi)) / gap)) if gap > 0 else 1

            tp = [f'<button onclick="one(\'{row.slot}\',this.closest(\'.row\'))">one</button>']
            if hi > lo:
                steps = 12 if row.slot == "lit" else 8
                tp.append(f'<button onclick="ladder(\'{row.slot}\',{lo},{hi},{steps},'
                          f'this.closest(\'.row\'))">ladder</button>')
            if gap > 0:
                n = min(24, max(6, int(1.6 / gap)))
                tp.append(f'<button onclick="run(\'{row.slot}\',{gap},{n},{lo},{hi},'
                          f'this.closest(\'.row\'))">run</button>')

            # The bar is scaled so the old set's worst offender would fill it: `pop`
            # carried 98% of its energy in this band, so 40% is a generous full scale.
            fill = min(100.0, s["harsh"] / 0.40 * 100.0)
            hot = " hot" if s["harsh"] > 0.25 else ""
            over = " over" if voices > 10 else ""

            cells.append(
                f'<div class="row" data-on="0">'
                f'<span style="color:var(--lamp)">{sparkline(a)}</span>'
                f'<span class="slot">{row.slot}</span>'
                f'<span class="what">{html.escape(what)}'
                f'<span class="why">{html.escape(row.note)}</span></span>'
                f'<span class="reads">'
                f'<span>{dur:.2f}s</span><span>{s["centroid"]:.0f}&#8202;Hz</span>'
                f'<span class="bar{hot}" title="{s["harsh"]:.0%} of energy in 2-5 kHz">'
                f'<i style="width:{fill:.0f}%"></i></span>'
                f'<span class="vc{over}" title="overlapping copies at its busiest">'
                f'{voices}</span></span>'
                f'<div class="tp">{"".join(tp)}</div></div>')

        sections.append(f'<h2>{FAMILY_OF.get(family, html.escape(family or "other"))}</h2>'
                        f'<div class="rows">{"".join(cells)}</div>')

    scene_html = "".join(
        f'<button class="scene" data-on="0" onclick="scene({i},this)">'
        f'<span class="n">{html.escape(label)}</span>'
        f'<span class="d">{html.escape(blurb)}</span></button>'
        for i, (label, blurb, _) in enumerate(SCENES))

    words = {1: "One", 2: "Two", 3: "Three", 4: "Four", 5: "Five", 6: "Six", 7: "Seven"}
    page = (PAGE
            .replace("__COUNT__", str(len(rows)))
            .replace("__SCENECOUNT__", words.get(len(SCENES), str(len(SCENES))).lower())
            .replace("__SCENES__", scene_html)
            .replace("__FAMILIES__", "\n".join(sections))
            .replace("__AUDIO__", json.dumps(audio))
            .replace("__SCENEDATA__", json.dumps([steps for _, _, steps in SCENES])))

    out_file.write_text(page, encoding="utf-8")
    kb = out_file.stat().st_size / 1024
    print(f"wrote {out_file}  ({kb:.0f} KB, self-contained)")
    print("Open it and press every scene, then ladder and run on every row.")
    return 0


# ----------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", type=Path, default=DEFAULT_SOURCE,
                    help="the extracted GameBurp pack")
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped wavs are what this writes")
    ap.add_argument("--report", action="store_true",
                    help="print what each clip measures, and its worst case against the voice pool")
    ap.add_argument("--contact", type=Path, metavar="HTML",
                    help="write a self-contained audition page: the clips, their "
                         "ladders, their runs, and four scenes from the real game")
    ap.add_argument("--only", help="one slot, for iterating")
    args = ap.parse_args()

    rows = load_table()
    if args.only:
        rows = [r for r in rows if r.slot == args.only]
        if not rows:
            raise SystemExit(f"no such slot: {args.only}")

    if args.report:
        return report(rows, args.source)
    if args.contact:
        return contact(rows, args.source, args.contact)

    bad = []
    for row in rows:
        a = cut(row, args.source)
        dest = OUT / f"{row.slot}.wav"
        if args.check:
            if not dest.exists():
                bad.append(f"{row.slot}: missing {dest}")
                continue
            want = dest.read_bytes()
            tmp = dest.with_suffix(".check.tmp")
            dsp.write(tmp, a)
            got = tmp.read_bytes()
            tmp.unlink()
            if got != want:
                bad.append(f"{row.slot}: {len(want)} bytes on disk, {len(got)} from the table")
        else:
            dsp.write(dest, a)
            print(f"  {row.slot:10} {a.size / dsp.RATE:5.2f}s  peak {dsp.peak(a):.2f}  <- {row.source}")

    if args.check:
        if bad:
            print("Shipped sound effects are NOT what Tools/sfx.tsv says:")
            for b in bad:
                print("  " + b)
            return 1
        print(f"ok - all {len(rows)} shipped clips reproduce from Tools/sfx.tsv")
        return 0

    print(f"\nwrote {len(rows)} clips to {OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
