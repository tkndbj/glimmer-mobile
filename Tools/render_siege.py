# -*- coding: utf-8 -*-
"""Draws every shipped Thornwatch level at the size a phone draws it. Look at it.

    python Tools/render_siege.py                        # every level of the chapter
    python Tools/render_siege.py --level s01_firstwatch
    python Tools/render_siege.py --raiders 0            # the board as the run opens

**This is the only check in the repository that can see a board that reads badly.** Every numeric
gate in this project reads the *model*, and the model is right the whole time. The Iron Quarry's
cage passed par, `ways`, `careless`, both validators, the content check and the art audit, and at
the size a phone drew it it was three brown logs (invariant 32b); Hollowmarch's road read as a row
of disconnected sockets (33h); Emberforge's cage as a tiny padlock (34e); Kindlewake's husk as a
hole (35h). Every one of them was caught by a render and by nothing else.

This mode needs one more than most, because it is the first board in this game that is not one
grid: it is three bands stacked - a hill, a ward line and a field - and how they divide is a
decision no gate can look at. Five questions to ask of what comes out, in order:

  1. **Is it obvious which band is which?** The hill has to read as somewhere things come *from*,
     the line as something being defended, and the field as the thing you touch.
  2. **Can you tell a ward's colour at a glance, and whether it is standing?** That is the one
     thing every decision in this mode rests on.
  3. **Can you tell a raider's colour at a glance?** The cast are painted in colours of their own
     that have nothing to do with this board's four, so a raider says its colour three times - a
     coat, an aura and a gem over its head - and if it still reads as "an orange monster" then one
     of the three is not doing its job.
  4. **Are the four gems told apart by shape as well as by colour?** A heart, a cabochon, a
     rhombus and an emerald cut. If two read as one silhouette at this size, a colour-blind player
     is playing a different game.
  5. **Is the field big enough to play on?** It is forty per cent of the height and it is where
     the finger goes.
  6. **Does the boss read as the boss?** It is three cells tall against a creeper's one, it
     stands still in the middle of the hill, its health is a bar pinned across the top of the
     board, and while it is winding up a ring closes over the ward it has chosen. Three of
     those four are placements, and a placement is exactly what no gate in this project can look
     at - two of them were already moved twice by this picture (`SiegeView.Crown`).
  7. **Are the four bosses four different things?** Render `s01_stonewatch`, `s01_warlordsgate`,
     `s01_blackmarch` and `s01_lastlight` side by side and the question answers itself - a
     floating eye, an armoured walker, a walking slab and a gold overlord, at four sizes, each
     casting in a colour no gem wears. **This picture is what said they were not.** The chapter
     shipped two bosses drawn from one reel and separated by a run-time hue, every gate green, and
     a player's verdict was that they looked exactly the same. It then caught the first repair too:
     the blightcaller cut at 2.6 cells stood beside a creeper and read as one, and a green coat put
     it in the same family as the green raiders around it.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import proto                                                            # noqa: E402
import siege                                                            # noqa: E402

ART = REPO / "Assets" / "Game" / "Art" / "Siege"
FX = REPO / "Assets" / "Game" / "Art" / "Fx" / "Siege"
CHAPTERS = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters"

#: The canvas the game lays out in, and the room `SiegeScreen.HostInset` leaves the board.
#:
#: **In the screen's own order: (left, bottom, right, top).** That is what a `Vector4` means to
#: `ModeScreen` - `offsetMin` is (left, bottom) and `offsetMax` is (-right, -top) - and this file
#: used to write it as (left, top, right, bottom), which put the board 55 points higher here than
#: on a phone. Harmless while both numbers were close and exactly the kind of quiet drift a
#: diagnostic must not have, since the whole reason this tool exists is that it is the only thing
#: that can see a band in the wrong place (invariant 37g).
#:
#: Kept in step with the screen by hand.
CANVAS = (1080, 1920)

#: `UtilityBar.Height`. The bar is a shelf that meets the board's plate rather than a strip
#: floating under it, so the bottom inset is the bar and nothing else.
BAR_HEIGHT = 228
INSET = (0, BAR_HEIGHT, 0, 300)

#: `UtilityBar`'s own numbers.
SLOTS, SLOT, BADGE, ICON = 5, 184, 60, 136
UTILITY_ART = REPO / "Assets" / "Game" / "Art" / "Ui" / "Utility"
UTILITIES = ["firepot", "mending", "surge"]

#: `SiegeView`'s own numbers. The field is laid out to the *width* and the hill and the line
#: then share what is left in the proportion below - see `SiegeView.Fit` and `MaxGemBand`.
MARGIN = 18
HILL_BAND, LINE_BAND, MAX_GEM_BAND = 0.44, 0.16, 0.52
GEM_INSET = 0.84
LANES = 5

#: `SiegeTuning.BlastRows`.
BLAST_ROWS = 4

#: `Pal.Board`, over the dark ground a sky is graded against.
PLATE = (14, 27, 37)
BACK = (9, 14, 20)

#: `SiegeView.Tints` - Pal.Poppy, Pal.Mint, Pal.Azure, Pal.Sun.
TINTS = [(242, 64, 79), (123, 216, 106), (79, 193, 255), (255, 201, 60)]

GEM_ART = {"r": "gem_r", "g": "gem_g", "b": "gem_b", "y": "gem_y", "*": "gem_cog"}

#: `SiegeView.WardArt` - five tiers times four colours, both baked. A ward carries its **rank** in
#: its silhouette and its colour in its hue, so nothing here is tinted (invariant 37l).
def ward_art(colour, rank):
    return "ward%d_%s" % (min(max(rank, 0), 4) + 1, colour)


SKINS = ["mon1", "mon2", "mon3"]


def sprite(name):
    path = ART / (name + ".png")
    if not path.exists():
        raise SystemExit("missing %s - run: python Tools/make_siege_art.py --write" % path.name)
    return Image.open(path).convert("RGBA")


def reel(name, frame=0):
    path = ART / name / ("f%02d.png" % frame)
    return Image.open(path).convert("RGBA") if path.exists() else None


def blast(name, frame=0):
    """One frame of a reel under `Art/Fx/Siege` - the muzzle flashes, bolts and impacts."""
    path = FX / name / ("f%02d.png" % frame)
    return Image.open(path).convert("RGBA") if path.exists() else None


def loudest(name):
    """The frame of a reel that has the most on it.

    **A still picture has to show a reel at its loudest or it is judging the wrong thing.** These
    effects are not steady: the fireball's muzzle draws a ring inward, goes almost dark, and then
    bursts, so a sheet drawn from a fixed frame index caught two of the four with nothing on them
    and read as a bug in the bake. Nothing about the reel was wrong; the frame was.
    """
    best, mass = None, -1.0
    folder = FX / name
    if not folder.is_dir():
        return None

    for path in sorted(folder.glob("f*.png")):
        im = Image.open(path).convert("RGBA")
        here = sum(im.getchannel("A").getdata())
        if here > mass:
            best, mass = im, here
    return best


#: `SiegeView.HeadAt` and `SiegeView.MuzzleAt` - where the thing a reel is drawn *on* sits in its
#: own frame, measured from the bottom. A comet's head leads and its trail hangs behind; a muzzle
#: flash is all in front of the barrel.
HEAD_AT = 0.82
MUZZLE_AT = 0.22


def aimed(sheet, im, hx, hy, wide, ux, uy, head=0.5):
    """Draws a reel frame turned to point along (ux, uy), with its head landing on (hx, hy).

    The bolts are baked as tall frames with the comet's head near the top and its trail running
    down (`SiegeShotBake.ShotAspect`), so drawing one is two things rather than one: turn it to the
    aim, then step the *centre* back along that aim by however far the head sits from it. Getting
    the second half wrong is a bolt that appears to land before it arrives, which is exactly the
    sort of wrongness a still picture is for.
    """
    if im is None:
        return

    tall = wide * im.height / im.width
    im = im.resize((max(1, int(wide)), max(1, int(tall))), Image.LANCZOS)

    # PIL turns counter-clockwise about the centre, and a picture's y runs down.
    import math
    spin = -math.degrees(math.atan2(ux, -uy))
    turned = im.rotate(spin, Image.BICUBIC, expand=True)

    back = (head - 0.5) * tall
    cx = hx - ux * back
    cy = hy - uy * back

    sheet.alpha_composite(turned, (int(cx - turned.width / 2), int(cy - turned.height / 2)))


def face(size):
    """A bold face for the floating numbers, or None if this machine has none to offer."""
    from PIL import ImageFont
    for name in ("arialbd.ttf", "seguibl.ttf", "DejaVuSans-Bold.ttf", "Arial Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return None


def damage(sheet, cx, cy, total, weak, cell):
    """`SiegeView.Number` - a raider's tally, outlined, floating off it.

    Drawn at the size the view draws it (`Paint`), which is what this is for: a number big enough
    to read against a lit hill and a bright cast is a decision no gate can look at, and the mode
    fires often enough that four wards on one raider is eighteen hits a second - so what is drawn
    is one figure per raider that climbs, never one per bolt.
    """
    grown = min(total, 30) / 30.0
    size = int(cell * (0.58 if weak else 0.40) * (1.0 + grown * 0.5))

    font = face(size)
    if font is None:
        return

    colour = (255, 201, 60, 255) if weak else (245, 236, 214, 255)
    ink = (23, 36, 51, 245)
    ring = max(2, int(cell * 0.055))

    text = str(total)
    layer = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(layer)

    for dx in range(-ring, ring + 1):
        for dy in range(-ring, ring + 1):
            if dx * dx + dy * dy > ring * ring:
                continue
            pen.text((cx + dx, cy + dy), text, font=font, fill=ink, anchor="mm")

    pen.text((cx, cy), text, font=font, fill=colour, anchor="mm")
    sheet.alpha_composite(layer)


def banner(sheet, cx, cy, depth, cell, span):
    """`SiegeView.Chain` - a cascade announced over the ward line.

    **Drawn here because it was drawn nowhere anybody looked.** It used to sit just above the
    gems, half this size, in a plain label with no outline, in the same band as forty gems - the
    loudest thing that can happen on this board, reading as a caption. This is the only check that
    can say whether the new one carries; every gate reads the model and the model was always right.
    """
    heat = {2: (255, 201, 60), 3: (255, 194, 60), 4: (255, 138, 61)}.get(depth, (240, 106, 128))
    size = int(cell * (0.72 + min(depth, 6) * 0.05))

    font = face(size)
    if font is None:
        return

    layer = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(layer)

    # The aura under it: a heavy outline alone is not enough over a lit hill.
    pen.ellipse([cx - span * 0.34, cy - cell * 1.1, cx + span * 0.34, cy + cell * 1.1],
                fill=(10, 15, 23, 158))
    layer = layer.filter(ImageFilter.GaussianBlur(cell * 0.18))

    pen = ImageDraw.Draw(layer)
    text = "CHAIN x%d" % depth
    ink = (23, 36, 51, 250)
    ring = max(2, int(cell * 0.07))

    for dx in range(-ring, ring + 1):
        for dy in range(-ring, ring + 1):
            if dx * dx + dy * dy > ring * ring:
                continue
            pen.text((cx + dx, cy + dy), text, font=font, fill=ink, anchor="mm")

    pen.text((cx, cy), text, font=font, fill=heat + (255,), anchor="mm")
    sheet.alpha_composite(layer)


#: The four bosses, as everything a picture of one needs: where it stops
#: (`SiegeTuning.HoldOf`), how tall it is drawn (`SiegeView.TallOf`), which reels its body wears,
#: which effect its spell is drawn with, and what colour that is (`SiegeView.Casting`).
#:
#: **Four rows rather than a pair of "greater or not" ternaries**, which is the same correction the
#: view itself needed: a bool can only ever answer "the other one", and a chapter shipped two
#: bosses separated by nothing but a hue because of it. None of the four spell colours is one of
#: the board's four gems, so nothing any of them lights can be read as a colour rule.
BOSSES = {
    "blightcaller": dict(hold=0.58, tall=3.0, stem="blight", fx="hex", fire=(59, 233, 216)),
    "warlord": dict(hold=0.46, tall=3.1, stem="boss", fx="spell", fire=(180, 120, 255)),
    "warbringer": dict(hold=0.34, tall=3.3, stem="bringer", fx="roar", fire=(255, 244, 206)),
    "overlord": dict(hold=0.38, tall=3.5, stem="over", fx="omen", fire=(255, 116, 212)),
}


def warlord(sheet, draw_on, lay, span, cell, hill_top, hill_foot, line_y, at, casting):
    """The warlord holding the middle of the hill, and the spell it is winding up.

    <p><b>Drawn here because it is the one thing on this board no number can judge.</b> Whether a
    boss reads as a boss is a question about size against the band it stands in, about its
    silhouette against four round monsters, and about whether the ring closing over the ward it
    has chosen is legible at the moment the player most needs to see it. Every gate in this
    project reads the model, and the model is right the whole time (invariants 32b, 33h, 34e,
    36g, 37g).</p>

    <p>Three questions to ask of what comes out. Does it read as <em>the</em> thing on the hill
    rather than as a big raider? Can you tell what colour it is - it says so three times, a coat,
    an aura and a gem over its head, and the coat is the one that struggles at this size? And can
    you see, without being told, which ward the closing ring is over?</p>
    """
    if not lay.boss:
        return

    # A level names its boss by kind (`SiegeLayout.BossNames`), so which of the four this is comes
    # out of the file rather than out of a case bit.
    look = BOSSES.get(lay.boss_kind)
    if look is None:
        return

    greater = lay.boss_kind == "overlord"
    colour = siege.LETTERS.index(lay.boss)
    tint = TINTS[colour]
    fire = look["fire"]

    tall = cell * look["tall"]
    ly = hill_top + (hill_foot - hill_top) * look["hold"]
    cx, cy = at(0.0, ly)

    # `SiegeView.Follow` - the walk while it is crossing ground, the idle once it is standing.
    # This picture is of a boss in place, so the idle is the honest one to draw; `--warlord walk`
    # is what looks at the reel that was missing for a whole session.
    stem = look["stem"]
    body = reel(stem + ("_cast" if casting == "cast" else "_walk" if casting == "walk" else ""))
    if body is None:
        return

    wide = tall * body.width / body.height

    # The aura, then the body: a wash behind it is the second of the three things that say its
    # colour, and it is the one that survives being drawn over a lit hill.
    glow = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(glow)
    pen.ellipse([cx - tall * 0.62, cy - tall * 0.62, cx + tall * 0.62, cy + tall * 0.62],
                fill=tint + (96,))
    glow = glow.filter(ImageFilter.GaussianBlur(cell * 0.55))
    sheet.alpha_composite(glow)

    if casting == "cast":
        spark = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        pen = ImageDraw.Draw(spark)
        pen.ellipse([cx - tall * 0.5, cy - tall * 0.5, cx + tall * 0.5, cy + tall * 0.5],
                    fill=fire + (150,))
        spark = spark.filter(ImageFilter.GaussianBlur(cell * 0.42))
        sheet.alpha_composite(spark)

    put(sheet, coat(body, tint), cx, cy, wide, tall)

    # `SiegeView.Crown` - a warlord's health is pinned across the top of the board rather than
    # carried, which is what lets it be three cells tall on a hill four cells deep.
    wide = span[0] * 0.60
    bx, by = at(cell * 0.34, hill_top + cell * 0.12)

    draw_on.rounded_rectangle([bx - wide / 2, by - cell * 0.13, bx + wide / 2, by + cell * 0.13],
                              radius=11, fill=(0, 0, 0, 178))
    draw_on.rounded_rectangle([bx - wide / 2 + 2, by - cell * 0.13 + 2,
                               bx - wide / 2 + 2 + wide * 0.64, by + cell * 0.13 - 2],
                              radius=11, fill=(255, 107, 87, 255))

    gx = bx - wide / 2 - cell * 0.45
    put(sheet, sprite(GEM_ART[lay.boss]), gx, by, cell * 0.62, cell * 0.62)

    if casting != "cast":
        return

    # **A roar is thrown at nothing**, so what is drawn for a warbringer is its ring going out over
    # the hill rather than a ring closing on a ward - which is the one thing a picture of this mode
    # can say about a boss that takes ground instead of health.
    if look["fx"] == "roar":
        for name, size in (("roar_hit", 7.0), ("roar_muzzle", 9.0)):
            ring = loudest(name)
            if ring is not None:
                put(sheet, ring, cx, cy + tall * 0.1, cell * size, cell * size)
        return

    # The spell in the air, and the ring closing over the ward it is coming for.
    ward = 1
    wx, wy = at(post_x(span, ward, len(lay.wards)), line_y + cell * 0.3)

    layer = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(layer)
    r = cell * 1.5
    pen.ellipse([wx - r, wy - r, wx + r, wy + r], outline=fire + (240,),
                width=max(4, int(cell * 0.09)))
    sheet.alpha_composite(layer)

    orb = blast(look["fx"], 9)
    if orb is not None:
        mx, my = cx, cy + tall * 0.1
        put(sheet, orb, mx + (wx - mx) * 0.55, my + (wy - my) * 0.55, cell * 1.6, cell * 1.6)

    flare = loudest(look["fx"] + "_muzzle")
    if flare is not None:
        put(sheet, flare, cx, cy + tall * 0.1, cell * 4.2, cell * 4.2)


def post_x(span, index, wards):
    """`SiegeView.PostX`, at module scope so the ward rings can use it too."""
    wide = span[0] / (wards + 0.6)
    return (index - (wards - 1) / 2) * wide


def lane_x(span, lane):
    """`SiegeView.LaneX`, at module scope so the aim grid can use it too."""
    return (lane - (LANES - 1) * 0.5) * (span[0] / LANES)


def put(sheet, im, cx, cy, w, h):
    """Draws a sprite centred on (cx, cy), fitted into w x h without changing its aspect."""
    if im is None:
        return
    ratio = min(w / im.width, h / im.height)
    size = (max(1, int(im.width * ratio)), max(1, int(im.height * ratio)))
    sheet.alpha_composite(im.resize(size, Image.LANCZOS),
                          (int(cx - size[0] / 2), int(cy - size[1] / 2)))


def stretch(sheet, im, cx, cy, w, h):
    """Draws a sprite stretched to w x h, which is what the view does to the hill and the wall."""
    sheet.alpha_composite(im.resize((max(1, int(w)), max(1, int(h))), Image.LANCZOS),
                          (int(cx - w / 2), int(cy - h / 2)))


def coat(im, tint):
    """`SiegeView.Coat` - the cast's bodies, pulled 62% toward the colour they wear.

    The packs draw four monsters in colours of their own that have nothing to do with this board's
    four, so an untinted raider wears a colour the player has to learn. **The wards do not use
    this** - a tint is a multiply and can only ever darken, which is fine for a raider and was not
    fine for a turret, so those carry a real hue in the sprite (`make_siege_art.hued`).
    """
    if im is None:
        return None

    px = im.load()
    out = im.copy()
    op = out.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            op[x, y] = (int(r + (tint[0] - r) * 0.62),
                        int(g + (tint[1] - g) * 0.62),
                        int(b + (tint[2] - b) * 0.62), a)
    return out


def layout_of(level):
    block = level["siege"]
    grid = proto.Grid(block["rows"], block["width"], block["height"], siege.CELLS)
    return siege.Layout(grid, block["gems"], block["wards"], block["waves"],
                        block.get("boss"), block.get("cogs", 0))


def draw(level, raiders, bolts=True, aim=False, boss="cast"):
    lay = layout_of(level)
    grid = lay.grid

    left, bottom, right, top = INSET
    host = (CANVAS[0] - left - right, CANVAS[1] - top - bottom)

    cell = min((host[0] - MARGIN * 2) / grid.w,
               (host[1] - MARGIN * 2) * MAX_GEM_BAND / grid.h)
    span = (max(cell * grid.w, host[0] - MARGIN * 2), max(cell * grid.h, host[1] - MARGIN * 2))

    gem_band = min(max(cell * grid.h / span[1], 0.28), MAX_GEM_BAND)
    rest = 1.0 - gem_band
    hill_band = rest * (HILL_BAND / (HILL_BAND + LINE_BAND))
    line_band = rest - hill_band

    hill_top = span[1] * 0.5 - cell * 0.35
    hill_foot = span[1] * (0.5 - hill_band)
    line_y = hill_foot - span[1] * line_band * 0.30
    gem_centre = (span[1] * (0.5 - hill_band - line_band) - span[1] * 0.5) * 0.5

    sheet = Image.new("RGBA", CANVAS, BACK + (255,))
    draw_on = ImageDraw.Draw(sheet)

    # The plate, exactly where ProtoView puts it - and rounded at the top only, because the
    # action bar is stacked directly under it (`SiegeView.PlateSkin`, `Art.RoundTop`).
    px = left + host[0] / 2
    py = top + host[1] / 2
    draw_on.rounded_rectangle(
        [px - span[0] / 2 - MARGIN, py - span[1] / 2 - MARGIN,
         px + span[0] / 2 + MARGIN, py + span[1] / 2 + MARGIN],
        radius=34, fill=PLATE + (210,), corners=(True, True, False, False))

    def at(x, y):
        """Field-local (x, y) to canvas pixels. The view's y runs up; a picture's runs down."""
        return px + x, py - y

    # ------------------------------------------------------------------ the hill
    h = hill_top - hill_foot + cell * 0.35
    cx, cy = at(0, (hill_top + hill_foot) / 2)
    stretch(sheet, sprite("hill"), cx, cy, span[0], h + cell * 0.5)

    # ------------------------------------------------------------------ the raiders
    # The last *authored* wave, because the one after it is the warlord and it is drawn on its
    # own below - `SiegeLayout` appends a one-raider wave for the boss (invariant 37t).
    body = [w for i, w in enumerate(lay.waves) if i != lay.boss_wave]
    wave = body[-1] if body else ""
    mob = []
    for i, token in enumerate(wave[:raiders]):
        colour = siege.LETTERS.index(token.lower())
        brute = token.isupper()
        lane = (i * 2 + 1) % LANES
        march = 0.18 + 0.16 * i

        tall = cell * (1.55 if brute else 1.15)
        lx = (lane - (LANES - 1) / 2) * (span[0] / LANES)
        ly = hill_top + (hill_foot - hill_top) * march

        cx, cy = at(lx, ly)
        mob.append((cx, cy))
        put(sheet, coat(reel("brute" if brute else SKINS[colour % 3]), TINTS[colour]),
            cx, cy, tall, tall)

        # The gem over its head, which is the third of the three things that say its colour.
        cx, cy = at(lx - tall * 0.46, ly + tall * 0.58)
        put(sheet, sprite(GEM_ART[siege.LETTERS[colour]]), cx, cy, cell * 0.34, cell * 0.34)

        # The health bar.
        cx, cy = at(lx, ly + tall * 0.58)
        draw_on.rounded_rectangle([cx - tall * 0.36, cy - cell * 0.065,
                                   cx + tall * 0.36, cy + cell * 0.065],
                                  radius=6, fill=(0, 0, 0, 168))
        draw_on.rounded_rectangle([cx - tall * 0.36 + 2, cy - cell * 0.065 + 2,
                                   cx + tall * 0.36 - 2, cy + cell * 0.065 - 2],
                                  radius=6, fill=(178, 120, 255, 255) if brute else (232, 97, 90, 255))

    # ------------------------------------------------------------------ the ward line
    band = span[1] * LINE_BAND
    cx, cy = at(0, hill_foot - band / 2)
    stretch(sheet, sprite("rampart"), cx, cy, span[0], band * 1.02)

    n = len(lay.wards)
    for i, ward in enumerate(lay.wards):
        wide = span[0] / (n + 0.6)
        wx = (i - (n - 1) / 2) * wide

        cx, cy = at(wx, line_y - cell * 0.88)
        put(sheet, sprite("socket"), cx, cy, cell * 1.7, cell * 0.8)

        tint = TINTS[siege.LETTERS.index(ward)]

        # No tint: a ward's colour is baked into its sprite (see make_siege_art.hued), and so is
        # its rank. Drawn one rank apart across the line, so one picture shows the whole ladder -
        # a still that drew four rank-one turrets could not say whether the tiers read as tiers.
        rank = i % 5
        cx, cy = at(wx, line_y + cell * 0.06)
        put(sheet, sprite(ward_art(ward, rank)), cx, cy, cell * 1.72, cell * 2.15)

        # The rank badge on its shoulder - `SiegeView.Badge`. Always drawn, and it says one before
        # a cog has been spent: the ladder is on the board from the first frame.
        cx, cy = at(wx - cell * 0.74, line_y + cell * 0.34)
        crest = ART / "crest.png"
        if crest.exists():
            put(sheet, Image.open(crest).convert("RGBA"), cx, cy, cell * 0.62, cell * 0.62)
        draw_on.text((cx - cell * 0.07, cy - cell * 0.17), str(rank + 1),
                     fill=(255, 243, 220, 255), font=face(int(cell * 0.34)))

        # The ward's own health, as a bar. Drawn three-quarters full, which is what a line that
        # has taken a leak looks like - the state worth checking is legible, not the fresh one.
        cx, cy = at(wx, line_y + cell * 1.26)
        draw_on.rounded_rectangle([cx - cell * 0.53, cy - cell * 0.085,
                                   cx + cell * 0.53, cy + cell * 0.085],
                                  radius=8, fill=(0, 0, 0, 168))
        draw_on.rounded_rectangle([cx - cell * 0.53 + 2, cy - cell * 0.085 + 2,
                                   cx - cell * 0.53 + 2 + cell * 1.02 * 0.72,
                                   cy + cell * 0.085 - 2],
                                  radius=8, fill=(255, 194, 60, 255))

    # The warlord, over the ward line so its ring reads on top of the turrets, and under the
    # exchange so a bolt crossing the hill still passes in front of it.
    warlord(sheet, draw_on, lay, span, cell, hill_top, hill_foot, line_y, at, casting=boss)

    # ------------------------------------------------------------------ the exchange
    # Every ward firing at once, each shot caught at a different point of its flight: the flash
    # still at the barrel, the comet crossing the hill, the impact on the raider. Drawn after the
    # line and before the field because that is where `_fx` sits in the view.
    #
    # **This is the half of the board no number can look at.** Four elements were baked out of the
    # projectile pack so a bolt is told apart by silhouette and not by hue alone; whether that is
    # true at the size a phone draws it is a question only this picture answers.
    if bolts and mob:
        import math

        for i, ward in enumerate(lay.wards):
            wide = span[0] / (len(lay.wards) + 0.6)
            wx = (i - (len(lay.wards) - 1) / 2) * wide
            key = ward   # r, g, b, y - the reels are named for the colour, not the post

            mx, my = at(wx, line_y + cell * 1.0)
            tx, ty = mob[i % len(mob)]

            dx, dy = tx - mx, ty - my
            far = math.hypot(dx, dy) or 1.0
            ux, uy = dx / far, dy / far

            # A different beat per ward, so one picture shows the whole event rather than four
            # copies of one instant.
            along = (0.30, 0.55, 0.78, 0.42)[i % 4]

            aimed(sheet, loudest("muzzle_" + key), mx, my, cell * 2.7, ux, uy, MUZZLE_AT)
            aimed(sheet, blast("shot_" + key, 6),
                  mx + dx * along, my + dy * along, cell * 1.0, ux, uy, HEAD_AT)
            aimed(sheet, loudest("hit_" + key), tx, ty, cell * 3.2, ux, uy)

            # The tally floating off it. Two of the four are drawn as doubles, because the
            # elemental double is the rule this mode is about and it has to read as a different
            # kind of number rather than as a bigger one.
            damage(sheet, tx, ty - cell * 0.7, (14, 6, 22, 8)[i % 4], i % 2 == 0, cell)

    # The chain banner, over the ward line where the view now puts it.
    if bolts:
        bx, by = at(0, line_y + cell * 2.35)
        banner(sheet, bx, by, 3, cell, span[0])

    # ------------------------------------------------------------------ the field
    cx, cy = at(0, gem_centre)
    stretch(sheet, sprite("plate"), cx, cy,
            cell * grid.w + cell * 0.34, cell * grid.h + cell * 0.34)

    for i, c in enumerate(grid.cells):
        gx = (i % grid.w - (grid.w - 1) / 2) * cell
        gy = gem_centre + ((grid.h - 1) / 2 - i // grid.w) * cell
        cx, cy = at(gx, gy)

        draw_on.rounded_rectangle([cx - cell * 0.46, cy - cell * 0.46,
                                   cx + cell * 0.46, cy + cell * 0.46],
                                  radius=16, fill=(255, 255, 255, 12))
        # A cog is drawn a shade smaller than a jewel, so the socket shows around it - it is the
        # one thing on this field that is not a gem, and the gap is the cheapest way of saying so
        # that survives being forty pixels wide (`SiegeView.Mint`).
        side = cell * (GEM_INSET * 0.88 if c == "*" else GEM_INSET)
        put(sheet, sprite(GEM_ART[c]), cx, cy, side, side)

    # ------------------------------------------------------------------ the fuel tubes
    # **Drawn after the field, because the view draws them after the field** (`SiegeView.Compose`
    # gives them a layer of their own above it). They sit on the plate's own top edge, in the strip
    # under the plinths - and every one of them would be hidden behind the plate if it were carried
    # by its turret, which is exactly what used to happen (invariant 37g).
    plate_top = gem_centre + (cell * grid.h + cell * 0.34) / 2
    tube_y = plate_top + cell * 0.22

    for i, ward in enumerate(lay.wards):
        wide = span[0] / (n + 0.6)
        wx = (i - (n - 1) / 2) * wide
        tint = TINTS[siege.LETTERS.index(ward)]

        cx, cy = at(wx, tube_y)
        draw_on.rounded_rectangle([cx - cell * 0.53, cy - cell * 0.115,
                                   cx + cell * 0.53, cy + cell * 0.115],
                                  radius=9, fill=(0, 0, 0, 178))
        draw_on.rounded_rectangle([cx - cell * 0.53 + 2, cy - cell * 0.115 + 2,
                                   cx - cell * 0.53 + 2 + cell * 1.02 * 0.55,
                                   cy + cell * 0.115 - 2],
                                  radius=9, fill=tint + (255,))

    if aim == "hill":
        hill_grid(sheet, span, cell, hill_top, hill_foot, at)
    elif aim == "wards":
        ward_rings(sheet, span, cell, line_y, len(lay.wards), at)

    return sheet


def levels():
    body = json.loads((CHAPTERS / "s01_thornwatch.json").read_text(encoding="utf-8"))
    return body["levels"]


def load(path):
    """One PNG, or None. Unlike `sprite` this takes a path, because the bar's art is not the
    mode's - it is shared UI, which is exactly why it lives in the global group."""
    return Image.open(path).convert("RGBA") if path.exists() else None


