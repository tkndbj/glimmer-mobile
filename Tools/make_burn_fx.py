# -*- coding: utf-8 -*-
"""Draws the flame a raider wears while an ember turret's fire is on it: four looping reels.

    python Tools/make_burn_fx.py --write     # cut them
    python Tools/make_burn_fx.py --check     # prove the shipped frames are what this draws
    python Tools/make_burn_fx.py --contact   # one sheet, to look at
    python Tools/make_burn_fx.py --report    # what Tools/verify/fxreels.py will measure
    python Tools/make_burn_fx.py --strip     # one reel end to end, every frame, to judge the loop

**What this is for.** Three turrets on the shelf - `ember`, `pyre` and the legendary
`pyroclast` - set what they hit alight, and the burn ticks away for three to six seconds
afterwards (`WardAbility.Ember`, `SiegeBoard.Smoulder`). Until now *nothing drew that*: the model
reported each tick as an ordinary `SiegeBolt`, so the view answered it the only way it knows how -
a recoil, a muzzle flash, a comet crossing the hill and an impact - about thirty times a second
out of one barrel. The ability read as a machine gun and the damage-over-time it actually is was
invisible. The rules were never wrong; the drawing was.

**Why it is drawn here rather than baked.** `make_legend_fx.py` argues this at length and every
word of it applies, but fire has one reason of its own: **this reel loops, and nothing else in
this mode does.** Every bought effect in the projectile pack is an *event* - a flash, a flight, an
impact - played once and thrown away, and a rasterised particle system has no reason for its last
frame to lead back into its first. A burn stands on a raider for six seconds, which is five times
round; a seam anywhere in it is a stutter the player sees five times. Looping is a property of how
the thing is *drawn*, so it has to be drawn by something that knows it has to loop.

**How the loop is made, which is the whole craft of this file.**

* **Nothing is random per frame.** `make_legend_fx.py` seeds a fresh generator for every frame,
  which is exactly right for lightning - what says *lightning* is that the next frame is
  unrelated. It is exactly wrong for fire: uncorrelated noise between frames is a strobe, not a
  flicker. Every number here is a smooth function of a phase, and the only "randomness" is
  `spread`, which mixes *once per element* and then holds still for the whole reel.
* **Every element is periodic in `t` with period one.** A tongue is born at the base, rises,
  narrows and goes out; tongue `k` runs on `u = (t + k/n) mod 1`, so as `t` passes 1 the ensemble
  is exactly where it started. `t` is `f / FRAMES` and **not** `f / (FRAMES - 1)`: the last frame
  has to be the one *before* the first, or the loop repeats a frame and hitches once a cycle.
* **The tongues are not identical.** If they were, the picture at `t + 1/n` would equal the
  picture at `t` and a twenty-four frame reel would hold `n` copies of the same eight - which
  reads as a pulse rather than as fire. Each tongue carries its own seat, sway, speed and heat out
  of `spread`, so the reel's only period is the whole of it.

**Colour, and why there are four of these rather than one white one worn in four hues.** A ward's
fire says which of the player's four seats is paying for it, and a burn is the one thing in this
mode that is on the screen long enough to be read at leisure. `Image.color` is a multiply
(invariant 37l, 44g), so a bleached reel can only ever be *darkened* toward a hue - and what makes
fire read is not its value but the run of hues *across its own body*: a saturated, cooling tip
over a hot middle over a white core. That gradient cannot survive a multiply, and the roster paid
for learning it once already on `WardModel.ShotFor`. So each of the four is drawn in its own three
colours, anchored on the tint `SiegeView.Tints` paints that ward in.

**The frame is the board's and is not a choice.** `SiegeView.Blaze` sizes the widget by its
*height* against the raider's and takes the width off the reel's own aspect (`SiegeView.Frame`),
so the aspect here is what decides how broad the fire sits on a body. The base of the fire is at
`BASE_Y` measured down the frame, and the widget is anchored so that lands on the raider's feet.
"""
from __future__ import annotations

import argparse
import io
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

