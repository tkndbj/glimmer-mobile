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
  3. **Can you tell a raider's colour at a glance?** It says it three ways and none of them is an
     overlay any more: the body is **hue-rotated in the bake**, it has a **body of its own per
     colour** so the silhouette carries it too, and it wears a **gem over its head**, which is the
     only one of the three that survives two raiders overlapping. The first two used to be a 62%
     multiply and a coloured wash, which is what a `Image.color` tint can do and no more - four
     silhouettes of one value, with everything the packs drew thrown away. If a raider now reads as
     the wrong colour, the bake's pull is the number to move (`make_siege_art.CAST_PULL`).
  3a. **And is a bulwark obviously carrying something?** It is the one raider whose answer is a
     colour rather than a quantity, and the only thing that says so is the shield in its hand. This
     picture is what caught it being drawn in a square box - fitted by width and served short, with
     the shield off the side of the plate (`SiegeView.LaneX`).
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
  8. **And do they throw four different things?** `--warlord storm` draws the frame a spell leaves
     rather than the wind-up, and `--level a,b,c,d` puts the four side by side. A chain that visits
     the wards it is not aimed at, a bombardment, a storm over the whole hill, a converging pair -
     or four colours of one volley? This picture caught four faults on its first frame, every one a
     placement or a value no numeric gate can look at (invariant 37ac): lightning that came out
     white whatever colour threw it, bolts drawn off the top of the plate and over the status bar,
     a bow too small for a volley to read as more than one orb, and a storm dense enough to read as
     noise. **The wind-up is the other half of it** - render `cast` beside `storm` and ask whether
     the ring closing over a ward is still the most legible thing in the cast, because a spectacle
     that eats its own warning has made the mode harder without any number saying so.
"""
from __future__ import annotations

import math
import random
import re
import argparse
import json
import sys
from pathlib import Path

try:
    from PIL import Image, ImageChops, ImageDraw, ImageFilter
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

#: What the system has taken at the foot of the display, in canvas units. Nought here, because
#: `CANVAS` above is a 16:9 sheet and no such display has a home indicator.
#:
#: **`--phone` is what makes this tool able to see the fault it was blind to.** A gap under the
#: action bar was reported from an iPhone and could not be reproduced in any picture this file
#: drew, for the plain reason that it drew a shape no iPhone has: 16:9, and no inset anywhere. The
#: run screens give up the *top* inset deliberately (`RunScreen.SafeEdges`), so the top needs
#: nothing here; the foot is the half that decides where the shelf sits.
SAFE_BOTTOM = 0

#: A tall phone with a home indicator: 19.5:9 at the canvas's own width, and iOS's 34pt strip at
#: the foot of it. `--phone` swaps these in.
PHONE_CANVAS = (1080, 2340)
PHONE_SAFE_BOTTOM = 94

#: A 4:3 tablet, in the canvas units `CanvasFit` gives one. `--tablet` swaps these in.
#:
#: **The shape this file could not draw, which is why the fault it hid was reported from a device
#: rather than found here.** A squarer display is not handed 1080 units across: `CanvasFit` widens
#: the canvas until it is `ShortHeight` (2160) units tall so that every layout keeps its sizes in
#: units and is simply drawn smaller, which for a 4:3 works out at 1620 x 2160. Every picture this
#: tool had ever drawn was 1080 wide, so the one screen in the game that *grows* when the canvas
#: widens - this board, laid out to the width - was drawing a cell a third too big on every tablet
#: and no render could say so (invariant 37cc).
#:
#: No home indicator: an iPad's is a 20pt strip and the board is laid out inside the safe layer
#: either way, and what `--phone` exists to show is the *shelf's* foot rather than the board's.
TABLET_CANVAS = (1620, 2160)
TABLET_SAFE_BOTTOM = 0

#: `CanvasFit.PhoneWidth` - the width every layout in this game was measured against.
PHONE_WIDTH = 1080

#: `UtilityBar.Shelf` and `UtilityBar.MostFoot`. The bar is a shelf that meets the board's plate
#: rather than a strip floating under it, so the bottom inset is the bar and nothing else - but
#: the bar's rect runs to the bottom of the *display* while the board's host is laid out inside
#: the safe layer, which is `UtilityBar.Room`'s whole reason for existing.
BAR_SHELF, BAR_MOST_FOOT = 228, 24


def bar_foot():
    """`UtilityBar.Foot`: how far the cells stand above the bottom of the display."""
    return min(SAFE_BOTTOM, BAR_MOST_FOOT)


def bar_height():
    """`UtilityBar.Height`: the whole shelf, measured from the bottom of the display."""
    return BAR_SHELF + bar_foot()


def inset():
    """`SiegeScreen.HostInset`, in `ModeScreen`'s own order - (left, bottom, right, top).

    <p>The screen's own number is `UtilityBar.Room` - what a board laid out *inside* the safe
    layer has to leave - and this file has no safe layer, so what it wants is that measured from
    the display instead, which is the bar's whole height. Written as the sum rather than as the
    constant so the two halves are visible: on a display with nothing in the way they are the
    same number, which is exactly why the gap was invisible here for as long as it was.</p>
    """
    return (0, SAFE_BOTTOM + max(0, bar_height() - SAFE_BOTTOM), 0, 236)

#: `UtilityBar`'s own numbers.
SLOTS, SLOT, BADGE, ICON = 5, 184, 60, 136
#: What an empty cell draws instead - `UtilityBar.EmptyIconSize`, `EmptyIconLift` and the
#: caption band under it. Mirrored here because whether the picture and the words clear each
#: other inside the slot's own well is the one question about them a number cannot answer.
EMPTY_ICON, EMPTY_LIFT = 88, 30
HINT_W, HINT_H, HINT_Y = 156, 48, -45
UTILITY_ART = REPO / "Assets" / "Game" / "Art" / "Ui" / "Utility"
#: In `order`, as `progression.json` authors it, with the seconds each one cools for. The
#: cooldown is here because it is 228 points of screen doing something a number cannot
#: describe: whether a cell counting down still reads as an item you have, and whether the
#: seconds are legible over the picture. Mirrors the `utilities` block.
UTILITIES = ["firepot", "mending", "surge", "stormcall"]
COOLDOWNS = {"firepot": 10, "mending": 15, "surge": 20, "stormcall": 30}

#: `SiegeView`'s own numbers. The field is laid out to the *width* and the hill and the line
#: then share what is left in the proportion below - see `SiegeView.Fit` and `MaxGemBand`.
MARGIN = 18
HILL_BAND, LINE_BAND, MAX_GEM_BAND = 0.54, 0.16, 0.52

#: `SiegeView.LineFloor`: the least the ward line may be, in cells. What stands on it is
#: sized off the cell, so a band that is only a share of what the field leaves can be
#: squeezed under the turrets on a short display.
LINE_FLOOR = 1.5
GEM_INSET = 0.84
LANES = 5

#: `SiegeTuning.BlastRows`.
BLAST_ROWS = 4
BLAST_REACH = 1

#: `Pal.Board`, over the dark ground a sky is graded against.
#: `Pal.Ember` - the warm outer rung of the ladder a strike is lit on (white core, `Pal.Sun`
#: body, this in the air around it). See `SiegeShotBake.StrikeHalo`.
EMBER = (255, 107, 87)

PLATE = (14, 27, 37)
BACK = (9, 14, 20)

#: `SiegeView.Tints` - Pal.Poppy, Pal.Mint, Pal.Azure, Pal.Amber.
TINTS = [(242, 64, 79), (123, 216, 106), (79, 193, 255), (255, 138, 43)]

GEM_ART = {"r": "gem_r", "g": "gem_g", "b": "gem_b", "y": "gem_y", "*": "gem_cog"}

#: `SiegeView.CharmFace` - which stone a charmed gem *is*, per charm and per colour.
#:
#: **A gem of its own rather than a mark worn over one**, which is the correction this table was
#: rewritten for: a lance and a stormglass used to be a white glyph printed on one of the four
#: jewels, and the owner's verdict was that the charms were the existing gems with an icon on them.
#: A prism is absent on purpose and always was - it is a face in `GEM_ART`'s sense, because it is
#: the one gem here that is not a colour.
CHARM_ART = {siege.LANCE: "gem_lance", siege.STORM: "gem_storm",
             siege.FURNACE: "gem_furnace", siege.HOURGLASS: "gem_hourglass",
             siege.ANVIL: "gem_anvil"}

#: `SiegeView.MarkInset` and `.RingInset`, as fractions of a cell.
#: `SiegeView.CharmInset` and `RingInset` - how big a charmed stone and its halo are drawn.
#:
#: A charmed stone is a shade larger than a plain one, because both charmed cuts are pointed or
#: round where the four are broad: fitted to the same box a star and an orb draw visibly smaller
#: than the stones beside them, which would say a charm is a lesser gem.
CHARM_INSET, CHARM_RING = 1.06, 1.18

#: `SiegeView.HeaveFront` - how deep the anvil's front is drawn, in cells. Mirrored rather than
#: imported, exactly as every other figure in this file is and for its reason: this tool runs with
#: no Unity anywhere.
#:
#: **The frame count is no longer written down here.** It was, against `HEAVEFRONT_FRAMES`, and the
#: two drifted the moment the reel was re-cut - the mirror then drew frame 11 of a 24-frame reel as
#: its last, which is a mirror quietly showing half an animation. `frames_in` counts what is on
#: disk, which is the same thing the view now does (`frames.Length / HeaveSweep`).
HEAVE_FRONT = 1.5

#: **`Pal.Rope` and `Pal.Glass` are gone from this file, and their absence is the point.** They were
#: what the anvil's front and the hourglass's front and dial were tinted with here, mirroring a view
#: that lent the same two colours - a dull tan and a near-white. All four reels carry their own paint
#: now (`make_siege_art.HEAVE_RAMP`, `STILL_RAMP`) and the view lends `Color.white`, so this mirror
#: draws them through `faded` rather than `tinted`. Keeping a tint here "just in case" is how a mirror
#: comes to report exactly the washed-out front the re-cut was for, which is invariant 44d's rule
#: about a comfortable lie said in reverse.

#: `SiegeLayout.Bomb` - what a bomber drops. Drawn as the firepot the player already owns, which
#: is what the view draws it as: tapping it throws exactly what a firepot throws, so a second
#: picture would be a second name for one thing.
BOMB_ART = "firepot"

#: `Pal.Gold` - the colour of the ring a standing bomb wears. Not `TINTS`, deliberately: a cog's
#: ring is the colour of the ward it ranks and a bomb ranks nothing, so it wears the one colour
#: this palette keeps for "a thing to take".
BOMB_RING = (255, 194, 60)


def utility(name):
    """A picture out of the action bar's own folder, which is where the firepot lives."""
    from PIL import Image as _I
    path = ART.parent / "Ui" / "Utility" / (name + ".png")
    if not path.exists():
        raise SystemExit("missing %s - run: python Tools/make_utility_art.py --write" % path.name)
    return _I.open(path).convert("RGBA")

#: Which four turrets the render stands on the line.
#:
#: **Four different models rather than four of the starter**, because the one thing this picture
#: is for that no number can answer is whether a line of four *chosen* turrets reads as a line -
#: four silhouettes side by side at the size a phone draws them, each in its own colour, each on
#: its own rank. `--line` swaps them.
LINE = ["bolt", "rime", "mortar", "harpoon"]   # shelf rungs 1, 5, 9 and 17

#: `SiegeView.WardArt` - the turret the player stood on this colour, baked in that colour.
#:
#: **A model rather than a tier**, which is what twenty player-chosen turrets cost the picture: the
#: silhouette belongs to the *choice* now, so the rank is carried by the plinth under it
#: (`rank_art`) and by the badge at its shoulder. Nothing here is tinted, for invariant 37l's
#: reason - `Image.color` is a multiply and a turret has to read as lit.
def ward_art(model, colour):
    if legendary(model):
        return "Wards/%s" % model
    return "Wards/%s_%s" % (model, colour)


def legendary(model_id):
    """`WardModel.Legendary` - whether this turret wears no ward colour at all.

    <b>Read off `progression.json` rather than typed</b>, exactly as `shelf` reads the rung: the
    flag is content, and a list here would be the copy that goes stale the first time a drop
    adds one. What it decides in this file is every address a legendary owns - its picture, its
    recoil and its three reels are all cut once (`make_legend_fx.py`), so a mirror that asked
    for `_r` would draw a white box and report the board as broken.
    """
    return bool(shelf_rows().get(model_id, {}).get("legendary"))


#: `WardModel.Elemental` - the one turret whose bolt is a different element on each colour. Every
#: other model throws one effect of its own, named after its id and baked in all four ward colours.
#:
#: **Named outright rather than derived**, which is the C# side's own rule and for its reason: it
#: used to be read off the ability, and that was honest only while the starter was the only model
#: without one. Never "the free one" - that is invariant 16j's trap, and it is wrong today anyway,
#: since the turret with the elemental set is one somebody pays credits for.
ELEMENTAL = "breaker"


#: `SiegeView.BodyWide` / `.BodyTall` and `.BarrelGap` - how wide a turret is drawn and how far
#: from its middle a twin hull carries its barrels, as a fraction of its own picture.
BODY_WIDE, BODY_TALL = 1.72, 2.15
BARREL_GAP = 0.097
APART_ON_ARRIVAL = 0.8

#: `SiegeView.Barrels` - how many bolts leave a turret, which is a fact about the **hull** and so
#: about the shelf rung rather than about the id (the art tool's rule is that the hull *is* the
#: rung). Mirrored by shelf position, so it survives the shelf being re-rung the way the game's
#: own copy does.
TWIN_RUNGS = (11, 14, 15, 17, 21)


def shelf_rows():
    """The authored roster, keyed by id. Read once."""
    if not hasattr(shelf_rows, "_rows"):
        path = REPO / "Assets" / "StreamingAssets" / "Content" / "progression.json"
        models = json.loads(path.read_text(encoding="utf-8"))["wards"]["models"]
        shelf_rows._rows = {m["id"]: m for m in models}

    return shelf_rows._rows


def shelf():
    """Which rung each turret stands on, read from `progression.json`.

    Read rather than typed, because the whole point of keying barrels on the rung is that the
    shelf can be re-rung - so a list here would be the copy that goes stale.
    """
    return {wid: int(m.get("order") or 0) for wid, m in shelf_rows().items()}


def barrels(model_id):
    """How many barrels the turret standing here is drawn with."""
    return 2 if shelf().get(model_id, 0) in TWIN_RUNGS else 1


def shot_key(kind, model, colour):
    """`WardModel.ShotFor` - which reel this turret throws, in this colour.

    Nothing is tinted on the way in: a roster reel is baked in all four ward colours, because a
    multiply can only darken and what makes these effects read is variation in hue rather than in
    value (invariant 37l, and `SiegeShotBake.BakeTurret`).
    """
    if model == ELEMENTAL:
        return "%s_%s" % (kind, colour)
    if legendary(model):
        return "%s_%s" % (kind, model)
    return "%s_%s_%s" % (kind, model, colour)


#: `SiegeView.BoltScale` - how big each turret's bolt is drawn, as a multiple of the ordinary one.
#:
#: <b>Mirrored because it is the one thing about a projectile that is a decision rather than a
#: bake</b>: every reel is framed round its own content, so how much of a frame an effect fills
#: says nothing about how big it is on the hill. A mirror that drew every bolt the same size would
#: report a sun and a dart as the same object, which is precisely the comparison this picture
#: exists to make.
BOLT_SCALES = {"apex": 1.55, "eclipse": 1.62, "breaker": 1.24,
               "spectrum": 1.16, "harpoon": 1.10}

#: What a legendary's bolt is drawn at when it is not named above. `SiegeView.LegendaryBolt`.
LEGENDARY_BOLT = 1.18


def bolt_scale(model):
    if model in BOLT_SCALES:
        return BOLT_SCALES[model]
    return LEGENDARY_BOLT if legendary(model) else 1.0


#: `SiegeView.StillFront`, `.DialWide` and `.DialInk` - how deep the hourglass's wavefront is
#: drawn, and how wide and how lit the dial it hangs over the hill is.
STILL_FRONT, DIAL_WIDE, DIAL_INK = 1.55, 4.2, 0.84



def frames_in(name):
    """How many frames a reel under `Art/Siege` really has.

    **Counted rather than mirrored**, which is the one figure in this file that may not be typed:
    every other constant here describes a *decision* the game makes and would be wrong to guess,
    but a reel's length is a fact about a folder, and a typed copy of it goes stale silently the
    first time the reel is re-cut.
    """
    folder = ART / name
    return len(list(folder.glob("f*.png"))) if folder.is_dir() else 0


def faded(im, alpha=1.0):
    """A painted reel lent `Color.white`: its own paint, at a share of its alpha.

    The mirror of `SiegeView.Wall` and `Dial` handing `Pal.A(Color.white, ink)` to a sprite that
    carries its own colours. `tinted` is the other case - a reel cut white because four ward
    colours have to come out of one drawing (invariant 37l).
    """
    if alpha >= 1.0:
        return im.copy()

    out = im.copy()
    out.putalpha(out.split()[3].point(lambda v: int(v * alpha)))
    return out


