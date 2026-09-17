# -*- coding: utf-8 -*-
"""Draws the Tasks & Bonuses page and its chest-odds panel, at the size a phone draws them.

    python Tools/render_tasks.py                 # the page
    python Tools/render_tasks.py --odds royal    # the panel a chest on the ladder opens
    python Tools/render_tasks.py --contact       # the page and all four panels side by side

**Why this exists.** Every question this page raises is a picture. Does the ladder read as a
pack of chests or as four icons on a plate; is a finished task's row obviously the thing to
tap; is the progress bar gold or is it the "dead yellow" it was reported as; do the panel's
lines read as a list of prizes or as a paragraph with dots in front of it. No numeric gate in
this project can open a PNG, and the Editor cannot photograph a `ScreenSpaceOverlay` canvas —
so the page is judged here, the way every board is (`CRAFT.md`).

**It reads the shipped content.** The slates, the targets, the tiers, the guaranteed bands and
the odds all come out of `progression.json` and `loc/en.json`, so a retune redraws rather than
going stale — a mirror with its own numbers answers questions about a screen the game does not
draw (invariant 44d).

**It does not draw the tweens**, so nothing here says whether the holy light around a ready row
*breathes* well; what it says is whether the light is visible at all against the kit's navy.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

W, H = K.W, K.H

CONTENT = K.REPO / "Assets" / "StreamingAssets" / "Content"
LOC = CONTENT / "loc" / "en.json"
TABLE = CONTENT / "progression.json"

# TasksScreen
CHROME = 92.0
BANNER_H = 138.0
LADDER_H = 252.0
HEADING_H = 76.0
ROW_H = 210.0
ROW_GAP = 14.0
WIDTH = 1000.0

# `TasksScreen.BuildRow` - the reward card's own furniture. `TEXT_X`/`TEXT_W` are the
# column between the seat and the chest; `HINT_W`/`HINT_H` is the box the hint shrinks in.
SEAT, GLYPH, SEAT_X = 148.0, 98.0, 96.0
TEXT_X, TEXT_W = 190.0, 570.0
HINT_W, HINT_H = TEXT_W, 32.0

#: `TasksScreen.ChestTall` and `ChestX`. A *drawn* height, for `StreakScreen.RewardTall`'s
#: reason: the closed chest carries the lid's headroom, so a box set straight from a height
#: draws it two thirds the size the number says and floats it high. `ChestPack` owns the
#: conversion and this mirror crops to alpha, so it draws the *drawn* size directly - and the
#: halo goes on the drawn middle rather than on the sprite's (invariant 44d).
ROW_CHEST_TALL, ROW_CHEST_X = 156.0, 112.0

#: `TasksScreen`'s seal - the mint disc and its tick, stamped on the lower-right of the drawn
#: chest once a task is paid. Drawn here because the chest it is placed against just moved, and
#: a mirror that skips a piece cannot say whether the piece landed on it (invariant 44d).
SEAL, SEAL_TICK, SEAL_X, SEAL_Y = 80.0, 46.0, 72.0, 52.0
CHEST_FILL, CHEST_WIDE = 155.0 / 244.0, 151.0 / 155.0
CHEST_LIFT, CHEST_ASPECT = 38.5 / 155.0, 176.0 / 244.0

# TasksScreen.BuildLadder — the pack, sharing HomeScreen.BuildChestRow's shape
CHEST_TALL, CHEST_SHORT = 196.0, 136.0
CHEST_DIP, CHEST_FLOOR, CHEST_OVERLAP = 20.0, -70.0, -.05

# ChestOddsOverlay
PANEL_W, BODY_W, LINE_W = 900.0, 760.0, 620.0
HEAD_ROOM, CHEST_H, RANK_H = 124.0, 250.0, 54.0
SECTION_H, LINE_H, SECTION_GAP, FOOT_ROOM = 64.0, 86.0, 18.0, 200.0
RIBBON_FRACTION, RIBBON_H, RIBBON_RISE, RIBBON_TILT = 0.78, 130.0, 22.0, -1.6
SCRIM = 0.72
INK = (59, 38, 26)
HEAD = (71, 46, 31)
ODDS = (199, 107, 26)

#: `PanelStack.TallestPanel` — the shortest canvas, with the ribbon's overhang counted at both
#: ends because a modal is centred.
TALLEST = K.W * 1.75 - 2 * 87.0


def strings():
    table = json.loads(LOC.read_text(encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


def tasks_table():
    return json.loads(TABLE.read_text(encoding="utf-8"))["tasks"]


LOCS = strings()
TASKS = tasks_table()


def txt(key, *args):
    s = LOCS.get(key, key)
    for i, a in enumerate(args):
        s = s.replace("{%d}" % i, str(a))
    return s


# ------------------------------------------------------------------ chest art
#: `TaskGoals.Icon`, without the leading "Ui/".
GOAL_ICON = {
    "runs": "ic_battle", "wins": "ic_trophy", "stars": "ic_star3d", "three_stars": "ic_stars",
    "matches": "ic_gem", "raiders": "Task/raiders", "boss": "Task/boss", "bosses": "Task/boss",
    "charms": "Task/charm", "cogs": "Task/cog", "bombs": "Utility/firepot",
    "utilities": "Utility/surge", "waves": "Task/wave", "streak": "ic_streak",
}


def chest_art():
    """tier id -> (sprite, its own alpha box). The closed icon carries the lid's headroom."""
    out = {}
    for tier in TASKS["tiers"]:
        im = Image.open(K.UI / "Chest" / ("%s.png" % tier["id"])).convert("RGBA")
        out[tier["id"]] = (im, im.getchannel("A").getbbox())
    return out


