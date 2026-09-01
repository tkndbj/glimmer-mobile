"""The Amberwood - the ten boards of chapter three, as they were built.

**The shipped chapter is `Content/chapters/c03_amberwood.json`, not this file.** That is
what the game reads, what the validators judge and what the build gate proves; nothing at
runtime knows this script exists, and the first two chapters have no equivalent.

What it is for is the next edit. A board written as a grid of tokens can only be retuned
by hand, one arm mask and one rotation at a time, and every one of them has to keep
agreeing with its neighbours - which is exactly the class of mistake `author.py` exists to
refuse. Written as runs and joins, moving a conduit re-derives every mask, every rotation
and par with it. Ten glades of colour-separation were not authorable any other way inside
a day, and the next chapter that wants to be will not be either.

The two can drift, so ask:

    python Tools/chapters/c03_amberwood.py --check     # does the shipped JSON still match?
    python Tools/chapters/c03_amberwood.py             # rewrite it from here

`--check` is deliberately **not** wired into the build gate. The gate's job is to prove the
shipped content is sound, which `ContentValidation` already does from the JSON alone - and
making it demand a Python source would make a chapter without one unbuildable, which is two
thirds of the ones that have shipped. Run it when you have edited either side.

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
sys.path.insert(0, HERE)

from author import Board, fit                                    # noqa: E402
import mapart                                                    # noqa: E402

CHAPTER = "c03_amberwood"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")
LOC = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")


def g1(seed, bias):
    """Amberlight 6x6 - amber needs a pair of springs of its own."""
    b = Board(6, 6)
    b.fill(0, 0, 6, 6)
    b.source(1, 1, 'R')                  # the pure red spring
    b.source(4, 4, 'G')                  # the pure green one
    b.source(1, 2, 'R')                  # and amber's own pair
    b.source(4, 3, 'G')
    b.cross(2, 2, 'NE')

    # pure red, along the first row inside the top
    b.path((1, 1), (2, 1), (3, 1), (4, 1))
    b.path((2, 1), (2, 2)); b.path((2, 2), (3, 2))
    b.path((1, 1), (1, 0), (0, 0), (0, 1))
    b.path((3, 1), (3, 0)); b.path((2, 0), (3, 0), (4, 0))
    b.path((4, 1), (5, 1), (5, 0))
    for p in [(0, 1), (2, 0), (4, 0), (3, 2)]:
        b.lamp(p[0], p[1], 'R')
    b.lamp(5, 0, 'A')

    # pure green, along the last row inside the foot
    b.path((1, 4), (2, 4), (3, 4), (4, 4))
    b.path((1, 4), (0, 4), (0, 5), (1, 5))
    b.path((2, 4), (2, 5)); b.path((3, 4), (3, 5))
    b.path((4, 4), (4, 5), (5, 5)); b.path((4, 4), (5, 4))
    for p in [(1, 5), (3, 5), (5, 4)]:
        b.lamp(p[0], p[1], 'G')
    for p in [(2, 5), (5, 5)]:
        b.lamp(p[0], p[1], 'A')

    # amber: its own two hearts, and the only ground where red and green may meet
    b.path((1, 2), (2, 2))
    b.path((2, 2), (2, 3), (3, 3), (4, 3))
    b.path((4, 3), (4, 2)); b.path((2, 3), (1, 3))
    b.path((1, 2), (0, 2), (0, 3))
    b.path((4, 2), (5, 2), (5, 3))
    for p in [(1, 3), (0, 3), (5, 3)]:
        b.lamp(p[0], p[1], 'Y')
    b.spin(seed, bias)
    b.owe((2, 2), 1)
    return b


def g2(seed, bias):
    """Two Lights, One Lane 6x7 - both lanes turn where they cross."""
    b = Board(6, 7)
    b.fill(0, 0, 6, 7)
    b.source(1, 1, 'R')                  # amber's pair
    b.source(4, 1, 'G')
    b.source(1, 5, 'R')                  # the red lane, which must stay red
    b.cross(2, 2, 'NE'); b.cross(3, 4, 'NW')

    # amber: a lane one row inside the top, cornering into both crossings
    b.path((1, 1), (2, 1), (3, 1), (4, 1))
    b.path((2, 1), (2, 2)); b.path((2, 2), (3, 2), (3, 3), (3, 4))
    b.path((3, 4), (2, 4))
    b.path((1, 1), (1, 0), (0, 0), (0, 1))
    b.path((3, 1), (3, 0)); b.path((2, 0), (3, 0), (4, 0))
    b.path((4, 1), (4, 2)); b.path((4, 1), (5, 1), (5, 0))
    b.path((4, 2), (5, 2))
    for p in [(2, 0), (4, 0), (5, 2), (2, 4)]:
        b.lamp(p[0], p[1], 'Y')
    for p in [(0, 1), (5, 0)]:
        b.lamp(p[0], p[1], 'A')

    # red, pure: the other strand of both, along the foot
    b.path((1, 5), (2, 5), (3, 5), (4, 5))
    b.path((2, 2), (1, 2), (1, 3), (1, 4), (1, 5))
    b.path((2, 2), (2, 3), (1, 3))
    b.path((3, 4), (3, 5))
    b.path((3, 4), (4, 4), (4, 5))
    b.path((1, 2), (0, 2)); b.path((1, 3), (0, 3)); b.path((1, 4), (0, 4))
    b.path((1, 5), (1, 6), (0, 6), (0, 5))
    b.path((2, 5), (2, 6)); b.path((3, 5), (3, 6), (4, 6))
    b.path((4, 5), (5, 5), (5, 6)); b.path((5, 5), (5, 4), (5, 3))
    b.path((4, 4), (4, 3))
    for p in [(0, 2), (0, 4), (2, 6), (4, 6), (5, 6), (4, 3)]:
        b.lamp(p[0], p[1], 'R')
    for p in [(0, 3), (0, 5), (5, 3)]:
        b.lamp(p[0], p[1], 'A')
    b.spin(seed, bias)
    b.owe((2, 2), 1); b.owe((3, 4), 1)
    return b


def g3(seed, bias):
    """The Verdigris 7x6 - teal between a blue comb and a green one."""
    b = Board(7, 6)
    b.fill(0, 0, 7, 6)
    b.source(1, 1, 'B')                  # the pure blue comb
    b.source(1, 4, 'G')                  # the pure green one
    b.source(1, 3, 'G')                  # and verdigris' own pair
    b.source(5, 2, 'B')
    b.cross(2, 2, 'NE'); b.cross(4, 3, 'SE')

    # blue, one row inside the top
    b.path((1, 1), (2, 1), (3, 1), (4, 1), (5, 1))
    b.path((2, 1), (2, 2)); b.path((2, 2), (3, 2))
    b.path((1, 1), (1, 0), (0, 0), (0, 1))
    b.path((2, 1), (2, 0)); b.path((3, 1), (3, 0)); b.path((4, 1), (4, 0))
    b.path((5, 1), (5, 0), (6, 0), (6, 1))
    for p in [(3, 2), (2, 0), (4, 0), (6, 1)]:
        b.lamp(p[0], p[1], 'B')
    for p in [(0, 1), (3, 0)]:
        b.lamp(p[0], p[1], 'A')

    # green, one row inside the foot
    b.path((1, 4), (2, 4), (3, 4), (4, 4), (5, 4))
    b.path((4, 4), (4, 3)); b.path((4, 3), (5, 3))
    b.path((1, 4), (0, 4), (0, 5), (1, 5))
    b.path((2, 4), (2, 5)); b.path((3, 4), (3, 5)); b.path((4, 4), (4, 5))
    b.path((5, 4), (5, 5), (6, 5), (6, 4), (6, 3))
    for p in [(5, 3), (2, 5), (4, 5), (6, 4)]:
        b.lamp(p[0], p[1], 'G')
    for p in [(1, 5), (3, 5), (6, 3)]:
        b.lamp(p[0], p[1], 'A')

    # verdigris, the only ground where green and blue may meet
    b.path((2, 2), (1, 2), (1, 3)); b.path((1, 3), (2, 3), (3, 3), (4, 3))
    b.path((2, 2), (2, 3))
    b.path((4, 3), (4, 2), (5, 2))
    b.path((1, 2), (0, 2)); b.path((1, 3), (0, 3)); b.path((5, 2), (6, 2))
    for p in [(0, 2), (0, 3), (6, 2)]:
        b.lamp(p[0], p[1], 'C')
    b.spin(seed, bias)
    b.owe((2, 2), 1); b.owe((4, 3), 1)
    return b


def g4(seed, bias):
    """Foxfire 7x6 - brittle stone on the only two joins."""
    b = Board(7, 6)
    b.fill(0, 0, 7, 6)
    b.source(1, 1, 'R')
    b.source(1, 4, 'B')
    b.source(1, 3, 'R')
    b.source(5, 2, 'B')
    b.cross(2, 2, 'NE', fragile=2); b.cross(4, 3, 'SE', fragile=2)

    b.path((1, 1), (2, 1), (3, 1), (4, 1), (5, 1))
    b.path((2, 1), (2, 2)); b.path((2, 2), (3, 2))
    b.path((1, 1), (1, 0), (0, 0), (0, 1))
    b.path((2, 1), (2, 0)); b.path((3, 1), (3, 0)); b.path((4, 1), (4, 0))
    b.path((5, 1), (5, 0), (6, 0), (6, 1))
    for p in [(3, 2), (2, 0), (4, 0), (6, 1)]:
        b.lamp(p[0], p[1], 'R')
    for p in [(0, 1), (3, 0)]:
        b.lamp(p[0], p[1], 'A')

    b.path((1, 4), (2, 4), (3, 4), (4, 4), (5, 4))
    b.path((4, 4), (4, 3)); b.path((4, 3), (5, 3))
    b.path((1, 4), (0, 4), (0, 5), (1, 5))
    b.path((2, 4), (2, 5)); b.path((3, 4), (3, 5)); b.path((4, 4), (4, 5))
    b.path((5, 4), (5, 5), (6, 5), (6, 4), (6, 3))
    for p in [(5, 3), (2, 5), (4, 5), (6, 4)]:
        b.lamp(p[0], p[1], 'B')
    for p in [(1, 5), (3, 5), (6, 3)]:
        b.lamp(p[0], p[1], 'A')

    b.path((2, 2), (1, 2), (1, 3)); b.path((1, 3), (2, 3), (3, 3), (4, 3))
    b.path((2, 2), (2, 3))
    b.path((4, 3), (4, 2), (5, 2))
    b.path((1, 2), (0, 2)); b.path((1, 3), (0, 3)); b.path((5, 2), (6, 2))
    for p in [(0, 2), (0, 3), (6, 2)]:
        b.lamp(p[0], p[1], 'M')
    b.spin(seed, bias)
    b.owe((2, 2), 1); b.owe((4, 3), 1)
    return b


def g5(seed, bias):
    """The Quiet Hollow 7x7 - amber's two springs meet only round the blue pool."""
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(1, 1, 'R')
    b.source(5, 5, 'G')
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
        b.lamp(p[0], p[1], 'Y')
    for p in [(0, 1), (6, 1), (5, 6), (0, 5)]:
        b.lamp(p[0], p[1], 'A')

    b.source(4, 3, 'B'); b.lamp(3, 3, 'B'); b.lamp(4, 4, 'B')
    b.path((2, 2), (3, 2)); b.path((3, 2), (4, 2)); b.path((3, 2), (3, 3))
    b.path((2, 2), (2, 3)); b.path((2, 3), (2, 4)); b.path((2, 3), (3, 3))
    b.path((4, 2), (4, 3), (4, 4), (3, 4), (3, 3))
    b.path((2, 4), (3, 4))
    b.spin(seed, bias)
    for p in [(2, 2), (4, 2), (2, 4)]:
        b.owe(p, 1)
    return b


