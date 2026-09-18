#!/usr/bin/env python3
"""Cut the XP boost's mark out of CraftPix's vector top-down level map pack.

One sprite: `Assets/Game/Art/Ui/ic_boost_up.png`, the green arrow the map's boost readout stands
beside its "XP" and its countdown.

**It is the update wall's arrow, turned over and turned green** — deliberately the same drawing
rather than a second one. `make_update_icon.py` cuts the identical member for invariant 49's
panel, and the two marks are the same idea pointing opposite ways: a gold arrow **down** says
*download* and a green arrow **up** says *this is lifting* (invariant 49h, which is where the
direction was first made to carry meaning). Two arrows drawn by different hands on one screen
would read as two unrelated controls; one arrow read twice reads as a system.

**The colour is a hue turn, never a multiply.** Invariant 44g: a multiply takes a colour toward
black along its own hue, so tinting this amber green arrives as *brown* — the fault scaled with
area the last time it was tried. The pixels are converted to HSV and the hue is rotated to the
grass the game already uses, with saturation and value left exactly as the artist set them, so
the rim, the bevel and the keyline all survive as themselves.

**It passes with no pack on disk**, which is `make_update_icon.py`'s bargain: a machine without
the licensed zip skips the cut and the committed PNG stands.

    python Tools/make_boost_icon.py            # cut it
    python Tools/make_boost_icon.py --check    # prove the shipped PNG is what this writes
    python Tools/make_boost_icon.py --contact  # look at it on the map's own ground

**The `.meta` carries a derived guid**, for `make_update_icon.meta`'s reason: Addressables keys
every entry on the guid, so a re-cut minting a fresh one would orphan the address and ship a
white rectangle (invariant 7b).
"""

from __future__ import annotations

import argparse
import colorsys
import hashlib
import io
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Ui"

NAME = "ic_boost_up"
SIZE = 128

#: Both roots the art tools read, in order — `make_update_icon.SOURCES`.
SOURCES = [Path(r"C:\Users\Digikey\Downloads\2D ASSETS"),
           Path(r"C:\Users\Digikey\Downloads")]

PACK = "craftpix-net-144512-vector-top-down-level-map-asset-pack.zip"
MEMBER = "Png/Arrow.png"

TEMPLATE = OUT / "ic_update.png.meta"

#: The hue the arrow is turned to, as a fraction of the wheel. 0.28 is the grass this game's
#: greens already sit on — `Pal.Mint` and the affirm face — rather than a primary green, which
#: reads as a system notification next to a painted map.
TARGET_HUE = 0.28

#: How far the source's own hue is allowed to sit from the arrow's gold before a pixel is left
#: alone. The keyline is near-black and the rim near-white; both have meaningless hues, and
#: rotating them would tint the outline. Guarding on *saturation* is what tells them apart.
MIN_SATURATION = 0.15


def pack():
    """The licensed zip, or None when this machine has never had it."""
    for folder in SOURCES:
        candidate = folder / PACK
        if candidate.exists():
            return candidate
    return None


def greened(art):
    """The arrow's own colours, rotated to green. Saturation and value are untouched.

    A hue rotation rather than a tint or a multiply — see the header. Everything the artist did
    to shape the form lives in S and V, so moving H alone keeps the bevel, the rim highlight and
    the dark keyline exactly as they are and changes only what colour the form is made of.
    """
    out = art.copy()
    pixels = out.load()

    for y in range(out.height):
        for x in range(out.width):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue

            h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
            if s < MIN_SATURATION:
                continue                        # the keyline and the rim have no hue to turn

            r2, g2, b2 = colorsys.hsv_to_rgb(TARGET_HUE, s, v)
            pixels[x, y] = (round(r2 * 255), round(g2 * 255), round(b2 * 255), a)

    return out


