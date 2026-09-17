#!/usr/bin/env python3
"""Writes the Infinite lane's shared reward vectors into firebase/shared/grove-vectors.json.

    python Tools/make_endless_vectors.py            # rewrite the block
    python Tools/make_endless_vectors.py --check    # prove the committed block is what this draws

**Why a third implementation.** The rule that turns a save's endless rows into XP exists twice
and must not drift — `EndlessLedger.LifetimeWavesIn` + `EndlessRewardTable.XpFor` in C#, and
`endlessWaves` + `endlessXp` in `functions/src/grove.ts` — because a keeper level the two halves
disagree about is a published card that silently *drops* whatever that level gated (invariant
19a). Typing the expected numbers by hand would make this file a transcription of one of them.
The mirror below is written from the prose rule instead, so a case that passes has been agreed by
three people who never read each other's arithmetic.

It touches only `endlessConfig`, `endlessDefaults`, `endlessCases` and their comment; every other
block in the file is left exactly as it was found, keys and order included.
"""

import argparse
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
VECTORS = ROOT / "firebase" / "shared" / "grove-vectors.json"

# Mirrors of the two compile-time bounds. `EndlessLedger.MaxWave` / `MAX_WAVE`, and
# `EndlessLimits.HardMaxWaves` / `HARD_MAX_LIFETIME_WAVES`.
MAX_WAVE = 9999
HARD_MAX_LIFETIME_WAVES = 1_000_000
MAX_ROWS = 64
MAX_LEVEL_ID = 48

# What ships in the build when no `endless` block has been published. `EndlessLimits` and
# `DEFAULT_ENDLESS`. Both sides assert these, so the three copies cannot drift apart.
DEFAULTS = {"xpPerWave": 15, "maxWaves": 99990}

# Deliberately not the shipped figures: a vector pinned to live tuning fails the day somebody
# retunes, which teaches everyone to edit vectors rather than to read them. Small numbers also
# put the ceiling cases within sight.
CONFIG = {"xpPerWave": 7, "maxWaves": 100}


def waves_of(rows):
    """Lifetime waves a save's rows are worth, written from the rule rather than from either copy."""
    total = 0

    # The cap bounds the *walk*, not the count of rows accepted: both copies stop at the 64th
    # array entry, malformed ones included, which is what firestore.rules bounds and what
    # `bestWave` beside it has always done.
    for row in (rows or [])[:MAX_ROWS]:
        if row is None:
            continue

        level = row.get("level") or ""
        if not level or len(level) > MAX_LEVEL_ID:
            continue

        # The two fields carry different ceilings: a best is the published figure, a tally is a
        # sum of bests. A best floors the tally, which is how a save written before the tally
        # existed still pays for the runs it plainly made.
        best = min(max(int(row.get("wave") or 0), 0), MAX_WAVE)
        tally = min(max(int(row.get("waves") or 0), 0), HARD_MAX_LIFETIME_WAVES)

        total += max(best, tally)

    return total


def xp_of(rows, config):
    rate = int(config["xpPerWave"])
    ceiling = int(config["maxWaves"])
    if rate <= 0 or ceiling <= 0:
        return 0
    return min(waves_of(rows), ceiling) * rate


def row(level, wave=0, waves=0):
    return {"level": level, "wave": wave, "waves": waves}


