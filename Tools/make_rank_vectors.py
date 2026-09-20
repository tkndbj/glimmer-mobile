#!/usr/bin/env python3
"""Writes the rank ladder's shared vectors into firebase/shared/grove-vectors.json.

    python Tools/make_rank_vectors.py            # rewrite the block
    python Tools/make_rank_vectors.py --check    # prove the committed block is what this draws

**Why a third implementation.** The rule that turns a save into a rank exists twice and must not
drift — `RankLadder.Held` over a `SaveRankSource` in C#, and `rungOf` in `functions/src/ranks.ts`
— because a rank went public (invariant 19a) and the two answers are drawn side by side: a
keeper's own map draws the client's, every board row and public profile draws the server's. A
disagreement is *silent*. Nothing throws, nothing is refused, no gate anywhere goes red; a player
simply wears one badge on their own screen and a different one on everybody else's, and the only
instrument that could ever find it is somebody looking at both.

Typing the expected rungs by hand would make this file a transcription of one of the two copies.
The mirror below is written from the prose rule instead — walk the ladder from the bottom, a rung
is met when every line is, a line is `reading >= target` — so a case that passes has been agreed
by three people who never read each other's arithmetic.

**What the cases are really about.** Four things, each of which has a way of going wrong that
nothing else can see:

  * the *walk*, which is what makes a rank monotone whatever content says — "highest met" hands
    rank six to somebody who never met rank five, and an authoring slip is enough to do it;
  * the *catalog bound*, because a record naming a withdrawn level must count on neither side;
  * the *lifetime floor*, which is why an account older than the feature reads correctly rather
    than starting again from nought — a server reading only the counted half publishes
    Cinderling for somebody the game calls Goldbrand;
  * the *clamps* — three stars a glade, 9,999 a wave, the tally's ceiling — because a forged save
    is precisely where two independently-written readings stop agreeing.

It touches only `rankLadder`, `rankChapters`, `rankCases` and their comment; every other block in
the file is left exactly as it was found, keys and order included.
"""

import argparse
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
VECTORS = ROOT / "firebase" / "shared" / "grove-vectors.json"

# Mirrors of the bounds both sides apply. `LevelId.MaxLength` / `MAX_LEVEL_ID_LENGTH`,
# `EndlessLedger.MaxWave` / `MAX_WAVE`, `EndlessLedger.MaxRows`, `LifetimeTally.Ceiling`, and
# the per-glade star clamp `SaveRankSource.MaxStars` / `MAX_STARS`.
MAX_LEVEL_ID = 48
MAX_WAVE = 9999
MAX_ENDLESS_ROWS = 64
HARD_MAX_LIFETIME_WAVES = 1_000_000
TALLY_CEILING = 999_999_999
MAX_STARS = 3

# The catalog the cases are measured against. Deliberately small and deliberately *not* the
# shipped one: a vector pinned to live content fails the day somebody authors a chapter, which
# teaches everyone to edit vectors rather than to read them.
CHAPTERS = [
    {"id": "ch_one", "levels": ["one_a", "one_b", "one_c"]},
    {"id": "ch_two", "levels": ["two_a", "two_b"]},
]

#: Every level the catalog ships, which is what bounds both walks.
CATALOG = {level: c["id"] for c in CHAPTERS for level in c["levels"]}

# The ladder the cases climb. Three rungs, each reaching for a different kind of measure, so a
# reading that breaks takes exactly one rung with it and the case names which.
LADDER = [
    {"id": "first", "requires": [
        {"measure": "levels_cleared", "scope": "ch_one", "target": 2},
    ]},
    {"id": "second", "requires": [
        {"measure": "stars", "target": 8},
        {"measure": "keeper_level", "target": 5},
    ]},
    {"id": "third", "requires": [
        {"measure": "three_stars", "scope": "ch_two", "target": 2},
        {"measure": "best_wave", "scope": "two_a", "target": 30},
        {"measure": "raiders", "target": 100},
        {"measure": "runs", "target": 5},
    ]},
]


# --------------------------------------------------------------------------- the rule
def levels_of(case):
    """The save's level rows, as a list of (level, stars) the way both wire shapes carry them."""
    return case.get("levels") or []


def walk_levels(case, scope, worth):
    """One walk over the level rows, catalog-bounded and scoped, with the per-row worth handed in."""
    total = 0

    for row in levels_of(case):
        level = row.get("level") or ""

        # The catalog bound. An id no shipped chapter names counts for nothing — `derivedXp`'s
        # rule, and the reason a withdrawn glade cannot hold a badge up.
        chapter = CATALOG.get(level)
        if chapter is None:
            continue
        if scope and chapter != scope:
            continue

        stars = int(row.get("stars") or 0)
        if stars <= 0:                       # `IsCleared` is `Stars > 0` on both sides
            continue

        total += worth(min(stars, MAX_STARS))

    return total


