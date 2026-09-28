# -*- coding: utf-8 -*-
"""Draws the keeper ladder page at the size a phone draws it.

    python Tools/render_keeper_ladder.py                       # a keeper at level 7, three levels bought
    python Tools/render_keeper_ladder.py --standing 12 --bought 0
    python Tools/render_keeper_ladder.py --standing 70          # at the top: the key is shut
    python Tools/render_keeper_ladder.py --unsold               # no ladder published: the key is shut
    python Tools/render_keeper_ladder.py --contact              # five states side by side

**Why this exists.** Every question this page raises is a picture. Does the climb read as a
path rising into the sky, or as a list? Can a reached disc be told from a locked one while
scrolling? Is the crowned disc *the* disc, and is the docked key the first thing the eye lands
on? Does a turret's name fit under its pedestal, and an honorific on the banner under its disc?
No numeric gate here can open a PNG, and the Editor cannot photograph a `ScreenSpaceOverlay`
canvas, so the page is judged here (`CRAFT.md`).

**It reads the shipped content.** The ladder, the turret gates, the lane's wall, the honorific
floors and every string come out of `progression.json`, `manifest.json` and `loc/en.json`, so a
retune redraws rather than going stale (invariant 44d). **Every caption is measured** against
the box it is drawn in, and one settling at its floor is reported (19n). **Every piece of the
climb is checked against every other** - a reward pedestal, its caption, a banner and a disc -
because what this page was rebuilt for is that nothing on it overlaps.

**It does not draw the tweens**: the fans turning, the halo breathing, the beam, the fill easing
in. What it says is whether the page reads at rest.
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
CX = W / 2

CONTENT = K.REPO / "Assets" / "StreamingAssets" / "Content"
LOC = CONTENT / "loc" / "en.json"
TABLE = CONTENT / "progression.json"
MANIFEST = CONTENT / "manifest.json"

# ------------------------------------------------------------------ KeeperScreen, mirrored
CHROME = 92.0
TOP_Y = 22.0
PILL_W, PILL_H = 212.0, 78.0

HERO_TOP = 124.0
EMBLEM = 260.0
EMBLEM_Y = 138.0                 # below HERO_TOP, to the disc's centre
FACE_LIFT = .184                 # the pack disc's face centre above its middle, of its size
FACE_W, FACE_H = .453, .3125     # the face ellipse, of the disc's size
BANNER_W, BANNER_H = 440.0, 118.0
BANNER_Y = 124.0                 # below the disc's centre
BAR_W, BAR_H = 640.0, 58.0
BAR_GAP = 16.0
HERO_FOOT = 18.0

DOCK_H = 172.0
KEY_W, KEY_H = 760.0, 136.0
TAG_W, TAG_H = 262.0, 88.0

CELL_H = 232.0
PAD_TOP, PAD_BOTTOM = 70.0, DOCK_H + 36.0
SWING, TURN = 118.0, 1.05        # the path's serpentine: amplitude and radians a level
NODE, CROWN = 150.0, 200.0
TRACK_W, TRACK_EDGE = 16.0, 32.0
COLUMN_X = 360.0
PEDESTAL_W, PEDESTAL_H = 176.0, 108.0
PEDESTAL_Y = -30.0
PRIZE = 124.0
PRIZE_Y = 26.0
CAPTION_Y = -96.0
CAPTION_ROOM = 220.0
RIBBON_W, RIBBON_H = 280.0, 80.0
RIBBON_Y = -84.0

SKY_TOP, SKY_MID, SKY_BOTTOM = (10, 18, 66), (38, 26, 104), (86, 34, 118)
TRACK_DIM = (70, 62, 140)
SILVER = (222, 226, 240)
DARK_NUMBER = (51, 41, 15)

# KeeperTitle.Floors / Keys
TITLE_FLOORS = ((0, "ui.title.seedling"), (5, "ui.title.sapling"), (10, "ui.title.keeper"),
                (20, "ui.title.warden"), (35, "ui.title.elder"))

TIGHT = []
BOXES = []                       # (what, level, x0, y0, x1, y1) in content space, for the overlap check


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
    """`KeeperScreen.ReadFacts`: what each level opens - prizes for the pedestals, a title for
    the banner - from the roster, the manifest and the honorific floors."""
    out = {level: {"prizes": [], "title": None} for level in range(1, top + 1)}
    for model in (TABLE_JSON.get("wards") or {}).get("models") or []:
        gate = model.get("minLevel", 0)
        if 0 < gate <= top:
            out[gate]["prizes"].append(("Wards/" + model["id"], txt("ward.%s.name" % model["id"])))
    seen = set()
    for chapter in MANIFEST_JSON.get("chapters") or []:
        wall = chapter.get("minKeeperLevel", 0)
        if 0 < wall <= top and wall not in seen and not chapter.get("disabled"):
            seen.add(wall)
            out[wall]["prizes"].append(("ic_endless", txt("chapter.%s.name" % chapter["id"])))
    for level in range(2, top + 1):
        if title_key(level) != title_key(level - 1):
            out[level]["title"] = txt(title_key(level))
    return out


def swing(level):
    """`KeeperScreen.SwingOf`: where a level's disc stands across the page."""
    return SWING * math.sin(level * TURN)


