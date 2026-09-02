"""The Shallows - the ten boards of chapter one, rebuilt as an onboarding ramp.

**The shipped chapter is `Content/chapters/c01_shallows.json`, not this file.** That is
what the game reads and what the build gate proves; nothing at runtime knows this script
exists. Same bargain as `c02_millvale.py`, and the same two commands:

    python Tools/chapters/c01_shallows.py --check     # does the shipped JSON still match?
    python Tools/chapters/c01_shallows.py             # rewrite it from here

Every glade keeps its id, its name, its subject, its palette, its backdrop and its place
on the map. What changed is how much the chapter asks, and when.

**The chapter opened at the difficulty it used to close at.** The first board a player
ever saw was a 5x5 with twenty-three conduits, a par of thirty-four, a seventy-four
second clock and a move budget - so somebody who had never turned a tile met a board they
could lose, and the star line (par x 1.00 seconds, which is 1.35 taps a second sustained)
was out of reach for anybody meeting the verb for the first time. Then it climbed almost
nowhere: par ran 34, 49, 38, 46, 45, 57, 43, 53, 48, 61, which is a chapter that opens at
its own average and wanders. And the mechanics arrived three at a time - three brittle
conduits on glade four, three taproots on five, three shadows on six.

So the ramp is the rebuild:

* **Glade one cannot be lost.** `budgetFactor` is negative, which
  is what the DTO documents them for - a tutorial board where a countdown teaches the
  wrong lesson. Nine tiles, three critters, one heart, and every tile placeable by
  looking at it. The clock is a stopwatch and the move counter counts up.
* **Par climbs 10 -> 50** rather than opening at 34. Nothing here is longer than the old
  opening board until glade six.
* **One new idea per glade, and one or two of it rather than three.** Two brittle
  conduits and one taproot, where the old chapter opened each of those subjects with
  three at once.
* **The ground stays open.** The Mill Vale fills its rectangles, because an arm with
  nowhere to go is an arm nobody has to think about (see `c02_millvale.py`). That is
  exactly what a first chapter wants: the Shallows reads at a glance on purpose, and the
  step up into filled ground is what chapter two is for.

What is deliberately *not* here: crossings and briars, which belong to chapters two and
four, and filled ground. `wins` is 1 on every board and that is the honest reading rather
than an oversight - these ten glades teach a vocabulary, and the measured decisions
(invariant 5d) start in the Mill Vale, which is the chapter that has the tile cheap
enough to carry them. What the Shallows carries instead is *reachable* mistakes: every
glade from the second on has at least one place where a wrong turn joins two things that
must stay apart, and the count is printed below as `near`.

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
sys.path.insert(0, HERE)

from author import Board, fit                                    # noqa: E402
import mapart                                                    # noqa: E402

CHAPTER = "c01_shallows"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")
LOC = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")


def rooted(b, p):
    """A conduit nobody can turn, authored at its own solution.

    Called *before* `spin`, which is not a style point: `spin` skips a rooted tile rather
    than turning it, so rooting after spinning would freeze whatever rotation the seed
    happened to hand out. A rooted tile off its solution is invariant 5c's failure - the
    board every check ran against is not the board that ships, and nothing can notice.
    """
    b.cells[p]['locked'] = True
    b.cells[p]['rot'] = 0


def taproot(b, rune, turns, *pts):
    """Bind these conduits to one root, and refuse a member that turning cannot move.

    `Board.root` derives each member's start rotation modulo its own *period* - after how
    many quarter turns a tile reads as itself again - so a member with a period of 1 is
    handed a rotation of 0 and the root quietly binds nothing. A four-armed conduit is
    exactly that: it wears all four arms at every angle, so `Puzzle.Alike` calls every
    rotation of it solved.

    Nothing offline sees it. `author.check()` passes, the board is winnable, par is right,
    and `difficulty.py` reports the root removing nothing - which is also what an honest
    root on an open board reports. The Editor's `ContentValidation` is the only thing that
    says so out loud ("every conduit on taproot 'A' looks the same in every orientation"),
    and it said so about the first cut of glade five. So the rule is asserted where the
    board is authored, in the file somebody edits, rather than left to be caught two tools
    downstream.
    """
    for p in pts:
        assert b.period(p) > 1, (
            f"taproot '{rune}' member {p} has {bin(b.mask(p)).count('1')} arms and reads "
            "the same at every angle, so turning the root can never matter")
    b.root(rune, turns, *pts)


def brittle(b, p, survives, needs):
    """Brittle stone that owes `needs` turns and survives `survives` of them.

    Both halves have to be said together or the board is unwinnable in a way that looks
    perfectly authored: `check()` refuses a conduit that owes more turns than it can take,
    and the turns owed are a property of the start rotation `spin` handed out. So the
    rotation is pinned here rather than left to the seed.
    """
    b.cells[p]['fragile'] = survives
    b.owe(p, needs)


def g1(seed, bias):
    """First Light 4x4 - the verb, and nothing else at all.

    One heart, three critters, nine tiles, and no rule beyond "tap a conduit to turn it".
    Every tile has at most two neighbours, so there is nothing to work out here - which is
    the point: the board somebody meets before they know what a board is should be a
    demonstration rather than a test. It is also the only glade in the game that cannot be
    lost, on either the clock or the budget.
    """
    b = Board(4, 4)
    b.source(1, 3, 'W')
    for p in [(1, 2), (1, 1), (1, 0), (2, 0), (2, 2)]:
        b.pipe(*p)
    for p in [(3, 0), (0, 2), (3, 2)]:
        b.lamp(p[0], p[1], 'A')
    b.path((1, 3), (1, 2), (1, 1), (1, 0), (2, 0), (3, 0))
    b.path((1, 2), (0, 2))
    b.path((1, 2), (2, 2), (3, 2))
    b.spin(seed, bias)
    return b


def g2(seed, bias):
    """Twin Streams 5x5 - two hearts, and the first thing that can go wrong.

    A ring broken in two places: red owns one arc and blue the other, and the two breaks
    are where the arcs come within a turn of each other. Nothing about the arms says the
    streams must stay apart - the critters do - so this is the first glade whose colours
    are load-bearing, and a wrong turn shows itself the moment it is made.
    """
    b = Board(5, 5)
    b.source(0, 0, 'R')
    for p in [(1, 0), (2, 0), (3, 0), (0, 1), (0, 2)]:
        b.pipe(*p)
    for p in [(4, 0), (1, 1), (3, 1), (1, 2)]:
        b.lamp(p[0], p[1], 'R')
    b.path((0, 0), (1, 0), (2, 0), (3, 0), (4, 0))
    b.path((0, 0), (0, 1), (0, 2))
    b.path((1, 0), (1, 1))
    b.path((3, 0), (3, 1))
    b.path((0, 2), (1, 2))

    b.source(4, 4, 'B')
    for p in [(3, 4), (2, 4), (1, 4), (4, 3), (4, 2)]:
        b.pipe(*p)
    for p in [(0, 4), (3, 3), (1, 3), (3, 2)]:
        b.lamp(p[0], p[1], 'B')
    b.path((4, 4), (3, 4), (2, 4), (1, 4), (0, 4))
    b.path((4, 4), (4, 3), (4, 2))
    b.path((3, 4), (3, 3))
    b.path((1, 4), (1, 3))
    b.path((4, 2), (3, 2))
    b.spin(seed, bias)
    return b


def g3(seed, bias):
    """Prism Heart 6x5 - two hearts running into one another, and one that must not.

    Red and green meet in the point of a V and everything past the join wakes gold, so
    the blend is not a rule to be told about - it is what the board plainly does, and the
    critters on the red arm are gold as well, which is the half of the idea a diagram
    would not get across. The blue spring in the corner is the counterweight: the same tap
    that makes gold out of two hearts would make a mess of a third.

    The point of the V is rooted, and that is where the chapter meets its cheapest
    mechanic. A tile that will not turn is the one rule here that costs a player nothing
    to discover - it takes turns *out* of par - so it belongs on a glade that already has
    a lesson rather than on one of its own, and it belongs before brittle stone, which is
    the same idea with a price on it.
    """
    b = Board(6, 5)
    b.source(0, 0, 'R')
    b.source(4, 0, 'G')
    for p in [(0, 1), (1, 1), (1, 2), (2, 2), (4, 1), (3, 1), (3, 2), (2, 3)]:
        b.pipe(*p)
    for p in [(0, 2), (1, 0), (5, 1), (3, 0), (2, 4), (1, 3), (3, 3)]:
        b.lamp(p[0], p[1], 'Y')
    b.path((0, 0), (0, 1), (1, 1), (1, 2), (2, 2))
    b.path((0, 1), (0, 2))
    b.path((1, 1), (1, 0))
    b.path((4, 0), (4, 1), (3, 1), (3, 2), (2, 2))
    b.path((4, 1), (5, 1))
    b.path((3, 1), (3, 0))
    b.path((2, 2), (2, 3), (2, 4))
    b.path((2, 3), (1, 3))
    b.path((2, 3), (3, 3))

    b.source(5, 4, 'B')
    b.pipe(4, 4)
    b.lamp(4, 3, 'B')
    b.lamp(5, 3, 'B')
    b.path((5, 4), (4, 4), (4, 3))
    b.path((5, 4), (5, 3))
    rooted(b, (2, 2))
    b.spin(seed, bias)
    return b


def g4(seed, bias):
    """Brittle Hollow 6x6 - one crumbling conduit on each stream, and a hollow between.

    Two brittle conduits where the old glade had three, each owing one turn and surviving
    two: exactly one wrong guess and no more. Both sit on a junction rather than on a
    straight run, so the tile that crumbles is the one whose turn decides where a stream
    goes, and the two streams pass each other at both ends of the hollow.
    """
    b = Board(6, 6)
    b.source(0, 0, 'R')
    for p in [(1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (5, 1), (0, 1), (0, 2)]:
        b.pipe(*p)
    for p in [(2, 1), (4, 1), (1, 2), (5, 2)]:
        b.lamp(p[0], p[1], 'R')
    b.path((0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (5, 1), (5, 2))
    b.path((0, 0), (0, 1), (0, 2), (1, 2))
    b.path((2, 0), (2, 1))
    b.path((4, 0), (4, 1))

    b.source(5, 5, 'B')
    for p in [(4, 5), (3, 5), (2, 5), (1, 5), (0, 5), (0, 4), (5, 4), (5, 3)]:
        b.pipe(*p)
    for p in [(3, 4), (1, 4), (4, 3), (0, 3)]:
        b.lamp(p[0], p[1], 'B')
    b.path((5, 5), (4, 5), (3, 5), (2, 5), (1, 5), (0, 5), (0, 4), (0, 3))
    b.path((5, 5), (5, 4), (5, 3), (4, 3))
    b.path((3, 5), (3, 4))
    b.path((1, 5), (1, 4))

    b.spin(seed, bias)
    brittle(b, (2, 0), 2, 1)
    brittle(b, (3, 5), 2, 1)
    return b


def g5(seed, bias):
    """Bound Roots 6x6 - one root, two junctions, opposite corners of the glade.

    A taproot is only worth anything when its members are far enough apart that a player
    cannot watch both at once, so this is one rune on two junctions in opposite corners:
    the tap that sends the red stream down its second arm sends the blue stream down its
    own. Par charges a bound group once however many members it has, which is why this
    glade is shorter than the one before it - the chapter's dip is its taproot board.

    Both hubs carry **three** arms and not four, and that is the whole board rather than a
    detail of it - see `taproot`. The first cut hung a fourth stub off each of them, which
    made them four-armed, which made them identical at every angle, which made the root
    decoration. Each lost its stub to the spring beside it instead.
    """
    b = Board(6, 6)
    b.source(0, 0, 'R')
    for p in [(0, 1), (1, 1), (2, 1), (3, 1), (1, 2), (3, 2)]:
        b.pipe(*p)
    for p in [(1, 0), (3, 0), (0, 2), (1, 3), (3, 3)]:
        b.lamp(p[0], p[1], 'R')
    b.path((0, 0), (0, 1), (1, 1), (2, 1), (3, 1))
    b.path((0, 1), (0, 2))
    b.path((0, 0), (1, 0))                # hangs off the spring, not off the hub
    b.path((3, 1), (3, 0))
    b.path((1, 1), (1, 2), (1, 3))
    b.path((3, 1), (3, 2), (3, 3))

    b.source(5, 5, 'B')
    for p in [(5, 4), (4, 4), (3, 4), (2, 4), (4, 3), (2, 3)]:
        b.pipe(*p)
    for p in [(4, 5), (2, 5), (5, 3), (4, 2), (2, 2)]:
        b.lamp(p[0], p[1], 'B')
    b.path((5, 5), (5, 4), (4, 4), (3, 4), (2, 4))
    b.path((5, 4), (5, 3))
    b.path((5, 5), (4, 5))                # and the same the other way up
    b.path((2, 4), (2, 5))
    b.path((4, 4), (4, 3), (4, 2))
    b.path((2, 4), (2, 3), (2, 2))

    b.spin(seed, bias)
    taproot(b, 'A', 1, (1, 1), (4, 4))
    return b


def g6(seed, bias):
    """Stillwater 7x6 - two streams, and a green pool between them.

    The pool in the middle carries a heart of its own, which is the shape that replaced the
    duskcap everywhere in the game (invariant 5f) and the reason this board is not simply a
    longer version of glade two. It stands where both streams pass: the red run ends one
    tile to its west and the blue one turns two tiles to its east, so there are three places
    a wrong turn pours somebody else's colour into the green and the pool's own critter goes
    out. The glade keeps the id `c01_duskcap_hollow` because an id is permanent (invariant
    1); nothing else about it still says so.
    """
    b = Board(7, 6)
    b.source(0, 0, 'R')
    for p in [(1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (6, 0), (6, 1), (6, 2),
              (0, 1), (0, 2), (0, 3), (1, 3), (2, 3)]:
        b.pipe(*p)
    for p in [(2, 1), (4, 1), (6, 3), (1, 4), (2, 4)]:
        b.lamp(p[0], p[1], 'R')
    b.path((0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (6, 0))
    b.path((6, 0), (6, 1), (6, 2), (6, 3))
    b.path((0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3))
    b.path((2, 0), (2, 1))
    b.path((4, 0), (4, 1))
    b.path((1, 3), (1, 4))
    b.path((2, 3), (2, 4))

    b.source(6, 5, 'B')
    for p in [(5, 5), (4, 5), (5, 4)]:
        b.pipe(*p)
    for p in [(3, 5), (5, 3), (4, 4)]:
        b.lamp(p[0], p[1], 'B')
    b.path((6, 5), (5, 5), (4, 5), (3, 5))
    b.path((5, 5), (5, 4), (5, 3))
    b.path((5, 4), (4, 4))

    b.source(4, 3, 'G')
    b.lamp(3, 2, 'G')
    b.pipe(3, 3)
    b.path((3, 2), (3, 3), (4, 3))
    b.spin(seed, bias)
    return b


def g7(seed, bias):
    """Lantern Ring 7x6 - four corners on one root, closed round a sleeping dark.

    The ring's corners wear the same rune, so one tap turns all four and the ring opens
    and shuts as one thing. What that leaves the player is the ring's *edges*: they are
    what can leak red light inward, and what is inward is a blue heart with one critter
    of its own. It is the chapter's one board where the two ideas it has taught
    separately have to be held at once.
    """
    b = Board(7, 6)
    ring = [(1, 1), (2, 1), (3, 1), (4, 1), (5, 1), (5, 2), (5, 3), (5, 4),
            (4, 4), (3, 4), (2, 4), (1, 4), (1, 3), (1, 2)]
    for p in ring:
        b.pipe(*p)
    b.path(*(ring + [(1, 1)]))

    b.source(0, 0, 'R')
    b.pipe(1, 0)
    b.path((0, 0), (1, 0), (1, 1))
    b.lamp(0, 1, 'R')
    b.path((0, 0), (0, 1))
    for tip, joint in [((3, 0), (3, 1)), ((6, 1), (5, 1)), ((0, 4), (1, 4)),
                       ((3, 5), (3, 4)), ((6, 4), (5, 4))]:
        b.lamp(tip[0], tip[1], 'R')
        b.path(joint, tip)

    b.source(3, 3, 'B')
    b.lamp(3, 2, 'B')
    b.path((3, 2), (3, 3))

    b.spin(seed, bias)
    taproot(b, 'A', 1, (1, 1), (5, 1), (5, 4), (1, 4))
    return b


def g8(seed, bias):
    """Sleeping Thicket 7x7 - a green stream, and a red and a blue either side of it.

    The thicket runs down the middle of the glade with a green heart of its own, and both
    streams pass it. Joining it to either pours a second colour into the green and the
    thicket's three critters go out together, which is the sentence the glade's tagline
    makes. The brittle conduit is on the red approach, so the side that can be tried is
    not the side that matters.
    """
    b = Board(7, 7)
    b.source(0, 0, 'R')
    for p in [(1, 0), (2, 0), (0, 1), (0, 2), (0, 3), (0, 4), (1, 4), (2, 4), (2, 3)]:
        b.pipe(*p)
    for p in [(2, 1), (1, 2), (0, 5), (1, 5), (2, 2)]:
        b.lamp(p[0], p[1], 'R')
    b.path((0, 0), (1, 0), (2, 0), (2, 1))
    b.path((0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (0, 5))
    b.path((0, 2), (1, 2))
    b.path((0, 4), (1, 4), (2, 4), (2, 3), (2, 2))
    b.path((1, 4), (1, 5))

    b.source(6, 6, 'B')
    for p in [(5, 6), (4, 6), (6, 5), (6, 4), (6, 3), (6, 2), (5, 2), (4, 2), (4, 3)]:
        b.pipe(*p)
    for p in [(4, 5), (5, 4), (6, 1), (5, 1), (4, 4)]:
        b.lamp(p[0], p[1], 'B')
    b.path((6, 6), (5, 6), (4, 6), (4, 5))
    b.path((6, 6), (6, 5), (6, 4), (6, 3), (6, 2), (6, 1))
    b.path((6, 4), (5, 4))
    b.path((6, 2), (5, 2), (4, 2), (4, 3), (4, 4))
    b.path((5, 2), (5, 1))

    b.source(3, 2, 'G')
    for p in [(3, 1), (3, 3), (3, 5)]:
        b.lamp(p[0], p[1], 'G')
    b.pipe(3, 4)
    b.path((3, 1), (3, 2), (3, 3), (3, 4), (3, 5))

    b.spin(seed, bias)
    brittle(b, (2, 4), 2, 1)
    return b


def g9(seed, bias):
    """Three Springs 7x7 - gold, teal, and a red spring of its own.

    Gold is red and green together; teal is green and blue. They already share a heart, so
    joining them does not blend to some fourth colour - it puts both of them white, and
    every critter on both networks goes out at once. That is the one colour lesson a board
    can make that two hearts cannot, and it is why this glade exists. The red spring in
    the corner is the control: the one stream here with nothing to lose by meeting anyone.
    """
    b = Board(7, 7)
    b.source(0, 0, 'R')
    b.source(0, 1, 'G')
    for p in [(1, 0), (1, 1), (2, 1), (3, 1), (4, 1), (2, 2), (3, 2)]:
        b.pipe(*p)
    for p in [(2, 0), (4, 0), (5, 1), (1, 2), (2, 3), (3, 3)]:
        b.lamp(p[0], p[1], 'Y')
    b.path((0, 0), (1, 0), (1, 1))
    b.path((0, 1), (1, 1))
    b.path((1, 1), (2, 1), (3, 1), (4, 1), (5, 1))
    b.path((1, 0), (2, 0))
    b.path((4, 1), (4, 0))
    b.path((1, 1), (1, 2))
    b.path((2, 1), (2, 2), (2, 3))
    b.path((2, 2), (3, 2), (3, 3))

    b.source(6, 6, 'G')
    b.source(6, 5, 'B')
    for p in [(5, 6), (5, 5), (4, 5), (3, 5), (2, 5), (4, 4)]:
        b.pipe(*p)
    for p in [(4, 6), (2, 6), (1, 5), (5, 4), (4, 3), (3, 4)]:
        b.lamp(p[0], p[1], 'C')
    b.path((6, 6), (5, 6), (5, 5))
    b.path((6, 5), (5, 5))
    b.path((5, 5), (4, 5), (3, 5), (2, 5), (1, 5))
    b.path((5, 6), (4, 6))
    b.path((2, 5), (2, 6))
    b.path((5, 5), (5, 4))
    b.path((4, 5), (4, 4), (4, 3))
    b.path((4, 4), (3, 4))

    b.source(0, 6, 'R')
    b.pipe(0, 5)
    b.lamp(0, 4, 'R')
    b.lamp(1, 6, 'R')
    b.path((0, 6), (0, 5), (0, 4))
    b.path((0, 6), (1, 6))
    b.spin(seed, bias)
    return b


def g10(seed, bias):
    """The Grovekeeper's Knot 7x7 - everything the Shallows taught, once each.

    A blend that has to happen, two streams that have to stay out of it, one brittle
    conduit and one taproot binding a junction in each network. One of each, which is the
    whole difference from the knot this replaces: that one carried two brittle conduits,
    two roots and five shadows on an 8x7 board, and was the hardest glade in the game
    outside the Amberwood.
    """
    b = Board(7, 7)
    b.source(0, 0, 'R')
    b.source(2, 0, 'G')
    for p in [(1, 0), (1, 1), (1, 2), (2, 2), (3, 2), (0, 1), (0, 2)]:
        b.pipe(*p)
    for p in [(0, 3), (2, 1), (3, 3), (4, 2), (1, 3)]:
        b.lamp(p[0], p[1], 'Y')
    b.path((0, 0), (1, 0), (2, 0))
    b.path((1, 0), (1, 1), (1, 2), (2, 2), (3, 2), (4, 2))
    b.path((0, 0), (0, 1), (0, 2), (0, 3))
    b.path((1, 1), (2, 1))
    b.path((3, 2), (3, 3))
    b.path((1, 2), (1, 3))

    b.source(6, 6, 'B')
    for p in [(5, 6), (5, 5), (5, 4), (4, 4), (3, 4), (6, 5), (6, 4)]:
        b.pipe(*p)
    for p in [(4, 6), (2, 4), (6, 3), (4, 5), (5, 3)]:
        b.lamp(p[0], p[1], 'B')
    b.path((6, 6), (5, 6), (4, 6))
    b.path((5, 6), (5, 5), (5, 4), (4, 4), (3, 4), (2, 4))
    b.path((6, 6), (6, 5), (6, 4), (6, 3))
    b.path((5, 5), (4, 5))
    b.path((5, 4), (5, 3))

    b.source(3, 1, 'B')
    b.lamp(3, 0, 'B')
    b.path((3, 0), (3, 1))

    b.spin(seed, bias)
    taproot(b, 'A', 1, (1, 1), (5, 5))
    brittle(b, (5, 4), 2, 1)
    return b


# ---------------------------------------------------------------- the chapter
# The palette is unchanged from the chapter that shipped: the boards were rebuilt, not the
# shallows. Neither the art nor the node positions are this file's decision any more - the
# map, the ten skies and where the ten glades stand all come from `mapart` and the ordinal
# below, which is what makes the first chapter of every mode the same place.
#
# The palette is still authored and still matters: `accent` and `slate` are the board's own
# light and its plate, which is where a glade's identity belongs. They no longer reach the
# backdrop.
PALETTE = {
    "c01_first_light": (None, None),          # the opener wears the chapter's own colours
    "c01_twin_streams": ("#4FC1FF", "#0F2A4A"),
    "c01_prism_heart": ("#FF74D4", "#241540"),
    "c01_thorn_hollow": ("#7ED957", "#1B2E1A"),
    "c01_bound_roots": ("#8B93FF", "#1B2140"),
    "c01_duskcap_hollow": ("#63C7C0", "#12242B"),
    "c01_lantern_ring": ("#FFC93C", "#3A2A12"),
    "c01_sleeping_thicket": ("#4FD18B", "#122318"),
    "c01_three_springs": ("#FF8FB1", "#33172A"),
    "c01_grovekeepers_knot": ("#9C8CFF", "#171436"),
}

#: Which chapter of its own mode this is. It buys the map and the ten skies - see
#: `mapart`, which owns that arithmetic for every chapter of every mode.
ORDINAL = 1
STRIPS = mapart.strips(ORDINAL)
SKIES = mapart.skies(ORDINAL)

PLACES = mapart.places(ORDINAL)


# Turns allowed before the run is lost, as a multiple of par. Negative is no budget at all;
# 0 takes the default 2.60.
#
# Turns allowed before the run is lost, as a multiple of par. 0 takes the default; a
# negative value removes the budget entirely.
#
# Only the first glade authors one now, and it authors "none". Everything else takes
# LevelTuning.DefaultBudgetFactor, which is 1.60 across every chapter - the budget is the
# only way a glade can be lost since the clock was removed, and a chapter-by-chapter ramp
# would be a difficulty curve, which is the boards' job (invariant 5d) and not this number's.
#
# Glade one is the exception the DTO documents a negative for: it is the first board anybody
# ever plays, nine tiles and three critters, and a lost heart in the first minute of the game
# is the most expensive heart in it.
BUDGET = [-1, 0, 0, 0, 0, 0, 0, 0, 0, 0]

# The only strings this rebuild is allowed to touch, and what it is changing them from.
#
# **This file deliberately does not own the chapter's wording.** `c02_millvale.py` carries
# a whole TEXT table because it wrote its chapter's strings in the first place; the
# Shallows' strings shipped long before it, they are edited by hand and by translators, and
# a table here would quietly re-assert its own copy every time anybody regenerated the
# boards. So the rule is narrower: every key must already exist (a missing one is an error
# rather than something to invent in the wrong voice), and a handful are rewritten -
# because the board underneath them changed and a tagline describing a board the player is
# not looking at is worse than no tagline at all.
#
# Glade four carries two streams where it carried three. The replacements avoid the words
# "glade", "grove" and "level" on purpose, so that whichever way the game's own vocabulary
# settles, no line has to be revisited.
#
# The rest of the table is the duskcap coming out. Every pool of dark in the chapter is now
# a pool of colour with a heart of its own, so four boards changed what they are about and
# three of them said "shadow" in their own name or tagline. Glade six is the one that
# cannot be tidied away: its id is `c01_duskcap_hollow` and an id is permanent (invariant
# 1), so the name above it is the only part of that word which is allowed to move.
#
# A level's third string - the `lesson` line - is gone entirely. It was floated along the
# bottom of any run that had nothing new to teach, which after the first few levels is
# every run in the game, and a box that appears on every level of every mode is furniture.
# Nothing reads the key now, so nothing authors one.
#
# A key whose text matches neither the old line nor the new one is reported and left alone.
# That is the whole point of keying on the previous text: somebody re-wording a line by
# hand must not have it silently overwritten by a board regeneration.
REWORDED = {
    "level.c01_thorn_hollow.tagline": (
        ("Three streams, and conduits that will not last",),
        "Two streams, and conduits that will not last"),
    "level.c01_duskcap_hollow.name": (
        ("Duskcap Hollow",),
        "Stillwater"),
    "level.c01_duskcap_hollow.tagline": (
        ("Wake the groove without waking the dark",),
        "Two streams, and a pool between them"),
    "level.c01_lantern_ring.tagline": (
        ("One ring of light around a sleeping dark",),
        "One ring of light around a light of its own"),
    "level.c01_sleeping_thicket.tagline": (
        ("Two streams, and a thicket between them",),
        "Two streams, and a third between them"),
}

BOARDS = [
    ("c01_first_light", g1, 10),
    ("c01_twin_streams", g2, 22),
    ("c01_prism_heart", g3, 24),
    ("c01_thorn_hollow", g4, 30),
    ("c01_bound_roots", g5, 28),
    ("c01_duskcap_hollow", g6, 34),
    ("c01_lantern_ring", g7, 36),
    ("c01_sleeping_thicket", g8, 42),
    ("c01_three_springs", g9, 40),
    ("c01_grovekeepers_knot", g10, 50),
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
    doc["accent"] = "#FFC93C"
    doc["slate"] = "#123640"
    doc["backdrop"] = SKIES[0]
    doc["mapStrips"] = list(STRIPS)

    levels = []
    for i, (lid, make, target) in enumerate(BOARDS):
        board = built[lid][0]
        level = collections.OrderedDict()
        level["id"] = lid
        level["width"] = board.w
        level["height"] = board.h
        level["mapX"], level["mapY"] = PLACES[i]
        if BUDGET[i] != 0:
            level["budgetFactor"] = BUDGET[i]
        accent, slate = PALETTE[lid]
        if accent:
            level["accent"] = accent
            level["slate"] = slate
        if i > 0:                       # the first glade inherits the chapter's backdrop
            level["backdrop"] = SKIES[i]
        level["rows"] = board.rows()
        levels.append(level)
    doc["levels"] = levels
    return doc


def write_strings():
    """Applies `REWORDED`, and proves every string this chapter needs is present.

    Returns (changed, already, conflicts, missing). See `REWORDED` for why the contract is
    "rewrite two, invent none" rather than `c02_millvale.py`'s whole-table one.
    """
    doc = json.load(io.open(LOC, encoding="utf-8"))
    entries = {e["key"]: e for e in doc["entries"]}

    wanted = [f"chapter.{CHAPTER}.name"]
    for lid, _, _ in BOARDS:
        wanted += [f"level.{lid}.name", f"level.{lid}.tagline"]
    missing = [k for k in wanted if k not in entries]

    changed, already, conflicts = [], [], []
    for key, (accepted, text) in REWORDED.items():
        entry = entries.get(key)
        if entry is None:
            continue                       # already named by `missing`
        if entry["text"] == text:
            already.append(key)
        elif entry["text"] in accepted:
            changed.append((key, entry["text"], text))
            entry["text"] = text
        else:
            conflicts.append((key, entry["text"], text))

    if changed:
        with io.open(LOC, "w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
            f.write("\n")
    return changed, already, conflicts, missing


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
        par = board.par()
        budget = "none" if BUDGET[i] < 0 else \
            f"{-(-par * int((BUDGET[i] or 1.6) * 100) // 100)}"
        print(f"{i + 1:>2} {lid:<24} {board.w}x{board.h} par {par:<3} "
              f"turns {budget:>4}  "
              f"tiles {r['tiles']:<3} glance {len(r['glance']):>2}  "
              f"arms {r['solutions']:>2} wins {r['wins']}  near {len(board.hazards()):>2}  "
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
    changed, already, conflicts, missing = write_strings()
    for key, was, now in changed:
        print(f"  rewrote {key}\n    was: {was}\n    now: {now}")
    for key in already:
        print(f"  {key} already reads as this rebuild wants it")
    for key, found, now in conflicts:
        print(f"  LEFT ALONE {key} - it reads neither the old line nor the new one, so "
              f"somebody has re-worded it\n    found: {found}\n    wanted: {now}")
    for key in missing:
        print(f"  MISSING {key}")
    print(f"rewrote {len(changed)} string(s) in {os.path.relpath(LOC, ROOT)}")
    if missing:
        print("\nA missing string fails Validate Content. Add it by hand - this file "
              "deliberately does not invent chapter text.")
        return 1
    print("\nNext: Content > Sync Manifest, then Validate Content.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
