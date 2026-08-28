# -*- coding: utf-8 -*-
"""Bakes the three painted layers the launch screen is built from.

    python Tools/make_splash_art.py            # write them
    python Tools/make_splash_art.py --check    # prove the shipped PNGs are what this writes

Three outputs, all under `Assets/Game/Art/Bg/`:

  * `splash_isle.png`  — the hero island: a floating shelf of the grove's own ground
    with the grove's own props standing on it, and a tapering root hanging under it.
  * `splash_far.png`   — four smaller islands, hazed back, for depth behind the hero.
  * `splash_mist.png`  — a horizontally **tileable** cloud strip, so the screen can
    scroll two copies of it against each other for ever without a seam.

Everything else the splash draws — the sky, the stars, the conduit, the glow at every
lantern — is generated at runtime out of `Art`, so it costs no delivered texture and no
load before the first frame. These three are here because a painted island is the one
thing code cannot make.

Four decisions worth not re-litigating.

**The island is composed from the shipped grove catalog, not from a source pack.** Every
prop on it is an id in `homestead.json` drawn at that entry's own `scale` and `lift`,
through the geometry in `GroveFloor`. So the launch screen is a picture of the thing the
player is being sold, it cannot drift stylistically from the Grovement, and a drop that
re-cuts `cottage` re-cuts the splash by being re-run. It is the argument
`make_chapter_art.py` makes for grading a backdrop out of a chapter's own colours, one
layer up.

**The root is derived from the ground's own silhouette.** The underside is the alpha of
the composed ground layer, re-drawn a few dozen times, each copy a little smaller, a
little lower and a little darker. Hand-drawing a root would be one more thing to redraw
whenever the island's footprint changes; deriving it means the footprint is the only
thing anybody edits.

**The islands are lit here and tinted there.** These bake at full daylight with a fixed
warm/cool gradient across them, and the screen tints the *sprite* from a cold night
value up to white as the load runs. Baking a night version and a day version would be
two textures to keep in step for an effect one `Image.color` already gives — and it is
the tint, rather than a second bake, that lets dawn arrive *continuously*.

**`--check` is the gate.** Regenerating art nobody diffed is how a pipeline rots. This
compares byte for byte against what is checked in, so a change to the layout table that
nobody re-ran fails a run rather than shipping the old picture.
"""
from __future__ import annotations

import argparse
import json
import math
import random
import sys
from pathlib import Path

try:
    from PIL import Image, ImageChops, ImageDraw, ImageFilter
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

Image.MAX_IMAGE_PIXELS = None

REPO = Path(__file__).resolve().parent.parent
CONTENT = REPO / "Assets" / "StreamingAssets" / "Content"
ART = REPO / "Assets" / "Game" / "Art"
OUT = ART / "Bg"

# ------------------------------------------------------------------ geometry
# GroveFloor / render_grove.py. Kept as literals rather than imported because this
# script has to run on a clone with nothing installed, and they have not moved in
# the life of the project — see render_grove.py for what each one is measured off.
TILE_W = 220.0
FACE_RATIO = 0.5628
TILE_H = TILE_W * FACE_RATIO
TILE_OVERLAP = 1.06
PIECE_SCALE = 1.15


def tile_x(col: int, row: int) -> float:
    return (col - row) * TILE_W * 0.5


def tile_y(col: int, row: int) -> float:
    return (col + row) * TILE_H * 0.5


def order(col: int, row: int) -> int:
    return (col + row) * 1024 + col


# ------------------------------------------------------------------- the isle
# The hero island's footprint and what stands on it. A clean 5x4 diamond with two
# opposite corners taken out and a two-tile spur on the right, so the silhouette
# reads as grown rather than as a rectangle seen at an angle.
HERO_TILES = (
    [(c, r) for c in range(5) for r in range(4) if (c, r) not in {(0, 3), (4, 0)}]
    + [(5, 1), (5, 2)]
)

# tile -> piece id. Back to front is (col + row) ascending, so the cottage sits on a
# low sum and the small bright things on a high one.
HERO_PROPS = {
    (1, 0): "tree_gold",
    (2, 1): "cottage",
    (3, 0): "oak_broad",
    (5, 1): "tree_amber",
    (0, 1): "willow",
    (4, 1): "pine_night",
    (0, 2): "great_stump",
    (5, 2): "sprout_teal",
    (3, 2): "mushroom_log",
    (1, 2): "blossom",
    (2, 3): "lily_pads",
    (3, 3): "daisies",
    (1, 3): "rune_stone",
}

