#!/usr/bin/env python3
"""Designs, proves, tunes and prints the Merge genre of the daily challenges.

    python Tools/make_merge_challenges.py --report [--id d70_merge]   # every merge row, and its faults
    python Tools/make_merge_challenges.py --search hard --size 6x5 --gems 11112248 --rocks 7 --from 1 --count 4000
    python Tools/make_merge_challenges.py --draft [--id d70_merge]    # compose DRAFTS rows, tune, place in the file
    python Tools/make_merge_challenges.py --write [--id d70_merge]    # re-tune every merge row's hill and waves
    python Tools/make_merge_challenges.py --check                     # prove the file, the drafts and the routes

**The rules it mirrors are `MergePuzzle.cs`** (invariant 56n): a dragged gem slides until an
edge, a rock (`#`) or a gem stops it, and becomes the next rank when what stopped it is a gem of
its own rank. Nothing is dealt. The C# is the authority whenever the two disagree, and
`ChallengeTests` replays every row's route against it.

**Every shipped board joins every gem into one.** Its gems' mass (a rank `r` gem is worth
`2^(r-1)`) is exactly the target's, so the merges are fixed at `gems - 1` and par is those plus
the *quiet* slides a board forces, which is where the whole puzzle is. That is also what makes
the solver fast: a merge costs nothing against the bound (`gems - 1` merges remain, whatever
order), so a 0-1 breadth-first search over quiet slides finds par exactly, in thousands of
states where a plain breadth-first search over moves spent hundreds of thousands.

**What a row is held to, as counts** (invariant 5d asked of a puzzle), per tier (`TIERS`):
par in the tier's band; enough quiet slides that the plan is the puzzle; and **openings** —
how many first moves keep par, against how many are legal — at most the tier's figure, so the
first move is a decision rather than a guess. And the hill, as the glade tool holds it: the
*slack* (the slowest pace, in hundredths of the route's turns, that still wins) near the tier's
target and inside `SLACK_BAND`, the hill never empty while the route is played, and the route's
own line whole but for one blow.

**A board is composed from its seed** (`DRAFTS`, `compose`), so the table says what the file
draws and `--check` proves it; `--search` is how a seed was found. The route each row is proved
by is written into `Assets/Game/Tests/MergeRoutes.cs` (`--draft`/`--write` regenerate it,
`--check` refuses drift), because a C# test replaying a route is cheap and a C# solver is a
second copy of this one.

Route notation: cell index then direction in board terms (`D` is down the rows), e.g. `7U`.
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
ROUTES = REPO / "Assets" / "Game" / "Tests" / "MergeRoutes.cs"

ROCK = -1
DIRS = ((0, -1, "U"), (0, 1, "D"), (-1, 0, "L"), (1, 0, "R"))
LANES = "rgby"                                     # ChallengeColours.Letters
LINE = {"damage": 1, "health": 3, "strike": 1}     # challenges.json's line block

# ------------------------------------------------------------------ tiers
#: What each tier asks of a board. `par` is a closed band, `quiet` the fewest set-up slides,
#: `keep` the most openings that keep par, `rocks` the largest share of cells that are stone.
TIERS = {
    "medium": {"par": (9, 12), "quiet": 3, "keep": 3, "rocks": .34, "slack": 150},
    "hard": {"par": (13, 19), "quiet": 5, "keep": 2, "rocks": .30, "slack": 135},
}

#: The band every row's slack must sit in, and the notch a tuning may miss its target by. The
#: glade's band (`ChallengeTests.SlowestPaceFloor`/`Ceiling`), held to the same numbers.
SLACK_BAND = (125, 165)

#: The shortest hill a row is tuned to, in steps. Shorter than the glade's 16 because a Merge
#: route is 9-19 moves where a glade's is fifty: a hill longer than the route makes every raider
#: muster on the first turn, eight at once and nobody after, which is a crowd and not a hill.
LEAST_HILL = 9

#: The route's line may take one blow and no more.
LEAST_LINE = 4 * LINE["health"] - 1

#: How many raiders a tuning wants standing on an average turn, and the most it may have: the
#: glade's ~3 and Pairs' ceiling (`ChallengeTests.PairsMostCrowd`). A hill of eight reads as a
#: crowd nobody can follow.
CROWD, MOST_CROWD = 3.0, 4.5


# ------------------------------------------------------------------ the rules
def parse(rows):
    board = []
    for row in rows:
        for ch in row:
            board.append(ROCK if ch == "#" else 0 if ch == "." else int(ch))
    return tuple(board), len(rows[0]), len(rows)


def rows_of(b, w, h):
    return ["".join("#" if v == ROCK else "." if v == 0 else str(v) for v in b[y * w:(y + 1) * w])
            for y in range(h)]


def rays_of(w, h):
    rays = []
    for c in range(w * h):
        x, y = c % w, c // w
        per = []
        for dx, dy, _ in DIRS:
            ray, nx, ny = [], x + dx, y + dy
            while 0 <= nx < w and 0 <= ny < h:
                ray.append(ny * w + nx)
                nx, ny = nx + dx, ny + dy
            per.append(ray)
        rays.append(per)
    return rays


def moves(b, rays):
    """Every legal slide: ((cell, dir), board after, rank made or 0). `MergePuzzle.Where`."""
    for c, r in enumerate(b):
        if r <= 0:
            continue
        for d, ray in enumerate(rays[c]):
            last, hit = c, None
            for n in ray:
                v = b[n]
                if v == 0:
                    last = n
                    continue
                if v == r:
                    hit = n
                break
            if hit is not None:
                nb = list(b)
                nb[c], nb[hit] = 0, r + 1
                yield (c, d), tuple(nb), r + 1
            elif last != c:
                nb = list(b)
                nb[c], nb[last] = 0, r
                yield (c, d), tuple(nb), 0


def mass(b):
    return sum(1 << (r - 1) for r in b if r > 0)


def gems(b):
    return sum(1 for r in b if r > 0)


def solve(b, w, h, target, limit=4_000_000, route=True, rays=None):
    """Fewest quiet slides to a gem of `target`: 0-1 breadth-first search, a merge free.
    -> (quiet, route, states); quiet is -1 when no route exists and None past `limit`."""
    rays = rays or rays_of(w, h)
    start = tuple(b)
    dist, prev = {start: 0}, {start: None}
    dq = deque([start])
    while dq:
        s = dq.popleft()
        q = dist[s]
        if max(s) >= target:
            path = []
            if route:
                t = s
                while prev[t]:
                    t, m = prev[t]
                    path.append(m)
                path.reverse()
            return q, path, len(dist)
        for m, n, made in moves(s, rays):
            nq = q if made else q + 1
            old = dist.get(n)
            if old is None or nq < old:
                dist[n] = nq
                prev[n] = (s, m)
                if made:
                    dq.appendleft(n)
                else:
                    dq.append(n)
                if len(dist) > limit:
                    return None, None, len(dist)
    return -1, None, len(dist)


def openings(b, w, h, target, quiet):
    """How many first moves keep par, how many are legal, and how many strand the board."""
    rays = rays_of(w, h)
    keep = legal = dead = 0
    for _, n, made in moves(b, rays):
        legal += 1
        q, _, _ = solve(n, w, h, target, route=False, rays=rays)
        if q == -1:
            dead += 1
        elif q is not None and q + (0 if made else 1) == quiet:
            keep += 1
    return keep, legal, dead


def fmt(route):
    return " ".join("%d%s" % (c, DIRS[d][2]) for c, d in route)


def unfmt(text):
    return [(int(s[:-1]), "UDLR".index(s[-1])) for s in text.split()]


def made_by(b, w, h, route):
    """The rank each step of a route made (0 for a quiet slide), replayed through `moves`."""
    rays = rays_of(w, h)
    out = []
    for m in route:
        for mm, n, made in moves(b, rays):
            if mm == m:
                b = n
                out.append(made)
                break
        else:
            raise ValueError("the route's %d%s is not a legal slide" % (m[0], DIRS[m[1]][2]))
    return out, b


def colour(rank):
    return (rank - 1) % 4


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


def compose(w, h, ranks, rocks, seed):
    """A board from its seed: rocks and gems dropped on distinct cells in the draw's order."""
    rng = Rng(seed)
    cells = list(range(w * h))
    for i in range(len(cells) - 1, 0, -1):
        j = rng.below(i + 1)
        cells[i], cells[j] = cells[j], cells[i]
    b = [0] * (w * h)
    for c in cells[:rocks]:
        b[c] = ROCK
    for r, c in zip(ranks, cells[rocks:]):
        b[c] = r
    return tuple(b)