def bar(sheet, held):
    """The action bar, where `SiegeScreen` hangs it: filling the foot of the safe area.

    <p>Drawn here rather than left to the imagination because it is not decoration - it is 228
    points of the screen, and it decides how much is left for the three bands above it
    (invariant 37g). What this picture is for is seeing whether the hill, the ward line and the
    field still read with a shelf under them, and whether the shelf reads as one.</p>
    """
    draw_on = ImageDraw.Draw(sheet)
    top = CANVAS[1] - BAR_HEIGHT

    tray = load(UTILITY_ART / "tray.png")
    if tray is not None:
        sheet.alpha_composite(tray.resize((CANVAS[0], BAR_HEIGHT), Image.LANCZOS), (0, top))

    cell = load(UTILITY_ART / "slot.png")

    for i in range(SLOTS):
        cx = CANVAS[0] * (2 * i + 1) / (2 * SLOTS)
        cy = top + BAR_HEIGHT / 2

        if cell is not None:
            # An empty place on the shelf is drawn dimmer than one holding something.
            pane = cell
            if i >= len(UTILITIES):
                pane = cell.copy()
                pane.putalpha(pane.split()[3].point(lambda v: int(v * 0.55)))

            put(sheet, pane, cx, cy, SLOT, SLOT)

        if i >= len(UTILITIES):
            continue

        name = UTILITIES[i]
        n = held.get(name, 0)
        icon = load(UTILITY_ART / (name + ".png"))

        if icon is not None:
            if n <= 0:
                faded = icon.copy()
                faded.putalpha(faded.split()[3].point(lambda v: int(v * 0.36)))
                icon = faded

            put(sheet, icon, cx, cy, ICON, ICON)

        if n > 0:
            bx = cx + SLOT / 2 - 4 - BADGE / 2
            by = cy - SLOT / 2 + 4 + BADGE / 2
            draw_on.ellipse([bx - BADGE / 2, by - BADGE / 2, bx + BADGE / 2, by + BADGE / 2],
                            fill=(24, 34, 46, 255))
            font = face(34)
            if font is not None:
                draw_on.text((bx, by), str(n), font=font, fill=(255, 243, 220, 255), anchor="mm")