# **One additive kit for the whole project, rather than a second copy of it here.** `Sheet` is an
# energy buffer with a soft-dot primitive and one conversion to pixels - the conversion being the
# part worth not having twice, because it is what makes an overlap go *white* instead of clipping
# to a saturated blob, and a second spelling of it would be a second answer to what "hot" looks
# like. Imported rather than duplicated, and both tools carry a `--check`, so a change to that kit
# that moved these reels fails a gate on the same run.
sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_legend_fx import Sheet, ease                                  # noqa: E402

REPO = Path(__file__).resolve().parent.parent

# --------------------------------------------------------------------------- the board's numbers

#: How wide and tall a flame's frame is cut.
#:
#: <b>Wider than it is tall, and it took two wrong cuts and a render each to get here.</b>
#: `SiegeView.Ablaze` sizes this against the **width** of the body it is on, and every raider in
#: this mode is a top-down insect drawn far wider than it is tall - a `mon` reel is about 300 x
#: 180. Cut at 160 x 224 the fire came out a candle standing on a beetle four times its width;
#: cut square it came out a bonfire. Both were right in every number and plainly wrong in the one
#: picture (`render_ward_preview.py --alight`), which is invariant 32b twice in a row.
#:
#: <b>What a wide frame buys is the thing a single flame cannot be: a *cluster*.</b> A burning
#: body in this genre is three or four fires of different sizes along it, out of step with each
#: other - see `CLUSTERS`. One flame stretched to a body's width is a camp fire; three at
#: different scales is something burning.
W, H = 272, 200

#: How many frames one loop holds, and the rate the board plays them at.
#:
#: <b>Twenty-four at twenty a second, so the loop is 1.2 s.</b> Short enough that the eye reads
#: flicker rather than a cycle, long enough that a tongue has time to be born, rise and go out
#: inside it. The shortest burn on the shelf is three seconds, so even the first rung of the
#: family plays this two and a half times: a seam would be seen.
FRAMES, FPS = 24, 20.0

#: Where the fire stands in its own frame, as shares measured **down** from the top: the foot of
#: the flame, and the highest a tongue may reach.
#:
#: <b>The foot is not the frame's bottom edge and must not be.</b> The under-glow and the bed
#: spread *below* the seat of the fire, and a seat on the edge would have both cut off square - a
#: straight line under a flame, which is the one thing that says "this is a sprite" (invariant
#: 37k's sliver, met from the other side).
BASE_Y, TIP_Y = 0.90, 0.06

#: How much of a flame's height its foot fades in over. See `blade`.
FOOT_FADE = 0.07

#: The middle of the frame, in pixels. Every cluster is drawn about its own offset from it.
CX = W * 0.5

#: The fires along a burning body, as (offset from the middle as a share of the frame's width,
#: how big against the tallest one, how far round the loop it runs).
#:
#: <b>Three of different sizes and out of phase, which is what a body on fire looks like and a
#: single flame never can.</b> The offsets are not symmetrical and the scales are not a series:
#: two fires of one size either side of a third is a candelabra. The phases are spread so that
#: the tall one is never guttering at the same moment as both short ones, which would read as the
#: whole effect blinking.
#:
#: <b>The phases must be rational fractions of the loop</b>, like everything else here: an offset
#: that does not divide into one turns a seamless reel into one that very nearly joins, which
#: reads as a fault rather than as a cycle.
#: <b>They overlap, and the first cut of them did not.</b> Spaced so their skirts cleared each
#: other, three fires along a body read as three camp fires standing in a row - which the contact
#: sheet said in one glance. They are set close enough that the masses merge at the foot and part
#: again at the tips, so the silhouette is one mound with three peaks in it.
CLUSTERS = (
    (-0.168, 0.80, 0.375),
    (+0.008, 1.00, 0.000),
    (+0.176, 0.89, 0.625),
)

#: How many tongues rise at once, and how many wisps break off above them.
#:
#: <b>Five and seven, which are coprime with nothing in particular and with each other</b> - two
#: families of the same count would drift into step and read as one.
TONGUES, WISPS = 6, 8

