#!/usr/bin/env python3
"""Designs, proves, tunes and prints the Push (sokoban) genre of the daily challenges.

    python Tools/make_push_challenges.py --report [--id d99_sokoban]   # every push row, and its faults
    python Tools/make_push_challenges.py --search hard --size 7x6 --gems 4 --count 3000
    python Tools/make_push_challenges.py --draft [--id d99_sokoban]    # compose DRAFTS rows, tune, place in the file
    python Tools/make_push_challenges.py --write [--id d99_sokoban]    # re-tune every push row's hill and waves
    python Tools/make_push_challenges.py --check                       # prove the file, the drafts and the routes
    python Tools/make_push_challenges.py --seats --id d99_sokoban      # the step each colour first seats on

**The rules it mirrors are `SokobanPuzzle.cs`**: a swipe walks the keeper one cell; walking into
a gem pushes it one cell, into nothing but floor; the board's edge is a wall. Every step is a
turn, so par is the *step*-shortest route, not the push-shortest one. A gem standing on the pad
of its own colour **streams** `bolts` at its turret every turn it stands there (a volley, fired
that turn or spent, 56l's shape: banked, a gem seated early stockpiled against every raider to
come and the hill stood empty). UNDO takes the last step back and is itself a turn. The C# is
the authority whenever the two disagree, and `ChallengeTests` replays every row's route against
it.

**A board is composed from its seed** (`DRAFTS`, `compose`), exactly as the Merge tool's are:
walls dropped as short runs, the floor trimmed to one room, pads placed, and then a
breadth-first search run *backwards* from the solved board - the keeper pulling gems off their
pads - so every state it reaches is solvable and its distance is exactly its par. The farthest
state within the tier's ceiling is the one dealt. `--search` is how a seed was found; `--check` proves the
file's rows are what the drafts compose.

**What a row is held to, as counts** (invariant 5d asked of a puzzle), per tier (`TIERS`): par
(steps) in the tier's band; enough pushes that the route is a plan; enough **switches** - the
number of times the route leaves one gem for another - that the order is the puzzle rather than
one gem walked home at a time; every gem pushed at least twice; and no gem dealt on its own pad.
And the hill, as the glade tool holds it: the *slack* (the slowest pace, in hundredths of the
route's turns, that still wins) near the tier's target and inside `SLACK_BAND`, the hill never
empty while the route is played, and the route's own line whole but for one blow.

**The route each row is proved by is written into `Assets/Game/Tests/PushRoutes.cs`**
(`--draft`/`--write` regenerate it, `--check` refuses drift), because a C# test replaying a
route is cheap and a C# solver would be a second copy of this one.

Route notation: `U D L R` in board terms (`U` is up the rows, `ChallengeInput.Swipe(0, 1)`).
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import time
from collections import deque
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
FILE = REPO / "Assets" / "StreamingAssets" / "Content" / "challenges.json"
ROUTES = REPO / "Assets" / "Game" / "Tests" / "PushRoutes.cs"

LANES = "rgby"                                     # ChallengeColours.Letters
PADS = "RGBY"
LINE = {"damage": 1, "health": 3, "strike": 1}     # challenges.json's line block
DIRS = (("U", 0, -1), ("D", 0, 1), ("L", -1, 0), ("R", 1, 0))
STEP = {d: (dx, dy) for d, dx, dy in DIRS}

# ------------------------------------------------------------------ tiers
#: What each tier asks of a board: `par` a closed band of steps, `pushes` and `switches` the
#: fewest a route may make, `gems` how many a board deals, `slack` the pace a hill is tuned to
#: forgive (hundredths of the route's turns).
TIERS = {
    "medium": {"par": (18, 32), "pushes": 9, "switches": 3, "gems": (2, 3), "slack": 150},
    "hard": {"par": (36, 60), "pushes": 18, "switches": 9, "gems": (3, 4), "slack": 135},
}

#: The band every row's slack must sit in: the glade's and Merge's
#: (`ChallengeTests.SlowestPaceFloor` / `SlowestPaceCeiling`).
SLACK_BAND = (125, 165)

#: The shortest hill a row is tuned to, in steps. A Push route is 18-80 steps, the glade's
#: range, so the glade's floor (a hill shorter than this is a step too big to watch).
LEAST_HILL = 12

#: The longest hill a tuning tries, in multiples of the route's turns. Longer than the glade's
#: .7, because a Push route seats its gems late - most of a board is set-up before the first gem
#: lands - so a hill shorter than the first seat has a raider on the line before any turret can
#: have fired, whatever the waves do.
HILL_REACH = 1.3

#: How long the streams run, in multiples of the route's turns: past anybody still standing at
#: the slowest pace a row forgives, so the hill is never emptied by the waves running out.
STREAM_END = 1.75

#: The route's line may take one blow and no more.
LEAST_LINE = 4 * LINE["health"] - 1

#: How many raiders a tuning wants standing on an average turn, and the most it may have.
CROWD, MOST_CROWD = 3.0, 4.5

#: What a step that only walks costs a composed board's score, against one for a push and two
#: more for a switch: a route that is mostly walking is a long route, not a hard one.
WALK_COST = .4

#: The most states one solve may visit before a board is called too big to measure.
LIMIT = 3_000_000


# ------------------------------------------------------------------ the board
class Board:
    """A parsed row: walls, pads, the keeper and the gems, in cell indices (row-major)."""

    def __init__(self, rows, gems):
        self.h, self.w = len(rows), len(rows[0])
        self.n = self.w * self.h
        self.wall = [False] * self.n
        self.pad = [-1] * self.n
        self.keeper = -1
        start = []
        for y in range(self.h):
            for x in range(self.w):
                i = y * self.w + x
                ch = rows[y][x]
                if ch == "#":
                    self.wall[i] = True
                elif ch == "@":
                    self.keeper = i
                elif ch in PADS:
                    self.pad[i] = PADS.index(ch)
                g = gems[y][x]
                if g != ".":
                    start.append((LANES.index(g), i))
        start.sort()
        self.colours = tuple(c for c, _ in start)
        self.start = tuple(i for _, i in start)
        self.nbr = []
        for i in range(self.n):
            x, y = i % self.w, i // self.w
            per = []
            for d, dx, dy in DIRS:
                nx, ny = x + dx, y + dy
                per.append(ny * self.w + nx if 0 <= nx < self.w and 0 <= ny < self.h else -1)
            self.nbr.append(per)
        self.live = [self._live(c) for c in range(4)]

    def floor(self, i):
        return i >= 0 and not self.wall[i]

    def _live(self, colour):
        """Cells from which a gem of this colour can still be pushed onto one of its pads,
        other gems ignored: a reverse pull from every pad. A push onto any other cell is a
        dead board, so the solver never makes one."""
        seen = {i for i in range(self.n) if self.pad[i] == colour}
        q = deque(seen)
        while q:
            p = q.popleft()
            for d in range(4):
                g = self.nbr[p][d]              # the gem stood here before the push...
                if not self.floor(g):
                    continue
                k = self.nbr[g][d]              # ...and the keeper here, pushing toward p
                opp = (1, 0, 3, 2)[d]
                back = self.nbr[g][opp]
                if back != p:
                    continue
                if not self.floor(k) or g in seen:
                    continue
                seen.add(g)
                q.append(g)
        return seen

    def solved(self, gems):
        return all(self.pad[p] == c for c, p in zip(self.colours, gems))

    def seated(self, gems):
        """How many gems stand on their own pad, per lane."""
        out = [0, 0, 0, 0]
        for c, p in zip(self.colours, gems):
            if self.pad[p] == c:
                out[c] += 1
        return out

    def canon(self, gems):
        """Gems of one colour are interchangeable: sort within a colour so a state has one key."""
        out, i = [], 0
        cs = self.colours
        while i < len(cs):
            j = i
            while j < len(cs) and cs[j] == cs[i]:
                j += 1
            out.extend(sorted(gems[i:j]))
            i = j
        return tuple(out)

    def frozen(self, gems, at):
        """A 2x2 block of wall and gem holding a gem off its own pad can never move again."""
        occupied = set(gems)
        x, y = at % self.w, at // self.w
        for ox in (-1, 0):
            for oy in (-1, 0):
                cells = []
                ok = True
                for dx in (0, 1):
                    for dy in (0, 1):
                        cx, cy = x + ox + dx, y + oy + dy
                        if not (0 <= cx < self.w and 0 <= cy < self.h):
                            cells.append(None)
                            continue
                        c = cy * self.w + cx
                        if self.wall[c]:
                            cells.append(None)
                        elif c in occupied:
                            cells.append(c)
                        else:
                            ok = False
                if not ok:
                    continue
                for c in cells:
                    if c is None:
                        continue
                    k = gems.index(c)
                    if self.pad[c] != self.colours[k]:
                        return True
        return False

    def moves(self, keeper, gems):
        """Every legal step: (direction, keeper after, gems after, pushed gem index or -1)."""
        for d in range(4):
            t = self.nbr[keeper][d]
            if not self.floor(t):
                continue
            if t in gems:
                k = gems.index(t)
                b = self.nbr[t][d]
                if not self.floor(b) or b in gems or b not in self.live[self.colours[k]]:
                    continue
                ng = list(gems)
                ng[k] = b
                ng = tuple(ng)
                if self.frozen(ng, b):
                    continue
                yield d, t, self.canon(ng), k
            else:
                yield d, t, gems, -1


def solve(board, limit=LIMIT):
    """The step-shortest route. -> (route string, states) with route None past `limit` or when
    no route exists (states is then -1 for 'no route')."""
    start = (board.keeper, board.canon(board.start))
    prev = {start: None}
    q = deque([start])
    while q:
        st = q.popleft()
        k, gems = st
        if board.solved(gems):
            path = []
            while prev[st] is not None:
                st, d = prev[st]
                path.append(DIRS[d][0])
            return "".join(reversed(path)), len(prev)
        for d, nk, ng, _ in board.moves(k, gems):
            ns = (nk, ng)
            if ns in prev:
                continue
            prev[ns] = (st, d)
            q.append(ns)
            if len(prev) > limit:
                return None, len(prev)
    return None, -1


def replay(board, route):
    """The route played through the rules, step by step.
    -> list of (seated per lane after the step, solved, pushed gem colour or -1)."""
    k, gems = board.keeper, list(board.start)
    out = []
    for s in route:
        d = "UDLR".index(s)
        t = board.nbr[k][d]
        if not board.floor(t):
            raise ValueError("the route walks into a wall")
        pushed = -1
        if t in gems:
            i = gems.index(t)
            b = board.nbr[t][d]
            if not board.floor(b) or b in gems:
                raise ValueError("the route pushes a gem into something")
            gems[i] = b
            pushed = board.colours[i]
        k = t
        g = tuple(gems)
        out.append((board.seated(g), board.solved(g), pushed))
    return out


def route_shape(board, route):
    """Pushes, switches (how often the pushed gem changes), and the fewest pushes any gem got."""
    k, gems = board.keeper, list(board.start)
    pushes = switches = 0
    last = None
    per = [0] * len(gems)
    for s in route:
        d = "UDLR".index(s)
        t = board.nbr[k][d]
        if t in gems:
            i = gems.index(t)
            gems[i] = board.nbr[t][d]
            pushes += 1
            per[i] += 1
            if last is not None and last != i:
                switches += 1
            last = i
        k = t
    return pushes, switches, min(per) if per else 0


def seats(board, route):
    """The step (1-based) on which each lane first has a gem seated."""
    first = {}
    for n, (seated, _, _) in enumerate(replay(board, route), 1):
        for lane in range(4):
            if seated[lane] and lane not in first:
                first[lane] = n
    return first


# ------------------------------------------------------------------ composing a board
class Rng:
    """xorshift32, so a seed draws the same board on every machine and every Python."""

    def __init__(self, seed):
        self.s = (seed * 2654435761 + 1) & 0xFFFFFFFF or 1

    def next(self):
        s = self.s
        s ^= (s << 13) & 0xFFFFFFFF
        s ^= s >> 17
        s ^= (s << 5) & 0xFFFFFFFF
        self.s = s & 0xFFFFFFFF
        return self.s

    def below(self, n):
        return self.next() % n


def room_of(w, h, runs, rng):
    """Walls as `runs` short runs of one to three cells, then the floor trimmed to its largest
    room. -> the wall mask, or None when the room is under two thirds of the board."""
    n = w * h
    wall = [False] * n
    for _ in range(runs):
        x, y = rng.below(w), rng.below(h)
        horizontal = rng.below(2) == 0
        length = 1 + rng.below(3)
        for s in range(length):
            cx, cy = (x + s, y) if horizontal else (x, y + s)
            if 0 <= cx < w and 0 <= cy < h:
                wall[cy * w + cx] = True
    best, seen = [], set()
    for i in range(n):
        if wall[i] or i in seen:
            continue
        comp, q = [i], deque([i])
        seen.add(i)
        while q:
            p = q.popleft()
            x, y = p % w, p // w
            for _, dx, dy in DIRS:
                nx, ny = x + dx, y + dy
                m = ny * w + nx
                if 0 <= nx < w and 0 <= ny < h and not wall[m] and m not in seen:
                    seen.add(m)
                    comp.append(m)
                    q.append(m)
        if len(comp) > len(best):
            best = comp
    room = set(best)
    if len(room) < n * 2 // 3:
        return None
    return [i not in room for i in range(n)]


def compose(w, h, ngems, runs, cap, seed, limit=2_500_000):
    """A board from its seed. -> (rows, gems) or None when the seed draws nothing fit.

    The room is `room_of`; the pads go on floor cells with at least two open sides, in
    `ngems` colours drawn from the four. Then **a breadth-first search runs backwards from
    the solved board** - every keeper cell around the seated gems at distance nought, and a
    keeper stepping away, pulling the gem behind it or not, one step further - so the
    distance of every state it reaches is *exactly* the step-shortest route home from it.
    The state dealt is the one whose route home is the most *puzzle* - pushes, twice as
    much for every switch from one gem to another, less for every step that only walks
    (`WALK_COST`) - among those between half of `cap` and `cap` steps out, with the keeper off
    every pad and no gem on its own; ties go to the farther state, then to the first found.
    The pushes and switches are carried along the search itself, one state to the next, so
    judging a million states costs nothing more than finding them. That is the best start
    this room holds for its tier, by construction rather than by luck."""
    rng = Rng(seed)
    wall = room_of(w, h, runs, rng)
    if wall is None:
        return None
    n = w * h
    nbr = []
    for i in range(n):
        x, y = i % w, i // w
        per = []
        for _, dx, dy in DIRS:
            nx, ny = x + dx, y + dy
            j = ny * w + nx
            per.append(j if 0 <= nx < w and 0 <= ny < h and not wall[j] else -1)
        nbr.append(per)
    open_cells = [i for i in range(n) if not wall[i] and sum(1 for j in nbr[i] if j >= 0) >= 2]
    if len(open_cells) < ngems + 3:
        return None

    lanes = [0, 1, 2, 3]
    for i in range(3, 0, -1):
        j = rng.below(i + 1)
        lanes[i], lanes[j] = lanes[j], lanes[i]
    colours = sorted(lanes[:ngems])
    pool = list(open_cells)
    pads = [pool.pop(rng.below(len(pool))) for _ in colours]
    pad = [-1] * n
    for c, p in zip(colours, pads):
        pad[p] = c

    # States are packed ints: the keeper in the low six bits, gem g in the six above g's.
    def pack(k, gems):
        key = k
        for g, p in enumerate(gems):
            key |= p << (6 * (g + 1))
        return key

    def unpack(key):
        return key & 63, tuple((key >> (6 * (g + 1))) & 63 for g in range(ngems))

    solved = tuple(pads)
    # parent[state] = (state it was reached from, forward direction, pushes, switches, last gem)
    parent = {}
    frontier = []
    for k in range(n):
        if not wall[k] and k not in solved:
            key = pack(k, solved)
            parent[key] = (None, -1, 0, 0, -1)
            frontier.append(key)
    depth = 0
    floor = cap // 2
    best, best_score = None, None
    opp = (1, 0, 3, 2)
    while frontier and depth < cap:
        nxt = []
        for key in frontier:
            k, gems = unpack(key)
            _, _, pushes, switches, last = parent[key]
            for d in range(4):
                t = nbr[k][d]
                if t < 0 or t in gems:
                    continue
                nk = pack(t, gems)
                if nk not in parent:
                    parent[nk] = (key, opp[d], pushes, switches, last)
                    nxt.append(nk)
                b = nbr[k][opp[d]]
                if b >= 0 and b in gems:
                    g = gems.index(b)
                    ng = list(gems)
                    ng[g] = k
                    pk = pack(t, ng)
                    if pk not in parent:
                        parent[pk] = (key, opp[d], pushes + 1, switches + (last >= 0 and last != g), g)
                        nxt.append(pk)
            if len(parent) > limit:
                return None
        depth += 1
        if depth >= floor:
            for key in nxt:
                k, gems = unpack(key)
                if pad[k] >= 0 or any(pad[g] == c for g, c in zip(gems, colours)):
                    continue
                _, _, pushes, switches, _ = parent[key]
                score = (pushes + 2 * switches - WALK_COST * (depth - pushes), depth)
                if best_score is None or score > best_score:
                    best, best_score = key, score
        frontier = nxt
    if best is None:
        return None

    keeper, gems = unpack(best)
    rows, grows = [], []
    for y in range(h):
        r, g = [], []
        for x in range(w):
            i = y * w + x
            r.append("#" if wall[i] else "@" if i == keeper else PADS[pad[i]] if pad[i] >= 0 else ".")
            g.append(LANES[colours[gems.index(i)]] if i in gems else ".")
        rows.append("".join(r))
        grows.append("".join(g))
    return rows, grows


def shape_faults(rows, gems):
    """What makes a board ugly before anything is solved."""
    out = []
    h, w = len(rows), len(rows[0])
    for y in range(h):
        if all(c == "#" for c in rows[y]):
            out.append("row %d is all wall" % y)
    for x in range(w):
        if all(rows[y][x] == "#" for y in range(h)):
            out.append("column %d is all wall" % x)
    for y in range(h):
        for x in range(w):
            g = gems[y][x]
            if g != "." and rows[y][x] == g.upper():
                out.append("a %s gem is dealt on its own pad" % g)
    return out


def measure(rows, gems, tier, limit=LIMIT):
    """Everything a row's board is judged on. -> dict, with `faults` empty when it ships."""
    reading = {"faults": shape_faults(rows, gems)}
    board = Board(rows, gems)
    route, states = solve(board, limit)
    reading["states"] = states
    if route is None:
        reading["faults"].append("no route" if states == -1 else "unsolved past %d states" % states)
        return reading
    pushes, switches, least = route_shape(board, route)
    spec = TIERS[tier]
    reading.update(route=route, par=len(route), pushes=pushes, switches=switches, least=least,
                   gems=len(board.colours))
    lo, hi = spec["par"]
    if not lo <= len(route) <= hi:
        reading["faults"].append("par %d outside %d-%d" % (len(route), lo, hi))
    if pushes < spec["pushes"]:
        reading["faults"].append("%d pushes, under %d" % (pushes, spec["pushes"]))
    if switches < spec["switches"]:
        reading["faults"].append("%d switches, under %d" % (switches, spec["switches"]))
    if least < 2:
        reading["faults"].append("a gem is pushed %d time(s)" % least)
    glo, ghi = spec["gems"]
    if not glo <= len(board.colours) <= ghi:
        reading["faults"].append("%d gems outside %d-%d" % (len(board.colours), glo, ghi))
    return reading