ART = chest_art()


def pack_numbers():
    """`ChestPack.Fill`, `Wide`, `Lift` and `Aspect`, measured off the PNGs the game ships.

    The C# constants are typed from these four and `ChestPack`'s doc claims "the four tiers
    agree to a pixel", so this measures **every** tier and returns the widest reading of each:
    an average would hide one icon drifting, which is the only failure worth printing. Nothing
    else can say the constants have gone stale after a re-cut (invariant 44b: measured, not
    typed), and it is printed rather than asserted, because a chest icon is allowed to change -
    what is not allowed is for it to change quietly.
    """
    out = []
    for tier in TASKS["tiers"]:
        im, box = ART[tier["id"]]
        tall = float(box[3] - box[1])
        out.append((tall / im.height,
                    (box[2] - box[0]) / tall,
                    ((box[1] + box[3]) / 2.0 - im.height / 2.0) / tall,
                    im.width / float(im.height)))
    return [max(col) for col in zip(*out)]


def pack(ids, tall, short, dip, floor, overlap):
    """`HomeScreen.BuildChestRow` — the arch, the cursor and the crest, for any row of ids.

    Returns a list of (id, x, foot, drawn width, drawn height) with the run centred on nought.
    """
    n = len(ids)
    near, far = (n - 1) % 2, n - 1
    span = far - near
    bell = [math.cos((0.0 if span <= 0 else (abs(2 * i - (n - 1)) - near) / float(span)) * math.pi * .5)
            for i in range(n)]
    high = [short + (tall - short) * b for b in bell]

    seat = sorted(range(n), key=lambda k: (-bell[k], -k))
    stands = [None] * n
    for k, s in enumerate(seat):
        stands[s] = ids[n - 1 - k]

    wide = []
    for i in range(n):
        im, box = ART[stands[i]]
        wide.append(high[i] * (box[2] - box[0]) / float(box[3] - box[1]))

    x = [0.0] * n
    for i in range(1, n):
        x[i] = x[i - 1] + (wide[i - 1] + wide[i]) * .5 - min(wide[i - 1], wide[i]) * overlap
    mid = (x[0] - wide[0] * .5 + x[n - 1] + wide[n - 1] * .5) * .5

    return [(stands[i], x[i] - mid, floor - dip * bell[i], wide[i], high[i]) for i in range(n)]


def drawn(tier, w, h, lit=True):
    im, box = ART[tier]
    out = im.crop(box).resize((max(1, int(round(w))), max(1, int(round(h)))), Image.LANCZOS)
    return out if lit else K.tint(out, (230, 235, 245))