def sweep_wall(sheet, at, name, frm, to, t, deep_cells, ink, span, cell, end_ink=0.34):
    """`SiegeView.Wall` - one painted front, drawn where it stands at `t` of its climb.

    **One routine for both charms, because the view has one.** An hourglass's wall of stopped time
    and an anvil's wall of driven ground travel, broaden and fade identically and differ only in
    the reel; two copies here would drift from each other exactly as the two in the view did.

    The position carries the view's own `Ease.OutQuad`. It did not, and that is the class of
    mirror bug invariant 44d is about - a front drawn at a fraction of the distance the game would
    not have it at is a picture answering a question nobody asked.
    """
    if t <= 0.0:
        return

    t = min(1.0, t)
    frames = frames_in(name)
    face = reel(name, int(t * max(0, frames - 1)))
    if face is None:
        return

    deep = cell * deep_cells * (0.80 + (1.24 - 0.80) * t)
    band = face.resize((int(span[0]), max(1, int(deep))), Image.LANCZOS)
    band = faded(band, ink + (ink * end_ink - ink) * t * t * t)

    eased = 1.0 - (1.0 - t) * (1.0 - t)
    x, y = at(0.0, frm + (to - frm) * eased)
    sheet.alpha_composite(band, (int(x - band.width / 2), int(y - band.height / 2)))


def tinted(im, colour, alpha=1.0):
    """`Image.color` on a white reel: the sprite's own alpha, wearing one colour."""
    from PIL import Image as _I
    solid = _I.new("RGBA", im.size, tuple(colour) + (255,))
    a = im.split()[3]
    if alpha < 1.0:
        a = a.point(lambda v: int(v * alpha))
    solid.putalpha(a)
    return solid


#: `SiegeView.RankTint` - what colour a ward's badge is at this rank. Steel, bronze, silver, gold,
#: white-hot, climbing in value as well as in hue.
RANK_TINTS = [(158, 173, 189), (217, 140, 82), (219, 227, 240), (255, 204, 77), (255, 250, 230)]


#: `SiegeView.Skin` - one body per colour, per kind. **Twelve reels rather than four, and none of
#: them is tinted here**: a raider used to be one of three creeper models multiplied toward its
#: colour at run time, which `Image.color` can only do by darkening. The colour is baked now
#: (`make_siege_art.RAIDER_SET`), so this draws the sprite as it is - which is also what makes this
#: render able to say whether the bake is any good.
#: Where a raider's shadow sits and how big it is, and how much of its frame a body fills -
#: `SiegeView.ShadowDrop`, `ShadowWide`, `ShadowTall`, `BodyLift` and `BodyFill`, mirrored.
#:
#: <b>This picture drew no shadow at all until a player reported the cast floating</b>, which is
#: the whole reason it is here: the one instrument this mode has for anything a number cannot see
#: was blind to the single widget that says a thing is standing on the ground. A render that draws
#: less than the screen cannot report what the screen gets wrong.
SHADOW_DROP, SHADOW_WIDE, SHADOW_TALL, BODY_LIFT = 0.21, 0.78, 0.37, 0.04

#: `SiegeView.BlazeWide` and `.BlazeSeat` - how wide the flame a burning raider wears is drawn
#: against the **width** of its body, and where the seat of the fire sits in its own frame.
#:
#: <b>Against the width, which is the whole of what the first cut of it got wrong.</b> Every body
#: on this hill is drawn far wider than it is tall, so a fire scaled by `Mob.Height` is a candle
#: standing on a beetle four times its width. Nothing numeric could have said so.
BLAZE_WIDE, BLAZE_SEAT = 1.16, 0.90


def body_fill(boss):
    return 0.72 if boss else 0.94


#: The alpha and falloff of the sprite the view puts under a raider - `SiegeView.ShadowInk`
#: and `ShadowFalloff`, mirrored.
#:
#: <b>Mirrored as the formula rather than approximated with a blurred ellipse</b>, which is the
#: difference between an instrument and a decoration: the power decides how much of the rect is
#: actually dark, so a blurred ellipse stood in for it draws a shadow of a quite different size and
#: weight - and this picture's whole job is saying whether that blob is the right size, in the
#: right place, and solid enough to ground the thing above it.
SHADOW_ALPHA, SHADOW_POWER = 0.55, 0.25


def shadow(sheet, cx, cy, wide, tall, boss=False):
    """The soft dark blob under a raider, drawn where `SiegeView.Hatch` puts it.

    **One set of numbers for every cast**, which is what withdrawing the baked one bought back:
    that cast was rendered at this board's rake with its feet at the bottom edge of the picture,
    so it needed a contact shadow most of a half-height down and carried four constants of its
    own. Every body this mode draws now is a flat cut from a bought sheet.
    """
    body = tall * body_fill(boss)
    drop, across, deep, alpha = SHADOW_DROP, SHADOW_WIDE, SHADOW_TALL, SHADOW_ALPHA
    w, h = max(2, int(wide * across)), max(2, int(body * deep))

    ys, xs = [(i + 0.5) / h * 2 - 1 for i in range(h)], [(i + 0.5) / w * 2 - 1 for i in range(w)]
    blob = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = blob.load()
    for y, dy in enumerate(ys):
        for x, dx in enumerate(xs):
            d = math.sqrt(dx * dx + dy * dy)
            if d >= 1.0:
                continue
            px[x, y] = (0, 0, 0, int(255 * alpha * (1.0 - d) ** SHADOW_POWER))

    sheet.alpha_composite(blob, (int(cx - w / 2),
                                 int(cy - h / 2 - BODY_LIFT * tall + body * drop)))


#: How many raiders are drawn wearing an ember turret's flame, nearest the line first. Set by
#: `main` from `--alight`.
#:
#: <b>A count rather than a flag, because the question the picture answers is about a *hill*.</b>
#: One burning body says whether the fire is the right size; three say whether a wave of them is
#: still a wave anybody can read - which is the one thing a preview panel cannot be asked, and the
#: reason this lives here as well as in `render_ward_preview.py`.
ALIGHT = 0

#: Which cast is being drawn. The empty string is the insects, which is what a chapter draws
#: unless something says otherwise.
#:
#: **Mirroring `SiegeMode.CastFor`** - a chapter's cast is arithmetic on its ordinal and the
#: Infinite lane draws a *medley*, so `--wave` draws what that lane really draws. Set by `main`;
#: a board drawn without one is the authored chapter's.
CAST = ""

#: The Infinite lane's square, mirroring `SiegeMode.MedleyOrder`: which family each kind draws in
#: each colour. Rows are creepers, brutes, bulwarks; columns are `siege.LETTERS`.
#:
#: **Written out rather than derived**, exactly as `CHAPTER_CASTS` is and for its reason: this
#: tool has no catalog, and a mirror that computes the answer a second way is a mirror that can
#: quietly draw a hill the game does not (invariant 44d).
MEDLEY = (
    ("",       "brood",  "bone",   "court"),
    ("rabble", "wild",   "court",  ""),
    ("bone",   "rabble", "wild",   "brood"),
)

#: Which row of `MEDLEY` a kind reads, in `SiegeMode.CastAddress`'s own order.
MEDLEY_ROWS = {"mon": 0, "brute": 1, "bulwark": 2}


def skin(kind, colour):
    cast = MEDLEY[MEDLEY_ROWS[kind]][colour] if CAST == "medley" else CAST

    return "%s%s_%s" % (cast, kind if not cast else kind[0].upper() + kind[1:],
                        siege.LETTERS[colour])


#: What a weaver and a thief leave on the field. Drawn rather than cut (invariant 32b, from the
#: other side): a web has to let the gem's colour read through it and a sack has to be the dullest
#: thing on a field of four saturated jewels.
FIELD_MARKS = ("web", "sack")


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

#: `SiegeView.StrikeAt` - how far up its own frame a stormcall's strike lands, drawn.
#:
#: **This is the one of the three that had never been mirrored, and it was wrong in the game.** The
#: reel was framed with its flash in the middle and drawn with its *centre* on the raider, so the
#: lightning went off nearly three cells over the target's head with nothing at all where it was
#: aimed - reported from a device as strikes landing at random spots rather than on enemies. No
#: gate in this project opens a PNG, so this picture is the only thing that can ever say whether a
#: strike lands on the thing it struck.
STRIKE_AT = 0.17

#: `SiegeView.StormTall` - how many cells tall a strike's whole frame is drawn.
STORM_TALL = 5.0


#: `SiegeView.HeadRoom` - how far behind the muzzle a bolt is drawn before it has flown anywhere,
#: in **cells**. Small, because it is hidden under the muzzle flash rather than by being deep;
#: see `SiegeView.Emerged`.
HEAD_ROOM = 0.35


def emerged(flown, cell, tall):
    """`SiegeView.Emerged` - how much of its own frame a bolt draws, top down.

    <b>Mirrored because the mirror is what found the fault it fixes.</b> A bolt drawn whole on the
    frame it is fired paints three or four cells of trail straight back down through the turret,
    so what a player reads is the chassis with a flame through it rather than a barrel firing.
    What is drawn is the head plus however far the shot has flown; a mirror still drawing the
    full reel would report a board that no longer exists.
    """
    if tall <= 0:
        return 1.0
    return max(0.0, min(1.0, (1.0 - HEAD_AT) + (HEAD_ROOM * cell + flown) / tall))


def aimed(sheet, im, hx, hy, wide, ux, uy, head=0.5, fill=1.0):
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

    # **Cropped from the bottom rather than scaled**, which is `SiegeView.Emerged`'s own
    # correction: squashing a reel drags anything drawn off its centre-line up beside the head.
    # The turn below happens after, so the cut travels with the shot exactly as it does on the
    # board.
    if fill < 1.0:
        im = im.crop((0, 0, im.width, max(1, int(round(tall * fill)))))

    # PIL turns counter-clockwise about the centre, and a picture's y runs down.
    import math
    spin = -math.degrees(math.atan2(ux, -uy))
    turned = im.rotate(spin, Image.BICUBIC, expand=True)

    back = (head - 0.5) * tall
    cx = hx - ux * back
    cy = hy - uy * back

    # **A crop shrinks the picture; `Image.fillAmount` does not shrink the rect.** The game leaves
    # the reel's box where it is and simply stops drawing part of it, so the head never moves. A
    # cropped PIL image pasted at the full frame's centre lands half the missing length too far
    # back down the shot - which drew every bolt behind where the board puts it, and is the third
    # time in one drop that a mirror invented a fault (44d). The centre moves toward the head by
    # half of what was cut.
    slid = (tall - tall * fill) * 0.5
    cx += ux * slid
    cy += uy * slid

    sheet.alpha_composite(turned, (int(cx - turned.width / 2), int(cy - turned.height / 2)))


def struck(sheet, cx, cy, cell, frame, plate_top):
    """One bolt of a stormcall, landing on the raider standing at (cx, cy).

    **Anchored by where it hits and never by the frame's middle**, which is the whole of what
    `SiegeView.StrikeAt` is for - see that constant. The reel is baked with the flash near the foot
    of the picture and the bolt filling the rest, so the sprite's centre goes below the target by
    however far the flash sits from it.

    It also draws the ground the bolt lands on - a warm scorch and a ring - because that is what
    `SiegeView.Struck` draws and a bolt with no lit ground under it is a picture of lightning
    rather than of something being struck.
    """
    im = blast("storm", frame)
    if im is None:
        return

    # One size wherever it lands, cut off at the top of the board - `SiegeView.Bolt` and the
    # `_strikes` layer's mask. Sizing it to the room above the target instead made a bolt on a
    # raider half way up the hill a cell and a half long, which reads as a spark.
    tall = cell * STORM_TALL
    wide = tall * im.width / im.height
    im = im.resize((max(1, int(wide)), max(1, int(tall))), Image.LANCZOS)
    im = im.transpose(Image.FLIP_TOP_BOTTOM)

    # The ground first, so the bolt and its sparks are drawn over their own light. Small: the reel
    # carries the bright half of the burst and this is only the warm rung under it.
    glow = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    g = ImageDraw.Draw(glow)
    rx, ry = cell * 0.95, cell * 0.42
    for k in range(7):
        t = k / 6.0
        g.ellipse((cx - rx * (1 - t * 0.8), cy - ry * (1 - t * 0.8),
                   cx + rx * (1 - t * 0.8), cy + ry * (1 - t * 0.8)),
                  fill=EMBER + (int(14 + 26 * t),))
    sheet.alpha_composite(glow)

    back = (STRIKE_AT - 0.5) * tall
    top = int(cy + back - im.height / 2)

    # The board clips it - `_strikes` carries a `RectMask2D`. Without this the picture would show
    # a bolt over the status bar that the game does not draw, which is a mirror lying the other
    # way round.
    cut = int(max(0, plate_top - top))
    if cut >= im.height:
        return
    if cut:
        im = im.crop((0, cut, im.width, im.height))
        top += cut

    sheet.alpha_composite(im, (int(cx - im.width / 2), top))


#: The face the game draws with (invariant 46a: `Fonts/GameFont` is a role). It is in the repo,
#: so it is always the one that answers - the fallbacks are for a checkout that has not got it.
GAME_FONT = REPO / "Assets" / "Game" / "Fonts" / "GameFont.ttf"


def face(size):
    """The shipped display face at `size`, or None if this machine has none to offer.

    **The game's own face rather than a bold system one**, because half of what this file is now
    asked is *how wide* a caption comes out (`fit_line`) - and a width measured in Arial is a
    width answering a question about a different font. Titan One is a third wider than Arial
    Bold at the same point size, which is the difference between a caption that fits the board
    and one drawn off both sides of it.
    """
    from PIL import ImageFont
    size = max(1, int(size))
    try:
        return ImageFont.truetype(str(GAME_FONT), size)
    except OSError:
        pass
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


#: `SiegeView.Captions` - the hill's two captions, as cells above the ward line. Both are
#: measured from the *same* anchor, which is the whole of that class: they were a cell apart on
#: paper and shared about 1.4 cells of row on every screen shape, because one was measured from
#: the ward line and the other from the hill's foot and those two move apart as the board changes
#: shape. This picture could not see it, because it drew the chain and never the wave banner.
CHAIN_RISE, CHAIN_BOX, CHAIN_DRIFT = 2.35, 1.25, 0.30
WAVE_BOX, WAVE_SWELL, WAVE_FLOAT = 0.90, 1.6, 0.80
CAPTION_CLEAR = 0.22

#: `SiegeView.CaptionGutter`, `CaptionFloor` and `WavePopFloor` - the air a caption leaves at each
#: end of the board, the smallest it may be shrunk to, and the least a banner may open by.
CAPTION_GUTTER, CAPTION_FLOOR, WAVE_POP_FLOOR = 0.35, 0.26, 1.15

#: `SiegeView.Announce`'s own sizes: an ordinary wave, a boss walking on, a boss falling, and the
#: word a last phase turning says.
WAVE_SIZE, BOSS_SIZE, FELLED_SIZE, TURN_SIZE = 0.46, 0.78, 0.70, 0.62


def caption_room(span, cell):
    """`SiegeView.Captions.Room` - the width a caption on this hill may draw into."""
    return max(cell, span[0] - cell * CAPTION_GUTTER * 2)


def fit_line(text, size, room, floor):
    """`UIKit.OneLineLabel` - the largest whole point size at which one line fits `room`.

    The same shape as the view's, because it is the same answer: one ratio off the measured
    width, then the last point or two walked off by hand, since a face's metrics are per glyph
    and do not scale evenly.
    """
    size = max(1, int(size))
    floor = max(1, int(floor))

    font = face(size)
    if font is None:
        return None, size, 0.0

    wide = font.getlength(text or "")
    if wide > room > 0:
        size = max(floor, int(size * room / wide))
        font = face(size)
        while size > floor and font.getlength(text or "") > room:
            size -= 1
            font = face(size)

    return font, size, font.getlength(text or "")


def caption_pop(room, wide):
    """`SiegeView.Captions.Pop` - how far a banner may open, given what is left of the room."""
    if wide <= 0 or room <= 0:
        return WAVE_SWELL

    return min(WAVE_SWELL, max(1.0, room / wide))


def captions(line_y, cell):
    """Where the chain banner and the wave banner sit. `SiegeView.Captions.Of`."""
    chain = line_y + CHAIN_RISE * cell
    chain_high = chain + (CHAIN_BOX / 2 + CHAIN_DRIFT) * cell
    half = WAVE_BOX * WAVE_SWELL / 2 * cell

    return chain, chain_high + CAPTION_CLEAR * cell + half


