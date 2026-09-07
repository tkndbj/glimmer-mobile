# -*- coding: utf-8 -*-
"""Paints the board backdrops of the village world: forty night villages, one per colour.

    python Tools/make_village_art.py --source "C:/path/to/_extracted"
    python Tools/make_village_art.py --source "C:/path/to/_extracted" --check
    python Tools/make_village_art.py --source "C:/path/to/_extracted" --contact villages.png

Writes `Assets/Game/Art/Bg/village_00.png` .. `village_39.png`, 720x1280 RGB, exactly the
size and count of the skies they sit beside.

**A third world, and the same rule as the first two.** Every backdrop in this game is
arithmetic on where a level sits (`Tools/chapters/mapart.py`): the grove draws one cloud
painting at forty colours, orbit draws forty voids, and a mode set somewhere else names a
world and gets forty pictures of it with nothing to cut and no row to add. The village world
is for a mode whose story is an alien raid on the monsters' village at night - so the board,
an opaque plate over the middle of the screen, has to sit over *the place the story happens*:
a small isometric village seen from above, at night, under stars, with a glow low on the
horizon a little warmer than the sky - the whole picture turned onto the level's own colour
by the grade, so the glow arrives beside that colour rather than being painted in it (see
`GLOW_TURN` for why it cannot be painted in it). A cloud sky behind that board would be the
wrong room (invariant 30e), and per-chapter art would be the nine-ways mess 7c ended.

**Painted, not cut.** There is no pack to cut a village *out of* - what the licensed packs
hold is tiles, so this tool lays them: a diamond field of ground blocks with a ragged coast,
props stood on the tiles' top faces, drawn back to front so nearer things overlap farther
ones. Everything is decided by `random.Random(index)` and nothing else, so a village is a
pure function of its number and `--check` can prove the shipped file is the one this tool
paints. The global RNG is never touched.

**How the packs were chosen, and what was left out.** Seventeen isometric CraftPix packs
were extracted; each was laid out on a contact sheet and looked at. Eight read as villages
or land at night - huts, tents, torches, gravestones, docks, dead trees, ruins, swamp
decks - and those are `PACKS` below, one row per index modulo eight. Beach, arctic, desert,
field and Westeros were left out for being the wrong place, and the fantasy-land pack for
being the fantasy pack again. Inside a chosen pack every file is named in a table as
GROUND or a PROP or is not used at all: number tiles, arrow signposts, the location-marker
signs with a triangle over them, chests, coins, sacks, hearts, stars, gifts, ladders
(nothing to lean on), hanging root tiles, two-by-two pond tiles, the `User0N` portraits and
the ghosts, frogs and dragons are all characters or UI and none of them is scenery.
Nothing is listed by scanning a folder: a new file in a pack changes no village until
somebody looks at it and names it.

**Every prop keeps the pack's own scale.** One number per pack - the tile's target width
over its native width - scales tiles and props alike, so a lantern is small beside a tent
because the vendor drew it that way. The one deliberate exception is the tiny map-marker
hut the flat-vector packs carry (54px against a 270px tile), boosted so a village has houses
in it; that boost is in the table and nowhere else.

**The grading is `make_chapter_art.vivid`, imported rather than copied**, with the same
`SLATE` and the same hue ladder as `make_sky_art.py` (`COUNT`, `STRIDE`, `colour`), so
`village_17` sits on the wheel exactly where `sky_17` does and a mode moving worlds moves
nothing else. It is a daylight grade and it lifts a night scene toward it; that is the house
rule for a board backdrop and the board's dark plate is what makes it safe. The night is
painted a little lighter than night for that reason - a true black sky lifted to the floor
comes out grey, where a deep blue lifted the same amount stays blue.
"""
import argparse, colorsys, io, os, random, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)

import make_chapter_art as chapter_art                              # noqa: E402
from PIL import Image, ImageDraw, ImageFilter                        # noqa: E402

BG_ART = os.path.join(ROOT, "Assets", "Game", "Art", "Bg")
BG_W, BG_H = chapter_art.BG_W, chapter_art.BG_H

