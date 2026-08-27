"""The Nightbriar - the ten boards of chapter four, as they were built.

**The shipped chapter is `Content/chapters/c04_nightbriar.json`, not this file.** That is
what the game reads, what the validators judge and what the build gate proves; nothing at
runtime knows this script exists.

    python Tools/chapters/c04_nightbriar.py --check     # does the shipped JSON still match?
    python Tools/chapters/c04_nightbriar.py             # rewrite it from here

The chapter's subject is the **briar** (`%NS+EW`), the one new rule here: four arms, of
which the thorns close two, and one tap swaps which. It is the crossing's opposite number -
a bridge lets both ways through, a bramble lets one - and it is the cheapest honest decision
a board can carry, because all four of its neighbours mate it at every angle and so nothing
about the pipe-fitting can settle it. That is the property `Tools/verify/difficulty.py`
counts, and it is why every glade here reports `arms` well above one.

Three shapes carry the whole chapter, and they are worth knowing before editing a board:

* **a briar cuts its own way.** Whatever the open pair was feeding goes dark.
* **a briar joins what its thorns were holding apart.** The pair it opens is *merged*, so
  thorns standing between a red network and a blue one blend both the moment they move.
* **a briar on a cycle only answers to the pocket beside it.** If the open pair is part
  of a loop, shutting it costs the grove nothing - the light goes round - so the only thing
  that changed is the pocket the other pair just let in on. Every pocket in this chapter
  carries a heart and a critter of its own, so the wrong turn puts *that* critter out and
  leaves the whole grove lit. That third one is `CONTENT.md`'s rule about fords, and the
  briar is what makes it easy to author rather than a happy accident.

Everything the boards do not decide lives in the tables below: the palette each backdrop is
graded to, the difficulty ramp, where the glades sit on the map, and the strings. `par` is
absent on purpose - it is derived from the board, and a typed one can drift.
"""
import argparse
import collections
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "Tools", "verify"))

from author import Board, fit                                    # noqa: E402

CHAPTER = "c04_nightbriar"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")
LOC = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")


def g1(seed, bias):
    """Thornlight 6x6 - thorns close two ways of four, and one tap moves them.

    Two groves, each reaching through a bramble into a pocket only it can feed. Nothing
    else is going on: turn either briar and the pocket behind it goes out.
    """
    b = Board(6, 6)
    b.fill(0, 0, 6, 6)
    b.source(2, 1, 'R'); b.source(3, 4, 'R')
    b.briar(1, 2, 'NS')
    b.briar(4, 3, 'NS')

    # the upper grove: a run one row inside the top, hung with short teeth
    b.path((0, 1), (1, 1), (2, 1), (3, 1), (4, 1), (5, 1))
    b.path((0, 1), (0, 0), (1, 0))
    b.path((2, 1), (2, 0), (3, 0))
    b.path((4, 1), (4, 0)); b.path((5, 1), (5, 0))
    b.path((0, 1), (0, 2)); b.path((5, 1), (5, 2))
    b.path((3, 1), (3, 2), (3, 3))

    # the lower grove, the same upside down
    b.path((0, 4), (1, 4), (2, 4), (3, 4), (4, 4), (5, 4))
    b.path((0, 4), (0, 5)); b.path((1, 4), (1, 5), (2, 5))
    b.path((3, 4), (3, 5), (4, 5)); b.path((5, 4), (5, 5))
    b.path((5, 4), (5, 3)); b.path((2, 4), (2, 3), (2, 2))

    # west: the upper grove reaches down through thorns into a pocket of its own
    b.path((1, 1), (1, 2), (1, 3), (0, 3))
    b.path((0, 2), (1, 2)); b.path((1, 2), (2, 2))

    # east: the lower grove reaches up through thorns into a pocket of its own
    b.path((4, 2), (4, 3), (4, 4))
    b.path((3, 3), (4, 3)); b.path((4, 3), (5, 3))

    for p in [(1, 0), (3, 0), (4, 0), (5, 0), (5, 2), (3, 3), (0, 3), (4, 2),
              (0, 5), (1, 5), (2, 5), (3, 5), (4, 5), (5, 5)]:
        b.lamp(p[0], p[1], 'A')

    b.spin(seed, bias)
    b.owe((1, 2), 1); b.owe((4, 3), 1)
    return b


