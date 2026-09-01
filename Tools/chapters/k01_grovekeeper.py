"""The Clearing - Groovekeeper's first chapter, ten grooves and the procession that blooms each.

**The shipped chapter is `Content/chapters/k01_grovekeeper.json`, not this file.** That is what
the game reads and what the build gate proves; nothing at runtime knows this script exists. Same
bargain as the glade chapters and the Deep Well, and the same two commands:

    python Tools/chapters/k01_grovekeeper.py --check     # does the shipped JSON still match?
    python Tools/chapters/k01_grovekeeper.py             # rewrite it from here

**Nothing here authors a difficulty number**, and that is the whole reason a groove can be
trusted. Par is the fewest tiles that open every bed, found by search (`Tools/verify/keeper.py`,
mirroring the shipping `KeeperSolver`), and both star lines and the basket are derived from it. A
typed par is the failure with no symptom: one too high hands three stars to a careless run for
ever, one too low makes them unreachable, and neither is visible in the file that caused it.

**The ladder is five dials and par is only one of them.** Par wanders on purpose - 2, 3, 4, 5, 5,
6, 6, 7, 6, 8 - exactly as a glade chapter's does, because par is length and ten rising numbers
read as a treadmill. What climbs is:

* **what the ground denies** - an open clearing, then stone, then corridors;
* **beds** - one, then two, then four at once;
* **heartbeds** - a bed that takes one colour and no other, so the *order* of the basket becomes
  the puzzle rather than a decoration (invariant 20e's argument, for a second mode);
* **`ways`** - how many different grooves of exactly par tiles win. Invariant 5d, counted: a
  groove almost any tidy play finishes is one where the ground is deciding nothing;
* **`greedy`** - whether a player who never looks past this turn finishes inside the basket.

**The first groove cannot run out.** `budgetFactor` is negative, which is what the DTO documents
one for, and it is the same decision the first glade and the first well make: the heart gate is
the only thing that can stop somebody playing, and the worst moment to meet it is while they are
still working out what the verb is. It also keeps the basket lesson off that board, because a
lesson shown over a meter that is not there can never be shown again.

Note the exact claim, which is the one Lightfall's first well makes too: the *basket* is off, not
every ending. A groove with nowhere left to grow is still over, because there is genuinely nothing
left to do on it and a board that can be neither won nor ended is the one state invariant 20g
forbids. On this groove that means filling twenty-seven cells without opening either bed, which is
work.

**Level five is the one the mode exists for.** Four beds around one bare cell, each a single
channel short of the same colour: four tiles to plant them and one to open all four at once. It
is the largest flourish the rules allow (`KeeperFlourish.Most`), it is the shortest answer the
board has, and the shape says so before a word is read - which is what a mechanic being *shown*
rather than explained looks like.

The numbers each groove lands on are printed by `report()` when this file is run.
"""
import argparse
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "Tools", "verify"))
sys.path.insert(0, HERE)

import keeper                                                    # noqa: E402
import mapart                                                    # noqa: E402

CHAPTER = "k01_grovekeeper"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")
LOC = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")

ACCENT, SLATE = "#7BD86A", "#14241A"

#: Which chapter of its own mode this is. It buys the map and the ten skies - see
#: `mapart`, which owns that arithmetic for every chapter of every mode.
ORDINAL = 1
STRIPS = mapart.strips(ORDINAL)
SKIES = mapart.skies(ORDINAL)

#: Left and right down the map, the spacing the other ten-node chapters already use - proved
#: against `ChapterMapValidator` rather than guessed at.
WHERE = [(0.30, 0.055), (0.72, 0.140), (0.26, 0.225), (0.70, 0.310), (0.28, 0.395),
         (0.74, 0.480), (0.30, 0.560), (0.68, 0.645), (0.26, 0.730), (0.72, 0.815)]

#: A groove that cannot be lost. See the note above.
UNLOSABLE = -1.0


class Groove(object):
    """One authored level: an id that is permanent, a name, a line, a ground and a procession."""

    def __init__(self, lid, name, tagline, rows, tiles, budget=0.0):
        self.id = lid
        self.name = name
        self.tagline = tagline
        self.rows = rows
        self.tiles = tiles
        self.budget = budget

    def survey(self):
        return keeper.survey(self.rows, self.tiles)