#: How many villages exist - the same forty as the skies, for the same reason: ten per
#: chapter, four ordinals deep, wrapping after that (`mapart.skies`). Only ever goes up.
COUNT = 40

#: The hue ladder, identical to `make_sky_art` so the two worlds agree about what colour
#: level seven of a second chapter is.
TARGET_S, TARGET_V = .72, 1.0
STRIDE = 13

#: Passed to `vivid` and unread by it, exactly as the skies pass it.
SLATE = (18, 34, 46)


def name(index):
    return "village_%02d" % index


def colour(index):
    """The colour village `index` is turned onto, as an (r, g, b) triple."""
    hue = ((index * STRIDE) % COUNT) / float(COUNT)
    return tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb(hue, TARGET_S, TARGET_V))


# ------------------------------------------------------------------ the packs
#
# One entry per pack. `ref` is the plain flat tile the pack's geometry is measured from;
# `plain` is what most cells are, `variant` the odd cell inside the island (a pond, a patch
# of cobbles, rubble), `water` what a coast cell may be instead of land, `homes` the built
# things a village needs two or three of, and `props` the dressing. A prop is
# `(file, boost)`, boost being a multiplier on the pack's own scale and 1.0 for everything
# the vendor drew in proportion. Every path is relative to the pack folder.

def _r(*ns):
    return ["png/Recurso %d.png" % n for n in ns]


def _r1x(*ns):
    return ["png/1x/Recurso %d.png" % n for n in ns]


def _pl(*ns):
    return ["PNG/Platforms/%02d.png" % n for n in ns]


def _el(*ns):
    return ["PNG/Elements/%02d.png" % n for n in ns]


def _plain(paths, boost=1.0):
    return [(p, boost) for p in paths]


