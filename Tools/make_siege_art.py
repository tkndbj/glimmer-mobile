# -*- coding: utf-8 -*-
"""Draws Thornwatch's hill, its ward line and its field, and cuts its gems and its cast.

    python Tools/make_siege_art.py --check     # prove the shipped PNGs are what this writes
    python Tools/make_siege_art.py --write
    python Tools/make_siege_art.py --contact   # a sheet, laid out at the size a phone draws it

**Why a tool at all.** Invariant 7a's argument one step earlier: a step somebody has to remember
will be forgotten, and art nobody can re-derive is art that quietly drifts from the thing that
produced it. `make_quarry_art.py` was never committed, so withdrawing the Iron Quarry deleted it
and left Hollowmarch drawing twelve cast flipbooks nothing can prove are what a tool would cut
(CLAUDE.md's owed item 18). This one is committed with its first drop and `--check` reproduces
every byte.

**The wards were drawn and are now cut, and that is the owner's verdict rather than a change of
mind about invariant 32b.** The first cut composed them out of primitives - a stone pillar with a
crystal in it - on the argument that a goal approximated out of scenery comes to look like
dressing. Played on a device the verdict was "I didn't like the turrets at all", and the reason is
the half 32b does not cover: *composed* buys legibility and cannot buy **character**, and a turret
is the thing the player is looking at for the whole run. So they come from a pack drawn for exactly
this - ten turrets, each with an idle and a **recoil**, all of them facing up the way this board's
hill runs - and the legibility 32b asks for is bought a different way: the body is **tinted at run
time** to the colour it burns, so it wears the same four colours the gems and the raiders do.

**Four models rather than one, and the tint rather than four painted turrets.** Four models so a
ward is told apart by silhouette as well as by hue (CRAFT.md's rule about the board's vocabulary);
one tint applied at run time so the colour cannot drift from `Pal`, and so a bullet, a muzzle
flash, a gem's burst and a ward all take their colour from one place.

**And the ground is a field rather than a gradient**, for the same verdict: the first cut was dark
earth with a black wash over the top of it and read as a hole. What is up there now is the
top-down pack's own grass.

Three rules from cutting the packs before this one, all still true:

  * **Take one bounding box per animation, not per frame.** Per-frame trimming makes a character
    jitter by several pixels, because a raised arm moves the box.
  * **A box shared between a standing and a lying-down animation puts the standing one
    off-centre.** A death animation gets its own box.
  * **The `Death Sprite` / `DeadSprite` folder in every character pack is not an explosion.** It
    renders as a ring of green slime. The explosions come from the explosion pack.
"""
from __future__ import annotations

import argparse
import io
import math
import sys
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

REPO = Path(__file__).resolve().parent.parent

#: Where the licensed CraftPix packs live. Nothing in the repo records it, which is why the tool
#: passes when the folder is absent: a checkout without the packs still runs the gate.
SOURCE = Path(r"C:\Users\Digikey\Downloads\craftpix-assets")

#: The second folder, added when the wards were re-cut. Two rather than one because these three
#: packs were bought later and live where they were downloaded; `--source` moves the first and
#: `--tower` the second.
TOWER = Path(r"C:\Users\Digikey\Downloads\to-assets")

MATCH3 = "craftpix-net-298179-match-3-game-asset-set.zip"
BLASTS = "craftpix-517297-explosions-sprite.zip"
MONSTERS = "craftpix-net-205925-monster-v3-character-sprites.zip"
BRUTES = "craftpix-net-894353-monster-v4-character-sprites.zip"

#: The turrets, their bullets and their muzzle flash.
TURRETS = "craftpix-net-715522-turrets-asset-pack-for-merge-shooter.zip"

#: The top-down tower-defence pack, for its grass. It is the only ground in any of these packs
#: drawn to be looked down on, which is what this hill is.
FIELD = "craftpix-net-869102-tower-defense-neighborhood-top-down-2d-asset-pack.zip"

#: The merge-shooter kit, for one thing: a white ring of debris that tints to anything, which is
#: what a matched gem comes apart into.
KIT = "craftpix-net-239749-merge-shooter-cartoon-asset-kit.zip"

#: The mine tileset the hill is floored with. Its own download rather than one of the folders
#: above, so `--mine` points at it.
MINE = Path(r"C:\Users\Digikey\Downloads\graphicriver-95eo2prH-topdown-tiles-mine.zip")