BOARDS = [
    # ------------------------------------------------------------------ 1. the verb
    # Two beds, two tiles and no basket at all - the only groove in the chapter that cannot be
    # lost, for the reason the first glade cannot.
    #
    # It is hand-drawn rather than searched for, because it has to teach in a particular order
    # and a sweep has no opinion about that. The blue between the red and the green blooms on
    # its own, which is the inversion in one move; the bed below it then wants what that first
    # tile became a neighbour of, which is the second idea and the reward for the first.
    Groove("k01_first_grove", "First Clearing", "Unlike edges bloom",
           ["......",
            "......",
            "..R*G.",
            "...*..",
            "...R.."],
           "BG", budget=UNLOSABLE),

    # ------------------------------------------------------------------ 2. the basket
    Groove("k01_the_second_bed", "The Second Bed", "Three tiles, and no more",
           ["...G..",
            "......",
            "..R*R.",
            "...*..",
            "......"],
           "BRG"),

    # ------------------------------------------------------------------ 3. stone
    # The first groove with any stone on it at all, which is what decides where the lesson
    # lands: `KeeperScreen.Lessons` asks the board whether it has one, so a decorative rock in
    # a corner of the opening groove would spend a once-in-a-lifetime lesson on scenery. The
    # first two are bare ground for exactly that reason - and `KeeperLadderTests` is what says
    # so if anybody frames them later.
    Groove("k01_stonecrop", "Stonecrop", "Nothing grows on stone",
           ["#R....#",
            "..#G#..",
            ".*.....",
            ".*#.#..",
            "#.R...#"],
           "RBRG"),

    # ------------------------------------------------------------------ 4. the boulder
    # The ground starts deciding things: the beds are on one side of the rock and half of what
    # would feed them is on the other.
    Groove("k01_the_boulder", "The Boulder", "Grow the long way round",
           ["#.....#",
            ".*.*...",
            ".G###..",
            "..B....",
            "#..G..#"],
           "RGBBR"),

    # ------------------------------------------------------------------ 5. four petals
    # The groove the mode exists for. Four beds around one bare cell, each one channel short of
    # blue: plant the four, then open all four with a single tile. It is the biggest flourish the
    # rules allow and it is also the shortest answer the board has, which is the whole design -
    # the prettiest play and the most efficient one are the same play.
    Groove("k01_four_petals", "Four Petals", "One tile. Four flowers.",
           ["..G..",
            "..*..",
            "G*.*G",
            "..*..",
            "..G.."],
           "RRRRB"),

    # ------------------------------------------------------------------ 6. the heartbed
    Groove("k01_heartwood", "Heartwood", "One bed takes one colour",
           ["#.....#",
            "..Bb...",
            ".*###R.",
            "....R..",
            "#.....#"],
           "RBBGGR"),

    # ------------------------------------------------------------------ 7. twin hearts
    Groove("k01_twin_hearts", "Twin Hearts", "Count forward, and compost the rest",
           ["...b...",
            "R#.G.#.",
            "..###..",
            ".#.g.#.",
            ".B*...."],
           "BGBRGR"),

    # ------------------------------------------------------------------ 8. the prism
    # One bed sits in a stone alcove with a single open neighbour, so its own tile and that
    # neighbour can carry two channels between them and never three. Nothing but the prism opens
    # it - which is the mechanic being *shown* rather than explained, and the reason the lesson
    # is one sentence about spending it rather than a paragraph about what it does.
    Groove("k01_the_prism", "The Prism", "One tile carries all three",
           ["#.....#",
            "..#.#*.",
            ".#*....",
            "..#.#..",
            "#R.B.G#"],
           "RGBPGR"),

    # ------------------------------------------------------------------ 9. the pocket
    # The dip, and the one board that asks both new questions at once: a heartbed that takes one
    # colour and a pocket that takes only the prism. Shorter than the groove before it and
    # tighter, which is what a dip is for - par is length, and ten rising numbers read as a
    # treadmill.
    Groove("k01_the_pocket", "The Pocket", "Save the one that fits",
           ["#R.B.G#",
            "..#.#..",
            "....*#.",
            "..#.#..",
            "#..g..#"],
           "RGBPGR"),

    # ------------------------------------------------------------------ 10. the finale
    # Two heartbeds at opposite ends of a corridor, the longest answer in the chapter, and two
    # grooves of eight tiles that win out of every sequence there is. No prism: the basket is
    # six pure tiles and the order of them is the whole of it.
    Groove("k01_keepers_grove", "The Keeper's Groove", "One colour each, and a long way to carry it",
           ["##.#.##",
            "#r...b#",
            "..#.#..",
            "#.G.R.#",
            "##.#.##"],
           "GRBGRB"),
]


