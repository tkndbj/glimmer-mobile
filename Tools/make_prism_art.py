# -*- coding: utf-8 -*-
"""Cuts Prismvale's floor, its gems, its lanterns, its cast and its flares out of the licensed packs.

    python Tools/make_prism_art.py --check     # prove the shipped PNGs are what this writes
    python Tools/make_prism_art.py --write
    python Tools/make_prism_art.py --contact   # a sheet, laid out at the size a phone draws it

**Why a tool at all.** Invariant 7a's argument, one step earlier: a step somebody has to remember
will be forgotten, and art nobody can re-derive is art that quietly drifts from the thing that
produced it. Removing the Iron Quarry deleted `make_quarry_art.py` because it had never been
committed, and Hollowmarch inherited twelve cast flipbooks that nothing can prove are what a tool
would cut (CLAUDE.md's owed item 18). This one is committed with its first drop, and `--check`
reproduces every byte.

**Its own folder rather than Emberforge's or the retired Kindlewake's**, though the packs are the
same. An address two chapters ask for belongs to neither (`AddressableAddresses.ChapterOwnership`),
so sharing would move the other mode's board into the global group and make it resident for the
whole session on every device (invariant 7b). It would also weld two modes together, and this
project withdraws modes often enough that being able to delete one without touching another is
worth a folder of pixels - which is exactly what happened to the mode this one replaced.

**Three rules inherited from every pack cut before this one, all still true.**

  * **Take one bounding box per animation, not per frame.** Per-frame trimming makes a character
    jitter by several pixels, because a raised arm moves the box.
  * **A box shared between a standing and a lying-down animation puts the standing one
    off-centre.** A death animation gets its own box.
  * **The `DeathFx` / `Death Sprite` folder in every character pack is not an explosion.** It
    renders as a ring of green slime. The flares come from the explosion pack.

**And two this mode adds.**

  * **A gem carries no glow of its own.** Whether a gem is lit is *state* here, not a fact about
    the picture: the view puts a halo behind it and brightens it when a vein reaches it, and takes
    both away when the vein is broken. A glow baked into the sprite would make every gem on the
    board look connected, which is the one thing the player is trying to read.
  * **The four have to differ in silhouette as well as in hue**, which is Emberforge's rule and
    decides more here than it does there: the whole verb is "is this gem the same as that one", so
    a player who cannot separate red from green has to be able to separate a heart from a rhombus.
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
OUT = REPO / "Assets" / "Game" / "Art" / "Prism"
FX = REPO / "Assets" / "Game" / "Art" / "Fx" / "Prism"

#: Where the licensed CraftPix packs live. Nothing in the repo records it, which is why the tool
#: passes when the folder is absent: a checkout without the packs still runs the gate.
SOURCE = Path(r"C:\Users\Digikey\Downloads\craftpix-assets")

#: One cell of the field, in pixels. The same figure Emberforge's board sprites use, and it is an
#: import-cap decision rather than a drawing one - `ArtImportRules.Caps` gives this folder 512, and
#: a texture costs its dimensions rather than its file size.
TILE = 192

#: How tall a cast frame is drawn. The width follows the animation's own box.
CAST = 180

#: Frames kept from an animation. The packs ship 10 to 40; a flipbook on a phone at 12 fps has
#: nothing to do with more than this, and every extra frame is a texture.
FRAMES = 12

MATCH3 = "craftpix-net-298179-match-3-game-asset-set.zip"
BLASTS = "craftpix-517297-explosions-sprite.zip"
CRITTERS = "craftpix-net-154190-monster-v2-character-sprites.zip"

# --------------------------------------------------------------------------- the field

#: The four gems: which jewel each colour is cut from, and what that colour is *called*.
#:
#: Index order is `PrismLayout.Gems` - r, g, b, y - and it is contract, because the board's letters
#: and the view's `Tint` both index off it.
#:
#: **Four distinct silhouettes**, for the reason at the top of this file: a heart, a full-square
#: cabochon, a round brilliant and a tall emerald cut. That is the difference a player who cannot
#: separate two hues has to be able to use, and it is why the choice is written down rather than
#: taken as whichever four the pack happened to ship first.
GEMS = {
    "gem_r": ("PNG/8.png", "a red heart"),
    "gem_g": ("PNG/5.png", "a green cabochon"),
    "gem_b": ("PNG/7.png", "a blue round brilliant"),
    "gem_y": ("PNG/6.png", "an amber emerald-cut"),
}

#: The two flares, and which of the pack's seven each is cut from. Hue is rotated where a flare has
#: to read as a different *kind* of thing rather than as a bigger one.
#:
#: `flare_bloom` is the one a waking critter gets and it is deliberately the more violent of the
#: two: waking a critter is the only thing on this board that finishes anything, so it gets the
#: biggest drawing in the mode (invariant 20m's first rule).
FLARES = {
    "flare_warm": ("4", 0.0, 1.0),
    "flare_bloom": ("7", 0.76, 1.15),
}

#: Every cast flipbook: the pack, and the folder inside it.
#:
#: **The three critters come from one pack on purpose** - waking one is the mode's payoff, and
#: three creatures drawn by three hands read as three games. They are a different three from
#: Emberforge's, because these are the ones asleep in the grove rather than the ones the raiders
#: carried off, and because an address owned by one chapter's scope is never re-claimed by another.
#:
#: There is deliberately **no `bolt` and no `collector`**: this mode ships without a story band
#: (see `PrismScreen`), and an address the manifest does not name is what `AddressableAudit` calls
#: dead weight.
CAST_SET = {
    "mon1": (CRITTERS, "PNG/Monster 1/Idle"),
    "mon1_jump": (CRITTERS, "PNG/Monster 1/Jump"),
    "mon2": (CRITTERS, "PNG/Monster 2/Idle"),
    "mon2_jump": (CRITTERS, "PNG/Monster 2/Jump"),
    "mon3": (CRITTERS, "PNG/Monster 3/Idle"),
    "mon3_jump": (CRITTERS, "PNG/Monster 3/Jump"),
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

    Per-frame trimming makes a character jitter by several pixels, because a raised arm moves the
    box. This is the fix and it is the first thing anybody cutting these packs gets wrong.
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

    Used only on the flares, so that two read as two *kinds* of thing rather than as one thing at
    two sizes - invariant 20m's first rule about the event being the reward, applied to the
    drawing rather than to the rule.
    """
    if turns == 0.0 and saturate == 1.0:
        return im

    a = np.asarray(im).astype(np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3:]

    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    span = mx - mn

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


