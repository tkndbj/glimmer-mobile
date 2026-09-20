# -*- coding: utf-8 -*-
"""Draws the streak page at the size a phone draws it.

    python Tools/render_streak.py                 # a live streak, one night waiting
    python Tools/render_streak.py --state risk    # the flame about to go out, with the CTA
    python Tools/render_streak.py --state none    # a player who has never held one
    python Tools/render_streak.py --state shield  # a protected streak, the offer row bought
    python Tools/render_streak.py --state week2   # the second lap, so the board is nights 8-14
    python Tools/render_streak.py --contact       # all six side by side

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

#: `StreakScreen` - one night is one row, the full width of the page.
ROW_H, ROW_GAP = 184.0, 12.0

#: `StreakScreen.SeatSize`, `SeatX`, `RewardTall`, `TextX` and `TextW`. `REWARD_TALL` is a
#: *drawn* height, which is the only unit a chest and a gem can share - the closed chest carries
#: the lid's headroom, so a box set straight from a height draws it two thirds the size of the
#: gem beside it. `ChestPack` owns the conversion and this mirror has to make the same one or it
#: answers the wrong question about every row that pays a chest (invariant 44d).
SEAT_SIZE, SEAT_X, REWARD_TALL = 148.0, 114.0, 110.0
TEXT_X, TEXT_W = 206.0, 450.0
CHEST_FILL, CHEST_LIFT = 155.0 / 244.0, 38.5 / 155.0
PER_ROW = 4

#: `StreakScreen.CardRound` - what the row's plate is rounded to, and what the rim and the
#: waiting row's light are both clipped to. One number here as there, for the reason it is one
#: number there: a mask a few units off the shape it clips is light outside the plate.
CARD_ROUND = 30

#: `StreakScreen.KeyW`, `KeyH`, `MarkH`, `MarkType`, `MarkLeast` and `MarkRoom` - the COLLECT
#: key at the right end of a row and the pill that stands in the same place when there is
#: nothing to collect yet. **One width**, because they are one answer in two moods; the room
#: `Scenery.Pill` really leaves its words is twenty units off the left and sixteen off the right.
KEY_W, KEY_H = 212.0, 84.0
MARK_H = 62.0
MARK_TYPE, MARK_LEAST = 23, 14
MARK_ROOM = KEY_W - 20.0 - 16.0

#: `StreakScreen.ClockType` and `ClockLeast` - the hero pill's design size and its floor. Every
#: fit starts from the design size, never from what the last line left behind, because the
#: screen's fitter does too (`UIKit.OneLineLabel`).
CLOCK_TYPE, CLOCK_LEAST = 23, 14

BAR_ORANGE = (255, 150, 30)
BAR_FULL = (96, 235, 70)
BAR_H = 26

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

    K.one_line(sheet, label, cx + h * .30, cy, w - h * .82 - 16,
               CLOCK_TYPE, CLOCK_LEAST, colour, "the hero pill")


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
    return y + HEADING_H


# ------------------------------------------------------------------- the board
def aura(sheet, left, cy, size):
    """`StreakScreen.Aura` - the halo and the turning fan, clipped to the card.

    Drawn at one instant, which is what a mirror can say about a loop: whether the light is
    *there* and whether the reward survives being inside it. Whether it reads as travelling is
    a question only a device answers.

    **What it can say, and did not, is where the light went.** The fan is 318 units across on a
    row 184 tall, so two thirds of it was drawn over the night above and the night below - and
    this drew it that way faithfully, which is the mirror doing its job and nobody reading it.
    The screen clips it now with a `Mask` over `Art.Round`, so the light is cut to the plate's
    own rounded shape; a rectangular clip leaks a square nub of light at each corner, which is
    the thing to look for here if the radius ever drifts (invariant 44i).

    **A gold ring scaling out of the seat was the third piece and is gone**, at the owner's
    instruction - so this drew it too, and a mirror still drawing a piece the screen has
    dropped is the one thing that makes a render worse than no render at all (invariant 44d).
    It is `aura` rather than `ring` for the same reason: the name said which of the three it
    was about, and it was the one that went.
    """
    cell = Image.new("RGBA", (int(WIDTH), int(ROW_H)), (0, 0, 0, 0))
    K.paste(cell, K.glow(int(size * 2.4), 1.7, K.GOLD, .46), SEAT_X, ROW_H / 2)

    fan = K.rays(256, 14).resize((int(size * 2.15), int(size * 2.15)), Image.LANCZOS)
    lit = Image.new("RGBA", fan.size, (*K.SUN, 0))
    lit.putalpha(fan.point(lambda v: int(v * .34)))
    cell.alpha_composite(lit, (int(SEAT_X - fan.width / 2), int(ROW_H / 2 - fan.height / 2)))

    shape = Image.new("L", cell.size, 0)
    ImageDraw.Draw(shape).rounded_rectangle([0, 0, cell.width - 1, cell.height - 1],
                                            radius=CARD_ROUND, fill=255)
    cell.putalpha(Image.composite(cell.getchannel("A"),
                                  Image.new("L", cell.size, 0), shape))

    sheet.alpha_composite(cell, (int(left), int(cy - ROW_H / 2)))


def row(sheet, cx, cy, night, rung, state, days):
    """One night's row.

    `state` is 'kept', 'waiting', 'lit', 'blocked', 'tonight' or 'ahead'.

    'blocked' is a night that is owed and cannot be handed over yet: a rung paying a
    chest needs an account id to roll it against, so before the first sign-in there is
    nothing honest to open (`DailyStreak.CanClaimChests`). It draws no key and no light
    and answers in the pill, which is what the screen does - see `StreakScreen.Paint`.
    """
    lit = state == "lit"
    kept = state == "kept"
    blocked = state == "blocked"
    waiting = state in ("lit", "waiting")

    if state == "ahead":
        cell = Image.new("RGBA", (int(WIDTH + 200), int(ROW_H + 60)), (0, 0, 0, 0))
        row(cell, cell.width / 2, cell.height / 2, night, rung, "ahead_flat", days)
        cell.putalpha(cell.getchannel("A").point(lambda v: int(v * .74)))
        sheet.alpha_composite(cell, (int(cx - cell.width / 2), int(cy - cell.height / 2)))
        return

    if lit:
        pool = K.glow(420, 1.35, K.SUN, .80).resize(
            (int(WIDTH + 150), int(ROW_H + 130)), Image.LANCZOS)
        K.paste(sheet, pool, cx, cy)

    card = K.skin("Hud/plate_navy", WIDTH, ROW_H)
    if kept:
        card = K.tint(card, (230, 240, 255))
    K.paste(sheet, card, cx, cy)

    left = cx - WIDTH / 2
    sx = left + SEAT_X

    if lit:
        aura(sheet, left, cy, SEAT_SIZE)

    K.paste(sheet, K.skin("Hud/slot", SEAT_SIZE, SEAT_SIZE), sx, cy)

    if rung.get("tier"):
        K.paste(sheet, drawn(rung["tier"], REWARD_TALL, not kept), sx, cy)
    else:
        kind = rung.get("kind") or ""
        glyph = {"credits": "Coin/f0", "gems": "ic_gem"}.get(kind, "ic_star")
        icon = Image.open(K.UI / ("%s.png" % glyph)).convert("RGBA")
        box = (int(REWARD_TALL), int(REWARD_TALL))
        K.paste(sheet, K.fit(icon, box) if not kept
                else K.tint(K.fit(icon, box), (214, 224, 240)), sx, cy)

    title = K.GOLD if lit else ((123, 216, 106) if kept else K.CREAM)
    K.text(sheet, txt("ui.streak.day_n", night).upper(), left + TEXT_X, cy - 30, 31,
           fill=title, anchor="l")
    K.text(sheet, says(rung), left + TEXT_X, cy + 26, 25,
           fill=(255, 243, 220) if not kept else (214, 205, 186), outline=2, anchor="l")

    # --- the right end: one answer at a time
    if waiting:
        bx = cx + WIDTH / 2 - 130
        K.paste(sheet, K.skin("btn_green", KEY_W, KEY_H), bx, cy)
        K.shrunk(sheet, txt("ui.streak.collect").upper(), bx, cy - KEY_H * 0.0231,
                 KEY_W - 36, 52, 34, 20)
    elif kept:
        sxx = cx + WIDTH / 2 - 146
        seal = Image.open(K.UI / "seal_gold.png").convert("RGBA")
        K.paste(sheet, K.fit(seal, (84, 84)), sxx, cy)
        tick = Image.open(K.UI / "ic_check.png").convert("RGBA")
        K.paste(sheet, K.tint(K.fit(tick, (44, 44)), K.CREAM), sxx, cy)
    else:
        bx = cx + WIDTH / 2 - 130
        away = night - days
        if blocked:
            words, fill = txt("ui.streak.needs_connection"), K.SUN
        elif state == "tonight":
            words, fill = txt("ui.streak.tonight"), K.AQUA
        elif away <= 1:
            words, fill = txt("ui.streak.in_one"), (255, 243, 220)
        else:
            words, fill = txt("ui.streak.in_many", away), (255, 243, 220)

        plate = Image.new("RGBA", (int(KEY_W), int(MARK_H)), (0, 0, 0, 0))
        dd = ImageDraw.Draw(plate)
        dd.rounded_rectangle([0, 0, KEY_W - 1, MARK_H - 1], radius=28, fill=(13, 23, 46, 179))
        dd.rounded_rectangle([1, 1, KEY_W - 2, MARK_H - 2], radius=28,
                             outline=(255, 255, 255, 33), width=3)
        K.paste(sheet, plate, bx, cy)
        K.one_line(sheet, words.upper(), bx, cy, MARK_ROOM, MARK_TYPE, MARK_LEAST, fill,
                   "night %d's pill" % night)

    # The rim, last, because on the screen it is the last child of the row but one and draws
    # over everything on it - including the light, whose cut edge it covers.
    if lit:
        d = ImageDraw.Draw(sheet)
        d.rounded_rectangle([cx - WIDTH / 2 + 3, cy - ROW_H / 2 + 3,
                             cx + WIDTH / 2 - 3, cy + ROW_H / 2 - 3],
                            radius=CARD_ROUND, outline=(255, 244, 206, 255), width=7)


def says(rung):
    """`StreakScreen.Says` - what a night pays, in words."""
    if rung.get("tier"):
        return txt("chest.%s.name" % rung["tier"])

    kind = rung.get("kind") or ""
    if not kind:
        return txt("ui.streak.rung_none")

    unit = txt("ui.reward.%s" % kind)
    return "%s %s" % ("{:,}".format(rung.get("amount", 0)), unit)


def board(sheet, top, first, states, days):
    """`StreakScreen.BuildBoard` - the list, and the fold it opens on."""
    tall = len(RUNGS) * (ROW_H + ROW_GAP) - ROW_GAP
    # `StreakScreen.BoardFoot` - nothing stands under the board, so the clearance is the
    # nav bar and a margin in every state (invariant 48i: the mirror drops a withdrawn piece
    # in the same change).
    bottom = K.NAV_HEIGHT + 20
    band = H - top - bottom

    # `StreakScreen.FocusOnPending` - the board opens on the night that can be taken, not on
    # night one, so the mirror has to scroll the same way or it draws a page nobody sees.
    offset = 0.0
    if tall > band:
        want = next((i for i, st in enumerate(states["rows"])
                     if st in ("lit", "blocked")), -1)
        if want >= 0:
            offset = max(0.0, min(want * (ROW_H + ROW_GAP) - (band - ROW_H) * .5, tall - band))

    strip = Image.new("RGBA", (W, int(max(tall, band))), (0, 0, 0, 0))

    for i, rung in enumerate(RUNGS):
        row(strip, W / 2, i * (ROW_H + ROW_GAP) + ROW_H / 2, first + i,
            rung, states["rows"][i], days)

    window = strip.crop((0, int(offset), W, int(offset + band)))
    sheet.alpha_composite(window, (0, int(top)))

    return tall > band


# ------------------------------------------------------------------- the states
def shot(state):
    """One page. The states are the six a player can actually be in."""
    n = len(RUNGS)

    # `played` is `StreakScreen._playedToday`, and it decides what the night above the streak
    # is called: **tonight** while today is still to be finished, **tomorrow night** once it
    # has. This mirror had no such flag and drew "tonight" in every state, so TOMORROW NIGHT -
    # half again the length of TONIGHT, and the longest thing either pill on this page ever
    # says - was the one string it could not reach. It was spilling out of the side of its
    # plate on a device the whole time, and nothing here could be asked about it (44d).
    played = True

    if state == "none":
        days, first, lit, kept, held, line, colour = 0, 1, -1, 0, False, \
            txt("ui.streak.explain_none"), (255, 243, 220)
        played = False
    elif state == "risk":
        days, first, lit, kept, held, line, colour = 4, 1, -1, 4, False, \
            txt("ui.streak.explain_risk_clock", "3h 21m"), (255, 158, 128)
        played = False
    elif state == "shield":
        days, first, lit, kept, held, line, colour = 9, 8, -1, 2, True, \
            txt("ui.streak.shield_left_many", 4), K.MINT
    elif state == "offline":
        # A chest night waiting on an account id. Rung index 2 is the silver chest, so
        # this is the pending night being a chest rather than a figure - which is the
        # only way the state is reachable at all.
        days, first, lit, kept, held, line, colour = 2, 1, 2, 2, False, \
            txt("ui.streak.waiting_one"), K.GOLD
    elif state == "week2":
        days, first, lit, kept, held, line, colour = 11, 8, 3, 3, False, \
            txt("ui.streak.waiting_one"), K.GOLD
    else:
        days, first, lit, kept, held, line, colour = 5, 1, 4, 4, False, \
            txt("ui.streak.waiting_one"), K.GOLD

    rows = []
    for i in range(n):
        night = first + i
        if i == lit:
            rows.append("blocked" if state == "offline" else "lit")
        elif night <= days and i < kept:
            rows.append("kept")
        elif night == days + 1 and not played:
            rows.append("tonight")
        else:
            rows.append("ahead")

    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.plain(sheet)
    K.rail(sheet, top=True)

    y = 22.0
    y = header(sheet, y)
    y = hero(sheet, y, days, max(0, min(n, days - first + 1)), n, line, colour)
    y = shield_row(sheet, y, held, 4)
    y = heading(sheet, y, 1 + (first - 1) // n)
    scrolls = board(sheet, y, first, {"rows": rows}, days)

    K.navbar(sheet, "home")
    return sheet.convert("RGB")


STATES = ("live", "risk", "none", "shield", "week2", "offline")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--state", choices=STATES, default="live")
    ap.add_argument("--contact", action="store_true", help="all six states side by side")
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
    print("  a row is %.0fx%.0f; the reward draws %.0f tall in a %.0f well, and a chest's own "
          "sprite box is %.0f" % (WIDTH, ROW_H, REWARD_TALL, SEAT_SIZE, REWARD_TALL / CHEST_FILL))
    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    main()
