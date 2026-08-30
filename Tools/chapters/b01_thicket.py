"""The Thicket - Budburst's first chapter, and for now its only level.

**The shipped chapter is `Content/chapters/b01_thicket.json`, not this file.** That is what the
game reads and what the build gate proves; nothing at runtime knows this script exists.

    python Tools/chapters/b01_thicket.py --check     # does the shipped JSON still match?
    python Tools/chapters/b01_thicket.py             # rewrite it from here

**One level, on purpose.** Two modes before this one were built out to five and ten levels and
then thrown away, because the thing that decides a mode is whether the *verb* lands and no
number can answer that. One board is enough to answer it and cheap enough to throw away.

**It authors a grid and nothing else.** Par is the fewest taps that free every critter, found by
search (`Tools/verify/bud.py`, mirroring the shipping `BudSolver`), and both star lines and the
tap budget derive from it.

**The strings live here too**, which the other chapters split into a `*_strings.py`. With one
level there is nothing to split: the moment a second chapter exists, the mode's own strings move
out, because those outlive any one chapter and would otherwise be rewritten every time a chapter
script ran.

**Why this board.** Thirty-six flowers in five colours with four cocoons set into it, and it
was composed rather than swept for: the layout was fixed by hand and only the *basket* was hunted,
which is the cheap half of the search and the half that decides how a board plays.

What it is built to do is show the mode in the first three seconds. The best opening tap runs
**three waves, bursts thirteen of the thirty-six flowers and frees three of the four critters at
once**, and two more taps finish it. Par is 3, and a player who just taps whatever looks biggest
finishes in 4 - which is still a three-star run, and which is the bar this mode is held to rather
than a difficulty reading. Everywhere else in this game a careless player finishing is a
complaint; here it is the brief (invariant 20k).

**Why this basket and not a shorter one.** Twenty-four baskets give this layout a par of 3 and
`GBR` was taken for two reasons that are not about difficulty. It deals all three colours, so the
rotation - the thing the queue under the grove is drawing - is visible on the first board rather
than being a rule the player takes on trust. And its careless play lands on 4 against a three-star
line of 4, where the shorter baskets land on 3: a greedy player who plays *optimally* means the
grove decided nothing at all, which is the one way a chill board can still be a bad one.

**Why par 3 and not par 2.** The layout's first basket gave par 2, and at par 2 the two star lines
both round to 3 - `ceil(2 x 1.20)` and `ceil(2 x 1.40)` are the same integer - so the two-star band
is empty and a careless player drops straight to one star. `CheckStarBands` reads the factors
rather than the thresholds and so says nothing about it (deliberately: it would be a complaint
about board size), which is exactly why it is worth stating here.

**The board must be authored settled.** A grove already holding three alike touching goes off in
the first frame - the player is shown a chain they did not cause, and par is measured against a
position they never met. `BudValidator.Settled` and `content.py` both refuse one.
"""
import argparse
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "Tools", "verify"))

import bud                                                       # noqa: E402

CHAPTER = "b01_thicket"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")
LOC = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")

ACCENT, SLATE = "#FFC24A", "#241A0E"
BACKDROP = "play_6"
STRIPS = ["strip0", "strip1", "strip2", "strip3", "strip4", "strip5"]

LEVEL = "b01_firstburst"
NAME, TAGLINE = "First Burst", "Tap a flower. Watch it run."

#: Thirty-six flowers in five colours with four cocoons set into them. `R G B` are the pure
#: colours and `Y M C W` the blends, exactly as everywhere else in this game; `o` is a cocoon,
#: `O` one that takes two cracks, `#` old wood and `.` bare ground.
ROWS = [
    "GYRYBBR",
    "BRoBoYG",
    "RBCRGRY",
    "GRoYoGY",
    "BBCRYRR",
    ".GGRYG.",
]

#: The basket, dealt one per tap and repeating. Pure colour only - a blend is something the
#: player makes, never something they are handed.
COLOURS = "GBR"

#: The strings the mode needs. See the note above about where these go when a second chapter
#: exists.
STRINGS = {
    "mode.bud.name": "Budburst",
    "mode.bud.tagline": "One tap, and it runs.",

    "mode.bud.taps": "TAPS",
    "mode.bud.taps_free": "free",
    "mode.bud.critters": "CRITTERS",

    # The running count, one per wave while the chain is going, and the word it earns at the end.
    # A single wave is not a chain and says nothing at all - see BudChain.
    "mode.bud.multiplier": "x{0}",

    # The four rungs. A ladder everybody can already order without being taught it - which is
    # the whole job of the word, because it *is* the score, said out loud. See BudChain.WordKey
    # for why the grove's own vocabulary (LOVELY / WILD / GLORIOUS) was the wrong instinct.
    "mode.bud.chain_great": "GREAT!",
    "mode.bud.chain_amazing": "AMAZING!",
    "mode.bud.chain_epic": "EPIC!",
    "mode.bud.chain_legendary": "LEGENDARY!",

    "mode.cap.taps": "taps left",
    "mode.cap.critters": "still shut in",

    # A Budburst record is a count of taps - see BudMode.RecordStem. Two forms, because
    # "1 taps" is wrong in English and worse in languages with real plural rules.
    "ui.rank.taps": "{0} taps",
    "ui.rank.taps_one": "{0} tap",

    # Two defeats, and they want opposite fixes, so they say opposite things.
    "ui.defeat.taps_title": "OUT OF TAPS",
    "ui.defeat.barren_title": "NOTHING LEFT TO TAP",

    "ui.continue.taps_title": "Out of taps",
    "ui.continue.taps_unit": "more taps",

    # The three lessons.
    "ui.tip.bud_chain.title": "MIX, THEN THREE",
    "ui.tip.bud_chain.body": "The colour in your hand MIXES into whatever flower you tap - red "
                             "with green in hand turns yellow.\n\nGet three of one colour "
                             "touching and they burst, splashing that colour into every flower "
                             "around them. Which makes more threes. Which makes more.",

    "ui.tip.bud_cocoon.title": "FREE THEM ALL",
    "ui.tip.bud_cocoon.body": "A critter is shut in until a burst goes off right beside it.\n\n"
                              "You cannot tap a cocoon open - you have to set something off next "
                              "to it. The ones drawn with a second ring take two.",

    "ui.tip.bud_satchel.title": "COUNT YOUR TAPS",
    "ui.tip.bud_satchel.body": "This grove is dealt so many taps and no more.\n\nThere is no "
                               "undo, and nothing grows back - so the fewer taps you free "
                               "everyone in, the more stars you keep.",
}