def glow(side, radius, colour, strength=1.0, power=2.0):
    """A soft radial disc, for the light a lantern gives off and for the lit floor tile."""
    y, x = np.mgrid[0:side, 0:side]
    c = (side - 1) / 2.0
    d = np.sqrt((x - c) ** 2 + (y - c) ** 2) / max(1.0, radius)

    a = np.clip(1.0 - d, 0.0, 1.0) ** power * strength
    rgba = np.zeros((side, side, 4), np.float32)
    rgba[..., 0] = colour[0]
    rgba[..., 1] = colour[1]
    rgba[..., 2] = colour[2]
    rgba[..., 3] = a * 255.0
    return Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA")


# --------------------------------------------------------------------------- the pieces


def husk():
    """A sleeping critter's cradle: a thick warm bowl with the light of something asleep in it.

    **Drawn rather than approximated out of the pack's scenery** (invariant 32b), and this exact
    drawing is the second attempt at it, which is the part worth keeping. The first was a ring of
    dark bark with a dim glow behind it, and on a contact sheet at the size a phone draws it the
    thing every level of this mode is *for* read as a hole - the least legible object on the board.
    That is Emberforge's cage and the Iron Quarry's before it, and the fix is the same one every
    time: a goal is composed to win a legibility fight rather than approximated and hoped over.

    So the bowl is *filled* as well as rimmed - a warm face the critter is drawn on top of, bright
    against a floor the whole mode keeps dark, and light in value where every gem is saturated in
    hue. It carries no hue of its own, because the ring the view breathes around it has to be the
    only colour on the object.
    """
    from PIL import ImageDraw

    pod = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))

    # The light coming out of it, wide and soft, so the object has a presence on the floor before
    # its own edges are read at all.
    pod.alpha_composite(glow(TILE, TILE * 0.54, (250, 220, 158), 0.62, power=1.4))

    shell = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(shell)

    bark = (96, 72, 44, 255)
    rim = (228, 198, 138, 255)
    face = (156, 122, 74, 235)
    inset = int(TILE * 0.07)

    # A filled bowl rather than a ring. The fill is what the critter is drawn against, and it is
    # the whole difference between "a cradle with something in it" and "a hole in the floor".
    d.ellipse([inset, inset, TILE - inset, TILE - inset], fill=face, outline=bark,
              width=int(TILE * 0.085))

    # The rim light along the bottom two-thirds, which is what makes it read as a bowl holding
    # something rather than as a disc lying flat.
    d.arc([inset, inset, TILE - inset, TILE - inset], 150, 390, fill=rim,
          width=int(TILE * 0.062))

    # Four ribs, which is what tells a cradle apart from a plate at the size a thumb covers it.
    for i in range(4):
        a0 = 30 + i * 90
        d.arc([int(TILE * 0.17), int(TILE * 0.17), int(TILE * 0.83), int(TILE * 0.83)],
              a0, a0 + 32, fill=rim, width=int(TILE * 0.042))

    pod.alpha_composite(shell)
    return pod


