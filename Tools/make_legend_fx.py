# -*- coding: utf-8 -*-
"""Draws what the ten **legendary** turrets throw: thirty flipbooks, cut from nothing.

    python Tools/make_legend_fx.py --write     # cut them
    python Tools/make_legend_fx.py --check     # prove the shipped frames are what this draws
    python Tools/make_legend_fx.py --contact   # one sheet, to look at

**Why this is drawn rather than baked, which is the one decision here worth arguing about.**
Every other projectile in this mode is a bought particle system rasterised by Unity
(`Assets/Game/Editor/Art/SiegeShotBake.cs`), and that is right: what was bought is sixty
*motions*, and an impression of one composed out of the pack's flat textures would be worse than
the thing itself. It is also spent. The pack holds twenty families and the roster already wears
nineteen of them, so a legendary band drawn from it would be ten *third variants* - the same
silhouette in a different hue, which is invariant 37z in one sentence: two things told apart by a
hue are not told apart. A turret at the top of the shelf cannot be the fourth spelling of the
lamp two rows down.

**So these are composed here, and the honest name for that is a different medium rather than a
cheaper one.** Budburst's explosions took this route and so did the grove's ground, the chest
pack and the season crest: a small kit of additive primitives - a soft dot, a soft segment, a
ring, a wedge, a band of turbulence - and a drawing made out of them. What it buys that the bake
cannot is that a legendary can be a *behaviour* rather than a shape. Chain lightning forks
differently every frame; a ricochet visibly bounces; a firethrower is a turbulent jet whose noise
scrolls. None of those are things a still silhouette can say, and all three are what the owner
asked for by name.

**And it runs offline, which is the other half.** A bake needs the Editor, the bought pack and a
GPU; this needs numpy and Pillow, so it is a gate like every other art tool here - `--check`
proves the shipped PNGs are byte-identical to what this draws, and `Tools/verify/fxreels.py`
proves each one is a *picture* (ink, and a lit box on both axes) rather than a thread down the
middle of an empty texture. `--contact` is the one that matters, because neither of those can
tell a good picture from a bad one (invariant 32b, and `fxreels.py`'s own last paragraph).

**The frame geometry is the board's and is not a choice.**

* A **bolt** is a tall frame whose head sits near the top: `SiegeView.Lend` rotates the widget so
  the sprite's own +Y runs along the shot, and anchors it at `SiegeView.HeadAt` measured from the
  bottom. So the head leads at the top and the tail has the rest of the frame to lie in - which
  is also why the frame is narrow: `SiegeView.Round` sizes a bolt by its frame's *width*, so a
  wide frame is a small bolt.
* A **muzzle flash** is square and sits low, at `SiegeView.MuzzleAt` from the bottom, because a
  flash is all in front of the gun - anchored in the middle, half of it draws behind the turret
  that threw it.
* An **impact** is square and centred.

**Colour is the one place these differ from everything else in the mode, and deliberately.**
Every other bolt wears the colour of the ward that threw it, which is what keeps a turret, its
bullets and the gems that feed it from ever disagreeing about what red is (invariant 37f). A
legendary wears *no* ward colour - it stands on any seat and fires at anything - so a reel graded
onto a seat's hue would be the picture telling a player the opposite of the rule. Each of these
ten carries a signature of its own instead, and the ten are spread right round the wheel so that
a line standing four of them still reads as four different weapons.
"""
from __future__ import annotations

import argparse
import io
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parent.parent

# --------------------------------------------------------------------------- the board's numbers

#: How wide and tall a bolt's frame is. **Narrow, because `SiegeView.Round` sizes a bolt by its
#: frame's width** - so the frame is cut tight to the bolt and the height is where the tail goes.
#: The roster's own reels are 56 to 96 wide by 384 tall; these sit in the middle of that.
SHOT_W, SHOT_H = 96, 384

#: How big a muzzle flash and an impact are cut. Square, because both are drawn round a point.
BURST = 192

#: How many frames each reel holds. **The roster's counts**, so a legendary plays at the same
#: cadence as the turret beside it: `SiegeView.Round` runs a bolt at 30fps looping, a flash at 33
#: and an impact at 35, and a reel of a different length would simply run for a different time.
SHOT_FRAMES, MUZZLE_FRAMES, HIT_FRAMES = 14, 8, 12

#: Where the head of a bolt is drawn, as a share of the frame measured from the **top**.
#:
#: <b>Not quite `SiegeView.HeadAt`, and the gap is measured off the shipped reels rather than
#: chosen.</b> The anchor is .82 from the bottom; the bake's own comets put their heads a little
#: ahead of it - `shot_lance_*` at about .95 and `shot_apex_*` at about .88 - because a particle
#: system emits at the anchor and the bright part of the head runs on. Drawn here, the head is a
#: shape rather than an emitter, so it is placed where those two average out.
HEAD_Y = 0.12

#: Where the barrel is in a muzzle flash's frame, as a share measured from the **top**. The
#: complement of `SiegeView.MuzzleAt`, which is measured from the bottom.
MUZZLE_Y = 0.78

#: How much the drawing is supersampled before it is cut down.
#:
#: <b>Two, and it is the whole of the anti-aliasing.</b> Every primitive here is a distance field
#: with a soft falloff, so most edges are already smooth; what supersampling fixes is the hard
#: ones - a crescent's cut edge, a shard's point, the rim of a ring - where a half-covered pixel
#: is the difference between a blade and a staircase. Four was drawn and compared and is
#: invisible at these sizes; it costs four times the memory for a frame that is thrown away.
SS = 2

#: The deepest a head's halo may reach, as a share of the frame measured from the top.
#:
#: <b>A ceiling the board imposes rather than a taste.</b> `SiegeView.Emerged` crops a bolt's trail
#: at the barrel, and a cut through a wide soft disc is a visible straight edge - so a halo has to
#: end *above* where the cut lands. The cut sits at `(1 - HeadAt) + HeadRoom / (4 * BoltScale)` of
#: the frame, which is .254 for a bolt drawn at the band's ordinary size and .234 for the sun,
#: whose frame is taller in cells. Six of the ten were drawn wider than that and were trimmed;
#: what they lost in bloom they got back in gain, so none of them is dimmer.
#:
#: **Check this before widening any `dot` centred on `HEAD_Y`.**
HALO_FLOOR = 0.234


# --------------------------------------------------------------------------- the kit


