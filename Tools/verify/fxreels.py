# -*- coding: utf-8 -*-
"""Proves every baked effect reel actually has a picture in it.

    python Tools/verify/fxreels.py

**`artnames.py` proves a reel's name resolves. Nothing proved the reel was visible**, and
that gap shipped three of them. The three worst reels in the whole game are all boss
spells, and all three are an order of magnitude below every other reel on disk:

    reel        ink at its loudest frame   lit box, as a share of its own frame
    snare                        0.14 %               2.1 % wide x 19.3 % tall
    quake                        0.22 %               5.2 % wide x 18.8 % tall
    quake_hit                    0.83 %               3.1 % wide x 55.3 % tall

A shackler's arrow is **two hundredths of a cell** across on the board, an ironclad's axe
is a tenth, and its landing is a hairline. The median reel here is 16.9 % ink, and the
worst legitimate one is 32 % x 33 %. There is no overlap and there never was: this is one
`if` that nobody had written.

**It is the same fault four times and this file is the fifth telling.** Invariant 37k's
sliver — a thin effect framed square comes out as a thread down the middle of an empty
texture — is written up in `SiegeShotBake` for the stormcall, for the cleaver's impact,
for the first hex and for the first spell. Each was found **by looking**, each was fixed
for that one reel, and the two rows that nobody happened to look at shipped. A fault that
recurs after four hand-fixes is a fault wanting a gate, not a fifth hand-fix.

**Why it can be offline when the bake cannot.** Rasterising a particle system needs the
Editor and the bought pack; reading the PNGs it wrote needs neither. So the *bake* stays an
Editor menu item and the *proof that what it wrote is a picture* runs here, with every
other gate, on a checkout that has never seen the pack — the frames are committed.

**What it cannot do is tell a good picture from a bad one.** It answers "is there anything
there", which is the question that was going unasked. Whether the thing there reads as an
axe coming down is a render and a pair of eyes (`Glimmer Grove > Art > Siege Projectile
Contact Sheet`), exactly as invariant 32b says.
"""
from __future__ import annotations

import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
FX = REPO / "Assets" / "Game" / "Art" / "Fx"
SIEGE = REPO / "Assets" / "Game" / "Art" / "Siege"

#: What counts as lit. Below this a pixel is the haze every additive effect sits in rather
#: than the effect — the bake keys against black, so a near-nought alpha is what it could
#: not quite remove, and counting it would let a cloud of nothing pass as a picture.
INK = 0.15

#: The floors, as a share of the reel's own frame at its loudest moment.
#:
#: **Two axes rather than an area, because the fault is always one axis.** A sliver has a
#: perfectly ordinary length and no width at all, so a single "how much of the frame is
#: filled" number lets one through at exactly the angle it fails. The pair is measured off
#: the lit bounding box: `LONG` is the larger side, `SHORT` the smaller.
#:
#: **Set where the population separates, not where it felt right.** Of 266 reels on disk
#: the three broken ones sit at 2.1, 5.2 and 3.1 per cent on their short side; the worst
#: legitimate reel (a ward's bolt impact) is 32. `roar_muzzle` is the only near thing at
#: 10.9, and it is a flat ground wash that is legitimately flat.
LONG, SHORT = 0.15, 0.08

#: And a floor on the ink itself, which catches the degenerate case the box cannot: a reel
#: whose lit pixels are spread over the frame in a handful of specks has a perfectly
#: healthy bounding box and no picture in it.
FLOOR = 0.010

#: How far a **standing** body may wander across its own frame, as a share of the frame's
#: width, measured centroid to centroid over the whole reel.
#:
#: <b>The second question this file asks, and it is the same shape as the first.</b> The ink
#: check answers "is there a picture in this reel"; this one answers "is it the *same* picture
#: in every frame". Both were unasked, and both shipped.
#:
#: <b>What it caught.</b> A row in `make_siege_art.BOSS_SET` names an animation folder. Three
#: rows - the bonecaller, the shackler and the ironclad - named the *character* folder instead,
#: which in the head-on packs holds four animations, so `ordered` collected all 72 frames of
#: Attack, Idle, Jump and Walk and sorted them by trailing number. `spaced` then took twelve
#: evenly through the mixture and each of the three shipped a walk that cut between a jump and a
#: lunge every frame. On the board the boss wandered out of its lane and off its footing, which
#: reads as a body pasted on the wall at the wrong angle rather than as an error - the same way
#: `ordered`'s own scrambled walk read as a jitter. Levels 30, 35 and 40 shipped that way, every
#: offline gate green.
#:
#: <b>Set where the population separates, exactly as `LONG`/`SHORT` are.</b> Of 119 reels on
#: disk the median stander wanders 0.65 % and the worst legitimate one (a bone brute, whose
#: bob carries a shoulder across) is 8.1 %. The three broken reels sat at 21.4, 25.6 and 32.9.
#: There is no overlap; 15 stands in the middle of the gap.
#:
#: <b>Asked of standing reels only, and that is the whole of why it can be this tight.</b> A
#: `_cast` or a `_swing` is a gesture and is *supposed* to travel - `clad_cast` reaches 18 %
#: and is correct. A reel that moves is not a fault; a reel that moves while standing still is.
WANDER = 0.15


