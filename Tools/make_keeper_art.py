#!/usr/bin/env python3
"""Cuts the keeper ladder's three node discs into Assets/Game/Art/Ui/ from the level-selection pack.

    python Tools/make_keeper_art.py               # re-cut from the pack in Downloads
    python Tools/make_keeper_art.py --check       # prove what ships is what this draws
    python Tools/make_keeper_art.py --contact     # the three discs on the plate they stand beside

**Why these three.** `KeeperScreen` draws every level of the ladder as a node on a spine, and a
node has three states a player must tell apart at a glance while scrolling: reached (the pack's
green *Available* disc), not yet reached (its grey *lock* disc) and the level they stand on (its
*3Star* disc, the one with the crown of stars). They come from `craftpix-net-390835`, the same
top-down level-selection pack whose sky-islands painting is `map8` - so the ladder is drawn in a
hand the game already shows, rather than a fourth style (invariant 44).

**Why a tool rather than three files dropped in** - `make_challenge_art.py`'s reason: the pack
ships 251-square discs, the screen draws them at 128 and a tablet at most twice that, so the cut
is a trim to the alpha and a fit to 256, written down here rather than done once in an editor.
The pack is optional, as every licensed source is: without it the shipped PNGs are the artifact.

The three names are listed by hand in `AssetManifest.UiSprites` so `artnames.py` can see them and
so they are in the global set - the ladder is one tap off the hub, and an `Image` with no sprite
is a white rectangle (7b). `KeeperLevelTests.TheThreeNodeDiscsArePreloadedAndOnDisk` holds both.
"""
import argparse
import io
import pathlib
import sys
import zipfile

try:
    from PIL import Image
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Ui"
PACK = pathlib.Path.home() / "Downloads" / "craftpix-net-390835-the-top-down-level-selection-asset-pack.zip"

# The screen draws a node at 128 (`KeeperScreen.NodeSize`); 256 leaves tablet headroom and is a
# clean downscale of the pack's 251.
LONG_EDGE = 256

# Below this the alpha is the export's soft edge rather than the drawing (`make_ad_art.TRIM_ALPHA`).
TRIM_ALPHA = 8

# pack member (inside Png/) -> the address the screen names, minus `Ui/`.
CUTS = {
    "Available level.png": "keeper_node_open",
    "lock Level.png": "keeper_node_locked",
    "3Star.png": "keeper_node_crown",
}


def load(member):
    """One disc out of the pack, or None when this checkout has no pack."""
    if not PACK.exists():
        return None
    with zipfile.ZipFile(PACK) as z:
        name = next((n for n in z.namelist() if n.endswith("/" + member) and "MACOSX" not in n), None)
        if name is None:
            sys.exit(f"{PACK.name} carries no {member}")
        return Image.open(io.BytesIO(z.read(name))).convert("RGBA")


def cut(image, edge=LONG_EDGE):
    """Trim to the drawing, then fit the long edge - the two decisions, in that order."""
    alpha = image.getchannel("A").point(lambda v: 255 if v >= TRIM_ALPHA else 0)
    box = alpha.getbbox()
    if box is None:
        sys.exit("a disc with nothing drawn in it")
    trimmed = image.crop(box)

    # Square the canvas so every disc lands at the same size on the spine whatever its bbox.
    side = max(trimmed.size)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.alpha_composite(trimmed, ((side - trimmed.width) // 2, (side - trimmed.height) // 2))
    return square.resize((edge, edge), Image.LANCZOS)


def same(path, art):
    if not path.exists():
        return False
    return Image.open(path).convert("RGBA").tobytes() == art.tobytes()


def draw():
    """name -> cut image, from the pack when it is here and from the shipped PNGs when it is not."""
    out = {}
    for member, name in CUTS.items():
        src = load(member)
        if src is None:
            path = OUT / (name + ".png")
            if not path.exists():
                sys.exit(f"no pack at {PACK} and no shipped {path.name} to fall back on")
            out[name] = Image.open(path).convert("RGBA")
        else:
            out[name] = cut(src)
    return out


def contact(arts):
    plate = Image.open(OUT / "Hud" / "plate_navy.png").convert("RGBA")
    cell = 160
    sheet = Image.new("RGBA", (cell * len(arts), cell + 40), (6, 24, 56, 255))
    bg = plate.resize((sheet.width, sheet.height), Image.LANCZOS)
    sheet.alpha_composite(bg)
    for i, (name, art) in enumerate(arts.items()):
        im = art.resize((128, 128), Image.LANCZOS)
        sheet.alpha_composite(im, (i * cell + 16, 20))
    path = ROOT / "out" / "keeper_art.png"
    path.parent.mkdir(exist_ok=True)
    sheet.save(path)
    print(f"wrote {path.relative_to(ROOT)}")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true", help="prove what ships is what this draws, and change nothing")
    ap.add_argument("--contact", action="store_true", help="draw the sheet and change nothing")
    args = ap.parse_args()

    arts = draw()

    if args.contact:
        contact(arts)
        return

    if args.check:
        bad = [name for name, art in arts.items() if not same(OUT / (name + ".png"), art)]
        if bad:
            print("not what the tool draws: " + ", ".join(bad), file=sys.stderr)
            sys.exit(1)
        print(f"{len(arts)} keeper node disc(s) are what make_keeper_art.py draws")
        return

    for name, art in arts.items():
        path = OUT / (name + ".png")
        art.save(path)
        print(f"wrote {path.relative_to(ROOT)} ({art.width}x{art.height})")


if __name__ == "__main__":
    main()
