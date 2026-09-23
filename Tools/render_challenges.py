# -*- coding: utf-8 -*-
"""Draws every shipped daily challenge at the size a phone draws it, with the real sprites.

    python Tools/render_challenges.py                     # every row of challenges.json
    python Tools/render_challenges.py --id d04_pipes
    python Tools/render_challenges.py --contact           # all four side by side
    python Tools/render_challenges.py --phone             # a 19.5:9 canvas instead of 16:9
    python Tools/render_challenges.py --out out/challenges.png
    python Tools/render_challenges.py --list               # the list page: deal band, cards, badges
    python Tools/render_challenges.py --list --held gold --spent pairs,merge

**Why this exists.** `ChallengeTests` plays every row with a bot and proves it is winnable; it
says nothing about whether the screen *reads*. Seven boards share one band arithmetic
(`ChallengeScreen.BuildBands`, `PuzzleView.Attach`), and the questions no fixture can answer
are the ones this picture is for: does a 6x10 well and a 4x4 grid both get a cell a finger can
use, does the hill leave room for the raiders to be seen walking, do the four posts line up
under the four lanes, and does a strip of keys under a board push the board into the line.

**It is a mirror, and mirrors drift.** Every constant is named after the field it copies
(`ChallengeScreen.HillShare`, `PuzzleView.Margin`, `ChallengeHillView.RaiderTall`), the board
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

#: `ChallengeScreen.HillShare` / `.HillLeast` / `.HillMost`, `.LineBandUnits`, `.BottomPad`, `.BandGap`.
HILL_SHARE, HILL_LEAST, HILL_MOST = 0.27, 330.0, 520.0
LINE_BAND_UNITS, BOTTOM_PAD, BAND_GAP = 2.3, 36.0, 14.0

#: `PuzzleView.Margin`, and each view's `MaxCell` / `StripHeight`.
MARGIN = 18.0
MAX_CELL = {"pairs": 200, "pipes": 190, "merge": 200, "sokoban": 150}
STRIP = {}

#: `ChallengeHillView.RaiderTall` and `.HillTopInset`.
RAIDER_TALL, HILL_TOP_INSET = 1.0, 0.95

#: `Pal.Slot`, `Pal.Dormant`, the pairs back, the mine slab, the sokoban wall.
SLOT = (255, 255, 255)
DORMANT = (58, 80, 100)
BACK = (31, 56, 87)
SLAB = (77, 107, 143)
WALL, WALL_FACE = (51, 43, 41), (92, 77, 69)


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
    if g == "pipes":
        asked = sum(1 for ch in row["sources"] if ch != ".")
        return txt("ui.challenges.pipes").replace("{0}", "0").replace("{1}", str(asked))
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

    for i in range(posts):
        cx, cy = post_x(i), top + line_y
        K.paste(sheet, K.fit(S.sprite("socket"), (unit * 1.7, unit * .8)), cx, cy + unit * .88)
        K.paste(sheet, K.glow(int(unit * 3.1), 2.1, TINTS[i], .22), cx, cy)
        body = K.fit(S.sprite("Wards/bolt_%s" % LETTERS[i]), (unit * 1.72, unit * 2.15))
        K.paste(sheet, body, cx, cy - unit * .06)

        bar_w, bar_h = unit * 1.06, unit * .17
        K.paste(sheet, K.round_rect(bar_w, bar_h, 10, (0, 0, 0), .66), cx, cy - unit * 1.26)
        K.paste(sheet, K.round_rect(bar_w - 4, bar_h - 4, 10, K.CREAM), cx, cy - unit * 1.26)

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
def puzzle(sheet, row, top, bottom, txt):
    """`PuzzleView.Attach` and the genre's `Build`, at rest."""
    g = row["genre"]
    cols, rws = row["width"], row["height"]
    host_w, host_h = W - 48.0, bottom - top
    strip = STRIP.get(g, 0)

    cell = int(min((host_w - MARGIN * 2) / cols, (host_h - strip - MARGIN * 2) / rws, MAX_CELL[g]))
    span_w, span_h = cols * cell, rws * cell
    cx = W / 2
    cy = top + (host_h - strip) / 2                  # the field is centred above the strip

    plate = K.nine(*S_load("plate"), span_w + cell * .34 + MARGIN, span_h + cell * .34 + MARGIN)
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

    elif g == "pipes":
        thick = cell * .30
        for i in range(cols * rws):
            socket(i)
            tok = row["rows"][i // cols].split()[i % cols]
            if tok == ".":
                continue
            arms, rot = tok.split("/") if "/" in tok else (tok, "0")
            tile = Image.new("RGBA", (int(cell), int(cell)), (0, 0, 0, 0))
            for d, letter in enumerate("NESW"):
                if letter not in arms:
                    continue
                arm = K.round_rect(thick, cell * .5 + thick * .5, 10, DORMANT)
                arm = arm.rotate(-90 * d, expand=True)
                # Anchored at the hub, reaching the edge the arm names.
                ox, oy = ((0, -1), (1, 0), (0, 1), (-1, 0))[d]
                K.paste(tile, arm, cell / 2 + ox * (cell * .5 + thick * .5) / 2, cell / 2 + oy * (cell * .5 + thick * .5) / 2)
            hub = Image.new("RGBA", (int(thick * 1.25), int(thick * 1.25)), (0, 0, 0, 0))
            ImageDraw.Draw(hub).ellipse([0, 0, hub.width - 1, hub.height - 1], fill=(*DORMANT, 255))
            K.paste(tile, hub, cell / 2, cell / 2)
            K.paste(sheet, tile.rotate(-90 * int(rot), Image.BICUBIC), *centre(i))
        for c, ch in enumerate(row["sources"]):
            if ch != ".":
                x, y = centre(c)
                K.paste(sheet, K.fit(S.sprite("gem_%s" % ch), (cell * .5, cell * .5)), x, y - cell * .72)
        for c, ch in enumerate(row["sinks"]):
            if ch != ".":
                x, y = centre((rws - 1) * cols + c)
                ring = K.round_rect(cell * .5, cell * .5, int(cell * .25), TINTS[LETTERS.index(ch)], .45, width=int(cell * .05))
                K.paste(sheet, ring, x, y + cell * .72)

    elif g == "merge":
        for i in range(cols * rws):
            socket(i)
            ch = row["rows"][i // cols][i % cols]
            if ch != ".":
                rank = int(ch)
                gem(i, (rank - 1) % 4, .66 + min(rank, 8) * .03)
                K.text(sheet, str(1 << rank), *centre(i), int(cell * .30), outline=2)

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
#: `DailyChallengesScreen.BannerH` / `.RuleH` / `.DealH` / `.DealW` / `.DealKeyW` / `.DealKeyH`.
LIST_BANNER_H, RULE_H, DEAL_H, DEAL_W, DEAL_KEY_W, DEAL_KEY_H = 138.0, 76.0, 112.0, 960.0, 220.0, 84.0
#: `DailyChallengesScreen.CardW` / `.CardH` / `.PlateW` / `.PlateH`.
CARD_W, CARD_H, PLATE_W, PLATE_H = 960.0, 250.0, 940.0, 222.0
#: `NavBar.Height` as hudkit carries it, and GridView's default top pad.
GRID_PAD_TOP = 12.0

#: `ChallengeGenres.Names`, in enum order.
GENRE_ORDER = ["pairs", "pipes", "merge", "sokoban"]

#: `DailyChallengesScreen.MarkSize` / `.MarkX` / `.TextX`.
MARK_SIZE, MARK_X, TEXT_X = 176.0, 118.0, 226.0


def table():
    return json.load(open(FILE, encoding="utf-8"))


def render_list(txt, canvas=(1080, 1920), held=None, spent=(), day_wins=None):
    """`DailyChallengesScreen` at rest: the band saying what the day allows, one card per genre
    with today's level, the plays left and the badge. `held` names a deal to draw as running,
    `spent` the genres drawn with no play left. Every caption is measured against its box and
    printed, because `UIKit.Shrinkable` truncates silently (invariant 19n)."""
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

    # The chrome: back key, ribbon, rule line.
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

    rule_y = cy + LIST_BANNER_H / 2 + 10 + RULE_H / 2
    floors.append(("rule", K.shrunk(sheet, txt("ui.challenges.rule"), W / 2, rule_y, 900, RULE_H, 24, 16,
                                    fill=(255, 245, 224), outline=2), 16))

    # The deal band.
    deal_y = rule_y + RULE_H / 2 + 10 + DEAL_H / 2
    K.paste(sheet, K.skin("Hud/plate_navy", DEAL_W, DEAL_H), W / 2, deal_y)
    if deal:
        days = txt("ui.challenges.days_left").replace("{0}", str(deal["days"]))
        line = (txt("ui.challenges.held_deal").replace("{0}", txt("challenge.tier.%s.name" % deal["id"]))
                .replace("{1}", str(deal["plays"])).replace("{2}", days))
    else:
        line = txt("ui.challenges.free_deal").replace("{0}", str(free))
    line_w = DEAL_W - DEAL_KEY_W - 90
    left = (W - DEAL_W) / 2 + 36
    floors.append(("deal line", K.shrunk_left(sheet, line, left, deal_y - (DEAL_H - 20) / 2, line_w, DEAL_H - 20, 26, 16,
                                              outline=2), 16))
    key_cx = (W + DEAL_W) / 2 - 24 - DEAL_KEY_W / 2
    K.paste(sheet, K.skin("btn_orange", DEAL_KEY_W, DEAL_KEY_H), key_cx, deal_y)
    K.one_line(sheet, txt("ui.challenges.deals").upper(), key_cx, deal_y - 4, DEAL_KEY_W - 40, 26, 16, outline=3)

    # The cards, one per genre, in the grid's window.
    top = deal_y + DEAL_H / 2 + 10 + GRID_PAD_TOP
    for i, genre in enumerate(genres):
        ccy = top + CARD_H * i + CARD_H / 2
        K.paste(sheet, K.skin("Hud/plate_blue", PLATE_W, PLATE_H), W / 2, ccy)
        px = (W - PLATE_W) / 2
        # `ChallengeArt.GenreMark`: the owner's picture, `Ui/challenge_{spelling}`, drawn off disk.
        mark = K.fit(K.load("challenge_%s" % genre)[0], (MARK_SIZE, MARK_SIZE))
        K.paste(sheet, mark, px + MARK_X, ccy)

        # `_name` is MiddleLeft in a box centred 240 in from TEXT_X, so the text starts at TEXT_X.
        K.text(sheet, txt("challenge.genre.%s.name" % genre), px + TEXT_X, ccy - 58, 40, outline=3, anchor="l")
        floors.append(("blurb %s" % genre,
                       K.shrunk_left(sheet, txt("challenge.genre.%s.blurb" % genre), px + TEXT_X, ccy + 4 - 33, 620, 66, 22, 15,
                                     fill=(255, 245, 224), outline=2), 15))
        row = next(r for r in t["challenges"] if r["genre"] == genre)
        today = txt("ui.challenges.today_level").replace("{0}", txt("challenge.%s.name" % row["id"]))
        floors.append(("level %s" % genre,
                       K.shrunk_left(sheet, today, px + TEXT_X, ccy + 66 - 20, 400, 40, 22, 15, fill=K.GOLD, outline=2), 15))

        is_spent = genre in spent
        left_plays = 0 if is_spent else allowance - (day_wins or {}).get(genre, 0)
        pill_w, pill_h = 250, 54
        pill_cx = px + PLATE_W - 26 - pill_w / 2
        pill_cy = ccy + 66
        K.paste(sheet, K.round_rect(pill_w, pill_h, 24, (158, 168, 189) if is_spent else K.GOLD, .95), pill_cx, pill_cy)
        caption = txt("ui.challenges.spent") if is_spent else \
            txt("ui.challenges.plays_left").replace("{0}", str(left_plays)).replace("{1}", str(allowance))
        floors.append(("pill %s" % genre, K.shrunk(sheet, caption, pill_cx, pill_cy, pill_w - 24, pill_h - 8, 22, 14,
                                                   fill=K.INK, outline=0), 14))

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
    hill_h = max(HILL_LEAST, min(HILL_MOST, room * HILL_SHARE))

    hill(sheet, row, top, hill_h + line_band, unit, line_band, txt)
    cell = puzzle(sheet, row, top + hill_h + line_band + BAND_GAP, H - BOTTOM_PAD, txt)

    print("  %-12s %-8s cell %3d  hill %.0f  line %.0f  puzzle band %.0f  on %dx%d"
          % (row["id"], row["genre"], cell, hill_h, line_band, H - BOTTOM_PAD - (top + hill_h + line_band + BAND_GAP), W, H))
    return sheet


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--id")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--phone", action="store_true", help="a 19.5:9 canvas (1080x2340)")
    ap.add_argument("--out")
    ap.add_argument("--list", action="store_true", help="the list page rather than a board")
    ap.add_argument("--held", help="with --list: a deal id drawn as running")
    ap.add_argument("--spent", default="", help="with --list: genres drawn with no play left, comma-separated")
    args = ap.parse_args()

    txt = lambda key: loc().get(key, key)
    canvas = (1080, 2340) if args.phone else (1080, 1920)

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