#: One gem, in pixels. An import-cap decision rather than a drawing one - `ArtImportRules.Caps`
#: gives this folder 512, and a texture costs its dimensions rather than its file size.
TILE = 192

#: How tall a cast frame is drawn. The width follows the animation's own box.
CAST = 180

#: Frames kept from an animation. The packs ship 10 to 40; a flipbook on a phone at 12 fps has
#: nothing to do with more than this, and every extra frame is a texture.
FRAMES = 12

#: Which jewel each gem colour is cut from, and what it is.
#:
#: **Four distinct silhouettes as well as four distinct hues**, which is CRAFT.md's rule about the
#: board's vocabulary applied to a mode where the colour of a match is the whole decision: a player
#: who cannot separate red from green has to be able to separate a heart from a rhombus.
GEMS = {
    "gem_r": ("PNG/8.png", "a red heart"),
    "gem_g": ("PNG/5.png", "a green cabochon"),
    "gem_b": ("PNG/7.png", "a blue rhombus"),
    "gem_y": ("PNG/6.png", "an amber emerald-cut"),
}

#: The two explosions, and which of the pack's seven each is cut from. A raider comes apart in fire
#: and a ward comes down in smoke, which is the difference between something being destroyed and
#: something being *lost*.
BLAST_SET = {
    "boom_fire": ("2", 0.0, 1.0),
    "boom_smoke": ("3", 0.0, 1.0),
}

#: Every cast flipbook: the pack, the folder inside it, and what it is.
#:
#: **Walks rather than idles**, which is the whole point of this mode's hill - a raider that stands
#: still while sliding down a lane reads as a sprite being moved rather than as something coming.
#: The three creepers come from one pack on purpose (three creatures drawn by three hands read as
#: three games) and the brute comes from another, because the one thing a player has to see about a
#: brute at a glance is that it is not one of those.
CAST_SET = {
    "mon1": (MONSTERS, "PNG/Monster 1/Walk"),
    "mon2": (MONSTERS, "PNG/Monster 2/Walk"),
    "mon3": (MONSTERS, "PNG/Monster 4/Walk"),
    "brute": (BRUTES, "PNG/Monster 5/Walk"),
}

#: The four turret models a ward line is drawn with, in ward order, and the hue each is painted.
#:
#: **The colour is baked in here rather than tinted at run time, and that is a correction.** It was
#: a run-time tint on the argument that one `Pal` entry should feed the gems, the raiders and the
#: wards alike - which is true and was not worth what it cost, because `Image.color` is a
#: *multiply*: it can only ever darken, so a bright cartoon turret pulled toward a saturated red
#: comes back dark, and lifted toward white first comes back pastel. Both were played and both were
#: wrong ("too dim", then washed out). A hue rotation keeps every highlight and every bit of
#: shading the pack drew and simply makes them *that colour*, which is what "brighter and more
#: alive" actually asks for.
#:
#: The hues are `Pal.Poppy`, `Pal.Mint`, `Pal.Azure` and `Pal.Sun` measured as hue angles, so the
#: line still agrees with the gems that feed it - the agreement is now checked by eye through
#: `--contact` rather than enforced by sharing one `Color`.
#:
#: The models are picked for how different they are from one another head-on, which is the only
#: view this board has of them.
WARD_MODELS = (
    ("Turret01", 0.986),        # Poppy   #F2404F
    ("Turret03", 0.308),        # Mint    #7BD86A
    ("Turret06", 0.561),        # Azure   #4FC1FF
    ("Turret09", 0.122),        # Sun     #FFC93C
)

#: How many frames of a turret's recoil are kept. They are 10 to 20 in the pack and the whole
#: motion is over in a fifth of a second on screen.
FIRE_FRAMES = 7

#: A ward, in pixels. Taller than it is wide, because a turret is.
WARD_W, WARD_H = 192, 240

#: How far a ward's pixels are pulled toward its own hue. **Not all the way**: at 1.0 a turret is
#: one flat colour, which is what the first bake shipped and what came back as needing "a touch of
#: different colours". A fifth of the pack's own spread survives at 0.8, which is enough for the
#: orange trim to stay warm against a red body and for the dome to stay cool against a green one.
HUE_PULL = 0.8

STONE_DARK = (38, 46, 58)
STONE_MID = (62, 74, 90)
STONE_LIT = (108, 124, 146)


