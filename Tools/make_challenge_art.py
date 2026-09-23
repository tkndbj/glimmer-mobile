#!/usr/bin/env python3
"""Cuts the daily challenges' owner-supplied genre pictures into Assets/Game/Art/Ui/.

    python Tools/make_challenge_art.py --source C:/Users/Digikey/Downloads   # re-cut from the artwork
    python Tools/make_challenge_art.py --check                               # prove what ships is what this draws
    python Tools/make_challenge_art.py --contact                             # one sheet, on the card's own plate

**Why a tool rather than four files dropped in** — `make_ad_art.py`'s reason, said of a card
mark: the supplied artwork is 1.25k square and the card draws it at 176, so importing it as-is
ships fifty times the pixels for nothing. The cut is two decisions, trim to the art and fit the
long edge, and both are written down here rather than done once in an image editor.

**The address is derived from the genre's spelling** (`Ui/challenge_{spelling}`,
`ChallengeArt.GenreMark`), which is invariant 7c's shape: a genre added to the enum names its own
picture and nothing here has to be told. The four names are listed by hand in
`AssetManifest.UiSprites` so `artnames.py` can see them and so they are in the global set — the
list is the first screen a player sees after the hub and an `Image` with no sprite is a white
rectangle (7b). `ChallengeLedgerTests.EveryGenresMarkIsPreloadedAndOnDisk` walks the enum against
both, so a fifth genre with no picture fails offline rather than on a device.

**These carry their own frame**, a rounded square in the genre's colour, so the card draws
nothing around them.
"""
import argparse
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Ui"

# The card draws the mark at 176 square at 1080 (`DailyChallengesScreen.MarkSize`); 384 leaves
# tablet headroom without approaching `/Art/Ui/`'s 1024 cap.
LONG_EDGE = 384

# Below this the alpha is the export's soft edge rather than the drawing (`make_ad_art.TRIM_ALPHA`).
TRIM_ALPHA = 8

# source stem -> genre spelling (`ChallengeGenres.Names`). The address is `challenge_{spelling}`.
CUTS = {
    "pairs": "pairs",
    "pipe": "pipes",
    "merge": "merge",
    "push": "sokoban",
}


def address(spelling):
    return "challenge_" + spelling


def load(source, stem):
    from PIL import Image

    path = pathlib.Path(source) / (stem + ".png")
    if not path.exists():
        sys.exit(f"no artwork at {path}")

    return Image.open(path).convert("RGBA")


def cut(image):
    """Trim to the drawing, then fit the long edge. Nothing else — no recolour, no grade."""
    from PIL import Image

    alpha = image.split()[-1]
    box = alpha.point(lambda v: 255 if v >= TRIM_ALPHA else 0).getbbox()
    if box is None:
        sys.exit("the artwork is entirely transparent")

    art = image.crop(box)

    w, h = art.size
    scale = LONG_EDGE / float(max(w, h))
    if scale < 1.0:
        art = art.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)

    return art


def draw(source):
    return {address(spelling): cut(load(source, stem)) for stem, spelling in CUTS.items()}


def contact():
    """One sheet, each mark on the card's own plate, at the size the card draws it."""
    from PIL import Image

    sys.path.insert(0, str(ROOT / "Tools"))
    import hudkit as K

    cell, pad = 300, 24
    names = sorted(address(s) for s in CUTS.values())
    sheet = Image.new("RGBA", (len(names) * (cell + pad) + pad, cell + pad * 2), (0, 0, 0, 0))

    for i, name in enumerate(names):
        x = pad + i * (cell + pad)
        K.paste(sheet, K.skin("Hud/plate_blue", cell, cell), x + cell / 2, pad + cell / 2)
        art = Image.open(OUT / (name + ".png")).convert("RGBA")
        K.paste(sheet, K.fit(art, (176, 176)), x + cell / 2, pad + cell / 2 - 14)
        K.text(sheet, name, x + cell / 2, pad + cell - 26, 20, fill=(255, 245, 225), outline=2)

    out = ROOT / "out" / "challenge_art.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print(f"wrote {out}  {sheet.width}x{sheet.height}  - look at it")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", help="folder holding pairs.png, pipe.png, merge.png and push.png")
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped PNGs are what this cuts from --source, and change nothing")
    ap.add_argument("--contact", action="store_true", help="draw the sheet and change nothing")
    args = ap.parse_args()

    if args.contact:
        contact()
        return

    if not args.source:
        # The source lives outside the repo (`art-source-packs`); without it the shipped PNGs are
        # the artifact and there is nothing to compare against. Reproducibility is proved with
        # `--source` plus a clean `git status`.
        if args.check:
            missing = [address(s) for s in CUTS.values() if not (OUT / (address(s) + ".png")).exists()]
            if missing:
                sys.exit("missing on disk: " + ", ".join(missing))
            print(f"challenge art: {len(CUTS)} mark(s) on disk; --source is needed to prove the cut")
            return
        sys.exit("--source is required to cut; --check needs nothing")

    made = draw(args.source)

    if args.check:
        drift = []
        for name, art in made.items():
            path = OUT / (name + ".png")
            if not path.exists():
                drift.append(name + " (missing)")
                continue
            from PIL import Image
            shipped = Image.open(path).convert("RGBA")
            if shipped.size != art.size or shipped.tobytes() != art.tobytes():
                drift.append(name)
        if drift:
            sys.exit("not what this cuts: " + ", ".join(drift))
        print(f"challenge art: {len(made)} mark(s), reproducible")
        return

    OUT.mkdir(parents=True, exist_ok=True)
    for name, art in made.items():
        art.save(OUT / (name + ".png"))
        print(f"  {name:<20} {art.width}x{art.height}")
    print(f"cut {len(made)} mark(s) into {OUT}; then Addressables > Sync All Assets and save")


if __name__ == "__main__":
    main()
