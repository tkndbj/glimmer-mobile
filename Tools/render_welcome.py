# -*- coding: utf-8 -*-
"""Draws the Welcome Bonus page and its choice sheet at the size a phone draws them.

    python Tools/render_welcome.py                 # one row in each state
    python Tools/render_welcome.py --done          # every quest taken
    python Tools/render_welcome.py --choice        # the sheet a COLLECT key opens
    python Tools/render_welcome.py --preview       # the same sheet, view-only, a tap on a row opens
    python Tools/render_welcome.py --out welcome.png

**Why this exists.** The page is a list of prizes (invariant 58), and every question it raises
is a picture: does the turret read as the biggest thing on its row, do the day boxes read as
"three days" and tick as they fill, does the COLLECT key stand where the boxes stood without
touching the pill, does the choice sheet's row carry a name, a sentence and a key without any of
the three leaving its box in the longest language. No numeric gate can open a PNG and the Editor
cannot photograph a `ScreenSpaceOverlay` canvas, so the page is judged here, the way the tasks
page, the streak board and the deal sheet are.

**It reads the shipped content.** The quests, the turrets, the days, the prices and the
sentences come out of `progression.json` and `loc/en.json`, so a retune redraws rather than
going stale.

**It is a mirror, and mirrors drift.** Every constant is named after the field it copies
(`WelcomeScreen.RowH`, `SeatX`, `PillX`, `WelcomeChoiceOverlay.RowW`). It does not draw the tweens.
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
WARDS = K.REPO / "Assets" / "Game" / "Art" / "Siege" / "Wards"

# WelcomeScreen
CHROME = 92.0
BANNER_H = 138.0
WIDTH = 1000.0
ROW_H, ROW_GAP = 250.0, 14.0
SEAT, SEAT_X, BODY_TALL = 148.0, 96.0, 120.0
TEXT_X, TEXT_W = 190.0, 560.0
BOX, BOX_GAP, TICK, MOST_BOXES = 48.0, 8.0, 30.0, 10
BAR_W, BAR_H = 360.0, 26.0
COLLECT_W, COLLECT_H = 280.0, 88.0
PILL_W, PILL_H, PILL_X = 232.0, 76.0, -136.0
TO_GO_W, TO_GO_NUMBER_Y, TO_GO_WORDS_Y, TO_GO_NUMBER, TO_GO_WORDS = 232.0, 24.0, -34.0, 72, 32
MINT = (0x7B, 0xD8, 0x6A)  # Pal.Mint
WALLET_W, WALLET_H, WALLET_X = 160.0, 72.0, 444.0
CARD_ROUND = 30
SUB_PILL, SUB_ART, SUB_SLOT_H = "btn_orange", "challenge_door", 210.0

# WelcomeChoiceOverlay, which is ChallengeTierOverlay's stack on VictoryFrame.
WIN_W, CREST_REACH = 1000.0, 202.0
PANEL_INK = (150, 184, 176)
CROWN_Y, BANNER_Y, BANNER_W, BANNER_H_WIN = 114.0, -30.0, 566.0, 157.0
WORD_LIFT, WORD_W, WORD_H = 34.0, 356.0, 74.0
LINE_Y, ROWS_TOP, SHEET_ROW_H, SHEET_ROW_GAP, TAIL, FOOT_H = 150.0, 200.0, 230.0, 14.0, 30.0, 170.0
SHEET_ROW_W, STONE, STONE_X, SHEET_TEXT_X, SHEET_TEXT_W = 880.0, 160.0, 104.0, 204.0, 370.0
LINE_TOP, LINE_H = 22.0, 124.0
KEY_W, KEY_H, KEY_INSET = 280.0, 116.0, 16.0
PREVIEW_PILL_W, PREVIEW_PILL_H = 280.0, 84.0

# SiegeView.Tints, in the order WardLine.Colours names them.
SEAT_TINTS = [(0xF2, 0x40, 0x4F), (0x7B, 0xD8, 0x6A), (0x4F, 0xC1, 0xFF), (0xFF, 0x8A, 0x2B)]
BAR_ORANGE, BAR_FULL = (255, 150, 30), (96, 235, 70)


def strings():
    with open(CONTENT / "loc" / "en.json", encoding="utf8") as f:
        return {e["key"]: e["text"] for e in json.load(f)["entries"]}


STR = strings()


def txt(key, *args):
    s = STR.get(key, key)
    return s.format(*args) if args else s


def progression():
    with open(CONTENT / "progression.json", encoding="utf8") as f:
        return json.load(f)


def quests():
    return list((progression().get("welcome") or {}).get("quests") or [])


def ward(ward_id):
    for m in (progression().get("wards") or {}).get("models") or []:
        if m.get("id") == ward_id:
            return m
    return {}


def seat(quest):
    """`WelcomeQuest.Colour`: the authored seat letter as an index, never the row's position."""
    return "rgby".index(quest["colour"])


