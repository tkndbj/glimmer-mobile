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

    # The fourth, and the only one a chapter rather than the mode brings. Two sentences and the
    # second one is the whole of it: the vine and the travelling light say what a runner does,
    # and no board anywhere can say what it takes to *fire* one. A player who has not been told
    # the threshold reads a vine that stayed dark as a bug rather than as a near miss.
    "ui.tip.bud_runner.title": "MIND THE VINE",
    "ui.tip.bud_runner.body": "A runner joins two squares of the grove, however far apart they "
                              "are.\n\nWhen a bursting bunch takes in one end, its colour runs "
                              "down the vine to whatever is standing on the other. Going off "
                              "next to an end is not enough - the flower on it has to be one of "
                              "the three.",

    "ui.tip.bud_satchel.title": "COUNT YOUR TAPS",
    "ui.tip.bud_satchel.body": "This grove is dealt so many taps and no more.\n\nThere is no "
                               "undo, and nothing grows back - so the fewer taps you free "
                               "everyone in, the more stars you keep.",
}

#: Ripplewake's, which never shipped, and Lightweave's, which did. Both modes are gone.
#: Plus Budburst's own first chain ladder, replaced rather than re-pointed - see BudChain.
RETIRED = (
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