def g6(seed, bias):
    """Rootbound Amber 7x7 - one tap, and the ridge decides three times."""
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(1, 1, 'R')
    b.source(1, 5, 'G')
    b.source(5, 1, 'G')
    b.cross(2, 2, 'NE', link='A')
    b.cross(3, 3, 'NE', link='A')
    b.cross(4, 4, 'NE', link='A')
    b.cross(5, 5, 'NE')

    b.path((1, 1), (2, 1), (3, 1), (4, 1), (5, 1))
    b.path((2, 1), (2, 2)); b.path((2, 2), (3, 2), (3, 3))
    b.path((3, 3), (4, 3), (4, 4))
    b.path((4, 4), (5, 4), (5, 5))
    b.path((5, 5), (6, 5), (6, 4), (6, 3), (6, 2), (6, 1))
    b.path((5, 1), (6, 1))
    b.path((4, 1), (4, 2)); b.path((5, 1), (5, 2), (5, 3))
    b.path((1, 1), (1, 0), (0, 0), (0, 1))
    b.path((3, 1), (3, 0)); b.path((2, 0), (3, 0), (4, 0))
    b.path((5, 1), (5, 0), (6, 0))
    for p in [(4, 2), (2, 0), (4, 0), (6, 4), (5, 3)]:
        b.lamp(p[0], p[1], 'Y')
    for p in [(0, 1), (6, 0), (6, 2)]:
        b.lamp(p[0], p[1], 'A')

    b.path((1, 5), (2, 5), (3, 5), (4, 5), (5, 5))
    b.path((2, 2), (1, 2), (1, 3), (1, 4), (1, 5))
    b.path((2, 2), (2, 3), (3, 3)); b.path((3, 3), (3, 4), (4, 4))
    b.path((4, 4), (4, 5))
    b.path((5, 5), (5, 6))
    b.path((2, 5), (2, 4))
    b.path((1, 2), (0, 2)); b.path((1, 3), (0, 3)); b.path((1, 4), (0, 4))
    b.path((1, 5), (1, 6), (0, 6), (0, 5))
    b.path((3, 5), (3, 6)); b.path((2, 6), (3, 6), (4, 6))
    b.path((5, 6), (6, 6))
    for p in [(2, 4), (0, 2), (0, 4), (2, 6), (4, 6)]:
        b.lamp(p[0], p[1], 'G')
    for p in [(0, 3), (0, 5), (6, 6)]:
        b.lamp(p[0], p[1], 'A')

    b.spin(seed, bias)
    b.root('A', 1, (2, 2), (3, 3), (4, 4))
    b.owe((5, 5), 1)
    return b


