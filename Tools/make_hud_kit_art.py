# -*- coding: utf-8 -*-
"""Cuts the app's furniture out of the cartoon UI kit, over a world cut from the map pack.

    python Tools/make_hud_kit_art.py            # write them
    python Tools/make_hud_kit_art.py --check    # prove the shipped PNGs and metas reproduce
    python Tools/make_hud_kit_art.py --contact <png>   # lay them out; look at it

Seventeen sprites under `Assets/Game/Art/Ui/Hud/` — the rails, the plates, the troughs, a
progress fill, the nav cap's two faces, the hero's lander and its beam, and the world they
all stand in — plus fourteen more the RECUT list replaces in place, which is where the pill
and square controls come from.

**This is the third kit to wear these names and the names did not move**, which is the whole
method (invariant 44). `Skins` says a *role* — the affirmative pill, the back key, the thing
that is not a control right now — and every one of the ninety-odd call sites names a colour
that has meant that role since the UI was written. So changing what the whole app looks like
is re-cutting what those names point at; nothing is swept, nothing is missed, the compile
proves nothing broke and `git checkout` on the PNGs puts the old look back. Two kits went the
same way before this one, which is the return on having made those call sites name roles.

**Why this pack, and what was actually wrong with the last one.** The owner's verdict on the
merge-shooter kit was that both screens were boring, the colours were bad and the assets were
wrong — and a render says exactly why, which is worth writing down because none of it is a
matter of taste. Every plate on those screens was a *cream rim around the ground colour*: the
card interiors were within a few points of the backdrop, so nothing on the screen read as an
object standing on anything. One rim, one width, one colour, on every surface — so no
hierarchy either. And the ground behind it all was a flat near-black wash, so the screen had
no world in it. **A screen made of outlines on a void is boring however well each outline is
drawn**, and the fix is not a better rim: it is opaque plates with their own material, and
something alive behind them.

This pack is that, and it is the register the genre actually plays in — Royal Match, Toy
Blast — which is what this game is aimed at. Every piece is a **saturated face inside one
heavy navy keyline**: `(6, 24, 56)`, the same on all sixty-eight sprites, which is what makes
a kit of ribbons, bars, badges and discs read as one object family. Its bars are two-tone —
a lighter top half over a saturated bottom — so a plate has a *material* rather than a fill,
and they nine-slice cleanly, which is what lets six sources carry every plate in the app.

**The world is the other half and it is most of the change.** `l1-sky` and `l2-ground` out of
the level-map pack, composed, softened and graded here rather than in a screen — a bright
floating-island world behind everything. That is what turns two screens of chrome into one
place. It is *cut* to portrait in this tool for `Scenery`'s reason and for invariant 44c's: a
screen can only crop by offset, which is a number nothing checks and which two screens would
each have to get right.

**Plates are navy on a bright world, which is the one palette decision here.** The kit hands
its bars over in blue, gold and green, and a blue plate on a blue sky is a plate that has to
be found. Sunk to the kit's own navy — the colour it already troughs and keylines everything
in — a plate is the darkest thing on the screen and its gold ribbon, green pill and orange
price are the brightest. That is the contrast the last kit spent a rim trying to buy.

**A re-paint is masked by *darkness*, and that is the one thing this kit needed that the last
did not.** The keyline is the family, so rotating a bar wholesale — which takes the outline
round with it — gives eight differently-outlined blobs rather than a kit. The last kit
protected its moulding by *hue*, which works only while nothing the pack paints a face in is
that colour; here the pack's blue bar sits 0.032 from the keyline's own navy, so a band wide
enough to cover an outline's antialiasing swallows the face as well and the paint becomes a
silent no-op. `DARK_FLOOR` separates them by the thing they actually differ in: the keyline
is 8% luma and the darkest *face* in the pack is 13%. That single number is what lets every
recut here rotate off one gold mould with the mask left on.

**Which axes slice is a decision, not a measurement** — but this pack needs the judgement
far less than the last two did. Its bars, troughs and squares are honest rounded rectangles
with nothing painted on the edges a slice stretches, so their borders are *measured*; the one
piece that could not be is the ribbon, whose tails are its silhouette, and the answer there is
that a ribbon does not slice at all.

**And a sliced piece is scaled for the size its corner should draw at** (invariant 44a). The
bar family is 400x166 with a 16-pixel corner, which is a small radius at native size and the
right one at the size a card draws — so most of these are cut at or near 1.0, where the last
kit's had to be halved.

**Nothing is keyed.** Every source is already on transparency and individually named.

**Four pieces are drawn rather than cut** — the beam, the burst, the companion's plinth and
the "+" — because no pack here has anything that is any of them. The first three are named in
DRAWN below; the "+" is drawn onto a cut square. **The tower pack is no longer read at all**,
which is worth saying: it was in this tool for one piece, and that piece is now drawn.

**The sources are outside the repo**, in the two folders the art-source-packs note records.
`--check` passes when they are absent, so a checkout without them still runs the gate.

**And this is the half a numeric gate cannot have an opinion about.** Every border measured,
every sprite reproduced, `--check` green, and what came out last time was a screen nobody
could read. `Tools/render_home.py` and `Tools/render_shop.py` are what say so.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import sys
import zipfile
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

try:
    import numpy as np
    from scipy import ndimage as ndi
except ImportError:                                        # pragma: no cover
    sys.exit("This needs numpy and scipy:  python -m pip install numpy scipy")

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Art" / "Ui" / "Hud"
SHARED = REPO / "Assets" / "Game" / "Art" / "Ui"
TEMPLATE = SHARED / "panel_main.png.meta"

#: Where the packs live. Two roots rather than one, because these are not all in the folder
#: `make_siege_art.py` reads — and copying a zip to make one path work is a second copy of a
#: licensed pack that nothing keeps in step. Both are searched, in order.
DEFAULT_SOURCE = Path(r"C:\Users\Digikey\Downloads\to-assets")
EXTRA_SOURCE = Path(r"C:\Users\Digikey\Downloads\2D ASSETS")

CARTOON = "craftpix-net-828046-cartoon-ui-elements-mini-kit (2).zip"
WORLD = "craftpix-135791-level-map-2d-game-backgrounds.zip"

#: Where each pack keeps the art this cuts.
ROOT = {"cartoon": "Png/", "world": "_PNG/"}

#: What a sunk well is made of: the kit's own trough navy, one step under the (9, 35, 92) it
#: draws every readout in. Named here because five plates share it and a screen where two of
#: them disagreed about "dark" would read as two kits.
WELL_INK = (10, 30, 72)

#: The keyline every one of this pack's sixty-eight sprites is drawn in, and the hue of it.
#: Measured, not typed: the darkest opaque pixel of every bar, trough, square, tab, disc and
#: badge is the same `(6, 24, 56)`. That single fact is what makes the kit a family, and it
#: is why a re-paint has to leave it alone.
KEYLINE = (6, 24, 56)

#: The hue the keyline happens to sit at, kept for the record and deliberately **not** used
#: as a mask. The last kit masked a re-paint by hue — protect anything within a band of the
#: moulding's own navy — which works only while nothing the kit paints a *face* in is that
#: colour. Here the pack's blue bar sits 0.032 away, well inside any band wide enough to
#: cover a keyline's antialiasing, so a hue mask either protects the face too (the paint
#: becomes a silent no-op) or is turned off and the outline rotates with it. `DARK_FLOOR`
#: separates them by value instead, which is what they actually differ in.
KEYLINE_HUE = 0.6111

#: Below this saturation a pixel has no hue worth moving — the white ring on a cap, the
#: highlights, the shadows. Rotating them is what turns a family's rim eight colours.
GREY_FLOOR = 0.18

#: Below this *luma* a pixel is keyline rather than face, whatever its hue. The one number
#: that lets a blue source be repainted blue-adjacent without its outline going round with
#: it — see `hued`. Measured: the keyline is 8% and the darkest face in the pack (the trough
#: navy) is 13%, so anything in between separates them and 0.16 leaves margin for the
#: antialiased pixels along the join.
DARK_FLOOR = 0.16

#: How far a hue rotation travels. Invariant 37p: most of the way, never all of it.
HUE_PULL = 0.86


class Piece:
    """One sprite: what it is cut from, what it is called, and how it is drawn.

    `pack` is "cartoon" or "world". `zoom` scales the source, `slice_x` / `slice_y`
    say which axes it stretches on, and `border` overrides the measurement — given in
    **source** pixels so it follows `zoom`.

    `hue` re-paints it, masked by INDIGO/GREY_FLOOR unless `mask` is off — which it has to be
    for a piece that is *all* keyline colour, where the mask would keep the whole sprite.
    `grey` drains it instead, which is a different question and not a hue at all. `well`
    sinks the interior; `dim` darkens the lot. `flip` turns it upside down, which is how one
    bar serves as both rails. `flatten` paints an ornament out — see `flattened`. `face`
    repaints the inside of a disc, which is how a glyphed round button becomes a blank cap —
    see `faced`. `scene` composes the world instead of reading one source. `trim` crops to
    the silhouette, which several of these need because the pack pads every artboard.
    """

    def __init__(self, pack, source, name, zoom=1.0, slice_x=False, slice_y=False, hue=None,
                 pull=HUE_PULL, sat=1.0, grey=False, dim=1.0, well=None, border=None,
                 crop=None, resize=None, blur=0.0, flip=False, plus=False, mask=True,
                 flatten=None, face=None, scene=None, trim=False):
        self.pack, self.source, self.name, self.zoom = pack, source, name, zoom
        self.slice_x, self.slice_y = slice_x, slice_y
        self.hue, self.pull, self.sat, self.grey, self.dim = hue, pull, sat, grey, dim
        self.well, self.border = well, border
        self.crop, self.resize, self.blur = crop, resize, blur
        self.flip, self.plus, self.mask = flip, plus, mask
        self.flatten, self.face, self.scene, self.trim = flatten, face, scene, trim


KIT = [
    # ---------------------------------------------------------------- the rails
    # The bar across the top of a screen and the one across its foot: the pack's long navy
    # trough, which is the one piece in it that is *quiet*. A rail frames content, so it may
    # not compete with it — the last kit put a saturated cyan slab across the top and another
    # across the foot and they were the loudest things on the screen.
    #
    # One source, flipped for the foot, because the trough is lit along its top edge: a foot
    # rail that is not flipped is lit on the edge facing away from the screen. Only x slices,
    # both being drawn to the full canvas width at one height.
    #
    # **Darker than the pack cuts it, and that is what makes the world behind it work.** Left
    # at (9, 35, 92) a rail is a mid navy band against a bright sky, which reads as a third
    # object rather than as the edge of the screen. Taken down it is the same navy the plates
    # are sunk to, so the top rail, the foot rail and every card's interior are one dark.
    Piece("cartoon", "Artboard 40", "rail_top", 1.0, slice_x=True, trim=True, dim=.62),
    Piece("cartoon", "Artboard 40", "rail_bottom", 1.0, slice_x=True, trim=True, flip=True,
          dim=.62),

    # ---------------------------------------------------------------- the plates
    # The modal plate and the card, and they are the same source painted two depths. The
    # pack's bar family — 400x166 with a 16-pixel corner and a two-tone face, six of them,
    # identical but for colour — is the one shape here that can carry every plate in the app,
    # and it nine-slices honestly because it really is a rounded rectangle with nothing
    # painted on it.
    #
    # **Navy, on a bright world, and that is the palette decision this restyle turns on.**
    # The pack hands these over in blue, gold and green; a blue plate on a blue sky has to be
    # *found*, and a gold one competes with every ribbon and price on top of it. Sunk to the
    # kit's own trough navy a plate is the darkest thing on the screen, its keyline still
    # reads against the world, and gold, green and orange on top of it are the brightest
    # things a player can see. That is the contrast the last kit spent a cream rim failing to
    # buy.
    #
    # A panel stands *in front of* the screen and a card stands *on* it, so the panel keeps
    # more of its face — a hair lighter and a shallower well — and the card is sunk further.
    # Both are measured rather than typed: this source has no ornament, which is exactly why
    # it was chosen over the pack's clipboard.
    Piece("cartoon", "Artboard 32 copy", "panel", 1.0, slice_x=True, slice_y=True, trim=True,
          hue=0.618, pull=1.0, sat=.80, dim=.94, well=(9, (18, 44, 98), 4.0, .16)),
    Piece("cartoon", "Artboard 32 copy", "card", 1.0, slice_x=True, slice_y=True, trim=True,
          hue=0.618, pull=1.0, sat=.88, dim=.82, well=(9, WELL_INK, 4.0, .14)),

    # A hole rather than a thing standing on the screen: an avatar's seat, an empty shop
    # cell, a chest's socket. The pack's own navy chip — the one piece it draws that is
    # already inset, and the only way any kit says "inset" is by being darker than everything
    # standing on it.
    Piece("cartoon", "Artboard 51", "slot", 1.0, slice_x=True, slice_y=True, trim=True,
          dim=.74),

    # ------------------------------------------------------- troughs and title bars
    # What a readout sits in: the pack's long navy trough, cut as it ships. This is the one
    # place the last kit needed `welled` and this one does not — the pack draws its readouts
    # *dark* because it writes light numbers on them, which is what this game does too. Two
    # kits in a row had to be sunk to stop being cream-on-cream; this one arrives right.
    Piece("cartoon", "Artboard 38", "trough", 1.0, slice_x=True, trim=True),

    # What goes *in* one. Cut near-white with the pack's two-tone shading kept, so a call
    # site tints it: `Image.color` is a multiply, so a white fill takes any colour cleanly
    # and keeps the lighter top half that makes it read as a filled tube rather than a block.
    # Cut from the gold fill rather than the blue one because gold is the least saturated of
    # them and so drains to the cleanest white.
    Piece("cartoon", "Artboard 39", "fill", 1.0, slice_x=True, trim=True, grey=True),

    # A title is a *word* the game owns rather than a number, and this pack draws the thing
    # that has been missing from every version of this UI: a real ribbon, with tails, in one
    # heavy keyline. Gold on it, which is what a heading is written in.
    #
    # **It does not slice and it must not.** The tails are the silhouette, the top edge is a
    # curve and the keyline runs round all of it — so a measured border reads the tails
    # (109 pixels of a 775-pixel sprite) and a stretched middle would flatten the curve away
    # from the sample column. A ribbon is drawn at its own aspect, which is what `Scenery`
    # does with it.
    Piece("cartoon", "Artboard 16", "title", 1.0, trim=True),

    # ---------------------------------------------------- the pills and squares
    # **The pill and square controls are not here.** They are re-cuts of `Ui/btn_*` and
    # `Ui/sq_*` under RECUT below, which is what lets the whole app move without a sweep — a
    # second copy here would be an addressed sprite nothing draws.
    #
    # The "+" is here because it is a *painted control*: the pack has no plus anywhere, so
    # this is its own square with one drawn on it, which keeps it one image where a square
    # plus a glyph would be two.
    Piece("cartoon", "Artboard 50", "add", 1.0, trim=True, plus=True),

    # The hub's affirmative, in the one colour no re-cut of `Ui/btn_*` carries: #FFC83D.
    # A KIT piece rather than a RECUT because it is a name the game did not already have,
    # and RECUT patches an existing `.meta` by design.
    Piece("cartoon", "Artboard 32 copy 2", "btn_gold", 1.0, slice_x=True, trim=True,
          hue=0.119, pull=1.0, sat=0.86),

    # ------------------------------------------------------------- the hub's plates
    # **The Battle key's own mould, sliced on both axes so it can be any size.** The three
    # boxes above the companion used to be a flat rounded rectangle with a traced rim, and the
    # owner's verdict was that the button looked alive beside them - which it does, because a
    # bought mould carries a two-tone face, a highlight along its top and a keyline that turns
    # with the colour, and a drawn rectangle carries none of those.
    #
    # `slice_y` is what makes this a different piece from `btn_gold` rather than the same one
    # at another size: the pill is sliced on x only, so stretching it to 240 or 300 units tall
    # would smear the face. Sliced both ways the corner stays 15 units and the two-tone split
    # scales with the box.
    #
    # Three hues rather than one, because these are three different things and the kit's own
    # family is told apart by hue. Saturation is pushed past the mould's own, which is the
    # "pure bright colours" this was asked for - nothing here is faded and nothing is greyed.
    Piece("cartoon", "Artboard 32 copy 2", "plate_blue", 1.0, slice_x=True, slice_y=True,
          trim=True, hue=0.565, pull=1.0, sat=1.14),
    Piece("cartoon", "Artboard 32 copy 2", "plate_orange", 1.0, slice_x=True, slice_y=True,
          trim=True, hue=0.062, pull=1.0, sat=1.14),
    Piece("cartoon", "Artboard 32 copy 2", "plate_violet", 1.0, slice_x=True, slice_y=True,
          trim=True, hue=0.782, pull=1.0, sat=1.10),

    # ------------------------------------------------------------- the nav caps
    # The bottom bar's five caps, lit and unlit. **Discs, and that is the change a player
    # notices first**: the pack draws a round button as a coloured face inside a thick white
    # ring inside the navy keyline, which is the single most legible control shape there is
    # at thumb size and the one thing the last two kits had nothing like. Five brown squares
    # along the foot of the screen was most of what read as dull.
    #
    # The pack's discs carry a play arrow, a burger and a cross, and this game's five
    # destinations are none of those — so the glyph is *painted out* and `NavBar` keeps
    # drawing the icons it already owns, which is what stops a restyle quietly renaming five
    # destinations. `faced` repaints the inner disc two-tone, so a blanked cap still has the
    # shading every other face in the kit has.
    #
    # Lit is the pack's gold and unlit its trough navy, which is the same pair the tabs use
    # one level down.
    Piece("cartoon", "Artboard 61", "cap_on", 1.0, trim=True,
          face=(0.760, (255, 216, 74), (240, 152, 16))),
    Piece("cartoon", "Artboard 61", "cap_off", 1.0, trim=True,
          face=(0.760, (52, 92, 158), (26, 56, 116))),

    # ---------------------------------------------------------------- the world
    # What the whole app stands in, and most of what makes this a restyle rather than a
    # re-paint. The level-map pack's sky and ground, composed here — a bright floating-island
    # world, softened so it is a place rather than a picture asking to be looked at.
    #
    # **Composed in the tool and not in a screen**, for invariant 44c's reason: a screen can
    # only crop by offset, which is a number nothing checks and which the hub and the
    # storefront would each have to get right. The source is portrait already (1536x2048
    # against a 1080x1920 canvas), so the crop is a *sixth off the top* — the map's own
    # signpost and its empty upper sky — and the envelope does the rest.
    #
    # **Blurred hard, and warmed, and that is all.** It is not dimmed here: `Scenery.Room`
    # shades it, and a backdrop baked dark cannot be lifted back up by a screen that wants it
    # brighter. What it *is* given is saturation back, because a heavy blur over flat vector
    # art costs colour as well as edges.
    #
    # **The blur is a decision and it went up twice.** At 3 pixels every tree, plank and
    # flower on the map was still legible, so the world competed with the plates standing on
    # it — the same fault as the last kit's junction box behind the companion, arriving from
    # the opposite direction. A backdrop has to read as *somewhere*, not as something. Nine
    # pixels at this size leaves the islands, the water and the path as shapes and takes the
    # detail away, which is what a depth-of-field does and what nothing else here can.
    Piece("world", "01/layers/l1-sky", "room", 1.0,
          scene=("01/layers/l2-ground", "01/layers/l3-decoartions"),
          crop=(0, 300, 1536, 2048), resize=(810, 1450), blur=0.0, sat=1.12),
]


#: The pieces no pack here has, drawn instead. Named as a list rather than folded into KIT so
#: that "what is cut and what is drawn" is a question this file answers at a glance —
#: `make_siege_art.py`'s rule, where the rampart and the plate are drawn and the rest is cut.
#: The "+" is drawn *onto* a cut square, so it is a KIT entry with `plus` set; these three are
#: drawn from nothing. The lander joined them the hard way — see `lander`.
DRAWN = ("beam", "burst", "lander")


# --------------------------------------------------------------------- the recuts
# The shared controls, re-cut in place. These keep the names the game already calls them by.
#
# **The `.meta` is patched rather than rewritten**, because Addressables keys its entries on
# the guid: a fresh guid would orphan fourteen registered addresses and the build would fail
# with a stack trace of package internals. Only the border moves, because the new art's
# corner is not the old art's corner.
#
# One mould for the pills and one for the squares, painted eight and six ways. The pack's
# own six colours were not enough and are not the right shapes — it draws green and teal
# wide, orange and gold square, and this UI needs eight of one and six of the other — so the
# colour is a rotation and the mould is the pack's.
#
# **The squares are scaled down to 0.62 and that is a bound rather than taste.** The smallest
# square control in this game is drawn at 84 units; the source's corner is 37 pixels, so at
# native size the two corners are 72 of those 84 and the button is nothing but corner. At
# 0.62 the pair is 45 and the rounding still reads at 138.
RECUT = [
    # **The pack's own three colours where it has them, and one gold mould for the rest.**
    # Green, gold and blue are cut as they ship, so those three keep the exact shading the
    # pack drew; the other five are rotations of the gold, because gold is the furthest of
    # the three from every colour asked for and so rotates with the least distortion. Rotating
    # the *blue* is what a naive reading suggests and is the worst choice available: it sits
    # a hair from the keyline's own hue, which is the whole reason `DARK_FLOOR` exists.
    #
    # All eight keep their keyline, which is the thing that makes them one family, and it is
    # kept by value rather than by hue — see `hued`.
    Piece("cartoon", "Artboard 32 copy 4", "btn_green", 1.0, slice_x=True, trim=True),
    Piece("cartoon", "Artboard 32 copy 2", "btn_orange", 1.0, slice_x=True, trim=True,
          hue=0.068, pull=1.0, sat=1.02),
    Piece("cartoon", "Artboard 32 copy 2", "btn_red", 1.0, slice_x=True, trim=True,
          hue=0.005, pull=1.0, sat=1.06),
    Piece("cartoon", "Artboard 32", "btn_blue", 1.0, slice_x=True, trim=True),
    Piece("cartoon", "Artboard 32 copy 2", "btn_violet", 1.0, slice_x=True, trim=True,
          hue=0.775, pull=1.0, sat=1.00),
    Piece("cartoon", "Artboard 32 copy 2", "btn_aqua", 1.0, slice_x=True, trim=True,
          hue=0.472, pull=1.0, sat=.98),
    Piece("cartoon", "Artboard 32", "btn_gray", 1.0, slice_x=True, trim=True, grey=True,
          dim=.80),
    Piece("cartoon", "Artboard 32", "btn_dark", 1.0, slice_x=True, trim=True, grey=True,
          dim=.44),

    # The squares are the same mould sliced on both axes rather than a different source, and
    # that is deliberate: the pack draws exactly two square chips, both navy-ish, so a family
    # of six squares cut from them would be six shades of one colour. A bar sliced on both
    # axes is the same object at a different aspect — same corner, same two-tone face, same
    # keyline — which is what a square control in this kit should be.
    #
    # Cut at native size, so the 15-pixel corner draws as 15 units on the 84..106-unit
    # controls these become. Invariant 44a in the easy direction for once: this pack's corner
    # is small enough that nothing has to be scaled down to keep a middle.
    Piece("cartoon", "Artboard 32 copy 2", "sq_orange", 1.0, slice_x=True, slice_y=True,
          trim=True, hue=0.068, pull=1.0, sat=1.02),
    Piece("cartoon", "Artboard 32", "sq_blue", 1.0, slice_x=True, slice_y=True, trim=True),
    Piece("cartoon", "Artboard 32 copy 2", "sq_aqua", 1.0, slice_x=True, slice_y=True,
          trim=True, hue=0.472, pull=1.0, sat=.98),
    Piece("cartoon", "Artboard 32 copy 4", "sq_green", 1.0, slice_x=True, slice_y=True,
          trim=True),
    Piece("cartoon", "Artboard 32", "sq_gray", 1.0, slice_x=True, slice_y=True, trim=True,
          grey=True, dim=.80),
    Piece("cartoon", "Artboard 32", "sq_dark", 1.0, slice_x=True, slice_y=True, trim=True,
          grey=True, dim=.44),
]


#: The two button moulds, and what a caption drawn on each has to be lifted by. Printed on
#: every run and read into `UIKit.PillFaceLift` / `SquareFaceLift` by hand — see `facelift`.
#:
#: **This kit moulds the other way round from the last one and the numbers say so.** The
#: merge kit put a bright face *above* an indigo base, so both lifts were positive and large;
#: this one draws a lighter top half over a saturated bottom half of the same hue, which is a
#: material rather than a moulding — so the face is very nearly the whole sprite and the lift
#: is very nearly nought. Keeping the question is the point: the answer has been 0.088, 0.000
#: and now this, over three kits, and not one of the twenty call sites moved.
MOULDS = (("pill", "Artboard 32 copy 4"), ("square", "Artboard 32"))


# --------------------------------------------------------------------------- reading
def zipped(folder, name):
    """One of the licensed packs, or None when it is not on this machine."""
    path = folder / name
    return zipfile.ZipFile(path) if path.exists() else None


def found(folders, name):
    """The first of the source folders holding a pack, or None when none of them does.

    Two roots rather than one because these packs are not all in the folder the siege tools
    read, and copying a licensed zip so that one path works is a second copy nothing keeps in
    step — the fault this project already records about content and about art.
    """
    for folder in folders:
        z = zipped(folder, name)
        if z is not None:
            return z
    return None


def read(z, root, name):
    return Image.open(io.BytesIO(z.read(root + name + ".png"))).convert("RGBA")


def corner(alpha):
    """A rounded rectangle's corner radius, as (left, bottom, right, top) in sprite pixels.

    Read off the sprite rather than typed, so a border cannot go stale when a source is
    re-cut — and overridden by `Piece.border` wherever the pack paints an ornament onto a
    silhouette that is already square, which is most of the plates in this one.
    """
    on = alpha > 128
    rows, cols = on.sum(1), on.sum(0)
    if not rows.max() or not cols.max():
        return None

    def run(profile, full):
        first = int(np.argmax(profile >= full - 2))
        last = len(profile) - 1 - int(np.argmax(profile[::-1] >= full - 2))
        return first, len(profile) - 1 - last

    top, bottom = run(rows, rows.max())
    left, right = run(cols, cols.max())
    return left, bottom, right, top


def facelift(im):
    """How far above a button's middle its lit face sits, as a fraction of its height.

    The pack moulds every control as a bright face over an indigo base, so a caption centred
    on the sprite lands low. The face is the run of rows from the top of the silhouette whose
    mean luminance is in the upper half of the sprite's range; the base is what is left.

    <b>Mean and not max.</b> A max reads the one-pixel specular streak along the top rim,
    which is present on the dark faces too — measured that way a purple button reports a lift
    of 0.39 and an orange one 0.07, from the same mould.
    """
    a = np.asarray(im)
    lit = a[..., 3] > 128
    lum = (a[..., :3].astype(np.float32) * np.array([.2126, .7152, .0722], np.float32)).sum(-1)

    rows = np.where(lit.any(1))[0]
    if not len(rows):
        return 0.0

    means = np.array([lum[y][lit[y]].mean() if lit[y].any() else 0.0 for y in range(a.shape[0])])
    cut = means[rows].min() + (means[rows].max() - means[rows].min()) * .55

    face = [y for y in rows if means[y] >= cut]
    if not face:
        return 0.0

    run = [face[0]]
    for y in face[1:]:
        if y > run[-1] + 3:
            break
        run.append(y)

    middle = (run[0] + run[-1]) / 2.0
    return (a.shape[0] / 2.0 - middle) / a.shape[0]


# --------------------------------------------------------------------------- painting
def crisp(im, scale):
    """Upscales flat-shaded art and puts its edges back — `make_iap_art.crisp`'s rule."""
    if scale <= 1:
        return im

    r, g, b, a = im.split()
    a = a.point(lambda v: max(0, min(255, int((v - 128) * 3.2 + 128))))
    rgb = Image.merge("RGB", (r, g, b)).filter(
        ImageFilter.UnsharpMask(radius=max(1, int(scale)), percent=70, threshold=2))
    return Image.merge("RGBA", (*rgb.split(), a))


