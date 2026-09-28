#!/usr/bin/env python3
"""Cut the hall of ranks' furniture from the owner's bought UI kits.

Five sprites into `Assets/Game/Art/Ui/Rank/`, four of them nine-sliced plates and one icon:

    kit_board   the notice board the requirement lines stand on ("what it asks")
    kit_row     the dark navy bar one requirement line is written on
    kit_seat    the dark blue-rimmed square a rung's badge sits in on the rail
    kit_chip    the navy pill the ordinal ("RANK 3") is written on
    ic_raiders  the crossed swords beside "Defeat N raiders" (Layer Lab's casual icon pack)

**The board is the cartoon kit's, since 2026-09-28** (the owner: "the What It Asks board is
bad"). The 300Mind kit's notched cyan panel read as a sticker from another game on this wall,
so the board is now `Artboard 20` of the CraftPix *Cartoon UI Elements Mini Kit* - the same kit
every plate, key and ribbon in the game is cut from (`make_hud_kit_art.py`, invariant 44): a
cream notice board under a riveted orange cap on a plinth. Its own ribbon is **lifted off**,
because a ribbon in the middle of a nine-slice's top edge is stretched with the board; the
columns it covers are found by comparing each against a plain column of the cap and are
painted from that column, and the title is written on `Hud/title` - the kit's ribbon, already
the game's - laid over the cap by `RanksScreen`. That also withdrew `kit_tab` (8d). The board's
slice is **four measured sides** rather than one number: a cap and a plinth are not a rim.

**Where they come from, and why the tool reads the package itself.** The kit is a Unity Asset
Store purchase (300Mind, *2D Mobile Game UI Kit*) that ships as two 3118x1754 sprite sheets
whose slices live only in the sheet's `.meta`; the Asset Store window downloads it to
`%APPDATA%\\Unity\\Asset Store-5.x\\...` and **it is deliberately not imported into
`Assets/`** - nothing in the game draws the sheets, and a 3118x1754 texture resident for five
small plates is exactly the memory fault invariant 7d is about. A `.unitypackage` is a gzipped
tar of `<guid>/asset` blobs with a sibling `pathname` and `asset.meta`, so it opens with
`tarfile` and needs no Editor (`make_shop_art.py`'s finding). Each piece is named by its
**sheet and slice index** (`CUTS`), cropped by the rect the kit's own meta gives it, trimmed to
its opaque box (the rects carry empty margin), and scaled to the size the page draws it at.

**Pre-scaled, so the slice is drawn 1:1.** A nine-slice keeps its corners at source size, and
the kit's corners are drawn for a 3118-wide sheet; drawn as they ship on a 1080 canvas a row's
rounded end would be a third of the row. Each piece is therefore scaled to the size the page
draws it (`scale` in `CUTS`, chosen against `RanksScreen`'s constants) and its border measured
*after* scaling, which is invariant 44a - scale a sliced piece for the size its corner should
draw at, never for resolution.

**The border is measured, never typed** (invariant 44b), and it is two measurements because
these pieces fail in two ways: a **rim** (how far in from the edge the face's colour settles,
read off the luminance profile across the middle rows) and a **corner reach** (how much later
the outermost row becomes opaque than the middle row does - a notch, a rounded end or a tab's
sloped side). The border is the larger of the two plus two pixels, so the slice never cuts
through a notch and never stretches a corner.

**It passes with no package on this machine**: the PNGs are committed, so a fresh clone runs
every gate without the Asset Store cache. **The `.meta` carries a derived guid**
(`make_rank_art.py`'s reason) and an existing one is kept, border aside.

    python Tools/make_rank_kit_art.py            # cut them
    python Tools/make_rank_kit_art.py --check    # prove the shipped PNGs are what this writes
    python Tools/make_rank_kit_art.py --contact  # every piece at the size the page draws it
"""

from __future__ import annotations

import argparse
import hashlib
import io
import os
import re
import sys
import tarfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Ui" / "Rank"
TEMPLATE = ROOT / "Assets" / "Game" / "Art" / "Ui" / "ic_update.png.meta"

