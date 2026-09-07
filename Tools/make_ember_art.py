# -*- coding: utf-8 -*-
"""Cuts Emberforge's wall, its fittings, its cast and its explosions out of the licensed packs.

    python Tools/make_ember_art.py --check     # prove the shipped PNGs are what this writes
    python Tools/make_ember_art.py --write
    python Tools/make_ember_art.py --contact   # a sheet, laid out at the size a phone draws it

**Why a tool at all.** Invariant 7a's argument, one step earlier: a step somebody has to
remember will be forgotten, and art nobody can re-derive is art that quietly drifts from the
thing that produced it. Removing the Iron Quarry deleted `make_quarry_art.py` because it had
never been committed, and Hollowmarch has inherited twelve cast flipbooks that nothing can
prove are what a tool would cut (CLAUDE.md's owed item 18). This one is committed with its
first drop, and `--check` reproduces every byte.

**Three rules learnt cutting the packs before this one, all of them still true.**

  * **Take one bounding box per animation, not per frame.** Per-frame trimming makes a
    character jitter by several pixels, because a raised arm moves the box.
  * **A box shared between a standing and a lying-down animation puts the standing one
    off-centre.** A death animation gets its own box.
  * **The `DeathFx` / `Death Sprite` folder in every character pack is not an explosion.** It
    renders as a ring of green slime. The explosions come from the explosion pack.

**And one this mode added: a keyed-out object whose edge *is* a glow cannot be flood-keyed.**
Nothing here is keyed at all - the match-3 set ships on transparency already, and the wall's
own fittings are composed out of pieces of it rather than approximated out of scenery. That is
invariant 32b: a goal has to be the most legible thing on the board, and no numeric gate can
tell you it is not.
"""
from __future__ import annotations

import argparse
import io
import os
import sys
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Art" / "Ember"
FX = REPO / "Assets" / "Game" / "Art" / "Fx" / "Ember"

#: Where the licensed CraftPix packs live. Nothing in the repo records it, which is why the
#: tool passes when the folder is absent: a checkout without the packs still runs the gate.
SOURCE = Path(r"C:\Users\Digikey\Downloads\craftpix-assets")

#: One cell of the wall, in pixels. The same figure Hollowmarch's board sprites use, and it is
#: an import-cap decision rather than a drawing one - `ArtImportRules.Caps` gives this folder
#: 512, and a texture costs its dimensions rather than its file size.
TILE = 192

#: How tall a cast frame is drawn. The width follows the animation's own box.
CAST = 180

#: Frames kept from an animation. The packs ship 10 to 40; a flipbook on a phone at 12 fps has
#: nothing to do with more than this, and every extra frame is a texture.
FRAMES = 12

MATCH3 = "craftpix-net-298179-match-3-game-asset-set.zip"
BLASTS = "craftpix-517297-explosions-sprite.zip"
ROBOTS = "craftpix-net-310523-3-robot-character-sprite-set.zip"
CRITTERS = "craftpix-net-205925-monster-v3-character-sprites.zip"
ALIENS = "craftpix-net-515480-alien-v4-character-sprites.zip"


# --------------------------------------------------------------------------- the wall

#: Which jewel each shard colour is cut from, and what it is.
#:
#: **Four distinct silhouettes as well as four distinct hues**, which is CRAFT.md's rule about
#: the board's vocabulary applied to a mode whose whole verb is "are these three the same": a
#: player who cannot separate red from green has to be able to separate a heart from a circle.
SHARDS = {
    "shard_r": ("PNG/8.png", "a red heart"),
    "shard_g": ("PNG/5.png", "a green cabochon"),
    "shard_b": ("PNG/7.png", "a blue rhombus"),
    "shard_y": ("PNG/6.png", "an amber emerald-cut"),
}

#: The five explosions, and which of the pack's seven each is cut from. Hue is rotated where a
#: blast has to read as a different *kind* of thing rather than as a bigger one.
BLAST_SET = {
    "boom_fire": ("2", 0.0, 1.0),
    "boom_gold": ("4", 0.0, 1.0),
    "boom_blue": ("5", 0.52, 1.15),
    "boom_violet": ("7", 0.76, 1.05),
    "boom_smoke": ("3", 0.0, 1.0),
}

