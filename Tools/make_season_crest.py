#!/usr/bin/env python3
"""Cut the season's crest out of the interface kit this whole app is drawn from.

One sprite: `Assets/Game/Art/Ui/ic_season.png`, the gold crown the hub's season box and the
season page's hero wear. It replaces a ring of twelve generated pips (`SeasonCrest.PaintWatch`)
that the owner rejected on sight — a disc with dots round it — and that is the ordinary outcome
for a generated emblem, for `make_update_icon`'s reason: **a shape assembled out of primitives
has no artist in it** (invariant 49h), and it reads as a placeholder beside a card cut from a
bought kit.

**Why this pack.** It is the pack every plate, ribbon, pill and trough in this app is already
cut from (`make_hud_kit_art`, invariant 44) — one heavy navy keyline at `(6, 24, 56)` over a
saturated face, on every one of its sixty-eight pieces. So the crest does not merely *suit* the
card it stands on: it is the same object family, and a kit swap re-cuts it along with everything
else rather than leaving one emblem behind in the old kit's palette.

**Why a crown.** Surveyed at the size the box draws it, on the box's own violet plate, against
every emblem in the four icon packs on this machine (invariant 46b: put several up at once
rather than one). The parchment kit's glyphs are monochrome and read as a stencil; the skill-icon
pack's hundred emblems are painted *inside a square frame* and cut as a sticker rather than a
silhouette; the flat RPG set's shields and pentagons are in the right register and say nothing —
a plain shield is a badge on any screen in any game. The crown is the genre's own word for what
this feature is, it is the only candidate in the kit's own family with any mass to it, and at
148 units it is read before the sentence beside it is.

**It carries no progress, and that is the trade.** The pips filled as the ladder filled, which
is a real property and the one thing the generated crest was good at. What buys it back is that
both callers already draw the count *and* a bar directly under the crest — the hub box prints
`marks / goal` with `FeatureBar` beneath it, the page prints the rungs with its own — so the
reading was being made twice and only one of the two was drawn by anybody. A crest says *which
season this is*; the bar says how far through it the player is. The lit-with-progress shape is
also the one invariant 37m refuses from the other end: a crest dimmed until the track fills is
dimmed on the first frame of every season anybody starts.

**It passes with no pack on disk**, which is `make_update_icon`'s bargain and the reason the gate
runs on a fresh checkout: a machine without the licensed zip skips the cut and the committed PNG
stands.

    python Tools/make_season_crest.py            # cut it
    python Tools/make_season_crest.py --check    # prove the shipped PNG is what this writes
    python Tools/make_season_crest.py --contact  # look at it on the box's own plate

**The `.meta` carries a derived guid**, for `make_hud_kit_art.meta_for`'s reason: Addressables
keys every registered entry on the guid, so a re-cut that minted a fresh one would orphan the
address rather than move it — the game would go on asking for `Ui/ic_season`, nothing would
answer, and what ships is a white rectangle (invariant 7b) on the first screen after the splash.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import sys
import zipfile
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/Game/Art/Ui"
TEMPLATE = OUT / "panel_main.png.meta"

#: Both roots the art tools read, in order — `make_update_icon.SOURCES`' note applies verbatim.
SOURCES = [Path(r"C:\Users\Digikey\Downloads\2D ASSETS"),
           Path(r"C:\Users\Digikey\Downloads\to-assets"),
           Path(r"C:\Users\Digikey\Downloads")]

#: The copy `make_hud_kit_art` reads, so the crest and the furniture it stands on can never come
#: from two different downloads of one pack. The suffixed name is what is on this machine; the
#: plain one is the same zip and is tried second for a clone that fetched it once.
PACKS = ["craftpix-net-828046-cartoon-ui-elements-mini-kit (2).zip",
         "craftpix-net-828046-cartoon-ui-elements-mini-kit.zip"]

#: The pack names its artboards by number and nothing else, so the member is written down here
#: with what it *is* beside it — a listing of this zip tells the next reader nothing.
MEMBER = "Png/Artboard 15.png"        # the gold crown
NAME = "ic_season"

#: The square the sprite is written at. The hub box draws it at 148 canvas units and the season
#: page at 150, so 256 is the nearest power of two above both — a downscale on every device,
#: which is the side of unity to be on (`make_update_icon.SIZE` takes the same view from the
#: other side, where the source was smaller than the draw).
SIZE = 256


def pack():
    """The licensed zip, or None when this machine does not have it."""
    for folder in SOURCES:
        for name in PACKS:
            path = folder / name
            if path.exists():
                return zipfile.ZipFile(path), name
    return None, None


def cut(z):
    """The crown, trimmed to its own alpha and centred in a square.

    Trimmed because the pack pads its artboards — this one by 34 pixels of nothing on the left
    alone — and an untrimmed sprite draws the crown smaller than the box it was given, which is
    the fault `make_siege_art.one_canvas` records about a body framed around its own halo. Centred in a
    square so both callers can size it on one axis and get the same picture.

    **`preserveAspect` is what the call sites then owe it**: the crown is wider than it is tall,
    so a square sprite sized to a square box is only the right picture because the transparent
    margin is symmetric.
    """
    art = Image.open(io.BytesIO(z.read(MEMBER))).convert("RGBA")

    box = art.getbbox()
    if box is None:
        sys.exit(f"{MEMBER} is empty")
    art = art.crop(box)

    scale = (SIZE - 8) / max(art.size)          # 4px of air, so the keyline is never clipped
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
            # An emblem is never nine-sliced: it is scaled whole, so a border would stretch its
            # band and pin its points.
            out.append("  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n")
        else:
            out.append(line)
    return "".join(out)


def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, "PNG")
    return buffer.getvalue()


def contact(image, path):
    """The crest on the box's own violet plate, at the two sizes the game draws it.

    The only thing that can say whether it reads — `--check` proves reproducibility and says
    nothing about quality, which is this project's rule about every art tool it has. The plate
    is the real `plate_violet`, because a crest judged against a flat swatch is a crest judged
    against a ground the game does not have (invariant 44i, said about the mirror).
    """
    plate = Image.open(OUT / "Hud/plate_violet.png").convert("RGBA")

    sheet = Image.new("RGBA", (760, 300), (11, 18, 38, 255))
    sheet.alpha_composite(plate.resize((760, 300), Image.LANCZOS))

    for i, drawn in enumerate((148, 96)):
        fitted = image.resize((drawn, drawn), Image.LANCZOS)
        sheet.alpha_composite(fitted, (70 + i * 300, (300 - drawn) // 2))

    sheet.convert("RGB").save(path)
    print(f"wrote {path}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped PNG is what this writes; no files touched")
    ap.add_argument("--contact", type=Path, default=None,
                    help="draw the crest on the season box's own plate at both drawn sizes")
    args = ap.parse_args()

    target = OUT / f"{NAME}.png"
    z, name = pack()

    if z is None:
        # The bargain that keeps the gate runnable on a fresh checkout — `make_update_icon`'s
        # note applies verbatim.
        if target.exists():
            print(f"{PACKS[0]} is not on this machine; {target.name} stands as committed")
            return 0
        sys.exit(f"{PACKS[0]} is not on this machine and {target.name} is not committed either")

    image = cut(z)

    if args.contact:
        contact(image, args.contact)

    if args.check:
        if not target.exists():
            sys.exit(f"{target} is missing")
        if target.read_bytes() != png_bytes(image):
            sys.exit(f"{target.name} is not what this tool writes; re-run without --check")
        print(f"{target.name} is exactly what this tool cuts ({SIZE}x{SIZE})")
        return 0

    target.write_bytes(png_bytes(image))

    # The `.meta` is written only when there is not one, for the guid's sake: a re-cut keeps
    # whatever Addressables already registered against this sprite.
    sidecar = OUT / f"{NAME}.png.meta"
    if not sidecar.exists():
        sidecar.write_text(meta(), encoding="utf8")
        print(f"wrote {sidecar.name} (new guid)")

    print(f"wrote {target.name} ({SIZE}x{SIZE}) from {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