def g2(seed, bias):
    """The Shut Way 6x7 - the thorns are the only ground where red and blue may meet.

    A lane across the middle holds a red heart and a blue one, so the lane is blossom and
    the critters at its ends want blossom. Above it a pure red grove, below it a pure blue
    one, and both brambles stand with their thorns across that divide: turning one cuts the
    lane *and* hands each grove the other's colour.
    """
    b = Board(6, 7)
    b.fill(0, 0, 6, 7)
    b.source(2, 1, 'R')                          # the pure red grove
    b.source(3, 5, 'B')                          # the pure blue one
    b.source(1, 3, 'R'); b.source(3, 3, 'B')     # and the lane's own pair
    b.briar(2, 3, 'EW')
    b.briar(4, 3, 'EW')

    # the lane, which is blossom only while both brambles hold it open
    b.path((0, 3), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3))

    # the red grove above: a run one row inside the top, and no tile on it wearing
    # four arms - a conduit that reads the same at every angle asks the player nothing
    b.path((0, 1), (1, 1), (2, 1), (3, 1), (4, 1), (5, 1))
    b.path((0, 1), (0, 0)); b.path((1, 1), (1, 0), (2, 0))
    b.path((3, 1), (3, 0), (4, 0)); b.path((5, 1), (5, 0))
    b.path((0, 1), (0, 2), (1, 2)); b.path((5, 1), (5, 2))
    b.path((2, 1), (2, 2)); b.path((4, 1), (4, 2))
    b.path((2, 2), (3, 2))

    # the blue grove below, hung the other way about so the two do not mirror
    b.path((0, 5), (1, 5), (2, 5), (3, 5), (4, 5), (5, 5))
    b.path((0, 5), (0, 6)); b.path((1, 5), (1, 6), (2, 6))
    b.path((3, 5), (3, 6), (4, 6)); b.path((5, 5), (5, 6))
    b.path((0, 5), (0, 4), (1, 4)); b.path((5, 5), (5, 4))
    b.path((2, 5), (2, 4)); b.path((4, 5), (4, 4))
    b.path((2, 4), (3, 4))

    for p in [(0, 0), (2, 0), (4, 0), (5, 0), (1, 2), (3, 2), (5, 2)]:
        b.lamp(p[0], p[1], 'R')
    for p in [(0, 6), (2, 6), (4, 6), (5, 6), (1, 4), (3, 4), (5, 4)]:
        b.lamp(p[0], p[1], 'B')
    for p in [(0, 3), (5, 3)]:
        b.lamp(p[0], p[1], 'M')

    b.spin(seed, bias)
    return b
def g3(seed, bias):
    """Nightfall 7x6 - the ford on a loop, so the pocket is the only thing that answers.

    The briar in the middle stands on a ring of live conduit. Shutting its way costs
    nothing at all - the light goes round the other side - so no critter on the ring ever
    tells the player they were wrong. What the same tap does is open the way north, into a
    pocket of green with a heart of its own, and the red pouring in puts both of its
    critters out at once.
    """
    b = Board(7, 6)
    b.fill(0, 0, 7, 6)
    b.source(3, 4, 'R')
    b.briar(3, 1, 'EW'); b.briar(4, 1, 'EW')

    # the ring both fords stand on, and the one cell they hand the light to
    b.path((2, 1), (3, 1), (4, 1), (4, 2), (4, 3), (3, 3), (2, 3), (2, 2), (2, 1))
    b.path((3, 3), (3, 4))
    b.path((2, 2), (3, 2)); b.path((3, 2), (3, 1))

    # the pocket of green the thorns are holding out of the red
    b.source(2, 0, 'G'); b.lamp(3, 0, 'G'); b.lamp(4, 0, 'G')
    b.path((2, 0), (3, 0), (4, 0)); b.path((3, 0), (3, 1))

    # the grove below
    b.path((0, 4), (1, 4), (2, 4), (3, 4), (4, 4), (5, 4), (6, 4))
    b.path((0, 4), (0, 5)); b.path((1, 4), (1, 5), (2, 5))
    b.path((4, 4), (4, 5), (3, 5)); b.path((6, 4), (6, 5), (5, 5))
    b.path((1, 4), (1, 3), (1, 2), (1, 1), (0, 1))
    b.path((5, 4), (5, 3), (5, 2), (5, 1), (6, 1))
    b.path((0, 1), (0, 0), (1, 0)); b.path((6, 1), (6, 0), (5, 0))
    b.path((0, 1), (0, 2), (0, 3)); b.path((6, 1), (6, 2), (6, 3))

    for p in [(1, 0), (5, 0), (0, 3), (6, 3), (3, 2),
              (0, 5), (2, 5), (3, 5), (5, 5)]:
        b.lamp(p[0], p[1], 'A')

    b.spin(seed, bias)
    b.owe((3, 1), 1)
    return b