def g7(seed, bias):
    """Three Coloured Springs 7x7 - every light used twice, every pair met once."""
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(1, 1, 'R'); b.source(5, 1, 'G')     # amber
    b.source(1, 3, 'R'); b.source(5, 3, 'B')     # foxfire
    b.source(1, 5, 'G'); b.source(5, 5, 'B')     # verdigris
    b.cross(2, 2, 'NE')                          # amber over foxfire
    b.cross(4, 2, 'NW')                          # amber over foxfire again
    b.cross(4, 4, 'NE')                          # foxfire over verdigris

    # amber, one row inside the top
    b.path((1, 1), (2, 1), (3, 1), (4, 1), (5, 1))
    b.path((2, 1), (2, 2)); b.path((2, 2), (3, 2), (4, 2))
    b.path((4, 2), (4, 1))
    b.path((1, 1), (1, 0), (0, 0), (0, 1))
    b.path((3, 1), (3, 0)); b.path((2, 0), (3, 0), (4, 0))
    b.path((5, 1), (5, 0), (6, 0), (6, 1))
    for p in [(2, 0), (4, 0), (6, 1), (3, 2)]:
        b.lamp(p[0], p[1], 'Y')
    for p in [(0, 1), (6, 0)]:
        b.lamp(p[0], p[1], 'A')

    # foxfire, along the middle, using the other strand of the top two crossings
    b.path((1, 3), (2, 3), (3, 3), (4, 3), (5, 3))
    b.path((2, 2), (1, 2), (1, 3)); b.path((2, 2), (2, 3))
    b.path((4, 2), (5, 2), (5, 3)); b.path((4, 2), (4, 3))
    b.path((4, 3), (4, 4)); b.path((4, 4), (5, 4), (5, 3))
    b.path((4, 4), (4, 5))
    b.path((1, 2), (0, 2)); b.path((1, 3), (0, 3)); b.path((5, 2), (6, 2))
    b.path((5, 3), (6, 3))
    for p in [(0, 2), (6, 2), (3, 3)]:
        b.lamp(p[0], p[1], 'M')
    for p in [(0, 3), (6, 3)]:
        b.lamp(p[0], p[1], 'A')

    # verdigris, one row inside the foot, under the third crossing
    b.path((1, 5), (2, 5), (3, 5), (4, 5), (5, 5))
    b.path((4, 4), (3, 4)); b.path((3, 4), (3, 5))
    b.path((1, 5), (1, 4), (0, 4)); b.path((1, 5), (1, 6), (0, 6), (0, 5))
    b.path((2, 5), (2, 4))
    b.path((2, 5), (2, 6)); b.path((4, 5), (4, 6)); b.path((3, 5), (3, 6))
    b.path((5, 5), (5, 6), (6, 6), (6, 5)); b.path((6, 5), (5, 5))
    b.path((6, 5), (6, 4))
    for p in [(0, 4), (2, 4), (2, 6), (4, 6), (6, 4)]:
        b.lamp(p[0], p[1], 'C')
    for p in [(0, 5), (3, 6)]:
        b.lamp(p[0], p[1], 'A')
    b.spin(seed, bias)
    for p in [(2, 2), (4, 2), (4, 4)]:
        b.owe(p, 1)
    return b