#: Every cast flipbook: the pack, the folder inside it, and how fast it is meant to read.
#:
#: The three critters come from one pack on purpose - a rescue is the mode's payoff and three
#: creatures drawn by three hands read as three games. The robots come from the one pack in the
#: folder with real **Hit** and **Die** animations, which is what a warden needs and what the
#: `DeathFx` folders emphatically are not.
#:
#: **The warden is a rolling armadillo and that is why it was chosen.** Its idle is coiled and
#: its `Hit_Step3` is the same machine uncoiled on its legs, so "the plating cracked" is drawn
#: by the character itself rather than by a tint - the one thing a board can show for itself
#: about a rule it would otherwise have to be told (invariant 20g).
CAST_SET = {
    "mon1": (CRITTERS, "PNG/Monster 2/Idle"),
    "mon1_jump": (CRITTERS, "PNG/Monster 2/Jump"),
    "mon2": (CRITTERS, "PNG/Monster 1/Idle"),
    "mon2_jump": (CRITTERS, "PNG/Monster 1/Jump"),
    "mon3": (CRITTERS, "PNG/Monster 4/Idle"),
    "mon3_jump": (CRITTERS, "PNG/Monster 4/Jump"),

    "warden": (ROBOTS, "PNG/Character3/Idle"),
    "warden_hit": (ROBOTS, "PNG/Character3/Hit_Step3"),
    "warden_dead": (ROBOTS, "PNG/Character3/Die"),

    "bolt": (ROBOTS, "PNG/Character1/Idle"),
    "collector": (ALIENS, "PNG/Alien05/Idle"),
}


# --------------------------------------------------------------------------- helpers


def zipped(name):
    path = SOURCE / name
    if not path.exists():
        return None
    return zipfile.ZipFile(path)


def read(z, name):
    return Image.open(io.BytesIO(z.read(name))).convert("RGBA")