def target_of(b):
    m = mass(b)
    t = m.bit_length()
    return t if m == 1 << (t - 1) else None


def shape_faults(b, w, h, tier):
    """What makes a board ugly or trivially read before anything is solved."""
    out = []
    if target_of(b) is None:
        out.append("the gems do not join into one gem (mass %d)" % mass(b))
    rocks = sum(1 for v in b if v == ROCK)
    if rocks > TIERS[tier]["rocks"] * w * h:
        out.append("%d rocks on %d cells" % (rocks, w * h))
    for y in range(h):
        if all(v == ROCK for v in b[y * w:(y + 1) * w]):
            out.append("row %d is all rock" % y)
    for x in range(w):
        if all(b[y * w + x] == ROCK for y in range(h)):
            out.append("column %d is all rock" % x)
    return out


def measure(b, w, h, tier):
    """Everything a row's board is judged on. -> dict, with `faults` empty when it ships."""
    reading = {"faults": shape_faults(b, w, h, tier)}
    t = target_of(b)
    if t is None:
        return reading
    q, route, states = solve(b, w, h, t)
    reading.update(target=t, states=states)
    if q is None or q < 0:
        reading["faults"].append("no route" if q == -1 else "unsolved past %d states" % states)
        return reading
    par = len(route)
    keep, legal, dead = openings(b, w, h, t, q)
    spec = TIERS[tier]
    reading.update(quiet=q, par=par, route=route, keep=keep, legal=legal, dead=dead)
    lo, hi = spec["par"]
    if not lo <= par <= hi:
        reading["faults"].append("par %d outside %d-%d" % (par, lo, hi))
    if q < spec["quiet"]:
        reading["faults"].append("%d quiet slide(s), under %d" % (q, spec["quiet"]))
    if keep > spec["keep"]:
        reading["faults"].append("%d openings keep par, over %d" % (keep, spec["keep"]))
    return reading