def columns(level, count):
    """`KeeperScreen.ColumnsFor`: one prize stands across the page from the disc's lean, two
    stand either side of the path."""
    lean = 1.0 if swing(level) >= 0 else -1.0
    if count <= 0:
        return []
    if count == 1:
        return [-lean * COLUMN_X]
    return [-lean * COLUMN_X, lean * COLUMN_X]


def one_line(sheet, s, cx, cy, room, size, floor, fill=K.CREAM, outline=2, anchor="c"):
    """`UIKit.OneLineLabel`: the largest size between `floor` and `size` at which the string
    fits `room`. Returns the size, so a caller can report one that settled at the floor."""
    while size > floor and K.font(size).getlength(s) > room:
        size -= 1
    K.text(sheet, s, cx, cy, size, fill=fill, outline=outline, anchor=anchor)
    return size


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


def gradient(w, h):
    """`Art.Gradient(bottom, middle, top)` stretched over the page."""
    im = Image.new("RGBA", (1, h))
    for y in range(h):
        v = 1 - y / (h - 1)
        a, b, t = (SKY_BOTTOM, SKY_MID, v * 2) if v < .5 else (SKY_MID, SKY_TOP, (v - .5) * 2)
        im.putpixel((0, y), tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,))
    return im.resize((w, h))


def stars(sheet):
    """`KeeperScreen.BuildSky`'s scatter: a fixed seed, so every open draws the same sky."""
    import random
    rnd = random.Random(57)
    for _ in range(46):
        x, y = rnd.uniform(20, W - 20), rnd.uniform(20, H - 300)
        s = rnd.uniform(10, 26)
        K.paste(sheet, K.glow(int(s * 2.4), 2.6, (255, 244, 214), rnd.uniform(.35, .85)), x, y)


def disc_face(size):
    """The ellipse laid over the locked disc's padlock, so a locked disc can carry its number."""
    w, h = int(size * FACE_W), int(size * FACE_H)
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(im).ellipse([0, 0, w - 1, h - 1], fill=(252, 252, 252, 255))
    return im


# ------------------------------------------------------------------ chrome
def top_bar(sheet):
    cy = TOP_Y + CHROME / 2
    K.paste(sheet, K.skin("sq_blue", CHROME, CHROME), 76, cy)
    K.paste(sheet, K.tint(icon("ic_left", (46, 46)), K.CREAM), 76, cy)

    title_left, title_right = 76 + CHROME / 2 + 22, W - 40 - PILL_W * 2 - 16 - 20
    TIGHT.append(("page title", one_line(sheet, txt("ui.keeper.page_title").upper(), title_left, cy,
                                         title_right - title_left, 46, 26, fill=K.GOLD, outline=3, anchor="l")))

    money = [(W - 40 - PILL_W / 2, K.BLOOM, "ic_gem", "1,240"),
             (W - 40 - PILL_W * 1.5 - 16, K.GOLD, "Coin/f0", "12,480")]
    for cx, colour, glyph, value in money:
        K.paste(sheet, K.skin("Hud/trough", PILL_W, PILL_H), cx, cy)
        gx = cx - PILL_W / 2 + 46
        K.paste(sheet, K.glow(96, 2.0, colour, .30), gx, cy)
        K.paste(sheet, icon(glyph, (52, 52)), gx, cy)
        K.text(sheet, value, cx + 28, cy, 30)


