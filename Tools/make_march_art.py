# -*- coding: utf-8 -*-
"""Draws Hollowmarch's road, its pods and the two overlays that hang on them.

    python Tools/make_march_art.py --check                  # prove the shipped PNGs are these
    python Tools/make_march_art.py --write
    python Tools/make_march_art.py --contact march_art.png

Writes `Assets/Game/Art/March/*.png`. `AddressableAutoRegister` addresses them as they
import, so nothing here touches the Addressables settings (invariant 7a).

**Composed rather than cut, and that is invariant 32b's lesson taken the first time rather
than the second.** The Iron Quarry approximated its cage out of a ruin pack's wooden posts,
passed every numeric gate, and read on a phone as three brown logs — firewood, in a mode
whose entire goal was the thing behind them. Nothing in seventeen licensed isometric
tilesets is a glowing energy pod either, and a pod is this board's *noun*: it is what the
player looks at, counts, matches and taps forty times a level. So the twelve sprites the
board is made of are drawn here, where their size, their contrast against the plate and
their silhouettes are decisions rather than accidents. The licensed art still does the work
it is good at — the cast that moves and the explosions — and none of that is touched here.

**Four colours, four silhouettes.** Every pod carries a mark inside it: a star, a leaf, a
diamond, a ring. It costs nothing and it is the difference between a board a colour-blind
player can read and one they cannot, which `CRAFT.md` already names as the standing cost of
grading a board by hue. It also makes the line readable at a glance when it is drawn small,
which is every board past the first.

**The road tile is rotationally symmetric on purpose.** A haul-road bends, forks nowhere and
runs in all four directions, so a tile with a direction in it would need four of itself and
would still be wrong on a corner. A square plate with a round socket in the middle reads as
"something travels along here" whichever way the road goes, and one sprite draws the lot.

**`--check` proves reproducibility and says nothing about quality.** `--contact` is the eye:
every sprite laid out at the size the board really draws it, on the board's own plate. Look
at it.
"""
from __future__ import annotations

import argparse
import io
import sys
from pathlib import Path

try:
    import numpy as np
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow and numpy:  python -m pip install pillow numpy")

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Art" / "March"

#: How wide a shipped sprite is. The board draws a cell at roughly 70-90 canvas units on a
#: phone, so this is a little over two device pixels per canvas unit — the same honest
#: ceiling `make_shop_art` uses, and comfortably under the 512 texture cap `ArtImportRules`
#: gives this folder.
SIZE = 192

#: Everything is drawn at this multiple and resampled down once, which is the cheapest
#: antialiasing there is and the only one that is the same on every machine.
OVER = 4

# --------------------------------------------------------------------------- the palette
# Four hues, chosen apart in *lightness* as well as in hue so the line still reads when the
# screen is dim or the player is not seeing colour the usual way. Amber is the brightest and
# blue the deepest, which is what stops the two greens-and-blues problem.
HUES = {
    "r": ((255, 78, 72), (168, 28, 40), "star"),
    "g": ((86, 219, 118), (26, 128, 72), "leaf"),
    "b": ((84, 170, 255), (26, 82, 176), "gem"),
    "y": ((255, 200, 66), (176, 116, 18), "ring"),
}

ROAD = (74, 82, 96)
ROAD_DARK = (44, 50, 62)
ROAD_LIT = (120, 150, 178)
GROUND = (30, 30, 38)
GROUND_SPECK = (44, 44, 54)
STONE = (126, 120, 114)
STONE_DARK = (66, 62, 60)
STEEL = (188, 196, 208)
STEEL_DARK = (78, 86, 100)
VOID = (150, 92, 255)
VOID_CORE = (232, 214, 255)


def canvas(scale=OVER):
    return Image.new("RGBA", (SIZE * scale, SIZE * scale), (0, 0, 0, 0))


def down(img):
    return img.resize((SIZE, SIZE), Image.LANCZOS)


def box(inset, scale=OVER):
    n = SIZE * scale
    return (inset * scale, inset * scale, n - inset * scale - 1, n - inset * scale - 1)


# --------------------------------------------------------------------------- the pods

