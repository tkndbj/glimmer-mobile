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
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import proto                                                            # noqa: E402
import siege                                                            # noqa: E402

ART = REPO / "Assets" / "Game" / "Art" / "Siege"
FX = REPO / "Assets" / "Game" / "Art" / "Fx" / "Siege"
CHAPTERS = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters"

#: The canvas the game lays out in, and the room `SiegeScreen.HostInset` leaves the board:
#: (left, top, right, bottom). Kept in step with the screen by hand - this is a diagnostic, not a
#: rendering, and a copy that drifts is still worth more than no picture at all.
CANVAS = (1080, 1920)
INSET = (10, 190, 10, 300)

#: `SiegeView`'s own numbers.
MARGIN = 18
HILL_BAND, LINE_BAND, GEM_BAND = 0.44, 0.16, 0.40
GEM_INSET = 0.84
LANES = 5

#: `Pal.Board`, over the dark ground a sky is graded against.
PLATE = (14, 27, 37)
BACK = (9, 14, 20)

#: `SiegeView.Tints` - Pal.Poppy, Pal.Mint, Pal.Azure, Pal.Sun.
TINTS = [(242, 64, 79), (123, 216, 106), (79, 193, 255), (255, 201, 60)]

GEM_ART = {"r": "gem_r", "g": "gem_g", "b": "gem_b", "y": "gem_y"}

#: `SiegeView.WardArt` - four turret models, none of them carrying a colour. The colour is `coat`,
#: applied here exactly as the view applies it.
WARD_ART = {"r": "ward1", "g": "ward2", "b": "ward3", "y": "ward4"}
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
    grid = proto.Grid(block["rows"], block["width"], block["height"], siege.LETTERS)
    return siege.Layout(grid, block["gems"], block["wards"], block["waves"])


def draw(level, raiders, bolts=True):
    lay = layout_of(level)
    grid = lay.grid

    host = (CANVAS[0] - INSET[0] - INSET[2], CANVAS[1] - INSET[1] - INSET[3])

    cell = min((host[0] - MARGIN * 2) / grid.w, (host[1] - MARGIN * 2) * GEM_BAND / grid.h)
    span = (max(cell * grid.w, host[0] - MARGIN * 2), max(cell * grid.h, host[1] - MARGIN * 2))

    hill_top = span[1] * 0.5 - cell * 0.35
    hill_foot = span[1] * (0.5 - HILL_BAND)
    line_y = hill_foot - span[1] * LINE_BAND * 0.30
    gem_centre = (span[1] * (0.5 - HILL_BAND - LINE_BAND) - span[1] * 0.5) * 0.5

    sheet = Image.new("RGBA", CANVAS, BACK + (255,))
    draw_on = ImageDraw.Draw(sheet)

    # The plate, exactly where ProtoView puts it.
    px = INSET[0] + host[0] / 2
    py = INSET[1] + host[1] / 2
    draw_on.rounded_rectangle(
        [px - span[0] / 2 - MARGIN, py - span[1] / 2 - MARGIN,
         px + span[0] / 2 + MARGIN, py + span[1] / 2 + MARGIN],
        radius=34, fill=PLATE + (210,))

    def at(x, y):
        """Field-local (x, y) to canvas pixels. The view's y runs up; a picture's runs down."""
        return px + x, py - y

    # ------------------------------------------------------------------ the hill
    h = hill_top - hill_foot + cell * 0.35
    cx, cy = at(0, (hill_top + hill_foot) / 2)
    stretch(sheet, sprite("hill"), cx, cy, span[0], h + cell * 0.5)

    # ------------------------------------------------------------------ the raiders
    wave = lay.waves[-1] if lay.waves else ""
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

        # No tint: a ward's colour is baked into its sprite (see make_siege_art.hued).
        cx, cy = at(wx, line_y + cell * 0.06)
        put(sheet, sprite(WARD_ART[ward]), cx, cy, cell * 1.72, cell * 2.15)

        # The fuel tube, drawn a third full so it reads as a meter rather than as a plinth.
        cx, cy = at(wx, line_y - cell * 0.62)
        draw_on.rounded_rectangle([cx - cell * 0.53, cy - cell * 0.115,
                                   cx + cell * 0.53, cy + cell * 0.115],
                                  radius=9, fill=(0, 0, 0, 158))
        draw_on.rounded_rectangle([cx - cell * 0.53 + 2, cy - cell * 0.115 + 2,
                                   cx - cell * 0.53 + 2 + cell * 1.02 * 0.55,
                                   cy + cell * 0.115 - 2],
                                  radius=9, fill=tint + (255,))

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

            aimed(sheet, loudest("muzzle_" + key), mx, my, cell * 1.9, ux, uy, MUZZLE_AT)
            aimed(sheet, blast("shot_" + key, 6),
                  mx + dx * along, my + dy * along, cell * 0.62, ux, uy, HEAD_AT)
            aimed(sheet, loudest("hit_" + key), tx, ty, cell * 2.35, ux, uy)

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
        put(sheet, sprite(GEM_ART[c]), cx, cy, cell * GEM_INSET, cell * GEM_INSET)

    return sheet


def levels():
    body = json.loads((CHAPTERS / "s01_thornwatch.json").read_text(encoding="utf-8"))
    return body["levels"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--level")
    ap.add_argument("--raiders", type=int, default=4,
                    help="how many of the first wave to stand on the hill")
    ap.add_argument("--no-bolts", action="store_true",
                    help="draw the board with nothing in flight")
    ap.add_argument("--out", default=str(REPO / "Tools" / "siege_boards.png"))
    args = ap.parse_args()

    picked = [lv for lv in levels() if args.level in (None, lv["id"])]
    if not picked:
        sys.exit("no level called %s" % args.level)

    shots = [(lv["id"], draw(lv, args.raiders, not args.no_bolts)) for lv in picked]

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