def g4(seed, bias):
    """Bramble and Bridge 7x7 - the two four-armed tiles, side by side.

    A crossing and a briar wear the same four arms and mean opposite things: the bridge
    carries both ways through and never needs turning, the bramble carries one and turning
    it is the point. A red lane runs west to east under the bridge and a green one north to
    south over it, and there is a bramble on each of them.
    """
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(1, 3, 'R'); b.source(5, 3, 'R')
    b.source(3, 1, 'G')
    b.cross(3, 3, 'NS')
    b.briar(2, 3, 'EW')
    b.briar(3, 4, 'NS')

    # the red lane, west to east under the bridge
    b.path((0, 3), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3), (6, 3))

    # the green lane, north to south over it
    b.path((3, 0), (3, 1), (3, 2), (3, 3), (3, 4), (3, 5), (3, 6))

    # what each bramble is holding apart
    b.path((2, 2), (2, 3)); b.path((2, 3), (2, 4))
    b.path((2, 4), (3, 4)); b.path((3, 4), (4, 4))

    # the green grove: the whole of the board above the red lane
    b.path((2, 1), (3, 1), (4, 1))
    b.path((2, 1), (1, 1), (0, 1)); b.path((4, 1), (5, 1), (6, 1))
    b.path((0, 1), (0, 0), (1, 0)); b.path((6, 1), (6, 0), (5, 0))
    b.path((3, 0), (2, 0)); b.path((3, 0), (4, 0))
    b.path((0, 1), (0, 2), (1, 2)); b.path((6, 1), (6, 2), (5, 2))
    b.path((2, 1), (2, 2)); b.path((4, 1), (4, 2))

    # the red grove: the whole of the board below it
    b.path((1, 3), (1, 4), (1, 5)); b.path((5, 3), (5, 4), (5, 5))
    b.path((1, 4), (2, 4)); b.path((5, 4), (4, 4))
    b.path((1, 5), (0, 5), (0, 4)); b.path((5, 5), (6, 5), (6, 4))
    b.path((0, 5), (0, 6), (1, 6)); b.path((6, 5), (6, 6), (5, 6))
    b.path((1, 5), (2, 5), (2, 6)); b.path((5, 5), (4, 5), (4, 6))

    # and the two ends of the green lane, which only it can reach
    b.path((3, 5), (3, 6))

    for p in [(0, 0), (1, 0), (5, 0), (6, 0), (2, 0), (4, 0), (1, 2), (5, 2), (2, 2), (4, 2)]:
        b.lamp(p[0], p[1], 'G')
    for p in [(0, 3), (6, 3), (0, 4), (6, 4), (1, 6), (5, 6), (2, 6), (4, 6)]:
        b.lamp(p[0], p[1], 'R')
    b.lamp(3, 6, 'G')

    b.spin(seed, bias)
    return b