class Sheet:
    """One frame, as additive energy.

    <b>Energy rather than colour, which is the only way a drawing like this reads as light.</b>
    Every primitive *adds* into a float buffer with no ceiling, so a place two of them overlap is
    brighter than either - which is what makes a core white without anybody painting a white core.
    The conversion to pixels happens once, at the end, in `image`.

    Coordinates are in **frame** units with y running down, exactly as an image does; the
    supersample is private to this class so no drawing has to know about it.
    """

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.e = np.zeros((h * SS, w * SS, 3), np.float32)

    # -- primitives ---------------------------------------------------------------------------
    def dot(self, x, y, radius, colour, gain=1.0, power=2.0):
        """A soft round light: full at the middle, nought at `radius`, falling as `power`.

        <b>Bounded to its own box rather than swept over the frame</b>, because a reel is a few
        hundred of these and a full-frame sweep each is the difference between two seconds and
        two minutes.
        """
        if radius <= 0 or gain <= 0:
            return

        x, y, radius = x * SS, y * SS, radius * SS
        x0, x1 = int(max(0, x - radius)), int(min(self.e.shape[1], x + radius + 1))
        y0, y1 = int(max(0, y - radius)), int(min(self.e.shape[0], y + radius + 1))
        if x1 <= x0 or y1 <= y0:
            return

        gy, gx = np.mgrid[y0:y1, x0:x1]
        d = np.hypot(gx + .5 - x, gy + .5 - y) / radius
        fall = np.clip(1.0 - d, 0.0, 1.0) ** power

        self.e[y0:y1, x0:x1] += fall[..., None] * (np.asarray(colour, np.float32) * gain)

    def seg(self, x0, y0, x1, y1, radius, colour, gain=1.0, power=2.0, taper=None):
        """A soft capsule between two points - a stroke with a round end.

        `taper` is the radius at the far end when a stroke has to narrow, which is what turns a
        line into a trail and a trail into a jet.
        """
        if gain <= 0:
            return

        far = radius if taper is None else taper
        span = math.hypot(x1 - x0, y1 - y0)

        # **Stepped by the *narrow* end and not the wide one**, which is the first thing the
        # contact sheet caught: a stroke tapering from twenty pixels to two was laid down every
        # seven pixels, so its far half came out as a string of beads rather than a trail. Four
        # of the ten wore that, and all four read as a dotted line.
        steps = max(2, int(span * SS / max(1.0, min(radius, far) * SS * .55)))

        for i in range(steps + 1):
            t = i / steps
            self.dot(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t,
                     radius + (far - radius) * t, colour, gain, power)

    def path(self, points, radius, colour, gain=1.0, power=2.0, taper=None):
        """A soft polyline. The forked bolt, the ricochet's zig-zag and every wake are one."""
        for i in range(len(points) - 1):
            a = i / max(1, len(points) - 1)
            b = (i + 1) / max(1, len(points) - 1)
            r0 = radius if taper is None else radius + (taper - radius) * a
            r1 = radius if taper is None else radius + (taper - radius) * b
            g0 = gain if taper is None else gain * (1.0 - .55 * a)
            self.seg(points[i][0], points[i][1], points[i + 1][0], points[i + 1][1],
                     r0, colour, g0, power, taper=r1)

    def ring(self, x, y, radius, width, colour, gain=1.0, start=0.0, sweep=math.tau,
             squash=1.0):
        """A soft annulus, or an arc of one. `squash` flattens it into an ellipse."""
        if radius <= 0 or gain <= 0:
            return

        steps = max(24, int(radius * SS * sweep / 2.2))
        for i in range(steps + 1):
            a = start + sweep * i / steps
            self.dot(x + math.cos(a) * radius, y + math.sin(a) * radius * squash,
                     width, colour, gain)

    def spikes(self, x, y, count, inner, outer, width, colour, gain=1.0, phase=0.0,
               jitter=0.0, rng=None):
        """A star of soft rays out of a point. A shatter, a nova and a punch-through are all one."""
        for i in range(count):
            a = phase + math.tau * i / count
            reach = outer * (1.0 + (rng.uniform(-jitter, jitter) if rng is not None else 0.0))
            self.seg(x + math.cos(a) * inner, y + math.sin(a) * inner,
                     x + math.cos(a) * reach, y + math.sin(a) * reach,
                     width, colour, gain, taper=width * .25)

    def cloud(self, x, y, w, h, colour, gain, rng, cells=6, power=1.6):
        """A band of turbulence: what a flame, a plume and a dust ring are made of.

        <b>Upsampled with Pillow rather than interpolated by hand</b>, because a bicubic resize of
        a small random grid *is* value noise and is the one route here that needs no third
        dependency. The grid is seeded, so the reel reproduces byte for byte.
        """
        x0, x1 = int(max(0, (x - w * .5) * SS)), int(min(self.e.shape[1], (x + w * .5) * SS))
        y0, y1 = int(max(0, (y - h * .5) * SS)), int(min(self.e.shape[0], (y + h * .5) * SS))
        if x1 <= x0 or y1 <= y0:
            return

        grid = rng.random((cells, max(2, int(cells * (y1 - y0) / max(1, x1 - x0))))).astype(np.float32)
        img = Image.fromarray((grid.T * 255).astype(np.uint8), "L")
        img = img.resize((x1 - x0, y1 - y0), Image.BICUBIC)
        n = np.asarray(img, np.float32) / 255.0

        gy, gx = np.mgrid[y0:y1, x0:x1]
        fx = np.clip(1.0 - np.abs(gx + .5 - x * SS) / max(1.0, w * .5 * SS), 0, 1)
        fy = np.clip(1.0 - np.abs(gy + .5 - y * SS) / max(1.0, h * .5 * SS), 0, 1)

        mask = (fx * fy) ** power * (n ** 1.4)
        self.e[y0:y1, x0:x1] += mask[..., None] * (np.asarray(colour, np.float32) * gain)

    # -- the conversion -----------------------------------------------------------------------
    def wane(self, from_depth, keep=0.0):
        """Fades the frame out along its length, from `from_depth` down to `keep` at the bottom.

        <b>Two jobs, and the first is just that a comet's trail should end.</b> These were drawn
        opaque to the bottom edge, so every one of them stopped rather than faded - which is the
        one thing a real trail never does. The second is the board's: `SiegeView.Emerged` crops a
        bolt at the barrel, and a cut is only invisible where there is little left to cut. Measured
        before this, the alpha at the cut row was **1.0 on every reel in the mode**.

        Applied to the energy rather than to the alpha, so the colour fades with it and a faint
        tail-end is a faint tail-end rather than a grey one.
        """
        h = self.e.shape[0]
        depth = (np.arange(h, dtype=np.float32) + .5) / h
        ramp = np.clip((depth - from_depth) / max(1e-6, 1.0 - from_depth), 0.0, 1.0)

        self.e *= (1.0 - (1.0 - keep) * ramp ** TRAIL_FALL)[:, None, None]

    def image(self):
        """The frame as RGBA.

        <b>The peak channel is the alpha and the hue is the energy normalised by it</b>, which is
        what makes an additive drawing survive being composited *normally* - which is what a uGUI
        `Image` does. A place with twice as much energy as it can show does not clip to a
        colour, it goes **white**: the overflow is added back into every channel, so a core reads
        as hot rather than as a saturated blob. That is the one thing every bought effect in this
        mode has and a naive `clip` throws away.
        """
        e = self.e
        peak = e.max(axis=-1)
        safe = np.maximum(peak, 1e-6)

        rgb = e / safe[..., None]
        rgb = np.clip(rgb + np.clip(peak - 1.0, 0.0, None)[..., None] * .9, 0.0, 1.0)

        a = np.clip(peak, 0.0, 1.0)

        out = np.concatenate([rgb, a[..., None]], axis=-1)
        im = Image.fromarray((out * 255.0 + .5).astype(np.uint8), "RGBA")

        return im.resize((self.w, self.h), Image.LANCZOS) if SS != 1 else im


def jag(rng, x, y0, y1, steps, spread, drift=0.0):
    """A jagged run down the frame: the spine of every arc of lightning here."""
    pts = []
    for i in range(steps + 1):
        t = i / steps
        pts.append((x + rng.uniform(-spread, spread) * (1.0 - abs(.5 - t)) + drift * t,
                    y0 + (y1 - y0) * t))
    return pts


def ease(t, power=2.0):
    """Nought to one, slowing at the end. What every burst expands on."""
    return 1.0 - (1.0 - t) ** power


# --------------------------------------------------------------------------- the ten

#: Each legendary's signature, as the colour its light is made of and the hotter one its core
#: takes. **Spread right round the wheel on purpose**: a line may stand four of these, and four
#: bolts a player cannot tell apart is the fault this whole band exists to avoid.
WHITE = (1.0, 1.0, 1.0)


def tempest(sheet, t, rng):
    """**Chain lightning.** A forked bolt that re-draws itself every frame.

    The one effect here that could not have been baked from a still: what says *lightning* is
    that the path is different in the next frame, and a pack's projectile is one shape flown
    along a line.
    """
    cx, hy = SHOT_W * .5, SHOT_H * HEAD_Y
    blue, pale = (0.30, 0.62, 1.0), (0.72, 0.90, 1.0)

    spine = jag(rng, cx, hy, SHOT_H * .96, 13, SHOT_W * .30)
    sheet.path(spine, SHOT_W * .16, blue, .55, power=1.4, taper=SHOT_W * .05)
    sheet.path(spine, SHOT_W * .050, pale, 1.5, power=1.2, taper=SHOT_W * .014)

    for _ in range(3):
        at = rng.integers(2, len(spine) - 3)
        x, y = spine[at]
        fork = jag(rng, x, y, y + SHOT_H * rng.uniform(.10, .22), 5,
                   SHOT_W * .26, drift=rng.uniform(-1, 1) * SHOT_W * .38)
        sheet.path(fork, SHOT_W * .030, pale, .95, power=1.2, taper=SHOT_W * .008)

    sheet.dot(cx, hy, SHOT_W * .46, blue, .60)
    sheet.dot(cx, hy, SHOT_W * .19, WHITE, 1.7)
    sheet.spikes(cx, hy, 6, SHOT_W * .10, SHOT_W * .40, SHOT_W * .028, pale, .8,
                 phase=t * 2.1, jitter=.35, rng=rng)


