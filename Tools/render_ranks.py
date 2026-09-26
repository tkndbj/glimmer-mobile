# -*- coding: utf-8 -*-
"""Draws the hall of ranks (`RanksScreen`) and the map's rank badge, at the size a phone draws them.

    python Tools/render_ranks.py                    # three rungs held, the held one on the stage
    python Tools/render_ranks.py --held 0           # an account below the first rung
    python Tools/render_ranks.py --held 7           # the top of the ladder
    python Tools/render_ranks.py --held 3 --show 5  # a locked rung chosen on the stage
    python Tools/render_ranks.py --tall             # a 20:9 phone, where the stage takes the air
    python Tools/render_ranks.py --map              # the badge in the map's chrome
    python Tools/render_ranks.py --contact          # every state side by side

**Why this exists.** Every question this page raises is a picture. Does the badge on the stage
read as *the thing you hold* or as a sticker on a wall; does the ring read as filling; does the
rail read as a ladder with a place on it; does a locked rung read as *not yet* rather than as
broken; does a rank name fit its band. No numeric gate in this project can open a PNG and the
Editor cannot photograph a `ScreenSpaceOverlay` canvas, so the page is judged here, the way
every board is (`CRAFT.md`).

**And it measures, which is the half a picture cannot do.** `UIKit.Shrinkable` truncates
silently, so a caption that does not fit is not a caption that wraps — it is a caption with its
end cut off, and the tell is Best Fit settling at its floor (invariant 19n). Every line this
page can say is measured against the box it is drawn in and the settled size is printed.
**That is the reading to look at first**, because a rank ladder is content: a live-ops retune
that pushes a target from 100 to 100,000 lengthens a sentence nobody re-measured. `--contact`
walks every rung onto the stage precisely so that every name, blurb and sentence is measured
once per run.

**It reads the shipped content.** The rungs, their requirements, their names and every sentence
come out of `progression.json` and `loc/en.json`, so a retune redraws rather than going stale —
a mirror with its own numbers answers questions about a screen the game does not draw
(invariant 44d). Every layout constant is named after the `RanksScreen` field it copies.

**What it cannot answer.** Whether the fans turning and the aurora drifting read as light or as
noise; whether the badge swap on a tap reads as one thing turning; whether the spark at the
head of the ring reads as the arc *filling*. Those are a device, and the C# says where the dials
are.
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

REPO = Path(__file__).resolve().parents[1]
RANK_ART = REPO / "Assets" / "Game" / "Art" / "Ui" / "Rank"
CONTENT = REPO / "Assets" / "StreamingAssets" / "Content"

W, H = K.W, K.H
TALL_H = 2400                       # a 20:9 phone's canvas, 1080 across

TABLE = json.loads((CONTENT / "progression.json").read_text(encoding="utf8"))
LOC = {e["key"]: e["text"]
       for e in json.loads((CONTENT / "loc" / "en.json").read_text(encoding="utf8"))["entries"]}
RUNGS = (TABLE.get("ranks") or {}).get("rungs") or []

# ------------------------------------------------------------------ RanksScreen's numbers
# The stack. Every one of these is named after the field it mirrors; a typed copy that drifted
# would be a mirror telling a comfortable lie about the screen (invariant 44d).
CHROME, BANNER_H, HEADER_TOP = 92.0, 138.0, 22.0
HEADER_H = HEADER_TOP + BANNER_H + 10.0
STAGE_LEAST, STAGE_MOST = 790.0, 1.24
RAIL_H = 150.0
SEAT, SEAT_PITCH_MOST, SEAT_FACE, SEAT_FACE_LOCKED, SEAT_CHOSEN = 104.0, 140.0, .82, .64, 1.22
LINK_THICK, LINK_GAP, LOCK_CHIP = 12.0, 6.0, 34.0
PLATE_W, PLATE_HEAD, LINE_H, PLATE_FOOT = 1024.0, 76.0, 88.0, 22.0
PLATE_GAP, FOOT_PAD = 18.0, 24.0
WELL_INSET, WELL_H, MARK, COUNT_W, BAR_H = 26.0, 78.0, 38.0, 210.0, 12.0
INSET_ALLOWANCE = 150.0
SHORTEST_CANVAS = 1080.0 * 1.75             # CanvasFit.ShortestCanvas

# The stage, down from its top (`RanksScreen`: the stage is cut to the scaled cluster and the
# rail and plate follow it, so a tall phone's air lands under the plate - `StageFit`).
RING_TOP, RING_SIZE, RING_THICK, BADGE = 290.0, 540.0, 9.0, 420.0
CHEVRON_X, CHEVRON = 380.0, 92.0
CHIP_TOP, CHIP_W, CHIP_H = RING_TOP + RING_SIZE * .5, 156.0, 46.0
EYEBROW_TOP, EYEBROW_H = 616.0, 32.0
NAME_TOP, NAME_H = 674.0, 76.0
PILL_TOP, PILL_H, PILL_W = 760.0, 56.0, 600.0
TEXT_W = 900.0
HALO, CORE, FAN, FAN2, SPARK = 980.0, 560.0, 1300.0, 940.0, 58.0
FAN_A, FAN2_A, HALO_A, CORE_A = .22, .13, .46, .34
AURORA_HOME = [(-360.0, 240.0), (380.0, 470.0)]      # down from the top
AURORA_SIZE = [980.0, 820.0]
AURORA_A = [.18, .13]
TROUGH = (8, 13, 36)
LOCKED_INK = (147, 166, 196)
BAR_ORANGE = (255, 150, 30)

#: `RankLook.Metals`, in ladder order.
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
MARK_SIZE, NAME_BLOCK_H, BADGE_W = 184.0, 44.0, 192.0
BOOST_GAP = 18.0

#: Every settled font size this run measured, so the floors can be reported in one place.
MEASURED = []


def metal(ordinal):
    """`RankLook.Metal` - arithmetic on the ordinal, wrapping, never a table keyed on an id."""
    return METALS[max(0, ordinal - 1) % len(METALS)]


def lift(colour, t):
    """`Pal.Lift` - toward white."""
    return tuple(int(round(c + (255 - c) * t)) for c in colour)


def txt(key, *args):
    s = LOC.get(key, "<%s>" % key)
    for i, a in enumerate(args):
        s = s.replace("{%d}" % i, str(a))
    return s


def badge(rid, box):
    """One rung's picture, at `box` pixels. **Never dimmed, in any state** (`RanksScreen`)."""
    path = RANK_ART / ("%s.png" % rid)
    if not path.exists():
        return Image.new("RGBA", (int(box), int(box)), (255, 0, 0, 90))
    return K.fit(Image.open(path).convert("RGBA"), (box, box))