def lifetime_waves(case):
    """Waves seen off across the whole lane: the rule `endlessCases` already pins, reused.

    **Handed to both sides rather than derived by either of them here.** The server takes it
    from `endlessWaves` and the client from `EndlessLedger.LifetimeWavesIn`, which are the two
    copies `endlessCases` holds together — so a rank case must not be free to *state* a figure
    its own rows do not support. It once was, and the very first run of these vectors caught it:
    a case saying "fifty lifetime waves" with no endless rows at all is a save that cannot
    exist, and the two sides answered it differently for a perfectly good reason.
    """
    total = 0

    for row in (case.get("endless") or [])[:MAX_ENDLESS_ROWS]:
        level = row.get("level") or ""
        if not level or len(level) > MAX_LEVEL_ID:
            continue

        # The two fields carry different ceilings and the best floors the tally — see
        # make_endless_vectors.py, which owns this rule.
        best = min(max(int(row.get("wave") or 0), 0), MAX_WAVE)
        tally = min(max(int(row.get("waves") or 0), 0), HARD_MAX_LIFETIME_WAVES)
        total += max(best, tally)

    return total


def best_wave(case, scope):
    """The furthest wave one endless level reached, or the best of all of them."""
    best = 0

    # The cap bounds the *walk* rather than the count of rows accepted, which is the reading
    # every copy of this in the project takes — see make_endless_vectors.py for what that
    # distinction once cost.
    for row in (case.get("endless") or [])[:MAX_ENDLESS_ROWS]:
        level = row.get("level") or ""
        if not level or len(level) > MAX_LEVEL_ID:
            continue
        if scope and level != scope:
            continue

        wave = int(row.get("wave") or 0)
        if wave > best:
            best = wave

    return 0 if best <= 0 else min(best, MAX_WAVE)


def lifetime(case, goal):
    """A counted verb: the larger of what was counted and what the rest of the save proves."""
    counted = 0
    for row in (case.get("lifetime") or []):
        if (row.get("goal") or "") != goal:
            continue
        count = int(row.get("count") or 0)
        if count <= 0:
            continue
        counted = max(counted, min(count, TALLY_CEILING))

    # The floor, mirroring `LifetimeTally.FloorFor`. A cleared glade is a run that happened and
    # a run that was won; the waves in the endless rows were seen off. Everything else counts
    # from the day the feature shipped, which is honest and is why the shipped ladder leans on
    # the readings that are provable.
    if goal in ("runs", "wins"):
        proved = walk_levels(case, "", lambda stars: 1)
    elif goal == "waves":
        proved = int(case.get("lifetimeWaves") or 0)
    else:
        proved = 0

    return max(counted, proved)


def read(case, line):
    """What the save holds against one line."""
    measure = line["measure"]
    scope = line.get("scope", "")

    if measure == "levels_cleared":
        return walk_levels(case, scope, lambda stars: 1)
    if measure == "stars":
        return walk_levels(case, scope, lambda stars: stars)
    if measure == "three_stars":
        return walk_levels(case, scope, lambda stars: 1 if stars >= 3 else 0)
    if measure == "keeper_level":
        return int(case.get("keeperLevel") or 1)
    if measure == "best_wave":
        return best_wave(case, scope)

    # Everything else is a counted verb read by its own id, which is what makes a future mode's
    # verb a rank requirement with no code on either side.
    return lifetime(case, measure)


def held(case, ladder):
    """The rung this save holds: the top of an unbroken run from the bottom."""
    standing = ""

    for rung in ladder:
        if not all(read(case, line) >= line["target"] for line in rung["requires"]):
            break
        standing = rung["id"]

    return standing