def orb(bright, deep, mark):
    """A glossy sphere with a mark in it and a soft glow around it.

    Drawn in numpy rather than with `ImageDraw`, because the whole read of a pod is the
    *gradient* - a flat disc with a highlight painted on reads as a sticker, and a sphere
    reads as an object sitting on the road. Three terms: a lambert falloff from a light up
    and to the left, a rim light on the far side so the silhouette does not dissolve into a
    dark plate, and a specular blob.
    """
    n = SIZE * OVER
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    cx = cy = (n - 1) / 2.0
    r = n * 0.40

    dx = (x - cx) / r
    dy = (y - cy) / r
    d2 = dx * dx + dy * dy

    inside = d2 <= 1.0
    z = np.sqrt(np.clip(1.0 - d2, 0.0, 1.0))

    # A light up and to the left, and slightly toward the viewer.
    lx, ly, lz = -0.45, -0.55, 0.70
    lam = np.clip(dx * lx + dy * ly + z * lz, 0.0, 1.0)

    bright = np.array(bright, np.float32)
    deep = np.array(deep, np.float32)

    body = deep + (bright - deep) * (0.28 + 0.72 * lam)[..., None]

    # Rim light: the far edge picks the board's own cool light up, so a dark pod on a dark
    # plate still has an outline that is not a black line drawn round it.
    rim = np.clip((d2 - 0.62) / 0.38, 0.0, 1.0) ** 2
    away = np.clip(-(dx * lx + dy * ly), 0.0, 1.0)
    body += (np.array([180, 205, 235], np.float32) - body) * (rim * away * 0.55)[..., None]

    # Specular: a small bright blob, offset toward the light.
    sx, sy = cx - r * 0.34, cy - r * 0.40
    s2 = ((x - sx) ** 2 + (y - sy) ** 2) / (r * 0.30) ** 2
    spec = np.exp(-s2 * 2.4)
    body += (255.0 - body) * (spec * 0.85)[..., None]

    rgba = np.zeros((n, n, 4), np.float32)
    rgba[..., :3] = np.clip(body, 0, 255)
    rgba[..., 3] = np.where(inside, 255.0, 0.0)

    # A soft halo in the pod's own colour, so a line of them reads as lit rather than as
    # painted-on. Kept well under the body's alpha or the board turns to soup.
    halo = np.clip((1.35 - np.sqrt(d2)) / 0.9, 0.0, 1.0) ** 3
    outside = ~inside
    rgba[..., 3] = np.where(outside, halo * 90.0, rgba[..., 3])
    rgba[..., :3] = np.where(outside[..., None], bright[None, None, :], rgba[..., :3])

    img = Image.fromarray(rgba.astype(np.uint8), "RGBA")
    stamp(img, mark)
    return down(img)


def stamp(img, mark):
    """The mark inside a pod: the half of telling four colours apart that is not colour."""
    n = SIZE * OVER
    face = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    pen = ImageDraw.Draw(face)

    c = n / 2.0
    r = n * 0.40
    ink = (255, 255, 255, 118)

    if mark == "star":
        pts = []
        for i in range(8):
            ang = np.pi * i / 4.0 - np.pi / 2.0
            reach = r * (0.52 if i % 2 == 0 else 0.20)
            pts.append((c + np.cos(ang) * reach, c + np.sin(ang) * reach))
        pen.polygon(pts, fill=ink)
    elif mark == "leaf":
        pen.polygon([(c, c - r * 0.50), (c + r * 0.44, c + r * 0.34),
                     (c - r * 0.44, c + r * 0.34)], fill=ink)
    elif mark == "gem":
        pen.polygon([(c, c - r * 0.52), (c + r * 0.42, c),
                     (c, c + r * 0.52), (c - r * 0.42, c)], fill=ink)
    else:
        wide = r * 0.46
        thin = r * 0.26
        pen.ellipse((c - wide, c - wide, c + wide, c + wide), fill=ink)
        pen.ellipse((c - thin, c - thin, c + thin, c + thin), fill=(0, 0, 0, 0))

    face = face.filter(ImageFilter.GaussianBlur(n * 0.004))
    img.alpha_composite(face)


def spark():
    """The forged core: white-hot, rayed, and obviously not one of the four.

    It is deliberately the only thing on the board with *points* on it. A player holding one
    has to read it as a different kind of thing at a glance, or they will spend it on the
    nearest run - which is invariant 26g's whole complaint about a special that competes on
    degree rather than on kind, arriving through the art instead of through the rules.
    """
    img = canvas()
    pen = ImageDraw.Draw(img)
    n = SIZE * OVER
    c = n / 2.0
    r = n * 0.44

    for i in range(12):
        ang = np.pi * i / 6.0
        reach = r * (1.00 if i % 3 == 0 else 0.78)
        pen.polygon([(c + np.cos(ang) * reach, c + np.sin(ang) * reach),
                     (c + np.cos(ang + 0.26) * r * 0.30, c + np.sin(ang + 0.26) * r * 0.30),
                     (c + np.cos(ang - 0.26) * r * 0.30, c + np.sin(ang - 0.26) * r * 0.30)],
                    fill=(255, 214, 120, 210))

    core = orb((255, 246, 214), (238, 168, 40), None)
    core = core.resize((int(SIZE * 0.86), int(SIZE * 0.86)), Image.LANCZOS)

    out = down(img)
    at = (SIZE - core.width) // 2
    out.alpha_composite(core, (at, at))
    return out


