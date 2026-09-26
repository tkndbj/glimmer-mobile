# -*- coding: utf-8 -*-
"""Draws every shipped daily challenge at the size a phone draws it, with the real sprites.

    python Tools/render_challenges.py                     # every row of challenges.json
    python Tools/render_challenges.py --id d04_pipes
    python Tools/render_challenges.py --contact           # all four side by side
    python Tools/render_challenges.py --phone             # a 19.5:9 canvas instead of 16:9
    python Tools/render_challenges.py --out out/challenges.png
    python Tools/render_challenges.py --list               # the list page: deal band, cards, badges
    python Tools/render_challenges.py --list --held gold --spent pairs,merge
    python Tools/render_challenges.py --deals               # the deal sheet on the victory frame
    python Tools/render_challenges.py --deals --held bronze

**Why this exists.** `ChallengeTests` plays every row with a bot and proves it is winnable; it
says nothing about whether the screen *reads*. Four boards share one band arithmetic
(`ChallengeScreen.BuildBands`, `PuzzleView.Attach`, `PuzzleView.BandWanted`), and the
questions no fixture can answer are the ones this picture is for: does a 10x6 room and a 6x3
grid both get a cell a finger can use, does the hill the board leaves give the raiders a walk
worth watching, do the four posts line up under the four lanes, and does a strip of keys under
a board push the board into the line. **The board is asked first and the hill takes the
rest** (2026-09-23): a board is laid out at the widest cell the width allows, so the hill's
height is what a wide, short board leaves it, bounded in siege cells either way. **The band is a
framed plate** (2026-09-26): the kit's panel fills whatever the hill leaves, the grid's plate
stands inside it as the well, and the hill's ceiling is eight cells, so what a tall phone has
over that is frame rather than the bare slab the owner circled.

**It is a mirror, and mirrors drift.** Every constant is named after the field it copies
(`ChallengeScreen.HillLeastUnits`, `PuzzleView.Margin`, `ChallengeHillView.PostScale`), the board
is read out of `challenges.json` through the same shape the game reads, and when a device
disagrees with this picture the picture is the one that is wrong (invariant 44d). The
genre-specific furniture is drawn as the views draw it — sockets and gems, pipes as arms from
a hub, walls and pads — at rest, before any move; nothing here animates.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402
import render_siege as S                                    # noqa: E402

REPO = K.REPO
CONTENT = REPO / "Assets" / "StreamingAssets" / "Content"
LOC = CONTENT / "loc" / "en.json"
FILE = CONTENT / "challenges.json"

W = K.W
LETTERS = "rgby"

#: `SiegeView.Tints` — poppy, mint, azure, amber.
TINTS = [(242, 64, 79), (123, 216, 106), (79, 193, 255), (255, 138, 43)]

# ------------------------------------------------------------------ ChallengeScreen
#: `ChallengeScreen.BannerW` / `.BannerH` / `.BannerSize`, `.ChromeSize`.
BANNER_W, BANNER_H, BANNER_SIZE, CHROME = 620.0, 112.0, 34, 92.0
RIBBON_TILT, RIBBON_ROOM, TITLE_FLOOR = -1.6, 0.74, 24

#: `ChallengeScreen.ReadoutH` / `.ReadoutGap`.
READOUT_H, READOUT_GAP = 56.0, 12.0

#: `ChallengeScreen.HillLeastUnits` / `.HillMostUnits`, `.LineBandUnits`, `.BottomPad`, `.BandGap`, `.PuzzleInset`.
HILL_LEAST_UNITS, HILL_MOST_UNITS = 3.6, 8.0
LINE_BAND_UNITS, BOTTOM_PAD, BAND_GAP, PUZZLE_INSET = 1.7, 0.0, 0.0, 0.0
#: `ChallengeScreen.Ground`: the opaque ground the puzzle band stands on, edge to edge.
GROUND = (15, 42, 74)

#: `PuzzleView.FrameInset` / `.FrameRim` / `.FrameSide` / `.PlateRim`, and each view's `MaxCell` / `StripHeight`.
FRAME_INSET, FRAME_RIM, PLATE_RIM = 10.0, 28.0, 0.34
FRAME_SIDE = FRAME_INSET + FRAME_RIM
#: `MergeView.LadderGap`: the air between the plate's foot and the ladder.
LADDER_GAP = 8.0
MAX_CELL = {"pairs": 200, "glade": 190, "merge": 200, "sokoban": 150}
STRIP = {}
#: Each view's `EdgeRows`: what a board hangs past its plate, above and below together, in cells,
#: and `EdgeBelow`, the part of it under the plate. `MergeView.LadderRows` is the rank ladder.
LADDER_ROWS = 0.58
EDGE_ROWS = {"merge": LADDER_ROWS}
EDGE_BELOW = {"merge": LADDER_ROWS}
#: `MergeView.Dim`: an unlit rung, the gem dimmed toward the plate.
RUNG_DIM = (117, 133, 158)

#: `ChallengeHillView.RaiderTall`, `.PostScale` and `.HillTopInset`.
RAIDER_TALL, POST_SCALE, HILL_TOP_INSET = 1.0, 0.72, 0.95

#: `Pal.Slot`, `Pal.Dormant`, the pairs back, the mine slab, the sokoban wall.
SLOT = (255, 255, 255)
DORMANT = (58, 80, 100)
BACK = (31, 56, 87)
SLAB = (77, 107, 143)
WALL, WALL_FACE = (51, 43, 41), (92, 77, 69)
#: `GladeView.Slate`, the first chapter's slate the glade's floor is themed from.
GLADE_SLATE = (9, 22, 47)


def loc():
    table = json.load(open(LOC, encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


def rows():
    return json.load(open(FILE, encoding="utf-8"))["challenges"]


def wave_at(row, turn):
    """The raiders standing on the hill before the first move: the waves with turn nought."""
    out = []
    for wave in row.get("waves") or []:
        parts = wave.split()
        if int(parts[0]) != turn:
            continue
        for tok in parts[1:]:
            out.append((LETTERS.index(tok[0]), int(tok[1:])))
    return out


# ------------------------------------------------------------------ the chrome
def chrome(sheet, txt, row):
    cy = 22.0 + BANNER_H / 2.0

    key = K.skin("sq_blue", CHROME, CHROME)
    K.paste(sheet, key, 76.0, cy)
    icon = K.fit(K.load("ic_left")[0], (CHROME * .5, CHROME * .5))
    K.paste(sheet, K.tint(icon, K.CREAM), 76.0, cy - CHROME * .0231)

    ribbon = K.skin("ribbon_orange", BANNER_W, BANNER_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.one_line(plate, txt("challenge.%s.name" % row["id"]).upper(), plate.width / 2, plate.height / 2,
               BANNER_W * RIBBON_ROOM, BANNER_SIZE, TITLE_FLOOR, outline=4)
    K.paste(sheet, plate.rotate(RIBBON_TILT, Image.BICUBIC, expand=True), W / 2, cy)

    ry = cy + BANNER_H / 2 + READOUT_GAP + READOUT_H / 2
    for x, wide, caption in ((40 + 150, 300, txt("ui.challenges.turns").replace("{0}", "0")),
                             (W / 2, 360, goal(txt, row)),
                             (W - 40 - 150, 300, next_wave(txt, row))):
        if not caption:
            continue
        K.paste(sheet, K.round_rect(wide, READOUT_H, 28, (15, 31, 43), .72), x, ry)
        K.paste(sheet, K.round_rect(wide, READOUT_H, 28, (255, 255, 255), .13, width=3), x, ry)
        K.shrunk(sheet, caption, x, ry, wide - 36, READOUT_H - 4, 24, 16)

    return cy + BANNER_H / 2 + READOUT_GAP + READOUT_H + READOUT_GAP


def goal(txt, row):
    g = row["genre"]
    w, h = row["width"], row["height"]
    if g == "pairs":
        return txt("ui.challenges.pairs").replace("{0}", "0").replace("{1}", str(w * h // 2))
    if g == "glade":
        lamps = sum(1 for r in row["rows"] for tok in r.split() if tok.startswith("@"))
        return txt("ui.challenges.lit").replace("{0}", "0").replace("{1}", str(lamps))
    if g == "merge":
        return txt("ui.challenges.rank").replace("{0}", str(1 << row["target"]))
    if g == "sokoban":
        pads = sum(1 for r in row["rows"] for ch in r if ch in "RGBY")
        return txt("ui.challenges.pads").replace("{0}", "0").replace("{1}", str(pads))
    return ""


def next_wave(txt, row):
    later = [int(w.split()[0]) for w in row["waves"] if int(w.split()[0]) > 0]
    return txt("ui.challenges.next").replace("{0}", str(min(later))) if later else ""


# ------------------------------------------------------------------ the hill and the line
def hill(sheet, row, top, height, unit, line_band, txt):
    """`ChallengeHillView.Build` — the ground in a masked band, four lanes, four posts."""
    host_h = height
    hill_top = unit * HILL_TOP_INSET                  # from the host's top edge, image y down
    hill_foot = host_h - line_band
    line_y = hill_foot + line_band * .30

    # The ground, enveloped into its band and clipped to it.
    band_w, band_h = W, (hill_foot - hill_top + unit * HILL_TOP_INSET) + unit * .5
    rock = S.sprite("hill1")
    s = max(band_w / rock.width, band_h / rock.height)
    rock = rock.resize((int(rock.width * s), int(rock.height * s)), Image.LANCZOS)
    band = Image.new("RGBA", (int(band_w), int(band_h)), (0, 0, 0, 0))
    band.alpha_composite(rock, ((band.width - rock.width) // 2, (band.height - rock.height) // 2))
    K.paste(sheet, band, W / 2, top + (hill_top + hill_foot) / 2)

    posts = 4

    def post_x(i):
        return W / 2 + (i - (posts - 1) * .5) * (W / (posts + .6))

    for i in range(posts):
        lane = K.round_rect(unit * .9, hill_foot - hill_top + unit * HILL_TOP_INSET, 40, TINTS[i], .10)
        K.paste(sheet, lane, post_x(i), top + (hill_top + hill_foot) / 2)

    # The rampart under the posts.
    K.paste(sheet, K.nine(*S_load("rampart"), W, line_band * 1.02), W / 2, top + hill_foot + line_band * .5)

    # A post and everything on it is sized off `ChallengeHillView.PostScale` of the unit.
    post = unit * POST_SCALE
    for i in range(posts):
        cx, cy = post_x(i), top + line_y
        K.paste(sheet, K.fit(S.sprite("socket"), (post * 1.7, post * .8)), cx, cy + post * .88)
        K.paste(sheet, K.glow(int(post * 3.1), 2.1, TINTS[i], .22), cx, cy)
        body = K.fit(S.sprite("Wards/bolt_%s" % LETTERS[i]), (post * 1.72, post * 2.15))
        K.paste(sheet, body, cx, cy - post * .06)

        bar_w, bar_h = post * 1.06, post * .17
        K.paste(sheet, K.round_rect(bar_w, bar_h, 10, (0, 0, 0), .66), cx, cy - post * 1.26)
        K.paste(sheet, K.round_rect(bar_w - 4, bar_h - 4, 10, K.CREAM), cx, cy - post * 1.26)

    # The raiders standing at the top of the hill before the first move.
    length = row["hill"]
    for n, (colour, health) in enumerate(wave_at(row, 0)):
        side = ((n % 3) - 1) * unit * .28
        cx = post_x(colour) + side
        t = 1.0 - min(1.0, length / float(length))
        cy = top + hill_top + (hill_foot - unit * .45 - hill_top) * t
        tall = unit * RAIDER_TALL
        frame = S.reel("mon_%s" % LETTERS[colour])
        if frame is not None:
            wide = tall * frame.width / frame.height
            S.shadow(sheet, cx, cy - 0.04 * tall + tall * .94 * .21, wide, tall)
            K.paste(sheet, frame.resize((int(wide), int(tall)), Image.LANCZOS), cx, cy - .04 * tall)
        bar_w, bar_h = tall * .72, unit * .13
        K.paste(sheet, K.round_rect(bar_w, bar_h, 10, (0, 0, 0), .66), cx, cy - tall * .58)
        K.paste(sheet, K.round_rect(bar_w - 4, bar_h - 4, 10, K.ROSE), cx, cy - tall * .58)
        gem = K.fit(S.sprite("gem_%s" % LETTERS[colour]), (unit * .34, unit * .34))
        K.paste(sheet, gem, cx, cy - tall * .58 - unit * .24)


def S_load(name):
    im = S.sprite(name)
    return im, K.border_of(S.ART / (name + ".png"))


# ------------------------------------------------------------------ the puzzle band
def cell_across(genre, cols, host_w):
    """`PuzzleView.CellAcross`: the cell a board gets across its host, plate and cap counted in."""
    return int(min((host_w - FRAME_SIDE * 2) / (cols + PLATE_RIM), MAX_CELL[genre]))


def band_wanted(genre, cols, rws, host_w):
    """`PuzzleView.BandWanted`: how tall a band the board asks for at the cell the width allows."""
    return (rws + PLATE_RIM + EDGE_ROWS.get(genre, 0)) * cell_across(genre, cols, host_w) + FRAME_SIDE * 2 + STRIP.get(genre, 0)


def puzzle(sheet, row, top, bottom, txt):
    """`PuzzleView.Attach` and the genre's `Build`, at rest."""
    g = row["genre"]
    cols, rws = row["width"], row["height"]
    host_w, host_h = W - PUZZLE_INSET * 2, bottom - top
    strip = STRIP.get(g, 0)
    cx = W / 2

    # `PuzzleView.Attach`: the kit's panel fills the band to FrameInset of its edges, and the
    # board is laid out in what is left inside FrameRim of the panel's edge.
    K.paste(sheet, K.skin("Hud/panel", host_w - FRAME_INSET * 2, host_h - FRAME_INSET * 2), cx, top + host_h / 2)
    inner_w, inner_h = host_w - FRAME_SIDE * 2, host_h - FRAME_SIDE * 2

    cell = int(min(cell_across(g, cols, host_w), (inner_h - strip) / (rws + PLATE_RIM + EDGE_ROWS.get(g, 0))))
    span_w, span_h = cols * cell, rws * cell
    cy = top + FRAME_SIDE + (inner_h - strip) / 2    # the field is centred above the strip, inside the frame
    # `PuzzleView.Attach`: furniture hung below the plate alone shifts the field up by half of it.
    edge = EDGE_ROWS.get(g, 0)
    cy -= (2 * EDGE_BELOW.get(g, edge / 2) - edge) * cell / 2

    if g != "glade":                                  # the glade brings its own floor (`PuzzleView.DrawsPlate`)
        plate = K.nine(*S_load("plate"), span_w + cell * PLATE_RIM, span_h + cell * PLATE_RIM)
        K.paste(sheet, plate, cx, cy)

    def centre(i):
        x, y = i % cols, i // cols
        return cx + (x - (cols - 1) * .5) * cell, cy - ((rws - 1) * .5 - y) * cell

    def socket(i, inset=.92):
        K.paste(sheet, K.round_rect(cell * inset, cell * inset, 16, SLOT, .045), *centre(i))

    def gem(i, colour, scale=.82, alpha=1.0):
        im = K.fit(S.sprite("gem_%s" % LETTERS[colour]), (cell * scale, cell * scale))
        if alpha < 1.0:
            im = K.tint(im, (255, 255, 255), alpha)
        K.paste(sheet, im, *centre(i))

    if g == "pairs":
        for i in range(cols * rws):
            socket(i)
            K.paste(sheet, K.round_rect(cell * .86, cell * .86, 18, BACK), *centre(i))
            K.paste(sheet, K.round_rect(cell * .34, cell * .34, 8, K.CREAM, .35), *centre(i))

    elif g == "glade":
        # The glade is the mode's own `BoardView` standing in the band (2026-09-23), so this
        # branch mirrors `BoardView.Build` and `TileView.Build` at rest rather than the shared
        # plate: the floor at the board's pitch, a slot per used cell, arms in the theme's
        # base colour, a crystal on its glow, a sleeping critter in its halo. Nothing is lit,
        # because no shipped row opens lit and the light is `Puzzle`'s alone (5b). Tokens are
        # the glade grammar, read as far as the drawing needs.
        pad, cap = 34.0, 190.0                                   # BoardView's pad and pitch clamp
        # `GladeView.Build`: the board is built into the frame's inside, so its pitch is read off that.
        pitch = max(64.0, min((inner_w - pad * 2) / cols, (inner_h - strip - pad * 2) / rws, cap))
        board_w, board_h = pitch * cols, pitch * rws
        size = pitch * .965
        slate = GLADE_SLATE
        floor = K.round_rect(board_w + 44, board_h + 44, 40, slate, .87)
        K.paste(sheet, floor, cx, cy)
        K.paste(sheet, K.round_rect(board_w + 44, board_h + 44, 40, (255, 255, 255), .19, width=4), cx, cy)
        arm_base = tuple(int(a + (b - a) * .44) for a, b in zip(slate, (148, 184, 214)))
        hub_col = tuple(int(a + (b - a) * .54) for a, b in zip(slate, (168, 204, 235)))
        thick = round(size * .175)
        energy = {"R": (242, 64, 79), "G": (255, 221, 87), "B": (79, 193, 255), "Y": (255, 138, 31),
                  "M": (180, 120, 255), "C": (84, 228, 140), "W": (255, 244, 206)}

        def at(i):
            x, y = i % cols, i // cols
            return cx + (x - (cols - 1) * .5) * pitch, cy + (y - (rws - 1) * .5) * pitch

        critter = 0
        for i in range(cols * rws):
            tok = row["rows"][i // cols].split()[i % cols]
            if tok == ".":
                continue
            head, rest = tok[0], tok[1:]
            locked = "!" in rest
            rest = rest.replace("!", "")
            rot = 0
            if "/" in rest:
                rest, r = rest.split("/")
                rot = int(r[0])
            colour = ""
            if "#" in rest:
                rest, colour = rest.split("#")
            first, _, second = rest.partition("+")
            arms = first + second
            col = energy.get(colour, DORMANT)
            x, y = at(i)

            K.paste(sheet, K.round_rect(size, size, 22, (255, 220, 140) if locked else (255, 255, 255), .10 if locked else .055), x, y)
            K.paste(sheet, K.round_rect(size, size, 22, (255, 255, 255), .075, width=3), x, y)
            if head == "*":
                K.paste(sheet, K.glow(int(size * 1.22), 2.0, col, .45), x, y)
            elif head == "@":
                ring = K.round_rect(size * .82, size * .82, int(size * .41), col, .72, width=int(size * .07))
                K.paste(sheet, ring, x, y + size * .02)

            tile = Image.new("RGBA", (int(size), int(size)), (0, 0, 0, 0))
            for d, letter in enumerate("NESW"):
                if letter not in arms:
                    continue
                shut = head == "%" and letter in second
                arm = K.round_rect(thick, size * .5 + 1 + thick * .5, thick // 2, WALL if shut else arm_base)
                arm = arm.rotate(-90 * d, expand=True)
                ox, oy = ((0, -1), (1, 0), (0, 1), (-1, 0))[d]
                K.paste(tile, arm, size / 2 + ox * (size * .5 + thick * .5) / 2, size / 2 + oy * (size * .5 + thick * .5) / 2)
            if head != "=" and head != "@" and head != "*":
                hub = Image.new("RGBA", (int(thick * 1.72), int(thick * 1.72)), (0, 0, 0, 0))
                ImageDraw.Draw(hub).ellipse([0, 0, hub.width - 1, hub.height - 1], fill=(*hub_col, 255))
                K.paste(tile, hub, size / 2, size / 2)
            K.paste(sheet, tile.rotate(-90 * rot, Image.BICUBIC), x, y)

            if head == "*":
                rim = size * .56
                d = ImageDraw.Draw(sheet)
                d.polygon([(x, y - rim / 2), (x + rim / 2, y), (x, y + rim / 2), (x - rim / 2, y)], fill=(23, 38, 54, 217))
                c = size * .46
                lift = tuple(int(v + (255 - v) * .45) for v in col)
                d.polygon([(x, y - c / 2), (x + c / 2, y), (x, y + c / 2), (x - c / 2, y)], fill=(*lift, 255))
            elif head == "@":
                # `BoardView.LampFace`: a gem in the lane's colour, dimmed by the sleep tint
                # until its light reaches it.
                lane = {"R": 0, "G": 1, "B": 2, "Y": 3}.get(colour, -1)
                if lane >= 0:
                    im = K.fit(S.sprite("gem_%s" % LETTERS[lane]), (size * .60, size * .60))
                    K.paste(sheet, K.tint(im, (112, 133, 153), .92), x, y - size * .01)
            if locked:
                mark = K.fit(K.load("padlock")[0], (size * .22, size * .22))
                K.paste(sheet, K.tint(mark, (255, 235, 184)), x + size * .33, y + size * .33)

    elif g == "merge":
        best = 0
        for i in range(cols * rws):
            socket(i)
            ch = row["rows"][i // cols][i % cols]
            if ch != ".":
                rank = int(ch)
                best = max(best, rank)
                gem(i, (rank - 1) % 4, .66 + min(rank, 8) * .03)
                K.text(sheet, str(1 << rank), *centre(i), int(cell * .30), outline=2)

        # `MergeView.Ladder`: every rank to the target as the gem it draws, under the plate,
        # lit up to the best on the board, the goal ringed in gold.
        rungs = max(1, int(row["target"]))
        tall = cell * LADDER_ROWS
        ly = cy + rws * cell / 2 + PLATE_RIM * cell / 2 + LADDER_GAP + tall / 2
        pitch = min(cell * .62, cols * cell / rungs)
        gsize = min(tall * .80, pitch * .86)
        K.paste(sheet, K.round_rect(rungs * pitch + gsize * .6, tall * .92, 20, (0, 0, 0), .26), cx, ly)
        for r in range(1, rungs + 1):
            x = cx + (r - 1 - (rungs - 1) * .5) * pitch
            im = K.fit(S.sprite("gem_%s" % LETTERS[(r - 1) % 4]), (gsize, gsize))
            lit = r <= best
            if not lit:
                im = K.tint(im, RUNG_DIM, .55)
            K.paste(sheet, im, x, ly)
            K.text(sheet, str(1 << r), x, ly, int(gsize * .36),
                   fill=K.CREAM if lit else tuple(K.CREAM[:3]) + (115,), outline=2)
        goal_x = cx + ((rungs - 1) - (rungs - 1) * .5) * pitch
        K.paste(sheet, K.round_rect(gsize * 1.34, gsize * 1.34, int(gsize * .67), K.GOLD, .95, width=8), goal_x, ly)

    elif g == "sokoban":
        for i in range(cols * rws):
            ch = row["rows"][i // cols][i % cols]
            if ch == "#":
                K.paste(sheet, K.round_rect(cell * .98, cell * .98, 14, WALL), *centre(i))
                K.paste(sheet, K.round_rect(cell * .82, cell * .82, 12, WALL_FACE), *centre(i))
                continue
            socket(i)
            if ch in "RGBY":
                ring = K.round_rect(cell * .72, cell * .72, int(cell * .36), TINTS[LETTERS.index(ch.lower())], .9, width=int(cell * .06))
                K.paste(sheet, ring, *centre(i))
            if ch == "@":
                disc = Image.new("RGBA", (int(cell * .74), int(cell * .74)), (0, 0, 0, 0))
                ImageDraw.Draw(disc).ellipse([0, 0, disc.width - 1, disc.height - 1], fill=(*K.GOLD, 255))
                K.paste(sheet, disc, *centre(i))
                mark = K.fit(K.load("ic_profile")[0], (cell * .42, cell * .42))
                K.paste(sheet, K.tint(mark, K.INK), *centre(i))
        for i in range(cols * rws):
            ch = row["gems"][i // cols][i % cols]
            if ch != ".":
                gem(i, LETTERS.index(ch), .74)

    return cell


# ------------------------------------------------------------------ the list page
#: `DailyChallengesScreen.BannerH` / `.DealH` / `.DealW` / `.DealKeyW` / `.DealKeyH`.
LIST_BANNER_H, DEAL_H, DEAL_W, DEAL_KEY_W, DEAL_KEY_H = 138.0, 200.0, 1024.0, 200.0, 108.0
#: `DailyChallengesScreen.ChestSize` / `.ChestX` / `.DealTextX`.
CHEST_SIZE, CHEST_X, DEAL_TEXT_X = 170.0, 104.0, 196.0
#: `DailyChallengesScreen.CardW` / `.CardH` / `.PlateW` / `.PlateH`.
CARD_W, CARD_H, PLATE_W, PLATE_H = 1040.0, 292.0, 1024.0, 264.0
#: `NavBar.Height` as hudkit carries it, and GridView's default top pad.
GRID_PAD_TOP = 12.0

#: `ChallengeGenres.Names`, in enum order.
GENRE_ORDER = ["pairs", "glade", "merge", "sokoban"]

#: `DailyChallengesScreen.MarkSize` / `.MarkX` / `.TextX`.
MARK_SIZE, MARK_X, TEXT_X = 216.0, 132.0, 268.0

# ------------------------------------------------------------------ the deal sheet
#: `ChallengeTierOverlay.PanelW` (the window's width here; `VictoryFrame.PanelWidth` is 900 on the victory panel),
#: then `VictoryFrame.CrestReach` / `.PanelInk` / `.CrownY` / `.BannerY` / `.BannerSize` / `.WordLift` / `.WordBox`.
WIN_W, CREST_REACH = 1000.0, 202.0
PANEL_INK = (150, 184, 176)
CROWN_Y, BANNER_Y, BANNER_W, BANNER_H_WIN = 114.0, -30.0, 566.0, 157.0
WORD_LIFT, WORD_W, WORD_H = 34.0, 356.0, 74.0
#: `ChallengeTierOverlay.FreeY` / `.RowsTop` / `.RowH` / `.RowGap` / `.Tail` / `.FootH`.
FREE_Y, ROWS_TOP, ROW_H, ROW_GAP, TAIL, FOOT_H = 150.0, 200.0, 230.0, 14.0, 30.0, 170.0
#: `ChallengeTierOverlay.RowW` / `.StoneSize` / `.StoneX` / `.TextX` / `.TextW` / `.KeySize` / `.KeyInset`.
ROW_W, STONE_SIZE, STONE_X, ROW_TEXT_X, ROW_TEXT_W = 880.0, 160.0, 104.0, 204.0, 370.0
KEY_W, KEY_H, KEY_INSET = 280.0, 116.0, 16.0


def table():
    return json.load(open(FILE, encoding="utf-8"))


def render_list(txt, canvas=(1080, 1920), held=None, spent=(), day_wins=None):
    """`DailyChallengesScreen` at rest: the band saying what the day allows with the crowned
    chest on it, one card per genre with today's level, the plays left and the badge. `held`
    names a deal to draw as running, `spent` the genres drawn with no play left. Every caption
    is measured against its box and printed, because `UIKit.Shrinkable` truncates silently
    (invariant 19n)."""
    global W
    W, H = canvas
    K.W, K.H = W, H
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    K.plain(sheet)
    t = table()
    genres = [g for g in GENRE_ORDER if any(r["genre"] == g for r in t["challenges"])]
    free = (t.get("allowance") or {}).get("freePlays") or 2
    tiers = t.get("tiers") or []
    deal = next((x for x in tiers if x["id"] == held), None) if held else None
    allowance = deal["plays"] if deal else free
    floors = []

    # The chrome: back key and ribbon. The rule line that stood under them is gone (2026-09-23).
    cy = 22.0 + LIST_BANNER_H / 2.0
    K.paste(sheet, K.skin("sq_blue", CHROME, CHROME), 76.0, cy)
    icon = K.fit(K.load("ic_left")[0], (CHROME * .5, CHROME * .5))
    K.paste(sheet, K.tint(icon, K.CREAM), 76.0, cy - CHROME * .0231)
    ribbon = K.skin("ribbon_orange", 720, LIST_BANNER_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.one_line(plate, txt("ui.challenges.title").upper(), plate.width / 2, plate.height / 2,
               720 * RIBBON_ROOM, 42, TITLE_FLOOR, outline=4)
    K.paste(sheet, plate.rotate(RIBBON_TILT, Image.BICUBIC, expand=True), W / 2, cy)

    # The deal band: the chest on the left (`ChallengeArt.Chest`, off disk), the line, the key.
    deal_y = cy + LIST_BANNER_H / 2 + 12 + DEAL_H / 2
    K.paste(sheet, K.skin("Hud/plate_orange", DEAL_W, DEAL_H), W / 2, deal_y)
    band_left = (W - DEAL_W) / 2
    K.paste(sheet, K.fit(K.load("challenge_chest")[0], (CHEST_SIZE, CHEST_SIZE)), band_left + CHEST_X, deal_y)
    if deal:
        days = txt("ui.challenges.days_left").replace("{0}", str(deal["days"]))
        line = (txt("ui.challenges.held_deal").replace("{0}", txt("challenge.tier.%s.name" % deal["id"]))
                .replace("{1}", str(deal["plays"])).replace("{2}", days))
    else:
        line = txt("ui.challenges.free_deal").replace("{0}", str(free))
    line_w = DEAL_W - DEAL_TEXT_X - 20 - DEAL_KEY_W - 24
    floors.append(("deal line", K.shrunk_left(sheet, line, band_left + DEAL_TEXT_X, deal_y - (DEAL_H - 24) / 2,
                                              line_w, DEAL_H - 24, 32, 18, outline=2), 18))
    key_cx = (W + DEAL_W) / 2 - 24 - DEAL_KEY_W / 2
    K.paste(sheet, K.skin("btn_green", DEAL_KEY_W, DEAL_KEY_H), key_cx, deal_y)     # Skins.Affirm
    K.one_line(sheet, txt("ui.challenges.deals").upper(), key_cx, deal_y - 4, DEAL_KEY_W - 40, 32, 18, outline=3)

    # The cards, one per genre, in the grid's window.
    top = deal_y + DEAL_H / 2 + 10 + GRID_PAD_TOP
    for i, genre in enumerate(genres):
        ccy = top + CARD_H * i + CARD_H / 2
        K.paste(sheet, K.skin("Hud/plate_blue", PLATE_W, PLATE_H), W / 2, ccy)
        px = (W - PLATE_W) / 2
        # `ChallengeArt.GenreMark`: the owner's picture, `Ui/challenge_{spelling}`, drawn off disk.
        mark = K.fit(K.load("challenge_%s" % genre)[0], (MARK_SIZE, MARK_SIZE))
        K.paste(sheet, mark, px + MARK_X, ccy)

        # `_name` is MiddleLeft in a box centred 280 in from TEXT_X, so the text starts at TEXT_X.
        K.text(sheet, txt("challenge.genre.%s.name" % genre), px + TEXT_X, ccy - 74, 48, outline=3, anchor="l")
        floors.append(("blurb %s" % genre,
                       K.shrunk_left(sheet, txt("challenge.genre.%s.blurb" % genre), px + TEXT_X, ccy + 8 - 40, 700, 80, 27, 17,
                                     fill=(255, 245, 224), outline=2), 17))
        row = next(r for r in t["challenges"] if r["genre"] == genre)
        today = txt("ui.challenges.today_level").replace("{0}", txt("challenge.%s.name" % row["id"]))
        floors.append(("level %s" % genre,
                       K.shrunk_left(sheet, today, px + TEXT_X, ccy + 82 - 23, 420, 46, 26, 16, fill=K.GOLD, outline=2), 16))

        is_spent = genre in spent
        left_plays = 0 if is_spent else allowance - (day_wins or {}).get(genre, 0)
        pill_w, pill_h = 290, 66
        pill_cx = px + PLATE_W - 26 - pill_w / 2
        pill_cy = ccy + 82
        K.paste(sheet, K.round_rect(pill_w, pill_h, 24, (158, 168, 189) if is_spent else K.GOLD, .95), pill_cx, pill_cy)
        if is_spent:
            caption = txt("ui.challenges.spent")
        else:
            caption = txt("ui.challenges.plays_left").replace("{0}", str(left_plays)).replace("{1}", str(allowance))
        floors.append(("pill %s" % genre, K.shrunk(sheet, caption, pill_cx, pill_cy, pill_w - 24, pill_h - 8, 26, 16,
                                                   fill=K.INK, outline=0), 16))

        if not is_spent and left_plays > 0:
            # WaitingBadge.Disc: a 66 gold disc, a dark rim, the number, on the plate's top-right.
            bx, by = px + PLATE_W - 30, ccy - PLATE_H / 2 + 28
            disc = Image.new("RGBA", (66, 66), (0, 0, 0, 0))
            ImageDraw.Draw(disc).ellipse([0, 0, 65, 65], fill=(*K.GOLD, 255))
            ImageDraw.Draw(disc).ellipse([0, 0, 65, 65], outline=(41, 31, 10, 242), width=7)
            K.paste(sheet, disc, bx, by)
            K.text(sheet, str(left_plays), bx, by, 36, fill=K.INK, outline=0)

    K.navbar(sheet, "home")

    for what, got, floor in floors:
        flag = "  <- AT ITS FLOOR: the string is too long for its box" if got <= floor else ""
        print("  %-16s settled at %2d (floor %d)%s" % (what, got, floor, flag))
    print("  list: %d genre(s), allowance %d, held %s, spent %s on %dx%d"
          % (len(genres), allowance, held or "-", ",".join(spent) or "-", W, H))
    return sheet


def price_key(sheet, caption, cx, cy, w, h, size):
    """A pill's caption with the gem trailing it (`UIKit.TextButton` with `iconTrails`): the
    glyph is .34 of the pill's height, the pair is one block centred on the face, and the
    caption shrinks to half its size before the block would outgrow the label's room."""
    glyph = h * .34
    room = w - 40 - glyph - 18
    px = size
    while px > size // 2 and K.font(px).getlength(caption) > room:
        px -= 1
    wide = K.font(px).getlength(caption)
    left = cx - (wide + 18 + glyph) / 2
    K.text(sheet, caption, left + wide / 2, cy, px, outline=3)
    gem = K.fit(Image.open(K.UI / "ic_gem.png").convert("RGBA"), (glyph, glyph))
    K.paste(sheet, gem, left + wide + 18 + glyph / 2, cy)
    return px


def render_deals(txt, canvas=(1080, 1920), held=None):
    """`ChallengeTierOverlay` at rest, on `VictoryFrame`: the scrim, the fan and bloom, the
    green window with the crown and banner over it, the free line, one row per shipped deal
    with its stone, its name, its line and its key, and DONE at the foot. `held` names the deal
    drawn as running, which turns its key into the ACTIVE tag and the rows above it into
    upgrades. The fit is mirrored too, so a canvas the block does not fit is drawn scaled as
    the device draws it."""
    global W
    W, H = canvas
    K.W, K.H = W, H
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    K.plain(sheet)
    scrim = Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .72)))
    sheet.alpha_composite(scrim)

    t = table()
    tiers = t.get("tiers") or []
    free = (t.get("allowance") or {}).get("freePlays") or 2
    deal = next((x for x in tiers if x["id"] == held), None) if held else None
    floors = []

    rows_h = len(tiers) * (ROW_H + ROW_GAP) - (ROW_GAP if tiers else 0)
    panel_h = ROWS_TOP + rows_h + TAIL + FOOT_H

    # `VictoryFrame.MakeFit`: the block is scaled to the screen, crest included.
    reach = panel_h + CREST_REACH
    fit = min(1.0, (H - 20 * 2) / reach)

    # Everything is drawn into a block at the fit's scale, then pasted centred. The panel is
    # offset up by half the crest inside the fit, so the block's centre is the fit's origin.
    block = Image.new("RGBA", (1680, int(reach + 500)), (0, 0, 0, 0))
    bw, bh = block.size
    bcx = bw / 2
    panel_top = bh / 2 - CREST_REACH / 2 - panel_h / 2
    crest_cy = panel_top - CREST_REACH / 2           # `crestY`, image-down

    fan = K.rays(1680, 14)
    fan_rgba = Image.new("RGBA", fan.size, (255, 204, 77, 0))
    fan_rgba.putalpha(fan.point(lambda v: int(v * .20)))
    K.paste(block, fan_rgba, bcx, crest_cy + 240)
    K.paste(block, K.glow(1240, 2.4, (255, 209, 97), .22), bcx, crest_cy + 200)

    window = K.tint(K.skin("Win/window", WIN_W, panel_h), PANEL_INK)
    K.paste(block, window, bcx, panel_top + panel_h / 2)
    K.paste(block, K.glow(int(WIN_W - 60), 1.9, (255, 245, 209), .11), bcx, panel_top + 70)

    # The crest: the crown above the top edge, the banner across it, the word on the face.
    crown = K.fit(K.load("Win/crown")[0], (180, 162))
    K.paste(block, crown, bcx, panel_top - CROWN_Y)
    banner = K.fit(K.load("Win/banner")[0], (BANNER_W, BANNER_H_WIN))
    K.paste(block, banner, bcx, panel_top - BANNER_Y)
    floors.append(("word", K.shrunk(block, txt("ui.challenges.deals_title"), bcx, panel_top - BANNER_Y - WORD_LIFT,
                                    WORD_W, WORD_H, 58, 32, outline=5), 32))

    line = txt("ui.challenges.free_deal").replace("{0}", str(free))
    floors.append(("free line", K.shrunk(block, line, bcx, panel_top + FREE_Y, 860, 60, 32, 20,
                                         fill=(255, 245, 224), outline=2), 20))

    y = panel_top + ROWS_TOP + ROW_H / 2
    for rung, tier in enumerate(tiers, 1):
        is_held = deal is not None and tier["id"] == deal["id"]
        under = deal is not None and deal["plays"] >= tier["plays"] and not is_held
        upgrades = deal is not None and tier["plays"] > deal["plays"]

        K.paste(block, K.round_rect(ROW_W, ROW_H, 28, (0, 0, 0), .32), bcx, y)
        edge = (K.GOLD, .62) if is_held else ((255, 245, 219), .14)
        K.paste(block, K.round_rect(ROW_W, ROW_H, 28, edge[0], edge[1], width=3), bcx, y)
        left = bcx - ROW_W / 2

        # The stone off the rung (`ChallengeArt.DealMark`), with its halo.
        K.paste(block, K.glow(int(STONE_SIZE * 1.9), 2.2, K.GOLD if is_held else (255, 116, 212), .34 if is_held else .18),
                left + STONE_X, y)
        stone_path = K.UI / ("challenge_deal_%d.png" % rung)
        if stone_path.exists():
            K.paste(block, K.fit(K.load("challenge_deal_%d" % rung)[0], (STONE_SIZE, STONE_SIZE)), left + STONE_X, y)

        name = txt("challenge.tier.%s.name" % tier["id"])
        floors.append(("name %s" % tier["id"],
                       K.shrunk_left(block, name, left + ROW_TEXT_X, y - 60 - 33, ROW_TEXT_W, 66, 52, 28,
                                     fill=K.GOLD if is_held else K.CREAM, outline=3), 28))
        days = txt("ui.challenges.days_one") if tier["days"] == 1 else txt("ui.challenges.days").replace("{0}", str(tier["days"]))
        line = txt("ui.challenges.deal_line").replace("{0}", str(tier["plays"])).replace("{1}", days)
        floors.append(("line %s" % tier["id"],
                       K.shrunk_left(block, line, left + ROW_TEXT_X, y + 20 - 42, ROW_TEXT_W, 84, 32, 20,
                                     fill=(255, 245, 224), outline=2), 20))

        key_cx = bcx + ROW_W / 2 - KEY_INSET - KEY_W / 2
        if is_held:
            days_left = txt("ui.challenges.days_left").replace("{0}", str(tier["days"]))
            K.paste(block, K.round_rect(KEY_W, KEY_H, 24, K.GOLD, .95), key_cx, y)
            floors.append(("held tag", K.shrunk(block, txt("ui.challenges.deal_held") + " " + days_left, key_cx, y,
                                                KEY_W, KEY_H, 30, 18, fill=K.INK, outline=0), 18))
            y += ROW_H + ROW_GAP
            continue

        price = tier["gems"] - deal["gems"] if upgrades else tier["gems"]
        caption = (txt("ui.challenges.upgrade") if upgrades else txt("ui.challenges.buy")).replace("{0}", str(price))
        K.paste(block, K.skin("btn_gray" if under else "btn_violet", KEY_W, KEY_H), key_cx, y)
        floors.append(("key %s" % tier["id"], price_key(block, caption, key_cx, y - 4, KEY_W, KEY_H, 36), 18))
        if upgrades:
            floors.append(("note %s" % tier["id"],
                           K.shrunk_left(block, txt("ui.challenges.upgrade_note"), left + ROW_TEXT_X, y + 88 - 24,
                                         ROW_TEXT_W, 48, 22, 14, fill=K.GOLD, outline=2), 14))
        y += ROW_H + ROW_GAP

    done_cy = panel_top + panel_h - (30 + 55)
    K.paste(block, K.skin("btn_blue", 400, 110), bcx, done_cy)
    K.one_line(block, txt("ui.challenges.done").upper(), bcx, done_cy - 4, 340, 36, 18, outline=3)

    if fit < 1.0:
        block = block.resize((int(bw * fit), int(bh * fit)), Image.LANCZOS)
    K.paste(sheet, block, W / 2, H / 2)

    for what, got, floor in floors:
        flag = "  <- AT ITS FLOOR: the string is too long for its box" if got <= floor else ""
        print("  %-16s settled at %2d (floor %d)%s" % (what, got, floor, flag))
    print("  deals: %d row(s), held %s, panel %.0f tall (+%.0f crest) fitted at %.2f on %dx%d"
          % (len(tiers), held or "-", panel_h, CREST_REACH, fit, W, H))
    return sheet


# ------------------------------------------------------------------ one screen
def render(row, txt, canvas=(1080, 1920)):
    global W
    W, H = canvas
    K.W, K.H = W, H
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    K.plain(sheet)

    top = chrome(sheet, txt, row)
    unit = W / 8.0
    line_band = unit * LINE_BAND_UNITS
    room = H - top - BOTTOM_PAD

    # `ChallengeScreen.BuildBands`: the board is asked first, the hill takes the rest, bounded.
    want = band_wanted(row["genre"], row["width"], row["height"], W - PUZZLE_INSET * 2)
    hill_h = max(unit * HILL_LEAST_UNITS, min(unit * HILL_MOST_UNITS, room - line_band - BAND_GAP - want))

    # `ChallengeScreen.GroundUnder`: the opaque ground from the band's top to the canvas foot.
    band_top = top + hill_h + line_band + BAND_GAP
    ImageDraw.Draw(sheet).rectangle([0, band_top, W, H], fill=(*GROUND, 255))

    hill(sheet, row, top, hill_h + line_band, unit, line_band, txt)
    cell = puzzle(sheet, row, band_top, H - BOTTOM_PAD, txt)

    band = H - BOTTOM_PAD - (top + hill_h + line_band + BAND_GAP)
    print("  %-12s %-8s %dx%d cell %3d  hill %.0f (%.1f cells)  line %.0f  puzzle band %.0f of which the board wants %.0f  on %dx%d"
          % (row["id"], row["genre"], row["width"], row["height"], cell, hill_h, hill_h / unit, line_band, band, want, W, H))
    return sheet


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--id")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--phone", action="store_true", help="a 19.5:9 canvas (1080x2340)")
    ap.add_argument("--out")
    ap.add_argument("--list", action="store_true", help="the list page rather than a board")
    ap.add_argument("--deals", action="store_true", help="the deal sheet (ChallengeTierOverlay) rather than a board")
    ap.add_argument("--held", help="with --list or --deals: a deal id drawn as running")
    ap.add_argument("--spent", default="", help="with --list: genres drawn with no play left, comma-separated")
    args = ap.parse_args()

    txt = lambda key: loc().get(key, key)
    canvas = (1080, 2340) if args.phone else (1080, 1920)

    if args.deals:
        sheet = render_deals(txt, canvas, args.held)
        path = Path(args.out) if args.out else REPO / "out" / "challenges_deals.png"
        path.parent.mkdir(parents=True, exist_ok=True)
        sheet.save(path)
        print("wrote %s  %dx%d  - look at it" % (path, sheet.width, sheet.height))
        return
    if args.list:
        sheet = render_list(txt, canvas, args.held, [g for g in args.spent.split(",") if g])
        path = Path(args.out) if args.out else REPO / "out" / "challenges_list.png"
        path.parent.mkdir(parents=True, exist_ok=True)
        sheet.save(path)
        print("wrote %s  %dx%d  - look at it" % (path, sheet.width, sheet.height))
        return
    picked = [r for r in rows() if not args.id or r["id"] == args.id]
    if not picked:
        sys.exit("no challenge named %r" % args.id)

    sheets = [render(r, txt, canvas) for r in picked]

    if args.contact or len(sheets) > 1:
        scale = .5
        tiles = [s.resize((int(s.width * scale), int(s.height * scale)), Image.LANCZOS) for s in sheets]
        out = Image.new("RGBA", (sum(t.width for t in tiles) + 12 * (len(tiles) - 1), tiles[0].height), (0, 0, 0, 255))
        x = 0
        for t in tiles:
            out.alpha_composite(t, (x, 0))
            x += t.width + 12
    else:
        out = sheets[0]

    path = Path(args.out) if args.out else REPO / "out" / ("challenges%s.png" % ("_" + args.id if args.id else ""))
    path.parent.mkdir(parents=True, exist_ok=True)
    out.save(path)
    print("wrote %s  %dx%d  - look at it" % (path, out.width, out.height))


if __name__ == "__main__":
    main()