# ------------------------------------------------------------------ the page
def header(sheet, y):
    """`TasksScreen.BuildHeader` — the name first, then what it says, then the wallet."""
    cy = y + BANNER_H / 2
    K.paste(sheet, K.skin("sq_blue", CHROME, CHROME), 76, cy)
    K.paste(sheet, K.skin("sq_orange", CHROME, CHROME), W - 76, cy)
    for x, glyph in ((76, "ic_left"), (W - 76, "ic_info")):
        K.paste(sheet, K.tint(K.fit(Image.open(K.UI / ("%s.png" % glyph)).convert("RGBA"), (46, 46)),
                              K.CREAM), x, cy)

    ribbon = K.skin("Hud/title", 720, BANNER_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.text(plate, txt("ui.tasks.title").upper(), plate.width / 2, plate.height / 2 - 6, 42, fill=K.SUN)
    K.paste(sheet, plate, W / 2, cy)
    y += BANNER_H + 4

    K.text(sheet, txt("ui.tasks.subtitle"), W / 2, y + 16, 24, fill=(219, 229, 255), outline=0)
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

    return y + CHROME + 18


def ladder(sheet, y, ready=("silver",)):
    """`TasksScreen.BuildLadder` — the same pack the hub draws, tappable and named."""
    cy = y + LADDER_H / 2
    K.paste(sheet, K.skin("Hud/panel", WIDTH, LADDER_H), W / 2, cy)

    band = Image.new("RGBA", (int(WIDTH) - 12, int(LADDER_H) - 12), (0, 0, 0, 0))
    fan = K.rays(256, 14).resize((820, 820), Image.LANCZOS)
    lit = Image.new("RGBA", fan.size, (*K.SUN, 0))
    lit.putalpha(fan.point(lambda v: int(v * .20)))
    band.alpha_composite(lit, (band.width // 2 - 410, band.height // 2 - 410 + 22))
    shelf = K.glow(170, 1.7, (10, 26, 60), .40).resize((900, 160), Image.LANCZOS)
    band.alpha_composite(shelf, (band.width // 2 - 450, band.height // 2 - 80 + 62))
    K.paste(sheet, band, W / 2, cy)

    ids = [t["id"] for t in TASKS["tiers"]]
    row = pack(ids, CHEST_TALL, CHEST_SHORT, CHEST_DIP, CHEST_FLOOR, CHEST_OVERLAP)

    for tier, x, foot, w, h in row:
        shade = K.glow(128, 1.9, (4, 12, 34), .46).resize(
            (max(1, int(w * 1.30)), max(1, int(h * .22))), Image.LANCZOS)
        K.paste(sheet, shade, W / 2 + x, cy - (foot - 2))

    for tier, x, foot, w, h in sorted(row, key=lambda r: r[4]):
        if tier in ready:
            K.paste(sheet, K.glow(int(h * 1.9), 1.7, K.GOLD, .55), W / 2 + x, cy - (foot + h / 2))
        K.paste(sheet, drawn(tier, w, h, tier in ready), W / 2 + x, cy - (foot + h / 2))

    K.text(sheet, txt("ui.tasks.ladder_hint"), W / 2, cy + LADDER_H / 2 - 22, 22,
           fill=(255, 243, 220), outline=2)

    return y + LADDER_H + 16


#: `TasksScreen.BarOrange` and `BarH` — pre-divided by what the beige fill sprite takes out of a
#: tint, and drawn 26 of the trough's 30. Both were settled on a swatch sheet drawn by this file.
BAR_ORANGE = (255, 150, 30)
BAR_FULL = (96, 235, 70)
BAR_H = 26


def bar(sheet, cx, cy, width, fill01):
    """`TasksScreen`'s progress bar — the trough, and the fill that turns green when it is full."""
    K.paste(sheet, K.skin("Hud/trough", width, 30), cx, cy)
    run = (width - 8) * fill01
    if run <= 1:
        return
    left = cx - width / 2 + 4
    colour = BAR_FULL if fill01 >= 1 else BAR_ORANGE
    K.paste(sheet, K.tint(K.skin("Hud/fill", run, BAR_H), colour), left + run / 2, cy)


def row_card(sheet, y, task, state):
    """One task row.

    `state` is 'counting', 'ready', 'held' or 'claimed'.

    'held' is finished and not yet claimable: the chest is rolled from the account id so the
    server can recompute it, and before the first sign-in there is nothing honest to open
    (`TaskLedger.CanClaim`). It carries the sentence and **not** the light, because a light
    is the page asking for a tap it is about to answer with an apology - see
    `TasksScreen.Paint`, where the same rule decides the streak board's halo.
    """
    cy = y + ROW_H / 2
    ready = state == "ready"
    held = state == "held"

    if ready:
        # `TasksScreen.Shine` — the pool, drawn under every card, at the top of its swell
        pool = K.glow(420, 1.35, K.SUN, .85).resize((int(WIDTH + 150), int(ROW_H + 130)), Image.LANCZOS)
        K.paste(sheet, pool, W / 2, cy)

    # `Skins.PlateNavy` - the mould the profile's sections wear, halved in value. The kit's
    # `Hud/card` is flat and unlit; this one has the two-tone face and the lit top edge, which
    # is the whole of what makes a row read as a thing holding a prize.
    card = K.skin("Hud/plate_navy", WIDTH, ROW_H)
    if state == "claimed":
        card = K.tint(card, (170, 175, 190))
    K.paste(sheet, card, W / 2, cy)

    if ready:
        # the rim, which is the half of the light that has to be drawn *on* the card
        d = ImageDraw.Draw(sheet)
        box = [W / 2 - WIDTH / 2 + 3, cy - ROW_H / 2 + 3, W / 2 + WIDTH / 2 - 3, cy + ROW_H / 2 - 3]
        d.rounded_rectangle(box, radius=30, outline=(255, 244, 206, 255), width=7)

    left = W / 2 - WIDTH / 2
    K.paste(sheet, K.skin("Hud/slot", int(SEAT), int(SEAT)), left + SEAT_X, cy)
    try:
        glyph = Image.open(K.UI / ("%s.png" % GOAL_ICON.get(task["goal"], "ic_gift"))).convert("RGBA")
        K.paste(sheet, K.fit(glyph, (int(GLYPH), int(GLYPH))), left + SEAT_X, cy - 2)
    except FileNotFoundError:
        pass

    text_x, text_w = left + TEXT_X, TEXT_W
    name = txt("task.%s.name" % task["id"], task["target"])
    K.text(sheet, name, text_x, cy - 50, 32, anchor="l")

    track = text_w - 130
    # A counting row has to be *genuinely* short of its target, or the mirror draws a row it
    # calls unfinished with a full green bar — which is the one thing this picture is for.
    done = task["target"] if state != "counting" else min(task["target"] - 1, max(0, task["target"] * 2 // 5))
    bar(sheet, text_x + track / 2, cy + 12, track, done / float(task["target"]))
    K.text(sheet, "%d / %d" % (done, task["target"]), text_x + track + 12, cy + 12, 26, anchor="l")

    hint = (txt("ui.tasks.tap_to_claim") if ready
            else txt("ui.chest.needs_connection") if held
            else txt("ui.tasks.claimed") if state == "claimed" else "")
    if hint:
        # `TasksScreen` builds this as a **MiddleLeft** `Shrinkable` in a 520x30 box
        # (`TextW`), so it runs rightwards from the text column and shrinks to 13 rather
        # than spilling. Drawn centred here it ran *leftwards off the card* - which looked
        # like a copy fault the moment a longer sentence arrived and was a mirror fault all
        # along (44d). `shrunk_left` is the shape that matches, and it returns the size it
        # settled on: "13 against a floor of 13" is the tell that a string has outgrown the
        # column and `UIKit.Shrinkable` is about to truncate it silently (19n).
        px = K.shrunk_left(sheet, hint, text_x, cy + 54 - HINT_H / 2, HINT_W, HINT_H, 24, 14,
                           fill=K.GOLD if ready else K.SUN if held else (255, 243, 220),
                           outline=0)
        if px <= 14:
            print("  hint '%s' settled at %dpx against a floor of 14 - it has outgrown the "
                  "column" % (hint, px))

    right = W / 2 + WIDTH / 2
    tier = task["tier"]
    h = ROW_CHEST_TALL
    w = h * CHEST_WIDE
    if ready:
        K.paste(sheet, K.glow(int(h * 2), 2.0, K.GOLD, .55), right - ROW_CHEST_X, cy)
    K.paste(sheet, drawn(tier, w, h, state != "claimed"), right - ROW_CHEST_X, cy)

    if state == "claimed":
        d = ImageDraw.Draw(sheet)
        cx, sy = right - SEAL_X, cy + SEAL_Y
        d.ellipse([cx - SEAL / 2, sy - SEAL / 2, cx + SEAL / 2, sy + SEAL / 2],
                  fill=K.MINT + (255,))
        try:
            tick = Image.open(K.UI / "ic_check.png").convert("RGBA")
            K.paste(sheet, K.tint(K.fit(tick, (int(SEAL_TICK), int(SEAL_TICK))), (255, 255, 255)),
                    cx, sy)
        except FileNotFoundError:
            pass


def heading(sheet, y, period):
    cy = y + HEADING_H / 2
    left = W / 2 - WIDTH / 2
    K.text(sheet, txt("ui.tasks.%s" % period).upper(), left + 18, cy, 30, fill=K.GOLD, anchor="l")
    K.text(sheet, "1 / 3", left + 428, cy, 26, fill=(255, 243, 220), outline=0, anchor="l")
    K.paste(sheet, K.skin("Hud/trough", 320, 52), W / 2 + WIDTH / 2 - 160, cy)
    K.text(sheet, txt("ui.tasks.resets_in", "6h 12m"), W / 2 + WIDTH / 2 - 150, cy, 22,
           fill=(255, 243, 220), outline=0)
    return y + HEADING_H




# `ConnectBanner` - the standing plate on an account that has never been online, drawn under
# the chest box here and under the pass on the season page. Its height decides where the list
# below starts, so a mirror that skipped it would draw a page nobody with a fresh install sees.
CONNECT_H, CONNECT_GAP = 104.0, 14.0


def connect_banner(sheet, y, width):
    """`ConnectBanner.Build` - amber plate, key, one wrapped sentence."""
    cy = y + CONNECT_H / 2
    K.paste(sheet, K.skin("Hud/plate_orange", width, CONNECT_H), W / 2, cy)

    left = W / 2 - width / 2
    K.paste(sheet, K.glow(200, 2.0, K.SUN, .26), left + 92, cy)

    mark = Image.open(K.UI / "ic_key.png").convert("RGBA")
    K.paste(sheet, K.tint(K.fit(mark, (64, 64)), K.CREAM), left + 92, cy)

    text_w = width - 184
    px = K.shrunk_left(sheet, txt("ui.chest.connect_once"), left + 160,
                       cy - (CONNECT_H - 24) / 2, text_w, CONNECT_H - 24, 26, 17,
                       fill=K.CREAM, outline=3)
    print("  connect banner: settled at %dpx against a floor of 17" % px)

    return y + CONNECT_H + CONNECT_GAP


def page(offline=False):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.plain(sheet)
    K.rail(sheet, top=True)

    y = 22.0
    y = header(sheet, y)
    y = ladder(sheet, y)
    if offline:
        y = connect_banner(sheet, y, WIDTH)

    y += 8
    # Four of the states a row can be in, so one page shows every one of them.
    states = ["ready", "counting", "claimed", "held"]
    for period in ("daily", "weekly"):
        y = heading(sheet, y, period)
        for i, task in enumerate(TASKS[period][:len(states)]):
            row_card(sheet, y, task, states[i])
            y += ROW_H + ROW_GAP
        y += 18

    K.navbar(sheet, "home")
    return sheet.convert("RGB")


# ------------------------------------------------------------------ the panel
def band_line(band):
    """`ChestOddsOverlay.Band` — "70-110 Coins", "1 Mending", "24h Heart Boost"."""
    kind = band["kind"]
    if kind == "utility":
        name = txt("utility.%s.name" % band["item"])
    else:
        name = txt("ui.reward.%s" % kind)
    lo, hi = band.get("min", 0), band.get("max", 0)
    if kind == "heart_boost":
        return "%dh %s" % (hi, name)
    return ("%d %s" % (lo, name)) if lo == hi else ("%d-%d %s" % (lo, hi, name))


def band_icon(band):
    kind = band["kind"]
    if kind == "credits":
        return "Coin/f0"
    if kind == "utility":
        return "Utility/%s" % band["item"]
    return {"gems": "ic_gem", "hearts": "ic_heart", "heart_boost": "ic_heart_boost",
            "hints": "ic_hint"}.get(kind, "ic_gift")


def odds_panel(tier_id, height):
    """`ChestOddsOverlay.Build` — the chest, its rank, the two lists and the way out.

    Every line is a prize's own picture in a seat, its name centred, and — for the weighted
    slot — the odds at the far end. The dot-and-left-aligned-line version this replaced is the
    one thing a numeric gate can never tell you was wrong.
    """
    tier = next(t for t in TASKS["tiers"] if t["id"] == tier_id)
    chest = tier["chest"]
    rank = [t["id"] for t in TASKS["tiers"]].index(tier_id) + 1

    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    cx, cy = W / 2, H / 2
    top = cy - height / 2

    K.paste(sheet, K.skin("panel_main", PANEL_W, height), cx, cy)

    ribbon = K.skin("ribbon_orange", PANEL_W * RIBBON_FRACTION, RIBBON_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.text(plate, txt("chest.%s.name" % tier_id).upper(), plate.width / 2, plate.height / 2 - 4,
           50, fill=K.CREAM, outline=4)
    plate = plate.rotate(RIBBON_TILT, resample=Image.BICUBIC, expand=True)
    K.paste(sheet, plate, cx, top + RIBBON_RISE)

    y = top + HEAD_ROOM
    im, box = ART[tier_id]
    w = CHEST_H * (box[2] - box[0]) / float(box[3] - box[1])
    sheet.alpha_composite(K.glow(int(CHEST_H * 1.9), 2.0, K.SUN, .38),
                          (int(cx - CHEST_H * .95), int(y + CHEST_H / 2 - CHEST_H * .95)))
    K.paste(sheet, drawn(tier_id, w, CHEST_H), cx, y + CHEST_H / 2)
    y += CHEST_H

    K.text(sheet, txt("ui.chest.rank", rank, len(TASKS["tiers"])).upper(), cx, y + RANK_H / 2, 28,
           fill=HEAD, outline=0)
    y += RANK_H

    for key, rows in (("ui.chest.always", [{"band": b} for b in chest["guaranteed"]]),
                      ("ui.chest.one_of", chest["options"])):
        if not rows:
            continue

        K.text(sheet, txt(key).upper(), cx, y + SECTION_H / 2, 34, fill=HEAD, outline=0)
        y += SECTION_H

        total = sum(r.get("weight", 0) for r in rows) or 1
        for r in rows:
            band = r.get("band", r)
            odds = "weight" in r
            mid = y + LINE_H / 2

            K.paste(sheet, K.skin("Hud/slot", 74, 74), cx - LINE_W / 2 + 44, mid)
            try:
                icon = Image.open(K.UI / ("%s.png" % band_icon(band))).convert("RGBA")
                K.paste(sheet, K.fit(icon, (54, 54)), cx - LINE_W / 2 + 44, mid)
            except FileNotFoundError:
                pass

            left = cx - LINE_W / 2 + 92
            right = cx + LINE_W / 2 - 118
            K.text(sheet, band_line(band), (left + right) / 2, mid, 32, fill=INK, outline=0)

            if odds:
                K.text(sheet, "%d%%" % round(100.0 * r["weight"] / total),
                       cx + LINE_W / 2, mid, 32, fill=ODDS, outline=0, anchor="r")
            y += LINE_H

        y += SECTION_GAP

    K.paste(sheet, K.skin("btn_green", 560, 120), cx, top + height - 92)
    K.text(sheet, txt("ui.common.got_it"), cx, top + height - 92 - 120 * 0.0231, 44,
           fill=K.CREAM, outline=4)

    return sheet


def odds_height(tier_id):
    """`ChestOddsOverlay.Height` — counted rather than reserved, so no panel carries a hole."""
    chest = next(t for t in TASKS["tiers"] if t["id"] == tier_id)["chest"]
    y = HEAD_ROOM + CHEST_H + RANK_H
    y += SECTION_H + LINE_H * len(chest["guaranteed"]) + SECTION_GAP
    if chest["options"]:
        y += SECTION_H + LINE_H * len(chest["options"]) + SECTION_GAP
    return y + FOOT_ROOM


def odds_shot(tier_id):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.plain(sheet)
    sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * SCRIM))))
    sheet.alpha_composite(odds_panel(tier_id, odds_height(tier_id)))
    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--odds", nargs="?", const="royal", default=None,
                    help="the panel a chest on the ladder opens (wood/silver/gold/royal)")
    ap.add_argument("--contact", action="store_true", help="the page and all four panels")
    ap.add_argument("--offline", action="store_true",
                    help="an account that has never been online: the connect-once banner")
    ap.add_argument("--out", type=Path, default=Path("tasks.png"))
    args = ap.parse_args()

    fill, wide, lift, aspect = pack_numbers()
    print("  fill %.4f  wide %.4f  lift %.4f  aspect %.4f"
          "   (ChestPack: 0.6352 / 0.9742 / 0.2484 / 0.7213)" % (fill, wide, lift, aspect))

    if args.contact:
        shots = [page(args.offline)] + [odds_shot(t["id"]) for t in TASKS["tiers"]]
        cell = 540
        sheet = Image.new("RGB", (cell * len(shots), int(cell * H / W)), K.GROUND)
        for i, s in enumerate(shots):
            sheet.paste(s.resize((cell, int(cell * H / W)), Image.LANCZOS), (i * cell, 0))
        out = sheet
    elif args.odds:
        out = odds_shot(args.odds)
        print("  %s panel %.0f tall (tallest a centred panel may be: %.0f)"
              % (args.odds, odds_height(args.odds), TALLEST))
    else:
        out = page(args.offline)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    main()