def tempest_muzzle(sheet, t, rng):
    cx, my = BURST * .5, BURST * MUZZLE_Y
    blue, pale = (0.30, 0.62, 1.0), (0.72, 0.90, 1.0)
    grow = ease(t)

    for i in range(7):
        a = -math.pi * .5 + (i - 3) * .30
        reach = BURST * .46 * grow * rng.uniform(.65, 1.0)
        pts = [(cx + math.cos(a) * r * reach + rng.uniform(-6, 6),
                my + math.sin(a) * r * reach + rng.uniform(-6, 6)) for r in (0, .35, .7, 1.0)]
        sheet.path(pts, BURST * .016, pale, 1.1 * (1.0 - t * .55), taper=BURST * .004)

    sheet.dot(cx, my, BURST * .26 * (.5 + grow), blue, .8 * (1.0 - t * .7))
    sheet.dot(cx, my, BURST * .10 * (.6 + grow * .7), WHITE, 1.9 * (1.0 - t * .75))


def tempest_hit(sheet, t, rng):
    cx = cy = BURST * .5
    blue, pale = (0.30, 0.62, 1.0), (0.72, 0.90, 1.0)
    grow = ease(t, 2.4)

    # **No rim.** A closed ring is what `hit_railgun`, `hit_stasis` and `hit_eclipse` are drawn
    # round, and on the hill three of those land within a cell of one another - so what separates
    # this one is that its edge is made of the arcs themselves, which wander, cross and are
    # different every frame. It reads as a cage rather than as a wheel.
    for i in range(11):
        a = math.tau * i / 11 + t * .6
        reach = BURST * .48 * grow * rng.uniform(.62, 1.05)
        pts = [(cx + math.cos(a) * r * reach + rng.uniform(-9, 9),
                cy + math.sin(a) * r * reach + rng.uniform(-9, 9)) for r in (0, .4, .75, 1.0)]
        sheet.path(pts, BURST * .020, pale, 1.3 * (1.0 - t * .8), taper=BURST * .004)

        # The arc that joins it to the next one round, which is what closes a cage without
        # drawing a rim: it is jagged, so the outline never resolves into a circle.
        b = math.tau * (i + 1) / 11 + t * .6
        near = BURST * .40 * grow
        hoop = [(cx + math.cos(a + (b - a) * k) * near + rng.uniform(-7, 7),
                 cy + math.sin(a + (b - a) * k) * near + rng.uniform(-7, 7))
                for k in (0, .5, 1.0)]
        sheet.path(hoop, BURST * .012, blue, .95 * (1.0 - t * .85), taper=BURST * .004)

    sheet.dot(cx, cy, BURST * .30 * (1.0 - t * .5), blue, 1.0 * (1.0 - t))
    sheet.dot(cx, cy, BURST * .13 * (1.0 - t * .4), WHITE, 2.2 * (1.0 - t))


def ricochet(sheet, t, rng):
    """**A bouncing ball.** The trail caroms off the walls of its own frame.

    Nothing about the rules bounces - a chain picks the nearest raiders (`SiegeBoard.Arc`) - so
    this is the drawing saying what the ability does in the one language a bolt has. The bounce
    points carry afterimages, which is what makes the path read as travelled rather than drawn.
    """
    gold, warm = (1.0, 0.62, 0.12), (1.0, 0.86, 0.45)
    cx, hy = SHOT_W * .5, SHOT_H * HEAD_Y

    pts, x, dx = [(cx, hy)], cx, SHOT_W * .42
    step = (SHOT_H * .95 - hy) / 4
    for i in range(4):
        x += dx
        if x < SHOT_W * .16 or x > SHOT_W * .84:
            dx = -dx
            x = min(max(x, SHOT_W * .16), SHOT_W * .84)
        pts.append((x, hy + step * (i + 1)))

    sheet.path(pts, SHOT_W * .17, gold, .42, power=1.5, taper=SHOT_W * .04)
    sheet.path(pts, SHOT_W * .055, warm, 1.15, power=1.3, taper=SHOT_W * .012)

    # **The bounce points wear an afterimage and a splash of sparks**, because a zig-zag on its
    # own reads as a wire: what says bounced is that something happened at each corner.
    for i, (px, py) in enumerate(pts[1:], start=1):
        fade = 1.0 - i / (len(pts) + 1)
        sheet.dot(px, py, SHOT_W * .26 * fade, gold, .85 * fade)
        sheet.dot(px, py, SHOT_W * .11 * fade, warm, 1.4 * fade)
        sheet.spikes(px, py, 5, SHOT_W * .05, SHOT_W * .22 * fade, SHOT_W * .016, warm,
                     .9 * fade, phase=i * 1.3, jitter=.3, rng=rng)

    spin = t * math.tau
    sheet.dot(cx, hy, SHOT_W * .48, gold, .70)
    sheet.dot(cx, hy, SHOT_W * .24, warm, 1.4)
    sheet.dot(cx, hy, SHOT_W * .11, WHITE, 2.0)
    sheet.ring(cx, hy, SHOT_W * .33, SHOT_W * .022, warm, .9, start=spin, sweep=math.pi * 1.3)


def ricochet_muzzle(sheet, t, rng):
    cx, my = BURST * .5, BURST * MUZZLE_Y
    gold, warm = (1.0, 0.62, 0.12), (1.0, 0.86, 0.45)
    grow = ease(t)

    for side in (-1, 1):
        for i in range(3):
            a = -math.pi * .5 + side * (.22 + i * .20)
            reach = BURST * (.20 + .26 * grow)
            sheet.seg(cx, my, cx + math.cos(a) * reach, my + math.sin(a) * reach,
                      BURST * .030, warm, 1.0 * (1.0 - t * .7), taper=BURST * .006)

    sheet.dot(cx, my, BURST * .27 * (.55 + grow * .7), gold, .95 * (1.0 - t * .65))
    sheet.dot(cx, my, BURST * .11 * (.6 + grow * .6), WHITE, 2.0 * (1.0 - t * .7))


def ricochet_hit(sheet, t, rng):
    cx = cy = BURST * .5
    gold, warm = (1.0, 0.62, 0.12), (1.0, 0.86, 0.45)
    grow = ease(t, 2.2)

    # **It splits.** Three balls leaving is the impact drawing the chain the ability is buying,
    # which is a thing a ring cannot say.
    for i in range(3):
        a = math.tau * i / 3 - math.pi * .5 + t * .4
        r = BURST * .40 * grow
        bx, by = cx + math.cos(a) * r, cy + math.sin(a) * r
        sheet.seg(cx, cy, bx, by, BURST * .030, gold, .55 * (1.0 - t), taper=BURST * .05)
        sheet.dot(bx, by, BURST * .10 * (1.0 - t * .4), warm, 1.3 * (1.0 - t * .7))
        sheet.dot(bx, by, BURST * .045 * (1.0 - t * .4), WHITE, 1.6 * (1.0 - t * .7))

    sheet.ring(cx, cy, BURST * .44 * grow, BURST * .020, warm, 1.0 * (1.0 - t))
    sheet.dot(cx, cy, BURST * .26 * (1.0 - t * .55), gold, 1.1 * (1.0 - t))
    sheet.dot(cx, cy, BURST * .12 * (1.0 - t * .5), WHITE, 2.1 * (1.0 - t))