def g5(seed, bias):
    """The Hollow Gate 7x7 - brittle stone where guessing is all you have.

    A twisted bramble has four states and its arms settle none of them, which is exactly
    where brittle stone belongs: two turns of stone against one turn owed is one wrong
    guess and no more. One stands where the green grove reaches its far pocket and one
    where the blue grove reaches its own, and the red lane runs between them with a
    shoulder against each.
    """
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(1, 1, 'R'); b.source(1, 5, 'G'); b.source(5, 5, 'B')
    b.briar(2, 2, 'SW', fragile=2)
    b.briar(4, 4, 'NE', fragile=2)

    # the red lane, one row inside the top, with a column down the middle
    b.path((0, 1), (1, 1), (2, 1), (3, 1), (4, 1), (5, 1), (6, 1))
    b.path((0, 1), (0, 0), (1, 0)); b.path((2, 1), (2, 0), (3, 0))
    b.path((4, 1), (4, 0)); b.path((6, 1), (6, 0), (5, 0))
    b.path((3, 1), (3, 2), (3, 3), (3, 4), (3, 5), (3, 6))

    # the green grove, west, and the pocket only its bramble can reach
    b.path((1, 5), (1, 4), (1, 3), (1, 2))
    b.path((1, 5), (0, 5), (0, 4), (0, 3), (0, 2))
    b.path((0, 5), (0, 6), (1, 6))
    b.path((1, 5), (2, 5), (2, 4)); b.path((2, 5), (2, 6))

    # the blue grove, east, likewise
    b.path((5, 5), (5, 4), (5, 3), (5, 2))
    b.path((5, 5), (6, 5), (6, 4), (6, 3), (6, 2))
    b.path((6, 5), (6, 6), (5, 6))
    b.path((5, 5), (4, 5), (4, 6)); b.path((5, 2), (4, 2))

    for p in [(1, 0), (3, 0), (4, 0), (5, 0), (3, 6)]:
        b.lamp(p[0], p[1], 'R')
    for p in [(0, 2), (1, 6), (2, 3), (2, 4), (2, 6)]:
        b.lamp(p[0], p[1], 'G')
    for p in [(6, 2), (5, 6), (4, 3), (4, 2), (4, 6)]:
        b.lamp(p[0], p[1], 'B')

    b.spin(seed, bias)
    b.owe((2, 2), 1); b.owe((4, 4), 1)
    return b
def g6(seed, bias):
    """Rootbriar 7x7 - one rune, two brambles, opposite corners, two pockets.

    A taproot's members should all be tiles the arms cannot settle, or the binding is a
    hint rather than a decision - and a briar is never settled by its arms. Both of these
    stand where the light can go round them, so the tap that turns them costs the grove no
    critter at all; what it does is pour red into a pocket of green at each end of the
    board, and the two critters standing in those pockets are the whole of the warning.
    """
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(3, 3, 'R')
    b.briar(2, 1, 'EW', link='A')
    b.briar(4, 5, 'EW', link='A')

    # the cross the heart stands at
    b.path((3, 1), (3, 2), (3, 3), (3, 4), (3, 5))
    b.path((1, 3), (2, 3), (3, 3), (4, 3), (5, 3), (6, 3))

    # the north-west bramble, and the way round it
    b.path((1, 1), (2, 1), (3, 1))
    b.path((1, 1), (1, 2), (2, 2))
    b.source(1, 0, 'G'); b.lamp(2, 0, 'G'); b.path((1, 0), (2, 0))

    # the south-east bramble, and the way round it
    b.path((3, 5), (4, 5), (5, 5))
    b.path((5, 5), (5, 4), (4, 4))
    b.source(5, 6, 'G'); b.lamp(4, 6, 'G'); b.path((4, 6), (5, 6))

    # the grove, hung off the four spokes
    b.path((1, 1), (0, 1), (0, 0)); b.path((0, 1), (0, 2), (0, 3))
    b.path((3, 1), (4, 1), (5, 1), (6, 1))
    b.path((4, 1), (4, 0), (3, 0)); b.path((4, 1), (4, 2))
    b.path((5, 1), (5, 2)); b.path((6, 1), (6, 0), (5, 0)); b.path((6, 1), (6, 2))
    b.path((1, 3), (0, 3)); b.path((1, 3), (1, 4), (0, 4))
    b.path((5, 3), (6, 3), (6, 4), (6, 5))
    b.path((1, 4), (1, 5), (0, 5), (0, 6), (1, 6))
    b.path((5, 5), (6, 5), (6, 6))
    b.path((3, 5), (2, 5), (2, 4)); b.path((2, 5), (2, 6), (3, 6))

    for p in [(0, 0), (3, 0), (5, 0), (4, 2), (5, 2), (6, 2),
              (0, 4), (2, 4), (1, 6), (3, 6), (6, 6)]:
        b.lamp(p[0], p[1], 'A')

    b.spin(seed, bias)
    b.root('A', 1, (2, 1), (4, 5))
    return b
