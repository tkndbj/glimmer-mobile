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
MANIFEST = CONTENT / "manifest.json"
OUT = K.REPO / "Tools" / "out"

# EventScreen
CHROME = 92.0
BANNER_H = 138.0
HERO_H = 240.0
PASS_H = 132.0
HEADING_H = 62.0
WIDTH = 1000.0

# SeasonLadder
ROW_H = 164.0
ROW_GAP = 14.0
FREE_X = 20.0
PASS_X = 310.0
SPLIT_X = (FREE_X + PASS_X) / 2
DISC_X = -404.0
GOAL_X = -330.0
CHEST_W, CHEST_H = 110.0, 152.0

# EventScreen.BarOrange / BarFull / BarH — the tasks page's numbers, deliberately shared.
BAR_ORANGE = (255, 150, 30)
BAR_FULL = (96, 235, 70)
BAR_H = 26

NAVBAR_H = 190.0

#: `EventScreen.HintW` — the gap between the pass crest and its button.
HINT_W = 520.0


def fitted(line, width, size, floor):
    """The point size `UIKit.Shrinkable` would settle on: the largest that fits, down to a floor."""
    while size > floor and K.font(size).getlength(line) > width:
        size -= 1
    return size


def strings():
    return {e["key"]: e["text"] for e in json.loads(LOC.read_text(encoding="utf-8"))["entries"]}


STR = strings()
TASKS = json.loads(TABLE.read_text(encoding="utf-8"))["tasks"]
SEASON = next(e for e in json.loads(MANIFEST.read_text(encoding="utf-8"))["events"]
              if e["id"] == "first_watch")


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


