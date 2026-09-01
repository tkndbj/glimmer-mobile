"""Every string Lightfall needs, in one place, inserted into `loc/en.json` beside its neighbours.

The chapter's own names and taglines live in `f01_lightfall.py` with the boards they describe;
this holds the strings that belong to the *mode* rather than to a level — its readouts, its two
defeats, its five lessons — because those outlive any one chapter and would otherwise be
rewritten every time a chapter script ran.

    python Tools/chapters/f01_strings.py --check
    python Tools/chapters/f01_strings.py

Rewriting one that has been re-worded by hand is refused rather than done, exactly as the glade
chapters refuse it: a translation is somebody's work and a generator is not entitled to it.
"""
import argparse
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ROOT = os.path.dirname(ROOT) if os.path.basename(ROOT) == 'Tools' else ROOT
LOC = os.path.join(ROOT, 'Assets', 'StreamingAssets', 'Content', 'loc', 'en.json')

STRINGS = {
    # ---------------------------------------------------------------- the mode
    'mode.fall.name': 'Lightfall',
    'mode.fall.tagline': 'Cook the light. Empty the well.',

    # The tray, under the board, where the player is actually looking while they choose.
    'mode.fall.supply': 'LEFT',
    'mode.fall.supply_free': 'free',

    # The running count, one per wave as the chain plays out, and the word it earns at the end.
    # A single burst is not a chain and says nothing at all - see FallChain.
    'mode.fall.multiplier': 'x{0}',
    'mode.fall.chain_amazing': 'AMAZING!',
    'mode.fall.chain_epic': 'EPIC!',
    'mode.fall.chain_legendary': 'LEGENDARY!',
    'mode.fall.chain_unreal': 'UNREAL!',

    # ---------------------------------------------------------------- the readouts
    'mode.cap.motes': 'motes left',
    'mode.cap.supply': 'supply',
    'mode.cap.chain': 'best chain',

    # ---------------------------------------------------------------- the record
    # A Lightfall record is a count of motes dropped - see FallMode.RecordStem. Two forms,
    # because "1 motes" is wrong in English and worse in languages with real plural rules.
    'ui.rank.motes': '{0} motes',
    'ui.rank.motes_one': '{0} mote',

    # ---------------------------------------------------------------- the two defeats
    # They want opposite fixes, so they say opposite things. A single sentence covering both
    # would have to be vague enough to help with neither.
    'ui.defeat.flood_title': 'THE WELL BRIMMED OVER',
    'ui.defeat.flood_reason': 'A mote came to rest above the line. A colour a stack already '
                              'holds has nowhere to go but upward.',
    'ui.defeat.motes_title': 'THE LIGHT RAN OUT',
    'ui.defeat.motes_reason': 'Nothing left to drop, and the well is not empty. Spend each '
                              'mote where it finishes something.',

    # ---------------------------------------------------------------- the five lessons
    'ui.tip.fall_cook.title': 'COOK, DO NOT MATCH',
    'ui.tip.fall_cook.body': 'A mote adds its colour to the one it lands on. Red on yellow '
                             'makes orange.\n\nA mote holding all three bursts, and washes '
                             'that colour into everything beside it.',

    'ui.tip.fall_supply.title': 'THE LIGHT IS COUNTED',
    'ui.tip.fall_supply.body': 'This well is dealt so many motes and no more.\n\nThere is no '
                               'undo here. Watch the ring under your thumb before you let go.',

    # The fourth arrived with the second chapter. It lives here rather than in that chapter's
    # own script for the reason all of these do: a mode's vocabulary outlives any one chapter,
    # and a lesson id travels in the save file exactly as a level id does.
    'ui.tip.fall_lens.title': 'FILL THE GLASS',
    'ui.tip.fall_lens.body': 'A lens fills one colour at a time - free from any burst beside '
                             'it, or a drop at a time by hand.\n\nFill all three and it '
                             'fires white to left and right, popping whatever each beam '
                             'lands on.\n\nLight one lens with another and it fires '
                             'every way - up and down as well.',

    # The fifth arrived with the third chapter, and lives here for the same reason the fourth
    # does: a mode's vocabulary outlives any one chapter, and a lesson id travels in the save
    # file exactly as a level id does.
    #
    # Three sentences, and the *order* is the lesson. What it does first, because that is what
    # the board cannot say on its own; that any light sets it off, because a player who does not
    # know that will lose the pair they spent four drops arranging to a chain that reached it
    # early; and what it is worth, which is the one thing no board can ever state - every other
    # rule in this mode adds a colour to a mote, so a cyan and a red are two drops apart, and
    # beside a whorl they are none.
    'ui.tip.fall_whorl.title': 'OPEN THE WHORL',
    'ui.tip.fall_whorl.body': 'A whorl holds no light. Any light opens it - a burst beside it, '
                              'a beam, or a drop straight onto it.' + '\n' + '\n' + 'On the next '
                              'wave it draws in whatever stands to its left and its right and '
                              'mixes them into one.' + '\n' + '\n' + 'Two motes a drop apiece '
                              'from white are none at all. Arrange the pair, then open it.',

    'ui.tip.fall_brim.title': 'MIND THE BRIM',
    'ui.tip.fall_brim.body': 'A mote that comes to rest above the line ends the run.\n\nA '
                             'colour the top of a stack already holds is what raises it - the '
                             'ring turns red when it would.',
}

