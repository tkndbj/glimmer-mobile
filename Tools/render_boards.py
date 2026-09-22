#!/usr/bin/env python3
"""Draws `LeaderboardScreen` offline, at the canvas a phone lays it out against.

    python Tools/render_boards.py                # the Endless Watch, a full board
    python Tools/render_boards.py --unranked     # every row's badge absent, which is the
                                                 # state every board is in until a deploy
    python Tools/render_boards.py --mixed        # some ranked, some not — the real first week
    python Tools/render_boards.py --empty        # each of the six refusals, side by side
    python Tools/render_boards.py --row          # one row at 1:1, which is what the badge
                                                 # question is actually about
    python Tools/render_boards.py --contact      # all of the above on one sheet

**Written because this screen had no mirror at all.** It is the screen a player meets the
moment they tap BOARDS, it is a hundred rows of one widget, and every question about it — is
the badge big enough to tell bronze from gold, does a long name reach the figure beside it,
does a row with no badge read as broken — cost a device build until this existed. That is
`render_ward_preview.py`'s argument, about the other screen nobody could see.

**What it can answer and what it cannot.** It mirrors `LeaderboardScreen.Row`'s geometry
constant for constant, so it answers "is this widget where I think it is" and "does this
caption fit". It cannot answer whether the palette is any good — a render is much weaker at
that — and it cannot answer whether a hundred of these scroll pleasantly.

**The badges are the shipped PNGs**, cut by `Tools/make_rank_art.py`, so the sizes here are the
sizes a phone draws. The plates are the shipped nine-slices through `hudkit`, which mirrors
`UIKit` and `Skins`.
"""

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import hudkit as k                                        # noqa: E402
from PIL import Image                                     # noqa: E402

REPO = Path(__file__).resolve().parents[1]
RANK_ART = REPO / "Assets" / "Game" / "Art" / "Ui" / "Rank"
PROGRESSION = REPO / "Assets" / "StreamingAssets" / "Content" / "progression.json"
LOC = REPO / "Assets" / "StreamingAssets" / "Content" / "loc" / "en.json"

# ---------------------------------------------------------------- LeaderboardScreen
# Every one of these is named after the field it mirrors. A typed copy that drifted would be a
# mirror telling a comfortable lie about the screen, which is worse than no mirror at all
# (invariant 44d).
HEADER_H = 208.0
ROW_H = 200.0
PLATE_W, PLATE_H = 940.0, 160.0
BADGE = 132.0
BOTTOM_PAD = 24.0
GRID_W = 960.0

# Row.  (x from the plate's left edge, y from its centre)
PLACE_X, PLACE_BOX, PLACE_PT = 78.0, (120.0, 66.0), 40
BADGE_X = 214.0
NAME_X, NAME_Y, NAME_BOX, NAME_PT = 515.0, 24.0, (430.0, 50.0), 34
WORTH_X, WORTH_Y, WORTH_BOX, WORTH_PT = 515.0, -26.0, (430.0, 40.0), 28

SHRINK_FLOOR_PLACE, SHRINK_FLOOR_NAME, SHRINK_FLOOR_WORTH = 20, 20, 18

# The name frame on the player's own row (`LeaderboardScreen.FrameW/FrameH/FrameX`, `Row.Seat`).
# The painting is drawn still here — a mirror draws a state, never a sequence (48l) — at the
# bind pose, which is the frame at rest. The hole is `FrameCatalog`'s, as fractions, y up.
FRAME_W, FRAME_H, FRAME_X, FRAME_Y = PLATE_W, PLATE_W / 3.0, PLATE_W / 2.0, 10.0
FRAMED_PLACE_X, FRAMED_PLACE_BOX, FRAMED_PLACE_PT = 50.0, (84.0, 60.0), 34
FRAMED_BADGE, FRAMED_BADGE_INSET = 104.0, 14.0      # the badge at the hole's right end
FRAMED_TEXT_LEFT, FRAMED_TEXT_GAP = 106.0, 12.0
FRAMED_NAME_PT, FRAMED_WORTH_PT = 32, 30
FRAMED_NAME_H, FRAMED_WORTH_H = 42.0, 38.0
FRAMES_ART = REPO / "Assets" / "Game" / "Art" / "Frames"
FRAME_HOLES = {"dragon": (0.2947, 0.2790, 0.6515, 0.3812)}   # x, y (from the bottom), w, h
FRAME_PLATES = {"dragon": (0.1381, 0.1989, 0.8218, 0.5387)}  # `FrameDefinition.Plate`, the same way