PACKS = (
    # Night forest: teal-topped blocks, blue water, tents, pines, lanterns. The pack that
    # named the world.  Not used: 2,3,4 (arrow signs), 13 (chest), 14 (coins), 15 (sack),
    # 23 (ladder), 40-69 (number tiles), world-sample.
    dict(
        folder="craftpix-net-808145-night-forest-isometric-tileset",
        ref="png/Recurso 27.png",
        plain=_r(25, 26, 27, 28, 29),
        variant=_r(32, 37, 38),
        water=_r(33, 34, 35, 36, 39),
        homes=_plain(_r(19)) + [("png/Recurso 18.png", 2.3)] + _plain(_r(30)),
        props=_plain(_r(20, 21, 22, 16, 17, 31, 24, 11, 12, 6, 7, 8, 10))
        + _plain(_r(9), 1.6) + _plain(_r(5, 1), 1.2),
    ),
    # Fantasy: the same vocabulary in green with grey and purple families left out so one
    # island is one stone.  Not used: 1 (skull sign under a marker triangle), 2,3,4 (arrow
    # signs), 7 (chest), 8 (heart), 9 (coins), 10 (sack), 23 (ladder), 28,29 (grey-based
    # blocks), 32-35 (purple), 41,42 (sand-rimmed ponds), 47-76 (number tiles).
    dict(
        folder="craftpix-net-378934-fantasy-isometric-tileset",
        ref="png/Recurso 27.png",
        plain=_r(24, 25, 26, 27),
        variant=_r(30, 31, 40, 44),
        water=_r(36, 37, 38, 39, 43),
        homes=[("png/Recurso 19.png", 2.3)] + _plain(_r(22, 45)),
        props=_plain(_r(18, 20, 21, 46, 11, 12, 13, 15, 16, 17))
        + _plain(_r(14), 1.6) + _plain(_r(5, 6), 1.2),
    ),
    # Ghosts: charcoal blocks, magenta pools, dead trees, gravestones, candles - the raid's
    # own palette.  Not used: 01 (potion), 1,2,3,4 (signs), 10 (heart), 12,13,14 (the
    # ghosts - characters), 22 (ladder), 27,28 (lilac blocks), 37-68 (number tiles).
    dict(
        folder="craftpix-net-693047-ghosts-isometric-tileset",
        ref="png/Recurso 26.png",
        plain=_r(23, 24, 25, 26, 29),
        variant=_r(30, 32, 33),
        water=_r(31, 34),
        homes=[("png/Recurso 18.png", 2.3)] + _plain(_r(5, 21)),
        props=_plain(_r(15, 16, 17, 19, 20, 35, 36))
        + _plain(_r(9, 11), 1.6) + _plain(_r(6, 7, 8), 1.2),
    ),
    # Daily forest: the daylight sibling of the night pack, graded to dusk.  Not used:
    # 14-17 (signs under marker triangles), 29 (heart), 30 (chest), 31 (coins), 32 (sack),
    # 40 (ladder), `number tiles/`.
    dict(
        folder="craftpix-net-163650-daily-forest-isometric-tileset",
        ref="png/Recurso 3.png",
        plain=_r(1, 2, 3, 4, 5),
        variant=_r(6, 11, 12),
        water=_r(7, 8, 9, 10, 13),
        homes=_plain(_r(36)) + [("png/Recurso 20.png", 2.3)] + _plain(_r(42)),
        props=_plain(_r(37, 38, 39, 19, 21, 22, 23, 24, 26, 27, 28, 33, 34, 35, 41, 43))
        + _plain(_r(25), 1.6) + _plain(_r(18), 1.2),
    ),
    # Fall forest: maroon blocks and turning trees; no water in the pack, so the coast is
    # all cliff.  Not used: 1,2,3 (the grey family), 14-44 and 68 (number tiles), 45-48
    # (signs), 55 (chest), 56 (coins), 57 (sack), 59 (ladder), 64-67 (loose leaves),
    # world-sample.
    dict(
        folder="craftpix-net-670262-fall-forest-isometric-tileset",
        ref="png/1x/Recurso 7.png",
        plain=_r1x(5, 6, 7, 8, 9),
        variant=_r1x(4, 10, 11),
        water=[],
        homes=_plain(_r1x(12, 60)),
        props=_plain(_r1x(61, 62, 63, 13, 51, 52, 53, 54))
        + _plain(_r1x(49, 50), 1.2),
    ),
    # Ruin: painted stone and moss, torches, braziers, pillars, a statue. Platforms 03 is
    # a heap of rubble three quarters of a tile wide, so it stands on a tile as a prop
    # rather than being one (the width gate in `Pack` is what said so).  Not used: CHIP/*
    # (plain colour chips), Elements 01 (rope ends), 04 (a light beam), 05 (vines that hang
    # from nothing), Platforms 05 (a two-by-two path corner), 12 (a hanging root), 14 (a
    # wall segment), 19 (a two-by-two pond), 20 (rope fence on its own tile), Gift, Star,
    # User01-06.
    dict(
        folder="craftpix-net-556799-isometric-ruin-tileset",
        ref="PNG/Platforms/09.png",
        plain=_pl(4, 6, 7, 8, 9),
        variant=_pl(10, 11, 13, 16, 17),
        water=[],
        homes=_plain(_el(6, 11, 12, 16) + _pl(15, 21)),
        props=_plain(_el(2, 3, 7, 8, 9, 10, 13, 14, 15) + _pl(1, 2, 3)),
    ),
    # Swamp: dark pools, willows, decks on stilts, a well.  Not used: CHIP/*, Elements 01
    # (arrow sign), 03 and 10 (a tree already standing on its own island), 06 (flat lily
    # pads), Platforms 03 (a lily pad), 04 and 05 (two-by-two), 14 and 15 (canvas), 20 and
    # 21 (arrow tarps), Gift, Star, User01-06 (frogs).
    dict(
        folder="craftpix-net-182714-isometric-swamp-game-tileset",
        ref="PNG/Platforms/01.png",
        plain=_pl(1, 2),
        variant=_pl(9, 11, 12, 13, 16),
        water=_pl(7, 8, 10),
        homes=_plain(_pl(6, 17, 18, 19, 25)),
        props=_plain(_el(2, 4, 5, 7, 8, 9, 11) + _pl(22, 23, 24)),
    ),
    # Forest: teal moss on stone, pines, boulders. One material in `plain` (the moss) and
    # everything stone or wood is a variant: with the stone flats in `plain` too the island
    # was a checkerboard.  Not used: CHIP/*, Elements 09 and 10 (clouds), Platforms 09 and
    # 10 (stair steps), 11, 18 and 24 (hanging roots), 25 and 26 (rock spires), Gift, Star,
    # User01-06 (mushrooms).
    dict(
        folder="craftpix-net-761235-isometric-forest-tileset",
        ref="PNG/Platforms/15.png",
        plain=_pl(15, 16),
        variant=_pl(1, 2, 3, 4, 5, 6, 7, 8, 12, 13, 14, 17, 19, 20, 21, 22, 23),
        water=[],
        homes=_plain(_el(8, 7)),
        props=_plain(_el(1, 2, 3, 4, 5, 6)),
    ),
)