def pyroclast(sheet, t, rng):
    """**A firethrower.** A tapering jet of turbulence whose noise scrolls down it.

    <b>The taper is the whole read.</b> A flame drawn as a straight band is a band; what says a
    jet is that it is narrow and white where it leaves the barrel and wide, dark and ragged by
    the time it is a frame away.
    """
    hot, mid, deep = (1.0, 0.92, 0.55), (1.0, 0.52, 0.10), (0.85, 0.16, 0.04)
    cx, hy = SHOT_W * .5, SHOT_H * HEAD_Y

    # **It narrows away from the head, and that is the board's rule rather than a flame's.** A
    # real jet widens as it leaves the nozzle - but this is flown *head first*, so the widening
    # end is the one that sits at the barrel, where `SiegeView.Emerged` cuts it flat. Drawn the
    # other way up it is still unmistakably a torrent of fire and it leaves nothing at the cut.
    for i in range(9):
        a = i / 8.0
        y = hy + (SHOT_H * .95 - hy) * a
        wide = SHOT_W * (.78 - .52 * a)
        sheet.cloud(cx + math.sin(a * 5.0 + t * 3.0) * SHOT_W * .07, y,
                    wide, SHOT_H * .16, deep if a > .55 else mid,
                    .52 * (1.0 - a * .55), rng, cells=5)

    sheet.seg(cx, hy, cx, hy + SHOT_H * .55, SHOT_W * .34, mid, .85,
              power=1.5, taper=SHOT_W * .09)
    sheet.seg(cx, hy, cx, hy + SHOT_H * .30, SHOT_W * .18, hot, 1.6,
              power=1.4, taper=SHOT_W * .05)

    for _ in range(14):
        a = rng.random()
        sheet.dot(cx + rng.uniform(-1, 1) * SHOT_W * (.16 + .55 * a),
                  hy + (SHOT_H * .95 - hy) * a,
                  SHOT_W * rng.uniform(.012, .034), hot, rng.uniform(.7, 1.5))

    sheet.dot(cx, hy, SHOT_W * .44, mid, .75)
    sheet.dot(cx, hy, SHOT_W * .17, WHITE, 1.9)


def pyroclast_muzzle(sheet, t, rng):
    cx, my = BURST * .5, BURST * MUZZLE_Y
    hot, mid = (1.0, 0.92, 0.55), (1.0, 0.52, 0.10)
    grow = ease(t)

    for i in range(6):
        a = i / 5.0
        sheet.cloud(cx + math.sin(a * 4.0) * BURST * .06, my - BURST * .46 * a * grow,
                    BURST * (.22 + .46 * a) * grow, BURST * .22, mid,
                    .70 * (1.0 - t * .5) * (1.0 - a * .4), rng, cells=5)

    sheet.seg(cx, my, cx, my - BURST * .34 * grow, BURST * .085, hot,
              1.5 * (1.0 - t * .5), taper=BURST * .18)
    sheet.dot(cx, my, BURST * .12 * (.6 + grow * .6), WHITE, 2.0 * (1.0 - t * .6))


def pyroclast_hit(sheet, t, rng):
    cx = cy = BURST * .5
    hot, mid, deep = (1.0, 0.92, 0.55), (1.0, 0.52, 0.10), (0.85, 0.16, 0.04)
    grow = ease(t, 2.2)

    for i in range(10):
        a = math.tau * i / 10 + t
        r = BURST * .36 * grow
        sheet.cloud(cx + math.cos(a) * r, cy + math.sin(a) * r,
                    BURST * .30, BURST * .30, deep if t > .5 else mid,
                    .60 * (1.0 - t * .6), rng, cells=4)

    for _ in range(16):
        a = rng.random() * math.tau
        r = BURST * .46 * grow * rng.uniform(.5, 1.0)
        sheet.dot(cx + math.cos(a) * r, cy + math.sin(a) * r,
                  BURST * rng.uniform(.008, .020), hot, rng.uniform(.8, 1.6) * (1.0 - t * .5))

    sheet.dot(cx, cy, BURST * .30 * (1.0 - t * .4), mid, 1.1 * (1.0 - t * .8))
    sheet.dot(cx, cy, BURST * .13 * (1.0 - t * .3), WHITE, 2.2 * (1.0 - t))


def permafrost(sheet, t, rng):
    """**A crystal head in a vapour trail.** Cold, and the one bolt here that is nearly white.

    It is white for `Shot.White`'s reason, met from the other side: a snowball thrown by a red
    turret is still a snowball, and a legendary throws no colour of anybody's anyway.
    """
    ice, pale = (0.35, 0.74, 1.0), (0.80, 0.95, 1.0)
    cx, hy = SHOT_W * .5, SHOT_H * HEAD_Y

    sheet.seg(cx, hy, cx, hy + SHOT_H * .52, SHOT_W * .20, ice, .48,
              power=1.6, taper=SHOT_W * .04)

    for i in range(8):
        a = (i + 1) / 8.0
        sheet.cloud(cx + math.sin(a * 4.0 + t * 2.0) * SHOT_W * .10,
                    hy + (SHOT_H * .92 - hy) * a,
                    SHOT_W * (.74 - .40 * a), SHOT_H * .16, ice,
                    .52 * (1.0 - a * .60), rng, cells=5)

    for _ in range(13):
        a = rng.random()
        sheet.spikes(cx + rng.uniform(-1, 1) * SHOT_W * (.10 + .30 * a),
                     hy + (SHOT_H * .92 - hy) * a, 4, 0.0,
                     SHOT_W * rng.uniform(.045, .085), SHOT_W * .013, pale,
                     rng.uniform(.7, 1.3) * (1.0 - a * .45), phase=rng.random())

    spin = t * 1.1
    sheet.dot(cx, hy, SHOT_W * .52, ice, .78)
    sheet.spikes(cx, hy, 6, 0.0, SHOT_W * .60, SHOT_W * .060, pale, 1.35, phase=spin)
    sheet.spikes(cx, hy, 6, 0.0, SHOT_W * .34, SHOT_W * .040, WHITE, 1.5,
                 phase=spin + math.pi / 6)
    sheet.dot(cx, hy, SHOT_W * .19, WHITE, 2.1)


def permafrost_muzzle(sheet, t, rng):
    cx, my = BURST * .5, BURST * MUZZLE_Y
    ice, pale = (0.35, 0.74, 1.0), (0.80, 0.95, 1.0)
    grow = ease(t)

    sheet.cloud(cx, my - BURST * .18 * grow, BURST * .66 * grow, BURST * .40 * grow,
                ice, .55 * (1.0 - t * .5), rng, cells=5)
    sheet.spikes(cx, my, 7, BURST * .05, BURST * .38 * grow, BURST * .026, pale,
                 1.2 * (1.0 - t * .6), phase=-math.pi * .5, jitter=.25, rng=rng)
    sheet.dot(cx, my, BURST * .11 * (.6 + grow * .6), WHITE, 1.9 * (1.0 - t * .65))


def permafrost_hit(sheet, t, rng):
    cx = cy = BURST * .5
    ice, pale = (0.35, 0.74, 1.0), (0.80, 0.95, 1.0)
    grow = ease(t, 2.3)

    sheet.ring(cx, cy, BURST * .44 * grow, BURST * .026, pale, 1.1 * (1.0 - t))

    # The shatter: shards leaving, each one a short tapered stroke rather than a dot, because a
    # broken crystal has an edge and a spark does not.
    for i in range(11):
        a = math.tau * i / 11 + .3
        r0, r1 = BURST * .10, BURST * .46 * grow * rng.uniform(.75, 1.05)
        sheet.seg(cx + math.cos(a) * r0, cy + math.sin(a) * r0,
                  cx + math.cos(a) * r1, cy + math.sin(a) * r1,
                  BURST * .028, pale, 1.2 * (1.0 - t * .8), taper=BURST * .005)

    sheet.spikes(cx, cy, 6, 0.0, BURST * .26 * (1.0 - t * .3), BURST * .034, WHITE,
                 1.5 * (1.0 - t), phase=t * .8)
    sheet.dot(cx, cy, BURST * .28 * (1.0 - t * .5), ice, 1.0 * (1.0 - t))
    sheet.dot(cx, cy, BURST * .11 * (1.0 - t * .4), WHITE, 2.1 * (1.0 - t))


