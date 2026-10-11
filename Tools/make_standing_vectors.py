#!/usr/bin/env python3
"""Writes the chapter standing's shared vectors into firebase/shared/grove-vectors.json.

    python Tools/make_standing_vectors.py            # rewrite the block
    python Tools/make_standing_vectors.py --check    # prove the committed block is what this draws

**Why a third implementation.** A finished chapter's score (invariant 62) exists twice and must
not drift: `ChapterStanding.ScoreOf` in C#, which folds the player's own save on the handset, and
`chapterScore` in `functions/src/standings.ts`, which folds everybody else's into the cut points
the handset compares against. A disagreement crashes nothing and refuses nothing - it tells a
player a number that is quietly about a different population. The mirror below is written from
the prose rule instead of from either copy, so a case that passes has been agreed by three people
who never read each other's arithmetic.

It touches only `standingConstants`, `standingCases` and their comment; every other block in the
file is left exactly as it was found, keys and order included.
"""

import argparse
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
VECTORS = ROOT / "firebase" / "shared" / "grove-vectors.json"

# Mirrors of the compile-time constants on both sides: `ChapterStanding.StarWeight` /
# `STAR_WEIGHT`, `ChapterStanding.MovesCap` / `MOVES_CAP`, `LevelRecord.MaxStars` / `MAX_STARS`.
STAR_WEIGHT = 10_000_000
MOVES_CAP = 9_999
MAX_STARS = 3

CONSTANTS = {"starWeight": STAR_WEIGHT, "movesCap": MOVES_CAP, "maxStars": MAX_STARS}


def score_of(levels, chapter):
    """The prose rule: every level cleared, stars first, fewer moves second, one integer.

    `None` is "not finished", which is a different thing from a bad score and is why it is not
    nought: the sampler leaves an unfinished chapter out of the population altogether.
    """
    if not chapter:
        return None

    stars = 0
    moves = 0
    for level_id in chapter:
        entry = levels.get(level_id)
        if not isinstance(entry, dict):
            return None

        raw_stars = entry.get("stars", 0)
        raw_stars = int(raw_stars) if isinstance(raw_stars, (int, float)) else 0
        if raw_stars <= 0:
            return None

        raw_moves = entry.get("bestMoves", 0)
        raw_moves = int(raw_moves) if isinstance(raw_moves, (int, float)) else 0

        stars += min(raw_stars, MAX_STARS)
        moves += MOVES_CAP if raw_moves <= 0 else min(raw_moves, MOVES_CAP)

    return stars * STAR_WEIGHT - moves


def rec(stars, moves):
    return {"stars": stars, "bestMoves": moves}


CHAPTER = ["s01_a", "s01_b", "s01_c"]


def cases():
    """Every case, named for the thing it would catch if it broke."""
    out = []

    def case(name, levels, chapter=None):
        chapter = CHAPTER if chapter is None else chapter
        out.append({
            "name": name,
            "chapter": chapter,
            "levels": levels,
            "score": score_of(levels, chapter),
        })

    # ------------------------------------------------------------- not finished
    case("an empty save has not finished anything", {})
    case("one level short is not finished",
         {"s01_a": rec(3, 10), "s01_b": rec(3, 10)})
    case("a level played but never cleared is not finished",
         {"s01_a": rec(3, 10), "s01_b": rec(3, 10), "s01_c": rec(0, 4)})
    case("a chapter with no levels has no standing", {"s01_a": rec(3, 10)}, [])
    case("a level of another chapter does not count towards this one",
         {"s01_a": rec(3, 10), "s01_b": rec(3, 10), "s02_x": rec(3, 10)})

    # ------------------------------------------------------------------ finished
    case("three levels three-starred", {k: rec(3, 10) for k in CHAPTER})
    case("stars are summed and moves subtracted",
         {"s01_a": rec(3, 12), "s01_b": rec(2, 20), "s01_c": rec(1, 7)})
    case("one star fewer outweighs any saving in moves",
         {"s01_a": rec(2, 1), "s01_b": rec(3, 1), "s01_c": rec(3, 1)})
    case("fewer moves on equal stars is the better score",
         {"s01_a": rec(3, 9), "s01_b": rec(3, 10), "s01_c": rec(3, 10)})
    case("the worst finished score is still positive",
         {k: rec(1, MOVES_CAP) for k in CHAPTER})

    # ------------------------------------------------------------------ the caps
    case("a fourth star counts as three", {"s01_a": rec(9, 10), "s01_b": rec(3, 10), "s01_c": rec(3, 10)})
    case("a cleared level with no move count counts as the cap",
         {"s01_a": rec(3, 0), "s01_b": rec(3, 10), "s01_c": rec(3, 10)})
    case("a negative move count counts as the cap",
         {"s01_a": rec(3, -5), "s01_b": rec(3, 10), "s01_c": rec(3, 10)})
    case("an absurd move count is capped, not refused",
         {"s01_a": rec(3, 10 ** 9), "s01_b": rec(3, 10), "s01_c": rec(3, 10)})
    case("the caps apply per level, before the sum",
         {k: rec(3, 10 ** 9) for k in CHAPTER})

    # ------------------------------------------------------------------ a long chapter
    long = ["s12_%02d" % i for i in range(20)]
    case("twenty levels, mixed", {k: rec(1 + i % 3, 5 + i) for i, k in enumerate(long)}, long)

    return out


COMMENT = [
    "Where a finished chapter stands, derived twice and pinned here (invariant 62).",
    "",
    "`standingCases` is the score a finished chapter is ranked on: every level cleared, stars",
    "summed and weighted so one star outweighs any number of moves, best moves subtracted,",
    "each level's moves clamped to `movesCap` (a cleared level with no count is the cap). It",
    "runs in C# (`ChapterStanding.ScoreOf`) to fold the player's own save on the handset, and",
    "in functions/src/standings.ts (`chapterScore`) to fold everybody else's into the cut points",
    "the handset compares against - and when the two disagree nothing crashes and nothing is",
    "refused; a player is told a number that is quietly about a different population.",
    "",
    "Each case is a `levels` map as the save carries it, the chapter's level ids, and the score",
    "both sides must produce - `null` when the chapter is not finished, which the sampler reads",
    "as 'not in this population' and the map reads as 'clear every level first'.",
    "",
    "Written by Tools/make_standing_vectors.py - a third implementation, so a passing case has",
    "been agreed by three copies that never read each other. Do not hand-edit.",
]


def build(existing):
    out = dict(existing)
    out["_standingComment"] = COMMENT
    out["standingConstants"] = CONSTANTS
    out["standingCases"] = cases()
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
            print("grove-vectors.json is not what make_standing_vectors.py draws; re-run without "
                  "--check", file=sys.stderr)
            return 1
        print(f"standing vectors: {len(built['standingCases'])} case(s), reproducible")
        return 0

    VECTORS.write_text(text, encoding="utf-8")
    print(f"standing vectors: {len(built['standingCases'])} case(s) written to {VECTORS}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