def lamp(alight):
    """A lantern: a burning core with rays coming off it.

    **Drawn rather than cut, drawn as a star, and drawn in white** - three decisions, and each of
    them fixes something.

    *Cut*: there is nothing in the jewel pack that reads as a *source* rather than as another gem,
    and a lantern that reads as a gem is the one confusion this board cannot afford - a gem moves
    and a lantern never does.

    *A star*: the first attempt was a glass drum in an iron cage, and a hooped circle with an
    upright through it draws as a **crosshair** at the size a phone renders a cell. A radiating
    shape cannot be mistaken for anything but light, which is the whole job, and it is the one
    silhouette on this board that is neither a faceted stone nor a round bowl.

    *White*: the view tints this by the lantern's own colour, so the sprite carries structure and
    value and no hue at all, which is what lets four lanterns be four colours out of one picture.

    `alight` is the difference between a lantern feeding a vein and one feeding nothing, which is
    this mode's one free readout - a route that is not open yet, said with a sprite rather than a
    sentence.
    """
    import math

    from PIL import ImageDraw

    out = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))

    if alight:
        out.alpha_composite(glow(TILE, TILE * 0.62, (255, 250, 232), 0.75, power=1.5))

    body = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(body)

    ray = (255, 255, 255, 240) if alight else (118, 126, 134, 210)
    core = (255, 255, 255, 255) if alight else (150, 158, 166, 255)
    ring = (255, 250, 235, 255) if alight else (96, 104, 112, 255)

    c = TILE / 2.0

    # Eight rays, long and short alternating, drawn as triangles out of the centre. A star with
    # only long points reads as a sparkle; alternating is what reads as a lamp burning.
    for i in range(8):
        angle = math.pi * i / 4.0
        reach = TILE * (0.47 if i % 2 == 0 else 0.34)
        width = TILE * (0.085 if i % 2 == 0 else 0.062)

        tip = (c + math.cos(angle) * reach, c + math.sin(angle) * reach)
        left = (c + math.cos(angle + math.pi / 2) * width,
                c + math.sin(angle + math.pi / 2) * width)
        right = (c + math.cos(angle - math.pi / 2) * width,
                 c + math.sin(angle - math.pi / 2) * width)

        d.polygon([tip, left, right], fill=ray)

    # The core, and a rim around it so the middle of the star is an object rather than a blur.
    inner = TILE * 0.185
    d.ellipse([c - inner, c - inner, c + inner, c + inner], fill=core, outline=ring,
              width=int(TILE * 0.032))

    out.alpha_composite(body)
    return out


