"""The Thicket - Budburst's first chapter, ten groves and the basket that empties each.

**The shipped chapter is `Content/chapters/b01_thicket.json`, not this file.** That is what the
game reads and what the build gate proves; nothing at runtime knows this script exists.

    python Tools/chapters/b01_thicket.py --check     # does the shipped JSON still match?
    python Tools/chapters/b01_thicket.py             # rewrite it from here

**Nothing here authors a difficulty number.** Par is the fewest taps that free every critter,
found by search (`Tools/verify/bud.py`, mirroring the shipping `BudSolver`), and both star lines
derive from it. The one number a grove does author is `spare`, which is the room above par it
forgives - a count, because the cost of a mistake here is a count (invariant 26e).

**The boards were swept for, and only their colour was.** Every layout below is drawn by hand -
where the cocoons sit, where the old wood runs, how big the grove is - and what was searched is
the *fill*: which colour stands on each cell and which basket the grove is dealt. That is the
cheap half of the search and the half that decides how a board plays, and it is
`b01_firstburst`'s bargain kept for the other nine. The filler lays the grove in **pairs** on
purpose: two alike touching is one wash from bursting, so a grove made of them cascades, which
is what this mode is for.

**Par is 3 on every rung, and that is a fact about the mode rather than a shortcut.** Two things
push against each other here and only one of them can win. Cost goes as the flower count to the
power of par, and the player's own device runs this search when it opens a level (invariant 26d)
- so a par of 4 on a grove big enough to cascade costs tens of thousands of positions and is
refused. Swept for anyway, a par-4 grove comes back at twenty flowers with a **one-wave** best
tap: solvable, correctly par'd, validated, and with the mode taken out of it. So par stays where
the cascades are, exactly as `CONTENT.md` says a glade's does - *par is length, not difficulty*.

**The ramp is one dial: how many are shut in.** Four, four, five, five, six, six, seven, eight,
eight, eight - and with it how much grove there is (36 flowers to 50) and how many cocoons take
two cracks rather than one. Every grove is dealt `par + 5`, which is eight taps for a three-tap
answer, and it stays eight on the tenth: freeing eight critters with the same allowance is more
to do than freeing four **without ever being tighter**, which is the only kind of harder this
mode is allowed to get (invariant 20k).

**Three other dials were tried and thrown away.** `spare` came down from five to three across the
chapter; `greedy` - whether a thoughtless run still scored three stars - was true early and false
late; and **old wood** ran through most of the middle of it. All three are ramps built out of
withholding, on a mode commissioned to be generous, and the wood is the clearest case: a barrier
is the one object here that can only ever make a chain *shorter*. `#` still parses (it is shared
vocabulary with Groovekeeper) and `BudValidator` warns on a grove that stands any; `bud_wood` is a
spent lesson id. Four level ids went with them and must never be reused: `b01_oldwood`,
`b01_twinhollows`, `b01_bramblebright` and `b01_hollowbanks`.

**What every grove has to do**, and what the sweep held out for: a *best play* that runs six waves
or more, and an opening tap that is itself a chain. The chapter's loudest is `b01_widewild`, whose
best opening tap runs **nine waves, bursts twenty-nine of fifty flowers and frees five critters at
once**; the finale's runs seven and frees five, and `b01_wildwaking`'s frees **six**. A grove whose
best play is three separate one-wave taps passes every other check in this repository with the
mode taken out of it, which is why this one is held to it (`BudLadderTests`).

**A grove must be authored settled.** Three alike already touching would go off before anybody
had touched the board - the player is shown a chain they did not cause, and par is measured
against a position they never met. `BudValidator.Settled` and `content.py` both refuse one.
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

import bud                                                       # noqa: E402
import b01_strings                                               # noqa: E402

CHAPTER = "b01_thicket"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")

ACCENT, SLATE = "#FFC24A", "#241A0E"
BACKDROP = "play_6"
STRIPS = ["strip0", "strip1", "strip2", "strip3", "strip4", "strip5"]

#: Left and right down the map, the spacing the other six-strip chapters use - proved against
#: `ChapterMapValidator` for ten nodes rather than guessed at.
WHERE = [(0.30, 0.055), (0.72, 0.140), (0.26, 0.225), (0.70, 0.310), (0.28, 0.395),
         (0.74, 0.480), (0.30, 0.560), (0.68, 0.645), (0.26, 0.730), (0.72, 0.815)]


class Grove(object):
    """One authored level: a permanent id, a name, a line, a board and a basket."""

    def __init__(self, lid, name, tagline, rows, colours, regrow=None, spare=0):
        self.id = lid
        self.name = name
        self.tagline = tagline
        self.rows = rows
        self.colours = colours
        self.regrow = regrow
        self.spare = spare

    @property
    def satchel(self):
        return self.par + (self.spare or bud.DEFAULT_SPARE)

    def survey(self):
        if not hasattr(self, "_survey"):
            self._survey = bud.survey(self.rows, self.colours, self.regrow)
        return self._survey

    @property
    def par(self):
        return self.survey()["par"]

    def json(self, at):
        block = {
            "width": len(self.rows[0]),
            "height": len(self.rows),
            "rows": list(self.rows),
            "colours": self.colours,
        }
        if self.regrow:
            block["regrow"] = self.regrow
        if self.spare:
            block["spare"] = self.spare

        return {
            "id": self.id,
            "mapX": WHERE[at][0],
            "mapY": WHERE[at][1],
            "bud": block,
        }


GROVES = []

#: 1. the verb, on the smallest grove in the game. par 3, satchel 8, three stars at 4, ways 80, greedy 3;
#: the best opening tap runs 3 wave(s), bursts 10 and frees 2.
GROVES.append(Grove(
    "b01_firstburst", "First Burst",
    "Tap a flower. Watch it run.",
    [
        "MRMGG",
        "MoMRR",
        "BRRGG",
        "RBBoB",
        "RMoRB",
    ], "RGB", "RGBYMCW"))

#: 2. a little more grove, one more shut in. par 3, satchel 8, three stars at 4, ways 54, greedy 3;
#: the best opening tap runs 4 wave(s), bursts 13 and frees 2.
GROVES.append(Grove(
    "b01_catchalight", "Catch Alight",
    "One tap, and it keeps going.",
    [
        "BBYRYG",
        "GoBGoR",
        "BBRGRG",
        "RRBBYY",
        "BoGGoG",
        "GGRYBB",
    ], "RBG", "RGBYM"))

#: 3. the tough cocoon. par 3, satchel 8, three stars at 4, ways 87, greedy 3;
#: the best opening tap runs 5 wave(s), bursts 16 and frees 4.
GROVES.append(Grove(
    "b01_twiceknocked", "Twice Knocked",
    "A second ring takes a second burst.",
    [
        "RGGYBB",
        "RoRRoR",
        "GYBYYB",
        "RYORBG",
        "BBRGBY",
        "YoYRoY",
    ], "RBG", "RGBYMCW"))

#: 4. six shut in, and the most forgiving board in the chapter. par 3, satchel 8, three stars at 4, ways 129, greedy 3;
#: the best opening tap runs 4 wave(s), bursts 20 and frees 5.
GROVES.append(Grove(
    "b01_sunspill", "Sun Spill",
    "Warm ground. Everything wants to go.",
    [
        "YYMYRR",
        "RoMYoB",
        "RBBRYG",
        "YOGGOG",
        "BGRRYY",
        "BoMMoR",
    ], "RBG", "RGBYMC"))

#: 5. seven waves off the opening tap. par 3, satchel 8, three stars at 4, ways 102, greedy 4;
#: the best opening tap runs 7 wave(s), bursts 22 and frees 5.
GROVES.append(Grove(
    "b01_dewfall", "Dewfall",
    "Six shut in, and the grove is willing.",
    [
        "MMCGRCR",
        "CoCGBoR",
        "CMRCBGG",
        "BMOMOCM",
        "BCBRBBM",
        "CoBMMoG",
    ], "GBR", "RGBYMC"))

#: 6. seven by seven. par 3, satchel 8, three stars at 4, ways 109, greedy 3;
#: the best opening tap runs 4 wave(s), bursts 15 and frees 4.
GROVES.append(Grove(
    "b01_widewild", "The Wide Wild",
    "Room enough for something enormous.",
    [
        "RRMBCCM",
        "GoBRRoC",
        "RGBCCGG",
        "BROBOBB",
        "BRBRRGG",
        "CoBGGoR",
        "CRRMMGG",
    ], "BGR", "RYGMBC"))

#: 7. the biggest grove yet. par 3, satchel 8, three stars at 4, ways 185, greedy 3;
#: the best opening tap runs 3 wave(s), bursts 10 and frees 1.
GROVES.append(Grove(
    "b01_honeylight", "Honeylight",
    "Seven, and the light is already moving.",
    [
        "CGCGCGGY",
        "CoRRBCoY",
        "GBBYGCBG",
        "YYGOOYBG",
        "GRRGGCYY",
        "GoBBYGoG",
        "BBRoYGCC",
    ], "GBR", "RGBYMC"))

#: 8. a ten-wave best play. par 3, satchel 8, three stars at 4, ways 86, greedy 3;
#: the best opening tap runs 4 wave(s), bursts 17 and frees 3.
GROVES.append(Grove(
    "b01_wildwaking", "Wild Waking",
    "Eight shut in. Wake them.",
    [
        "YBRRMYGM",
        "YoYBYRoM",
        "RMRBYRGR",
        "RMORROGR",
        "YBBGGBBM",
        "YoGBBRoM",
        "MMYooMRR",
    ], "BRGR", "RYGMBC"))

#: 9. the opening tap frees seven of the nine. par 3, satchel 8, three stars at 4, ways 99, greedy 3;
#: the best opening tap runs 6 wave(s), bursts 25 and frees 7.
GROVES.append(Grove(
    "b01_everbloom", "Everbloom",
    "Nine shut in, and the grove is full.",
    [
        "YoGYMBoR",
        "MYGYMBMR",
        "RBOGYOGG",
        "RBMGMGMR",
        "GYOBMOGR",
        "MYGYYRRB",
        "MoRMoGoB",
    ], "GRB", "RGBYMCW"))

#: 10. the finale: eight waves, twenty-seven flowers and ten critters off the opening tap. par 3, satchel 8, three stars at 4, ways 61, greedy 3;
#: the best opening tap runs 8 wave(s), bursts 27 and frees 10.
GROVES.append(Grove(
    "b01_thicketheart", "The Thicket's Heart",
    "Everything this grove can do, at once.",
    [
        "YoRGCBoR",
        "YCCGBGBR",
        "COGooGOM",
        "BBYYBBRM",
        "MOGooCOB",
        "RYYMYRMY",
        "YoCCRBoY",
    ], "RGB", "RGBYM"))

# ---------------------------------------------------------------------------- writing it out
def chapter_json():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        "accent": ACCENT,
        "slate": SLATE,
        "backdrop": BACKDROP,
        "mapStrips": list(STRIPS),
        "levels": [g.json(i) for i, g in enumerate(GROVES)],
    }


def report():
    print("%-20s %-5s %-4s %-4s %-4s %-4s %-4s %-5s %-6s  %s"
          % ("id", "size", "flw", "shut", "par", "sat", "gold", "ways", "greedy", "best tap"))

    for g in GROVES:
        s = g.survey()
        gold = bud.over(s["par"], 120)
        greedy = s["careless"]

        print("%-20s %dx%-3d %-4d %-4d %-4d %-4d %-4d %-5d %-6s  %dw/%df/%dc  (%d nodes)"
              % (g.id, s["w"], s["h"], s["flowers"], s["cocoons"], s["par"], g.satchel,
                 gold, s["ways"], greedy if greedy >= 0 else "-",
                 s["bestWaves"], s["bestBurst"], s["bestFreed"], s["nodes"]))


def level_strings():
    wanted = {"chapter.%s.name" % CHAPTER: "The Thicket"}
    for g in GROVES:
        wanted["level.%s.name" % g.id] = g.name
        wanted["level.%s.tagline" % g.id] = g.tagline
    return wanted


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    report()

    doc = chapter_json()
    added, differs, stale = b01_strings.apply(level_strings(), (), args.check)

    if args.check:
        shipped = json.load(io.open(BODY, encoding="utf-8"))
        same = shipped == json.loads(json.dumps(doc))

        for key, text in added:
            print("  MISSING  %s = %s" % (key, text))
        for key, was, now in differs:
            print("  DIFFERS  %s\n    file: %s\n    here: %s" % (key, was, now))

        if same and not added:
            print("\n%s matches this source" % os.path.relpath(BODY, ROOT))
            return 0

        if not same:
            print("\n%s DIFFERS from this source" % os.path.relpath(BODY, ROOT))
        return 1

    with io.open(BODY, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print("\nwrote %s" % os.path.relpath(BODY, ROOT))
    for key, _ in added:
        print("  added   %s" % key)
    for key, was, now in differs:
        print("  LEFT ALONE %s - somebody has re-worded it" % key)

    print("\nNext: python Tools/chapters/b01_strings.py, then Content > Sync Manifest, "
          "then Validate Content.")
    return 0


if __name__ == "__main__":
    sys.setrecursionlimit(10000)
    raise SystemExit(main())
