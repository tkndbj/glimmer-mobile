# -*- coding: utf-8 -*-
"""Draws every shipped Hollowmarch road at the size a phone draws it. Look at it.

    python Tools/render_march.py                       # every level of the chapter
    python Tools/render_march.py --level m01_thegate
    python Tools/render_march.py --out board.png

**This is the only check in the repository that can see a board that reads badly**, and it
exists because the mode it replaced proved the point. The Iron Quarry's cage passed par,
`ways`, `careless`, `chain`, both validators, the content check and the art audit, and at the
size a phone drew it it was three brown logs — firewood, in a mode whose entire goal was the
thing behind them (invariant 32b). Every numeric gate in this project reads the *model*, and
the model is right the whole time.

So this draws the real board out of the real chapter body with the real sprites, laid out with
`MarchView`'s own arithmetic and `MarchScreen.HostInset`'s own room. Three questions to ask of
what comes out, in order:

  1. **Can you see the matches?** A pair of alike pods has to be findable at a glance, or the
     mode is asking the player to do arithmetic - which is exactly what Budburst was withdrawn
     and rebuilt for (invariant 20l).
  2. **Is the gate the most frightening thing on the screen?** It is what the whole march is
     about, and a threat that reads as scenery is not one.
  3. **Do the caged pods stand out from the plain ones?** They are what the level is *for*. If
     a cage reads as decoration on a pod rather than as cargo, nothing else on the board will
     make the player go for it.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import march                                                            # noqa: E402
import proto                                                            # noqa: E402

ART = REPO / "Assets" / "Game" / "Art" / "March"
CHAPTERS = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters"

#: The canvas the game lays out in, and the room `MarchScreen.HostInset` leaves the board:
#: (left, top, right, bottom). Kept in step with the screen by hand - this is a diagnostic,
#: not a rendering, and a copy that drifts is still worth more than no picture at all.
CANVAS = (1080, 1920)
INSET = (14, 300, 14, 300)

#: `Pal.Board` over the dark plate a village backdrop is graded against.
PLATE = (14, 27, 37)
BACK = (9, 14, 20)


def sprite(name):
    path = ART / (name + ".png")
    if not path.exists():
        raise SystemExit("missing %s - run: python Tools/make_march_art.py --write" % path.name)
    return Image.open(path).convert("RGBA")


def layout_of(level):
    block = level["march"]
    grid = proto.Grid(block["rows"], block["width"], block["height"], march.LETTERS)
    return march.Layout(grid, block.get("spare") or 0, block.get("cores"))


def draw(level):
    lay = layout_of(level)
    board = march.Board(lay)

    w, h = lay.w, lay.h

    room_x = CANVAS[0] - INSET[0] - INSET[2]
    room_y = CANVAS[1] - INSET[1] - INSET[3]

    # `ProtoView.Fit`, and the margin it leaves.
    margin = 18
    cell = int(min((room_x - margin * 2) / w, (room_y - margin * 2) / h))

    span = (w * cell, h * cell)
    sheet = Image.new("RGB", (span[0] + margin * 4, span[1] + margin * 4 + cell * 2), BACK)

    plate = Image.new("RGBA", (span[0] + margin * 2, span[1] + margin * 2), PLATE + (255,))
    sheet.paste(plate, (margin, margin), plate)

    ox = margin * 2
    oy = margin * 2

    def at_cell(index):
        return ox + (index % w) * cell, oy + (index // w) * cell

    def stamp(img, index, scale=1.0, dx=0, dy=0):
        size = max(1, int(cell * scale))
        cut = img.resize((size, size), Image.LANCZOS)
        x, y = at_cell(index)
        sheet.paste(cut, (x + (cell - size) // 2 + dx, y + (cell - size) // 2 + dy), cut)

    ground = sprite("ground")
    rubble = sprite("rubble")
    road = sprite("road")

    for i in range(len(lay.grid.cells)):
        c = lay.grid.cells[i]
        stamp(rubble if c == march.RUBBLE else ground, i)

    for cellindex in lay.path:
        stamp(road, cellindex)

    stamp(sprite("portal"), lay.path[0], 1.55)

    # Bolt stands at the launcher, and the next three cores sit under him.
    bolt = ART / "bolt"
    frames = sorted(bolt.glob("*.png")) if bolt.exists() else []
    if frames:
        stamp(Image.open(frames[0]).convert("RGBA"), lay.path[-1], 1.5, dy=-int(cell * .18))

    pods = {"R": sprite("pod_r"), "G": sprite("pod_g"),
            "B": sprite("pod_b"), "Y": sprite("pod_y")}
    cage = sprite("cage")
    plated = sprite("plate")

    for k, what in enumerate(board.line):
        index = lay.slots[board.head + k]

        if march.is_raider(what):
            crew = ART / ("drone" if what == march.HAULER else "brute")
            shots = sorted(crew.glob("*.png")) if crew.exists() else []
            if shots:
                stamp(Image.open(shots[0]).convert("RGBA"), index, 1.25)
            if what != march.HAULER:
                stamp(plated, index, 1.05)
            continue

        stamp(pods[march.hue(what)], index, .92)
        if march.is_caged(what):
            stamp(cage, index, .92)

    # The magazine, drawn where the screen draws it: beside the launcher and offset *inward*,
    # which is `MarchView.Magazine`'s rule and not a detail. A launcher may be authored in any
    # corner of the board, so an offset in a fixed direction puts the cores off the plate on
    # three roads out of four - which is what the first render showed.
    lx, ly = at_cell(lay.path[-1])
    inward = (1 if lx < ox + span[0] / 2 else -1, 1 if ly < oy + span[1] / 2 else -1)

    for i in range(3):
        colour = lay.cores[(board.index + i) % len(lay.cores)]
        size = int(cell * (.80 if i == 0 else .48))
        cut = pods[colour].resize((size, size), Image.LANCZOS)
        step = int(cell * (0 if i == 0 else .52 + (i - 1) * .42))
        sheet.paste(cut, (lx + cell // 2 - size // 2 + inward[0] * step,
                          ly + cell // 2 - size // 2 + inward[1] * int(cell * .95)), cut)

    return sheet, lay, board


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--chapter", default="m01_hollowmarch")
    ap.add_argument("--level", default="")
    ap.add_argument("--out", type=Path, default=REPO / "march_boards.png")
    args = ap.parse_args()

    body = json.loads((CHAPTERS / (args.chapter + ".json")).read_text(encoding="utf-8"))
    levels = [lv for lv in body["levels"]
              if not args.level or lv["id"] == args.level]

    if not levels:
        sys.exit("no such level in %s" % args.chapter)

    sheets = []
    for level in levels:
        sheet, lay, board = draw(level)
        sheets.append(sheet)

        read = march.readings(lay)
        print("%-16s %2dx%-2d  pods %2d  cages %d  raiders %d  runway %2d  jam-at %2d  cores %s"
              % (level["id"], lay.w, lay.h, read["pods"], read["cages"],
                 read["haulers"] + read["wardens"], read["runway"], read["menace"],
                 lay.cores))

    width = max(s.width for s in sheets)
    gap = 24
    out = Image.new("RGB", (width, sum(s.height for s in sheets) + gap * (len(sheets) - 1)),
                    BACK)
    y = 0
    for s in sheets:
        out.paste(s, ((width - s.width) // 2, y))
        y += s.height + gap

    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print("\nwrote %s  - look at it; no numeric gate can" % args.out)


if __name__ == "__main__":
    main()