def wave_banner(sheet, cx, cy, text, cell, span, fill=(242, 236, 220, 255),
                size=WAVE_SIZE, swell=False):
    """`SiegeView.Announce` - what the hill says when a wave walks on, or a boss falls.

    **Drawn beside the chain banner rather than alone**, because the one thing a picture is needed
    for here is whether the two share a row - and a cascade during a wave's arrival is ordinary.

    **Drawn at the widest frame of the pop**, which is the frame that used to leave the screen: a
    banner opens at `WAVE_SWELL` and shrinks into place, so a still of the settled size would
    answer the easy half of the question this picture is now asked.
    """
    room = caption_room(span, cell)
    floor = cell * CAPTION_FLOOR

    font, pt, wide = fit_line(text, cell * size,
                              room / WAVE_POP_FLOOR if swell else room, floor)
    if font is None:
        return

    font = face(pt * (caption_pop(room, wide) if swell else 1.0))
    if font is None:
        return

    layer = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(layer)
    ring = max(2, int(cell * 0.05))

    for dx in range(-ring, ring + 1):
        for dy in range(-ring, ring + 1):
            if dx * dx + dy * dy > ring * ring:
                continue
            pen.text((cx + dx, cy + dy), text, font=font, fill=(23, 36, 51, 250), anchor="mm")

    pen.text((cx, cy), text, font=font, fill=fill, anchor="mm")
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
#: `SiegeTuning.BossPhases` and `SiegeView.MarkWide` - how many phases a boss fight has, and how
#: thick the notch on its bar is, in cells. **A mirror**: it moves in the same change as the C#.
BOSS_PHASES = 3
MARK_WIDE = 0.05

BOSSES = {
    "blightcaller": dict(hold=0.58, tall=3.0, stem="blight", fx="hex", fire=(59, 233, 216)),
    "warlord": dict(hold=0.46, tall=3.1, stem="boss", fx="spell", fire=(180, 120, 255)),
    "warbringer": dict(hold=0.46, tall=3.3, stem="bringer", fx="roar", fire=(255, 244, 206)),
    "overlord": dict(hold=0.38, tall=3.5, stem="over", fx="omen", fire=(255, 116, 212)),
    # `Pal.Verdant` and `Pal.Glass` - the two that land on the hill rather than on the line, so
    # neither can be read as "this hurts the green ward more" (`SiegeView.Casting`).
    #
    # **Both said `fx="roar"` until the day the drawings were counted, and that is a mirror
    # telling a comfortable lie about the screen** (invariant 44d). It was a true mirror when it
    # was written - the game really did draw a gravemaw and a bonecaller in the warbringer's two
    # reels under colours of their own - so this table was quietly the only written-down record of
    # invariant 37z being broken, and it read as a palette decision. Each has its own pair now
    # (`SiegeShotBake.Maw`, `.Crypt`).
    "gravemaw": dict(hold=0.62, tall=3.2, stem="maw", fx="maw", fire=(84, 228, 140)),
    "bonecaller": dict(hold=0.40, tall=3.5, stem="caller", fx="crypt", fire=(220, 235, 245)),
    # `Pal.Dormant` and `Pal.Radiance` - iron and dust. **Two chapters late**: the fourth
    # chapter's pair shipped with no row here at all, so `--warlord cast` could not draw either
    # of them and the one gate that can see a boss's cast has never been pointed at half the
    # bosses in the game.
    "shackler": dict(hold=0.68, tall=3.2, stem="snare", fx="snare", fire=(58, 80, 100)),
    "ironclad": dict(hold=0.42, tall=3.7, stem="clad", fx="quake", fire=(255, 244, 206)),
    # `Pal.Sun` and `Pal.Thorn` - lightning and stone, the fifth chapter's pair. Both rows were
    # written with the bosses rather than two chapters late (the shackler's lesson, one row up).
    "thunderer": dict(hold=0.50, tall=3.3, stem="thunder", fx="levin", fire=(255, 201, 60)),
    "colossus": dict(hold=0.40, tall=3.8, stem="colossus", fx="boulder", fire=(138, 112, 96)),
    # `Pal.Rope` and `Pal.Ember` - sun-bleached limestone and heat off sand, the sixth chapter's
    # pair. Both rows written with the bosses, for the shackler's reason two comments up.
    "gorgon": dict(hold=0.52, tall=3.4, stem="gorgon", fx="gaze", fire=(217, 195, 154)),
    "sunlord": dict(hold=0.44, tall=3.6, stem="sunlord", fx="decree", fire=(255, 107, 87)),
}

#: Which bosses are aimed at no ward, and therefore draw a pair of reels where they stand rather
#: than something crossing the hill - `SiegeTuning.AimsAtAWard`, mirrored.
#:
#: **A set rather than a test on `fx == "roar"`**, which is the clause the game itself had to
#: correct on the same day: naming one of the three grounded bosses and letting the other two
#: fall through whichever branch they happened to land in is how two of them came to draw a flat
#: ground wash standing upright in the air.
GROUNDED = ("roar", "maw", "crypt")


def warlord(sheet, draw_on, kind, colour, wards, span, cell, hill_top, hill_foot, line_y, at,
            casting, lane=None, slot=0):
    """A boss holding its place on the hill, and the spell it is winding up.

    **Told what it is rather than asked a layout**, because the Infinite lane authors no boss at
    all - what stands there is a rule read at a wave number (`SiegeEndless.BossesAt`) - and from
    wave 21 it sends **two**. `slot` is which rung of the top of the board its health hangs from,
    which is `SiegeView.FreeCrown`: a crown is anchored rather than carried, so a second one drawn
    at the first one's y is two readouts nobody can read (invariant 37u).

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
    if not kind or not colour:
        return

    # A level names its boss by kind (`SiegeLayout.BossNames`), so which of the four this is comes
    # out of the file rather than out of a case bit; an endless wave names it the same way, out of
    # the ramp instead of out of a body.
    look = BOSSES.get(kind)
    if look is None:
        return

    greater = kind == "overlord"
    letter = colour
    colour = siege.LETTERS.index(letter)
    tint = TINTS[colour]
    fire = look["fire"]

    tall = cell * look["tall"]

    # **Standing on its ground, except while it is walking to it.** `SiegeTuning.HoldOf` is where
    # a boss stops, so every other picture here is of one in place; drawing the entrance at that
    # same spot would be a walk cycle on a body that has arrived, which is the fault this flag
    # exists to look at, drawn.
    reached = look["hold"] * (0.5 if casting == "walk" else 1.0)
    ly = hill_top + (hill_foot - hill_top) * reached

    # **A lone boss walks down the middle and a pair stands either side of it**, which is
    # `SiegeBoard.Muster`'s own rule rather than this picture's - the middle lane is where a
    # player looks for one, so a second arriving there would be two enormous bodies in one column.
    cx, cy = at(0.0 if lane is None else lane_x(span, lane), ly)

    # `SiegeView.Follow` - the walk while it is crossing ground, the idle once it is standing.
    # This picture is of a boss in place, so the idle is what it draws unless asked otherwise, and
    # `--warlord walk` is the entrance.
    stem = look["stem"]

    # **Two reels per boss, and there used to be a third.** Every pack here draws its bosses one
    # animation, so standing and walking are one picture and the board is the only thing that
    # moves it (`SiegeView.WalkReel`). Three of the eight were rendered out of rigged 3D for one
    # drop and did genuinely stand still when they arrived, so they carried a walk of their own;
    # that bake is withdrawn, and `--warlord walk` went with it rather than being left as a flag
    # that silently draws the stand (invariant 44e).
    body = None
    if casting == "cast":
        body = reel(stem + "_cast")

    if body is None:
        body = reel(stem)
    if body is None:
        return

    wide = tall * body.width / body.height

    if casting == "cast":
        spark = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        pen = ImageDraw.Draw(spark)
        pen.ellipse([cx - tall * 0.5, cy - tall * 0.5, cx + tall * 0.5, cy + tall * 0.5],
                    fill=fire + (150,))
        spark = spark.filter(ImageFilter.GaussianBlur(cell * 0.42))
        sheet.alpha_composite(spark)

    # **No coat and no aura.** A boss used to be drawn through the same 62% multiply the raiders
    # were, plus a coloured wash behind it - which on a body this large read as "a purple alien
    # with a red light on it" rather than as red, and threw away everything the pack drew. There is
    # only ever one boss on the hill, and its colour is carried by the gem beside its health bar,
    # which is drawn at two thirds of a cell.
    put(sheet, body, cx, cy, wide, tall)

    # `SiegeView.Crown` - a warlord's health is pinned across the top of the board rather than
    # carried, which is what lets it be three cells tall on a hill four cells deep.
    wide = span[0] * 0.60
    bx, by = at(cell * 0.34, hill_top + cell * 0.12 - slot * cell * 0.42)

    draw_on.rounded_rectangle([bx - wide / 2, by - cell * 0.13, bx + wide / 2, by + cell * 0.13],
                              radius=11, fill=(0, 0, 0, 178))
    draw_on.rounded_rectangle([bx - wide / 2 + 2, by - cell * 0.13 + 2,
                               bx - wide / 2 + 2 + wide * 0.64, by + cell * 0.13 - 2],
                              radius=11, fill=(255, 107, 87, 255))

    # `SiegeView.Marks` - the bar is cut into phases (`SiegeTuning.BossPhases`), one notch at
    # every threshold in the trough's own ink. The notch is placed by the same arithmetic the
    # fill is drawn with, so it sits on the pixel the fill reaches when the phase turns.
    for p in range(BOSS_PHASES - 1):
        share = (BOSS_PHASES - 1 - p) / float(BOSS_PHASES)
        mx = bx - wide / 2 + 2 + (wide - 4) * share
        draw_on.rounded_rectangle([mx - cell * MARK_WIDE / 2, by - cell * 0.13 + 1,
                                   mx + cell * MARK_WIDE / 2, by + cell * 0.13 - 1],
                                  radius=3, fill=(0, 0, 0, 199))

    # **No ring.** A boss used to stand in a circle of its own fire while its guard was up, and
    # `--warlord guard` was the picture that judged it. The guard is a floor under the boss's
    # health now (`SiegeTuning.BossPhases`) - the line fires at a boss from the frame it plants -
    # so there is no window for a ring to mark, and the ring itself was withdrawn at the owner's
    # instruction for the reason every other ring in this mode was: a circle laid over a boss
    # reads as a shape drawn on top of the board rather than as something the boss is doing. The
    # mirror drops it in the same change (invariant 44d).

    # No gem beside the bar: a boss wears no colour (37dn).

    if casting not in ("cast", "storm"):
        return

    # **The release, which is a different picture from the wind-up and needs to be.** `--warlord
    # cast` draws the tell - the ring closing over a ward, which is the thing a player has to read
    # in time to answer it - and `--warlord storm` draws the frame the spell leaves, which is what
    # invariant 37ac is about. Rendering the two side by side is the only way to answer the
    # question the change was made against: is the spectacle louder than the warning?
    if casting == "storm":
        # The seed is the level's own boss and slot, so the picture is the same picture every run
        # and a difference in it is a difference in the code (`Tools/siege_sweep.py`'s rule).
        random.seed(hash((kind, letter, slot)) & 0xFFFF)

        aimed_at = 1 if slot == 0 else 2
        storm(sheet, kind, look, fire,
              at(0.0 if lane is None else lane_x(span, lane), ly + tall * 0.1),
              at(post_x(span, aimed_at, wards), line_y + cell * 0.3),
              cell, span, at(0, hill_top)[1], (hill_top, hill_foot), at,
              wards, aimed_at,
              (at(-span[0] * 0.5, hill_top)[0], at(0, hill_top)[1],
               at(span[0] * 0.5, hill_top)[0], at(0, hill_foot)[1]))
        return

    # **A roar is thrown at nothing**, so what is drawn for a warbringer is its ring going out over
    # the hill rather than a ring closing on a ward - which is the one thing a picture of this mode
    # can say about a boss that takes ground instead of health.
    if look["fx"] in GROUNDED:
        for suffix, size in (("_hit", 7.0), ("_muzzle", 9.0)):
            ring = loudest(look["fx"] + suffix)
            if ring is not None:
                put(sheet, ring, cx, cy + tall * 0.1, cell * size, cell * size)
        return

    # The spell in the air, and the ring closing over the ward it is coming for. A pair aims at
    # two different turrets, or the second ring is drawn inside the first.
    ward = 1 if slot == 0 else 2
    wx, wy = at(post_x(span, ward, wards), line_y + cell * 0.3)

    # **`SiegeView.Winding` - the crackle and the tether, drawn *under* the ring.** They are the
    # loud half of the wind-up (invariant 37ac) and the ring is the half that has to be read, so
    # the whole point of putting them in this picture is being able to see whether one has eaten
    # the other. The order is the answer: the warning goes on top.
    random.seed(hash((kind, letter, slot, "wind")) & 0xFFFF)
    plate = (at(-span[0] * 0.5, hill_top)[0], at(0, hill_top)[1],
             at(span[0] * 0.5, hill_top)[0], at(0, hill_foot)[1])
    hand = (cx, cy + tall * 0.1)

    for _ in range(3):
        turn = random.uniform(0, math.tau)
        reach = tall * 0.38 * 1.6
        axis = (math.cos(turn) * reach, math.sin(turn) * reach)
        bolt(sheet,
             onboard((hand[0] - axis[0], hand[1] - axis[1]), plate),
             onboard((hand[0] + axis[0], hand[1] + axis[1]), plate),
             fire, cell * 0.075, cell, jag=0.3, forks=1)

    bolt(sheet, hand, (wx, wy), fire, cell * 0.045, cell, jag=0.22)

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



# ------------------------------------------------------------------ the storm
#: `SiegeView.ArcSegments`. Each segment is three `Image`s on the real board and a storm is nine
#: bolts with forks on them, so this number is multiplied by about sixty before it reaches a frame.
#: A bolt with ten bends in it is not visibly straighter than one with fourteen.
ARC_SEGMENTS = 10


def joints(a, b, jag, cell):
    """`SiegeView.Joints` - a straight run pushed sideways, pinched at both ends.

    The pinch is the whole of it: `sin(pi*t)` is nought at both ends, so a bolt leaves the hand
    that threw it and arrives on the thing it hit, and wanders only in between. Without that one
    term this is a broken line rather than lightning.
    """
    (ax, ay), (bx, by) = a, b
    dx, dy = bx - ax, by - ay
    length = math.hypot(dx, dy)

    steps = max(3, min(ARC_SEGMENTS, int(round(length / (cell * 0.7)))))
    sx, sy = (-dy / length, dx / length) if length > 0.001 else (1.0, 0.0)

    out = []
    for i in range(steps + 1):
        t = i / steps
        push = random.uniform(-1.0, 1.0) * jag * cell * math.sin(t * math.pi)
        out.append((ax + dx * t + sx * push, ay + dy * t + sy * push))
    return out


def onboard(p, plate):
    """`SiegeView.OnBoard` - a point pulled back inside the plate.

    **A bolt that leaves the board is a bolt drawn on the app's background**, and nothing about the
    effects layer stops one: it is sized to the field and carries no mask, so a strike out of the
    sky and a bolt thrown off a boss's hand both happily draw over the status bar. This picture is
    what said so, on the first frame it ever drew (invariants 37g, 37u - a placement is exactly
    what no numeric gate can look at).
    """
    (x0, y0, x1, y1) = plate
    return (min(max(p[0], x0), x1), min(max(p[1], y0), y1))


def bolt(sheet, a, b, fire, wide, cell, jag=0.34, forks=0, alpha=255):
    """`SiegeView.Arc` - one jagged bolt, a white filament inside a coloured sheath.

    **Two passes and both are needed.** Real lightning is white with a coloured glow round it, and
    a bolt drawn in a flat hue reads as a painted stick; the halo is what says whose lightning
    this is on a hill already carrying four ward colours.
    """
    trunk = joints(a, b, jag, cell)
    lines = [(trunk, wide)]

    for _ in range(forks):
        root = trunk[random.randrange(1, max(2, len(trunk) - 1))]
        dx, dy = b[0] - a[0], b[1] - a[1]
        span = math.hypot(dx, dy) or 1.0
        ux, uy = dx / span, dy / span
        turn = random.uniform(0.5, 1.3) * random.choice((-1.0, 1.0))
        vx, vy = ux - uy * turn, uy + ux * turn
        vlen = math.hypot(vx, vy) or 1.0
        reach = span * random.uniform(0.22, 0.42)
        tip = (root[0] + vx / vlen * reach, root[1] + vy / vlen * reach)
        lines.append((joints(root, tip, jag * 1.4, cell), wide * 0.58))

    # **Three passes rather than two, and the middle one is the whole reason the colour survives.**
    # A white filament in a blurred halo is what lightning looks like in isolation and *not* what it
    # looks like over a lit hill: at the size a phone draws it the halo is thin enough to read as an
    # edge, so the bolt comes out white and a magenta overlord throws the same lightning a teal
    # blightcaller does. The sheath is drawn at full strength in the boss's own colour, at about
    # twice the filament's width, so the thing carries its colour even where the glow is lost.
    halo = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(halo)
    for path, w in lines:
        pen.line(path, fill=fire + (int(alpha * 0.55),), width=max(3, int(w * 4.2)), joint="curve")
    sheet.alpha_composite(halo.filter(ImageFilter.GaussianBlur(cell * 0.16)))

    sheath = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(sheath)
    for path, w in lines:
        pen.line(path, fill=fire + (alpha,), width=max(3, int(w * 2.0)), joint="curve")
    sheet.alpha_composite(sheath)

    core = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(core)
    hot = tuple(int(c + (255 - c) * 0.62) for c in fire)
    for path, w in lines:
        pen.line(path, fill=hot + (alpha,), width=max(2, int(w)), joint="curve")
    sheet.alpha_composite(core)


def strike(sheet, spot, fire, wide, cell, sky):
    """`SiegeView.Strike` - a bolt out of the sky, and the ground answering it.

    It comes down from above the hill rather than from the boss, which is what makes a volley read
    as weather rather than as more throwing: a boss that only ever emits things has one direction,
    and a strike gives the board a second one.
    """
    x, y = spot
    start = (x + random.uniform(-cell * 0.8, cell * 0.8), sky + cell * random.uniform(0.0, 0.5))
    bolt(sheet, start, (x, y), fire, wide, cell, jag=0.26, forks=1)

    flash = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(flash)
    r = cell * 1.1
    pen.ellipse([x - r, y - r, x + r, y + r], fill=fire + (150,))
    sheet.alpha_composite(flash.filter(ImageFilter.GaussianBlur(cell * 0.3)))


def storm(sheet, kind, look, fire, hand, target, cell, span, sky, hill, at, wards, ward, plate):
    """`SiegeView.Unleash` - the frame a spell leaves, drawn as the volley it is.

    <p><b>This is the picture the whole change exists for.</b> Every gate in this project reads the
    model, and the model did not move at all: the same spell, the same tell, the same flight, the
    same damage and the same cadence. What moved is what a player watches across a window they were
    already waiting through, and there is no number anywhere that can say whether it is worth
    watching.</p>

    <p>Two questions to ask of what comes out. Are the four <em>shapes of attack</em> different - a
    chain that visits the wards it is not aimed at, a bombardment, a storm over the whole hill, a
    converging pair - or is it four colours of one volley? And is the ward the thing is coming for
    still the most legible object on the board, or has the spectacle eaten its own tell?</p>

    `hand` and `target` are canvas pixels; `hill` is the hill's own (top, foot) in board units.
    """
    scale = 1.2 if kind == "overlord" else 0.85 if kind == "blightcaller" else 1.0
    wx, wy = target

    # `SiegeView.Leaving` - the pack's muzzle, and bolts thrown off the hand. A warbringer's muzzle
    # reel is spoken for: `Roar` draws it flat over the ground, so drawing it upright here as well
    # would be the same picture twice, once wrong.
    # `SiegeView.Leaving` - the three grounded bosses lay their own muzzle reel *flat* over the
    # ground, so drawing it upright here as well is the same picture twice, once wrong. Asked of
    # the set rather than of one name, which is the correction the game itself needed.
    if look["fx"] not in GROUNDED:
        flare = loudest(look["fx"] + "_muzzle")
        if flare is not None:
            put(sheet, flare, hand[0], hand[1], cell * 4.6 * scale, cell * 4.6 * scale)

    for i in range(5):
        turn = i / 5 * math.tau + random.uniform(-0.3, 0.3)
        away = cell * random.uniform(1.2, 2.0) * scale
        bolt(sheet, hand,
             onboard((hand[0] + math.cos(turn) * away, hand[1] + math.sin(turn) * away), plate),
             fire, cell * 0.055, cell, jag=0.4, forks=1)

    orb = blast(look["fx"], 9)

    def fly(t, bowed):
        """One arm of the volley at phase `t`, bowed sideways by `SiegeView.Hurl`'s own sine."""
        dx, dy = wx - hand[0], wy - hand[1]
        length = math.hypot(dx, dy) or 1.0
        sx, sy = -dy / length, dx / length
        # **The bow is more than a cell at full lean, and it has to be.** At half a cell three orbs
        # on spread arcs are one orb drawn three times and an overlord's pair is a single sun; what
        # makes a volley read as a volley is that the arms are visibly *apart* in the middle of the
        # flight and visibly *together* at the end of it.
        push = bowed * 1.4 * math.sin(t * math.pi) * cell
        return hand[0] + dx * t + sx * push, hand[1] + dy * t + sy * push

    if kind == "blightcaller":
        # **Chain lightning** - the only one of the four whose bolt visits the wards it is not
        # aimed at, which is exactly what a douse is: something spreading through the line and
        # settling on one of them. In post order rather than by distance, so the walk is the same
        # shape every time and can be learned.
        path, taken = [hand], 0
        for i in range(wards):
            if i == ward or taken >= 2:
                continue
            path.append(at(post_x(span, i, wards), hill[1]))
            path[-1] = (path[-1][0], wy)
            taken += 1
        path.append(target)

        for i in range(len(path) - 1):
            bolt(sheet, path[i], path[i + 1], fire, cell * 0.075, cell, forks=1)

        if orb is not None:
            put(sheet, orb, *fly(0.72, 0.55), cell * 1.35, cell * 1.35)

    elif kind == "warlord":
        # **Four strikes and a triple volley.** A smite takes health, so what it looks like is a
        # bombardment: three orbs on spread arcs and four bolts out of the sky onto the ward.
        if orb is not None:
            for t, bowed, size in ((0.78, -0.7, 0.62), (0.66, 0.0, 1.0), (0.55, 0.7, 0.62)):
                put(sheet, orb, *fly(t, bowed), cell * 1.6 * size, cell * 1.6 * size)

        for _ in range(4):
            strike(sheet, (wx + random.uniform(-cell * 0.5, cell * 0.5), wy), fire,
                   cell * 0.07, cell, sky)

    elif kind == "warbringer":
        # **A storm over the whole hill**, because a roar is aimed at nothing and the one thing it
        # has to say is that everything out there is about to move.
        for suffix, size in (("_hit", 7.0), ("_muzzle", 9.0)):
            ring = loudest(look["fx"] + suffix)
            if ring is not None:
                put(sheet, ring, hand[0], hand[1], cell * size, cell * size)

        # Six rather than nine, spread a little wider apart: nine read as noise over the hill
        # rather than as six things being struck, and cost half as much again on a real frame.
        for _ in range(6):
            spot = at(random.uniform(-span[0] * 0.44, span[0] * 0.44),
                      random.uniform(hill[1], hill[0]))
            strike(sheet, spot, fire, cell * 0.07, cell, sky)

        for i in range(2):
            y = hill[1] + (hill[0] - hill[1]) * (0.3 + i * 0.35)
            bolt(sheet, at(-span[0] * 0.5, y), at(span[0] * 0.5, y),
                 fire, cell * 0.05, cell, jag=0.5, forks=3)

    elif kind == "shackler":
        # **One shot, straight, and a chain paying out behind it.** Everything else thrown at a
        # ward here is a volley because everything else is a bombardment; a bind is one arrow
        # finding one turret, so the straightness *is* the reading and it takes no bow at all.
        if orb is not None:
            put(sheet, orb, *fly(0.74, 0.0), cell * 1.15, cell * 1.15)

        for i in range(3):
            bolt(sheet, hand, target, fire, cell * 0.05 * (1 + i * 0.35), cell, jag=0.02, forks=0)

    elif kind == "ironclad":
        # **One heavy thing, thrown high and arriving downward.** An axe is the only thing in the
        # mode that has to read as *falling*, so it leaves from above the boss's head and bows
        # hard - what the eye follows is a rise and a drop rather than a crossing.
        if orb is not None:
            put(sheet, orb, *fly(0.68, -1.1), cell * 1.9, cell * 1.9)

        for _ in range(3):
            strike(sheet, (wx + random.uniform(-cell * 0.5, cell * 0.5), wy), fire,
                   cell * 0.08, cell, sky)

    else:
        # **Double rockets** - the finale's spell takes a rank as well as health, so it is the one
        # that has to look like more than a bigger smite: two orbs bowing hard in opposite
        # directions and converging on the ward together, with a bolt riding down between them.
        if orb is not None:
            for t, bowed in ((0.70, -1.35), (0.62, 1.35)):
                put(sheet, orb, *fly(t, bowed), cell * 2.1 * 0.85, cell * 2.1 * 0.85)

        for _ in range(3):
            strike(sheet, (wx + random.uniform(-cell * 0.7, cell * 0.7), wy), fire,
                   cell * 0.085, cell, sky)

        bolt(sheet, hand, target, fire, cell * 0.06, cell, jag=0.3, forks=2)


