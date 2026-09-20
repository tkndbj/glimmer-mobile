# -*- coding: utf-8 -*-
"""Draws the Infinite lane's hub at the size a phone draws it, with the real sprites.

    python Tools/render_endless.py
    python Tools/render_endless.py --unplayed        # before anybody has held a wave
    python Tools/render_endless.py --locked 10       # behind its keeper wall, which is a whole screen
    python Tools/render_endless.py --short           # the squarest canvas this game supports
    python Tools/render_endless.py --out out/hub.png

**Why this exists.** The hub replaced a map, and everything that was wrong with the map was
wrong by eye: a painted island chain with one node loose on it and a sealed teaser promising a
chapter that will never exist. No gate in this project could see any of that, and none can see
whether what replaced it is better - `EndlessHubTests` proves the column clears its own
furniture and says nothing about whether it reads as a *place*.

So this mirrors `EndlessHub` and the half of `LevelsScreen` that stands around it - the plaque,
both switcher pills, the corner keys and the loadout shelf - because the whole
question is whether the column sits well *between* two pieces of furniture that were sized
without it. Drawing the column alone would answer the easy half.

**It is a mirror, and mirrors drift.** Every constant below is named after the field it copies
(`EndlessHubLayout.CrestSize`, `LevelsScreen.BannerY`, `LoadoutBar.Bare`), so a change on one
side is findable on the other - and when a device disagrees with this picture, the picture is
the one that is wrong. It draws the furniture and the copy; it does not draw the tweens, so
nothing here says whether the march reads as a march rather than as five raiders in a row.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

REPO = K.REPO
SIEGE = REPO / "Assets" / "Game" / "Art" / "Siege"
BG = REPO / "Assets" / "Game" / "Art" / "Bg"
LOC = REPO / "Assets" / "StreamingAssets" / "Content" / "loc" / "en.json"
TABLE = REPO / "Assets" / "StreamingAssets" / "Content" / "progression.json"

W = K.W

#: `CanvasFit.ShortestCanvas` - 1080 x 1.75, the squarest display this game lays out for, and
#: the one the hub's column has the least room on. `--short` draws it.
SHORT_H = int(K.W * 1.75)

# --------------------------------------------------------------- EndlessHubLayout
POINTS = 3

# The hero: a starburst, a gold medallion carrying the wave, and a ribbon naming it.
HERO_H = 284.0 + 78.0 / 2      # `EndlessHubLayout.HeroHeight`: derived from the plate
BURST_SIZE = 310.0
DISC_SIZE = 222.0
DISC_DOWN = 155.0                  # the disc's centre: half the burst, so nothing leaves the box
RIBBON_W, RIBBON_H = 360.0, 78.0
RIBBON_DOWN = 284.0

# The panel: three rows, each a framed icon and a sentence.
PANEL_W = 840.0
PANEL_PAD = 16.0
ROW_H, ROW_GAP = 90.0, 6.0
PANEL_H = PANEL_PAD * 2 + ROW_H * POINTS + ROW_GAP * (POINTS - 1)
SLOT_SIZE, ICON_SIZE = 88.0, 68.0

BUTTON_W, BUTTON_H = 620.0, 178.0

HERO_GAP, PANEL_GAP = 24.0, 34.0

#: Where the column sits in a band with room to spare: a little above centre.
#: A tall phone's slack has to go somewhere, and the foot is where a screen wants
#: it - the shelf's tab stands proud of its own plate and the eye ends on the key.
LIFT = 0.42
HEAD_CLEAR = 36.0

HERO_CENTRE = HERO_H / 2
PANEL_CENTRE = HERO_H + HERO_GAP + PANEL_H / 2
BUTTON_CENTRE = HERO_H + HERO_GAP + PANEL_H + PANEL_GAP + BUTTON_H / 2
COLUMN_H = BUTTON_CENTRE + BUTTON_H / 2


def row_centre(i):
    """A row's centre, measured down from the panel's top edge."""
    return PANEL_PAD + ROW_H / 2 + i * (ROW_H + ROW_GAP)


# --------------------------------------------------------------- EndlessHub
#: The three marks. A phoenix for a fight with no end and rising bolts for a hill that gets
#: harder, both cut from the skill-icon pack by `make_siege_art.HUB_ICONS`; then the wallet's
#: own heart for what a watch costs, which is shipped art rather than a pack cut.
#:
#: An int is a pack index and a string is a path under `Assets/Game/Art`, because the third
#: mark is a sprite this game already draws everywhere else and re-cutting it from a pack would
#: be the mirror drawing a picture the screen does not.
ICONS = [90, 84, "Ui/ic_heart"]
ART = REPO / "Assets" / "Game" / "Art"
ICON_PACK = Path(r"C:\Users\Digikey\Downloads\craftpix-net-629015-100-skill-icons-pack-for-rpg")

# --------------------------------------------------------------- LevelsScreen
BANNER_W, BANNER_H, BANNER_Y = 476.0, 138.0, -142.0
CORNER_SIZE, CORNER_X, CORNER_Y = 118.0, 96.0, -132.0

# `LevelsScreen.BoostGap` and the left-hand column it measures. The column hangs off the back
# key's bottom edge; what is in it is the rank badge and then the boost clock.
BOOST_GAP = 18.0
COLUMN_TOP = CORNER_Y - CORNER_SIZE / 2 - BOOST_GAP

#: `RankBadge`'s own block, and **the reason this mirror grew one**: the badge is drawn on the
#: ranked lane only now, so this hub is the only screen in the game that carries it and the
#: only place its neighbours can be judged. Twice the size it was first cut at.
RANK_MARK, RANK_NAME_H, RANK_W = 184.0, 44.0, 192.0
RANK_H = RANK_MARK + RANK_NAME_H
RANK_Y = COLUMN_TOP - RANK_H / 2

BOOST_MARK, BOOST_LABEL, BOOST_CLOCK = 46.0, 48.0, 34.0
BOOST_H = BOOST_MARK + BOOST_CLOCK + 2.0
# `LevelsScreen.BoostY` — the block's *centre*, because `UIKit.Box` always pivots there. Written
# in the screen's own shape rather than as the top edge the drawing wants, so a change to either
# side of the pair is a change to one expression. Under the badge on this lane; on the ordinary
# ladder it takes the badge's seat, which this file cannot draw and `render_ranks.py --map` can.
BOOST_Y = RANK_Y - RANK_H / 2 - BOOST_GAP - BOOST_H / 2
# `LevelsScreen`'s star count is deliberately absent: it is drawn on a laddered lane only, so
# on this one the space under the "i" is empty and the mirror has to say so.
PILL_W, PILL_H, MODES_GAP = 372.0, 116.0, 20.0
MODES_Y = BANNER_Y - BANNER_H / 2 - MODES_GAP - PILL_H / 2
LANE_Y = MODES_Y - PILL_H - MODES_GAP


def header_foot(modes):
    """How far the header column really reaches, which is **not** `LevelsScreen.HeaderUnderside`.

    That constant is the bottom of the *mode* switcher's slot, and the track switcher is drawn
    under it when both are shown - 136 units further down. The map never noticed, because a map
    scrolls under its own header; a column placed against it does.

    The shipped catalog holds one mode, so the mode pill folds away and the track pill takes its
    slot (`HeaderMenu.Build` draws nothing for a question with one answer). `--modeswitch` draws
    the other case.
    """
    return -(LANE_Y if modes else MODES_Y) + PILL_H / 2

#: `LoadoutBar.Bare` - the shelf before the display's own foot, which is what a squarish phone
#: pays. Pad + TurretCell + Gap + KitCell + Pad.
#: ...and `LoadoutBar.Overhang`, the orange tab standing proud of the shelf's own
#: rectangle. The bar's *rect* is the shelf; the tab is drawn outside it, so a button
#: measured against the rect alone is a button the tab can reach.
SHELF = 18.0 + 208.0 + 12.0 + 164.0 + 18.0 + (66.0 - 8.0)


def loc(key, fallback=""):
    """The shipped string, so the picture says what the game says."""
    if not hasattr(loc, "_table"):
        rows = json.loads(LOC.read_text(encoding="utf-8"))["entries"]
        loc._table = {r["key"]: r["text"] for r in rows}
    return loc._table.get(key, fallback)


def sprite(rel):
    p = SIEGE / rel
    return Image.open(p).convert("RGBA") if p.exists() else None


def reel(folder):
    """A sprite set's first frame - what a flipbook shows before it starts."""
    p = SIEGE / folder / "f00.png"
    return Image.open(p).convert("RGBA") if p.exists() else None


