#!/usr/bin/env python3
"""Cuts the owner-drawn pointing hand for the advert's showcase into Assets/Game/Art/Ui/.

    python Tools/make_showcase_hand.py --source "C:/Users/Digikey/Downloads/hand.png"   # cut it
    python Tools/make_showcase_hand.py --check                                          # prove the tip
    python Tools/make_showcase_hand.py --contact                                        # look at it

**Why a tool rather than a file dropped in** - `make_challenge_art.py`'s reason: the drawing is
1.2k square and the screen draws it 250 tall, so importing it as-is ships twenty times the
pixels for nothing. The cut is two decisions, trim to the drawing and fit the long edge, and
both are written down here.

**The fingertip is measured, never typed.** `ShowcaseHand.Fingertip` is a pivot in sprite
space; the screen seats that point on the gem it presses, so a drawing whose tip has moved
puts the hand three pixels beside the cell that moves, which is the one fault a viewer cannot
un-see. `--check` reads the tip off the shipped PNG (the topmost opaque row, the middle of it)
and refuses if it disagrees with the constant read out of `ShowcaseHand.cs`. Without the
source the shipped PNG is the artifact, as every art tool here treats a licensed pack.
"""
import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Ui" / "showcase_hand.png"
SOURCE_CS = ROOT / "Assets" / "Game" / "Scripts" / "Presentation" / "App" / "ShowcaseHand.cs"

# The screen draws the hand 250 tall at 1080 (`ShowcaseHand.Tall`); 512 leaves tablet headroom
# without approaching `/Art/Ui/`'s 1024 cap.
LONG_EDGE = 512

# Below this the alpha is the export's soft edge rather than the drawing (`make_ad_art.TRIM_ALPHA`).
TRIM_ALPHA = 8

# How far the constant may sit from the measured tip, as a fraction of the sprite: two pixels
# on a 512 cut, which is under what a screen can show at 250.
TOLERANCE = 2.5 / LONG_EDGE


def cut(image, edge=LONG_EDGE):
    """Trim to the drawing, then fit the long edge. Nothing else."""
    from PIL import Image

    alpha = image.split()[-1]
    box = alpha.point(lambda v: 255 if v >= TRIM_ALPHA else 0).getbbox()
    if box is None:
        sys.exit("the artwork is entirely transparent")

    art = image.crop(box)
    w, h = art.size
    scale = edge / float(max(w, h))
    if scale < 1.0:
        art = art.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)

    return art


def fingertip(art):
    """The pivot of the tip: the middle of the topmost opaque row, in sprite space (y up)."""
    alpha = art.split()[-1]
    w, h = art.size
    px = alpha.load()

    for y in range(h):
        xs = [x for x in range(w) if px[x, y] >= TRIM_ALPHA]
        if xs:
            # A few rows in, so the reading is the pad of the finger rather than one antialiased pixel.
            y2 = min(h - 1, y + 3)
            xs = [x for x in range(w) if px[x, y2] >= TRIM_ALPHA] or xs
            u = (min(xs) + max(xs)) / 2.0 / w
            v = 1.0 - (y + 1) / float(h)
            return u, v

    sys.exit("the artwork is entirely transparent")


def constant():
    """`ShowcaseHand.Fingertip` as written in the C#."""
    text = SOURCE_CS.read_text(encoding="utf-8")
    m = re.search(r"Fingertip\s*=\s*new Vector2\(\s*([-.\d]+)f?\s*,\s*([-.\d]+)f?\s*\)", text)
    if not m:
        sys.exit(f"could not read Fingertip out of {SOURCE_CS}")
    return float(m.group(1)), float(m.group(2))


def same(path, art):
    from PIL import Image

    if not path.exists():
        return False
    shipped = Image.open(path).convert("RGBA")
    return shipped.size == art.size and shipped.tobytes() == art.tobytes()


def check(art):
    u, v = fingertip(art)
    cu, cv = constant()
    print(f"fingertip measured ({u:.3f}, {v:.3f}); ShowcaseHand.Fingertip says ({cu:.3f}, {cv:.3f})")
    if abs(u - cu) > TOLERANCE or abs(v - cv) > TOLERANCE:
        sys.exit("ShowcaseHand.Fingertip does not match the drawing's tip - "
                 f"write it as new Vector2({u:.3f}f, {v:.3f}f)")


def contact(art):
    """The hand at the size the screen draws it, over a gem plate, tip on a cell's centre."""
    from PIL import Image, ImageDraw

    sys.path.insert(0, str(ROOT / "Tools"))
    import hudkit as K

    tall = 250
    sheet = Image.new("RGBA", (700, 600), (18, 34, 46, 255))
    d = ImageDraw.Draw(sheet)

    # A 3x3 of cells with the target in the middle, so the tip's seat can be judged.
    cell, ox, oy = 138, 140, 120
    for r in range(3):
        for c in range(3):
            x, y = ox + c * cell, oy + r * cell
            d.rounded_rectangle((x, y, x + cell - 6, y + cell - 6), 18, fill=(30, 56, 74, 255))
    cx, cy = ox + cell + cell // 2 - 3, oy + cell + cell // 2 - 3
    d.ellipse((cx - 6, cy - 6, cx + 6, cy + 6), fill=(255, 220, 90, 255))

    scale = tall / float(art.height)
    hand = art.resize((max(1, round(art.width * scale)), tall), Image.LANCZOS)
    u, v = fingertip(art)
    sheet.alpha_composite(hand, (int(cx - u * hand.width), int(cy - (1 - v) * hand.height)))

    K.text(sheet, "tip on the dot", 350, 560, 22, fill=(255, 245, 225), outline=2)

    out = ROOT / "out" / "showcase_hand.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print(f"wrote {out}  - look at it")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", help="the owner's PNG")
    ap.add_argument("--check", action="store_true", help="prove the tip and, with --source, the cut; change nothing")
    ap.add_argument("--contact", action="store_true", help="draw the sheet and change nothing")
    args = ap.parse_args()

    from PIL import Image

    if args.source:
        art = cut(Image.open(args.source).convert("RGBA"))
        if args.check:
            if not same(OUT, art):
                sys.exit(f"{OUT.name} is not what this cuts from {args.source}")
            check(art)
            print("showcase hand: reproducible")
            return
        OUT.parent.mkdir(parents=True, exist_ok=True)
        art.save(OUT, optimize=True)
        u, v = fingertip(art)
        print(f"wrote {OUT}  {art.width}x{art.height}  fingertip ({u:.3f}, {v:.3f})")
        check(art)
        if args.contact:
            contact(art)
        return

    if not OUT.exists():
        sys.exit(f"nothing at {OUT}; cut it with --source first")

    art = Image.open(OUT).convert("RGBA")
    if args.contact:
        contact(art)
    if args.check or not args.contact:
        check(art)
        print("showcase hand: tip agrees with ShowcaseHand.Fingertip (no --source, so the cut is taken as shipped)")


if __name__ == "__main__":
    main()
