# -*- coding: utf-8 -*-
"""Draws `RankUpOverlay` - the rank ceremony - at the size a phone draws it.

    python Tools/render_rank_ceremony.py               # the settled state, mid-ladder
    python Tools/render_rank_ceremony.py --gather      # the motes on their way in
    python Tools/render_rank_ceremony.py --strike      # the frame the badge lands
    python Tools/render_rank_ceremony.py --rung 1      # the first rank, climbed from nothing
    python Tools/render_rank_ceremony.py --rung 7      # the top of the ladder
    python Tools/render_rank_ceremony.py --long        # a 24-rung ladder, the reader's bound
    python Tools/render_rank_ceremony.py --bare        # a rung with no badge and no name
    python Tools/render_rank_ceremony.py --captions    # measure every line against its box
    python Tools/render_rank_ceremony.py --contact     # every state side by side

**Why this exists.** The ceremony is the loudest thing in the game and there is no numeric gate
anywhere that can open a PNG. Every question it raises is a picture: does the badge read at 440
against a fan that is three times its size; does the rail at the foot read as a *ladder* or as a
row of dots; does the name sit clear of the rule under it; is there anything left on screen at
the bottom of a 4:3 canvas. The Editor cannot photograph a `ScreenSpaceOverlay` canvas either,
so this is where it is judged - the way every board in this project is (`CRAFT.md`).

**And it measures, which is the half a picture cannot do.** `UIKit.Shrinkable` truncates
silently, so a caption that does not fit is not one that wraps - it is one with its end cut off,
and the tell is Best Fit settling at its floor (invariant 19n). `--captions` runs every rank
name and every blurb the shipped ladder can say through the boxes this screen draws them in and
prints what each settles at. A rank ladder is **content** (invariant 52a): a live-ops retune
that renames a rung or lengthens a blurb is one push away at all times, and nothing else in this
project would ever notice.

**It reads the shipped content.** The rungs, their names, their blurbs and the ladder's length
come out of `progression.json` and `loc/en.json`, and the badges off the disc - so a retune
redraws rather than going stale. A mirror carrying its own numbers answers questions about a
screen the game does not draw (invariant 44d).

**What it deliberately does not draw.** The sequence. Nothing here says whether the breath
before the strike lands, whether the motes read as *the things you did* rather than as sparks,
or whether the rail lighting bottom-to-top reads as a climb - those are the questions for a
device, and they are written down in `CLAUDE.md` where they can be answered. What the sheet
answers is where everything sits and whether it fits, which is what a render is strong at
(invariant 44d's own note).

**Three states are drawn that no player may ever see**, and they are the reason this file is
worth its length: `--long` is the 24-rung ladder `RankLadder.MaxRungs` allows, `--rung 1` is the
climb from nothing (no badge rises up the shaft, because there is none below), and `--bare` is
the rung whose badge and strings this build has never heard of - reachable the day a content
push adds a rung ahead of the client reading it. All three have to look deliberate.
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
RANK_ART = K.UI / "Rank"

LOC = {e["key"]: e["text"]
       for e in json.loads((CONTENT / "loc" / "en.json").read_text(encoding="utf-8"))["entries"]}
TABLE = json.loads((CONTENT / "progression.json").read_text(encoding="utf-8"))

RUNGS = (TABLE.get("ranks") or {}).get("rungs") or []

# ------------------------------------------------------------- RankUpOverlay's own numbers
#: The bands, as middles. `UIKit.Box` pivots at centre whatever it is anchored to.
EYEBROW_Y, BADGE_Y, NAME_Y, RULE_Y = 690.0, 300.0, -80.0, -180.0
BLURB_Y, RAIL_Y, RAIL_TEXT_Y, ACT_Y = -280.0, -470.0, -550.0, -730.0

#: `RankUpOverlay.EyebrowH` is 56 rather than 44 because of this file: a shrinkable label
#: measures a line box of 1.2x its size, so 44pt type in a 44-unit band settles at 36. Every
#: band here is at least 1.2x the type it carries, and `--captions` is what proves it.
EYEBROW_H, NAME_H, RULE_H, BLURB_H = 56.0, 140.0, 10.0, 140.0
RAIL_TEXT_H, ACT_H = 44.0, 150.0

BADGE_SIZE, HALO_SIZE, FAN_SIZE = 440.0, 980.0, 1320.0
SHAFT_W, SHAFT_GLOW_W = 300.0, 720.0
RAIL_W, RAIL_TROUGH, PIP_MAX = 860.0, 12.0, 34.0
#: `RankUpOverlay.PipHeldScale` - the rung reached stays larger than the rest once the light has
#: stopped moving, because "which one is me" has to be answerable from the rail alone.
PIP_HELD_SCALE = 1.4
CLIMB = 76.0

VIGNETTE_A, FAN_A, FAN2_A = .78, .30, .20
HALO_A, SHAFT_A, SHAFT_GLOW_A = .52, .34, .26

#: `RankLook.Ghost` - the one alpha on this screen, worn by the rung below as it rises.
GHOST = .38

#: `RankLook.Metals`, in ladder order. Measured off the badges themselves; the C# says which
#: two were settled by eye and why.
METALS = [
    (0xF0, 0x8A, 0x46),   # 1 Cinderling  - copper
    (0xC6, 0xD8, 0xEE),   # 2 Silverwatch - steel
    (0xFF, 0xB5, 0x24),   # 3 Goldbrand   - gold
    (0x9A, 0x4C, 0xF2),   # 4 Duskcrown   - violet
    (0xF6, 0x4A, 0x38),   # 5 Fireheart   - crimson
    (0x2F, 0x9C, 0xFF),   # 6 Frozencrest - ice
    (0x3B, 0xE9, 0xD8),   # 7 Gemfire     - prism, at its cyan end
]

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


def hsv(colour):
    r, g, b = [c / 255.0 for c in colour]
    mx, mn = max(r, g, b), min(r, g, b)
    v, d = mx, mx - mn
    s = 0.0 if mx == 0 else d / mx
    if d == 0:
        h = 0.0
    elif mx == r:
        h = ((g - b) / d % 6) / 6.0
    elif mx == g:
        h = ((b - r) / d + 2) / 6.0
    else:
        h = ((r - g) / d + 4) / 6.0
    return h, s, v


def rgb(h, s, v):
    i = int(h * 6) % 6
    f = h * 6 - int(h * 6)
    p, q, t = v * (1 - s), v * (1 - f * s), v * (1 - (1 - f) * s)
    r, g, b = [(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)][i]
    return int(r * 255), int(g * 255), int(b * 255)


def scheme(colour):
    """`RankUpOverlay.SchemeFor` - the room's tint, partner, accent and deep, off the metal.

    Narrow and symmetric rather than a fifth of a turn round the wheel, and this file is why:
    gold's hue is .105, so +.19 is .295, and the first contact sheet drew Goldbrand arriving in
    a green room. The C# carries the argument.
    """
    h, s, v = hsv(colour)
    partner = rgb((h - .07) % 1.0, min(1.0, s * .92), v)
    accent = rgb((h + .06) % 1.0, min(1.0, s * .70), min(1.0, v * 1.14))
    deep = rgb(h, min(1.0, s * .90), v * .14)
    return colour, partner, accent, deep


def lerp(a, b, t):
    return tuple(int(x + (y - x) * t) for x, y in zip(a, b))


def lift(colour, t):
    """`Pal.Lift` - a colour taken toward white, never a tint.

    `Image.color` multiplies, so lightening a saturated metal by tinting it is impossible - it
    can only ever darken (invariant 44g). Every highlight in this screen is a lift.
    """
    return lerp(colour, (255, 255, 255), t)


def at(y, climbed=False):
    """Canvas y (centre origin, up positive) to sheet y (top origin, down positive)."""
    return H / 2 - y - (CLIMB if climbed else 0)


# --------------------------------------------------------------------------- the pieces
def badge(rid, box):
    """One rung's picture at `box` pixels, or nothing when this build does not carry it.

    **Nothing, and never a placeholder.** `RankArt.Paint` switches the node off rather than
    handing an `Image` a null sprite, which is a white rectangle at the graphic's own colour
    (invariant 7b) - so a mirror drawing a red square here would be reporting a state the game
    cannot reach, and one drawing a grey badge would be hiding the state it can.
    """
    path = RANK_ART / ("%s.png" % rid)
    if not path.exists():
        return None
    return K.fit(Image.open(path).convert("RGBA"), (box, box))


def ghosted(im, alpha):
    faded = im.copy()
    faded.putalpha(im.getchannel("A").point(lambda a: int(a * alpha)))
    return faded


def gradient(top, middle, bottom):
    """`Art.Gradient` over the full canvas - the room's sky."""
    im = Image.new("RGBA", (W, H))
    d = ImageDraw.Draw(im)
    for y in range(H):
        v = 1.0 - y / float(H - 1)
        c = lerp(bottom, middle, v * 2) if v < .5 else lerp(middle, top, (v - .5) * 2)
        d.line([(0, y), (W, y)], fill=(*c, 255))
    return im


