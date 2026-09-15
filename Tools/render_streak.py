# -*- coding: utf-8 -*-
"""Draws the streak page at the size a phone draws it.

    python Tools/render_streak.py                 # a live streak, one night waiting
    python Tools/render_streak.py --state risk    # the flame about to go out, with the CTA
    python Tools/render_streak.py --state none    # a player who has never held one
    python Tools/render_streak.py --state shield  # a protected streak, the offer row bought
    python Tools/render_streak.py --state week2   # the second lap, so the board is nights 8-14
    python Tools/render_streak.py --contact       # all five side by side

**Why this exists.** Every question this page raises is a picture. Is the count the hero, or
is the board? Is a chest night told apart from a credit night at a tile's width? Does the
shield row read as an offer rather than as a status bar — and once it is bought, does it read
as a *countdown* rather than as a thing you can buy again? Does the waiting night's light
survive the kit's navy card? No numeric gate in this project can open a PNG, and the Editor
cannot photograph a `ScreenSpaceOverlay` canvas — so the page is judged here, the way every
board is (`CRAFT.md`).

**It reads the shipped content.** The ladder, the chest tiers, the shield's price and window
and every string come out of `progression.json` and `loc/en.json`, so a retune redraws rather
than going stale — a mirror with its own numbers answers questions about a screen the game
does not draw (invariant 44d).

**It does not draw the tweens**, so nothing here says whether the waiting tile's light
*breathes* well; what it says is whether the light is visible at all, and whether the tile it
is on can be read while it is running.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

W, H = K.W, K.H

CONTENT = K.REPO / "Assets" / "StreamingAssets" / "Content"
LOC = CONTENT / "loc" / "en.json"
TABLE = CONTENT / "progression.json"

# StreakScreen
CHROME = 92.0
BANNER_H = 138.0
HERO_H = 240.0
SHIELD_H = 168.0
HEADING_H = 62.0
WIDTH = 1000.0

TILE_W, TILE_H, TILE_GAP = 240.0, 320.0, 18.0
BOARD_LEAD = .28

#: `StreakScreen.SeatSize`, `SeatY` and `RewardTall`. The last is a *drawn* height, which is the
#: only unit a chest and a gem can share - the closed chest carries the lid's headroom, so a box
#: set straight from a height draws it two thirds the size of the gem beside it. `ChestPack`
#: owns the conversion and this mirror has to make the same one or it answers the wrong question
#: about every tile that pays a chest (invariant 44d).
SEAT_SIZE, SEAT_Y, REWARD_TALL = 176.0, 14.0, 126.0
CHEST_FILL, CHEST_LIFT = 155.0 / 244.0, 38.5 / 155.0
PER_ROW = 4

BAR_ORANGE = (255, 150, 30)
BAR_FULL = (96, 235, 70)
BAR_H = 26

#: `StreakScreen.FooterTop` — the CTA's band, taken out of the board only when it is drawn.
FOOTER_H = 176.0


def strings():
    table = json.loads(LOC.read_text(encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


LOCS = strings()
TABLE_JSON = json.loads(TABLE.read_text(encoding="utf-8"))
STREAK = TABLE_JSON.get("streak") or {}
TIERS = [t["id"] for t in (TABLE_JSON.get("tasks") or {}).get("tiers") or []]
RUNGS = STREAK.get("rungs") or []
SHIELD_DAYS = STREAK.get("shieldDays") or 7
SHIELD_GEMS = STREAK.get("shieldGems") or 120


def txt(key, *args):
    s = LOCS.get(key, key)
    for i, a in enumerate(args):
        s = s.replace("{%d}" % i, str(a))
    return s


def chest_art():
    """tier id -> (sprite, its own alpha box). The closed icon carries the lid's headroom."""
    out = {}
    for tier in TIERS:
        im = Image.open(K.UI / "Chest" / ("%s.png" % tier)).convert("RGBA")
        out[tier] = (im, im.getchannel("A").getbbox())
    return out


ART = chest_art()


def drawn(tier, h, lit=True):
    im, box = ART[tier]
    w = h * (box[2] - box[0]) / float(box[3] - box[1])
    out = im.crop(box).resize((max(1, int(round(w))), max(1, int(round(h)))), Image.LANCZOS)
    return out if lit else K.tint(out, (205, 212, 226))


