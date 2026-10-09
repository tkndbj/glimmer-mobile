# -*- coding: utf-8 -*-
"""Cuts the Shuffle lane's art: one tile per upgrade card, the hub's two marks, and its wall.

    python Tools/make_shuffle_art.py             # cut everything
    python Tools/make_shuffle_art.py --check     # prove the shipped PNGs are what this writes
    python Tools/make_shuffle_art.py --contact   # every tile at the size the hand draws it

**What is cut, and from where.**

* `Art/Ui/Shuffle/<card>.png` - one per card in `ShuffleCards.All`, the skill-icon pack's
  picture named on the card (`ShuffleCard.Picture`), squared, rounded and keylined by the same
  cutter the overcharge glyph and the Infinite hub's marks go through (`make_siege_art.tile_of`),
  so a bought icon is seated in this kit one way. The table below is a *mirror* of the C#
  catalog; `ShuffleArtTests` walks the catalog against the disk, and this file refuses to cut a
  card the catalog does not name.
* `Art/Ui/ic_shuffle.png`, `Art/Ui/ic_deck.png` - the hub's two marks, cut exactly as
  `make_siege_art.HUB_ICONS` cuts the Infinite lane's.
* `Art/Ui/shuffle_crest.png` - the lane's crest, the owner's own picture (2026-10-09), which
  the hub stands where the Infinite lane stands the rank badge (`EndlessHub.HubLane.Crest`).
  The picture is painted on a dark glow, not cut out, so it is given an alpha here rather than
  keyed: the paint's own darkness (black is nothing) under an elliptical feather, so its glow
  dies into the charcoal wall instead of ending at a rectangle. Cut only when the source is on
  this machine; the PNG is committed.
* `Art/Bg/plain_shuffle.png` - the ranked lane's purple brick wall turned to teal and then put
  nearly out: a hue rotation first (CLAUDE.md 44g: hue-rotate in the tool) and a shade on top,
  so the wall keeps every brick and its highlights and reads as charcoal with a cold cast - the
  owner asked for "darker, like black but not fully black" (2026-10-09), because a bright wall
  fought the crest.

**Every `.meta` carries a derived guid** (`make_rank_kit_art.py`'s reason): the tool is
reproducible, `--check` means something, and a re-cut keeps the address the first cut
registered. **Art written with the Editor closed is unaddressed** (CLAUDE.md 7b): run
`Glimmer Grove > Addressables > Sync All Assets` afterwards.

**It passes with the pack absent** - the PNGs are committed - and `--check` then has nothing to
hold them to and says so.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import re
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

sys.path.insert(0, str(Path(__file__).resolve().parent))

from make_siege_art import ICONS, tile_of                 # noqa: E402

REPO = Path(__file__).resolve().parents[1]
ART = REPO / "Assets" / "Game" / "Art"
OUT = ART / "Ui" / "Shuffle"
UI = ART / "Ui"
BG = ART / "Bg"
CATALOG = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Modes" / "Shuffle" / "ShuffleCard.cs"

ICON_TEMPLATE = UI / "ic_update.png.meta"
WALL_TEMPLATE = BG / "plain_ranked.png.meta"
WALL_SOURCE = BG / "plain_ranked.png"

#: `ShuffleHandLayout.IconSize` is 150 on a 1080 canvas; a 192 tile is drawn down, never up.
TILE, ROUND = 192, 28

#: The hub's seats are smaller (`EndlessHubLayout.IconSize` 52), cut as `make_siege_art` cuts
#: the Infinite lane's marks.
HUB_SIZE, HUB_ROUND = 96, 16

#: The hub's two marks: a vortex for the deal, shards for the hand.
HUB_MARKS = (("ic_shuffle", 60), ("ic_deck", 66))

#: How far round the wheel the ranked wall's purple is turned to reach the Shuffle lane's teal,
#: in Pillow's 0..255 hue. Measured: the wall's dominant hue sits near 200 (purple); 95 lands
#: it near 105 (teal-green). A rotation rather than a tint, because a tint multiplies and can
#: only ever darken (CLAUDE.md 37l).
WALL_TURN = -95

#: After the turn, how much of the wall's colour and light is kept: charcoal, not black.
WALL_SATURATION, WALL_VALUE = .40, .30

#: The owner's crest picture, and the size it is cut at: the hub draws it 553 across on a 1080
#: canvas (`EndlessHubLayout.CrestWidth`), so 768 is drawn down on any phone and under the
#: folder's cap. Its glow is given an alpha (see `crest`): opaque inside `CREST_INNER` of the
#: ellipse, feathered to nothing at the edge, and black is nothing everywhere.
CREST_SOURCE = Path(r"C:\Users\Digikey\Downloads\SHUFFLE_ Neon Gem Upgrade Frenzy.png")
CREST_W, CREST_H = 768, 512
CREST_INNER = .78
CREST_BLACK_FROM, CREST_BLACK_TO = 6, 40

ROW = re.compile(r'new ShuffleCard\("([a-z_]+)",\s*ShuffleTier\.(\w+),\s*ShuffleEffect\.\w+,'
                 r'\s*-?\d+,\s*-?\d+,\s*\d+,\s*(\d+)\)')


def catalog():
    """Every card the C# catalog names, with its picture: (id, tier, picture)."""
    text = CATALOG.read_text(encoding="utf-8")
    rows = ROW.findall(text)
    if not rows:
        sys.exit("no cards read out of %s - the row shape moved" % CATALOG)
    return [(cid, tier, int(pic)) for cid, tier, pic in rows]