# --------------------------------------------------------------------------- the cases
def cases():
    """Every case, named for the thing it would catch if it broke."""
    out = []

    def case(name, levels=None, endless=None, lifetime_rows=None, keeper=1, ladder=None):
        entry = {
            "name": name,
            "levels": levels or [],
            "endless": endless or [],
            "lifetime": lifetime_rows or [],
            "keeperLevel": keeper,
        }

        # Derived from the case's own rows rather than stated. See `lifetime_waves`.
        entry["lifetimeWaves"] = lifetime_waves(entry)
        if ladder is not None:
            entry["ladder"] = ladder

        entry["held"] = held(entry, LADDER if ladder is None else ladder)
        out.append(entry)

    def lvl(level, stars):
        return {"level": level, "stars": stars}

    def wave(level, w):
        return {"level": level, "wave": w}

    # --------------------------------------------------------------- nothing played
    case("an empty save holds no rung")
    case("a save with rows worth nothing holds no rung",
         levels=[lvl("one_a", 0), lvl("one_b", 0)])

    # ------------------------------------------------------------------ the first rung
    case("one glade is not two", levels=[lvl("one_a", 1)])
    case("two glades of the named chapter take the first rung",
         levels=[lvl("one_a", 1), lvl("one_b", 1)])
    case("two glades of the wrong chapter do not",
         levels=[lvl("two_a", 3), lvl("two_b", 3)])

    # ---------------------------------------------------------------- the catalog bound
    # The one place this side may legitimately answer less than a device's live ledger, and the
    # reason it must: a record naming a withdrawn glade would otherwise hold a rung up for ever.
    case("a record naming a level no chapter ships counts for nothing",
         levels=[lvl("one_a", 3), lvl("ghost_level", 3)])

    # --------------------------------------------------------------- the star clamp
    case("a forged star count is worth three",
         levels=[lvl("one_a", 99), lvl("one_b", 99)], keeper=9)
    case("and a third glade takes the line honestly, at three stars each",
         levels=[lvl("one_a", 99), lvl("one_b", 99), lvl("one_c", 99)], keeper=9)

    # ------------------------------------------------------------------ the second rung
    case("eight stars and the keeper level take the second",
         levels=[lvl("one_a", 3), lvl("one_b", 3), lvl("one_c", 2)], keeper=5)
    case("eight stars without the keeper level do not",
         levels=[lvl("one_a", 3), lvl("one_b", 3), lvl("one_c", 2)], keeper=4)
    case("nor the keeper level without the stars",
         levels=[lvl("one_a", 3), lvl("one_b", 3)], keeper=40)

    # ------------------------------------------------------------------- the walk itself
    # **The rule that makes a rank monotone whatever content says.** Every line of the third
    # rung is met and the second's are not, so the answer is *nothing above the first* — not
    # the third. An authoring slip is all it takes to reach this, which is why it is pinned
    # rather than argued.
    case("a rung whose own lines are met is not held while the one below it is unmet",
         levels=[lvl("one_a", 1), lvl("one_b", 1), lvl("two_a", 3), lvl("two_b", 3)],
         endless=[wave("two_a", 60)],
         lifetime_rows=[{"goal": "raiders", "count": 900}, {"goal": "runs", "count": 90}],
         keeper=1)

    # --------------------------------------------------------------------- the third rung
    full = dict(
        levels=[lvl("one_a", 3), lvl("one_b", 3), lvl("one_c", 3),
                lvl("two_a", 3), lvl("two_b", 3)],
        endless=[wave("two_a", 30)],
        lifetime_rows=[{"goal": "raiders", "count": 100}],
        keeper=12,
    )
    case("every line of every rung met takes the top", **full)
    case("one wave short of the line holds at the second",
         levels=full["levels"], endless=[wave("two_a", 29)],
         lifetime_rows=full["lifetime_rows"], keeper=12)
    case("a wave on the wrong level does not count for a scoped line",
         levels=full["levels"], endless=[wave("one_a", 900)],
         lifetime_rows=full["lifetime_rows"], keeper=12)
    case("one raider short holds at the second",
         levels=full["levels"], endless=full["endless"],
         lifetime_rows=[{"goal": "raiders", "count": 99}], keeper=12)

    # ------------------------------------------------------------------ the lifetime floor
    # **Why an account older than the feature reads correctly.** Five cleared glades are five
    # runs whether or not a counter ever saw them, so the `runs` line is met with no tally at
    # all. A copy that read only the counted half would publish the second rung here and the
    # player's own map would draw the third.
    case("five cleared glades are five runs with no tally at all", **full)
    case("a tally under the floor does not lower it",
         levels=full["levels"], endless=full["endless"],
         lifetime_rows=[{"goal": "raiders", "count": 100}, {"goal": "runs", "count": 1}],
         keeper=12)

    # And the floor that is not provable from a record: a verb with nothing behind it counts
    # from the day it shipped, which is the honest answer and the reason the ladder leans
    # elsewhere.
    case("a verb the save cannot prove counts only what was counted",
         levels=full["levels"], endless=full["endless"],
         lifetime_rows=[{"goal": "raiders", "count": 99}], keeper=12)

    # ------------------------------------------------------------------- the wave clamps
    case("a forged wave is capped at the published ceiling",
         levels=full["levels"], endless=[wave("two_a", 10 ** 9)],
         lifetime_rows=full["lifetime_rows"], keeper=12)
    case("a negative wave is nought and does not subtract",
         levels=full["levels"],
         endless=[wave("two_a", -9), wave("two_a", 30)],
         lifetime_rows=full["lifetime_rows"], keeper=12)
    case("a level id longer than a catalog could ship is refused",
         levels=full["levels"], endless=[{"level": "x" * 49, "wave": 900}],
         lifetime_rows=full["lifetime_rows"], keeper=12)

    # A refused row still spends one of the sixty-four, because the cap bounds the walk. The
    # less obvious of the two readings, and the one both copies take.
    case("a refused row still spends one of the sixty-four",
         levels=full["levels"],
         endless=[{"level": "", "wave": 1}]
                 + [wave("lane%02d" % i, 1) for i in range(63)]
                 + [wave("two_a", 30)],
         lifetime_rows=full["lifetime_rows"], keeper=12)

    # ------------------------------------------------------------- the waves floor
    # `lifetimeWaves` is handed to both sides rather than derived here, because the rule that
    # produces it already exists twice and is pinned by `endlessCases`. What this pins is that
    # a rank *uses* it as the floor under the `waves` verb.
    waves_ladder = [{"id": "only", "requires": [{"measure": "waves", "target": 50}]}]
    case("the waves verb is floored by what the lane proves",
         endless=[wave("two_a", 50)], ladder=waves_ladder)
    case("and a tally over it still wins",
         endless=[wave("two_a", 10)], lifetime_rows=[{"goal": "waves", "count": 50}],
         ladder=waves_ladder)
    case("neither reaching the line holds no rung",
         endless=[wave("two_a", 10)], lifetime_rows=[{"goal": "waves", "count": 20}],
         ladder=waves_ladder)

    # ---------------------------------------------------------------- the tally ceiling
    ceiling_ladder = [{"id": "only",
                       "requires": [{"measure": "raiders", "target": TALLY_CEILING}]}]
    case("a tally at the ceiling meets a line asking for the ceiling",
         lifetime_rows=[{"goal": "raiders", "count": TALLY_CEILING}], ladder=ceiling_ladder)
    # **Inside a signed 32-bit int, deliberately.** The tally travels as an `int` on the C#
    # wire (`TaskCountDto.count`), so a case carrying a larger number could never reach that
    # side at all and would be pinning the server against nothing. The ceiling is `int.MaxValue`
    # over twice, so there is plenty of room above it to forge into.
    case("and one above it is clamped down to exactly the ceiling",
         lifetime_rows=[{"goal": "raiders", "count": 2_000_000_000}],
         ladder=ceiling_ladder)

    # --------------------------------------------------------------------- an empty ladder
    case("an empty ladder holds nothing, which is a build with no ranks in its content",
         levels=full["levels"], endless=full["endless"],
         lifetime_rows=full["lifetime_rows"], keeper=99, ladder=[])

    return out


