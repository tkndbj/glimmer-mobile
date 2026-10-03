#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Draws the gravity well - the four pieces a Gravity Hole is stacked from on the hill - and cuts
its bar icon out of the owner's painting.

    python Tools/make_gravity_fx.py --write      # draw the pieces (and re-cut the icon if the
                                                 #   painting is where --source says it is)
    python Tools/make_gravity_fx.py --check      # prove the shipped PNGs are what this draws
    python Tools/make_gravity_fx.py --contact gravity.png   # the stack, on the hill's colour

**Drawn, and stacked at run time, for the strike kit's reason** (`make_strike_fx.py`, MODES.md
37eu): a black hole is light being bent, so what reads as one is several things turning at
different speeds over each other - and a baked reel of that is a photograph of light, the same
frame on every use. What ships is four pieces the board stacks and spins itself
(`SiegeView.Gravity.cs`):

* ``disc``  - the accretion disc seen face on: spiral bands, white-gold at the inner edge through
  orange and magenta to indigo at the rim. Additive. The board squashes it into a tilted ellipse
  and turns it, twice, at two speeds.
* ``arms``  - thin spiral filaments, the light falling in. Additive, turned faster than the disc.
* ``core``  - the horizon: a near-black ball with a violet limb. The one piece that is *not*
  additive, because the middle of a black hole is the one thing on this board that has to take
  light away.
* ``ring``  - the photon ring round the horizon, brighter on one side. Additive.

**The colours are the painting's**, read off the owner's picture rather than chosen: its disc runs
white-yellow, gold, orange, magenta, violet, indigo from the inside out, and so does this one, so
the thing on the bar and the thing on the hill are one object.

**Every piece is seamless in angle** - every band is an integer number of turns - because the
board rotates them for ever and a seam would come round once a turn.