def cut(z):
    """The arrow, turned over, turned green, trimmed and centred in a square.

    Trimmed because the pack pads its artboards and an untrimmed sprite draws the arrow smaller
    than the box it was given; centred in a square so a caller can size it on one axis and get
    the same picture on every display. `make_update_icon.cut`'s reasoning, and the two must stay
    the same shape or the pair stop reading as one mark.
    """
    from PIL import Image

    art = Image.open(io.BytesIO(z.read(MEMBER))).convert("RGBA")

    # Turned over first, so the trim below measures the arrow as it will be drawn.
    art = art.transpose(Image.FLIP_TOP_BOTTOM)
    art = greened(art)

    box = art.getbbox()
    if box is None:
        sys.exit(f"{MEMBER} is empty")
    art = art.crop(box)

    scale = (SIZE - 8) / max(art.size)          # 4px of air, so the rim is never clipped
    art = art.resize((max(1, round(art.width * scale)), max(1, round(art.height * scale))),
                     Image.LANCZOS)

    square = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    square.alpha_composite(art, ((SIZE - art.width) // 2, (SIZE - art.height) // 2))
    return square


def meta():
    """This project's own importer settings, with a guid derived from the name."""
    text = TEMPLATE.read_text(encoding="utf8")
    guid = hashlib.md5(("glimmer.ui." + NAME).encode("utf8")).hexdigest()

    out = []
    for line in text.splitlines(keepends=True):
        if line.startswith("guid: "):
            out.append(f"guid: {guid}\n")
        elif line.strip().startswith("spriteBorder:"):
            out.append("  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n")
        else:
            out.append(line)
    return "".join(out)


def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, "PNG")
    return buffer.getvalue()


def contact(image, path):
    """The mark beside its own readout, on the map's dark chrome.

    The ground matters: this sits on a painted chapter map under the back key, not on white, and
    a green rim is only bright or muddy against something.
    """
    from PIL import Image, ImageDraw

    sys.path.insert(0, str(ROOT / "Tools"))
    import hudkit as K

    sheet = Image.new("RGBA", (420, 260), (18, 28, 52, 255))
    draw = ImageDraw.Draw(sheet)
    draw.rounded_rectangle([20, 20, 400, 240], radius=18, fill=(11, 18, 38, 255))

    glyph = K.fit(image, (64, 64))
    K.paste(sheet, glyph, 120, 108)
    K.text(sheet, "XP", 186, 108, 40, fill=(146, 226, 122), outline=3)
    K.text(sheet, "13h24m", 150, 168, 32, fill=(236, 244, 255), outline=3)

    sheet.convert("RGB").save(path)
    print(f"wrote {path} - look at it")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped PNG is what this writes, and change nothing")
    ap.add_argument("--contact", action="store_true", help="draw the sheet and change nothing")
    args = ap.parse_args()

    png = OUT / f"{NAME}.png"
    zip_path = pack()

    if zip_path is None:
        # The committed PNG stands. A checkout that has never had the licensed pack still
        # passes every gate, which is the whole reason the cut art is committed.
        if not png.exists():
            sys.exit(f"no pack on this machine and no committed {NAME}.png to fall back on")

        if args.check:
            print(f"{NAME}: no pack on this machine; the committed PNG stands")
            return 0

        print(f"{NAME}: no pack on this machine, nothing re-cut")
        return 0

    with zipfile.ZipFile(zip_path) as z:
        image = cut(z)

    if args.contact:
        contact(image, ROOT / "boost_icon.png")
        return 0

    fresh = png_bytes(image)

    if args.check:
        if not png.exists():
            print(f"{NAME}.png is missing", file=sys.stderr)
            return 1
        if png.read_bytes() != fresh:
            print(f"{NAME}.png is not what this tool cuts; re-run without --check",
                  file=sys.stderr)
            return 1

        print(f"{NAME}: {SIZE}x{SIZE}, reproducible")
        return 0

    png.write_bytes(fresh)
    (OUT / f"{NAME}.png.meta").write_text(meta(), encoding="utf8", newline="\n")

    print(f"{NAME}: wrote {png} ({SIZE}x{SIZE}) and its .meta")
    return 0


if __name__ == "__main__":
    sys.exit(main())