def inside(im):
    """How far each pixel is from the outside of the sprite, in pixels.

    Padded before the transform for `thinner`'s reason: several of these are opaque to their
    own canvas edge, and without a transparent margin the distance is measured from nothing.
    """
    alpha = np.asarray(im)[..., 3]
    pad = 4
    solid = np.pad(alpha > 128, pad, constant_values=False)
    return ndi.distance_transform_edt(solid)[pad:-pad, pad:-pad]


def welled(im, depth, ink, ramp=5.0, keep=.09):
    """Sinks a plate's interior to a *named colour*, leaving its rim alone.

    What makes a trough a trough here. The pack draws for its own light screens — cream
    readout bars, amber boards — and this game writes `Pal.Cream` and `Pal.Sun` on them, so
    the well has to be dark and the rim has to stay.

    <b>It replaces the colour rather than dimming it, and that distinction is the whole
    difference between a plate and a piece of cardboard.</b> The first cut multiplied the
    interior by .30, which is arithmetically a perfectly good way to make something darker
    and is visually a disaster: amber multiplied down is **brown**. Every panel and every
    card in the game became a mud-coloured box inside a thick pale frame — shipped to a
    device, and the verdict was one word. A well is a *material*, so it is given one: the
    kit's own indigo taken most of the way to black, with a trace of what was underneath
    kept so the plate still has the pack's grain in it rather than reading as a flat hole.

    <b>Dimming is never the way to recolour something warm.</b> Multiply takes a hue toward
    black along its own line, so anything amber, coral or gold arrives as mud, and the more
    of the screen it covers the worse it reads — which is why the small troughs survived this
    and the big plates did not.
    """
    far = inside(im)
    k = np.clip((far - depth) / ramp, 0.0, 1.0)[..., None]

    a = np.asarray(im).astype(np.float32)
    target = np.array(ink, np.float32)

    # A trace of the original keeps the pack's shading; without it the well is a flat hole.
    sunk = target + (a[..., :3] - target) * keep
    a[..., :3] = a[..., :3] * (1.0 - k) + sunk * k
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def flattened(im, top, colour):
    """Paints an ornament out of a band of rows, leaving the silhouette alone.

    <b>Both of this kit's boards carry a coral tab, and a tab cannot survive a nine-slice.</b>
    It is painted *onto* the board rather than standing above it — the silhouette does not
    change where it is, so there is nothing to crop — and it sits in the middle of the top
    edge, which is precisely the part a slice stretches. Cut as it ships, every panel and
    every card in the game drew a coral band across its whole top, straight through whatever
    was written there: the daily-bonus header ran into it and both feature cards wore it like
    a highlighter smear. No gate can see that and the render showed it at once.

    Only the colour moves. Alpha is untouched, so the rounded corners keep their edges, and
    the board is flat colour in this band anyway — measured, rows 100..1332 of the store board
    are one amber — so what is lost is the tab and nothing else.
    """
    a = np.array(im)
    band = a[:top]
    lit = band[..., 3] > 128
    band[..., :3][lit] = colour
    a[:top] = band
    return Image.fromarray(a, "RGBA")