# ------------------------------------------------------------------ the chrome
def header(sheet, y):
    """`StreakScreen.BuildHeader` — the name first, then what it says, then the wallet."""
    cy = y + BANNER_H / 2
    K.paste(sheet, K.skin("sq_blue", CHROME, CHROME), 76, cy)
    K.paste(sheet, K.skin("sq_orange", CHROME, CHROME), W - 76, cy)
    for x, glyph in ((76, "ic_left"), (W - 76, "ic_info")):
        K.paste(sheet, K.tint(K.fit(Image.open(K.UI / ("%s.png" % glyph)).convert("RGBA"), (46, 46)),
                              K.CREAM), x, cy)

    ribbon = K.skin("Hud/title", 720, BANNER_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.text(plate, txt("ui.streak.title").upper(), plate.width / 2, plate.height / 2 - 6, 42, fill=K.SUN)
    K.paste(sheet, plate, W / 2, cy)
    y += BANNER_H + 4

    K.shrunk(sheet, txt("ui.streak.subtitle"), W / 2, y + 16, 880, 32, 24, 15,
             fill=(219, 229, 255), outline=0)
    y += 32 + 14

    money = [(-232, K.ROSE, "ic_heart", "5/5"), (0, K.GOLD, "Coin/f0", "12,480"),
             (232, K.BLOOM, "ic_gem", "1,240")]
    for dx, colour, glyph, value in money:
        cx = W / 2 + dx
        K.paste(sheet, K.skin("Hud/trough", 212, 78), cx, y + CHROME / 2)
        gx = cx - 106 + 52
        K.paste(sheet, K.glow(96, 2.0, colour, .30), gx, y + CHROME / 2)
        K.paste(sheet, K.fit(Image.open(K.UI / ("%s.png" % glyph)).convert("RGBA"), (52, 52)),
                gx, y + CHROME / 2)
        K.text(sheet, value, cx + 24, y + CHROME / 2, 30)

    return y + CHROME + 16


def hero(sheet, y, days, lap_done, lap_len, state_line, state_colour):
    """`StreakScreen.BuildHero` — the flame, the count, the lap and the one live line."""
    cy = y + HERO_H / 2
    left = W / 2 - WIDTH / 2
    K.paste(sheet, K.skin("Hud/panel", WIDTH, HERO_H), W / 2, cy)

    band = Image.new("RGBA", (int(WIDTH) - 12, int(HERO_H) - 12), (0, 0, 0, 0))
    fan = K.rays(256, 14).resize((760, 760), Image.LANCZOS)
    lit = Image.new("RGBA", fan.size, (*K.SUN, 0))
    lit.putalpha(fan.point(lambda v: int(v * .16)))
    band.alpha_composite(lit, (150 - 380 - 6, band.height // 2 - 380 - 10))
    K.paste(sheet, band, W / 2, cy)

    fx, fy = left + 128, cy - 6
    K.paste(sheet, K.glow(260, 2.0, (255, 158, 56), .34 if days else .10), fx, fy)
    flame = Image.open(K.UI / "Flame" / "f00.png").convert("RGBA")
    K.paste(sheet, K.fit(flame, (168, 168)) if days
            else K.tint(K.fit(flame, (168, 168)), (255, 255, 255), .45), fx, fy)

    # Both are `MiddleLeft` in a 300-wide box starting at 236, so they are drawn from that
    # edge rather than centred on it — a mirror that centred them would put the count under
    # the bar's middle and answer the wrong question about the plate.
    K.text(sheet, str(days) if days else "—", left + 236, cy - 34, 58, anchor="l")
    K.text(sheet, txt("ui.streak.days" if days != 1 else "ui.streak.day"),
           left + 236, cy + 12, 24, fill=(255, 243, 220), outline=2, anchor="l")

    # the lap fraction, in its own well at the right end
    lap_w = 232.0
    lx = W / 2 + WIDTH / 2 - lap_w / 2 - 26
    K.paste(sheet, K.skin("Hud/trough", lap_w, 104), lx, cy + 22)
    K.shrunk(sheet, txt("ui.tasks.fraction", lap_done, lap_len), lx, cy + 8, lap_w - 24, 52, 40, 22,
             fill=K.GOLD)
    K.shrunk(sheet, txt("ui.streak.nights"), lx, cy + 48, lap_w - 24, 28, 21, 13,
             fill=(255, 243, 220), outline=0)

    # the bar
    track = 460.0
    tx = left + 236 + track / 2
    K.paste(sheet, K.skin("Hud/trough", track, 30), tx, cy + 52)
    run = (track - 8) * min(1.0, lap_done / float(lap_len))
    if run > 1:
        colour = BAR_FULL if lap_done >= lap_len else BAR_ORANGE
        K.paste(sheet, K.tint(K.skin("Hud/fill", run, BAR_H), colour),
                tx - track / 2 + 4 + run / 2, cy + 52)

    # the live line, in the plate's own corner
    clock_w, clock_h = 340.0, 54.0
    px = W / 2 + WIDTH / 2 - 28 - clock_w / 2
    py = cy - HERO_H / 2 + 20 + clock_h / 2
    pill(sheet, px, py, clock_w, clock_h, state_line, state_colour, "ic_streak")

    return y + HERO_H + 14


def pill(sheet, cx, cy, w, h, label, colour, glyph):
    """`Scenery.Pill` — a rounded plate, a glyph in its own lane, and one line of words."""
    plate = Image.new("RGBA", (int(w), int(h)), (0, 0, 0, 0))
    d = ImageDraw.Draw(plate)
    d.rounded_rectangle([0, 0, w - 1, h - 1], radius=28, fill=(13, 23, 46, 199))
    d.rounded_rectangle([1, 1, w - 2, h - 2], radius=28, outline=(255, 255, 255, 33), width=3)
    K.paste(sheet, plate, cx, cy)

    try:
        icon = Image.open(K.UI / ("%s.png" % glyph)).convert("RGBA")
        K.paste(sheet, K.fit(icon, (int(h * .62), int(h * .62))), cx - w / 2 + h * .5, cy)
    except FileNotFoundError:
        pass

    # `UIKit.OneLineLabel` — the largest size between the floor and 23 at which the string
    # fits on ONE line in the room the glyph leaves it. Reported when it lands on the floor,
    # because "23 against a floor of 14" and "14 against a floor of 14" are the difference
    # between a caption that fits and one Unity will not clip (invariants 19n, 37n).
    room = w - h * .82 - 16
    size = 23
    while size > 14 and K.font(size).getlength(label) > room:
        size -= 1

    if K.font(size).getlength(label) > room:
        print("  TIGHT  the hero pill's line needs %.0f units and has %.0f: '%s'"
              % (K.font(size).getlength(label), room, label))

    K.text(sheet, label, cx + h * .30, cy, size, fill=colour, outline=2)


def shield_row(sheet, y, held, days_left):
    """`StreakScreen.BuildShield` — an offer while nothing is running, a countdown once one is."""
    cy = y + SHIELD_H / 2
    left = W / 2 - WIDTH / 2
    K.paste(sheet, K.skin("Hud/plate_blue", WIDTH, SHIELD_H), W / 2, cy)

    K.paste(sheet, K.glow(262, 2.0, K.MINT, .24), left + 112, cy)
    crest = Image.open(K.UI / "shield.png").convert("RGBA")
    K.paste(sheet, K.fit(crest, (126, 126)) if held
            else K.tint(K.fit(crest, (126, 126)), (209, 219, 235)), left + 112, cy)

    hint_w = 490.0
    hx = left + 196 + hint_w / 2
    K.text(sheet, txt("ui.streak.shield"), hx - hint_w / 2, cy - 24, 36,
           fill=K.MINT if held else K.CREAM, anchor="l")
    K.text(sheet, txt("ui.streak.shield_on_hint", SHIELD_DAYS) if held
           else txt("ui.streak.shield_hint", SHIELD_DAYS),
           hx - hint_w / 2, cy + 24, 23, fill=(255, 243, 220), outline=0, anchor="l")

    bx = W / 2 + WIDTH / 2 - 150
    K.paste(sheet, K.skin("btn_violet" if not held else "btn_gray", 272, 104), bx, cy)

    caption = txt("ui.streak.shield_days_left", days_left) if held \
        else txt("ui.streak.shield_price", SHIELD_GEMS)
    glyph = "ic_check" if held else "ic_gem"
    icon = K.fit(Image.open(K.UI / ("%s.png" % glyph)).convert("RGBA"), (42, 42))

    lift = 104 * 0.0231
    gap = 10
    text_w = K.font(36).getlength(caption)
    block = text_w + gap + icon.width
    K.text(sheet, caption, bx - block / 2, cy - lift, 36, anchor="l")
    K.paste(sheet, icon, bx - block / 2 + text_w + gap + icon.width / 2, cy - lift)

    return y + SHIELD_H + 14


def heading(sheet, y, cycle):
    cy = y + HEADING_H / 2
    left = W / 2 - WIDTH / 2
    K.text(sheet, (txt("ui.streak.week_n", cycle) if cycle > 1
                   else txt("ui.streak.week_one")).upper(),
           left + 8, cy, 28, fill=K.GOLD, anchor="l")
    K.text(sheet, txt("ui.streak.board_hint"), W / 2 + WIDTH / 2 - 8, cy, 22,
           fill=(255, 243, 220), outline=0, anchor="r")
    return y + HEADING_H


# ------------------------------------------------------------------- the board
def tile(sheet, cx, cy, night, rung, state):
    """One night. `state` is 'kept', 'waiting', 'lit', 'tonight' or 'ahead'."""
    lit = state == "lit"
    kept = state == "kept"

    # A night still ahead is drawn on the screen through a CanvasGroup at .70, so the mirror
    # draws the whole tile onto its own sheet and fades it. Faded per-element instead, the
    # picture would be wrong in exactly the way that matters: the card would stay solid and
    # only its contents would recede.
    if state == "ahead":
        cell = Image.new("RGBA", (int(TILE_W + 40), int(TILE_H + 40)), (0, 0, 0, 0))
        tile(cell, cell.width / 2, cell.height / 2, night, rung, "ahead_flat")
        cell.putalpha(cell.getchannel("A").point(lambda v: int(v * .70)))
        sheet.alpha_composite(cell, (int(cx - cell.width / 2), int(cy - cell.height / 2)))
        return

    if lit:
        pool = K.glow(420, 1.35, K.SUN, .80).resize(
            (int(TILE_W + 130), int(TILE_H + 120)), Image.LANCZOS)
        K.paste(sheet, pool, cx, cy)
        K.paste(sheet, K.glow(int(TILE_W * 1.5), 2.0, K.GOLD, .55), cx, cy + 6)

    card = K.skin("Hud/panel", TILE_W, TILE_H)
    if kept:
        card = K.tint(card, (224, 234, 250))
    K.paste(sheet, card, cx, cy)

    if lit:
        d = ImageDraw.Draw(sheet)
        d.rounded_rectangle([cx - TILE_W / 2 + 3, cy - TILE_H / 2 + 3,
                             cx + TILE_W / 2 - 3, cy + TILE_H / 2 - 3],
                            radius=30, outline=(255, 244, 206, 255), width=7)

    # `StreakScreen.ChipSkin` — the chip is the state, and it is the one coloured thing here.
    chip = {"kept": "sq_green", "lit": "sq_orange", "waiting": "sq_orange",
            "tonight": "sq_aqua"}.get(state, "sq_dark")
    K.paste(sheet, K.skin(chip, TILE_W - 44, 54), cx, cy - TILE_H / 2 + 22)
    K.shrunk(sheet, txt("ui.streak.day_n", night).upper(),
             cx, cy - TILE_H / 2 + 22 - 54 * 0.0231, TILE_W - 66, 36, 25, 15)

    # the seat, and the lamp in it
    K.paste(sheet, K.skin("Hud/slot", SEAT_SIZE, SEAT_SIZE), cx, cy - SEAT_Y)
    K.paste(sheet, K.glow(int(SEAT_SIZE + 24), 1.9, K.SUN, .16), cx, cy - SEAT_Y)

    # the strip the amount stands on
    K.paste(sheet, K.skin("Hud/trough", TILE_W - 44, 58), cx, cy + TILE_H / 2 - 26)

    if rung.get("tier"):
        # The *drawn* chest at REWARD_TALL, and its middle where the game puts it: the sprite's
        # own box is higher by `CHEST_LIFT` of that height, so the mirror shifts by the same.
        K.paste(sheet, drawn(rung["tier"], REWARD_TALL, not kept), cx, cy - SEAT_Y)
        K.shrunk(sheet, txt("chest.%s.name" % rung["tier"]), cx, cy + TILE_H / 2 - 26,
                 TILE_W - 62, 38, 25, 14,
                 fill=K.CREAM if not kept else (222, 214, 196))
    else:
        kind = rung.get("kind") or ""
        glyph = {"credits": "Coin/f0", "gems": "ic_gem"}.get(kind, "ic_star")
        icon = Image.open(K.UI / ("%s.png" % glyph)).convert("RGBA")
        box = (int(REWARD_TALL), int(REWARD_TALL))
        K.paste(sheet, K.fit(icon, box) if not kept
                else K.tint(K.fit(icon, box), (209, 219, 235)), cx, cy - SEAT_Y)
        amount = "{:,}".format(rung.get("amount", 0)) if kind else txt("ui.streak.rung_none")
        K.shrunk(sheet, amount, cx, cy + TILE_H / 2 - 26, TILE_W - 62, 46, 36, 20,
                 fill=K.CREAM if not kept else (222, 214, 196))

    if kept:
        sx, sy = cx + TILE_W * .29, cy + TILE_W * .22 - SEAT_Y
        seal = Image.open(K.UI / "seal_gold.png").convert("RGBA")
        K.paste(sheet, K.fit(seal, (66, 66)), sx, sy)
        tick = Image.open(K.UI / "ic_check.png").convert("RGBA")
        K.paste(sheet, K.tint(K.fit(tick, (34, 34)), K.CREAM), sx, sy)


def board(sheet, top, first, states):
    rows = (len(RUNGS) + PER_ROW - 1) // PER_ROW
    tall = rows * (TILE_H + TILE_GAP) - TILE_GAP
    bottom = K.NAV_HEIGHT + 20 + (FOOTER_H if states.get("cta") else 0)
    slack = max(0.0, (H - top - bottom) - tall)

    for i, rung in enumerate(RUNGS):
        row, col = divmod(i, PER_ROW)
        in_row = min(PER_ROW, len(RUNGS) - row * PER_ROW)
        x = W / 2 + (col - (in_row - 1) * .5) * (TILE_W + TILE_GAP)
        y = top + slack * BOARD_LEAD + row * (TILE_H + TILE_GAP) + TILE_H / 2
        tile(sheet, x, y, first + i, rung, states["tiles"][i])


def footer(sheet):
    cy = H - K.NAV_HEIGHT - 32 - 132 / 2
    K.paste(sheet, K.glow(640, 1.7, K.MINT, .24), W / 2, cy)
    K.paste(sheet, K.skin("btn_green", 560, 132), W / 2, cy)
    K.text(sheet, txt("ui.streak.cta"), W / 2, cy - 132 * 0.0231, 44)


# ------------------------------------------------------------------- the states
def shot(state):
    """One page. The states are the five a player can actually be in."""
    n = len(RUNGS)

    if state == "none":
        days, first, lit, kept, cta, held, line, colour = 0, 1, -1, 0, True, False, \
            txt("ui.streak.explain_none"), (255, 243, 220)
    elif state == "risk":
        days, first, lit, kept, cta, held, line, colour = 4, 1, -1, 4, True, False, \
            txt("ui.streak.explain_risk_clock", "3h 21m"), (255, 158, 128)
    elif state == "shield":
        days, first, lit, kept, cta, held, line, colour = 9, 8, -1, 2, False, True, \
            txt("ui.streak.shield_left_many", 4), K.MINT
    elif state == "week2":
        days, first, lit, kept, cta, held, line, colour = 11, 8, 3, 3, False, False, \
            txt("ui.streak.waiting_one"), K.GOLD
    else:
        days, first, lit, kept, cta, held, line, colour = 5, 1, 4, 4, False, False, \
            txt("ui.streak.waiting_one"), K.GOLD

    tiles = []
    for i in range(n):
        night = first + i
        if i == lit:
            tiles.append("lit")
        elif night <= days and i < kept:
            tiles.append("kept")
        elif night == days + 1:
            tiles.append("tonight")
        else:
            tiles.append("ahead")

    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.plain(sheet)
    K.rail(sheet, top=True)

    y = 22.0
    y = header(sheet, y)
    y = hero(sheet, y, days, max(0, min(n, days - first + 1)), n, line, colour)
    y = shield_row(sheet, y, held, 4)
    y = heading(sheet, y, 1 + (first - 1) // n)
    board(sheet, y, first, {"tiles": tiles, "cta": cta})

    if cta:
        footer(sheet)

    K.navbar(sheet, "home")
    return sheet.convert("RGB")


STATES = ("live", "risk", "none", "shield", "week2")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--state", choices=STATES, default="live")
    ap.add_argument("--contact", action="store_true", help="all five states side by side")
    ap.add_argument("--out", type=Path, default=Path("streak.png"))
    args = ap.parse_args()

    if args.contact:
        shots = [shot(s) for s in STATES]
        cell = 540
        sheet = Image.new("RGB", (cell * len(shots), int(cell * H / W)), K.GROUND)
        for i, s in enumerate(shots):
            sheet.paste(s.resize((cell, int(cell * H / W)), Image.LANCZOS), (i * cell, 0))
        out = sheet
    else:
        out = shot(args.state)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)

    print("  ladder: %d night(s); shield %d gems for %d days" % (len(RUNGS), SHIELD_GEMS, SHIELD_DAYS))
    print("  a tile is %.0fx%.0f; the reward draws %.0f tall in a %.0f seat, and a chest's own "
          "sprite box is %.0f" % (TILE_W, TILE_H, REWARD_TALL, SEAT_SIZE, REWARD_TALL / CHEST_FILL))
    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    main()