def loudest(folder):
    """The frame carrying the most light, and what it is made of.

    **Never frame nought and never the middle one.** These reels open on a wind-up and
    close on drifting smoke, so a fixed index is a coin toss — `render_siege.py` learned
    this by reporting two good muzzles as broken, and the answer there is the answer here:
    judge a reel at its loudest.

    Answers ``(ink, wide, tall, frames)`` as shares of the frame, or ``None`` when the
    folder holds no frames at all.
    """
    import numpy as np
    from PIL import Image

    files = sorted(p for p in folder.iterdir() if p.suffix.lower() == ".png")
    if not files:
        return None

    best = None
    for path in files:
        with Image.open(path) as im:
            a = np.asarray(im.convert("RGBA"), dtype=np.float32)[..., 3] / 255.0
        if best is None or a.sum() > best.sum():
            best = a

    lit = best > INK
    ys, xs = np.nonzero(lit)

    if len(xs) == 0:
        return 0.0, 0.0, 0.0, len(files)

    wide = float(xs.max() - xs.min() + 1) / best.shape[1]
    tall = float(ys.max() - ys.min() + 1) / best.shape[0]

    return float(lit.mean()), wide, tall, len(files)


def wander(folder):
    """How far the lit body's centre travels across a standing reel, as a share of frame width.

    Answers ``(span, frames)``, or ``None`` when the folder holds fewer than two frames with
    anything lit in them — a single sprite cannot wander and is none of this check's business.
    """
    import numpy as np
    from PIL import Image

    files = sorted(p for p in folder.iterdir() if p.suffix.lower() == ".png")
    if len(files) < 2:
        return None

    centres, width = [], None
    for path in files:
        with Image.open(path) as im:
            alpha = np.asarray(im.convert("RGBA"))[..., 3]
        width = alpha.shape[1]
        ys, xs = np.nonzero(alpha > 40)
        if len(xs):
            centres.append(float(xs.mean()))

    if len(centres) < 2:
        return None

    return (max(centres) - min(centres)) / width, len(files)


def standers():
    """Every siege reel a body *stands* in, as (name, folder).

    **The gesture reels are left out by name rather than by measurement**, because a `_cast`
    and a `_swing` are drawn to travel and the whole value of `WANDER` is that it can be tight.
    """
    if not SIEGE.is_dir():
        return []

    return [(p.name, p) for p in sorted(SIEGE.iterdir())
            if p.is_dir() and not p.name.endswith(("_cast", "_swing"))]


def reels():
    """Every reel under `Art/Fx`, as (set name, folder).

    A reel is a folder of frames, which is what `Flipbook` plays and what a `SpriteSet`
    request resolves by label. A loose `.png` beside them is a single sprite and none of
    this file's business.
    """
    out = []
    for root in sorted(p for p in FX.iterdir() if p.is_dir()):
        for folder in sorted(p for p in root.iterdir() if p.is_dir()):
            out.append((f"{root.name}/{folder.name}", folder))
    return out


def main():
    try:
        import numpy  # noqa: F401
        from PIL import Image  # noqa: F401
    except ImportError:
        print("fxreels: needs numpy and Pillow (this repo has no requirements.txt — see "
              "CLAUDE.md's Verifying section).")
        return 2

    bad, thin, seen, drifting = [], [], [], []

    for name, folder in reels():
        got = loudest(folder)
        if got is None:
            bad.append((name, "holds no frames"))
            continue

        ink, wide, tall, count = got
        long_side, short_side = max(wide, tall), min(wide, tall)
        seen.append((short_side, ink, name, wide, tall, count))

        if ink < FLOOR:
            thin.append((name, ink, wide, tall,
                         f"{ink * 100:.2f}% ink, under the {FLOOR * 100:.1f}% floor"))
        elif long_side < LONG or short_side < SHORT:
            thin.append((name, ink, wide, tall,
                         f"lit box {wide * 100:.1f}% x {tall * 100:.1f}% of its frame"))

    for name, folder in standers():
        got = wander(folder)
        if got is None:
            continue
        span, count = got
        if span > WANDER:
            drifting.append((name, span, count))

    seen.sort()

    print(f"{'ink%':>6s} {'box w%':>7s} {'box h%':>7s} {'frames':>7s}  reel")
    print("-" * 64)
    for short_side, ink, name, wide, tall, count in seen[:12]:
        print(f"{ink * 100:6.2f} {wide * 100:7.1f} {tall * 100:7.1f} {count:7d}  {name}")
    print(f"   ... {len(seen)} reels, tightest twelve shown.")

    if not bad and not thin and not drifting:
        print(f"\nfxreels: OK - {len(seen)} reels, every one of them a picture;")
        print(f"         {len(standers())} standing bodies, every one of them still.")
        return 0

    print()
    for name, why in bad:
        print(f"fxreels: ERROR  {name}: {why}")

    for name, ink, wide, tall, why in thin:
        print(f"fxreels: ERROR  {name}: {why}.")
        print( "                A reel this thin draws as a thread on the board "
               "(invariant 37k).")
        print( "                Fix the framing on the row in SiegeShotBake, or the row's "
               "source, and re-bake.")

    for name, span, count in drifting:
        print(f"fxreels: ERROR  Siege/{name}: the body wanders {span * 100:.1f}% of its own "
              f"frame across {count} frames,")
        print(f"                over the {WANDER * 100:.0f}% a standing reel may.")
        print( "                Almost always the row in make_siege_art's body table names a "
               "character")
        print( "                folder rather than one animation inside it, so several "
               "gestures are")
        print( "                interleaved into one reel. Add the animation suffix and "
               "re-cut.")

    return 1


if __name__ == "__main__":
    sys.exit(main())
