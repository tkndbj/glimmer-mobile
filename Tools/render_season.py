# -*- coding: utf-8 -*-
"""Draws The First Watch season page at the size a phone draws it.

    python Tools/render_season.py                # the page, pass not held
    python Tools/render_season.py --owned        # with the pass unlocked
    python Tools/render_season.py --contact      # both, side by side

**Why this exists.** Every question this page raises is a picture. Do two chests on a card read
as two rewards for one rung or as clutter; is the locked column obviously locked rather than
merely dim; does the hero's mark count read as the thing the page is scored on; is a rung that
can be opened findable in a list of forty. No numeric gate in this project can open a PNG and
the Editor cannot photograph a `ScreenSpaceOverlay` canvas, so the page is judged here the way
every board is (`CRAFT.md`).

**It shares `hudkit` with `render_tasks.py` and `render_home.py`**, which is invariant 44d: two
screens made of the same furniture get one mirror, because two copies would be two answers to
questions this project settled once. The season page *is* the tasks page's furniture — the same
plates, the same ribbon, the same wallet row, the same chest art — so almost nothing here is
new geometry, and what is new is the rung card.

**It reads the shipped content.** The ladder, the tiers and the copy come out of
`manifest.json`, `progression.json` and `loc/en.json`, so a retune redraws rather than going
stale — a mirror with its own numbers answers questions about a screen the game does not draw.

**It does not draw the tweens.** What it says about a rung that is ready is whether the light is
visible at all against the kit's navy, not whether it breathes well.
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
MANIFEST = CONTENT / "manifest.json"
OUT = K.REPO / "Tools" / "out"

# EventScreen
CHROME = 92.0
BANNER_H = 138.0
HERO_H = 240.0
PASS_H = 176.0
HEADING_H = 62.0
WIDTH = 1000.0

# SeasonLadder
ROW_H = 192.0
ROW_GAP = 14.0
FREE_X = 20.0
PASS_X = 310.0
SPLIT_X = (FREE_X + PASS_X) / 2
DISC_X = -404.0
GOAL_X = -330.0

#: `SeasonLadder.ChestTall` - a *drawn* height, which is the only thing this file can honestly
#: mirror: it crops the closed icon to its own alpha before drawing it, so it has always drawn
#: the picture the screen was *meant* to draw and could not see that the screen was hanging the
#: whole sprite box centred instead (invariant 48j, the same trap from the other end). Now the
#: screen converts through `ChestPack` and the two agree on purpose rather than by accident.
CHEST_TALL = 122.0

#: `SeasonLadder.StampAt` - the seal and the padlock, off the drawn chest rather than typed.
STAMP_X, STAMP_Y = CHEST_TALL * .46, CHEST_TALL * .42

# EventScreen.BarOrange / BarFull / BarH — the tasks page's numbers, deliberately shared.
BAR_ORANGE = (255, 150, 30)
BAR_FULL = (96, 235, 70)
BAR_H = 26

NAVBAR_H = 190.0

#: `EventScreen.TextX` / `HintW` — where the pass row's two lines start, and the room they
#: have between the crest and the button. Both moved when the plate grew to 176.
TEXT_X = 200.0
HINT_W = 470.0

#: The hint's box is two lines tall on purpose — `UIKit.Shrinkable` wraps and then truncates, so
#: a one-line box shrinks a long sentence instead of widening it.
HINT_H = 64.0


def strings():
    return {e["key"]: e["text"] for e in json.loads(LOC.read_text(encoding="utf-8"))["entries"]}


STR = strings()
TASKS = json.loads(TABLE.read_text(encoding="utf-8"))["tasks"]
#: The season the page draws, and which cycle of it.
#:
#: **The shipped season repeats** (`SeasonCycle`), so the manifest holds a *stem* and a window
#: describing cycle nought rather than a dated entry. The ladder, the pass price and the window
#: length are the same on every cycle - which is the whole point of a recurrence - so the mirror
#: draws cycle `CYCLE` and takes its name out of the pool, exactly as the screen does. Drawing
#: a fixed `ui.event.<id>.name` was right until the id stopped being a thing anybody writes.
SEASON = next(e for e in json.loads(MANIFEST.read_text(encoding="utf-8"))["events"]
              if not e.get("disabled"))

#: Which cycle to draw. Only the *name* changes with it, so this is a knob for looking at the
#: pool rather than a parameter of the page.
CYCLE = 0

#: `SeasonCycle.NamePoolSize` - mirrored, because a render that names the season out of a key
#: the game does not use is a render of a screen that does not exist (invariant 44d).
NAME_POOL = 12


def season_name_key(cycle=CYCLE):
    """`SeasonCycle.NameKeyFor` - the pool slot a cycle takes its name from."""
    return "ui.season.%d.name" % (cycle % NAME_POOL) if SEASON.get("repeats")         else "ui.event.%s.name" % SEASON["id"]


def txt(key, *args):
    s = STR.get(key, "<%s>" % key)
    for i, a in enumerate(args):
        s = s.replace("{%d}" % i, str(a))
    return s


def chest_art():
    """tier id -> (sprite, its own alpha box). The closed icon carries the lid's headroom."""
    out = {}
    for tier in TASKS["tiers"]:
        im = Image.open(K.UI / "Chest" / ("%s.png" % tier["id"])).convert("RGBA")
        out[tier["id"]] = (im, im.getchannel("A").getbbox())
    return out


