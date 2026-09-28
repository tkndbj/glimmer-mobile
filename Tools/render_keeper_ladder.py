# -*- coding: utf-8 -*-
"""Draws the keeper ladder page at the size a phone draws it.

    python Tools/render_keeper_ladder.py                       # a keeper at level 7, three levels bought
    python Tools/render_keeper_ladder.py --standing 12 --bought 0
    python Tools/render_keeper_ladder.py --standing 70          # at the top: no key, no price
    python Tools/render_keeper_ladder.py --unsold               # no ladder published: the key is shut
    python Tools/render_keeper_ladder.py --contact              # the four states side by side

**Why this exists.** Every question this page raises is a picture. Does a row read as a node
on a spine, or as a list? Can a reached level be told from a locked one while scrolling? Is the
crowned row *the* row, and is the buy key on the next one the first thing the eye lands on? Does
"Unlocks Mortar" and its thumbnail fit beside a level number at the size a phone draws it, and
does a price with a coin fit the key's foot? No numeric gate here can open a PNG, and the Editor
cannot photograph a `ScreenSpaceOverlay` canvas, so the page is judged here (`CRAFT.md`).

**It reads the shipped content.** The ladder, the turret gates, the lane's wall, the honorific
floors and every string come out of `progression.json`, `manifest.json` and `loc/en.json`, so a
retune redraws rather than going stale (invariant 44d). **Every caption is measured** against
the box it is drawn in, and one settling at its floor is reported (19n).

**It does not draw the tweens**: the ring turning, the pool breathing, the fill easing in. What it
says is whether the page reads at rest.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image                                       # noqa: E402
import hudkit as K                                          # noqa: E402

W, H = K.W, K.H

CONTENT = K.REPO / "Assets" / "StreamingAssets" / "Content"
LOC = CONTENT / "loc" / "en.json"
TABLE = CONTENT / "progression.json"
MANIFEST = CONTENT / "manifest.json"

# KeeperScreen
CHROME = 92.0
BANNER_H = 138.0
HERO_H = 300.0
NOTE_H = 34.0
HEADING_H = 62.0
WIDTH = 1000.0
ROW_H, ROW_GAP = 156.0, 14.0
CELL_H = ROW_H + ROW_GAP
SPINE_X, SPINE_W, NODE = -410.0, 14.0, 128.0
PLATE_LEFT, PLATE_RIGHT = -330.0, 500.0
PLATE_W, PLATE_X = PLATE_RIGHT - PLATE_LEFT, (PLATE_LEFT + PLATE_RIGHT) / 2
KEY_W, KEY_H, CHIP_W, CHIP_H = 250.0, 84.0, 190.0, 46.0

# KeeperTitle.Floors / Keys
TITLE_FLOORS = ((0, "ui.title.seedling"), (5, "ui.title.sapling"), (10, "ui.title.keeper"),
                (20, "ui.title.warden"), (35, "ui.title.elder"))

TIGHT = []


def strings():
    table = json.loads(LOC.read_text(encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


LOCS = strings()
TABLE_JSON = json.loads(TABLE.read_text(encoding="utf-8"))
MANIFEST_JSON = json.loads(MANIFEST.read_text(encoding="utf-8"))


def txt(key, *args):
    s = LOCS.get(key, key)
    for i, a in enumerate(args):
        s = s.replace("{%d}" % i, str(a))
    return s


def title_key(level):
    key = TITLE_FLOORS[0][1]
    for floor, k in TITLE_FLOORS:
        if level >= floor:
            key = k
    return key


# ------------------------------------------------------------------ the rule, mirrored
def ladder():
    block = TABLE_JSON.get("keeperLevels")
    if not block:
        return None
    return {"top": block["top"], "anchors": block["anchors"]}


def between(a, b, step, steps):
    if steps <= 0:
        return a
    return a + (2 * (b - a) * step + steps) // (2 * steps)


def price_of(lad, level):
    if lad is None:
        return None
    rows = lad["anchors"]
    if level < rows[0]["level"] or level > lad["top"]:
        return None
    i = 0
    while i + 1 < len(rows) and rows[i + 1]["level"] <= level:
        i += 1
    lower = rows[i]
    if i + 1 >= len(rows) or lower["level"] == level:
        return lower["currency"], lower["price"]
    upper = rows[i + 1]
    if lower["currency"] != upper["currency"]:
        return lower["currency"], lower["price"]
    return lower["currency"], between(lower["price"], upper["price"],
                                      level - lower["level"], upper["level"] - lower["level"])


def facts(top):
    """`KeeperScreen.ReadFacts`: what each level opens, from the roster, the manifest, the titles."""
    out = {level: {"lines": [], "thumb": None} for level in range(1, top + 1)}
    for model in (TABLE_JSON.get("wards") or {}).get("models") or []:
        gate = model.get("minLevel", 0)
        if 0 < gate <= top:
            out[gate]["lines"].append(txt("ui.keeper.unlocks_turret", txt("ward.%s.name" % model["id"])))
            if out[gate]["thumb"] is None:
                out[gate]["thumb"] = "Wards/" + model["id"]
    seen = set()
    for chapter in MANIFEST_JSON.get("chapters") or []:
        wall = chapter.get("minKeeperLevel", 0)
        if 0 < wall <= top and wall not in seen and not chapter.get("disabled"):
            seen.add(wall)
            out[wall]["lines"].append(txt("ui.keeper.unlocks_chapter", txt("chapter.%s.name" % chapter["id"])))
    for level in range(2, top + 1):
        if title_key(level) != title_key(level - 1):
            out[level]["lines"].append(txt("ui.keeper.title_tier", txt(title_key(level))))
    return out




def one_line(sheet, s, cx, cy, room, size, floor, fill=K.CREAM, outline=2, anchor="c"):
    """`UIKit.OneLineLabel` for a caption that may be left-aligned: the largest size between
    `floor` and `size` at which the string fits `room`, drawn at that size. Returns the size, so a
    caller can report one that settled at the floor (19n)."""
    while size > floor and K.font(size).getlength(s) > room:
        size -= 1
    K.text(sheet, s, cx, cy, size, fill=fill, outline=outline, anchor=anchor)
    return size

# ------------------------------------------------------------------ pieces
def icon(name, box):
    im, _ = K.load(name)
    return K.fit(im, box)


def compact(n):
    """`Compact.Number` - as the pills print it."""
    if n >= 1_000_000:
        return "%.1fM" % (n / 1_000_000)
    if n >= 10_000:
        return "%.1fK" % (n / 1_000)
    return "{:,}".format(n)


def header(sheet, y):
    cy = y + BANNER_H / 2
    K.paste(sheet, K.skin("sq_blue", CHROME, CHROME), 76, cy)
    K.paste(sheet, K.tint(icon("ic_left", (46, 46)), K.CREAM), 76, cy)

    ribbon = K.skin("ribbon_orange", 720, BANNER_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.text(plate, txt("ui.keeper.page_title").upper(), plate.width / 2, plate.height / 2 - 6, 42, fill=K.CREAM)
    K.paste(sheet, plate.rotate(1.6, resample=Image.BICUBIC, expand=False), W / 2, cy)
    y += BANNER_H + 4

    K.shrunk(sheet, txt("ui.keeper.subtitle"), W / 2, y + 16, 880, 32, 24, 15, fill=(219, 229, 255), outline=0)
    y += 32 + 14

    money = [(-232, K.ROSE, "ic_heart", "5/5"), (0, K.GOLD, "Coin/f0", "12,480"),
             (232, K.BLOOM, "ic_gem", "1,240")]
    for dx, colour, glyph, value in money:
        cx = W / 2 + dx
        K.paste(sheet, K.skin("Hud/trough", 212, 78), cx, y + CHROME / 2)
        gx = cx - 106 + 52
        K.paste(sheet, K.glow(96, 2.0, colour, .30), gx, y + CHROME / 2)
        K.paste(sheet, icon(glyph, (52, 52)), gx, y + CHROME / 2)
        K.text(sheet, value, cx + 24, y + CHROME / 2, 30)
    return y + CHROME + 16


def hero(sheet, y, standing, earned, bought, offer, into, need):
    cy = y + HERO_H / 2
    left = W / 2 - WIDTH / 2
    K.paste(sheet, K.skin("Hud/panel", WIDTH, HERO_H), W / 2, cy)

    # the medallion
    mx, my = left + 130, cy - 12
    K.paste(sheet, K.glow(300, 2.1, K.GOLD, .30), mx, my)
    ring = K.round_rect(196, 196, 98, K.GOLD, width=14)
    K.paste(sheet, ring, mx, my)
    K.paste(sheet, K.round_rect(164, 164, 82, K.GOLD), mx, my)
    K.text(sheet, str(standing), mx, my - 2, 72, fill=(77, 51, 13), outline=0)
    K.shrunk(sheet, txt(title_key(standing)), mx, cy + 112, 260, 34, 26, 16, fill=K.GOLD, outline=2)

    # the bar
    bar_left, bar_right = left + 260, left + 690
    bar_w, bar_x = bar_right - bar_left, (bar_left + bar_right) / 2
    K.text(sheet, txt("ui.keeper.level_heading").upper(), bar_left, cy - 88, 26, fill=K.GOLD, outline=2, anchor="l")
    K.paste(sheet, K.skin("Hud/trough", bar_w, 40), bar_x, cy - 34)
    fill_w = (bar_w - 12) * (into / need if need else 1.0)
    if fill_w > 4:
        K.paste(sheet, K.tint(K.skin("Hud/fill", fill_w, 30), K.MINT), bar_left + 5 + fill_w / 2, cy - 34)
    xp_line = txt("ui.profile.xp", "{:,}".format(into), "{:,}".format(need))
    TIGHT.append(("xp line", one_line(sheet, xp_line, bar_left, cy + 6, bar_w, 22, 14, fill=(219, 229, 255), anchor="l")))
    bought_line = txt("ui.keeper.bought_n", bought) if bought > 0 else txt("ui.keeper.bought_none")
    TIGHT.append(("bought line", one_line(sheet, bought_line, bar_left, cy + 40, bar_w, 22, 14, fill=K.GOLD, anchor="l")))

    # the key
    kx = W / 2 + WIDTH / 2 - 40 - KEY_W / 2
    if offer:
        currency, price, level = offer
        K.paste(sheet, K.skin("btn_violet" if currency == "gems" else "btn_orange", KEY_W, 96), kx, cy - 22)
        TIGHT.append(("buy key", one_line(sheet, txt("ui.keeper.buy", level).upper(), kx, cy - 22 - 96 * .0231, KEY_W - 40, 28, 16)))
        K.text(sheet, compact(price), kx - 22, cy + 48, 30)
        K.paste(sheet, icon("ic_gem" if currency == "gems" else "Coin/f0", (40, 40)), kx + KEY_W / 2 - 18 - 20, cy + 48)
    else:
        K.paste(sheet, K.skin("btn_gray", KEY_W, 96), kx, cy - 22)
        word = txt("ui.keeper.key_top" if standing >= (ladder() or {"top": 0})["top"] and ladder() else "ui.keeper.key_not_sold")
        TIGHT.append(("shut key", one_line(sheet, word.upper(), kx, cy - 22 - 96 * .0231, KEY_W - 40, 28, 16)))
    return y + HERO_H + 10


def note(sheet, y):
    TIGHT.append(("rank note", one_line(sheet, txt("ui.keeper.rank_note"), W / 2, y + NOTE_H / 2, WIDTH - 40, 22, 14,
                                          fill=(219, 229, 255), outline=0)))
    return y + NOTE_H + 6


def headings(sheet, y, top):
    cy = y + HEADING_H / 2
    K.text(sheet, txt("ui.keeper.ladder_heading").upper(), W / 2 - WIDTH / 2 + 20, cy, 30, fill=K.GOLD, anchor="l")
    K.text(sheet, txt("ui.keeper.to_top", top), W / 2 + WIDTH / 2 - 20, cy, 24, fill=(219, 229, 255), outline=2, anchor="r")
    return y + HEADING_H


def row(sheet, cy, level, standing, earned, top, fact, offer, lad):
    reached = level <= standing
    crowned = level == standing
    nxt = level == standing + 1
    cx = W / 2

    lit, dim = (*K.GOLD, 242), (255, 255, 255, 26)
    sx = cx + SPINE_X
    if level > 1:
        seg = Image.new("RGBA", (int(SPINE_W), int(CELL_H / 2)), lit if reached else dim)
        sheet.alpha_composite(seg, (int(sx - SPINE_W / 2), int(cy - CELL_H / 2)))
    if level < top:
        seg = Image.new("RGBA", (int(SPINE_W), int(CELL_H / 2)), lit if level < standing else dim)
        sheet.alpha_composite(seg, (int(sx - SPINE_W / 2), int(cy)))

    px = cx + PLATE_X
    if nxt:
        K.paste(sheet, K.glow(420, 1.35, K.SUN, .34).resize((int(PLATE_W + 160), int(ROW_H + 140)), Image.LANCZOS), px, cy)
    plate = K.skin("Hud/plate_navy", PLATE_W, ROW_H)
    if not (reached or nxt):
        plate = K.tint(plate, (184, 194, 214), .92)
    K.paste(sheet, plate, px, cy)

    disc = "keeper_node_crown" if crowned else "keeper_node_open" if reached else "keeper_node_locked"
    K.paste(sheet, icon(disc, (NODE, NODE)), sx, cy)
    K.text(sheet, str(level), sx, cy - 16, 34, fill=(51, 41, 15) if reached else (242, 242, 250), outline=0)

    plate_left = px - PLATE_W / 2
    thumb = fact["thumb"]
    text_x = plate_left + 36 + 220 if thumb else plate_left + 36 + 160
    if thumb:
        im = icon(thumb, (84, 84))
        if not (reached or nxt):
            im = K.tint(im, (255, 255, 255), .55)
        K.paste(sheet, im, plate_left + 36 + 42, cy)

    title_fill = K.SUN if crowned else K.GOLD if reached else (204, 214, 235)
    K.text(sheet, txt("ui.keeper.level_n", level).upper(), text_x, cy - 42, 30, fill=title_fill, anchor="l")

    lines = list(fact["lines"])
    ink = K.CREAM if reached else (204, 214, 235)
    if not lines:
        if reached:
            lines = [txt("ui.keeper.reached")]
        else:
            got = price_of(lad, level)
            lines = [txt("ui.keeper.price_gems" if got[0] == "gems" else "ui.keeper.price_coins", compact(got[1]))
                     if got else txt("ui.keeper.earn_it")]
        ink = (219, 229, 255)
    for i, line in enumerate(lines[:3]):
        TIGHT.append(("row %d line %d" % (level, i + 1),
                      one_line(sheet, line, text_x, cy - 8 + i * 28, 440, 22, 13, fill=ink, anchor="l")))

    right = px + PLATE_W / 2
    if nxt and offer and offer[2] == level:
        currency, price, _ = offer
        kx = right - 30 - KEY_W / 2
        K.paste(sheet, K.skin("btn_violet" if currency == "gems" else "btn_orange", KEY_W, KEY_H), kx, cy - 12)
        TIGHT.append(("row key", one_line(sheet, txt("ui.keeper.buy_short").upper(), kx, cy - 12 - KEY_H * .0231, KEY_W - 40, 24, 14)))
        K.text(sheet, compact(price), right - 30 - 46, cy + 46, 22, anchor="r")
        K.paste(sheet, icon("ic_gem" if currency == "gems" else "Coin/f0", (30, 30)), right - 30 - 20, cy + 46)
    elif crowned or (earned < level < standing):
        word = txt("ui.keeper.here" if crowned else "ui.keeper.bought_chip").upper()
        chip = K.round_rect(CHIP_W, CHIP_H, 23, K.SUN if crowned else K.MINT)
        K.paste(sheet, chip, right - 30 - CHIP_W / 2, cy)
        TIGHT.append(("chip", one_line(sheet, word, right - 30 - CHIP_W / 2, cy, CHIP_W - 16, 20, 12,
                                         fill=(77, 51, 13), outline=0)))


def shot(standing, bought, unsold=False):
    lad = None if unsold else ladder()
    top = max(lad["top"] if lad else standing, standing, 1)
    earned = max(1, standing - bought)
    got = price_of(lad, standing + 1) if lad and standing < top else None
    offer = (got[0], got[1], standing + 1) if got else None
    fx = facts(top)

    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.plain(sheet)
    K.rail(sheet, top=True)

    y = 22.0
    y = header(sheet, y)
    y = hero(sheet, y, standing, earned, bought, offer, 340, 1250)
    y = note(sheet, y)
    y = headings(sheet, y, top)

    # The board, centred on the standing row as `OnPresented` scrolls it (`GridView.ScrollTo`).
    view_top, view_bottom = y, H - K.NAV_HEIGHT - 16
    view_h = view_bottom - view_top
    want = 6 + (standing - 1) * CELL_H - max(0, view_h - CELL_H) / 2
    most = max(0, 6 + top * CELL_H + 30 - view_h)
    scroll = min(max(want, 0), most)
    board = Image.new("RGBA", (W, int(view_h)), (0, 0, 0, 0))
    for level in range(1, top + 1):
        cy = 6 + (level - 1) * CELL_H + CELL_H / 2 - scroll
        if cy < -CELL_H or cy > view_h + CELL_H:
            continue
        row(board, cy, level, standing, earned, top, fx[level], offer, lad)
    sheet.alpha_composite(board, (0, int(view_top)))

    K.navbar(sheet, "home")
    return sheet.convert("RGB")


STATES = (("mid", 7, 3), ("start", 1, 0), ("gems", 12, 0), ("top", 70, 0))


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--standing", type=int, default=7, help="the level the keeper stands at")
    ap.add_argument("--bought", type=int, default=3, help="how many of those were bought")
    ap.add_argument("--unsold", action="store_true", help="no ladder published: the key is shut")
    ap.add_argument("--contact", action="store_true", help="four states side by side")
    ap.add_argument("--out", type=Path, default=Path("out") / "keeper.png")
    args = ap.parse_args()

    if args.contact:
        shots = [shot(s, b) for _, s, b in STATES] + [shot(7, 0, unsold=True)]
        cell = 540
        sheet = Image.new("RGB", (cell * len(shots), int(cell * H / W)), K.GROUND)
        for i, s in enumerate(shots):
            sheet.paste(s.resize((cell, int(cell * H / W)), Image.LANCZOS), (i * cell, 0))
        out = sheet
    else:
        out = shot(args.standing, args.bought, args.unsold)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)

    lad = ladder()
    print("  ladder: %s" % ("levels %d..%d over %d anchor(s)" % (lad["anchors"][0]["level"], lad["top"], len(lad["anchors"])) if lad else "not for sale"))
    print("  a row is %.0fx%.0f; the node is %.0f on a spine at x=%.0f; the plate runs %.0f..%.0f" % (WIDTH, ROW_H, NODE, SPINE_X, PLATE_LEFT, PLATE_RIGHT))
    floors = [(what, size) for what, size in TIGHT if size is not None and size <= 14]
    print("  %d caption(s) measured, %d at the floor%s" % (len(TIGHT), len(floors), ": " + ", ".join(w for w, _ in floors[:6]) if floors else ""))
    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    main()