# ------------------------------------------------------------------ the hill
class Hill:
    """`ChallengeHill`, line for line: fire, step, strike, muster. A seated gem feeds a
    *volley* (`ChallengeHill.Volley`), which fires this turn or is spent."""

    def __init__(self, length, waves):
        self.length = max(1, length)
        self.waves = waves
        self.wards = [[LINE["health"], 0] for _ in range(4)]
        self.volley = [0, 0, 0, 0]
        self.raiders = []
        self.turn = 0
        self.muster(0)

    def standing(self):
        return any(w[0] > 0 for w in self.wards)

    def present(self):
        return sum(1 for r in self.raiders if r[1] > 0)

    def stream(self, lane, bolts):
        if self.wards[lane][0] > 0 and bolts > 0:
            self.volley[lane] += bolts

    def resolve(self):
        self.turn += 1
        for lane, ward in enumerate(self.wards):
            volley, self.volley[lane] = self.volley[lane], 0
            if ward[0] <= 0:
                continue
            while volley > 0 or ward[1] > 0:
                target = None
                for r in self.raiders:
                    if r[1] > 0 and r[0] == lane and (target is None or r[2] < target[2]):
                        target = r
                if target is None:
                    break
                if volley > 0:
                    volley -= 1
                else:
                    ward[1] -= 1
                target[1] = max(0, target[1] - LINE["damage"])
        for r in self.raiders:
            if r[1] > 0 and r[2] > 0:
                r[2] -= 1
        for r in self.raiders:
            if r[1] <= 0 or r[2] > 0:
                continue
            at = self.target(r[0])
            if at < 0:
                break
            ward = self.wards[at]
            ward[0] = max(0, ward[0] - LINE["strike"])
            if ward[0] <= 0:
                ward[1] = 0
        self.muster(self.turn)

    def target(self, lane):
        if self.wards[lane][0] > 0:
            return lane
        best, gap = -1, 99
        for i, w in enumerate(self.wards):
            if w[0] > 0 and abs(i - lane) < gap:
                best, gap = i, abs(i - lane)
        return best

    def muster(self, turn):
        for t, raiders in self.waves:
            if t == turn:
                for lane, health in raiders:
                    self.raiders.append([lane, health, self.length])