COMMENT = [
    "What rank a save holds, derived twice and pinned here.",
    "",
    "A rank used to be a private badge derived on the device and stored nowhere (invariant",
    "52). It is on a public board now, which is what invariant 19a is about: a number that",
    "goes public stops being derived-and-trusted and becomes adjudicated. So the ladder is",
    "climbed in two places — RankLadder.Held over a SaveRankSource in C#, and rungOf in",
    "functions/src/ranks.ts — and the two are drawn side by side: the player's own map shows",
    "the client's answer, every board row and public profile shows the server's.",
    "",
    "**A disagreement between them is silent.** Nothing throws, nothing is refused and no gate",
    "goes red; a keeper simply wears one badge on their own screen and another on everybody",
    "else's. These cases are the only instrument that can see it.",
    "",
    "`rankChapters` is the catalog a case is measured against and `rankLadder` the ladder it",
    "climbs; a case may carry its own `ladder` instead. `levels`, `endless` and `lifetime` are",
    "the save's rows in whichever shape each side stores them, `keeperLevel` and",
    "`lifetimeWaves` are the two figures both sides are handed rather than derive, and `held`",
    "is the rung id both must answer — empty for an account below the first rung.",
    "",
    "Written by Tools/make_rank_vectors.py — a third implementation, so a passing case has",
    "been agreed by three copies that never read each other. Do not hand-edit.",
]


def build(existing):
    out = dict(existing)
    out["_rankComment"] = COMMENT
    out["rankChapters"] = CHAPTERS
    out["rankLadder"] = LADDER
    out["rankCases"] = cases()
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--check", action="store_true",
                    help="prove the committed block is what this draws, and change nothing")
    args = ap.parse_args()

    existing = json.loads(VECTORS.read_text(encoding="utf-8"))
    built = build(existing)
    text = json.dumps(built, indent=2, ensure_ascii=False) + "\n"

    if args.check:
        if VECTORS.read_text(encoding="utf-8") != text:
            print("grove-vectors.json is not what make_rank_vectors.py draws; re-run without "
                  "--check", file=sys.stderr)
            return 1
        print(f"rank vectors: {len(built['rankCases'])} case(s), reproducible")
        return 0

    VECTORS.write_text(text, encoding="utf-8")
    print(f"rank vectors: {len(built['rankCases'])} case(s) written to {VECTORS}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