def g7(seed, bias):
    """Three Thorns 7x7 - three groves, and every bramble on a border.

    No dark and no brittle stone: three hearts, three groves that must stay pure, and three
    brambles, each standing where two of them come within a tile of each other. Every one of
    them has red on one side of its thorns and green or blue on the other, so a wrong turn
    does not merely cut a way - it hands one grove another's colour.
    """
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(1, 1, 'R'); b.source(5, 1, 'G'); b.source(3, 5, 'B')
    b.briar(3, 1, 'NS')
    b.briar(1, 3, 'EW')
    b.briar(5, 3, 'EW')

    # the red grove, north-west
    b.path((0, 1), (1, 1), (2, 1))
    b.path((1, 1), (1, 0), (0, 0)); b.path((2, 1), (2, 0))
    b.path((0, 1), (0, 2)); b.path((1, 1), (1, 2), (2, 2)); b.path((2, 2), (2, 3))

    # the green grove, north-east
    b.path((6, 1), (5, 1), (4, 1))
    b.path((5, 1), (5, 0), (6, 0)); b.path((4, 1), (4, 0))
    b.path((6, 1), (6, 2)); b.path((5, 1), (5, 2), (4, 2)); b.path((4, 2), (4, 3))

    # the blue grove, which reaches up between them
    b.path((3, 0), (3, 1), (3, 2), (3, 3), (3, 4), (3, 5))
    b.path((0, 5), (1, 5), (2, 5), (3, 5), (4, 5), (5, 5), (6, 5))
    b.path((0, 5), (0, 4)); b.path((1, 5), (1, 4), (2, 4))
    b.path((5, 5), (5, 4), (4, 4)); b.path((6, 5), (6, 4))
    b.path((0, 5), (0, 6), (1, 6)); b.path((2, 5), (2, 6), (3, 6))
    b.path((4, 5), (4, 6)); b.path((6, 5), (6, 6), (5, 6))

    for p in [(0, 0), (2, 0), (0, 2), (2, 2), (0, 3), (2, 3)]:
        b.lamp(p[0], p[1], 'R')
    for p in [(6, 0), (4, 0), (6, 2), (4, 2), (6, 3), (4, 3)]:
        b.lamp(p[0], p[1], 'G')
    for p in [(3, 0), (0, 4), (2, 4), (4, 4), (6, 4), (1, 6), (3, 6), (5, 6), (4, 6)]:
        b.lamp(p[0], p[1], 'B')

    b.spin(seed, bias)
    return b
def g8(seed, bias):
    """The Long Lane 8x7 - a green lane that runs the width of the board, under two bridges.

    The grove is a ladder above the lane and a ladder below it, joined down each side by a
    column that crosses the lane without touching it. Both brambles stand on those ladders,
    so shutting a way costs nothing - the light goes round the rung next to it - and the
    only thing a wrong turn does is pour red into the green, which only the lane's own two
    critters will tell you about.
    """
    b = Board(8, 7)
    b.fill(0, 0, 8, 7)
    b.source(3, 1, 'R'); b.source(4, 5, 'R')
    b.cross(1, 3, 'NS'); b.cross(6, 3, 'NS')
    b.briar(2, 2, 'EW')
    b.briar(5, 4, 'EW')
    b.source(4, 3, 'G'); b.lamp(0, 3, 'G'); b.lamp(7, 3, 'G')

    # the green lane, from one edge of the board to the other, under both bridges
    b.path((0, 3), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3), (6, 3), (7, 3))

    # the upper ladder, with the first bramble in its lower rail
    b.path((1, 1), (2, 1), (3, 1), (4, 1), (5, 1), (6, 1))
    b.path((1, 2), (2, 2), (3, 2), (4, 2), (5, 2), (6, 2))
    b.path((1, 1), (1, 2)); b.path((3, 1), (3, 2))
    b.path((4, 1), (4, 2)); b.path((6, 1), (6, 2))

    # the lower ladder, with the second
    b.path((1, 5), (2, 5), (3, 5), (4, 5), (5, 5), (6, 5))
    b.path((1, 4), (2, 4), (3, 4), (4, 4), (5, 4), (6, 4))
    b.path((1, 4), (1, 5)); b.path((3, 4), (3, 5))
    b.path((4, 4), (4, 5)); b.path((6, 4), (6, 5))

    # the two columns, each crossing the lane on its way down
    b.path((1, 2), (1, 3), (1, 4)); b.path((6, 2), (6, 3), (6, 4))

    # and the border, hung off the ladders
    b.path((1, 1), (0, 1), (0, 0), (1, 0), (2, 0))
    b.path((5, 1), (5, 0), (4, 0), (3, 0))
    b.path((6, 1), (6, 0), (7, 0), (7, 1), (7, 2))
    b.path((0, 1), (0, 2))
    b.path((1, 5), (0, 5), (0, 6), (1, 6), (2, 6))
    b.path((5, 5), (5, 6), (4, 6), (3, 6))
    b.path((6, 5), (6, 6), (7, 6), (7, 5), (7, 4))
    b.path((0, 5), (0, 4))

    for p in [(2, 0), (3, 0), (0, 2), (7, 2), (2, 6), (3, 6), (0, 4), (7, 4),
              (5, 2), (2, 4), (5, 1)]:
        b.lamp(p[0], p[1], 'A')

    b.spin(seed, bias)
    b.owe((2, 2), 1)
    return b