#: A deterministic, well-mixed number in [0, 1) for element `k`, parameter `i`.
#:
#: <b>An irrational stride rather than a seeded generator, and that is the loop's own
#: requirement.</b> `make_legend_fx.py` reaches for `np.random.default_rng(seed_of(key, frame))`,
#: which hands every frame an unrelated draw - right for lightning, a strobe for fire. What is
#: wanted here is a *character* per tongue that then holds perfectly still while the tongue lives
#: its life, and the golden ratio's own property is that `k phi mod 1` spreads any number of
#: elements about as evenly as they can be spread. It is also the one shape that cannot go stale:
#: raising `TONGUES` re-mixes nothing that was already there.
GOLD, SILVER = 0.6180339887498949, 0.3183098861837907


def spread(k, i=0):
    return ((k + 1) * GOLD + i * SILVER) % 1.0


def mix(a, b, t):
    """Nought to one along a colour."""
    return tuple(a[j] + (b[j] - a[j]) * t for j in range(3))


def wobble(u, phase, freq=1.0):
    """A smooth, strictly periodic wander in [-1, 1].

    <b>Two sines rather than one, and the frequencies are whole numbers.</b> A single sine reads
    as a pendulum; a second one an octave and a bit above it never quite repeats inside the loop
    and reads as air moving. Both periods divide the loop exactly, which is what makes the whole
    thing seamless - an irrational frequency here would be a drawing that very nearly loops, which
    is worse than one that plainly does not, because it reads as a fault rather than as a cycle.
    """
    return (math.sin(math.tau * (u * freq + phase)) * 0.68
            + math.sin(math.tau * (u * freq * 3.0 + phase * 2.3)) * 0.32)


# --------------------------------------------------------------------------- the four fires

#: Each ward colour's fire, as three colours: the cooling tip, the body, and the heart.
#:
#: <b>Anchored on `SiegeView.Tints` and then taken *hotter toward the middle*, which is what makes
#: four differently-hued flames all read as fire.</b> Real fire is a temperature gradient - white
#: at the heart, the hue at the body, saturated and dark at the tip where it is giving up its heat
#: - so the ward's own tint is the *body*, the tip is that tint driven down and round toward its
#: own deep end, and the heart is the same tint lifted almost to white. Drawn any other way round
#: (a hue on top of a white body) a flame reads as a coloured smear, which is precisely what
#: `WardModel.ShotFor` records about bleaching a reel and tinting it.
#:
#: The hexes each tint is taken from are named so a retune of `Pal` can be followed here by eye;
#: this tool has no palette to import, exactly as `make_siege_art.py` has none.
FIRES = (
    #  letter   tip (cooling)          body (the ward's tint)   heart (almost white)
    ("r", (0.86, 0.10, 0.16), (0.95, 0.25, 0.31), (1.00, 0.80, 0.62)),   # Pal.Poppy   #F2404F
    ("g", (0.18, 0.62, 0.20), (0.48, 0.85, 0.42), (0.88, 1.00, 0.76)),   # Pal.Mint    #7BD86A
    ("b", (0.10, 0.38, 0.90), (0.31, 0.76, 1.00), (0.80, 0.96, 1.00)),   # Pal.Azure   #4FC1FF
    ("y", (0.90, 0.32, 0.03), (1.00, 0.54, 0.17), (1.00, 0.92, 0.66)),   # Pal.Amber   #FF8A2B
)


def heat_at(depth, fire):
    """The colour of fire at `depth` down the frame: heart at the seat, tip at the top.

    <b>One ramp asked by every primitive rather than a colour chosen per part</b>, so the bed, the
    body, the tongues and the wisps cannot come to disagree about where the fire is hottest - and
    so that moving `BASE_Y` moves the whole gradient with it rather than leaving the colours where
    they were.
    """
    _, tip, body, heart = fire

    # Nought at the highest a tongue reaches, one at the seat.
    k = (depth - TIP_Y) / max(1e-6, BASE_Y - TIP_Y)
    k = min(1.0, max(0.0, k))

    # **The heart is the bottom fifth and the tip is the top half**, with the body between - which
    # is where fire actually keeps its temperatures, rather than a straight lerp from one end to
    # the other. A linear ramp drew a flame that was pink in the middle.
    if k > 0.78:
        return mix(body, heart, (k - 0.78) / 0.22)
    return mix(tip, body, min(1.0, k / 0.55))


