#!/usr/bin/env python3
"""Measures and drafts Merge challenge boards (one gem slides at a time, `MergePuzzle`).

    python Tools/make_merge_challenges.py --report            # every shipped merge row: par, route, openings
    python Tools/make_merge_challenges.py --draft 200 --seed 7 # propose boards; writes nothing

**A mirror, not the authority.** The rules are `MergePuzzle.cs`: a dragged gem slides until the
edge, a rock or another gem stops it, and merges into the next rank when what stopped it is a
gem of its own rank. Nothing is dealt. `ChallengeTests.ShippedMergeIsWonByItsAuthoredRoute`
plays the route this prints against the real rules, and that test is what holds a row.

**What a good board reads as** (invariant 5d asked of a puzzle): the route is the shortest by
breadth-first search, `openings` is how many first moves keep that shortest length against how
many are legal — the smaller the first figure, the more the opening is a decision rather than a
guess — and the route should hold slides that merge nothing, because those are the plan.

Route notation: cell index then direction in board terms (`D` is down the rows), e.g. `7U`.
"""
import argparse
import json
import pathlib
import random
import sys
import time
from collections import deque

ROOT = pathlib.Path(__file__).resolve().parents[1]
CHALLENGES = ROOT / "Assets" / "StreamingAssets" / "Content" / "challenges.json"
DIRS = {"U": (0, -1), "D": (0, 1), "L": (-1, 0), "R": (1, 0)}
ROCK = -1


def parse(rows):
    board = []
    for row in rows:
        for ch in row:
            board.append(ROCK if ch == "#" else 0 if ch == "." else int(ch))
    return tuple(board), len(rows[0]), len(rows)


def slide(b, w, h, cell, d):
    """`MergePuzzle.Where` and `Apply`: the board after the move and the rank made, or None."""
    rank = b[cell]
    if rank <= 0:
        return None
    dx, dy = DIRS[d]
    x, y = cell % w, cell // w
    while True:
        nx, ny = x + dx, y + dy
        if not (0 <= nx < w and 0 <= ny < h):
            break
        nxt = b[ny * w + nx]
        if nxt == 0:
            x, y = nx, ny
            continue
        if nxt == rank:
            nb = list(b)
            nb[cell] = 0
            nb[ny * w + nx] = rank + 1
            return tuple(nb), rank + 1
        break
    to = y * w + x
    if to == cell:
        return None
    nb = list(b)
    nb[cell] = 0
    nb[to] = rank
    return tuple(nb), 0


def moves(b, w, h):
    for cell in range(len(b)):
        if b[cell] > 0:
            for d in "UDLR":
                after = slide(b, w, h, cell, d)
                if after:
                    yield (cell, d), after


def solve(b, w, h, target, limit=1500000):
    """Shortest route to a gem of `target`: (route, states), route None past the limit, False if none."""
    prev = {b: None}
    queue = deque([b])
    while queue:
        s = queue.popleft()
        if max(s) >= target:
            route = []
            while prev[s]:
                s, m = prev[s]
                route.append(m)
            return route[::-1], len(prev)
        for m, (n, _) in moves(s, w, h):
            if n not in prev:
                prev[n] = (s, m)
                queue.append(n)
                if len(prev) > limit:
                    return None, len(prev)
    return False, len(prev)


def openings(b, w, h, target, par):
    """How many first moves keep the shortest route, and how many there are."""
    keep = legal = 0
    for _, (n, _) in moves(b, w, h):
        legal += 1
        route, _ = solve(n, w, h, target)
        if route and len(route) == par - 1:
            keep += 1
    return keep, legal


def fmt(route):
    return " ".join("%d%s" % m for m in route)


def rows_of(b, w, h):
    return ["".join("#" if v == ROCK else "." if v == 0 else str(v) for v in b[y * w:(y + 1) * w]) for y in range(h)]


def report():
    table = json.loads(CHALLENGES.read_text(encoding="utf-8"))
    rows = [r for r in table["challenges"] if r.get("genre") == "merge"]
    for row in rows:
        b, w, h = parse(row["rows"])
        t0 = time.time()
        route, states = solve(b, w, h, int(row["target"]))
        if not route:
            print("%s: %s after %d states" % (row["id"], "no route" if route is False else "unsolved", states))
            continue
        made = []
        s = b
        for cell, d in route:
            s, rank = slide(s, w, h, cell, d)
            made.append(rank)
        keep, legal = openings(b, w, h, int(row["target"]), len(route))
        quiet = sum(1 for r in made if r == 0)
        print("%s: par %d (%d merges, %d quiet), openings %d of %d, %d states, %.1fs"
              % (row["id"], len(route), len(route) - quiet, quiet, keep, legal, states, time.time() - t0))
        print("  route  %s" % fmt(route))
        print("  feeds  %s" % " ".join("rgby"[(r - 1) % 4] if r else "-" for r in made))
    return 0


def draft(count, seed, w, h, target, gems, rocks):
    rng = random.Random(seed)
    found = []
    for _ in range(count):
        n = rng.choice(rocks)
        cells = rng.sample(range(w * h), len(gems) + n)
        b = [0] * (w * h)
        for c in cells[:n]:
            b[c] = ROCK
        for g, c in zip(gems, cells[n:]):
            b[c] = g
        b = tuple(b)
        route, _ = solve(b, w, h, target, 700000)
        if not route or len(route) < 9:
            continue
        keep, legal = openings(b, w, h, target, len(route))
        found.append((len(route), -keep, b, route, keep, legal))
        print("par %d openings %d of %d | %s | %s" % (len(route), keep, legal, " ".join(rows_of(b, w, h)), fmt(route)),
              flush=True)
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--draft", type=int, metavar="N", help="try N random boards")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--size", default="6x4")
    ap.add_argument("--target", type=int, default=5)
    ap.add_argument("--gems", default="1111224", help="the ranks dealt onto a drafted board")
    args = ap.parse_args()

    if args.draft:
        w, h = (int(v) for v in args.size.split("x"))
        return draft(args.draft, args.seed, w, h, args.target, [int(c) for c in args.gems], [5, 6, 7])
    return report()


if __name__ == "__main__":
    sys.exit(main())
