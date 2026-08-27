"""The Mill Vale - the ten boards of chapter two, as they were rebuilt.

**The shipped chapter is `Content/chapters/c02_millvale.json`, not this file.** That is
what the game reads and what the build gate proves; nothing at runtime knows this script
exists. Same bargain as `c03_amberwood.py`, and the same two commands:

    python Tools/chapters/c02_millvale.py --check     # does the shipped JSON still match?
    python Tools/chapters/c02_millvale.py             # rewrite it from here

The chapter shipped once already, and every glade below keeps its id, its name and its
subject. What changed is the ground under them, for a reason that was measured rather than
felt - see `Tools/verify/difficulty.py`. Two findings drove the whole rebuild.

**The boards were open ground.** Every glade was a corridor a tile or two wide with empty
cells either side, and an arm with nowhere to go is an arm nobody has to think about. Fill
the ground and a tile with four neighbours has four candidate orientations; `glance` counts
the tiles a player cannot place by looking at them, and it is the difference between a
puzzle and a dot-to-dot.

**And no mechanic on them rejected anything.** Counted, twenty-two of the game's thirty
glades had exactly *one* arrangement in which every arm mated - so the brittle stone, the
taproots and the pools could all have been deleted without changing a single solution.
A twisted crossing is the cheapest honest decision this board can carry, because it wears
all four arms at every angle: nothing about the arms can settle it and only colour can.
Every other mechanic here now rides on that. Brittle stone sits on a crossing, so it asks a
player who *cannot* simply try the tile. A taproot binds two of them, so one tap answers
two corners of the board. And a pool's ford is placed on a **cycle** of the live network,
which is the whole difference between a pool that matters and a pool that is scenery:
turning that ford pours the grove's colour into the pool without breaking anything, so
every critter on the ring stays lit and only the pool's own go out. That is a wrong turn
whose warning is somewhere the player is not looking, which is as much as one tile can ask
without lying to them.

The numbers each board lands on are printed by `report()` when this file is run.
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

CHAPTER = "c02_millvale"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")
LOC = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")


def g1(seed, bias):
    """Two Ways Over 6x6 - one crossing, and it is the whole glade.

    Red runs east and turns north; blue runs north and turns east; they do it in the same
    tile. Turning that tile pours both hearts into one channel, and it is the only thing on
    the board that can go wrong - which is why it is the only mechanic on it.
    """
    b = Board(6, 6)
    b.fill(0, 0, 6, 6)
    b.source(0, 2, 'R')
    b.source(3, 5, 'B')
    b.cross(3, 2, 'NW')
    b.path((0, 2), (1, 2), (2, 2), (3, 2))
    b.path((3, 2), (3, 1), (3, 0), (4, 0), (5, 0), (5, 1))
    b.path((0, 2), (0, 1), (0, 0), (1, 0), (2, 0))
    b.path((1, 0), (1, 1))
    b.path((2, 2), (2, 1))
    b.path((4, 0), (4, 1))
    for p in [(2, 0), (1, 1), (2, 1), (4, 1), (5, 1)]:
        b.lamp(p[0], p[1], 'R')
    b.path((3, 5), (3, 4), (3, 3), (3, 2))
    b.path((3, 2), (4, 2), (5, 2), (5, 3))
    b.path((4, 2), (4, 3))
    b.path((3, 5), (2, 5), (1, 5), (0, 5), (0, 4), (0, 3))
    b.path((1, 5), (1, 4), (1, 3))
    b.path((2, 5), (2, 4), (2, 3))
    b.path((3, 5), (4, 5), (5, 5), (5, 4))
    b.path((4, 5), (4, 4))
    for p in [(5, 3), (4, 3), (0, 3), (1, 3), (2, 3), (5, 4), (4, 4)]:
        b.lamp(p[0], p[1], 'B')
    b.spin(seed, bias)
    b.owe((3, 2), 1)                     # never author the one decision already made
    return b


def g2(seed, bias):
    """The Millrace 6x7 - a rope of two runs, cornering through one another three times.

    Three twisted crossings is three independent decisions, so eight arrangements mate
    every arm and exactly one of them keeps the two hearts apart. That ratio is the glade.
    """
    b = Board(6, 7)
    b.fill(0, 0, 6, 7)
    b.source(0, 0, 'R')
    b.source(0, 4, 'B')
    for p in [(1, 1), (3, 3), (4, 5)]:
        b.cross(p[0], p[1], 'NE')        # red corners north to east, blue west to south
    b.path((0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (5, 1), (5, 2), (5, 3))
    b.path((1, 0), (1, 1))
    b.path((1, 1), (2, 1), (3, 1), (3, 2), (3, 3))
    b.path((3, 3), (4, 3), (4, 4), (4, 5))
    b.path((4, 5), (5, 5), (5, 6))
    b.path((4, 0), (4, 1)); b.path((2, 1), (2, 2)); b.path((5, 2), (4, 2))
    b.path((5, 3), (5, 4))
    for p in [(4, 1), (2, 2), (4, 2), (5, 4), (5, 6)]:
        b.lamp(p[0], p[1], 'R')
    b.path((0, 4), (0, 3), (0, 2), (0, 1), (1, 1))
    b.path((1, 1), (1, 2), (1, 3), (2, 3), (3, 3))
    b.path((3, 3), (3, 4), (3, 5), (4, 5))
    b.path((4, 5), (4, 6))
    b.path((0, 4), (0, 5), (0, 6), (1, 6), (2, 6), (3, 6))
    b.path((1, 3), (1, 4), (1, 5))
    b.path((1, 4), (2, 4)); b.path((1, 5), (2, 5))
    for p in [(2, 4), (2, 5), (3, 6), (4, 6)]:
        b.lamp(p[0], p[1], 'B')
    b.spin(seed, bias)
    for p in [(1, 1), (3, 3), (4, 5)]:
        b.owe(p, 1)
    return b


def g3(seed, bias):
    """Under the Boughs 7x6 - a green pool inside the grove's own tiles.

    The grove closes in a ring one tile inside the edge, so both fords sit on a cycle:
    turn one and the grove pours into the pool without a critter on the ring going out. That is
    the first arrangement in this game that looks finished and will not settle, and
    building one is the entire point of the lesson. Everything outside the ring is a short
    chain ending in a critter, which is also what keeps the board off the edges - an arm
    with nowhere to go is an arm nobody has to think about.
    """
    b = Board(7, 6)
    b.fill(0, 0, 7, 6)
    b.source(1, 1, 'R')
    b.cross(2, 2, 'NW'); b.cross(4, 3, 'SE')

    ring = ([(x, 1) for x in range(1, 6)] + [(5, y) for y in range(2, 5)]
            + [(x, 4) for x in range(4, 0, -1)] + [(1, 3), (1, 2), (1, 1)])
    b.path(*ring)
    b.path((2, 1), (2, 2)); b.path((1, 2), (2, 2))
    b.path((4, 4), (4, 3)); b.path((5, 3), (4, 3))

    b.path((1, 1), (1, 0), (0, 0), (0, 1))
    b.path((3, 1), (3, 0), (2, 0))
    b.path((4, 1), (4, 0), (5, 0))
    b.path((5, 1), (6, 1), (6, 0))
    b.path((5, 2), (6, 2))
    b.path((5, 4), (5, 5), (6, 5)); b.path((6, 5), (6, 4), (6, 3))
    b.path((3, 4), (3, 5), (4, 5))
    b.path((2, 4), (2, 5), (1, 5), (0, 5))
    b.path((1, 4), (0, 4))
    b.path((1, 3), (0, 3), (0, 2))
    for p in [(2, 0), (5, 0), (6, 2), (6, 3), (4, 5), (0, 4)]:
        b.lamp(p[0], p[1], 'R')
    for p in [(0, 1), (6, 0), (0, 5), (0, 2)]:
        b.lamp(p[0], p[1], 'A')

    b.source(3, 2, 'G'); b.lamp(4, 2, 'G'); b.lamp(2, 3, 'G')
    b.path((2, 2), (3, 2), (4, 2), (4, 3)); b.path((3, 2), (3, 3))
    b.path((2, 2), (2, 3)); b.path((2, 3), (3, 3)); b.path((3, 3), (4, 3))
    b.spin(seed, bias)
    b.owe((2, 2), 1); b.owe((4, 3), 1)
    return b

def g4(seed, bias):
    """The Old Weir 7x6 - brittle stone on the two tiles nothing but colour can settle.

    Brittle stone asks nothing of a player who can simply try the tile, so both crumbling
    conduits here are crossings: an arm mask says nothing about which way a crossing faces,
    so the only way to know is to read the two hearts. Each survives two turns and needs
    one, which is exactly one wrong guess and no more.
    """
    b = Board(7, 6)
    b.fill(0, 0, 7, 6)
    b.source(1, 1, 'R')
    b.source(1, 4, 'G')
    b.cross(2, 2, 'NE', fragile=2)       # red corners north-east, green west-south
    b.cross(4, 3, 'NW', fragile=2)       # red corners north-west, green east-south

    b.path((1, 1), (2, 1), (3, 1), (4, 1), (5, 1))
    b.path((2, 1), (2, 2)); b.path((2, 2), (3, 2), (3, 3), (4, 3))
    b.path((4, 3), (4, 2), (4, 1))
    b.path((1, 1), (1, 0), (0, 0), (0, 1))
    b.path((3, 1), (3, 0)); b.path((2, 0), (3, 0), (4, 0))
    b.path((5, 1), (5, 0), (6, 0)); b.path((5, 1), (6, 1))
    for p in [(0, 1), (2, 0), (4, 0), (6, 1)]:
        b.lamp(p[0], p[1], 'R')
    b.lamp(6, 0, 'A')

    b.path((1, 4), (2, 4), (3, 4), (4, 4), (5, 4))
    b.path((4, 4), (4, 3)); b.path((4, 3), (5, 3), (5, 4))
    b.path((2, 2), (1, 2), (1, 3), (1, 4))
    b.path((2, 2), (2, 3), (2, 4))
    b.path((5, 3), (5, 2))
    b.path((1, 2), (0, 2)); b.path((1, 3), (0, 3))
    b.path((1, 4), (0, 4), (0, 5), (1, 5), (2, 5))
    b.path((3, 4), (3, 5), (4, 5))
    b.path((5, 4), (6, 4), (6, 3), (6, 2))
    b.path((6, 4), (6, 5), (5, 5))
    for p in [(0, 2), (0, 3), (2, 5), (6, 2), (5, 2)]:
        b.lamp(p[0], p[1], 'G')
    for p in [(4, 5), (5, 5)]:
        b.lamp(p[0], p[1], 'A')

    b.spin(seed, bias)
    b.owe((2, 2), 1); b.owe((4, 3), 1)
    return b

def g5(seed, bias):
    """Braided Water 7x7 - two crossings on one root, at opposite corners of the braid.

    A taproot only asks anything when its members are tiles the arms cannot settle, so both
    members here are crossings. Bound, the two ends of the braid are one decision and the
    tap that answers the near one answers the far one with it; the third braid between them
    is free, so the board is four arrangements rather than eight.
    """
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(1, 1, 'R')
    b.source(5, 5, 'B')
    b.cross(2, 2, 'NE', link='A')
    b.cross(4, 4, 'NE', link='A')
    b.cross(3, 3, 'NE')

    b.path((1, 1), (2, 1), (3, 1), (4, 1), (5, 1))
    b.path((2, 1), (2, 2)); b.path((2, 2), (3, 2), (3, 3))
    b.path((3, 3), (4, 3), (4, 4))
    b.path((4, 4), (5, 4), (5, 3), (5, 2), (5, 1))
    b.path((4, 1), (4, 2))
    b.path((1, 1), (1, 0), (0, 0), (0, 1))
    b.path((3, 1), (3, 0)); b.path((2, 0), (3, 0), (4, 0))
    b.path((5, 1), (5, 0), (6, 0), (6, 1))
    b.path((5, 2), (6, 2)); b.path((5, 3), (6, 3)); b.path((5, 4), (6, 4))
    for p in [(4, 2), (2, 0), (4, 0), (6, 2), (6, 4)]:
        b.lamp(p[0], p[1], 'R')
    for p in [(0, 1), (6, 1), (6, 3)]:
        b.lamp(p[0], p[1], 'A')

    b.path((1, 5), (2, 5), (3, 5), (4, 5), (5, 5))
    b.path((2, 2), (1, 2), (1, 3), (1, 4), (1, 5))
    b.path((2, 2), (2, 3), (3, 3)); b.path((3, 3), (3, 4), (4, 4))
    b.path((4, 4), (4, 5))
    b.path((2, 5), (2, 4))
    b.path((1, 2), (0, 2)); b.path((1, 3), (0, 3)); b.path((1, 4), (0, 4))
    b.path((1, 5), (1, 6), (0, 6), (0, 5))
    b.path((3, 5), (3, 6)); b.path((2, 6), (3, 6), (4, 6))
    b.path((5, 5), (6, 5), (6, 6), (5, 6))
    for p in [(2, 4), (0, 2), (0, 4), (2, 6), (4, 6)]:
        b.lamp(p[0], p[1], 'B')
    for p in [(0, 3), (0, 5), (5, 6)]:
        b.lamp(p[0], p[1], 'A')

    b.spin(seed, bias)
    b.root('A', 1, (2, 2), (4, 4))
    b.owe((3, 3), 1)
    return b

def g6(seed, bias):
    """The Wheelhouse 7x7 - two hearts kept pure, two more allowed to meet.

    A crossing turned the wrong way here does not merely blend: it blends the pair that
    were meant to stay apart while starving the pair that were meant to meet.
    """
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(0, 0, 'R')                  # pure red, down the left
    b.source(6, 0, 'B')                  # pure blue, down the right
    b.source(0, 6, 'R')                  # and the wheel's own pair
    b.source(6, 6, 'B')
    b.cross(4, 2, 'NE')                  # wheel across blue
    b.cross(3, 4, 'NW')                  # wheel across red
    b.cross(1, 5, 'NW')                  # and across red again

    b.path(*[(x, 6) for x in range(7)])
    b.path((3, 6), (3, 5), (3, 4))
    b.path((3, 4), (4, 4), (4, 3), (4, 2))
    b.path((4, 2), (3, 2), (3, 1), (3, 0))
    b.path((1, 6), (1, 5))
    b.path((1, 5), (2, 5))
    b.path((4, 4), (4, 5)); b.path((5, 6), (5, 5))
    for p in [(3, 0), (2, 5), (4, 5), (5, 5)]:
        b.lamp(p[0], p[1], 'M')

    b.path((0, 0), (1, 0), (2, 0))
    b.path((0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (0, 5), (1, 5))
    b.path((0, 3), (1, 3), (2, 3), (3, 3), (3, 4))
    b.path((0, 4), (1, 4), (1, 5))
    b.path((1, 4), (2, 4), (3, 4))
    b.path((1, 0), (1, 1), (1, 2)); b.path((2, 0), (2, 1))
    b.path((2, 3), (2, 2))
    for p in [(1, 2), (2, 2), (2, 1)]:
        b.lamp(p[0], p[1], 'R')

    b.path((6, 0), (5, 0), (4, 0))
    b.path((6, 0), (6, 1), (6, 2), (6, 3), (6, 4), (6, 5))
    b.path((6, 2), (5, 2), (4, 2))
    b.path((4, 2), (4, 1), (4, 0))
    b.path((5, 0), (5, 1)); b.path((6, 4), (5, 4)); b.path((6, 3), (5, 3))
    for p in [(5, 1), (5, 3), (5, 4)]:
        b.lamp(p[0], p[1], 'B')

    b.spin(seed, bias)
    for p in [(4, 2), (3, 4), (1, 5)]:
        b.owe(p, 1)
    return b


def g7(seed, bias):
    """Hollow Ford 7x7 - one green pool in the middle, forded three times.

    Every ford carries a strand of the pool and a strand of the grove, and the grove's two
    arms at each of them come back together round the ring. So a ford turned the wrong way
    ruins the pool while every critter on the ring stays lit: seven arrangements that mate
    every arm, light every critter, and are not finished.
    """
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(1, 1, 'R')
    b.cross(2, 2, 'NW'); b.cross(4, 2, 'NE'); b.cross(2, 4, 'SW')

    ring = ([(x, 1) for x in range(1, 6)] + [(5, y) for y in range(2, 6)]
            + [(x, 5) for x in range(4, 0, -1)] + [(1, y) for y in range(4, 0, -1)])
    b.path(*ring)
    b.path((2, 1), (2, 2)); b.path((1, 2), (2, 2))
    b.path((4, 1), (4, 2)); b.path((5, 2), (4, 2))
    b.path((2, 5), (2, 4)); b.path((1, 4), (2, 4))

    b.path((1, 1), (1, 0), (0, 0), (0, 1))
    b.path((3, 1), (3, 0)); b.path((2, 0), (3, 0), (4, 0))
    b.path((5, 1), (5, 0), (6, 0), (6, 1))
    b.path((5, 3), (6, 3)); b.path((6, 2), (6, 3), (6, 4))
    b.path((5, 5), (6, 5), (6, 6), (5, 6))
    b.path((3, 5), (3, 6)); b.path((2, 6), (3, 6), (4, 6))
    b.path((1, 5), (1, 6), (0, 6), (0, 5))
    b.path((1, 3), (0, 3)); b.path((0, 2), (0, 3), (0, 4))
    for p in [(2, 0), (4, 0), (6, 2), (6, 4), (2, 6), (4, 6), (0, 2), (0, 4)]:
        b.lamp(p[0], p[1], 'R')
    for p in [(0, 1), (6, 1), (5, 6), (0, 5)]:
        b.lamp(p[0], p[1], 'A')

    b.source(4, 3, 'G'); b.lamp(3, 3, 'G'); b.lamp(4, 4, 'G')
    b.path((2, 2), (3, 2)); b.path((3, 2), (4, 2)); b.path((3, 2), (3, 3))
    b.path((2, 2), (2, 3)); b.path((2, 3), (2, 4)); b.path((2, 3), (3, 3))
    b.path((4, 2), (4, 3), (4, 4), (3, 4), (3, 3))
    b.path((2, 4), (3, 4))
    b.spin(seed, bias)
    for p in [(2, 2), (4, 2), (2, 4)]:
        b.owe(p, 1)
    return b

def g8(seed, bias):
    """Stonebridge 7x7 - four bridges nobody can turn, and two spans that must be.

    A straight crossing is inert at every angle, so rooting one says out loud what it
    already was: architecture. What the vale asks about is the two spans beside them.
    """
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(0, 0, 'R')
    b.source(0, 3, 'G')
    for p in [(1, 2), (3, 2), (1, 4), (3, 4)]:
        b.cross(p[0], p[1], 'NS', locked=True)
    b.cross(5, 2, 'NE')
    b.cross(5, 4, 'NW')

    b.path(*[(x, 0) for x in range(7)])
    b.path((0, 0), (0, 1))
    for x in (1, 3):
        b.path(*[(x, y) for y in range(7)])
    b.path((5, 0), (5, 1), (5, 2))
    b.path((5, 2), (6, 2), (6, 3), (6, 4), (5, 4))
    b.path((5, 4), (5, 5), (5, 6))
    b.path((1, 3), (2, 3)); b.path((1, 5), (2, 5)); b.path((3, 5), (4, 5))
    b.path((1, 6), (0, 6)); b.path((1, 6), (2, 6)); b.path((3, 6), (4, 6))
    b.path((5, 6), (6, 6)); b.path((6, 4), (6, 5))
    b.path((2, 0), (2, 1)); b.path((4, 0), (4, 1)); b.path((6, 0), (6, 1))
    for p in [(0, 1), (2, 1), (6, 1), (2, 3), (2, 5), (0, 6), (4, 6), (6, 5)]:
        b.lamp(p[0], p[1], 'R')
    for p in [(4, 1), (4, 5), (2, 6), (6, 6)]:
        b.lamp(p[0], p[1], 'A')

    b.path((0, 3), (0, 2), (1, 2))
    b.path((1, 2), (2, 2), (3, 2))
    b.path((3, 2), (4, 2), (5, 2))
    b.path((5, 2), (5, 3), (5, 4))
    b.path((5, 4), (4, 4), (3, 4))
    b.path((3, 4), (2, 4), (1, 4))
    b.path((1, 4), (0, 4), (0, 3))
    b.path((0, 4), (0, 5)); b.path((4, 2), (4, 3))
    for p in [(0, 5), (4, 3)]:
        b.lamp(p[0], p[1], 'G')

    b.spin(seed, bias)
    b.owe((5, 2), 1); b.owe((5, 4), 1)
    return b


def g9(seed, bias):
    """Three Bridges 8x7 - a cascade, handed on inside one tile at every span."""
    b = Board(8, 7)
    b.fill(0, 0, 8, 7)
    b.source(0, 0, 'R')                  # the amber spring, with its green partner
    b.source(0, 2, 'G')
    b.source(0, 4, 'R')                  # pure red
    b.source(4, 6, 'G')                  # pure green
    b.source(7, 6, 'B')                  # pure blue
    b.cross(2, 2, 'NE')                  # amber over red
    b.cross(4, 3, 'NW')                  # red over green
    b.cross(6, 4, 'NW')                  # green over blue

    b.path((0, 2), (0, 1), (0, 0), (1, 0), (2, 0), (2, 1), (2, 2))
    b.path((2, 2), (3, 2), (3, 1), (3, 0), (4, 0), (5, 0), (6, 0), (7, 0))
    b.path((1, 0), (1, 1)); b.path((5, 0), (5, 1)); b.path((4, 0), (4, 1))
    b.path((6, 0), (6, 1)); b.path((7, 0), (7, 1))
    for p in [(1, 1), (4, 1), (6, 1)]:
        b.lamp(p[0], p[1], 'Y')
    for p in [(5, 1), (7, 1)]:
        b.lamp(p[0], p[1], 'A')

    b.path((0, 4), (0, 3), (1, 3), (1, 2), (2, 2))
    b.path((2, 2), (2, 3), (2, 4), (3, 4), (3, 3), (4, 3))
    b.path((4, 3), (4, 2), (5, 2), (6, 2))
    b.path((0, 4), (1, 4)); b.path((2, 4), (2, 5))
    for p in [(1, 4), (2, 5), (6, 2)]:
        b.lamp(p[0], p[1], 'R')

    b.path((4, 6), (4, 5), (4, 4), (4, 3))
    b.path((4, 3), (5, 3), (5, 4), (6, 4))
    b.path((6, 4), (6, 3), (7, 3), (7, 2))
    b.path((4, 6), (3, 6), (2, 6), (1, 6), (0, 6), (0, 5))
    b.path((3, 6), (3, 5)); b.path((1, 6), (1, 5))
    for p in [(0, 5), (3, 5), (1, 5), (7, 2)]:
        b.lamp(p[0], p[1], 'G')

    b.path((7, 6), (6, 6), (6, 5), (6, 4))
    b.path((6, 4), (7, 4), (7, 5), (7, 6))
    b.path((6, 6), (5, 6)); b.path((6, 5), (5, 5))
    for p in [(5, 6), (5, 5)]:
        b.lamp(p[0], p[1], 'B')

    b.spin(seed, bias)
    for p in [(2, 2), (4, 3), (6, 4)]:
        b.owe(p, 1)
    return b


def g10(seed, bias):
    """The Miller's Knot 8x7 - everything the vale has taught, tied once.

    Four spans on the braid: two of them one root, two of them brittle, and a fifth over a
    green pool. Sixteen arrangements mate every arm; one settles the glade.
    """
    b = Board(8, 7)
    b.fill(0, 0, 8, 7)
    b.source(0, 0, 'R')
    b.source(0, 6, 'B')
    b.source(7, 1, 'R')                  # the wheel's own pair, up in the corner
    b.source(7, 3, 'B')
    b.cross(1, 1, 'NE', link='A')
    b.cross(6, 5, 'NE', link='A')
    b.cross(3, 2, 'NE', fragile=2)
    b.cross(5, 4, 'NE', fragile=2)
    b.cross(2, 5, 'NW')                  # the ford, on a cycle of the blue

    b.path(*[(x, 0) for x in range(8)])
    b.path((1, 0), (1, 1))
    b.path((1, 1), (2, 1), (3, 1), (3, 2))
    b.path((3, 2), (4, 2), (4, 3), (5, 3), (5, 4))
    b.path((5, 4), (6, 4), (6, 5))
    b.path((6, 5), (7, 5), (7, 6))
    b.path((4, 0), (4, 1)); b.path((5, 0), (5, 1)); b.path((5, 3), (5, 2))
    b.path((6, 0), (6, 1))
    for p in [(4, 1), (5, 2), (7, 6)]:
        b.lamp(p[0], p[1], 'R')
    for p in [(5, 1), (6, 1)]:
        b.lamp(p[0], p[1], 'A')

    b.path(*[(x, 6) for x in range(6, -1, -1)])
    b.path((0, 6), (0, 5), (0, 4), (0, 3), (0, 2), (0, 1), (1, 1))
    b.path((1, 1), (1, 2), (2, 2), (3, 2))
    b.path((3, 2), (3, 3), (3, 4), (4, 4), (5, 4))
    b.path((5, 4), (5, 5), (6, 5))
    b.path((6, 5), (6, 6))
    b.path((1, 2), (1, 3)); b.path((2, 2), (2, 3))
    b.path((3, 5), (2, 5)); b.path((3, 5), (3, 6))
    b.path((0, 3), (0, 2)); b.path((4, 6), (4, 5))
    for p in [(1, 3), (2, 3), (4, 5)]:
        b.lamp(p[0], p[1], 'B')

    b.path((7, 1), (7, 2), (7, 3))
    b.path((7, 2), (6, 2), (6, 3))
    b.path((7, 3), (7, 4))
    for p in [(6, 3), (7, 4)]:
        b.lamp(p[0], p[1], 'M')

    b.source(2, 4, 'G'); b.lamp(1, 4, 'G'); b.lamp(1, 5, 'G')
    b.path((2, 4), (1, 4), (1, 5), (2, 5))
    b.path((2, 5), (2, 4)); b.path((2, 5), (2, 6))
    b.spin(seed, bias)
    b.root('A', 1, (1, 1), (6, 5))
    for p in [(3, 2), (5, 4), (2, 5)]:
        b.owe(p, 1)
    return b


# ---------------------------------------------------------------- the chapter
# Palette, art and map positions are unchanged from the chapter that shipped: the boards
PALETTE = {
    "c02_two_ways_over": ("#7ED957", "#16301C"),
    "c02_the_millrace": ("#4FC1FF", "#0E2A3A"),
    "c02_under_the_boughs": ("#A98BFF", "#1E1A38"),
    "c02_the_old_weir": ("#FFB347", "#33220F"),
    "c02_braided_water": ("#63C7C0", "#10262B"),
    "c02_the_wheelhouse": ("#FF74D4", "#2B1436"),
    "c02_hollow_ford": ("#8FD694", "#14251A"),
    "c02_stonebridge": ("#D8CBA6", "#2A2A24"),
    "c02_three_bridges": ("#FFD75E", "#332812"),
    "c02_the_millers_knot": ("#E86A5A", "#3A1A18"),
}

MAPX = [0.30, 0.70, 0.26, 0.72, 0.26, 0.68, 0.28, 0.72, 0.23, 0.71]
MAPY = [0.065, 0.145, 0.220, 0.300, 0.390, 0.485, 0.560, 0.650, 0.752, 0.830]

TEXT = {
    "c02_two_ways_over": (
        "Two Ways Over",
        "One tile, and two streams that never meet"),
    "c02_the_millrace": (
        "The Millrace",
        "A rope of light, twisted three times"),
    "c02_under_the_boughs": (
        "Under the Boughs",
        "A green pool inside the red ring"),
    "c02_the_old_weir": (
        "The Old Weir",
        "Brittle stone on both approaches"),
    "c02_braided_water": (
        "Braided Water",
        "Two crossings, one root, opposite corners"),
    "c02_the_wheelhouse": (
        "The Wheelhouse",
        "Kept apart here, blended there"),
    "c02_hollow_ford": (
        "Hollow Ford",
        "The green pool, forded three times"),
    "c02_stonebridge": (
        "Stonebridge",
        "Four bridges nobody can turn"),
    "c02_three_bridges": (
        "Three Bridges",
        "A cascade, handed on at every span"),
    "c02_the_millers_knot": (
        "The Miller's Knot",
        "Everything the vale has taught, tied once"),
}

BOARDS = [
    ("c02_two_ways_over", g1, 36),
    ("c02_the_millrace", g2, 48),
    ("c02_under_the_boughs", g3, 41),
    ("c02_the_old_weir", g4, 52),
    ("c02_braided_water", g5, 43),
    ("c02_the_wheelhouse", g6, 58),
    ("c02_hollow_ford", g7, 49),
    ("c02_stonebridge", g8, 55),
    ("c02_three_bridges", g9, 51),
    ("c02_the_millers_knot", g10, 63),
]


def build():
    out = collections.OrderedDict()
    for lid, make, target in BOARDS:
        seed, bias, board = fit(make, target)
        errs, warns = board.check()
        if errs:
            raise SystemExit(f"{lid}: " + "; ".join(errs))
        out[lid] = (board, seed, bias, warns)
    return out


def chapter_json(built):
    doc = collections.OrderedDict()
    doc["schemaVersion"] = 2
    doc["id"] = CHAPTER
    doc["accent"] = "#9BD84A"
    doc["slate"] = "#17301E"
    doc["backdrop"] = "c02_play_0"
    doc["mapStrips"] = [f"c02_strip{i}" for i in range(4)]
    doc["teaserX"] = 0.3

    levels = []
    for i, (lid, make, target) in enumerate(BOARDS):
        board = built[lid][0]
        level = collections.OrderedDict()
        level["id"] = lid
        level["width"] = board.w
        level["height"] = board.h
        level["mapX"] = MAPX[i]
        level["mapY"] = MAPY[i]
        accent, slate = PALETTE[lid]
        level["accent"] = accent
        level["slate"] = slate
        if i > 0:                       # the first glade inherits the chapter's backdrop
            level["backdrop"] = f"c02_play_{i}"
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

    add(f"chapter.{CHAPTER}.name", "The Mill Vale")
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
              f"glance {len(r['glance']):>2}/{r['tiles']:<3} arms {r['solutions']:>2} "
              f"wins {r['wins']}  colour {r['colour_only']:>2}  "
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
