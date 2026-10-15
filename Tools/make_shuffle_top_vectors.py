#!/usr/bin/env python3
"""Writes the Shuffle graded run's shared vectors into firebase/shared/grove-vectors.json.

    python Tools/make_shuffle_top_vectors.py            # rewrite the block
    python Tools/make_shuffle_top_vectors.py --check    # prove the committed block is what this draws

**Why a third implementation.** A keeper's best-graded Shuffle run (MODES.md 59o) is read off a
save twice and must not drift: `ShuffleLedger.TopIn` in C# (through `FirestoreSaveMapper`, the
reading that decides whether a card owes a publish) and `shuffleTop` in
`functions/src/grove.ts` (the reading a stranger is shown). A disagreement crashes nothing - a
profile quietly shows a run, or a figure, the player's own device never banked. The mirror below
is written from the prose rule rather than from either copy, so a case that passes has been
agreed by three readers that never read each other.

It touches only `shuffleTopConstants`, `shuffleTopCases` and their comment; every other block in
the file is left exactly as it was found, keys and order included.
"""

import argparse
import json
import math
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
VECTORS = ROOT / "firebase" / "shared" / "grove-vectors.json"

# Mirrors of the constants on both sides: `ShuffleLedger.MaxRows` / the rules' 64,
# `LevelId.MaxLength` / `MAX_LEVEL_ID_LENGTH`, `ShuffleGrades.MaxOrdinal`, `ShuffleGrades.MaxPoints`,
# `ShuffleLedger.MaxWave` / `MAX_WAVE`, `ShuffleStats.MaxRows`, `.MaxValue`, `.MaxIdLength`.
MAX_ROWS = 64
MAX_LEVEL = 48
MAX_GRADE = 63
MAX_POINTS = 1_000_000
MAX_WAVE = 9_999
MAX_STATS = 16
MAX_STAT = 100_000_000
MAX_STAT_ID = 24

CONSTANTS = {
    "maxRows": MAX_ROWS, "maxLevelId": MAX_LEVEL, "maxGrade": MAX_GRADE,
    "maxPoints": MAX_POINTS, "maxWave": MAX_WAVE, "maxStats": MAX_STATS,
    "maxStat": MAX_STAT, "maxStatId": MAX_STAT_ID,
}

INT32 = (-2 ** 31, 2 ** 31 - 1)
STAT_ID = re.compile(r"^[a-z0-9_]+$")


def number(value):
    """A wire figure: a number toward nought, NaN as nought, saturating at 32 bits; anything else nought."""
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return 0
    if isinstance(value, float):
        if math.isnan(value):
            return 0
        if math.isinf(value):
            return INT32[1] if value > 0 else INT32[0]
        value = math.trunc(value)
    return max(INT32[0], min(INT32[1], value))


def row_wave(value):
    """A row's wave as the save mapper reads it: a number toward nought, or a whole-number string."""
    if isinstance(value, str):
        return int(value) if re.fullmatch(r"-?\d+", value) else 0
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return 0
    if isinstance(value, float) and math.isnan(value):
        return 0
    return math.trunc(value)


def record(raw):
    """The prose rule for one record: no points is no run; every figure bounded; stats canonical."""
    if not isinstance(raw, dict):
        return None

    points = number(raw.get("points"))
    if points <= 0:
        return None

    stats = {}
    raw_stats = raw.get("stats")
    if isinstance(raw_stats, dict):
        for key, value in raw_stats.items():
            if not isinstance(key, str) or not key or len(key) > MAX_STAT_ID or not STAT_ID.match(key):
                continue
            stats[key] = max(stats.get(key, 0), max(0, min(MAX_STAT, number(value))))

    # The first MAX_STATS ids in id order, never the first that arrived.
    kept = {k: stats[k] for k in sorted(stats)[:MAX_STATS]}

    return {
        "grade": max(0, min(MAX_GRADE, number(raw.get("grade")))),
        "points": min(points, MAX_POINTS),
        "wave": max(0, min(MAX_WAVE, number(raw.get("wave")))),
        "stats": kept,
    }