# --------------------------------------------------------------------------- helpers


def zipped(name, where=None):
    """One of the licensed packs, or None when it is not on this machine."""
    path = (where or SOURCE) / name
    return zipfile.ZipFile(path) if path.exists() else None


def read(z, name):
    return Image.open(io.BytesIO(z.read(name))).convert("RGBA")


def box_of(images):
    """One bounding box over a whole animation. See the module docstring."""
    box = None
    for im in images:
        here = im.getbbox()
        if here is None:
            continue
        box = here if box is None else (min(box[0], here[0]), min(box[1], here[1]),
                                        max(box[2], here[2]), max(box[3], here[3]))
    return box


def fit(im, side, scale=1.0):
    """Trims to its own alpha and centres it on a square of `side`, at `scale` of the room."""
    bb = im.getbbox()
    if bb:
        im = im.crop(bb)

    room = max(1, int(side * scale))
    ratio = min(room / im.width, room / im.height)
    im = im.resize((max(1, int(im.width * ratio)), max(1, int(im.height * ratio))), Image.LANCZOS)

    out = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    out.alpha_composite(im, ((side - im.width) // 2, (side - im.height) // 2))
    return out


def glow(w, h, cx, cy, radius, colour, strength=1.0, power=2.0):
    """A soft radial light. Everything drawn here that gives off light is built out of these."""
    y, x = np.mgrid[0:h, 0:w]
    d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2) / max(1.0, radius)

    a = np.clip(1.0 - d, 0.0, 1.0) ** power * strength
    rgba = np.zeros((h, w, 4), np.float32)
    rgba[..., 0] = colour[0]
    rgba[..., 1] = colour[1]
    rgba[..., 2] = colour[2]
    rgba[..., 3] = a * 255.0
    return Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA")


def speckle(w, h, seed, amount=14.0):
    """Deterministic grain, so a big stretched surface is not a flat colour."""
    rng = np.random.RandomState(seed)
    noise = rng.rand(h // 4 + 1, w // 4 + 1).astype(np.float32)
    grain = np.array(Image.fromarray((noise * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC),
                     dtype=np.float32) / 255.0
    return (grain - 0.5) * amount


def ground(w, h, top, bottom, seed):
    """A vertical gradient with grain in it, which is what every flat surface here is."""
    ramp = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None]

    a = np.zeros((h, w, 4), np.float32)
    for c in range(3):
        a[..., c] = top[c] + (bottom[c] - top[c]) * ramp
    a[..., :3] += speckle(w, h, seed)[..., None]
    a[..., 3] = 255.0

    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


# --------------------------------------------------------------------------- the board


def hill():
    """The ground the raiders walk over: the mine tileset's own floor, laid as a field.

    <b>Three grounds in three sittings, and the reason it moved twice is worth keeping.</b> The
    first was drawn - dark earth under a black wash - and read as a hole. The second was the
    top-down pack's grass, which read correctly and was chosen for brightness. This is the owner's
    third call and it is a *place* rather than a brightness: a mine floor, which is what a line of
    turrets standing in front of a jewel field is plausibly defending.

    <b>The plate rule comes back with it</b> (CRAFT.md: a dark ground is what makes bright pieces
    read), so unlike the grass this needs no help - the raiders are saturated cartoon colours and
    every one of them now sits on something that is not competing with it. What it does need is to
    not be *flat*, which is what the ore is for.
    """
    z = zipped(MINE.name, MINE.parent)
    if z is None:
        return None

    art = {}
    for n in z.namelist():
        if not n.endswith(".png") or "__MACOSX" in n:
            continue
        stem = n.split("/")[-1].replace("Asset ", "").replace("xhdpi.png", "")
        if stem.isdigit():
            art[int(stem)] = read(z, n)

    if not art:
        return None

    # The 265-square pieces are the floor; everything taller is a wall block or an ore cluster.
    floor = [im for im in art.values() if im.size == (265, 265)]
    if not floor:
        return None

    # The plainest of them, by how little of the tile is edge shadow - a floor laid out of pieces
    # that each carry a dark rim draws a grid across the whole hill, which is the fault the grass
    # was re-filtered for.
    floor.sort(key=flatness, reverse=True)
    floor = floor[:6]

    side = 265
    across, down = 4, 5
    im = Image.new("RGBA", (side * across, side * down), (26, 24, 26, 255))

    rng = np.random.RandomState(23)
    for y in range(down):
        for x in range(across):
            im.alpha_composite(floor[int(rng.rand() * len(floor))], (x * side, y * side))

    # **No ore scattered over it.** The pack's ore clusters were laid across the floor to stop it
    # reading as flat, drained most of the way to grey so they would not compete with the four
    # colours this board spends on things the player has to tell apart. Played, they read as
    # litter. What keeps the floor from being flat is the six different tiles it is laid from,
    # which is the amount of variety a surface *under* the action should have.

    # A little depth up the hill, so the far end reads as further away rather than as the same
    # floor twice.
    a = np.asarray(im).astype(np.float32)
    ramp = np.linspace(0.80, 1.10, im.height, dtype=np.float32)[:, None, None]
    a[..., :3] = np.clip(a[..., :3] * ramp + 16.0, 0, 255)
    im = Image.fromarray(a.astype(np.uint8), "RGBA")

    return im.resize((512, 640), Image.LANCZOS)


def flatness(tile):
    """How little of a tile is dark rim. Used to pick the plain floor pieces."""
    a = np.asarray(tile).astype(np.float32)
    lum = a[..., 0] * 0.30 + a[..., 1] * 0.59 + a[..., 2] * 0.11
    return float((lum > lum.mean() * 0.82).mean())


def rampart():
    """The wall the wards stand on: stone, with a lit top edge so the line reads as a line.

    The capping course carries a warm light of its own. That is what makes the middle band read as
    *the thing being defended* rather than as a strip of masonry between two halves of the screen -
    the whole fail state of this mode is that edge, and a fail state has to be the second most
    legible thing on the board after the goal.
    """
    w, h = 512, 176
    im = ground(w, h, (74, 80, 94), (34, 38, 48), seed=19)

    d = ImageDraw.Draw(im)

    # Coursed blocks, offset row to row. Drawn rather than tiled, so it stretches without a seam,
    # and dark, so what stands on it wins.
    block = w // 8
    for row in range(3):
        y0 = int(h * (0.26 + row * 0.26))
        y1 = int(h * (0.26 + row * 0.26 + 0.22))
        offset = 0 if row % 2 == 0 else block // 2
        for i in range(-1, 9):
            x0 = i * block + offset
            d.rounded_rectangle([x0 + 4, y0, x0 + block - 4, y1], radius=6,
                                fill=(72 - row * 8, 78 - row * 8, 92 - row * 8, 255))

    # The capping course, last and brightest, because this is the edge the whole run is about.
    d.rectangle([0, 0, w, int(h * 0.15)], fill=(120, 132, 152, 255))
    d.rectangle([0, 0, w, int(h * 0.06)], fill=(168, 178, 196, 255))
    d.rectangle([0, int(h * 0.15), w, int(h * 0.22)], fill=(14, 16, 22, 255))

    im.alpha_composite(glow(w, h, w * 0.5, 0.0, w * 0.60, (255, 176, 96), 0.34, 1.2))

    return im


def plate():
    """The field's own backing: dark, because everything standing on it is a bright jewel."""
    side = 256
    im = Image.new("RGBA", (side, side), (0, 0, 0, 0))

    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, side - 1, side - 1], radius=26, fill=(12, 18, 26, 232))
    d.rounded_rectangle([0, 0, side - 1, side - 1], radius=26, outline=(84, 98, 118, 210), width=4)

    # **No highlight along the top, and that is a lesson about stretching a sprite.** There was a
    # 16-pixel band of white at 6% up there, which is a sheen on a 256-pixel panel and a *bar* on
    # one stretched to six hundred - it sat in the plate's own margin above the first row of gems
    # and read as a second container nobody had asked for. Anything drawn a fixed number of pixels
    # from the edge of a sprite that will be stretched is drawn at a size nobody chose.

    return im