STORE = Path(os.environ.get("APPDATA", r"C:\Users\Digikey\AppData\Roaming")) / "Unity" / "Asset Store-5.x"
KIT_PACKAGE = STORE / "300Mind" / "Textures MaterialsGUI Skins" / "2D Mobile Game UI Kit.unitypackage"
ICON_PACKAGE = STORE / "LAYERLAB" / "Textures MaterialsIcons UI" / "2D Icons - Casual Icon Pack.unitypackage"

#: name -> (sheet, slice index, scale). The index is the kit's own `UI-pack_Sprite_<sheet>_<i>`.
#: Scales are against the size `RanksScreen` draws each piece at on a 1080 canvas: a row
#: 952x78, a seat 104, the chip 156x48.
CUTS = {
    "kit_row": (2, 5, .37),
    "kit_seat": (1, 26, .62),
    "kit_chip": (2, 10, .40),
}

#: The board: the cartoon kit's zip (read where `make_hud_kit_art.py` reads it), the entry, and
#: the scale. At .32 the cap draws 69 units tall and the plinth 79 - `RanksScreen.PlateHead` /
#: `PlateFoot` are read off the border this measures, never typed beside it.
CARTOON_ZIP = Path(r"C:\Users\Digikey\Downloads\2D ASSETS") / "craftpix-net-828046-cartoon-ui-elements-mini-kit (2).zip"
BOARD = ("kit_board", "Png/Artboard 20.png", .32)

#: A plain column of the board's cap, left of the ribbon and right of the rivet, in source pixels,
#: and how deep the ribbon's reach is probed. The span painted over is measured against it.
BOARD_PLAIN_COLUMN = 200
BOARD_RIBBON_DEPTH = 340

#: The one icon the game did not already have: (path inside the icon package, long edge).
ICONS = {
    "ic_raiders": ("Icons/256/Icon_Equipment_Weapon_Sword02.png", 160),
}

DARK = 60
BORDER_MOST_FRACTION = .42


# ------------------------------------------------------------------ the packages
def package_files(path):
    """Every `pathname -> bytes` a unitypackage holds, plus each asset's meta under `<pathname>.meta`."""
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
            if "asset.meta" in d:
                files[name + ".meta"] = tar.extractfile(d["asset.meta"]).read()
    return files


def sources():
    """Both packages, or None when this machine never bought them."""
    if not (KIT_PACKAGE.exists() and ICON_PACKAGE.exists()):
        return None
    return package_files(KIT_PACKAGE), package_files(ICON_PACKAGE)


RECT = re.compile(r"name: UI-pack_Sprite_(\d)_(\d+)\n\s+rect:\n\s+serializedVersion: 2\n"
                  r"\s+x: ([\d.]+)\n\s+y: ([\d.]+)\n\s+width: ([\d.]+)\n\s+height: ([\d.]+)")


def sheet_slices(kit, sheet):
    """The kit's sheet as an image and its slices as {index: (x, y, w, h)}, y from the top."""
    from PIL import Image

    base = "Assets/300Mind/2D Game UI Kit/Sprites/UI-pack_Sprite_%d.png" % sheet
    image = Image.open(io.BytesIO(kit[base])).convert("RGBA")
    rects = {}
    for s, i, x, y, w, h in RECT.findall(kit[base + ".meta"].decode("utf8")):
        if int(s) != sheet:
            continue
        x, y, w, h = (float(v) for v in (x, y, w, h))
        rects[int(i)] = (int(x), int(image.height - y - h), int(x + w), int(image.height - y))
    return image, rects


# ------------------------------------------------------------------ measuring
def luminance(image):
    import numpy as np
    arr = np.asarray(image).astype(int)
    lum = (arr[:, :, 0] * 299 + arr[:, :, 1] * 587 + arr[:, :, 2] * 114) // 1000
    return np.where(arr[:, :, 3] > 10, lum, 0), arr[:, :, 3]