def icon(name, box, colour):
    return K.tint(K.fit(Image.open(K.UI / (name + ".png")).convert("RGBA"), (box, box)), colour)


def ring(size, thick, colour, alpha=1.0, sweep=None):
    """`Art.Ring` at `size`, and with `sweep` the radial fill uGUI draws over it: clockwise
    from the bottom, which is where the chip covers the seam."""
    size = int(size)
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    t = thick * size / 256.0
    box = [t / 2, t / 2, size - t / 2, size - t / 2]
    fill = (*colour, int(255 * alpha))
    if sweep is None or sweep >= .999:
        d.ellipse(box, outline=fill, width=int(round(t)))
    elif sweep > 0:
        # Pillow's angles run clockwise from 3 o'clock on a y-down image. Bottom is 90; a
        # clockwise sweep on screen from the bottom goes toward the right on a y-down image,
        # which is *decreasing* angle here.
        d.arc(box, start=90 - sweep * 360, end=90, fill=fill, width=int(round(t)))
    return im


def spark(size, colour):
    """`Art.Spark(96)` - a four-pointed star of light."""
    size = int(size)
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = im.load()
    h = size / 2.0
    for y in range(size):
        for x in range(size):
            dx, dy = abs(x - h) / (h - 1), abs(y - h) / (h - 1)
            v = dx ** .45 + dy ** .45
            a = max(0.0, min(1.0, (1 - v) * 5))
            px[x, y] = (*colour, int(255 * a))
    return im


# ------------------------------------------------------------------ the readings
def standing_of(order, held):
    if order == held:
        return "held"
    if order < held:
        return "earned"
    return "next" if order == held + 1 else "locked"


def chapter_name(cid):
    return LOC.get("chapter.%s.name" % cid, cid)


