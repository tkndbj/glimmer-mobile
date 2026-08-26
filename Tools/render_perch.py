#!/usr/bin/env python3
"""
Draw a level node's **perch** exactly as the map draws it, without Unity.

    python Tools/render_perch.py --out out/perches --source "C:/.../village-assets/_extracted"

## Why this exists

A perch is the one visual difference between two modes' maps (`ModeLook.Perch`), so
choosing one is a decision made entirely by eye — and the thing being judged is not the
tile on its own but the tile *with a glade disc standing on it*, tinted by the mode's
wash, over the chapter's own map art. A contact sheet of source PNGs answers the wrong
question: half the packs' platforms are handsome alone and unusable here, because the disc
sits near the top of the tile and a tall or busy top face swallows it.

So this reimplements `LevelsScreen.MakePerch` and the disc half of `BuildNode` against the
same numbers, which are mirrored below rather than imported for `render_grove.py`'s reason:
this runs with no Unity anywhere.

It is a *renderer*. It takes no view on which tile is right — that is what eyes are for,
and this is what puts the picture in front of them.
"""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parent.parent
MAP_ART = REPO / "Assets" / "Game" / "Art" / "Map"

# ---------------------------------------------------------------- geometry
# LevelsScreen.MakePerch, in canvas units. A perch is a 360x420 node holding a
# shadow, the rock, a contact shadow and the glade disc, in that order.
PERCH_W, PERCH_H = 360, 420
ROCK_BOX = (360, 290)          # the rock's box; the art fits inside it, aspect kept
ROCK_Y = -50                   # ...centred this far below the node's centre
SHADOW = (370, 150, 0, -150)   # w, h, x, y  — Art.Glow(96, 2.2) at (.03,.10,.16,.38)
CONTACT = (232, 74, 0, -44)    # w, h, x, y  — Art.Glow(96, 2.6) at (.02,.08,.12,.45)
NODE_SIZE = 196                # LevelsScreen.NodeSize
NODE_Y = 2                     # the disc sits this far above the node's centre
NODE_FACE_LIFT = 0.165         # UIKit.NodeFaceLift — where the number sits on the face

SHADOW_RGBA = (8, 26, 41, 97)
CONTACT_RGBA = (5, 20, 31, 115)

# ModeLook.Wash, per mode. White is "no wash", which is what the glade takes.
WASHES = {
    "glade": (255, 255, 255),
    "weave": (204, 250, 255),
    "fall": (255, 214, 204),
    "keeper": (214, 255, 224),
}


def glow(size: int, power: float, rgba) -> Image.Image:
    """Art.Glow: a radial falloff, `size` square, raised to `power`."""
    half = size * 0.5
    mask = Image.new("L", (size, size))
    px = mask.load()
    for y in range(size):
        for x in range(size):
            dx, dy = (x - half) / half, (y - half) / half
            d = math.sqrt(dx * dx + dy * dy)
            px[x, y] = int(round(255 * max(0.0, min(1.0, 1.0 - d)) ** power))
    out = Image.new("RGBA", (size, size), rgba[:3] + (0,))
    out.putalpha(Image.eval(mask, lambda v: v * rgba[3] // 255))
    return out


def fit(image: Image.Image, box) -> Image.Image:
    """`Image.preserveAspect`: the largest copy of `image` that fits inside `box`."""
    w, h = image.size
    scale = min(box[0] / w, box[1] / h)
    return image.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)


def tint(image: Image.Image, rgb) -> Image.Image:
    """`Image.color`: a multiply over the sprite, alpha untouched."""
    if rgb == (255, 255, 255):
        return image
    r, g, b, a = image.split()
    table = lambda c: [i * c // 255 for i in range(256)]  # noqa: E731
    return Image.merge("RGBA", (r.point(table(rgb[0])), g.point(table(rgb[1])),
                                b.point(table(rgb[2])), a))


def paste(canvas: Image.Image, sprite: Image.Image, x: float, y: float) -> None:
    """Place a sprite by its centre, in canvas units measured from the perch's centre."""
    cx, cy = canvas.size[0] / 2 + x, canvas.size[1] / 2 - y
    canvas.alpha_composite(sprite, (round(cx - sprite.size[0] / 2),
                                    round(cy - sprite.size[1] / 2)))


def perch(rock: Path, wash, number: str = "7", stars: int = 0) -> Image.Image:
    """One perch with a glade disc on it, on a transparent 360x420 canvas."""
    canvas = Image.new("RGBA", (PERCH_W, PERCH_H), (0, 0, 0, 0))

    paste(canvas, glow(96, 2.2, SHADOW_RGBA).resize(SHADOW[:2], Image.LANCZOS),
          SHADOW[2], SHADOW[3])

    art = fit(Image.open(rock).convert("RGBA"), ROCK_BOX)
    paste(canvas, tint(art, wash), 0, ROCK_Y)

    paste(canvas, glow(96, 2.6, CONTACT_RGBA).resize(CONTACT[:2], Image.LANCZOS),
          CONTACT[2], CONTACT[3])

    skin = "node_s%d" % stars if stars else "node_open"
    disc = fit(Image.open(MAP_ART / (skin + ".png")).convert("RGBA"), (NODE_SIZE, NODE_SIZE))
    paste(canvas, disc, 0, NODE_Y)

    draw = ImageDraw.Draw(canvas)
    face_y = canvas.size[1] / 2 - NODE_Y - NODE_SIZE * NODE_FACE_LIFT
    draw.text((canvas.size[0] / 2, face_y), number, fill=(77, 54, 33, 255),
              anchor="mm", font=_font(62))
    return canvas


def _font(size: int):
    from PIL import ImageFont
    for name in ("seguibl.ttf", "arialbd.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def backdrop(strip: str, box) -> Image.Image:
    """A window onto a real map strip, so a perch is judged over what it stands on."""
    src = Image.open(MAP_ART / (strip + ".png")).convert("RGBA")
    left = (src.size[0] - box[0]) // 2
    top = (src.size[1] - box[1]) // 2
    return src.crop((left, top, left + box[0], top + box[1]))


def card(rock: Path, wash, strip: str, pad: int = 14) -> Image.Image:
    """A perch on its map, cropped to a card."""
    box = (PERCH_W + pad * 2, PERCH_H + pad * 2)
    out = backdrop(strip, box)
    out.alpha_composite(perch(rock, wash), (pad, pad))
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("rocks", nargs="*", help="source PNGs; default is every shipped perch")
    ap.add_argument("--mode", default="weave", choices=sorted(WASHES))
    ap.add_argument("--strip", default="c02_strip1")
    ap.add_argument("--out", default="out/perches")
    args = ap.parse_args()

    rocks = [Path(r) for r in args.rocks] or sorted(MAP_ART.glob("rock_*.png"))
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    for source in rocks:
        if not source.exists():
            print(f"  missing {source}", file=sys.stderr)
            continue
        image = card(source, WASHES[args.mode], args.strip)
        target = out / (source.stem + ".png")
        image.convert("RGB").save(target)
        print(f"  {target}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