def chest(tier, w, h, face):
    """One chest at one of the three faces a cell draws. `SeasonLadder.Paint`'s tints."""
    im, box = ART[tier]
    out = im.crop(box).resize((max(1, int(round(w))), max(1, int(round(h)))), Image.LANCZOS)

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
    K.text(plate, txt("ui.event.first_watch.name").upper(), plate.width / 2,
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


#: `SeasonCrest.PaintWatch` — how many tally marks the ring carries.
CREST_MARKS = 12


def watch_crest(size, open01):
    """`SeasonCrest.PaintWatch` — a ring of tally marks lighting clockwise round a lit core."""
    crest = Image.new("RGBA", (int(size), int(size)), (0, 0, 0, 0))
    draw = ImageDraw.Draw(crest)

    lit = 0 if open01 <= 0 else max(1, min(CREST_MARKS, math.ceil(open01 * CREST_MARKS)))

    # The dark band the marks sit on, so an unlit pip reads as a place for one.
    band = size * .5
    draw.ellipse((size / 2 - band, size / 2 - band, size / 2 + band, size / 2 + band),
                 outline=(15, 26, 41, 217), width=int(round(size * .07)))

    radius = size * .40
    pip = size * .13

    for i in range(CREST_MARKS):
        angle = math.pi * .5 - i * (math.pi * 2 / CREST_MARKS)
        cx = size / 2 + math.cos(angle) * radius
        cy = size / 2 - math.sin(angle) * radius

        if i < lit:
            crest.alpha_composite(
                K.glow(64, 2.0, K.BLOOM, .55).resize((int(pip * 2.2), int(pip * 2.2)), Image.LANCZOS),
                (int(cx - pip * 1.1), int(cy - pip * 1.1)))

        colour = (255, 180, 232, 255) if i < lit else (66, 82, 102, 235)
        draw.ellipse((cx - pip / 2, cy - pip / 2, cx + pip / 2, cy + pip / 2), fill=colour)

    core = size * .26 * (.34 + .66 * open01)
    crest.alpha_composite(
        K.glow(96, 2.0, K.SUN, .40).resize((int(core * 2.1), int(core * 2.1)), Image.LANCZOS),
        (int(size / 2 - core * 1.05), int(size / 2 - core * 1.05)))
    draw.ellipse((size / 2 - core, size / 2 - core, size / 2 + core, size / 2 + core),
                 fill=(*K.SUN, 255))
    return crest


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

    open01 = 0.0 if not rungs else rungs / float(len(SEASON["milestones"]))
    K.paste(sheet, watch_crest(150, open01), left + 132, cy - 14)

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
    K.paste(sheet, K.glow(128, 2.0, K.BLOOM, .22), left + 104, cy)
    K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "Hud" / "burst.png").convert("RGBA"), (96, 96)),
                          K.GOLD), left + 104, cy)
    K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_gem.png").convert("RGBA"), (44, 44)),
                          K.CREAM), left + 104, cy)

    K.text(sheet, txt("ui.mark.pass"), left + 176, cy - 20, 32,
           fill=K.MINT if owned else K.CREAM, anchor="l")

    # Drawn at the size `UIKit.Shrinkable` would land on rather than at the authored one,
    # because the room between the crest and the button is the whole question here: a mirror
    # that let the line run under the button would be answering it about a screen the game
    # does not draw (invariant 44d), and a Unity label that overflows is not clipped and
    # nothing says so (37n).
    K.text(sheet, txt("ui.mark.owned_hint" if owned else "ui.mark.purchase_hint"),
           left + 176, cy + 20, fitted(txt("ui.mark.owned_hint" if owned
                                           else "ui.mark.purchase_hint"), HINT_W, 22, 14),
           fill=(255, 243, 220), outline=0, anchor="l")

    btn = K.skin("btn_violet", 268, 88)
    plate = Image.new("RGBA", btn.size, (0, 0, 0, 0))
    plate.alpha_composite(btn)
    # The caption and its trailing glyph, in both states: a gem on a price, a tick on the
    # word that replaces one. See `EventScreen.RefreshPass`.
    if owned:
        caption, mark = txt("ui.mark.unlocked"), "ic_check.png"
        glyph = 34
        run = K.font(34).getlength(caption) + 10 + glyph
        left = plate.width / 2 - run / 2
        K.text(plate, caption, left, plate.height / 2 - 6, 34, anchor="l")
        K.paste(plate, K.tint(K.fit(Image.open(K.UI / mark).convert("RGBA"), (glyph, glyph)),
                              K.CREAM), left + run - glyph / 2, plate.height / 2 - 6)
    else:
        # The caption and its trailing gem, as `Btn.IconTrails` lays them out: the pair is
        # centred together, so the words sit left of middle by half the glyph and its gap.
        price = txt("ui.mark.buy", "{:,}".format(SEASON.get("passGems", 0)))
        glyph = 34
        run = K.font(34).getlength(price) + 10 + glyph
        left = plate.width / 2 - run / 2
        K.text(plate, price, left, plate.height / 2 - 6, 34, anchor="l")
        K.paste(plate, K.fit(Image.open(K.UI / "ic_gem.png").convert("RGBA"), (glyph, glyph)),
                left + run - glyph / 2, plate.height / 2 - 6)
    K.paste(sheet, plate, W / 2 + WIDTH / 2 - 152, cy)

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

    K.paste(sheet, K.skin("Hud/card", WIDTH, ROW_H), W / 2, cy)

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
            K.paste(sheet, K.glow(240, 2.0, K.GOLD, .55), cx, cy)

        K.paste(sheet, chest(tier, CHEST_W, CHEST_H, face), cx, cy)

        if face == "sealed":
            seal = Image.new("RGBA", (56, 56), (0, 0, 0, 0))
            d = ImageDraw.Draw(seal)
            d.ellipse((0, 0, 55, 55), fill=(*K.MINT, 255))
            K.paste(sheet, seal, cx + 50, cy + 46)
            K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_check.png").convert("RGBA"),
                                        (34, 34)), (255, 255, 255)), cx + 50, cy + 46)
        elif face == "locked" and x == PASS_X and not owned:
            K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_lock.png").convert("RGBA"),
                                        (46, 46)), K.CREAM), cx + 50, cy + 46)

    return y + ROW_H + ROW_GAP


def page(owned=False, marks=62):
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    K.plain(sheet)

    rungs = sum(1 for r in SEASON["milestones"] if r["goal"] <= marks)
    top = SEASON["milestones"][-1]["goal"]

    y = 22.0
    y = header(sheet, y)
    y = hero(sheet, y, marks, rungs, top)
    y = pass_banner(sheet, y, owned)
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
    args = parser.parse_args()

    OUT.mkdir(parents=True, exist_ok=True)

    if args.contact:
        shots = [page(False, args.marks), page(True, args.marks)]
        sheet = Image.new("RGBA", (W * len(shots) + 40 * (len(shots) + 1), H + 80), (16, 18, 26, 255))
        for i, shot in enumerate(shots):
            sheet.alpha_composite(shot, (40 + i * (W + 40), 40))
        path = OUT / "season_contact.png"
        sheet.convert("RGB").save(path, quality=94)
    else:
        path = OUT / ("season_owned.png" if args.owned else "season.png")
        page(args.owned, args.marks).convert("RGB").save(path, quality=94)

    print("wrote %s" % path)


if __name__ == "__main__":
    main()