#: Retired with the score attack. It said "The well is full." over a mode that had no goal to
#: reach and no way to be lost except that one, and nothing raises it now: a well that floods
#: goes through `DefeatOverlay` like every other loss in the game.
#: `mode.fall.chain` is retired with the banner it belonged to - one line printed after the
#: cascade, which said the same thing for a two-chain and a six. What replaced it is a count that
#: climbs while the chain runs and a word at the end (`FallChain`).
#:
#: `ui.tip.fall_mirror.*` is the third chapter's *first* mechanic, withdrawn before it shipped.
#: A mirror turned a lens's beam ninety degrees, which meant it had no event of its own: on a
#: board with no glass it did nothing, and on a board with glass it was a narrower spelling of
#: what a struck lens already does in four directions. It was reported as useless inside one
#: session of play, correctly.
#:
#: `ui.tip.fall_wick.*` is its *second*, withdrawn after one session for the same fault seen a
#: step further in. A wick held one authored channel and washed it into the four cells beside it
#: when any light touched it - which is this mode's own burst with the colour swapped, on an
#: object with no decision anywhere in it: its colour was the author's, its trigger was free, and
#: its effect was identical on every board it ever stood on. It was reported as **boring**, which
#: is the more useful verdict of the two. See invariant 26h.
#:
#: **`fall_mirror` and `fall_wick` are spent lesson ids and must never be reused** - a lesson id
#: travels in `tipsSeen` exactly as a level id does, so re-pointing one at a rule it never
#: described would tell a player they have already been shown something they never saw. The same
#: goes for the ten level names and the chapter name the wick's chapter carried: `f03_wickwater`
#: and its levels were never committed, let alone released, so they were re-authored honestly
#: rather than kept, and their strings go with them.
RETIRED = ('mode.fall.over', 'mode.fall.chain',
           'ui.tip.fall_mirror.title', 'ui.tip.fall_mirror.body',
           'ui.tip.fall_wick.title', 'ui.tip.fall_wick.body',
           'chapter.f03_wickwater.name',
           'level.f03_first_wick.name', 'level.f03_first_wick.tagline',
           'level.f03_the_wall.name', 'level.f03_the_wall.tagline',
           'level.f03_touchpaper.name', 'level.f03_touchpaper.tagline',
           'level.f03_lanternlight.name', 'level.f03_lanternlight.tagline',
           'level.f03_up_the_column.name', 'level.f03_up_the_column.tagline',
           'level.f03_the_fuse.name', 'level.f03_the_fuse.tagline',
           'level.f03_slow_burn.name', 'level.f03_slow_burn.tagline',
           'level.f03_deep_wick.name', 'level.f03_deep_wick.tagline',
           'level.f03_wildfire.name', 'level.f03_wildfire.tagline',
           'level.f03_wickwater.name', 'level.f03_wickwater.tagline')


def load():
    with io.open(LOC, encoding='utf-8') as f:
        return json.load(f)


def insert(doc, key, text):
    """Puts a new key beside the ones it is related to, so the file stays readable."""
    entries = doc['entries']

    best, at = -1, len(entries)
    for i, entry in enumerate(entries):
        shared = 0
        for a, b in zip(entry['key'], key):
            if a != b:
                break
            shared += 1
        if shared >= best:
            best, at = shared, i + 1

    entries.insert(at, {'key': key, 'text': text})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--check', action='store_true')
    args = ap.parse_args()

    doc = load()
    have = {e['key']: e['text'] for e in doc['entries']}

    added, rewritten, left, retired = [], [], [], []

    for key, text in STRINGS.items():
        if key not in have:
            added.append(key)
        elif have[key] == text:
            continue
        elif key in ('mode.fall.name',):
            continue                       # shipped and correct; never rewritten
        else:
            rewritten.append((key, have[key], text))

    for key in RETIRED:
        if key in have:
            retired.append(key)

    if args.check:
        problems = added + [k for k, _, _ in rewritten] + retired
        for key in added:
            print('MISSING  %s' % key)
        for key, was, now in rewritten:
            print('DIFFERS  %s\n    file: %s\n    here: %s' % (key, was, now))
        for key in retired:
            print('RETIRED  %s is still in the file and nothing reads it' % key)
        print('%d string(s) in this file, %d to write' % (len(STRINGS), len(problems)))
        return 1 if problems else 0

    for key in added:
        insert(doc, key, STRINGS[key])

    for key, was, now in rewritten:
        # A hand re-wording is somebody's work. Reported and left alone.
        left.append((key, was, now))

    doc['entries'] = [e for e in doc['entries'] if e['key'] not in RETIRED]

    with io.open(LOC, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write('\n')

    for key in added:
        print('  added   %s' % key)
    for key in retired:
        print('  removed %s (retired)' % key)
    for key, was, now in left:
        print('  LEFT ALONE %s - somebody has re-worded it\n    file: %s\n    here: %s'
              % (key, was, now))

    print('wrote %s' % os.path.relpath(LOC, ROOT))
    return 0


if __name__ == '__main__':
    sys.exit(main())