# ------------------------------------------------------------------ the hill
class Hill:
    """`ChallengeHill`, line for line: fire, step, strike, muster. A merge's bolts bank."""

    def __init__(self, length, waves):
        self.length = max(1, length)
        self.waves = waves
        self.wards = [[LINE["health"], 0] for _ in range(4)]
        self.raiders = []
        self.turn = 0
        self.muster(0)

    def standing(self):
        return any(w[0] > 0 for w in self.wards)

    def present(self):
        return sum(1 for r in self.raiders if r[1] > 0)

    def feed(self, lane, bolts):
        if self.wards[lane][0] > 0 and bolts > 0:
            self.wards[lane][1] += bolts

    def resolve(self):
        self.turn += 1
        for lane, ward in enumerate(self.wards):
            if ward[0] <= 0:
                continue
            while ward[1] > 0:
                target = None
                for r in self.raiders:
                    if r[1] > 0 and r[0] == lane and (target is None or r[2] < target[2]):
                        target = r
                if target is None:
                    break
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


def run(made, hill_length, waves, bolts, pace=100):
    """`ChallengeTests.MergeHoldsAt`, line for line: the route at `pace` hundredths of its own
    speed. The player's k-th turn has done what the route had done by turn k*100/pace, and a
    step's merge is fed once, the turn the player reaches it; the solving turn never walks.
    -> (won, health left, empty turns, raiders summed over walked turns, turns walked)."""
    hill = Hill(hill_length, waves)
    total = len(made)
    finish = max(total, (total * pace + 99) // 100)
    done = empty = crowd = walked = 0
    for k in range(1, finish):
        progress = min(total - 1, k * 100 // pace)
        while done < progress:
            if made[done]:
                hill.feed(colour(made[done]), bolts)
            done += 1
        on = hill.present()
        empty += on == 0
        crowd += on
        walked += 1
        hill.resolve()
        if not hill.standing():
            return False, 0, empty, crowd, walked
    return True, sum(w[0] for w in hill.wards), empty, crowd, walked


def slack_of(made, hill_length, waves, bolts):
    best = 100
    for p in range(105, 501, 5):
        if not run(made, hill_length, waves, bolts, p)[0]:
            break
        best = p
    return best


def raids(made, hill_length, lead, split, health):
    """**One raider per merge, in the merge's colour, arriving `lead` times the route's turn for
    it** — so every raider on the hill is answered by a merge the plan makes, and the turret a
    merge feeds always has somebody walking into its fire. `split` raiders a merge, each of
    `health`. The solving merge is answered by nothing (the run is won first), so its raiders
    are the clock: they walk until the board is solved. -> [(turn, [(lane, health)])]"""
    at = {}
    for k, rank in enumerate(made, 1):
        if not rank:
            continue
        t = max(0, int(round(k * lead)) - hill_length)
        for _ in range(split):
            at.setdefault(t, []).append((colour(rank), health))
    return sorted(at.items())


def tune(made, tier, target=None):
    """The hill and the waves that put a row's slack nearest its tier's target, with the hill
    never empty and the route's line whole but for one blow. Among tunings inside a notch of
    the target, the hill nearest `CROWD` standing on an average turn wins, then the
    sturdier raider, then the hill nearest the route's length. Never over `MOST_CROWD`. -> (fields, reading) or None."""
    target = target or TIERS[tier]["slack"]
    total = len(made)
    best = None
    for hill_length in range(max(LEAST_HILL, int(total * .6)), max(LEAST_HILL, int(total * 1.6)) + 1):
        for bolts, split, health in ((2, 1, 2), (3, 1, 3), (2, 2, 1), (4, 2, 2), (3, 3, 1)):
            for lead in (1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.8, 2.0):
                waves = raids(made, hill_length, lead, split, health)
                won, left, empty, crowd, walked = run(made, hill_length, waves, bolts)
                if not won or left < LEAST_LINE or empty:
                    continue
                got = slack_of(made, hill_length, waves, bolts)
                if abs(got - target) > 10 or not SLACK_BAND[0] <= got <= SLACK_BAND[1]:
                    continue
                density = crowd / max(1, walked)
                if density > MOST_CROWD:
                    continue
                score = (abs(got - target) // 6, round(abs(density - CROWD), 1), -health, abs(hill_length - total))
                if best is None or score < best[0]:
                    best = (score, {"hill": hill_length, "bolts": bolts, "waves": wave_strings(waves)},
                            {"slack": got, "target": target, "density": density, "turns": total,
                             "lead": lead, "split": split, "health": health, "left": left})
    return None if best is None else (best[1], best[2])


# ------------------------------------------------------------------ the drafts
#: Every merge row but the hand-laid `d05_merge`: (tier, width, height, ranks, rocks, seed).
#: The seed was found by `--search` and composes the board (`compose`); `--check` proves the
#: file's rows are what these draw.
DRAFTS = {
    # id           tier      w  h  ranks                        rocks seed
    "d69_merge": ("medium", 5, 4, [1, 1, 1, 1, 3, 3, 3], 4, 2110),
    "d70_merge": ("medium", 5, 4, [1, 1, 1, 1, 2, 2, 4], 4, 1276),
    "d71_merge": ("medium", 5, 4, [1, 1, 1, 1, 2, 2, 4], 4, 1665),
    "d72_merge": ("medium", 5, 4, [1, 1, 1, 1, 3, 3, 3], 4, 2878),
    "d73_merge": ("medium", 6, 4, [1, 1, 1, 1, 2, 2, 4], 7, 4714),
    "d74_merge": ("medium", 5, 4, [1, 1, 1, 1, 2, 2, 4], 4, 1159),
    "d75_merge": ("medium", 5, 4, [1, 1, 1, 1, 2, 2, 4], 4, 1080),
    "d76_merge": ("medium", 5, 4, [1, 1, 1, 1, 3, 3, 3], 4, 2370),
    "d77_merge": ("medium", 6, 4, [1, 1, 1, 1, 2, 2, 4], 7, 4304),
    "d78_merge": ("medium", 6, 4, [1, 1, 1, 1, 2, 2, 4], 7, 5090),
    "d79_merge": ("medium", 6, 4, [1, 1, 2, 2, 2, 3, 3], 6, 3103),
    "d80_merge": ("medium", 6, 4, [1, 1, 2, 2, 2, 3, 3], 6, 3162),
    "d81_merge": ("medium", 5, 4, [1, 1, 1, 1, 3, 3, 3], 4, 2603),
    "d82_merge": ("medium", 6, 4, [1, 1, 2, 2, 2, 3, 3], 6, 3028),
    "d83_merge": ("hard", 6, 5, [1, 1, 1, 1, 3, 4], 7, 21823),
    "d84_merge": ("hard", 6, 5, [1, 1, 2, 3, 4, 5], 7, 20709),
    "d85_merge": ("hard", 6, 5, [1, 1, 2, 3, 4, 4, 4], 7, 24054),
    "d86_merge": ("hard", 7, 5, [1, 1, 2, 3, 4, 5], 8, 23668),
    "d87_merge": ("hard", 6, 5, [1, 1, 1, 1, 3, 4], 7, 21147),
    "d88_merge": ("hard", 6, 5, [1, 1, 2, 3, 4, 5], 7, 20557),
    "d89_merge": ("hard", 6, 5, [1, 1, 2, 3, 4, 4, 4], 7, 24085),
    "d90_merge": ("hard", 7, 5, [1, 1, 2, 3, 4, 5], 8, 23212),
    "d91_merge": ("hard", 6, 5, [1, 1, 2, 3, 4, 5], 7, 20911),
    "d92_merge": ("hard", 6, 5, [1, 1, 2, 3, 4, 4, 4], 7, 24210),
    "d93_merge": ("hard", 6, 5, [2, 2, 3, 4, 5], 6, 22277),
    "d94_merge": ("hard", 6, 5, [1, 1, 2, 3, 4, 5], 7, 20685),
    "d95_merge": ("hard", 6, 5, [1, 1, 2, 3, 4, 5], 7, 20988),
    "d96_merge": ("hard", 7, 5, [1, 1, 2, 3, 4, 5], 8, 23029),
    "d97_merge": ("hard", 7, 5, [1, 1, 2, 3, 4, 5], 8, 23925),
    "d98_merge": ("hard", 7, 5, [1, 1, 2, 3, 4, 5], 8, 23737),
}


# ------------------------------------------------------------------ the file
def load():
    raw = open(FILE, "rb").read()
    crlf = b"\r\n" in raw
    return raw.decode("utf-8").replace("\r\n", "\n"), crlf


def save(text, crlf):
    out = text.replace("\n", "\r\n") if crlf else text
    open(FILE, "wb").write(out.encode("utf-8"))


def merge_rows():
    return [r for r in json.load(open(FILE, encoding="utf-8"))["challenges"] if r["genre"] == "merge"]


def render_row(row):
    lines = ["    {",
             '      "id": %s,' % json.dumps(row["id"]),
             '      "genre": "merge",',
             '      "seed": %d,' % row["seed"],
             '      "width": %d,' % row["width"],
             '      "height": %d,' % row["height"],
             '      "rows": [']
    lines += ["        %s%s" % (json.dumps(r), "," if i < len(row["rows"]) - 1 else "") for i, r in enumerate(row["rows"])]
    lines += ["      ],",
              '      "target": %d,' % row["target"],
              '      "hill": %d,' % row["hill"],
              '      "bolts": %d,' % row["bolts"],
              '      "waves": [']
    lines += ["        %s%s" % (json.dumps(v), "," if i < len(row["waves"]) - 1 else "") for i, v in enumerate(row["waves"])]
    lines += ["      ]", "    }"]
    return "\n".join(lines)


def place(text, row):
    """Replace a row's whole block, or insert it after the last merge row, leaving every other
    byte of the hand-laid file as it was."""
    block = render_row(row)
    m = re.search(r'\n    \{\n      "id": %s,.*?\n    \}' % re.escape(json.dumps(row["id"])), text, re.S)
    if m:
        return text[:m.start()] + "\n" + block + text[m.end():]
    last = None
    for m in re.finditer(r'\n    \{\n      "id": "[^"]+",\n      "genre": "merge",.*?\n    \}', text, re.S):
        last = m
    if last is None:
        raise ValueError("challenges.json has no merge row to place a new one after")
    return text[:last.end()] + ",\n" + block + text[last.end():]


def tier_of(row):
    if row["id"] in DRAFTS:
        return DRAFTS[row["id"]][0]
    return "medium"


def report(row, quiet_print=False):
    """A row's full reading. -> (reading, faults)."""
    b, w, h = parse(row["rows"])
    tier = tier_of(row)
    reading = measure(b, w, h, tier)
    faults = list(reading["faults"])
    if "route" not in reading:
        return reading, faults
    if reading["target"] != row["target"]:
        faults.append("target %d, the gems join into %d" % (row["target"], reading["target"]))
    made, _ = made_by(b, w, h, reading["route"])
    reading["made"] = made
    waves = read_waves(row["waves"])
    won, left, empty, crowd, walked = run(made, row["hill"], waves, row["bolts"])
    got = slack_of(made, row["hill"], waves, row["bolts"])
    reading.update(won=won, left=left, empty=empty, density=crowd / max(1, walked), slack=got)
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
    if row["id"] in DRAFTS:
        _, dw, dh, ranks, rocks, seed = DRAFTS[row["id"]]
        drawn = rows_of(compose(dw, dh, ranks, rocks, seed), dw, dh)
        if drawn != row["rows"] or seed != row["seed"]:
            faults.append("the board is not what its draft composes")
    return reading, faults


def print_report(row, reading, faults):
    if "par" not in reading:
        print("%-11s %s" % (row["id"], "; ".join(faults)))
        return
    feeds = " ".join(LANES[colour(r)] if r else "-" for r in reading.get("made", []))
    print("%-11s %-6s %dx%d to %d  par %2d (%d quiet)  openings %d of %d (%d strand)  hill %d  bolts %d  "
          "slack %.2fx  %.1f on the hill  line %d"
          % (row["id"], tier_of(row), row["width"], row["height"], 1 << reading["target"], reading["par"],
             reading["quiet"], reading["keep"], reading["legal"], reading["dead"], row["hill"], row["bolts"],
             reading.get("slack", 0) / 100, reading.get("density", 0), reading.get("left", 0)))
    print("            route %s" % fmt(reading["route"]))
    print("            feeds %s" % feeds)
    for f in faults:
        print("            FAULT %s" % f)


def routes_source(rows):
    """`MergeRoutes.cs`: every row's route, for `ChallengeTests` to replay against the C#."""
    lines = [
        "// Generated by Tools/make_merge_challenges.py (--draft / --write). Do not edit;",
        "// `--check` refuses a file that is not what the tool writes.",
        "using System.Collections.Generic;",
        "",
        "namespace GlimmerGrove.Tests",
        "{",
        "    /// <summary>",
        "    /// The shortest route through every shipped Merge row, found by the tool's 0-1",
        "    /// breadth-first search over quiet slides, as cell and direction in board terms",
        "    /// (<c>D</c> is down the rows). <see cref=\"ChallengeTests\"/> replays each against",
        "    /// <c>MergePuzzle</c> and paces it against the hill (invariant 56n).",
        "    /// </summary>",
        "    static class MergeRoutes",
        "    {",
        "        public static readonly Dictionary<string, string> Shortest = new Dictionary<string, string>",
        "        {",
    ]
    for row, route in rows:
        lines.append('            ["%s"] = "%s",' % (row, route))
    lines += ["        };", "    }", "}", ""]
    return "\n".join(lines)


def write_routes(readings):
    text = routes_source([(rid, fmt(r["route"])) for rid, r in readings])
    open(ROUTES, "w", encoding="utf-8", newline="\r\n").write(text)


def routes_on_disk():
    if not ROUTES.exists():
        return None
    return dict(re.findall(r'\["([^"]+)"\] = "([^"]*)"', ROUTES.read_text(encoding="utf-8")))


# ------------------------------------------------------------------ the commands
def cmd_report(ids):
    bad = 0
    for row in merge_rows():
        if ids and row["id"] not in ids:
            continue
        reading, faults = report(row)
        print_report(row, reading, faults)
        bad += bool(faults)
    return 1 if bad else 0


def tune_row(row, tier):
    b, w, h = parse(row["rows"])
    q, route, _ = solve(b, w, h, row["target"])
    made, _ = made_by(b, w, h, route)
    return tune(made, tier)


def refresh(ids, compose_drafts):
    text, crlf = load()
    if compose_drafts:
        for rid, (tier, w, h, ranks, rocks, seed) in DRAFTS.items():
            if ids and rid not in ids:
                continue
            b = compose(w, h, ranks, rocks, seed)
            row = {"id": rid, "seed": seed, "width": w, "height": h, "rows": rows_of(b, w, h),
                   "target": target_of(b), "hill": LEAST_HILL, "bolts": 2, "waves": ["0 r1"]}
            text = place(text, row)
        save(text, crlf)
    for row in merge_rows():
        if ids and row["id"] not in ids:
            continue
        got = tune_row(row, tier_of(row))
        if not got:
            print("%s: nothing in the search space hits its target" % row["id"], flush=True)
            return 1
        fields, reading = got
        row.update(fields)
        text = place(text, row)
        print("%s: hill %d, %d bolt(s), %d raider(s) of %d a merge arriving %.1fx the route's turn, "
              "%.1f on the hill, slack %.2fx (target %.2fx) over %d turns"
              % (row["id"], fields["hill"], fields["bolts"], reading["split"], reading["health"], reading["lead"],
                 reading["density"], reading["slack"] / 100, reading["target"] / 100, reading["turns"]), flush=True)
    save(text, crlf)
    return verify()


def verify():
    readings, failed = [], []
    for row in merge_rows():
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
    print("%d merge row(s) written and proved; routes in %s" % (len(readings), ROUTES.relative_to(REPO)))
    return 0


def cmd_check():
    rows = merge_rows()
    ids = {r["id"] for r in rows}
    faults = ["%s is drafted and not in the file" % rid for rid in DRAFTS if rid not in ids]
    wanted = {}
    for row in rows:
        reading, got = report(row)
        faults += ["%s: %s" % (row["id"], f) for f in got]
        if "route" in reading:
            wanted[row["id"]] = fmt(reading["route"])
    if routes_on_disk() != wanted:
        faults.append("%s is not what the tool writes (run --write)" % ROUTES.relative_to(REPO))
    for f in faults:
        print("FAULT " + f)
    print("merge: %d row(s), %d fault(s)" % (len(rows), len(faults)))
    return 1 if faults else 0


def search_one(args):
    tier, w, h, ranks, rocks, seed = args
    b = compose(w, h, ranks, rocks, seed)
    if shape_faults(b, w, h, tier):
        return None
    t = target_of(b)
    q, route, _ = solve(b, w, h, t, limit=600_000)
    if q is None or q < 0:
        return None
    par, spec = len(route), TIERS[tier]
    if not spec["par"][0] <= par <= spec["par"][1] or q < spec["quiet"]:
        return None
    keep, legal, dead = openings(b, w, h, t, q)
    if keep > spec["keep"]:
        return None
    return seed, par, q, keep, legal, dead, rows_of(b, w, h), fmt(route)


def cmd_search(tier, size, ranks, rocks, start, count, jobs):
    from multiprocessing import Pool
    w, h = (int(v) for v in size.split("x"))
    ranks = [int(c) for c in ranks]
    tasks = [(tier, w, h, ranks, rocks, s) for s in range(start, start + count)]
    t0 = time.time()
    with Pool(jobs) as pool:
        for got in pool.imap_unordered(search_one, tasks, chunksize=4):
            if got:
                seed, par, q, keep, legal, dead, rows, route = got
                print("seed %6d  par %2d (%d quiet)  openings %d of %d (%d strand)  %s  | %s"
                      % (seed, par, q, keep, legal, dead, " ".join(rows), route), flush=True)
    print("searched %d seed(s) in %.0fs" % (count, time.time() - t0), flush=True)
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--draft", action="store_true")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--id", action="append")
    ap.add_argument("--search", choices=sorted(TIERS))
    ap.add_argument("--size", default="6x4")
    ap.add_argument("--gems", default="1111224", help="the ranks a searched board carries")
    ap.add_argument("--rocks", type=int, default=6)
    ap.add_argument("--from", dest="start", type=int, default=1)
    ap.add_argument("--count", type=int, default=2000)
    ap.add_argument("--jobs", type=int, default=12)
    a = ap.parse_args()
    ids = set(a.id or [])
    if a.search:
        return cmd_search(a.search, a.size, a.gems, a.rocks, a.start, a.count, a.jobs)
    if a.draft:
        return refresh(ids, True)
    if a.write:
        return refresh(ids, False)
    if a.check:
        return cmd_check()
    return cmd_report(ids)


if __name__ == "__main__":
    sys.exit(main())