**The icon and the void stone are cut, not drawn** (`Ui/Utility/gravityhole.png`,
`Siege/gem_void.png` - the stone a singularity field deals, `SiegeLayout.Singularity`): it is the owner's own artwork,
trimmed to its ink and fitted to the 256 square every utility icon is. The painting lives outside
the repo (`art-source-packs`' arrangement), so ``--check`` proves the four drawn pieces always and
the icon only when the painting can be found; the committed PNG is the artifact.

**Sizes follow the folder rule** (`ProjectSetup.Caps`, `/Art/Fx/` at 512, invariant 7d).
**The `.meta` carries a derived guid** (`make_rank_art.py`'s reason): Addressables keys an entry
on the guid, so a re-draw minting a fresh one would orphan the address.

Needs numpy and Pillow (this repo has no requirements.txt - see CLAUDE.md's Verifying section).
"""

from __future__ import annotations

import argparse
import hashlib
import io
import sys
from pathlib import Path

try:
    import numpy as np
    from PIL import Image
except ImportError:
    sys.exit("make_gravity_fx: needs numpy and Pillow.")

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Art" / "Fx" / "Gravity"
ICON = REPO / "Assets" / "Game" / "Art" / "Ui" / "Utility" / "gravityhole.png"

#: The void stone a singularity field deals (`SiegeLayout.Singularity`): the same painting, cut
#: to the size and the room every gem on the field is cut at (`Art/Siege/gem_obsidian.png`).
GEM = REPO / "Assets" / "Game" / "Art" / "Siege" / "gem_void.png"
GEM_META_TEMPLATE = REPO / "Assets" / "Game" / "Art" / "Siege" / "gem_obsidian.png.meta"

META_TEMPLATE = REPO / "Assets" / "Game" / "Art" / "Siege" / "web.png.meta"
ICON_META_TEMPLATE = REPO / "Assets" / "Game" / "Art" / "Ui" / "Utility" / "firepot.png.meta"

#: Where the owner's painting is looked for when `--source` is not given.
PAINTING = Path.home() / "Downloads" / "Neon Purple and Gold Black Hole.png"

#: What every utility icon is written at (`make_utility_art.SIZE`), and the air it keeps.
ICON_SIZE, ICON_PAD = 256, 0.03

#: The painting's own ramp, inner edge to rim, as (radius share, r, g, b).
RAMP = (
    (0.00, 255, 252, 226),
    (0.16, 255, 214, 92),
    (0.36, 255, 138, 48),
    (0.56, 255, 92, 214),
    (0.78, 132, 72, 255),
    (1.00, 52, 30, 196),
)


# --------------------------------------------------------------------------- helpers
def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def polar(size):
    """Radius (0 at the middle, 1 at the edge of the square) and angle for every pixel."""
    axis = (np.arange(size, dtype=np.float64) + 0.5) / size * 2.0 - 1.0
    x, y = np.meshgrid(axis, axis)
    return np.sqrt(x * x + y * y), np.arctan2(y, x)


def ramp(t):
    """`RAMP` sampled at `t` (0..1), as three float arrays."""
    stops = np.array([s[0] for s in RAMP])
    out = []
    for channel in (1, 2, 3):
        out.append(np.interp(t, stops, np.array([s[channel] for s in RAMP], dtype=np.float64)))
    return out


def image(r, g, b, a):
    stack = np.stack([np.clip(c, 0.0, 255.0) for c in (r, g, b, a * 255.0)], axis=-1)
    return Image.fromarray(np.rint(stack).astype(np.uint8), "RGBA")


# --------------------------------------------------------------------------- the pieces
def disc(size=512):
    """The accretion disc, face on. Bands wound on a logarithmic spiral, so turning it reads as
    matter going round and *in* rather than as a wheel."""
    r, th = polar(size)
    wind = np.log(np.maximum(r, 1e-4))

    inner, outer = 0.27, 0.985
    body = smooth(inner, inner + 0.07, r) * (1.0 - smooth(0.52, outer, r)) ** 1.35

    # Three families of band at whole numbers of turns (a seam would come round once a turn).
    wide = 0.5 + 0.5 * np.sin(3.0 * th + 8.5 * wind)
    mid = 0.5 + 0.5 * np.sin(7.0 * th + 15.0 * wind + 1.3)
    fine = 0.5 + 0.5 * np.sin(16.0 * th + 27.0 * wind + 2.1)
    bands = (0.50 * wide + 0.32 * mid + 0.18 * fine) ** 1.45

    # The lanes a disc is combed into, running round it.
    lanes = 0.86 + 0.14 * np.sin(74.0 * r + 2.0 * np.sin(2.0 * th))

    # A hot lip at the inner edge: the part nearest the horizon is the brightest thing here.
    lip = np.exp(-((r - (inner + 0.055)) / 0.045) ** 2)

    alpha = np.clip(body * (0.30 + 0.70 * bands) * lanes + 0.55 * lip * body, 0.0, 1.0)

    t = np.clip((r - inner) / (outer - inner), 0.0, 1.0)
    red, green, blue = ramp(t ** 0.82)
    return image(red, green, blue, alpha)


def arms(size=512):
    """Filaments of light falling in: five thin arms on a tighter spiral, bright at the horizon
    and gone by the rim."""
    r, th = polar(size)
    wind = np.log(np.maximum(r, 1e-4))
    count, twist = 5, 2.9

    # Angular distance to the nearest arm, as an arc length so an arm is as thick at the rim as
    # at the middle before the taper is applied.
    phase = (th - twist * wind) * count
    gap = np.abs(np.arctan2(np.sin(phase), np.cos(phase))) / count * r

    taper = 0.004 + 0.030 * np.clip(1.0 - r, 0.0, 1.0) ** 1.2
    thread = np.exp(-(gap / taper) ** 2)
    glow = 0.30 * np.exp(-(gap / (taper * 3.4)) ** 2)

    reach = smooth(0.20, 0.30, r) * (1.0 - smooth(0.42, 0.98, r)) ** 1.1
    alpha = np.clip((thread + glow) * reach, 0.0, 1.0)

    # White where the thread is, violet where it is only glow.
    heat = np.clip(thread, 0.0, 1.0)
    red = 150.0 + 105.0 * heat
    green = 96.0 + 150.0 * heat
    blue = 255.0 + 0.0 * heat
    return image(red, green, blue, alpha)


def core(size=256):
    """The horizon. Near-black with a violet limb, and a soft edge so it sits *in* the disc
    rather than on it. Ordinary alpha: this is the piece that takes light away."""
    r, _ = polar(size)

    alpha = 1.0 - smooth(0.80, 0.985, r)
    limb = smooth(0.45, 0.93, r) ** 2.0

    red = 5.0 + 46.0 * limb
    green = 2.0 + 14.0 * limb
    blue = 12.0 + 104.0 * limb
    return image(red, green, blue, alpha)


def ring(size=256):
    """The photon ring: a hot thread round the horizon inside a wider glow, brighter on the side
    the disc is turning toward."""
    r, th = polar(size)
    at = 0.70

    thread = np.exp(-((r - at) / 0.022) ** 2)
    glow = 0.42 * np.exp(-((r - at) / 0.085) ** 2)
    beam = 0.72 + 0.28 * np.cos(th - 2.4)

    alpha = np.clip((thread + glow) * beam, 0.0, 1.0)

    red = 255.0 + 0.0 * thread
    green = 190.0 + 62.0 * thread
    blue = 70.0 + 150.0 * thread
    return image(red, green, blue, alpha)


#: The pieces this tool draws. `GravityFx.All` (Domain) names the same four, and
#: `GravityFxTests` holds the two lists together.
CUTS = {
    "disc": disc,
    "arms": arms,
    "core": core,
    "ring": ring,
}


# --------------------------------------------------------------------------- the icon
def fitted(painting, size, pad):
    """The owner's painting, trimmed to its ink and fitted to a square of `size`."""
    art = Image.open(painting).convert("RGBA")
    box = art.split()[3].point(lambda v: 255 if v > 8 else 0).getbbox()
    art = art.crop(box)

    room = int(round(size * (1.0 - 2.0 * pad)))
    scale = room / max(art.width, art.height)
    art = art.resize((max(1, round(art.width * scale)), max(1, round(art.height * scale))),
                     Image.LANCZOS)

    sheet = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sheet.alpha_composite(art, ((size - art.width) // 2, (size - art.height) // 2))
    return sheet


def icon(painting):
    """The bar icon: the painting at the size every utility icon is."""
    return fitted(painting, ICON_SIZE, ICON_PAD)


def gem(painting):
    """The void stone: the painting at whatever size the field's other stone is cut at, so the
    two sit in a socket alike."""
    size = Image.open(GEM_META_TEMPLATE.with_suffix("")).width
    return fitted(painting, size, 0.02)


# --------------------------------------------------------------------------- io
def png_bytes(img):
    buffer = io.BytesIO()
    img.save(buffer, "PNG", optimize=True)
    return buffer.getvalue()


def guid_for(name):
    return hashlib.md5(("glimmer.fx.gravity." + name).encode("utf8")).hexdigest()


def meta_from(template, name):
    out = []
    for line in template.read_text(encoding="utf8").splitlines(keepends=True):
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


def write(made, painted):
    OUT.mkdir(parents=True, exist_ok=True)
    OUT.with_suffix(".meta").write_text(FOLDER_META.format(guid=guid_for("folder")),
                                        encoding="utf8", newline="\n")

    for name, img in sorted(made.items()):
        (OUT / f"{name}.png").write_bytes(png_bytes(img))
        (OUT / f"{name}.png.meta").write_text(meta_from(META_TEMPLATE, name),
                                              encoding="utf8", newline="\n")
        print(f"  {name:6s} {img.width}x{img.height}")

    if painted is not None:
        ICON.write_bytes(png_bytes(painted))
        Path(str(ICON) + ".meta").write_text(meta_from(ICON_META_TEMPLATE, "icon"),
                                             encoding="utf8", newline="\n")
        print(f"  icon   {painted.width}x{painted.height}  -> {ICON.relative_to(REPO)}")

        stone = gem(SOURCE[0])
        GEM.write_bytes(png_bytes(stone))
        Path(str(GEM) + ".meta").write_text(meta_from(GEM_META_TEMPLATE, "gem"),
                                            encoding="utf8", newline="\n")
        print(f"  gem    {stone.width}x{stone.height}  -> {GEM.relative_to(REPO)}")
    else:
        print("  icon   not re-cut: the painting was not found (pass --source)")

    print(f"wrote {len(made)} pieces under Assets/Game/Art/Fx/Gravity")
    print("the Editor has to address them (invariant 7a): with it open the importer does; with "
          "it closed, Glimmer Grove > Addressables > Sync All Assets, AND SAVE")


def check(made, painted):
    missing, differ = [], []

    for name, img in sorted(made.items()):
        target = OUT / f"{name}.png"
        if not target.exists():
            missing.append(name)
        elif target.read_bytes() != png_bytes(img):
            differ.append(name)

    if not ICON.exists():
        missing.append("icon")
    elif painted is not None and ICON.read_bytes() != png_bytes(painted):
        differ.append("icon")

    if not GEM.exists():
        missing.append("gem")
    elif painted is not None and GEM.read_bytes() != png_bytes(gem(SOURCE[0])):
        differ.append("gem")

    if missing or differ:
        for name in missing:
            print(f"missing: {name}")
        for name in differ:
            print(f"differs: {name}")
        sys.exit(f"{len(missing)} missing, {len(differ)} differ - re-run with --write")

    print(f"{len(made)} pieces are what this tool draws"
          + ("; the icon is what it cuts" if painted is not None
             else "; the icon is on disk (painting not found, so not re-cut)"))


def stack(made, size, turn, tilt=0.56, lean=-18.0):
    """The well as the board stacks it, `turn` degrees into its spin: two discs and the arms
    squashed into a tilted ellipse and added, the horizon over them, the ring over that, and the
    near half of the disc drawn again in front of the horizon."""
    def layer(name, degrees, scale, squash):
        img = made[name].rotate(degrees, resample=Image.BICUBIC)
        w = int(size * scale)
        img = img.resize((w, max(1, int(w * squash))), Image.LANCZOS)
        img = img.rotate(lean, resample=Image.BICUBIC, expand=True)

        # Never wider than the tile it is drawn into: a lean widens the box.
        if img.width > size or img.height > size:
            x, y = max(0, (img.width - size) // 2), max(0, (img.height - size) // 2)
            img = img.crop((x, y, x + min(size, img.width), y + min(size, img.height)))

        return img

    def add(canvas, img, gain=1.0):
        arr = np.asarray(img, dtype=np.float64)
        x = (canvas.shape[1] - img.width) // 2
        y = (canvas.shape[0] - img.height) // 2
        canvas[y:y + img.height, x:x + img.width] += arr[..., :3] * (arr[..., 3:4] / 255.0) * gain

    def over(canvas, img):
        arr = np.asarray(img, dtype=np.float64)
        x = (canvas.shape[1] - img.width) // 2
        y = (canvas.shape[0] - img.height) // 2
        a = arr[..., 3:4] / 255.0
        part = canvas[y:y + img.height, x:x + img.width]
        part *= (1.0 - a)
        part += arr[..., :3] * a

    def front(img):
        """The half of a tilted layer nearer the eye: everything below its own long axis."""
        arr = np.array(img)
        h, w = arr.shape[:2]
        yy, xx = np.mgrid[0:h, 0:w]
        slope = np.tan(np.radians(-lean))
        below = (yy - h / 2.0) > (xx - w / 2.0) * slope
        arr[..., 3] = np.where(below, arr[..., 3], 0)
        return Image.fromarray(arr, "RGBA")

    canvas = np.zeros((size, size, 3), dtype=np.float64)
    canvas[:] = (74, 96, 66)                       # the hill's own plate colour
    canvas *= 0.55                                 # under the veil a well lays over the hill

    back = [("disc", turn, 0.90, tilt, 1.0), ("disc", -turn * 0.6 + 40.0, 0.72, tilt, 0.7),
            ("arms", turn * 2.2, 0.84, tilt, 0.9)]

    for name, degrees, scale, squash, gain in back:
        add(canvas, layer(name, degrees, scale, squash), gain)

    ball = int(size * 0.30)
    over(canvas, made["core"].resize((ball, ball), Image.LANCZOS))
    halo = int(ball * 1.36)
    add(canvas, made["ring"].resize((halo, halo), Image.LANCZOS))

    for name, degrees, scale, squash, gain in back[:1]:
        add(canvas, front(layer(name, degrees, scale, squash)), gain)

    return Image.fromarray(np.clip(canvas, 0, 255).astype(np.uint8), "RGB")


def contact(made, path):
    """Four beats of the spin side by side, then each piece alone. Look at it: `--check` proves
    reproducibility and says nothing about whether it reads as a black hole."""
    cell = 420
    names = list(CUTS)
    sheet = Image.new("RGB", (cell * 4, cell * 2), (40, 53, 37))

    for i, turn in enumerate((0.0, 35.0, 70.0, 105.0)):
        sheet.paste(stack(made, cell, turn), (i * cell, 0))

    for i, name in enumerate(names):
        tile = Image.new("RGB", (cell, cell), (74, 96, 66) if name == "core" else (16, 20, 28))
        piece = made[name].resize((cell - 40, cell - 40), Image.LANCZOS)
        tile.paste(piece, (20, 20), piece)
        sheet.paste(tile, (i * cell, cell))

    Path(path).parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)
    print(f"wrote {path} - look at it; --check cannot")


#: The painting `main` was handed, for the two cuts made from it.
SOURCE = [None]


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", metavar="PNG")
    ap.add_argument("--source", type=Path, default=PAINTING,
                    help="the owner's black hole painting, for the bar icon")
    args = ap.parse_args()

    if not (args.write or args.check or args.contact):
        ap.error("pass --write, --check or --contact")

    made = {name: draw() for name, draw in CUTS.items()}
    painted = icon(args.source) if args.source and args.source.exists() else None
    SOURCE[0] = args.source

    if args.write:
        write(made, painted)
    if args.check:
        check(made, painted)
    if args.contact:
        contact(made, args.contact)


if __name__ == "__main__":
    main()
