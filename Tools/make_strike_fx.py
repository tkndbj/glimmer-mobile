#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Cuts the strike kit - the light a stormcall and an overcharge are drawn with - out of the
owner's bought lightning pack.

    python Tools/make_strike_fx.py --write     # cut them
    python Tools/make_strike_fx.py --check     # prove the shipped PNGs are what this cuts
    python Tools/make_strike_fx.py --contact   # one sheet, every piece on the hill's plate colour

**What this is for.** The stormcall used to be one baked reel: `SiegeShotBake` stood the pack's
whole prefab on a stage, photographed it twenty times from a tilted camera and the board played
the photographs back, alpha-blended, as a 304-pixel strip stretched five cells tall. The owner's
verdict on that was that it looked nothing like the pack it was bought from, and the reason is
not the bake - it is that a photograph of light is not light. The pack is *additive*: every
strand, flare and ring is authored to be drawn `One One` over its neighbours, so where two overlap
the picture goes white, and a bolt is bright because six things are stacked on it. A baked frame
flattens that into one opaque picture and `Image.color` can then only ever darken it (invariant
37l). What ships instead is the pack's own *pieces* - its flares, its ring, its shockwave, its
crack, its painted strand - as white coverage masks the board stacks additively at run time
(`Assets/Game/Shaders/UIAdditive.shader`, `Additive.cs`) around a bolt it draws for itself
(`Lightning.cs`). The stack is what makes it look like the store, and the pieces are what this file
cuts.

**Why the pieces and not the prefab** (`bake-vfx-packs-to-flipbooks`, the second route). The
picture wanted is lying in the pack's `Textures/` folder; what was bought was the *look*, and the
motion is a strike, which is the one motion this board already knows how to make. A reel of the
prefab would also be a stamp - the same bolt on every raider - which is the fault the boss storm's
procedural lightning was written to avoid (37ac), and a storm falls on up to nine.

