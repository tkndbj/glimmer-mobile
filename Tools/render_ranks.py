# -*- coding: utf-8 -*-
"""Draws the Ranks page and the map's rank badge, at the size a phone draws them.

    python Tools/render_ranks.py                 # the page, three rungs earned
    python Tools/render_ranks.py --held 0        # an account below the first rung
    python Tools/render_ranks.py --held 7        # the top of the ladder
    python Tools/render_ranks.py --tall          # the whole scrolling page
    python Tools/render_ranks.py --map           # the badge in the map's chrome
    python Tools/render_ranks.py --contact       # every state side by side

**Why this exists.** Every question this page raises is a picture. Does the ladder read as a
ladder or as seven unrelated badges; is the rung being climbed obviously the one to look at;
does a locked rung read as *not yet* or as broken art; does a rank name fit its plate. No
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

**Nothing on this page is transparent, and the mirror has to be honest about that.** An
unearned rung is a *darker plate* rather than a fainter one (`RanksScreen`'s class remarks), so
this file carries no alpha fade for a locked card, a locked well or a locked badge. If one
creeps back in, the sheet stops being able to answer the question the change was made for.

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
HERO_H = 420.0
WIDTH = 1024.0
ROW_HEAD, LINE_H, BAR_BAND, ROW_FOOT, ROW_GAP = 214.0, 66.0, 84.0, 26.0, 28.0
WELL_INSET, WELL_H = 30.0, 56.0
BADGE, BADGE_SEAT = 176.0, 198.0
MARK_W, COUNT_W, ANSWER = 34.0, 200.0, 88.0
CHIP_W, CHIP_H = 136.0, 40.0
BAR_TROUGH, BAR_H = 36.0, 32.0
HERO_BADGE, HERO_BADGE_X = 268.0, 216.0
TEXT_X = HERO_BADGE_X + HERO_BADGE * .5 + 40.0
PIP_SEAT, PIP_GAP, PIP_FACE = 68.0, 22.0, .84
LINK_W = 14.0

#: `RankLook.Ghost` - how the hero and the map corner draw the first rung to an account that
#: has not reached it. The one alpha on this page, and the C# says why it is not the fault the
#: rest of the page was rebuilt to fix.
GHOST = .38

#: `RanksScreen.LockedInk` and `LockedName` - **opaque**, and recede by value. There is no
#: `Unearned` alpha here any more and there must not be one again; see the module docstring.
LOCKED_INK = (147, 166, 196)
LOCKED_NAME = (192, 207, 228)

#: `RankLook.Metals`, in ladder order. Measured off the badges themselves and two settled by
#: eye - the C# file says which two and why.
METALS = [
    (0xF0, 0x8A, 0x46),   # 1 Cinderling  - copper
    (0xC6, 0xD8, 0xEE),   # 2 Silverwatch - steel
    (0xFF, 0xB5, 0x24),   # 3 Goldbrand   - gold
    (0x9A, 0x4C, 0xF2),   # 4 Duskcrown   - violet
    (0xF6, 0x4A, 0x38),   # 5 Fireheart   - crimson
    (0x2F, 0x9C, 0xFF),   # 6 Frozencrest - ice
    (0x3B, 0xE9, 0xD8),   # 7 Gemfire     - prism, at its cyan end
]

#: `LevelsScreen.CornerSize` / `CornerX` / `CornerY`, and `RankBadge`'s own block.
CORNER, CORNER_X, CORNER_Y = 118.0, 96.0, 132.0
MARK_SIZE, NAME_H, BADGE_W = 92.0, 30.0, 168.0
BOOST_GAP = 18.0

#: Every settled font size this run measured, so the floors can be reported in one place.
MEASURED = []


def metal(ordinal):
    """`RankLook.Metal` - arithmetic on the ordinal, wrapping, never a table keyed on an id."""
    return METALS[max(0, ordinal - 1) % len(METALS)]


def txt(key, *args):
    s = LOC.get(key, "<%s>" % key)
    for i, a in enumerate(args):
        s = s.replace("{%d}" % i, str(a))
    return s


def badge(rid, box, ghost=False):
    """One rung's picture, at `box` pixels, ghosted when it is standing in for an unheld rank.

    **A card's badge is never dimmed, in any state**, mirroring `RanksScreen`: the badge is the
    thing a player is working toward and the only reason to scroll, and a page of bright
    medallions down a ladder is a trophy case. What says *not yet* is the plate under it, the
    padlock beside it and the drained ordinal chip - three solid things instead of one faded
    one.

    **`ghost` is for the two places that draw a badge nobody holds** - the hero's seat and the
    map's corner, both showing the first rung to an account below it (`RankLook.Ghost`). Alpha
    and never a tint, because a multiply turns bronze to mud (invariant 44g).
    """
    path = RANK_ART / ("%s.png" % rid)
    if not path.exists():
        return Image.new("RGBA", (int(box), int(box)), (255, 0, 0, 90))

    im = K.fit(Image.open(path).convert("RGBA"), (box, box))
    if not ghost:
        return im

    faded = im.copy()
    faded.putalpha(im.getchannel("A").point(lambda a: int(a * GHOST)))
    return faded


def burst(box, colour, alpha):
    """`Art.Rays(256, 16)` in a rung's metal - the light behind an earned badge.

    A fan rather than the kit's starburst, and the C# says why: a hard-rimmed star overhangs the
    plate at any size that reads as rays, because the badge column is inset only `WELL_INSET`.
    """
    box = int(box)
    fan = K.rays(256, 16).resize((box, box), Image.LANCZOS)
    im = Image.new("RGBA", fan.size, (*colour, 0))
    im.putalpha(fan.point(lambda v: int(v * alpha)))
    return im


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


def pips(sheet, cy, held):
    """`RanksScreen.BuildPips` - every rung in the game as one strip of seats.

    **An unearned seat is drawn empty rather than dimmed.** A strip of seven bright badges says
    nothing at all and a strip of seven faint ones says the art is broken; a hole is
    unambiguous. It is the only thing on the page that answers "how far up am I" without
    scrolling, which on a phone is four of seven rungs away.
    """
    n = len(RUNGS)
    if n <= 0:
        return

    room = WIDTH - 80
    pitch = min(PIP_SEAT + PIP_GAP, room / n)
    seat = min(PIP_SEAT, pitch - 8)
    x0 = W / 2 - pitch * (n - 1) / 2

    for i, rung in enumerate(RUNGS):
        x = x0 + pitch * i
        K.paste(sheet, K.skin("Hud/slot", seat, seat), x, cy)
        if i < held:
            K.paste(sheet, badge(rung["id"], seat * PIP_FACE), x, cy)
        if i == held - 1:
            K.paste(sheet, K.round_rect(seat + 6, seat + 6, 18, metal(i + 1), .95, width=4), x, cy)


def hero(sheet, y, held):
    """`RanksScreen.BuildHero` - the badge worn now, large, in its own metal, over the strip."""
    cy = y + HERO_H / 2
    K.paste(sheet, K.skin("Hud/panel", WIDTH, HERO_H), W / 2, cy)

    left = W / 2 - WIDTH / 2
    accent = metal(held) if held else K.SUN

    # **Clipped to the plate, because the screen clips it** - `BuildHero` parents the fan to a
    # `RectMask2D` inset 8 from the plate's own edge. An unclipped mirror draws a sunburst
    # spilling onto the wall, which is a picture of a screen this game does not draw (44d).
    fan = K.rays(256, 14).resize((int(HERO_H * 2.3), int(HERO_H * 2.3)), Image.LANCZOS)
    lit = Image.new("RGBA", fan.size, (*accent, 0))
    lit.putalpha(fan.point(lambda v: int(v * .17)))

    clip = Image.new("RGBA", (int(WIDTH - 16), int(HERO_H - 16)), (0, 0, 0, 0))
    clip.alpha_composite(lit, (int(clip.width / 2 - lit.width / 2),
                               int(clip.height / 2 - 24 - lit.height / 2)))
    K.paste(sheet, clip, W / 2, cy)

    bx, by = left + HERO_BADGE_X, cy - 34
    K.paste(sheet, K.glow(int(HERO_BADGE * 1.5), 2.1, accent, .34 if held else .18), bx, by)
    K.paste(sheet, K.skin("Hud/slot", HERO_BADGE * 1.12, HERO_BADGE * 1.12), bx, by)

    K.paste(sheet, K.round_rect(HERO_BADGE * 1.12, HERO_BADGE * 1.12, 24, accent,
                                .95 if held else .35, width=6), bx, by)

    rid = RUNGS[held - 1]["id"] if held else RUNGS[0]["id"]
    K.paste(sheet, badge(rid, HERO_BADGE, ghost=not held), bx, by)

    name = txt("rank.%s.name" % rid) if held else txt("ui.ranks.unranked")
    blurb = txt("rank.%s.blurb" % rid) if held else txt("ui.ranks.unranked_blurb")

    text_x = left + TEXT_X
    text_w = (left + WIDTH) - text_x - 40

    px = K.shrunk_left(sheet, txt("ui.ranks.mark"), text_x, cy - 148 - 15, text_w, 30, 24, 14,
                       fill=accent, outline=2)
    MEASURED.append(("hero kicker", px, 14))

    px = K.shrunk_left(sheet, name, text_x, cy - 84 - 37, text_w, 74, 60, 28,
                       fill=K.GOLD if held else (255, 243, 220))
    MEASURED.append(("hero name '%s'" % name, px, 28))

    px = K.shrunk_left(sheet, blurb, text_x, cy - 4 - 46, text_w, 92, 28, 18,
                       fill=(255, 243, 220), outline=2)
    MEASURED.append(("hero blurb", px, 18))

    line = txt("ui.ranks.held_count", held, len(RUNGS))
    K.paste(sheet, K.round_rect(text_w, 62, 28, (13, 23, 46), .80), text_x + text_w / 2, cy + 74)
    K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_star.png").convert("RGBA"), (32, 32)),
                          K.CREAM), text_x + 26, cy + 74)
    px = K.shrunk_left(sheet, line, text_x + 54, cy + 74 - 16, text_w - 88, 52, 28, 17,
                       fill=K.CREAM, outline=2)
    MEASURED.append(("hero count '%s'" % line, px, 17))

    rule = Image.new("RGBA", (int(WIDTH - WELL_INSET * 2), 2), (*accent, 56))
    K.paste(sheet, rule, W / 2, cy + 116)

    pips(sheet, cy + 160, held)

    return y + HERO_H + 22


def row(sheet, y, rung, order, held, chained):
    """`RanksScreen.BuildRow` - one rung, as tall as its own line count makes it."""
    lines = rung["requires"]
    state = held_of(rung, order, held)
    earned = state == "earned"
    climbing = state == "climbing"
    live = earned or climbing
    tone = metal(order)

    # `RanksScreen.BarBand` - the bar's seat belongs to the one row that draws a bar.
    height = ROW_HEAD + len(lines) * LINE_H + (BAR_BAND if climbing else ROW_FOOT)
    cy = y + height / 2

    left = W / 2 - WIDTH / 2
    bx = left + WELL_INSET + BADGE_SEAT / 2

    if climbing:
        K.paste(sheet, K.glow(int(WIDTH + 280), 1.35, tone, .28), W / 2, cy)

    # The link down to the next rung, hung in the gap under the badge column. See the C#.
    if chained:
        K.paste(sheet,
                K.round_rect(LINK_W, ROW_GAP + 12, LINK_W / 2,
                             tone if earned else (255, 243, 220), .85 if earned else .12),
                bx, cy + height / 2 + ROW_GAP / 2)

    # **Value, never alpha**: a locked rung stands on the muted plate at full strength.
    K.paste(sheet, K.skin("Hud/plate_navy" if live else "Hud/panel", WIDTH, height), W / 2, cy)

    K.paste(sheet, K.round_rect(int(WIDTH), int(height), 30,
                                (255, 244, 206) if climbing else tone,
                                .80 if climbing else (.55 if earned else .22), width=8),
            W / 2, cy)

    head_y = cy - height / 2 + ROW_HEAD / 2

    if earned:
        K.paste(sheet, burst(BADGE_SEAT * 1.60, tone, .34), bx, head_y)
    K.paste(sheet, K.glow(int(BADGE_SEAT * 1.34), 2.1, tone,
                          .22 if earned else (.24 if climbing else .10)), bx, head_y)
    K.paste(sheet, K.skin("Hud/slot", BADGE_SEAT, BADGE_SEAT), bx, head_y)
    K.paste(sheet, K.round_rect(BADGE_SEAT, BADGE_SEAT, 24, tone,
                                .95 if earned else (.80 if climbing else .45), width=5),
            bx, head_y)
    K.paste(sheet, badge(rung["id"], BADGE), bx, head_y)

    # The answer column: one answer per row, and exactly one of the three is up (48g).
    answer_x = left + WIDTH - WELL_INSET - ANSWER / 2
    if earned:
        K.paste(sheet, K.glow(int(ANSWER), 1.0, K.MINT, 1.0), answer_x, head_y)
        K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_check.png").convert("RGBA"),
                                    (ANSWER * .58, ANSWER * .58)), (255, 255, 255)),
                answer_x, head_y)
    elif climbing:
        K.paste(sheet, K.skin("Hud/slot", ANSWER, ANSWER), answer_x, head_y)
        K.paste(sheet, K.round_rect(ANSWER, ANSWER, 20, tone, 1.0, width=4), answer_x, head_y)
        pct = txt("ui.ranks.percent", 45)
        px = K.shrunk(sheet, pct, answer_x, head_y, ANSWER - 12, 40, 30, 16, fill=tone, outline=2)
        MEASURED.append(("standing '%s'" % pct, px, 16))
    else:
        K.paste(sheet, K.skin("sq_dark", ANSWER, ANSWER), answer_x, head_y)
        K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_lock.png").convert("RGBA"),
                                    (ANSWER * .56, ANSWER * .56)), LOCKED_INK),
                answer_x, head_y)

    text_x = bx + BADGE_SEAT / 2 + 28
    text_w = (answer_x - ANSWER / 2 - 24) - text_x

    # The ordinal in a chip rather than on the plate - a pale metal written straight onto
    # `PlateNavy` is the yellow-bar-on-a-yellow-card fault with a different pair of colours.
    chip_x, chip_y = text_x + CHIP_W / 2, head_y - 66
    K.paste(sheet, K.round_rect(CHIP_W, CHIP_H, 14, (0, 0, 0), .40 if live else .26),
            chip_x, chip_y)
    K.paste(sheet, K.round_rect(CHIP_W, CHIP_H, 14, tone, .95 if live else .55, width=3),
            chip_x, chip_y)
    ordinal = txt("ui.ranks.ordinal", order)
    px = K.shrunk(sheet, ordinal, chip_x, chip_y, CHIP_W - 16, 28, 22, 13, fill=tone, outline=2)
    MEASURED.append(("ordinal '%s'" % ordinal, px, 13))

    name = txt("rank.%s.name" % rung["id"])
    px = K.shrunk_left(sheet, name, text_x, head_y - 12 - 29, text_w, 58, 50, 24,
                       fill=K.GOLD if earned else (K.CREAM if climbing else LOCKED_NAME))
    MEASURED.append(("row name '%s'" % name, px, 24))

    px = K.shrunk_left(sheet, txt("rank.%s.blurb" % rung["id"]), text_x, head_y + 52 - 31,
                       text_w, 62, 26, 16,
                       fill=(255, 243, 220) if live else LOCKED_INK, outline=2)
    MEASURED.append(("row blurb '%s'" % rung["id"], px, 16))

    # The hairline between the head and the checklist, in the rung's metal.
    rule = Image.new("RGBA", (int(WIDTH - WELL_INSET * 2), 2),
                     (*tone, int(255 * (.34 if live else .20))))
    K.paste(sheet, rule, W / 2, cy - height / 2 + ROW_HEAD - 6)

    well_w = WIDTH - WELL_INSET * 2
    line_y = cy - height / 2 + ROW_HEAD + LINE_H / 2

    for line in lines:
        have = progress(line, held, order)
        met = have >= line["target"]

        # Full strength on every row: a faded checklist under a solid plate is the one thing on
        # the card that looks unfinished rather than unearned.
        K.paste(sheet, K.skin("Hud/trough", well_w, WELL_H), W / 2, line_y)

        well_left = W / 2 - well_w / 2
        mx = well_left + 26 + MARK_W / 2

        if met:
            K.paste(sheet, K.tint(K.fit(Image.open(K.UI / "ic_check.png").convert("RGBA"),
                                        (MARK_W, MARK_W)), K.MINT), mx, line_y)
        else:
            # `Art.Ring` in the rung's metal - deliberately not a star. See `RanksScreen`.
            K.paste(sheet, K.round_rect(MARK_W, MARK_W, MARK_W / 2, tone,
                                        .70 if live else .45, width=5), mx, line_y)

        said_x = well_left + 26 + MARK_W + 22
        said_w = well_w - (said_x - well_left) - COUNT_W - 34

        said = sentence(line)
        px = K.shrunk_left(sheet, said, said_x, line_y - (WELL_H - 10) / 2, said_w, WELL_H - 10,
                           30, 17, fill=K.CREAM if live else LOCKED_INK, outline=2)
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
    last = len(RUNGS) - 1

    height = H
    if tall:
        # Measure the list first so the sheet is exactly as tall as the page really is.
        probe = Image.new("RGBA", (W, H * 8), (0, 0, 0, 0))
        y = 22.0
        y = header(probe, y)
        y = hero(probe, y, held)
        for order, rung in enumerate(RUNGS, start=1):
            y += row(probe, y, rung, order, held, order - 1 < last) + ROW_GAP
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
        y += row(sheet, y, rung, order, held, order - 1 < last) + ROW_GAP
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

    K.paste(sheet, badge(rid, MARK_SIZE, ghost=not held),
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
