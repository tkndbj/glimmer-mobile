"""Hold the inline rung tables in `SiegeRuleTests` to the chapter bodies that ship.

    python Tools/verify/rungs.py              # check, and name the lines to change
    python Tools/verify/rungs.py --print      # emit the correct rows, ready to paste

**Named for rungs rather than for chapters**, because `Tools/chapters/` is a package the chapter
tools import from and a module called `chapters` on the path shadows it - which it did, the moment
this file was written.

**Why a hand copy exists at all.** Every `*LadderTests` in this project holds its boards inline,
because a fixture that loads JSON goes through `JsonUtility` - a native call - so the offline runner
reports the whole file as "needs the Editor" and it becomes the one gate nobody runs on the way past
(see `Tools/verify/tests.py`). For a siege that is not a convenience: a siege has nothing to search
(invariant 37a), so `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine` and
`TheSecondChapterAsksForBetterTurrets` and `TheThirdChapterAsksForABoughtLine` are
the *only* instruments this mode has, and they can only
run offline if the boards are inline.

**And why that needs a gate.** A hand copy is a second source of truth. Thirty rungs are now typed
out twice - once in `Tools/chapters/*.py`, which writes the body a player actually plays, and once
in a C# array, which is what every tuning decision in this mode is read off. When they drift, nothing
fails: both files parse, both tables are internally consistent, every gate stays green, and the
simulation happily measures a chapter nobody ships. That is invariant 5b's fault exactly - one rule
written out twice, each copy correct until a case arrives that only one of them was given - and it is
the same shape as `SiegeGroundTests`, which exists because two switches could disagree about which
floor a rung draws.

**It compares the rows, the gems, the wards, the waves, the boss and the cog rate**, which is
everything `SiegeLayout` is built from, and it reports the *body* as the truth: the body is what a
player opens, and the chapter tool re-derives it from a seed, so a disagreement is always the C# copy
being stale.

It runs offline with nothing but the standard library, so it belongs in any gate that runs
`content.py`.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent

TESTS = REPO / "Assets" / "Game" / "Tests"
CHAPTERS = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters"

#: Which inline table mirrors which chapter body, and where it is written down.
#:
#: A table per chapter rather than one big one, because the two are read by different gates and a
#: reader looking for "where is Broodmarch written twice" should find one row saying so.
TABLES = (
    ("Chapter", "s01_thornwatch", "SiegeRuleTests.cs"),
    ("Broodmarch", "s03_broodmarch", "SiegeRuleTests.Chapters.cs"),
    ("Barrowfell", "s04_barrowfell", "SiegeRuleTests.Chapters.cs"),
)

#: One `new Rung(...)` line. Deliberately narrow - it matches the shape this project writes and
#: nothing else, so a row reformatted by hand is reported rather than silently skipped.
ROW = re.compile(
    r'new Rung\("(?P<id>[^"]*)",\s*'
    r'new\[\]\s*\{(?P<rows>[^}]*)\},\s*'
    r'"(?P<gems>[^"]*)",\s*'
    r'"(?P<wards>[^"]*)",\s*'
    r'new\[\]\s*\{(?P<waves>[^}]*)\},\s*'
    r'"(?P<boss>[^"]*)",\s*'
    r'(?P<cogs>\d+)'
    r'(?:,\s*(?P<tough>\d+))?'
    r'(?:,\s*(?P<gold>\d+),\s*(?P<silver>\d+))?'
    r'(?:,\s*"(?P<charms>[^"]*)")?\)'
)

STRINGS = re.compile(r'"([^"]*)"')


def table_of(source, name):
    """The rows of one inline table, in order, as dicts. Empty when the table is not there."""
    at = source.find("Rung[] %s =" % name)
    if at < 0:
        return None

    open_at = source.find("{", at)
    close_at = source.find("};", open_at)
    if open_at < 0 or close_at < 0:
        return None

    made = []
    for m in ROW.finditer(source[open_at:close_at]):
        made.append({
            "id": m.group("id"),
            "rows": STRINGS.findall(m.group("rows")),
            "gems": m.group("gems"),
            "wards": m.group("wards"),
            "waves": STRINGS.findall(m.group("waves")),
            "boss": m.group("boss"),
            "cogs": int(m.group("cogs")),
            "tough": int(m.group("tough") or 0),
            "gold": int(m.group("gold") or 120),
            "silver": int(m.group("silver") or 140),
            "charms": m.group("charms") or "",
        })

    return made


def body_of(chapter):
    """The same rungs, read out of the body a player opens."""
    path = CHAPTERS / (chapter + ".json")
    if not path.exists():
        return None

    made = []
    for level in json.loads(path.read_text(encoding="utf-8"))["levels"]:
        block = level.get("siege")
        if block is None:
            return None

        made.append({
            "id": level["id"],
            "rows": list(block["rows"]),
            "gems": block["gems"],
            "wards": block["wards"],
            "waves": list(block["waves"]),
            "boss": block.get("boss", "") or "",
            "cogs": int(block.get("cogs", 0)),
            "tough": int(block.get("tough", 0)),

            # **The star lines too**, because a siege authors its own (`siege.STAR_FACTORS`) and a
            # hand copy that graded against the shared 1.20 would sweep a ladder nobody ships.
            "gold": int(round(level.get("goldFactor", 1.2) * 100)),
            "silver": int(round(level.get("silverFactor", 1.4) * 100)),

            # **And the charms, for the surge's reason exactly** (invariant 37by): this fixture
            # has no catalog, so a set the C# copy is missing is ninety runs a chapter played on a
            # board nobody ships, with every gate green.
            "charms": block.get("charms", "") or "",
        })

    return made


def row_text(rung):
    """One `new Rung(...)` line, spelled the way this project writes them.

    **Every optional argument is emitted, always.** They are positional in C# and four of them now
    exist - the surge, the two star lines and the charms - so a `--print` that stopped at `cogs`
    would hand back a row that compiles, reads plausibly and quietly plays the wrong chapter. That
    is the exact fault this whole file exists to catch, so it may not be reintroduced by the fixer.
    """
    rows = ", ".join('"%s"' % r for r in rung["rows"])
    waves = ", ".join('"%s"' % w for w in rung["waves"])
    return ('            new Rung("%s", new[] { %s }, "%s", "%s", new[] { %s }, "%s", %d, %d, %d, '
            '%d, "%s"),'
            % (rung["id"], rows, rung["gems"], rung["wards"], waves, rung["boss"], rung["cogs"],
               rung["tough"], rung["gold"], rung["silver"], rung["charms"]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--print", dest="show", action="store_true",
                    help="emit the correct rows rather than checking")
    args = ap.parse_args()

    bad = False

    for name, chapter, filename in TABLES:
        path = TESTS / filename
        if not path.exists():
            print("%s: no such file" % filename)
            bad = True
            continue

        inline = table_of(path.read_text(encoding="utf-8"), name)
        shipped = body_of(chapter)

        if shipped is None:
            print("%s: no siege body at %s.json" % (name, chapter))
            bad = True
            continue

        if inline is None:
            print("%s: no inline table called `%s` in %s" % (name, name, filename))
            bad = True
            continue

        if args.show:
            print("// %s, from %s.json" % (name, chapter))
            for rung in shipped:
                print(row_text(rung))
            print()
            continue

        if len(inline) != len(shipped):
            print("%s: %d row(s) inline against %d in %s.json"
                  % (name, len(inline), len(shipped), chapter))
            bad = True

        for i in range(min(len(inline), len(shipped))):
            if inline[i] == shipped[i]:
                continue

            print("%s row %d disagrees with %s.json:" % (name, i + 1, chapter))
            for key in ("id", "rows", "gems", "wards", "waves", "boss", "cogs", "tough",
                        "gold", "silver", "charms"):
                if inline[i][key] != shipped[i][key]:
                    print("    %-6s inline  %r" % (key, inline[i][key]))
                    print("    %-6s shipped %r" % ("", shipped[i][key]))
            print("  the shipped body is the truth; this line should read")
            print(row_text(shipped[i]))
            bad = True

        if not bad:
            print("%s: %d rung(s) agree with %s.json" % (name, len(inline), chapter))

    if bad and not args.show:
        print()
        print("run `python Tools/verify/rungs.py --print` for the rows to paste")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