# --------------------------------------------------------------------------- the anatomy

#: The silhouette of a flame, as half-width against height: narrow at the seat's feet, widest a
#: quarter of the way up, tapering to a point.
#:
#: <b>A table rather than a formula, because this is the one thing here that is judged by eye.</b>
#: The first cut of this tool stamped soft dots up a spine, which is how every other effect in
#: this project is drawn and is wrong for exactly one of them: a stack of overlapping discs
#: accumulates energy fastest where it is thickest, so the mass blew out to white and what came
#: back off the contact sheet was a light bulb with wires coming out of the top. A flame is a
#: *silhouette* - what makes it read is the outline and the colour running up it, and neither of
#: those survives being approximated by circles. So the shape is written down, and filled.
#:
#: **Widest at 0.26 and not at the bottom.** Fire necks in just above its fuel, where cold air is
#: being dragged past it, and that pinch is most of what tells a flame from a plume.
FLAME = ((0.00, 0.42), (0.05, 0.68), (0.11, 0.88), (0.20, 1.00), (0.30, 0.98),
         (0.41, 0.88), (0.53, 0.73), (0.65, 0.56), (0.76, 0.39), (0.86, 0.23),
         (0.94, 0.11), (1.00, 0.00))

#: The same, for a lick running off the shoulder of the mass: fat where it leaves, and pointed.
LICK = ((0.00, 0.00), (0.08, 1.00), (0.34, 0.80), (0.62, 0.48), (0.84, 0.22), (1.00, 0.00))

#: The two profiles as arrays, built once. `np.interp` wants them and this is asked several
#: hundred times a reel.
_TABLES = {}


def along(table, s):
    """The half-width a profile gives at height `s`, for a whole column of heights at once."""
    got = _TABLES.get(id(table))
    if got is None:
        got = (np.asarray([p[0] for p in table], np.float32),
               np.asarray([p[1] for p in table], np.float32))
        _TABLES[id(table)] = got

    return np.interp(s, got[0], got[1], left=0.0, right=0.0).astype(np.float32)


def ramp(fire, depths):
    """`heat_at` for a whole column of depths at once."""
    return np.asarray([heat_at(float(d), fire) for d in depths], np.float32)