# --------------------------------------------------------------------------- the road

def speckle(img, colour, seed, count, spread):
    """Deterministic grit. Never `random` without a seed - a texture nobody can reproduce is
    a texture `--check` cannot police."""
    rng = np.random.RandomState(seed)
    pen = ImageDraw.Draw(img)
    n = SIZE * OVER

    for _ in range(count):
        x, y = rng.randint(0, n, 2)
        size = rng.randint(int(n * 0.006), int(n * spread))
        pen.ellipse((x, y, x + size, y + size), fill=colour)


def ground():
    img = Image.new("RGBA", (SIZE * OVER,) * 2, GROUND + (255,))
    speckle(img, GROUND_SPECK + (255,), 11, 160, 0.020)
    return down(img)


def rubble():
    img = ground().resize((SIZE * OVER,) * 2, Image.NEAREST)
    pen = ImageDraw.Draw(img)
    n = SIZE * OVER
    c = n / 2.0

    for reach, fill in ((0.42, STONE_DARK), (0.36, STONE)):
        pen.polygon([(c - n * reach, c + n * reach * 0.72),
                     (c - n * reach * 0.55, c - n * reach * 0.60),
                     (c + n * reach * 0.30, c - n * reach * 0.82),
                     (c + n * reach, c + n * reach * 0.30),
                     (c + n * reach * 0.42, c + n * reach * 0.86)],
                    fill=fill + (255,))

    speckle(img, (168, 162, 154, 110), 23, 40, 0.014)
    return down(img)


def road(lit=False):
    """A slab with a socket in it: the same tile whichever way the road runs.

    **Full-bleed, and that is the whole of whether it reads as a road.** The first cut inset
    the slab and rounded its corners, which is right for a single tile and wrong for forty of
    them: laid end to end they read as a row of separate *sockets* with dark gaps between,
    so the haul-road - the one thing on the board that says where the convoy is going -
    disappeared into the ground. Nothing but a render could have shown that (`render_march.py`,
    invariant 32b). Edge to edge, adjacent slabs merge into one band and the sockets read as
    sleepers along it.
    """
    body = ROAD_LIT if lit else ROAD
    img = Image.new("RGBA", (SIZE * OVER,) * 2, (0, 0, 0, 0))
    pen = ImageDraw.Draw(img)
    n = SIZE * OVER

    pen.rectangle((0, 0, n - 1, n - 1), fill=body + (255,))

    # A lip along the top and left only, so a run of tiles reads as one surface catching one
    # light rather than as a grid of separately bevelled squares.
    lip = int(n * 0.035)
    pen.rectangle((0, 0, n - 1, lip), fill=tuple(min(255, int(v * 1.28)) for v in body) + (255,))
    pen.rectangle((0, 0, lip, n - 1), fill=tuple(min(255, int(v * 1.18)) for v in body) + (255,))
    pen.rectangle((0, n - 1 - lip, n - 1, n - 1),
                  fill=tuple(int(v * 0.72) for v in body) + (255,))

    # The socket. Deep enough to read as a groove the line runs in rather than as a dot.
    c = n / 2.0
    r = n * 0.30
    pen.ellipse((c - r, c - r, c + r, c + r), fill=ROAD_DARK + (255,))
    r *= 0.74
    pen.ellipse((c - r, c - r, c + r, c + r),
                fill=(tuple(int(v * (1.35 if lit else 0.78)) for v in ROAD_DARK)) + (255,))

    # Rivets, one per corner. Four tiles meeting put four of them together, which is what
    # says "plates laid down" rather than "a square painted on the ground".
    rv = n * 0.042
    for ox, oy in ((0.09, 0.09), (0.91, 0.09), (0.09, 0.91), (0.91, 0.91)):
        pen.ellipse((n * ox - rv, n * oy - rv, n * ox + rv, n * oy + rv),
                    fill=(tuple(min(255, int(v * 1.32)) for v in body)) + (255,))

    if lit:
        glow = img.filter(ImageFilter.GaussianBlur(n * 0.03))
        out = Image.new("RGBA", img.size, (0, 0, 0, 0))
        out.alpha_composite(glow)
        out.alpha_composite(img)
        img = out

    return down(img)