def level_name(lid):
    return LOC.get("level.%s.name" % lid, lid)


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
    """A plausible figure for the drawing: full on an earned rung, part-way on the next, a
    little on the one after.

    **Invented rather than read**, and said out loud because a mirror may not tell a
    comfortable lie about the screen (invariant 44d): there is no save here, so what this draws
    is the *shape* of a fraction rather than anybody's real one. What it is for is measuring how
    wide the widest of them gets and how a part-filled ring and bar look.
    """
    target = line["target"]
    if order <= held:
        return target
    if order == held + 1:
        return max(0, int(target * .45))
    if order == held + 2:
        return max(0, int(target * .12))
    return 0


def share(rung, held, order):
    """`RanksScreen.Share` - the mean of the clamped line shares."""
    lines = rung["requires"]
    if not lines:
        return 1.0
    return sum(min(1.0, progress(l, held, order) / float(l["target"])) for l in lines) / len(lines)


def plate_band():
    """`RanksScreen.PlateBand` - the tallest rung's plate, bounded by the shortest canvas."""
    most = max((len(r["requires"]) for r in RUNGS), default=1)
    wanted = PLATE_HEAD + max(1, most) * LINE_H + PLATE_FOOT
    room = SHORTEST_CANVAS - INSET_ALLOWANCE - HEADER_H - STAGE_LEAST - RAIL_H - PLATE_GAP - FOOT_PAD
    return min(wanted, room)


# ------------------------------------------------------------------ the page
def header(sheet):
    """`RanksScreen.BuildHeader`."""
    cy = HEADER_TOP + BANNER_H / 2
    K.paste(sheet, K.skin("sq_blue", CHROME, CHROME), 76, cy)
    K.paste(sheet, icon("ic_left", 46, K.CREAM), 76, cy)

    ribbon = K.skin("ribbon_orange", 720, BANNER_H).rotate(1.6, resample=Image.BICUBIC, expand=True)
    K.paste(sheet, ribbon, W / 2, cy)
    K.text(sheet, txt("ui.ranks.title").upper(), W / 2, cy - 4, 42, fill=K.CREAM, outline=4)