def border_of(piece):
    """The nine-slice border: the larger of the rim and the corner reach. See the module doc."""
    import numpy as np

    lum, alpha = luminance(piece)
    h, w = lum.shape

    def settle(profile):
        core = np.median(profile[int(len(profile) * .35):int(len(profile) * .65)])
        for i in range(len(profile) - 6):
            if all(abs(profile[i + k] - core) <= 10 for k in range(6)):
                return i
        return len(profile) // 2

    rows = lum[int(h * .4):int(h * .6)].mean(axis=0)
    rim = max(settle(rows), settle(rows[::-1]))

    def first_opaque(row):
        hits = np.where(row > 128)[0]
        return int(hits[0]) if len(hits) else w

    def last_opaque(row):
        hits = np.where(row > 128)[0]
        return int(hits[-1]) if len(hits) else 0

    # Probed on the outermost rows and columns that are opaque anywhere - the piece's very
    # edge is its anti-aliased shadow and answers "opaque nowhere", which read as a reach of
    # the whole width on the first cut.
    opaque_rows = np.where((alpha > 128).any(axis=1))[0]
    opaque_cols = np.where((alpha > 128).any(axis=0))[0]
    mid = alpha[h // 2]
    reach = 0
    for y in (opaque_rows[0], opaque_rows[-1]):
        reach = max(reach, first_opaque(alpha[y]) - first_opaque(mid),
                    last_opaque(mid) - last_opaque(alpha[y]))
    # The same question asked of the columns, for a piece whose corner is taller than wide.
    midc = alpha[:, w // 2]
    for x in (opaque_cols[0], opaque_cols[-1]):
        col = alpha[:, x]
        reach = max(reach, first_opaque(col) - first_opaque(midc),
                    last_opaque(midc) - last_opaque(col))

    border = int(max(rim, reach)) + 2
    most = int(min(w, h) * BORDER_MOST_FRACTION)
    return max(4, min(border, most))


# ------------------------------------------------------------------ cutting
def cut(kit, icons):
    """Every piece: name -> (image, border). An icon's border is nought."""
    from PIL import Image

    made = {}
    sheets = {}
    for name, (sheet, index, scale) in CUTS.items():
        if sheet not in sheets:
            sheets[sheet] = sheet_slices(kit, sheet)
        image, rects = sheets[sheet]
        if index not in rects:
            sys.exit(f"{name}: the kit's sheet {sheet} has no slice {index}")
        piece = image.crop(rects[index])
        box = piece.getbbox()
        if box is None:
            sys.exit(f"{name}: slice {index} is empty")
        piece = piece.crop(box)
        piece = piece.resize((max(1, int(round(piece.width * scale))),
                              max(1, int(round(piece.height * scale)))), Image.LANCZOS)
        made[name] = (piece, border_of(piece))

    for name, (path, edge) in ICONS.items():
        full = "Assets/Layer Lab/2D Icons-CasualIconPack/" + path
        if full not in icons:
            sys.exit(f"{name}: the icon pack has no {path}")
        icon = Image.open(io.BytesIO(icons[full])).convert("RGBA")
        icon = icon.crop(icon.getbbox())
        k = edge / max(icon.size)
        icon = icon.resize((max(1, int(round(icon.width * k))), max(1, int(round(icon.height * k)))), Image.LANCZOS)
        made[name] = (icon, 0)
    return made


def board_source():
    """The cartoon kit's board as an image, or None when this machine has no copy of the kit."""
    import zipfile
    from PIL import Image

    if not CARTOON_ZIP.exists():
        return None
    with zipfile.ZipFile(CARTOON_ZIP) as z:
        return Image.open(io.BytesIO(z.read(BOARD[1]))).convert("RGBA")


def cut_board(image):
    """The notice board with its ribbon lifted off, trimmed, scaled: (image, (l, b, r, t))."""
    import numpy as np
    from PIL import Image

    arr = np.asarray(image).copy()
    top = arr[:BOARD_RIBBON_DEPTH].astype(int)
    plain = top[:, BOARD_PLAIN_COLUMN:BOARD_PLAIN_COLUMN + 1]
    differs = np.abs(top - plain).sum(axis=2).max(axis=0) > 24
    # The ribbon is the run of differing columns between the two rivets; everything past the
    # plain column on each side up to its mirror is the cap, flat across.
    w = arr.shape[1]
    span = [x for x in range(BOARD_PLAIN_COLUMN + 1, w - BOARD_PLAIN_COLUMN - 1) if differs[x]]
    if not span:
        sys.exit("kit_board: no ribbon found over the cap - is this still Artboard 20?")
    x0, x1 = span[0] - 6, span[-1] + 6
    arr[:BOARD_RIBBON_DEPTH, x0:x1 + 1] = arr[:BOARD_RIBBON_DEPTH, BOARD_PLAIN_COLUMN:BOARD_PLAIN_COLUMN + 1]

    piece = Image.fromarray(arr, "RGBA")
    piece = piece.crop(piece.getbbox())
    scale = BOARD[2]
    piece = piece.resize((max(1, int(round(piece.width * scale))),
                          max(1, int(round(piece.height * scale)))), Image.LANCZOS)
    return piece, board_border(piece)


def board_border(piece):
    """Four sides, measured: how far in from each edge the board stops being what its middle row
    and column are. Unity's order, (left, bottom, right, top)."""
    import numpy as np

    a = np.asarray(piece).astype(int)
    h, w = a.shape[:2]
    rows = np.abs(a - a[h // 2:h // 2 + 1]).sum(axis=2).max(axis=1) > 24
    cols = np.abs(a - a[:, w // 2:w // 2 + 1]).sum(axis=2).max(axis=0) > 24
    top = int(np.where(rows[:h // 2])[0].max()) + 3
    bottom = h - int(np.where(rows[h // 2:])[0].min() + h // 2) + 3
    left = int(np.where(cols[:w // 2])[0].max()) + 3
    right = w - int(np.where(cols[w // 2:])[0].min() + w // 2) + 3
    return (left, bottom, right, top)


def sides(border):
    """A border as Unity's four sides, whether it was measured as one number or four."""
    return tuple(border) if isinstance(border, tuple) else (border,) * 4


def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def rebordered(text, border, guid=None):
    out = []
    for line in text.splitlines(keepends=True):
        if guid and line.startswith("guid: "):
            out.append(f"guid: {guid}\n")
        elif line.strip().startswith("spriteBorder:"):
            out.append("  spriteBorder: {x: %d, y: %d, z: %d, w: %d}\n" % sides(border))
        else:
            out.append(line)
    return "".join(out)


def meta(name, border):
    guid = hashlib.md5(("glimmer.ui.rank.kit." + name).encode("utf8")).hexdigest()
    return rebordered(TEMPLATE.read_text(encoding="utf8"), border, guid)


def nine(im, border, w, h, zoom=1.0):
    """`Image.Type.Sliced`: corners kept at `border / zoom`, edges and middle stretched. The
    border is one number or Unity's four sides (left, bottom, right, top). Shared with
    `render_ranks.py`."""
    from PIL import Image

    w, h = max(1, int(round(w))), max(1, int(round(h)))
    src = im if zoom == 1.0 else im.resize((max(1, int(round(im.width / zoom))),
                                            max(1, int(round(im.height / zoom)))), Image.LANCZOS)
    bl, bb, br, bt = (int(round(s / zoom)) for s in sides(border))
    W0, H0 = src.size
    if bl + br > w:
        bl = br = w // 2
    if bt + bb > h:
        bt = bb = h // 2
    if max(bl, bb, br, bt) <= 0:
        return src.resize((w, h), Image.LANCZOS)
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    xs = [(0, bl, 0, bl), (bl, W0 - br, bl, w - br), (W0 - br, W0, w - br, w)]
    ys = [(0, bt, 0, bt), (bt, H0 - bb, bt, h - bb), (H0 - bb, H0, h - bb, h)]
    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0:
                continue
            part = src.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.LANCZOS)
            out.paste(part, (dx0, dy0), part)
    return out


def contact(made, path):
    """Every piece as cut and at the size the page draws it, on the wall the page draws it on."""
    from PIL import Image, ImageDraw

    uses = {"kit_board": (1024, 530), "kit_row": (952, 78), "kit_seat": (104, 104),
            "kit_chip": (156, 48), "ic_raiders": (58, 58)}
    pad = 24
    rows = []
    for name, (piece, border) in made.items():
        w, h = uses[name]
        drawn = nine(piece, border, w, h) if border else piece.resize((w, h), Image.LANCZOS)
        rows.append((name, piece, border, drawn))
    width = max(p.width + d.width for _, p, _, d in rows) + pad * 3
    height = sum(max(p.height, d.height) + pad + 20 for _, p, _, d in rows) + pad
    sheet = Image.new("RGBA", (width, height), (26, 30, 44, 255))
    wall = ROOT / "Assets" / "Game" / "Art" / "Bg" / "plain.png"
    if wall.exists():
        bg = Image.open(wall).convert("RGBA")
        s = max(width / bg.width, height / bg.height)
        sheet.alpha_composite(bg.resize((int(bg.width * s), int(bg.height * s)), Image.LANCZOS), (0, 0))
    draw = ImageDraw.Draw(sheet)
    y = pad
    for name, piece, border, drawn in rows:
        sheet.alpha_composite(piece, (pad, y))
        sheet.alpha_composite(drawn, (pad * 2 + piece.width, y))
        draw.text((pad, y + max(piece.height, drawn.height) + 2),
                  f"{name}  {piece.width}x{piece.height}  border {border}", fill=(255, 243, 220, 255))
        y += max(piece.height, drawn.height) + pad + 20
    sheet.save(path)
    print(f"wrote {path}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="prove the shipped PNGs are what this tool cuts")
    parser.add_argument("--contact", action="store_true", help="write the contact sheet")
    args = parser.parse_args()

    names = [BOARD[0]] + list(CUTS) + list(ICONS)
    src = sources()
    board = board_source()
    made = cut(*src) if src is not None else {}
    if board is not None:
        made[BOARD[0]] = cut_board(board)
    missing = [n for n in names if n not in made and not (OUT / f"{n}.png").exists()]
    if missing:
        sys.exit("a kit is not on this machine and no committed PNG for: " + ", ".join(missing))
    if not made:
        print(f"rank kit: no kit on this machine; the {len(names)} committed PNGs stand")
        return 0
    if len(made) < len(names):
        print("rank kit: %d of %d piece(s) cut here; the rest stand committed" % (len(made), len(names)))

    if args.contact:
        contact(made, ROOT / "rank_kit.png")
        return 0

    if args.check:
        bad = []
        for name, (piece, border) in made.items():
            png = OUT / f"{name}.png"
            side = OUT / f"{name}.png.meta"
            if not png.exists():
                bad.append(f"{name}.png is missing")
            elif png.read_bytes() != png_bytes(piece):
                bad.append(f"{name}.png is not what this tool cuts")
            if side.exists():
                want = "spriteBorder: {x: %d, y: %d, z: %d, w: %d}" % sides(border)
                if want not in side.read_text(encoding="utf8"):
                    bad.append(f"{name}.png.meta does not carry the measured border {border}")
        if bad:
            for line in bad:
                print(line, file=sys.stderr)
            print("re-run without --check", file=sys.stderr)
            return 1
        print("rank kit: %d piece(s), reproducible; borders %s"
              % (len(made), ", ".join(f"{n} {b}" for n, (_, b) in made.items())))
        return 0

    OUT.mkdir(parents=True, exist_ok=True)
    for name, (piece, border) in made.items():
        (OUT / f"{name}.png").write_bytes(png_bytes(piece))
        side = OUT / f"{name}.png.meta"
        if side.exists():
            side.write_text(rebordered(side.read_text(encoding="utf8"), border), encoding="utf8", newline="\n")
        else:
            side.write_text(meta(name, border), encoding="utf8", newline="\n")
        print(f"  {name}: {piece.width}x{piece.height}, border {border}")
    print(f"rank kit: wrote {len(made)} piece(s) to {OUT}, each with its .meta")
    return 0


if __name__ == "__main__":
    sys.exit(main())
