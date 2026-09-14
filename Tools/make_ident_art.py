# -*- coding: utf-8 -*-
"""Bakes the publisher card's wordmark from Orbitron.

    python Tools/make_ident_art.py
    python Tools/make_ident_art.py --weight 800 --track .08     # try another cut
    python Tools/make_ident_art.py --check                      # prove it reproduces

Input   Tools/IconSource/Orbitron[wght].ttf   (SIL OFL, licence beside it)
Output  Assets/Game/Art/Bg/ident_word.png     (checked in, addressed as Bg/ident_word)

**Why a bake and not a font in the build.** The mark is nine letters that never change, and a
font in `Assets` is a file, a manifest entry, an Addressables row, an audit and a second face
loaded at runtime for one screen. A PNG is one address on a scope the launch screen already
holds. It also keeps the glyphs out of the shipped binary entirely, which is the tidiest answer
to the licence even though the OFL would have permitted either.

**Why Black rather than Bold.** The card's whole trick is a neon sweep travelling *inside* the
letters, masked to their shapes. A light weight has no interior to show it in — the colour would
read as a fringe on the edges of the strokes rather than as light inside them. Weight is what
makes the effect legible, so it is chosen against the effect rather than against the lettering.

**The alpha is the mask.** The PNG is white throughout and carries the shape only in its alpha,
because `StudioIdent` uses it twice: once tinted white as the letters, and once as a Unity `Mask`
that clips the neon band. A mark baked with colour in it would tint the sweep.
"""
from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parent.parent
FONT = REPO / "Tools" / "IconSource" / "Orbitron[wght].ttf"
OUT = REPO / "Assets" / "Game" / "Art" / "Bg" / "ident_word.png"

WORD = "TEKOWORLD"

# The cut. Weight is argued for above; the tracking is what turns a word into a mark, and
# Orbitron is already wide, so this is much less than the stroke alphabet this replaced wanted.
WEIGHT, TRACK_EM = 900, .07

# Drawn at a size that comfortably exceeds what any display asks for: the mark is about 630
# canvas units across on a phone, which is 1890 device pixels at 3x, and the supersample below
# is thrown away in the downsample.
TARGET_WIDTH = 1800
SUPERSAMPLE = 3


def bake(weight: int, track_em: float) -> Image.Image:
    size = 240 * SUPERSAMPLE
    font = ImageFont.truetype(str(FONT), size)
    font.set_variation_by_axes([weight])
    track = size * track_em

    advances = [font.getlength(c) for c in WORD]
    span = int(sum(advances) + track * (len(WORD) - 1))

    canvas = Image.new("RGBA", (span + size, int(size * 2)), (255, 255, 255, 0))
    draw = ImageDraw.Draw(canvas)

    x = size * .5
    for i, c in enumerate(WORD):
        # White throughout: the shape lives in the alpha, so the same file is both the letters
        # and the mask that clips the neon.
        draw.text((x, size * .5), c, font=font, fill=(255, 255, 255, 255))
        x += advances[i] + track

    canvas = canvas.crop(canvas.getbbox())
    height = max(1, round(TARGET_WIDTH * canvas.height / canvas.width))
    return canvas.resize((TARGET_WIDTH, height), Image.LANCZOS)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--weight", type=int, default=WEIGHT)
    ap.add_argument("--track", type=float, default=TRACK_EM)
    ap.add_argument("--check", action="store_true",
                    help="rebuild and compare against what is checked in, writing nothing")
    args = ap.parse_args()

    if not FONT.exists():
        print(f"missing font: {FONT}", file=sys.stderr)
        return 1

    mark = bake(args.weight, args.track)
    aspect = mark.width / mark.height

    if args.check:
        if not OUT.exists():
            print(f"missing {OUT.relative_to(REPO)}", file=sys.stderr)
            return 1
        from io import BytesIO
        buf = BytesIO()
        mark.save(buf, "PNG", optimize=True)
        fresh = hashlib.sha256(buf.getvalue()).hexdigest()
        held = hashlib.sha256(OUT.read_bytes()).hexdigest()
        same = fresh == held
        print(("reproduces" if same else "DIFFERS from") + f" {OUT.relative_to(REPO)}")
        return 0 if same else 1

    OUT.parent.mkdir(parents=True, exist_ok=True)
    mark.save(OUT, "PNG", optimize=True)
    print(f"wrote {OUT.relative_to(REPO)}  {mark.width}x{mark.height}  aspect {aspect:.3f}")
    print(f"  weight {args.weight}, tracking {args.track:.3f} em")
    print("  the aspect is read off the sprite at runtime, so there is no constant to update")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