def stage(sheet, top, bottom, held, chosen):
    """`RanksScreen.BuildStage` + `DressStage` + `PaintStage`: the room, the light, the ring,
    the badge, the words - for the rung chosen."""
    sh = int(bottom - top)
    room = Image.new("RGBA", (W, sh), (0, 0, 0, 0))
    cx = W / 2

    # `RanksScreen.StageFit`: the hero cluster is laid out at the least stage's size and scaled
    # to the room, so everything from here down to the fireflies is drawn on `layer` at 1:1 and
    # the layer is scaled once, exactly as the canvas scales the node. Captions are therefore
    # measured at the size they are *laid out* at, which is the size Best Fit sees.
    grow = min(max(sh / STAGE_LEAST, 1.0), STAGE_MOST)
    layer = Image.new("RGBA", (W, int(STAGE_LEAST)), (0, 0, 0, 0))
    order = chosen
    rung = RUNGS[order - 1]
    tone = metal(order)
    lit = lift(tone, .45)
    standing = standing_of(order, held)
    live = standing != "locked"

    # No wash: the wall is the room (cut 2026-09-26 at the owner's instruction).
    # Fireflies, at rest: a scatter in the metal's lift.
    rng = 7
    for i in range(16):
        rng = (rng * 1103515245 + 12345) & 0x7fffffff
        fx = (rng % 1080) - 540
        rng = (rng * 1103515245 + 12345) & 0x7fffffff
        fy = rng % sh
        rng = (rng * 1103515245 + 12345) & 0x7fffffff
        fs = 5 + rng % 12
        K.paste(room, K.glow(int(fs * 2), 1.7, lift(tone, .35), .45), cx + fx, fy)


    # The aurora is the stage's, hung from its top; the fans, the halo and the core are the
    # cluster's but **unclipped by it** - only the stage masks - so they are drawn on the room at
    # the cluster's scale rather than on the layer, which would crop them to the cluster's box
    # (a mirror that under-draws sends you off to fix what is not broken, 44d).
    for i, (hx, hy) in enumerate(AURORA_HOME):
        K.paste(room, K.glow(int(AURORA_SIZE[i]), 1.7, tone if i == 0 else lit, AURORA_A[i]),
                cx + hx, hy)
    ry_room = sh - (STAGE_LEAST - RING_TOP) * grow

    def fan(size, count, colour, alpha, angle):
        f = K.rays(256, count).resize((int(size), int(size)), Image.LANCZOS).rotate(angle, resample=Image.BICUBIC)
        im = Image.new("RGBA", f.size, (*colour, 0))
        im.putalpha(f.point(lambda v: int(v * alpha)))
        return im

    ry = RING_TOP
    K.paste(room, fan(FAN * grow, 14, tone, FAN_A, 11), cx, ry_room)
    K.paste(room, fan(FAN2 * grow, 7, lit, FAN2_A, -23), cx, ry_room)
    K.paste(room, K.glow(int(HALO * grow), 1.8, tone, HALO_A), cx, ry_room)

    K.paste(room, K.glow(int(CORE * grow), 2.4, lift(tone, .5), CORE_A), cx, ry_room)
    K.paste(layer, ring(RING_SIZE, RING_THICK, TROUGH, .78), cx, ry)
    fill = share(rung, held, order)
    K.paste(layer, ring(RING_SIZE, RING_THICK, tone, 1.0, sweep=fill), cx, ry)
    if fill > .004:
        a = fill * math.pi * 2
        r = RING_SIZE / 2 - RING_THICK * (RING_SIZE / 256.0) / 2
        K.paste(layer, spark(SPARK, lift(tone, .55)), cx - math.sin(a) * r, ry + math.cos(a) * r)

    K.paste(layer, badge(rung["id"], BADGE), cx, ry)

    for sx, name, on in ((-CHEVRON_X, "ic_left", order > 1), (CHEVRON_X, "ic_right", order < len(RUNGS))):
        cap = K.skin("sq_blue", CHEVRON, CHEVRON)
        if not on:
            cap = K.tint(cap, (158, 168, 179), .85)
        K.paste(layer, cap, cx + sx, ry)
        K.paste(layer, icon(name, CHEVRON * .5, K.CREAM if on else (255, 255, 255)), cx + sx, ry - CHEVRON * .04)

    # The ordinal chip on the ring's foot.
    chip_y = CHIP_TOP
    K.paste(layer, K.round_rect(CHIP_W, CHIP_H, 14, (0, 0, 0), .55), cx, chip_y)
    K.paste(layer, K.round_rect(CHIP_W, CHIP_H, 14, tone, 1.0, width=3), cx, chip_y)
    ordinal = txt("ui.ranks.ordinal", order)
    px = K.shrunk(layer, ordinal, cx, chip_y, CHIP_W - 16, 30, 24, 14, fill=tone, outline=2)
    MEASURED.append(("ordinal '%s'" % ordinal, px, 14))

    eyebrow = {"held": "ui.ranks.mark", "earned": "ui.ranks.earned",
               "next": "ui.ranks.next", "locked": "ui.ranks.locked"}[standing]
    eyebrow = txt(eyebrow).upper()
    px = K.shrunk(layer, eyebrow, cx, EYEBROW_TOP, TEXT_W, EYEBROW_H, 26, 15,
                  fill=lift(tone, .25) if live else LOCKED_INK, outline=2)
    MEASURED.append(("eyebrow '%s'" % eyebrow, px, 15))

    # The name, with `TextGradient`'s shimmer across it: lift .70 at the ends, .30 in the
    # middle, over white type.
    name = txt("rank.%s.name" % rung["id"])
    px = K.shrunk(layer, name, cx, NAME_TOP, TEXT_W, NAME_H, 78, 34, fill=lift(tone, .55), outline=3)
    MEASURED.append(("name '%s'" % name, px, 34))

    if standing == "held":
        pill = txt("ui.ranks.top") if order >= len(RUNGS) else txt("ui.ranks.held_count", held, len(RUNGS))
    elif standing == "earned":
        pill = txt("ui.ranks.held_count", held, len(RUNGS))
    elif standing == "next":
        pill = txt("ui.ranks.progress", int(round(fill * 100)))
    else:
        pill = txt("ui.ranks.locked_hint", txt("rank.%s.name" % RUNGS[order - 2]["id"]))

    py = PILL_TOP
    K.paste(layer, K.round_rect(PILL_W, PILL_H, 28, (10, 18, 46), .82), cx, py)
    K.paste(layer, K.round_rect(PILL_W, PILL_H, 28, (255, 255, 255), .13, width=3), cx, py)
    K.paste(layer, icon("ic_star", PILL_H * .52, K.CREAM), cx - PILL_W / 2 + PILL_H * .42, py)
    left = cx - PILL_W / 2 + PILL_H * .82
    px = K.shrunk_left(layer, pill, left, py - (PILL_H - 4) / 2, PILL_W - PILL_H * .82 - 16, PILL_H - 4,
                       26, 16, fill=K.CREAM, outline=3)
    MEASURED.append(("pill '%s'" % pill, px, 16))

    if grow != 1.0:
        layer = layer.resize((int(round(layer.width * grow)), int(round(layer.height * grow))), Image.LANCZOS)
    # Pivoted at its foot: the cluster stands on the stage's bottom edge and grows upward.
    room.alpha_composite(layer, (int((W - layer.width) / 2), int(sh - layer.height)))
    print("  hero cluster at x%.2f in a %d stage" % (grow, sh))
    sheet.alpha_composite(room, (0, int(top)))


