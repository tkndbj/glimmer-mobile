# -*- coding: utf-8 -*-
"""Draws the curse: the obsidian stone on the field and the sigil a hex hangs on a body.

    python Tools/make_obsidian_art.py --write     # cut them
    python Tools/make_obsidian_art.py --check     # prove the shipped PNGs are what this draws
    python Tools/make_obsidian_art.py --contact   # one sheet, to look at - the gate that matters

**What this is for.** The ninth Thornwatch chapter deals a fifth kind of gem into its refill -
the cursed stone the Pairs challenges already draw (`SiegeLayout.Obsidian`). It burns in no ward,
so it is clutter; three in a line break the curse, every obsidian on the field is drawn into the
break, and every raider on the hill is hexed (`SiegeRaider.Hexed`). Two pictures say that, and
the rest of it is drawn by the board (`SiegeView.Obsidian`):

* **`Siege/gem_obsidian.png`** - the stone. **Cut from `Art/Challenge/pair_curse.png`, the Pairs
  curse stone, and not from the licensed pack**: the owner's brief was "use the cursed gem we have
  in Pairs", so a player who has met it on a card meets the same stone on the field - and reading
  the committed PNG rather than the pack means this tool runs on any checkout. What it adds is the
  one thing the card never needed: **violet fissures of light** under the black, so the stone reads
  as *cursed* on a board of four saturated jewels rather than as a hole, and so the break that
  shatters it is visibly the same light that falls on the hill.
* **`Siege/hex_sigil.png`** - a white rune circle, a coverage mask for the additive material
  (`Additive`, invariant 37eu) exactly as the strike kit's pieces are, so one texture serves the
  break's burst, the stamp on each body and the ring a hexed raider stands in. Two rings, a band of
  sixteen runes, a toothed wheel with three swirling arms, and a core.
**There was a third, `Siege/hexwave/`** - a painted front the curse rode up the hill on, twenty-four
frames. It was the third front that hill had (an hourglass's and an anvil's are the other two) and
came back from the owner as "not unique"; a curse reaches the hill as a **lash** now, black whips
thrown from the break at every body standing (`SiegeView.Cursed`, `Lash`, MODES.md 37ex), which is
a mesh and ships no picture. The reel went with its frames, its addresses and its label (8d).

**Nothing here is random.** The fissures and the runes are seeded once, so `--check` reproduces
every byte. The palette is written down here because this tool has no palette to import, and
`CurseLight` (the view's palette) is held to it by `SiegeObsidianTests` - including `INK`, which
this tool paints nothing with and the view lays every whip on in.
"""
from __future__ import annotations

import argparse
import io
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_legend_fx import Sheet                                        # noqa: E402

REPO = Path(__file__).resolve().parent.parent
ART = REPO / "Assets" / "Game" / "Art"

#: The Pairs curse stone, which this cuts the field's obsidian from.
SOURCE = ART / "Challenge" / "pair_curse.png"

# --------------------------------------------------------------------------- the palette

#: The curse's light, from the hottest to the deepest. `VIOLET` is `CurseLight.Violet`, `LILAC`
#: `CurseLight.Lilac` and `DEEP` `CurseLight.Deep`, exactly - a fixture holds all three together.
LILAC = (232, 206, 255)
VIOLET = (168, 92, 255)
DEEP = (70, 22, 128)

#: The dark itself - `CurseLight.Ink`, what a whip and the knot it is thrown from are laid on in.
#: Not black, which on a phone is a hole cut in the picture: the stone's own shadow.
INK = (16, 6, 30)


def unit(rgb):
    """A 0-255 colour as the additive kit's 0-1 energy (`make_legend_fx.Sheet` works in units)."""
    return tuple(c / 255.0 for c in rgb)

# --------------------------------------------------------------------------- the stone

#: The field's gem canvas and how much of it a stone fills - `make_siege_art`'s `TILE` and the four
#: jewels' own box (their lit box runs 150-170 of 192), so the obsidian sits in its socket at the
#: size its neighbours do.
TILE = 192
STONE_FILL = 0.86


