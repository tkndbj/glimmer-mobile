#!/usr/bin/env python3
"""
Draw the bonus wheel exactly as the game draws it, without Unity.

    python Tools/render_wheel.py                       # the shipped wheel
    python Tools/render_wheel.py --spin 3              # stopped on slice 3, lit
    python Tools/render_wheel.py --slices 100,300,500,900 --base 250

## Why this exists

`render_grove.py`'s argument, for the one other object in this game whose quality is only
visible as a picture. Everything about the wheel that can be proved is proved — where it
comes to rest, which slice the seed picked, what the slice pays, that both runtimes agree
about all three (`BonusWheelTests`, `RewardVectorTests`, `firebase/functions/test`). None
of that can say whether eight figures at 45 degrees of arc are legible on a phone, whether
the colour ramp reads as worse-to-better, or whether a twelve-slice wheel a content push
could publish tomorrow would overlap its own captions.

Opening Unity to look is a domain reload, a play session and a screenshot per iteration,
which is far too slow a loop to *design* against — and the wheel's whole job is to be
looked at.

So this reimplements the drawing half of `WheelFace` against the same numbers:

  * the wedge is `Art.Wedge`'s distance field - an outer rim, a hub bore, and an angular
    edge feathered against **arc length** rather than against angle, so the softness is the
    same width in pixels at the hub and at the rim
  * slice i is drawn (i + 1/2) steps clockwise from the pointer, which is exactly what
    `WheelSpin.Rest` inverts
  * the tint is `WheelPaint.For` - ranked within its own wheel, never keyed on a percentage
  * the figure is `slice.Pays(base)` and the badge is the multiplier, at `LabelRadius` and
    `BadgeRadius` of the radius

It is a *renderer*, deliberately: it takes no view on whether a ladder is good. That is
what eyes are for, and this is what puts the picture in front of them.
"""

import argparse
import math
import os
import sys

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:
    sys.exit("this needs Pillow: python -m pip install pillow")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
PROGRESSION = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "progression.json")

# ---------------------------------------------------------------------- the numbers
# WheelFace's geometry, as fractions of the radius.
HUB_FRACTION = .24
LABEL_RADIUS = .655
BADGE_RADIUS = .45
RIM_THICKNESS = .075
LAMP_SIZE = .052
LAMP_RADIUS = .945
POINTER_SIZE = .21

# Pal, and WheelPaint's ramp in its own order. Cool to warm, because the wheel has to be
# ranked at a glance while it is moving and six unrelated hues rank as six colours.
PAL = {
    "azure":   (0x4F, 0xC1, 0xFF),
    "aqua":    (0x3B, 0xE9, 0xD8),
    "verdant": (0x54, 0xE4, 0x8C),
    "sun":     (0xFF, 0xC9, 0x3C),
    "amber":   (0xFF, 0x8A, 0x2B),
    "gold":    (0xFF, 0xC2, 0x3C),
    "bloom":   (0xFF, 0x74, 0xD4),
    "cream":   (0xFF, 0xF3, 0xDC),
    "ink":     (0x20, 0x30, 0x3F),
}

# Worst to best. The top rung is deliberately not gold: gold is what the rim, the hub and
# the lamps are made of, so a gold jackpot is the one prize with nothing to stand against -
# and Pal.Gold and Pal.Sun differ by seven parts in a hundred, which wasted a rung.
RAMP = ["azure", "aqua", "verdant", "sun", "amber", "bloom"]

# What a wedge is settled against: the panel's own dark, not black.
DEEP = (18, 28, 38)

FEATHER = 1.35


def cover(d):
    """Art.Cover: coverage from a signed distance, in pixels."""
    return max(0.0, min(1.0, .5 - d / FEATHER))


def tint_for(percents, index):
    """WheelPaint.For - the slice's place in the *order* of this wheel's figures.

    Ranked among the distinct values rather than along the span between worst and best:
    interpolating the percentage collapses on any ladder with a real top prize, and the
    shipped one put five of its eight slices on one colour.
    """
    distinct = sorted(set(percents))
    if len(distinct) <= 1:
        return PAL[RAMP[0]]

    rank = distinct.index(percents[index]) * (len(RAMP) - 1) // (len(distinct) - 1)
    return PAL[RAMP[max(0, min(len(RAMP) - 1, rank))]]


def seat(tint, index):
    """WheelPaint.Seat - blended toward the panel's dark, never multiplied.

    Scaling a saturated yellow toward zero is exactly how olive is made, which is what the
    first cut did to the two best prizes. A lerp keeps the hue and moves only the value.
    Every other wedge goes a shade deeper, so two neighbours that legitimately land on one
    colour still read as two slices.
    """
    k = .14 if index % 2 == 0 else .26
    return tuple(int(c + (d - c) * k) for c, d in zip(tint, DEEP))