def blade(sheet, fire, table, wide, foot, head, lean, phase, gain, cx=None,
          soft=0.40, edge=1.25, freq=1.0, flare=0.0, warp=0.0, rim=0.0):
    """Fills one flame shape into the sheet's energy: a profile, swayed, coloured by height.

    <b>A field rather than a stroke, and that is the whole difference between this reel and the
    one it replaced.</b> Every pixel is asked how far it is from the flame's own spine *as a share
    of the flame's width at that height*, so the edge is exactly as soft as `edge` says wherever
    it is - a stroke made of discs is soft in proportion to the disc, so the wide part of a flame
    gets a woolly edge and the narrow part a hard one. It is also the only way the energy stays
    where it is put: nothing overlaps anything, so a layer's gain is the gain it has, and the only
    place the buffer goes past full is where two layers are deliberately laid over each other.

    <b>The two sides are asked separately</b> (`warp`), because a symmetrical flame is a leaf. One
    side is widened and the other narrowed by the same amount, so the outline wanders without the
    flame changing size.
    """
    e = sheet.e
    ss = e.shape[0] / H

    rows = (np.arange(e.shape[0], dtype=np.float32) + .5) / ss
    cols = (np.arange(e.shape[1], dtype=np.float32) + .5) / ss

    # Nought at the flame's foot, one at its head. Rows outside it are never touched, which is
    # what keeps a lick near the top from costing a sweep of the whole frame.
    span = max(1e-6, (foot - head) * H)
    s = (foot * H - rows) / span

    live = (s >= 0.0) & (s <= 1.0)
    if not live.any():
        return

    r0 = int(np.argmax(live))
    r1 = int(e.shape[0] - np.argmax(live[::-1]))
    s = s[r0:r1]

    # **Widths are shares of the frame's *height*, never its width.** The frame is wide because
    # it holds three fires; a flame's own proportions are a fact about a flame, so a wider frame
    # must not make every fire in it fatter.
    half = along(table, s) * wide * H * 0.5

    # It leans further the higher it goes - a flame is held at its fuel and free at its tip - and
    # wanders on the way with the same two-sine air `wobble` gives everything else here.
    sway = np.asarray([wobble(phase, ph, freq) for ph in s * 0.42], np.float32)
    centre = (CX if cx is None else cx) + (lean * s * s + sway * flare) * H

    skew = np.asarray([wobble(phase + .5, ph * 1.3 + .21, freq) for ph in s], np.float32) * warp

    off = cols[None, :] - centre[:, None]
    span_w = np.maximum(1e-3, half[:, None] * (1.0 + np.sign(off) * skew[:, None]))

    # **A shoulder rather than a slope, which is the second thing the contact sheet caught.** A
    # plain `(1 - d)` falloff is brightest at the spine and fades all the way to the outline, so
    # the shape has no edge at all - what came back was a gas plume. `soft` is how much of the
    # half-width the fade is allowed to occupy: inside that the flame is simply *lit*, and the
    # last third of it is the rim. A flame's outline is the thing the eye reads it by.
    d = np.abs(off) / span_w
    lit = (np.clip((1.0 - d) / soft, 0.0, 1.0) ** edge).astype(np.float32)

    # **And the foot dissolves rather than stopping.** A profile with real width at `s = 0` draws
    # a flat horizontal edge across the bottom of the flame, which is the one line in the picture
    # that says "sprite" - and it cannot be fixed in the profile, because a flame genuinely *is*
    # as wide as its fuel where it meets it. So the shape keeps its width and gives up its light.
    lit *= np.clip(s / FOOT_FADE, 0.0, 1.0)[:, None] ** 1.2

    # **And a bright band just inside the outline**, which is how every painted flame in the
    # genre is drawn and is not something a falloff can produce: the rim is hotter than the body
    # because it is the part still burning, so an edge lit brighter than what it encloses reads
    # as fire where the same shape filled flat reads as a stencil.
    if rim > 0.0:
        lit += rim * (np.clip(1.0 - np.abs(d - 0.80) / 0.22, 0.0, 1.0) ** 1.4).astype(np.float32)

    if lit.max() <= 0.0:
        return

    e[r0:r1] += lit[..., None] * ramp(fire, foot - (foot - head) * s)[:, None, :] * gain


def underglow(sheet, t, fire, cx, size):
    """The light the fire throws onto the ground it stands on.

    <b>The half that makes a burning raider look *lit* rather than stickered.</b> Everything else
    here is drawn above the seat; this is the only part that says the fire is casting light on
    something, and without it a flame reads as a decal laid over a body. It is the one part still
    drawn as a soft dot, because a pool of light is genuinely what it is.
    """
    _, _, body, _ = fire
    breath = 1.0 + 0.10 * math.sin(math.tau * (t * 2.0))

    sheet.dot(cx, H * (BASE_Y - 0.005), H * 0.44 * size * breath, body, 0.13, power=3.0)
    sheet.dot(cx, H * (BASE_Y - 0.020), H * 0.25 * size * breath, body, 0.16, power=2.4)