def sunderer(sheet, t, rng):
    """**Two crossing crescents, turning.** What cuts through plating.

    <b>A crescent rather than a ball, because the ability is a cut.</b> The pair turn together,
    so the silhouette is different in every frame without the shape ever changing - which is how
    a blade reads as spinning rather than as a still picture being flown.
    """
    violet, pale = (0.62, 0.28, 1.0), (0.88, 0.72, 1.0)
    cx, hy = SHOT_W * .5, SHOT_H * HEAD_Y

    sheet.seg(cx, hy, cx, hy + SHOT_H * .52, SHOT_W * .20, violet, .50,
              power=1.6, taper=SHOT_W * .035)

    # **Opposite rather than a quarter turn apart**, which is what the first cut got wrong: two
    # arcs ninety degrees apart and a hundred and ten degrees long simply join, and what came out
    # was one hook. Facing each other they read as a blade turning.
    spin = t * math.tau
    for k in (0, 1):
        a0 = spin + k * math.pi
        sheet.ring(cx, hy, SHOT_W * .43, SHOT_W * .080, pale, 1.30,
                   start=a0 - .90, sweep=1.80)
        sheet.ring(cx, hy, SHOT_W * .43, SHOT_W * .034, WHITE, 1.45,
                   start=a0 - .76, sweep=1.52)

    sheet.dot(cx, hy, SHOT_W * .52, violet, .82)
    sheet.dot(cx, hy, SHOT_W * .26, (0.78, 0.52, 1.0), 1.25)
    sheet.dot(cx, hy, SHOT_W * .12, WHITE, 2.0)


def sunderer_muzzle(sheet, t, rng):
    cx, my = BURST * .5, BURST * MUZZLE_Y
    violet, pale = (0.62, 0.28, 1.0), (0.88, 0.72, 1.0)
    grow = ease(t)

    for side in (-1, 1):
        tipx = cx + side * BURST * .34 * grow
        sheet.seg(cx, my, tipx, my - BURST * .36 * grow, BURST * .034, pale,
                  1.3 * (1.0 - t * .65), taper=BURST * .006)

    sheet.ring(cx, my, BURST * .22 * grow, BURST * .020, pale, 1.0 * (1.0 - t * .7),
               start=-math.pi * .95, sweep=math.pi * .9)
    sheet.dot(cx, my, BURST * .24 * (.55 + grow * .6), violet, .85 * (1.0 - t * .7))
    sheet.dot(cx, my, BURST * .10 * (.6 + grow * .6), WHITE, 1.9 * (1.0 - t * .7))


def sunderer_hit(sheet, t, rng):
    cx = cy = BURST * .5
    violet, pale = (0.62, 0.28, 1.0), (0.88, 0.72, 1.0)
    grow = ease(t, 2.1)

    # Two gashes crossing, which is a *cut* rather than a burst - the one thing this ability has
    # that the splash two rows down does not.
    for a in (math.pi * .30, -math.pi * .30):
        reach = BURST * .46 * grow
        sheet.seg(cx - math.cos(a) * reach, cy - math.sin(a) * reach,
                  cx + math.cos(a) * reach, cy + math.sin(a) * reach,
                  BURST * .055 * (1.0 - t * .4), violet, 1.0 * (1.0 - t * .75))
        sheet.seg(cx - math.cos(a) * reach * .9, cy - math.sin(a) * reach * .9,
                  cx + math.cos(a) * reach * .9, cy + math.sin(a) * reach * .9,
                  BURST * .020 * (1.0 - t * .4), WHITE, 1.4 * (1.0 - t * .8))

    for _ in range(12):
        a = rng.random() * math.tau
        r = BURST * .44 * grow * rng.uniform(.5, 1.0)
        sheet.dot(cx + math.cos(a) * r, cy + math.sin(a) * r,
                  BURST * rng.uniform(.007, .017), pale, rng.uniform(.9, 1.7) * (1.0 - t * .6))

    sheet.dot(cx, cy, BURST * .24 * (1.0 - t * .5), violet, 1.0 * (1.0 - t))
    sheet.dot(cx, cy, BURST * .10 * (1.0 - t * .4), WHITE, 2.1 * (1.0 - t))


def stasis(sheet, t, rng):
    """**A singularity.** A bright rim round a dark middle, with motes falling inward.

    <b>The one bolt in this mode with a hole in it</b>, which is the only way to draw *stop*
    additively: energy cannot be taken away, so the middle is simply never lit and the rim is lit
    hard, and what the eye reads is a disc.
    """
    indigo, pale = (0.35, 0.34, 1.0), (0.72, 0.80, 1.0)
    cx, hy = SHOT_W * .5, SHOT_H * HEAD_Y
    spin = t * math.tau

    sheet.seg(cx, hy, cx, hy + SHOT_H * .50, SHOT_W * .18, indigo, .46,
              power=1.6, taper=SHOT_W * .03)

    # Motes spiralling in. Each carries its own short wake, so the frame says which way they go.
    for i in range(14):
        a = spin * 1.6 + math.tau * i / 14
        r = SHOT_W * (.40 + .22 * ((i * .37 + t) % 1.0))
        sheet.seg(cx + math.cos(a + .40) * (r + SHOT_W * .14),
                  hy + math.sin(a + .40) * (r + SHOT_W * .14),
                  cx + math.cos(a) * r, hy + math.sin(a) * r,
                  SHOT_W * .022, pale, 1.05, taper=SHOT_W * .007)

    sheet.ring(cx, hy, SHOT_W * .36, SHOT_W * .065, pale, 1.55)
    sheet.ring(cx, hy, SHOT_W * .36, SHOT_W * .026, WHITE, 1.3, start=spin, sweep=math.pi * 1.1)
    sheet.dot(cx, hy, SHOT_W * .52, indigo, .58)


def stasis_muzzle(sheet, t, rng):
    cx, my = BURST * .5, BURST * MUZZLE_Y
    indigo, pale = (0.35, 0.34, 1.0), (0.72, 0.80, 1.0)

    # It **collapses** rather than expands, which is the flash saying what the turret does.
    shrink = 1.0 - ease(t) * .72
    sheet.ring(cx, my, BURST * .42 * shrink, BURST * .026, pale, 1.2)
    sheet.ring(cx, my, BURST * .30 * shrink, BURST * .016, WHITE, .9)
    sheet.dot(cx, my, BURST * .22 * (1.0 - shrink * .5), indigo, .9)
    sheet.dot(cx, my, BURST * .09 * (.4 + t * .8), WHITE, 1.4 + t)


def stasis_hit(sheet, t, rng):
    cx = cy = BURST * .5
    indigo, pale = (0.35, 0.34, 1.0), (0.72, 0.80, 1.0)
    grow = ease(t, 2.6)

    # The ring snaps out and then **holds**, and the spokes hold with it: what a stun looks like
    # is time stopping, not an explosion.
    hold = min(1.0, t * 3.0)
    sheet.ring(cx, cy, BURST * .44 * hold, BURST * .024, pale, 1.2 * (1.0 - t * .5))
    sheet.ring(cx, cy, BURST * .44 * hold, BURST * .009, WHITE, 1.0 * (1.0 - t * .4))

    for i in range(12):
        a = math.tau * i / 12
        sheet.seg(cx + math.cos(a) * BURST * .18, cy + math.sin(a) * BURST * .18,
                  cx + math.cos(a) * BURST * .42 * hold, cy + math.sin(a) * BURST * .42 * hold,
                  BURST * .012, pale, .9 * (1.0 - t * .55), taper=BURST * .004)

    sheet.dot(cx, cy, BURST * .30 * grow, indigo, .85 * (1.0 - t * .5))
    sheet.dot(cx, cy, BURST * .10, WHITE, 1.8 * (1.0 - t * .6))


def railgun(sheet, t, rng):
    """**A beam with a diamond head.** Straight, because the ability is a lane.

    Shock rings run down the shaft rather than the shaft flickering, which is what tells a beam
    from a lightning bolt at the size these are drawn.
    """
    rose, pale = (1.0, 0.22, 0.62), (1.0, 0.72, 0.88)
    cx, hy = SHOT_W * .5, SHOT_H * HEAD_Y

    sheet.seg(cx, hy, cx, SHOT_H * .98, SHOT_W * .16, rose, .55, power=1.6, taper=SHOT_W * .05)
    sheet.seg(cx, hy, cx, SHOT_H * .96, SHOT_W * .055, pale, 1.3, power=1.3, taper=SHOT_W * .016)
    sheet.seg(cx, hy, cx, SHOT_H * .92, SHOT_W * .020, WHITE, 1.6, power=1.2, taper=SHOT_W * .006)

    for i in range(4):
        y = hy + ((i * .25 + t) % 1.0) * (SHOT_H * .92 - hy)
        r = SHOT_W * (.10 + .20 * (y - hy) / max(1.0, SHOT_H * .92 - hy))
        sheet.ring(cx, y, r, SHOT_W * .016, pale, .85, squash=.42)

    sheet.spikes(cx, hy, 4, 0.0, SHOT_W * .42, SHOT_W * .040, pale, 1.2, phase=math.pi * .25)
    sheet.dot(cx, hy, SHOT_W * .40, rose, .70)
    sheet.dot(cx, hy, SHOT_W * .15, WHITE, 2.1)


