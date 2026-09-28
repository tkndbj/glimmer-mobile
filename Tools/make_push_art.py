# -*- coding: utf-8 -*-
"""Cuts the Push challenge's keeper: the little cannon a player walks round the board.

    python Tools/make_push_art.py            # write Art/Challenge/push_keeper.png
    python Tools/make_push_art.py --check    # prove the committed PNG is what this cuts
    python Tools/make_push_art.py --contact  # out/push_art.png: the keeper at board size, four ways

**Why a cannon.** The first keeper was a gold disc with the profile mark on it - a person icon,
which the owner rejected (2026-09-27: "change that person icon with something proper"). The
owner's own picture of this genre (`Art/Ui/challenge_push`, cut from their `push.png`) draws a
turret shoving a gem along with its barrel, so the keeper is a turret: the same kit, the same
line and the same outline as the four posts it feeds, and a thing that plainly *pushes*.

**Why this turret.** The merge-shooter kit's gun ladder is ten models; six to ten are the posts
every ward is drawn as (`make_siege_art.WARD_TIERS`) and one to five were left unused on
purpose. `Gun03` is the one that reads as none of the four lanes - cream, butter and a blue
foot, where `Gun02` reads amber and `Gun04` wears a red barrel - so a keeper standing beside a
red gem is never mistaken for being red. **It is the pack's own colours, never hue-rotated**,
because its whole job is to be the one thing on the board that is not a colour.

**The pivot is the body, not the picture.** The view turns the keeper to face the way it last
walked (`SokobanPuzzle.FacingX`/`FacingY`), so the canvas is square and the hull's centre is its
centre: the barrel reaches up from the middle, and a quarter turn swings the barrel round a hull
that stays on its cell. Cut at the pack's own scale - the board draws the keeper at about 120
units, so the 131-pixel source is not an upscale on any phone this game ships to.

The pack is optional: without it `--check` still passes on the committed PNG, as every art tool
here does.
"""
from __future__ import annotations

import argparse
import io
import sys
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "Tools"))

import make_siege_art as siege  # noqa: E402  (the pack root and `zipped`)

OUT = REPO / "Assets" / "Game" / "Art" / "Challenge" / "push_keeper.png"
CONTACT = REPO / "out" / "push_art.png"

#: The model and the frame: the idle pose, barrel up.
MODEL = "Gun03"
SOURCE = "Png/Guns/%s/Idle/%s-Idle_0.png" % (MODEL, MODEL)

#: The canvas, square so a quarter turn keeps it on its cell.
SIZE = 256

#: A row belongs to the hull when its ink is at least this share of the widest row.
HULL_ROW = .70


def cut(src):
    """The keeper on a square canvas with its hull's centre at the canvas's centre."""
    a = np.asarray(src.getchannel("A")) > 24
    rows = a.sum(axis=1)
    widest = rows.max()
    hull = np.nonzero(rows >= widest * HULL_ROW)[0]
    cols = np.nonzero(a[hull[0]:hull[-1] + 1].any(axis=0))[0]
    cy = (hull[0] + hull[-1] + 1) / 2.0
    cx = (cols[0] + cols[-1] + 1) / 2.0

    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ox, oy = int(round(SIZE / 2 - cx)), int(round(SIZE / 2 - cy))
    if ox < 0 or oy < 0 or ox + src.width > SIZE or oy + src.height > SIZE:
        raise SystemExit("the keeper does not fit a %d canvas pivoted on its hull" % SIZE)
    out.alpha_composite(src, (ox, oy))
    return out


def source():
    z = siege.zipped(siege.KIT, siege.TOWER)
    if z is None:
        return None
    return siege.read(z, SOURCE)


def png_bytes(im):
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def write():
    src = source()
    if src is None:
        sys.exit("the merge-shooter kit (%s) is not in %s" % (siege.KIT, siege.TOWER))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(png_bytes(cut(src)))
    print("wrote %s" % OUT.relative_to(REPO).as_posix())


def check():
    if not OUT.exists():
        sys.exit("%s is not on disk" % OUT.relative_to(REPO).as_posix())
    src = source()
    if src is None:
        print("the kit is absent: %s is on disk and was not re-cut" % OUT.name)
        return
    got = np.asarray(Image.open(OUT).convert("RGBA")).astype(np.int16)
    want = np.asarray(cut(src)).astype(np.int16)
    if got.shape != want.shape or np.abs(got - want).max() > 1:
        sys.exit("%s is not what the tool cuts" % OUT.name)
    print("ok: %s is what the tool cuts" % OUT.name)


def contact():
    keeper = Image.open(OUT).convert("RGBA") if OUT.exists() else cut(source())
    cell = 150
    sheet = Image.new("RGBA", (cell * 4, cell), (15, 42, 74, 255))
    for n, turn in enumerate((0, -90, 180, 90)):
        im = keeper.rotate(turn, resample=Image.BICUBIC).resize((int(cell * .9), int(cell * .9)), Image.LANCZOS)
        sheet.alpha_composite(im, (n * cell + (cell - im.width) // 2, (cell - im.height) // 2))
    CONTACT.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(CONTACT)
    print("wrote %s" % CONTACT.relative_to(REPO).as_posix())


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    a = ap.parse_args()
    if a.check:
        check()
    elif a.contact:
        contact()
    else:
        write()


if __name__ == "__main__":
    main()
