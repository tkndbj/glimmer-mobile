#!/usr/bin/env python3
"""Cut the chapter standing's trophy from the owner's bought icon pack (invariant 62).

One sprite into `Assets/Game/Art/Ui/`:

    ic_trophy_gold   Layer Lab's gold cup (2D Icons - Casual Icon Pack, `Icon_Collectibles_Trophy01_Gold`)

    python Tools/make_standing_art.py            # cut it, with its .meta
    python Tools/make_standing_art.py --check    # prove the shipped PNG is what this cuts

The owner asked for "a proper trophy" over the game's own flat `ic_trophy` glyph (2026-10-11), and
the pack the hall of ranks already draws its swords from carries one. The package is read the way
`make_rank_kit_art.py` reads it - a `.unitypackage` is a gzipped tar of `<guid>/asset` blobs with a
sibling `pathname`, so it opens with `tarfile` and needs no Editor - and the cut passes every gate
with the pack absent, because the PNG is committed. The `.meta` carries a derived guid, so two
machines cutting it agree about its identity.
"""

import argparse
import hashlib
import io
import os
import sys
import tarfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Ui"
TEMPLATE = OUT / "ic_update.png.meta"

STORE = Path(os.environ.get("APPDATA", r"C:\Users\Digikey\AppData\Roaming")) / "Unity" / "Asset Store-5.x"
ICON_PACKAGE = STORE / "LAYERLAB" / "Textures MaterialsIcons UI" / "2D Icons - Casual Icon Pack.unitypackage"

#: name -> (path inside the icon package, long edge in pixels). 192 for a sprite drawn 96 units
#: tall on a 1080 canvas, which is 2x on the densest phones this ships to and no more.
ICONS = {
    "ic_trophy_gold": ("Assets/Layer Lab/2D Icons-CasualIconPack/Icons/256/Icon_Collectibles_Trophy01_Gold.png", 192),
}


def package_files(path, wanted):
    """`pathname -> bytes` for the wanted paths only; the package is 40 MB of icons nobody asked for."""
    files = {}
    with tarfile.open(path, "r:gz") as tar:
        members = {}
        for m in tar.getmembers():
            parts = m.name.split("/")
            if len(parts) == 2:
                members.setdefault(parts[0], {})[parts[1]] = m
        for d in members.values():
            if "pathname" not in d or "asset" not in d:
                continue
            name = tar.extractfile(d["pathname"]).read().decode("utf8").splitlines()[0]
            if name in wanted:
                files[name] = tar.extractfile(d["asset"]).read()
    return files


def cut():
    """Every icon, or None when this machine never bought the pack."""
    from PIL import Image

    if not ICON_PACKAGE.exists():
        return None
    icons = package_files(ICON_PACKAGE, {path for path, _ in ICONS.values()})
    made = {}
    for name, (path, edge) in ICONS.items():
        if path not in icons:
            sys.exit(f"{name}: the icon pack has no {path}")
        icon = Image.open(io.BytesIO(icons[path])).convert("RGBA")
        icon = icon.crop(icon.getbbox())
        k = edge / max(icon.size)
        icon = icon.resize((max(1, int(round(icon.width * k))), max(1, int(round(icon.height * k)))), Image.LANCZOS)
        made[name] = icon
    return made


def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def meta(name):
    guid = hashlib.md5(("glimmer.ui.standing." + name).encode("utf8")).hexdigest()
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
            sys.exit("the icon pack is not on this machine and no committed PNG for: " + ", ".join(missing))
        print(f"standing art: no pack on this machine; the {len(ICONS)} committed PNG(s) stand")
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
        print("standing art: %d piece(s), reproducible" % len(made))
        return 0

    for name, piece in made.items():
        (OUT / f"{name}.png").write_bytes(png_bytes(piece))
        side = OUT / f"{name}.png.meta"
        if not side.exists():
            side.write_text(meta(name), encoding="utf8", newline="\n")
        print(f"  {name}: {piece.width}x{piece.height}")
    print(f"standing art: wrote {len(made)} piece(s) to {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
