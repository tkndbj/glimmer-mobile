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

#: The season's id, and whether it comes round again.
#:
#: **A repeating season draws its name from a pool and a fixed one does not, and that is the
#: whole difference these two constants carry.** `content.py` asks for `ui.season.{n}.name` and
#: `ui.season.blurb` when `repeats` is set, and for `ui.event.{id}.name` and `.blurb` when it is
#: not - so getting this wrong is not a cosmetic drift, it is two content errors and a build the
#: gate refuses.
#:
#: **They are here because the season changed under this tool and nobody told it.** It was
#: written for a one-off called `first_watch`; the shipped season became a repeating `watch`, and
#: this file went on writing the old shape. `--check` had been red ever since, and the remedy it
#: prints - "re-run without --check" - *reverted the feature*: a repeating season back to a
#: single one, with its two loc keys now missing. A generator whose own advice undoes a shipped
#: change is worse than no generator, which is why the id is a constant now rather than a literal
#: in two places.
SEASON_ID = "watch"
REPEATS = True

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

    block = {
        "id": SEASON_ID,
        "icon": "watch",
        "passGems": PASS_GEMS,
        "startUnix": START,
        "endUnix": END,
        "disabled": False,
    }

    # Written only when it is true, and *before* the milestones, so the block this writes is
    # byte-for-byte the one on disk rather than merely equal to it.
    if REPEATS:
        block["repeats"] = True

    block["milestones"] = milestones
    return block


def rewrite(path, data):
    """Write `data` to `path`, and **only if the file does not already say it**.

    <b>A generator that reformats a file it had nothing to change is a generator nobody dares
    run.</b> `progression.json` is hand-formatted where it is meant to be read - the notification
    slate is a column-aligned table - and `json.dumps` expands every one of those rows onto six
    lines. Re-running this tool with nothing to do produced a seventy-six line diff in a block it
    does not own, which is noise at best and, in a tree somebody else is reading, a change that
    has to be reviewed and decided about before it can be dismissed.

    <b>The comparison is on the parsed content rather than on the text</b>, which is what makes
    that safe: formatting is ignored, so a file that already says the right thing is left exactly
    as its author left it, and a real change still writes.
    """
    if path.exists():
        try:
            if json.loads(path.read_text(encoding='utf-8')) == data:
                return False
        except ValueError:
            pass                       # unreadable is a reason to write, not a reason to stop

    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding='utf-8')
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                        help="prove the shipped JSON is what this tool writes, and change nothing")
    args = parser.parse_args()

    manifest = json.loads(MANIFEST.read_text(encoding='utf-8'))
    progression = json.loads(PROGRESSION.read_text(encoding='utf-8'))

    if args.check:
        wanted = build()
        shipped = next((e for e in manifest.get('events') or [] if e.get('id') == SEASON_ID), None)
        problems = []

        if shipped != wanted:
            problems.append(f"manifest.json's {SEASON_ID!r} block is not what this tool writes "
                            f"(shipped: {'absent' if shipped is None else 'different'})")

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
    rewrite(MANIFEST, manifest)

    progression = json.loads(PROGRESSION.read_text(encoding='utf-8'))
    for tier in progression['tasks']['tiers']:
        tier['marks'] = MARKS[tier['id']]
    rewrite(PROGRESSION, progression)

    free = {}
    paid = {}
    for rung in range(1, RUNGS + 1):
        free[tier_for(rung, True)] = free.get(tier_for(rung, True), 0) + 1
        paid[tier_for(rung, False)] = paid.get(tier_for(rung, False), 0) + 1

    print(f"season {SEASON_ID}{' (repeating)' if REPEATS else ''}: {RUNGS} rungs, "
          f"{STEP} marks apart, top at {RUNGS * STEP}")
    print(f"  window {DAYS} days: {START} -> {END}")
    print(f"  free column {free}")
    print(f"  pass column {paid}")


# **Guarded, because this module's `main` *writes*.** Without it, `import author_season` - which
# any diagnostic, any sibling tool and any future `--check` harness might reasonably do - runs
# `main()` with whatever `sys.argv` happens to be. With no `--check` in it that is the write
# path, so merely importing this file rewrites `manifest.json` and `progression.json`.
#
# That is not hypothetical: it happened, silently, in the middle of an unrelated drop. The tell
# was this file's own summary printing before the importer's first line, and what it left behind
# was a repeating season reverted to the single-season shape and two content errors in a gate
# that had been green all day. Every other tool here is guarded; this was the one that was not.
if __name__ == "__main__":
    main()