# Islands behind the hero: (tiles across, tiles deep, props, scale, haze, x, y).
# Deliberately sparse — a distant island with a village on it competes with the one
# the eye is meant to land on.
FAR_ISLES = [
    (2, 2, {(0, 0): "pine_night", (1, 1): "tree_night"}, 0.46, 0.62, 0.13, 0.20),
    (2, 1, {(1, 0): "tree_amber"}, 0.32, 0.74, 0.83, 0.09),
    (3, 2, {(0, 1): "oak_broad", (2, 0): "tree_gold"}, 0.38, 0.68, 0.70, 0.47),
    (1, 1, {}, 0.24, 0.80, 0.29, 0.62),
]

HAZE = (150, 176, 205)          # what distance fades an island toward

ROOT_STEPS = 160
# The root is two flat facets and a band of dirt under the grass line, which is how
# the islands in the game's own hub backdrop are painted. A soft gradient was tried
# first and reads as a shadow rather than as rock: what makes stylised stone look
# like stone is the hard edge down the middle, not the shading either side of it.
ROOT_DIRT = (0x8E, 0x5E, 0x46)
ROOT_LIT = (0x63, 0x79, 0x8C)
ROOT_DARK = (0x44, 0x56, 0x68)
ROOT_TIP = (0x28, 0x34, 0x42)

_sprites: dict[str, Image.Image | None] = {}


def sprite(art: str) -> Image.Image | None:
    if art not in _sprites:
        path = ART / (art + ".png")
        if not path.exists():
            folder = ART / art
            frames = sorted(folder.glob("*.png")) if folder.is_dir() else []
            path = frames[0] if frames else None
        _sprites[art] = Image.open(path).convert("RGBA") if path and path.exists() else None
        if _sprites[art] is None:
            print(f"  note: no art for {art}", file=sys.stderr)
    return _sprites[art]


def catalog() -> tuple[dict, dict]:
    h = json.loads((CONTENT / "homestead.json").read_text(encoding="utf8"))
    pieces = {p["id"]: p for p in h["pieces"]}
    return h["floor"], pieces