def rail(sheet, bottom, held, chosen):
    """`RanksScreen.BuildRail` + `PaintRail`: every rung as a seat, chained."""
    n = len(RUNGS)
    cy = bottom - RAIL_H / 2
    pitch = min(SEAT_PITCH_MOST, (PLATE_W - 40) / n)
    seat = min(SEAT, pitch - 10)
    x0 = W / 2 - pitch * (n - 1) / 2

    # The chain first, under every seat.
    for i in range(n - 1):
        tone = metal(i + 1)
        length = pitch - seat - LINK_GAP * 2
        on = (i + 1) < held
        K.paste(sheet, K.round_rect(length, LINK_THICK, LINK_THICK / 2,
                                    tone if on else (255, 243, 220), .85 if on else .14),
                x0 + pitch * i + pitch / 2, cy)

    for i, rung in enumerate(RUNGS):
        order = i + 1
        x = x0 + pitch * i
        tone = metal(order)
        standing = standing_of(order, held)
        earned = standing in ("held", "earned")
        nxt = standing == "next"
        scale = SEAT_CHOSEN if order == chosen else 1.0
        s = seat * scale

        if earned:
            K.paste(sheet, K.glow(int(s * 1.9), 2.0, tone, .34), x, cy)
        elif nxt:
            K.paste(sheet, K.glow(int(s * 1.9), 2.0, tone, .18), x, cy)
            # The pulse, caught mid-breath.
            K.paste(sheet, ring(s + 16 + 24, 8 * 128 / (s + 16), tone, .45), x, cy)

        K.paste(sheet, K.skin("Hud/slot", s, s), x, cy)
        K.paste(sheet, K.round_rect(s, s, 20, tone, .95 if earned else (.75 if nxt else .30), width=5), x, cy)
        face = SEAT_FACE if (earned or nxt) else SEAT_FACE_LOCKED
        K.paste(sheet, badge(rung["id"], s * face), x, cy)

        if not earned and not nxt:
            lx = x + s / 2 - LOCK_CHIP * .34
            ly = cy + s / 2 - LOCK_CHIP * .34
            K.paste(sheet, K.skin("sq_dark", LOCK_CHIP, LOCK_CHIP), lx, ly)
            K.paste(sheet, icon("ic_lock", LOCK_CHIP * .58, LOCKED_INK), lx, ly)