def post_x(span, index, wards):
    """`SiegeView.PostX`, at module scope so the ward rings can use it too."""
    wide = span[0] / (wards + 0.6)
    return (index - (wards - 1) / 2) * wide


#: `SiegeLanes.Stray` / `.Home` / `.Spread` - how far a raider may stray from its colour's own
#: lane, and how often it does not. Three rolls in five keep it at home, so a colour reads as a
#: column with stragglers rather than as a band three lanes wide.
STRAY = 1
HOME = 3
SPREAD = HOME + STRAY * 2


def lane_home(ward, wards):
    """`SiegeLanes.HomeOf` - the lane a ward's own raiders walk down.

    **Mirrored rather than approximated**, because a picture that draws a different hill from the
    one being played is this mode's one diagnostic lying about its subject (invariant 44d). The
    raiders used to be laid out at `(i * 2 + 1) % LANES`, which was as good as anything while a
    lane was dealt and is simply wrong now that a lane is a raider's colour.
    """
    if wards <= 1:
        return LANES // 2

    at = max(0, min(wards - 1, ward))
    return (at * (LANES - 1) * 2 + (wards - 1)) // ((wards - 1) * 2)


def lane_walk(ward, wards, roll):
    """`SiegeLanes.Walk` - its colour's lane, strayed by `roll`."""
    home = lane_home(ward, wards)
    pick = roll % SPREAD

    lane = home if pick < HOME else home - STRAY if pick == HOME else home + STRAY
    return max(0, min(LANES - 1, lane))


def lane_x(span, lane):
    """`SiegeView.LaneX` - where a raider stands, inset so a wide reel stays on the plate.

    It was `span[0] / LANES` here for a long time while the view used `LANES + 0.6`, so the mirror
    stood every boss a little further out than the board does - and the raiders beside it were
    already drawn on the view's pitch, so the picture disagreed with itself."""
    return (lane - (LANES - 1) * 0.5) * (span[0] / (LANES + 0.6))


#: `SiegeView.CogNudge` - how far a cog sits from the middle of its box, so a bomber dropping both
#: does not stack two taps on one point.
COG_NUDGE = 0.26


#: `SiegeView.BossKey` - which key each boss is announced under. **Written out rather than built
#: by concatenation**, exactly as the view writes it out (invariant 6), so a boss added without a
#: key of its own is missing here rather than quietly announced as a warlord.
BOSS_KEY = {
    "blightcaller": "mode.siege.blightcaller",
    "warlord": "mode.siege.boss",
    "warbringer": "mode.siege.warbringer",
    "overlord": "mode.siege.overlord",
    "gravemaw": "mode.siege.gravemaw",
    "bonecaller": "mode.siege.bonecaller",
    "shackler": "mode.siege.shackler",
    "ironclad": "mode.siege.ironclad",
    "thunderer": "mode.siege.thunderer",
    "colossus": "mode.siege.colossus",
    "gorgon": "mode.siege.gorgon",
    "sunlord": "mode.siege.sunlord",
}

_LOC = {}


def loc(key, *args):
    """`Loc.Get` / `Loc.Format`, read from the strings the game ships.

    **Read rather than copied**, because the copy went stale: two bosses shipped after the table
    below was written out by hand and both of them drew as THE WARLORD here, which is the one
    fault the banner itself exists to stop (`SiegeCaptionTests.EveryBossIsAnnouncedAsItself`)
    wearing a mirror's clothes.
    """
    if not _LOC:
        rows = json.loads((REPO / "Assets" / "StreamingAssets" / "Content" / "loc"
                           / "en.json").read_text(encoding="utf-8"))
        for row in (rows.get("entries", rows) if isinstance(rows, dict) else rows):
            _LOC[row["key"]] = row["text"]

    said = _LOC.get(key, key)
    for i, arg in enumerate(args):
        said = said.replace("{%d}" % i, str(arg))

    return said


def boss_banner(kind):
    """What a boss of `kind` is announced as - `SiegeView.BossKey` through the shipped strings."""
    return loc(BOSS_KEY.get(kind, "mode.siege.boss"))


def foretell(sheet, lay, wave, span, cell, hill_top, hill_foot, at):
    """`SiegeView.Foretell` - the forecast band, drawn on the hill during a breather.

    **The one moment the largest surface on the screen is empty**, which is what makes it free to
    draw there: a breather is a cleared hill, so the forecast costs the board nothing and lands
    exactly where the player has to look to use it.
    """
    draw_on = ImageDraw.Draw(sheet)

    sent = coming(lay, wave)
    if not sent:
        return

    owed = {}
    for letter, _ in sent:
        owed[letter] = owed.get(letter, 0) + 1

    most = max(owed.values()) if owed else 1
    seats = list(lay.wards)

    # **A boss wave is announced as itself and the gem row comes off.** The forecast's whole job is
    # to say which colour to bank, and the answer before a boss is not a colour - it is *that a
    # boss is coming*. Reported from play as confusing: a blightcaller and a warbringer ride the
    # head of their last authored wave (37ad), so the row really did have counts to show, and they
    # were the wrong news.
    kinds = [k for _, k in sent if k in BOSSES]
    if not kinds and not lay.endless and lay.boss_wave >= 0:
        kinds = [lay.boss_kind]

    facing = kinds[0] if kinds else None

    mid = (hill_top + hill_foot) / 2
    cx, cy = at(0, mid + (cell * 0.2 if facing else cell * 0.85))

    # Fitted to the board like every other caption on this hill (`SiegeView.Foretell`): a boss's
    # name is drawn here at half a cell of type and "THE BLIGHTCALLER" is sixteen characters.
    said = boss_banner(facing) if facing else loc("mode.siege.next")
    font, _, _ = fit_line(said, cell * (0.52 if facing else 0.34),
                          caption_room(span, cell), cell * CAPTION_FLOOR)

    write(sheet, draw_on, said, font, cx, cy,
          (255, 255, 255, 255) if facing else (242, 236, 220, 255))

    step = cell * 1.35
    first = -(len(seats) - 1) * step / 2

    for i, ward in enumerate(() if facing else seats):
        many = owed.get(ward, 0)
        size = cell * (0.54 + (many / most) * 0.30) if most else cell * 0.54

        gx, gy = at(first + i * step, mid)

        gem = sprite(GEM_ART[ward])
        if gem is not None:
            if not many:
                gem = gem.copy()
                gem.putalpha(gem.getchannel("A").point(lambda a: int(a * 0.38)))
            put(sheet, gem, gx, gy, size, size)

        tx, ty = at(first + i * step, mid - cell * 0.62)
        write(sheet, draw_on, str(many) if many else "-", face(int(cell * 0.40)), tx, ty,
              (242, 236, 220, 255) if many else (255, 255, 255, 86))

    # Whole seconds, counted down: a number that ticks is a clock, and one that runs to two decimal
    # places is a readout nobody can use.
    sx, sy = at(0, mid - cell * 1.4)
    write(sheet, draw_on, str(int(siege_breather())), face(int(cell * 0.54)), sx, sy,
          (255, 255, 255, 255) if facing else (255, 199, 92, 255))


def siege_breather():
    """`SiegeTuning.Breather` - the shortest a quiet may be once the hill has been cleared."""
    return 4


def box_wide(span):
    """`SiegeView.BoxWide` - the aiming grid tiles the board flat, and is not inset."""
    return span[0] / LANES


def box_at(span, hill_top, hill_foot, lane, row):
    """`SiegeView.BoxAt` - the middle of one box of the hill's aiming grid.

    Worked out from the march mapping rather than beside it: `march` nought is `hill_top` and one
    is `hill_foot`, so the boxes a player taps are the bands `SiegeTuning.RowOf` reads. The view
    laid them out from `hill_top + cell * 0.35` for a long time, which put every boundary on the
    screen up to a third of a cell above the boundary the rule used."""
    march = (row + 0.5) / BLAST_ROWS
    return ((lane - (LANES - 1) * 0.5) * box_wide(span),
            hill_top + (hill_foot - hill_top) * march)


def in_blast(at_lane, at_row, lane, row):
    """`SiegeTuning.InBlast` - the plus a firepot burns, around the box that was tapped."""
    return abs(lane - at_lane) + abs(row - at_row) <= BLAST_REACH


def boss_lane(index, bosses):
    """`SiegeTuning.BossLane` - the middle alone, either side of it as a pair.

    Mirrored rather than approximated, because the whole use of this picture is answering whether
    a pair reads as a climax or as a pile-up, and a render that stood them somewhere the board
    does not would be answering about a hill nobody plays."""
    return LANES // 2 if bosses < 2 else (1 if index == 0 else LANES - 2)


def put(sheet, im, cx, cy, w, h):
    """Draws a sprite centred on (cx, cy), fitted into w x h without changing its aspect."""
    if im is None:
        return
    ratio = min(w / im.width, h / im.height)
    size = (max(1, int(im.width * ratio)), max(1, int(im.height * ratio)))
    sheet.alpha_composite(im.resize(size, Image.LANCZOS),
                          (int(cx - size[0] / 2), int(cy - size[1] / 2)))


