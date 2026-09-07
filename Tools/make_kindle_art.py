# -*- coding: utf-8 -*-
"""Cuts Kindlewake's hollow, its embers, its cast and its flares out of the licensed packs.

    python Tools/make_kindle_art.py --check     # prove the shipped PNGs are what this writes
    python Tools/make_kindle_art.py --write
    python Tools/make_kindle_art.py --contact   # a sheet, laid out at the size a phone draws it

**Why a tool at all.** Invariant 7a's argument, one step earlier: a step somebody has to remember
will be forgotten, and art nobody can re-derive is art that quietly drifts from the thing that
produced it. Removing the Iron Quarry deleted `make_quarry_art.py` because it had never been
committed, and Hollowmarch inherited twelve cast flipbooks that nothing can prove are what a tool
would cut (CLAUDE.md's owed item 18). This one is committed with its first drop, and `--check`
reproduces every byte.

**Its own folder rather than Emberforge's, though the packs are the same.** An address two chapters
ask for belongs to neither (`AddressableAddresses.ChapterOwnership`), so sharing would move
Emberforge's wall into the global group and make it resident for the whole session on every device
(invariant 7b). It would also weld the two modes together, and this project withdraws modes often
enough that being able to delete one without touching another is worth a folder of pixels.

**Three rules inherited from every pack cut before this one, all still true.**

  * **Take one bounding box per animation, not per frame.** Per-frame trimming makes a character
    jitter by several pixels, because a raised arm moves the box.
  * **A box shared between a standing and a lying-down animation puts the standing one
    off-centre.** A death animation gets its own box.
  * **The `DeathFx` / `Death Sprite` folder in every character pack is not an explosion.** It
    renders as a ring of green slime. The flares come from the explosion pack.

**And one this mode adds: an ember has to be recognisably one of three colours at the size a
finger covers it.** The whole verb is "are these two the same", so the three are separated by
*value* as well as by hue and each carries its own coloured light - a hue difference alone is a
difference only some people can see, which is `Pal`'s own rule about the energy wheel applied to
the one object on this board a finger can touch.
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
OUT = REPO / "Assets" / "Game" / "Art" / "Kindle"
FX = REPO / "Assets" / "Game" / "Art" / "Fx" / "Kindle"

#: Where the licensed CraftPix packs live. Nothing in the repo records it, which is why the tool
#: passes when the folder is absent: a checkout without the packs still runs the gate.
SOURCE = Path(r"C:\Users\Digikey\Downloads\craftpix-assets")

#: One cell of the hollow, in pixels. The same figure Emberforge's board sprites use, and it is an
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

# --------------------------------------------------------------------------- the hollow

#: The three embers: which jewel each channel is cut from, and the light it gives off.
#:
#: **`Energy`'s channel order and `Pal`'s paint**, which is the one place those two meet outside
#: `Pal.EnergyColour` itself. The middle channel is drawn yellow rather than green, so the blends
#: a player builds on this board fall out of the wheel a five-year-old knows - red and yellow make
#: orange, red and blue make purple, yellow and blue make green. The letters stay R, G and B
#: because they name masks and not paint (see `Energy.TryParse`).
#:
#: **Four distinct silhouettes were the rule in Emberforge and three distinct ones are the rule
#: here**, for the same reason: the verb is "are these two the same", so a player who cannot
#: separate red from blue has to be able to separate a heart from a rhombus.
EMBERS = {
    "ember_r": ("PNG/8.png", (242, 64, 79), "a red heart"),
    "ember_g": ("PNG/6.png", (255, 221, 87), "an amber emerald-cut"),
    "ember_b": ("PNG/7.png", (79, 193, 255), "a blue rhombus"),
}

#: The three flares, and which of the pack's seven each is cut from. Hue is rotated where a flare
#: has to read as a different *kind* of thing rather than as a bigger one.
#:
#: `flare_bloom` is the one a crossing gets and it is deliberately the most violent of the three:
#: a crossing is the only thing on this board the player had to *arrange*, so it gets the biggest
#: drawing in the mode (invariant 20m's first rule).
FLARES = {
    "flare_warm": ("4", 0.0, 1.0),
    "flare_cold": ("5", 0.52, 1.10),
    "flare_bloom": ("7", 0.76, 1.15),
}

#: Every cast flipbook: the pack, and the folder inside it.
#:
#: **The three critters come from one pack on purpose** - waking one is the mode's payoff, and
#: three creatures drawn by three hands read as three games. They are a different three from
#: Emberforge's, because these are the ones asleep in the grove rather than the ones the raiders
#: carried off, and because an address owned by one chapter's scope is never re-claimed by another.
#: **A different pack from Emberforge's, not three different folders of the same one.** The
#: smelter's captives are monster-v3 and these are monster-v2, so the two sets of critters are
#: drawn by one hand each and read as two places rather than as one cast split up. It also means
#: neither mode can be withdrawn out from under the other's art.
#:
#: There is deliberately **no `bolt` and no `collector`**: this mode ships without a story band
#: (see `KindleScreen`), and an address the manifest does not name is what `AddressableAudit`
#: calls dead weight.
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

    Used only on the flares, so that three read as three *kinds* of thing rather than as one
    thing at three sizes - invariant 20m's first rule about the event being the reward, applied
    to the drawing rather than to the rule.
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
    """A soft radial disc, for the light an ember gives off and for the lit floor tile."""
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


def husk(z):
    """A sleeping critter's cradle: a thick warm bowl of woven bark with a light inside it.

    **Drawn rather than approximated out of the pack's scenery** (invariant 32b), and drawn
    *twice*, which is the part worth recording. The first cut was a dark ring on dark moss with
    a dim brown light in it, and every numeric gate was green through it - because no gate here
    opens a PNG. A contact sheet at the size a phone draws it showed the thing every level of
    this mode is *for* as the least legible object on the board: a hole. That is Emberforge's
    cage, one mode later, and the fix is the same one - the goal is composed to win a
    legibility fight rather than approximated and hoped over.

    So it is bright where the hollow is dark, warm where the embers are saturated, and open at
    the top so the critter asleep in it reads through. The coloured ring saying *what* it wants
    is the view's (`KindleView.Wants`) and is drawn over this - which is why the bowl itself
    carries no hue of its own to fight with it.
    """
    from PIL import ImageDraw

    pod = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))

    # The light inside, so whatever is asleep in there reads at all on a near-black floor.
    # Warm, wide and well above the moss in value: this is the object that has to be seen.
    pod.alpha_composite(glow(TILE, TILE * 0.46, (236, 206, 146), 0.72, power=1.5))

    shell = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(shell)

    bark = (124, 98, 64, 255)
    lit = (206, 176, 118, 255)
    inset = int(TILE * 0.06)

    # A bowl rather than a closed shell: a critter behind something solid is a critter nobody
    # can see, and the goal has to be the most legible thing on the board.
    d.ellipse([inset, inset, TILE - inset, TILE - inset], outline=bark,
              width=int(TILE * 0.105))

    # The rim light along the bottom two-thirds, which is what makes it read as a bowl holding
    # something rather than as a ring lying flat.
    d.arc([inset, inset, TILE - inset, TILE - inset], 160, 380, fill=lit,
          width=int(TILE * 0.055))

    # Four ribs, which is what tells a cradle apart from a ring at the size a thumb covers it.
    for i in range(4):
        a0 = 30 + i * 90
        d.arc([int(TILE * 0.15), int(TILE * 0.15), int(TILE * 0.85), int(TILE * 0.85)],
              a0, a0 + 34, fill=lit, width=int(TILE * 0.045))

    pod.alpha_composite(shell)
    return pod


def socket(z):
    """What an ember leaves behind: a cold, cracked seat with the light gone out of it.

    **It has to be visible and it has to look dead**, which are two requirements pulling
    opposite ways and both of them matter. The material is this mode's whole economy, so a
    hollow that quietly forgot what it had spent would be a hollow nobody could plan on - and
    one where a spent seat still looked like an ember would be worse, because every tap on it
    is a move the player thought they had.

    So: cool where every ember is warm or saturated, ringed rather than filled, and with no
    glow of its own at all. The first cut was a small dark jewel shaded down, and on a contact
    sheet it read as a smudge of nothing.
    """
    from PIL import ImageDraw

    out = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(out)

    rim = (86, 104, 118, 255)
    shadow = (26, 34, 42, 255)
    inset = int(TILE * 0.26)

    d.ellipse([inset, inset, TILE - inset, TILE - inset], fill=shadow)
    d.ellipse([inset, inset, TILE - inset, TILE - inset], outline=rim,
              width=int(TILE * 0.045))

    # A break across the rim, so it reads as something used up rather than as an empty slot
    # waiting to be filled.
    d.line([int(TILE * 0.34), int(TILE * 0.32), int(TILE * 0.66), int(TILE * 0.68)],
           fill=shadow, width=int(TILE * 0.07))

    return out


def floor_tiles(z):
    """The hollow's ground, and its lit twin."""
    made = {}

    back = read(z, "PNG/stone background.png").resize((TILE, TILE), Image.LANCZOS)

    # Drawn under every cell and never taken away. Dark and cooled toward the grove's green,
    # because everything standing on it is a bright saturated shape and this is what makes them
    # read (CRAFT.md's plate rule) - and because this mode is the only one played in the dark.
    made["moss"] = shade(back, 0.60, -6)

    # Its lit twin. Kept for the aim preview and for anything that wants a cell to read as
    # touched; cool rather than brighter, so a lit line reads as light and not as selection.
    lit = shade(back, 1.00, 10)
    lit.alpha_composite(glow(TILE, TILE * 0.86, (150, 230, 180), 0.42, power=1.4))
    made["moss_lit"] = lit

    made["stone"] = shade(fit(read(z, "PNG/stone.png"), TILE, 0.98), 0.86, -4)

    made["socket"] = socket(z)
    made["husk"] = husk(z)

    for key, (src, light, _) in EMBERS.items():
        # The glow goes *under* the jewel so the object keeps its own outline - a glow
        # composited on top turns a cut stone into a smudge. Two discs rather than one: a wide
        # soft one for the light it throws on the floor, and a tight bright one for the stone
        # itself, which is what makes an ember read as a source rather than as a sticker.
        ember = glow(TILE, TILE * 0.62, light, 0.85)
        ember.alpha_composite(glow(TILE, TILE * 0.34, (255, 250, 232), 0.55))
        ember.alpha_composite(shade(fit(read(z, src), TILE, 0.72), 1.30, 18))
        made[key] = ember

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
        made["Kindle/%s.png" % key] = im

    for key, (folder, turns, saturate) in FLARES.items():
        for i, im in enumerate(flare_frames(blasts, folder, turns, saturate)):
            made["Fx/Kindle/%s/f%02d.png" % (key, i)] = im

    packs = {}
    for key, (pack, folder) in CAST_SET.items():
        if pack not in packs:
            packs[pack] = zipped(pack)
        z = packs[pack]
        if z is None:
            continue
        for i, im in enumerate(cast_frames(z, folder)):
            made["Kindle/%s/f%02d.png" % (key, i)] = im

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
    print("wrote %d files under Assets/Game/Art/Kindle and Art/Fx/Kindle" % len(made))


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

    board = [k for k in sorted(made) if k.startswith("Kindle/") and k.count("/") == 1]
    reels = sorted({k.split("/")[1] for k in made
                    if k.startswith("Kindle/") and k.count("/") == 2})
    flares = sorted({k.split("/")[2] for k in made if k.startswith("Fx/Kindle/")})

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
        frames = sorted(k for k in made if k.startswith("Kindle/%s/" % name))
        for i, key in enumerate(frames[:cols]):
            put(made[key], i, row, name if i == 0 else "")
        row += 1

    for name in flares:
        frames = sorted(k for k in made if k.startswith("Fx/Kindle/%s/" % name))
        for i, key in enumerate(frames[:cols]):
            put(made[key], i, row, name if i == 0 else "")
        row += 1

    out = REPO / "Tools" / "kindle_contact.png"
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