def g8(seed, bias):
    """Under the Lanterns 7x7 - white in the middle, gardens all round it."""
    b = Board(7, 7)
    b.fill(0, 0, 7, 7)
    b.source(3, 1, 'R'); b.source(1, 3, 'G'); b.source(5, 3, 'B')   # the white ring
    b.source(0, 0, 'R'); b.source(6, 0, 'G'); b.source(6, 6, 'B')   # three pure gardens
    b.source(0, 6, 'R'); b.source(2, 6, 'G')                        # and one that blends
    for p in [(1, 1), (5, 1), (5, 5), (1, 5)]:
        b.cross(p[0], p[1], 'ES' if p == (1, 1) else
                ('SW' if p == (5, 1) else ('NW' if p == (5, 5) else 'NE')))

    ring = ([(x, 1) for x in range(1, 6)] + [(5, y) for y in range(2, 6)]
            + [(x, 5) for x in range(4, 0, -1)] + [(1, y) for y in range(4, 0, -1)])
    b.path(*ring)
    b.path((2, 1), (2, 2)); b.path((4, 1), (4, 2))
    b.path((1, 3), (2, 3)); b.path((5, 3), (4, 3))
    b.path((2, 5), (2, 4)); b.path((4, 5), (4, 4))
    b.path((3, 5), (3, 4), (3, 3), (3, 2))
    for p in [(2, 2), (4, 2), (2, 3), (3, 2), (4, 3), (2, 4), (4, 4)]:
        b.lamp(p[0], p[1], 'W')

    b.path((1, 1), (1, 0), (0, 0), (0, 1)); b.path((0, 1), (1, 1))
    b.path((0, 1), (0, 2), (0, 3))
    b.path((1, 0), (2, 0), (3, 0))
    for p in [(0, 3), (3, 0)]:
        b.lamp(p[0], p[1], 'R')
    b.lamp(0, 2, 'A')

    b.path((5, 1), (5, 0), (6, 0), (6, 1)); b.path((6, 1), (5, 1))
    b.path((6, 1), (6, 2), (6, 3))
    b.path((5, 0), (4, 0))
    for p in [(6, 3), (4, 0)]:
        b.lamp(p[0], p[1], 'G')
    b.lamp(6, 2, 'A')

    b.path((5, 5), (5, 6), (6, 6), (6, 5)); b.path((6, 5), (5, 5))
    b.path((6, 5), (6, 4))
    b.path((5, 6), (4, 6))
    for p in [(6, 4), (4, 6)]:
        b.lamp(p[0], p[1], 'B')

    b.path((1, 5), (1, 6), (0, 6), (0, 5)); b.path((0, 5), (1, 5))
    b.path((0, 5), (0, 4))
    b.path((1, 6), (2, 6), (3, 6))
    for p in [(0, 4), (3, 6)]:
        b.lamp(p[0], p[1], 'Y')
    b.spin(seed, bias)
    for p in [(1, 1), (5, 1), (5, 5), (1, 5)]:
        b.owe(p, 1)
    return b


