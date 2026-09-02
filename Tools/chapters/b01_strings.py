"""The strings Budburst needs that belong to the *mode* rather than to any one grove.

They lived in `b01_thicket.py` while the chapter was one level, with a note saying they would
move out the moment a second one existed. This is that move: a mode's own vocabulary — the word
a chain earns, what its record is counted in, what its two defeats say, what it teaches —
outlives any chapter, and leaving it inside one means it is rewritten every time that chapter's
board sweep is re-run.

    python Tools/chapters/b01_strings.py --check     # is en.json still what this says?
    python Tools/chapters/b01_strings.py             # write the missing ones

Same bargain as `k01_strings.py` and `f01_strings.py`: it **adds** and it **retires**, and it
never overwrites a line somebody has re-worded — a translator's edit is worth more than this
file's opinion, and a difference is reported rather than silently reverted.
"""
import argparse
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
LOC = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")

STRINGS = {
    "mode.bud.name": "Budburst",
    "mode.bud.tagline": "One tap, and it runs.",

    "mode.bud.taps": "TAPS",
    "mode.bud.taps_free": "free",
    "mode.bud.critters": "CRITTERS",

    # The hint key under the grove, and its two refusals. Its own three keys rather than the
    # glade's, because two of the three say something a grove-shaped thing has to say: what is
    # bought here is a *spot*, and there is no conduit to turn.
    "mode.bud.hint": "hint",
    "mode.bud.hint_used": "here — and watch what it sets off",
    "mode.bud.hint_nothing": "nothing on this grove would take the colour in your hand",

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

    # The four lessons. `bud_wood` was another for one drop and is retired - a
    # barrier can only ever make a chain shorter, which is the opposite of what this
    # mode is for.
    "ui.tip.bud_chain.title": "MIX, THEN THREE",
    "ui.tip.bud_chain.body": "The colour in your hand MIXES into whatever flower you tap - red "
                             "with green in hand turns yellow.\n\nGet three of one colour "
                             "touching and they burst, splashing that colour into every flower "
                             "around them. Which makes more threes. Which makes more.",

    "ui.tip.bud_cocoon.title": "FREE THEM ALL",
    "ui.tip.bud_cocoon.body": "A critter is shut in until a burst goes off right beside it.\n\n"
                              "You cannot tap a cocoon open - you have to set something off next "
                              "to it. The ones drawn with a second ring take two.",

    # The three the second chapter brings. Each says the half no board can show: that a graft
    # has to make a bunch, that FIVE is what forges a bolt, and that a special in a fired
    # special's reach fires too. `bud_runner`, `bud_gust`, `bud_firefly`, `bud_puff` and
    # `bud_hive` are retired - see RETIRED.
    "ui.tip.bud_graft.title": "DRAG TO TRADE",
    "ui.tip.bud_graft.body": "Drag a flower onto its neighbour and the two trade places - if "
                             "that makes three of a colour touching.\n\nA trade that makes "
                             "nothing snaps back and costs nothing. One that works costs a tap, "
                             "and you keep the colour in your hand.",

    "ui.tip.bud_bolt.title": "MAKE FIVE",
    "ui.tip.bud_bolt.body": "Five of a colour touching leave a BOLT behind, right where you "
                            "tapped.\n\nTap the bolt and lightning clears its whole row and "
                            "column - critters and all.",

    "ui.tip.bud_sun.title": "MAKE EIGHT",
    "ui.tip.bud_sun.body": "Eight of a colour touching leave a SUN. Tap it and everything "
                           "within two squares goes up at once.\n\nA bolt or a sun caught in "
                           "another one's blast fires too. Chain them.",

    "ui.tip.bud_satchel.title": "COUNT YOUR TAPS",
    "ui.tip.bud_satchel.body": "This grove is dealt so many taps and no more.\n\nThere is no "
                               "undo, and nothing grows back - so the fewer taps you free "
                               "everyone in, the more stars you keep.",
}

