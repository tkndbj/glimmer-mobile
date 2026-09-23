# -*- coding: utf-8 -*-
"""The shortest route through a Push (sokoban) challenge, as `SokobanPuzzle.Apply` plays it.

    python Tools/push_route.py                 # every sokoban row of challenges.json
    python Tools/push_route.py --id d06_sokoban
    python Tools/push_route.py --seats         # also print the move on which each colour first seats

**Why this exists.** `ChallengeTests.ShippedSokobanIsWonByItsAuthoredRoute` plays a route
constant against the shipped row, and the waves of that row are timed off the order the route
seats the gems (a colour's raider must be on the hill for two steps after its gem seats, and
off the line before then). Both the constant and the timing are derived here rather than
typed: a breadth-first search over keeper moves, with exactly the rules the puzzle keeps — a
gem is pushed one cell, into nothing but floor, and the keeper never stands on one.

It is a plain BFS over (keeper, gems) and takes a minute or two on a 10x6 room; that is the
price of a step-optimal answer, which is what the test's comment promises.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import deque
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
FILE = REPO / "Assets" / "StreamingAssets" / "Content" / "challenges.json"

DIRS = {"U": (0, -1), "D": (0, 1), "L": (-1, 0), "R": (1, 0)}   # `Swipe(0,1)` is U: up-positive


def solve(rows, gems):
    """The step-shortest route, or None when no route exists."""
    H, W = len(rows), len(rows[0])
    wall = [[c == "#" for c in r] for r in rows]
    pad = {(x, y): rows[y][x].lower() for y in range(H) for x in range(W) if rows[y][x] in "RGBY"}
    keeper = next((x, y) for y in range(H) for x in range(W) if rows[y][x] == "@")
    start = (keeper, tuple(sorted(((x, y), gems[y][x]) for y in range(H) for x in range(W) if gems[y][x] != ".")))

    def solved(gs):
        return all(pad.get(p) == c for p, c in gs)

    seen = {start: None}
    q = deque([start])
    while q:
        st = q.popleft()
        (kx, ky), gs = st
        if solved(gs):
            route, cur = [], st
            while seen[cur] is not None:
                prev, d = seen[cur]
                route.append(d)
                cur = prev
            return "".join(reversed(route))
        gd = dict(gs)
        for d, (dx, dy) in DIRS.items():
            tx, ty = kx + dx, ky + dy
            if not (0 <= tx < W and 0 <= ty < H) or wall[ty][tx]:
                continue
            ng = dict(gd)
            if (tx, ty) in ng:
                bx, by = tx + dx, ty + dy
                if not (0 <= bx < W and 0 <= by < H) or wall[by][bx] or (bx, by) in ng:
                    continue
                ng[(bx, by)] = ng.pop((tx, ty))
            ns = ((tx, ty), tuple(sorted(ng.items())))
            if ns in seen:
                continue
            seen[ns] = (st, d)
            q.append(ns)
    return None


def seats(rows, gems, route):
    """The move (1-based) on which each colour first stands on its pad along the route."""
    H, W = len(rows), len(rows[0])
    pad = {(x, y): rows[y][x].lower() for y in range(H) for x in range(W) if rows[y][x] in "RGBY"}
    g = {(x, y): gems[y][x] for y in range(H) for x in range(W) if gems[y][x] != "."}
    k = next((x, y) for y in range(H) for x in range(W) if rows[y][x] == "@")
    first = {}
    for i, s in enumerate(route):
        dx, dy = DIRS[s]
        t = (k[0] + dx, k[1] + dy)
        if t in g:
            g[(t[0] + dx, t[1] + dy)] = g.pop(t)
        k = t
        for p, c in g.items():
            if pad.get(p) == c and c not in first:
                first[c] = i + 1
    return first


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--id")
    ap.add_argument("--seats", action="store_true")
    args = ap.parse_args()

    table = json.load(open(FILE, encoding="utf-8"))
    picked = [r for r in table["challenges"] if r["genre"] == "sokoban" and (not args.id or r["id"] == args.id)]
    if not picked:
        sys.exit("no sokoban challenge named %r" % args.id)

    for row in picked:
        route = solve(row["rows"], row["gems"])
        if route is None:
            print("%s: NO ROUTE - the row cannot be solved" % row["id"])
            continue
        print("%s: %s (%d moves, the hill walks %d)" % (row["id"], route, len(route), len(route) - 1))
        if args.seats:
            for colour, move in sorted(seats(row["rows"], row["gems"], route).items(), key=lambda kv: kv[1]):
                print("   %s seats on move %d" % (colour, move))


if __name__ == "__main__":
    main()
