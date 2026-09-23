#!/usr/bin/env python3
"""Writes the daily challenges' shared vectors into firebase/shared/grove-vectors.json.

    python Tools/make_challenge_vectors.py            # rewrite the block
    python Tools/make_challenge_vectors.py --check    # prove the committed block is what this draws

**Why a third implementation.** Two rules about the daily challenges exist twice and must not
drift. The XP rule turns a save's `challenges.clears` rows into XP — `ChallengeLedger.LifetimeClearsIn`
+ `ChallengeRewardRule.XpFor` in C#, `challengeClears` + `challengeXp` in `functions/src/challenges.ts` —
and a keeper level the two halves disagree about is a published card that silently *drops*
whatever that level gated (invariant 19a). The allowance rule turns the deals an account holds
into the plays a day allows — `ChallengeAllowance.On` in C#, `allowanceOn` on the server — and a
disagreement there is a coin claim refused for a play the page offered, which is money shown and
taken back (45d). Typing the expected numbers by hand would make this file a transcription of one
of them; the mirrors below are written from the prose rules instead.

It touches only the `challenge*` keys and their comment; every other block in the file is left
exactly as it was found, keys and order included.
"""

import argparse
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
VECTORS = ROOT / "firebase" / "shared" / "grove-vectors.json"

# Mirrors of the compile-time bounds. `ChallengeLimits.HardMaxClears` / `HARD_MAX_CLEARS`,
# `ChallengeLimits.MaxClearRows` / `MAX_CLEAR_ROWS`, and the genre spelling shape.
HARD_MAX_CLEARS = 1_000_000
MAX_CLEAR_ROWS = 64
MAX_GENRE = 32

# What ships in the build when no `challenges` block has been published. `ChallengeLimits` and
# `DEFAULT_CHALLENGES`. Both sides assert these, so the three copies cannot drift apart.
DEFAULTS = {"freePlays": 2, "coins": 40, "xp": 20, "maxClears": 25000}

# Deliberately not the shipped figures: a vector pinned to live tuning fails the day somebody
# retunes, which teaches everyone to edit vectors rather than to read them.
CONFIG = {"xp": 7, "maxClears": 100}

# A deal ladder for the allowance cases, likewise unlike the shipped one.
TIERS = [
    {"id": "small", "gems": 10, "plays": 4, "days": 3},
    {"id": "large", "gems": 30, "plays": 9, "days": 5},
]
FREE = 2


def genre_ok(name):
    return bool(name) and len(name) <= MAX_GENRE and all(c.islower() or c.isdigit() or c == "_" for c in name)


def clears_of(rows):
    """Lifetime clears a save's rows are worth, written from the rule rather than from either copy."""
    total = 0
    # The cap bounds the *walk*, not the count of rows accepted (the endless rule's reading).
    for row in (rows or [])[:MAX_CLEAR_ROWS]:
        if row is None:
            continue
        if not genre_ok(row.get("genre") or ""):
            continue
        count = int(row.get("count") or 0)
        if count <= 0:
            continue
        total += min(count, HARD_MAX_CLEARS)
    return total


def xp_of(rows, config):
    rate = int(config["xp"])
    ceiling = int(config["maxClears"])
    if rate <= 0 or ceiling <= 0:
        return 0
    return min(clears_of(rows), ceiling) * rate


def allowance_on(tiers, held, day, free):
    """Plays a day on `day`: the covering deal with the most plays, else the free figure."""
    best = None
    for tier in tiers:
        start = held.get(tier["id"])
        if start is None or start <= 0:
            continue
        if not (start <= day < start + tier["days"]):
            continue
        if best is None or tier["plays"] > best["plays"]:
            best = tier
    return best["plays"] if best else free


def row(genre, count):
    return {"genre": genre, "count": count}