def g9(seed, bias):
    """The Green Path 8x7 - one green lane, fording both others at four corners."""
    b = Board(8, 7)
    b.fill(0, 0, 8, 7)
    b.source(3, 1, 'R'); b.source(4, 2, 'G')     # the amber lane
    b.source(3, 5, 'B')                          # the blue one
    b.cross(1, 2, 'NE'); b.cross(6, 2, 'NW', fragile=2)
    b.cross(1, 4, 'SE', fragile=2); b.cross(6, 4, 'SW')

    # the amber lane: a loop two rows deep, one row inside the top
    b.path(*[(x, 1) for x in range(1, 7)])
    b.path((6, 1), (6, 2)); b.path((6, 2), (5, 2), (4, 2), (3, 2), (2, 2), (1, 2))
    b.path((1, 2), (1, 1))
    for x in range(1, 7):
        b.path((x, 1), (x, 0))
    b.path((1, 0), (0, 0), (0, 1)); b.path((6, 0), (7, 0), (7, 1))
    for p in [(2, 0), (4, 0), (0, 1), (7, 1)]:
        b.lamp(p[0], p[1], 'Y')
    for p in [(3, 0), (5, 0)]:
        b.lamp(p[0], p[1], 'A')

    # the blue one: the same loop, one row inside the foot
    b.path(*[(x, 5) for x in range(1, 7)])
    b.path((6, 5), (6, 4)); b.path((6, 4), (5, 4), (4, 4), (3, 4), (2, 4), (1, 4))
    b.path((1, 4), (1, 5))
    for x in range(1, 7):
        b.path((x, 5), (x, 6))
    b.path((1, 6), (0, 6), (0, 5)); b.path((6, 6), (7, 6), (7, 5))
    for p in [(2, 6), (4, 6), (0, 5), (7, 5)]:
        b.lamp(p[0], p[1], 'B')
    for p in [(3, 6), (5, 6)]:
        b.lamp(p[0], p[1], 'A')

    # the green lane: one island under both others, meeting itself down the middle
    b.source(5, 3, 'G')
    for p in [(0, 3), (7, 3), (3, 3)]:
        b.lamp(p[0], p[1], 'G')
    b.path(*[(x, 3) for x in range(1, 7)])
    b.path((0, 2), (0, 3), (0, 4)); b.path((7, 2), (7, 3), (7, 4))
    b.path((0, 2), (1, 2)); b.path((1, 2), (1, 3))
    b.path((7, 2), (6, 2)); b.path((6, 2), (6, 3))
    b.path((0, 4), (1, 4)); b.path((1, 4), (1, 3))
    b.path((7, 4), (6, 4)); b.path((6, 4), (6, 3))
    b.spin(seed, bias)
    for p in [(1, 2), (6, 2), (1, 4), (6, 4)]:
        b.owe(p, 1)
    return b