def railgun_muzzle(sheet, t, rng):
    cx, my = BURST * .5, BURST * MUZZLE_Y
    rose, pale = (1.0, 0.22, 0.62), (1.0, 0.72, 0.88)
    grow = ease(t)

    sheet.seg(cx - BURST * .44 * grow, my, cx + BURST * .44 * grow, my,
              BURST * .034 * (1.0 - t * .5), pale, 1.3 * (1.0 - t * .6), taper=BURST * .005)
    sheet.dot(cx, my, BURST * .30 * grow, rose, .55 * (1.0 - t * .6))
    sheet.seg(cx, my, cx, my - BURST * .40 * grow,
              BURST * .050, rose, 1.0 * (1.0 - t * .6), taper=BURST * .010)
    sheet.seg(cx, my, cx, my - BURST * .32 * grow,
              BURST * .018, WHITE, 1.7 * (1.0 - t * .6), taper=BURST * .004)
    sheet.dot(cx, my, BURST * .11 * (.6 + grow * .5), WHITE, 2.2 * (1.0 - t * .65))


def railgun_hit(sheet, t, rng):
    cx = cy = BURST * .5
    rose, pale = (1.0, 0.22, 0.62), (1.0, 0.72, 0.88)
    grow = ease(t, 2.4)

    # **A punch-through rather than a burst**, which is what a lane weapon does when it arrives:
    # the shaft carries straight on out of the far side and the only thing that spreads is the
    # collar it tore on the way in. Flattened to a quarter, so nothing here reads as a ring.
    sheet.seg(cx, cy - BURST * .50, cx, cy + BURST * .50,
              BURST * .055, rose, .85 * (1.0 - t * .7), taper=BURST * .055)
    sheet.seg(cx, cy - BURST * .50, cx, cy + BURST * .50,
              BURST * .018 * (1.0 - t * .3), WHITE, 1.6 * (1.0 - t * .7), taper=BURST * .018)

    sheet.ring(cx, cy, BURST * .40 * grow, BURST * .030, pale, 1.1 * (1.0 - t), squash=.26)
    sheet.ring(cx, cy, BURST * .40 * grow, BURST * .011, WHITE, .9 * (1.0 - t), squash=.26)

    for side in (-1, 1):
        for i in range(4):
            a = side * (.18 + i * .16)
            reach = BURST * .44 * grow
            sheet.seg(cx, cy, cx + math.sin(a) * reach, cy + math.cos(a) * reach * .30,
                      BURST * .016, pale, 1.0 * (1.0 - t * .8), taper=BURST * .004)

    sheet.dot(cx, cy, BURST * .24 * (1.0 - t * .45), rose, 1.1 * (1.0 - t))
    sheet.dot(cx, cy, BURST * .11 * (1.0 - t * .4), WHITE, 2.3 * (1.0 - t))


def starfall(sheet, t, rng):
    """**A meteor.** A burning head with a long ragged tail and sparks coming off it.

    <b>The head is drawn dark-rimmed on purpose.</b> Everything else in this set is light all the
    way through; a falling rock has a body, and the only way to say so additively is to lay a
    warm ring round a cool middle and let the tail do the burning.
    """
    ember, hot = (1.0, 0.45, 0.06), (1.0, 0.86, 0.40)
    cx, hy = SHOT_W * .5, SHOT_H * HEAD_Y

    # Narrowing away from the head, for `pyroclast`'s reason: the far end is what parks at the
    # barrel, so it is the end that has to be quiet.
    for i in range(10):
        a = (i + 1) / 10.0
        sheet.cloud(cx + math.sin(a * 6.0 + t * 4.0) * SHOT_W * .11 * (1.0 - a * .6),
                    hy + (SHOT_H * .96 - hy) * a,
                    SHOT_W * (.86 - .58 * a), SHOT_H * .17, ember,
                    .88 * (1.0 - a * .55), rng, cells=5)

    sheet.seg(cx, hy, cx, hy + SHOT_H * .62, SHOT_W * .34, ember, 1.05,
              power=1.4, taper=SHOT_W * .08)
    sheet.seg(cx, hy, cx, hy + SHOT_H * .34, SHOT_W * .20, hot, 1.30,
              power=1.4, taper=SHOT_W * .06)

    for _ in range(16):
        a = rng.random()
        sheet.dot(cx + rng.uniform(-1, 1) * SHOT_W * (.34 - .22 * a),
                  hy + (SHOT_H * .96 - hy) * a,
                  SHOT_W * rng.uniform(.010, .028), hot,
                  rng.uniform(.8, 1.6) * (1.0 - a * .55))

    sheet.dot(cx, hy, SHOT_W * .50, ember, .66)
    sheet.ring(cx, hy, SHOT_W * .21, SHOT_W * .060, hot, 1.5)
    sheet.ring(cx, hy, SHOT_W * .21, SHOT_W * .024, WHITE, 1.2, start=t * 2.0, sweep=math.pi)


def starfall_muzzle(sheet, t, rng):
    cx, my = BURST * .5, BURST * MUZZLE_Y
    ember, hot = (1.0, 0.45, 0.06), (1.0, 0.86, 0.40)
    grow = ease(t)

    for i in range(5):
        a = i / 4.0
        sheet.cloud(cx + math.sin(a * 3.0) * BURST * .08, my - BURST * .44 * a * grow,
                    BURST * (.24 + .40 * a) * grow, BURST * .24, ember,
                    .62 * (1.0 - t * .5) * (1.0 - a * .35), rng, cells=4)

    sheet.seg(cx, my, cx, my - BURST * .30 * grow, BURST * .075, hot,
              1.3 * (1.0 - t * .55), taper=BURST * .16)
    sheet.dot(cx, my, BURST * .11 * (.6 + grow * .6), WHITE, 2.0 * (1.0 - t * .6))


def starfall_hit(sheet, t, rng):
    cx = cy = BURST * .5
    ember, hot = (1.0, 0.45, 0.06), (1.0, 0.86, 0.40)
    grow = ease(t, 2.0)

    # A crater: a flat ring on the ground, debris out of it, and a dust cloud that outlives both.
    sheet.ring(cx, cy, BURST * .46 * grow, BURST * .030, ember, 1.0 * (1.0 - t), squash=.55)
    sheet.ring(cx, cy, BURST * .46 * grow, BURST * .012, hot, .9 * (1.0 - t * .9), squash=.55)

    for i in range(9):
        a = math.tau * i / 9 + .2
        r = BURST * .44 * grow * rng.uniform(.6, 1.0)
        sheet.seg(cx, cy, cx + math.cos(a) * r, cy + math.sin(a) * r * .8,
                  BURST * .026, hot, .95 * (1.0 - t * .75), taper=BURST * .009)

    sheet.cloud(cx, cy, BURST * .90 * grow, BURST * .50 * grow, ember,
                .45 * (1.0 - t * .55), rng, cells=5)
    sheet.dot(cx, cy, BURST * .26 * (1.0 - t * .5), hot, 1.2 * (1.0 - t))
    sheet.dot(cx, cy, BURST * .12 * (1.0 - t * .4), WHITE, 2.2 * (1.0 - t))


