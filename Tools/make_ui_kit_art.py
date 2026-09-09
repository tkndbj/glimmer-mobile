# -*- coding: utf-8 -*-
"""Cuts the shop's UI furniture out of the Layer Lab pack's own sample interface.

    python Tools/make_ui_kit_art.py            # write them
    python Tools/make_ui_kit_art.py --check    # prove the shipped PNGs and metas reproduce
    python Tools/make_ui_kit_art.py --contact <png>   # lay them out; look at it

Sixteen sprites under `Assets/Game/Art/Ui/Kit/` — card frames, price buttons, tab buttons,
currency pills, the "+" glyphs, the two badge starbursts and a ribbon. They are the same
pack the shop's coin and gem pictures are cut from, which is the whole argument for using
them: a storefront whose furniture and whose merchandise were drawn by different hands is
the thing a player reads as "unfinished" without being able to say why.

**This tool writes the `.meta` as well as the PNG, and that is the point of it.** A
nine-sliced sprite's border lives in the importer, not in the image — `UIKit.Img` turns on
`Image.Type.Sliced` for any sprite whose `border` is non-zero — so a PNG dropped in without
one stretches its corners and there is nothing anywhere that could notice. The border is
**measured** rather than typed: a rounded rectangle's corner is the first row whose opaque
span reaches the sprite's full width, which is exact for this kit and fails loudly for
anything that is not a rounded rectangle.

**Every sprite is upscaled, and that is not laziness about resolution.** The kit is authored
small (a button is 60x62), and a nine-sliced corner is drawn at one sprite pixel per UI unit
— so at native size a card frame would carry a 23-unit corner where this project's own
buttons carry 40-46. The scale is therefore chosen per sprite to put its *corner* in the
house range, and the upscale is followed by the alpha steepening `make_iap_art.crisp` uses,
because this is flat-region art whose edges really are steps.

**The source is outside the repo** (see the art-source-packs note). Nothing here is
imported into `Assets/` from the pack directly; what ships is what this writes.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import sys
from pathlib import Path

try:
    from PIL import Image, ImageFilter
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

try:
    import numpy as np
    from scipy import ndimage as ndi
except ImportError:                                        # pragma: no cover
    sys.exit("This needs numpy and scipy:  python -m pip install numpy scipy")

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Art" / "Ui" / "Kit"
TEMPLATE = REPO / "Assets" / "Game" / "Art" / "Ui" / "sq_blue.png.meta"

DEFAULT_SOURCE = Path(r"C:\Users\Digikey\Downloads\2D ASSETS\_extracted")
PACK = Path("layerlab-shop-pack-2") / "Sprites" / "SampleUI"


class Piece:
    """One sprite: what it is cut from, what it is called, and how it is drawn.

    `scale` is chosen so a sliced piece's measured corner lands in this project's own
    border range; an unsliced one only wants enough pixels for the size it is drawn at.


    `trim` is how much of the baked black keyline to take back off, in the *upscaled*
    pixels that are also UI units — see `thinner`.
    """

    def __init__(self, source, name, scale, sliced=False, trim=0, flat=False):
        self.source, self.name, self.scale, self.sliced = source, name, scale, sliced
        self.trim, self.flat = trim, flat


KIT = [
    # The card plates. Three colours because a shelf is told apart by one — see `ShopSkins`.
    Piece("Frame_Green", "frame_green", 3, sliced=True, trim=7, flat=True),
    Piece("Frame_Purple", "frame_purple", 3, sliced=True, trim=7, flat=True),
    Piece("Frame_Yellow", "frame_yellow", 3, sliced=True, trim=7, flat=True),

    # The price bar at the foot of a card, and the same shape as a tab's chip.
    Piece("Button_Blue", "btn_blue", 3, sliced=True, trim=5),
    Piece("Button_Purple", "btn_purple", 3, sliced=True, trim=5),
    Piece("Button_Yellow", "btn_yellow", 3, sliced=True, trim=5),

    # The currency readout's trough, and the "+" that opens the shop from it.
    Piece("Resource_Bg", "pill", 4, sliced=True),
    Piece("Icon_Add_Blue", "add_blue", 4),
    Piece("Icon_Add_Purple", "add_purple", 4),
    Piece("Icon_Add_Yellow", "add_yellow", 4),

    # The shelf switcher.
    Piece("Tab_Button_Focus", "tab_on", 3, sliced=True, trim=9),
    Piece("Tab_Button_Nomal", "tab_off", 3, sliced=True, trim=9),

    # What a card wears when it is the one worth pointing at.
    Piece("shop_badge_red", "badge_red", 2),
    Piece("shop_badge_purple", "badge_purple", 2),
    Piece("Ribbon_Red", "ribbon", 2),

    # The light behind a featured card. Unsliced and drawn at whatever size it is asked for,
    # because it is a glow rather than a shape.
    Piece("Banner_Glow", "glow", 1),
]


# --------------------------------------------------------------------------- cutting
def corner(alpha):
    """A rounded rectangle's corner radius, as (left, bottom, right, top) in source pixels.

    Read off the sprite rather than typed. Each side is the distance from the edge to the
    first line whose opaque span is the full width (or height) of the shape — which for a
    rounded rectangle is exactly where the arc ends and the straight run begins.
    """
    # Solid alpha, not "anything painted". These are measured after an upscale, so the
    # silhouette carries a soft edge a pixel or two wide; counting it makes the span grow
    # gradually and the arc appear to end late, which reads as a corner half again as round
    # as the one the kit drew.
    on = alpha > 128
    rows, cols = on.sum(1), on.sum(0)
    if not rows.max() or not cols.max():
        return None

    def run(profile, full):
        first = int(np.argmax(profile >= full - 2))
        last = len(profile) - 1 - int(np.argmax(profile[::-1] >= full - 2))
        return first, len(profile) - 1 - last

    top, bottom = run(rows, rows.max())
    left, right = run(cols, cols.max())
    return left, bottom, right, top


def crisp(im, scale):
    """Upscales flat-shaded art and puts its edges back — `make_iap_art.crisp`'s rule."""
    if scale == 1:
        return im

    im = im.resize((im.width * scale, im.height * scale), Image.LANCZOS)
    r, g, b, a = im.split()
    a = a.point(lambda v: max(0, min(255, int((v - 128) * 3.2 + 128))))
    rgb = Image.merge("RGB", (r, g, b)).filter(
        ImageFilter.UnsharpMask(radius=max(1, scale), percent=70, threshold=2))
    return Image.merge("RGBA", (*rgb.split(), a))