def meta_for(template, name, salt):
    guid = hashlib.md5((salt + name).encode("utf8")).hexdigest()
    out = []
    for line in template.read_text(encoding="utf8").splitlines(keepends=True):
        out.append("guid: %s\n" % guid if line.startswith("guid: ") else line)
    return "".join(out)


def wall():
    """The ranked wall turned to teal and shaded to charcoal: hue rotated, then saturation and
    value scaled, so every brick and highlight survives at a fraction of its light."""
    im = Image.open(WALL_SOURCE).convert("RGBA")
    rgb = im.convert("RGB").convert("HSV")
    h, s, v = rgb.split()
    h = h.point(lambda x: (x + WALL_TURN) % 256)
    s = s.point(lambda x: int(x * WALL_SATURATION))
    v = v.point(lambda x: int(x * WALL_VALUE))
    turned = Image.merge("HSV", (h, s, v)).convert("RGB")
    turned.putalpha(im.split()[3])
    return turned


def crest():
    """The owner's picture with an alpha it never had: the paint's darkness under an elliptical
    feather. Black is nothing (the wall shows through), the glow fades out before the edge."""
    import numpy as np

    im = Image.open(CREST_SOURCE).convert("RGBA").resize((CREST_W, CREST_H), Image.LANCZOS)
    px = np.asarray(im).astype(np.float32)
    w, h = im.size

    ys, xs = np.mgrid[0:h, 0:w]
    r = np.sqrt(((xs + .5 - w / 2) / (w / 2)) ** 2 + ((ys + .5 - h / 2) / (h / 2)) ** 2)
    t = np.clip((1.0 - r) / (1.0 - CREST_INNER), 0.0, 1.0)
    feather = t * t * (3.0 - 2.0 * t)

    light = px[:, :, :3].max(axis=2)
    paint = np.clip((light - CREST_BLACK_FROM) / float(CREST_BLACK_TO - CREST_BLACK_FROM), 0.0, 1.0)

    alpha = (feather * paint * 255.0).round().astype(np.uint8)
    out = Image.fromarray(px[:, :, :3].astype(np.uint8), "RGB")
    out.putalpha(Image.fromarray(alpha, "L"))
    return out


def raw(im):
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=False)
    return buf.getvalue()


def cut():
    """Everything this tool writes, as `path -> (bytes, meta text)`."""
    root = ICONS if ICONS.exists() else None
    made = {}

    if root is not None:
        for cid, _tier, pic in catalog():
            tile = tile_of(root, pic, TILE, ROUND)
            if tile is None:
                sys.exit("card '%s' names picture %d, which the pack does not carry" % (cid, pic))
            made[OUT / ("%s.png" % cid)] = (raw(tile), meta_for(ICON_TEMPLATE, cid, "glimmer.ui.shuffle."))

        for name, pic in HUB_MARKS:
            tile = tile_of(root, pic, HUB_SIZE, HUB_ROUND)
            made[UI / ("%s.png" % name)] = (raw(tile), meta_for(ICON_TEMPLATE, name, "glimmer.ui.shuffle."))

    made[BG / "plain_shuffle.png"] = (raw(wall()), meta_for(WALL_TEMPLATE, "plain_shuffle", "glimmer.bg."))

    if CREST_SOURCE.exists():
        made[UI / "shuffle_crest.png"] = (raw(crest()), meta_for(ICON_TEMPLATE, "shuffle_crest", "glimmer.ui.shuffle."))

    return made


def contact(made):
    tiles = sorted(p for p in made if p.parent == OUT)
    cols, cell = 8, TILE + 16
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * (cell + 24)), (6, 24, 56, 255))
    d = ImageDraw.Draw(sheet)
    for i, p in enumerate(tiles):
        im = Image.open(io.BytesIO(made[p][0])).convert("RGBA")
        x, y = (i % cols) * cell + 8, (i // cols) * (cell + 24) + 8
        sheet.alpha_composite(im, (x, y))
        d.text((x, y + TILE + 4), p.stem, fill=(255, 243, 220, 255))
    out = REPO / "Tools" / "out" / "shuffle_contact.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out)
    print("wrote", out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    args = ap.parse_args()

    made = cut()

    if args.contact:
        contact(made)
        return

    if args.check:
        if ICONS.exists() is False:
            print("shuffle art: the skill-icon pack is absent, so only the wall is checked")
        if not CREST_SOURCE.exists():
            print("shuffle art: the crest's source is absent, so the crest is not checked")
        bad = []
        for p, (png, meta) in made.items():
            if not p.exists():
                bad.append("%s is missing" % p.relative_to(REPO))
            elif p.read_bytes() != png:
                bad.append("%s differs from what this writes" % p.relative_to(REPO))
            side = Path(str(p) + ".meta")
            if not side.exists():
                bad.append("%s has no .meta" % p.relative_to(REPO))
        for cid, _tier, _pic in catalog():
            if not (OUT / ("%s.png" % cid)).exists():
                bad.append("card '%s' has no picture on disk" % cid)
        if bad:
            sys.exit("shuffle art:\n  " + "\n  ".join(bad))
        print("shuffle art: %d file(s) are what this writes" % len(made))
        return

    OUT.mkdir(parents=True, exist_ok=True)
    for p, (png, meta) in made.items():
        p.write_bytes(png)
        side = Path(str(p) + ".meta")
        if not side.exists():
            side.write_text(meta, encoding="utf8", newline="\n")
    print("shuffle art: wrote %d file(s) under %s" % (len(made), ART.relative_to(REPO)))
    print("Editor: Glimmer Grove > Addressables > Sync All Assets, then Audit Addresses")


if __name__ == "__main__":
    main()
