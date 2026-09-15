"""Re-authors The First Watch and gives every chest tier its mark worth.

The ladder, and why it is shaped this way:

  * 40 rungs, 5 marks apart, so the last asks for 200.
  * A tier is worth wood 1 / silver 2 / gold 3 / royal 5 marks.
  * A fully engaged player is dealt 3 daily tasks a day and 3 weekly a week. The daily
    slate is five wood and five silver, so a day averages 4.5 marks; the weekly slate is
    one silver, six gold and three royal, so a week averages 10.5 — 6.0 marks a day, and
    about 250 over the 42-day window.
  * **The slack is the decision.** 250 against a 200 ladder tops the track around day 33,
    so somebody who misses a day a week still finishes and somebody who plays every day is
    not left with a fortnight of nothing to climb. A ladder set to what a perfect player
    deals exactly (240, which is where this started) has no slack at all: one missed day
    and the last rung is unreachable, which is a countdown with a prize behind it nobody
    can take. `content.py` prints both figures on every run.
  * Free column: wood, with silver on every fifth rung and gold on every tenth.
  * Pass column: silver, with gold on every fifth and royal on every tenth.
"""

import argparse
import json
import pathlib
import sys

MANIFEST = pathlib.Path('Assets/StreamingAssets/Content/manifest.json')
PROGRESSION = pathlib.Path('Assets/StreamingAssets/Content/progression.json')

RUNGS = 40
STEP = 5
DAYS = 42

#: What the pass costs, in gems.
#:
#: Gems rather than money: a pass is an ordinary spend now (invariant 18), so it is priced
#: against what a player already holds rather than against a store listing. 350 is about
#: seven weeks of free play at ~7 gems a day — dear enough that it is a decision and cheap
#: enough that it is not a second economy, and comfortably inside the smallest gem pack.
PASS_GEMS = 350

# 2026-09-15 00:00:00 UTC — the morning after the drop, so the window starts clean.
START = 1789430400
END = START + DAYS * 24 * 60 * 60

MARKS = {"wood": 1, "silver": 2, "gold": 3, "royal": 5}


def tier_for(rung, free):
    if rung % 10 == 0:
        return "gold" if free else "royal"
    if rung % 5 == 0:
        return "silver" if free else "gold"
    return "wood" if free else "silver"


def build():
    """The season block and the tier mark worths, as they should be on disk."""
    milestones = [
        {
            "goal": rung * STEP,
            "tier": tier_for(rung, True),
            "premiumTier": tier_for(rung, False),
        }
        for rung in range(1, RUNGS + 1)
    ]

    return {
        "id": "first_watch",
        "icon": "watch",
        "passGems": PASS_GEMS,
        "startUnix": START,
        "endUnix": END,
        "disabled": False,
        "milestones": milestones,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                        help="prove the shipped JSON is what this tool writes, and change nothing")
    args = parser.parse_args()

    manifest = json.loads(MANIFEST.read_text(encoding='utf-8'))
    progression = json.loads(PROGRESSION.read_text(encoding='utf-8'))

    if args.check:
        wanted = build()
        shipped = next((e for e in manifest.get('events') or [] if e.get('id') == 'first_watch'), None)
        problems = []

        if shipped != wanted:
            problems.append("manifest.json's first_watch block is not what this tool writes")

        for tier in progression['tasks']['tiers']:
            if tier.get('marks') != MARKS.get(tier['id']):
                problems.append(f"tier '{tier['id']}' is worth {tier.get('marks')} marks, "
                                f"not {MARKS.get(tier['id'])}")

        for problem in problems:
            print("  " + problem)

        print("season: " + ("stale - re-run without --check" if problems else "reproducible"))
        sys.exit(1 if problems else 0)

    # One copy of the block, and `--check` reads the same one.
    #
    # There were two for a while — this wrote its own dict and carried the old fields
    # forward with `.get(..., default)`, so raising the price moved `build()` and left the
    # file exactly where it was. `--check` is what said so, which is the whole reason a
    # generator has one.
    manifest['events'] = [build()]

    MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')

    progression = json.loads(PROGRESSION.read_text(encoding='utf-8'))
    for tier in progression['tasks']['tiers']:
        tier['marks'] = MARKS[tier['id']]
    PROGRESSION.write_text(json.dumps(progression, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')

    free = {}
    paid = {}
    for rung in range(1, RUNGS + 1):
        free[tier_for(rung, True)] = free.get(tier_for(rung, True), 0) + 1
        paid[tier_for(rung, False)] = paid.get(tier_for(rung, False), 0) + 1

    print(f"season first_watch: {RUNGS} rungs, {STEP} marks apart, top at {RUNGS * STEP}")
    print(f"  window {DAYS} days: {START} -> {END}")
    print(f"  free column {free}")
    print(f"  pass column {paid}")


main()