def floor_tiles(z):
    """The grove floor, and its lit twin."""
    made = {}

    back = read(z, "PNG/stone background.png").resize((TILE, TILE), Image.LANCZOS)

    # Drawn under every cell and never taken away. Dark and cooled toward the grove's green,
    # because everything standing on it is a bright saturated shape and this is what makes them
    # read (CRAFT.md's plate rule) - and because this mode is played in a grove the light has gone
    # out of.
    made["moss"] = shade(back, 0.60, -6)

    # Its lit twin, which the board wears only when it is finished. Cool rather than brighter, so
    # a lit field reads as light coming back rather than as a selection.
    lit = shade(back, 1.00, 10)
    lit.alpha_composite(glow(TILE, TILE * 0.86, (150, 230, 180), 0.42, power=1.4))
    made["moss_lit"] = lit

    made["husk"] = husk()
    made["lamp"] = lamp(True)
    made["lamp_dark"] = lamp(False)

    for key, (src, _) in GEMS.items():
        # **No glow.** Whether a gem is lit is state, and the view says so with a halo it can take
        # away again; a glow baked in here would make every gem on the board look connected, which
        # is the one thing the player is trying to read.
        made[key] = shade(fit(read(z, src), TILE, 0.86), 1.12, 6)

    return made


def flare_frames(z, folder, turns, saturate):
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


def cast_frames(z, folder):
    names = sorted(n for n in z.namelist()
                   if n.startswith(folder + "/") and n.lower().endswith(".png"))
    if not names:
        return []

    # Evenly spaced rather than the first N, or a 35-frame animation would ship as its opening
    # flinch and never reach the part that says what it is.
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
    """Every PNG this mode ships, as {relative path: image}. None when the packs are absent."""
    match3, blasts = zipped(MATCH3), zipped(BLASTS)
    if match3 is None or blasts is None:
        return None

    made = {}

    for key, im in floor_tiles(match3).items():
        made["Prism/%s.png" % key] = im

    for key, (folder, turns, saturate) in FLARES.items():
        for i, im in enumerate(flare_frames(blasts, folder, turns, saturate)):
            made["Fx/Prism/%s/f%02d.png" % (key, i)] = im

    packs = {}
    for key, (pack, folder) in CAST_SET.items():
        if pack not in packs:
            packs[pack] = zipped(pack)
        z = packs[pack]
        if z is None:
            continue
        for i, im in enumerate(cast_frames(z, folder)):
            made["Prism/%s/f%02d.png" % (key, i)] = im

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
    print("wrote %d files under Assets/Game/Art/Prism and Art/Fx/Prism" % len(made))


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
    """A sheet at the size a phone really draws these, because a check proves reproducibility and
    says nothing about quality. Look at it."""
    from PIL import ImageDraw

    board = [k for k in sorted(made) if k.startswith("Prism/") and k.count("/") == 1]
    reels = sorted({k.split("/")[1] for k in made
                    if k.startswith("Prism/") and k.count("/") == 2})
    flares = sorted({k.split("/")[2] for k in made if k.startswith("Fx/Prism/")})

    cell = 132
    cols = max(len(board), 9)
    rows = 1 + len(reels) + len(flares)

    sheet = Image.new("RGBA", (cols * cell, rows * (cell + 18) + 24), (12, 18, 16, 255))
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
        frames = sorted(k for k in made if k.startswith("Prism/%s/" % name))
        for i, key in enumerate(frames[:cols]):
            put(made[key], i, row, name if i == 0 else "")
        row += 1

    for name in flares:
        frames = sorted(k for k in made if k.startswith("Fx/Prism/%s/" % name))
        for i, key in enumerate(frames[:cols]):
            put(made[key], i, row, name if i == 0 else "")
        row += 1

    out = REPO / "Tools" / "prism_contact.png"
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
        # Passing when the packs are absent is deliberate: a checkout without them still runs the
        # gate, exactly as `make_village_art.py` does.
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