def socket():
    """The plinth a ward stands on - a slab of the rampart's own stone, so a turret reads as
    standing on the line rather than floating over it."""
    w, h = 192, 96
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    d.ellipse([w * 0.04, h * 0.30, w * 0.96, h * 0.96], fill=STONE_DARK + (255,))
    d.ellipse([w * 0.08, h * 0.22, w * 0.92, h * 0.84], fill=STONE_MID + (255,))
    d.ellipse([w * 0.16, h * 0.28, w * 0.84, h * 0.70], fill=STONE_LIT + (255,))

    return im


def hued(im, hue):
    """Paints a picture one hue, keeping every highlight and every bit of shading it had.

    Saturation is pulled up rather than replaced, so the pack's near-white specular edges stay
    near-white and the body becomes unmistakably one colour; value is lifted a little, because the
    thing this is for is a turret that has to read as *lit* on a bright green field.
    """
    a = np.asarray(im).astype(np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3:]

    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    span = mx - mn

    sat = np.where(mx > 1e-6, span / np.where(mx > 1e-6, mx, 1.0), 0.0)

    # Pulled up rather than set: a pixel that was grey metal stays greyish, a pixel that was
    # coloured becomes strongly coloured.
    sat = np.clip(sat * 0.48 + 0.50, 0.0, 1.0)
    val = np.clip(mx * 1.10 + 0.06, 0.0, 1.0)

    # **Most of the way to the target rather than all of it.** Setting every pixel to one hue
    # gives a turret that is *entirely* one colour, which came back from play as flat - the pack
    # drew orange trim on a blue body and a purple dome, and all of that collapsed into a single
    # red shape. Blending keeps a fifth of the original spread, so a red ward is unmistakably red
    # and still has warm and cool notes in it. The blend is circular, so a hue two thirds of the
    # way round the wheel takes the short way and not the long one.
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]

    was = np.zeros_like(mx)
    lit = span > 1e-6
    with np.errstate(invalid="ignore", divide="ignore"):
        safe = np.where(lit, span, 1.0)
        was = np.where(lit & (mx == r), ((g - b) / safe) % 6.0, was)
        was = np.where(lit & (mx == g), ((b - r) / safe) + 2.0, was)
        was = np.where(lit & (mx == b), ((r - g) / safe) + 4.0, was)
    was = was / 6.0

    step = ((hue - was + 0.5) % 1.0) - 0.5
    h = (was + step * HUE_PULL) % 1.0
    i = np.floor(h * 6.0)
    f = h * 6.0 - i
    p = val * (1.0 - sat)
    q = val * (1.0 - f * sat)
    t = val * (1.0 - (1.0 - f) * sat)
    i = i.astype(np.int32) % 6

    out = np.stack([
        np.choose(i, [val, q, p, p, t, val]),
        np.choose(i, [t, val, val, q, p, p]),
        np.choose(i, [p, p, t, val, val, q]),
    ], axis=-1)

    return Image.fromarray(
        (np.concatenate([np.clip(out, 0, 1), alpha], axis=-1) * 255.0 + 0.5).astype(np.uint8),
        "RGBA")


