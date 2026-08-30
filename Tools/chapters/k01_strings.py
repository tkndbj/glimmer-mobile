"""Every string Groovekeeper needs, in one place, inserted into `loc/en.json` beside its
neighbours.

The chapter's own names and taglines live in `k01_grovekeeper.py` with the boards they describe;
this holds the strings that belong to the *mode* rather than to a level — its readouts, its two
defeats, its six lessons — because those outlive any one chapter and would otherwise be rewritten
every time a chapter script ran. `f01_strings.py` is the same file for Lightfall.

    python Tools/chapters/k01_strings.py --check
    python Tools/chapters/k01_strings.py

Rewriting one that has been re-worded by hand is refused rather than done, exactly as the glade
chapters refuse it: a translation is somebody's work and a generator is not entitled to it.

Note the spelling. The player-facing name of this mode is **Groovekeeper** and the place it is
played in is a **groove**, which is what every shipped string in this game already says; the code
says grove throughout. Neither is going to be renamed to match the other — one of them is on every
device in the world and the other is in every file here.
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
    'mode.keeper.name': 'Groovekeeper',
    # Shipped with the prototype and still exactly right, so it is kept rather than
    # re-worded: it is the one sentence that says the inversion.
    'mode.keeper.tagline': 'Unlike edges bloom.',

    # The basket, under the board, where the player is actually looking while they choose.
    'mode.keeper.basket': 'LEFT',
    'mode.keeper.basket_free': 'free',
    'mode.keeper.compost': 'COMPOST',

    # The one refusal the board cannot answer for itself. Stone is drawn as a rock, an occupied
    # cell already holds a tile and a heartbed flares the colour it wants - but bare ground away
    # from the groove looks exactly like bare ground beside it, so a tap there has to be answered
    # in words. See KeeperBoard.Adrift.
    'mode.keeper.adrift': 'Tiles go beside the groove - tap next to something already growing.',

    # The running count, one per flower as the cascade plays out, and the word it earns at the
    # end. A single bloom is not a flourish and says nothing at all - see KeeperFlourish.
    'mode.keeper.multiplier': 'x{0}',
    'mode.keeper.flourish_lovely': 'LOVELY!',
    'mode.keeper.flourish_radiant': 'RADIANT!',
    'mode.keeper.flourish_glorious': 'GLORIOUS!',

    # ---------------------------------------------------------------- the readouts
    'mode.cap.beds': 'beds left',
    'mode.cap.tiles': 'tiles left',
    'mode.cap.flourish': 'best flourish',

    # ---------------------------------------------------------------- the record
    # A Groovekeeper record is a count of tiles spent - see KeeperMode.RecordStem. Two forms,
    # because "1 tiles" is wrong in English and worse in languages with real plural rules.
    'ui.rank.tiles': '{0} tiles',
    'ui.rank.tiles_one': '{0} tile',

    # ---------------------------------------------------------------- the two defeats
    # They want opposite fixes, so they say opposite things. A single sentence covering both
    # would have to be vague enough to help with neither.
    #
    # A title and no body, because `DefeatOverlay.TitleKey` is the only thing that reads either
    # and there is nowhere on that panel a second sentence would go. Lightfall's `f01_strings.py`
    # declares a `_reason` for each of its two and neither was ever written into `en.json` -
    # which `loc.py` would have reported as two more unused keys had they been. A string nothing
    # reads is dead weight the whole translation pipeline carries.
    'ui.defeat.tiles_title': 'THE BASKET IS EMPTY',
    'ui.defeat.overgrown_title': 'NOWHERE LEFT TO GROW',

    # ---------------------------------------------------------------- the continue
    # What the offer over a lost run says, in the unit this mode is measured in.
    'ui.continue.tiles_title': 'Out of tiles',
    'ui.continue.tiles_unit': 'more tiles',

    # ---------------------------------------------------------------- the six lessons
    'ui.tip.keeper_bloom.title': 'UNLIKE, NOT ALIKE',
    'ui.tip.keeper_bloom.body': 'A tile goes beside the groove, never on its own.\n\nOne that '
                                'gathers all three colours - counting its neighbours - bursts '
                                'into bloom. Two of a colour together are worth nothing.',

    'ui.tip.keeper_basket.title': 'THE BASKET IS COUNTED',
    'ui.tip.keeper_basket.body': 'This groove is dealt so many tiles and no more, in the order '
                                 'the basket shows.\n\nThere is no undo here. The ring under '
                                 'your thumb says what a tile would open before you let go.',

    'ui.tip.keeper_stone.title': 'STONE GROWS NOTHING',
    'ui.tip.keeper_stone.body': 'Nothing may be planted on stone, and no light passes through '
                                'it.\n\nA bed beside one has fewer neighbours to gather from, '
                                'so what it is missing has to come from a shorter list of '
                                'cells.',

    'ui.tip.keeper_compost.title': 'COMPOST WHAT YOU CANNOT USE',
    'ui.tip.keeper_compost.body': 'The key beside the basket spends the tile in your hand '
                                  'without planting it, and brings the next one round.\n\nIt '
                                  'costs a tile like any other. Sometimes that is the cheapest '
                                  'move there is.',

    'ui.tip.keeper_heartbed.title': 'A HEARTBED WANTS ITS OWN',
    'ui.tip.keeper_heartbed.body': 'A bed drawn in a colour takes that colour and no other. '
                                   'Anything else is refused outright, so nothing can be '
                                   'spoiled by a stray tap.\n\nCount forward through the '
                                   'basket, and compost what is in the way.',

    'ui.tip.keeper_prism.title': 'ONE PRISM, ONE CHANCE',
    'ui.tip.keeper_prism.body': 'A prism carries all three colours at once. It blooms wherever '
                                'it lands and opens any bed, heartbed included.\n\nThere is '
                                'only one. Spend it where two ordinary tiles could not.',
}

#: Retired with the score attack. It said "Groove finished - {0} points." over a mode that had
#: no goal to reach and no way to be lost at all, and nothing raises it now: a groove that is
#: finished goes through `WinOverlay` like every other clear in the game, and one that is lost
#: goes through `DefeatOverlay`. `mode.cap.score` and `mode.cap.blooms` were its readouts, and
#: both are replaced - a run is graded on the tiles it spent, so what it counts is beds left,
#: tiles left and the best flourish.
RETIRED = ('mode.keeper.over', 'mode.cap.score', 'mode.cap.blooms',
           'ui.defeat.tiles_reason', 'ui.defeat.overgrown_reason')


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
        elif key in ('mode.keeper.name',):
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
