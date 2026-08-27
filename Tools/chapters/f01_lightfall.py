"""The Deep Well - Lightfall's first chapter, ten wells and the procession that empties each.

**The shipped chapter is `Content/chapters/f01_lightfall.json`, not this file.** That is what
the game reads and what the build gate proves; nothing at runtime knows this script exists.
Same bargain as the glade chapters, and the same two commands:

    python Tools/chapters/f01_lightfall.py --check     # does the shipped JSON still match?
    python Tools/chapters/f01_lightfall.py             # rewrite it from here

**Nothing here authors a difficulty number**, and that is the whole reason a well can be
trusted. Par is the fewest drops that empty it without ever breaching the brim, found by search
(`Tools/verify/fall.py`, mirroring the shipping `FallSolver`), and both star lines and the
supply the run is dealt are multiples of par. A typed par is the failure with no symptom: one
too high hands three stars to a careless run for ever, one too low makes them unreachable, and
neither is visible in the file that caused it.

**The boards were searched for rather than typed**, because a random fill is almost never
solvable - every pure mote needs two more channels and the stragglers pile up faster than any
chain clears them. Each of these is a handful of connected blend blobs, one drop each, with pure
motes on the seams between them: a red between a yellow blob and a magenta blob takes blue from
one burst and green from the other and goes without anybody ever dropping on it. That is what
makes the *order* the thing being decided rather than the aim.

**The ladder is four dials and par is not one of them.** Par is length, so it wanders on purpose
- 2, 2, 3, 5, 4, 5, 6, 6, 6, 6 - exactly as a glade chapter's does. What climbs is:

* **what is standing in it** - three motes to thirty;
* **headroom** - four spare rows on the opening well, two on the finale, so a wasted mote costs
  a row as well as a mote;
* **`greedy`** - whether a player who never looks ahead, always taking the biggest burst going,
  clears it inside its supply. On the first three they do; from the fourth on they do not;
* **`ways`** - how many distinct shortest solutions there are. Invariant 5d, counted.

**The first well cannot be lost.** `budgetFactor` is negative, which is what the DTO documents
one for, and it is the same decision the first glade in the game makes: the heart gate is the
only thing that can stop somebody playing and the worst moment to meet it is while they are
still working out what the verb is. It also keeps the supply lesson off that board, because a
lesson shown over a meter that is not there can never be shown again.

The numbers each well lands on are printed by `report()` when this file is run.
"""
import argparse
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "Tools", "verify"))

import fall                                                      # noqa: E402

CHAPTER = "f01_lightfall"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")
LOC = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")

ACCENT, SLATE = "#FF6B57", "#241414"
BACKDROP = "play_2"
STRIPS = ["strip0", "strip1", "strip2", "strip3", "strip4", "strip5"]

#: Left and right down the map, the spacing Lightweave's six strips already use - proved
#: against `ChapterMapValidator` for ten nodes rather than guessed at.
WHERE = [(0.30, 0.055), (0.72, 0.140), (0.26, 0.225), (0.70, 0.310), (0.28, 0.395),
         (0.74, 0.480), (0.30, 0.560), (0.68, 0.645), (0.26, 0.730), (0.72, 0.815)]