def price_of(quest):
    m = ward(quest.get("ward"))
    if (m.get("gemPrice") or 0) > 0:
        return m["gemPrice"], "gems"
    return m.get("coinPrice") or 0, "credits"


def compact(n):
    """`Compact.Number`: thousands with one decimal, as the pills print them."""
    n = int(n)
    if n >= 1_000_000:
        return ("%.1fM" % (n / 1e6)).replace(".0M", "M")
    if n >= 10_000:
        return ("%.1fK" % (n / 1e3)).replace(".0K", "K")
    return "{:,}".format(n)


SENTENCE = {
    "runs": "ui.welcome.goal.runs", "wins": "ui.welcome.goal.wins",
    "task_claims": "ui.welcome.goal.task_claims", "challenge_plays": "ui.welcome.goal.challenge_plays",
    "challenge_wins": "ui.welcome.goal.challenge_wins", "streak": "ui.welcome.goal.streak",
}


def left_line(sheet, s, left, cy, room, size, floor, fill=K.CREAM, outline=3):
    """A `MiddleLeft` one-line `Shrinkable`: the largest size from `size` down to `floor` whose
    line fits `room`, drawn flush left. Returns the size it settled at."""
    while size > floor and K.font(size).getlength(s) > room:
        size -= 1
    K.text(sheet, s, left, cy, size, fill=fill, outline=outline, anchor="l")
    return size


