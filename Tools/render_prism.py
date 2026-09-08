# -*- coding: utf-8 -*-
"""Draws every shipped Prismvale board at the size a phone draws it. Look at it.

    python Tools/render_prism.py                        # every level of the chapter
    python Tools/render_prism.py --level p01_firstvein
    python Tools/render_prism.py --board "Rrb.r.,bg@bgg,..." --out board.png
    python Tools/render_prism.py --lit                  # with a shortest answer already played

**This is the only check in the repository that can see a board that reads badly.** Every numeric
gate in this project reads the *model*, and the model is right the whole time. The Iron Quarry's
cage passed par, `ways`, `careless`, both validators, the content check and the art audit, and at
the size a phone drew it it was three brown logs (invariant 32b); Hollowmarch's road passed
everything and read as a row of disconnected sockets (33h); Emberforge's cage passed everything and
read as a tiny padlock (34e); the retired Kindlewake's husk passed everything and read as a hole
(35h). Every one of them was caught by a render and by nothing else. This mode's own lantern was
the fifth: a hooped glass drum that drew as a **crosshair**, on the one object the player must
never mistake for a gem.

So this draws the real board out of the real chapter body with the real sprites, laid out with
`PrismView`'s own arithmetic and `PrismScreen.HostInset`'s own room. Four questions to ask of what
comes out, in order:

  1. **Can you see which gems match?** The whole verb is "are these two the same", and at this size
     it has to be answerable at a glance rather than by comparing two tints.
  2. **Are the four gems told apart by shape as well as by colour?** A heart, a cabochon, a round
     brilliant and an emerald cut. If two read as one silhouette at this size, one of them is wrong
     and a colour-blind player is playing a different game.
  3. **Is a sleeping critter the most legible thing on the board?** It is what the level is *for*,
     and a goal that reads as decoration is a goal nobody goes for (invariant 32b). It has to beat
     a field of bright saturated jewels, which is a harder fight than it sounds.
  4. **With `--lit`, does the vein read as a line?** The bars between lit gems are the mode's whole
     payoff and its only readout: if the run from the lantern to the critter does not read as one
     connected thing, the mechanic is not being drawn (invariant 20m).
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

import prism                                                            # noqa: E402
import proto                                                            # noqa: E402

ART = REPO / "Assets" / "Game" / "Art" / "Prism"
CHAPTERS = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters"

#: The canvas the game lays out in, and the room `PrismScreen.HostInset` leaves the board:
#: (left, top, right, bottom). Kept in step with the screen by hand - this is a diagnostic, not a
#: rendering, and a copy that drifts is still worth more than no picture at all.
CANVAS = (1080, 1920)
INSET = (14, 250, 14, 300)

#: `Pal.Board` over the dark plate a backdrop is graded against.
PLATE = (14, 27, 37)
BACK = (9, 14, 20)

#: `PrismView.Tint`, mirrored. Four colours separated by value as well as by hue, because the
#: verb is "are these two the same" and a hue difference alone is one only some people can see.
HUES = {
    0: (255, 87, 102),      # ruby
    1: (107, 235, 133),     # emerald
    2: (102, 189, 255),     # sapphire
    3: (255, 214, 87),      # amber
}


def soft(side, colour, strength):
    """A radial glow, matching `Art.Glow(96, 1.35)` closely enough to judge a board by."""
    out = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    px = out.load()
    c = (side - 1) / 2.0
    for y in range(side):
        for x in range(side):
            d = (((x - c) ** 2 + (y - c) ** 2) ** 0.5) / c
            a = max(0.0, 1.0 - d) ** 1.35 * strength
            if a > 0.004:
                px[x, y] = colour + (int(a * 255),)
    return out


def sprite(name):
    path = ART / (name + ".png")
    if not path.exists():
        raise SystemExit("missing %s - run: python Tools/make_prism_art.py --write" % path.name)
    return Image.open(path).convert("RGBA")


def reel(name, frame=0):
    path = ART / name / ("f%02d.png" % frame)
    return Image.open(path).convert("RGBA") if path.exists() else None


#: Which sprite each authored letter draws. `PrismView.Face`, mirrored.
FACES = {"r": "gem_r", "g": "gem_g", "b": "gem_b", "y": "gem_y"}


def layout_of(level):
    block = level["prism"]
    grid = proto.Grid(block["rows"], block["width"], block["height"], prism.LETTERS)
    return prism.Layout(grid, block.get("spare") or 0)


def solved(lay):
    """One shortest answer, played out - so `--lit` shows a board mid-run rather than dealt.

    Breadth-first exactly as `ProtoSearch` is, and it stops at the first winning line it reaches:
    which of several equally short answers that is does not matter here, because the question this
    picture asks is whether a *vein* reads, not which vein.
    """
    from collections import deque

    start = prism.Board(lay)
    seen = {start.key()}
    queue = deque([start])

    while queue:
        at = queue.popleft()

        for move in at.moves():
            forked = at.fork()
            if forked.play(move) is None:
                continue
            if forked.finished():
                return forked
            key = forked.key()
            if key in seen:
                continue
            seen.add(key)
            queue.append(forked)

    return start


def draw(level, lit=False):
    lay = layout_of(level)
    board = solved(lay) if lit else prism.Board(lay)
    veins = board.veins()

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
    dim = moss.copy()
    dim.putalpha(moss.getchannel("A").point(lambda a: int(a * 0.55)))

    for i in range(w * h):
        cx, cy = centre(i)
        put(dim if lay.grid.cells[i] == prism.BARE else moss, cx, cy, 1.0)

    # The veins, drawn under the pieces: a bar between every pair of touching cells the light runs
    # through, which is `PrismView.Relink`. This is the mode's only readout and the thing the whole
    # render exists to judge.
    line = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    ink = ImageDraw.Draw(line)

    for i in range(w * h):
        for nb in lay.around(i):
            if nb < i:
                continue

            here, there = veins[i], veins[nb]
            ca, cb = board.cells[i], board.cells[nb]

            if here >= 0 and here == there:
                tint = HUES[here]
            elif prism.is_lamp(ca) and there >= 0 and prism.hue_of(ca) == there:
                tint = HUES[there]
            elif prism.is_lamp(cb) and here >= 0 and prism.hue_of(cb) == here:
                tint = HUES[here]
            else:
                continue

            ax, ay = centre(i)
            bx, by = centre(nb)
            ink.line([ax, ay, bx, by], fill=tint + (170,), width=max(3, int(cell * 0.40)))
            ink.line([ax, ay, bx, by], fill=(255, 250, 235, 90), width=max(1, int(cell * 0.10)))

    sheet.paste(line, (0, 0), line)

    for i in range(w * h):
        c = board.cells[i]
        cx, cy = centre(i)

        if c == prism.BARE:
            continue

        if c == prism.ASLEEP or c == prism.AWAKE:
            husk = sprite("husk")
            if c == prism.AWAKE:
                faded = husk.copy()
                faded.putalpha(husk.getchannel("A").point(lambda a: int(a * 0.30)))
                put(faded, cx, cy, 1.0)
                continue

            put(husk, cx, cy, 1.0)

            crew = reel("mon1")
            if crew is not None:
                ratio = crew.width / float(crew.height)
                size = int(cell * 0.62)
                thumb = crew.resize((max(1, int(size * ratio)), size), Image.LANCZOS)
                sheet.paste(thumb, (int(cx - thumb.width / 2), int(cy - thumb.height / 2)),
                            thumb)

            # The ring the view breathes around a sleeper, which is the one thing saying it is
            # still waiting. Drawn last, so the render answers the question it is here to answer.
            ring = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
            r = int(cell * 0.47)
            ImageDraw.Draw(ring).ellipse([cx - r, cy - r, cx + r, cy + r],
                                         outline=(255, 244, 206, 200),
                                         width=max(2, int(cell * 0.06)))
            sheet.paste(ring, (0, 0), ring)
            continue

        if prism.is_lamp(c):
            hue = HUES[prism.hue_of(c)]
            alight = any(veins[nb] == prism.hue_of(c) for nb in lay.around(i))

            glow = soft(int(cell * 1.7), hue, 0.55 if alight else 0.0)
            if alight:
                sheet.paste(glow, (int(cx - cell * 0.85), int(cy - cell * 0.85)), glow)

            lamp = sprite("lamp" if alight else "lamp_dark")
            tinted = Image.new("RGBA", lamp.size, hue + (255,))
            tinted.putalpha(lamp.getchannel("A"))
            body = Image.composite(tinted, lamp, Image.new("L", lamp.size, 110))
            put(body, cx, cy, 0.92)
            continue

        # A gem. Lit ones wear a halo and are drawn a shade larger, which is `PrismView.Shine` -
        # three differences rather than one, so the board's whole question can be answered without
        # relying on hue.
        if veins[i] >= 0:
            halo = soft(int(cell * 1.3), HUES[veins[i]], 0.55)
            sheet.paste(halo, (int(cx - cell * 0.65), int(cy - cell * 0.65)), halo)

        put(sprite(FACES[c]), cx, cy, 0.78 * (1.06 if veins[i] >= 0 else 0.92))

    return sheet


def caption(sheet, text):
    band = Image.new("RGB", (sheet.width, 34), BACK)
    ImageDraw.Draw(band).text((6, 10), text, fill=(220, 220, 220))
    out = Image.new("RGB", (sheet.width, sheet.height + 34), BACK)
    out.paste(sheet, (0, 0))
    out.paste(band, (0, sheet.height))
    return out


def chapter():
    path = CHAPTERS / "p01_prismvale.json"
    if not path.exists():
        sys.exit("no chapter body yet at %s" % path)
    return json.loads(path.read_text(encoding="utf-8"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--level")
    ap.add_argument("--board", help="rows, comma separated")
    ap.add_argument("--spare", type=int, default=0)
    ap.add_argument("--lit", action="store_true",
                    help="play a shortest answer first, so the veins are on the board")
    ap.add_argument("--out", default=str(REPO / "Tools" / "prism_boards.png"))
    args = ap.parse_args()

    if args.board:
        rows = [r.strip() for r in args.board.split(",")]
        levels = [{"id": "board", "prism": {"width": len(rows[0]), "height": len(rows),
                                            "rows": rows, "spare": args.spare}}]
    else:
        levels = [lv for lv in chapter()["levels"]
                  if args.level is None or lv["id"] == args.level]

    if not levels:
        sys.exit("no such level")

    sheets = []
    for level in levels:
        lay = layout_of(level)
        board = prism.Board(lay)
        par, ways, nodes, _ = proto.search(prism.Future(board))
        read = prism.readings(lay)
        sheets.append(caption(draw(level, args.lit),
                              "%s  %dx%d  par %d  ways %d  asleep %d  dealt %d  used %d"
                              % (level["id"], lay.w, lay.h, par, ways, board.goals(),
                                 read["dealt"], read["used"])))

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