def lerp_rgb(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


# --------------------------------------------------------------- island build
def build_island(tiles, props, floor, pieces, scale=1.0, root=True, pad=40):
    """One floating island, returned tight to its own content.

    Ground first, then the root derived from the ground's silhouette, then everything
    standing on it back to front — the same order and the same numbers `GroveFieldView`
    draws in, so a piece that looks right in the Grovement looks right here.
    """
    xs = [tile_x(c, r) for c, r in tiles]
    ys = [tile_y(c, r) for c, r in tiles]
    min_x, max_x = min(xs) - TILE_W, max(xs) + TILE_W
    min_y, max_y = min(ys) - TILE_H * 6, max(ys) + TILE_H * 3

    root_drop = (max_x - min_x) * 1.05 if root else 0.0
    W = int((max_x - min_x) * scale) + pad * 2
    H = int((max_y - min_y + root_drop) * scale) + pad * 2

    ground = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    stand = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    def paste(target, img, cx, cy, w, h, flip=False):
        if img is None or w < 1 or h < 1:
            return
        s = img.resize((max(1, int(w * scale)), max(1, int(h * scale))), Image.LANCZOS)
        if flip:
            s = s.transpose(Image.FLIP_LEFT_RIGHT)
        target.alpha_composite(
            s,
            (int((cx - min_x) * scale + pad - s.width / 2),
             int((cy - min_y) * scale + pad - s.height / 2)),
        )

    tile_art = sprite(floor["tileArt"])
    if tile_art is not None:
        aspect = tile_art.height / tile_art.width
        gw = TILE_W * TILE_OVERLAP
        gh = gw * aspect
        drop = (gh - TILE_H * TILE_OVERLAP) * 0.5
        for (c, r) in sorted(tiles, key=lambda t: order(*t)):
            paste(ground, tile_art, tile_x(c, r), tile_y(c, r) + drop, gw, gh)

    for (c, r) in sorted(props.keys(), key=lambda t: order(*t)):
        piece = pieces.get(props[(c, r)])
        if piece is None:
            print(f"  note: unknown piece {props[(c, r)]}", file=sys.stderr)
            continue
        img = sprite(piece["art"])
        if img is None:
            continue
        w = img.width * float(piece.get("scale", 1.0)) * PIECE_SCALE
        h = img.height * float(piece.get("scale", 1.0)) * PIECE_SCALE
        lift = h * float(piece.get("lift", 0.45))
        paste(stand, img, tile_x(c, r), tile_y(c, r) - lift, w, h)

    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    if root:
        out.alpha_composite(build_root(ground))
    out.alpha_composite(ground)
    out.alpha_composite(stand)
    return out


def build_root(ground: Image.Image) -> Image.Image:
    """The underside, drawn as a profile rather than assembled out of copies.

    The obvious construction — take the ground's alpha, re-draw it a few dozen times
    each a little smaller and a little lower — does not work and it is worth saying
    why, because it looks like it should. Consecutive copies overlap almost entirely,
    so the silhouette that survives is the *envelope*, which is just the widest copy
    stretched downward: a flat ellipse, whatever the shrink curve says. A root is a
    width per depth, so that is what this writes down.

    Drawn at twice size and resized once, which is the whole of the anti-aliasing, and
    shaded by two ramps at right angles — warm on the sunlit right, cold into the tip —
    because a single flat colour under a lit island reads as a shadow rather than rock.
    """
    box = ground.getbbox()
    if box is None:
        return Image.new("RGBA", ground.size, (0, 0, 0, 0))

    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    cx = (x0 + x1) / 2.0

    half = w * 0.46                      # a shade narrower than the grass shelf above it
    reach = w * 0.62
    top = y0 + h * 0.55                  # starts behind the island, so no seam shows
    SS = 2

    def profile(u):
        """Half-width at depth `u` in 0..1. Two bulges, so it reads as grown rock."""
        core = (1.0 - u) ** 0.62
        return core * (1.0 + 0.085 * math.sin(u * 7.4) - 0.05 * u)

    left, right = [], []
    steps = 120
    for i in range(steps + 1):
        u = i / steps
        y = top + reach * u
        hw = half * profile(u)
        lean = math.sin(u * 2.1) * w * 0.030
        left.append(((cx - hw + lean) * SS, y * SS))
        right.append(((cx + hw + lean) * SS, y * SS))

    mask = Image.new("L", (ground.width * SS, ground.height * SS), 0)
    ImageDraw.Draw(mask).polygon(left + right[::-1], fill=255)
    mask = mask.resize(ground.size, Image.LANCZOS)

    # Two facets split on the centre line, a dirt band under the grass, and the whole
    # thing cooling into the tip. Written per scanline because the split moves with the
    # lean, and stepped across in threes because nothing here changes that fast.
    body = Image.new("RGB", ground.size)
    px = body.load()
    for y in range(ground.height):
        u = min(1.0, max(0.0, (y - top) / reach))
        lean = math.sin(u * 2.1) * w * 0.030
        dirt = max(0.0, 1.0 - u / 0.16)                # a hand's width of earth, then rock
        lit = lerp_rgb(lerp_rgb(ROOT_LIT, ROOT_TIP, u ** 1.7), ROOT_DIRT, dirt)
        dark = lerp_rgb(lerp_rgb(ROOT_DARK, ROOT_TIP, u ** 1.7),
                        lerp_rgb(ROOT_DIRT, ROOT_TIP, 0.28), dirt)
        split = cx + lean - half * 0.16 * (1.0 - u)
        for x in range(0, ground.width, 3):
            c = lit if x < split else dark
            for dx in range(3):
                if x + dx < ground.width:
                    px[x + dx, y] = c

    out = body.convert("RGBA")
    out.putalpha(mask)
    return out


def grade(img: Image.Image, warm=(1.06, 1.01, 0.92), cool=(0.80, 0.86, 1.02)) -> Image.Image:
    """A fixed light across the island: warm from the upper right, cool into the lower left.

    Multiplied over the whole thing including the root, so the props and the ground they
    stand on are lit by one lamp rather than each carrying its own.
    """
    W, H = img.size
    ramp = Image.new("RGB", (W, H))
    px = ramp.load()
    for y in range(H):
        for x in range(0, W, 4):
            t = 0.5 + 0.5 * ((x / W - 0.5) * 0.7 - (y / H - 0.5) * 0.7)
            c = tuple(int(255 * (cool[i] + (warm[i] - cool[i]) * t)) for i in range(3))
            for dx in range(4):
                if x + dx < W:
                    px[x + dx, y] = c

    base = img.convert("RGB")
    lit = Image.blend(base, ImageChops.multiply(base, ramp), 0.85)
    lit.putalpha(img.getchannel("A"))
    return lit


def haze(img: Image.Image, amount: float, alpha: float = 1.0) -> Image.Image:
    """Fade an island toward the sky. Distance, in one number."""
    flat = Image.new("RGBA", img.size, HAZE + (255,))
    out = Image.blend(img.convert("RGBA"), flat, amount)
    a = img.getchannel("A")
    if alpha < 1.0:
        a = a.point(lambda v: int(v * alpha))
    out.putalpha(a)
    return out


def trim(img: Image.Image, pad: int = 8) -> Image.Image:
    box = img.getbbox()
    if box is None:
        return img
    x0, y0, x1, y1 = box
    x0, y0 = max(0, x0 - pad), max(0, y0 - pad)
    x1, y1 = min(img.width, x1 + pad), min(img.height, y1 + pad)
    return img.crop((x0, y0, x1, y1))


# ------------------------------------------------------------------- the mist
def build_mist(width=1024, height=380, seed=7) -> Image.Image:
    """A cloud strip that tiles horizontally.

    Every puff is drawn three times, one period apart, on a canvas three periods
    wide; the middle period is then cut out. That is what makes the blur seamless —
    blurring a single-width canvas and hoping is the version with a visible seam.
    """
    rng = random.Random(seed)
    W = width * 3
    band = Image.new("L", (W, height), 0)
    d = ImageDraw.Draw(band)

    for _ in range(120):
        x = rng.uniform(0, width)
        y = rng.uniform(height * 0.18, height * 0.86)
        rx = rng.uniform(width * 0.05, width * 0.18)
        ry = rx * rng.uniform(0.18, 0.34)
        v = rng.randint(70, 190)
        for k in range(3):
            cx = x + width * k
            d.ellipse((cx - rx, y - ry, cx + rx, y + ry), fill=v)

    band = band.filter(ImageFilter.GaussianBlur(width * 0.035))
    band = band.crop((width, 0, width * 2, height))

    # Faded out at the top and solid at the foot, so it settles as a bank rather than
    # hanging as a stripe with an edge on it.
    env = Image.new("L", (1, height))
    epx = env.load()
    for y in range(height):
        u = y / (height - 1)
        e = 0.0 if u <= 0.0 else min(1.0, (u / 0.62) ** 1.6)
        epx[0, y] = int(255 * e)
    env = env.resize((width, height))
    band = ImageChops.multiply(band, env)

    out = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    tint = Image.new("RGBA", (width, height), (226, 236, 248, 255))
    tint.putalpha(band.point(lambda v: int(v * 0.78)))
    out.alpha_composite(tint)
    return out


# -------------------------------------------------------------------- writing
def build_all():
    floor, pieces = catalog()

    hero = grade(build_island(HERO_TILES, HERO_PROPS, floor, pieces, scale=1.0))
    hero = trim(hero)

    far_w, far_h = 1200, 1400
    far = Image.new("RGBA", (far_w, far_h), (0, 0, 0, 0))
    for (cols, rows, props, scale, back, fx, fy) in FAR_ISLES:
        tiles = [(c, r) for c in range(cols) for r in range(rows)]
        isle = trim(grade(build_island(tiles, props, floor, pieces, scale=scale)))
        isle = haze(isle, back, alpha=1.0 - back * 0.35)
        far.alpha_composite(
            isle, (int(fx * far_w - isle.width / 2), int(fy * far_h - isle.height / 2))
        )

    return {"splash_isle": hero, "splash_far": far, "splash_mist": build_mist()}


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true",
                    help="fail if the shipped PNGs differ from what this would write")
    args = ap.parse_args()

    made = build_all()
    OUT.mkdir(parents=True, exist_ok=True)

    bad = []
    for name, img in made.items():
        path = OUT / f"{name}.png"
        import io
        buf = io.BytesIO()
        img.save(buf, "PNG", optimize=True)
        data = buf.getvalue()

        if args.check:
            if not path.exists() or path.read_bytes() != data:
                bad.append(name)
            continue

        path.write_bytes(data)
        print(f"  wrote {path.relative_to(REPO)}  {img.width}x{img.height}")

    if args.check:
        if bad:
            sys.exit("stale, re-run without --check: " + ", ".join(bad))
        print("splash art is what the tool would write")


if __name__ == "__main__":
    main()