# ------------------------------------------------------------------ the page
def header(sheet, y):
    """`WelcomeScreen.BuildHeader` - the back key, the ribbon, the credits pill, the sentence."""
    cy = y + BANNER_H / 2
    K.paste(sheet, K.skin("sq_blue", CHROME, CHROME), 76, cy)
    try:
        K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_left.png").convert("RGBA"), (46, 46)), K.CREAM), 76, cy)
    except FileNotFoundError:
        pass

    ribbon = K.skin("Hud/title", 720, BANNER_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.text(plate, txt("ui.welcome.title").upper(), plate.width / 2, plate.height / 2 - 6, 42, fill=K.SUN)
    K.paste(sheet, plate, W / 2, cy)

    # `BuildWallet`: the tasks page's pill at a narrower cut, clear of the ribbon's right end.
    wx = W / 2 + WALLET_X
    K.paste(sheet, K.skin("Hud/trough", WALLET_W, WALLET_H), wx, cy)
    gx = wx - WALLET_W / 2 + 44
    K.paste(sheet, K.glow(88, 2.0, K.GOLD, .30), gx, cy)
    try:
        K.paste(sheet, K.fit(Image.open(K.UI / "Coin" / "f0.png").convert("RGBA"), (48, 48)), gx, cy)
    except FileNotFoundError:
        pass
    K.text(sheet, "12.5K", wx + 26, cy, 28)
    print("  wallet pill: left edge %d against the ribbon's right end %d" % (wx - WALLET_W / 2, W / 2 + 360))
    y += BANNER_H + 4

    # `DoorKey.Plate`: the hub's Daily Challenges door, cut orange, with no tap.
    _, size, room = K.door_key(sheet, W / 2, y + SUB_SLOT_H, WIDTH, SUB_SLOT_H,
                               txt("ui.welcome.subtitle").upper(), SUB_ART, pill=SUB_PILL,
                               caption_left=K.DOOR_PLATE_CAPTION_LEFT, ink=(255, 255, 255))
    print("  subtitle plate: caption settled at %d in %d of room (floor 26)" % (size, room))
    return y + SUB_SLOT_H + 14


def boxes(sheet, left, cy, days, done):
    """`WelcomeScreen.BuildRow` - a kit slot per day, a mint tick in the ones that are in."""
    tick = Image.open(K.UI / "ic_check.png").convert("RGBA")
    for i in range(days):
        cx = left + BOX / 2 + i * (BOX + BOX_GAP)
        lit = i < done
        K.paste(sheet, K.tint(K.skin("Hud/slot", BOX, BOX), (255, 255, 255), 1.0 if lit else .55), cx, cy)
        if lit:
            K.paste(sheet, K.tint(K.fit(tick, (TICK, TICK)), K.MINT), cx, cy - 1)


def bar(sheet, left, cy, fill01, done, days):
    K.paste(sheet, K.skin("Hud/trough", BAR_W, 30), left + BAR_W / 2, cy)
    run = (BAR_W - 8) * fill01
    if run > 1:
        colour = BAR_FULL if fill01 >= 1 else BAR_ORANGE
        K.paste(sheet, K.tint(K.skin("Hud/fill", run, BAR_H), colour), left + 4 + run / 2, cy)
    K.text(sheet, txt("ui.tasks.fraction", done, days), left + BAR_W + 16, cy, 30, anchor="l")


def row(sheet, y, quest, index, state, done):
    """One quest row. `state` is 'counting', 'ready' or 'claimed'."""
    cy = y + ROW_H / 2
    ready, claimed = state == "ready", state == "claimed"
    days = int(quest.get("days", 0))
    tint = SEAT_TINTS[seat(quest)]
    left, right = W / 2 - WIDTH / 2, W / 2 + WIDTH / 2

    if ready:
        pool = K.glow(420, 1.35, K.SUN, .85).resize((int(WIDTH + 150), int(ROW_H + 130)), Image.LANCZOS)
        K.paste(sheet, pool, W / 2, cy)

    card = K.skin("Hud/plate_navy", WIDTH, ROW_H)
    if claimed:
        card = K.tint(card, (170, 175, 190))
    K.paste(sheet, card, W / 2, cy)

    if ready:
        d = ImageDraw.Draw(sheet)
        d.rounded_rectangle([left + 3, cy - ROW_H / 2 + 3, right - 3, cy + ROW_H / 2 - 3],
                            radius=CARD_ROUND, outline=(255, 244, 206, 255), width=7)

    # the seat, its colour on the rim, the turret standing in it
    K.paste(sheet, K.skin("Hud/slot", SEAT, SEAT), left + SEAT_X, cy)
    K.paste(sheet, K.round_rect(SEAT - 10, SEAT - 10, 26, tint, .85, 5), left + SEAT_X, cy)
    if ready:
        K.paste(sheet, K.glow(int(SEAT * 1.9), 2.0, tint, .55), left + SEAT_X, cy)
    try:
        body = Image.open(WARDS / ("%s_%s.png" % (quest.get("ward"), "rgby"[seat(quest)]))).convert("RGBA")
        body = K.fit(body, (BODY_TALL * .8, BODY_TALL))
        if claimed:
            body = K.tint(body, (199, 209, 224))
        K.paste(sheet, body, left + SEAT_X, cy - 4)
    except FileNotFoundError:
        pass

    text_x = left + TEXT_X
    left_line(sheet, txt("ward.%s.name" % quest.get("ward")), text_x, cy - 78, TEXT_W, 40, 22)
    size = left_line(sheet, txt(SENTENCE.get(quest.get("goal"), quest.get("goal")), days), text_x, cy - 28, TEXT_W, 30, 18)
    if size <= 18:
        print("  sentence for %s settled at %dpx against a floor of 18 - it has outgrown the column"
              % (quest.get("id"), size))

    if ready:
        # `CollectSize`, left-anchored where the boxes stood.
        kx = text_x + COLLECT_W / 2
        K.paste(sheet, K.skin("btn_green", COLLECT_W, COLLECT_H), kx, cy + 30)
        K.one_line(sheet, txt("ui.chest.collect"), kx, cy + 30 - COLLECT_H * K.PILL_FACE_LIFT, COLLECT_W - 40, 34, 18,
                   outline=3)
    elif days <= MOST_BOXES:
        boxes(sheet, text_x, cy + 30, days, done)
    else:
        bar(sheet, text_x, cy + 30, done / float(days), done, days)

    # the right end: COLLECTED on its pill once taken; the days to go, plateless, while counting
    px = right + PILL_X
    if claimed:
        label = txt("ui.welcome.taken").upper()
        K.paste(sheet, K.round_rect(PILL_W, PILL_H, 28, (15, 31, 43), .72), px, cy)
        got = K.one_line(sheet, label, px, cy, PILL_W - 28, 28, 16, outline=2)
        if got <= 16:
            print("  pill '%s' settled at %dpx against a floor of 16" % (label, got))
    elif not ready:
        left_n = days - done
        K.one_line(sheet, str(left_n), px, cy - TO_GO_NUMBER_Y, TO_GO_W, TO_GO_NUMBER, 36, fill=MINT, outline=4)
        words = txt("ui.welcome.to_go_one" if left_n == 1 else "ui.welcome.to_go")
        got = K.one_line(sheet, words, px, cy - TO_GO_WORDS_Y, TO_GO_W, TO_GO_WORDS, 18, fill=MINT, outline=3)
        if got <= 18:
            print("  to-go words '%s' settled at %dpx against a floor of 18" % (words, got))

    if claimed:
        d = ImageDraw.Draw(sheet)
        sx, sy = left + SEAT_X + 54, cy + 46
        d.ellipse([sx - 32, sy - 32, sx + 32, sy + 32], fill=K.MINT + (255,))
        try:
            tick = Image.open(K.UI / "ic_check.png").convert("RGBA")
            K.paste(sheet, K.tint(K.fit(tick, (38, 38)), (255, 255, 255)), sx, sy)
        except FileNotFoundError:
            pass


def page(done=False):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.plain(sheet)
    K.rail(sheet, top=True)

    y = 22.0
    y = header(sheet, y)
    print("  rows start at %d from the top of the safe area" % y)

    rows = quests()
    states = ["ready", "counting", "counting", "claimed"]
    for i, quest in enumerate(rows):
        days = int(quest.get("days", 0))
        state = "claimed" if done else states[i % len(states)]
        n = days if state in ("ready", "claimed") else (max(1, days * 2 // 5) if i % 2 else max(0, days - 1))
        row(sheet, y, quest, i, state, n)
        y += ROW_H + ROW_GAP

    K.navbar(sheet, "home")
    print("  page: %d row(s), the last ends %d above the nav bar on the squarest canvas (1890)"
          % (len(rows), 1890 - K.NAV_HEIGHT - (y - ROW_GAP)))
    return sheet.convert("RGB")


# ----------------------------------------------------------------- the sheet
def choice(index=0, view_only=False):
    """`WelcomeChoiceOverlay` on `VictoryFrame`, which is `render_challenges.render_deals`' frame:
    the scrim, the fan, the green window with the crown and banner over it, the line, the turret
    row and the price row, and NOT NOW at the foot. `view_only` is the preview a tap on a counting
    row opens: the days to go where each TAKE key stands, and BACK at the foot."""
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    K.plain(sheet)
    sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .72))))

    quest = quests()[index]
    amount, currency = price_of(quest)
    tint = SEAT_TINTS[seat(quest)]
    floors = []

    rows_h = 2 * (SHEET_ROW_H + SHEET_ROW_GAP) - SHEET_ROW_GAP
    panel_h = ROWS_TOP + rows_h + TAIL + FOOT_H
    reach = panel_h + CREST_REACH
    fit = min(1.0, (H - 20 * 2) / reach)

    block = Image.new("RGBA", (1680, int(reach + 500)), (0, 0, 0, 0))
    bw, bh = block.size
    bcx = bw / 2
    panel_top = bh / 2 - CREST_REACH / 2 - panel_h / 2
    crest_cy = panel_top - CREST_REACH / 2

    fan = K.rays(1680, 14)
    fan_rgba = Image.new("RGBA", fan.size, (255, 204, 77, 0))
    fan_rgba.putalpha(fan.point(lambda v: int(v * .20)))
    K.paste(block, fan_rgba, bcx, crest_cy + 240)
    K.paste(block, K.glow(1240, 2.4, (255, 209, 97), .22), bcx, crest_cy + 200)

    K.paste(block, K.tint(K.skin("Win/window", WIN_W, panel_h), PANEL_INK), bcx, panel_top + panel_h / 2)
    K.paste(block, K.glow(int(WIN_W - 60), 1.9, (255, 245, 209), .11), bcx, panel_top + 70)

    K.paste(block, K.fit(K.load("Win/crown")[0], (180, 162)), bcx, panel_top - CROWN_Y)
    K.paste(block, K.fit(K.load("Win/banner")[0], (BANNER_W, BANNER_H_WIN)), bcx, panel_top - BANNER_Y)
    floors.append(("word", K.shrunk(block, txt("ui.welcome.choose_word").upper(), bcx, panel_top - BANNER_Y - WORD_LIFT,
                                    WORD_W, WORD_H, 58, 32, outline=5), 32))
    floors.append(("line", K.shrunk(block, txt("ui.welcome.choose_line"), bcx, panel_top + LINE_Y, 860, 60, 32, 20,
                                    fill=(255, 255, 255), outline=2), 20))

    y = panel_top + ROWS_TOP + SHEET_ROW_H / 2
    key_cx = bcx + SHEET_ROW_W / 2 - KEY_INSET - KEY_W / 2
    left = bcx - SHEET_ROW_W / 2

    # the turret row
    K.paste(block, K.round_rect(SHEET_ROW_W, SHEET_ROW_H, 28, (0, 0, 0), .32), bcx, y)
    K.paste(block, K.round_rect(SHEET_ROW_W, SHEET_ROW_H, 28, tint, .55, width=3), bcx, y)
    K.paste(block, K.glow(int(STONE * 1.9), 2.2, tint, .22), left + STONE_X, y)
    try:
        body = Image.open(WARDS / ("%s_%s.png" % (quest.get("ward"), "rgby"[seat(quest)]))).convert("RGBA")
        K.paste(block, K.fit(body, (STONE * .8, STONE)), left + STONE_X, y)
    except FileNotFoundError:
        pass
    floors.append(("name", K.shrunk_left(block, txt("ward.%s.name" % quest.get("ward")), left + SHEET_TEXT_X,
                                         y - 60 - 33, SHEET_TEXT_W, 66, 52, 28, fill=K.CREAM, outline=3), 28))
    floors.append(("turret line", K.shrunk_left(block, txt("ui.welcome.choose_turret"), left + SHEET_TEXT_X,
                                                y - LINE_TOP, SHEET_TEXT_W, LINE_H, 36, 20,
                                                fill=(255, 255, 255), outline=2), 20))
    days_to_go(block, quest, key_cx, y, floors) if view_only else take_key(block, "btn_green", key_cx, y, floors)
    y += SHEET_ROW_H + SHEET_ROW_GAP

    # the price row
    money = (255, 116, 212) if currency == "gems" else K.GOLD
    K.paste(block, K.round_rect(SHEET_ROW_W, SHEET_ROW_H, 28, (0, 0, 0), .32), bcx, y)
    K.paste(block, K.round_rect(SHEET_ROW_W, SHEET_ROW_H, 28, money, .55, width=3), bcx, y)
    K.paste(block, K.glow(int(STONE * 1.9), 2.2, money, .22), left + STONE_X, y)
    try:
        glyph = "ic_gem.png" if currency == "gems" else "Coin/f0.png"
        K.paste(block, K.fit(Image.open(K.UI / glyph).convert("RGBA"), (STONE * .78, STONE * .78)), left + STONE_X, y)
    except FileNotFoundError:
        pass
    name = txt("ui.welcome.gems" if currency == "gems" else "ui.welcome.coins", compact(amount))
    floors.append(("price name", K.shrunk_left(block, name, left + SHEET_TEXT_X, y - 60 - 33, SHEET_TEXT_W, 66, 52, 28,
                                               fill=money, outline=3), 28))
    floors.append(("price line", K.shrunk_left(block, txt("ui.welcome.choose_price"), left + SHEET_TEXT_X,
                                               y - LINE_TOP, SHEET_TEXT_W, LINE_H, 36, 20,
                                               fill=(255, 255, 255), outline=2), 20))
    days_to_go(block, quest, key_cx, y, floors) if view_only else take_key(block, "btn_orange", key_cx, y, floors)

    done_cy = panel_top + panel_h - (30 + 55)
    K.paste(block, K.skin("btn_blue", 400, 110), bcx, done_cy)
    foot = txt("ui.common.back" if view_only else "ui.common.cancel").upper()
    floors.append(("not now", K.one_line(block, foot, bcx, done_cy - 4, 340, 36, 18, outline=3), 18))

    if fit < 1.0:
        block = block.resize((int(bw * fit), int(bh * fit)), Image.LANCZOS)
    K.paste(sheet, block, W / 2, H / 2)

    for what, got, floor in floors:
        flag = "  <- AT ITS FLOOR: the string is too long for its box" if got <= floor else ""
        print("  %-12s settled at %2d (floor %d)%s" % (what, got, floor, flag))
    print("  choice: panel %.0f tall (+%.0f crest) fitted at %.2f" % (panel_h, CREST_REACH, fit))
    return sheet.convert("RGB")