def g9(seed, bias):
    """Wick and Wane 7x7 - a blend to make, a blue pair pinned between two brambles.

    Every critter here wants amber, and amber needs the red heart and the green one joined -
    which they are, across one brittle bramble in the middle of the board and nowhere else.
    The blue pair below it is held out of the amber by that bramble's thorns on one side
    and by a second bramble's on the other, and the second one stands on a loop, so only
    the pair's own critter will warn you.
    """
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(1, 3, 'R'); b.source(5, 3, 'G')
    b.briar(3, 3, 'EW', fragile=2)
    b.briar(3, 5, 'EW')
    b.source(2, 4, 'B'); b.lamp(3, 4, 'B'); b.path((2, 4), (3, 4))

    # the lane: a heart at each hand of the bramble that joins them
    b.path((0, 3), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3), (6, 3))

    # the upper grove, hung off the green heart alone, so the lane is the only join
    b.path((5, 3), (5, 2), (5, 1)); b.path((5, 2), (4, 2))
    b.path((1, 1), (2, 1), (3, 1), (4, 1), (5, 1), (6, 1))
    b.path((6, 1), (6, 0)); b.path((6, 1), (6, 2))
    b.path((4, 1), (4, 0), (5, 0))
    b.path((3, 1), (3, 2))
    b.path((2, 1), (2, 2))
    b.path((1, 1), (1, 2)); b.path((1, 1), (1, 0), (0, 0), (0, 1), (0, 2))
    b.path((1, 0), (2, 0), (3, 0))

    # the lower grove, hung off the red heart alone, with the second bramble on its loop
    b.path((1, 3), (1, 4), (1, 5))
    b.path((1, 5), (0, 5), (0, 4)); b.path((0, 5), (0, 6), (1, 6))
    b.path((1, 5), (2, 5), (2, 6), (3, 6), (4, 6), (4, 5), (5, 5))
    b.path((2, 5), (3, 5), (4, 5))
    b.path((3, 5), (3, 6))
    b.path((5, 5), (5, 6), (6, 6)); b.path((5, 5), (6, 5), (6, 4), (5, 4), (4, 4))

    for p in [(0, 3), (6, 3), (0, 4), (4, 4), (5, 4), (1, 6), (6, 6),
              (0, 2), (1, 2), (2, 2), (4, 2), (6, 2), (3, 0), (5, 0), (6, 0)]:
        b.lamp(p[0], p[1], 'Y')

    b.spin(seed, bias)
    b.owe((3, 3), 1)
    return b