#: Ripplewake's, which never shipped, and Lightweave's, which did. Both modes are gone.
#: Plus Budburst's own first chain ladder, replaced rather than re-pointed - see BudChain.
RETIRED = (
    # The runner - a vine joining two squares of the grove - was the Tanglewood's first
    # object and was withdrawn after one session of play: it fired by itself, drew nothing of
    # its own and crossed the board with a line. Its lesson id and the ten level ids of the
    # chapter it shipped in are retired with it and must never be reused.
    "ui.tip.bud_runner.title", "ui.tip.bud_runner.body",
    # The four that replaced the runner for one build, played on a device and withdrawn -
    # every one paid out as the same chain. Their lesson ids and the five level ids they
    # stood on are spent.
    "ui.tip.bud_gust.title", "ui.tip.bud_gust.body",
    "ui.tip.bud_firefly.title", "ui.tip.bud_firefly.body",
    "ui.tip.bud_puff.title", "ui.tip.bud_puff.body",
    "ui.tip.bud_hive.title", "ui.tip.bud_hive.body",
    "level.b02_windrow.name", "level.b02_windrow.tagline",
    "level.b02_lanternfly.name", "level.b02_lanternfly.tagline",
    "level.b02_graftwood.name", "level.b02_graftwood.tagline",
    "level.b02_puffhollow.name", "level.b02_puffhollow.tagline",
    "level.b02_hivehill.name", "level.b02_hivehill.tagline",
    "level.b02_firstvine.name", "level.b02_firstvine.tagline",
    "level.b02_longreach.name", "level.b02_longreach.tagline",
    "level.b02_deepthicket.name", "level.b02_deepthicket.tagline",
    "level.b02_windingway.name", "level.b02_windingway.tagline",
    "level.b02_twovines.name", "level.b02_twovines.tagline",
    "level.b02_thewilds.name", "level.b02_thewilds.tagline",
    "level.b02_crossvine.name", "level.b02_crossvine.tagline",
    "level.b02_thornedvine.name", "level.b02_thornedvine.tagline",
    "level.b02_thetangle.name", "level.b02_thetangle.tagline",
    "level.b02_tangleheart.name", "level.b02_tangleheart.tagline",

    # Old wood, authored across most of the Thicket for one drop and taken out again. The
    # lesson id `bud_wood` is retired with it and must never be reused, and so are the four
    # level ids that were named after it.
    "ui.tip.bud_wood.title", "ui.tip.bud_wood.body",
    "level.b01_oldwood.name", "level.b01_oldwood.tagline",
    "level.b01_twinhollows.name", "level.b01_twinhollows.tagline",
    "level.b01_bramblebright.name", "level.b01_bramblebright.tagline",
    "level.b01_hollowbanks.name", "level.b01_hollowbanks.tagline",

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


def apply(wanted, retired, check):
    """Adds what is missing and drops what is retired. Never overwrites a re-wording."""
    doc = json.load(io.open(LOC, encoding="utf-8"))
    have = {e["key"]: e["text"] for e in doc["entries"]}

    added = [(k, v) for k, v in wanted.items() if k not in have]
    differs = [(k, have[k], v) for k, v in wanted.items() if k in have and have[k] != v]
    stale = sorted(k for k in have if k in retired)

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

    added, differs, stale = apply(STRINGS, RETIRED, args.check)

    for key, text in added:
        print(("  MISSING  %s = %s" if args.check else "  added   %s") % (
            (key, text) if args.check else key))
    for key, was, now in differs:
        print("  DIFFERS  %s\n    file: %s\n    here: %s" % (key, was, now))
    for key in stale:
        print(("  RETIRED  %s" if args.check else "  removed %s") % key)

    if args.check and (added or stale):
        return 1

    if not args.check:
        print("\nen.json is up to date with %s" % os.path.basename(__file__))
    elif not differs:
        print("en.json matches this source")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