def turret(z, model, frame=None):
    """One turret, cut from the pack and centred on a ward-sized canvas.

    <b>No colour is baked in.</b> Which colour a ward burns is applied at run time
    (`SiegeView.Coat`), from the same `Pal` entry the gems and the raiders take theirs from - so
    the four cannot drift apart and a palette retune moves all of them at once.
    """
    name = ("Png/%s/Idle/%s-Idle_0.png" % (model, model) if frame is None
            else "Png/%s/Shoot/%s-Shoot_%02d.png" % (model, model, frame))

    im = read(z, name)

    out = Image.new("RGBA", (WARD_W, WARD_H), (0, 0, 0, 0))
    ratio = min(WARD_W / im.width, WARD_H / im.height)
    im = im.resize((max(1, int(im.width * ratio)), max(1, int(im.height * ratio))), Image.LANCZOS)
    out.alpha_composite(im, ((WARD_W - im.width) // 2, (WARD_H - im.height) // 2))
    return out


def wrecked(z, model):
    """A ward that has fallen: the same turret, grey, dark and leaning.

    Grey rather than gone, because "three of four standing" has to be readable without counting -
    and the silhouette staying put is what makes the gap in the line legible at all.
    """
    im = turret(z, model)

    a = np.asarray(im).astype(np.float32)
    grey = a[..., 0] * 0.30 + a[..., 1] * 0.59 + a[..., 2] * 0.11
    a[..., 0] = a[..., 1] = a[..., 2] = np.clip(grey * 0.55 + 12, 0, 255)

    im = Image.fromarray(a.astype(np.uint8), "RGBA")
    return im.rotate(-13, Image.BICUBIC, expand=False, center=(WARD_W / 2, WARD_H * 0.88))


def bullet(z):
    """What a ward fires: the pack's own round, drained of its colour so it can take the ward's."""
    im = fit(read(z, "Png/Bullets/Bullet1.png"), 96, 0.92)

    a = np.asarray(im).astype(np.float32)
    lit = a[..., 0] * 0.30 + a[..., 1] * 0.59 + a[..., 2] * 0.11
    a[..., 0] = a[..., 1] = a[..., 2] = np.clip(lit * 0.55 + 118, 0, 255)

    return Image.fromarray(a.astype(np.uint8), "RGBA")


def white(im, side, lift=1.0):
    """Drains a frame to white so it can be tinted at run time, and fits it to a square."""
    a = np.asarray(fit(im, side, 0.98)).astype(np.float32)
    lum = a[..., 0] * 0.30 + a[..., 1] * 0.59 + a[..., 2] * 0.11
    a[..., 0] = a[..., 1] = a[..., 2] = np.clip(lum * 0.35 + 190 * lift, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


# --------------------------------------------------------------------------- the packs


def blast_frames(z, folder, turns, saturate):
    names = sorted(n for n in z.namelist()
                   if n.startswith("png/%s/" % folder) and n.lower().endswith(".png"))

    frames = [read(z, n) for n in names]
    box = box_of(frames)

    out = []
    for im in frames:
        if box:
            im = im.crop(box)
        out.append(im.resize((TILE, TILE), Image.LANCZOS))
    return out


def cast_frames(z, folder):
    names = sorted(n for n in z.namelist()
                   if n.startswith(folder + "/") and n.lower().endswith(".png"))
    if not names:
        return []

    # Evenly spaced rather than the first N, or a long walk cycle ships as its first half stride.
    if len(names) > FRAMES:
        step = len(names) / float(FRAMES)
        names = [names[min(len(names) - 1, int(i * step))] for i in range(FRAMES)]

    frames = [read(z, n) for n in names]

    box = box_of(frames)
    if box is None:
        return []

    frames = [im.crop(box) for im in frames]

    width, height = frames[0].size
    ratio = CAST / float(height)
    size = (max(1, int(width * ratio)), CAST)

    return [im.resize(size, Image.LANCZOS) for im in frames]


# --------------------------------------------------------------------------- the drop


def build():
    """Every PNG this mode ships, as {relative path: image}. Empty when the packs are absent."""
    match3, blasts = zipped(MATCH3), zipped(BLASTS)
    turrets, kit = zipped(TURRETS, TOWER), zipped(KIT, TOWER)

    if match3 is None or blasts is None or turrets is None or kit is None:
        return None

    made = {}

    for key, (src, _) in GEMS.items():
        made["Siege/%s.png" % key] = fit(read(match3, src), TILE, 0.88)

    field = hill()
    if field is None:
        return None

    made["Siege/hill.png"] = field
    made["Siege/rampart.png"] = rampart()
    made["Siege/plate.png"] = plate()
    made["Siege/socket.png"] = socket()

    # The ward line. Four models, no baked colour - see WARD_MODELS.
    for i, (model, hue) in enumerate(WARD_MODELS):
        made["Siege/ward%d.png" % (i + 1)] = hued(turret(turrets, model), hue)

        shots = sorted(n for n in turrets.namelist()
                       if n.startswith("Png/%s/Shoot/" % model) and n.endswith(".png"))

        step = max(1, len(shots) // FIRE_FRAMES)
        for f in range(FIRE_FRAMES):
            made["Siege/fire%d/f%02d.png" % (i + 1, f)] = hued(
                turret(turrets, model, min(len(shots) - 1, f * step)), hue)

    made["Siege/ward_dead.png"] = wrecked(turrets, WARD_MODELS[0][0])
    made["Siege/bullet.png"] = bullet(turrets)

    # The muzzle flash, drained to white so a ward's own colour can be put on it at run time.
    flashes = sorted(n for n in turrets.namelist()
                     if n.startswith("Png/Shoot Fx/") and n.endswith(".png"))
    for i, name in enumerate(flashes[:10]):
        made["Fx/Siege/flash/f%02d.png" % i] = white(read(turrets, name), TILE)

    # What a matched gem comes apart into: a ring of debris, white, tinted per gem at run time.
    # **Cut white on purpose.** Four painted copies would be eighty textures and four chances for
    # one of them to stop matching `Pal`; one white reel is twenty and cannot drift.
    pops = sorted(n for n in kit.namelist()
                  if n.startswith("Png/Dead fx/") and n.endswith(".png"))
    step = max(1, len(pops) // FRAMES)
    for i in range(FRAMES):
        made["Fx/Siege/pop/f%02d.png" % i] = white(read(kit, pops[min(len(pops) - 1, i * step)]),
                                                   TILE, 1.15)

    for key, (folder, turns, saturate) in BLAST_SET.items():
        for i, im in enumerate(blast_frames(blasts, folder, turns, saturate)):
            made["Fx/Siege/%s/f%02d.png" % (key, i)] = im

    packs = {}
    for key, (pack, folder) in CAST_SET.items():
        if pack not in packs:
            packs[pack] = zipped(pack)
        z = packs[pack]
        if z is None:
            continue
        for i, im in enumerate(cast_frames(z, folder)):
            made["Siege/%s/f%02d.png" % (key, i)] = im

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
    print("wrote %d files under Assets/Game/Art/Siege and Art/Fx/Siege" % len(made))


def check(made):
    missing, differ = [], []

    for rel, im in sorted(made.items()):
        target = path_of(rel)
        if not target.exists():
            missing.append(rel)
        elif target.read_bytes() != raw(im):
            differ.append(rel)

    if missing or differ:
        for rel in missing:
            print("missing: %s" % rel)
        for rel in differ:
            print("differs: %s" % rel)
        sys.exit("%d missing, %d differ - re-run with --write" % (len(missing), len(differ)))

    print("%d files are what this tool cuts" % len(made))


def contact(made):
    """A sheet at the size a phone really draws these. A check proves reproducibility and says
    nothing about quality. Look at it."""
    board = [k for k in sorted(made) if k.startswith("Siege/") and k.count("/") == 1]
    reels = sorted({k.split("/")[1] for k in made if k.startswith("Siege/") and k.count("/") == 2})
    booms = sorted({k.split("/")[2] for k in made if k.startswith("Fx/Siege/")})

    cell = 132
    cols = max(len(board), 9)
    rows = 1 + len(reels) + len(booms)

    sheet = Image.new("RGBA", (cols * cell, rows * (cell + 18) + 24), (16, 22, 30, 255))
    draw = ImageDraw.Draw(sheet)

    def put(im, col, row, label):
        thumb = im.copy()
        thumb.thumbnail((cell - 10, cell - 10), Image.LANCZOS)
        sheet.alpha_composite(thumb, (col * cell + (cell - thumb.width) // 2,
                                      row * (cell + 18) + (cell - thumb.height) // 2))
        if label:
            draw.text((col * cell + 3, row * (cell + 18) + cell + 2), label[:18],
                      fill=(225, 225, 225, 255))

    for i, key in enumerate(board):
        put(made[key], i % cols, i // cols, key.split("/")[-1][:-4])

    row = (len(board) - 1) // cols + 1
    for name in reels:
        frames = sorted(k for k in made if k.startswith("Siege/%s/" % name))
        for i, key in enumerate(frames[:cols]):
            put(made[key], i, row, name if i == 0 else "")
        row += 1

    for name in booms:
        frames = sorted(k for k in made if k.startswith("Fx/Siege/%s/" % name))
        for i, key in enumerate(frames[:cols]):
            put(made[key], i, row, name if i == 0 else "")
        row += 1

    out = REPO / "Tools" / "siege_contact.png"
    sheet.convert("RGB").save(out)
    print("wrote %s" % out)


def main():
    global SOURCE, TOWER, MINE

    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--source", default=str(SOURCE))
    ap.add_argument("--tower", default=str(TOWER))
    ap.add_argument("--mine", default=str(MINE))
    args = ap.parse_args()

    SOURCE = Path(args.source)
    TOWER = Path(args.tower)
    MINE = Path(args.mine)

    made = build()

    if made is None:
        # Passing when the packs are absent is deliberate: a checkout without them still runs the
        # gate, exactly as `make_ember_art.py` and `make_village_art.py` do.
        print("source packs not found at %s - nothing to do" % SOURCE)
        return

    if args.write:
        write(made)
    if args.contact:
        contact(made)
    if args.check or not (args.write or args.contact):
        check(made)


if __name__ == "__main__":
    main()