def fan(box, colour, count, alpha, rotation=0.0):
    """`Art.Rays(size, count)` in the rung's metal."""
    box = int(box)
    im = K.rays(256, count).resize((box, box), Image.LANCZOS)
    if rotation:
        im = im.rotate(rotation, resample=Image.BICUBIC)
    out = Image.new("RGBA", im.size, (*colour, 0))
    out.putalpha(im.point(lambda v: int(v * alpha)))
    return out


def vignette(sheet, colour):
    vig = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(vig)
    steps = 90
    for i in range(steps):
        k = i / float(steps)
        inset = int(min(W, H) * .52 * (1 - k) * .5)
        d.ellipse([-W * .25 + inset, -H * .12 + inset, W * 1.25 - inset, H * 1.12 - inset],
                  outline=(*colour, int(255 * VIGNETTE_A / steps * 3)), width=max(2, int(H / steps)))
    sheet.alpha_composite(vig)


def shaft(sheet, colour):
    """The column the climb happens inside, and the light falling through it.

    The streaks are the whole of the ascent - nothing in this composition moves upward by more
    than `CLIMB`, so the rise is carried by the ground going the other way. Drawn at one instant
    here, which is the one thing about them a still cannot answer: whether the fall reads as
    fall rather than as rain.
    """
    glow = K.glow(int(SHAFT_GLOW_W * 2), 1.5, colour, SHAFT_GLOW_A)
    glow = glow.resize((int(SHAFT_GLOW_W), int(H * 1.1)), Image.LANCZOS)
    K.paste(sheet, glow, W / 2, at(BADGE_Y * .35))

    column = Image.new("RGBA", (int(SHAFT_W), H), (0, 0, 0, 0))
    d = ImageDraw.Draw(column)
    for y in range(H):
        v = abs(y - H / 2) / (H / 2)
        d.line([(0, y), (SHAFT_W, y)], fill=(*colour, int(255 * SHAFT_A * .9 * (1 - v))))
    K.paste(sheet, column, W / 2, at(BADGE_Y * .35))

    # Fourteen, at the phases one frame of the loop happens to catch.
    for i in range(14):
        thickness = 4 + (i * 7) % 5
        length = 120 + (i * 97) % 300
        x = W / 2 - SHAFT_W * .42 + (i * 173) % int(SHAFT_W * .84)
        k = ((i * 0.137) + 0.21) % 1.0
        alpha = math.sin(k * math.pi) * .55
        y = at(1180 - 2360 * k)

        bar = Image.new("RGBA", (int(thickness), int(length)), (0, 0, 0, 0))
        bd = ImageDraw.Draw(bar)
        for yy in range(int(length)):
            edge = 1 - abs(yy - length / 2) / (length / 2)
            bd.line([(0, yy), (thickness, yy)],
                    fill=(*lift(colour, .5),
                          int(255 * alpha * edge)))
        sheet.alpha_composite(bar, (int(x), int(y - length / 2)))