def compare(a, b):
    """Grade, then points, then wave, then the stats in id order: a row the other lacks first wins."""
    if a is None and b is None:
        return 0
    if a is None:
        return -1
    if b is None:
        return 1
    for key in ("grade", "points", "wave"):
        if a[key] != b[key]:
            return -1 if a[key] < b[key] else 1
    xs, ys = list(a["stats"].items()), list(b["stats"].items())
    for (xi, xv), (yi, yv) in zip(xs, ys):
        if xi != yi:
            return 1 if xi < yi else -1
        if xv != yv:
            return -1 if xv < yv else 1
    return (len(xs) > len(ys)) - (len(xs) < len(ys))


def top_of(save):
    """The best record a save holds: valid rows (a level, a wave above nought), the first 64, short ids."""
    rows = save.get("shuffleBest") if isinstance(save, dict) else None
    if not isinstance(rows, list):
        return None

    valid = [r for r in rows
             if isinstance(r, dict) and isinstance(r.get("level"), str) and r["level"]
             and row_wave(r.get("wave")) > 0]

    best = None
    for row in valid[:MAX_ROWS]:
        if len(row["level"]) > MAX_LEVEL:
            continue
        candidate = record(row.get("top"))
        if compare(candidate, best) > 0:
            best = candidate
    return best


def run(grade, points, wave, **stats):
    return {"grade": grade, "points": points, "wave": wave, "stats": stats}


def cases():
    """Every case, named for the thing it would catch if it broke."""
    out = []

    def case(name, rows):
        save = {"shuffleBest": rows}
        out.append({"name": name, "save": save, "top": top_of(save)})

    plain = run(3, 2400, 61, damage=1240, speed=880, crit=35, health=150, cards=30, bonds=3)

    # ------------------------------------------------------------- nothing
    case("no rows is no run", [])
    case("a row with no top is no run", [{"level": "s13_shuffle", "wave": 12}])
    case("a top with no points is no run", [{"level": "s13_shuffle", "wave": 12, "top": run(5, 0, 12)}])
    case("negative points are no run", [{"level": "s13_shuffle", "wave": 12, "top": run(5, -40, 12)}])
    case("a top that is not a map is no run", [{"level": "s13_shuffle", "wave": 12, "top": "S++"}])

    # ------------------------------------------------------------- one run
    case("one run is read whole", [{"level": "s13_shuffle", "wave": 61, "top": plain}])
    case("a run with no stats is still a run", [{"level": "s13_shuffle", "wave": 9, "top": {"grade": 1, "points": 300, "wave": 9}}])
    case("a row whose wave is nought is not read", [{"level": "s13_shuffle", "wave": 0, "top": plain}])
    case("a row with no level is not read", [{"level": "", "wave": 61, "top": plain}])
    case("a level id no catalog could ship is not read", [{"level": "x" * 49, "wave": 61, "top": plain}])
    case("a whole-number string wave is a wave", [{"level": "s13_shuffle", "wave": "61", "top": plain}])

    # ------------------------------------------------------------- the bounds
    case("a grade a newer build appended is carried as written", [{"level": "a", "wave": 5, "top": run(40, 100, 5)}])
    case("an absurd grade is bounded", [{"level": "a", "wave": 5, "top": run(900, 100, 5)}])
    case("a negative grade reads as D", [{"level": "a", "wave": 5, "top": run(-3, 100, 5)}])
    case("points are bounded", [{"level": "a", "wave": 5, "top": run(7, 10 ** 12, 5)}])
    case("a fractional figure is taken toward nought", [{"level": "a", "wave": 5, "top": run(4.9, 999.99, 5.5)}])
    case("a wave is bounded", [{"level": "a", "wave": 5, "top": run(2, 600, 10 ** 9)}])
    case("a negative wave reads as nought", [{"level": "a", "wave": 5, "top": run(2, 600, -4)}])
    case("a figure that is not a number is nought",
         [{"level": "a", "wave": 5, "top": {"grade": "7", "points": 500, "wave": True, "stats": {"damage": "900", "speed": 300}}}])
    case("stats are bounded, malformed ids dropped, unknown ids carried",
         [{"level": "a", "wave": 5, "top": run(2, 600, 5, damage=-50, speed=10 ** 12, future_stat=44,
                                                 **{"Bad": 3, "with space": 1, "x" * 25: 9, "ok_9": 2})}])
    case("only the first sixteen stat ids are kept, in id order",
         [{"level": "a", "wave": 5, "top": run(2, 600, 5, **{"s%02d" % i: i for i in range(30, 0, -1)})}])

    # ------------------------------------------------------------- the best of several
    case("the higher grade wins over more points",
         [{"level": "a", "wave": 80, "top": run(4, 4600, 80)}, {"level": "b", "wave": 90, "top": run(5, 7600, 90, damage=1)}])
    case("on one grade the more points win",
         [{"level": "a", "wave": 80, "top": run(4, 4700, 70)}, {"level": "b", "wave": 90, "top": run(4, 4600, 90)}])
    case("on one score the further wave wins",
         [{"level": "a", "wave": 80, "top": run(4, 4600, 70)}, {"level": "b", "wave": 90, "top": run(4, 4600, 71)}])
    case("on everything else the stats decide",
         [{"level": "a", "wave": 80, "top": run(4, 4600, 70, damage=500)},
          {"level": "b", "wave": 90, "top": run(4, 4600, 70, damage=510)}])
    case("a record carrying a stat the other lacks wins the tie",
         [{"level": "a", "wave": 80, "top": run(4, 4600, 70, damage=500, speed=1)},
          {"level": "b", "wave": 90, "top": run(4, 4600, 70, apex=1, damage=500)}])
    case("an invalid row cannot win",
         [{"level": "a", "wave": 0, "top": run(7, 99999, 300)}, {"level": "b", "wave": 9, "top": run(1, 300, 9)}])

    # ------------------------------------------------------------- the walk
    walk = [{"level": "l%03d" % i, "wave": 5, "top": run(1, 100 + i, 5)} for i in range(70)]
    case("no more rows are read than the rules allow", walk)
    holes = [{"level": "", "wave": 5, "top": run(7, 99999, 5)}] * 10 + walk
    case("the walk counts readable rows, not raw ones", holes)

    return out


