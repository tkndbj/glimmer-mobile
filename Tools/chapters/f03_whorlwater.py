"""Whorlwater - Lightfall's third chapter, and the one that brings the whorl.

**The shipped chapter is `Content/chapters/f03_whorlwater.json`, not this file.** That is what the
game reads and what the build gate proves; nothing at runtime knows this script exists.

    python Tools/chapters/f03_whorlwater.py --check     # does the shipped JSON still match?
    python Tools/chapters/f03_whorlwater.py             # rewrite it from here

**A whorl draws the motes standing either side of it together and mixes them into one.** Any light
opens it - a burst beside it, a lens beam, or a drop straight onto it - and on the *next* wave it
takes what is standing to its left and its right, leaves one mote holding both, and is gone. A
whorl with nothing beside it closes instead, which is what keeps it removable and the well
winnable.

**It is the mode's own arithmetic on a pair of operands it never had.** Everything else in
Lightfall adds a *colour* to a cell - a drop adds one channel, a wash adds one, a beam adds all
three. This is the only place two *motes* are combined, so a cyan and a red that were each a drop
away from white are none. Nothing has to be taught for it: a player who has cooked one mote
already knows what yellow and blue make.

**It pulls sideways, which is a fact about gravity rather than a choice.** The well falls, so
across is the one direction nothing here ever travels in - the same observation that makes a lens
fire sideways, turned into a verb. It is the only object in this mode that *moves* a mote.

**This chapter replaced two mechanics, and the second replacement is the useful one.** The first
cut shipped a *mirror* that turned a lens's beam ninety degrees; it had no event of its own, so on
a board with no glass it did nothing at all, and it was reported as useless inside a session. The
second shipped a *wick* - one authored channel, lit by any light, washing that colour into its four
neighbours - and it was reported as **boring**, correctly. It had an event and no *decision*: its
colour was fixed by the author, its trigger was free, and its effect was the same on every board it
ever stood on. A whorl is bought with **position**, which is the one currency this mode had never
charged in: what it gives back is decided by what the player arranged beside it, and the well
collapses under every chain. See invariant 26h.

**Nothing here authors a difficulty number.** Par is the fewest drops that empty the well without
ever breaching the brim, found by search (`Tools/verify/fall.py`, mirroring the shipping
`FallSolver`), and both star lines and the supply are multiples of it.

**The shapes are drawn by hand and only the fill is swept.** Where the terrain lies, how the
islands are cut apart and where the whorls stand in them is what teaches, so it is composed; which
blend stands where and what the well deals is the cheap half of the search and is hunted. A random
per-cell fill is almost never solvable - every pure mote wants two more channels and the stragglers
pile up faster than any chain clears them - so the sweep fills a *connected blob* with one blend,
which is the Deep Well's own construction, and picks the cells either side of a whorl separately,
because those are the ones the whole mechanic turns on.

**The ladder is four dials and par is not one of them.** Par is length, so it wanders on purpose.
What climbs is how much is standing in the well (4 motes to 26), how little headroom it leaves
(4 rows to 2), how many whorls there are, and whether there is glass to compose with them. From
the second rung on, a player who never looks ahead loses every board. The numbers each well lands
on are printed by `report()` when this file is run.
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

import fall                                                      # noqa: E402
import mapart                                                    # noqa: E402

CHAPTER = "f03_whorlwater"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")
LOC = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")

#: Firelight on still water, against Glasswater's cold cyan and the Deep Well's ember. The accent
#: is what every backdrop is turned onto (`make_chapter_art.py`'s `vivid`) as well as what the map
#: is graded to, so the three chapters of this mode read as three places.
ACCENT, SLATE = "#FF9A5C", "#1A1220"

#: Which chapter of its own mode this is. It buys the map and the ten skies - see
#: `mapart`, which owns that arithmetic for every chapter of every mode.
ORDINAL = 3
STRIPS = mapart.strips(ORDINAL)
SKIES = mapart.skies(ORDINAL)

#: The marker sits opposite the last glade, which ends on the right - the same side
#: c03_amberwood puts it on, because the two chapters draw the same map.
TEASER = 0.30

#: The Deep Well's own spacing, proved against `ChapterMapValidator` for ten nodes at five
#: strips, which is what the third chapter of every mode is cut into.
WHERE = [(0.30, 0.055), (0.72, 0.140), (0.26, 0.225), (0.70, 0.310), (0.28, 0.395),
         (0.74, 0.480), (0.30, 0.560), (0.68, 0.645), (0.26, 0.730), (0.72, 0.815)]


class Well(object):
    """One authored level: an id that is permanent, a name, a line, a board and a procession."""

    def __init__(self, lid, name, tagline, rows, motes, budget=0.0):
        self.id = lid
        self.name = name
        self.tagline = tagline
        self.rows = rows
        self.motes = motes
        self.budget = budget

    def survey(self):
        return fall.survey(self.rows, self.motes)


BOARDS = [
    # The verb, on the smallest board that can carry it: a yellow and a cyan either side of a
    # whorl, which between them are white. One drop opens it and the pair goes off where it
    # stood.
    #
    # **And the order already matters**, which is the second thing this mechanic asks and is
    # worth meeting on a board with four motes on it. The lone cyan on the left can only be
    # finished by red, and red is the first drop; open the whorl with it instead and the cyan is
    # stranded. Two drops, one of which has to come first.
    Well("f03_first_whorl", "First Whorl", "It takes whatever stands either side of it.",
         [".....",
          ".....",
          ".....",
          ".....",
          ".....",
          "CY@C."],
         "RGB"),

    # The pair has to be *made*. Nothing here is white to begin with: the blob on the left has to
    # be cooked into something the blue on the right completes, and only then is the whorl worth
    # opening. Opening it early spends it on a pair that is not ready yet, which is the whole of
    # what makes the timing a decision.
    Well("f03_make_the_pair", "Make the Pair", "Cook one side before you open it.",
         ["......",
          "......",
          "......",
          "......",
          ".CC...",
          ".CM@B."],
         "RGBB"),

    # Two blobs with bare ground between them, which is the one thing a wash can never cross: a
    # burst reaches what it *touches* and stops. The whorl stands in the gap, and what it hands
    # back is the only light that ever gets from one side to the other.
    Well("f03_the_span", "The Span", "A wash stops at the gap. This does not.",
         ["......",
          "......",
          "......",
          ".Y...C",
          ".Y...C",
          ".YY@YC"],
         "GBR"),

    # Buried, and that is the third way in. This whorl is under the stack rather than on top of
    # it, so no drop will ever land on it - the only thing that reaches it is a chain that got
    # there, which makes where the chain *goes* the decision.
    Well("f03_buried", "Buried", "Some of them you have to reach.",
         ["......",
          "......",
          "......",
          "...Y..",
          ".M.Y..",
          ".MMM..",
          ".MM@M."],
         "BRGB"),

    # Two shores and a channel of bare ground between them, on a board twice the size of anything
    # before it. The whorl is on the smaller shore, and what it gives back has to be planned from
    # the other side of the well.
    Well("f03_two_shores", "Two Shores", "Nothing crosses but what you arrange.",
         [".......",
          ".......",
          ".......",
          ".......",
          "YYY..YY",
          "YYY.MMM",
          "YYY.G@B"],
         "GBR"),

    # Two whorls, one to a shore, and one procession to feed both. They cannot both be opened
    # with the same drop and they cannot both wait, so the order the two are taken in is the
    # board.
    Well("f03_twin_mouths", "Twin Mouths", "Two of them, and one deal to feed both.",
         [".......",
          ".......",
          ".......",
          ".......",
          "CCC.YYY",
          "CCC.YYY",
          "C@R.R@G"],
         "GBR"),

    # Glass and a whorl on one board, which is what makes them one game rather than two sharing
    # a well. A beam is the third way a whorl is opened, and the only one that crosses the gap -
    # so the pane on the left shore decides when the mouth on the right turns.
    Well("f03_through_the_glass", "Through the Glass", "A beam is the third way in.",
         [".......",
          ".......",
          ".......",
          ".......",
          "YYY..YY",
          "YYY.YYY",
          "YYY.YYY",
          "YOY.Y@C"],
         "BRGB"),

    # The deepest well in the chapter, and the tightest: two rows of headroom, so a wasted drop
    # costs a row as well as a mote. The whorl is on the floor of the tall side, under everything.
    Well("f03_the_deep_draw", "The Deep Draw", "Little room, and a long way down.",
         [".......",
          ".......",
          ".......",
          "MMM....",
          "MMM..C.",
          "MMM.CCC",
          "MMM.CCC",
          "C@C.CCC"],
         "GBBR"),

    # Everything this mode has, on one board: two whorls, a pane, and a deal that has to serve
    # all three. Both merges are worth making and neither is free.
    Well("f03_every_light", "Every Light", "Glass, mouths, and one procession.",
         [".......",
          ".......",
          ".......",
          ".......",
          "MMM.YYY",
          "MMM.YYY",
          "MMM.OMM",
          "M@B.G@M"],
         "GBBR"),

    # The finale. Twenty-six motes, two rows of headroom, two whorls, and both of them have to
    # reach white for the shortest line - a player who never looks ahead loses.
    Well("f03_whorlwater", "Whorlwater", "Two mouths, and everything you have learnt.",
         [".......",
          ".......",
          ".......",
          ".Y...Y.",
          "MMM.MMM",
          "MMM.MMM",
          "MMM.YYY",
          "C@Y.G@Y"],
         "BRGG"),
]


# ---------------------------------------------------------------------------- writing it out
def level_json(well, at):
    x, y = WHERE[at]
    block = {"width": len(well.rows[0]), "height": len(well.rows),
             "rows": well.rows, "motes": well.motes}

    out = {"id": well.id, "mapX": x, "mapY": y}
    if at:                              # level one takes the chapter's own
        out["backdrop"] = SKIES[at]
    if well.budget:
        out["budgetFactor"] = well.budget
    out["fall"] = block
    return out


def chapter_json():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        "accent": ACCENT,
        "slate": SLATE,
        "backdrop": SKIES[0],
        "mapStrips": list(STRIPS),
        "teaserX": TEASER,
        "levels": [level_json(w, i) for i, w in enumerate(BOARDS)],
    }


def report():
    """What each well actually asks, counted rather than argued about."""
    print(f"{'level':<24}{'well':<7}{'motes':<7}{'whorl':<7}{'fused':<7}{'kindl':<7}{'lens':<6}"
          f"{'asks':<6}{'aim':<5}{'head':<6}{'par':<5}{'3*':<5}{'2*':<5}{'supply':<8}{'ways':<6}"
          f"{'greedy':<8}{'nodes':<8}deal")

    worst = 0
    for well in BOARDS:
        s = well.survey()
        if not s["proved"] or s["par"] < 1:
            print(f"  {well.id:<22} UNSOLVED (proved={s['proved']}, nodes={s['nodes']})")
            continue

        par = s["par"]
        bounded = well.budget >= 0
        supply = (par + fall.DEFAULT_SPARE) if bounded else 0
        greedy = s["greedy"] if s["greedy"] >= 0 else "-"
        worst = max(worst, s["nodes"])

        # `asks` is what the board charges for its glass: three channels a lens, less whatever it
        # was authored already holding. Whorls are counted beside it rather than folded in,
        # because the two are paid for in different currencies - a pane costs drops in any order
        # at all, and a whorl costs one arrangement.
        asks = sum(3 - bin(g).count('1') for g in s['glass'])

        print(f"{well.id:<24}{str(s['width']) + 'x' + str(s['height']):<7}{s['motes']:<7}"
              f"{s['whorls']:<7}{s['fused']:<7}{s['kindled']:<7}{s['lenses']:<6}{asks:<6}"
              f"{s['aim']:<5}{s['headroom']:<6}{par:<5}"
              f"{fall.over(par, 120):<5}{fall.over(par, 140):<5}{(supply or 'free'):<8}"
              f"{s['ways']:<6}{str(greedy):<8}{s['nodes']:<8}{well.motes}")

    print(f"\ndearest proof: {worst} position(s) against the 40000 a level is expected to "
          f"cost and the 120000 it is refused above - see FallValidator. Cost goes as the "
          f"column count to the power of par.")


# ---------------------------------------------------------------------------- the strings
def write_strings(check):
    """Adds this chapter's names and taglines, and never rewrites one somebody has changed.

    The *mode's* strings - its readouts, its defeats, its five lessons, the whorl among them -
    live in `f01_strings.py`, because a mode's vocabulary outlives any one chapter.
    """
    doc = json.load(io.open(LOC, encoding="utf-8"))
    entries = {e["key"]: e for e in doc["entries"]}

    wanted = [(f"chapter.{CHAPTER}.name", "Whorlwater")]
    for well in BOARDS:
        wanted.append((f"level.{well.id}.name", well.name))
        wanted.append((f"level.{well.id}.tagline", well.tagline))

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