def rail(sheet, held, count, colour, climbed=True):
    """The ladder itself: one pip per rung, lit to the one reached.

    **The half that makes it a rank rather than a prize.** A badge alone says "you got a thing";
    the same badge over a rail with three of seven lamps lit says where you are and that there is
    further to go. Drawn from the ladder's own length, so a retune redraws.
    """
    cy = at(RAIL_Y)

    trough = K.round_rect(RAIL_W, RAIL_TROUGH, RAIL_TROUGH / 2, K.CREAM, .16)
    K.paste(sheet, trough, W / 2, cy)

    step = RAIL_W / (count - 1) if count > 1 else 0.0
    #: `held` of nought is the rail before the climb: every pip dark and no fill at all, which is
    #: how it stands through the whole gathering.
    reach = (held - 1) / float(count - 1) if count > 1 else (1.0 if held else 0.0)
    if reach > 0:
        fill = K.round_rect(RAIL_W * reach, RAIL_TROUGH, RAIL_TROUGH / 2, colour, .9)
        sheet.alpha_composite(fill, (int(W / 2 - RAIL_W / 2), int(cy - RAIL_TROUGH / 2)))

    pip = min(PIP_MAX, step * .72) if count > 1 else PIP_MAX

    for i in range(count):
        ordinal = i + 1
        x = W / 2 - RAIL_W / 2 + step * i if count > 1 else W / 2

        if ordinal < held:
            disc = K.round_rect(pip, pip, pip / 2, metal(ordinal), .92)
        elif ordinal == held:
            # The rung reached: its own metal at full, and popped larger than the rest.
            disc = K.round_rect(pip * PIP_HELD_SCALE, pip * PIP_HELD_SCALE,
                                pip * PIP_HELD_SCALE / 2, lift(metal(ordinal), .35), 1.0)
        else:
            disc = K.round_rect(pip, pip, pip / 2, K.CREAM, .22)

        K.paste(sheet, disc, x, cy)


