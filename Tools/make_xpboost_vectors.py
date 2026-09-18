#!/usr/bin/env python3
"""Writes the XP boost's shared clamp vectors into firebase/shared/grove-vectors.json.

    python Tools/make_xpboost_vectors.py            # rewrite the block
    python Tools/make_xpboost_vectors.py --check    # prove the committed block is what this draws

**What is under contract, and what is deliberately not.** Only the *clamp* is shared, because it
is the only half both runtimes compute: `XpBoost.BonusFrom` in C# and `xpBoostXp` in
`functions/src/grove.ts` both turn a stored bonus plus a provable XP figure into what is actually
paid. The windows, the cooldown and the percentages are facts about *offering* a boost, which no
server ever does — a server only answers "how much of this could honestly have been earned".

A drift in the clamp is silent: a `publishGrove` that clamps differently answers 200, writes a
valid card, and computes a keeper level that differs from the device's — whereupon `buildCard`
*drops* whatever the lower level gated (invariant 19a), for the players who buy the most boosts.

The mirror below is written from the prose rule rather than from either copy, so a passing case
has been agreed by three implementations that never read each other. Do not hand-edit.
"""

import argparse
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
VECTORS = ROOT / "firebase" / "shared" / "grove-vectors.json"

# Mirrors of the one compile-time bound. `XpBoostLimits.HardMaxBonusXp` / `HARD_MAX_BOOST_XP`.
HARD_MAX_BONUS_XP = 1_000_000_000

# What ships when no `xpBoost` block has been published. `XpBoostLimits` / `DEFAULT_XP_BOOST`.
DEFAULTS = {"maxPercent": 150}


def bonus_of(stored, provable, max_percent):
    """What a stored bonus is really worth, written from the rule rather than from either copy.

    The bound is *proportional*: a boost can only ever have multiplied XP that was really paid,
    so anything above `provable x maxPercent%` is arithmetically impossible however it got into
    the file. Integer throughout, and the divide happens once — nothing that decides a payment
    may be a float, because the runtimes disagree about them.
    """
    if max_percent <= 0 or provable <= 0 or stored <= 0:
        return 0

    ceiling = provable * max_percent // 100
    return min(stored, ceiling, HARD_MAX_BONUS_XP)


def cases():
    out = []

    def case(name, stored, provable, max_percent=150):
        out.append({
            "name": name,
            "stored": stored,
            "provable": provable,
            "maxPercent": max_percent,
            "bonus": bonus_of(stored, provable, max_percent),
        })

    # ---------------------------------------------------------------- nothing to pay
    case("a save that has never earned a bonus", 0, 5000)
    case("a bonus with no provable XP behind it is worth nothing", 900, 0)
    case("a negative stored bonus is nought", -900, 5000)
    case("negative provable XP pays nothing", 900, -5000)

    # ------------------------------------------------------------ under the ceiling
    case("a bonus well under the share is paid in full", 100, 5000)
    case("a bonus exactly on the share is paid in full", 7500, 5000)
    case("a bonus one over the share is clamped", 7501, 5000)

    # ------------------------------------------------------- the clamp doing its job
    case("a forged bonus is clamped to the share of provable XP", 10 ** 9, 1000)
    case("a forged bonus against no progress pays nothing", 10 ** 9, 0)
    case("a forged bonus against a little progress buys a little", 10 ** 9, 150)

    # ------------------------------------------------------------- the percentage
    case("a withdrawn cap pays nothing at all", 9000, 5000, 0)
    case("the watched window alone", 9000, 5000, 50)
    case("the bought window alone", 9000, 5000, 100)
    case("a generous cap widens the share", 10 ** 6, 5000, 1000)

    # --------------------------------------------------------------- the arithmetic
    # Truncation, which is where two runtimes most easily disagree. 333 x 150% is 499.5.
    case("the share truncates rather than rounding", 10 ** 6, 333)
    case("and again on an awkward percentage", 10 ** 6, 777, 50)

    # The overflow guard behind the proportional bound. Only reachable when provable XP is
    # itself enormous, which is the case the hard ceiling exists for.
    case("the structural ceiling holds behind the share",
         HARD_MAX_BONUS_XP * 2, HARD_MAX_BONUS_XP, 1000)

    return out


COMMENT = [
    "The XP boost's clamp, derived twice and pinned here.",
    "",
    "A boost multiplies XP at the moment it is paid and banks the bonus, because XP is derived",
    "and there is no running total to scale (invariant 9's second exception, see XpBoost). What",
    "both runtimes then have to agree on is how much of that banked figure is real: it is",
    "clamped to `provable x maxPercent%`, where provable is the star ledger's XP plus the",
    "Infinite lane's. That is a *proportional* bound rather than a flat ceiling — a boost can",
    "only ever have multiplied XP that was really paid — and it is the same shape groveWorth",
    "uses to clamp a grove to what the account could afford (19a).",
    "",
    "Only the clamp is shared. Windows, cooldowns and percentages are facts about offering a",
    "boost, which no server does.",
    "",
    "A drift here is silent: the card is valid, the keeper level is merely lower, and 19a drops",
    "what that level gated rather than clamping it.",
    "",
    "Written by Tools/make_xpboost_vectors.py — a third implementation. Do not hand-edit.",
]


def build(existing):
    out = dict(existing)
    out["_xpBoostComment"] = COMMENT
    out["xpBoostDefaults"] = DEFAULTS
    out["xpBoostCases"] = cases()
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
            print("grove-vectors.json is not what make_xpboost_vectors.py draws; re-run without "
                  "--check", file=sys.stderr)
            return 1
        print(f"xp boost vectors: {len(built['xpBoostCases'])} case(s), reproducible")
        return 0

    VECTORS.write_text(text, encoding="utf-8")
    print(f"xp boost vectors: {len(built['xpBoostCases'])} case(s) written to {VECTORS}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