# ------------------------------------------------------------------ sprites

_FACTS = {}
_SCALED = {}


def facts(path):
    """What the painter needs to know about one file: its alpha box and its midline.

    `mid` is the first row at which the picture reaches its full width. On a ground tile
    that is the top face's horizontal diagonal - the walls below it are no wider - and it is
    where the tile is anchored, so a deep block and a flat tile stand on the same grid point
    and only their cliffs differ. Measured rather than assumed as half the height, because a
    block is taller than its face by however deep the vendor drew it.
    """
    if path in _FACTS:
        return _FACTS[path]
    image = Image.open(path).convert("RGBA")
    alpha = image.getchannel("A")
    box = alpha.getbbox()
    if box is None:
        sys.exit(f"{path} is entirely transparent")
    widest, mid = -1, box[1]
    for y in range(box[1], box[3]):
        row = alpha.crop((box[0], y, box[2], y + 1)).tobytes()
        left = next((i for i, v in enumerate(row) if v > 40), None)
        if left is None:
            continue
        right = len(row) - next(i for i, v in enumerate(reversed(row)) if v > 40)
        if right - left > widest:
            widest, mid = right - left, y
    _FACTS[path] = dict(image=image, box=box, mid=mid)
    return _FACTS[path]


def scaled(path, scale):
    key = (path, round(scale, 5))
    if key not in _SCALED:
        image = facts(path)["image"]
        size = (max(1, int(round(image.size[0] * scale))), max(1, int(round(image.size[1] * scale))))
        _SCALED[key] = image.resize(size, Image.LANCZOS)
    return _SCALED[key]


class Pack:
    """One pack resolved against the source folder, with its tile geometry measured."""

    def __init__(self, root, spec):
        self.folder = os.path.join(root, spec["folder"])
        if not os.path.isdir(self.folder):
            sys.exit(f"pack folder missing: {self.folder}")
        self.plain = [self.path(p) for p in spec["plain"]]
        self.variant = [self.path(p) for p in spec["variant"]]
        self.water = [self.path(p) for p in spec["water"]]
        self.homes = [(self.path(p), b) for p, b in spec["homes"]]
        self.props = [(self.path(p), b) for p, b in spec["props"]]

        ref = facts(self.path(spec["ref"]))
        self.tile_w = ref["box"][2] - ref["box"][0]
        self.face_h = 2 * (ref["mid"] - ref["box"][1])

        # A ground tile that is not the width of the reference is not a tile of this grid
        # - a two-by-two, or a deck whose posts widen the box - and it would either leave
        # holes or stand a picture over its neighbours. Refused by name.
        for path in self.plain + self.variant + self.water:
            w = facts(path)["box"][2] - facts(path)["box"][0]
            if abs(w - self.tile_w) > self.tile_w * .08:
                sys.exit(f"{path} is {w}px wide against a {self.tile_w}px tile - not ground")

    def path(self, rel):
        full = os.path.join(self.folder, rel)
        if not os.path.isfile(full):
            sys.exit(f"named in a table and not in the pack: {full}")
        return full