def mass(sheet, t, fire, cx, size):
    """The body of the fire: three nested silhouettes, cooler and wider outward.

    <b>Nested rather than blended, which is how a painted flame is built and is not how a particle
    system makes one.</b> Each layer is the same profile at its own width and its own sway, so the
    outline is a *cool* edge with a hotter one inside it and a white heart at the bottom - and
    because the three sway out of step, the inner flame visibly moves inside the outer one, which
    is the single most fire-like thing in this drawing.

    <b>The gains are set so that only the heart goes over one.</b> `Sheet.image` sends anything
    past full energy toward white, which is what makes a core read as hot; let the whole mass do
    it and the flame is a white blob with a coloured rim, which is exactly what the first cut of
    this was.
    """
    tall = (BASE_Y - TIP_Y) * size

    # The outer envelope: widest, coolest, dimmest, slowest to move.
    blade(sheet, fire, FLAME, wide=0.66 * size, foot=BASE_Y, head=BASE_Y - tall * 0.92, cx=cx,
          lean=0.082 * wobble(t, 0.11, 1.0), phase=t, gain=0.26,
          soft=0.86, edge=1.55, freq=1.0, flare=0.034, warp=0.22, rim=0.0)

    # The body, leaning the other way, so the two edges never sit parallel.
    blade(sheet, fire, FLAME, wide=0.52 * size, foot=BASE_Y - 0.005, head=BASE_Y - tall * 0.74, cx=cx,
          lean=0.105 * wobble(t, 0.63, 1.0), phase=t + 0.37, gain=0.44,
          soft=0.36, edge=1.12, freq=2.0, flare=0.048, warp=0.30, rim=0.50)

    # The heart: low, narrow, and hot enough to clip white where it lies over the two above.
    blade(sheet, fire, FLAME, wide=0.27 * size, foot=BASE_Y - 0.012, head=BASE_Y - tall * 0.27, cx=cx,
          lean=0.046 * wobble(t, 0.29, 2.0), phase=t + 0.71, gain=0.74,
          soft=0.58, edge=1.25, freq=2.0, flare=0.030, warp=0.18)


def tongue(sheet, t, k, fire, cx, size):
    """One lick of flame: born on the shoulder of the mass, rising, narrowing, going out.

    <b>This is the motion, and the reason it loops is that `u` does.</b> Tongue `k` runs a fifth
    of a cycle behind tongue `k-1`, so at any instant one is being born, two are climbing and two
    are guttering out - which is what a fire is. Its character comes out of `spread` once and then
    never changes, so the tongue being born on the last frame is the same tongue that was born on
    the first.

    <b>It leaves from inside the mass rather than from the seat</b>, which is what makes the top
    of the fire ragged instead of pointed: the mass's own profile closes to nothing, and these are
    what carry on past it.
    """
    u = (t + k / TONGUES) % 1.0

    seat = (spread(k, 0) - 0.5) * 0.30          # where across the shoulder it leaves
    sway = 0.6 + spread(k, 1)
    reach = 0.55 + 0.45 * spread(k, 2)
    freq = 1.0 + round(spread(k, 3) * 2.0)      # whole, so it stays periodic
    fat = 0.095 + 0.080 * spread(k, 5)

    # **Fast attack, long decay.** A tongue that faded in as slowly as it faded out would read as
    # a glow swelling rather than as flame being thrown off - and the attack has to be shorter
    # than one frame's worth of `u`, or the birth is never seen at all.
    life = min(1.0, u / 0.12) * (1.0 - u) ** 1.25
    if life <= 0.02:
        return

    tall = (BASE_Y - TIP_Y) * size
    foot = BASE_Y - tall * (0.16 + 0.15 * spread(k, 4))
    head = foot - tall * (0.30 + 0.60 * ease(u, 1.6) * reach)

    blade(sheet, fire, LICK, wide=fat * size * (1.0 - 0.28 * u), foot=foot,
          head=max(TIP_Y, head), cx=cx,
          lean=seat * size + 0.10 * sway * size * wobble(u, spread(k, 4), freq),
          phase=u + spread(k, 4),
          gain=0.66 * life, soft=0.46, edge=1.15, freq=freq, flare=0.055 * sway * size,
          warp=0.28, rim=0.45)