def hued(im, hue, pull=HUE_PULL, sat_scale=1.0, mask=True):
    """Paints a piece one hue, keeping the kit's keyline and anything with no hue to move.

    `make_siege_art.hued` with three masks added, and they are what make eight buttons a
    family rather than eight monochrome blobs. A pixel keeps its hue if it is already in the
    keyline's navy band, if it is too grey to have one (GREY_FLOOR), or if it is **darker
    than anything this kit paints a face in** (DARK_FLOOR) — and all three tests are on the
    pixel's *original* value, so asking for a violet button still paints its face violet.

    <b>The darkness mask is the one that matters and it was added the hard way.</b> The hue
    band alone cannot protect a keyline on a source whose *face* is also blue — the pack's
    blue bar sits 0.03 from the navy, so the mask either keeps the keyline and the face
    together (the paint is a silent no-op) or, with the mask off, rotates both and the button
    comes out wearing a red or a violet outline. Eight buttons whose outlines disagree are
    not a kit, and a contact sheet is the only thing that shows it.

    Measured rather than reasoned: `(6, 24, 56)` is 8% luma and the darkest *face* colour in
    the pack is the navy trough at 13%, so a floor between them separates the keyline from
    everything it is drawn around whatever hue either happens to be. That is why every recut
    here can now be rotated off one gold mould with the mask left on.

    The blend stops short of the target for invariant 37p's reason: at 1.0 a face is one flat
    colour and the shading the pack drew into it is gone.
    """
    a = np.asarray(im).astype(np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3:]

    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    span = mx - mn

    with np.errstate(invalid="ignore", divide="ignore"):
        sat = np.where(mx > 1e-6, span / np.where(mx > 1e-6, mx, 1.0), 0.0)
    val = mx

    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    was = np.zeros_like(mx)
    lit = span > 1e-6
    with np.errstate(invalid="ignore", divide="ignore"):
        safe = np.where(lit, span, 1.0)
        was = np.where(lit & (mx == r), ((g - b) / safe) % 6.0, was)
        was = np.where(lit & (mx == g), ((b - r) / safe) + 2.0, was)
        was = np.where(lit & (mx == b), ((r - g) / safe) + 4.0, was)
    was = was / 6.0

    # The two masks. `keep` is the keyline and everything with no hue worth moving.
    #
    # **Off for a piece that is all keyline or all rim.** The mask is right for a control,
    # which is a face inside an outline; it is exactly wrong for a sprite that is nothing but
    # outline — there the mask keeps every pixel and the paint is a silent no-op that comes
    # back unchanged. A rule that protects an outline cannot be asked about a sprite that is
    # nothing else.
    if mask:
        luma = (rgb * np.array([.2126, .7152, .0722], np.float32)).sum(-1)
        keep = (sat < GREY_FLOOR) | (luma < DARK_FLOOR)
    else:
        keep = np.zeros_like(sat, dtype=bool)

    step = ((hue - was + 0.5) % 1.0) - 0.5
    h = np.where(keep, was, (was + step * pull) % 1.0)
    sat = np.where(keep, sat, np.clip(sat * sat_scale, 0.0, 1.0))

    i = np.floor(h * 6.0)
    f = h * 6.0 - i
    p = val * (1.0 - sat)
    q = val * (1.0 - sat * f)
    t = val * (1.0 - sat * (1.0 - f))
    i = i.astype(np.int32) % 6

    # The conditions carry a trailing axis so they broadcast against the three-channel
    # choices; without it `np.select` refuses the pair rather than picking per pixel.
    out = np.select(
        [(i == k)[..., None] for k in range(6)],
        [np.stack([val, t, p], -1), np.stack([q, val, p], -1), np.stack([p, val, t], -1),
         np.stack([p, q, val], -1), np.stack([t, p, val], -1), np.stack([val, p, q], -1)])

    return Image.fromarray(
        (np.concatenate([np.clip(out, 0, 1), alpha], -1) * 255).astype(np.uint8), "RGBA")