def rungs():
    """The shipped ladder, humblest first — the same order `RankLadder` builds."""
    block = json.loads(PROGRESSION.read_text(encoding="utf8")).get("ranks", {})
    return [r["id"] for r in block.get("rungs", [])]


def strings():
    rows = json.loads(LOC.read_text(encoding="utf8"))["entries"]
    return {r["key"]: r["text"] for r in rows}


def badge(rid):
    """One badge, as the shipped PNG. None for a rung this build does not carry — `RankArt`."""
    path = RANK_ART / (rid + ".png")
    return Image.open(path).convert("RGBA") if path.exists() else None


# ------------------------------------------------------------------------- the row
def draw_row(sheet, top, entry, mine=False):
    """One row, drawn exactly where `LeaderboardScreen.Row` builds it."""
    cx = k.W / 2
    cy = top + ROW_H / 2

    plate_name = "Hud/plate_orange" if mine else "Hud/plate_blue"
    left = cx - PLATE_W / 2

    frame = entry.get("frame") if mine else None
    art_path = FRAMES_ART / (frame + ".png") if frame else None
    framed = bool(frame) and art_path.exists() and frame in FRAME_HOLES
    if framed:
        # The plate hides behind the frame, filling the painting's plate box (`Row.Seat`).
        px, py, pw, ph = FRAME_PLATES[frame]
        k.paste(sheet, k.skin(plate_name, pw * FRAME_W, ph * FRAME_H),
                left + FRAME_X + (px + pw / 2 - .5) * FRAME_W,
                cy - FRAME_Y - ((py + ph / 2) - .5) * FRAME_H)
    else:
        k.paste(sheet, k.skin(plate_name, PLATE_W, PLATE_H), cx, cy)

    # The place. Shrinkable, so a three-digit position is drawn smaller rather than clipped.
    k.shrunk(sheet, str(entry["place"]), left + PLACE_X, cy,
             PLACE_BOX[0], PLACE_BOX[1], PLACE_PT, SHRINK_FLOOR_PLACE, k.CREAM)

    # The badge. `preserveAspect`, and **absent when there is none** — which is an ordinary
    # state, not a fault: every keeper below the first rung, and every card written before the
    # server learned to derive a rung.
    art = badge(entry["rung"]) if entry.get("rung") else None

    if framed:
        # Framed: the painting across the whole plate, its hole centred on the row, and the
        # place, the badge and the two lines inside the hole at the smaller sizes (`Row.Seat`).
        painting = Image.open(art_path).convert("RGBA")
        k.paste(sheet, k.fit(painting, (FRAME_W, FRAME_H)), left + FRAME_X, cy - FRAME_Y)

        hx, hy, hw, hh = FRAME_HOLES[frame]
        x0 = left + FRAME_X + (hx - .5) * FRAME_W
        x1 = x0 + hw * FRAME_W
        hole_cy = cy - FRAME_Y - ((hy + hh / 2) - .5) * FRAME_H   # y up in the frame, y down here
        k.shrunk(sheet, str(entry["place"]), x0 + FRAMED_PLACE_X, hole_cy,
                 FRAMED_PLACE_BOX[0], FRAMED_PLACE_BOX[1], FRAMED_PLACE_PT, SHRINK_FLOOR_PLACE, k.CREAM)
        badge_x = x1 - FRAMED_BADGE_INSET - FRAMED_BADGE / 2
        if art is not None:
            k.paste(sheet, k.fit(art, (FRAMED_BADGE, FRAMED_BADGE)), badge_x, hole_cy)
        text_left = x0 + FRAMED_TEXT_LEFT
        text_w = badge_x - FRAMED_BADGE / 2 - FRAMED_TEXT_GAP - text_left
        split = hole_cy - 2
        k.shrunk_left(sheet, entry["name"], text_left, split - FRAMED_NAME_H, text_w, FRAMED_NAME_H,
                      FRAMED_NAME_PT, SHRINK_FLOOR_NAME, (255, 247, 230))
        k.shrunk_left(sheet, entry["figure"], text_left, split, text_w, FRAMED_WORTH_H,
                      FRAMED_WORTH_PT, SHRINK_FLOOR_WORTH, k.GOLD)
        return

    if art is not None:
        k.paste(sheet, k.fit(art, (BADGE, BADGE)), left + BADGE_X, cy)

    # `UIKit.Titled` at MiddleLeft inside a box that pivots at centre, so the text starts at the
    # box's own left edge — `UIKit.Box` always pivots at centre (invariant 44d, which is where
    # three "faults" in another mirror turned out to live).
    k.shrunk_left(sheet, entry["name"], left + NAME_X - NAME_BOX[0] / 2, cy - NAME_Y - NAME_BOX[1] / 2,
                  NAME_BOX[0], NAME_BOX[1], NAME_PT, SHRINK_FLOOR_NAME,
                  (255, 247, 230))

    k.shrunk_left(sheet, entry["figure"], left + WORTH_X - WORTH_BOX[0] / 2,
                  cy - WORTH_Y - WORTH_BOX[1] / 2,
                  WORTH_BOX[0], WORTH_BOX[1], WORTH_PT, SHRINK_FLOOR_WORTH, k.GOLD)


