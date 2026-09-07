# -*- coding: utf-8 -*-
"""Draws every shipped Emberforge wall at the size a phone draws it. Look at it.

    python Tools/render_ember.py                        # every level of the chapter
    python Tools/render_ember.py --level e01_ironward
    python Tools/render_ember.py --board "rgby..,#..gC." --out wall.png

**This is the only check in the repository that can see a board that reads badly.** Every
numeric gate in this project reads the *model*, and the model is right the whole time. The
Iron Quarry's cage passed par, `ways`, `careless`, both validators, the content check and the
art audit, and at the size a phone drew it it was three brown logs (invariant 32b); Hollowmarch's
road passed everything and read as a row of disconnected sockets (33h). Both were caught by a
render and by nothing else.

So this draws the real wall out of the real chapter body with the real sprites, laid out with
`EmberView`'s own arithmetic and `EmberScreen.HostInset`'s own room. Four questions to ask of
what comes out, in order:

  1. **Can you see the matches?** Two alike shards with a gap have to be findable at a glance or
     the mode is asking the player to do arithmetic in their head - which is exactly what
     Budburst was withdrawn and rebuilt for (invariant 20l).
  2. **Are the four shards told apart by shape as well as by colour?** A heart, a circle, a
     rhombus and a rectangle. If two of them read as the same silhouette at this size, one of
     them is wrong, and a colour-blind player is playing a different game.
  3. **Is a cage the most legible thing on the wall?** It is what the level is *for*, and a cage
     that reads as decoration is a goal nobody goes for (invariant 32b).
  4. **Does an ember look like it wants to be touched?** It is the only thing on this board that
     is tapped rather than dragged, and nothing but its own drawing says so.
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

import ember                                                            # noqa: E402
import proto                                                            # noqa: E402

ART = REPO / "Assets" / "Game" / "Art" / "Ember"
CHAPTERS = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters"

#: The canvas the game lays out in, and the room `EmberScreen.HostInset` leaves the wall:
#: (left, top, right, bottom). Kept in step with the screen by hand - this is a diagnostic,
#: not a rendering, and a copy that drifts is still worth more than no picture at all.
CANVAS = (1080, 1920)
INSET = (14, 300, 14, 260)

#: `Pal.Board` over the dark plate a village backdrop is graded against.
PLATE = (14, 27, 37)
BACK = (9, 14, 20)


def sprite(name):
    path = ART / (name + ".png")
    if not path.exists():
        raise SystemExit("missing %s - run: python Tools/make_ember_art.py --write" % path.name)
    return Image.open(path).convert("RGBA")


def reel(name, frame=0):
    path = ART / name / ("f%02d.png" % frame)
    return Image.open(path).convert("RGBA") if path.exists() else None


#: Which sprite each authored letter draws. `EmberView.Face`, mirrored.
FACES = {
    "r": "shard_r", "g": "shard_g", "b": "shard_b", "y": "shard_y",
    ember.EMBER: "ember", ember.STONE: "stone", ember.FROST: "frost", ember.CAGE: "cage",
}


def layout_of(level):
    block = level["ember"]
    grid = proto.Grid(block["rows"], block["width"], block["height"], ember.LETTERS)
    return ember.Layout(grid, block.get("spare") or 0)


def draw(level):
    lay = layout_of(level)
    board = ember.Board(lay)

    w, h = lay.w, lay.h

    room_x = CANVAS[0] - INSET[0] - INSET[2]
    room_y = CANVAS[1] - INSET[1] - INSET[3]

    # `ProtoView.Fit`, and the margin it leaves.
    margin = 18
    cell = int(min((room_x - margin * 2) / w, (room_y - margin * 2) / h))

    span = (w * cell, h * cell)
    sheet = Image.new("RGB", (span[0] + margin * 4, span[1] + margin * 4), BACK)

    plate = Image.new("RGBA", (span[0] + margin * 2, span[1] + margin * 2), PLATE + (255,))
    sheet.paste(plate, (margin, margin), plate)

    def put(im, cx, cy, scale):
        size = max(1, int(cell * scale))
        thumb = im.resize((size, size), Image.LANCZOS)
        sheet.paste(thumb, (int(cx - size / 2), int(cy - size / 2)), thumb)

    tile = sprite("wall")

    for i in range(w * h):
        x, y = i % w, i // w
        cx = margin * 2 + x * cell + cell // 2
        cy = margin * 2 + y * cell + cell // 2

        put(tile, cx, cy, 1.0)

        c = board.cells[i]
        if c == ember.BREACH:
            continue

        if c == ember.WARDEN:
            crew = reel("warden")
            if crew is not None:
                ratio = crew.width / float(crew.height)
                size = int(cell * 1.22)
                thumb = crew.resize((max(1, int(size * ratio)), size), Image.LANCZOS)
                sheet.paste(thumb, (int(cx - thumb.width / 2), int(cy - thumb.height / 2)), thumb)
            put(sprite("plate"), cx, cy, 1.02)
            continue

        if c == ember.CAGE:
            # The critter is drawn behind the bars, exactly as the view stacks it - which is the
            # whole question this render is here to answer about a cage.
            crew = reel("mon1")
            if crew is not None:
                ratio = crew.width / float(crew.height)
                size = int(cell * 0.72)
                thumb = crew.resize((max(1, int(size * ratio)), size), Image.LANCZOS)
                sheet.paste(thumb, (int(cx - thumb.width / 2), int(cy - thumb.height / 2)), thumb)

        put(sprite(FACES[c]), cx, cy, 1.0 if c == ember.CAGE else 0.88)

    return sheet


def caption(sheet, text):
    band = Image.new("RGB", (sheet.width, 34), BACK)
    ImageDraw.Draw(band).text((6, 10), text, fill=(220, 220, 220))
    out = Image.new("RGB", (sheet.width, sheet.height + 34), BACK)
    out.paste(sheet, (0, 0))
    out.paste(band, (0, sheet.height))
    return out


def chapter():
    path = CHAPTERS / "e01_emberforge.json"
    if not path.exists():
        sys.exit("no chapter body yet at %s" % path)
    return json.loads(path.read_text(encoding="utf-8"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--level")
    ap.add_argument("--board", help="rows, comma separated")
    ap.add_argument("--spare", type=int, default=0)
    ap.add_argument("--out", default=str(REPO / "Tools" / "ember_boards.png"))
    args = ap.parse_args()

    if args.board:
        rows = [r.strip() for r in args.board.split(",")]
        level = {"id": "board", "ember": {"width": len(rows[0]), "height": len(rows),
                                          "rows": rows, "spare": args.spare}}
        levels = [level]
    else:
        levels = [lv for lv in chapter()["levels"]
                  if args.level is None or lv["id"] == args.level]

    if not levels:
        sys.exit("no such level")

    sheets = []
    for level in levels:
        lay = layout_of(level)
        board = ember.Board(lay)
        par, ways, nodes, _ = proto.search(ember.Future(board))
        sheets.append(caption(draw(level),
                              "%s   %dx%d   par %d   ways %d   goals %d"
                              % (level["id"], lay.w, lay.h, par, ways, board.goals)))

    cols = min(3, len(sheets))
    rows = (len(sheets) + cols - 1) // cols
    cw = max(s.width for s in sheets)
    ch = max(s.height for s in sheets)

    out = Image.new("RGB", (cols * cw, rows * ch), BACK)
    for i, s in enumerate(sheets):
        out.paste(s, ((i % cols) * cw, (i // cols) * ch))

    out.save(args.out)
    print("wrote %s  (%d board(s))" % (args.out, len(sheets)))


if __name__ == "__main__":
    main()