def hill_grid(sheet, span, cell, hill_top, hill_foot, at):
    """`SiegeView.AimHill` - what a firepot is aimed with.

    <p>Drawn because it is the one thing on this board that is neither the board nor the bar, and
    because whether twenty panes over a hill full of raiders reads as a target or as a mess is a
    question only a picture answers.</p>
    """
    lanes, rows = LANES, BLAST_ROWS
    top = hill_top + cell * 0.35
    wide = span[0] / lanes
    tall = (top - hill_foot) / rows

    layer = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(layer)

    for row in range(rows):
        for lane in range(lanes):
            cx, cy = at(lane_x(span, lane), top - (row + 0.5) * tall)

            pen.rounded_rectangle(
                [cx - wide / 2 + 3, cy - tall / 2 + 3, cx + wide / 2 - 3, cy + tall / 2 - 3],
                radius=18, fill=(79, 193, 255, 46))

            r = min(wide, tall) * 0.23
            pen.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(79, 193, 255, 200), width=6)

    sheet.alpha_composite(layer)


def ward_rings(sheet, span, cell, line_y, wards, at):
    """`SiegeView.AimWards` - what a mending and a surge are aimed with.

    <p>Drawn because "does the ring cover the turret" is the only question about it, and it is a
    question about two sprites at a size neither of them chose. The first cut was a circle of 1.6
    cells centred a third of a cell high, which sat on the barrel rather than round the ward.</p>
    """
    layer = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(layer)

    for i in range(wards):
        cx, cy = at(post_x(span, i, wards), line_y + cell * 0.06)
        rx, ry = cell * 2.05 / 2, cell * 2.6 / 2

        pen.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], outline=(255, 201, 60, 230), width=7)

    sheet.alpha_composite(layer)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--level")
    ap.add_argument("--raiders", type=int, default=4,
                    help="how many of the first wave to stand on the hill")
    ap.add_argument("--no-bolts", action="store_true",
                    help="draw the board with nothing in flight")
    ap.add_argument("--warlord", default="cast", choices=("cast", "idle", "walk"),
                    help="draw the warlord mid-cast, standing, or walking on")
    ap.add_argument("--no-bar", action="store_true",
                    help="draw the board without the utility bar under it")
    ap.add_argument("--aim", choices=("hill", "wards"),
                    help="draw a utility's targeting: the firepot's grid, or the ward rings")
    ap.add_argument("--out", default=str(REPO / "Tools" / "siege_boards.png"))
    args = ap.parse_args()

    picked = [lv for lv in levels() if args.level in (None, lv["id"])]
    if not picked:
        sys.exit("no level called %s" % args.level)

    held = {"firepot": 2, "mending": 0, "surge": 5}

    shots = []
    for lv in picked:
        shot = draw(lv, args.raiders, not args.no_bolts, aim=args.aim,
                    boss=args.warlord)
        if not args.no_bar:
            bar(shot, held)
        shots.append((lv["id"], shot))

    pad = 24
    sheet = Image.new("RGBA",
                      (len(shots) * (CANVAS[0] + pad) + pad, CANVAS[1] + pad * 2),
                      (24, 24, 30, 255))
    for i, (_, shot) in enumerate(shots):
        sheet.alpha_composite(shot, (pad + i * (CANVAS[0] + pad), pad))

    out = Path(args.out)
    sheet.convert("RGB").save(out)
    print("wrote %s (%s)" % (out, ", ".join(name for name, _ in shots)))


if __name__ == "__main__":
    main()