def box_of(images):
    """One bounding box over a whole animation.

    Per-frame trimming makes a character jitter by several pixels, because a raised arm moves
    the box. This is the fix and it is the first thing anybody cutting these packs gets wrong.
    """
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
    im = im.resize((max(1, int(im.width * ratio)), max(1, int(im.height * ratio))),
                   Image.LANCZOS)

    out = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    out.alpha_composite(im, ((side - im.width) // 2, (side - im.height) // 2))
    return out


def spin(im, turns, saturate=1.0):
    """Rotates every hue by `turns` and scales saturation. Alpha is untouched.

    Used only on the explosions, so that five blasts read as five *kinds* of thing rather than
    as one thing at five sizes - which is invariant 20m's first rule about the event being the
    reward, applied to the drawing rather than to the rule.
    """
    if turns == 0.0 and saturate == 1.0:
        return im

    a = np.asarray(im).astype(np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3:]

    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    span = mx - mn

    # Hue, in turns. The three-way branch is the textbook conversion written out rather than
    # reached through colorsys, because that one is per-pixel Python and this is 9 frames of
    # half a megapixel.
    hue = np.zeros_like(mx)
    safe = span > 1e-6
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]

    with np.errstate(invalid="ignore", divide="ignore"):
        hue = np.where(safe & (mx == r), ((g - b) / np.where(safe, span, 1.0)) % 6.0, hue)
        hue = np.where(safe & (mx == g), ((b - r) / np.where(safe, span, 1.0)) + 2.0, hue)
        hue = np.where(safe & (mx == b), ((r - g) / np.where(safe, span, 1.0)) + 4.0, hue)

    hue = (hue / 6.0 + turns) % 1.0
    sat = np.where(mx > 1e-6, np.clip(span / np.where(mx > 1e-6, mx, 1.0) * saturate, 0, 1), 0)
    val = mx

    i = np.floor(hue * 6.0)
    f = hue * 6.0 - i
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


def shade(im, mul, add=0):
    """Multiplies the colour of a picture and lifts it, leaving alpha alone."""
    a = np.asarray(im).astype(np.float32)
    a[..., :3] = np.clip(a[..., :3] * mul + add, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def glow(side, radius, colour, strength=1.0):
    """A soft radial disc, for the light an ember gives off and for the lit wall tile."""
    y, x = np.mgrid[0:side, 0:side]
    c = (side - 1) / 2.0
    d = np.sqrt((x - c) ** 2 + (y - c) ** 2) / max(1.0, radius)

    a = np.clip(1.0 - d, 0.0, 1.0) ** 2.0 * strength
    rgba = np.zeros((side, side, 4), np.float32)
    rgba[..., 0] = colour[0]
    rgba[..., 1] = colour[1]
    rgba[..., 2] = colour[2]
    rgba[..., 3] = a * 255.0
    return Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA")


# --------------------------------------------------------------------------- the pieces

def spark(side, at, radius):
    """A hot point of light, for the lit end of a fuse."""
    y, x = np.mgrid[0:side, 0:side]
    d = np.sqrt((x - at[0]) ** 2 + (y - at[1]) ** 2) / max(1.0, radius)

    a = np.clip(1.0 - d, 0.0, 1.0) ** 1.6
    rgba = np.zeros((side, side, 4), np.float32)
    rgba[..., 0] = 255
    rgba[..., 1] = 244
    rgba[..., 2] = 206
    rgba[..., 3] = a * 255.0
    return Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA")


def bars(lock):
    """An iron cage: a warm light, four bars, a rail top and bottom, and a padlock on the join.

    Drawn rather than cut, because the cage is what every level of this mode is *for* and
    invariant 32b is unambiguous about what happens to a goal approximated out of scenery: it
    comes to look like dressing, and no numeric gate can tell you it has.
    """
    from PIL import ImageDraw

    cage = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))

    # The light behind it, so whatever is standing in the cage reads through the bars.
    cage.alpha_composite(glow(TILE, TILE * 0.46, (255, 186, 84), 0.62))

    iron = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(iron)

    dark, lit = (44, 50, 62, 255), (128, 142, 162, 255)
    inset, thick = int(TILE * 0.06), int(TILE * 0.055)

    # Four uprights, evenly spaced across the opening. Four rather than five because a bar wide
    # enough to read at this size and a gap wide enough to see a critter through do not both fit
    # more times than that.
    for i in range(4):
        x = inset + int((TILE - inset * 2 - thick) * i / 3.0)
        d.rounded_rectangle([x, inset, x + thick, TILE - inset], radius=thick // 2, fill=dark)
        d.rounded_rectangle([x + thick // 4, inset, x + thick // 2, TILE - inset],
                            radius=thick // 4, fill=lit)

    rail = int(TILE * 0.075)
    for y in (inset, TILE - inset - rail):
        d.rounded_rectangle([inset - thick // 2, y, TILE - inset + thick // 2, y + rail],
                            radius=rail // 2, fill=dark)
        d.rounded_rectangle([inset - thick // 2, y + rail // 5,
                             TILE - inset + thick // 2, y + rail // 2],
                            radius=rail // 5, fill=lit)

    cage.alpha_composite(iron)

    # The lock sits low and centred, on the join a beam is about to break.
    cage.alpha_composite(lock.transform(lock.size, Image.AFFINE,
                                        (1, 0, 0, 0, 1, -TILE * 0.24), Image.BILINEAR))
    return cage




def wall_pieces(z):
    """The eleven sprites that draw a whole wall."""
    made = {}

    back = read(z, "PNG/stone background.png").resize((TILE, TILE), Image.LANCZOS)

    # The backing is drawn under every cell and never taken away, so a breach is a hole with
    # the hull behind it rather than a gap in the picture. Dark, because everything standing on
    # it is a bright saturated shape and this is what makes them read (CRAFT.md's plate rule).
    made["wall"] = shade(back, 0.62, -6)

    # And its lit twin, which is the aim preview: holding an ember lights the cells its cross
    # would reach. Warm rather than brighter, so a lit row reads as heat and not as selection.
    lit = shade(back, 1.05, 10)
    lit.alpha_composite(glow(TILE, TILE * 0.78, (255, 150, 60), 0.30))
    made["wall_lit"] = lit

    made["stone"] = fit(read(z, "PNG/stone.png"), TILE, 0.98)
    made["frost"] = fit(read(z, "PNG/ice.png"), TILE, 0.96)

    for key, (src, _) in SHARDS.items():
        made[key] = fit(read(z, src), TILE, 0.86)

    # The ember: the pack's bomb, with the fuse lit. The glow goes *under* it so the object
    # keeps its own outline - a glow composited on top turns a black sphere into a grey one.
    #
    # **A render is why it is this bright.** The first cut was the bomb at 0.80 of a cell over a
    # narrow glow, and on a dark wall at the size a phone draws it, the one thing on the board
    # that is *tapped* rather than dragged was also the dimmest thing on it. Nothing but its own
    # drawing says an ember wants to be touched.
    ember = glow(TILE, TILE * 0.66, (255, 122, 30), 1.0)
    ember.alpha_composite(glow(TILE, TILE * 0.36, (255, 208, 116), 0.85))
    # The body is lifted as well as lit: the pack's bomb is very nearly black, and a black
    # object on a dark wall is the dimmest thing on a board where it has to be the brightest.
    ember.alpha_composite(shade(fit(read(z, "PNG/bomb.png"), TILE, 0.74), 1.55, 26))
    ember.alpha_composite(spark(TILE, (TILE * 0.63, TILE * 0.19), TILE * 0.20))
    made["ember"] = ember

    # A cage. **Drawn rather than approximated out of the pack's scenery** (invariant 32b): the
    # first cut was the stone frame with both chains and the padlock over it, and at the size a
    # phone draws it that read as a tiny lock over a mess - in a mode whose entire goal is the
    # thing behind those bars. It is the one object on the wall that has to win a legibility
    # fight against four jewels, so its bars are as thick as they can be while still showing
    # what is behind them, and it carries its own warm light so a critter reads through it.
    made["cage"] = bars(fit(read(z, "PNG/padlock.png"), TILE, 0.30))

    # A warden's plating: the same frame in bright steel, so armour and a cage read as the same
    # material bolted on by the same people.
    #
    # **Lifted hard, and a render is why.** The first cut shaded it *down* and it disappeared into
    # the dark backing tile - so a warden read as a robot standing on the wall with nothing on it,
    # in a mode where the one thing a player has to know about a warden is that it is armoured and
    # costs two beams. A rule the board cannot show is a rule that has to be told (invariant 20g),
    # and this one is drawable.
    made["plate"] = shade(fit(read(z, "PNG/frame stone.png"), TILE, 1.04), 1.45, 58)

    return made


def blast_frames(z, folder, turns, saturate):
    names = sorted(n for n in z.namelist()
                   if n.startswith("png/%s/" % folder) and n.lower().endswith(".png"))

    frames = [read(z, n) for n in names]
    box = box_of(frames)

    out = []
    for im in frames:
        if box:
            im = im.crop(box)
        im = im.resize((TILE, TILE), Image.LANCZOS)
        out.append(spin(im, turns, saturate))
    return out


def cast_frames(z, folder, key):
    names = sorted(n for n in z.namelist()
                   if n.startswith(folder + "/") and n.lower().endswith(".png"))
    if not names:
        return []

    # Evenly spaced rather than the first N, or a 35-frame death animation would ship as its
    # opening flinch and never reach the part where the thing falls over.
    if len(names) > FRAMES:
        step = len(names) / float(FRAMES)
        names = [names[min(len(names) - 1, int(i * step))] for i in range(FRAMES)]

    frames = [read(z, n) for n in names]

    # One box for the whole animation - see box_of. A death animation gets its own box because
    # it is drawn lying down, and sharing one with the idle puts the standing pose off-centre.
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
    if match3 is None or blasts is None:
        return None

    made = {}

    for key, im in wall_pieces(match3).items():
        made["Ember/%s.png" % key] = im

    for key, (folder, turns, saturate) in BLAST_SET.items():
        for i, im in enumerate(blast_frames(blasts, folder, turns, saturate)):
            made["Fx/Ember/%s/f%02d.png" % (key, i)] = im

    packs = {}
    for key, (pack, folder) in CAST_SET.items():
        if pack not in packs:
            packs[pack] = zipped(pack)
        z = packs[pack]
        if z is None:
            continue
        for i, im in enumerate(cast_frames(z, folder, key)):
            made["Ember/%s/f%02d.png" % (key, i)] = im

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
    print("wrote %d files under Assets/Game/Art/Ember and Art/Fx/Ember" % len(made))


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
    """A sheet at the size a phone really draws these, because a check proves reproducibility
    and says nothing about quality. Look at it."""
    from PIL import ImageDraw

    board = [k for k in sorted(made) if k.startswith("Ember/") and k.count("/") == 1]
    reels = sorted({k.split("/")[1] for k in made if k.startswith("Ember/") and k.count("/") == 2})
    booms = sorted({k.split("/")[2] for k in made if k.startswith("Fx/Ember/")})

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
        put(made[key], i, 0, key.split("/")[-1][:-4])

    row = 1
    for name in reels:
        frames = sorted(k for k in made if k.startswith("Ember/%s/" % name))
        for i, key in enumerate(frames[:cols]):
            put(made[key], i, row, name if i == 0 else "")
        row += 1

    for name in booms:
        frames = sorted(k for k in made if k.startswith("Fx/Ember/%s/" % name))
        for i, key in enumerate(frames[:cols]):
            put(made[key], i, row, name if i == 0 else "")
        row += 1

    out = REPO / "Tools" / "ember_contact.png"
    sheet.convert("RGB").save(out)
    print("wrote %s" % out)


def main():
    global SOURCE

    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--source", default=str(SOURCE))
    args = ap.parse_args()

    SOURCE = Path(args.source)

    made = build()

    if made is None:
        # Passing when the packs are absent is deliberate: a checkout without them still runs
        # the gate, exactly as `make_village_art.py` does.
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
