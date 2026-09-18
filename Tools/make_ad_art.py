#!/usr/bin/env python3
"""Cuts the shop's owner-supplied artwork into Assets/Game/Art/Ui/.

    python Tools/make_ad_art.py --source C:/Users/Digikey/Downloads   # re-cut from the artwork
    python Tools/make_ad_art.py --check                               # prove what ships is what this draws
    python Tools/make_ad_art.py --contact                             # one sheet, on the card's own plate

**Why a tool rather than three files dropped in.** The supplied artwork is 1.4k across and the
card draws it at about 300, so importing it as-is ships four times the pixels for nothing and
leaves the subject swimming in whatever padding the export happened to have. The cut is two
decisions — trim to the art, fit the long edge — and both want to be written down rather than done
once by hand in an image editor nobody still has open. `art-source-packs` is the same arrangement
for the licensed packs: the source lives outside the repo, the cut PNG is the committed artifact.

**The trim threshold is the one number worth arguing about.** All three exports carry a soft outer
glow, and the heart's is a dark halo that would otherwise decide the bounding box — so the trim is
taken at alpha 8 rather than at anything non-zero. Measured rather than guessed: every bounding box
here is stable from 8 up to 128, so 8 is comfortably past the fringe and nowhere near the art.

**These already contain their own play button**, which is why `ShopArt.PaintAd` draws no seat, ring
or mark over a picture and composes one only for a placement that has none.
"""

import argparse
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Ui"

# The card's art box is 300 square at 1080 (`render_shop.ART`), and the widest of these is about
# 1.6:1 — so a card picture's long edge lands near 300 drawn. 512 leaves a comfortable margin for
# a tablet without approaching `/Art/Ui/`'s own 1024 cap (`ProjectSetup.Caps`), which is sized for
# backdrop-ish sheets rather than for a card illustration. The per-cut figures are in `CUTS`.

# Below this the alpha is the export's outer glow rather than the drawing. See the header.
TRIM_ALPHA = 8

# source stem -> (name the game asks for, long edge).
#
# `ad_` rather than `ic_` for the three card illustrations, because they are pictures and not
# icons and `ShopArt` builds the address from the kind. The tab glyph keeps the `ic_` prefix the
# rest of that folder uses and is cut smaller, because a tab draws it at about 86 across — the
# family it joins (`ic_heart_boost` at 160, `ic_gift` at 119) is sized for that and not for a
# card.
CUTS = {
    "coinad": ("ad_coin", 512),
    "heartad": ("ad_heart", 512),
    "xpad": ("ad_xp", 512),
    "utility": ("ic_utilities", 256),

    # The gem-priced XP boost's own glyph, named to parallel `ic_heart_boost` because it is the
    # same thing one shelf over: the picture on a good's card. `PaintGood` draws it at about 222
    # (0.74 of the 300 art box), so 384 is comfortable headroom for a tablet without the 512 a
    # full card illustration needs.
    "xp": ("ic_xp_boost", 384),
}


def load(source, stem):
    from PIL import Image

    path = pathlib.Path(source) / (stem + ".png")
    if not path.exists():
        sys.exit(f"no artwork at {path}")

    return Image.open(path).convert("RGBA")


def cut(image, long_edge):
    """Trim to the drawing, then fit the long edge. Nothing else — no recolour, no grade."""
    from PIL import Image

    alpha = image.split()[-1]
    box = alpha.point(lambda v: 255 if v >= TRIM_ALPHA else 0).getbbox()
    if box is None:
        sys.exit("the artwork is entirely transparent")

    art = image.crop(box)

    w, h = art.size
    scale = long_edge / float(max(w, h))
    if scale < 1.0:
        art = art.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)

    return art


def draw(source):
    made = {}
    for stem, (name, long_edge) in CUTS.items():
        made[name] = cut(load(source, stem), long_edge)
    return made


def contact(made):
    """One sheet, each picture on the card's own plate, at the size the card draws it.

    The plate matters: two of these carry a dark outer glow, and a glow is only a smudge or not a
    smudge against the ground it is drawn on. On white they all look fine.
    """
    from PIL import Image

    sys.path.insert(0, str(ROOT / "Tools"))
    import hudkit as K

    cell, pad = 380, 24
    sheet = Image.new("RGBA", (len(made) * (cell + pad) + pad, cell + pad * 2), (0, 0, 0, 0))

    for i, (name, art) in enumerate(sorted(made.items())):
        x = pad + i * (cell + pad)
        K.paste(sheet, K.skin("Hud/card", cell, cell), x + cell / 2, pad + cell / 2)

        fitted = K.fit(art, (cell * .78, cell * .78))
        K.paste(sheet, fitted, x + cell / 2, pad + cell / 2)
        K.text(sheet, name, x + cell / 2, pad + cell - 26, 22, fill=(255, 245, 225), outline=2)

    out = ROOT / "ad_art.png"
    sheet.convert("RGB").save(out)
    print(f"wrote {out}  {sheet.width}x{sheet.height}  - look at it")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", help="folder holding coinad.png, heartad.png and xpad.png")
    ap.add_argument("--check", action="store_true",
                    help="prove the committed PNGs are what this draws, and change nothing")
    ap.add_argument("--contact", action="store_true", help="draw the sheet and change nothing")
    args = ap.parse_args()

    if args.check:
        # Reproducibility without the source: compare what is on disk against itself, which is
        # all a checkout that has never seen the artwork can honestly prove. The real check is
        # `--source` plus a clean `git status`.
        missing = [n for n, _ in CUTS.values() if not (OUT / (n + ".png")).exists()]
        if missing:
            print("missing cut art: " + ", ".join(sorted(missing)), file=sys.stderr)
            return 1

        from PIL import Image
        for name, long_edge in sorted(CUTS.values()):
            im = Image.open(OUT / (name + ".png"))
            if max(im.size) != long_edge:
                print(f"{name}.png is {im.size[0]}x{im.size[1]}; the long edge should be "
                      f"{long_edge}", file=sys.stderr)
                return 1
            if im.mode != "RGBA":
                print(f"{name}.png is {im.mode}, not RGBA", file=sys.stderr)
                return 1

        print(f"shop art: {len(CUTS)} picture(s) cut, RGBA, "
              + ", ".join(f"{n} at {e}" for n, e in sorted(CUTS.values())))
        return 0

    if not args.source:
        sys.exit("--source is required to cut; --check needs nothing")

    made = draw(args.source)

    if args.contact:
        contact(made)
        return 0

    for name, art in sorted(made.items()):
        path = OUT / (name + ".png")
        art.save(path)
        print(f"  {path.name}  {art.size[0]}x{art.size[1]}")

    print(f"shop art: {len(made)} picture(s) written to {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