def portal():
    """The gate the raiders are walking to. Cold violet, because everything else on this
    board is warm - so the one thing the player is racing is the one thing that is not."""
    img = canvas()
    pen = ImageDraw.Draw(img)
    n = SIZE * OVER
    c = n / 2.0

    # A wash first, so the gate throws light onto the road in front of it. It is the thing the
    # player is racing, and a threat drawn only inside its own cell reads as scenery.
    for k, reach in enumerate((0.50, 0.44, 0.37, 0.30, 0.23, 0.16)):
        fade = 70 + k * 34
        pen.ellipse((c - n * reach, c - n * reach * 1.04,
                     c + n * reach, c + n * reach * 1.04),
                    outline=VOID + (fade,), width=int(n * 0.032))

    r = n * 0.13
    pen.ellipse((c - r, c - r * 1.04, c + r, c + r * 1.04), fill=VOID_CORE + (245,))

    img = img.filter(ImageFilter.GaussianBlur(n * 0.010))

    halo = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(halo).ellipse((c - n * 0.49, c - n * 0.49, c + n * 0.49, c + n * 0.49),
                                 fill=VOID + (58,))
    halo = halo.filter(ImageFilter.GaussianBlur(n * 0.05))

    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.alpha_composite(halo)
    out.alpha_composite(img)

    # A hard rim over the blur, or the whole thing reads as fog rather than as a hole.
    pen = ImageDraw.Draw(out)
    pen.ellipse((c - n * 0.50, c - n * 0.52, c + n * 0.50, c + n * 0.52),
                outline=(236, 220, 255, 235), width=int(n * 0.024))
    return down(out)


# --------------------------------------------------------------------------- the overlays

def cage():
    """Bars over a pod, and the one thing on the board the player is actually here for.

    An overlay rather than its own pod, so a caged pod keeps the colour it is matched by -
    which is what makes a cage a target a core can be aimed at rather than a separate kind
    of thing standing in the way.
    """
    img = canvas()
    pen = ImageDraw.Draw(img)
    n = SIZE * OVER
    c = n / 2.0
    r = n * 0.40

    # Every bar is cut to the chord of the sphere at its own x, so the cage sits *on* the pod
    # rather than in front of the whole tile. A rail wider than the thing it is caging reads
    # as scaffolding, which is invariant 32b's firewood arriving by a different route.
    for k in (-1.5, -0.5, 0.5, 1.5):
        ox = k * 0.40
        x = c + r * ox
        reach = np.sqrt(max(0.0, 1.0 - ox * ox)) * r * 0.90
        pen.line([(x, c - reach), (x, c + reach)],
                 fill=STEEL_DARK + (250,), width=int(n * 0.040))
        pen.line([(x - n * 0.006, c - reach), (x - n * 0.006, c + reach)],
                 fill=STEEL + (250,), width=int(n * 0.018))

    for oy in (-0.58, 0.58):
        y = c + r * oy
        reach = np.sqrt(max(0.0, 1.0 - oy * oy)) * r * 0.98
        pen.line([(c - reach, y), (c + reach, y)],
                 fill=STEEL_DARK + (255,), width=int(n * 0.058))
        pen.line([(c - reach, y - n * 0.010), (c + reach, y - n * 0.010)],
                 fill=STEEL + (255,), width=int(n * 0.024))

    lock = n * 0.070
    pen.ellipse((c - lock, c + r * 0.42, c + lock, c + r * 0.42 + lock * 2),
                fill=(255, 208, 96, 255), outline=(90, 62, 12, 255),
                width=int(n * 0.012))
    return down(img)