def plate(sheet, band_bottom, band_h, held, chosen):
    """`RanksScreen.BuildPlate` + `PaintPlate`: the chosen rung's lines, each with its bar."""
    order = chosen
    rung = RUNGS[order - 1]
    lines = rung["requires"]
    tone = metal(order)
    standing = standing_of(order, held)
    live = standing != "locked"

    height = PLATE_HEAD + len(lines) * LINE_H + PLATE_FOOT
    top = band_bottom - band_h
    cy = top + height / 2
    K.paste(sheet, K.skin("Hud/plate_navy" if live else "Hud/panel", PLATE_W, height), W / 2, cy)
    K.paste(sheet, K.round_rect(PLATE_W, height, 30, tone, .55 if live else .22, width=6), W / 2, cy)

    well_w = PLATE_W - WELL_INSET * 2
    well_left = W / 2 - well_w / 2
    head_y = top + PLATE_HEAD / 2

    title = txt("ui.ranks.requirements").upper()
    px = K.shrunk_left(sheet, title, well_left, head_y - 17, well_w * .6, 34, 28, 15,
                       fill=tone if live else LOCKED_INK, outline=2)
    MEASURED.append(("plate title '%s'" % title, px, 15))

    met = sum(1 for l in lines if progress(l, held, order) >= l["target"])
    count = txt("ui.ranks.fraction", met, len(lines))
    K.text(sheet, count, well_left + well_w - K.font(30).getlength(count) / 2, head_y, 30,
           fill=K.MINT if met >= len(lines) else K.CREAM, outline=2)

    line_y = top + PLATE_HEAD + LINE_H / 2
    for line in lines:
        have = progress(line, held, order)
        done = have >= line["target"]
        K.paste(sheet, K.skin("Hud/trough", well_w, WELL_H), W / 2, line_y)

        mx = well_left + 22 + MARK / 2
        if done:
            K.paste(sheet, icon("ic_check", MARK, K.MINT), mx, line_y)
        else:
            K.paste(sheet, K.round_rect(MARK, MARK, MARK / 2, tone, .70 if live else .45, width=5), mx, line_y)

        said_x = well_left + 22 + MARK + 18
        said_w = well_w - (said_x - well_left) - COUNT_W - 30
        said = sentence(line)
        px = K.shrunk_left(sheet, said, said_x, line_y - 13 - 19, said_w, 38, 32, 18,
                           fill=K.CREAM if live else LOCKED_INK, outline=2)
        MEASURED.append(("line '%s'" % said, px, 18))

        K.paste(sheet, K.round_rect(said_w, BAR_H, BAR_H / 2, (0, 0, 0), .42), said_x + said_w / 2, line_y + 21)
        frac = min(1.0, have / float(line["target"]))
        if frac > 0:
            K.paste(sheet, K.round_rect(said_w * frac, BAR_H, BAR_H / 2, K.MINT if done else BAR_ORANGE),
                    said_x + said_w * frac / 2, line_y + 21)

        fraction = txt("ui.ranks.fraction", have, line["target"])
        px = K.shrunk(sheet, fraction, well_left + well_w - 22 - COUNT_W / 2, line_y, COUNT_W, WELL_H - 10,
                      32, 18, fill=K.MINT if done else (K.GOLD if live else LOCKED_INK), outline=2)
        MEASURED.append(("fraction '%s'" % fraction, px, 18))
        line_y += LINE_H


def page(held, chosen=None, tall=False):
    """The page, on a 16:9 canvas or a 20:9 one. `chosen` is the rung on the stage; the screen
    opens on the held rung, or the first when none is held (`RanksScreen.Opening`)."""
    height = TALL_H if tall else H
    if chosen is None:
        chosen = held if held > 0 else 1
    chosen = max(1, min(chosen, len(RUNGS)))

    sheet = Image.new("RGBA", (W, height), (*K.GROUND, 255))
    K.plain(sheet)

    band = plate_band()
    # `RanksScreen.StageFit`: the room between the header and the bands is what the cluster is
    # scaled against; the stage is cut to the scaled cluster and everything under it rises by
    # whatever is spare, which lands at the foot of the page.
    floor = FOOT_PAD + band + PLATE_GAP + RAIL_H
    room = height - HEADER_H - floor
    grow = min(max(room / STAGE_LEAST, 1.0), STAGE_MOST)
    spare = max(0.0, room - STAGE_LEAST * grow)
    plate_bottom = height - FOOT_PAD - spare
    rail_bottom = plate_bottom - band - PLATE_GAP
    stage_bottom = rail_bottom - RAIL_H

    stage(sheet, HEADER_H, stage_bottom, held, chosen)
    header(sheet)
    rail(sheet, rail_bottom, held, chosen)
    plate(sheet, plate_bottom, band, held, chosen)

    stage_h = stage_bottom - HEADER_H
    print("  stage %.0f tall on a %d canvas (least %.0f); plate band %.0f; %.0f spare at the foot"
          % (stage_h, height, STAGE_LEAST, band, spare))
    if stage_h < STAGE_LEAST - .5:
        print("  STAGE SHORTER THAN ITS LEAST - the bands do not fit this canvas")
    return sheet.convert("RGB")