COMMENT = [
    "A keeper's best-graded Shuffle run, read off a save twice and pinned here (MODES.md 59o).",
    "",
    "`shuffleTopCases` is the record a save's `shuffleBest` rows carry under `top`: the rows with",
    "a level and a wave above nought, the first `maxRows` of them, ids no longer than `maxLevelId`;",
    "each `top` read as {grade, points, wave, stats} with no points meaning no run, every figure",
    "taken toward nought and bounded (a grade to `maxGrade`, carried past the grades a build knows), stat ids lower-case letters, digits and underscores no longer",
    "than `maxStatId`, values within `maxStat`, and only the first `maxStats` ids in id order kept;",
    "the best chosen by grade, then points, then wave, then the stats in id order. It runs in C#",
    "(`ShuffleLedger.TopIn` over `FirestoreSaveMapper`) to decide whether a card owes a publish,",
    "and in functions/src/grove.ts (`shuffleTop`) to build the card a stranger reads.",
    "",
    "Each case is a save as the wire carries it and the record both sides must produce - `null`",
    "for none. Written by Tools/make_shuffle_top_vectors.py - a third implementation, so a",
    "passing case has been agreed by three readers that never read each other. Do not hand-edit.",
]


def build(existing):
    out = dict(existing)
    out["_shuffleTopComment"] = COMMENT
    out["shuffleTopConstants"] = CONSTANTS
    out["shuffleTopCases"] = cases()
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
            print("grove-vectors.json is not what make_shuffle_top_vectors.py draws; re-run without "
                  "--check", file=sys.stderr)
            return 1
        print(f"shuffle top vectors: {len(built['shuffleTopCases'])} case(s), reproducible")
        return 0

    VECTORS.write_text(text, encoding="utf-8", newline="\n")
    print(f"shuffle top vectors: {len(built['shuffleTopCases'])} case(s) written to {VECTORS}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