# ---------------------------------------------------------------------------- writing it out
def level_json(groove, at):
    x, y = WHERE[at]
    block = {"width": len(groove.rows[0]), "height": len(groove.rows),
             "rows": groove.rows, "tiles": groove.tiles}

    out = {"id": groove.id, "mapX": x, "mapY": y}
    if at:                              # level one takes the chapter's own
        out["backdrop"] = SKIES[at]
    if groove.budget:
        out["budgetFactor"] = groove.budget
    out["keeper"] = block
    return out


def chapter_json():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        "accent": ACCENT,
        "slate": SLATE,
        "backdrop": SKIES[0],
        "mapStrips": list(STRIPS),
        "levels": [level_json(g, i) for i, g in enumerate(BOARDS)],
    }


def report():
    """What each groove actually asks, counted rather than argued about."""
    print(f"{'level':<24}{'grove':<7}{'beds':<6}{'heart':<7}{'stone':<7}{'par':<5}{'3*':<5}"
          f"{'2*':<5}{'basket':<8}{'ways':<6}{'greedy':<8}{'nodes':<8}deal")

    worst = 0
    for groove in BOARDS:
        s = groove.survey()
        if not s["proved"] or s["par"] < 1:
            print(f"  {groove.id:<22} UNSOLVED (proved={s['proved']}, nodes={s['nodes']})")
            continue

        par = s["par"]
        bounded = groove.budget >= 0
        basket = (par + keeper.DEFAULT_SPARE) if bounded else 0
        greedy = s["greedy"] if s["greedy"] >= 0 else "-"
        worst = max(worst, s["nodes"])

        print(f"{groove.id:<24}{str(s['width']) + 'x' + str(s['height']):<7}{s['beds']:<6}"
              f"{s['heartbeds']:<7}{s['stone']:<7}{par:<5}{keeper.over(par, 120):<5}"
              f"{keeper.over(par, 140):<5}{(basket or 'free'):<8}{s['ways']:<6}"
              f"{str(greedy):<8}{s['nodes']:<8}{groove.tiles}")

    print(f"\ndearest proof: {worst} position(s) against the 30000 a level is expected to cost "
          f"and the 90000 it is refused above - see KeeperValidator. Cost goes roughly as the "
          f"open cell count to the power of par.")


# ---------------------------------------------------------------------------- the strings
def write_strings(check):
    """Adds this chapter's names and taglines, and never rewrites one somebody has changed."""
    doc = json.load(io.open(LOC, encoding="utf-8"))
    entries = {e["key"]: e for e in doc["entries"]}

    wanted = [(f"chapter.{CHAPTER}.name", "The Clearing")]
    for groove in BOARDS:
        wanted.append((f"level.{groove.id}.name", groove.name))
        wanted.append((f"level.{groove.id}.tagline", groove.tagline))

    added, differs = [], []
    for key, text in wanted:
        entry = entries.get(key)
        if entry is None:
            added.append((key, text))
        elif entry["text"] != text:
            differs.append((key, entry["text"], text))

    if check:
        return added, differs

    for key, text in added:
        # Beside the keys it is related to, so the file stays readable.
        best, at = -1, len(doc["entries"])
        for i, entry in enumerate(doc["entries"]):
            shared = 0
            for a, b in zip(entry["key"], key):
                if a != b:
                    break
                shared += 1
            if shared >= best:
                best, at = shared, i + 1
        doc["entries"].insert(at, {"key": key, "text": text})

    if added:
        with io.open(LOC, "w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
            f.write("\n")

    return added, differs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    report()

    doc = chapter_json()
    added, differs = write_strings(args.check)

    if args.check:
        shipped = json.load(io.open(BODY, encoding="utf-8"))
        same = shipped == json.loads(json.dumps(doc))

        for key, text in added:
            print(f"  MISSING  {key} = {text}")
        for key, was, now in differs:
            print(f"  DIFFERS  {key}\n    file: {was}\n    here: {now}")

        if same and not added:
            print(f"\n{os.path.relpath(BODY, ROOT)} matches this source")
            return 0

        if not same:
            print(f"\n{os.path.relpath(BODY, ROOT)} DIFFERS from this source")
        return 1

    with io.open(BODY, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print(f"\nwrote {os.path.relpath(BODY, ROOT)}")
    for key, text in added:
        print(f"  added   {key} = {text}")
    for key, was, now in differs:
        print(f"  LEFT ALONE {key} - somebody has re-worded it\n    file: {was}\n    here: {now}")

    print("\nNext: Content > Sync Manifest, then Validate Content.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