**Where they come from, and why the tool reads the package itself.** The pack is a Unity Asset
Store purchase (Mirza Beig, *Lightning VFX*), and the Asset Store window keeps a copy of it at
`%APPDATA%\\Unity\\Asset Store-5.x\\Mirza Beig\\Particle Systems\\Lightning VFX BiRP.unitypackage`.
A `.unitypackage` is a gzipped tar of `<guid>/asset` blobs with a sibling `pathname`, so it opens
with `tarfile` and needs no Editor (`make_rank_kit_art.py`'s finding), and the imported copy under
`Assets/Mirza Beig/` is gitignored - it is a bake input, never a shipped asset. This runs against
the package first and the imported folder second, and **passes with neither**: the PNGs are
committed, so a fresh clone runs every gate without the Asset Store cache.

**Every piece is a white coverage mask** - RGB flat white, the whole shape in alpha - and
**alpha is normalised to full range**, which is `make_bud_fx.py`'s bargain twice over: a mask
tints cleanly under `Image.color` in both the additive material and the ordinary one, and a
sheet authored for an additive particle material peaks well under 255 (the ring at 252, the
star at 223) because the material was going to add it several times over. The pack's shapes are
carried two ways and this file reads both: a texture whose alpha is flat holds its shape in
luminance (the flares, the ring, the shockwave), and one whose alpha varies holds it there and
is white everywhere it is drawn (the two PSDs and the star).

**Sizes follow the folder rule** (`ProjectSetup.Caps`, `/Art/Fx/` at 512, invariant 7d): the
importer would cap a 1024-tall strand at 512 anyway, so it is cut at 512 here and disk is what
ships. The streak is trimmed to its lit rows, because a 512-square holding a 60-row streak is
eight times the memory of the picture in it.

**The `.meta` carries a derived guid** (`make_rank_art.py`'s reason): Addressables keys every
entry on the guid, so a re-cut minting a fresh one would orphan the address. The folder's meta is
written the same way so the Editor does not mint one.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import os
import sys
import tarfile
from pathlib import Path

try:
    import numpy as np
    from PIL import Image
except ImportError:
    sys.exit("make_strike_fx: needs numpy and Pillow (this repo has no requirements.txt - see "
             "CLAUDE.md's Verifying section).")

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Art" / "Fx" / "Strike"
META_TEMPLATE = REPO / "Assets" / "Game" / "Art" / "Siege" / "web.png.meta"

STORE = Path(os.environ.get("APPDATA", r"C:\Users\Digikey\AppData\Roaming")) / "Unity" / "Asset Store-5.x"
PACKAGE = STORE / "Mirza Beig" / "Particle Systems" / "Lightning VFX BiRP.unitypackage"
IMPORTED = REPO / "Assets" / "Mirza Beig" / "Lightning VFX" / "Textures"
PACK_TEXTURES = "Assets/Mirza Beig/Lightning VFX/Textures/"

#: What is cut: our name -> (the pack's file, the longest edge it ships at).
#:
#: The names are what `StrikeFx` (Domain) asks for and `SiegeView.Strike` draws; a name changed
#: here is changed there in the same commit (the fixture `SiegeArtTests.EveryStrikePieceIsCut`
#: holds the two together). The pack's own names say what each was for and are kept in the
#: comments, because the pack's `Textures/` folder holds shapes and utility maps under one
#: naming scheme and the wrong pick draws perfectly (`vfx-assets-must-be-2d`).
CUTS = {
    "bolt":   ("Lightning 1.psd", 512),   # the painted strand: a trunk with forks and tendrils
    "flare":  ("Flare 3.png", 512),       # six soft rays round a bright disc - the strike's flash
    "glint":  ("Flare 2.png", 256),       # four hard rays through a ring - the impact's twinkle
    "streak": ("Flare 1.png", 512),       # an anamorphic streak - the flash across the ground
    "ring":   ("Ring.png", 512),          # a thin ring - the shockwave, squashed to the hill
    "wave":   ("Shockwave.png", 512),     # a radial burst - the ground answering
    "crack":  ("Shatter.psd", 512),       # the crazing a bolt leaves in the ground
    "star":   ("Star.psd", 256),          # a soft four-point star - sparks and twinkles
}

#: **There was a ninth, `splat`** (`Ground Crack.psd`, the spiked splash an overcharge left on
#: the ground). Withdrawn on 2026-09-29 with its PNG, its `.meta` and its Addressables row
#: (invariant 8d), when the overcharge stopped drawing on the ground (MODES.md 37ev).

#: **The pack's two PSDs are named the wrong way round for what they draw**: `Shatter.psd` is
#: the web of cracks and `Ground Crack.psd` is the spiked splash. The first cut of this file
#: trusted the names and shipped them swapped; the contact sheet is what said so, which is the
#: whole reason it exists (`vfx-assets-must-be-2d`: look at the sheet, `--check` cannot).

#: Rows fainter than this at the streak's edges are trimmed away. High, deliberately: the
#: streak sits in a faint halo that reaches most of its 512 rows, and the halo is what the
#: board's own glow already draws under it.
TRIM_FLOOR = 40


# ------------------------------------------------------------------ the package
def package_files(path):
    """Every `pathname -> bytes` a unitypackage holds."""
    files = {}
    with tarfile.open(path, "r:gz") as tar:
        members = {}
        for m in tar.getmembers():
            parts = m.name.split("/")
            if len(parts) < 2:
                continue
            members.setdefault(parts[0], {})[parts[1]] = m
        for guid, d in members.items():
            if "pathname" not in d or "asset" not in d:
                continue
            name = tar.extractfile(d["pathname"]).read().decode("utf8").splitlines()[0]
            files[name] = tar.extractfile(d["asset"]).read()
    return files


def source_bytes():
    """Each pack texture's bytes by file name, from the package or the imported copy, or None."""
    if PACKAGE.exists():
        files = package_files(PACKAGE)
        found = {name: files[PACK_TEXTURES + name] for name, _ in CUTS.values()
                 if PACK_TEXTURES + name in files}
        if len(found) == len(CUTS):
            return found
    if IMPORTED.is_dir():
        found = {}
        for name, _ in CUTS.values():
            path = IMPORTED / name
            if path.exists():
                found[name] = path.read_bytes()
        if len(found) == len(CUTS):
            return found
    return None


# ------------------------------------------------------------------ the cut
def coverage(image):
    """The shape of a pack texture as one 0..255 channel, normalised to full range.

    A flat alpha means the shape is the luminance (an additive sheet on black); a varying one
    means the shape *is* the alpha and the colour under it is white. Nothing here guesses from
    the file name.
    """
    a = np.asarray(image.convert("RGBA")).astype(np.float32)
    alpha = a[..., 3]
    if alpha.min() == alpha.max():
        shape = a[..., :3].mean(-1)
    else:
        shape = alpha
    peak = float(shape.max())
    if peak <= 0:
        return np.zeros(shape.shape, np.uint8)
    out = shape * (255.0 / peak)
    out[out < 2] = 0
    return np.clip(out + .5, 0, 255).astype(np.uint8)


def mask(shape):
    """A white sprite whose alpha is `shape`."""
    h, w = shape.shape
    rgba = np.empty((h, w, 4), np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = shape
    return Image.fromarray(rgba, "RGBA")


def trimmed_rows(shape):
    """The streak: its lit rows only, with two rows of air."""
    lit = np.where(shape.max(axis=1) > TRIM_FLOOR)[0]
    if len(lit) == 0:
        return shape
    top = max(0, int(lit[0]) - 2)
    bottom = min(shape.shape[0], int(lit[-1]) + 3)
    # An even height, so the streak's middle row lands on a pixel centre when it is drawn.
    if (bottom - top) % 2:
        bottom = min(shape.shape[0], bottom + 1)
    return shape[top:bottom]


def fitted(image, longest):
    w, h = image.size
    if max(w, h) <= longest:
        return image
    k = longest / max(w, h)
    return image.resize((max(1, round(w * k)), max(1, round(h * k))), Image.LANCZOS)


def cut(sources):
    made = {}
    for name, (file, longest) in CUTS.items():
        image = Image.open(io.BytesIO(sources[file]))
        shape = coverage(image)
        if name == "streak":
            shape = trimmed_rows(shape)
        made[name] = fitted(mask(shape), longest)
    return made


# ------------------------------------------------------------------ writing
def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def guid_for(name):
    return hashlib.md5(("glimmer.fx.strike." + name).encode("utf8")).hexdigest()


def meta_for(name):
    """The project's own importer settings for a single sprite, under a derived guid."""
    out = []
    for line in META_TEMPLATE.read_text(encoding="utf8").splitlines(keepends=True):
        out.append(f"guid: {guid_for(name)}\n" if line.startswith("guid: ") else line)
    return "".join(out)


FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def write(made):
    OUT.mkdir(parents=True, exist_ok=True)
    folder_meta = OUT.with_suffix(".meta")
    folder_meta.write_text(FOLDER_META.format(guid=guid_for("folder")), encoding="utf8", newline="\n")

    for name, image in sorted(made.items()):
        (OUT / f"{name}.png").write_bytes(png_bytes(image))
        (OUT / f"{name}.png.meta").write_text(meta_for(name), encoding="utf8", newline="\n")
        print(f"  {name:8s} {image.width}x{image.height}")

    print(f"wrote {len(made)} pieces under Assets/Game/Art/Fx/Strike")
    print("the Editor has to address them: Glimmer Grove > Addressables > Sync All Assets, "
          "AND SAVE (invariant 7a) - until then a strike draws its bolt and none of its light (7b)")


def check(made):
    missing, differ = [], []
    for name, image in sorted(made.items()):
        target = OUT / f"{name}.png"
        if not target.exists():
            missing.append(name)
        elif target.read_bytes() != png_bytes(image):
            differ.append(name)
        meta = OUT / f"{name}.png.meta"
        if meta.exists() and meta.read_text(encoding="utf8") != meta_for(name):
            differ.append(name + ".meta")
    if missing or differ:
        for name in missing:
            print(f"missing: {name}")
        for name in differ:
            print(f"differs: {name}")
        sys.exit(f"{len(missing)} missing, {len(differ)} differ - re-run with --write")
    print(f"{len(made)} pieces are what this tool cuts")


def shipped():
    """The committed PNGs, for a contact sheet on a machine with no pack."""
    made = {}
    for name in CUTS:
        path = OUT / f"{name}.png"
        if path.exists():
            made[name] = Image.open(path).convert("RGBA")
    return made


def contact(made, path):
    """Every piece twice: alpha-blended in the storm's gold, and stacked additively over itself
    three times, which is roughly what the board does with it. On the hill's own plate colour,
    because a white mask on white says nothing."""
    plate = (74, 96, 66)
    gold = (255, 201, 60)
    cell = 300
    names = list(CUTS)
    sheet = Image.new("RGB", (cell * len(names), cell * 2 + 40), plate)

    from PIL import ImageDraw
    pen = ImageDraw.Draw(sheet)

    for i, name in enumerate(names):
        im = made.get(name)
        if im is None:
            continue
        fit = im.copy()
        fit.thumbnail((cell - 24, cell - 24))
        x = i * cell + (cell - fit.width) // 2
        y = (cell - fit.height) // 2 + 40

        # Row one: `Image.color` - a multiply, so the mask wears the gold and nothing more.
        tint = Image.new("RGBA", fit.size, gold + (255,))
        tint.putalpha(fit.getchannel("A"))
        sheet.paste(tint, (x, y), tint)

        # Row two: `Blend SrcAlpha One`, three times over - the stack the board draws.
        base = np.asarray(sheet.crop((x, y + cell, x + fit.width, y + cell + fit.height))).astype(np.int32)
        a = np.asarray(fit.getchannel("A")).astype(np.int32)[..., None]
        add = np.array(gold, np.int32)[None, None, :] * a // 255
        for _ in range(3):
            base = np.clip(base + add, 0, 255)
        sheet.paste(Image.fromarray(base.astype(np.uint8), "RGB"), (x, y + cell))

        pen.text((i * cell + 12, 10), f"{name}  {im.width}x{im.height}", fill=(240, 236, 220))

    sheet.save(path)
    print(f"wrote {path}")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--write", action="store_true", help="cut the pieces into Assets/Game/Art/Fx/Strike")
    ap.add_argument("--check", action="store_true", help="prove the shipped PNGs are what this cuts")
    ap.add_argument("--contact", type=Path, nargs="?", const=REPO / "Tools" / "out" / "strike_fx.png",
                    metavar="PNG", help="write the contact sheet")
    args = ap.parse_args()

    if not (args.write or args.check or args.contact):
        ap.print_help()
        return 2

    sources = source_bytes()
    made = cut(sources) if sources else None

    if args.write:
        if made is None:
            sys.exit(f"the pack is not on this machine ({PACKAGE}) and Assets/Mirza Beig is not imported")
        write(made)
    if args.check:
        if made is None:
            print("no pack on this machine - the shipped PNGs are what ships; nothing to compare against")
        else:
            check(made)
    if args.contact:
        args.contact.parent.mkdir(parents=True, exist_ok=True)
        contact(made or shipped(), args.contact)
    return 0


if __name__ == "__main__":
    sys.exit(main())