def g10(seed, bias):
    """The Amberwood Knot 8x7 - everything the wood has taught, tied once."""
    b = Board(8, 7)
    b.fill(0, 0, 8, 7)
    b.source(3, 1, 'R'); b.source(3, 2, 'G')     # the amber ridge
    b.source(3, 4, 'G'); b.source(3, 5, 'B')     # the verdigris foot
    b.source(4, 3, 'R')                          # and the red lane between them
    b.cross(1, 2, 'NE', link='A'); b.cross(6, 2, 'NW', fragile=2)
    b.cross(1, 5, 'NE', fragile=2); b.cross(6, 5, 'NW', link='A')

    # the amber ridge, a loop one row inside the top
    b.path(*[(x, 1) for x in range(1, 7)])
    b.path((6, 1), (6, 2)); b.path((6, 2), (5, 2), (4, 2), (3, 2), (2, 2), (1, 2))
    b.path((1, 2), (1, 1))
    for x in range(1, 7):
        b.path((x, 1), (x, 0))
    b.path((1, 0), (0, 0), (0, 1)); b.path((6, 0), (7, 0), (7, 1))
    for p in [(2, 0), (4, 0), (0, 1), (7, 1)]:
        b.lamp(p[0], p[1], 'Y')
    for p in [(3, 0), (5, 0)]:
        b.lamp(p[0], p[1], 'A')

    # the red lane between them, threaded through both corners of the ridge
    b.path((1, 2), (1, 3)); b.path((6, 2), (6, 3))
    b.path(*[(x, 3) for x in range(1, 7)])
    b.path((1, 3), (0, 3)); b.path((6, 3), (7, 3))
    for p in [(0, 2), (7, 2), (0, 3), (7, 3)]:
        b.lamp(p[0], p[1], 'R')
    b.path((0, 2), (1, 2)); b.path((7, 2), (6, 2))

    # the verdigris foot, the same loop upside down
    b.path(*[(x, 4) for x in range(1, 7)])
    b.path((6, 4), (6, 5)); b.path((6, 5), (5, 5), (4, 5), (3, 5), (2, 5), (1, 5))
    b.path((1, 5), (1, 4))
    b.path((1, 4), (0, 4)); b.path((6, 4), (7, 4))
    for x in range(2, 6):
        b.path((x, 5), (x, 6))
    for p in [(0, 4), (7, 4), (2, 6), (4, 6)]:
        b.lamp(p[0], p[1], 'C')
    for p in [(3, 6), (5, 6)]:
        b.lamp(p[0], p[1], 'A')

    # and two red pools under it, one at each corner
    b.source(0, 5, 'R'); b.source(7, 5, 'R')
    b.lamp(0, 6, 'R'); b.lamp(7, 6, 'R')
    b.path((1, 5), (0, 5)); b.path((0, 5), (0, 6)); b.path((0, 6), (1, 6))
    b.path((1, 6), (1, 5))
    b.path((6, 5), (7, 5)); b.path((7, 5), (7, 6)); b.path((7, 6), (6, 6))
    b.path((6, 6), (6, 5))
    b.spin(seed, bias)
    b.root('A', 1, (1, 2), (6, 5))
    for p in [(6, 2), (1, 5)]:
        b.owe(p, 1)
    return b