def compact(n):
    """Compact.Number, near enough for a preview."""
    return f"{n:,}"


def multiplier(percent):
    """WheelFace.Mult - '2' for double, '2.5' for two and a half, never '3.0'."""
    if percent % 100 == 0:
        return str(percent // 100)
    return f"{percent / 100:.2f}".rstrip("0").rstrip(".")


def fit(text, size, room, minimum):
    """UIKit.Shrinkable: never grows, shrinks until the line fits its own box.

    Modelled rather than skipped, because without it this file reports a twelve-slice wheel
    as a pile-up the game would never draw - a preview whose whole job is to be judged by eye
    must not lie in the direction of "this is broken" any more than in the other one.
    """
    while size > minimum:
        f = font(size)
        if f.getlength(text) <= room:
            return f
        size -= 1

    return font(minimum)


def font(size, bold=True):
    for name in ("arialbd.ttf" if bold else "arial.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


# ---------------------------------------------------------------------- the drawing
def wedge_mask(size, count, hub=HUB_FRACTION):
    """Art.Wedge: one wedge pointing straight up, with the hub bored out of it."""
    h = size * .5
    outer = h - 1.5
    inner = outer * hub
    half = math.pi / count

    mask = Image.new("L", (size, size))
    px = mask.load()

    for y in range(size):
        for x in range(size):
            dx, dy = x + .5 - h, (size - 1 - y) + .5 - h        # PIL's y runs down
            r = math.hypot(dx, dy)
            if r < 1e-4:
                continue

            a = abs(math.atan2(dx, dy))

            # The angular edge is feathered against arc length, not against the angle:
            # fading on angle alone gives a wedge crisp outside and blurred in the middle.
            alpha = min(cover(r - outer), cover(inner - r), cover(r * (a - half)))
            px[x, y] = int(alpha * 255 + .5)

    return mask


def pointer_mask(size):
    """Art.Pointer: a teardrop, point down, from Art.SdRoundCone."""
    h = size * .5
    ax, ay, ra = h, size * .74, size * .23
    bx, by, rb = h, size * .12, size * .015

    mask = Image.new("L", (size, size))
    px = mask.load()

    for y in range(size):
        for x in range(size):
            fx, fy = x + .5, (size - 1 - y) + .5
            bax, bay = bx - ax, by - ay
            l2 = bax * bax + bay * bay
            t = max(0.0, min(1.0, ((fx - ax) * bax + (fy - ay) * bay) / l2))
            cx, cy = ax + bax * t, ay + bay * t
            d = math.hypot(fx - cx, fy - cy) - (ra + (rb - ra) * t)
            px[x, y] = int(cover(d) * 255 + .5)

    return mask


def render(percents, base, landed, diameter=560, out="wheel.png"):
    count = len(percents)
    step = 360.0 / count
    radius = diameter * .5

    pad = int(diameter * .22)
    w = h = diameter + pad * 2
    img = Image.new("RGBA", (w, h), (18, 26, 34, 255))
    draw = ImageDraw.Draw(img)

    cx, cy = w // 2, h // 2

    # The seat: a dark disc a shade wider than the wedges, which is what gives every slice
    # an outline without eight of them each needing one.
    draw.ellipse([cx - radius - 7, cy - radius - 7, cx + radius + 7, cy + radius + 7],
                 fill=(13, 23, 33, 255))

    # ------------------------------------------------------------------ the slices
    # Each slice is drawn **upright, whole** — the wedge, its figure and its chip together —
    # and the finished tile is rotated once. That is not a shortcut, it is the structure
    # `WheelFace` has: the caption is a *child* of the wedge, so it cannot end up on a
    # different one. Drawing the wedges and then the labels in two passes is what this file
    # did first, and the two passes disagreed about which way round the wheel went — every
    # figure sat on the wrong prize's colour, in a preview whose whole job is to be judged
    # by eye.
    base_mask = wedge_mask(320, count).resize((diameter, diameter), Image.LANCZOS)
    arc = 2 * radius * LABEL_RADIUS * math.sin(math.radians(step * .5))
    chip_w = int(min(max(70, arc * .92) * .72, 118))

    for i, percent in enumerate(percents):
        tint = tint_for(percents, i)
        lit = landed is not None and i == landed
        colour = tint if lit else seat(tint, i)
        alpha = 242 if lit else (255 if landed is None else 102)

        tile = Image.new("RGBA", (diameter, diameter), (0, 0, 0, 0))
        tile.paste(Image.new("RGBA", (diameter, diameter), colour + (alpha,)), (0, 0), base_mask)

        td = ImageDraw.Draw(tile)
        mid = diameter // 2
        pays = base if percent <= 100 else base * percent // 100

        figure = compact(pays)
        td.text((mid, mid - radius * LABEL_RADIUS), figure,
                font=fit(figure, 46, max(70, arc * .92), 22),
                fill=PAL["cream"] + (255,), anchor="mm",
                stroke_width=4, stroke_fill=(14, 22, 32, 240))

        if percent > 100:
            # A cream chip with dark text on it: the wedge is already the slice's own colour,
            # so a badge painted in that colour is the one thing here guaranteed to have
            # nothing to sit against.
            by = mid - radius * BADGE_RADIUS
            td.rounded_rectangle([mid - chip_w // 2, by - 23, mid + chip_w // 2, by + 23],
                                 radius=18, fill=PAL["cream"] + (237,))
            badge = "x" + multiplier(percent)
            td.text((mid, by), badge, font=fit(badge, 30, chip_w - 14, 16),
                    fill=(51, 36, 26, 255), anchor="mm")

        # Slice i's centre goes (i + 1/2) steps *clockwise* from the pointer, which is what
        # WheelSpin.Rest inverts. PIL rotates counter-clockwise, so the sign flips.
        spun = tile.rotate(-(i + .5) * step, resample=Image.BICUBIC)
        img.alpha_composite(spun, (cx - mid, cy - mid))

    # A dark bar on every boundary, from the hub to the rim. It is what makes a wheel read as
    # a wheel rather than as a pie chart, and it is what lets the shading above stay a hint:
    # separating two same-coloured neighbours by depth alone means taking one far enough down
    # to tell apart, and far enough is where a saturated yellow becomes khaki.
    hub_r = radius * HUB_FRACTION
    for i in range(count):
        a = math.radians(i * step)
        sx, sy = math.sin(a), -math.cos(a)
        draw.line([cx + sx * hub_r, cy + sy * hub_r, cx + sx * radius, cy + sy * radius],
                  fill=(13, 20, 28, 217), width=4)

    # The rim and the lamps set into it - one on every boundary, so the lamps and the slice
    # edges are the same thing rather than two decorations that nearly line up.
    rim = int(diameter * RIM_THICKNESS)
    draw.ellipse([cx - radius - 8, cy - radius - 8, cx + radius + 8, cy + radius + 8],
                 outline=PAL["gold"] + (255,), width=rim)

    lamp = diameter * LAMP_SIZE * .5
    for i in range(count):
        # On the boundaries, so a lamp is the peg the pointer clicks past.
        a = math.radians(i * step)
        lx = cx + math.sin(a) * radius * LAMP_RADIUS
        ly = cy - math.cos(a) * radius * LAMP_RADIUS
        draw.ellipse([lx - lamp, ly - lamp, lx + lamp, ly + lamp], fill=(255, 247, 214, 255))

    # The hub, over the points of every wedge.
    hub = diameter * HUB_FRACTION * .5
    draw.ellipse([cx - hub, cy - hub, cx + hub, cy + hub], fill=(23, 36, 48, 255),
                 outline=PAL["gold"] + (255,), width=9)
    draw.ellipse([cx - hub * .26, cy - hub * .26, cx + hub * .26, cy + hub * .26],
                 fill=PAL["gold"] + (255,))

    # The pointer, hung above the rim with its tip on it.
    size = int(diameter * POINTER_SIZE)
    tip = pointer_mask(128).resize((size, size), Image.LANCZOS)
    layer = Image.new("RGBA", (size, size), PAL["cream"] + (255,))
    top = cy - radius * .99
    img.paste(layer, (cx - size // 2, int(top - size * .12)), tip)

    img.save(out)
    return out


def shipped():
    import json
    doc = json.load(open(PROGRESSION, encoding="utf-8"))
    ads = doc.get("ads") or {}
    slices = [int(s["percent"]) for s in (ads.get("wheel") or {}).get("slices") or []]

    base = 0
    for placement in ads.get("placements") or []:
        if placement.get("id") == "win_bonus":
            base = placement.get("amount", 0)

    return slices, base


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--slices", help="comma-separated percentages; default is the shipped wheel")
    ap.add_argument("--base", type=int, help="what one flat view pays; default is win_bonus")
    ap.add_argument("--spin", type=int, help="draw it stopped on this slice, lit")
    ap.add_argument("--size", type=int, default=560)
    ap.add_argument("--out", default="wheel.png")
    args = ap.parse_args()

    slices, base = shipped()
    if args.slices:
        slices = [int(p) for p in args.slices.split(",")]
    if args.base:
        base = args.base

    if not slices:
        sys.exit("no wheel authored in progression.json, and none given with --slices")

    path = render(slices, base or 200, args.spin, args.size, args.out)
    print(f"{len(slices)} slice(s) over a base of {base}: " +
          "  ".join(f"{p}%" for p in slices))
    print(f"wrote {path}")


if __name__ == "__main__":
    main()