def hero(sheet, standing, bought, into, need, maxed):
    ey = HERO_TOP + EMBLEM_Y

    fan = K.rays(760, 16)
    fan_rgba = Image.new("RGBA", fan.size, (*K.SUN, 0))
    fan_rgba.putalpha(fan.point(lambda v: int(v * .30)))
    K.paste(sheet, fan_rgba, CX, ey)
    K.paste(sheet, K.glow(560, 1.8, K.SUN, .42), CX, ey)

    disc = icon("keeper_node_crown", (EMBLEM, EMBLEM))
    K.paste(sheet, disc, CX, ey)
    number = str(standing)
    face_y = ey - EMBLEM * FACE_LIFT
    TIGHT.append(("medallion number", one_line(sheet, number, CX, face_y, EMBLEM * FACE_W * .92,
                                               96, 48, fill=DARK_NUMBER, outline=0)))

    if bought > 0:
        chip = txt("ui.keeper.bought_count", bought).upper()
        cw, ch = 196.0, 54.0
        kx, ky = CX + EMBLEM * .56, ey - EMBLEM * .30
        K.paste(sheet, K.round_rect(cw, ch, 27, K.MINT), kx, ky)
        TIGHT.append(("bought chip", one_line(sheet, chip, kx, ky, cw - 24, 26, 16, fill=DARK_NUMBER, outline=0)))

    by = ey + BANNER_Y
    K.paste(sheet, K.skin("Hud/title", BANNER_W, BANNER_H), CX, by)
    TIGHT.append(("honorific", one_line(sheet, txt(title_key(standing)).upper(), CX, by - 10,
                                        BANNER_W * .62, 40, 22)))

    bar_y = by + BANNER_H / 2 + BAR_GAP + BAR_H / 2
    K.paste(sheet, K.skin("Hud/trough", BAR_W, BAR_H), CX, bar_y)
    fill_w = (BAR_W - 12) * (1.0 if maxed else (into / need if need else 1.0))
    if fill_w > 4:
        K.paste(sheet, K.tint(K.skin("Hud/fill", fill_w, BAR_H - 12), K.MINT),
                CX - BAR_W / 2 + 6 + fill_w / 2, bar_y)
    line = txt("ui.keeper.xp_max") if maxed else txt("ui.keeper.xp_short", "{:,}".format(into), "{:,}".format(need))
    TIGHT.append(("xp line", one_line(sheet, line.upper(), CX, bar_y, BAR_W - 40, 30, 18)))
    return bar_y + BAR_H / 2 + HERO_FOOT


def dock(sheet, offer, at_top):
    cy = H - K.NAV_HEIGHT - DOCK_H / 2
    fade = Image.new("RGBA", (W, int(DOCK_H + 60)), (0, 0, 0, 0))
    for y in range(fade.height):
        a = min(1.0, y / (fade.height * .55))
        ImageDraw.Draw(fade).line([(0, y), (W, y)], fill=(*SKY_BOTTOM, int(235 * a)))
    sheet.alpha_composite(fade, (0, int(H - K.NAV_HEIGHT - fade.height)))

    if offer:
        currency, price, level = offer
        K.paste(sheet, K.glow(int(KEY_W * 1.2), 2.0, K.SUN, .40).resize((int(KEY_W * 1.2), int(KEY_H * 2.2))), CX, cy)
        K.paste(sheet, K.skin("btn_violet" if currency == "gems" else "btn_orange", KEY_W, KEY_H), CX, cy)
        lift = KEY_H * .0231
        tag_x = CX + KEY_W / 2 - 22 - TAG_W / 2
        caption_room = KEY_W - 44 - TAG_W - 30
        caption_x = CX - KEY_W / 2 + 22 + caption_room / 2
        TIGHT.append(("buy key", one_line(sheet, txt("ui.keeper.buy", level).upper(), caption_x, cy - lift,
                                          caption_room, 44, 24)))
        K.paste(sheet, K.skin("Hud/trough", TAG_W, TAG_H), tag_x, cy - lift)
        K.paste(sheet, icon("ic_gem" if currency == "gems" else "Coin/f0", (56, 56)), tag_x - TAG_W / 2 + 44, cy - lift)
        TIGHT.append(("price", one_line(sheet, compact(price), tag_x + 26, cy - lift, TAG_W - 96, 40, 22)))
    else:
        K.paste(sheet, K.skin("btn_gray", KEY_W, KEY_H), CX, cy)
        word = txt("ui.keeper.key_top" if at_top else "ui.keeper.key_not_sold").upper()
        TIGHT.append(("shut key", one_line(sheet, word, CX, cy - KEY_H * .0231, KEY_W - 60, 44, 24)))


