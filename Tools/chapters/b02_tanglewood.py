"""The Tanglewood - Budburst's second chapter: the bolt, the sun and the graft.

**The shipped chapter is `Content/chapters/b02_tanglewood.json`, not this file.** That is what the
game reads and what the build gate proves; nothing at runtime knows this script exists.

    python Tools/chapters/b02_tanglewood.py --check     # does the shipped JSON still match?
    python Tools/chapters/b02_tanglewood.py             # rewrite it from here
    python Tools/chapters/b02_tanglewood.py --fixture   # the C# rungs for BudLadderTests

**Why specials, and why now.** The chapter shipped first with a runner and then with five objects on
a grove each - a windmill, a firefly, the graft, a puffball, a hive - and play reported both cuts the
same way: *nothing different, all I see is flowers popping* (invariant 20m). Every one of them was
placed by an author and paid out as the same chain. What replaced them is the genre's own loop: a
bunch of **five** leaves a **bolt** where the player tapped, a bunch of **eight** a **sun**; a
special fires when tapped, when a bunch takes it in, or when another special's reach hits it, and
a bolt clears its row and column while a sun clears the five-by-five around it. The graft stayed,
because it is how the genre makes fives.

**Every grove grafts and forges, is 8x7, and carries fifteen or more shut in**, several tough - not
as a ramp but because a graft bursts by construction and anything less collapses to par 2. Every rung
is still par 3 (26d), and what the sweep held out for on each is `fired` - that every shortest play
fires a special - and `forgeable` - that an opening move can forge one on the board as dealt.

**The ramp is three dials, none of them par.** What is dealt: rung one deals a **bolt** already
forged, so the first thing anybody does is fire one and watch the line; rung three deals a **sun**;
the rest deal nothing and have to be made. How many take two hits: three on the first five, six to
eight on the back half. And the **satchel**: `par + 5` on the first five, `par + 4` on six to
eight, `par + 3` on nine and ten - which is the owner's ask for *a little challenge*, and the least
a satchel can be, because `par + 2` would sit on the two-star line and strand a band (invariant 22).
A careless player still finishes every rung inside its satchel, which is the bar (20k).
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

#: A lime running over deep leaf, which is the wood's own colour rather than the Thicket's honey.
ACCENT, SLATE = "#C8E24A", "#1B2A12"

#: Which chapter of its own mode this is. It buys the map and the skies - see `mapart`, which owns
#: that arithmetic for every chapter of every mode (invariant 7c).
ORDINAL = 2
STRIPS = mapart.strips(ORDINAL)
SKIES = mapart.skies(ORDINAL)

#: Where the ten nodes stand - `mapart`'s one layout for every chapter of every mode, with
#: the last glade on the left and the end-of-chapter marker on the right, proved against
#: `ChapterMapValidator` by `Tools/verify/content.py`.
WHERE = mapart.places(ORDINAL)



class Grove(object):
    """One authored level: a permanent id, a name, a line, a board, what it deals and a basket."""

    def __init__(self, lid, name, tagline, rows, colours, regrow=None, specials=None, spare=0):
        self.id = lid
        self.name = name
        self.tagline = tagline
        self.rows = rows
        self.colours = colours
        self.regrow = regrow
        self.specials = specials
        self.spare = spare

    @property
    def satchel(self):
        return self.par + (self.spare or bud.DEFAULT_SPARE)

    def survey(self):
        if not hasattr(self, "_survey"):
            self._survey = bud.survey(self.rows, self.colours, self.regrow, True, self.specials,
                                      True)
        return self._survey

    @property
    def par(self):
        return self.survey()["par"]

    def json(self, at):
        block = {
            "width": len(self.rows[0]),
            "height": len(self.rows),
            "rows": list(self.rows),
            "grafts": True,
            "forges": True,
        }
        if self.specials:
            block["specials"] = list(self.specials)

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

#: Deals a bolt already forged, so the first thing anybody does is fire one and watch the line.
GROVES.append(Grove(
    "b02_firstbolt", "First Bolt",
    "Tap the bolt. Watch the row go.",
    [
        "oOGRMCOo",
        "GYYMGBCC",
        "oMBOBYYo",
        "RMGCRCRR",
        "oYRBGCBo",
        "GYOGYORM",
        "oORCCGOo",
    ], "GRB", "RGBYMC",
    specials=[
        "........",
        "........",
        "........",
        "......|.",
        "........",
        "........",
        "........",
    ]))

#: Deals nothing; the five has to be made.
GROVES.append(Grove(
    "b02_makefive", "Make Five",
    "Five of a colour leave one behind, right where you tapped.",
    [
        "oOCCYCOo",
        "RMYRBGRB",
        "oBMOCYCo",
        "YRCRMYGY",
        "oMYRMCBo",
        "CYOYCOBR",
        "oOMBYCOo",
    ], "GRB", "RGBYMC"))

#: Deals a sun, so one is seen before one is ever made.
GROVES.append(Grove(
    "b02_sunspark", "Sunspark",
    "Eight make a sun. A sun takes everything near it.",
    [
        "oOCGBMOo",
        "CGRRYYRY",
        "oGBOCBRo",
        "MBMRYMBM",
        "oCYCRYCo",
        "GCOYMOYG",
        "oOMGRBOo",
    ], "GBR", "RGBYMCW",
    specials=[
        "........",
        ".*......",
        "........",
        "........",
        "........",
        "........",
        "........",
    ]))

#: Nothing dealt, and the best opening is a graft.
GROVES.append(Grove(
    "b02_crossfire", "Crossfire",
    "A bolt in a sun's blast fires too. Line them up.",
    [
        "oOGGYGOo",
        "CBYRCCGY",
        "oRMOYYMo",
        "MGMBGGMG",
        "oGBYMCRo",
        "BBOYMORB",
        "oOGCGGOo",
    ], "GBR", "RYGMBC"))

#: The finale. Nothing dealt.
GROVES.append(Grove(
    "b02_stormheart", "The Storm's Heart",
    "Fifteen shut in. Make the storm.",
    [
        "oOGMCCOo",
        "BCBMGYCM",
        "oRGOBMGo",
        "MRCMBRCY",
        "oGBYMRYo",
        "GBOGMORY",
        "oOYRRGOo",
    ], "GRB", "RGBYMCW"))

#: six - 16 shut in, dealt par + 4.
GROVES.append(Grove(
    "b02_sixfold", "Sixfold",
    "Six take two hits. Two bolts crossed take them all.",
    [
        "oOBCMMOo",
        "GRYBRCRB",
        "oGMOBCGo",
        "GBCCMMCR",
        "oMBOGYYo",
        "BROGBOGM",
        "oOGYYROo",
    ], "BGR", "RGBYMCW", spare=4))

#: seven - 16 shut in, dealt par + 4.
GROVES.append(Grove(
    "b02_thundering", "Thundering",
    "Trade for the five. Fire it where the tough ones stand.",
    [
        "oOGBYYOo",
        "BYRORMGB",
        "OGYMYBGO",
        "CCRGCBCC",
        "OGGOGGMO",
        "MMOCYOBB",
        "oOMCYMOo",
    ], "RBG", "RGBYMC", spare=4))

#: eight - 16 shut in, dealt par + 4.
GROVES.append(Grove(
    "b02_sunwell", "Sunwell",
    "Eight in a bunch is a sun. Find the eight.",
    [
        "OOGBYBOO",
        "BGYYGCMB",
        "oGCOMGBo",
        "RBBCOMRG",
        "oMGRCBBo",
        "MROMCOMM",
        "OOYBBMOO",
    ], "RGB", "RGBYMC", spare=4))

#: nine - 16 shut in, dealt par + 3.
GROVES.append(Grove(
    "b02_wildstorm", "Wildstorm",
    "Six taps. Every one of them has to count.",
    [
        "oOCBGGOo",
        "BBROBRYB",
        "OYGRYRYO",
        "RRGBYGMB",
        "OYYOBGRO",
        "CCORROMR",
        "oOCYGBOo",
    ], "RBG", "RYGMBC", spare=3))

#: ten - 16 shut in, dealt par + 3.
GROVES.append(Grove(
    "b02_stormcrown", "The Storm's Crown",
    "Everything this wood can do, in six taps.",
    [
        "OORBRBOO",
        "CCBMYGCY",
        "oGYOCMMo",
        "BMGGORGY",
        "oMCRRBGo",
        "YBOMCOYM",
        "OOYRRYOO",
    ], "RBG", "RGBYM", spare=3))



# ---------------------------------------------------------------------------- writing it out
def chapter_json():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        "accent": ACCENT,
        "slate": SLATE,
        "backdrop": SKIES[0],
        "mapStrips": list(STRIPS),
        "levels": [g.json(i) for i, g in enumerate(GROVES)],
    }


def report():
    print("%-16s %-5s %-4s %-4s %-4s %-4s %-4s %-5s %-6s %-5s %-9s %-11s %s"
          % ("id", "size", "flw", "shut", "par", "sat", "gold", "ways", "greedy", "dealt",
             "forgeable", "fired/ways", "best move"))

    for g in GROVES:
        s = g.survey()
        gold = bud.over(s["par"], 120)
        greedy = s["careless"]

        print("%-16s %dx%-3d %-4d %-4d %-4d %-4d %-4d %-5d %-6s %-5d %-9d %-11s %s %dw/%db/%df  (%d nodes)"
              % (g.id, s["w"], s["h"], s["flowers"], s["cocoons"], s["par"], g.satchel,
                 gold, s["ways"], greedy if greedy >= 0 else "-", s["specials"],
                 s["forgeable"], "%d/%d" % (s["fired"], s["ways"]),
                 s["bestMove"], s["bestWaves"], s["bestBurst"], s["bestFreed"], s["nodes"]))


def fixture():
    """The C# rungs, for `BudLadderTests.Tangle`. Printed rather than written, so the fixture
    stays a file somebody reads and edits."""
    for g in GROVES:
        s = g.survey()
        kind, at, other = s["bestMove"]
        print('            new Rung("%s", "%s", "%s",' % (g.id, g.colours, g.regrow or ""))
        print('                     par: %d, ways: %d, careless: %d, nodes: %d,'
              % (s["par"], s["ways"], s["careless"], s["nodes"]))
        print('                     spare: %d,' % (g.spare or bud.DEFAULT_SPARE))
        print('                     flowers: %d, cocoons: %d,' % (s["flowers"], s["cocoons"]))
        print('                     bestAt: %d, bestBurst: %d, bestWaves: %d, bestFreed: %d,'
              % (at, s["bestBurst"], s["bestWaves"], s["bestFreed"]))
        for i, r in enumerate(g.rows):
            print('                     "%s"%s' % (r, "," if i < len(g.rows) - 1 else ")"))
        print('            {')
        print('                Grafts = true, Forges = true, Dealt = %d,' % s["specials"])
        if g.specials:
            print('                Specials = new[]')
            print('                {')
            for r in g.specials:
                print('                    "%s",' % r)
            print('                },')
        print('                Forgeable = %d, Forged = %d, Fired = %d,'
              % (s["forgeable"], s["forged"], s["fired"]))
        print('                BestKind = "%s", BestOther = %d,' % (kind, other))
        print('            },')


def level_strings():
    wanted = {"chapter.%s.name" % CHAPTER: "The Tanglewood"}
    for g in GROVES:
        wanted["level.%s.name" % g.id] = g.name
        wanted["level.%s.tagline" % g.id] = g.tagline
    return wanted


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--fixture", action="store_true")
    args = ap.parse_args()

    if args.fixture:
        fixture()
        return 0

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