def g10(seed, bias):
    """The Nightbriar Knot 8x7 - everything the wood has taught, tied once.

    An amber ridge along the top whose two hearts are joined only through a pair of bound
    brambles; a blue foot along the bottom; two bridges carrying the blue up through a
    red lane that runs the whole width of the board; and, in the middle of it all, a green
    pair pinned between two more brambles - one standing in the lane itself, one brittle.
    """
    b = Board(8, 7)
    b.fill(0, 0, 8, 7)
    b.source(1, 1, 'R'); b.source(6, 1, 'G')
    b.source(3, 5, 'B')
    b.cross(2, 3, 'NS'); b.cross(5, 3, 'NS')
    b.briar(2, 1, 'EW', link='A')
    b.briar(5, 1, 'EW', link='A')
    b.briar(4, 3, 'EW')
    b.briar(4, 5, 'EW', fragile=2)
    b.source(3, 3, 'R'); b.lamp(0, 3, 'R'); b.lamp(7, 3, 'R')
    b.source(3, 4, 'G'); b.lamp(4, 4, 'G')

    # the amber ridge: red at one hand, green at the other, joined across both brambles
    b.path((0, 1), (1, 1), (2, 1), (3, 1), (4, 1), (5, 1), (6, 1), (7, 1))
    b.path((1, 1), (1, 0), (2, 0)); b.path((6, 1), (6, 0), (5, 0))
    b.path((4, 1), (4, 0), (3, 0))
    b.path((0, 1), (0, 0)); b.path((7, 1), (7, 0))
    b.path((0, 1), (0, 2), (1, 2)); b.path((7, 1), (7, 2), (6, 2))
    b.path((3, 1), (3, 2), (4, 2))

    # the red lane, running the width of the board under both bridges
    b.path((0, 3), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3), (6, 3), (7, 3))

    # the blue foot, and the two columns it sends up through the bridges
    b.path((0, 5), (1, 5), (2, 5), (3, 5), (4, 5), (5, 5), (6, 5), (7, 5))
    b.path((2, 5), (2, 4), (2, 3)); b.path((2, 3), (2, 2))
    b.path((5, 5), (5, 4), (5, 3)); b.path((5, 3), (5, 2))
    b.path((0, 5), (0, 4)); b.path((1, 5), (1, 4)); b.path((6, 5), (6, 4))
    b.path((7, 5), (7, 4)); b.path((3, 4), (4, 4))
    b.path((0, 5), (0, 6), (1, 6)); b.path((3, 5), (3, 6), (2, 6))
    b.path((3, 6), (4, 6)); b.path((7, 5), (7, 6), (6, 6), (5, 6))

    for p in [(0, 0), (2, 0), (3, 0), (5, 0), (7, 0), (1, 2), (6, 2), (4, 2)]:
        b.lamp(p[0], p[1], 'Y')
    for p in [(0, 4), (1, 4), (6, 4), (7, 4), (1, 6), (2, 6), (5, 6), (6, 6)]:
        b.lamp(p[0], p[1], 'B')

    b.spin(seed, bias)
    b.root('A', 1, (2, 1), (5, 1))
    b.owe((4, 5), 1)
    return b
PALETTE = {
    "c04_thornlight":        ("#F2B45C", "#1B1426"),
    "c04_the_shut_way":      ("#C77BFF", "#1A1030"),
    "c04_nightfall":         ("#5FD1B0", "#0F2027"),
    "c04_bramble_and_bridge": ("#FF8C6B", "#2A1420"),
    "c04_the_hollow_gate":   ("#9BB6FF", "#141A2E"),
    "c04_rootbriar":         ("#A8D94A", "#1A2413"),
    "c04_three_thorns":      ("#FF6FA8", "#2A1226"),
    "c04_the_long_dark":     ("#6E8BFF", "#101830"),
    "c04_wick_and_wane":     ("#FFD36B", "#241B12"),
    "c04_the_nightbriar_knot": ("#D96BFF", "#20122E"),
}


# Walked up the map, alternating sides. Six strips is 7200 canvas units tall, so the
# nearest pair is about 620 apart against a 220-unit floor.
MAPX = [0.30, 0.72, 0.26, 0.70, 0.28, 0.74, 0.30, 0.68, 0.26, 0.72]
MAPY = [0.055, 0.140, 0.225, 0.310, 0.395, 0.480, 0.560, 0.645, 0.730, 0.815]

TEXT = {
    "c04_thornlight": (
        "Thornlight",
        "Thorns close two ways, and a tap moves them"),
    "c04_the_shut_way": (
        "The Shut Way",
        "The only ground where red and blue may meet"),
    "c04_nightfall": (
        "Nightfall",
        "The loop forgives the turn; the green pocket does not"),
    "c04_bramble_and_bridge": (
        "Bramble and Bridge",
        "Four arms, two meanings"),
    "c04_the_hollow_gate": (
        "The Hollow Gate",
        "Brittle stone where guessing is all you have"),
    "c04_rootbriar": (
        "Rootbriar",
        "One rune, two brambles, opposite corners"),
    "c04_three_thorns": (
        "Three Thorns",
        "Three grooves, and every bramble a colour"),
    "c04_the_long_dark": (
        "The Long Lane",
        "A green lane under the groove, forded twice"),
    "c04_wick_and_wane": (
        "Wick and Wane",
        "A blend to make, and a blue pair to leave alone"),
    "c04_the_nightbriar_knot": (
        "The Nightbriar Knot",
        "Everything the wood has taught, tied once"),
}