def cases():
    """Every case, named for the thing it would catch if it broke."""
    out = []

    def case(name, rows, config=None):
        out.append({
            "name": name,
            "rows": rows,
            "config": config or CONFIG,
            "waves": waves_of(rows),
            "xp": xp_of(rows, config or CONFIG),
        })

    # ------------------------------------------------------------- nothing played
    case("a save with no endless rows at all", [])
    case("a row naming no level is not a run", [row("", waves=900)])
    case("a level id no catalog could have shipped is refused", [row("x" * 49, waves=900)])
    case("a row with both floors at nought is not a run", [row("s02_endlesswatch")])

    # ------------------------------------------------- the tally, and the best under it
    case("a tally on its own is what is paid", [row("s02_endlesswatch", waves=40)])
    case("a best on its own floors the tally, which is the migration off a v29 file",
         [row("s02_endlesswatch", wave=40)])
    case("a tally under the best is floored by it, because a lifetime is at least one run",
         [row("s02_endlesswatch", wave=40, waves=9)])
    case("a tally over the best is what counts", [row("s02_endlesswatch", wave=40, waves=57)])
    case("equal halves pay once rather than twice", [row("s02_endlesswatch", wave=12, waves=12)])

    # ------------------------------------------------------------------ several lanes
    case("every row is summed, where the published board takes a maximum",
         [row("s02_endlesswatch", wave=12, waves=30),
          row("s07_second_lane", wave=40, waves=9),
          row("s08_third_lane", waves=4)])

    # ---------------------------------------------------------------------- refusals
    case("a negative tally is nought", [row("s02_endlesswatch", waves=-9)])
    case("a negative best is nought, and does not subtract",
         [row("s02_endlesswatch", wave=-9, waves=20)])

    # ------------------------------------------------------------------ the two bounds
    case("a forged best is capped at the published ceiling, not at the tally's",
         [row("s02_endlesswatch", wave=10 ** 9)])
    case("a forged tally is capped at the structural ceiling",
         [row("s02_endlesswatch", waves=10 ** 9)])
    case("the per-row caps apply before the sum, so one absurd row cannot dominate",
         [row("s02_endlesswatch", wave=10 ** 9, waves=10 ** 9),
          row("s07_second_lane", waves=5)])

    # ---------------------------------------------------- the ceiling on what is paid
    case("a total under the ceiling pays in full", [row("s02_endlesswatch", waves=99)])
    case("a total on the ceiling pays in full", [row("s02_endlesswatch", waves=100)])
    case("a total over the ceiling pays the ceiling", [row("s02_endlesswatch", waves=101)])
    case("the ceiling is on the total and not on the row",
         [row("s02_endlesswatch", waves=60), row("s07_second_lane", waves=60)])

    # --------------------------------------------------------------- withdrawn payment
    case("a rate of nought withdraws the payment without withdrawing the lane",
         [row("s02_endlesswatch", waves=500)], {"xpPerWave": 0, "maxWaves": 100})
    case("a ceiling of nought pays nothing",
         [row("s02_endlesswatch", waves=500)], {"xpPerWave": 7, "maxWaves": 0})

    # ------------------------------------------------------------------- the row cap
    # Past `EndlessLedger.MaxRows`, which is also the rules' own cap. A document written before
    # that cap existed must not be able to cost either side an unbounded walk, and both sides
    # have to stop at the same row or their totals differ.
    case("no more rows are read than the rules allow",
         [row("lane%02d" % i, waves=1) for i in range(200)],
         {"xpPerWave": 1, "maxWaves": HARD_MAX_LIFETIME_WAVES})

    # A refused row *does* spend a place, because the cap bounds the walk. That is the less
    # obvious of the two readings and it is the one both copies take, so it is pinned here
    # rather than left to be rediscovered: this case is what caught the two sides disagreeing
    # in the first place, and it can only ever differ on a document somebody has tampered with.
    case("a refused row still spends one of the sixty-four, because the cap bounds the walk",
         [row("", waves=1)] + [row("lane%02d" % i, waves=1) for i in range(64)],
         {"xpPerWave": 1, "maxWaves": HARD_MAX_LIFETIME_WAVES})

    # --------------------------------------------------------------------- arithmetic
    # The product at full stretch, which is where an `int` would wrap on either side.
    case("the largest total the shipped ceiling allows does not overflow",
         [row("s02_endlesswatch", waves=HARD_MAX_LIFETIME_WAVES)], DEFAULTS)

    return out


COMMENT = [
    "What the Infinite lane pays, derived twice and pinned here.",
    "",
    "`endlessCases` is the only rule in this game that turns a save into XP without a star",
    "behind it (invariant 9's one exception, see EndlessRewardTable). It runs in C# so the",
    "game can draw a keeper level offline, and in functions/src/grove.ts so a published card",
    "carries the same one — and when those two disagree nothing crashes and nothing is",
    "refused: `buildCard` simply *drops* whatever the lower level gated (invariant 19a), in",
    "silence, for the players who play the lane most.",
    "",
    "Each case is a set of save rows and a config. `waves` is what both sides must read off",
    "the rows; `xp` is what both must pay for it. The cases that matter most are the two",
    "ceilings — a best is capped at 9,999 because it is published, a tally an order of",
    "magnitude higher because it is a sum of bests — and the row cap, where a divergence",
    "would only ever show up on an account somebody had tampered with.",
    "",
    "`endlessDefaults` is what both sides use when no `endless` block has been published.",
    "They agree on purpose rather than failing closed: a server that is a deploy behind would",
    "otherwise publish every Infinite player short, with nothing said anywhere.",
    "",
    "Written by Tools/make_endless_vectors.py — a third implementation, so a passing case",
    "has been agreed by three copies that never read each other. Do not hand-edit.",
]


def build(existing):
    out = dict(existing)
    out["_endlessComment"] = COMMENT
    out["endlessDefaults"] = DEFAULTS
    out["endlessConfig"] = CONFIG
    out["endlessCases"] = cases()
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
            print("grove-vectors.json is not what make_endless_vectors.py draws; re-run without "
                  "--check", file=sys.stderr)
            return 1
        print(f"endless vectors: {len(built['endlessCases'])} case(s), reproducible")
        return 0

    VECTORS.write_text(text, encoding="utf-8")
    print(f"endless vectors: {len(built['endlessCases'])} case(s) written to {VECTORS}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