PALETTE = {
    "c03_amberlight":         ("#FFC24A", "#33240F"),
    "c03_two_lights":         ("#FF8A5C", "#351C14"),
    "c03_the_verdigris":      ("#4FD1C5", "#0F2E2C"),
    "c03_foxfire":            ("#E86AD8", "#2E1233"),
    "c03_the_quiet_hollow":   ("#A98BFF", "#1C1830"),
    "c03_rootbound":          ("#C9D94A", "#262B12"),
    "c03_three_springs":      ("#7FD6FF", "#10243A"),
    "c03_under_the_lanterns": ("#FFE08A", "#30291A"),
    "c03_the_ashen_path":     ("#D9A05B", "#2A211A"),
    "c03_the_amberwood_knot": ("#F0803C", "#3A1E10"),
}


# Walked up the map, alternating sides. Five strips is 6000 canvas units tall, so the
# nearest pair is about 700 apart against a 220-unit floor.
#: Which chapter of its own mode this is. It buys the map and the ten skies - see
#: `mapart`, which owns that arithmetic for every chapter of every mode.
ORDINAL = 3
STRIPS = mapart.strips(ORDINAL)
SKIES = mapart.skies(ORDINAL)

MAPX = [0.28, 0.72, 0.26, 0.70, 0.30, 0.74, 0.28, 0.70, 0.24, 0.72]
MAPY = [0.060, 0.145, 0.225, 0.305, 0.390, 0.475, 0.555, 0.640, 0.725, 0.815]