def board(entries, title="BOARDS"):
    sheet = Image.new("RGBA", (k.W, k.H), k.GROUND)
    k.plain(sheet)

    # The banner, and the two keys beside it.
    k.paste(sheet, k.skin("Hud/title", 470, 128), k.W / 2, 106 + 64)
    k.text(sheet, title, k.W / 2, 106 + 64, 38, k.CREAM)

    # The player's own row is drawn last, as the screen raises it over its neighbours when it
    # wears a frame (`Row.Seat`): the frame overhangs the plate and must not sit under the row
    # below it.
    top = HEADER_H
    later = []
    for entry in entries:
        if top + ROW_H > k.H - k.NAV_HEIGHT - BOTTOM_PAD:
            break
        if entry.get("mine", False):
            later.append((top, entry))
        else:
            draw_row(sheet, top, entry, False)
        top += ROW_H
    for row_top, entry in later:
        draw_row(sheet, row_top, entry, True)

    k.navbar(sheet, "ranks")
    return sheet


# ------------------------------------------------------------------------ the cases
NAMES = ["Fern Willow", "Thornbite", "Ash", "Marigold Quickstep", "Bram",
         "Silverleaf Wanderer", "Pip", "Hollyhock"]


def entries(ladder, ranked="all", frame=None):
    out = []
    for i, name in enumerate(NAMES):
        if ranked == "all":
            rung = ladder[max(0, len(ladder) - 1 - i)] if ladder else ""
        elif ranked == "none":
            rung = ""
        else:
            rung = ladder[max(0, len(ladder) - 1 - i)] if (ladder and i % 3 != 1) else ""

        out.append({
            "place": i + 1,
            "name": name,
            "rung": rung,
            "figure": "Wave %d" % (120 - i * 9),
            "mine": i == 3,
            "frame": frame if i == 3 else None,
        })
    return out