def thinner(im, trim):
    """Takes `trim` pixels off the outside of a sprite, which thins its baked keyline.

    The kit draws a black outline into every control at a fixed *source* width — four
    pixels on a card frame, three on a button, five on a tab. Upscaling multiplies it: at
    three times, a card ships a twelve-unit keyline, which is what "the outlines are too
    thick" is. Nothing can be done about it in the importer, because the outline is paint
    rather than a border.

    So the outermost band is removed. It works because the keyline is the *outside* of the
    shape by construction — eroding the silhouette eats the rim and nothing else, and what
    is left is the same outline drawn narrower. The edge is kept soft to a pixel, so a
    thinned frame is no more aliased than the one it replaces.
    """
    if trim <= 0:
        return im

    alpha = np.asarray(im)[..., 3]

    # Padded, because several of these sprites are opaque right to their own canvas edge —
    # a button is a rounded rectangle that fills its frame. `distance_transform_edt`
    # measures to the nearest *zero inside the array*, so without a transparent margin the
    # edge pixels come back far from anything and the erosion is a silent no-op. It was:
    # the keyline measured the same width before and after.
    pad = trim + 2
    solid = np.pad(alpha > 128, pad, constant_values=False)
    inside = ndi.distance_transform_edt(solid)[pad:-pad, pad:-pad]

    out = np.array(im)
    out[..., 3] = np.minimum(alpha, (np.clip(inside - trim + .5, 0, 1) * 255).astype(np.uint8))
    return Image.fromarray(out, "RGBA")


def flatten(im):
    """Paints a frame's interior one colour, keeping only its keyline.

    The kit draws a soft lighter band across the top of every card frame. It is good design
    on its own terms and it was withdrawn by the owner twice, circled on a device: on a card
    whose picture is a bright object floating in the middle of the plate, a band behind that
    object reads as *decoration behind the item* rather than as shading on the card. Which is
    the same reading that took the ray fan and the halos off — an opaque frame changes what a
    wash behind an object means.

    The fill is sampled from the lower half, away from the band, so the card keeps the colour
    the kit chose rather than an average of the colour and its highlight. Only fully opaque
    non-keyline pixels are repainted, so the black outline and the antialiased rim survive
    untouched.
    """
    a = np.array(im).astype(int)
    solid = a[..., 3] > 200
    body = solid & (a[..., :3].max(2) > 90)          # everything that is not the keyline

    lower = body.copy()
    lower[:int(a.shape[0] * .55)] = False
    if not lower.any():
        return im

    out = a.copy()
    out[..., :3][body] = np.median(a[..., :3][lower], axis=0)
    return Image.fromarray(out.astype(np.uint8), "RGBA")