def read_waves(texts):
    out = []
    for text in texts:
        parts = text.split()
        out.append((int(parts[0]), [(LANES.index(t[0]), int(t[1:])) for t in parts[1:]]))
    return out


def wave_strings(waves):
    return ["%d " % t + " ".join("%s%d" % (LANES[lane], hp) for lane, hp in raiders) for t, raiders in waves]


def run(schedule, hill_length, waves, bolts, pace=100):
    """`ChallengeTests.HoldsAt`, line for line: the route at `pace` hundredths of its speed
    against a fresh hill - the player's k-th turn stands where the route stood after turn
    k*100/pace, every seated gem streams, and the solving turn never walks.
    -> (won, health left, empty turns, raiders summed over walked turns, turns walked)."""
    hill = Hill(hill_length, waves)
    total = len(schedule)
    finish = max(total, (total * pace + 99) // 100)
    empty = crowd = walked = 0
    for k in range(1, finish):
        progress = min(total - 1, k * 100 // pace)
        if progress >= 1:
            seated = schedule[progress - 1][0]
            for lane in range(4):
                hill.stream(lane, seated[lane] * bolts)
        on = hill.present()
        empty += on == 0
        crowd += on
        walked += 1
        hill.resolve()
        if not hill.standing():
            return False, 0, empty, crowd, walked
    return True, sum(w[0] for w in hill.wards), empty, crowd, walked


def slack_of(schedule, hill_length, waves, bolts):
    best = 100
    for p in range(105, 501, 5):
        if not run(schedule, hill_length, waves, bolts, p)[0]:
            break
        best = p
    return best


def streams(order, first, hill_length, period, health, lead, end):
    """**Every colour is a stream, opened in the order the route seats them** - the glade's
    shape (`make_glade_challenges.streams`). The first lane sends its first raider before the
    first step; every other opens so its first raider reaches the line `lead` times the
    route's step for seating it; each then sends a raider every `period` turns to the end. So
    the hill is never empty, a seated gem always has somebody walking into its fire, and the
    colour still off its pad is the one marching. -> [(turn, [(lane, health)])]"""
    at, opened = {}, -1
    for n, lane in enumerate(order):
        t = 0 if n == 0 else max(opened + 1, int(round(first[lane] * lead)) - hill_length)
        opened = t
        while t <= end:
            at.setdefault(t, []).append((lane, health))
            t += period
    return sorted(at.items())


def tune(schedule, first, tier, target=None):
    """The hill and the waves that put a row's slack nearest its tier's target, with the hill
    never empty and the route's line whole but for one blow. Among tunings inside a notch of
    the target, the hill nearest `CROWD` standing on an average turn wins, then the sturdier
    raider, then the hill nearest half the route. -> (fields, reading) or None."""
    target = target or TIERS[tier]["slack"]
    total = len(schedule)
    order = sorted(first, key=lambda lane: (first[lane], lane))
    end = int(total * STREAM_END)
    best = None
    for hill_length in range(max(LEAST_HILL, int(total * .35)), max(LEAST_HILL, int(total * HILL_REACH)) + 1):
        for bolts in (1, 2):
            for period in (4, 5, 6, 8, 10, 12, 15):
                for health in (3, 2):
                    for lead in (1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.8):
                        waves = streams(order, first, hill_length, period, health, lead, end)
                        won, left, empty, crowd, walked = run(schedule, hill_length, waves, bolts)
                        if not won or left < LEAST_LINE or empty:
                            continue
                        got = slack_of(schedule, hill_length, waves, bolts)
                        if abs(got - target) > 10 or not SLACK_BAND[0] <= got <= SLACK_BAND[1]:
                            continue
                        density = crowd / max(1, walked)
                        if density > MOST_CROWD:
                            continue
                        score = (abs(got - target) // 6, round(abs(density - CROWD), 1), -health,
                                 abs(hill_length - total * .5))
                        if best is None or score < best[0]:
                            best = (score, {"hill": hill_length, "bolts": bolts, "waves": wave_strings(waves)},
                                    {"slack": got, "target": target, "density": density, "turns": total,
                                     "order": "".join(LANES[lane] for lane in order), "period": period,
                                     "health": health, "lead": lead, "left": left})
    return None if best is None else (best[1], best[2])


# ------------------------------------------------------------------ the drafts
#: Every push row: (tier, width, height, gems, wall runs, cap, seed) - `cap` the longest route
#: the reverse search may deal. The seed was found by
#: `--search` and composes the board (`compose`); `--check` proves the file's rows are what
#: these draw. **No outer ring of wall**: the board's edge already is one, and a ring cost the
#: first cut two columns and two rows of a band the owner called too small to see (2026-09-27).
DRAFTS = {
    # id             tier      w  h  gems runs cap  seed
    "d06_sokoban":  ("medium", 6, 4, 2, 3, 32,  740),
    "d99_sokoban":  ("medium", 6, 4, 2, 3, 32,  247),
    "d100_sokoban": ("medium", 6, 4, 2, 3, 32,  732),
    "d101_sokoban": ("medium", 6, 5, 2, 4, 32,  823),
    "d102_sokoban": ("medium", 6, 5, 2, 4, 32,  668),
    "d103_sokoban": ("medium", 6, 5, 2, 4, 32, 1321),
    "d104_sokoban": ("medium", 6, 5, 2, 4, 32,  983),
    "d105_sokoban": ("medium", 6, 5, 3, 5, 32,  865),
    "d106_sokoban": ("medium", 6, 5, 3, 5, 32,  940),
    "d107_sokoban": ("medium", 6, 5, 3, 5, 32,  546),
    "d108_sokoban": ("medium", 6, 5, 3, 5, 32, 1195),
    "d109_sokoban": ("medium", 6, 5, 3, 5, 32, 1245),
    "d110_sokoban": ("medium", 6, 5, 3, 5, 32,   94),
    "d111_sokoban": ("medium", 6, 5, 3, 5, 32,  579),
    "d112_sokoban": ("hard",  7, 5, 3, 6, 60,  709),
    "d113_sokoban": ("hard",  7, 5, 3, 6, 60, 1050),
    "d114_sokoban": ("hard",  7, 5, 3, 6, 60,  645),
    "d115_sokoban": ("hard",  7, 5, 3, 6, 60,  949),
    "d116_sokoban": ("hard",  7, 5, 4, 7, 60,  914),
    "d117_sokoban": ("hard",  7, 6, 4, 9, 60,  424),
    "d118_sokoban": ("hard",  7, 6, 4, 9, 60,   22),
    "d119_sokoban": ("hard",  7, 5, 4, 7, 60,  140),
    "d120_sokoban": ("hard",  6, 5, 4, 5, 60,   95),
    "d121_sokoban": ("hard",  7, 5, 4, 7, 60,  398),
    "d122_sokoban": ("hard",  7, 5, 4, 7, 60,  971),
    "d123_sokoban": ("hard",  6, 5, 4, 5, 60,  821),
    "d124_sokoban": ("hard",  6, 5, 4, 5, 60,  543),
    "d125_sokoban": ("hard",  7, 6, 4, 9, 60,  594),
    "d126_sokoban": ("hard",  7, 5, 4, 7, 60,  943),
    "d127_sokoban": ("hard",  6, 5, 4, 5, 60,  630),
}


# ------------------------------------------------------------------ the file
def load():
    raw = open(FILE, "rb").read()
    crlf = b"\r\n" in raw
    return raw.decode("utf-8").replace("\r\n", "\n"), crlf


def save(text, crlf):
    out = text.replace("\n", "\r\n") if crlf else text
    open(FILE, "wb").write(out.encode("utf-8"))


def push_rows():
    return [r for r in json.load(open(FILE, encoding="utf-8"))["challenges"] if r["genre"] == "sokoban"]


def render_row(row):
    def block(name, items, last=False):
        out = ['      "%s": [' % name]
        out += ["        %s%s" % (json.dumps(v), "," if i < len(items) - 1 else "") for i, v in enumerate(items)]
        out.append("      ]" + ("" if last else ","))
        return out

    lines = ["    {",
             '      "id": %s,' % json.dumps(row["id"]),
             '      "genre": "sokoban",',
             '      "seed": %d,' % row["seed"],
             '      "width": %d,' % row["width"],
             '      "height": %d,' % row["height"]]
    lines += block("rows", row["rows"])
    lines += block("gems", row["gems"])
    lines += ['      "hill": %d,' % row["hill"],
              '      "bolts": %d,' % row["bolts"]]
    lines += block("waves", row["waves"], last=True)
    lines.append("    }")
    return "\n".join(lines)


def place(text, row):
    """Replace a row's whole block, or insert it after the last push row, leaving every other
    byte of the hand-laid file as it was."""
    block = render_row(row)
    m = re.search(r'\n    \{\n      "id": %s,.*?\n    \}' % re.escape(json.dumps(row["id"])), text, re.S)
    if m:
        return text[:m.start()] + "\n" + block + text[m.end():]
    last = None
    for m in re.finditer(r'\n    \{\n      "id": "[^"]+",\n      "genre": "sokoban",.*?\n    \}', text, re.S):
        last = m
    if last is None:
        raise ValueError("challenges.json has no push row to place a new one after")
    return text[:last.end()] + ",\n" + block + text[last.end():]


def tier_of(row):
    return DRAFTS[row["id"]][0] if row["id"] in DRAFTS else "medium"


def schedule_of(board, route):
    return [(seated, won) for seated, won, _ in replay(board, route)]


def report(row):
    """A row's full reading. -> (reading, faults)."""
    tier = tier_of(row)
    reading = measure(row["rows"], row["gems"], tier)
    faults = list(reading["faults"])
    if "route" not in reading:
        return reading, faults
    board = Board(row["rows"], row["gems"])
    schedule = schedule_of(board, reading["route"])
    waves = read_waves(row["waves"])
    won, left, empty, crowd, walked = run(schedule, row["hill"], waves, row["bolts"])
    got = slack_of(schedule, row["hill"], waves, row["bolts"])
    reading.update(won=won, left=left, empty=empty, density=crowd / max(1, walked), slack=got,
                   seats=seats(board, reading["route"]))
    if not won:
        faults.append("the route loses")
    elif left < LEAST_LINE:
        faults.append("the route's line takes %d blows" % (4 * LINE["health"] - left))
    if empty:
        faults.append("the hill stands empty on %d of the route's turns" % empty)
    if reading["density"] > MOST_CROWD:
        faults.append("%.1f raiders on the hill on an average turn, over %.1f" % (reading["density"], MOST_CROWD))
    if not SLACK_BAND[0] <= got <= SLACK_BAND[1]:
        faults.append("slack %.2fx outside %.2f-%.2fx" % (got / 100, SLACK_BAND[0] / 100, SLACK_BAND[1] / 100))
    sent = {lane for _, raiders in waves for lane, _ in raiders}
    held = set(board.colours)
    if not sent <= held:
        faults.append("the waves send %s, which no gem can answer" % "".join(LANES[l] for l in sorted(sent - held)))
    if row["id"] in DRAFTS:
        _, w, h, ngems, runs, cap, seed = DRAFTS[row["id"]]
        drawn = compose(w, h, ngems, runs, cap, seed)
        if drawn is None or drawn[0] != row["rows"] or drawn[1] != row["gems"] or seed != row["seed"]:
            faults.append("the board is not what its draft composes")
    return reading, faults


def print_report(row, reading, faults):
    if "par" not in reading:
        print("%-12s %s" % (row["id"], "; ".join(faults)))
        return
    order = "".join(LANES[l] for l, _ in sorted(reading.get("seats", {}).items(), key=lambda kv: kv[1]))
    print("%-12s %-6s %dx%d  %d gems  par %2d  pushes %2d  switches %2d  hill %2d  bolts %d  seats %-4s  "
          "slack %.2fx  %.1f on the hill  line %d  (%d states)"
          % (row["id"], tier_of(row), row["width"], row["height"], reading["gems"], reading["par"],
             reading["pushes"], reading["switches"], row["hill"], row["bolts"], order,
             reading.get("slack", 0) / 100, reading.get("density", 0), reading.get("left", 0), reading["states"]))
    print("             route %s" % reading["route"])
    for f in faults:
        print("             FAULT %s" % f)


def routes_source(rows):
    """`PushRoutes.cs`: every row's route, for `ChallengeTests` to replay against the C#."""
    lines = [
        "// Generated by Tools/make_push_challenges.py (--draft / --write). Do not edit;",
        "// `--check` refuses a file that is not what the tool writes.",
        "using System.Collections.Generic;",
        "",
        "namespace GlimmerGrove.Tests",
        "{",
        "    /// <summary>",
        "    /// The step-shortest route through every shipped Push row, found by the tool's",
        "    /// breadth-first search over keeper steps, as <c>U D L R</c> in board terms (<c>U</c>",
        "    /// is up the rows). <see cref=\"ChallengeTests\"/> replays each against",
        "    /// <c>SokobanPuzzle</c> and paces it against the hill.",
        "    /// </summary>",
        "    static class PushRoutes",
        "    {",
        "        public static readonly Dictionary<string, string> Shortest = new Dictionary<string, string>",
        "        {",
    ]
    for rid, route in rows:
        lines.append('            ["%s"] = "%s",' % (rid, route))
    lines += ["        };", "    }", "}", ""]
    return "\n".join(lines)


def write_routes(readings):
    text = routes_source([(rid, r["route"]) for rid, r in readings])
    open(ROUTES, "w", encoding="utf-8", newline="\r\n").write(text)


def routes_on_disk():
    if not ROUTES.exists():
        return None
    return dict(re.findall(r'\["([^"]+)"\] = "([^"]*)"', ROUTES.read_text(encoding="utf-8")))


# ------------------------------------------------------------------ the commands
def cmd_report(ids):
    bad = 0
    for row in push_rows():
        if ids and row["id"] not in ids:
            continue
        reading, faults = report(row)
        print_report(row, reading, faults)
        bad += bool(faults)
    return 1 if bad else 0


def refresh(ids, compose_drafts):
    text, crlf = load()
    if compose_drafts:
        for rid, (tier, w, h, ngems, runs, cap, seed) in DRAFTS.items():
            if ids and rid not in ids:
                continue
            drawn = compose(w, h, ngems, runs, cap, seed)
            if drawn is None:
                print("%s: its seed composes nothing" % rid)
                return 1
            row = {"id": rid, "seed": seed, "width": w, "height": h, "rows": drawn[0], "gems": drawn[1],
                   "hill": LEAST_HILL, "bolts": 1, "waves": ["0 %s1" % next(c for c in "".join(drawn[1]) if c != ".")]}
            text = place(text, row)
        save(text, crlf)
    for row in push_rows():
        if ids and row["id"] not in ids:
            continue
        tier = tier_of(row)
        board = Board(row["rows"], row["gems"])
        route, _ = solve(board)
        if route is None:
            print("%s: no route" % row["id"])
            return 1
        got = tune(schedule_of(board, route), seats(board, route), tier)
        if not got:
            print("%s: nothing in the search space hits its target" % row["id"], flush=True)
            return 1
        fields, reading = got
        row.update(fields)
        text = place(text, row)
        print("%s: hill %d, %d bolt(s), a raider of %d every %d turns per lane in order %s, lead %.1f, "
              "%.1f on the hill, slack %.2fx (target %.2fx) over %d turns"
              % (row["id"], fields["hill"], fields["bolts"], reading["health"], reading["period"], reading["order"],
                 reading["lead"], reading["density"], reading["slack"] / 100, reading["target"] / 100,
                 reading["turns"]), flush=True)
    save(text, crlf)
    return verify()


def verify():
    readings, failed = [], []
    for row in push_rows():
        reading, faults = report(row)
        if faults:
            failed.append(row["id"])
            print_report(row, reading, faults)
        if "route" in reading:
            readings.append((row["id"], reading))
    write_routes(readings)
    if failed:
        print("written, and these read back with a fault: " + ", ".join(failed))
        return 1
    print("%d push row(s) written and proved; routes in %s" % (len(readings), ROUTES.relative_to(REPO)))
    return 0


def cmd_check():
    rows = push_rows()
    ids = {r["id"] for r in rows}
    faults = ["%s is drafted and not in the file" % rid for rid in DRAFTS if rid not in ids]
    faults += ["%s is in the file and not drafted" % rid for rid in sorted(ids) if rid not in DRAFTS]
    wanted = {}
    for row in rows:
        reading, got = report(row)
        faults += ["%s: %s" % (row["id"], f) for f in got]
        if "route" in reading:
            wanted[row["id"]] = reading["route"]
    if routes_on_disk() != wanted:
        faults.append("%s is not what the tool writes (run --write)" % ROUTES.relative_to(REPO))
    for f in faults:
        print("FAULT " + f)
    print("push: %d row(s), %d fault(s)" % (len(rows), len(faults)))
    return 1 if faults else 0


def search_one(args):
    tier, w, h, ngems, runs, cap, seed = args
    drawn = compose(w, h, ngems, runs, cap, seed)
    if drawn is None:
        return None
    rows, gems = drawn
    if shape_faults(rows, gems):
        return None
    reading = measure(rows, gems, tier, limit=600_000)
    if reading["faults"]:
        return None
    return seed, reading["par"], reading["pushes"], reading["switches"], reading["states"], rows, gems, reading["route"]


def cmd_search(tier, size, ngems, runs, cap, start, count, jobs):
    from multiprocessing import Pool
    w, h = (int(v) for v in size.split("x"))
    tasks = [(tier, w, h, ngems, runs, cap or TIERS[tier]["par"][1], s) for s in range(start, start + count)]
    t0 = time.time()
    with Pool(jobs) as pool:
        for got in pool.imap_unordered(search_one, tasks, chunksize=4):
            if got:
                seed, par, pushes, switches, states, rows, gems, route = got
                print("seed %6d  par %2d  pushes %2d  switches %2d  states %7d  %s  | %s  | %s"
                      % (seed, par, pushes, switches, states, " ".join(rows), " ".join(gems), route), flush=True)
    print("searched %d seed(s) in %.0fs" % (count, time.time() - t0), flush=True)
    return 0


def cmd_seats(ids):
    for row in push_rows():
        if ids and row["id"] not in ids:
            continue
        board = Board(row["rows"], row["gems"])
        route, _ = solve(board)
        print("%s: %s (%d steps)" % (row["id"], route, len(route or "")))
        for lane, step in sorted(seats(board, route).items(), key=lambda kv: kv[1]):
            print("   %s seats on step %d" % (LANES[lane], step))
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--draft", action="store_true")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--seats", action="store_true")
    ap.add_argument("--id", action="append")
    ap.add_argument("--search", choices=sorted(TIERS))
    ap.add_argument("--size", default="6x5")
    ap.add_argument("--gems", type=int, default=3)
    ap.add_argument("--runs", type=int, default=5, help="wall runs a searched board drops")
    ap.add_argument("--cap", type=int, default=0, help="the longest route a searched board may deal (default: the tier's par ceiling)")
    ap.add_argument("--from", dest="start", type=int, default=1)
    ap.add_argument("--count", type=int, default=2000)
    ap.add_argument("--jobs", type=int, default=12)
    a = ap.parse_args()
    ids = set(a.id or [])
    if a.search:
        return cmd_search(a.search, a.size, a.gems, a.runs, a.cap, a.start, a.count, a.jobs)
    if a.draft:
        return refresh(ids, True)
    if a.write:
        return refresh(ids, False)
    if a.check:
        return cmd_check()
    if a.seats:
        return cmd_seats(ids)
    return cmd_report(ids)


if __name__ == "__main__":
    sys.exit(main())
