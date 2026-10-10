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
* `Art/Ui/shuffle_crest.png` and `Art/Ui/shuffle_crest_<code>.png` - the lane's crest, the
  owner's own picture (2026-10-09), which the hub stands where the Infinite lane stands the
  rank badge (`EndlessHub.HubLane.Crest`). **The word is painted into it**, so there is one
  picture per shipped language (the owner's other eight, 2026-10-10), English keeping the name
  it shipped with; `AssetManifest.ShuffleCrest` derives the address from the language and the
  game loads only the player's. The picture is painted on a dark glowing backdrop, so it is
  cut off it here by its keyline (see `crest`). Each is cut only when its source is on this
  machine; the PNGs are committed.
* `Art/Bg/plain_shuffle.png` - the ranked lane's purple brick wall turned to a deep teal-blue:
  a hue rotation first (CLAUDE.md 44g: hue-rotate in the tool) and a shade on top, so the wall
  keeps every brick and its highlights. Dark, because a bright wall fought the crest; coloured,
  because the charcoal it was before read as dull (the owner, 2026-10-09).

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

#: How far round the wheel the ranked wall's purple is turned to reach the Shuffle lane's deep
#: teal-blue, in Pillow's 0..255 hue. Measured: the wall's dominant hue sits near 200 (purple);
#: 45 lands it near 155 (teal-blue), the complement of the crest's gold and well clear of the
#: Infinite lane's purple. A rotation rather than a tint, because a tint multiplies and can
#: only ever darken (CLAUDE.md 37l).
WALL_TURN = -45

#: After the turn, how much of the wall's colour and light is kept. The owner asked for the
#: first (bright teal) wall darker and then for the charcoal it became to be "something nicer"
#: (both 2026-10-09): so a rich colour at well under half its light, dark enough that the crest
#: is still the brightest thing on the screen.
WALL_SATURATION, WALL_VALUE = .85, .42

#: The owner's crest pictures, one per language in `Loc.Languages` (English replaced 2026-10-09
#: by "Shuffle Cards Game Logo", the other eight 2026-10-10), and the size they are cut at: the
#: hub draws it up to 840 across on a 1080 canvas (`EndlessHubLayout.CrestWidest`), so 1020 is
#: drawn down on any phone and under the `/Art/Ui/` folder's 1024 cap; both sides a multiple of
#: four, as block compression wants. `EndlessHubTests.EveryLanguageHasItsOwnCrest` holds the
#: files this writes to `Loc.Languages`.
DOWNLOADS = Path(r"C:\Users\Digikey\Downloads")
CREST_SOURCES = {
    "en": DOWNLOADS / "Shuffle Cards Game Logo.png",
    "es": DOWNLOADS / "BARAJAR_ Turbo Card Royale.png",
    "pt": DOWNLOADS / "Embaralhar_ Cartas e Torretes.png",
    "fr": DOWNLOADS / "MÉLANGE Tower Card Game Logo.png",
    "de": DOWNLOADS / "Mischen Card Game Logo.png",
    "it": DOWNLOADS / "MESCOLA_ Turret Card Shuffle Logo.png",
    "tr": DOWNLOADS / "Glossy KARIŞTIR Turret Card Logo.png",
    "pl": DOWNLOADS / "SZUFLA_ Neonowa Talia Wieżyczek.png",
    "ar": DOWNLOADS / "Glossy Arabic Shuffle Game Emblem.png",
}
CREST_W, CREST_H = 1020, 680


def crest_name(code):
    """`AssetManifest.ShuffleCrest`'s file name: English keeps the name it shipped with."""
    return "shuffle_crest" if code == "en" else "shuffle_crest_" + code

#: The crest's cut-out (see `crest`): a step between neighbours under `CREST_SMOOTH` (largest
#: channel, 0..255) is the painted backdrop's gradient; anything steeper is the picture's
#: keyline. The silhouette is then grown by `CREST_GROW` pixels to take back the keyline where
#: it met black, and feathered by `CREST_SOFT`.
CREST_SMOOTH, CREST_GROW, CREST_SOFT = 14, 3, 1.2

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


def crest(source):
    """The owner's picture cut off its painted backdrop.

    The backdrop is a dark gradient with coloured glows, as bright as 155 under the lettering,
    so neither its darkness nor an oval feather separates it: the first cut (an oval) would
    fade the outer cards, which reach the frame's edge. What *does* separate it is that it is
    smooth and the picture is keylined. So the backdrop is everything reachable from the
    frame's edge without crossing a step of `CREST_SMOOTH`; the rest is the picture. A flood
    stops at the keyline's far side where the backdrop glows and at its near side where it is
    black, so the silhouette is grown by the keyline's width to keep it whole either way."""
    import numpy as np
    from PIL import ImageFilter

    im = Image.open(source).convert("RGB").resize((CREST_W, CREST_H), Image.LANCZOS)
    px = np.asarray(im).astype(np.int16)
    h, w = CREST_H, CREST_W

    step = np.zeros((h, w), np.int16)
    dx = np.abs(px[:, 1:] - px[:, :-1]).max(axis=2)
    dy = np.abs(px[1:] - px[:-1]).max(axis=2)
    step[:, 1:] = np.maximum(step[:, 1:], dx)
    step[:, :-1] = np.maximum(step[:, :-1], dx)
    step[1:] = np.maximum(step[1:], dy)
    step[:-1] = np.maximum(step[:-1], dy)
    smooth = step < CREST_SMOOTH

    ground = np.zeros((h, w), bool)
    ground[0], ground[-1] = smooth[0], smooth[-1]
    ground[:, 0], ground[:, -1] = smooth[:, 0], smooth[:, -1]
    while True:
        grown = ground.copy()
        grown[1:] |= ground[:-1]
        grown[:-1] |= ground[1:]
        grown[:, 1:] |= ground[:, :-1]
        grown[:, :-1] |= ground[:, 1:]
        grown &= smooth
        if (grown == ground).all():
            break
        ground = grown

    alpha = Image.fromarray(np.where(ground, 0, 255).astype(np.uint8), "L")
    for _ in range(CREST_GROW):
        alpha = alpha.filter(ImageFilter.MaxFilter(3))
    alpha = alpha.filter(ImageFilter.GaussianBlur(CREST_SOFT))

    out = im.copy()
    out.putalpha(alpha)
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

    for code, source in CREST_SOURCES.items():
        if source.exists():
            name = crest_name(code)
            made[UI / (name + ".png")] = (raw(crest(source)), meta_for(ICON_TEMPLATE, name, "glimmer.ui.shuffle."))

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
        bad = []
        for code, source in CREST_SOURCES.items():
            if not source.exists():
                print("shuffle art: the '%s' crest's source is absent, so it is not checked" % code)
            if not (UI / (crest_name(code) + ".png")).exists():
                bad.append("the '%s' crest is not on disk" % code)
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