ART = chest_art()


def chest(tier, tall, face):
    """One chest at one of the three faces a cell draws. `SeasonLadder.Paint`'s tints.

    `tall` is the *drawn* height. The width comes off the art's own alpha box rather than a
    second constant, so a re-cut at another aspect cannot leave a number here describing the
    last one (invariant 44b).
    """
    im, box = ART[tier]
    wide = tall * (box[2] - box[0]) / float(box[3] - box[1])
    out = im.crop(box).resize((max(1, int(round(wide))), max(1, int(round(tall)))), Image.LANCZOS)

    if face == "ready":
        return out
    if face == "sealed":
        return K.tint(out, (189, 199, 214))
    return K.tint(out, (158, 168, 189))


# ------------------------------------------------------------------- the page
def header(sheet, y):
    """`EventScreen.BuildHeader` — the name first, then what it says, then the wallet."""
    cy = y + BANNER_H / 2
    K.paste(sheet, K.skin("sq_blue", CHROME, CHROME), 76, cy)
    K.paste(sheet, K.skin("sq_orange", CHROME, CHROME), W - 76, cy)
    for x, glyph in ((76, "ic_left"), (W - 76, "ic_info")):
        K.paste(sheet, K.tint(K.fit(Image.open(K.UI / ("%s.png" % glyph)).convert("RGBA"), (46, 46)),
                              K.CREAM), x, cy)

    ribbon = K.skin("Hud/title", 720, BANNER_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.text(plate, txt(season_name_key()).upper(), plate.width / 2,
           plate.height / 2 - 6, 42, fill=K.SUN)
    K.paste(sheet, plate, W / 2, cy)
    y += BANNER_H + 4

    K.text(sheet, txt("ui.mark.subtitle"), W / 2, y + 16, 24, fill=(219, 229, 255), outline=0)
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


def season_crest(size):
    """`SeasonCrest.PaintCrest` — the bought crown, sized to its host with `preserveAspect`.

    **This generated a ring of twelve pips for as long as the screen did**, which was a
    faithful mirror of a crest the owner then rejected on sight; the picture is a sprite now
    (`Tools/make_season_crest.py`) and so this loads it, which is the only version of this
    function that cannot come to disagree with the screen (invariant 44d). It takes no
    progress, because the crest no longer carries any — the bar under it does.
    """
    mark = Image.open(K.UI / "ic_season.png").convert("RGBA")
    return K.fit(mark, (int(size), int(size)))


def hero(sheet, y, marks, rungs, top):
    """`EventScreen.BuildHero` — the mark, the count, the run to the next rung and the clock."""
    cy = y + HERO_H / 2
    K.paste(sheet, K.skin("Hud/panel", WIDTH, HERO_H), W / 2, cy)

    left = W / 2 - WIDTH / 2
    fan = K.rays(256, 14).resize((760, 760), Image.LANCZOS)
    lit = Image.new("RGBA", fan.size, (*K.SUN, 0))
    lit.putalpha(fan.point(lambda v: int(v * .16)))

    band = Image.new("RGBA", (int(WIDTH) - 12, int(HERO_H) - 12), (0, 0, 0, 0))
    band.alpha_composite(lit, (150 - 380, band.height // 2 - 380 - 10))
    K.paste(sheet, band, W / 2, cy)

    K.paste(sheet, season_crest(150), left + 132, cy - 14)

    K.text(sheet, txt("ui.mark.grown", "{:,}".format(marks)), left + 236, cy - 34, 58,
           anchor="l")

    ahead = next((r["goal"] for r in SEASON["milestones"] if r["goal"] > marks), 0)
    K.text(sheet, txt("ui.mark.to_next", ahead - marks) if ahead else txt("ui.mark.complete"),
           left + 236, cy + 12, 24, fill=(255, 243, 220), outline=0, anchor="l")

    behind = max([0] + [r["goal"] for r in SEASON["milestones"] if r["goal"] <= marks])
    span = (ahead - behind) or 1
    fill01 = 1.0 if not ahead else max(0.0, min(1.0, (marks - behind) / float(span)))

    rungs_w = 232.0
    rx = left + WIDTH - rungs_w / 2 - 26
    K.paste(sheet, K.skin("Hud/trough", rungs_w, 104), rx, cy + 22)
    K.text(sheet, "%d / %d" % (rungs, len(SEASON["milestones"])), rx, cy + 8, 40, fill=K.GOLD)
    K.text(sheet, txt("ui.mark.rungs"), rx, cy + 48, 21, fill=(255, 243, 220), outline=0)

    track = 460.0
    K.paste(sheet, K.skin("Hud/trough", track, 30), left + 236 + track / 2, cy + 52)
    run = (track - 8) * fill01
    if run > 1:
        K.paste(sheet, K.tint(K.skin("Hud/fill", run, BAR_H),
                              BAR_FULL if fill01 >= 1 else BAR_ORANGE),
                left + 236 + 4 + run / 2, cy + 52)

    pill = Image.new("RGBA", (300, 50), (0, 0, 0, 0))
    d = ImageDraw.Draw(pill)
    d.rounded_rectangle((0, 0, 299, 49), 24, fill=(13, 23, 46, 200))
    K.text(pill, txt("ui.event.ends_in", "12d 4h"), 160, 25, 22, fill=(255, 243, 220), outline=0)
    K.paste(sheet, pill, W / 2 + WIDTH / 2 - 28 - 150, cy - HERO_H / 2 + 26)

    return y + HERO_H + 14


def pass_banner(sheet, y, owned):
    """`EventScreen.BuildPassBanner` — an offer while it is not held, a strip once it is."""
    cy = y + PASS_H / 2
    K.paste(sheet, K.skin("Hud/plate_violet", WIDTH, PASS_H), W / 2, cy)

    left = W / 2 - WIDTH / 2
    K.paste(sheet, K.glow(128, 2.0, K.BLOOM, .22), left + 116, cy)
    K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "Hud" / "burst.png").convert("RGBA"), (128, 128)),
                          K.GOLD), left + 116, cy)
    K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_gem.png").convert("RGBA"), (58, 58)),
                          K.CREAM), left + 116, cy)

    K.text(sheet, txt("ui.mark.pass"), left + TEXT_X, cy - 38, 38,
           fill=K.MINT if owned else K.CREAM, anchor="l")

    # Wrapped and best-fitted exactly as `UIKit.Shrinkable` does it, rather than drawn at the
    # authored size, because the room between the crest and the button is the whole question
    # here: a mirror that let the line run under the button would be answering it about a
    # screen the game does not draw (invariant 44d), and a Unity label that overflows is not
    # clipped and nothing says so (37n). The settled size is printed for the same reason the
    # connect banner prints its own — a sentence sitting on its floor is one push from being
    # truncated.
    px = K.shrunk_left(sheet, txt("ui.mark.owned_hint" if owned else "ui.mark.purchase_hint"),
                       left + TEXT_X, cy, HINT_W, HINT_H, 26, 17,
                       fill=(255, 243, 220), outline=0)
    print("  pass hint: settled at %dpx against a floor of 17" % px)

    btn = K.skin("btn_violet", 300, 104)
    plate = Image.new("RGBA", btn.size, (0, 0, 0, 0))
    plate.alpha_composite(btn)
    # The caption and its trailing glyph, in both states: a gem on a price, a tick on the
    # word that replaces one. See `EventScreen.RefreshPass`.
    if owned:
        caption, mark = txt("ui.mark.unlocked"), "ic_check.png"
        glyph = 35
        run = K.font(38).getlength(caption) + 10 + glyph
        left = plate.width / 2 - run / 2
        K.text(plate, caption, left, plate.height / 2 - 6, 38, anchor="l")
        K.paste(plate, K.tint(K.fit(Image.open(K.UI / mark).convert("RGBA"), (glyph, glyph)),
                              K.CREAM), left + run - glyph / 2, plate.height / 2 - 6)
    else:
        # The caption and its trailing gem, as `Btn.IconTrails` lays them out: the pair is
        # centred together, so the words sit left of middle by half the glyph and its gap.
        price = txt("ui.mark.buy", "{:,}".format(SEASON.get("passGems", 0)))
        glyph = 35
        run = K.font(38).getlength(price) + 10 + glyph
        left = plate.width / 2 - run / 2
        K.text(plate, price, left, plate.height / 2 - 6, 38, anchor="l")
        K.paste(plate, K.fit(Image.open(K.UI / "ic_gem.png").convert("RGBA"), (glyph, glyph)),
                left + run - glyph / 2, plate.height / 2 - 6)
    K.paste(sheet, plate, W / 2 + WIDTH / 2 - 166, cy)

    return y + PASS_H + 14


