#!/usr/bin/env python3
"""Cut the armoured raider's shield mark from the owner's bought RPG UI pack.

One sprite into `Assets/Game/Art/Siege/`:

    armour   CraftPix's Cartoon 2D RPG Game UI (craftpix-199100), `Game Icon - Defense`

    python Tools/make_armour_art.py            # cut it, with its .meta
    python Tools/make_armour_art.py --check    # prove the shipped PNG is what this cuts

`SiegeView` draws it beside the health bar of every raider that arrives armoured - a brute and a
bulwark (`SiegeView.Armoured`) - on the side of the bar opposite the colour gem, so the two
readouts a raider carries never overlap. The owner asked for "a shield icon, something proper"
(2026-10-15): this pack's mark is a steel face in a gold rim inside a dark keyline, which is the
silhouette that still reads at the forty-odd units the hill draws it at.

**Never upscaled.** The pack draws the shield 140 x 146 inside a 160 square; the cut is that box
and no larger, because a raster blown up past its own pixels is a blur, and 146 px is already
past 2x for the size it is drawn. The EPS beside it would scale cleanly but wants a PostScript
interpreter this machine does not have.

The zip is read where it was downloaded (a root per download, never a copy), and the cut passes
every gate with the pack absent, because the PNG is committed. The `.meta` carries a derived
guid, so two machines cutting it agree about its identity. The licence (craftpix.net/file-licenses)
asks for no attribution (invariant 46).
"""

import argparse
import hashlib
import io
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Siege"
TEMPLATE = OUT / "charge.png.meta"

PACK = Path.home() / "Downloads" / "2D ASSETS" / "craftpix-199100-cartoon-2d-rpg-game-ui.zip"

#: name -> (path inside the pack, the longest edge it may be cut to).
ICONS = {
    "armour": ("PNG/Game Icon/Cartoon RPG UI_Game Icon - Defense.png", 192),
}


def cut():
    """Every icon, or None when this machine never downloaded the pack."""
    from PIL import Image

    if not PACK.exists():
        return None
    made = {}
    with zipfile.ZipFile(PACK) as pack:
        names = set(pack.namelist())
        for name, (path, edge) in ICONS.items():
            if path not in names:
                sys.exit(f"{name}: the pack has no {path}")
            icon = Image.open(io.BytesIO(pack.read(path))).convert("RGBA")
            icon = icon.crop(icon.getbbox())
            k = min(1.0, edge / max(icon.size))
            if k < 1.0:
                icon = icon.resize((max(1, int(round(icon.width * k))),
                                    max(1, int(round(icon.height * k)))), Image.LANCZOS)
            made[name] = icon
    return made


def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def meta(name):
    guid = hashlib.md5(("glimmer.siege.armour." + name).encode("utf8")).hexdigest()
    out = []
    for line in TEMPLATE.read_text(encoding="utf8").splitlines(keepends=True):
        out.append(f"guid: {guid}\n" if line.startswith("guid: ") else line)
    return "".join(out)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="prove the shipped PNG is what this tool cuts")
    args = parser.parse_args()

    made = cut()
    if made is None:
        missing = [n for n in ICONS if not (OUT / f"{n}.png").exists()]
        if missing:
            sys.exit("the RPG UI pack is not on this machine and no committed PNG for: " + ", ".join(missing))
        print(f"armour art: no pack on this machine; the {len(ICONS)} committed PNG(s) stand")
        return 0

    if args.check:
        bad = []
        for name, piece in made.items():
            png = OUT / f"{name}.png"
            if not png.exists():
                bad.append(f"{name}.png is missing")
            elif png.read_bytes() != png_bytes(piece):
                bad.append(f"{name}.png is not what this tool cuts")
            if not (OUT / f"{name}.png.meta").exists():
                bad.append(f"{name}.png.meta is missing")
        if bad:
            for line in bad:
                print(line, file=sys.stderr)
            print("re-run without --check", file=sys.stderr)
            return 1
        print("armour art: %d piece(s), reproducible" % len(made))
        return 0

    for name, piece in made.items():
        (OUT / f"{name}.png").write_bytes(png_bytes(piece))
        side = OUT / f"{name}.png.meta"
        if not side.exists():
            side.write_text(meta(name), encoding="utf8", newline="\n")
        print(f"  {name}: {piece.width}x{piece.height}")
    print(f"armour art: wrote {len(made)} piece(s) to {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