def captions(sheet, rung, ordinal, count, report):
    """Every line this screen says, measured against the box it is drawn in."""
    colour = metal(ordinal)

    px = K.shrunk(sheet, txt("ui.rankup.title"), W / 2, at(EYEBROW_Y), 900, EYEBROW_H, 44, 26,
                  fill=lift(colour, .35), outline=3)
    report.append(("eyebrow", px, 26))

    name = LOC.get("rank.%s.name" % rung["id"], "")
    if name:
        px = K.shrunk(sheet, name, W / 2, at(NAME_Y), 940, NAME_H, 100, 52, fill=K.CREAM, outline=5)
        report.append(("name  %-12s" % rung["id"], px, 52))

    rule = K.round_rect(420, RULE_H, RULE_H / 2, colour, .85)
    K.paste(sheet, rule, W / 2, at(RULE_Y))

    blurb = LOC.get("rank.%s.blurb" % rung["id"], "")
    if blurb:
        px = K.shrunk(sheet, blurb, W / 2, at(BLURB_Y) + 20, 880, BLURB_H, 38, 24,
                      fill=(255, 243, 220), outline=3)
        report.append(("blurb %-12s" % rung["id"], px, 24))

    px = K.shrunk(sheet, txt("ui.ranks.held_count", ordinal, count), W / 2, at(RAIL_TEXT_Y),
                  880, RAIL_TEXT_H, 34, 22, fill=(255, 243, 220), outline=3)
    report.append(("rail count", px, 22))