def wisp(sheet, t, k, fire, cx, size):
    """An ember that has broken off the top of the fire and is going up with the heat.

    <b>The cheapest part of this drawing and the one that sells it.</b> A flame with nothing
    coming off it is a shape; a flame shedding sparks is a thing burning. They are drawn *above*
    where the tongues give out, so what the eye reads is the fire handing them on rather than a
    second effect standing in the same place.
    """
    u = (t + spread(k, 6)) % 1.0

    born = BASE_Y - (BASE_Y - TIP_Y) * size * (0.60 + 0.24 * spread(k, 7))
    seat = (spread(k, 8) - 0.5) * H * 0.44 * size
    drift = (spread(k, 9) - 0.5) * H * 0.26
    grain = H * (0.013 + 0.016 * spread(k, 10))

    # It fades in as it separates and out as it cools, and is gone well before it reaches the top
    # of the frame - an ember that left the picture still lit would read as a seam.
    life = min(1.0, u / 0.10) * (1.0 - u) ** 1.9
    if life <= 0.01:
        return

    depth = born - (born - TIP_Y + 0.02) * ease(u, 1.35)
    x = cx + seat + drift * u + wobble(u, spread(k, 11), 2.0) * H * 0.05

    sheet.dot(x, H * depth, grain * 2.8, heat_at(depth, fire), 0.26 * life, power=1.9)
    sheet.dot(x, H * depth, grain, heat_at(min(BASE_Y, depth + 0.22), fire), 1.20 * life,
              power=1.5)


def smoke(sheet, t, fire):
    """What is left above the flame: two dim, cool smudges rolling upward.

    <b>Dim on purpose, and `--report` is what says it stays that way.</b> Smoke is the one part of
    a fire that is *not* light, and an additive buffer can only add - so this is drawn in the tip
    colour at a low gain, which reads as the flame's own haze rather than as grey. Enough to stop
    the picture ending in a hard line, and no more.
    """
    _, tip, _, _ = fire

    for k in range(3):
        u = (t + k / 3.0) % 1.0
        depth = 0.30 - 0.22 * u
        x = CX + (spread(k, 12) - 0.5) * W * 0.44 + wobble(u, 0.3 + k * 0.4, 1.0) * H * 0.10
        fade = min(1.0, u / 0.25) * (1.0 - u) ** 1.4

        sheet.dot(x, H * depth, H * (0.11 + 0.15 * u), tip, 0.11 * fade, power=2.4)


def flame(sheet, t, fire):
    """One frame of one colour's fire: three of them along a body, back to front.

    <b>Every cluster is drawn whole before the next</b> rather than layer by layer across all
    three, which is the order that reads: a fire in front of another fire has to be in front of
    all of it, and an envelope laid over a neighbour's own tongues turns the pair into one haze.
    """
    for at, size, phase in CLUSTERS:
        cx = CX + at * W
        u = (t + phase) % 1.0

        underglow(sheet, u, fire, cx, size)
        mass(sheet, u, fire, cx, size)

        for k in range(TONGUES):
            tongue(sheet, u, k, fire, cx, size)

        for k in range(WISPS):
            wisp(sheet, u, k, fire, cx, size)

    # Once for the whole body rather than once per fire: three columns of haze standing side by
    # side is a picture of three camp fires, which is what the cluster exists to stop being.
    smoke(sheet, t, fire)


# --------------------------------------------------------------------------- the reels