def headings(sheet, y):
    """`EventScreen.BuildHeadings` — said once above the list, not on forty cards."""
    cy = y + HEADING_H / 2
    K.text(sheet, txt("ui.mark.free").upper(), W / 2 + FREE_X, cy, 24, fill=K.MINT)
    K.text(sheet, txt("ui.mark.pass").upper(), W / 2 + PASS_X, cy, 24, fill=K.BLOOM)
    return y + HEADING_H


def rung_card(sheet, y, index, rung, marks, owned):
    """One rung. `SeasonLadder.BuildRow` and `Paint`."""
    cy = y + ROW_H / 2
    reached = marks >= rung["goal"]
    left = W / 2 - WIDTH / 2

    # The first reached rung on the sheet is drawn already opened, so the contact sheet
    # carries all three faces rather than only the two a fresh account would show.
    claimed = reached and rung["goal"] <= 55
    free_face = "sealed" if claimed else "ready" if reached else "locked"
    pass_face = ("locked" if not owned
                 else "sealed" if claimed
                 else "ready" if reached else "locked")

    lit = free_face == "ready" or pass_face == "ready"

    if lit:
        K.paste(sheet, K.glow(180, 1.35, K.SUN, .70).resize(
            (int(WIDTH + 150), int(ROW_H + 130)), Image.LANCZOS), W / 2, cy)

    K.paste(sheet, K.skin("Hud/plate_navy", WIDTH, ROW_H), W / 2, cy)

    K.paste(sheet, K.skin("Hud/slot", 104, 104), left + WIDTH / 2 + DISC_X, cy)

    split = Image.new("RGBA", (3, int(ROW_H - 44)), (255, 255, 255, 23))
    K.paste(sheet, split, W / 2 + SPLIT_X, cy)
    K.text(sheet, str(index + 1), left + WIDTH / 2 + DISC_X, cy - 2, 40,
           fill=K.CREAM if reached else (214, 205, 186))
    K.text(sheet, txt("ui.mark.rung_goal", rung["goal"]), left + WIDTH / 2 + GOAL_X, cy, 24,
           fill=K.GOLD if reached else (198, 190, 172), outline=2, anchor="l")

    for x, tier, face in ((FREE_X, rung["tier"], free_face),
                          (PASS_X, rung["premiumTier"], pass_face)):
        cx = W / 2 + x
        if face == "ready":
            K.paste(sheet, K.glow(int(CHEST_TALL * 2), 2.0, K.GOLD, .55), cx, cy)

        K.paste(sheet, chest(tier, CHEST_TALL, face), cx, cy)

        if face == "sealed":
            seal = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
            d = ImageDraw.Draw(seal)
            d.ellipse((0, 0, 63, 63), fill=(*K.MINT, 255))
            K.paste(sheet, seal, cx + STAMP_X, cy + STAMP_Y)
            K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_check.png").convert("RGBA"),
                                        (38, 38)), (255, 255, 255)), cx + STAMP_X, cy + STAMP_Y)
        elif face == "locked" and x == PASS_X and not owned:
            K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_lock.png").convert("RGBA"),
                                        (52, 52)), K.CREAM), cx + STAMP_X, cy + STAMP_Y)

    return y + ROW_H + ROW_GAP




