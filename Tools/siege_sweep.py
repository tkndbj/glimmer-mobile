# -*- coding: utf-8 -*-
"""Deals Thornwatch fields and reports what is worth knowing about them.

    python Tools/siege_sweep.py --seeds 4000 --w 8 --h 5 --gems rgby --cogs 1

**What is designed and what is dealt.** Invariant 32d's split, read across to a siege: where the
wards stand, what comes down the hill, in what order and how often a cog turns up are all things a
player *reads*, so they are written by hand in `Tools/chapters/s01_thornwatch.py`. Which gem sits in
which socket is exactly the sort of arrangement nobody can eyeball, so it is swept for here and kept
for what it measured - an even spread of the colours, no three alike already touching, and enough
opening swaps that the first second of the run is not spent watching the board deal itself again.

**What this deliberately does not do is play the level.** A siege runs on a clock and has no search
(invariant 37a), so "can this be held" cannot be answered by any arrangement of the field - it is
answered by `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine`, which plays the real C# rules over every
rung of the chapter. A second implementation of `SiegeBoard.Advance` in Python would be a mirror of
the one thing here that is genuinely hard to mirror, and every divergence in it would be silent.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import siege as rules                                                   # noqa: E402


def xorshift(state):
    """`SiegeBoard.Next`, so a sweep and the game deal from one arithmetic."""
    x = state & 0xFFFFFFFF
    x ^= (x << 13) & 0xFFFFFFFF
    x ^= x >> 17
    x ^= (x << 5) & 0xFFFFFFFF
    return x if x else 2463534242


def deal(seed, w, h, gems, cogs):
    """One field, dealt from a seed. `cogs` is how many are stood on it."""
    state = seed if seed else 1
    cells = []

    for _ in range(w * h):
        state = xorshift(state)
        cells.append(gems[state % len(gems)])

    for _ in range(cogs):
        for _ in range(200):
            state = xorshift(state)
            at = state % (w * h)

            # Never on the bottom row, and never on top of another cog. The bottom row is the one
            # place a cog can only be reached sideways - everything above it can be matched into
            # from either direction - and the rung that teaches cogs should not be teaching a
            # special case.
            if at // w == h - 1 or cells[at] == "*":
                continue
            cells[at] = "*"
            break

    return cells


def spread(cells, gems):
    """The commonest colour's share of the field, as a percentage. Lower is more even."""
    counted = [cells.count(g) for g in gems]
    return max(counted) * 100 // max(1, sum(counted))


def swaps(cells, w, h):
    """How many swaps on this field line something up. See `siege.any_swap`, counted."""
    n = 0
    work = list(cells)

    for y in range(h):
        for x in range(w):
            here = y * w + x
            for other in ((here + 1) if x + 1 < w else None,
                          (here + w) if y + 1 < h else None):
                if other is None or work[here] == work[other]:
                    continue
                work[here], work[other] = work[other], work[here]
                if rules.runs(work, w, h):
                    n += 1
                work[here], work[other] = work[other], work[here]

    return n


def rows_of(cells, w, h):
    return ["".join(cells[y * w:(y + 1) * w]) for y in range(h)]


def worth(cells, w, h, gems):
    """Whether this field is worth keeping, and what it measured."""
    if rules.runs(cells, w, h):
        return None

    made = swaps(cells, w, h)
    if made < 4:
        return None

    # Evenness is judged against what an even field *would* be, so the bar is the same shape on a
    # three-colour deal as on a four-colour one: at three, a third is even and 32% is unreachable.
    even = spread(cells, gems)
    if even > 100 // len(gems) + 7:
        return None

    return dict(swaps=made, spread=even)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", type=int, default=4000)
    ap.add_argument("--w", type=int, default=8)
    ap.add_argument("--h", type=int, default=5)
    ap.add_argument("--gems", default="rgby")
    ap.add_argument("--cogs", type=int, default=0)
    ap.add_argument("--from-seed", type=int, default=1)
    ap.add_argument("--show", type=int, default=6)
    ap.add_argument("--want", type=int, default=0,
                    help="how many opening swaps to aim for; 0 ranks by the most")
    args = ap.parse_args()

    kept = []

    for seed in range(args.from_seed, args.from_seed + args.seeds):
        cells = deal(seed, args.w, args.h, args.gems, args.cogs)
        read = worth(cells, args.w, args.h, args.gems)
        if read is None:
            continue
        kept.append((seed, read, rows_of(cells, args.w, args.h)))

    if args.want > 0:
        kept.sort(key=lambda k: (abs(k[1]["swaps"] - args.want), k[1]["spread"]))
    else:
        kept.sort(key=lambda k: (-k[1]["swaps"], k[1]["spread"]))

    print("%d of %d seeds worth keeping" % (len(kept), args.seeds))

    for seed, read, rows in kept[:args.show]:
        print("\nseed %-8d %d swaps, commonest colour %d%%" % (seed, read["swaps"], read["spread"]))
        for row in rows:
            print("    %s" % row)


if __name__ == "__main__":
    main()
