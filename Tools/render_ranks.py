# -*- coding: utf-8 -*-
"""Draws the Ranks page and the map's rank badge, at the size a phone draws them.

    python Tools/render_ranks.py                 # the page, three rungs earned
    python Tools/render_ranks.py --held 0        # an account below the first rung
    python Tools/render_ranks.py --held 7        # the top of the ladder
    python Tools/render_ranks.py --map           # the badge in the map's chrome
    python Tools/render_ranks.py --contact       # every state side by side

**Why this exists.** Every question this page raises is a picture. Does the ladder read as a
ladder or as seven unrelated badges; is the rung being climbed obviously the one to look at;
does a dimmed badge read as *not yet* or as broken art; does a rank name fit its plate. No
numeric gate in this project can open a PNG, and the Editor cannot photograph a
`ScreenSpaceOverlay` canvas — so the page is judged here, the way every board is (`CRAFT.md`).

**And it measures, which is the half a picture cannot do.** `UIKit.Shrinkable` truncates
silently, so a caption that does not fit is not a caption that wraps — it is a caption with its
end cut off, and the tell is Best Fit settling at its floor (invariant 19n). Every line this
page can say is measured against the box it is drawn in, and the settled size is printed.
**That is the reading to look at first**, because a rank ladder is content: a live-ops retune
that pushes a target from 100 to 100,000 lengthens a sentence nobody re-measured.

**It reads the shipped content.** The rungs, their requirements, their names and every sentence
come out of `progression.json` and `loc/en.json`, so a retune redraws rather than going stale —
a mirror with its own numbers answers questions about a screen the game does not draw
(invariant 44d).

**It does not draw the tweens.** Nothing here says whether the light around the rung being
climbed *breathes* well; what it says is whether the light is visible at all against the kit's
navy, and whether exactly one row has it.
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
RANK_ART = K.UI / "Rank"

LOC = {e["key"]: e["text"]
       for e in json.loads((CONTENT / "loc" / "en.json").read_text(encoding="utf-8"))["entries"]}
TABLE = json.loads((CONTENT / "progression.json").read_text(encoding="utf-8"))
MANIFEST = json.loads((CONTENT / "manifest.json").read_text(encoding="utf-8"))

RUNGS = (TABLE.get("ranks") or {}).get("rungs") or []

# ------------------------------------------------------------------ RanksScreen's numbers
CHROME = 92.0
BANNER_H = 138.0
HERO_H = 384.0
WIDTH = 1024.0
ROW_HEAD, LINE_H, BAR_BAND, ROW_FOOT, ROW_GAP = 214.0, 68.0, 84.0, 26.0, 20.0
WELL_INSET, WELL_H = 30.0, 58.0
BADGE, BADGE_SEAT = 176.0, 198.0
MARK_W, COUNT_W, ANSWER = 34.0, 200.0, 88.0
BAR_TROUGH, BAR_H = 36.0, 32.0
HERO_BADGE, HERO_BADGE_X = 268.0, 216.0
TEXT_X = HERO_BADGE_X + HERO_BADGE * .5 + 40.0

#: `RanksScreen.Unearned`, `Unlit` and `LockedInk`. The badge is faded less than its card on
#: purpose - see the C# field.
UNEARNED = .38
UNLIT = .60
LOCKED_INK = (255, 243, 220, 122)

#: `LevelsScreen.CornerSize` / `CornerX` / `CornerY`, and `RankBadge`'s own block.
CORNER, CORNER_X, CORNER_Y = 118.0, 96.0, 132.0
MARK_SIZE, NAME_H, BADGE_W = 92.0, 30.0, 168.0
BOOST_GAP = 18.0

#: Every settled font size this run measured, so the floors can be reported in one place.
MEASURED = []


def txt(key, *args):
    s = LOC.get(key, "<%s>" % key)
    for i, a in enumerate(args):
        s = s.replace("{%d}" % i, str(a))
    return s


def badge(rid, box, earned=True):
    """One rung's picture, at `box` pixels, dimmed with alpha when it is not held.

    **Alpha and never a tint**, mirroring `RanksScreen`: `Image.color` is a multiply and would
    take these saturated metals toward black along their own hue (invariant 44g). A mirror that
    dimmed them the wrong way would report a fault the screen does not have — or, worse, hide
    one it does.
    """
    path = RANK_ART / ("%s.png" % rid)
    if not path.exists():
        return Image.new("RGBA", (int(box), int(box)), (255, 0, 0, 90))

    im = K.fit(Image.open(path).convert("RGBA"), (box, box))
    if earned:
        return im

    faded = im.copy()
    faded.putalpha(im.getchannel("A").point(lambda a: int(a * UNLIT)))
    return faded


# ------------------------------------------------------------------ the readings
def held_of(rung, order, held):
    """What state a row is in: earned, the one being climbed, or locked."""
    if order <= held:
        return "earned"
    return "climbing" if order == held + 1 else "locked"


def chapter_name(cid):
    key = "chapter.%s.name" % cid
    return LOC.get(key, cid)


def level_name(lid):
    key = "level.%s.name" % lid
    return LOC.get(key, lid)


def sentence(line):
    """One requirement, exactly as `RankRequirement.Sentence` builds it."""
    measure, scope, target = line["measure"], line.get("scope"), line["target"]

    if scope:
        named = chapter_name(scope) if measure != "best_wave" else level_name(scope)
        key = "rank.req.%s.in" % measure
        if key in LOC:
            return txt(key, target, named)

    return txt("rank.req.%s" % measure, target)


def progress(line, held, order):
    """A plausible figure for the drawing: full on an earned rung, part-way on the next.

    **Invented rather than read**, and said out loud because a mirror may not tell a comfortable
    lie about the screen (invariant 44d): there is no save here, so what this draws is the
    *shape* of a fraction rather than anybody's real one. What it is for is measuring how wide
    the widest of them gets.
    """
    target = line["target"]
    if order <= held:
        return target
    if order == held + 1:
        return max(0, int(target * .45))
    return 0


# ------------------------------------------------------------------ the page
def header(sheet, y):
    """`RanksScreen.BuildHeader`."""
    cy = y + BANNER_H / 2
    K.paste(sheet, K.skin("sq_blue", CHROME, CHROME), 76, cy)
    K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_left.png").convert("RGBA"), (46, 46)),
                          K.CREAM), 76, cy)

    ribbon = K.skin("Hud/title", 720, BANNER_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.text(plate, txt("ui.ranks.title").upper(), plate.width / 2, plate.height / 2 - 6, 42,
           fill=K.SUN)
    K.paste(sheet, plate, W / 2, cy)
    y += BANNER_H + 4

    px = K.shrunk(sheet, txt("ui.ranks.subtitle"), W / 2, y + 16, 880, 32, 24, 15,
                  fill=(219, 229, 255), outline=0)
    MEASURED.append(("subtitle", px, 15))

    return y + 32 + 16


def hero(sheet, y, held):
    """`RanksScreen.BuildHero` — the badge worn now, large, with how far up the ladder it is."""
    cy = y + HERO_H / 2
    K.paste(sheet, K.skin("Hud/panel", WIDTH, HERO_H), W / 2, cy)

    left = W / 2 - WIDTH / 2

    fan = K.rays(256, 14).resize((int(HERO_H * 2.3), int(HERO_H * 2.3)), Image.LANCZOS)
    lit = Image.new("RGBA", fan.size, (*K.SUN, 0))
    lit.putalpha(fan.point(lambda v: int(v * .17)))
    K.paste(sheet, lit, W / 2, cy - 10)

    bx, by = left + HERO_BADGE_X, cy - 14
    K.paste(sheet, K.glow(int(HERO_BADGE * 1.5), 2.1, K.SUN, .30), bx, by)
    K.paste(sheet, K.skin("Hud/slot", HERO_BADGE * 1.12, HERO_BADGE * 1.12), bx, by)

    rid = RUNGS[held - 1]["id"] if held else RUNGS[0]["id"]
    K.paste(sheet, badge(rid, HERO_BADGE, earned=held > 0), bx, by)

    name = txt("rank.%s.name" % rid) if held else txt("ui.ranks.unranked")
    blurb = txt("rank.%s.blurb" % rid) if held else txt("ui.ranks.unranked_blurb")

    text_x = left + TEXT_X
    text_w = (left + WIDTH) - text_x - 40

    px = K.shrunk_left(sheet, txt("ui.ranks.mark"), text_x, cy - 112 - 15, text_w, 30, 24, 14,
                       fill=(255, 201, 60), outline=2)
    MEASURED.append(("hero kicker", px, 14))

    px = K.shrunk_left(sheet, name, text_x, cy - 50 - 37, text_w, 74, 60, 28,
                       fill=K.GOLD if held else (255, 243, 220))
    MEASURED.append(("hero name '%s'" % name, px, 28))

    px = K.shrunk_left(sheet, blurb, text_x, cy + 34 - 48, text_w, 96, 28, 18,
                       fill=(255, 243, 220), outline=2)
    MEASURED.append(("hero blurb", px, 18))

    line = txt("ui.ranks.held_count", held, len(RUNGS))
    K.paste(sheet, K.round_rect(text_w, 66, 30, (13, 23, 46), .80), text_x + text_w / 2, cy + 122)
    K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_star.png").convert("RGBA"), (34, 34)),
                          K.CREAM), text_x + 28, cy + 122)
    px = K.shrunk_left(sheet, line, text_x + 56, cy + 122 - 17, text_w - 90, 56, 28, 17,
                       fill=K.CREAM, outline=2)
    MEASURED.append(("hero count '%s'" % line, px, 17))

    return y + HERO_H + 22


def row(sheet, y, rung, order, held):
    """`RanksScreen.BuildRow` — one rung, as tall as its own line count makes it."""
    lines = rung["requires"]
    state = held_of(rung, order, held)
    earned = state == "earned"
    climbing = state == "climbing"

    # `RanksScreen.BarBand` — the bar's seat belongs to the one row that draws a bar.
    height = ROW_HEAD + len(lines) * LINE_H + (BAR_BAND if climbing else ROW_FOOT)
    cy = y + height / 2

    if climbing:
        K.paste(sheet, K.glow(int(WIDTH + 280), 1.35, K.SUN, .26), W / 2, cy)

    plate = K.skin("Hud/plate_navy", WIDTH, height)
    if not earned and not climbing:
        plate.putalpha(plate.getchannel("A").point(lambda a: int(a * UNEARNED)))
    K.paste(sheet, plate, W / 2, cy)

    if earned or climbing:
        K.paste(sheet, K.round_rect(int(WIDTH), int(height), 30,
                                    K.SUN if earned else (255, 244, 206),
                                    .32 if earned else .8, width=8), W / 2, cy)

    left = W / 2 - WIDTH / 2
    bx = left + WELL_INSET + BADGE_SEAT / 2
    head_y = cy - height / 2 + ROW_HEAD / 2

    K.paste(sheet, K.glow(int(BADGE_SEAT * 1.3), 2.1, K.SUN, .16), bx, head_y)
    K.paste(sheet, K.skin("Hud/slot", BADGE_SEAT, BADGE_SEAT), bx, head_y)
    K.paste(sheet, badge(rung["id"], BADGE, earned=earned), bx, head_y)

    answer_x = left + WIDTH - WELL_INSET - ANSWER / 2
    if earned:
        K.paste(sheet, K.glow(int(ANSWER), 1.0, K.MINT, 1.0), answer_x, head_y)
        K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_check.png").convert("RGBA"),
                                    (ANSWER * .58, ANSWER * .58)), (255, 255, 255)),
                answer_x, head_y)
    elif not climbing:
        K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_lock.png").convert("RGBA"),
                                    (ANSWER * .82, ANSWER * .82)), LOCKED_INK[:3],
                              LOCKED_INK[3] / 255.0),
                answer_x, head_y)

    text_x = bx + BADGE_SEAT / 2 + 28
    text_w = (answer_x - ANSWER / 2 - 24) - text_x

    ink = K.CREAM if climbing else (K.GOLD if earned else LOCKED_INK)

    px = K.shrunk_left(sheet, txt("ui.ranks.ordinal", order), text_x, head_y - 62 - 14,
                       text_w, 28, 22, 13,
                       fill=(255, 201, 60) if earned or climbing else LOCKED_INK, outline=2)
    MEASURED.append(("ordinal", px, 13))

    name = txt("rank.%s.name" % rung["id"])
    px = K.shrunk_left(sheet, name, text_x, head_y - 16 - 28, text_w, 56, 46, 24, fill=ink)
    MEASURED.append(("row name '%s'" % name, px, 24))

    px = K.shrunk_left(sheet, txt("rank.%s.blurb" % rung["id"]), text_x, head_y + 46 - 31,
                       text_w, 62, 26, 16,
                       fill=(255, 243, 220) if earned or climbing else LOCKED_INK, outline=2)
    MEASURED.append(("row blurb '%s'" % rung["id"], px, 16))

    # The hairline between the head and the checklist.
    rule = Image.new("RGBA", (int(WIDTH - WELL_INSET * 2), 2),
                     (255, 255, 255, 26 if earned or climbing else 13))
    K.paste(sheet, rule, W / 2, cy - height / 2 + ROW_HEAD - 6)

    well_w = WIDTH - WELL_INSET * 2
    line_y = cy - height / 2 + ROW_HEAD + LINE_H / 2

    for line in lines:
        have = progress(line, held, order)
        met = have >= line["target"]

        well = K.skin("Hud/trough", well_w, WELL_H)
        if not earned and not climbing:
            well.putalpha(well.getchannel("A").point(lambda a: int(a * UNEARNED)))
        K.paste(sheet, well, W / 2, line_y)

        well_left = W / 2 - well_w / 2
        mx = well_left + 26 + MARK_W / 2

        if met:
            K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_check.png").convert("RGBA"),
                                        (MARK_W, MARK_W)), K.MINT), mx, line_y)
        else:
            # `Art.Ring` — a neutral mark, deliberately not a star. See `RanksScreen.BuildRow`.
            K.paste(sheet, K.round_rect(MARK_W, MARK_W, MARK_W / 2, (255, 243, 220), .32, width=5),
                    mx, line_y)

        said_x = well_left + 26 + MARK_W + 22
        said_w = well_w - (said_x - well_left) - COUNT_W - 34

        said = sentence(line)
        px = K.shrunk_left(sheet, said, said_x, line_y - (WELL_H - 10) / 2, said_w, WELL_H - 10,
                           30, 17,
                           fill=K.CREAM if earned or climbing else LOCKED_INK, outline=2)
        MEASURED.append(("line '%s'" % said, px, 17))

        fraction = txt("ui.ranks.fraction", have, line["target"])
        px = K.shrunk(sheet, fraction, well_left + well_w - 26 - COUNT_W / 2, line_y,
                      COUNT_W, WELL_H - 10, 32, 18,
                      fill=K.MINT if met else K.GOLD if climbing else LOCKED_INK, outline=2)
        MEASURED.append(("fraction '%s'" % fraction, px, 18))

        line_y += LINE_H

    if climbing:
        bar_y = cy + height / 2 - BAR_BAND / 2
        K.paste(sheet, K.skin("Hud/trough", well_w, BAR_TROUGH), W / 2, bar_y)
        fill = K.skin("Hud/fill", (well_w - 6) * .45, BAR_H)
        K.paste(sheet, K.tint(fill, (255, 150, 30)),
                W / 2 - well_w / 2 + 3 + (well_w - 6) * .45 / 2, bar_y)

    return height


def page(held, scroll=0.0, tall=False):
    """The page. `scroll` drops the list the way a thumb would; `tall` draws the whole thing.

    **A mirror that cannot reach a state cannot be asked about it** (invariant 44l), and on this
    page every state but the first two is below the fold: the rung being climbed, the locked
    rungs and the padlock column are all off the bottom of a phone at rest. Without one of these
    the sheet answers "do the earned rows look right" and nothing else.
    """
    height = H
    if tall:
        # Measure the list first so the sheet is exactly as tall as the page really is.
        probe = Image.new("RGBA", (W, H * 8), (0, 0, 0, 0))
        y = 22.0
        y = header(probe, y)
        y = hero(probe, y, held)
        for order, rung in enumerate(RUNGS, start=1):
            y += row(probe, y, rung, order, held) + ROW_GAP
        height = int(y + 40)
        MEASURED.clear()          # the probe pass would otherwise count every caption twice

    sheet = Image.new("RGBA", (W, height), (*K.GROUND, 255))
    K.plain(sheet)
    K.rail(sheet, top=True)

    y = 22.0
    y = header(sheet, y)
    y = hero(sheet, y, held)
    y -= scroll

    for order, rung in enumerate(RUNGS, start=1):
        y += row(sheet, y, rung, order, held) + ROW_GAP
        if y > height:
            break

    return sheet.convert("RGB")


# ------------------------------------------------------------------ the map's corner
def map_corner(held):
    """`RankBadge` where `LevelsScreen` puts it: under the back key, above the boost clock.

    **The other half of the change, and the half with a neighbour.** The page can be judged on
    its own; the badge cannot, because what can be wrong with it is where it sits against the
    two controls it shares a column with.
    """
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.plain(sheet)

    K.paste(sheet, K.skin("sq_blue", CORNER, CORNER), CORNER_X, CORNER_Y)
    K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_left.png").convert("RGBA"), (59, 59)),
                          K.CREAM), CORNER_X, CORNER_Y)

    rank_y = CORNER_Y + CORNER / 2 + BOOST_GAP + (MARK_SIZE + NAME_H) / 2
    rid = RUNGS[held - 1]["id"] if held else RUNGS[0]["id"]

    K.paste(sheet, badge(rid, MARK_SIZE, earned=held > 0),
            CORNER_X, rank_y - (MARK_SIZE + NAME_H) / 2 + MARK_SIZE / 2)

    name = txt("rank.%s.name" % rid) if held else txt("ui.ranks.unranked")
    px = K.shrunk(sheet, name, CORNER_X, rank_y - (MARK_SIZE + NAME_H) / 2 + MARK_SIZE + NAME_H / 2,
                  BADGE_W, NAME_H, 24, 14, fill=K.GOLD if held else (255, 243, 220), outline=0)
    MEASURED.append(("map badge '%s'" % name, px, 14))

    # The boost clock's seat, so the column reads as a column. Drawn as an outline rather than
    # as a clock, because what is being judged here is the *gap*: the badge is above the clock
    # precisely so that a session with no boost running leaves its hole at the bottom of the
    # column rather than in the middle of it (`LevelsScreen.RankY`).
    boost_y = rank_y + (MARK_SIZE + NAME_H) / 2 + BOOST_GAP + 80 / 2
    K.paste(sheet, K.round_rect(BADGE_W, 80, 12, (255, 243, 220), .22, width=2),
            CORNER_X, boost_y)
    K.text(sheet, "boost", CORNER_X, boost_y, 20, fill=(255, 243, 220, 140), outline=0)

    return sheet


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--held", type=int, default=3, help="how many rungs are earned")
    ap.add_argument("--scroll", type=float, default=0.0, help="drop the list, as a thumb would")
    ap.add_argument("--tall", action="store_true", help="the whole page, however long it is")
    ap.add_argument("--map", action="store_true", help="the badge in the map's chrome")
    ap.add_argument("--contact", action="store_true", help="every state side by side")
    ap.add_argument("--out", type=Path, default=Path("ranks.png"))
    args = ap.parse_args()

    if not RUNGS:
        sys.exit("progression.json has no 'ranks' block; there is nothing to draw")

    if args.contact:
        shots = [page(n, tall=True) for n in (0, 3, len(RUNGS))]
        cell = 540
        sheet = Image.new("RGB", (cell * len(shots), int(cell * H / W)), K.GROUND)
        for i, s in enumerate(shots):
            sheet.paste(s.resize((cell, int(cell * H / W)), Image.LANCZOS), (i * cell, 0))
        out = sheet
    elif args.map:
        out = map_corner(args.held).convert("RGB")
    else:
        out = page(max(0, min(args.held, len(RUNGS))), args.scroll, args.tall)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)

    # The measurement, which is the half a picture cannot do. A caption settling at its floor
    # has not fitted, it has been truncated (invariant 19n), so the floors are printed whether
    # or not anything reached one — a line that is one retune away from its floor is worth
    # seeing before it gets there.
    print("  %d caption(s) measured" % len(MEASURED))
    tight = sorted(MEASURED, key=lambda m: m[1] - m[2])[:6]
    for what, px, floor in tight:
        flag = "  <- AT ITS FLOOR, truncated" if px <= floor else ""
        print("    %-58s %2dpx (floor %d)%s" % (what[:58], px, floor, flag))

    at_floor = [m for m in MEASURED if m[1] <= m[2]]
    if at_floor:
        print("  %d caption(s) settled at their floor; widen the plate or shorten the string"
              % len(at_floor))

    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    sys.exit(main())