def faced(im, radius, top, bottom):
    """Repaints the inside of a disc two-tone, painting whatever glyph was on it out.

    The pack draws its round buttons as a coloured face inside a thick white ring inside the
    navy keyline, and every one of them carries a glyph — a play arrow, a burger, a cross.
    This game's five destinations are none of those, so what is wanted is the *mould* with a
    blank face, exactly as the two kits before this one supplied a blank cap.

    <b>Painted rather than cropped, because the glyph is inside the shape.</b> There is no
    silhouette to trim and no band of rows to flatten (the glyph sits in the middle of the
    face, which is the one place both of those miss) — so the inner disc is simply replaced,
    and the ring, the keyline and the drop shadow the pack drew around it are all untouched.

    <b>Two-tone rather than flat</b>, top over bottom, because that is what every other face
    in this kit is: a lighter half over a saturated half of the same hue. A flat disc here
    reads as a hole cut in the button, which is the one thing a cap may not look like.
    `radius` is a fraction of the disc's own half-width — 0.716 is where the pack's face
    begins, measured along the middle row, so anything at or a hair above that covers it.

    <b>The circle is found, not assumed from the canvas.</b> These are drawn with a drop
    shadow below, so the sprite is 208x219 for a disc 207 across — take the centre as the
    canvas centre and the repaint comes out an ellipse sitting low, which leaves a crescent
    of the pack's own green along the top of every cap. The disc is as wide as the sprite, so
    its centre is half a width below the first opaque row and its radius is half a width.
    """
    a = np.asarray(im).astype(np.float32)
    h, w = a.shape[:2]
    solid = a[..., 3] > 128
    rows = np.where(solid.any(1))[0]
    if not len(rows):
        return im

    half = w / 2.0
    cx, cy = (w - 1) / 2.0, rows[0] + half
    yy, xx = np.mgrid[0:h, 0:w]
    r = np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2) / half

    # A soft edge, or the repaint leaves a stair-stepped rim against the pack's own antialias.
    k = np.clip((radius - r) * half / 1.6, 0.0, 1.0)
    k *= solid

    ramp = np.clip((yy - (cy - radius * half)) / max(1.0, radius * w), 0.0, 1.0)
    paint = (np.array(top, np.float32)[None, None, :] * (1.0 - ramp[..., None])
             + np.array(bottom, np.float32)[None, None, :] * ramp[..., None])

    a[..., :3] = a[..., :3] * (1.0 - k[..., None]) + paint * k[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def saturated(im, by):
    """Pushes a piece's colour out from its own luma, keeping value and alpha.

    Wanted by exactly one piece and worth its own function for that: a blur over flat vector
    art costs saturation as well as edges, so the world comes back a step washed out from the
    picture the pack drew. This is not `hued` with the hue left off — that one *rotates*
    toward a target, and there is no target here; the world keeps every hue it has.
    """
    if by == 1.0:
        return im
    a = np.asarray(im).astype(np.float32)
    lum = (a[..., :3] * np.array([.2126, .7152, .0722], np.float32)).sum(-1, keepdims=True)
    a[..., :3] = lum + (a[..., :3] - lum) * by
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def dimmed(im, by):
    """Takes a piece down in value without touching its alpha or its edges."""
    if by >= 1.0:
        return im
    a = np.asarray(im).astype(np.float32)
    a[..., :3] *= by
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def drained(im, keep=0.10):
    """Takes the colour out of a piece, keeping its value and every edge it had.

    Grey is not a hue, which is why this is not a call to `hued`. A trace of the original is
    kept so a dead control still has the pack's warmth in it rather than reading as a
    screenshot in black and white.
    """
    a = np.asarray(im).astype(np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3:]

    # Rec. 709 luma, so a saturated orange does not come back darker than the grey a viewer
    # would have called its equal.
    lum = (rgb * np.array([.2126, .7152, .0722], np.float32)).sum(-1, keepdims=True)
    out = lum + (rgb - lum) * keep

    return Image.fromarray(
        (np.concatenate([np.clip(out, 0, 1), alpha], -1) * 255).astype(np.uint8), "RGBA")


def plussed(im):
    """Draws the kit's missing "+" onto one of its squares.

    Neither pack has a plus anywhere. It is drawn rather than borrowed from the tower pack's
    because that one is a black-keylined chip and this is a control that sits *inside* a
    readout, touching the trough's cream rim on one side — the one place in this UI where a
    foreign keyline would be a quarter of an inch from one of the kit's own.

    On the face rather than on the sprite's middle: the mould is a face over a base, so a
    glyph centred on the image lands on the moulding. That is `facelift`'s number, used here
    instead of being handed to a call site.

    <b>One outlined polygon, not two outlined bars.</b> Drawing a horizontal rounded bar and a
    vertical one, each with the kit's indigo keyline, puts a keyline straight through the
    middle of the glyph where the two overlap — so what came out was a fat cross with four
    notches bitten out of its waist. A cross is one shape and has to be drawn as one.
    """
    out = im.copy()
    draw = ImageDraw.Draw(out)

    w, h = out.size
    cx, cy = w / 2.0, h * (0.5 - facelift(im))
    arm, thick = w * .215, w * .075

    ring = [(cx - thick, cy - arm), (cx + thick, cy - arm), (cx + thick, cy - thick),
            (cx + arm, cy - thick), (cx + arm, cy + thick), (cx + thick, cy + thick),
            (cx + thick, cy + arm), (cx - thick, cy + arm), (cx - thick, cy + thick),
            (cx - arm, cy + thick), (cx - arm, cy - thick), (cx - thick, cy - thick)]

    draw.polygon(ring, fill=(255, 247, 228, 255))
    draw.line(ring + [ring[0]], fill=KEYLINE + (255,),
              width=max(2, int(w * .045)), joint="curve")
    return out


# ----------------------------------------------------------------------- the drawn two
def beam():
    """The shaft of light the hub's companion stands in.

    Drawn because no pack here has one: the cartoon kit is a sheet of chrome and lights
    nothing, and the tower pack has nothing lit either. A cone rather than a column,
    brightest at the pad and gone by the top, so it reads as light landing rather than as a
    rectangle of colour.
    """
    w, h = 320, 560
    a = np.zeros((h, w, 4), np.float32)

    ys = np.arange(h)[:, None]
    xs = np.arange(w)[None, :]

    # Wide at the foot, narrow at the head, and faded out at both ends so nothing has an
    # edge: a beam with a hard top is a wedge, and a beam with a hard bottom is a lamp.
    half = (0.22 + 0.78 * (ys / (h - 1.0))) * (w * .46)
    across = np.clip(1.0 - np.abs(xs - w / 2.0) / half, 0.0, 1.0) ** 1.7
    down = np.clip(ys / (h * .34), 0, 1) * (1.0 - (ys / (h - 1.0)) * .18)

    a[..., 0], a[..., 1], a[..., 2] = 132, 236, 255
    a[..., 3] = np.clip(across * down * 168.0, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def burst():
    """The starburst a card wears when it is the one worth pointing at.

    Drawn, and white, so a call site can tint it to whatever the mark means — the pack's own
    star is gold at 50 pixels, and a gold star multiplied by `Pal.Rose` is a muddy orange
    rather than a rose seal.

    **Ten points at a shallow waist rather than twelve at a deep one**, because a seal here
    carries a *word*: at .62 the star's flat middle was narrower than "POPULAR" and the render
    showed the caption hanging out over the points on both sides. A seal is a plate first and
    a star second.

    **It wears the kit's keyline, and that is what stopped it being the odd object out.** Every
    other thing on a shop card — the plate, the price, the ribbon, the tabs — is a face inside
    one heavy navy outline, and a bare tinted star beside them read as a sticker from a
    different set. The outline survives the tint because `Image.color` is a *multiply*: navy
    times any colour a call site picks is still navy, where a white keyline would take the
    tint with it and vanish.
    """
    size, points = 256, 10
    mid = size / 2.0
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    ring = []
    for i in range(points * 2):
        ang = i * np.pi / points - np.pi / 2.0
        r = mid * (.99 if i % 2 == 0 else .78)
        ring.append((mid + np.cos(ang) * r, mid + np.sin(ang) * r))

    d = ImageDraw.Draw(out)
    d.polygon(ring, fill=(255, 255, 255, 255))
    d.line(ring + [ring[0]], fill=KEYLINE + (255,), width=11, joint="curve")
    return out


def lander():
    """The plinth the hub's companion stands on.

    <b>Drawn, because every disc any of these packs ships is a coin.</b> The tower pack's
    level-select nodes are the closest thing to a pad in any of them and all nineteen are a
    bright round face inside a gold rim — which is precisely a coin, and at 436 units under a
    companion it reads as one however it is graded. That was tried three ways on the kit
    before this one (gold, teal, dimmed to bronze) and then a fourth here (gold, sunk), and
    the render said "coin" every time. **A shape whose whole silhouette is wrong cannot be
    fixed with colour**, which is this project's own note about the merge kit's coral tab,
    arriving on an object instead of an ornament.

    So it is composed out of the kit's own material instead: a navy two-tone face inside the
    keyline, a gold rim, and a *side* below it — which is the one thing a coin does not have
    and the thing that makes a disc read as something standing on the ground rather than
    lying on it. Gold on navy, so it belongs to the plates rather than competing with them.

    Wide rather than round, because the companion stands on it in a three-quarter view and a
    circle at that angle is an ellipse.
    """
    w, h = 440, 268
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(out)

    key = KEYLINE + (255,)
    rim, side = (247, 190, 42, 255), (150, 96, 12, 255)
    top, bottom = (34, 78, 152, 255), (16, 44, 104, 255)

    # The shadow it sits in, drawn first and softened, so the plinth has somewhere to be.
    cast = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(cast).ellipse([26, 116, w - 26, h - 10], fill=(4, 16, 40, 150))
    out.alpha_composite(cast.filter(ImageFilter.GaussianBlur(14)))

    lift = 34                                   # how tall the side is
    face = [16, 8, w - 16, h - 40 - lift]

    # The side: the same ellipse dropped, in the rim's own colour taken down, so the plinth
    # is one object lit from above rather than two discs stacked.
    d.ellipse([face[0], face[1] + lift, face[2], face[3] + lift], fill=side, outline=key, width=9)
    d.rectangle([face[0], (face[1] + face[3]) / 2, face[2], (face[1] + face[3]) / 2 + lift],
                fill=side)
    d.line([(face[0], (face[1] + face[3]) / 2), (face[0], (face[1] + face[3]) / 2 + lift)],
           fill=key, width=9)
    d.line([(face[2], (face[1] + face[3]) / 2), (face[2], (face[1] + face[3]) / 2 + lift)],
           fill=key, width=9)

    # The gold rim, and the navy face inside it.
    d.ellipse(face, fill=rim, outline=key, width=9)
    inset = 26
    d.ellipse([face[0] + inset, face[1] + inset * .62, face[2] - inset, face[3] - inset * .62],
              fill=bottom, outline=key, width=6)

    # The lighter top half of the face, which is what every other piece in this kit has.
    lit = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ld = ImageDraw.Draw(lit)
    ld.ellipse([face[0] + inset, face[1] + inset * .62, face[2] - inset, face[3] - inset * .62],
               fill=top)
    ld.rectangle([0, (face[1] + face[3]) / 2, w, h], fill=(0, 0, 0, 0))
    out.alpha_composite(lit)

    return out


# --------------------------------------------------------------------------- cutting
def build(packs, pieces):
    made = {}

    for piece in pieces:
        z = packs[piece.pack]
        root = ROOT[piece.pack]

        try:
            im = read(z, root, piece.source)
            if piece.scene:
                # The world is layers rather than a picture: sky, then ground, then the
                # decorations that stand on it, each authored on the same canvas. Composed
                # here rather than taking the pack's own flattened preview, because a preview
                # is a *sheet* — the hard-won note about vendor lettering is about exactly
                # that folder — and because the layers are what let the crop be chosen.
                for layer in piece.scene:
                    im.alpha_composite(read(z, root, layer))
        except KeyError:
            sys.exit(f"{piece.name}: {piece.source} is not in the {piece.pack} pack")

        if piece.trim:
            # The pack pads every artboard with transparent margin — a 400x166 bar arrives on
            # a 466x216 canvas. Left on, the margin becomes part of the sprite, so a
            # nine-sliced plate carries invisible padding on all four sides and every call
            # site's size is quietly wrong by the difference.
            box = im.getbbox()
            if box is None:
                sys.exit(f"{piece.name}: nothing opaque to trim to")
            im = im.crop(box)
        if piece.crop:
            im = im.crop(piece.crop)
        if piece.flatten:
            im = flattened(im, *piece.flatten)
        if piece.flip:
            im = im.transpose(Image.FLIP_TOP_BOTTOM)
        if piece.resize:
            im = im.resize(piece.resize, Image.LANCZOS)
        elif piece.zoom != 1.0:
            im = crisp(im.resize((max(1, int(round(im.width * piece.zoom))),
                                  max(1, int(round(im.height * piece.zoom)))), Image.LANCZOS),
                       piece.zoom)
        if piece.blur:
            im = im.filter(ImageFilter.GaussianBlur(piece.blur))

        if piece.face:
            im = faced(im, *piece.face)
        if piece.well:
            im = welled(im, *piece.well)
        if piece.hue is not None:
            im = hued(im, piece.hue, piece.pull, piece.sat, piece.mask)
        elif piece.sat != 1.0:
            # Saturation without a hue is a different question and is not `hued` with the
            # target left off — see `saturated`. Reachable only when no hue is asked for, so
            # a piece cannot silently apply it twice.
            im = saturated(im, piece.sat)
        if piece.grey:
            im = drained(im)
        im = dimmed(im, piece.dim)
        if piece.plus:
            im = plussed(im)

        border = (0, 0, 0, 0)
        if piece.slice_x or piece.slice_y:
            if piece.border is not None:
                # Typed in source pixels, so it follows `zoom` rather than going stale.
                left, bottom, right, top = (int(round(v * piece.zoom)) for v in piece.border)
            else:
                measured = corner(np.asarray(im)[..., 3])
                if measured is None:
                    sys.exit(f"{piece.name}: nothing opaque to measure a border from")
                left, bottom, right, top = measured

            if not piece.slice_x:
                left = right = 0
            if not piece.slice_y:
                bottom = top = 0
            border = (left, bottom, right, top)

            if piece.slice_x and left + right >= im.width:
                sys.exit(f"{piece.name}: a corner of {border} leaves no middle to stretch "
                         "across - it is not a rounded rectangle, so it cannot be sliced")
            if piece.slice_y and bottom + top >= im.height:
                sys.exit(f"{piece.name}: a corner of {border} leaves no middle to stretch "
                         "down - it is not a rounded rectangle, so it cannot be sliced")

            # **A sunk plate's ramp has to finish inside its border**, and getting that wrong
            # is not subtle. `welled` measures from the sprite's own edge, so the shading is
            # baked per pixel; a nine-slice then keeps the corners and stretches the middle.
            # If the ramp crosses the border, the corner pieces carry a *rounded* inset and
            # the stretched middle carries a straight one, and where they meet the plate wears
            # a step. Both boards shipped that on the first cut - notches at all four corners
            # of every card on the hub - and no measurement anywhere disagreed with it; the
            # render is what saw it. Inside the border the ramp lives entirely in the pieces
            # that are never stretched, and the middle is one flat colour.
            if piece.well:
                edge = min([v for v, on in ((left, piece.slice_x), (right, piece.slice_x),
                                            (bottom, piece.slice_y), (top, piece.slice_y))
                            if on] or [0])
                reach = piece.well[0] + (piece.well[2] if len(piece.well) > 2 else 5.0)
                if reach > edge:
                    sys.exit(f"{piece.name}: sunk {reach:.0f} pixels in on a border of {edge} "
                             "- the ramp crosses the slice, so the corners and the stretched "
                             "middle will disagree and the plate will wear a step")

        made[piece.name] = (im, border)

    return made


def meta_for(name, border):
    """The importer settings for one sprite: this project's own, with a measured border.

    The guid is derived from the name rather than random, so the tool is reproducible and
    `--check` means something — and so that a second kit wearing these names keeps the
    addresses the first one registered. Unity only asks that it be unique.
    """
    text = TEMPLATE.read_text(encoding="utf8")
    guid = hashlib.md5(("glimmer.ui.hud." + name).encode("utf8")).hexdigest()

    out = []
    for line in text.splitlines(keepends=True):
        if line.startswith("guid: "):
            out.append(f"guid: {guid}\n")
        elif line.strip().startswith("spriteBorder:"):
            out.append("  spriteBorder: {x: %d, y: %d, z: %d, w: %d}\n" % border)
        else:
            out.append(line)
    return "".join(out)


def rebordered(meta, border):
    """The sprite's existing importer settings with one line changed.

    A re-cut keeps its `.meta` — guid and all — because Addressables keys every registered
    entry on the guid, and a fresh one orphans the address rather than moving it: the game
    still asks for `Ui/btn_green`, nothing answers, and what ships is a white rectangle
    (invariant 7b). What has to move is the nine-slice border, because the new art's corner
    is not the old art's corner.
    """
    out = []
    seen = False
    for line in meta.read_text(encoding="utf8").splitlines(keepends=True):
        if line.strip().startswith("spriteBorder:"):
            out.append("  spriteBorder: {x: %d, y: %d, z: %d, w: %d}\n" % border)
            seen = True
        else:
            out.append(line)

    if not seen:
        sys.exit(f"{meta.name}: no spriteBorder line to set - a sprite this tool re-cuts "
                 "must already be a Sprite importer")
    return "".join(out)


# --------------------------------------------------------------------------- looking
def contact(made, path, cell=250):
    """Every piece at a common size, on the room's own colour.

    Look at it. `--check` proves a sprite reproduces and says nothing at all about whether it
    is the right sprite, which is the distinction that shipped four broken shop cards.
    """
    names = sorted(made)
    cols = 6
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 20)), (17, 30, 45))
    draw = ImageDraw.Draw(sheet)

    for i, name in enumerate(names):
        im = made[name][0]
        s = min((cell - 18) / im.width, (cell - 18) / im.height)
        one = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)
        x = (i % cols) * cell + (cell - one.width) // 2
        y = (i // cols) * (cell + 20) + (cell - one.height) // 2
        sheet.paste(one, (x, y), one)
        b = made[name][1]
        draw.text(((i % cols) * cell + 4, (i // cols) * (cell + 20) + cell + 4),
                  f"{name} {im.size} {b}", fill=(180, 205, 235))

    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


# --------------------------------------------------------------------------- entry
def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    ap.add_argument("--extra", type=Path, default=EXTRA_SOURCE,
                    help="the second pack folder; both are searched, in order")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", type=Path, metavar="PNG")
    args = ap.parse_args()

    roots = [args.source, args.extra]
    packs = {"cartoon": found(roots, CARTOON), "world": found(roots, WORLD)}
    if any(v is None for v in packs.values()):
        # Passing when the packs are absent is deliberate: a checkout without them still runs
        # the gate, exactly as `make_siege_art.py` and `make_village_art.py` do.
        missing = [n for n, v in packs.items() if v is None]
        print(f"the {', '.join(missing)} pack(s) are not in "
              f"{' or '.join(str(r) for r in roots)} - nothing to do")
        return

    made = build(packs, KIT)
    made["beam"] = (beam(), (0, 0, 0, 0))
    made["burst"] = (burst(), (0, 0, 0, 0))
    made["lander"] = (lander(), (0, 0, 0, 0))

    recut = build(packs, RECUT)
    if not args.check:
        OUT.mkdir(parents=True, exist_ok=True)

    stale = []

    def emit(png, meta, img, text, label):
        buf = io.BytesIO()
        img.save(buf, "PNG", optimize=True)
        data = buf.getvalue()

        if args.check:
            if not png.exists() or png.read_bytes() != data:
                stale.append(label)
            elif not meta.exists() or meta.read_text(encoding="utf8") != text:
                stale.append(label + " (meta)")
            return

        png.write_bytes(data)
        meta.write_text(text, encoding="utf8")
        print(f"  wrote {label}  {img.width}x{img.height}")

    for name, (img, border) in sorted(made.items()):
        emit(OUT / f"{name}.png", OUT / f"{name}.png.meta", img,
             meta_for(name, border), f"Ui/Hud/{name}.png border {border}")

    for name, (img, border) in sorted(recut.items()):
        png = SHARED / f"{name}.png"
        meta = SHARED / f"{name}.png.meta"
        if not meta.exists():
            sys.exit(f"{name}: nothing to re-cut - this list replaces sprites the game "
                     "already names, so a missing .meta means a name that does not exist")
        emit(png, meta, img, rebordered(meta, border), f"Ui/{name}.png border {border}")

    if args.contact:
        contact({**made, **{k + " *": v for k, v in recut.items()}}, args.contact)
        print(f"  wrote {args.contact}  - look at it; --check cannot")

    if args.check:
        if stale:
            sys.exit("stale, re-run without --check: " + ", ".join(stale))
        print("the HUD kit is what the tool would write "
              f"({len(made)} sprites, {len(recut)} re-cut in place)")
    else:
        # The two numbers `UIKit` has to carry, measured here rather than guessed there.
        for role, source in MOULDS:
            print("  %-6s face lift %.4f   (UIKit.%sFaceLift)"
                  % (role, facelift(read(packs["cartoon"], ROOT["cartoon"], source)),
                     "Pill" if role == "pill" else "Square"))


if __name__ == "__main__":
    main()