def meta_for(name, border):
    """The importer settings for one sprite: this project's own, with a measured border.

    The guid is derived from the name rather than random, so the tool is reproducible and
    `--check` means something. Unity only asks that it be unique.
    """
    text = TEMPLATE.read_text(encoding="utf8")
    guid = hashlib.md5(("glimmer.ui.kit." + name).encode("utf8")).hexdigest()

    out = []
    for line in text.splitlines(keepends=True):
        if line.startswith("guid: "):
            out.append(f"guid: {guid}\n")
        elif line.strip().startswith("spriteBorder:"):
            out.append("  spriteBorder: {x: %d, y: %d, z: %d, w: %d}\n" % border)
        else:
            out.append(line)
    return "".join(out)


def build(source):
    folder = source / PACK
    made = {}

    for piece in KIT:
        path = folder / f"{piece.source}.png"
        if not path.exists():
            sys.exit(f"source missing: {path}\n"
                     "pass --source, or see the art-source-packs note for where packs live")

        im = thinner(crisp(Image.open(path).convert("RGBA"), piece.scale), piece.trim)
        if piece.flat:
            im = flatten(im)

        border = (0, 0, 0, 0)
        if piece.sliced:
            # Measured on the sprite that ships rather than on the source, because the trim
            # moves the corner: a border read before it would slice a millimetre outside the
            # arc it is meant to preserve.
            measured = corner(np.asarray(im)[..., 3])
            if measured is None:
                sys.exit(f"{piece.source}: nothing opaque to measure a border from")

            border = measured
            w, h = im.width, im.height
            if border[0] + border[2] >= w or border[1] + border[3] >= h:
                sys.exit(f"{piece.source}: a corner of {measured} leaves no middle to stretch "
                         "- it is not a rounded rectangle, so it cannot be nine-sliced")

        made[piece.name] = (im, border)

    return made


# --------------------------------------------------------------------------- looking
def contact(made, path, cell=210):
    """Every piece at a common size, on the shop's own ground."""
    from PIL import ImageDraw

    names = sorted(made)
    cols = 6
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 18)), (18, 36, 74))
    draw = ImageDraw.Draw(sheet)

    for i, name in enumerate(names):
        im = made[name][0]
        s = min((cell - 16) / im.width, (cell - 16) / im.height)
        one = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)
        x = (i % cols) * cell + (cell - one.width) // 2
        y = (i // cols) * (cell + 18) + (cell - one.height) // 2
        sheet.paste(one, (x, y), one)
        draw.text(((i % cols) * cell + 4, (i // cols) * (cell + 18) + cell + 3),
                  f"{name}  {made[name][1][0]}", fill=(190, 210, 240))

    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


# --------------------------------------------------------------------------- entry
def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", type=Path, metavar="PNG")
    args = ap.parse_args()

    made = build(args.source)
    if not args.check:
        OUT.mkdir(parents=True, exist_ok=True)

    stale = []
    for name, (img, border) in sorted(made.items()):
        png, meta = OUT / f"{name}.png", OUT / f"{name}.png.meta"

        buf = io.BytesIO()
        img.save(buf, "PNG", optimize=True)
        data, text = buf.getvalue(), meta_for(name, border)

        if args.check:
            if not png.exists() or png.read_bytes() != data:
                stale.append(name)
            elif not meta.exists() or meta.read_text(encoding="utf8") != text:
                stale.append(name + " (meta)")
            continue

        png.write_bytes(data)
        meta.write_text(text, encoding="utf8")
        print(f"  wrote Kit/{name}.png  {img.width}x{img.height}  border {border}")

    if args.contact:
        contact(made, args.contact)
        print(f"  wrote {args.contact}  - look at it; --check cannot")

    if args.check:
        if stale:
            sys.exit("stale, re-run without --check: " + ", ".join(stale))
        print(f"the UI kit is what the tool would write ({len(made)} sprites)")


if __name__ == "__main__":
    main()