# `ConnectBanner` - the standing plate on an account that has never been online, drawn under
# the pass here and under the chest box on the tasks page. Its height decides where the ladder
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


def page(owned=False, marks=62, offline=False):
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    K.plain(sheet)

    rungs = sum(1 for r in SEASON["milestones"] if r["goal"] <= marks)
    top = SEASON["milestones"][-1]["goal"]

    y = 22.0
    y = header(sheet, y)
    y = hero(sheet, y, marks, rungs, top)
    y = pass_banner(sheet, y, owned)
    if offline:
        y = connect_banner(sheet, y, WIDTH)
    y = headings(sheet, y)

    # The list starts on the rung the screen focuses: the first with something unopened.
    first = max(0, rungs - 2)
    index = first
    while y < H - NAVBAR_H and index < len(SEASON["milestones"]):
        y = rung_card(sheet, y, index, SEASON["milestones"][index], marks, owned)
        index += 1

    K.navbar(sheet, "home")
    return sheet


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--owned", action="store_true", help="draw it with the pass unlocked")
    parser.add_argument("--marks", type=int, default=62, help="how far up the ladder to draw")
    parser.add_argument("--contact", action="store_true", help="both states side by side")
    parser.add_argument("--offline", action="store_true",
                        help="an account that has never been online: the connect-once banner")
    args = parser.parse_args()

    OUT.mkdir(parents=True, exist_ok=True)

    if args.contact:
        shots = [page(False, args.marks, args.offline),
                 page(True, args.marks, args.offline)]
        sheet = Image.new("RGBA", (W * len(shots) + 40 * (len(shots) + 1), H + 80), (16, 18, 26, 255))
        for i, shot in enumerate(shots):
            sheet.alpha_composite(shot, (40 + i * (W + 40), 40))
        path = OUT / "season_contact.png"
        sheet.convert("RGB").save(path, quality=94)
    else:
        path = OUT / ("season_owned.png" if args.owned else "season.png")
        page(args.owned, args.marks, args.offline).convert("RGB").save(path, quality=94)

    print("wrote %s" % path)


if __name__ == "__main__":
    main()