# ------------------------------------------------------------------ painting

#: Where the horizon glow sits, as a fraction of the height. The board's plate covers
#: roughly the middle three fifths of the screen, so what a player sees of this picture is
#: its top fifth and its bottom fifth: the glow sits just above the plate's top edge with the
#: far coast under it, and the near coast and its houses show under the plate's bottom edge.
#: The first cut put the horizon at .40 and the island under it, and the whole village was
#: behind the board.
HORIZON = .22

#: The field's vertical centre. A ten-or-eleven tile island centred here spans from just
#: under the horizon to the lower tenth of the canvas, trees reaching up to the glow.
FIELD_CY = .55

#: Night colours before the grade: a saturated deep blue rather than a black. `vivid` lifts
#: the picture to its daylight floor and scales saturation to its target, and both are
#: kinder to a colour than to a black - a black lifted comes out grey, and a grey scaled
#: comes out neon. A blue lifted is a blue-hour sky, which is what a village at night looks
#: like on a phone anyway.
ZENITH = (14, 24, 92)
GROUND = (22, 20, 62)
GROUND_NEAR = (34, 28, 82)

#: The horizon glow, as a hue offset from the zenith and its saturation and value.
#:
#: **The accent reaches the horizon through the grade, never through the paint.** `vivid`
#: turns the whole picture so that its dominant hue - the sky, which is most of it - lands on
#: the level's colour, and every other hue moves with it by the same amount. Painting the
#: glow *in* the accent therefore put it wherever the accent happened to sit relative to
#: blue: on half the ladder that was the far side of the wheel, and the horizon came out as
#: a complementary stripe across the screen (a red village with a green band). Painted a
#: fixed thirty degrees from the zenith, the glow arrives thirty degrees from the accent on
#: every village - the same soft, slightly warmer dusk on all forty.
GLOW_TURN = -.085
GLOW_S, GLOW_V = .58, .66
HAZE_S, HAZE_V = .74, .48

#: Props are drawn this much larger than the pack's own scale. The grade blurs six pixels,
#: and at a 110px tile a sapling drawn true to the vendor's proportion is a smudge; a little
#: over life size keeps a tree a tree behind the plate. Huts carry their own boost on top.
PROP_BOOST = 1.18


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def hsv(rgb, turn, s, v):
    """`rgb`'s hue turned by `turn` of a wheel, at a chosen saturation and value."""
    h, _, _ = colorsys.rgb_to_hsv(*[c / 255.0 for c in rgb])
    return tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb((h + turn) % 1.0, s, v))


def sky(rng):
    """A night sky: zenith to a warmer glow at the horizon, dark land below it.

    The level's colour is not read here - see `GLOW_TURN` for why it cannot be.
    """
    glow = hsv(ZENITH, GLOW_TURN, GLOW_S, GLOW_V)
    haze = hsv(ZENITH, GLOW_TURN * .5, HAZE_S, HAZE_V)
    column = Image.new("RGB", (1, BG_H))
    horizon = int(BG_H * HORIZON)
    for y in range(BG_H):
        if y < horizon:
            t = y / float(horizon)
            # Most of the sky stays night; the glow gathers in the last fifth above the
            # line, narrow on purpose - the grade lifts contrast and a wide band became a
            # stripe across the screen.
            c = lerp(ZENITH, haze, t ** 2.4)
            if t > .80:
                c = lerp(c, glow, ((t - .80) / .20) ** 1.8)
        else:
            t = (y - horizon) / float(BG_H - horizon)
            c = lerp(lerp(glow, GROUND, min(1.0, t * 14.0) ** .7), GROUND_NEAR, t)
        column.putpixel((0, y), c)
    canvas = column.resize((BG_W, BG_H), Image.BILINEAR)

    draw = ImageDraw.Draw(canvas)
    for _ in range(rng.randint(260, 420)):
        x = rng.uniform(0, BG_W)
        y = rng.uniform(0, horizon * 1.02)
        fade = max(0.0, 1.0 - y / float(horizon)) ** .5
        bright = rng.uniform(.25, 1.0) * fade
        if bright < .05:
            continue
        r = 1.0 if rng.random() < .80 else rng.uniform(1.6, 2.6)
        tint = lerp((255, 255, 255), lerp(haze, (255, 255, 255), .6), rng.uniform(0, .5))
        c = lerp((0, 0, 0), tint, bright)
        if r > 1.5:
            halo = lerp((0, 0, 0), c, .35)
            draw.ellipse((x - r * 2.2, y - r * 2.2, x + r * 2.2, y + r * 2.2), fill=halo)
        draw.ellipse((x - r, y - r, x + r, y + r), fill=c)
    return canvas


