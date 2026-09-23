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
taken back (45d). The upgrade rule prices a bigger deal under a running one as the difference —
`ChallengeAllowance.Price` and `dealPrice` — and a disagreement there is gems taken and given back.
Typing the expected numbers by hand would make this file a transcription of one of them; the
mirrors below are written from the prose rules instead.

**A window is an instant on the client and a day on the server, on purpose.** The save holds the
instant a window began and the client covers exactly `days` of the clock from it (56h); the
server records the day off the spend id and covers day keys `fromDay .. fromDay + days`
*inclusive*, one key wider than the instant window could ever reach, so a claim on the window's
last partial day is never refused. Every allowance case here carries both readings.

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


DAY = 86400


def day_of(unix):
    return unix // DAY if unix > 0 else 0


def governing(tiers, held, now):
    """The running deal with the most plays at instant `now`, or None. Client reading."""
    best = None
    for tier in tiers:
        start = held.get(tier["id"])
        if start is None or start <= 0:
            continue
        if not (start <= now < start + tier["days"] * DAY):
            continue
        if best is None or tier["plays"] > best["plays"]:
            best = tier
    return best


def allowance_on(tiers, held, now, free):
    """Plays a day at instant `now`: the governing deal's figure, else the free figure."""
    best = governing(tiers, held, now)
    return best["plays"] if best else free


def server_allowance_on(tiers, held_days, day, free):
    """The server's reading: day keys `from .. from + days` inclusive, off the wallet's days."""
    best = None
    for tier in tiers:
        start = held_days.get(tier["id"])
        if start is None or start <= 0:
            continue
        if not (start <= day <= start + tier["days"]):
            continue
        if best is None or tier["plays"] > best["plays"]:
            best = tier
    return best["plays"] if best else free


def price(tiers, held, now, target):
    """What `target` costs at `now`: full with nothing running, the difference under a smaller
    running deal (an upgrade), nought when refused. Returns (price, upgraded id or '')."""
    running = governing(tiers, held, now)
    if running is None:
        return target["gems"], ""
    if running["plays"] >= target["plays"]:
        return 0, ""
    return max(0, target["gems"] - running["gems"]), running["id"]


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


# Instants, chosen off a day boundary so the two readings can be told apart: day 100 at noon.
T0 = 100 * DAY + 12 * 3600


def allowance_cases():
    out = []

    def case(name, held, now):
        held_days = {k: day_of(v) for k, v in held.items()}
        out.append({
            "name": name,
            "held": held,
            "now": now,
            "allowance": allowance_on(TIERS, held, now, FREE),
            "serverDay": day_of(now),
            "serverAllowance": server_allowance_on(TIERS, held_days, day_of(now), FREE),
        })

    case("nothing held is the free figure", {}, T0)
    case("a deal bought this instant covers it", {"small": T0}, T0)
    case("a three-day deal still runs a second before three days are up", {"small": T0}, T0 + 3 * DAY - 1)
    case("and is free again exactly three days after purchase", {"small": T0}, T0 + 3 * DAY)
    case("a deal bought tomorrow does not cover today", {"small": T0 + DAY}, T0)
    case("the larger of two overlapping deals governs", {"small": T0, "large": T0 + DAY}, T0 + 2 * DAY)
    case("an upgrade sharing the window ends with it", {"small": T0, "large": T0}, T0 + 3 * DAY - 1)
    case("a deal this table does not sell is ignored", {"huge": T0}, T0)
    case("a nought instant is not a purchase", {"small": 0}, T0)

    # The one place the two readings differ by design: the last partial day. The instant window
    # has closed, the server's day window has not — so a play dealt before the instant and
    # claimed after it is still paid. Pinned so nobody "fixes" the server back to exclusive.
    case("on the last partial day the server is one day more generous than the client",
         {"small": T0}, T0 + 3 * DAY + 3600)

    return out


def upgrade_cases():
    out = []

    def case(name, held, now, target):
        got, upgrades = price(TIERS, held, now, next(t for t in TIERS if t["id"] == target))
        out.append({"name": name, "held": held, "now": now, "target": target, "price": got, "upgrades": upgrades})

    case("with nothing running a deal costs its full price", {}, T0, "small")
    case("the larger one too", {}, T0, "large")
    case("under a running smaller deal the larger costs the difference", {"small": T0}, T0 + DAY, "large")
    case("the running deal itself is not for sale", {"small": T0}, T0 + DAY, "small")
    case("a smaller deal under a running larger one is refused", {"large": T0}, T0 + DAY, "small")
    case("after the window a purchase is a fresh full-price one", {"small": T0}, T0 + 3 * DAY, "large")
    case("an expired deal held beside a running one does not discount", {"small": T0 - 10 * DAY, "large": T0}, T0, "large")

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
    "`challengeAllowanceCases` is the deal rule: which held deal governs an instant and how",
    "many plays it allows (`allowance`, the client's reading off `held` instants and `now`),",
    "beside the server's reading off the days (`serverDay`, `serverAllowance`), which is one",
    "day more generous at the end of a window on purpose. `challengeUpgradeCases` is the",
    "upgrade price: the difference under a running smaller deal, sharing its window. A",
    "disagreement is a claim refused for a play the page offered (45d), or gems taken back.",
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
    out["challengeUpgradeCases"] = upgrade_cases()
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
              f"{len(built['challengeAllowanceCases'])} allowance case(s), "
              f"{len(built['challengeUpgradeCases'])} upgrade case(s), reproducible")
        return 0

    VECTORS.write_text(text, encoding="utf-8")
    print(f"challenge vectors: {len(built['challengeCases'])} xp case(s), "
          f"{len(built['challengeAllowanceCases'])} allowance case(s), "
          f"{len(built['challengeUpgradeCases'])} upgrade case(s) written to {VECTORS}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
