#!/usr/bin/env python3
"""Cut the update wall's mark out of CraftPix's vector top-down level map pack.

One sprite: `Assets/Game/Art/Ui/ic_update.png`, the gold arrow the forced-update panel
(invariant 49) stands above its sentence. It replaces a ring-and-chevron this screen used to
*generate*, which the owner rejected on sight — and that is the ordinary outcome, because a
shape assembled out of primitives has no artist in it and reads as a placeholder next to a
panel cut from a bought kit.

**Why an arrow and why this one.** Surveyed at the size the panel draws it, on the panel's own
parchment, against every other candidate in the packs (invariant 46b: put several up at once
rather than one). Three finalists reached a mock of the real panel and the choice was not close:
the merge kit's arrow carries a **violet** keyline that fights the orange ribbon and the green
key either side of it, and the shipped `ic_restart` reads thin and muddy on cream because its
baked drop-shadow has nothing dark to sit against. This one is gold with a white rim, which is
the hub's own palette, and it points **down** — the one direction that says *download* rather
than *upgrade*, which is the sentence the panel is making.

**It passes with no pack on disk**, which is `make_siege_art.py`'s bargain and the reason the
gate can run on a fresh checkout: a machine without the licensed zip skips the cut and the
committed PNG stands.

    python Tools/make_update_icon.py            # cut it
    python Tools/make_update_icon.py --check    # prove the shipped PNG is what this writes
    python Tools/make_update_icon.py --contact  # look at it on the panel's own ground

**The `.meta` carries a derived guid**, for `make_hud_kit_art.meta_for`'s reason: Addressables
keys every registered entry on the guid, so a re-cut that minted a fresh one would orphan the
address rather than move it — the game would go on asking for `Ui/ic_update`, nothing would
answer, and what ships is a white rectangle (invariant 7b).
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

#: Both roots the art tools read, in order. Two rather than one because the packs are not all
#: in one folder, and copying a licensed zip so a single path works is a second copy nothing
#: keeps in step — `make_hud_kit_art.found` says the same thing at more length.
SOURCES = [Path(r"C:\Users\Digikey\Downloads\2D ASSETS"),
           Path(r"C:\Users\Digikey\Downloads\to-assets")]

PACK = "craftpix-net-144512-vector-top-down-level-map-asset-pack.zip"
MEMBER = "Png/Arrow.png"
NAME = "ic_update"

#: The square the sprite is written at. The panel draws it at 230 canvas units, so 256 is the
#: nearest power of two above that — drawn slightly *down* rather than up, which is the side
#: of unity to be on. The source's long axis is 150px, so this is a 1.7x enlargement of flat
#: vector-derived art, which survives it; a photographic source would not.
SIZE = 256


def pack():
    """The licensed zip, or None when this machine does not have it."""
    for folder in SOURCES:
        path = folder / PACK
        if path.exists():
            return zipfile.ZipFile(path)
    return None


def cut(z):
    """The arrow, trimmed to its own alpha and centred in a square.

    Trimmed because the pack pads its artboards and an untrimmed sprite draws the arrow
    smaller than the box it was given — the fault `make_siege_art.one_canvas` records about a body framed
    around its own halo. Centred in a square so the panel can size it on one axis and get the
    same picture on every display.
    """
    art = Image.open(io.BytesIO(z.read(MEMBER))).convert("RGBA")

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
            # A glyph is never nine-sliced: it is scaled whole, so a border would stretch its
            # middle and pin its point.
            out.append("  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n")
        else:
            out.append(line)
    return "".join(out)


def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, "PNG")
    return buffer.getvalue()


def contact(image, path):
    """The mark on the panel's own parchment, at the size the panel draws it.

    The only thing that can say whether it reads — `--check` proves reproducibility and says
    nothing about quality, which is this project's rule about every art tool it has.
    """
    ground = Image.open(OUT / "panel_main.png").convert("RGBA")
    parchment = ground.getpixel((ground.width // 2, int(ground.height * .55)))[:3]

    sheet = Image.new("RGB", (860, 420), parchment)
    for i, drawn in enumerate((230, 150, 96)):
        fitted = image.resize((drawn, drawn), Image.LANCZOS)
        sheet.paste(parchment, (0, 0, 1, 1))
        sheet.paste(fitted.convert("RGB"), (60 + i * 260, (420 - drawn) // 2), fitted)

    sheet.save(path)
    print(f"wrote {path}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped PNG is what this writes; no files touched")
    ap.add_argument("--contact", type=Path, default=None,
                    help="draw the mark on the panel's ground at three sizes")
    args = ap.parse_args()

    target = OUT / f"{NAME}.png"
    z = pack()

    if z is None:
        # The bargain that keeps the gate runnable on a fresh checkout. A missing pack is not
        # a failure: what ships is the committed PNG, and it is committed precisely so that
        # building this game never depends on a licensed zip being on the machine.
        if target.exists():
            print(f"{PACK} is not on this machine; {target.name} stands as committed")
            return 0
        sys.exit(f"{PACK} is not on this machine and {target.name} is not committed either")

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

    print(f"wrote {target.name} ({SIZE}x{SIZE}) from {PACK}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