def dyed(im, rgb, alpha=255):
    """`Image.color` on a white sprite: a multiply, so it can only ever darken.

    Written out rather than approximated, because that *is* the rule the game draws by and a
    mirror that lightened instead would show a charm's halo the game cannot produce (invariant
    37l is the whole of why the wards carry a baked hue rotation rather than a tint).
    """
    if im is None:
        return None

    out = im.copy()
    r, g, b, a = out.split()
    lut = lambda ch, k: ch.point(lambda v: v * k // 255)

    return Image.merge("RGBA", (lut(r, rgb[0]), lut(g, rgb[1]), lut(b, rgb[2]),
                                a.point(lambda v: v * alpha // 255)))


def stretch(sheet, im, cx, cy, w, h):
    """Draws a sprite stretched to w x h, which is what the view does to the wall."""
    sheet.alpha_composite(im.resize((max(1, int(w)), max(1, int(h))), Image.LANCZOS),
                          (int(cx - w / 2), int(cy - h / 2)))


def envelope(sheet, im, cx, cy, w, h):
    """`AspectRatioFitter.EnvelopeParent` inside a `RectMask2D` - scaled uniformly to cover the
    band, centred, and cut at its edges.

    <b>This mirror drew the hill stretched for as long as the game did, which is the whole
    argument for keeping the two in step.</b> `render_siege.py` exists to catch what no numeric
    gate can see, and a ground squashed sideways by 1.42x on the shape most players hold is
    exactly that - but the tool reproduced the squash faithfully, so every picture it drew agreed
    with the game and looked composed. A mirror vouches for a bug as readily as for a feature
    (44d); it can only ever say the two *agree*.
    """
    w, h = max(1, int(w)), max(1, int(h))
    scale = max(w / im.width, h / im.height)
    big = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))),
                    Image.LANCZOS)
    sheet.alpha_composite(big.crop(((big.width - w) // 2, (big.height - h) // 2,
                                   (big.width - w) // 2 + w, (big.height - h) // 2 + h)),
                          (int(cx - w / 2), int(cy - h / 2)))


def layout_of(level):
    """The level's layout, told which ladder it is on.

    An endless lane authors no waves and no boss, so a `Layout` that did not know it was one
    would report the level as unreadable - which is the check doing its job and the wrong
    question here (`SiegeLayout.Endless`)."""
    block = level["siege"]
    grid = proto.Grid(block["rows"], block["width"], block["height"], siege.CELLS)
    return siege.Layout(grid, block["gems"], block["wards"], block["waves"],
                        block.get("boss"), block.get("cogs", 0),
                        endless=bool(block.get("endless")),
                        charms=block.get("charms", ""))


def coming(lay, wave):
    """What is standing on the hill, as (colour, kind) pairs, on either ladder.

    On the ordinary ladder it is the wave the boss stands in, which is where the picture is worth
    taking. A warlord, a warbringer and an overlord get a wave of their own (`SiegeLayout` appends
    a one-raider one, 37t), so what is standing with them is the wave in front; a boss that cannot
    bring a ward down rides the last authored wave instead, and then its company *is* that wave -
    which is the whole of what a picture of `s01_stonewatch` has to show. On the Infinite lane
    there is nothing authored at all: the muster is a rule, so the picture reads it at a wave
    number exactly as the board does (`SiegeEndless.WaveAt`, invariant 43) - which is what lets
    one picture say whether wave forty is a hill anybody could hold."""
    if lay.endless:
        return siege.endless_wave(list(lay.deal), lay.seed, wave)

    if lay.boss_wave >= 0:
        # The boss itself is index nought and is drawn on its own, below.
        beside = lay.coming[lay.boss_wave][1:]
        if beside:
            return beside

    body = [w for i, w in enumerate(lay.waves) if i != lay.boss_wave]
    return siege.read_wave(body[-1] if body else "", lay.boss_kind, False)


def ground(rung):
    """The ground a rung is fought over, by its place in the chapter.

    `SiegeMode.Ground` and `SiegeView.GroundAddress` in two switches of ten literals; here it can
    be arithmetic, because a Python diagnostic is not what `artnames.py` reads."""
    return "hill%d" % (rung % GROUNDS + 1)


def beam_thicks():
    """`SiegeView.HazeThick` / `BodyThick` / `CoreThick`, read off the source that draws them.

    **A mirror that types its own numbers vouches for a picture the game does not produce**
    (invariant 44d), and thickness is the one question `--volley` exists to answer: whether a dozen
    layered beams standing at once read as a barrage or as a white smear. Typed here, this tool
    would go on answering it about whatever the numbers used to be.
    """
    src = (REPO / "Assets/Game/Scripts/Presentation/Board/Siege/SiegeView.Charms.cs") \
        .read_text(encoding="utf-8")

    said = re.search(r"HazeThick\s*=\s*([\d.]+)f,\s*BodyThick\s*=\s*([\d.]+)f,"
                     r"\s*CoreThick\s*=\s*([\d.]+)f", src)

    if not said:
        raise SystemExit("SiegeView.Charms: no HazeThick/BodyThick/CoreThick to mirror")

    return tuple(float(g) for g in said.groups())


def white_glow(side):
    """`Art.Glow` - the soft radial the view marks a muzzle with."""
    side = max(8, int(side))
    im = Image.new("RGBA", (side, side), (255, 255, 255, 0))
    px = im.load()
    mid = side / 2.0

    for y in range(side):
        for x in range(side):
            r = math.hypot(x - mid, y - mid) / mid
            if r >= 1.0:
                continue
            px[x, y] = (255, 255, 255, int((1.0 - r) ** 2.0 * 235.0))

    return im


def stood_lance(where, level):
    """`--lance`, as a cell index, or None.

    Bare means the middle of the field, which is the worst case for the picture: a cross drawn from
    the middle has an arm running to every edge, so nothing about the beam is hidden by the board
    ending early.
    """
    if where is None:
        return None

    if where >= 0:
        return where

    block = level.get("siege") or {}
    wide, tall = block.get("width") or 0, block.get("height") or 0
    return (tall // 2) * wide + wide // 2


def stood_charms(spec, level):
    """`--charms`, as {cell: kind}.

    **`auto` stands one of each along the middle row**, which is the arrangement that answers the
    question this is for: three marks side by side over three different jewels, at the size a phone
    draws them. A named list is for looking at one particular pairing - a lance on a yellow gem is
    the worst case, because white on amber is the least contrast this board can produce.
    """
    if not spec:
        return {}

    block = level.get("siege") or {}
    wide, tall = block.get("width") or 0, block.get("height") or 0

    # **The roster, read rather than spelled.** Three copies of it in this one function is three
    # places a seventh charm has to be remembered, and the first two of them fail by refusing a
    # name the game deals rather than by drawing the wrong thing - which is a mirror that cannot
    # be pointed at the board it is a mirror of.
    kinds = [kind for _, kind in siege.CHARM_ROSTER]

    if spec == "auto":
        row = (tall // 2) * wide
        return {row + 1 + i * 2: kinds[i % len(kinds)] for i in range(min(3, (wide - 1) // 2))}

    out = {}

    for part in spec.split(","):
        cell, _, kind = part.partition("=")
        if kind.strip() not in kinds:
            raise SystemExit("--charms: '%s' is not one of %s"
                             % (kind.strip(), ", ".join(kinds)))
        out[int(cell)] = kind.strip()

    return out


def board_fit(grid):
    """The room, the cell and the board's span on whatever canvas is set - `SiegeView.Fit`.

    **A function rather than four statements inside `draw`**, because the captions gate asks the
    same question of three canvases without drawing any of them, and a second copy of this
    arithmetic is a mirror that can disagree with its own pictures (invariant 44d).

    `SiegeView.CellFor` - the field is laid out to the width a *phone* would have given it,
    because the extra width on a squarer canvas was bought to buy height and is not the board's
    to spend (invariant 37cc). Without it a 4:3 tablet draws a 160-unit cell against a phone's
    124, which puts the field on its `MAX_GEM_BAND` ceiling and leaves the hill 3.2 cells.
    """
    left, bottom, right, top = inset()
    host = (CANVAS[0] - left - right, CANVAS[1] - top - bottom)

    scale = min(1.0, PHONE_WIDTH / CANVAS[0])

    cell = min((host[0] * scale - MARGIN * 2) / grid.w,
               (host[1] - MARGIN * 2) * MAX_GEM_BAND / grid.h)
    span = (max(cell * grid.w, host[0] - MARGIN * 2), max(cell * grid.h, host[1] - MARGIN * 2))

    return host, cell, span


def draw(level, raiders, bolts=True, aim=False, boss="cast", rung=0, wave=1, line=None,
         burn=None, storm=0, bombs=None, cogs=0, forecast=False, armed=(), charms=None,
         lance=None, volley=None, stilled=None, heaved=None):
    lay = layout_of(level)
    grid = lay.grid
    charms = charms or {}

    # **Every damage figure on this picture, painted last.** It mirrors the view's own layer
    # order rather than the order the code happens to run in (invariant 44d): `SiegeView.Number`
    # builds onto `_figures`, which is made after `_fx` and after `_sky`, so nothing drawn on this
    # board is ever over a number. A mirror that painted them where they were computed would
    # answer the wrong question about the one picture it exists to answer it about.
    figures = []

    left, bottom, right, top = inset()
    host, cell, span = board_fit(grid)

    gem_band = min(max(cell * grid.h / span[1], 0.28), MAX_GEM_BAND)
    rest = 1.0 - gem_band
    hill_band = rest * (HILL_BAND / (HILL_BAND + LINE_BAND))
    line_band = rest - hill_band

    floor = min(rest, LINE_FLOOR * cell / span[1])
    if line_band < floor:
        line_band = floor
        hill_band = rest - line_band

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
    # `SiegeView.PlateWide` - the hill and the rampart run to the plate's edge, not the
    # field's, which is what the margin either side of them used to be.
    envelope(sheet, sprite(ground(rung)), cx, cy, span[0] + MARGIN * 2, h + cell * 0.5)

    # ------------------------------------------------------------------ the raiders
    # A boss is drawn on its own below, at its own size and in its own place, so what walks the
    # hill here is everything else the wave sends.
    sent = coming(lay, wave)
    bosses = [(x, k) for x, k in sent if k in BOSSES]
    mob = []

    for i, (letter, kind) in enumerate([x for x in sent if x[1] not in BOSSES][:raiders]):
        colour = siege.LETTERS.index(letter)
        brute = kind == "brute"
        bulwark = kind == "bulwark"
        # **Its colour's lane, strayed by one** - `SiegeLanes.Walk`. The stray is dealt from the
        # raider's own index rather than from the board's stream, because this picture has no
        # stream: what it has to be right about is that the hill reads as sorted by colour with
        # mixing at the edges, which is the whole reason the lanes are coloured.
        lane = lane_walk(lay.wards.index(letter) if letter in lay.wards else 0,
                         len(lay.wards), i)
        march = 0.18 + 0.16 * i

        tall = cell * (1.85 if bulwark else 1.55 if brute else 1.15)
        lx = (lane - (LANES - 1) / 2) * (span[0] / (LANES + 0.6))
        ly = hill_top + (hill_foot - hill_top) * march

        cx, cy = at(lx, ly)
        mob.append((cx, cy, letter))
        # **Sized by its own height with the width following the picture**, which is
        # `SiegeView.Frame` and was not what this drew. A square box fits a *wide* reel by its
        # width and draws it short - and a bulwark is the widest thing on this hill, because it is
        # carrying a shield. Drawn square it came out two thirds of its height with the shield off
        # the side of the board, which is the mechanic invisible in the one picture that exists to
        # check it.
        body = reel(skin("bulwark" if bulwark else "brute" if brute else "mon", colour))
        wide = tall if body is None else tall * body.width / body.height
        shadow(sheet, cx, cy, wide, tall)
        put(sheet, body, cx, cy - BODY_LIFT * tall, wide, tall)

        # **The flame an ember turret leaves on it** (`SiegeView.Ablaze`). Over the body and under
        # the gem and the health bar below, which is the order the board draws them in and is not
        # a nicety: fire may cover a raider and may never cover the two readouts on it.
        #
        # Sized off the body's own drawn width, seated where `shadow` puts the shadow - the same
        # `FootOf` both halves of the game ask - and turned in the ward's colour rather than the
        # raider's, because what a burn says is which of the player's four seats is paying for it.
        if i < ALIGHT:
            fire = blast("burn_%s" % siege.LETTERS[i % len(siege.LETTERS)],
                         (i * 7 + 5) % 24)
            if fire is None:
                raise SystemExit("missing Art/Fx/Siege/burn_* - run: "
                                 "python Tools/make_burn_fx.py --write")

            fwide = wide * BLAZE_WIDE
            ftall = fwide * fire.height / fire.width
            foot = BODY_LIFT * tall - tall * body_fill(False) * SHADOW_DROP

            # y runs down an image and up a canvas, so the seat is *subtracted* here - the other
            # half of invariant 44d's finding, and the one a mirror gets wrong on its own.
            put(sheet, fire, cx, cy - foot - (BLAZE_SEAT - 0.5) * ftall, fwide, ftall)

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
                                  radius=6,
                                  fill=(178, 120, 255, 255) if brute or bulwark
                                  else (232, 97, 90, 255))

    # ------------------------------------------------------------------ the ward line
    band = span[1] * line_band
    cx, cy = at(0, hill_foot - band / 2)
    stretch(sheet, sprite("rampart"), cx, cy, span[0] + MARGIN * 2, band * 1.02)

    n = len(lay.wards)
    for i, ward in enumerate(lay.wards):
        wide = span[0] / (n + 0.6)
        wx = (i - (n - 1) / 2) * wide

        cx, cy = at(wx, line_y - cell * 0.88)
        put(sheet, sprite("socket"), cx, cy, cell * 1.7, cell * 0.8)

        tint = TINTS[siege.LETTERS.index(ward)]

        # No tint: a ward's colour is baked into its sprite (see make_siege_art.hued). Drawn one
        # rank apart across the line, so one picture shows the whole ladder - a still that drew
        # four rank-one plinths could not say whether the pips read as ranks.
        rank = i % 5

        cx, cy = at(wx, line_y + cell * 0.06)
        stood = line or LINE
        put(sheet, sprite(ward_art(stood[i % len(stood)], ward)), cx, cy, cell * 1.72, cell * 2.15)

        # The rank badge on its shoulder - `SiegeView.Badge`. Always drawn, and it says one before
        # a cog has been spent: the ladder is on the board from the first frame.
        cx, cy = at(wx - cell * 0.74, line_y + cell * 0.34)
        crest = ART / "crest.png"
        if crest.exists():
            # Tinted by rank, which is the whole of how a rank is said now: a plinth under the
            # turret was tried and is invisible, because a ward's foot is behind the field's plate
            # on every screen this mode is drawn at (invariant 37y). This picture is what said so.
            badge = Image.open(crest).convert("RGBA")
            wash = Image.new("RGBA", badge.size, RANK_TINTS[rank] + (255,))
            badge = ImageChops.multiply(badge, wash)
            put(sheet, badge, cx, cy, cell * 0.62, cell * 0.62)
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

    # The bosses, over the ward line so a ring reads on top of the turrets, and under the exchange
    # so a bolt crossing the hill still passes in front of one. An authored rung sends at most one;
    # the Infinite lane sends two from wave 21, and drawing both is the only way this picture can
    # say whether a pair reads as a climax or as a pile-up (invariant 43).
    if lay.boss:
        bosses = [(lay.boss, lay.boss_kind)]

    for slot, (letter, kind) in enumerate(bosses[:2]):
        warlord(sheet, draw_on, kind, letter, len(lay.wards), span, cell, hill_top, hill_foot,
                line_y, at, casting=boss, slot=slot,
                lane=boss_lane(slot, len(bosses)))

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
            # **The turret decides the shape and the ward decides the colour** - the reels are
            # named for the model a player stood here, not for the post and not for the colour,
            # and a bleached one is multiplied by that ward's own `Pal` entry (`WardModel.ShotFor`,
            # `SiegeView.ShotTint`). Nineteen bought turrets each throw something of their own, so
            # whether nineteen silhouettes actually read as nineteen is a question only this
            # picture answers.
            stood_here = (line or LINE)[i % len(line or LINE)]

            cx, cy = at(wx, line_y + cell * 1.0)
            tx, ty, _ = mob[i % len(mob)]

            # A different beat per ward, so one picture shows the whole event rather than four
            # copies of one instant.
            along = (0.30, 0.55, 0.78, 0.42)[i % 4]

            # **A twin-barrelled turret throws one bolt per barrel** (`SiegeView.Barrels`), which
            # is drawing and not damage: the impact is drawn once, because the board fired once.
            # Whether two converging comets read as one shot or as two is a question only this
            # picture answers.
            count = barrels(stood_here)
            reach = cell * BODY_WIDE * BARREL_GAP if count > 1 else 0.0

            for b in range(count):
                step = (b * 2 - (count - 1)) * reach if count > 1 else 0.0
                mx, my = cx + step, cy

                # `SiegeView.ApartOnArrival` - each bolt lands *beside* the raider rather than on
                # it, so the pair stays parallel instead of converging into one comet.
                lx, ly = tx + step * APART_ON_ARRIVAL, ty

                dx, dy = lx - mx, ly - my
                far = math.hypot(dx, dy) or 1.0
                ux, uy = dx / far, dy / far

                # **The trail unrolls out of the barrel** (`SiegeView.Emerged`). `along` is how
                # far down its flight this bolt is caught, so the tail it has grown is the
                # distance it has covered - which is why the four in this picture, caught at four
                # different beats, wear four different lengths of trail.
                # **Named apart from `reel`**, which is a module-level function this same routine
                # already calls - a local of that name makes Python treat every earlier call in
                # the function as a read of an unassigned local, which is how this first ran.
                comet = blast(shot_key("shot", stood_here, ward), 6)
                fat = bolt_scale(stood_here)
                boltw = cell * fat
                bolth = boltw * (comet.height / comet.width) if comet else 0.0

                aimed(sheet, comet, mx + dx * along, my + dy * along,
                      boltw, ux, uy, HEAD_AT, emerged(far * along, cell, bolth))

                # **After the bolt**, which is `SiegeView.Bolt`'s order: the crop leaves a straight
                # edge at the barrel and this is what covers it.
                aimed(sheet, loudest(shot_key("muzzle", stood_here, ward)), mx, my,
                      cell * 2.7 * (0.70 if count > 1 else 1.0), ux, uy, MUZZLE_AT)

            dx, dy = tx - cx, ty - cy
            far = math.hypot(dx, dy) or 1.0
            ux, uy = dx / far, dy / far
            aimed(sheet, loudest(shot_key("hit", stood_here, ward)), tx, ty,
                  cell * 3.2 * bolt_scale(stood_here), ux, uy)

            # The tally floating off it. Two of the four are drawn as doubles, because the
            # elemental double is the rule this mode is about and it has to read as a different
            # kind of number rather than as a bigger one.
            #
            # **Collected rather than painted here**, so that every figure lands above every
            # effect - `SiegeView._figures`, the layer built last for exactly this reason. Painted
            # in place, a number would be covered by whatever is drawn after it, which is the
            # fault this mirrors: on a stormglass that is a dozen beams and a dozen scorches
            # arriving over the top of it inside half a second.
            figures.append((tx, ty - cell * 0.7, (14, 6, 22, 8)[i % 4], i % 2 == 0))

    # ------------------------------------------------------------------ the hourglass stopping
    # `SiegeView.Stilled` - the wall of stopped time sweeping the hill and the dial it hangs over
    # it. **The one payoff in this mode that no picture could look at**: the charm's whole value is
    # a moment, and a moment is exactly what a still frame is for.
    if stilled is not None:
        t = max(0.0, min(1.0, stilled))

        frm = line_y + cell * 0.6
        to = hill_top

        # **Named apart from anything this routine already uses.** A local shadows for the whole
        # function in Python, so a name reused here breaks a read further up - which is how both
        # of the last two additions to this file first ran.
        # **The wake first and the face over it**, in the order `SiegeView.Stilled` builds them:
        # the deeper, dimmer second wall trails the first by a sixth of the sweep, so at `t` it is
        # still down the hill. Drawing one wall where the game draws two is a mirror reporting a
        # thinner payoff than the one that ships (44d).
        sweep_wall(sheet, at, "stillwave", frm, to,
                   max(0.0, (t - 0.16) / 1.18), STILL_FRONT * 1.7, 0.52, span, cell)
        sweep_wall(sheet, at, "stillwave", frm, to, t, STILL_FRONT, 1.0, span, cell)

        stillface = reel("stilldial", int(t * max(0, frames_in("stilldial") - 1)))
        if stillface is not None:
            dialsize = int(cell * DIAL_WIDE)
            dialx, dialy = at(0.0, (hill_top + hill_foot) * 0.5)
            dialdisc = faded(stillface.resize((dialsize, dialsize), Image.LANCZOS), DIAL_INK)
            sheet.alpha_composite(dialdisc, (int(dialx - dialsize / 2),
                                             int(dialy - dialsize / 2)))

    # ------------------------------------------------------------------ the anvil throwing
    # `SiegeView.Heaved` - the wall of driven dust rolling from the line to the crest. **The same
    # argument the hourglass's own flag makes**: the charm's whole payoff is a quarter of a second
    # of model time, and a still frame is exactly what that is for.
    #
    # **And the one thing to look at is whether it reads as the *other* front.** A still wave and
    # this are the only two things in the mode that sweep the hill and they arrive one chapter
    # apart, so draw them side by side before believing either (`--stilled` against `--heaved`).
    if heaved is not None:
        u = max(0.0, min(1.0, heaved))

        heavefrom = line_y + cell * 0.5

        # The dust behind the front, then the front, then the clock over both - `SiegeView.Heaved`
        # in the order it builds them.
        sweep_wall(sheet, at, "heavefront", heavefrom, hill_top,
                   max(0.0, (u - 0.18) / 1.22), HEAVE_FRONT * 1.9, 0.50, span, cell)
        sweep_wall(sheet, at, "heavefront", heavefrom, hill_top, u, HEAVE_FRONT, 1.0, span, cell)

        # **The reversed clock, which is the whole reason this flag is worth drawing at a middling
        # `u`.** The one thing a still frame can be asked about a hand that runs backwards is
        # whether a player can tell which way it is going *from one frame* - so look at the smear
        # and the arrow, because at 24fps nothing else answers it.
        heaveface = reel("heavedial", int(u * max(0, frames_in("heavedial") - 1)))
        if heaveface is not None:
            hdsize = int(cell * DIAL_WIDE)
            hdx, hdy = at(0.0, (hill_top + hill_foot) * 0.5)
            hddisc = faded(heaveface.resize((hdsize, hdsize), Image.LANCZOS), DIAL_INK)
            sheet.alpha_composite(hddisc, (int(hdx - hdsize / 2), int(hdy - hdsize / 2)))

    # **Both of the hill's captions, together, because apart they say nothing.** Each was
    # individually well placed and the pair overlapped on every shape; drawing only the chain is
    # what let that ship. A cascade while a wave walks on is ordinary, so this is the real worst
    # case rather than a contrived one.
    # **And never with the forecast**, which is the rule `SiegeView.Chain` and `Foretell` make
    # between them: the hill holds one wide caption at a time and whichever is already standing
    # keeps it. Drawing all three at once would be a mirror showing a screen the game cannot
    # produce, which is worse than drawing none of them (44d).
    if bolts and not forecast:
        chain_y, wave_y = captions(line_y, cell)

        bx, by = at(0, chain_y)
        banner(sheet, bx, by, 3, cell, span[0])

        boss_here = [k for _, k in coming(lay, wave) if k in BOSSES]
        wx, wy = at(0, wave_y)

        if boss_here:
            wave_banner(sheet, wx, wy, boss_banner(boss_here[0]), cell, span,
                        (255, 255, 255, 255), BOSS_SIZE, swell=True)
        elif wave > 1:
            # **Nothing on wave one, because the screen says nothing there either**: the count-in
            # has just said GO! on the frame the first raider stepped out (`SiegeView.Arrival`).
            # A mirror still drawing a caption the board has stopped drawing is worse than one
            # drawing none (invariant 44d) - `--wave 2` is the picture of an ordinary arrival.
            wave_banner(sheet, wx, wy,
                        loc("mode.siege.wave", wave, max(wave, len(lay.waves))), cell, span)

    # ------------------------------------------------------------------ the field
    cx, cy = at(0, gem_centre)
    # `SiegeView.Sockets` - the plate runs to the edge of the board like the ground and the
    # rampart do. On a phone the two are the same number to within six units; on a tablet the
    # field is narrower than the board (invariant 37cc) and a plate cut to the cells would leave
    # a strip of bare board down each side of it.
    stretch(sheet, sprite("plate"), cx, cy,
            max(cell * grid.w + cell * 0.34, span[0] + MARGIN * 2), cell * grid.h + cell * 0.34)

    for i, c in enumerate(grid.cells):
        gx = (i % grid.w - (grid.w - 1) / 2) * cell
        gy = gem_centre + ((grid.h - 1) / 2 - i // grid.w) * cell
        cx, cy = at(gx, gy)

        draw_on.rounded_rectangle([cx - cell * 0.46, cy - cell * 0.46,
                                   cx + cell * 0.46, cy + cell * 0.46],
                                  radius=16, fill=(255, 255, 255, 12))

        # **The charms, stood where `--charms` asks for them** (`SiegeCharm`). They are dealt at a
        # rate rather than authored, so no shipped field carries one and this is the only way to
        # look at one at all - which is the whole job of this tool: whether a charmed *stone* is
        # told apart from the four beside it at forty pixels, and whether it still reads as its own
        # colour, are two questions no numeric gate can be asked (invariant 32b).
        charm = charms.get(i)

        if charm:
            put(sheet, dyed(sprite("charm_ring"), TINTS[siege.LETTERS.index(c)], 216),
                cx, cy, cell * CHARM_RING, cell * CHARM_RING)

        # A cog is drawn a shade smaller than a jewel, so the socket shows around it - it is the
        # one thing on this field that is not a gem, and the gap is the cheapest way of saying so
        # that survives being forty pixels wide (`SiegeView.Mint`).
        side = cell * (GEM_INSET * 0.88 if c == "*" else GEM_INSET)

        # **Every charm is a face** (`SiegeView.CharmFace`): a prism is the one gem here that is
        # not a colour, and the other two are their own stone cut in the colour they are worth. So
        # a charm never stands *on* a jewel - it replaces it, which is what a player separates at a
        # glance on a board that also has a hill walking down it.
        if charm == siege.PRISM:
            art = "gem_prism"
        elif charm:
            art = "%s_%s" % (CHARM_ART[charm], c)
            side *= CHARM_INSET
        else:
            art = GEM_ART[c]

        put(sheet, sprite(art), cx, cy, side, side)

    # ------------------------------------------------------------------ a lance going off
    # **`--lance`, and it is the one thing in this mode a still picture can still be asked.** A
    # charm's whole complaint was the drawing (invariant 37cn), and a drawing is what no numeric
    # gate opens: whether the beam reads as light rather than as a highlighter line, whether it is
    # thick enough to cover the gems it is taking without hiding the row either side, and whether a
    # burst on every cell of the cross reads as a row failing or as noise.
    #
    # **The peak frame rather than the sequence.** The game draws this over about a second - charge,
    # beams, then the cross failing outward from the stone - and a still can only stand at one
    # moment of that. The moment worth looking at is the one where the most is on the screen, which
    # is the beams at full and the wavefront about two thirds of the way out: the bursts near the
    # stone are already fading and the far ones have not started, which is exactly the reading the
    # `Ruin` stagger is for.
    if lance is not None and 0 <= lance < len(grid.cells):
        lx, ly = lance % grid.w, lance // grid.w
        hue = TINTS[siege.LETTERS.index(grid.cells[lance])]             if grid.cells[lance] in siege.LETTERS else (255, 243, 220)

        def cell_at(gx_i, gy_i):
            return at((gx_i - (grid.w - 1) / 2) * cell,
                      gem_centre + ((grid.h - 1) / 2 - gy_i) * cell)

        sx, sy = cell_at(lx, ly)

        # The two strokes, body then core - a tint is a multiply, so the white filament the art
        # tool drew only survives as a second, thinner draw over the top (`SiegeView.Stroke`).
        beam_body = reel("beam", 4)

        for wide, tall in ((cell * grid.w, cell * 0.66), (cell * 0.66, cell * grid.h)):
            across = wide > tall
            for colour, thin in ((hue, 1.0), ((255, 255, 255), 0.34)):
                im = dyed(beam_body, colour, 236 if thin == 1.0 else 210)
                if im is None:
                    continue
                if not across:
                    im = im.transpose(Image.ROTATE_90)
                    stretch(sheet, im, sx, sy, wide * thin, tall)
                else:
                    stretch(sheet, im, sx, sy, wide, tall * thin)

        # The cross failing, outward. Two thirds of the way out, so the near cells are past their
        # peak and the far ones have not lit - which is what says the light travelled.
        front = int(max(grid.w, grid.h) * 0.66)
        burst = "hit_%s" % (grid.cells[lance] if grid.cells[lance] in siege.LETTERS else "r")

        for gx_i, gy_i in ([(x, ly) for x in range(grid.w)]
                           + [(lx, y) for y in range(grid.h) if y != ly]):
            far = max(abs(gx_i - lx), abs(gy_i - ly))
            if far > front:
                continue

            bx, by = cell_at(gx_i, gy_i)
            put(sheet, blast(burst, min(11, 2 + (front - far) * 3)),
                bx, by, cell * 1.7, cell * 1.7)

        # And the stone's own detonation, which is the reel baked for this and nothing else.
        put(sheet, blast("charm_blast_%s" % (grid.cells[lance]
                                             if grid.cells[lance] in siege.LETTERS else "r"), 7),
            sx, sy, cell * 4.2, cell * 4.2)

    # ------------------------------------------------------------------ a stormglass firing
    # **`--volley`, and it is the only way to look at the one moment this mode stops for.** The
    # board and the hill are both frozen while a stormglass fires (invariant 37cq), so what is on
    # the screen is every standing ward's beam at once - which is a question about *density* and
    # *thickness* that no numeric gate can be asked and that one beam on its own cannot answer
    # either. Drawn at the peak: every beam open, before any of them has begun to close.
    if volley is not None and 0 <= volley < len(grid.cells):
        vx, vy = volley % grid.w, volley // grid.w
        sx, sy = at((vx - (grid.w - 1) / 2) * cell,
                    gem_centre + ((grid.h - 1) / 2 - vy) * cell)

        bolt = reel("laser", 3)

        # **Read off `SiegeView`'s own constants rather than typed here**, which is invariant 44d's
        # rule about a mirror: two numbers for one thickness is a tool vouching for a beam the game
        # does not draw, and thickness is the one question this exists to answer.
        thicks = beam_thicks()
        layers = ((thicks[0], 107, True), (thicks[1], 242, True), (thicks[2], 242, False))

        for n, (tx, ty, wears) in enumerate(mob):
            letter = lay.wards[n % len(lay.wards)] if lay.wards else "r"
            hue = TINTS[siege.LETTERS.index(letter)]

            dx, dy = tx - sx, ty - sy
            far = math.hypot(dx, dy) or 1.0
            lean = math.degrees(math.atan2(dy, dx))

            # Run a little past the raider, so a beam ends *in* what it hit rather than at its
            # feet - `SiegeView.Beam`.
            length = far + cell * 0.55
            mx = sx + dx / far * (length / 2.0)
            my = sy + dy / far * (length / 2.0)

            for thick, alpha, hued_layer in layers:
                if bolt is None:
                    break

                im = bolt.resize((max(1, int(length)), max(1, int(cell * thick))), Image.LANCZOS)
                if hued_layer:
                    im = dyed(im, hue, alpha)
                im = im.rotate(-lean, expand=True, resample=Image.BICUBIC)
                sheet.alpha_composite(im, (int(mx - im.width / 2), int(my - im.height / 2)))

            # Where it lands - `SiegeView.Scorch`, the charm's own burst rather than a bolt's.
            put(sheet, loudest("hit_%s" % letter), tx, ty, cell * 1.9, cell * 1.9)

            # **What it took, which is the whole point of stopping the board for it.** A
            # stormglass is the biggest number in the mode and it is the one moment the whole
            # game stops for, so a figure that cannot be read there is the payoff saying nothing.
            # `SiegeView.Number` tallies every ward's bolt into one climbing figure per raider,
            # so what stands over a raider is the line's whole answer to it rather than a figure
            # per beam.
            #
            # **The arithmetic is `SiegeBoard.Volley`'s, at the starter line's own weight** - four
            # standing wards, rank nought, one star: the ward wearing the charm's colour throws
            # `CharmVolleyOwn` and every other throws `CharmVolley`, at 40 on a raider of that
            # ward's colour and 20 off it. That is the worst case for legibility, because a bought
            # and cogged line draws bigger numbers. Gold on every one of them, and that is not a
            # mistake in the mirror: a tally goes gold if *any* hit in it was a double
            # (`Number`'s `running.Weak |= weak`), and in a volley every raider is answered by its
            # own ward.
            charm = grid.cells[volley] if grid.cells[volley] in siege.LETTERS else "r"
            figures.append((tx, ty - cell * 0.7, 140 if wears == charm else 120, True))

        # And the stone opening fire: a white core over a wide bloom, because what leaves here is
        # every ward at once and no one colour may own it.
        put(sheet, white_glow(cell * 7.2), sx, sy, cell * 7.2, cell * 7.2)

    # ------------------------------------------------------------------ the bombs
    # **What a bomber leaves, standing on the hill.** Whether a bomb reads as a thing to *tap* on
    # a board where every other input is a drag, and whether it can be picked out of a hill full
    # of walking monsters at all, are the two questions only a picture answers.
    for lane, row in (bombs or ()):
        bx = (lane - (LANES - 1) / 2) * (span[0] / (LANES + 0.6))
        by = hill_top + (hill_foot - hill_top) * ((row + 0.5) / BLAST_ROWS)
        cx, cy = at(bx, by)

        glow = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        ImageDraw.Draw(glow).ellipse([cx - cell, cy - cell, cx + cell, cy + cell],
                                     fill=(255, 190, 90, 60))
        sheet.alpha_composite(glow)

        # The ring, which is the whole of how a bomb is found (`SiegeView.Dropped`). Gold and
        # steady, where a cog's is the ward's colour and shrinks: a bomb names no ward and runs no
        # clock, so the only thing its ring says is *here, and tap it*.
        ring = cell * 0.6
        draw_on.ellipse([cx - ring, cy - ring, cx + ring, cy + ring],
                        outline=BOMB_RING + (216,), width=max(2, int(cell * 0.05)))

        put(sheet, utility(BOMB_ART), cx, cy, cell * 0.92, cell * 0.92)

    # ------------------------------------------------------------------ the cogs
    # **What a kill pays, lying where it fell** (`SiegeView.Dropped`). A cog is the second thing on
    # this hill a finger does anything to, so the question a picture answers is whether it can be
    # picked out of a hill full of walking monsters - and whether the ring counting it down reads
    # as a clock rather than as one more threat.
    for i in range(cogs):
        lane, row = (i * 2 + 1) % LANES, i % BLAST_ROWS
        bx, by = box_at(span, hill_top, hill_foot, lane, row)
        cx, cy = at(bx + cell * COG_NUDGE, by + cell * COG_NUDGE)

        # The colour of the ward it will rank, which on a three-ward line is one of three.
        tint = TINTS[siege.LETTERS.index(lay.wards[i % len(lay.wards)])]

        glow = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        ImageDraw.Draw(glow).ellipse([cx - cell * 0.9, cy - cell * 0.9,
                                      cx + cell * 0.9, cy + cell * 0.9],
                                     fill=tint + (48,))
        sheet.alpha_composite(glow)

        # The ring is the clock, in the ward's own colour rather than a warning red: what it counts
        # down is a prize, and an alarm colour on a thing the player wants would read as a threat.
        ring = cell * (0.35 + 0.225 * (1.0 - i / max(1, cogs)))
        draw_on.ellipse([cx - ring, cy - ring, cx + ring, cy + ring],
                        outline=tint + (216,), width=max(2, int(cell * 0.05)))

        put(sheet, sprite("gem_cog"), cx, cy, cell * 0.86, cell * 0.86)

    # ------------------------------------------------------------------ the damage figures
    # **Last of everything on the hill**, which is the whole of what `SiegeView._figures` is: a
    # number is a readout rather than an effect, so nothing drawn on this board may cover one.
    for fx, fy, total, weak in figures:
        damage(sheet, fx, fy, total, weak, cell)

    # ------------------------------------------------------------------ the fuel tubes
    # **Drawn after the field, because the view draws them after the field** (`SiegeView.Compose`
    # gives them a layer of their own above it). They sit on the plate's own top edge, in the strip
    # under the plinths - and every one of them would be hidden behind the plate if it were carried
    # by its turret, which is exactly what used to happen (invariant 37g).
    plate_top = gem_centre + (cell * grid.h + cell * 0.34) / 2
    tube_y = plate_top + cell * 0.22

    # What each colour on the hill is worth killing, as a share of the busiest - `SiegeBoard.
    # DemandOf` over `SiegeBoard.Busiest`, counted in health because a brute is two matches and a
    # creeper is one.
    weight = {"creeper": siege.CREEPER_HEALTH, "brute": siege.BRUTE_HEALTH,
              "bulwark": siege.BULWARK_HEALTH, "bomber": siege.BOMBER_HEALTH}

    owed = {}
    for letter, kind in [x for x in sent if x[1] not in BOSSES][:raiders]:
        owed[letter] = owed.get(letter, 0) + weight.get(kind, siege.CREEPER_HEALTH)

    most = max(owed.values()) if owed else 0
    want = {k: v / most for k, v in owed.items()} if most else {}

    for i, ward in enumerate(lay.wards):
        wide = span[0] / (n + 0.6)
        wx = (i - (n - 1) / 2) * wide
        tint = TINTS[siege.LETTERS.index(ward)]

        cx, cy = at(wx, tube_y)

        # **The demand light, behind the tube** - `SiegeView.Wanted`. How much of what is standing
        # on the hill this turret is the answer to, squared so the busiest is unmistakably the
        # busiest rather than merely a little brighter. It is the one readout that turns "which
        # colour is coming" from a parse into a glance, and nothing but a picture can say whether
        # it does.
        share = want.get(ward, 0.0)
        if share > 0.01:
            lit = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
            grow = 0.9 + share * 0.22
            ImageDraw.Draw(lit).ellipse(
                [cx - cell * 0.71 * grow, cy - cell * 0.26 * grow,
                 cx + cell * 0.71 * grow, cy + cell * 0.26 * grow],
                fill=tint + (int(share * share * 224),))
            sheet.alpha_composite(lit)

        draw_on.rounded_rectangle([cx - cell * 0.53, cy - cell * 0.115,
                                   cx + cell * 0.53, cy + cell * 0.115],
                                  radius=9, fill=(0, 0, 0, 178))
        draw_on.rounded_rectangle([cx - cell * 0.53 + 2, cy - cell * 0.115 + 2,
                                   cx - cell * 0.53 + 2 + cell * 1.02 * 0.55,
                                   cy + cell * 0.115 - 2],
                                  radius=9, fill=tint + (255,))

    if forecast:
        foretell(sheet, lay, wave + 1, span, cell, hill_top, hill_foot, at)

    # **The overcharge glyph, on the turret's own chassis** (`SiegeView.Ready`). Drawn on every
    # ward the caller says is armed, because where it sits is the one thing about it a number
    # cannot answer - it was on the fuel bar until a device circled the chassis instead.
    for i in armed:
        if i < 0 or i >= n:
            continue

        wx = (i - (n - 1) / 2) * (span[0] / (n + 0.6))
        cx, cy = at(wx, line_y)

        tint = TINTS[siege.LETTERS.index(lay.wards[i])]

        glow = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
        ImageDraw.Draw(glow).ellipse([cx - cell * 0.6, cy - cell * 0.6,
                                      cx + cell * 0.6, cy + cell * 0.6],
                                     fill=tint + (128,))
        sheet.alpha_composite(glow)

        put(sheet, sprite("charge"), cx, cy, cell * 0.62, cell * 0.62)

    if aim == "hill":
        hill_grid(sheet, span, cell, hill_top, hill_foot, at, burn)

    # ------------------------------------------------------------------ the stormcall
    # Bolts falling on the raiders the item killed, drawn at three points of one reel so a still
    # picture shows the strike arriving, at its loudest, and going out.
    if storm and mob:
        for i, (mx, my, _) in enumerate(mob[:storm]):
            struck(sheet, mx, my, cell, (2, 6, 11, 8)[i % 4], at(0, hill_top)[1])
    elif aim == "wards":
        ward_rings(sheet, span, cell, line_y, len(lay.wards), at)

    return sheet


#: How many grounds this mode ships, and therefore where the ladder starts again.
#: `SiegeMode.Grounds`.
GROUNDS = 10


def levels():
    """Every siege rung this project ships, ordinary ladder first.

    **Both chapters rather than one**, because the endless lane is drawn on the same board with
    the same wards and the same cast - and a picture that could not show it would be a picture of
    half the mode (invariant 43). It authors no waves at all, so what stands on its hill is the
    ramp read at `--wave` (`siege.endless_wave`, wave one by default) - which is the only way to
    look at a wave nobody has typed anywhere: wave 21 sends **two bosses**, and whether that reads
    as the ramp's climax or as a pile-up is a question no number can answer.
    """
    made = []

    for name in CHAPTER_CASTS:
        path = CHAPTERS / (name + ".json")
        if not path.exists():
            continue

        for lv in json.loads(path.read_text(encoding="utf-8"))["levels"]:
            made.append((name, lv))

    return made


#: Which cast each shipped chapter draws, mirroring `SiegeMode.CastFor`.
#:
#: **Written out rather than derived from an ordinal**, because this tool has no catalog: it reads
#: chapter bodies off disk and a body does not carry its own place in the ladder. What that costs is
#: a row per chapter; what it buys is that the picture cannot quietly draw a different cast from the
#: one the game loads - which is the fault this whole file exists to catch (invariant 44d: a render
#: that draws differently from the screen sends you off to fix something that was never broken).
CHAPTER_CASTS = {
    "s01_thornwatch": "",
    "s03_broodmarch": "brood",
    "s04_barrowfell": "bone",
    "s05_ashenhold": "rabble",
    "s06_thundercrag": "wild",
    "s07_dustcrown": "court",
    "s02_endlesswatch": "medley",
}


def load(path):
    """One PNG, or None. Unlike `sprite` this takes a path, because the bar's art is not the
    mode's - it is shared UI, which is exactly why it lives in the global group."""
    return Image.open(path).convert("RGBA") if path.exists() else None


def cooling_pane(cell, left):
    """`UtilityBar.Sweep` - the wedge over a cell that is not ready yet.

    <p>The cell's own sprite tinted dark and cut by angle, so the wedge is the shape of the well
    it covers rather than a square laid over a rounded one. From the top and clockwise, with
    `left` being what is *still to wait*, so it shrinks away.</p>

    <p>The pieslice is drawn on a circle twice the cell wide, because Unity's `Radial360` fills
    the whole rectangle by angle and an inscribed circle would leave the four corners uncovered -
    which reads as a broken sweep rather than as a nearly-finished one.</p>
    """
    if cell is None or left <= 0:
        return None

    pane = cell.resize((SLOT, SLOT), Image.LANCZOS)

    # `Pal.Glass` at a third. Pale rather than dark, because the well is already the darkest
    # thing on the shelf and ink over ink says nothing (invariant 39d, and 37m's rule that a
    # state which has happened reads as brighter).
    dark = Image.new("RGBA", (SLOT, SLOT), (220, 235, 245, 84))
    dark.putalpha(ImageChops.multiply(dark.split()[3], pane.split()[3]))

    mask = Image.new("L", (SLOT, SLOT), 0)
    ImageDraw.Draw(mask).pieslice(
        [-SLOT / 2, -SLOT / 2, SLOT * 1.5, SLOT * 1.5],
        -90, -90 + 360 * min(1.0, left), fill=255)

    dark.putalpha(ImageChops.multiply(dark.split()[3], mask))
    return dark


#: `ModeScreen`'s header: the bar's height, and the readout row's middle and height.
#:
#: **Drawn here because the row moved into the bar's own band.** It used to sit below it, so the
#: only thing its spacing could collide with was itself and there was nothing for a picture to
#: judge; it is level with the two corner keys now, which bought the board 64 units of height and
#: made "does a number land on a button" a question — and one that is invisible in every gate,
#: because a number drawn over a button is perfectly legible.
#:
#: Kept in step with `ModeScreen` and `ReadoutRow` by hand, exactly as the band numbers are.
BAR_H, READOUTS_Y, ROW_H = 210, 186, 88
VALUE_Y, CAPTION_Y, VALUE_PT, CAPTION_PT = 14, -29, 56, 22
SLOT_W, TRIPLE_STEP, KEY_REACH, KEY_SIZE = 220, 250, 161, 118

#: `RunScreen`'s level tag, beside the way back: the air after the key, its box, and its type.
#:
#: **It is drawn here for the same reason the readouts are.** The tag has no horizontal room at
#: all - a row of two reaches within forty units of where it ends and a row of three runs through
#: it - so what keeps them apart is that the tag is short and hangs from the key's own centre,
#: and whether that reads is a picture's question rather than a number's. `RunHeaderTests` holds
#: the arithmetic; this says whether a run's number looks like it belongs in the corner.
TAG_GAP, TAG_W, TAG_H, TAG_PT, KEY_Y = 14, 250, 46, 34, -4


def header(sheet, readouts, level=None):
    """`ModeScreen.BuildHeader` and `BuildReadouts`, at the size a phone draws them.

    <p>Only what can collide: the shade, the two keys where they really sit, and the row of
    numbers where it really sits. It draws no icons and no captions worth reading — what this is
    for is whether a value lands on a key, and whether the row clears a camera cutout.</p>
    """
    draw_on = ImageDraw.Draw(sheet, "RGBA")

    # The shade, opaque along the top edge and gone by `ShadeDrop` below the bar.
    drop = READOUTS_Y + ROW_H / 2 + 40 - BAR_H
    for y in range(int(BAR_H + drop)):
        k = 1.0 - y / (BAR_H + drop)
        draw_on.line([(0, y), (CANVAS[0], y)], fill=(5, 10, 20, int(150 * k)))

    # The two corner keys, 118 square with their centres 102 in from each edge.
    for cx in (102, CANVAS[0] - 102):
        cy = BAR_H / 2 - KEY_Y
        draw_on.rounded_rectangle([cx - KEY_SIZE / 2, cy - KEY_SIZE / 2,
                                   cx + KEY_SIZE / 2, cy + KEY_SIZE / 2],
                                  radius=26, fill=(38, 58, 96, 255), outline=(96, 130, 190, 255),
                                  width=4)

    # And the band a key reaches into, so the picture says where the wall is rather than leaving
    # it to be eyeballed.
    for x in (KEY_REACH, CANVAS[0] - KEY_REACH):
        draw_on.line([(x, 0), (x, BAR_H + drop)], fill=(240, 90, 90, 90), width=2)

    # The level's number, beside the way back and level with it. Its box is drawn as well as
    # its words, because what is being judged is whether it clears the row under it.
    if level is not None:
        cy = BAR_H / 2 - KEY_Y
        left = KEY_REACH + TAG_GAP
        draw_on.rectangle([left, cy - TAG_H / 2, left + TAG_W, cy + TAG_H / 2],
                          outline=(120, 200, 255, 70))
        font = face(TAG_PT)
        write(sheet, draw_on, level, font,
              left + draw_on.textlength(level, font=font) / 2, cy, (242, 236, 220, 209))

    n = len(readouts)
    for i, (value, caption) in enumerate(readouts):
        x = CANVAS[0] / 2 + (0 if n <= 1 else
                             (-170 if i == 0 else 170) if n == 2 else
                             (i - 1) * TRIPLE_STEP)

        # The slot's own box, which is what `ReadoutRow.ClearsTheKeys` is about.
        draw_on.rectangle([x - SLOT_W / 2, READOUTS_Y - ROW_H / 2,
                           x + SLOT_W / 2, READOUTS_Y + ROW_H / 2],
                          outline=(120, 200, 255, 70))

        write(sheet, draw_on, value, face(VALUE_PT), x, READOUTS_Y - VALUE_Y, (242, 236, 220, 255))
        write(sheet, draw_on, caption, face(CAPTION_PT), x, READOUTS_Y - CAPTION_Y,
              (235, 245, 255, 158))


def write(sheet, draw_on, text, font, cx, cy, fill):
    """One centred, outlined line - `UIKit.Titled`'s own look, near enough to judge a position."""
    if not text:
        return

    box = draw_on.textbbox((0, 0), text, font=font)
    at = (cx - (box[2] - box[0]) / 2 - box[0], cy - (box[3] - box[1]) / 2 - box[1])

    for dx, dy in ((-2, 0), (2, 0), (0, -2), (0, 2)):
        draw_on.text((at[0] + dx, at[1] + dy), text, font=font, fill=(8, 12, 20, 220))

    draw_on.text(at, text, font=font, fill=fill)


def bar(sheet, held, cooling=None):
    """The action bar, where `SiegeScreen` hangs it: filling the foot of the safe area.

    <p>Drawn here rather than left to the imagination because it is not decoration - it is 228
    points of the screen, and it decides how much is left for the three bands above it
    (invariant 37g). What this picture is for is seeing whether the hill, the ward line and the
    field still read with a shelf under them, and whether the shelf reads as one.</p>

    <p><b>And with `--phone`, whether there is anything under it.</b> The shelf's plate runs to the
    bottom of the display and its cells stand `UtilityBar.Foot` above it, so the home indicator's
    strip is shelf rather than backdrop. That was reported from a device and could not be drawn
    here at all until this file learned what a phone is shaped like.</p>
    """
    draw_on = ImageDraw.Draw(sheet)

    # The plate runs to the bottom of the display; the cells stand `UtilityBar.Foot` above it.
    top = CANVAS[1] - bar_height()

    tray = load(UTILITY_ART / "tray.png")
    if tray is not None:
        sheet.alpha_composite(
            tray.resize((CANVAS[0], int(round(bar_height()))), Image.LANCZOS), (0, int(top)))

    cell = load(UTILITY_ART / "slot.png")

    for i in range(SLOTS):
        cx = CANVAS[0] * (2 * i + 1) / (2 * SLOTS)
        cy = CANVAS[1] - bar_foot() - BAR_SHELF / 2

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

        # Seconds still to wait on this one, or nought.
        waiting = (cooling or {}).get(name, 0)
        full = COOLDOWNS.get(name, 0)

        # An empty cell that can be *bought* from is drawn as an invitation: a smaller picture,
        # lifted, with "TAP TO BUY" in the room under it. Never while it is cooling, because the
        # seconds are drawn across the same middle at font 56 (`UtilityBar.Paint`).
        inviting = n <= 0 and waiting <= 0

        if icon is not None:
            if n <= 0:
                faded = icon.copy()
                faded.putalpha(faded.split()[3].point(lambda v: int(v * 0.36)))
                icon = faded

            size = EMPTY_ICON if inviting else ICON
            put(sheet, icon, cx, cy - (EMPTY_LIFT if inviting else 0), size, size)

        if inviting:
            font = face(26)
            if font is not None:
                draw_on.text((cx, cy - HINT_Y), "TAP TO BUY", font=font,
                             fill=(255, 206, 92, 255), anchor="mm",
                             stroke_width=3, stroke_fill=(23, 36, 51, 242))

        # The sweep, over the picture and under the badge, which is the order the hierarchy
        # draws them in - how many you hold is true whether or not it is ready.
        if waiting > 0 and full > 0:
            wedge = cooling_pane(cell, waiting / float(full))
            if wedge is not None:
                put(sheet, wedge, cx, cy, SLOT, SLOT)

        if n > 0:
            bx = cx + SLOT / 2 - 4 - BADGE / 2
            by = cy - SLOT / 2 + 4 + BADGE / 2
            draw_on.ellipse([bx - BADGE / 2, by - BADGE / 2, bx + BADGE / 2, by + BADGE / 2],
                            fill=(24, 34, 46, 255))
            font = face(34)
            if font is not None:
                draw_on.text((bx, by), str(n), font=font, fill=(255, 243, 220, 255), anchor="mm")

        if waiting > 0 and full > 0:
            font = face(56)
            if font is not None:
                secs = str(int(math.ceil(waiting)))
                draw_on.text((cx, cy), secs, font=font, fill=(255, 243, 220, 255), anchor="mm",
                             stroke_width=3, stroke_fill=(23, 36, 51, 242))


def hill_grid(sheet, span, cell, hill_top, hill_foot, at, burn=None):
    """`SiegeView.AimHill` - what a firepot is aimed with.

    <p>Drawn because it is the one thing on this board that is neither the board nor the bar, and
    because whether twenty panes over a hill full of raiders reads as a target or as a mess is a
    question only a picture answers.</p>

    <p>`burn` is a (lane, row) tap: the plus a firepot would take is lit in ember, which is
    `SiegeView.Scorch`. It is the only picture that can say whether five boxes of fire over a hill
    full of raiders reads as one blast or as five.</p>
    """
    lanes, rows = LANES, BLAST_ROWS
    wide = box_wide(span)
    tall = (hill_top - hill_foot) / rows

    layer = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(layer)

    for row in range(rows):
        for lane in range(lanes):
            cx, cy = at(*box_at(span, hill_top, hill_foot, lane, row))

            lit = burn is not None and in_blast(burn[0], burn[1], lane, row)
            fill = (255, 148, 66, 150) if lit else (79, 193, 255, 46)

            pen.rounded_rectangle(
                [cx - wide / 2 + 3, cy - tall / 2 + 3, cx + wide / 2 - 3, cy + tall / 2 - 3],
                radius=18, fill=fill)

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


#: The shapes the captions gate sweeps: the sheet this file draws by default, the tall phone the
#: mode is played on, and the squarest canvas `CanvasFit` allows. **Held here rather than taken
#: from the flags**, because the whole point of the sweep is that no single display answers it.
CAPTION_SHAPES = (("16:9 sheet", CANVAS, SAFE_BOTTOM),
                  ("19.5:9 phone", PHONE_CANVAS, PHONE_SAFE_BOTTOM),
                  ("4:3 tablet", TABLET_CANVAS, TABLET_SAFE_BOTTOM))


def caption_lines():
    """Every caption the hill can say, with the size it is said at and whether it opens.

    **All of them, rather than the ones somebody thought of.** The bosses come off `BOSS_KEY`,
    which is the mirror of the switch the view announces from, so a boss added to the mode turns
    up here without anybody adding a line - which is the half of this that goes stale otherwise.
    """
    said = []

    for kind in BOSS_KEY:
        said.append(("arrival", boss_banner(kind), BOSS_SIZE, True))
        said.append(("falls", loc("mode.siege.felled", boss_banner(kind)), FELLED_SIZE, True))
        said.append(("forecast", boss_banner(kind), 0.52, False))

    said.append(("wave", loc("mode.siege.wave", 10, 12), WAVE_SIZE, False))
    said.append(("enraged", loc("mode.siege.enraged"), TURN_SIZE, True))
    said.append(("unsealed", loc("mode.siege.unsealed"), TURN_SIZE, True))
    said.append(("forecast", loc("mode.siege.next"), 0.34, False))
    said.append(("chain", loc("mode.siege.chain", 9), 1.02, False))
    said.append(("count", loc("mode.siege.go"), 1.1, False))

    return said


def caption_gate(grid):
    """Measures every caption on every shape, and says which of them leaves the board.

    **The one gate that can see this at all.** A caption here is one unbroken line with wrapping
    off (`UIKit.Label`), so a string too wide for the board is not clipped, not wrapped and not
    reported - it is drawn off both ends, which is how "THE BLIGHTCALLER FALLS" shipped at nine
    cells on a board of eight. The view fits every one of them now
    (`SiegeView.Captions.Room`); what nothing in C# can check offline is whether the fit has
    anything left to give, because that needs the face's own metrics. This has them.
    """
    kept, over = CANVAS, []

    for name, canvas, safe in CAPTION_SHAPES:
        globals()["CANVAS"] = canvas
        globals()["SAFE_BOTTOM"] = safe

        _, cell, span = board_fit(grid)
        room = caption_room(span, cell)

        print("\n%-14s cell %5.1f   board %4.2fc   room %4.2fc"
              % (name, cell, span[0] / cell, room / cell))

        for what, text, size, swell in caption_lines():
            font, pt, wide = fit_line(text, cell * size,
                                      room / WAVE_POP_FLOOR if swell else room,
                                      cell * CAPTION_FLOOR)
            if font is None:
                sys.exit("no face on this machine to measure a caption with")

            pop = caption_pop(room, wide) if swell else 1.0
            drawn = wide * pop

            mark = "  " if drawn <= room + 0.5 else "<-"
            if drawn > room + 0.5:
                over.append((name, text))

            print("  %s %-9s %-26s %4.2fc -> %4.2fc  pop %4.2f  drawn %4.2fc"
                  % (mark, what, text[:26], size, pt / cell, pop, drawn / cell))

    globals()["CANVAS"] = kept

    if over:
        print("\n%d caption(s) drawn off the board:" % len(over))
        for name, text in over:
            print("  %s: %s" % (name, text))
        return False

    print("\nevery caption fits every shape, at its widest frame.")
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--level")
    ap.add_argument("--raiders", type=int, default=4,
                    help="how many of the first wave to stand on the hill")
    ap.add_argument("--no-bolts", action="store_true",
                    help="draw the board with nothing in flight")
    ap.add_argument("--warlord", default="cast", choices=("cast", "idle", "storm"),
                    help="draw the boss winding up (cast), standing (idle), or the frame its "
                         "volley leaves (storm)")
    ap.add_argument("--cooling", nargs="?", const="firepot=6,stormcall=22", default="",
                    help="draw slots mid-cooldown, as id=seconds pairs; bare gives a sample")
    ap.add_argument("--no-bar", action="store_true",
                    help="draw the board without the utility bar under it")
    ap.add_argument("--cast", default="", choices=("", "brood", "bone", "rabble", "wild", "court", "medley"),
                    help="which cast to draw: the insects (default), one of the five that "
                         "came after them, or the medley the Infinite lane draws")
    ap.add_argument("--wave", type=int, default=1,
                    help="which Infinite wave to stand on the hill; ignored on the authored "
                         "ladder, whose hill is its last authored wave")
    ap.add_argument("--line",
                    help="the turrets to stand, comma separated (%s, ...); the loadout is the "
                         "player's, so this is the only way to look at one" % ", ".join(LINE))
    ap.add_argument("--aim", choices=("hill", "wards"),
                    help="draw a utility's targeting: the firepot's grid, or the ward rings")
    ap.add_argument("--storm", type=int, nargs="?", const=3, default=0, metavar="N",
                    help="drop a stormcall bolt on the first N raiders - the only picture that "
                         "says whether a strike lands on the thing it struck")
    ap.add_argument("--alight", type=int, default=0, metavar="N",
                    help="draw the first N raiders wearing an ember turret's flame "
                         "(SiegeView.Ablaze) - the only picture that says whether a hill of "
                         "burning bodies is still a hill anybody can read")
    ap.add_argument("--burn", metavar="LANE,ROW",
                    help="with --aim hill, light the plus a firepot dropped on that box would "
                         "burn (`SiegeView.Scorch`) - the only picture that says whether five "
                         "boxes of fire read as one blast")
    ap.add_argument("--stood", action="store_true",
                    help="stand two live bombs on the hill where bombers died - the only picture "
                         "that says whether a bomb can be picked out of a hill full of walking "
                         "monsters, and whether it reads as a thing to tap")
    ap.add_argument("--armed", default="", metavar="N,N",
                    help="light the overcharge glyph on these wards (0-based), which is the only "
                         "picture that says whether it reads as a thing to tap and whether it "
                         "sits where a finger goes")
    ap.add_argument("--cogs", type=int, default=0, metavar="N",
                    help="lie N cogs on the hill, each with its own countdown ring - the only "
                         "picture that says whether the prize a kill pays can be picked out of a "
                         "hill full of walking monsters, and whether the ring reads as a clock "
                         "rather than as one more threat")
    ap.add_argument("--charms", nargs="?", const="auto", default="", metavar="CELL=KIND,...",
                    help="stand charms on the field - `12=prism,19=lance,27=storm`, or bare for "
                         "one of each on the middle row. They are dealt at a rate rather than "
                         "authored (one a window, `SiegeTuning.CharmWithin`), so no shipped field carries "
                         "one and this is the only way to look at one: whether a charmed *stone* "
                         "is told apart from the four beside it at forty pixels, and whether it "
                         "still reads as its own colour")
    ap.add_argument("--lance", nargs="?", type=int, const=-1, default=None, metavar="CELL",
                    help="draw a lance going off in CELL, or bare for the middle of the field - "
                         "the peak frame of it, with both beams at full and the cross failing "
                         "outward from the stone. The one thing about invariant 37cn a still can "
                         "still be asked: whether the beam reads as light rather than as a "
                         "highlighter line, and whether a burst on every cell reads as a row "
                         "failing or as noise")
    ap.add_argument("--volley", nargs="?", type=int, const=-1, default=None, metavar="CELL",
                    help="draw a stormglass firing from CELL, or bare for the middle of the "
                         "field - every beam open at once, which is what the frozen board "
                         "really shows. The question it answers is density: whether a dozen "
                         "layered beams read as a barrage or as a white smear")
    ap.add_argument("--heaved", type=float, default=None, metavar="T",
                    help="draw an anvil's front mid-sweep, nought at the line and one at the "
                         "crest - the charm's whole payoff is a moment, so a still frame is what "
                         "it is for")
    ap.add_argument("--stilled", type=float, default=None, metavar="T",
                    help="draw the hourglass stopping the hill, T of the way through the sweep "
                         "(0..1) - the wavefront and the dial it hangs over the hill")
    ap.add_argument("--forecast", action="store_true",
                    help="draw the breather's forecast band over the hill - what the next wave is "
                         "bringing, by colour. The one readout that turns 'which colour is "
                         "coming' from a parse into a glance, and nothing but a picture can say "
                         "whether it does")
    ap.add_argument("--level-tag", default="LEVEL 2",
                    help="what the header's level tag says, beside the way back. Empty leaves "
                         "it off.")
    ap.add_argument("--no-header", action="store_true",
                    help="leave off the header bar and its readouts, which since the row moved "
                         "up level with the two corner keys is the only picture that says "
                         "whether a number lands on a button")
    ap.add_argument("--phone", action="store_true",
                    help="draw a 19.5:9 display with an iPhone's home-indicator strip at the "
                         "foot of it, rather than the 16:9 sheet this file draws by default - "
                         "the only shape in which the shelf's own foot is visible at all")
    ap.add_argument("--tablet", action="store_true",
                    help="draw a 4:3 tablet's canvas (1620x2160 units, per `CanvasFit`) rather "
                         "than the 16:9 sheet this file draws by default - the only shape in "
                         "which the board's own bands can be seen at a tablet's proportions")
    ap.add_argument("--captions", action="store_true",
                    help="measure every caption the hill can say against the board it is drawn "
                         "on, at three canvas shapes, and say which of them leaves it - the "
                         "only gate that can see a caption too wide for the screen, because "
                         "nothing clips one")
    ap.add_argument("--out", default=str(REPO / "Tools" / "siege_boards.png"))
    args = ap.parse_args()

    if args.phone and args.tablet:
        sys.exit("--phone and --tablet are two displays; draw one at a time")

    if args.captions:
        first = next(iter(levels()))[1]
        sys.exit(0 if caption_gate(layout_of(first).grid) else 1)

    if args.phone:
        globals()["CANVAS"] = PHONE_CANVAS
        globals()["SAFE_BOTTOM"] = PHONE_SAFE_BOTTOM

    if args.tablet:
        globals()["CANVAS"] = TABLET_CANVAS
        globals()["SAFE_BOTTOM"] = TABLET_SAFE_BOTTOM

    # **Comma separated**, because the one picture this mode cannot do without is the four boss
    # rungs side by side: invariant 37z was found by looking at exactly that, and 37ac was tuned
    # against it. One at a time is four windows and no comparison.
    wanted = [x.strip() for x in args.level.split(",")] if args.level else None
    picked = [(i, chapter, lv) for i, (chapter, lv) in enumerate(levels())
              if wanted is None or lv["id"] in wanted]
    if not picked:
        sys.exit("no level called %s" % args.level)

    # **Named rather than indexed**, because what this flag is for is looking at a line somebody
    # chose - and a turret whose pictures are not on disk draws as a white rectangle rather than
    # as a missing one (invariant 7b), so it is refused here instead.
    stood = [s.strip() for s in args.line.split(",")] if args.line else None
    for model in stood or ():
        worn = "%s.png" % model if legendary(model) else "%s_r.png" % model
        if not (ART / "Wards" / worn).exists():
            sys.exit("no turret called %s" % model)

    held = {"firepot": 2, "mending": 0, "surge": 5, "stormcall": 1}

    cooling = {}
    for pair in (args.cooling or "").split(","):
        if "=" not in pair:
            continue
        name, _, secs = pair.partition("=")
        cooling[name.strip()] = float(secs)

    burn = None
    if args.burn:
        lane, _, row = args.burn.partition(",")
        burn = (int(lane), int(row))
        if not (0 <= burn[0] < LANES and 0 <= burn[1] < BLAST_ROWS):
            sys.exit("--burn is lane 0..%d, row 0..%d" % (LANES - 1, BLAST_ROWS - 1))

    # **Each rung draws its own chapter's cast**, which is `SiegeMode.CastFor` mirrored: a picture
    # of two chapters side by side that drew one cast on both would be a picture of neither.
    # `--cast` overrides, for the one job an override is for - holding two casts up against each
    # other on the same rung.
    #
    # **It used to read `"kay" if args.wave else ""`, and `--wave` defaults to one**, so the
    # condition was true on every run this tool had made: the authored chapter was drawn with the
    # Infinite lane's cast, in the one picture this mode has for everything a number cannot see.
    # That is invariant 44d's own trap - a render that draws something other than the screen sends
    # you off to fix what was never broken - and the lane needed no special case at all, because
    # it is a chapter and the chapter decides.
    global CAST, ALIGHT

    ALIGHT = args.alight

    shots = []
    for rung, chapter, lv in picked:
        CAST = args.cast or CHAPTER_CASTS.get(chapter, "")
        bombs = [(1, 2), (3, 1)] if args.stood else None

        shot = draw(lv, args.raiders, not args.no_bolts, aim=args.aim,
                    boss=args.warlord, rung=rung, wave=args.wave, line=stood, burn=burn,
                    storm=args.storm, bombs=bombs, cogs=args.cogs, forecast=args.forecast,
                    armed=[int(x) for x in args.armed.split(",") if x.strip()],
                    charms=stood_charms(args.charms, lv), lance=stood_lance(args.lance, lv),
                    volley=stood_lance(args.volley, lv), stilled=args.stilled,
                  heaved=args.heaved)
        if not args.no_bar:
            bar(shot, held, cooling)
        if not args.no_header:
            # **The siege's own one.** How far through the raid this is, and nothing else: the
            # raiders left and the matches spent both came off the header after a device said so
            # (invariant 37v's rule applied twice more), and a row of one sits in the middle.
            header(shot, [("3/8", "WAVE")], level=args.level_tag)
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