#: Ripplewake's, which never shipped, and Lightweave's, which did. Both modes are gone.
#: Plus Budburst's own first chain ladder, replaced rather than re-pointed - see BudChain.
RETIRED = (
    "mode.bud.chain_lovely", "mode.bud.chain_wild",
    "mode.bud.chain_glorious", "mode.bud.chain_wildfire",
    "mode.ripple.name", "mode.ripple.tagline", "mode.ripple.satchel",
    "mode.ripple.satchel_free", "mode.ripple.release", "mode.ripple.multiplier",
    "mode.ripple.wakes", "mode.ripple.chime_swell", "mode.ripple.chime_surge",
    "mode.ripple.chime_tidal",
    "mode.cap.stones", "mode.cap.splash", "mode.cap.asleep",
    "ui.rank.stones", "ui.rank.stones_one",
    "ui.defeat.stones_title", "ui.continue.stones_title", "ui.continue.stones_unit",
    "ui.tip.ripple_meet.title", "ui.tip.ripple_meet.body",
    "ui.tip.ripple_satchel.title", "ui.tip.ripple_satchel.body",
    "ui.tip.ripple_reed.title", "ui.tip.ripple_reed.body",
    "ui.tip.ripple_deep.title", "ui.tip.ripple_deep.body",
    "ui.tip.ripple_lily.title", "ui.tip.ripple_lily.body",
)


def chapter_json():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        "accent": ACCENT,
        "slate": SLATE,
        "backdrop": BACKDROP,
        "mapStrips": list(STRIPS),
        "levels": [{
            "id": LEVEL,
            "mapX": 0.3,
            "mapY": 0.08,
            "bud": {
                "width": len(ROWS[0]),
                "height": len(ROWS),
                "rows": list(ROWS),
                "colours": COLOURS,
            },
        }],
    }


def report():
    s = bud.survey(ROWS, COLOURS)

    print("%-18s %dx%d  flowers %d  critters %d  deals %s"
          % (LEVEL, s["w"], s["h"], s["flowers"], s["cocoons"], COLOURS))
    print("   par %d   three stars at %d   two at %d   satchel %d   ways %d   careless %s"
          % (s["par"], bud.over(s["par"], 120), bud.over(s["par"], 140),
             s["par"] + bud.DEFAULT_SPARE, s["ways"],
             s["careless"] if s["careless"] >= 0 else "-"))
    print("   best opening tap: %d wave(s), %d flower(s), %d critter(s) freed"
          % (s["bestWaves"], s["bestBurst"], s["bestFreed"]))
    print("   proving it cost %d position(s) against the 20000 a level is expected to cost "
          "and the 60000 it is refused above" % s["nodes"])


def write_strings(check):
    doc = json.load(io.open(LOC, encoding="utf-8"))
    have = {e["key"]: e["text"] for e in doc["entries"]}

    wanted = dict(STRINGS)
    wanted["chapter.%s.name" % CHAPTER] = "The Thicket"
    wanted["level.%s.name" % LEVEL] = NAME
    wanted["level.%s.tagline" % LEVEL] = TAGLINE

    added = [(k, v) for k, v in wanted.items() if k not in have]
    differs = [(k, have[k], v) for k, v in wanted.items() if k in have and have[k] != v]
    stale = sorted(k for k in have
                   if k in RETIRED or k.startswith("level.r01_") or k.startswith("chapter.r01_"))

    if check:
        return added, differs, stale

    for key, text in added:
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

    if stale:
        doc["entries"] = [e for e in doc["entries"] if e["key"] not in set(stale)]

    if added or stale:
        with io.open(LOC, "w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
            f.write("\n")

    return added, differs, stale


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    report()

    doc = chapter_json()
    added, differs, stale = write_strings(args.check)

    if args.check:
        shipped = json.load(io.open(BODY, encoding="utf-8"))
        same = shipped == json.loads(json.dumps(doc))

        for key, text in added:
            print("  MISSING  %s = %s" % (key, text))
        for key, was, now in differs:
            print("  DIFFERS  %s\n    file: %s\n    here: %s" % (key, was, now))
        for key in stale:
            print("  RETIRED  %s belongs to a mode this build no longer has" % key)

        if same and not added and not stale:
            print("\n%s matches this source" % os.path.relpath(BODY, ROOT))
            return 0

        if not same:
            print("\n%s DIFFERS from this source" % os.path.relpath(BODY, ROOT))
        return 1

    with io.open(BODY, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print("\nwrote %s" % os.path.relpath(BODY, ROOT))
    for key, text in added:
        print("  added   %s" % key)
    for key in stale:
        print("  removed %s (retired)" % key)
    for key, was, now in differs:
        print("  LEFT ALONE %s - somebody has re-worded it" % key)

    print("\nNext: Content > Sync Manifest, then Validate Content.")
    return 0


if __name__ == "__main__":
    sys.setrecursionlimit(10000)
    raise SystemExit(main())