# The ladder. Par is length rather than difficulty, so it is deliberately not monotonic -
# the taproot glade is the low point precisely because one tap moves two brambles and par
# charges for it once, which is the thing that glade is about.
BOARDS = [
    ("c04_thornlight", g1, 42),
    ("c04_the_shut_way", g2, 47),
    ("c04_nightfall", g3, 44),
    ("c04_bramble_and_bridge", g4, 52),
    ("c04_the_hollow_gate", g5, 49),
    ("c04_rootbriar", g6, 45),
    ("c04_three_thorns", g7, 58),
    ("c04_the_long_dark", g8, 54),
    ("c04_wick_and_wane", g9, 51),
    ("c04_the_nightbriar_knot", g10, 69),
]


def build():
    """Every glade, fitted to its target par and proved. Raises on any board that fails."""
    out = collections.OrderedDict()
    for lid, make, target in BOARDS:
        seed, bias, board = fit(make, target)
        errs, warns = board.check()
        if errs:
            raise SystemExit(f"{lid}: " + "; ".join(errs))
        out[lid] = (board, seed, bias, warns)
    return out


def chapter_json(built):
    accent, slate = PALETTE["c04_thornlight"]
    doc = collections.OrderedDict()
    doc["schemaVersion"] = 2
    doc["id"] = CHAPTER
    doc["accent"] = accent
    doc["slate"] = slate
    doc["backdrop"] = "c04_play_0"
    doc["mapStrips"] = [f"c04_strip{i}" for i in range(6)]
    doc["teaserX"] = 0.28

    levels = []
    for i, (lid, make, target) in enumerate(BOARDS):
        board = built[lid][0]
        level = collections.OrderedDict()
        level["id"] = lid
        level["width"] = board.w
        level["height"] = board.h
        level["mapX"] = MAPX[i]
        level["mapY"] = MAPY[i]
        if i > 0:                       # the first glade inherits the chapter's palette
            a, s = PALETTE[lid]
            level["accent"] = a
            level["slate"] = s
            level["backdrop"] = f"c04_play_{i}"
        level["rows"] = board.rows()
        levels.append(level)
    doc["levels"] = levels
    return doc


def write_strings():
    """Adds any missing loc key. Never rewrites one - a translation is not ours to clobber."""
    doc = json.load(io.open(LOC, encoding="utf-8"))
    have = {e["key"] for e in doc["entries"]}
    added = []

    def add(key, text):
        if key in have:
            return
        doc["entries"].append({"key": key, "text": text})
        have.add(key)
        added.append(key)

    add(f"chapter.{CHAPTER}.name", "The Nightbriar")
    for lid, _, _ in BOARDS:
        name, tagline = TEXT[lid]
        add(f"level.{lid}.name", name)
        add(f"level.{lid}.tagline", tagline)

    if added:
        with io.open(LOC, "w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
            f.write("\n")
    return added


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true",
                    help="compare the shipped chapter against this source instead of writing it")
    args = ap.parse_args()

    built = build()
    doc = chapter_json(built)

    for i, (lid, make, target) in enumerate(BOARDS):
        board, seed, bias, warns = built[lid]
        r = board.reading()
        print(f"{i + 1:>2} {lid:<26} {board.w}x{board.h} par {board.par():<3} "
              f"glance {len(r['glance']):>2}/{r['tiles']:<3} arms {r['solutions']:>3} "
              f"wins {r['wins']}  colour {r['colour_only']:>3}  "
              f"(seed {seed}, bias {bias})"
              + ("  WARN " + "; ".join(warns) if warns else ""))

    if args.check:
        shipped = json.load(io.open(BODY, encoding="utf-8"))
        if shipped == json.loads(json.dumps(doc)):
            print(f"\n{os.path.relpath(BODY, ROOT)} matches this source")
            return 0
        print(f"\n{os.path.relpath(BODY, ROOT)} DIFFERS from this source")
        for lid in [b[0] for b in BOARDS]:
            was = next((l for l in shipped.get("levels", []) if l.get("id") == lid), None)
            now = next(l for l in doc["levels"] if l["id"] == lid)
            if was != now:
                for key in now:
                    if was is None or was.get(key) != now[key]:
                        print(f"  {lid}.{key}")
        print("\nEdit this file and re-run without --check, or accept the JSON and update this.")
        return 1

    with io.open(BODY, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"\nwrote {os.path.relpath(BODY, ROOT)}")
    added = write_strings()
    print(f"added {len(added)} string(s) to {os.path.relpath(LOC, ROOT)}")
    print("\nNext: Content > Sync Manifest, then Validate Content.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