def stone():
    """The obsidian: the Pairs curse stone, fitted to the field's socket and cracked with light."""
    src = Image.open(SOURCE).convert("RGBA")
    box = src.getbbox()
    cut = src.crop(box)

    scale = TILE * STONE_FILL / max(cut.size)
    size = (max(1, round(cut.size[0] * scale)), max(1, round(cut.size[1] * scale)))
    cut = cut.resize(size, Image.LANCZOS)

    canvas = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    canvas.alpha_composite(cut, ((TILE - size[0]) // 2, (TILE - size[1]) // 2))

    a = np.asarray(canvas, np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3]

    # **Cooled toward the curse, not recoloured.** The card's stone is a neutral black with white
    # facet highlights; pulled a fifth of the way toward the deep violet in its shadows it is still
    # obsidian and already belongs to the light that is about to come out of it.
    lum = rgb.mean(axis=-1, keepdims=True)
    deep = np.asarray(DEEP, np.float32) / 255.0
    rgb = rgb * (1.0 - 0.22 * (1.0 - lum)) + deep * 0.22 * (1.0 - lum)

    # **The inside, and where it is lit from.** An eroded mask is the body of the stone away from
    # its outline, so the light sits under the facets rather than on the silhouette.
    inside = np.asarray(Image.fromarray((alpha * 255).astype(np.uint8), "L")
                        .filter(ImageFilter.MinFilter(13)), np.float32) / 255.0
    inner = np.asarray(Image.fromarray((inside * 255).astype(np.uint8), "L")
                       .filter(ImageFilter.GaussianBlur(9)), np.float32) / 255.0

    # **The fissures**: four jagged cracks out of a point a little above the middle, drawn as
    # additive light and then clipped to the inside, so they are *in* the stone rather than on it.
    sheet = Sheet(TILE, TILE)
    rng = np.random.RandomState(9001)
    cx, cy = TILE * .5, TILE * .46
    for k in range(5):
        ang = math.tau * (k / 5.0) + rng.uniform(-.35, .35) - math.pi / 2
        reach = TILE * rng.uniform(.26, .36)
        pts, x, y = [(cx, cy)], cx, cy
        for s in range(6):
            step = reach / 6.0
            ang += rng.uniform(-.45, .45)
            x += math.cos(ang) * step
            y += math.sin(ang) * step
            pts.append((x, y))
        sheet.path(pts, 5.5, unit(VIOLET), gain=.62, taper=1.2)
        sheet.path(pts, 1.6, unit(LILAC), gain=1.0, taper=.45)

    # The heart the cracks run out of: a small bright core and a wider bloom.
    sheet.dot(cx, cy, TILE * .10, unit(VIOLET), gain=.55)
    sheet.dot(cx, cy, TILE * .03, unit(LILAC), gain=1.0)

    light = np.asarray(sheet.image(), np.float32) / 255.0
    glow = light[..., :3] * light[..., 3:4] * inner[..., None]

    # Screened on, so the black stays black where there is no light and the cracks burn.
    rgb = 1.0 - (1.0 - rgb) * (1.0 - glow)

    # **A thin violet rim on the silhouette**, which is what separates a dark stone from the dark
    # well it sits in - the board's plate is nearly this colour, and a black gem with no rim is a
    # hole in the field.
    rim = np.clip(alpha - inside, 0.0, 1.0)
    rim = np.asarray(Image.fromarray((rim * 255).astype(np.uint8), "L")
                     .filter(ImageFilter.GaussianBlur(1.2)), np.float32) / 255.0
    violet = np.asarray(VIOLET, np.float32) / 255.0
    rgb = rgb + (violet - rgb) * (0.55 * rim)[..., None]

    out = np.concatenate([np.clip(rgb, 0, 1), alpha[..., None]], axis=-1)
    return Image.fromarray((out * 255.0 + .5).astype(np.uint8), "RGBA")


# --------------------------------------------------------------------------- the sigil

#: The sigil's canvas. Drawn at 256 for a stamp that may be four cells wide over a boss.
SIGIL = 256


def _rune(sheet, x, y, size, angle, kind, gain):
    """One rune of the band: a short made mark, one of four shapes, turned to face the centre."""
    c, s = math.cos(angle), math.sin(angle)

    def at(u, v):
        # u runs round the band, v runs outward from the centre.
        return x + u * size * -s + v * size * c, y + u * size * c + v * size * s

    w = size * .09
    white = (1.0, 1.0, 1.0)
    if kind == 0:        # a tall stroke with a bar through it
        sheet.path([at(0, -.5), at(0, .5)], w, white, gain)
        sheet.path([at(-.28, .05), at(.28, .05)], w, white, gain)
    elif kind == 1:      # an open chevron
        sheet.path([at(-.3, -.4), at(0, .4), at(.3, -.4)], w, white, gain)
    elif kind == 2:      # a hook
        sheet.path([at(-.25, .45), at(-.25, -.35), at(.2, -.45), at(.28, 0)], w, white, gain)
    else:                # a dot over a bar
        sheet.path([at(-.3, -.3), at(.3, -.3)], w, white, gain)
        sheet.dot(*at(0, .3), w * 1.9, white, gain)


def sigil():
    """The hex's rune circle, as a white coverage mask for the additive material."""
    n = SIGIL
    sheet = Sheet(n, n)
    c = n * .5
    white = (1.0, 1.0, 1.0)

    # A wide soft floor under the whole circle, so the stamp reads as light on the ground rather
    # than as a line drawing.
    sheet.dot(c, c, n * .47, white, gain=.16, power=1.3)

    # Two rings and the band between them.
    sheet.ring(c, c, n * .455, n * .012, white, gain=1.0)
    sheet.ring(c, c, n * .385, n * .007, white, gain=.9)

    # Sixteen runes round the band, four shapes dealt in a fixed order.
    for i in range(16):
        ang = math.tau * i / 16.0
        _rune(sheet, c + math.cos(ang) * n * .42, c + math.sin(ang) * n * .42,
              n * .052, ang, (i * 7) % 4, .95)

    # **A clockwork wheel rather than a star**, and that is a decision about who plays this: every
    # familiar star polygon is somebody's sacred sign, and a *curse* mark drawn in one is a
    # picture a globally shipped game must never put on a screen. A toothed ring and three
    # swirling arms say *a mechanism winding* - which is the chapter (Cogspire) and is nobody's
    # emblem.
    r = n * .30
    for k in range(24):
        a0 = math.tau * k / 24.0
        if k % 2 == 0:
            sheet.path([(c + math.cos(a0) * r, c + math.sin(a0) * r),
                        (c + math.cos(a0) * (r + n * .03), c + math.sin(a0) * (r + n * .03)),
                        (c + math.cos(a0 + math.tau / 24) * (r + n * .03),
                         c + math.sin(a0 + math.tau / 24) * (r + n * .03)),
                        (c + math.cos(a0 + math.tau / 24) * r,
                         c + math.sin(a0 + math.tau / 24) * r)], n * .006, white, gain=.9)
    sheet.ring(c, c, r, n * .006, white, gain=.9)

    for k in range(3):
        base = math.tau * k / 3.0
        arm = [(c + math.cos(base + t * 1.9) * n * (.05 + t * .22),
                c + math.sin(base + t * 1.9) * n * (.05 + t * .22)) for t in np.linspace(0, 1, 14)]
        sheet.path(arm, n * .010, white, gain=.95, taper=n * .003)

    # The core: a point.
    sheet.dot(c, c, n * .05, white, gain=1.1)

    im = sheet.image()
    a = np.asarray(im, np.float32)
    a[..., :3] = 255.0          # a mask: all the information is in the alpha
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


# --------------------------------------------------------------------------- the tool

def build():
    return {"Siege/gem_obsidian.png": stone(), "Siege/hex_sigil.png": sigil()}


def raw(im):
    buffer = io.BytesIO()
    im.save(buffer, "PNG", optimize=False)
    return buffer.getvalue()


def path_of(rel):
    return ART / rel


def write(made):
    for rel, im in sorted(made.items()):
        target = path_of(rel)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw(im))

    print("wrote %d files under Assets/Game/Art/Siege" % len(made))
    print("the Editor has to address them: Glimmer Grove > Addressables > Sync All Assets, "
          "AND SAVE (invariant 7a) - until then an obsidian is a WHITE RECTANGLE (7b)")


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

    print("%d files are what this tool draws" % len(made))


def contact(made):
    """The stone among the four jewels, and the sigil on the board's dark."""
    from PIL import ImageDraw

    ground = (26, 28, 40, 255)
    sheet = Image.new("RGBA", (1280, 300), ground)
    pen = ImageDraw.Draw(sheet)

    x = 20
    for name in ("gem_r", "gem_g", "gem_b", "gem_y"):
        sheet.alpha_composite(Image.open(ART / "Siege" / (name + ".png")).convert("RGBA"), (x, 20))
        x += 200
    sheet.alpha_composite(made["Siege/gem_obsidian.png"], (x, 20))
    pen.text((x, 214), "gem_obsidian", fill=(220, 220, 230, 255))

    # The sigil drawn the way the view draws it: tinted violet, added over the dark.
    sig = np.asarray(made["Siege/hex_sigil.png"], np.float32) / 255.0
    violet = np.asarray(VIOLET, np.float32) / 255.0
    base = np.asarray(sheet, np.float32) / 255.0
    ox, oy = 1010, 12
    patch = base[oy:oy + SIGIL, ox:ox + SIGIL, :3]
    patch += sig[..., 3:4] * violet
    base[oy:oy + SIGIL, ox:ox + SIGIL, :3] = np.clip(patch, 0, 1)
    sheet = Image.fromarray((base * 255).astype(np.uint8), "RGBA")
    pen = ImageDraw.Draw(sheet)
    pen.text((ox, oy + SIGIL + 2), "hex_sigil (added, violet)", fill=(220, 220, 230, 255))

    out = REPO / "Tools" / "out" / "obsidian_contact.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print("wrote %s" % out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    args = ap.parse_args()

    made = build()

    if args.write:
        write(made)
    if args.contact:
        contact(made)
    if args.check or not (args.write or args.contact):
        check(made)


if __name__ == "__main__":
    main()