def wellspring(sheet, t, rng):
    """**A draught being drawn in.** Motes spiralling toward a core rather than away from it.

    Every other effect here throws something outward; this one gathers, which is the only
    reading of *a kill hands the fuel back* that a bolt can carry.
    """
    leaf, pale = (0.35, 1.0, 0.45), (0.85, 1.0, 0.70)
    cx, hy = SHOT_W * .5, SHOT_H * HEAD_Y
    spin = t * math.tau

    sheet.seg(cx, hy, cx, hy + SHOT_H * .54, SHOT_W * .19, leaf, .50,
              power=1.6, taper=SHOT_W * .035)

    # **A helix wound round the shaft rather than arcs thrown wide of it**, which is the second
    # thing the sheet caught: motes that leave the axis read as a scatter, and a siphon has to
    # read as a *draught*. Each one runs a little way round the trail and a little way up it, so
    # the frame says which way the light is going without anything being drawn as an arrow.
    for i in range(16):
        p = ((i * .23 + t) % 1.0)
        a = spin * 1.6 + math.tau * i / 16

        arc = [(cx + math.cos(a + .70 * k) * SHOT_W * (.30 - .05 * k),
                hy + SHOT_H * (.33 * p + .028 * k)
                   + math.sin(a + .70 * k) * SHOT_W * (.30 - .05 * k) * .30)
               for k in (2, 1, 0)]
        sheet.path(arc, SHOT_W * .015, pale, 1.30 * (1.0 - p * .30), taper=SHOT_W * .030)

    sheet.ring(cx, hy, SHOT_W * .40, SHOT_W * .034, leaf, 1.05, squash=.36)
    sheet.dot(cx, hy, SHOT_W * .52, leaf, .78)
    sheet.dot(cx, hy, SHOT_W * .25, pale, 1.4)
    sheet.dot(cx, hy, SHOT_W * .12, WHITE, 2.1)


def wellspring_muzzle(sheet, t, rng):
    cx, my = BURST * .5, BURST * MUZZLE_Y
    leaf, pale = (0.35, 1.0, 0.45), (0.85, 1.0, 0.70)
    close = 1.0 - ease(t) * .62

    for i in range(9):
        a = math.tau * i / 9 + t * 1.4
        r = BURST * .44 * close
        sheet.seg(cx + math.cos(a) * r, my + math.sin(a) * r * .55,
                  cx + math.cos(a) * r * .35, my + math.sin(a) * r * .20,
                  BURST * .020, pale, 1.0, taper=BURST * .005)

    sheet.dot(cx, my, BURST * .24 * (.5 + t * .7), leaf, .95)
    sheet.dot(cx, my, BURST * .10 * (.4 + t * .8), WHITE, 1.5 + t)


def wellspring_hit(sheet, t, rng):
    cx = cy = BURST * .5
    leaf, pale = (0.35, 1.0, 0.45), (0.85, 1.0, 0.70)
    grow = ease(t, 2.2)

    # It blooms and then **draws up**: the ring goes out, the motes come back and rise, which is
    # the fuel going home.
    sheet.ring(cx, cy, BURST * .40 * grow, BURST * .024, leaf, 1.0 * (1.0 - t))
    for i in range(12):
        a = math.tau * i / 12 + t * .8
        r = BURST * .40 * (1.0 - t * .75)
        lift = BURST * .34 * t
        sheet.dot(cx + math.cos(a) * r, cy + math.sin(a) * r * .7 - lift,
                  BURST * .016, pale, 1.2 * (1.0 - t * .5))

    sheet.dot(cx, cy, BURST * .26 * (1.0 - t * .4), leaf, 1.1 * (1.0 - t * .8))
    sheet.dot(cx, cy, BURST * .11 * (1.0 - t * .3), WHITE, 2.1 * (1.0 - t * .9))


#: The three rays an eclipse throws, and the one place in this file a colour is not a signature.
#:
#: <b>A prism, because the dearest turret in the game should be throwing every colour at once</b>
#: - which is also the honest picture of a turret that answers every colour on the hill. They are
#: not the board's four: a bolt wearing red, green, blue and amber would be read as a colour rule,
#: which is exactly what a legendary does not have.
PRISM = ((1.0, 0.30, 0.55), (0.35, 0.85, 1.0), (1.0, 0.80, 0.25))


def eclipse(sheet, t, rng):
    """**A corona.** A blazing rim round an unlit middle, with three prismatic flares turning.

    The grandest thing either line throws, and it is drawn as the *biggest* rather than the
    brightest - `SiegeView.BoltScale` gives it the room a sun gets, and what fills that room is a
    ring of fire rather than a bigger ball, because a ball at that size is a blob.
    """
    cx, hy = SHOT_W * .5, SHOT_H * HEAD_Y
    spin = t * math.tau

    sheet.seg(cx, hy, cx, hy + SHOT_H * .58, SHOT_W * .22, (0.55, 0.45, 0.85), .50,
              power=1.6, taper=SHOT_W * .04)

    # **Held under one, which is the tonemap in `Sheet.image` read backwards.** Energy over one
    # is added back into every channel so a core goes white; three rays laid at 1.25 therefore
    # arrived as three white rays, and the whole point of them is that they are not. The ring
    # behind them is where the white belongs.
    for i, colour in enumerate(PRISM):
        a = spin + math.tau * i / 3
        sheet.seg(cx - math.cos(a) * SHOT_W * .78, hy - math.sin(a) * SHOT_W * .78,
                  cx + math.cos(a) * SHOT_W * .78, hy + math.sin(a) * SHOT_W * .78,
                  SHOT_W * .052, colour, .88, taper=SHOT_W * .052)

    sheet.ring(cx, hy, SHOT_W * .40, SHOT_W * .090, (1.0, 0.92, 0.75), 1.45)
    sheet.ring(cx, hy, SHOT_W * .40, SHOT_W * .036, WHITE, 1.6)
    sheet.ring(cx, hy, SHOT_W * .40, SHOT_W * .018, WHITE, 1.3,
               start=spin * -1.4, sweep=math.pi * .8)
    sheet.dot(cx, hy, SHOT_W * .44, (0.85, 0.70, 1.0), .58)


def eclipse_muzzle(sheet, t, rng):
    cx, my = BURST * .5, BURST * MUZZLE_Y
    grow = ease(t)

    for i, colour in enumerate(PRISM):
        a = -math.pi * .5 + (i - 1) * .42 + t * .3
        sheet.seg(cx, my, cx + math.cos(a) * BURST * .46 * grow,
                  my + math.sin(a) * BURST * .46 * grow,
                  BURST * .034, colour, 1.2 * (1.0 - t * .6), taper=BURST * .006)

    sheet.ring(cx, my, BURST * .26 * grow, BURST * .028, (1.0, 0.92, 0.75),
               1.2 * (1.0 - t * .6))
    sheet.dot(cx, my, BURST * .12 * (.6 + grow * .6), WHITE, 2.2 * (1.0 - t * .65))


def eclipse_hit(sheet, t, rng):
    cx = cy = BURST * .5
    grow = ease(t, 2.3)

    for i, colour in enumerate(PRISM):
        sheet.ring(cx, cy, BURST * (.24 + .22 * i) * grow, BURST * .022, colour,
                   1.1 * (1.0 - t))

    for i in range(12):
        a = math.tau * i / 12 + t * .5
        r = BURST * .48 * grow
        sheet.seg(cx + math.cos(a) * BURST * .08, cy + math.sin(a) * BURST * .08,
                  cx + math.cos(a) * r, cy + math.sin(a) * r,
                  BURST * .020, PRISM[i % 3], 1.1 * (1.0 - t * .8), taper=BURST * .004)

    sheet.ring(cx, cy, BURST * .18 * (1.0 + t), BURST * .040, (1.0, 0.92, 0.75),
               1.3 * (1.0 - t))
    sheet.dot(cx, cy, BURST * .14 * (1.0 - t * .3), WHITE, 2.4 * (1.0 - t))


#: What each legendary throws: the bolt, the flash at the barrel and what it does when it lands.
#:
#: **A row per turret rather than a shared shape with a hue on it**, which is the whole reason
#: this file is long. `SiegeShotBake`'s own note says the pack's twenty families are the scarce
#: thing and that two rungs of one ability told apart by a colour are told apart by nothing
#: (invariant 37z); ten legendaries drawn as ten tints would be that fault at the top of the
#: shelf, where a player has just paid the most this game asks for anything.
LEGENDS = (
    ("tempest",    tempest,    tempest_muzzle,    tempest_hit),
    ("ricochet",   ricochet,   ricochet_muzzle,   ricochet_hit),
    ("pyroclast",  pyroclast,  pyroclast_muzzle,  pyroclast_hit),
    ("permafrost", permafrost, permafrost_muzzle, permafrost_hit),
    ("sunderer",   sunderer,   sunderer_muzzle,   sunderer_hit),
    ("stasis",     stasis,     stasis_muzzle,     stasis_hit),
    ("railgun",    railgun,    railgun_muzzle,    railgun_hit),
    ("starfall",   starfall,   starfall_muzzle,   starfall_hit),
    ("wellspring", wellspring, wellspring_muzzle, wellspring_hit),
    ("eclipse",    eclipse,    eclipse_muzzle,    eclipse_hit),
)