def island(n, rng):
    """Which cells of an n x n grid are land: an ellipse with one to three nibbled off each
    row end, so the coast is ragged rather than a diamond."""
    keep = set()
    c = (n - 1) / 2.0
    rx = rng.uniform(.62, .72) * n
    ry = rng.uniform(.62, .72) * n
    for i in range(n):
        for j in range(n):
            d = ((i - c) / rx) ** 2 + ((j - c) / ry) ** 2
            if d <= 1.0 + rng.uniform(-.08, .08):
                keep.add((i, j))
    # One to three off each row's ends, and each column's, but never the middle rows'
    # middles: the island has to hold a village.
    for i in range(n):
        cells = sorted(j for (ii, j) in keep if ii == i)
        for _ in range(rng.randint(0, 2)):
            if len(cells) > 4:
                keep.discard((i, cells.pop(0)))
        for _ in range(rng.randint(0, 2)):
            if len(cells) > 4:
                keep.discard((i, cells.pop()))
    for j in range(n):
        cells = sorted(i for (i, jj) in keep if jj == j)
        for _ in range(rng.randint(0, 1)):
            if len(cells) > 4:
                keep.discard((cells.pop(0), j))
        for _ in range(rng.randint(0, 1)):
            if len(cells) > 4:
                keep.discard((cells.pop(), j))
    return keep


def paint(root, index, packs):
    """One village, ungraded, RGB at 720x1280."""
    rng = random.Random(index)
    pack = packs[index % len(packs)]

    canvas = sky(rng).convert("RGBA")

    n = rng.choice((10, 11, 11))
    tile_w = rng.randint(104, 120)
    scale = tile_w / float(pack.tile_w)
    w = pack.tile_w * scale
    h = pack.face_h * scale
    cx = BG_W / 2.0 + rng.uniform(-w * .5, w * .5)
    cy0 = BG_H * FIELD_CY - (n - 1) * h / 2.0

    keep = island(n, rng)

    def edge(i, j):
        return any((a, b) not in keep for a, b in ((i - 1, j), (i + 1, j), (i, j - 1), (i, j + 1)))

    # Ground is decided before any prop, so the prop pass can refuse water. Variants are
    # rare: at one cell in eight the island read as a checkerboard of materials rather
    # than as land with the odd pond on it.
    ground = {}
    for (i, j) in sorted(keep):
        if pack.water and edge(i, j) and rng.random() < .28:
            ground[(i, j)] = (rng.choice(pack.water), True)
        elif not edge(i, j) and pack.variant and rng.random() < .07:
            ground[(i, j)] = (rng.choice(pack.variant), False)
        else:
            ground[(i, j)] = (rng.choice(pack.plain), False)

    land = sorted(cell for cell, (_, wet) in ground.items() if not wet)
    rng.shuffle(land)
    count = min(len(land), rng.randint(18, 30))
    homes = min(count, rng.randint(3, 5))
    placed = {}
    for k, cell in enumerate(land[:count]):
        placed[cell] = rng.choice(pack.homes if k < homes else pack.props)

    def anchor(i, j):
        return cx + (i - j) * w / 2.0, cy0 + (i + j) * h / 2.0

    for (i, j) in sorted(keep, key=lambda c: (c[0] + c[1], c[0])):
        x, y = anchor(i, j)
        path, _ = ground[(i, j)]
        f = facts(path)
        sprite = scaled(path, scale)
        px = int(round(x - (f["box"][0] + f["box"][2]) / 2.0 * scale))
        py = int(round(y - f["mid"] * scale))
        canvas.alpha_composite(sprite, (px, py))

        if (i, j) in placed:
            path, boost = placed[(i, j)]
            f = facts(path)
            s = scale * boost * PROP_BOOST
            sprite = scaled(path, s)
            # Base centred on the top face, its foot a whisker above the face's midline so
            # a shadow the vendor painted under it stays on the tile.
            px = int(round(x - (f["box"][0] + f["box"][2]) / 2.0 * s))
            py = int(round(y - f["box"][3] * s + h * .06))
            canvas.alpha_composite(sprite, (px, py))

    return canvas.convert("RGB")