def ring(size, thickness, colour, alpha):
    """`Art.Ring` tinted."""
    im = Image.new("RGBA", (int(size), int(size)), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.ellipse([1, 1, size - 2, size - 2], outline=(*colour, int(255 * alpha)),
              width=int(thickness))
    return im


def rank_badge(sheet, held):
    """`RankBadge` under the back key — the picture, and the rank's name under it.

    **Solid in both states.** An account below the first rung is shown the first rung with the
    word *Unranked* under it rather than an empty corner, and it is drawn at full alpha: the
    faded stand-in read as art that had failed to load (`RankBadge`'s class remarks). What says
    *not yet* is the word, in cream rather than a rank's gold.

    Reads the shipped ladder and the shipped badges, so a retune redraws rather than going
    stale — a mirror with its own list answers questions about a screen the game does not draw
    (invariant 44d).
    """
    rungs = (json.loads(TABLE.read_text(encoding="utf-8")).get("ranks") or {}).get("rungs") or []
    if not rungs:
        return

    rung = rungs[held - 1] if 0 < held <= len(rungs) else rungs[0]
    art = REPO / "Assets" / "Game" / "Art" / "Ui" / "Rank" / ("%s.png" % rung["id"])
    if not art.exists():
        return

    top = -RANK_Y - RANK_H / 2
    K.paste(sheet, K.fit(Image.open(art).convert("RGBA"), (RANK_MARK, RANK_MARK)),
            CORNER_X, top + RANK_MARK / 2)

    earned = 0 < held <= len(rungs)
    name = loc("rank.%s.name" % rung["id"], rung["id"]) if earned else loc("ui.ranks.unranked",
                                                                          "Unranked")
    px = K.shrunk(sheet, name, CORNER_X, top + RANK_MARK, RANK_W, RANK_NAME_H, 32, 18,
                  fill=K.GOLD if earned else K.CREAM, outline=0)
    print("  rank name '%s': settled at %dpx against a floor of 18" % (name, px))


def boost_readout(sheet, left):
    """`BoostReadout` — the green arrow, the letters XP, and how long is left.

    Drawn only when a window is running, which is the widget's own rule: an empty clock is a
    control that answers no question, and this sits on a painted map where every pixel is
    somebody's artwork. The mirror takes the remaining seconds as an argument because a render
    has no wallet — what it proves is the *placement*, which is the one thing a fixture cannot.
    """
    top = -BOOST_Y - BOOST_H / 2
    block_w = BOOST_MARK + BOOST_LABEL
    cx = CORNER_X

    mark = K.fit(Image.open(ART / "Ui" / "ic_boost_up.png").convert("RGBA"), (BOOST_MARK, BOOST_MARK))
    K.paste(sheet, mark, cx - block_w / 2 + BOOST_MARK / 2, top + BOOST_MARK / 2)

    K.text(sheet, "XP", cx - block_w / 2 + BOOST_MARK + BOOST_LABEL / 2 - 6,
           top + BOOST_MARK / 2, 34, fill=(146, 226, 122), outline=2)

    hours, minutes = left // 3600, left % 3600 // 60
    clock = f"{hours}h {minutes:02d}m" if left >= 3600 else f"{left // 60}:{left % 60:02d}"
    K.text(sheet, clock, cx, top + BOOST_MARK + BOOST_CLOCK / 2 + 2, 30,
           fill=K.CREAM, outline=2)


# --------------------------------------------------------------- the ground
def plain(sheet, h, wall="plain_ranked"):
    """`Scenery.Plain` - the quiet patterned ground every screen that is not the hub stands on.

    Defaulted to the **ranked** wall here, because this file only ever draws the Infinite lane
    and `EndlessHub.Build` reads the wall off `lane` (`Scenery.WallRanked`). A mirror drawing
    the blue one would be drawing a screen the game does not build (invariant 44d).
    """
    p = BG / ("%s.png" % wall)
    if not p.exists():
        return
    im = Image.open(p).convert("RGBA")
    s = max(W / im.width, h / im.height)
    im = im.resize((int(im.width * s), int(im.height * s)), Image.LANCZOS)
    sheet.alpha_composite(im, ((W - im.width) // 2, (h - im.height) // 2))


def icon(n):
    """One mark, at the size the seat draws it.

    A **string** is shipped art under `Assets/Game/Art`, drawn as it is - it carries its own
    alpha and is the same file the game loads. An **int** is a skill-icon index, rounded into a
    tile the way `make_siege_art.charge` cuts the overcharge glyph: those are painted *on* their
    ground, so keying one out takes the light with it and leaves a scribble.
    """
    if isinstance(n, str):
        f = ART / (n + ".png")
        if not f.exists():
            return None

        im = Image.open(f).convert("RGBA")
        im.thumbnail((int(ICON_SIZE), int(ICON_SIZE)), Image.LANCZOS)
        return im

    f = ICON_PACK / "PNG" / ("skill icon %d.png" % n)
    if not f.exists():
        return None

    im = Image.open(f).convert("RGBA")
    side = min(im.size)
    im = im.crop(((im.width - side) // 2, (im.height - side) // 2,
                  (im.width - side) // 2 + side, (im.height - side) // 2 + side))
    im = im.resize((int(ICON_SIZE), int(ICON_SIZE)), Image.LANCZOS)

    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, im.width - 1, im.height - 1],
                                           radius=14, fill=255)
    tile = Image.new("RGBA", im.size, (0, 0, 0, 0))
    tile.paste(im, (0, 0), mask)
    return tile


# --------------------------------------------------------------- the column
def hero(sheet, top, played, wave, standing=0):
    """The record, drawn as the thing this lane is about rather than as a line of text."""
    cx = W / 2
    cy = top + DISC_DOWN

    K.paste(sheet, K.glow(BURST_SIZE * 1.6, 2.0, K.SUN, 0.20), cx, cy)

    burst = K.fit(K.load("Hud/burst")[0], (BURST_SIZE, BURST_SIZE))
    K.paste(sheet, K.tint(burst, K.GOLD if played else (104, 128, 176), 0.92), cx, cy)

    disc = K.fit(K.load("Hud/cap_on" if played else "Hud/cap_off")[0], (DISC_SIZE, DISC_SIZE))
    K.paste(sheet, disc, cx, cy)

    if played:
        K.text(sheet, str(wave), cx, cy - 4, 116, fill=(82, 54, 15), outline=0)
    else:
        # **An empty star rather than a dash**, which read as a missing character: a medal
        # nobody has earned yet is a thing the kit already draws.
        K.paste(sheet, K.fit(K.load("star_empty")[0], (DISC_SIZE * .52,) * 2), cx, cy)

    # **A sliced trough rather than the kit's ribbon.** `ribbon_orange` is taller than it
    # is wide (188x152) and a caption plate is the other way round, so fitting one here drew
    # it at a third of the width asked for - the fault a still is the only thing that sees.
    K.paste(sheet, K.skin("Hud/trough", RIBBON_W, RIBBON_H), cx, top + RIBBON_DOWN)

    # `EndlessHub.CaptionFor`, mirrored. Three states and the plate never moves, so the
    # question this render exists to answer is whether the longest of them is still legible
    # once Best Fit has shrunk it - `shrunk` returns the size it settled on and the caller
    # prints it, because "22 against a floor of 22" is the tell that the string has outgrown
    # the trough and the trough is what has to change.
    if not played:
        caption = loc("ui.endless.unplayed", "NO RUN YET")
    elif standing > 0:
        caption = loc("ui.endless.standing", "TOP {0}% OF WATCHERS").format(standing)
    else:
        caption = loc("ui.endless.best_label", "BEST WAVE")

    px = K.shrunk(sheet, caption.upper(), cx, top + RIBBON_DOWN,
                  RIBBON_W - 40, RIBBON_H, 34, 22, fill=K.CREAM, outline=3)
    print("  nameplate: %-22r at %dpx (floor 22)" % (caption.upper(), px))


def points(sheet, top):
    """Three rows on one plate: a framed mark and a sentence."""
    ptop = top + HERO_H + HERO_GAP
    K.paste(sheet, K.skin("Hud/panel", PANEL_W, PANEL_H), W / 2, ptop + PANEL_H / 2)

    left = W / 2 - PANEL_W / 2

    for i in range(POINTS):
        cy = ptop + row_centre(i)

        K.paste(sheet, K.skin("Hud/slot", SLOT_SIZE, SLOT_SIZE),
                left + PANEL_PAD + 12 + SLOT_SIZE / 2, cy)

        tile = icon(ICONS[i])
        if tile is not None:
            K.paste(sheet, tile, left + PANEL_PAD + 12 + SLOT_SIZE / 2, cy)

        say = loc("track.infinite.point%d" % (i + 1), "...")
        x = left + PANEL_PAD + 12 + SLOT_SIZE + 26
        room = PANEL_W - (x - left) - PANEL_PAD - 12

        if K.font(34).getlength(say) > room:
            print("  !! line %d is %.0f wide in %.0f of room"
                  % (i + 1, K.font(34).getlength(say), room))

        K.text(sheet, say, x, cy, 34, fill=K.CREAM, outline=3, anchor="l")


def battle(sheet, top, wall=0):
    """`EndlessHub.Battle` - the way in, and the same key wearing its wall when there is one.

    **The locked face is a whole screen and no gate can look at it.** The caption is a sentence
    rather than a word (invariant 42e: the strip names the level, because LOCKED says a player
    cannot have this and not what would change that), so the one question is whether it still
    reads inside a pill sized for BATTLE - which is what this draws and prints.
    """
    cx, cy = W / 2, top + BUTTON_CENTRE
    shut = wall > 0

    # The glow, the breath and the sheen are one decision: nothing invites a press that will be
    # refused, so a shut key carries none of them. Only the glow is drawable here.
    if not shut:
        K.paste(sheet, K.glow(760, 2.1, K.SUN, 0.26), cx, cy)

    if shut:
        K.paste(sheet, K.skin("btn_gray", BUTTON_W, BUTTON_H), cx, cy)
    else:
        K.paste(sheet, K.skin("Hud/btn_gold", BUTTON_W, BUTTON_H), cx, cy)

    mark = "ic_padlock" if shut else "ic_battle"
    box = 88 if shut else 112
    glyph = K.fit(Image.open(K.UI / ("%s.png" % mark)).convert("RGBA"), (box, box))

    caption = (loc("ui.levels.keeper_gate", "reach keeper level {0}").format(wall).upper()
               if shut else loc("ui.endless.battle", "BATTLE"))
    size = 38 if shut else 62

    wide = K.font(size).getlength(caption)
    block = glyph.width + 14 + wide

    # `UIKit.TextButton` gives the label the pill less 40, and `UIKit.OneLine` shrinks it to fit
    # rather than wrapping - so a caption past this is drawn smaller than authored, which is a
    # thing to know about before a device says it.
    room = BUTTON_W - 40 - glyph.width - 14
    if wide > room:
        print("  !! the key says %.0f wide in %.0f of room, so it is shrunk"
              % (wide, room))

    K.paste(sheet, glyph, cx - block / 2 + glyph.width / 2, cy)
    K.text(sheet, caption, cx - block / 2 + glyph.width + 14, cy, size, outline=4, anchor="l")


# --------------------------------------------------------------- the furniture
def header(sheet, modes, boost=0, rank=3):
    """`LevelsScreen.BuildHeader` - what stands above the column and is unchanged by it."""
    fade = Image.new("RGBA", (W, 300), (0, 0, 0, 0))
    d = ImageDraw.Draw(fade)
    for y in range(300):
        d.line([(0, y), (W, y)], fill=(5, 15, 23, int(199 * (1 - y / 300.0))))
    sheet.alpha_composite(fade, (0, 0))

    K.paste(sheet, K.skin("sq_blue", CORNER_SIZE, CORNER_SIZE), CORNER_X, -CORNER_Y)
    K.paste(sheet, K.skin("sq_orange", CORNER_SIZE, CORNER_SIZE), W - CORNER_X, -CORNER_Y)

    rank_badge(sheet, rank)

    if boost:
        boost_readout(sheet, boost)

    K.paste(sheet, K.fit(K.load("Hud/title")[0], (BANNER_W, BANNER_H)), W / 2, -BANNER_Y)
    K.text(sheet, "ENDLESS WATCH", W / 2, -BANNER_Y - 4, 40, fill=(92, 61, 41), outline=0)

    if modes:
        K.paste(sheet, K.skin("btn_violet", PILL_W, PILL_H), W / 2, -MODES_Y)
        K.text(sheet, "THORNWATCH", W / 2, -MODES_Y, 34, outline=3)

    lane_y = -(LANE_Y if modes else MODES_Y)
    K.paste(sheet, K.skin("btn_orange", PILL_W, PILL_H), W / 2, lane_y)
    K.text(sheet, loc("track.infinite.name", "Infinite").upper(), W / 2, lane_y, 34, outline=3)


def shelf(sheet, h):
    """`LoadoutBar` - the four turrets and the five kits, as the block they occupy."""
    top = h - SHELF
    K.paste(sheet, K.skin("Hud/rail_bottom", W, SHELF), W / 2, top + SHELF / 2)

    for i in range(4):
        cx = W * (i + 0.5) / 4
        K.paste(sheet, K.skin("Hud/slot", 196, 196), cx, top + 18 + 104)

        turret = sprite("Wards/bolt_%s.png" % "rgby"[i])
        if turret is not None:
            K.paste(sheet, K.fit(turret, (150, 150)), cx, top + 18 + 104)

    for i in range(5):
        cx = W * (i + 0.5) / 5
        K.paste(sheet, K.skin("Hud/slot", 152, 152), cx, top + 18 + 208 + 12 + 82)


# --------------------------------------------------------------- the screen
def screen(h, played=True, modes=False, wall=0, standing=0, boost=0, rank=3):
    sheet = Image.new("RGBA", (W, h), (*K.GROUND, 255))

    plain(sheet, h)

    foot = header_foot(modes)
    band = h - foot - HEAD_CLEAR - SHELF
    top = foot + HEAD_CLEAR + max(0.0, (band - COLUMN_H) * LIFT)

    hero(sheet, top, played, 12, standing)
    points(sheet, top)
    battle(sheet, top, wall)

    header(sheet, modes, boost, rank)
    shelf(sheet, h)

    print("  canvas %dx%d   band %.0f   column %.0f   air %.0f above, %.0f below"
          % (W, h, band, COLUMN_H, (band - COLUMN_H) * LIFT,
             (band - COLUMN_H) * (1 - LIFT)))

    if COLUMN_H > band:
        print("  !! the column is %.0f taller than the band - the button is behind the shelf"
              % (COLUMN_H - band))

    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--unplayed", action="store_true",
                    help="before anybody has held a wave")
    ap.add_argument("--modeswitch", action="store_true",
                    help="draw the mode pill too, as a catalog with a second mode would")
    ap.add_argument("--locked", type=int, default=0, metavar="LEVEL",
                    help="draw it behind a keeper wall at this level, as a new account meets it")
    ap.add_argument("--standing", type=int, default=0, metavar="TOP",
                    help="draw the nameplate as a standing - 'top N%% of watchers' - which is "
                         "what it says once enough keepers have run the lane")
    ap.add_argument("--short", action="store_true",
                    help="the squarest canvas this game lays out for, where the band is tightest")
    ap.add_argument("--boost", type=int, default=0, metavar="SECONDS",
                    help="draw the XP boost clock under the back key, with this long left")
    ap.add_argument("--rank", type=int, default=3, metavar="RUNGS",
                    help="how many rank rungs are held; 0 draws the unranked corner, which is "
                         "the state every account is in until it has earned one")
    ap.add_argument("--out", type=Path, default=Path("endless.png"))
    args = ap.parse_args()

    # A wall is met by an account that has not run the lane, so the two states arrive together
    # unless the caller says otherwise - drawing a gold medal behind a padlock would be a
    # picture of a state no player can be in.
    out = screen(SHORT_H if args.short else K.H,
                 played=not args.unplayed and args.locked <= 0,
                 modes=args.modeswitch, wall=max(0, args.locked),
                 standing=max(0, min(99, args.standing)),
                 boost=max(0, args.boost),
                 rank=max(0, args.rank))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    main()