# ------------------------------------------------------------------ the map's corner
def map_corner(held):
    """`RankBadge` where `LevelsScreen` puts it: under the back key, above the boost clock.

    **The other half of the change, and the half with a neighbour.** The page can be judged on
    its own; the badge cannot, because what can be wrong with it is where it sits against the
    two controls it shares a column with. Nothing here is ghosted: the map's badge is drawn
    solid even for an account below the first rung, and what says *not yet* is the word under
    it. It is drawn on the ranked lane only; `render_endless.py` draws this same corner inside
    the whole hub.
    """
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.plain(sheet)

    K.paste(sheet, K.skin("sq_blue", CORNER, CORNER), CORNER_X, CORNER_Y)
    K.paste(sheet, icon("ic_left", 59, K.CREAM), CORNER_X, CORNER_Y)

    rank_y = CORNER_Y + CORNER / 2 + BOOST_GAP + (MARK_SIZE + NAME_BLOCK_H) / 2
    rid = RUNGS[held - 1]["id"] if held else RUNGS[0]["id"]

    K.paste(sheet, badge(rid, MARK_SIZE),
            CORNER_X, rank_y - (MARK_SIZE + NAME_BLOCK_H) / 2 + MARK_SIZE / 2)

    name = txt("rank.%s.name" % rid) if held else txt("ui.ranks.unranked")
    px = K.shrunk(sheet, name, CORNER_X, rank_y - (MARK_SIZE + NAME_BLOCK_H) / 2 + MARK_SIZE + NAME_BLOCK_H / 2,
                  BADGE_W, NAME_BLOCK_H, 32, 18, fill=K.GOLD if held else (255, 243, 220), outline=0)
    MEASURED.append(("map badge '%s'" % name, px, 18))

    boost_y = rank_y + (MARK_SIZE + NAME_BLOCK_H) / 2 + BOOST_GAP + 80 / 2
    K.paste(sheet, K.round_rect(BADGE_W, 80, 12, (255, 243, 220), .22, width=2), CORNER_X, boost_y)
    K.text(sheet, "boost", CORNER_X, boost_y, 20, fill=(255, 243, 220, 140), outline=0)
    return sheet


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--held", type=int, default=3, help="how many rungs are earned")
    ap.add_argument("--show", type=int, default=None, help="which rung is on the stage (default: the held one)")
    ap.add_argument("--tall", action="store_true", help="a 20:9 phone rather than a 16:9 one")
    ap.add_argument("--map", action="store_true", help="the badge in the map's chrome")
    ap.add_argument("--contact", action="store_true", help="every state side by side")
    ap.add_argument("--out", type=Path, default=Path("ranks.png"))
    args = ap.parse_args()

    if not RUNGS:
        sys.exit("progression.json has no 'ranks' block; there is nothing to draw")

    n = len(RUNGS)
    if args.contact:
        # Unranked; three held; the top; three held with the next chosen; three held with a
        # locked rung chosen; and every remaining rung walked onto the stage so each name,
        # blurb and sentence is measured once.
        states = [(0, None), (3, None), (n, None), (3, 4), (3, 6)]
        seen = {s[1] or (s[0] if s[0] else 1) for s in states}
        states += [(3, o) for o in range(1, n + 1) if o not in seen]
        shots = [page(h, c) for h, c in states]
        cell = 540
        sheet = Image.new("RGB", (cell * len(shots), int(cell * H / W)), K.GROUND)
        for i, s in enumerate(shots):
            sheet.paste(s.resize((cell, int(cell * H / W)), Image.LANCZOS), (i * cell, 0))
        out = sheet
    elif args.map:
        out = map_corner(max(0, min(args.held, n))).convert("RGB")
    else:
        out = page(max(0, min(args.held, n)), args.show, args.tall)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)

    print("  %d caption(s) measured" % len(MEASURED))
    tight = sorted(MEASURED, key=lambda m: m[1] - m[2])[:8]
    for what, px, floor in tight:
        flag = "  <- AT ITS FLOOR, truncated" if px <= floor else ""
        print("    %-58s %2dpx (floor %d)%s" % (what[:58], px, floor, flag))

    at_floor = [m for m in MEASURED if m[1] <= m[2]]
    if at_floor:
        print("  %d caption(s) settled at their floor; widen the band or shorten the string"
              % len(at_floor))

    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    sys.exit(main())
