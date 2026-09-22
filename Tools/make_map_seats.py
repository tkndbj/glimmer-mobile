#!/usr/bin/env python3
"""
Where a level node **stands** on each of the four map paintings.

    python Tools/make_map_seats.py --check        # the shipped table reproduces
    python Tools/make_map_seats.py --contact      # draw every map with its nodes on it
    python Tools/make_map_seats.py --write        # rewrite the table in chapters/mapart.py

## Why this exists

A node used to stand on a **perch** — a floating island tile with a shadow under it — and
that is what let its position be a shared serpentine table (`mapart.XS`/`YS`): a tile brings
its own ground, so it does not matter what the painting has underneath. Every one of the four
paintings draws a path, and the serpentine ignored all four: bare discs at those coordinates
land in a river, on a rooftop, in a lake and off the side of a cliff. The perch was hiding it.

Standing a node **on the ground the painting already draws** is the look the packs are sold
with, and it costs exactly this tool: the ground has to be *found* rather than assumed, because
`mapX`/`mapY` are authored numbers and a painting is a picture nothing else in this project
opens (invariant 32b).

So a seat is derived from the drawing, which is the only shape in which what is drawn and what
is played cannot disagree (invariant 33g). The y ladder is kept — it is what every clearance
rule in `ChapterMap` was tuned against — and what this finds is **where across the map the
ground is** at each rung, with the old serpentine surviving as a *preference* rather than as
the answer, because all four paths zig-zag anyway.

## What it cannot do

It cannot tell you whether a seat *reads*. `--contact` is the gate that matters: a disc can sit
on perfectly good ground and still be lost against it, and no number here can see that.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

REPO = Path(__file__).resolve().parent.parent
MAP_ART = REPO / "Assets" / "Game" / "Art" / "Map"
MAPART_PY = REPO / "Tools" / "chapters" / "mapart.py"

sys.path.insert(0, str(REPO / "Tools"))
from chapters import mapart  # noqa: E402

# ---------------------------------------------------------------- mirrored geometry
# `ChapterMap`, in canvas units. Mirrored rather than imported for the same
# reason: this runs with no Unity anywhere. `Tools/verify/content.py` is what holds the
# shipped table to the real rules; these are here so a seat is not *proposed* in a place
# the validator is only going to refuse.
WIDTH = 1080.0
STRIP_HEIGHT = 1200.0
NODE_DIAMETER = 196.0
MIN_SEPARATION = NODE_DIAMETER + 24.0        # ChapterMap.MinimumNodeSeparation
CROWN_HALF, CROWN_BOTTOM, CROWN_TOP = 204.0, -52.0, 302.0   # the record badge stands beside
BODY_HALF, BODY_BELOW, BODY_ABOVE = 180.0, 227.0, 118.0
TEASER_X = 0.66
TEASER_GAP = 0.22
TEASER_HEADROOM = 700.0

#: Decimal places a seat is written to, which is also the precision it is judged at.
PRECISION = 3

#: Hand corrections to what the search finds, in **canvas units**, per map and per rung.
#:
#: **This is the one authored thing in this file and it is deliberately not a way of placing a
#: node.** The search decides where the road is; an eye decides which *part* of the road a node
#: looks right on, and no measurement answers that — a seat can be dead centre of a path and
#: still sit under a tree, behind a name plate, or a little short of the bend the chain wants
#: to turn at. Every entry here came from somebody looking at the map on a phone and saying
#: which way to move it.
#:
#: Two rules keep it honest. **A nudge is small**: it shifts a seat along ground the search
#: already found, it does not put one somewhere the search refused, and every nudged seat is
#: still checked against `ChapterMap`'s clearance rules afterwards — a nudge that collides is
#: refused, loudly, rather than shipped. And **it is per map rather than per chapter**, like
#: the seats themselves, because a map is drawn by every mode's chapter at that ordinal.
#:
#: A **borrowed** map (`BORROWS`) nudges the chain it borrowed, after the transfer and before
#: its footing is reported, so what is checked and what is printed are both the seats that
#: ship. It is the one case where a nudge moves a seat this picture's own search did not find
#: — nothing found it, the borrow asserted it — which is why the two maps' entries are kept
#: apart: moving the wasteland must never move the crag.
#:
#: Rungs are 1-based, matching the numbers a player reads on the map. `"marker"` is the
#: end-of-chapter signpost.
NUDGE = {
    1: {
        1: (-113, 554),       # clear of the bar, and clear of the left edge
        2: (330, 148),        # up the current, not off it
        5: (-60, 90),
        7: (0, 170),
        8: (0, -110),
        9: (-95, 0),
    },
    2: {
        4: (-157, 0),         # makes room for 15 to come left
        6: (-119, 0),         # ...and 6 has to come with it
        5: (-194, 0),         # level 15
        7: (119, 0),          # makes room for 18 to go right
        8: (235, 0),          # level 18
        "marker": (0, 0),     # see `nudged`: the road at its height is out of reach
    },
    3: {
        1: (0, 45), 2: (0, 90), 3: (0, 42),
        4: (-90, 0), 5: (0, -45), 6: (-70, 0),
        7: (0, -32), 8: (-101, 0), 9: (60, 0), 10: (0, -90),
        "marker": (80, 0),
    },
    5: {
        # **Authored by the owner in centimetres, off a phone**, and converted at **167 canvas
        # units to the centimetre**: `Boot` scales the canvas on width alone
        # (`matchWidthOrHeight = 0`) against a reference width of `ChapterMap.Width`, so 1080
        # units *is* the screen whatever the phone, and a 6.1" screen is 6.45 cm across (Pixel 8
        # 6.41, Galaxy S23 6.45, iPhone 15 Pro 6.51). Down is -y.
        #
        # **Every one of these is inward, and the chain was already at the inward limit** - see
        # `ACCEPTED_OVERLAPS` for what that costs and who decided to pay it.
        #
        # The second round is in **pixels**, off the phone, and a pixel is a canvas unit here for
        # the same reason a centimetre is 167 of them: 1080 units is the screen. They are folded
        # into the figures below rather than kept as a second column, because a seat is one
        # number and a running total of corrections is a thing that can disagree with itself.
        #
        # The foot's 100 is not the owner's and is kept: this is a siege chapter's map and the
        # loadout bar stands in the bottom of it (see `EDGE_MARGIN`).
        1: (-261, 100),      # level 41, 1.5 cm left then 10 px further
        2: (167, 0),         # level 42, 1 cm right
        4: (281, 0),         # level 44, 1.5 cm right then 15 px, then 15 px more
        5: (-180, 0),        # level 45, 1 cm left then 5 px, then 8 px more
        6: (355, -167),      # level 46, 2 cm right then 10 px twice over, and 1 cm down
        7: (-114, 0),        # level 47, 0.5 cm left then 15 px twice over
        9: (-187, 10),       # level 49, 1 cm left then 10 px twice over, and 10 px up
        10: (335, 0),        # level 50, 2 cm right
    },
    4: {
        1: (-33, 58),         # level 31
        2: (70, 0),
        3: (-15, -43),
        4: (526, 60),         # level 34
        5: (-557, 50),        # level 35
        6: (293, 0),
        7: (102, 14),
        8: (5, 50),
        9: (-9, 0),
        10: (0, 0),            # right is blocked by the marker’s own mark
    },
    6: {
        # **The owner's, in pixels, off a phone**, and a pixel is a canvas unit for `map5`'s
        # reason: `Boot` scales the canvas on width alone against a reference width of
        # `ChapterMap.Width`, so 1080 units *is* the screen whatever the phone. Down is -y.
        #
        # `map6` **borrows** `map5`'s chain (`BORROWS`) and then moves it, and that is the whole
        # reason a borrowed map applies nudges at all. The two paintings are drawn to one plan,
        # so the chain transfers; but the wasteland's slabs do not fall exactly where the crag's
        # do, and which part of a mesa a disc looks right on is the question no measurement in
        # this file answers.
        #
        # **Eight rounds, folded into one figure each**, for `map5`'s reason: a seat is one
        # number, and a running total of corrections is a thing that can disagree with itself.
        # Only the sum is kept; the rounds survive in the comments because they are the record
        # of an eye converging, not a second source of truth.
        #
        # **They are large, and that is the borrow being paid for rather than a nudge being
        # abused.** Rung 7 is now better than a quarter of the screen from where the crag stands
        # it: `map5`'s road runs up the middle and the wasteland's crosses from mesa to mesa, so
        # a transferred chain is right about the *route* and wrong about the ledge, by about the
        # width of a plateau. `GROUND[6]` cannot say so (it lists the sand, not the trail), which
        # is why an eye is doing it. **At this size the borrow has stopped paying**: re-list
        # `GROUND[6]` as the trail and let the search seat this map on its own picture, and most
        # of this table goes with it.
        #
        # **Every round has moved the same way**, which is a scale error rather than indecision:
        # a figure read off a phone is in *device* pixels, and a canvas unit is only a device
        # pixel on a 1080-wide screen. On a 1440-wide one a 40 px move arrives as 30.
        1: (-45, 0),          # level 51, 10+10+10+15 px left
        2: (90, 0),           # level 52, 15+20+20+15+20 px right
        3: (58, 20),          # level 53, 8+10+10+15+15 px right, and 20 px up
        4: (190, 0),          # level 54, 30+40+40+30+50 px right
        5: (-8, 0),           # level 55, 4 px left twice over
        6: (15, 0),           # level 56, 5 px right three times over
        7: (-305, 0),         # level 57, 30+40+40+40+80+30+30+15 px left
        8: (170, 0),          # level 58, 20+20+20+20+40+30+20 px right
        9: (15, 0),           # level 59, 5 px right three times over
        10: (-85, 0),         # level 60, 10+15+15+15+20+10 px left
    },
    7: {
        # **The dead lands draw no road at all**, and that is what these are for. `map1` to
        # `map6` each paint a way across themselves - a path, a trail, a line of stepping slabs
        # - so `GROUND` can be read as *the route* and the search's answer is already most of
        # the way to a chain that reads like one. This painting is a stack of floating plateaus
        # joined by rope bridges: the only thing under a node is rock, every part of a plateau
        # top is as good as every other part by every number in this file, and **which part of
        # it a node looks right on is the whole question**. That is invariant 8g's second half
        # doing all the work rather than some of it.
        #
        # So each of these takes its rung to the **middle of the open rock on the plateau its
        # rung belongs to**, read off the distance transform of the seatable mask - the point
        # furthest from any edge, crack or drop - and then off a picture. Three of them are
        # about a *prop* rather than about the ground: rung 9 stood on the crystal outcrop
        # (and, once the fissures were healed, on the bare rim to the right of it), rung 1 was
        # drawn through the great skull at the foot of the map, and the marker stood on rung
        # nine's own record mark.
        #
        # **They are large because the search's answer is an edge, not because a nudge is being
        # abused.** `candidates` keeps the two *ends* of every row it looks at, which is exactly
        # right on a road - both ends of a road are road - and exactly wrong on a plateau, where
        # both ends are the lip. Every entry here moves inward.
        1: (-118, 91),        # level 61, off the great skull and onto the rock beside it, then 40 px left
        2: (48, -29),         # level 62, 80 px right
        3: (19, 154),         # the right lobe's own middle, not its rim, then 30+40 px left
        4: (318, 130),        # level 64, 120+120+40+60 px right
        5: (-323, 134),       # level 65, 90+120+60+30 px left and 100 px up
        6: (55, 32),          # the foot of the bridge, which is what the plateau is for, then 40 px down
        7: (23, -30),         # level 67, 30 px down
        8: (-119, 5),         # level 68, 20 px right
        9: (-407, -125),      # off the crystal outcrop and onto the open top west of it, then 40 px left
        10: (68, -5),
        "marker": (138, 0),   # see `ACCEPTED_OVERLAPS`: this is what stopped being impossible
    },
}


#: Maps whose nudges the owner has signed off **knowing they overlap**, so the check above says
#: so rather than refusing.
#:
#: `map5` is the only entry and it is not a loophole, it is a decision with a name on it. Its
#: painting draws one road, straight up the middle, and the owner wants the chain on the road.
#: `ChapterMap`'s crown rule says two nodes less than 384 units apart across the map need 529 of
#: drop between them, and this map's rungs are 278-509 apart - so a chain on a central road is a
#: chain whose plates sit on each other's record-and-rank marks, and there is no cut of this
#: painting that is both tall enough to fix it and wide enough to still be the place. Asked for
#: twice, in those terms, and confirmed. **The build gate still says it** (`ChapterMapValidator`
#: warns), which is the right place for it to be said: this file's job is to stop a nudge
#: colliding *by accident*.
#:
#: `map6` is here only because it **borrows** `map5`'s chain (`BORROWS`), so it inherits that
#: chain's overlaps along with its seats — and goes on inheriting them once its own `NUDGE`
#: entries have moved eight of the ten, because those moves are tens of units against a rule
#: measured in hundreds. It is the same decision, not a second one.
#: **`map7` was the third entry and is not one any more, and that is worth keeping written
#: down.** Its marker provably could not be placed: a marker's height is not ours (`nudged`),
#: and at the ceiling it stood 125 units above rung ten and 394 above rung nine, both under the
#: 529 of drop the crown rule wants - so it had to clear *both* by 384 units across the map,
#: and those two rungs were themselves 529 apart on opposite sides. A point 384 from each of
#: two points 529 apart needs 1152 units of map and this one is 1080 wide. The arithmetic was
#: right and the *premise* was not: rungs nine and ten were where they were because the search
#: had almost nowhere to put them (see `HEAL`), and once this painting's fissures stopped being
#: read as holes in the world the chain could be stood on the plateau tops instead, which put
#: rung ten at 0.343 and left the marker the whole right-hand half of its plateau. It ships
#: with **no** warning now, against `map5`'s and `map6`'s nine each. **An impossibility proof is
#: only ever as good as the inputs it is run on.**
#:
#: **`map7` is back, and it is here as named rungs rather than as a whole map, which is the
#: difference that matters.** `True` accepts anything this picture's chain ever does; a set
#: accepts exactly the rungs somebody looked at and signed off, and every other collision on the
#: same map is still refused loudly. These eight are the owner's own placement of this chapter's
#: chain on 2026-09-21, moved a round at a time off a phone. Four pairs stand inside the crown
#: rule: 61/62, 63/64, 64/65 and 65/66 - and **64 and 65 are inside the disc rule too**, 209
#: units between centres against a 220 minimum, which is the two discs touching rather than one
#: plate reaching over the other's record mark. Every one of them is where it was asked to be.
ACCEPTED_OVERLAPS = {
    5: True,
    6: True,
    7: {1, 2, 3, 4, 5, 6, 8, 9},
}


def _overlap_accepted(which: int, rung: int) -> bool:
    """Whether this map has signed off *this* rung standing on a neighbour. See above."""
    accepted = ACCEPTED_OVERLAPS.get(which)
    return accepted is True or (accepted is not None and rung in accepted)


#: Maps that stand their chain exactly where another map's chain stands, rather than searching
#: their own picture for one.
#:
#: **One entry, and it is the owner's call rather than a shortcut.** `map5` (the lava crag) and
#: `map6` (the wasteland mesas) are the two `|`-joined sources cut at four strips, and they are
#: drawn to one plan: a route up the middle of a stack of plateaus, crossing from one to the
#: next on a bridge. So the chain that follows `map5`'s road follows `map6`'s too, and it was
#: looked at before it was believed (`--contact`, which is the gate here).
#:
#: **A borrow is a starting point rather than a copy.** `map6`'s own `NUDGE` entries are applied
#: on top of the transferred chain, so the two maps no longer stand their nodes in the same ten
#: places; what they share is the route, which is what was decided here.
#:
#: **What the search made of `map6` on its own is why.** `GROUND[6]` lists the map's two sand
#: planes rather than its road, for the reason written there - the road is a *scatter* of
#: stepping slabs and `DESPECKLE` throws a scatter away - so on this one painting the whole
#: mesa top is seatable and nothing tells the search where the route is. It therefore fell back
#: on `XS`, the serpentine, and the serpentine put four of the ten rungs out on side mesas the
#: trail never reaches: a node on good ground, by every number in this file, and off the way.
#: That is invariant 8e's fault in the one shape this tool cannot measure its way out of.
#:
#: **And it is paid for in the check, not hidden from it.** Because `GROUND[6]` is the sand, a
#: seat on the slabs or on a bridge deck is *off* ground by that list, so `borrowed` reports
#: most of the ten - seven of them today - as seats this map's own footing test would refuse,
#: loudly, every run, rather than quietly passing. The count moves when a nudge moves, which is
#: the report doing its job rather than a figure to hold anything to. That report is the honest
#: state: the numbers say no, the picture says yes, and the picture was looked at. Re-list
#: `GROUND[6]` as the road and this whole entry can go.
BORROWS = {6: 5}


def nudged(which: int, rung, seat):
    """
    A seat with its authored correction applied, still rounded to what ships.

    **A marker may only be moved across the map, never up or down**, and that is not a rule of
    taste: its height is derived by `ChapterMap.TeaserPosition` from the highest glade, and a
    chapter body can author only its `teaserX`. A vertical nudge on one would move it in this
    tool's picture and nowhere else - a mirror telling a comfortable lie about the screen, which
    is the one thing a mirror may never do (44d).
    """
    dx, dy = NUDGE.get(which, {}).get(rung, (0, 0))
    if rung == "marker":
        dy = 0
    if not dx and not dy:
        return seat
    return (round(min(1.0, max(0.0, seat[0] + dx / WIDTH)), PRECISION),
            round(min(1.0, max(0.0, seat[1] + dy / (STRIP_HEIGHT * mapart.STRIPS[which]))),
                  PRECISION),
            seat[2])

#: The colours each painting draws **ground a node may stand on**, and the one thing in this
#: file that cannot be derived: a road, a trail, a stone shelf and an island top are the same
#: idea drawn four ways, and only somebody looking at the picture can say which is which.
#:
#: `map1` looked like the odd one and is not: it draws paths too, cut across its island tops in
#: a darker shade, and they are as much a road as `map2`'s. What it also draws — and no other
#: map does — is a **current** in the water between the islands, which is `STREAM` below.
#: Its cliff faces are deliberately absent, and they are the reason `TOLERANCE` is as tight as
#: it is: the teal of a forest island's path and the teal of that island's own side are **26
#: apart**, so at the tolerance this file shipped with they were one colour and a seat could be
#: put on a vertical rock face. The other three draw a road the player is meant to read as
#: a route, so their ground is that road and deliberately **not** the meadow either side of it:
#: a node in the middle of a field is on ground and still off the way.
GROUND = {
    1: [(124, 120, 39), (137, 135, 43), (83, 145, 138)],
    2: [(199, 243, 146), (213, 253, 153)],
    3: [(171, 161, 99), (163, 151, 93)],
    4: [(124, 89, 100), (234, 163, 87)],
    5: [(67, 77, 89), (63, 72, 84)],

    # `map6` is the wasteland, and what is listed is its **two sand planes** rather than its
    # road, for `map5`'s reason: the road here is a scatter of orange stepping slabs
    # (224, 139, 65) laid *on* the sand, and a scatter is speckle - `DESPECKLE` would throw most
    # of it away and what survived would seat a node on a single stone. The sand is what the
    # stones are laid on, so a node stands on the mesa the trail crosses. The two planes are the
    # lit top (253, 221, 138) and the tier below it (246, 203, 109); they are 29 apart in blue,
    # so at this map's tolerance they are two colours and both are wanted. The cliff *faces* -
    # (212, 153, 66) and the browns around it - are 41 or more from either and stay out, which
    # is what stops a node hanging on the side of a mesa.
    6: [(253, 221, 138), (246, 203, 109)],

    # `map7` is the dead lands, and what is listed is the **lit top faces of its plateaus** -
    # `map5`'s and `map6`'s answer for the third time, and for a third reason. This painting
    # draws no road at all: its chain is a stack of floating slabs with rope bridges between
    # them, so the only thing a node can stand on is the rock itself. The two tops are the
    # broad table (70, 97, 114) and the pale slabs laid on it (86, 121, 143); they are 29
    # apart in blue, so at this map's tolerance they are two colours and both are wanted. The
    # **cliff faces** below them are (45, 51, 58) - 46 clear of the nearer top - and stay out,
    # which is what keeps a node off the side of a plateau where the drop begins.
    7: [(70, 97, 114), (86, 121, 143)],
}

# `map5` is the one painting here whose **road cannot be used**, and that is a fact about the
# picture rather than a preference. Its path is a chain of dark stepping slabs laid across the
# crag, and a slab's own top runs (24, 30, 38) to (39, 46, 53) while the cliff faces beside it
# are (33, 39, 45) - three apart from the middle of that gradient, which is `map1`'s teal
# failure again with no tolerance small enough to separate them. So what is listed is the
# **lit top face of the crag**, (67, 77, 89), which is 38 clear of both and is the surface the
# slabs are laid on: a node stands on the rock the path runs over rather than on the path.

#: Where a node may stand **on the water**, and the whole of why `map1` is not like the other
#: three.
#:
#: It is an archipelago, so a chain that only ever stands on land is a chain with three clumps
#: in it and two long silences — and the painting already answers that, because it draws a pale
#: **current** running the whole height of the map between the islands. A node on the current is
#: standing on nothing, so it gets a **perch** back: the floating tile every node used to have,
#: kept for the one map whose picture asks for it.
#:
#: Empty for the three road maps, and that emptiness is the rule rather than an omission: a road
#: map is a continent, there is nowhere on it a node would be afloat, and a perch drawn on one
#: would be a tile hovering over solid ground — which is the whole fault this was fixed to end.
STREAM = {
    1: [(153, 217, 237), (73, 187, 223), (72, 186, 223)],
    2: [],
    3: [],
    4: [],
    5: [],
    6: [],
    7: [],
}

#: What each painting draws **instead of land**: sea, sky, lake, chasm, void.
#:
#: The second of two questions a seat is asked, and a different one. `GROUND` asks whether the
#: disc's own footprint is clear — road, not tree — and that is all three road maps need,
#: because a road is surrounded by meadow and a node can never hang off the side of one.
#: `map1` is an archipelago and can: an island's grass is `GROUND`, its palms are not, and its
#: palms are most of it, so asking `GROUND` how big a landmass is reported the map's own
#: islands as too small to stand on while the bare rocks in the sea between them passed.
#:
#: So the wide test asks the only question it was ever really asking — **is there land all
#: round this, or is it an edge** — and land is everything that is not one of these. The three
#: road maps list their water anyway, because a node on a bridge is the same fault.
#:
#: **It is subtracted from `GROUND` as well**, which is what makes one list do both jobs.
#: `map4`'s lava is within `TOLERANCE` of its own orange stone — the pack painted them from
#: one palette — so the first cut of this seated the opening node of a chapter in a pool of
#: molten rock, on ground, by every number in this file.
VOID = {
    1: [(26, 154, 198), (39, 170, 225), (153, 217, 237), (73, 187, 223), (72, 186, 223),
        (211, 248, 250), (234, 253, 249)],
    2: [(0, 174, 239), (122, 213, 246), (234, 253, 249)],
    3: [(0, 174, 239), (122, 213, 246), (234, 253, 249)],
    4: [(251, 176, 64), (247, 148, 29), (255, 242, 0), (234, 253, 249)],
    5: [(255, 223, 87), (240, 150, 30), (202, 108, 9)],

    # `map6`'s void is sky, and it is the widest gradient of any map here: the upper board opens
    # at a near-black navy and the lower one ends in a pale cyan, so four stops are needed to
    # close it at `VOID_TOLERANCE`. None of them is within reach of sand or stone, which is the
    # one thing this list has to be true of (`map4`'s lava is the counter-example).
    6: [(12, 24, 42), (58, 130, 166), (87, 195, 241), (122, 212, 244)],

    # `map7`'s void is a flat night sky with a maroon nebula and olive mist drawn through it,
    # and one stop closes the lot: (12, 23, 41) at `VOID_TOLERANCE` reaches the nebula's
    # (56, 37, 50) and the shadowed navy (25, 34, 51) without coming within 58 of either
    # ground colour. It also takes in the **cliff faces** (45, 51, 58), which is wanted rather
    # than tolerated on this painting: a plateau's side is where the drop starts, so the broad
    # "is there land all round" test should refuse a seat that hangs over one. **And it takes
    # in the fissures painted across the tops, which is not wanted at all and cannot be argued
    # away in colour** — see `HEAL`, which closes them by shape instead.
    7: [(12, 23, 41)],
}

# `map4`'s own shadow deliberately does not appear above, and that is the correction worth
# keeping. It is a volcanic map drawn almost entirely in four near-blacks, and listing the
# background ones as void cut its continent into a hundred pieces that `MIN_LANDMASS` then
# threw away - so the tool reported nowhere to stand on a map that is nothing but standing
# room. A road map is a continent; what it needs from this table is the lava, so that a
# molten pool is not read as the orange stone the pack painted it from.

#: How far a colour may be from one in a list and still count, per channel — **per painting,
#: because how close two of its colours are is a fact about the painting**.
#:
#: 26 everywhere except `map1`, and that exception is measured rather than chosen: the teal of
#: a forest island's *path* and the teal of that island's own *cliff face* are exactly 26
#: apart, so at the shared figure they are one colour and a quarter of what reads as path is
#: the vertical side of an island. At 12 they do not touch at all.
#:
#: **It cannot simply be tightened everywhere**, which is what the first attempt did. `map4` is
#: a volcanic map whose road is painted in half a dozen browns that shade into each other, and
#: at 12 its seatable ground fell from 8.1% to 5.4% and it could not seat ten nodes at all.
#: A flat-colour map wants a tight tolerance and a shaded one wants a loose one; there is no
#: number that is right for both.
TOLERANCE = {1: 12, 5: 12}
DEFAULT_TOLERANCE = 26


def tolerance_of(which: int) -> int:
    """The colour tolerance this painting is read at."""
    return TOLERANCE.get(which, DEFAULT_TOLERANCE)

#: The same, for `VOID`, and it has to be wider.
#:
#: A road is flat colour with a hard edge; open water is a *gradient* — `map1` paints its sea
#: in five listed blues and every shade between them, and at the road's tolerance the shades
#: in between came out as land. That left hairline threads of "land" running right across the
#: sea, which merged every island and every stepping stone into **one** connected region of
#: 1.35 million pixels — so `MIN_LANDMASS` was measuring the whole map and threw nothing away,
#: and the last glade of the first chapter in the game was seated on a rock in open water.
#:
#: Wide enough to close those gradients and still nowhere near any of the greens. One figure
#: rather than a table: a void list is sea, sky and lava, and none of those is ever a hair from
#: something a node may stand on.
VOID_TOLERANCE = 48

#: Speckle smaller than this many pixels across is not ground — it is a pebble, a flower or
#: the gap between two dashes of a dotted trail.
DESPECKLE = 9

#: How wide a crack a painting draws **on** its own ground, in pixels — and the one figure in
#: this file that is a fact about a drawing *style* rather than about a colour.
#:
#: `DESPECKLE` closes the gap between two dashes of a dotted trail, and nine pixels is the size
#: of that gap on a road map. `map7` is not drawn that way. Its plateau tops are lit rock with
#: **fissures painted across them** — forty pixels wide, running the whole width of a table, and
#: drawn in (45, 51, 58), which is the colour of the cliff *faces*. Nothing about the colour can
#: separate the two, and nothing ever will: that same (45, 51, 58) is **fifteen** from this
#: painting's own nebula (44, 36, 51), so there is no `VOID_TOLERANCE` that keeps the sky out
#: and the fissures in. A crack is rock you can stand on; so it is closed by **shape** instead,
#: on the ground mask and on the land mask alike, before anything is measured.
#:
#: **What it bought, and why the seats were wrong without it.** At nine, 5.3% of `map7` could
#: take a node, in fifteen smudges lying wherever the fissures happened not to cross — and
#: `candidates` keeps the two *ends* of a row, so every rung landed on the rim of one. The chain
#: read as ten discs falling off ten ledges, with rung 9 standing on the crystal outcrop and
#: rung 10 out on a side ledge. At forty-one it is 13.9%, and it is the plateau tops themselves.
#:
#: Absent means `DESPECKLE`, so **no other painting moves by a pixel** — checked, the six other
#: maps' seats reproduce byte for byte.
HEAL = {7: 41}


def heal_of(which: int) -> int:
    """How wide a crack this painting draws on its own ground. See `HEAL`."""
    return HEAL.get(which, DESPECKLE)

#: How much ground a seat needs directly under it, as a radius in canvas units and the share
#: of that disc that has to be ground.
#:
#: **Much smaller than the disc it carries, and that is the measurement this tool was nearly
#: built on the wrong side of.** A node is 196 across; measured on the paintings themselves,
#: `map2`'s road is 68 units thick and `map3`'s trail 136. Asking for most of a 124-unit disc
#: to be road therefore seats nothing at all on two of the four maps — and it is asking the
#: wrong thing, because a level node is *supposed* to be wider than the path it stands on.
#: That is how every pack in this genre draws its own preview, ours included.
#:
#: So this asks only that the node's own footing is on the way: its middle, not its skirt.
FOOTING_RADIUS = 26.0
FOOTING_SHARE = 0.90

#: A second, wider footing, and the share of *it* a seat needs.
#:
#: The narrow test alone says "the middle is on ground" and a pebble passes it. `map1` is an
#: archipelago and paints a dozen one-cell islets in the sea in exactly the grass the real
#: islands are drawn in, so the first pass seated two glades on rocks smaller than the disc
#: standing on them - on ground, by every number, and reading as a node dropped in the water.
#: A wide disc at a low share keeps a node on a road (which is narrower than this and has
#: verge either side) and refuses anything whose land runs out in every direction.
BROAD_RADIUS = 110.0
BROAD_SHARE = 0.80

#: The smallest piece of land a node may stand on, in square canvas units.
#:
#: `map1` paints two dozen bare hexagonal rocks in the sea as scenery, in exactly the grass
#: its real islands are drawn in — so a colour test cannot tell an island from a stepping
#: stone, and the wide test cannot either once a rock is bigger than a disc. What separates
#: them is that one of them is a *place* and the other is a speck, which is a question about
#: the region rather than about any pixel in it.
#:
#: About the area of a three-by-three block of nodes. The three road maps are one connected
#: continent each and never come near it.
MIN_LANDMASS = 520_000

#: How much **current** a seat on the water needs under it, and the share of that disc.
#:
#: Wider than the land test on purpose. The same colours that draw the current also draw the
#: **foam ring** around every island and every bare rock in the sea — the pack paints a wake and
#: a shoreline out of one palette — and a ring is thin where a current is about 230 units
#: across. A 46-unit disc asked to be almost entirely current passes the middle of the stream
#: and nothing that is merely an edge.
AFLOAT_RADIUS = 46.0
AFLOAT_SHARE = 0.94

#: How far up or down from its rung a seat may be moved to find ground, as a share of one
#: strip. A road that runs flat for a while has no ground at some y values at all, and the
#: alternative to moving is standing the node in the field beside it.
RUNG_SEARCH = 0.34

#: How close to the side of the map a seat may sit, in canvas units.
#:
#: Half a disc plus air. `LevelsScreen` draws a node's plate 340 wide under it and its
#: standing mark 408 wide over it, both centred - but those hang off the node and are allowed
#: to run off the painting, where the disc is the control and may not. A seat at x=0.04 put a
#: third of a tappable node past the edge of the map.
#:
#: Applied to the foot and the head of the map as well as its sides. The map scrolls, so the
#: first glade at y=0.012 is not clipped by anything - it is simply drawn hanging over the
#: bottom edge of the painting, with sea under half of it.
EDGE_MARGIN = 118.0

#: **The foot of the map wants more than this and does not get it**, which is a known gap kept
#: narrow on purpose. A node moored on the water carries a perch hanging 195 units below its
#: centre (`PERCH_Y + PERCH_BOX[1] / 2`) with a shadow under that, the map opens scrolled to
#: its bottom, and on a siege the loadout bar stands in that foot - so the first glade of the
#: game came out 122 units up with its tile behind the bar. Widening this margin re-seats every
#: rung of every map, which throws away hand corrections that have already been approved, so
#: the first rung is lifted by a nudge instead. **Widen it the next time the seats are being
#: re-derived anyway, and delete this paragraph.** No render here draws the loadout bar, so
#: nothing but a phone can see the fault.


def stack(which: int) -> Image.Image:
    """A whole map painting, assembled exactly as `LevelsScreen.BuildMapArt` assembles it."""
    count = mapart.STRIPS[which]
    height = int(count * STRIP_HEIGHT)
    canvas = Image.new("RGB", (int(WIDTH), height), (0, 0, 0))
    for i in range(count):
        strip = Image.open(MAP_ART / f"map{which}_strip{i}.png").convert("RGB")
        strip = strip.resize((int(WIDTH), int(STRIP_HEIGHT)), Image.LANCZOS)
        canvas.paste(strip, (0, height - (i + 1) * int(STRIP_HEIGHT)))   # strip0 at the foot
    return canvas


def _near(pixels: np.ndarray, colours, tolerance: int) -> np.ndarray:
    """Which pixels are within `tolerance` of any of those colours."""
    mask = np.zeros(pixels.shape[:2], bool)
    for colour in colours:
        mask |= np.abs(pixels - np.array(colour)).max(axis=2) <= tolerance
    return mask


def land_mask(image: Image.Image, which: int) -> np.ndarray:
    """Which pixels of a painting are land at all — anything that is not `VOID`."""
    void = _near(np.asarray(image).astype(np.int16), VOID[which], VOID_TOLERANCE)
    # Whitecaps, spray and the gaps between two clouds are sea; a lone land pixel in open
    # water is not an island. Closing the void rather than opening the land, so an island's
    # own shoreline does not move.
    land = ~ndimage.binary_closing(void, np.ones((DESPECKLE, DESPECKLE)))

    # ...and a rock in the sea is not a place. See `MIN_LANDMASS`.
    labels, count = ndimage.label(land)
    if count:
        areas = np.bincount(labels.ravel())
        areas[0] = 0
        land = np.isin(labels, np.flatnonzero(areas >= MIN_LANDMASS))

    # A fissure painted across a plateau is not a hole in the world. See `HEAL` — and note it
    # is done **after** the landmass filter, so healing can never resurrect a rock the sea test
    # has already thrown away; it only closes a crack inside land that is already a place.
    heal = heal_of(which)
    if heal > DESPECKLE:
        land = ndimage.binary_closing(land, np.ones((heal, heal)))
    return land


def _tidy(mask: np.ndarray, heal: int = DESPECKLE) -> np.ndarray:
    """A dotted trail is ground with holes in it and a meadow is ground with flowers on it.

    The closing is what fills a hole and the opening is what throws a speck away, so they are
    **not** one number: `heal` widens only the first (see `HEAL`), and a pebble is still a
    pebble at any of them.
    """
    mask = ndimage.binary_closing(mask, np.ones((heal, heal)))
    return ndimage.binary_opening(mask, np.ones((DESPECKLE, DESPECKLE)))


def ground_mask(image: Image.Image, which: int) -> np.ndarray:
    """Which pixels of a painting are ground a node may stand on."""
    pixels = np.asarray(image).astype(np.int16)
    # The subtraction is at the *narrow* tolerance, deliberately: at `VOID_TOLERANCE` the
    # lava would take `map4`'s orange stone with it, and the stone is the road.
    tol = tolerance_of(which)
    return _tidy(_near(pixels, GROUND[which], tol) & ~_near(pixels, VOID[which], tol),
                 heal_of(which))


def stream_mask(image: Image.Image, which: int) -> np.ndarray:
    """Which pixels of a painting are water a node may be moored on. See `STREAM`."""
    if not STREAM[which]:
        return np.zeros(np.asarray(image).shape[:2], bool)
    return _tidy(_near(np.asarray(image).astype(np.int16), STREAM[which],
                       tolerance_of(which)))


#: How much the mask is shrunk before the footing disc is convolved over it.
#:
#: A 125-unit disc over a 1080x7200 painting is twenty million multiply-adds per pixel-row and
#: takes minutes; at a quarter scale it is seconds. The error it buys is +/-2 canvas units on
#: a 62-unit radius, which is a tenth of the tolerance that decides whether a colour is ground
#: at all - so this is a speed-up rather than an approximation anybody has to reason about.
FOOTING_SCALE = 4


def _disc_share(small: np.ndarray, radius_units: float) -> np.ndarray:
    """The share of a disc of that radius which is ground, on the shrunk mask."""
    radius = max(1, int(round(radius_units / FOOTING_SCALE)))
    ys, xs = np.mgrid[-radius:radius + 1, -radius:radius + 1]
    disc = (xs * xs + ys * ys) <= radius * radius
    kernel = disc.astype(np.float32) / disc.sum()
    return ndimage.convolve(small, kernel, mode="constant", cval=0.0)


#: What `footing` writes for a seat that stands on the ground, and for one moored on the water.
ON_GROUND, AFLOAT = 1, 2


def footing(mask: np.ndarray, land: np.ndarray, stream: np.ndarray) -> np.ndarray:
    """
    Where a node may stand, and on what: `ON_GROUND`, `AFLOAT`, or 0 for nowhere.

    A seat on the ground answers two tests — is its own footing on the way, and is there land
    all round it rather than an edge (`GROUND` says why the second is a different question).
    A seat on the water answers **one**: the broad test would refuse every point of a current,
    correctly, because a current is surrounded by sea. What stands in for it is that the narrow
    test is wider there (`AFLOAT_RADIUS`).

    The sides of the map are refused here rather than in `seat`, so nothing downstream has to
    remember to check.
    """
    small = mask[::FOOTING_SCALE, ::FOOTING_SCALE].astype(np.float32)
    whole = land[::FOOTING_SCALE, ::FOOTING_SCALE].astype(np.float32)
    wet = stream[::FOOTING_SCALE, ::FOOTING_SCALE].astype(np.float32)

    on_ground = ((_disc_share(small, FOOTING_RADIUS) >= FOOTING_SHARE)
                 & (_disc_share(whole, BROAD_RADIUS) >= BROAD_SHARE))
    afloat = _disc_share(wet, AFLOAT_RADIUS) >= AFLOAT_SHARE

    # Ground wins a tie. Nothing should be both, and if a painting ever makes it so, a node
    # standing on a road is the honest reading and a tile hovering over one is not.
    kinds = np.where(on_ground, ON_GROUND, np.where(afloat, AFLOAT, 0)).astype(np.float32)

    full = np.repeat(np.repeat(kinds, FOOTING_SCALE, axis=0), FOOTING_SCALE,
                     axis=1)[:mask.shape[0], :mask.shape[1]]
    margin = int(round(EDGE_MARGIN))
    full[:, :margin] = 0
    full[:, -margin:] = 0
    full[:margin, :] = 0

    full[-margin:, :] = 0
    return full


def _separated(seat, taken, height: float) -> bool:
    """`ChapterMap.Separation` and `ChapterMap.Overshadows`, against everything placed so far."""
    for other in taken:
        dx = (seat[0] - other[0]) * WIDTH
        dy = (seat[1] - other[1]) * height
        if (dx * dx + dy * dy) ** 0.5 < MIN_SEPARATION:
            return False
        for body, crown in ((seat, other), (other, seat)):
            ax = abs(body[0] - crown[0]) * WIDTH
            if ax >= BODY_HALF + CROWN_HALF:
                continue
            ay = (body[1] - crown[1]) * height
            if ay - BODY_BELOW < CROWN_TOP and ay + BODY_ABOVE > CROWN_BOTTOM:
                return False
    return True


def reach_of(ground: "Ground"):
    """The lowest and highest a node can stand on this map, as y fractions."""
    if ground.rows.size == 0:
        raise SystemExit("  no ground anywhere on this map - check GROUND")
    return (1.0 - ground.rows.max() / ground.height,
            1.0 - ground.rows.min() / ground.height)


#: How far apart the rows the search actually tries are, in canvas units.
#:
#: The search backtracks, so it visits a row many times over; walking every one of a
#: seven-thousand-unit map at every node of that tree is minutes of work for an answer that is
#: rounded to three decimal places anyway. Twelve units is a twentieth of a node.
SEARCH_STEP = 12


class Ground:
    """
    A map's seatable pixels, arranged for the search rather than for the eye.

    `candidates` is called thousands of times over one map and asks the same question of the
    same rows each time, so the rows are reduced once: the leftmost and rightmost seat on each
    of them, and nothing in between. **Only three columns of a row can ever be the best seat
    on it** — its two ends and whichever is nearest the rung's preferred side — because the
    cost is |x - want_x| and that is V-shaped, so anything between two of those is beaten by
    one of them at the same height.
    """

    def __init__(self, score: np.ndarray, land: np.ndarray, has_stream: bool):
        self.height = float(score.shape[0])
        self.width = float(score.shape[1])
        self.kind = score
        self.land = land
        self.has_stream = has_stream
        ok = score > 0.5
        any_row = ok.any(axis=1)
        self.rows = np.flatnonzero(any_row)
        first = ok.argmax(axis=1)
        last = self.width - 1 - ok[:, ::-1].argmax(axis=1)
        self.lo = np.where(any_row, first, -1)
        self.hi = np.where(any_row, last, -1)
        self.ok = ok

    def afloat_at(self, row: int, col: int) -> bool:
        """Whether a seat there is moored on the water rather than standing on the ground."""
        return bool(self.kind[row, col] > ON_GROUND + 0.5)


def candidates(ground: Ground, want_x: float, want_y: float, taken, *,
               reach: float, floor_y: float = -1.0, roof_y: float = 2.0, keep: int = 5):
    """
    The best places to stand a node near (`want_x`, `want_y`), best first.

    Best means: on ground, above everything already placed (`floor_y`, which is what keeps the
    trail from running back down the map), under `roof_y`, clear of every seat taken, and then
    as close as it can be to what was asked for — across the map first, because which side of
    a road a node sits on is the painting's business and the rung is the thing worth keeping.

    A list rather than one answer, because the walk backtracks: see `_walk`.
    """
    height, width = ground.height, ground.width
    span = int(round(reach * height))
    centre = int(round((1.0 - want_y) * height))

    lo_row = max(0, min(int(height) - 1, centre - span))
    hi_row = max(0, min(int(height) - 1, centre + span))

    found = []
    for row in range(lo_row, hi_row + 1, SEARCH_STEP):
        if ground.lo[row] < 0:
            continue
        y = 1.0 - row / height
        if y <= floor_y or y > roof_y:
            continue
        wanted = int(round(want_x * width))
        near = wanted if ground.ok[row, min(int(width) - 1, max(0, wanted))] else None
        for col in {int(ground.lo[row]), int(ground.hi[row])} | ({near} if near else set()):
            # Rounded *before* it is judged, because the rounded pair is what ships. A seat
            # is written to three decimals and `ChapterMapValidator` measures the file, so
            # proving clearance on the full-precision pair proves it of coordinates nothing
            # will ever hold: two glades came out 383.4 units apart against a 384-unit crown
            # rule, cleared here and warned by the build gate, on a difference of six tenths
            # of a canvas unit that this tool introduced itself.
            seat = (round(col / width, PRECISION), round(y, PRECISION),
                    ground.afloat_at(row, col))
            if not _separated(seat, taken, height):
                continue
            found.append((abs(seat[0] - want_x) * 2.0 + abs(y - want_y) * 9.0, seat))
    found.sort(key=lambda pair: pair[0])
    return [seat for _, seat in found[:keep]]


#: How far from its rung a seat may be moved, as a share of the whole map.
#:
#: A share of the map rather than of a strip, because what it is really covering is the gap
#: between two pieces of ground, and that is measured against the whole picture.
#:
#: Sized on `map1`, which is the worst case by a distance: it is an archipelago, and the open
#: sea between two of its islands is about an eighth of the map, and a rung landing in the
#: middle of one has to be able to reach the far shore. The three road
#: maps never spend more than a fiftieth of this, because a road is continuous — so what this
#: number really buys is that one rule covers a painting with a path on it and a painting
#: with nothing but islands, rather than a mode of operation per map.
SEAT_REACH = 0.17

#: The least a node must stand above the one before it, in canvas units.
#:
#: `ChapterMapValidator.CheckAscending` only asks that the chain climbs *at all*, which is a
#: rule about the trail doubling back and was written when the ladder was a table of evenly
#: spaced rungs that could not do anything else. A seat free to follow a road can: two nodes
#: a single unit apart in y sit side by side, clear every clearance rule there is, and read
#: as a rung that was skipped rather than as a step along the way.
#:
#: Sized by what the paintings can actually carry rather than by taste: `map1` is an
#: archipelago whose land is three islands and 40% of its own height, and at 280 it cannot
#: seat ten nodes at all.
MIN_ASCENT = 240.0


def _walk(which: int, ground: Ground, ascent: float):
    """
    Ten seats up one map at this stride, or `None` and the rung it gave up on.

    **It backtracks, and `map1` is why.** Placing each rung at its own best seat and moving on
    is a greedy walk, and a greedy walk up an archipelago strands itself: the sea gaps push
    the early rungs higher than they wanted to be, the top island is the only land left, and
    the tenth glade arrives to find the ninth standing in the one place its own record mark
    would have to go. Nothing about the ninth's seat was wrong when it was chosen. So a rung
    that cannot be placed sends the one below it to its next-best seat instead of failing the
    whole map — which costs nothing on the three road maps, where the first try is the answer.
    """
    height = ground.height
    ceiling = max(0.0, min(1.0, 1.0 - TEASER_HEADROOM / height))

    # The last node has to leave the marker a stride of room under the ceiling, and no more
    # than that: reserving a share of `TEASER_GAP` instead threw away the top eighth of every
    # map, which on `map1` is most of the island the chapter is supposed to finish on. What
    # keeps the marker off the last glade's record is `_separated`, which is the real rule.
    lowest, highest = reach_of(ground)
    top = min(highest, ceiling - ascent / height)

    deepest = [0]
    budget = [6_000]

    def place(taken, want_y):
        i = len(taken)
        if i == mapart.PER_CHAPTER:
            return taken
        deepest[0] = max(deepest[0], i)
        floor_y = taken[-1][1] + ascent / height if taken else -1.0
        for found in candidates(ground, mapart.XS[i], min(want_y, top), taken,
                                reach=SEAT_REACH, floor_y=floor_y, roof_y=top):
            if budget[0] <= 0:
                break
            budget[0] -= 1
            # The stride is re-derived from the room that is actually left rather than fixed
            # up front, which is what keeps the walk from running out of map. A fixed stride
            # added to a seat that was itself pushed upward compounds: on `map1`, whose land
            # is three islands separated by 1,300 units of open sea, the eighth rung was being
            # asked for a height above the ceiling.
            left = mapart.PER_CHAPTER - 1 - i
            done = place(taken + [found],
                         found[1] + (top - found[1]) / left if left > 0 else top)
            if done is not None:
                return done
        return None

    taken = place([], lowest)
    if taken is None:
        return None, f"rung {deepest[0] + 1}"

    # The marker caps the chain and is not a level, so it stands on ground like everything
    # else — but only its **x** is ours to choose. Its height is derived by
    # `ChapterMap.TeaserPosition` from the highest glade, and a chapter cannot author it
    # (`ChapterDefinition.TeaserX` is the only axis there is). So the search is pinned to that
    # one row: a marker found at some other height would be a seat this tool believed in and
    # the game drew somewhere else.
    aim = min(ceiling, taken[-1][1] + TEASER_GAP)
    seated = candidates(ground, TEASER_X, aim, taken, reach=0.0, keep=1)
    marker = seated[0] if seated else None
    if marker is None:
        # Nowhere at that height clears the last glade's standing mark. The
        # marker is scenery and a signpost rather than a level, so clearing the mark wins over
        # standing on the road: a marker in the scrub reads as a signpost off the path, where
        # one sitting on a record covers the number a cleared glade is showing off.
        marker = _clear_of(taken, aim, ground)
    return (taken, marker), None


def _clear_of(taken, y, ground):
    """
    The x nearest `TEASER_X` at this height that clears everything already placed.

    **And it is afloat wherever it lands on nothing.** The marker's height is derived and not
    ours to move, so on a map whose ground runs out up there — `map1`'s does, and its own last
    island stops short of the ceiling — the fallback is all there is. A signpost hanging in
    open water with no tile under it is the exact fault this whole tool exists to end, so the
    fallback asks what it came down on and takes a perch when the answer is nothing. On a road
    map it lands on the continent and takes none.
    """
    land = ground.land
    row = max(0, min(int(ground.height) - 1, int(round((1.0 - y) * ground.height))))

    best, best_cost = None, None
    step = 1.0 / 10 ** PRECISION
    for i in range(10 ** PRECISION + 1):
        x = round(i * step, PRECISION)
        col = max(0, min(int(ground.width) - 1, int(round(x * ground.width))))
        # Only a map that paints a current may moor anything. `map4` is a continent with a
        # shadow under it, and reading that shadow as "nothing" stood a grass-topped island
        # tile over volcanic rock — the right question asked of the wrong painting.
        seat = (x, round(y, PRECISION),
                ground.has_stream and not bool(land[row, col]))
        if not _separated(seat, taken, ground.height):
            continue
        cost = abs(x - TEASER_X)
        if best_cost is None or cost < best_cost:
            best, best_cost = seat, cost
    return best or (TEASER_X, round(y, PRECISION), ground.has_stream)


def seats_for(which: int, score: np.ndarray, land: np.ndarray):
    """
    Where a chapter drawing this map stands its ten nodes, and its end-of-chapter marker.

    `MIN_ASCENT` is what the walk *wants*; a painting may not be able to give it. `map1` has
    three islands and 40% of its height in open water, and its own top island cannot take two
    more nodes at a full stride without one standing on the other's record — `ChapterMap`'s
    crown rule needs 529 units between two nodes on the same side of the map, which is twice
    the ascent. So the ascent is relaxed a step at a time and the *first* one that seats the
    chapter wins, which leaves every road map on the stride it asked for and bends only where
    the ground makes it.

    **Never the authored rung.** That is what the serpentine was, and on `map1` it is open
    sea: a node the tool could not seat would be quietly put back in the water, which is the
    exact fault this tool exists to end. A map that cannot seat ten nodes at any stride is a
    picture somebody has to look at.
    """
    ground = Ground(score, land, bool(STREAM[which]))
    for ascent in (MIN_ASCENT, MIN_ASCENT * 0.8, MIN_ASCENT * 0.65,
                   MIN_ASCENT * 0.5, MIN_ASCENT * 0.4, MIN_ASCENT * 0.3):
        found, why = _walk(which, ground, ascent)
        if found is not None:
            return found
    raise SystemExit(
        f"  map{which}: nowhere to stand {why} at any stride - "
        f"loosen GROUND, raise SEAT_REACH, or cut the map differently")


def apply_nudges(which: int, places, marker, height: float):
    """
    The authored corrections, and the check that they are still legal.

    Re-checked rather than trusted: a nudge is a hand edit, and the whole reason the seats are
    generated is that hand-placed coordinates drift into each other. `ChapterMapValidator` would
    catch it at build time anyway — this says which nudge did it, which that cannot.
    """
    moved = [nudged(which, i + 1, seat) for i, seat in enumerate(places)]
    moved_marker = nudged(which, "marker", marker)

    for i, seat in enumerate(moved):
        if not _separated(seat, moved[:i] + moved[i + 1:] + [moved_marker], height):
            if _overlap_accepted(which, i + 1):
                print(f"  map{which}: rung {i + 1} overlaps a neighbour - accepted, see "
                      f"ACCEPTED_OVERLAPS")
                continue
            raise SystemExit(
                f"  map{which}: the nudge on rung {i + 1} puts it on top of another node - "
                f"shrink it, or move the one it collides with too")
    return moved, moved_marker


def borrowed(which: int, source: int, score: np.ndarray):
    """
    Another map's chain, stood on this one, nudged, checked against this one and reported.

    A borrow is an assertion where a search is a derivation, so the two things that made the
    search trustworthy are kept: every seat is re-checked for clearance on *this* map, and
    every seat is asked this map's own footing question and the answer is **printed** rather
    than swallowed. `BORROWS` says why the answer is expected to be no.

    The chain is checked as it transfers and **then** given this map's own `NUDGE` entries, so
    the two failures stay apart: a chain that does not transfer is one thing, a hand correction
    that collides is another, and each says so in its own words. The footing is reported after
    the nudge, because the seat that was moved is the seat that ships.

    Refused outright if the two maps are not cut to the same height. A seat is a fraction, so
    the same pair of numbers on a map with a different strip count is a different place in
    canvas units - which is every distance `ChapterMap` measures.
    """
    if mapart.STRIPS[which] != mapart.STRIPS[source]:
        raise SystemExit(
            f"  map{which} cannot borrow map{source}: {mapart.STRIPS[which]} strips against "
            f"{mapart.STRIPS[source]} - a seat is a fraction, so the chain would not land where "
            f"it was looked at")

    _, _, places, marker = measure(source)
    places, marker = list(places), marker
    height = float(score.shape[0])

    for i, seat in enumerate(places):
        if not _separated(seat, places[:i] + places[i + 1:] + [marker], height):
            if not _overlap_accepted(which, i + 1):
                raise SystemExit(
                    f"  map{which}: rung {i + 1} of map{source}'s chain overlaps a neighbour - "
                    f"the chain does not transfer, seat this map on its own picture")

    places, marker = apply_nudges(which, places, marker, height)

    off = [str(i + 1) for i, seat in enumerate(places)
           if score[max(0, min(int(height) - 1, int(round((1.0 - seat[1]) * height)))),
                    max(0, min(score.shape[1] - 1, int(round(seat[0] * WIDTH))))] < 0.5]
    print(f"  map{which}: chain borrowed from map{source}" +
          (f" - rungs {', '.join(off)} stand where this map's own footing test refuses, see "
           f"BORROWS" if off else ""))
    return places, marker


#: Every map measured once, because a borrow asks for the map it borrows from.
_MEASURED: dict = {}


def measure(which: int):
    """Everything about one map: the picture, its ground, and where the nodes go."""
    if which in _MEASURED:
        return _MEASURED[which]
    image = stack(which)
    mask = ground_mask(image, which)
    land = land_mask(image, which)
    score = footing(mask, land, stream_mask(image, which))
    if which in BORROWS:
        places, marker = borrowed(which, BORROWS[which], score)
    else:
        places, marker = seats_for(which, score, land)
        places, marker = apply_nudges(which, places, marker, float(score.shape[0]))
    _MEASURED[which] = (image, mask, places, marker)
    return _MEASURED[which]


# ---------------------------------------------------------------- the table


def render_table(all_seats) -> str:
    """The `SEATS` block as `chapters/mapart.py` carries it."""
    lines = ["SEATS = {"]
    for which in sorted(all_seats):
        places, marker = all_seats[which]
        lines.append("    %d: (" % which)
        for x, y, afloat in places:
            lines.append("        (%.*f, %.*f, %s)," % (PRECISION, x, PRECISION, y,
                                                        "True" if afloat else "False"))
        lines.append("    ),")
    lines.append("}")
    lines.append("")
    lines.append("#: Where the end-of-chapter marker stands on each map, found the same way.")
    lines.append("MARKERS = {")
    for which in sorted(all_seats):
        _, marker = all_seats[which]
        lines.append("    %d: (%.*f, %.*f, %s)," % (which, PRECISION, marker[0],
                                                    PRECISION, marker[1],
                                                    "True" if marker[2] else "False"))
    lines.append("}")
    return "\n".join(lines)


_BLOCK = re.compile(r"^SEATS = \{.*?^\}\n\n#: Where the end-of-chapter.*?^\}$",
                    re.S | re.M)


def write_table(text: str) -> bool:
    source = MAPART_PY.read_text(encoding="utf-8")
    if not _BLOCK.search(source):
        print("  mapart.py has no SEATS block to replace", file=sys.stderr)
        return False
    updated = _BLOCK.sub(lambda _: text, source, count=1)
    if updated == source:
        return False
    MAPART_PY.write_text(updated, encoding="utf-8")
    return True


# ---------------------------------------------------------------- the picture


def contact(all_seats, masks, images, out: Path, *, show_mask: bool) -> None:
    """
    Every map with its nodes standing on it, side by side.

    This is the gate. `--check` proves the table reproduces and says nothing at all about
    whether a disc reads against the ground it is standing on.
    """
    disc = Image.open(MAP_ART / "node_open.png").convert("RGBA")
    disc = disc.resize((int(NODE_DIAMETER), int(NODE_DIAMETER)), Image.LANCZOS)
    lock = Image.open(MAP_ART / "node_lock.png").convert("RGBA")
    lock = lock.resize((int(NODE_DIAMETER), int(NODE_DIAMETER)), Image.LANCZOS)
    perch = Image.open(MAP_ART / (PERCH + ".png")).convert("RGBA")
    perch.thumbnail((int(PERCH_BOX[0]), int(PERCH_BOX[1])), Image.LANCZOS)

    cards = []
    for which in sorted(all_seats):
        canvas = images[which].convert("RGBA")
        if show_mask:
            tinted = np.asarray(canvas).copy()
            m = masks[which]
            tinted[m] = (tinted[m] * 0.55 + np.array([255, 40, 200, 255]) * 0.45).astype(np.uint8)
            canvas = Image.fromarray(tinted)

        height = canvas.size[1]
        places, marker = all_seats[which]
        draw = ImageDraw.Draw(canvas, "RGBA")

        # No trail between the nodes, because the screen draws none: the road the painting
        # already carries is the chain (`LevelsScreen.BuildNodes`). A mirror that drew one
        # would be answering a question about a map the game does not produce (44d), and it
        # is the question this sheet exists for - a straight run of dots between two seats on
        # a winding road is exactly what a picture has to be opened to see.
        for i, (x, y, afloat) in enumerate(places):
            _stand(canvas, draw, x * WIDTH, (1 - y) * height, disc, afloat, str(i + 1), perch)

        _stand(canvas, draw, marker[0] * WIDTH, (1 - marker[1]) * height, lock, marker[2],
               None, perch)

        card = canvas.convert("RGB")
        card.thumbnail((460, 9000), Image.LANCZOS)
        cards.append(card)

    tall = max(c.size[1] for c in cards)
    sheet = Image.new("RGB", (sum(c.size[0] for c in cards) + 10 * (len(cards) + 1), tall + 20),
                      (28, 30, 36))
    x = 10
    for card in cards:
        sheet.paste(card, (x, 10))
        x += card.size[0] + 10
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out)
    print(f"  {out}")


#: `LevelsScreen`'s perch, mirrored: the sprite, the box it is fitted into with its aspect
#: kept, how far below the node's centre that box sits, and how far above the centre the disc
#: stands so that it lands on the tile's top face rather than through it.
PERCH = "rock_grass"
PERCH_BOX = (360.0, 290.0)
PERCH_Y = -50.0
PERCH_LIFT = 14.0


def _stand(canvas, draw, cx, cy, disc, afloat, number, perch):
    """One node on the map, with its perch under it when it is moored on the water."""
    lift = PERCH_LIFT if afloat else 0.0
    if afloat:
        glow = Image.new("RGBA", (370, 150), (0, 0, 0, 0))
        ImageDraw.Draw(glow).ellipse([0, 0, 369, 149], fill=(8, 26, 41, 97))
        canvas.alpha_composite(glow, (round(cx - 185), round(cy + 150 - 75)))
        canvas.alpha_composite(perch, (round(cx - perch.size[0] / 2),
                                       round(cy - PERCH_Y - perch.size[1] / 2)))

    shadow = Image.new("RGBA", (214, 62), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse([0, 0, 213, 61], fill=(5, 20, 31, 107))
    canvas.alpha_composite(shadow, (round(cx - 107), round(cy - lift - 31 + 34)))
    canvas.alpha_composite(disc, (round(cx - 98), round(cy - lift - 98)))
    if number is not None:
        draw.text((cx, cy - lift - NODE_DIAMETER * 0.165), number,
                  fill=(77, 54, 33, 255), anchor="mm", font=_font(62))


def _font(size: int):
    from PIL import ImageFont
    for name in ("seguibl.ttf", "arialbd.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="prove the table in mapart.py is what this tool finds")
    ap.add_argument("--write", action="store_true", help="rewrite that table")
    ap.add_argument("--contact", action="store_true",
                    help="draw every map with its nodes on it — the gate that matters")
    ap.add_argument("--mask", action="store_true", help="tint the ground on the contact sheet")
    ap.add_argument("--out", default="out/map_seats.png")
    args = ap.parse_args()

    all_seats, masks, images = {}, {}, {}
    for which in sorted(mapart.STRIPS):
        image, mask, places, marker = measure(which)
        all_seats[which], masks[which], images[which] = (places, marker), mask, image
        gaps = [(places[i + 1][1] - places[i][1]) * image.size[1] for i in range(len(places) - 1)]
        wet = sum(1 for seat in places if seat[2])
        print(f"  map{which}: {len(places)} seats ({wet} afloat), rungs "
              f"{min(gaps):.0f}-{max(gaps):.0f} units apart, marker at "
              f"({marker[0]:.3f}, {marker[1]:.3f}){' afloat' if marker[2] else ''}")

    table = render_table(all_seats)

    if args.contact:
        contact(all_seats, masks, images, REPO / args.out, show_mask=args.mask)

    if args.write:
        print("  mapart.py rewritten" if write_table(table) else "  mapart.py already current")

    if args.check:
        shipped = render_table({w: (list(mapart.SEATS[w]), mapart.MARKERS[w])
                                for w in sorted(mapart.SEATS)})
        if shipped.split() != table.split():
            print("  the shipped seats are not what this tool finds", file=sys.stderr)
            return 1
        print("  seats reproduce")

    if not (args.check or args.write or args.contact):
        print()
        print(table)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
