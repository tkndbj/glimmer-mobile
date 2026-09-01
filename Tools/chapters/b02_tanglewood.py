"""The Tanglewood - Budburst's second chapter, and the one that brings the runner.

**The shipped chapter is `Content/chapters/b02_tanglewood.json`, not this file.** That is what the
game reads and what the build gate proves; nothing at runtime knows this script exists.

    python Tools/chapters/b02_tanglewood.py --check     # does the shipped JSON still match?
    python Tools/chapters/b02_tanglewood.py             # rewrite it from here

**What a runner is, and why it is the only thing this mode had left.** Every object Budburst
shipped with acts on a cell and its four neighbours - the mix, the bunch, the wash, the crack, the
bomb's square - so anything else built out of adjacency competes with the chain on degree, which
is what got a mirror and a wick withdrawn from Lightfall (invariants 26g and 26h). A runner takes
the adjacency out: two squares of the grove are joined by a vine, and a bunch that **takes in**
one end sends its colour to whatever is standing on the other, however far across the grove that
is. Same operator, same wash, no new cell - and it belongs to the *ground* rather than to the
flower, which is what lets a living grove fall straight through it. See invariant 20m.

**The threshold is what stops it being a solvent.** The end has to be *in* the bunch, not beside
one. A spread that fires on anything happening nearby walks outward for ever and makes every board
more solvable (invariant 20j); one that has to be built into is a thing the player arranges - and
it is the whole decision, because what a vine is worth is settled by what is standing at the *far*
end at the instant it fires.

**Two rungs teach with a motif and the rest do not.** The teaching motif is four cells: a runner
end wearing `R` with a `Y` beside it, at both ends, against a basket that deals `G` first. Tap the
near end, `R|G` makes `Y`, three yellows burst and take the end in, and the yellow that runs down
the vine does exactly the same thing at the other end. That is the mechanic said once, on the
board, in one tap - and it is authored rather than swept, because a sweep will not find it: over
several thousand candidates on an unauthored skeleton, not one produced a vine that bought a burst
on the opening board.

**The ramp is the same dial the Thicket's was, plus the vines.** Six shut in to fourteen, 7x6 up
to 8x7, tough cocoons from the third rung, and one, two or three runners - with **three rungs
carrying no vine at all**, because a chapter of nothing but its own new object is a chapter with
one board in it. Par is 3 on every rung and cannot be anything else (invariant 26d): cost goes as
the flower count to the power of par and the player's own device runs this search when it opens
the level.

**What every grove is held to**, and what the sweep held out for: `caught` at least 1 on every
grove that carries a vine - an opening tap that bursts *more* because a vine carried, which is the
arrangement the player is making - and a best opening tap that is itself a chain. `changed` is the
gate rather than the goal: nought opening taps playing differently with the vines cut condemns the
board (invariant 26g's test), and `BudValidator.Carrying` and `content.py` both say so.

**Where it gets harder than the Thicket.** Not in the arithmetic - every grove is still dealt
`par + 5` and still graded on the same multiples. It is that there is more shut in, more grove to
read, and a second thing on the board to work out. `greedy` is still the bar rather than a target
(invariant 20k): a careless player finishes every one of these, and on the later rungs they no
longer three-star it.
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
import mapart                                                    # noqa: E402

CHAPTER = "b02_tanglewood"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")

#: A lime running over deep leaf, which is the vine's own colour rather than the Thicket's honey.
ACCENT, SLATE = "#C8E24A", "#1B2A12"

#: Which chapter of its own mode this is. It buys the map and the ten skies - see `mapart`,
#: which owns that arithmetic for every chapter of every mode (invariant 7c). Being the second
#: chapter of Budburst, it draws exactly what the second chapter of the glades draws: `map2` and
#: skies ten to nineteen. That is the rule working rather than a coincidence - a mode is told
#: apart on the map by its perch and by nothing else.
ORDINAL = 2
STRIPS = mapart.strips(ORDINAL)
SKIES = mapart.skies(ORDINAL)

#: Left and right down the map, the spacing `c02_millvale` uses - the other four-strip chapter,
#: proved against `ChapterMapValidator` for ten nodes rather than guessed at.
WHERE = [(0.30, 0.065), (0.70, 0.145), (0.26, 0.220), (0.72, 0.300), (0.26, 0.390),
         (0.68, 0.485), (0.28, 0.560), (0.72, 0.650), (0.23, 0.752), (0.71, 0.830)]

#: The marker sits opposite the last grove, which ends on the right - the same side
#: `c02_millvale` puts it on, because the two chapters draw the same map.
TEASER = 0.30


def canon(runners):
    """The vines re-tagged in reading order, which is what `BudLayout.WrittenRunners` answers.

    **The shipped file is the canonical form on purpose.** Which letter an author happens to give
    a vine says nothing - `a` is not special and the parser joins ends by matching tags - so a
    round-trip proof against a hand-picked ordering would fail on a file that is exactly right.
    Normalising here makes the body, the fixture and `WrittenRunners` one answer instead of three.
    """
    seen, out = {}, []
    for row in runners:
        line = []
        for c in row:
            if c == ".":
                line.append(".")
                continue
            if c not in seen:
                seen[c] = chr(ord("a") + len(seen))
            line.append(seen[c])
        out.append("".join(line))
    return out


class Grove(object):
    """One authored level: a permanent id, a name, a line, a board, its vines and a basket."""

    def __init__(self, lid, name, tagline, rows, colours, regrow=None, runners=None, spare=0):
        self.id = lid
        self.name = name
        self.tagline = tagline
        self.rows = rows
        self.colours = colours
        self.regrow = regrow
        self.runners = runners
        self.spare = spare

    @property
    def satchel(self):
        return self.par + (self.spare or bud.DEFAULT_SPARE)

    def survey(self):
        if not hasattr(self, "_survey"):
            self._survey = bud.survey(self.rows, self.colours, self.regrow, self.runners)
        return self._survey

    @property
    def par(self):
        return self.survey()["par"]

    def json(self, at):
        block = {
            "width": len(self.rows[0]),
            "height": len(self.rows),
            "rows": list(self.rows),
        }
        if self.runners:
            block["runners"] = canon(self.runners)

        block["colours"] = self.colours

        if self.regrow:
            block["regrow"] = self.regrow
        if self.spare:
            block["spare"] = self.spare

        out = {
            "id": self.id,
            "mapX": WHERE[at][0],
            "mapY": WHERE[at][1],
        }
        if at:                          # grove one takes the chapter's own
            out["backdrop"] = SKIES[at]
        out["bud"] = block
        return out


GROVES = []

#: 1. par 3, satchel 8, three stars at 4, ways 49, greedy 3; 2 changed and 2 caught of 18 opening taps, best tap fires 1;
#: the best opening tap runs 2 wave(s), bursts 6 and frees 2.
GROVES.append(Grove(
    "b02_firstvine", "First Vine",
    "It goes off here. It answers over there.",
    [
        "MoMMCGG",
        "RRYRGoB",
        "CYGRGBo",
        "CGoGYBM",
        "GRRBRYG",
        "MoCMCoB",
    ], "GBR", "RGBYMCW",
    [
        ".......",
        ".a.....",
        ".......",
        ".......",
        "....a..",
        ".......",
    ]))

#: 2. par 3, satchel 8, three stars at 4, ways 43, greedy 3; 2 changed and 2 caught of 17 opening taps, best tap fires 1;
#: the best opening tap runs 3 wave(s), bursts 9 and frees 4.
GROVES.append(Grove(
    "b02_longreach", "Long Reach",
    "The vine crosses what a chain never could.",
    [
        "YoGCoMG",
        "GRYGBBG",
        "oYMRCoR",
        "CCoCYMR",
        "BGGMoYM",
        "BoRRYRM",
    ], "GBR", "RGBYMCW",
    [
        ".......",
        ".a.....",
        ".......",
        ".......",
        ".......",
        ".....a.",
    ]))

#: 3. par 3, satchel 8, three stars at 4, ways 89, greedy 3; no vine;
#: the best opening tap runs 7 wave(s), bursts 30 and frees 5.
GROVES.append(Grove(
    "b02_deepthicket", "Deep Thicket",
    "No vines here. Just a great deal of grove.",
    [
        "YoRYYoG",
        "GBBOMYB",
        "YRRCGMB",
        "CoCMGoC",
        "MBGGYBB",
        "MGoCoMC",
        "GRRCYMC",
    ], "BGR", "RGBYMC"))

#: 4. par 3, satchel 8, three stars at 4, ways 59, greedy 3; 1 changed and 1 caught of 21 opening taps, best tap fires 1;
#: the best opening tap runs 6 wave(s), bursts 27 and frees 6.
GROVES.append(Grove(
    "b02_windingway", "The Winding Way",
    "Feed the end, not the flower beside it.",
    [
        "MoRRGoY",
        "GMBMGBG",
        "oRMCBoY",
        "CRBOBRC",
        "CoYRMCo",
        "GRBMRGB",
        "GYYoBBR",
    ], "RGB", "RGBYMC",
    [
        ".......",
        "..a....",
        ".......",
        ".......",
        ".......",
        "....a..",
        ".......",
    ]))

#: 5. par 3, satchel 8, three stars at 4, ways 68, greedy 3; 1 changed and 1 caught of 20 opening taps, best tap fires 1;
#: the best opening tap runs 3 wave(s), bursts 12 and frees 4.
GROVES.append(Grove(
    "b02_twovines", "Two Vines",
    "One is taught. The other you will find.",
    [
        "RoRBBGoC",
        "GGCCYRCM",
        "RCYoBGoM",
        "RBMMORRC",
        "YoGGoBCM",
        "RYMBGCBM",
        "BBoBGoCC",
    ], "BRG", "RGBYMC",
    [
        ".......b",
        ".a......",
        "........",
        "........",
        "........",
        "......a.",
        "b.......",
    ]))

#: 6. par 3, satchel 8, three stars at 4, ways 85, greedy 4; no vine;
#: the best opening tap runs 7 wave(s), bursts 36 and frees 6.
GROVES.append(Grove(
    "b02_thewilds", "The Wilds",
    "Ten shut in and nothing to help you.",
    [
        "MoGGCOBB",
        "MRYCYYGG",
        "CoRYoGBo",
        "CRCYCBYM",
        "YOBBoCRR",
        "MGRGYBCC",
        "RRoGBoGY",
    ], "BRG", "RGBYM"))

#: 7. par 3, satchel 8, three stars at 4, ways 101, greedy 3; 3 changed and 2 caught of 21 opening taps, best tap fires 1;
#: the best opening tap runs 5 wave(s), bursts 24 and frees 6.
GROVES.append(Grove(
    "b02_crossvine", "Crossed Runners",
    "What one vine starts, the other carries on.",
    [
        "MoYCMOMM",
        "RGGYYGGB",
        "oCYMoRBo",
        "CGYMGGoG",
        "MOMoBYRG",
        "YRRBYRMY",
        "BBoCGGoR",
    ], "RGB", "RGBYMC",
    [
        ".......b",
        "..a.....",
        "........",
        "........",
        "........",
        ".....a..",
        "b.......",
    ]))

#: 8. par 3, satchel 8, three stars at 4, ways 204, greedy 3; 2 changed and 2 caught of 24 opening taps, best tap fires 1;
#: the best opening tap runs 8 wave(s), bursts 37 and frees 7.
GROVES.append(Grove(
    "b02_thornedvine", "Thorn and Vine",
    "Two rings, two vines, and one basket.",
    [
        "YoRMOBBo",
        "MRMYRRCC",
        "YMRoMBoR",
        "oGRYMBCG",
        "YOGGoCMR",
        "YMYYMMBB",
        "MYoGGoMY",
    ], "BRG", "RGBYM",
    [
        "........",
        ".a......",
        ".......b",
        "........",
        "b.......",
        "......a.",
        "........",
    ]))

#: 9. par 3, satchel 8, three stars at 4, ways 126, greedy 3; 3 changed and 3 caught of 21 opening taps, best tap fires 2;
#: the best opening tap runs 7 wave(s), bursts 35 and frees 10.
GROVES.append(Grove(
    "b02_thetangle", "The Tangle",
    "Three vines. Every one of them is a decision.",
    [
        "GoBCORRo",
        "RBCBCGGR",
        "RCYoMMoY",
        "oBYBYYRo",
        "BOCYoGCC",
        "BMCBMCGB",
        "GoYRoCoB",
    ], "GBR", "RGBYM",
    [
        "......b.",
        ".a......",
        "c.......",
        "........",
        ".......c",
        "......a.",
        "b.......",
    ]))

#: 10. par 3, satchel 8, three stars at 4, ways 54, greedy 3; 3 changed and 3 caught of 24 opening taps, best tap fires 1;
#: the best opening tap runs 7 wave(s), bursts 30 and frees 11.
GROVES.append(Grove(
    "b02_tangleheart", "The Tanglewood's Heart",
    "Everything this grove can do, at once.",
    [
        "GoGoOBBo",
        "BBMYRYCG",
        "RMRoCCoR",
        "oBBCMMCo",
        "YOCBoBMR",
        "BRRBGMBR",
        "CoYoOGoC",
    ], "RBG", "RYGMBC",
    [
        "......b.",
        ".a......",
        "c.......",
        "........",
        ".......c",
        "......a.",
        "b.......",
    ]))


# ---------------------------------------------------------------------------- writing it out
def chapter_json():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        "accent": ACCENT,
        "slate": SLATE,
        "backdrop": SKIES[0],
        "mapStrips": list(STRIPS),
        "teaserX": TEASER,
        "levels": [g.json(i) for i, g in enumerate(GROVES)],
    }


def report():
    print("%-20s %-5s %-4s %-4s %-4s %-4s %-4s %-5s %-6s %-5s %-14s %s"
          % ("id", "size", "flw", "shut", "par", "sat", "gold", "ways", "greedy", "vines",
             "changed/caught", "best tap"))

    for g in GROVES:
        s = g.survey()
        gold = bud.over(s["par"], 120)
        greedy = s["careless"]
        vines = ("%d/%d of %d" % (s["changed"], s["caught"], s["taps"])) if s["runners"] else "-"

        print("%-20s %dx%-3d %-4d %-4d %-4d %-4d %-4d %-5d %-6s %-5d %-14s %dw/%df/%dc  (%d nodes)"
              % (g.id, s["w"], s["h"], s["flowers"], s["cocoons"], s["par"], g.satchel,
                 gold, s["ways"], greedy if greedy >= 0 else "-", s["runners"], vines,
                 s["bestWaves"], s["bestBurst"], s["bestFreed"], s["nodes"]))


def level_strings():
    wanted = {"chapter.%s.name" % CHAPTER: "The Tanglewood"}
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

    print("\nNext: Content > Sync Manifest, then Validate Content.")
    return 0


if __name__ == "__main__":
    sys.setrecursionlimit(10000)
    raise SystemExit(main())