def one_row(ladder, frame=None):
    """A single row at 1:1 against a ruler, which is what the badge question is about."""
    pad = 40
    sheet = Image.new("RGBA", (int(k.W), int(ROW_H * 3 + pad * 2)), k.GROUND)

    shown = [
        {"place": 1, "name": "Fern Willow", "rung": ladder[-1] if ladder else "",
         "figure": "Wave 120", "mine": False},
        {"place": 42, "name": "Marigold Quickstep", "rung": ladder[0] if ladder else "",
         "figure": "Wave 31", "mine": True, "frame": frame},
        {"place": 100, "name": "Pip", "rung": "",
         "figure": "Wave 4", "mine": False},
    ]

    # The player's own row last, for `board()`'s reason.
    for i, entry in enumerate(shown):
        if not entry["mine"]:
            draw_row(sheet, pad + i * ROW_H, entry, False)
    for i, entry in enumerate(shown):
        if entry["mine"]:
            draw_row(sheet, pad + i * ROW_H, entry, True)

    return sheet


def empties(loc):
    """The six refusals, side by side. Each one has to say which it is (`PaintEmpty`)."""
    keys = ["ui.board.offline", "ui.board.loading", "ui.board.no_connection",
            "ui.board.failed", "ui.board.no_rows"]

    cell_w, cell_h = 520, 240
    sheet = Image.new("RGBA", (cell_w * 3, cell_h * 2), k.GROUND)

    for i, key in enumerate(keys):
        cx = (i % 3) * cell_w + cell_w / 2
        cy = (i // 3) * cell_h + cell_h / 2
        k.paste(sheet, k.skin("Hud/plate_blue", cell_w - 40, cell_h - 60), cx, cy)
        k.shrunk(sheet, loc.get(key, "(missing)"), cx, cy, cell_w - 90, cell_h - 100, 28, 18,
                 (255, 245, 220))
        k.text(sheet, key, cx, cy + cell_h / 2 - 22, 18, k.MUTED, outline=2)

    return sheet


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--unranked", action="store_true", help="no row carries a badge")
    ap.add_argument("--mixed", action="store_true", help="some ranked, some not")
    ap.add_argument("--empty", action="store_true", help="the six refusals")
    ap.add_argument("--row", action="store_true", help="three rows at 1:1")
    ap.add_argument("--contact", action="store_true", help="all of it on one sheet")
    ap.add_argument("--framed", metavar="ID", nargs="?", const="dragon", default=None,
                    help="the player's own row wears this name frame (default: dragon)")
    args = ap.parse_args()

    ladder = rungs()
    loc = strings()
    out = REPO / ("boards_framed.png" if args.framed else "boards.png")
    frame = args.framed

    if args.contact:
        full = board(entries(ladder, "all", frame))
        mixed = board(entries(ladder, "mixed", frame))
        bare = board(entries(ladder, "none", frame))
        rows = one_row(ladder, frame)

        scale = 0.42
        small = [im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
                 for im in (full, mixed, bare)]

        gap = 24
        width = sum(im.width for im in small) + gap * 4
        height = max(im.height for im in small) + rows.height + gap * 3

        sheet = Image.new("RGBA", (max(width, rows.width + gap * 2), height), (18, 22, 34, 255))
        x = gap
        for im in small:
            sheet.alpha_composite(im, (x, gap))
            x += im.width + gap

        sheet.alpha_composite(rows, (gap, small[0].height + gap * 2))
        sheet.save(out)
        print(f"wrote {out}  {sheet.size[0]}x{sheet.size[1]}  - look at it")
        return 0

    if args.row:
        sheet = one_row(ladder, frame)
    elif args.empty:
        sheet = empties(loc)
    elif args.unranked:
        sheet = board(entries(ladder, "none", frame))
    elif args.mixed:
        sheet = board(entries(ladder, "mixed", frame))
    else:
        sheet = board(entries(ladder, "all", frame))

    sheet.save(out)
    print(f"wrote {out}  {sheet.size[0]}x{sheet.size[1]}  - look at it")
    return 0


if __name__ == "__main__":
    sys.exit(main())