def take_key(block, pill, cx, cy, floors):
    K.paste(block, K.skin(pill, KEY_W, KEY_H), cx, cy)
    floors.append(("take key", K.one_line(block, txt("ui.welcome.take").upper(), cx, cy - 4, KEY_W - 40, 36, 20,
                                          outline=3), 20))


def days_to_go(block, quest, cx, cy, floors):
    """`WelcomeChoiceOverlay.DaysToGo` - the page's pill, drawn as if one day were in."""
    left = max(1, int(quest.get("days", 0)) - 1)
    words = txt("ui.welcome.days_left_one") if left == 1 else txt("ui.welcome.days_left", left)
    K.paste(block, K.round_rect(PREVIEW_PILL_W, PREVIEW_PILL_H, 28, (15, 31, 43), .72), cx, cy)
    floors.append(("days to go", K.one_line(block, words, cx, cy, PREVIEW_PILL_W - 28, 30, 16, outline=2), 16))


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--done", action="store_true", help="every quest taken")
    ap.add_argument("--choice", action="store_true", help="the choice sheet over the page")
    ap.add_argument("--preview", action="store_true", help="the choice sheet a tap on a counting row opens")
    ap.add_argument("--row", type=int, default=0, help="which quest the choice sheet is about")
    ap.add_argument("--out", type=Path, default=Path("welcome.png"))
    args = ap.parse_args()

    out = (choice(args.row, view_only=args.preview) if args.choice or args.preview
           else page(done=args.done))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}  {out.width}x{out.height}  - look at it")


if __name__ == "__main__":
    main()
