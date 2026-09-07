# -*- coding: utf-8 -*-
"""Draws every shipped Kindlewake hollow at the size a phone draws it. Look at it.

    python Tools/render_kindle.py                        # every level of the chapter
    python Tools/render_kindle.py --level k01_crossways
    python Tools/render_kindle.py --board "r.R.b,..#.." --out hollow.png
    python Tools/render_kindle.py --lit                  # with a shortest answer already drawn

**This is the only check in the repository that can see a board that reads badly.** Every
numeric gate in this project reads the *model*, and the model is right the whole time. The Iron
Quarry's cage passed par, `ways`, `careless`, both validators, the content check and the art
audit, and at the size a phone drew it it was three brown logs (invariant 32b); Hollowmarch's road
passed everything and read as a row of disconnected sockets (33h); Emberforge's cage passed
everything and read as a tiny padlock (34e). Every one of them was caught by a render and by
nothing else. This mode's own husk was the fourth: a dark ring on dark moss, and the thing every
level is *for* was the least legible object on the board.

So this draws the real hollow out of the real chapter body with the real sprites, laid out with
`KindleView`'s own arithmetic and `KindleScreen.HostInset`'s own room. Four questions to ask of
what comes out, in order:

  1. **Can you see which embers pair?** Two alike with a clear line between them has to be
     findable at a glance, or the mode is asking the player to do arithmetic in their head -
     which is exactly what Budburst was withdrawn and rebuilt for (invariant 20l).
  2. **Are the three embers told apart by shape as well as by colour?** A heart, an emerald cut
     and a rhombus. If two read as the same silhouette at this size, one of them is wrong and a
     colour-blind player is playing a different game.
  3. **Is a sleeping critter the most legible thing on the hollow?** It is what the level is
     *for*, and a goal that reads as decoration is a goal nobody goes for (invariant 32b). Its
     ring is the colour it wants; that ring has to be readable against the moss and against
     every ember.
  4. **With `--lit`, does the crossing read?** The one square carrying two colours is the thing
     the player had to arrange, and it is the whole mode. If it does not stand out from the two
     strands that made it, the payoff is not being drawn (invariant 20m).
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

import kindle                                                           # noqa: E402
import proto                                                            # noqa: E402

ART = REPO / "Assets" / "Game" / "Art" / "Kindle"
CHAPTERS = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters"

#: The canvas the game lays out in, and the room `KindleScreen.HostInset` leaves the hollow:
#: (left, top, right, bottom). Kept in step with the screen by hand - this is a diagnostic, not
#: a rendering, and a copy that drifts is still worth more than no picture at all.
CANVAS = (1080, 1920)
INSET = (14, 250, 14, 300)

#: `Pal.Board` over the dark plate a backdrop is graded against.
PLATE = (14, 27, 37)
BACK = (9, 14, 20)

#: `Pal.EnergyColour`, mirrored. The middle channel is painted yellow, so the blends fall out of
#: the wheel a five-year-old knows rather than out of additive light (see `Pal`).
ENERGY = {
    0: (58, 80, 100),
    1: (242, 64, 79),      # R  poppy
    2: (255, 221, 87),     # G  pollen
    4: (79, 193, 255),     # B  azure
    3: (255, 138, 31),     # R|G  marigold
    5: (180, 120, 255),    # R|B  foxglove
    6: (84, 228, 140),     # G|B  verdant
    7: (255, 244, 206),    # W    radiance
}


def soft(side, colour, strength):
    """A radial glow, matching `Art.Glow(96, 1.5)` closely enough to judge a board by."""
    out = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    px = out.load()
    c = (side - 1) / 2.0
    for y in range(side):
        for x in range(side):
            d = (((x - c) ** 2 + (y - c) ** 2) ** 0.5) / c
            a = max(0.0, 1.0 - d) ** 1.5 * strength
            if a > 0.004:
                px[x, y] = colour + (int(a * 255),)
    return out


def sprite(name):
    path = ART / (name + ".png")
    if not path.exists():
        raise SystemExit("missing %s - run: python Tools/make_kindle_art.py --write"
                         % path.name)
    return Image.open(path).convert("RGBA")


def reel(name, frame=0):
    path = ART / name / ("f%02d.png" % frame)
    return Image.open(path).convert("RGBA") if path.exists() else None


#: Which sprite each authored letter draws. `KindleView.Face`, mirrored.
FACES = {
    "r": "ember_r", "g": "ember_g", "b": "ember_b",
    kindle.STONE: "stone", kindle.SPENT: "socket",
}


def layout_of(level):
    block = level["kindle"]
    grid = proto.Grid(block["rows"], block["width"], block["height"], kindle.LETTERS)
    return kindle.Layout(grid, block.get("spare") or 0)


def solved(lay):
    """One shortest answer, played out - so `--lit` shows a hollow mid-run rather than dealt.

    Breadth-first exactly as `ProtoSearch` is, and it stops at the first winning line it
    reaches: which of several equally short answers that is does not matter here, because the
    question this picture asks is whether a crossing *reads*, not which crossing.

    It hands back the strands as well as the board, because the strand is the thing the player
    actually looks at - a render that drew only the light left behind would be answering a
    question nobody asked.
    """
    from collections import deque

    start = kindle.Board(lay)
    seen = {start.key()}
    queue = deque([(start, [])])

    while queue:
        at, path = queue.popleft()
        if len(path) > proto.MAX_DEPTH:
            break

        for move in at.moves():
            forked = at.fork()
            log = forked.draw(move)
            if log is None:
                continue
            drawn = path + [(log.frm, log.to, log.channel)]
            if forked.finished():
                return forked, drawn
            key = forked.key()
            if key in seen:
                continue
            seen.add(key)
            queue.append((forked, drawn))

    return start, []


def draw(level, lit=False):
    lay = layout_of(level)
    board, strands = solved(lay) if lit else (kindle.Board(lay), [])

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

    def centre(i):
        x, y = i % w, i // w
        return margin * 2 + x * cell + cell // 2, margin * 2 + y * cell + cell // 2

    def put(im, cx, cy, scale):
        size = max(1, int(cell * scale))
        thumb = im.resize((size, size), Image.LANCZOS)
        sheet.paste(thumb, (int(cx - size / 2), int(cy - size / 2)), thumb)

    moss = sprite("moss")

    for i in range(w * h):
        cx, cy = centre(i)
        put(moss, cx, cy, 1.0)

    # The strands, drawn under everything: a coloured bar with a bright vein down the middle.
    if strands:
        line = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        ink = ImageDraw.Draw(line)
        for frm, to, channel in strands:
            ax, ay = centre(frm)
            bx, by = centre(to)
            tint = ENERGY[channel]
            ink.line([ax, ay, bx, by], fill=tint + (150,), width=max(3, int(cell * 0.30)))
            ink.line([ax, ay, bx, by], fill=(255, 250, 235, 110), width=max(1, int(cell * 0.08)))
        sheet.paste(line, (0, 0), line)

    # And the pools *over* them - only where two or more channels meet, which is
    # `KindleView.Pool`. A cell carrying one channel already has the strand running through it in
    # that colour; a cell carrying two is a colour neither strand had, and it is the only thing on
    # the board the player had to arrange.
    pool = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    for i in range(w * h):
        mask = board.light[i]
        n = kindle.channels(mask)
        if n < 2:
            continue
        cx, cy = centre(i)
        alpha = 0.92 if n > 2 else 0.78
        pool.alpha_composite(soft(int(cell * 1.02), ENERGY[mask], alpha),
                             (int(cx - cell * 0.51), int(cy - cell * 0.51)))
    sheet.paste(pool, (0, 0), pool)

    for i in range(w * h):
        c = board.cells[i]
        cx, cy = centre(i)

        if c == kindle.BARE:
            continue

        if kindle.is_sleeper(c) or c == kindle.WOKEN:
            husk = sprite("husk")
            if c == kindle.WOKEN:
                faded = husk.copy()
                faded.putalpha(husk.getchannel("A").point(lambda a: int(a * 0.35)))
                put(faded, cx, cy, 1.0)
                continue

            crew = reel("mon1")
            if crew is not None:
                ratio = crew.width / float(crew.height)
                size = int(cell * 0.66)
                thumb = crew.resize((max(1, int(size * ratio)), size), Image.LANCZOS)
                sheet.paste(thumb, (int(cx - thumb.width / 2), int(cy - thumb.height / 2)),
                            thumb)

            put(husk, cx, cy, 1.0)

            # The ring saying what it wants, which is `KindleView.Wants` and the one readout on
            # this board. Drawn last, so the render answers the question it is here to answer.
            want = kindle.want_of(c)
            ring = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
            r = int(cell * 0.46)
            ImageDraw.Draw(ring).ellipse([cx - r, cy - r, cx + r, cy + r],
                                         outline=ENERGY[want] + (245,),
                                         width=max(2, int(cell * 0.07)))
            sheet.paste(ring, (0, 0), ring)
            continue

        put(sprite(FACES[c]), cx, cy, 1.0 if c == kindle.STONE else 0.82)

    return sheet


def caption(sheet, text):
    band = Image.new("RGB", (sheet.width, 34), BACK)
    ImageDraw.Draw(band).text((6, 10), text, fill=(220, 220, 220))
    out = Image.new("RGB", (sheet.width, sheet.height + 34), BACK)
    out.paste(sheet, (0, 0))
    out.paste(band, (0, sheet.height))
    return out


def chapter():
    path = CHAPTERS / "k01_kindlewake.json"
    if not path.exists():
        sys.exit("no chapter body yet at %s" % path)
    return json.loads(path.read_text(encoding="utf-8"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--level")
    ap.add_argument("--board", help="rows, comma separated")
    ap.add_argument("--spare", type=int, default=0)
    ap.add_argument("--lit", action="store_true",
                    help="play a shortest answer first, so the crossings are on the board")
    ap.add_argument("--out", default=str(REPO / "Tools" / "kindle_boards.png"))
    args = ap.parse_args()

    if args.board:
        rows = [r.strip() for r in args.board.split(",")]
        levels = [{"id": "board", "kindle": {"width": len(rows[0]), "height": len(rows),
                                             "rows": rows, "spare": args.spare}}]
    else:
        levels = [lv for lv in chapter()["levels"]
                  if args.level is None or lv["id"] == args.level]

    if not levels:
        sys.exit("no such level")

    sheets = []
    for level in levels:
        lay = layout_of(level)
        board = kindle.Board(lay)
        par, ways, nodes, _ = proto.search(kindle.Future(board))
        read = kindle.readings(lay)
        sheets.append(caption(draw(level, args.lit),
                              "%s  %dx%d  par %d  ways %d  asleep %d  crs %d  bln %d"
                              % (level["id"], lay.w, lay.h, par, ways, board.goals(),
                                 read["crossed"], read["blended"])))

    cols = min(3, len(sheets))
    rows = (len(sheets) + cols - 1) // cols
    cw = max(s.width for s in sheets)
    ch = max(s.height for s in sheets)

    out = Image.new("RGB", (cols * cw, rows * ch), BACK)
    for i, s in enumerate(sheets):
        out.paste(s, ((i % cols) * cw, (i // cols) * ch))

    out.save(args.out)
    print("wrote %s  (%d hollow(s))" % (args.out, len(sheets)))


if __name__ == "__main__":
    main()