# --------------------------------------------------------------------------- the drop


def seed_of(key, frame):
    """A frame's own seed, so `--check` reproduces it byte for byte.

    <b>Derived from the reel's name rather than counted</b>, for the reason every derived id in
    this project is: a counter makes the seventh reel's randomness depend on how many reels came
    before it, so inserting one would re-draw every reel after it and the diff would be the whole
    folder. FNV-1a over the name, which is the hash this project already uses everywhere a
    stream has to be reproduced on two runtimes (invariant 9c).
    """
    h = 2166136261
    for ch in key.encode("utf-8"):
        h = ((h ^ ch) * 16777619) & 0xFFFFFFFF
    return (h ^ (frame * 2654435761)) & 0xFFFFFFFF


#: Where a bolt's trail starts fading, what is left of it at the frame's bottom edge, and how
#: sharply it goes. See `Sheet.wane`. Shot reels only - a flash and an impact are drawn round a
#: point.
#:
#: **The curve bends the way it does because of where the trail is cut.** `SiegeView.Emerged` draws
#: the reel from its top down to wherever the shot has flown, so the *tail end* of what is drawn is
#: always parked at the barrel - and a flat cut through something wide, bright and opaque is a slab
#: lying across the turret's shoulder. A power under one fades hard immediately after the head and
#: then trails off, which is both what a comet does and what leaves nothing at the cut to see.
TRAIL_WANE, TRAIL_KEEP, TRAIL_FALL = 0.16, 0.0, 0.75


#: How big and how bright the core every muzzle flash carries at the barrel, as a share of the
#: burst frame and a gain.
#:
#: <b>It is doing two jobs and only one of them is drawing.</b> A flash *should* be brightest where
#: the barrel is - several of these were rings and fans with a hole in the middle, which is a flash
#: that looks like it happened somewhere else. And the board crops a bolt's trail at the barrel
#: (`SiegeView.Emerged`), so the flash is what covers that straight edge: measured, every reel in
#: this mode is opaque along its whole trail, so there is no cut that hides itself. A dense core
#: here is what makes `HeadRoom` small enough to be honest.
MUZZLE_CORE, MUZZLE_CORE_GAIN = 0.16, 1.15


def reel(key, draw, w, h, frames):
    """One flipbook, as {relative path: image}."""
    made = {}

    for f in range(frames):
        t = f / max(1, frames - 1)
        sheet = Sheet(w, h)
        draw(sheet, t, np.random.default_rng(seed_of(key, f)))

        # A bolt's trail ends rather than stopping - see `Sheet.wane`. Before the core, because
        # a flash has no length to fade along.
        if key.startswith("shot_"):
            sheet.wane(TRAIL_WANE, TRAIL_KEEP)

        # The core, laid over whatever the flash drew - see `MUZZLE_CORE`. It fades with the
        # flash rather than outliving it, so what a player reads is still one event.
        if key.startswith("muzzle_"):
            sheet.dot(w * .5, h * MUZZLE_Y, w * MUZZLE_CORE * (.70 + .30 * (1.0 - t)),
                      WHITE, MUZZLE_CORE_GAIN * (1.0 - t * .80), power=1.8)

        made["Fx/Siege/%s/f%02d.png" % (key, f)] = sheet.image()

    return made


def build():
    """Every PNG this tool cuts, as {relative path: image}."""
    made = {}

    for wid, shot, muzzle, hit in LEGENDS:
        made.update(reel("shot_" + wid, shot, SHOT_W, SHOT_H, SHOT_FRAMES))
        made.update(reel("muzzle_" + wid, muzzle, BURST, BURST, MUZZLE_FRAMES))
        made.update(reel("hit_" + wid, hit, BURST, BURST, HIT_FRAMES))

    return made


def raw(im):
    buffer = io.BytesIO()
    im.save(buffer, "PNG", optimize=False)
    return buffer.getvalue()


def path_of(rel):
    return REPO / "Assets" / "Game" / "Art" / rel


def write(made):
    for rel, im in sorted(made.items()):
        target = path_of(rel)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw(im))
    print("wrote %d frames under Assets/Game/Art/Fx/Siege" % len(made))


def check(made):
    missing, differ = [], []

    for rel, im in sorted(made.items()):
        target = path_of(rel)
        if not target.exists():
            missing.append(rel)
        elif target.read_bytes() != raw(im):
            differ.append(rel)

    if missing or differ:
        for rel in missing[:12]:
            print("missing: %s" % rel)
        for rel in differ[:12]:
            print("differs: %s" % rel)
        sys.exit("%d missing, %d differ - re-run with --write" % (len(missing), len(differ)))

    print("%d frames are what this tool draws" % len(made))


def report(made):
    """What `Tools/verify/fxreels.py` will say, printed here so a bad frame is caught while it is
    still being drawn rather than two gates later.

    It is a *report* and not a gate: this file cuts the art and that file judges it, exactly as
    `make_siege_art.py` prints its own figures and leaves `content.py` to refuse them.
    """
    rows = []

    for wid, _, _, _ in LEGENDS:
        for kind in ("shot", "muzzle", "hit"):
            key = "%s_%s" % (kind, wid)
            best = None

            for rel, im in made.items():
                if not rel.startswith("Fx/Siege/%s/" % key):
                    continue
                a = np.asarray(im, np.float32)[..., 3] / 255.0
                if best is None or a.sum() > best.sum():
                    best = a

            lit = best > 0.15
            ys, xs = np.nonzero(lit)
            wide = (xs.max() - xs.min() + 1) / best.shape[1] if len(xs) else 0.0
            tall = (ys.max() - ys.min() + 1) / best.shape[0] if len(ys) else 0.0
            rows.append((key, best.sum() / best.size, wide, tall))

    print("\n  ink%   box w%  box h%  reel")
    print("  " + "-" * 44)
    for key, ink, wide, tall in rows:
        flag = "  <-- thin" if ink < .01 or max(wide, tall) < .15 or min(wide, tall) < .08 else ""
        print("  %5.2f   %5.1f   %5.1f   %s%s" % (ink * 100, wide * 100, tall * 100, key, flag))


def contact(made):
    """One sheet: every reel, four frames of it, on the board's own dark ground.

    <b>The gate that matters, and the only one that can say a picture is the *right*
    picture.</b> `--check` proves reproducibility and `fxreels.py` proves there is ink; neither
    can see that a crescent reads as a smear or that two bolts are the same bolt.
    """
    cell, cols = 132, 4
    rows = len(LEGENDS) * 3
    sheet = Image.new("RGBA", (cols * cell + 150, rows * cell), (26, 30, 26, 255))

    from PIL import ImageDraw
    pen = ImageDraw.Draw(sheet)

    row = 0
    for wid, _, _, _ in LEGENDS:
        for kind in ("shot", "muzzle", "hit"):
            key = "%s_%s" % (kind, wid)
            frames = sorted(r for r in made if r.startswith("Fx/Siege/%s/" % key))
            pen.text((6, row * cell + cell // 2 - 6), key, fill=(210, 220, 210, 255))

            for i in range(cols):
                rel = frames[min(len(frames) - 1, int((i + .6) * len(frames) / cols))]
                im = made[rel].copy()
                im.thumbnail((cell - 8, cell - 8), Image.LANCZOS)
                sheet.alpha_composite(im, (150 + i * cell + (cell - im.width) // 2,
                                           row * cell + (cell - im.height) // 2))
            row += 1

    out = REPO / "Tools" / "out" / "legend_fx_contact.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print("wrote %s" % out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--report", action="store_true", help="print what fxreels.py will measure")
    args = ap.parse_args()

    made = build()

    if args.write:
        write(made)
    if args.contact:
        contact(made)
    if args.report:
        report(made)
    if args.check or not (args.write or args.contact or args.report):
        check(made)


if __name__ == "__main__":
    main()