def plate():
    """A raider's plating: a band of rivets that flashes when a blast is turned away.

    Drawn as a band rather than as a full shell, because the raider under it has to stay
    recognisable - a player has to see *what* is armoured, not only that something is.
    """
    img = canvas()
    pen = ImageDraw.Draw(img)
    n = SIZE * OVER
    c = n / 2.0
    r = n * 0.44

    pen.arc((c - r, c - r, c + r, c + r), 186, 354, fill=STEEL_DARK + (255,),
            width=int(n * 0.110))
    pen.arc((c - r, c - r, c + r, c + r), 186, 354, fill=STEEL + (255,),
            width=int(n * 0.070))
    pen.arc((c - r * 1.02, c - r * 1.02, c + r * 1.02, c + r * 1.02), 186, 354,
            fill=(238, 246, 255, 255), width=int(n * 0.020))

    for deg in (196, 222, 248, 274, 300, 326, 348):
        ang = np.radians(deg)
        x, y = c + np.cos(ang) * r, c + np.sin(ang) * r
        rv = n * 0.030
        pen.ellipse((x - rv, y - rv, x + rv, y + rv), fill=(96, 106, 122, 255))
        rv *= 0.55
        pen.ellipse((x - rv, y - rv, x + rv, y + rv), fill=(240, 248, 255, 255))

    return down(img)


# --------------------------------------------------------------------------- assembly

def build():
    made = {
        "ground": ground(),
        "rubble": rubble(),
        "road": road(False),
        "road_lit": road(True),
        "portal": portal(),
        "pod_spark": spark(),
        "cage": cage(),
        "plate": plate(),
    }

    for key, (bright, deep, mark) in HUES.items():
        made["pod_" + key] = orb(bright, deep, mark)

    return made


# --------------------------------------------------------------------------- looking
#: The board's own plate, so a sprite is judged against the ground it will really be seen
#: on. Kept in step with `Pal.Board` by hand; it is a diagnostic, not a rendering.
PLATE = (18, 20, 28)
CELL = 96


def contact(made, path):
    """Every sprite at the size a cell really draws it, on the board's own plate."""
    names = sorted(made)
    cols = 6
    rows = (len(names) + cols - 1) // cols

    sheet = Image.new("RGB", (cols * CELL * 2, rows * CELL * 2), PLATE)
    for i, name in enumerate(names):
        cell = made[name].resize((CELL, CELL), Image.LANCZOS)
        x = (i % cols) * CELL * 2 + CELL // 2
        y = (i // cols) * CELL * 2 + CELL // 2
        sheet.paste(cell, (x, y), cell)

    # And a strip of the road with a line standing on it, which is the only way to see
    # whether the pods read against the tile rather than against a flat swatch.
    strip = Image.new("RGB", (CELL * 10, CELL * 2), PLATE)
    for k in range(10):
        strip.paste(made["road"].resize((CELL, CELL), Image.LANCZOS),
                    (k * CELL, CELL // 2), made["road"].resize((CELL, CELL), Image.LANCZOS))
    for k, key in enumerate(["pod_r", "pod_r", "pod_g", "pod_b", "pod_b", "pod_y",
                             "pod_spark", "pod_g", "pod_y", "pod_r"]):
        pod = made[key].resize((CELL, CELL), Image.LANCZOS)
        strip.paste(pod, (k * CELL, CELL // 2), pod)
    caged = made["cage"].resize((CELL, CELL), Image.LANCZOS)
    strip.paste(caged, (2 * CELL, CELL // 2), caged)
    strip.paste(caged, (5 * CELL, CELL // 2), caged)

    out = Image.new("RGB", (max(sheet.width, strip.width), sheet.height + strip.height),
                    PLATE)
    out.paste(sheet, (0, 0))
    out.paste(strip, (0, sheet.height))

    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    out.save(path)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true",
                    help="fail if the shipped PNGs differ from what this would write")
    ap.add_argument("--write", action="store_true", help="write them")
    ap.add_argument("--contact", type=Path, metavar="PNG",
                    help="also write a contact sheet at the size the board draws them")
    args = ap.parse_args()

    made = build()
    OUT.mkdir(parents=True, exist_ok=True)

    stale = []
    for name, img in sorted(made.items()):
        path = OUT / f"{name}.png"

        buf = io.BytesIO()
        img.save(buf, "PNG", optimize=True)
        data = buf.getvalue()

        if args.check:
            if not path.exists() or path.read_bytes() != data:
                stale.append(name)
            continue

        if args.write:
            path.write_bytes(data)
            print(f"  wrote {path.relative_to(REPO)}  {img.width}x{img.height}")

    if args.contact:
        contact(made, args.contact)
        print(f"  wrote {args.contact}  - look at it; --check cannot")

    if args.check:
        if stale:
            sys.exit("stale, re-run with --write: " + ", ".join(stale))
        print(f"Hollowmarch's board art is what the tool would write ({len(made)} sprites)")


if __name__ == "__main__":
    main()