TEXT = {
    "c03_amberlight": (
        "Amberlight",
        "Amber needs two springs of its own"),
    "c03_two_lights": (
        "Two Lights, One Lane",
        "Both lanes turn where they cross"),
    "c03_the_verdigris": (
        "The Verdigris",
        "Teal, between the blue comb and the green one"),
    "c03_foxfire": (
        "Foxfire",
        "Brittle stone on the only join"),
    "c03_the_quiet_hollow": (
        "The Quiet Hollow",
        "Amber all round, and a blue pool in the middle"),
    "c03_rootbound": (
        "Rootbound Amber",
        "One tap, and the ridge decides three times"),
    "c03_three_springs": (
        "Three Coloured Springs",
        "Every light used twice, every pair met once"),
    "c03_under_the_lanterns": (
        "Under the Lanterns",
        "White in the middle, and gardens all round it"),
    "c03_the_ashen_path": (
        "The Green Path",
        "The green path fords both lanes and joins beneath them"),
    "c03_the_amberwood_knot": (
        "The Amberwood Knot",
        "Everything the wood has taught, tied once"),
}

BOARDS = [
    ("c03_amberlight", "Amberlight", g1, 44),
    ("c03_two_lights", "Two Lights, One Lane", g2, 50),
    ("c03_the_verdigris", "The Verdigris", g3, 47),
    ("c03_foxfire", "Foxfire", g4, 53),
    ("c03_the_quiet_hollow", "The Quiet Hollow", g5, 57),
    ("c03_rootbound", "Rootbound Amber", g6, 54),
    ("c03_three_springs", "Three Coloured Springs", g7, 64),
    ("c03_under_the_lanterns", "Under the Lanterns", g8, 58),
    ("c03_the_ashen_path", "The Ashen Path", g9, 59),
    ("c03_the_amberwood_knot", "The Amberwood Knot", g10, 70),
]


def build():
    """Every glade, fitted to its target par and proved. Raises on any board that fails."""
    out = collections.OrderedDict()
    for lid, name, make, target in BOARDS:
        seed, bias, board = fit(make, target)
        errs, warns = board.check()
        if errs:
            raise SystemExit(f"{lid}: " + "; ".join(errs))
        out[lid] = (board, seed, bias, warns)
    return out


def chapter_json(built):
    accent, slate = PALETTE["c03_amberlight"]
    doc = collections.OrderedDict()
    doc["schemaVersion"] = 2
    doc["id"] = CHAPTER
    doc["accent"] = accent
    doc["slate"] = slate
    doc["backdrop"] = SKIES[0]
    doc["mapStrips"] = list(STRIPS)
    doc["teaserX"] = 0.3

    levels = []
    for i, (lid, name, make, target) in enumerate(BOARDS):
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
            level["backdrop"] = SKIES[i]
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

    add(f"chapter.{CHAPTER}.name", "The Amberwood")
    for lid, _, _, _ in BOARDS:
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

    for i, (lid, name, make, target) in enumerate(BOARDS):
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
