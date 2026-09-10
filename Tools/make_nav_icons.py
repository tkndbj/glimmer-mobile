#!/usr/bin/env python3
"""Cut the nav bar's and the Battle key's glyphs out of Layer Lab's Casual Icon Pack.

The pack is an Asset Store `.unitypackage` sitting in Unity's own download cache rather than
anything in this repo, and that is deliberate: a `.unitypackage` unpacked under `Assets/`
would drop 45 files and a demo scene into the project to ship six pictures. A
`.unitypackage` is a gzipped tar of `<guid>/asset` + `<guid>/pathname`, so the six wanted
entries are read straight out of it and written where this game keeps its own glyphs.

**The pack is not in the repo, so this passes when it is absent** — `make_siege_art.py`'s
bargain, so a checkout without the cache still runs the gate. `--check` proves the six PNGs
are what the tool would write; `--contact` lays them out to be looked at, which is the only
thing that can say whether a glyph reads at the size a nav bar draws it.

**The `.meta` is written with a derived guid**, for `make_hud_kit_art.meta_for`'s reason: the
tool is reproducible and a re-cut keeps the address Addressables registered against it.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import sys
import tarfile
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/Game/Art/Ui"
TEMPLATE = OUT / "panel_main.png.meta"

LAYERLAB = (Path.home() / "AppData/Roaming/Unity/Asset Store-5.x/LAYERLAB"
            / "Textures MaterialsIcons UI")

CACHE = LAYERLAB / "2D Icons - Casual Icon Pack.unitypackage"
SHOP2 = LAYERLAB / "2D Icons - Shop Pack 2.unitypackage"

#: Which of the pack's fourteen icons each destination wears, and what this game calls it.
#:
#: The pack has no house and no person, which is why Home wears the world map and the profile
#: wears a record card — the two names most likely to be re-pointed, and re-pointing one is a
#: line here rather than anything in the game.
#:
#: 256 rather than 512: the largest of these is drawn at about 140 units, and 256 is already
#: comfortably above that. `ArtImportRules` caps this folder at 1024, so the choice is ours.
WANTED = {
    "ic_nav_shop":    "Icon_Materials_Gem02_Purple",
    "ic_nav_grove":   "Icon_Misc_Documents_Map01",
    "ic_nav_ranks":   "Icon_Collectibles_Trophy01_Gold",
    "ic_battle":      "Icon_Equipment_Weapon_Sword02",
    # The hub's streak box. A calendar rather than a flame: the count under it is nights in a
    # row, and the flipbook that used to stand there was the one animated thing on a screen of
    # still ones.
    "ic_streak":      "Icon_Misc_ETC_Calendar01",
}

#: **`ic_nav_home` and `ic_nav_profile` are not on that list and must not be put back.** The
#: pack has neither a house nor a person, so those two were the wrong-shaped stand-ins from the
#: first cut (a shopfront and a clipboard) and are now authored art checked in beside the rest.
#: Naming them here would make this tool overwrite them and `--check` fail on the difference.
AUTHORED = ("ic_nav_home", "ic_nav_profile")

#: And from Shop Pack 2, which is where the chests are. Its own folder tree, hence its own
#: table: `Sprites/Icons_Shop/2x` rather than `Icons/256`.
WANTED2 = {
    "ic_chest_wood": "chest_wood",
}

SIZE = "256"
SIZE2 = "2x"


def read_pack(pkg: Path, folder: str):
    """Every `*.png` under one folder of the package, by its own file stem."""
    found = {}
    with tarfile.open(pkg, "r:gz") as tar:
        names = {}
        for m in tar.getmembers():
            if not m.name.endswith("/pathname"):
                continue
            f = tar.extractfile(m)
            if f is None:
                continue
            path = f.read().decode("utf8").splitlines()[0].strip()
            names[m.name.rsplit("/", 1)[0]] = path

        for guid, path in names.items():
            if folder not in path or not path.endswith(".png"):
                continue
            try:
                f = tar.extractfile(guid + "/asset")
            except KeyError:
                continue
            if f is None:
                continue
            found[Path(path).stem] = Image.open(io.BytesIO(f.read())).convert("RGBA")
    return found


def trimmed(im):
    """The picture with its transparent margin taken off.

    The pack pads every icon to a square canvas, and the padding differs per icon — so a row
    of them drawn to one box comes out at a row of different sizes. Trimming to the ink and
    letting `preserveAspect` do the rest is what makes five glyphs read as one set.
    """
    box = im.getbbox()
    return im.crop(box) if box else im


def capped(im, most):
    """The picture no larger than `most` on its longest side."""
    if max(im.size) <= most:
        return im
    k = most / max(im.size)
    return im.resize((max(1, round(im.width * k)), max(1, round(im.height * k))), Image.LANCZOS)


def meta_for(name):
    text = TEMPLATE.read_text(encoding="utf8")
    guid = hashlib.md5(("glimmer.ui.casual." + name).encode("utf8")).hexdigest()

    out = []
    for line in text.splitlines(keepends=True):
        if line.startswith("guid: "):
            out.append(f"guid: {guid}\n")
        elif line.strip().startswith("spriteBorder:"):
            out.append("  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n")
        else:
            out.append(line)
    return "".join(out)


def contact(made, path, cell=260):
    """Every glyph at a common size, on the nav bar's own plate colour. Look at it."""
    names = sorted(made)
    sheet = Image.new("RGB", (len(names) * cell, cell + 22), (11, 66, 120))
    draw = ImageDraw.Draw(sheet)
    for i, name in enumerate(names):
        im = made[name]
        s = min((cell - 40) / im.width, (cell - 40) / im.height)
        one = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)
        sheet.paste(one, (i * cell + (cell - one.width) // 2, (cell - one.height) // 2), one)
        draw.text((i * cell + 6, cell + 4), f"{name} {im.size}", fill=(210, 228, 250))
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", type=Path, default=CACHE)
    ap.add_argument("--shop", type=Path, default=SHOP2)
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", type=Path, metavar="PNG")
    args = ap.parse_args()

    absent = [p for p in (args.source, args.shop) if not p.exists()]
    if absent:
        print("not in the Asset Store cache, nothing to do:")
        for p in absent:
            print(f"  {p}")
        return

    pack = read_pack(args.source, f"/Icons/{SIZE}/")
    shop = read_pack(args.shop, f"/Icons_Shop/{SIZE2}/")

    missing = ([src for src in WANTED.values() if src not in pack]
               + [src for src in WANTED2.values() if src not in shop])
    if missing:
        sys.exit("the pack(s) no longer carry: " + ", ".join(missing))

    made = {name: trimmed(pack[src]) for name, src in WANTED.items()}

    # Shop Pack 2 draws at 2x for a 1024 canvas; a chest here is 96 units, so it comes down to
    # the casual pack's own scale rather than shipping a 919-pixel picture of one.
    made.update({name: capped(trimmed(shop[src]), 256) for name, src in WANTED2.items()})

    stale = []
    for name, im in sorted(made.items()):
        png, meta = OUT / f"{name}.png", OUT / f"{name}.png.meta"
        buf = io.BytesIO()
        im.save(buf, "PNG", optimize=True)
        data, text = buf.getvalue(), meta_for(name)

        if args.check:
            if not png.exists() or png.read_bytes() != data:
                stale.append(f"Ui/{name}.png")
            elif not meta.exists() or meta.read_text(encoding="utf8") != text:
                stale.append(f"Ui/{name}.png (meta)")
            continue

        png.write_bytes(data)
        meta.write_text(text, encoding="utf8")
        print(f"  wrote Ui/{name}.png  {im.width}x{im.height}")

    if args.contact:
        contact(made, args.contact)
        print(f"  wrote {args.contact}  - look at it; --check cannot")

    if args.check:
        if stale:
            sys.exit("stale, re-run without --check:\n  " + "\n  ".join(stale))
        print(f"the nav glyphs are what the tool would write ({len(made)} sprites)")


if __name__ == "__main__":
    main()