def key(sheet, report):
    """`Skins.Settled` - green, because nothing here costs anything."""
    plate = K.skin("btn_green", 620, ACT_H)
    K.paste(sheet, plate, W / 2, at(ACT_Y))
    px = K.shrunk(sheet, txt("ui.rankup.onward"), W / 2, at(ACT_Y) - ACT_H * 0.0231, 460, 66,
                  46, 26, fill=K.CREAM, outline=0)
    report.append(("key label", px, 26))


# ---------------------------------------------------------------------------- the states
def draw(ordinal, rungs, beat="settled", bare=False):
    """One frame. `beat` is `gather`, `strike` or `settled`."""
    count = len(rungs)
    rung = rungs[ordinal - 1]
    colour = metal(ordinal)
    tint, partner, accent, deep = scheme(colour)

    sheet = gradient(lerp(deep, partner, .22), deep, lerp(deep, (0, 0, 0), .55))

    for home, size, alpha, nth in ((( -380, 660), 1180, .20, partner),
                                   ((  420, 140), 1000, .16, accent),
                                   (( -260, -640), 1260, .13, tint)):
        blob = K.glow(int(size), 1.7, nth, alpha)
        K.paste(sheet, blob, W / 2 + home[0], at(home[1]))

    shaft(sheet, colour)
    vignette(sheet, lerp(deep, (0, 0, 0), .6))

    climbed = beat == "settled"
    face = None if bare else badge(rung["id"], BADGE_SIZE)

    if beat == "gather":
        # The lines that were met, on their way in. One mote per requirement, at the phase a
        # frame two thirds of the way through the flight happens to catch.
        lines = max(1, len(rung.get("requires") or []))
        size = 120 + (52 - 120) * min(1.0, (lines - 1) / 7.0)

        below = rungs[ordinal - 2] if ordinal > 1 else None
        if below is not None:
            rising = badge(below["id"], BADGE_SIZE * .42 * .62)
            if rising is not None:
                K.paste(sheet, ghosted(rising, GHOST), W / 2, at(BADGE_Y * .4))

        core = K.glow(260, 2.4, lift(colour, .55), .72)
        K.paste(sheet, core, W / 2, at(BADGE_Y))

        for i in range(lines):
            t = .66
            angle = math.pi * .5 + i * math.pi * 2 / lines + 1.15 * math.pi * 2 * t
            r = 1250 * (1 - t)
            spark = K.glow(int(size * 1.6), 1.9, (tint, partner, accent)[i % 3], .9)
            K.paste(sheet, spark, W / 2 + math.cos(angle) * r, at(BADGE_Y + math.sin(angle) * r * .78))

        # The rail stands there dark from the first frame - it is built by `Build`, not by a
        # beat, and nothing fades it in. A mirror that left it out would be reporting an emptier
        # screen than the game draws, on the one piece of furniture this ceremony is about
        # (invariant 44d).
        rail(sheet, 0, count, colour)

        K.shrunk(sheet, txt("ui.rankup.title"), W / 2, at(EYEBROW_Y), 900, EYEBROW_H, 44, 26,
                 fill=lift(colour, .35), outline=3)
        return sheet

    # The fans bloom on the strike and stay for the rest of it.
    K.paste(sheet, fan(FAN_SIZE, tint, 14, FAN_A), W / 2, at(BADGE_Y, climbed))
    K.paste(sheet, fan(FAN_SIZE * .72, partner, 7, FAN2_A, 17), W / 2, at(BADGE_Y, climbed))
    K.paste(sheet, K.glow(int(HALO_SIZE), 1.8, colour, HALO_A), W / 2, at(BADGE_Y, climbed))

    if beat == "strike":
        for i, ring in enumerate((440, 700, 980, 1240)):
            band = K.round_rect(ring, ring, ring / 2, (tint, partner, accent)[i % 3],
                                .8 * (1 - i / 4.0), width=10)
            K.paste(sheet, band, W / 2, at(BADGE_Y))

    if face is not None:
        K.paste(sheet, face, W / 2, at(BADGE_Y, climbed))

    report = []
    if beat == "settled":
        captions(sheet, rung, ordinal, count, report)
        rail(sheet, ordinal, count, colour)
        key(sheet, report)
    else:
        rail(sheet, 0, count, colour)
        K.shrunk(sheet, txt("ui.rankup.title"), W / 2, at(EYEBROW_Y), 900, EYEBROW_H, 44, 26,
                 fill=lift(colour, .35), outline=3)

    MEASURED.extend(report)
    return sheet