#: A well that cannot be lost. See the note above.
UNLOSABLE = -1.0


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
    # ------------------------------------------------------------------ 1. the verb
    # Three motes, four spare rows and no supply at all - the only well in the game that
    # cannot be lost, for the reason the first glade cannot.
    #
    # It is the one board here that is hand-drawn rather than searched for, because it has to
    # teach in a particular order and a sweep has no opinion about that. Green onto the red
    # makes a yellow and nothing bursts, which is the whole lesson: a mote *adds* its colour
    # rather than matching it. Then one blue finishes the row it just made and the chain takes
    # all three. Two drops, three ideas, and the second drop is the reward for the first.
    Well("f01_first_fall", "First Fall", "Never match. Cook.",
         ["....",
          "....",
          "....",
          "....",
          "....",
          "RYY."],
         "GBR", budget=UNLOSABLE),

    # ------------------------------------------------------------------ 2. the supply
    Well("f01_two_lights", "Two Lights", "Blue is the one thing yellow lacks.",
         ["....",
          "....",
          "....",
          "....",
          "Y.YY",
          "YYYM"],
         "BGRGBR"),

    # ------------------------------------------------------------------ 3. the chain
    Well("f01_the_kindling", "The Kindling", "One drop, and the light runs.",
         [".....",
          ".....",
          ".....",
          ".....",
          "YB...",
          "YMM.M",
          "YYMMM"],
         "GBRBRG"),

    # ------------------------------------------------------------------ 4. looking ahead
    # The first well a player who never does cannot clear.
    Well("f01_narrow_water", "Narrow Water", "A colour it already holds is a wasted mote.",
         [".....",
          ".....",
          ".....",
          ".....",
          "..YY.",
          ".CRYY",
          "CBGYM",
          "CCMRM"],
         "GBRGRBR"),

    # ------------------------------------------------------------------ 5. the brim
    Well("f01_the_stack", "The Stack", "Light spreads sideways as well as down.",
         [".....",
          ".....",
          ".....",
          ".YRB.",
          "YYGC.",
          "YYYCM",
          "YYGCM"],
         "BGRGBRB"),

    # ------------------------------------------------------------------ 6. what is buried
    Well("f01_deep_cistern", "The Cistern", "What is buried is reached from beside it.",
         ["......",
          "......",
          "......",
          "......",
          "..MRB.",
          "M.MRCC",
          "YMRBCC",
          "YYYYCC"],
         "RGBGBRGR"),

    # ------------------------------------------------------------------ 7. pressure
    Well("f01_brimming", "Brimming", "The line at the top is the one that ends you.",
         ["......",
          "......",
          "......",
          ".YYCG.",
          ".YGBB.",
          ".YYMBC",
          "YYGMMM",
          "YYYRMM"],
         "BGRGRBGB"),

    # ------------------------------------------------------------------ 8. thoughtlessness drowns
    # The one well where the greedy reading is not a number but a drowning: a player who always
    # takes the biggest burst going puts a mote above the brim before the supply runs out.
    Well("f01_the_undertow", "The Undertow", "Clear the floor and the rest comes down.",
         ["......",
          "......",
          "......",
          "..B...",
          ".RBMMM",
          "YYGMRM",
          "YYYGBC",
          "YYYGCC",
          "YYYYBC"],
         "BRGBRGRB"),

    # ------------------------------------------------------------------ 9. counting ahead
    Well("f01_last_ember", "Last Ember", "Count what is still to come.",
         ["......",
          "......",
          "......",
          ".Y....",
          "RGYY.Y",
          "RYYY.Y",
          "GYGYYY",
          "CBYYYY",
          "CCBRYY"],
         "GRBRBGRG"),

    # ------------------------------------------------------------------ 10. the finale
    # The most motes in the chapter, two rows of headroom, and two shortest answers out of every
    # sequence of drops there is.
    Well("f01_the_deep_well", "The Deep Well", "Every mote, or none.",
         ["......",
          "......",
          "......",
          ".R.RM.",
          ".YRRM.",
          "YGGMM.",
          "YYRMMM",
          "YYRMMM",
          "YYRMMM"],
         "GBRGRBBG"),
]


# ---------------------------------------------------------------------------- writing it out
def level_json(well, at):
    x, y = WHERE[at]
    block = {"width": len(well.rows[0]), "height": len(well.rows),
             "rows": well.rows, "motes": well.motes}

    out = {"id": well.id, "mapX": x, "mapY": y}
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
        "backdrop": BACKDROP,
        "mapStrips": list(STRIPS),
        "levels": [level_json(w, i) for i, w in enumerate(BOARDS)],
    }


def report():
    """What each well actually asks, counted rather than argued about."""
    print(f"{'level':<24}{'well':<7}{'motes':<7}{'head':<6}{'par':<5}{'3*':<5}{'2*':<5}"
          f"{'supply':<8}{'ways':<6}{'greedy':<8}{'nodes':<8}deal")

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

        print(f"{well.id:<24}{str(s['width']) + 'x' + str(s['height']):<7}{s['motes']:<7}"
              f"{s['headroom']:<6}{par:<5}{fall.over(par, 120):<5}{fall.over(par, 140):<5}"
              f"{(supply or 'free'):<8}{s['ways']:<6}{str(greedy):<8}{s['nodes']:<8}{well.motes}")

    print(f"\ndearest proof: {worst} position(s) against the 40000 a level is expected to "
          f"cost and the 120000 it is refused above - see FallValidator. Cost goes as the "
          f"column count to the power of par.")


# ---------------------------------------------------------------------------- the strings
def write_strings(check):
    """Adds this chapter's names and taglines, and never rewrites one somebody has changed."""
    doc = json.load(io.open(LOC, encoding="utf-8"))
    entries = {e["key"]: e for e in doc["entries"]}

    wanted = [(f"chapter.{CHAPTER}.name", "The Deep Well")]
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