def cut(root, index, packs):
    """One graded village."""
    return chapter_art.vivid(paint(root, index, packs), SLATE, colour(index))


def contact(images, path):
    """All the villages on one sheet, eight to a row, at thumbnail size."""
    tw, th, cols = 180, 320, 8
    rows = (len(images) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * (tw + 6) + 6, rows * (th + 22) + 6), (24, 24, 28))
    draw = ImageDraw.Draw(sheet)
    for k, (index, image) in enumerate(images):
        x = 6 + (k % cols) * (tw + 6)
        y = 6 + (k // cols) * (th + 22)
        sheet.paste(image.resize((tw, th), Image.LANCZOS), (x, y))
        draw.text((x + 2, y + th + 4), name(index), fill=(220, 220, 220))
    sheet.save(path)
    print(f"contact sheet: {path}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", required=True, help="folder the tile packs were extracted into")
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped villages are what this tool paints, and write nothing")
    ap.add_argument("--only", type=int, help="paint one village, for judging a change")
    ap.add_argument("--out", help="write somewhere other than the project")
    ap.add_argument("--contact", help="also write a sheet of every village painted, to this path")
    args = ap.parse_args()

    if not os.path.isdir(args.source):
        if args.check:
            print(f"source folder not present ({args.source}); nothing to check against")
            return
        sys.exit(f"source folder not found: {args.source}")

    packs = [Pack(args.source, spec) for spec in PACKS]

    out_dir = args.out or BG_ART
    wanted = [args.only] if args.only is not None else list(range(COUNT))

    if not args.check:
        os.makedirs(out_dir, exist_ok=True)

    bad, painted = 0, []
    for index in wanted:
        image = cut(args.source, index, packs)
        painted.append((index, image))
        path = os.path.join(out_dir, name(index) + ".png")
        r, g, b = colour(index)

        if args.check:
            if not os.path.exists(path):
                print(f"  {name(index)}  MISSING")
                bad += 1
                continue
            buffer = io.BytesIO()
            image.save(buffer, format="PNG")
            with open(path, "rb") as f:
                same = f.read() == buffer.getvalue()
            print(f"  {name(index)}  #{r:02X}{g:02X}{b:02X}  {'ok' if same else 'DIFFERS'}")
            if not same:
                bad += 1
            continue

        print(f"  {name(index)}  #{r:02X}{g:02X}{b:02X}  {PACKS[index % len(PACKS)]['folder'][13:]}"
              f"  {image.size[0]}x{image.size[1]}")
        image.save(path)

    if args.contact:
        contact(painted, args.contact)

    if args.check:
        if bad:
            sys.exit(f"{bad} of {len(wanted)} village(s) are not what this tool paints")
        print(f"all {len(wanted)} villages are what this tool paints")
        return

    print("\nNext: Glimmer Grove > Addressables > Sync All Assets, then > Validate Art.")
    print("The importer hook only addresses art that arrives while the Editor is running.")


if __name__ == "__main__":
    main()