def xp_cases():
    out = []

    def case(name, rows, config=None):
        out.append({
            "name": name,
            "rows": rows,
            "config": config or CONFIG,
            "clears": clears_of(rows),
            "xp": xp_of(rows, config or CONFIG),
        })

    case("a save with no challenge rows at all", [])
    case("a row naming no genre is not a clear", [row("", 9)])
    case("a genre spelling no build could have written is refused", [row("Pairs!", 9)])
    case("a row at nought is not a clear", [row("pairs", 0)])
    case("one genre's tally is what is paid", [row("pairs", 12)])
    case("every genre is summed, a withdrawn one included",
         [row("pairs", 12), row("merge", 3), row("sudoku", 5)])
    case("the ceiling is on the count, not the product",
         [row("pairs", 70), row("merge", 70)])
    case("a rate of nought withdraws the payment", [row("pairs", 50)], {"xp": 0, "maxClears": 100})
    case("a ceiling of nought pays nothing", [row("pairs", 50)], {"xp": 7, "maxClears": 0})
    case("no more rows are read than the rules allow",
         [row("g%02d" % i, 1) for i in range(200)], {"xp": 1, "maxClears": HARD_MAX_CLEARS})
    case("a refused row still spends one of the sixty-four, because the cap bounds the walk",
         [row("", 1)] + [row("g%02d" % i, 1) for i in range(64)], {"xp": 1, "maxClears": HARD_MAX_CLEARS})
    case("a row above the structural ceiling is clamped to it",
         [row("pairs", 5_000_000)], {"xp": 1, "maxClears": HARD_MAX_CLEARS * 4})
    case("the largest total the built-in figures allow does not overflow",
         [row("pairs", HARD_MAX_CLEARS)], DEFAULTS_XP)

    return out


DEFAULTS_XP = {"xp": DEFAULTS["xp"], "maxClears": DEFAULTS["maxClears"]}


def allowance_cases():
    out = []

    def case(name, held, day):
        out.append({
            "name": name,
            "held": held,
            "day": day,
            "allowance": allowance_on(TIERS, held, day, FREE),
        })

    case("nothing held is the free figure", {}, 100)
    case("a deal bought today covers today", {"small": 100}, 100)
    case("the last day of a three-day deal is the second day after purchase", {"small": 100}, 102)
    case("the day after that is free again", {"small": 100}, 103)
    case("a deal bought tomorrow does not cover today", {"small": 101}, 100)
    case("the larger of two overlapping deals governs", {"small": 100, "large": 101}, 102)
    case("the smaller resumes after the larger ends", {"small": 104, "large": 100}, 105)
    case("a deal this table does not sell is ignored", {"huge": 100}, 100)
    case("a nought date is not a purchase", {"small": 0}, 100)

    return out


COMMENT = [
    "What the daily challenges pay and allow, derived twice and pinned here.",
    "",
    "`challengeCases` is the third rule in this game that turns a save into XP without a star",
    "behind it (invariant 9d's shape, see ChallengeRewardRule). It runs in C# so the game can",
    "draw a keeper level offline, and in functions/src/challenges.ts so a published card",
    "carries the same one — and when those two disagree nothing crashes and nothing is",
    "refused: `buildCard` simply *drops* whatever the lower level gated (invariant 19a).",
    "",
    "`challengeAllowanceCases` is the deal rule: which held deal governs a day and how many",
    "plays it allows. The client draws plays off it and the server bounds coin claims by it;",
    "a disagreement is a claim refused for a play the page offered (45d).",
    "",
    "`challengeDefaults` is what both sides use when no `challenges` block has been published.",
    "They agree on purpose rather than failing closed, for the endless block's reason.",
    "",
    "Written by Tools/make_challenge_vectors.py — a third implementation, so a passing case",
    "has been agreed by three copies that never read each other. Do not hand-edit.",
]


def build(existing):
    out = dict(existing)
    out["_challengeComment"] = COMMENT
    out["challengeDefaults"] = DEFAULTS
    out["challengeConfig"] = CONFIG
    out["challengeTiers"] = TIERS
    out["challengeFreePlays"] = FREE
    out["challengeCases"] = xp_cases()
    out["challengeAllowanceCases"] = allowance_cases()
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
        current = VECTORS.read_text(encoding="utf-8")
        if current != text:
            print("grove-vectors.json is not what make_challenge_vectors.py draws; re-run without "
                  "--check", file=sys.stderr)
            return 1
        print(f"challenge vectors: {len(built['challengeCases'])} xp case(s), "
              f"{len(built['challengeAllowanceCases'])} allowance case(s), reproducible")
        return 0

    VECTORS.write_text(text, encoding="utf-8")
    print(f"challenge vectors: {len(built['challengeCases'])} xp case(s), "
          f"{len(built['challengeAllowanceCases'])} allowance case(s) written to {VECTORS}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