# ------------------------------------------------------------------ the climb
def cell(board, cy, level, standing, earned, top, fact, lad, offer):
    """`KeeperScreen.LevelCell.Bind`, drawn onto the board at the cell's centre line."""
    x = CX + swing(level)
    reached, crowned = level <= standing, level == standing
    nxt = offer is not None and offer[2] == level

    def track(to_level, lit_colour):
        """Half the way to the neighbouring disc, which draws the other half: a bar pivoted at
        this disc's centre, the edge under the core."""
        tx = CX + swing(to_level)
        ty = cy - CELL_H if to_level > level else cy + CELL_H
        mx, my = (x + tx) / 2, (cy + ty) / 2
        layer = Image.new("RGBA", board.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        for width, colour, alpha in ((TRACK_EDGE, (16, 14, 52), .85), (TRACK_W, lit_colour or TRACK_DIM, 1.0)):
            d.line([(x, cy), (mx, my)], fill=(*colour, int(255 * alpha)), width=int(width))
        board.alpha_composite(layer)

    def segment_colour(upper):
        if upper > standing:
            return None
        return K.MINT if upper > earned else K.GOLD

    if level < top:
        track(level + 1, segment_colour(level + 1))
    if level > 1:
        track(level - 1, segment_colour(level))

    if crowned:
        beam = K.tint(K.skin("Hud/beam", 230, 470), K.SUN, .55)
        K.paste(board, beam, x, cy - 235 + 30)
        fan = K.rays(420, 12)
        fan_rgba = Image.new("RGBA", fan.size, (*K.SUN, 0))
        fan_rgba.putalpha(fan.point(lambda v: int(v * .45)))
        K.paste(board, fan_rgba, x, cy)
    if crowned or nxt:
        K.paste(board, K.glow(340, 1.9, K.SUN, .55 if crowned else .40), x, cy)

    # the prizes
    prizes = fact["prizes"][:2]
    for (sprite, name), px in zip(prizes, columns(level, len(prizes))):
        px = CX + px
        lit = reached or nxt
        K.paste(board, K.glow(260, 2.0, K.SUN, .22 if lit else .08), px, cy + PRIZE_Y)
        ped = K.skin("Hud/lander", PEDESTAL_W, PEDESTAL_H)
        pic = icon(sprite, (PRIZE, PRIZE))
        if not lit:
            ped, pic = K.tint(ped, (150, 156, 196)), K.tint(pic, (150, 156, 196), .9)
        K.paste(board, ped, px, cy - PEDESTAL_Y)
        K.paste(board, pic, px, cy - PRIZE_Y)
        fill = K.CREAM if lit else (190, 196, 226)
        size = one_line(board, name.upper(), px, cy - CAPTION_Y, CAPTION_ROOM, 28, 18, fill=fill)
        TIGHT.append(("L%d prize" % level, size))
        wide = min(CAPTION_ROOM, K.font(size).getlength(name.upper()))
        BOXES.append(("prize", level, px - PEDESTAL_W / 2, cy - PRIZE_Y - PRIZE / 2, px + PEDESTAL_W / 2, cy - PEDESTAL_Y + PEDESTAL_H / 2))
        BOXES.append(("caption", level, px - wide / 2, cy - CAPTION_Y - 18, px + wide / 2, cy - CAPTION_Y + 18))

    # the disc
    size = CROWN if crowned else NODE
    name = "keeper_node_crown" if crowned else "keeper_node_open" if reached else "keeper_node_locked"
    disc = icon(name, (size, size))
    if not reached and not nxt:
        disc = K.tint(disc, (200, 206, 232))
    K.paste(board, disc, x, cy)
    face_y = cy - size * FACE_LIFT
    if not reached:
        K.paste(board, disc_face(size) if nxt else K.tint(disc_face(size), (200, 206, 232)), x, face_y)
    ink = DARK_NUMBER if reached else (70, 76, 104)
    TIGHT.append(("L%d number" % level, one_line(board, str(level), x, face_y, size * FACE_W * .9,
                                                  64 if crowned else 48, 28, fill=ink, outline=0)))
    BOXES.append(("disc", level, x - size / 2, cy - size / 2, x + size / 2, cy + size * .42))

    if reached and level > earned and not crowned:
        got = price_of(lad, level)
        badge = icon("ic_gem" if got and got[0] == "gems" else "Coin/f0", (48, 48))
        K.paste(board, badge, x + size * .36, cy - size * .30)

    if fact["title"]:
        ry = cy - RIBBON_Y
        rib = K.skin("Hud/title", RIBBON_W, RIBBON_H)
        if not reached:
            rib = K.tint(rib, (170, 176, 210))
        K.paste(board, rib, x, ry)
        TIGHT.append(("L%d title" % level, one_line(board, fact["title"].upper(), x, ry - 7, RIBBON_W * .62, 30, 18)))
        BOXES.append(("banner", level, x - RIBBON_W / 2, ry - RIBBON_H / 2, x + RIBBON_W / 2, ry + RIBBON_H / 2))


def overlaps():
    """Every pair of pieces from different kinds that meet on the page. The banner sits on its
    own disc's foot by design, so that pair is allowed."""
    bad = []
    for i, a in enumerate(BOXES):
        for b in BOXES[i + 1:]:
            if {a[0], b[0]} == {"banner", "disc"} and a[1] == b[1]:
                continue
            if a[0] == b[0] == "prize" or (a[1] == b[1] and {a[0], b[0]} == {"prize", "caption"}):
                continue
            if a[2] < b[4] and b[2] < a[4] and a[3] < b[5] and b[3] < a[5]:
                bad.append("%s L%d x %s L%d" % (a[0], a[1], b[0], b[1]))
    return bad


def shot(standing, bought, unsold=False):
    lad = None if unsold else ladder()
    top = max(lad["top"] if lad else standing, standing, 1)
    earned = max(1, standing - bought)
    got = price_of(lad, standing + 1) if lad and standing < top else None
    offer = (got[0], got[1], standing + 1) if got else None
    fx = facts(top)

    sheet = gradient(W, H)
    stars(sheet)
    top_bar(sheet)
    y = hero(sheet, standing, bought, 340, 1250, standing >= 70 and bought == 0)

    # The board: highest level at the top, level 1 at the foot, centred on the standing disc
    # as `OnPresented` scrolls it (`GridView.ScrollTo`).
    view_top, view_bottom = y, H - K.NAV_HEIGHT
    view_h = view_bottom - view_top
    index = top - standing
    want = PAD_TOP + index * CELL_H - max(0, view_h - CELL_H) / 2
    most = max(0, PAD_TOP + top * CELL_H + PAD_BOTTOM - view_h)
    scroll = min(max(want, 0), most)
    board = Image.new("RGBA", (W, int(view_h)), (0, 0, 0, 0))
    for i in range(top):
        level = top - i
        cy = PAD_TOP + i * CELL_H + CELL_H / 2 - scroll
        if cy < -CELL_H or cy > view_h + CELL_H:
            continue
        cell(board, cy, level, standing, earned, top, fx[level], lad, offer)
    sheet.alpha_composite(board, (0, int(view_top)))

    dock(sheet, offer, lad is not None and standing >= top)
    K.navbar(sheet, "home")
    return sheet.convert("RGB")


STATES = (("mid", 7, 3), ("start", 1, 0), ("gems", 12, 0), ("top", 70, 0))


def audit(top):
    """Every level's cell drawn once off-screen, so the overlap check covers the whole climb
    rather than the rows one shot happens to show."""
    lad = ladder()
    fx = facts(top)
    board = Image.new("RGBA", (W, int(PAD_TOP + top * CELL_H + PAD_BOTTOM)), (0, 0, 0, 0))
    del BOXES[:]
    for i in range(top):
        level = top - i
        cell(board, PAD_TOP + i * CELL_H + CELL_H / 2, level, 30, 30, top, fx[level], lad, None)
    return overlaps()


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--standing", type=int, default=7, help="the level the keeper stands at")
    ap.add_argument("--bought", type=int, default=3, help="how many of those were bought")
    ap.add_argument("--unsold", action="store_true", help="no ladder published: the key is shut")
    ap.add_argument("--contact", action="store_true", help="five states side by side")
    ap.add_argument("--out", type=Path, default=Path("out") / "keeper.png")
    args = ap.parse_args()

    if args.contact:
        shots = [shot(s, b) for _, s, b in STATES] + [shot(7, 0, unsold=True)]
        cell_w = 540
        sheet = Image.new("RGB", (cell_w * len(shots), int(cell_w * H / W)), K.GROUND)
        for i, s in enumerate(shots):
            sheet.paste(s.resize((cell_w, int(cell_w * H / W)), Image.LANCZOS), (i * cell_w, 0))
        out = sheet
    else:
        out = shot(args.standing, args.bought, args.unsold)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)

    lad = ladder()
    print("  ladder: %s" % ("levels %d..%d over %d anchor(s)" % (lad["anchors"][0]["level"], lad["top"], len(lad["anchors"])) if lad else "not for sale"))
    clashes = audit(lad["top"] if lad else 70)
    print("  the whole climb: %d overlap(s)%s" % (len(clashes), ": " + ", ".join(clashes[:8]) if clashes else ""))
    floors = [(what, size) for what, size in TIGHT if size is not None and size <= 18]
    print("  %d caption(s) measured, %d at the floor%s" % (len(TIGHT), len(floors), ": " + ", ".join(w for w, _ in floors[:6]) if floors else ""))
    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))
    return 1 if clashes else 0


if __name__ == "__main__":
    sys.exit(main())