def synthetic(count):
    """A ladder of `count` rungs, for the reader's own bound (`RankLadder.MaxRungs`).

    The shipped names are reused so the type is real; past the seventh the ids repeat, which is
    fine - nothing here is keyed on one, which is the property being tested.
    """
    out = []
    for i in range(count):
        base = RUNGS[i % len(RUNGS)] if RUNGS else {"id": "cinderling", "requires": []}
        out.append(dict(base))
    return out


def save(sheet, name):
    path = K.REPO / "Tools" / "out" / name
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(path, quality=92)
    print("  wrote %s" % path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rung", type=int, default=3, help="which rung was reached (1-based)")
    ap.add_argument("--gather", action="store_true", help="the motes on their way in")
    ap.add_argument("--strike", action="store_true", help="the frame the badge lands")
    ap.add_argument("--long", action="store_true", help="a 24-rung ladder")
    ap.add_argument("--bare", action="store_true", help="a rung with no badge and no strings")
    ap.add_argument("--captions", action="store_true", help="measure every line, draw nothing")
    ap.add_argument("--contact", action="store_true", help="every state side by side")
    args = ap.parse_args()

    if not RUNGS:
        sys.exit("progression.json carries no `ranks` block, so there is no ladder to draw")

    if args.captions:
        blank = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        for i, rung in enumerate(RUNGS):
            captions(blank, rung, i + 1, len(RUNGS), MEASURED)
        key(blank, MEASURED)
        report()
        return 0

    if args.contact:
        states = [
            ("gather", draw(3, RUNGS, "gather")),
            ("strike", draw(3, RUNGS, "strike")),
            ("rung 1", draw(1, RUNGS, "settled")),
            ("rung 3", draw(3, RUNGS, "settled")),
            ("rung 7", draw(len(RUNGS), RUNGS, "settled")),
            ("24 rungs", draw(17, synthetic(24), "settled")),
            ("no art", draw(3, RUNGS, "settled", bare=True)),
        ]
        scale = .40
        tw, th = int(W * scale), int(H * scale)
        sheet = Image.new("RGBA", (tw * len(states), th + 54), (10, 14, 22, 255))
        for i, (label, im) in enumerate(states):
            sheet.alpha_composite(im.convert("RGBA").resize((tw, th), Image.LANCZOS),
                                  (tw * i, 54))
            K.text(sheet, label, tw * i + tw / 2, 28, 26)
        save(sheet, "rank_ceremony_contact.png")
        report()
        return 0

    beat = "gather" if args.gather else "strike" if args.strike else "settled"
    rungs = synthetic(24) if args.long else RUNGS
    ordinal = max(1, min(args.rung, len(rungs)))

    save(draw(ordinal, rungs, beat, bare=args.bare),
         "rank_ceremony_%s_%d.png" % (beat, ordinal))
    report()
    return 0


def report():
    if not MEASURED:
        return

    print("\n  settled font sizes (floor in brackets) - a line ON its floor is a line that is")
    print("  about to be truncated rather than shrunk (invariant 19n):")
    for what, px, floor in MEASURED:
        flag = "  <-- ON THE FLOOR" if px <= floor else ""
        print("    %-22s %3d  (%d)%s" % (what, px, floor, flag))


if __name__ == "__main__":
    sys.exit(main())