def build():
    """Every PNG this tool cuts, as {relative path: image}."""
    made = {}

    for fire in FIRES:
        letter = fire[0]

        for f in range(FRAMES):
            # **`f / FRAMES` and never `f / (FRAMES - 1)`.** The last frame of a loop is the one
            # *before* the first comes round again; dividing by `FRAMES - 1` makes the last frame
            # identical to the first, so the reel holds the same picture twice and hitches once a
            # cycle. Every other tool here divides by `FRAMES - 1` because every other reel is
            # played once.
            sheet = Sheet(W, H)
            flame(sheet, f / FRAMES, fire)
            made["Fx/Siege/burn_%s/f%02d.png" % (letter, f)] = sheet.image()

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
    print("the Editor has to address them: Glimmer Grove > Addressables > Sync All Assets, "
          "AND SAVE (invariant 7a) - until then a burning raider is a WHITE RECTANGLE (7b)")


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
    """What `Tools/verify/fxreels.py` will say, plus the one reading only a loop needs.

    **The seam.** That file measures ink and a lit box, which between them prove there is a
    picture; neither can see that frame 23 does not lead back into frame 0. The number printed
    here is the mean absolute difference in alpha between consecutive frames, and then the same
    across the wrap - and a loop is seamless exactly when the wrap is *no worse than the typical
    step*. A ratio near one is a reel that joins; a large one is a jump.

    It is a report and not a gate, exactly as `make_legend_fx.py --report` is: this file cuts the
    art and `fxreels.py` judges it.
    """
    print("\n  ink%   box w%  box h%   step   wrap  ratio  reel")
    print("  " + "-" * 56)

    for fire in FIRES:
        key = "burn_%s" % fire[0]
        frames = [np.asarray(made["Fx/Siege/%s/f%02d.png" % (key, f)], np.float32)[..., 3] / 255.0
                  for f in range(FRAMES)]

        best = max(frames, key=lambda a: a.sum())
        lit = best > 0.15
        ys, xs = np.nonzero(lit)
        wide = (xs.max() - xs.min() + 1) / best.shape[1] if len(xs) else 0.0
        tall = (ys.max() - ys.min() + 1) / best.shape[0] if len(ys) else 0.0

        steps = [np.abs(frames[i + 1] - frames[i]).mean() for i in range(FRAMES - 1)]
        step = float(np.mean(steps))
        wrap = float(np.abs(frames[0] - frames[-1]).mean())

        flag = ""
        if lit.mean() < .01 or max(wide, tall) < .15 or min(wide, tall) < .08:
            flag = "  <-- thin"
        elif wrap > step * 1.8:
            flag = "  <-- seam"

        print("  %5.2f   %5.1f   %5.1f  %.4f %.4f  %5.2f  %s%s"
              % (lit.mean() * 100, wide * 100, tall * 100, step, wrap,
                 wrap / max(step, 1e-9), key, flag))


def contact(made):
    """One sheet: all four fires, six frames each, on the board's own dark ground.

    <b>The gate that matters</b> (invariant 32b). `--check` proves reproducibility, `--report`
    proves the loop joins arithmetically, and neither can say whether the thing on the screen
    reads as fire.
    """
    from PIL import ImageDraw

    cell, cols = 150, 6
    sheet = Image.new("RGBA", (cols * cell + 110, len(FIRES) * cell), (26, 30, 26, 255))
    pen = ImageDraw.Draw(sheet)

    for row, fire in enumerate(FIRES):
        key = "burn_%s" % fire[0]
        pen.text((8, row * cell + cell // 2 - 6), key, fill=(210, 220, 210, 255))

        for i in range(cols):
            im = made["Fx/Siege/%s/f%02d.png" % (key, i * FRAMES // cols)].copy()
            im.thumbnail((cell - 10, cell - 10), Image.LANCZOS)
            sheet.alpha_composite(im, (110 + i * cell + (cell - im.width) // 2,
                                       row * cell + (cell - im.height) // 2))

    out = REPO / "Tools" / "out" / "burn_fx_contact.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print("wrote %s" % out)


def strip(made, letter="r"):
    """One reel, every frame in order, twice - which is the only way to *see* a seam.

    Two cycles laid end to end put the join in the middle of the strip where the eye finds it,
    rather than between the last tile and the wall.
    """
    cell = 132
    sheet = Image.new("RGBA", (FRAMES * cell // 2, cell * 4), (26, 30, 26, 255))

    for pass_ in range(2):
        for f in range(FRAMES):
            im = made["Fx/Siege/burn_%s/f%02d.png" % (letter, f)].copy()
            im.thumbnail((cell - 8, cell - 8), Image.LANCZOS)
            row = pass_ * 2 + f // (FRAMES // 2)
            col = f % (FRAMES // 2)
            sheet.alpha_composite(im, (col * cell + (cell - im.width) // 2,
                                       row * cell + (cell - im.height) // 2))

    out = REPO / "Tools" / "out" / ("burn_fx_strip_%s.png" % letter)
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print("wrote %s" % out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--report", action="store_true", help="fxreels.py's figures, and the seam")
    ap.add_argument("--strip", nargs="?", const="r", default="",
                    help="one colour's reel end to end, twice, to judge the loop")
    args = ap.parse_args()

    made = build()

    if args.write:
        write(made)
    if args.contact:
        contact(made)
    if args.strip:
        strip(made, args.strip)
    if args.report:
        report(made)
    if args.check or not (args.write or args.contact or args.report or args.strip):
        check(made)


if __name__ == "__main__":
    main()
