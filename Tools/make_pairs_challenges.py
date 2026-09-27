# -*- coding: utf-8 -*-
"""Drafts, tunes and reports the Pairs daily challenges (CLAUDE.md 56m).

    python Tools/make_pairs_challenges.py --report            # every pairs row's reading
    python Tools/make_pairs_challenges.py --report --id d39_pairs
    python Tools/make_pairs_challenges.py --draft             # compose every row in DRAFTS and tune it
    python Tools/make_pairs_challenges.py --draft --id d39_pairs
    python Tools/make_pairs_challenges.py --write             # retune the hill and waves of the rows as written

**A Pairs board is dealt, so a row is measured over deals rather than over one layout.** The
row authors *which* cards (`r1 .. y6`, and `o` for a cursed stone); every play shuffles them
(`PairsPuzzle.Shuffle`, keyed on the ledger's `ChallengePlay.Deal`). So this plays `DEALS`
deals of every row with a perfect-memory bot, and reads a row off the **median** deal - the
luck a player usually has - while holding **every** deal to a win at the bot's pace, because
a board the bot loses on a bad shuffle is a board a player can lose to the shuffle alone.

**The bot remembers everything and explores in reading order** (`ChallengeTests.PerfectMemory`,
line for line): cash a known pair, else turn the first unseen card, take its partner if it has
been seen, else turn the next unseen one. Over a shuffled deal, reading order *is* a random
order, which is how a first-sight player explores.

**Slack is the hill's question, asked of memory**: how many times the bot's turns a player may
take and still win (`ChallengeTests.HoldsAt`, the glade's measure). A human's extra turns are
misses they would not have made with a perfect memory, so a slack is a statement about how
good a memory the row asks for. `TARGET` is the dial; `SLACK_BAND` is what the gates hold.

The shuffle, the bot, the combo, the curse and the hill are mirrored from C# exactly (xorshift32,
`fmix32`), so a figure printed here is the figure `ChallengeTests` prints. `content.py` runs
`report` over every shipped row, and the C# fixture holds the same three gates.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
FILE = REPO / "Assets" / "StreamingAssets" / "Content" / "challenges.json"

LANES = "rgby"                      # ChallengeColours.Letters
VARIANTS = 6                        # PairsGems.Variants
CURSE = -1                          # PairsGems.Curse
COMBO_CAP = 3                       # PairsPuzzle.ComboCap
MOST_CURSES = 3                     # PairsGems.MostCurses
LINE = {"damage": 1, "health": 3, "strike": 1}
MASK = 0xFFFFFFFF

#: How many deals a row is measured over. `ChallengeTests.PairsDeals` is the same figure, and
#: the deals are 1..DEALS, which `PairsPuzzle` mixes with the row's seed.
DEALS = 48

#: The slowest pace, in hundredths of the bot's turns, a row is tuned to forgive on its median
#: deal. A medium row forgives a player who takes 1.7 times the turns a perfect memory would;
#: a hard row asks for a good memory. `ChallengeTests` holds every row to `SLACK_BAND`.
TARGET = {"medium": 170, "hard": 145}
SLACK_BAND = (135, 185)

#: **The turrets have to matter** (invariant 5d asked of the fusion): the same run with every
#: bolt taken away must forgive at least this much less, in hundredths of the bot's turns. A row
#: whose hill is lost or won the same whatever the pairs fed is a timer with a picture on it.
FIRE_WORTH = 25

#: The most raiders a row may stand on the hill on an average turn at the bot's pace. The hill
#: is four lanes and a few cells tall on a phone; past this the bodies stack into one smear and
#: the colour of the nearest threat - the thing a player reads to choose a pair - is lost.
MOST_CROWD = 4.5

#: The line health the **unluckiest** of the `DEALS` deals must keep at the bot's pace, of
#: twelve. A shuffle can turn a cursed stone first or leave every pair to the end, and a row
#: a perfect memory barely survives on a bad deal is a row a player loses to the deal alone.
LUCK_FLOOR = {"medium": 6, "hard": 3}

#: A row with this many pairs or more is a hard row.
HARD_PAIRS = 13

#: How long the streams run, in multiples of the median deal's turns: past anybody still
#: standing at the slowest pace a row forgives, so the waves never run out on a slow player.
STREAM_END = 2.0

#: The shortest hill, in steps. A Pairs run is shorter than a glade's, so its hill is too; at
#: the d01 row's 10 a step was the "big step" the owner asked to be made smaller (2026-09-26).
LEAST_HILL = 12


# ------------------------------------------------------------------ the rules, mirrored
def mix(h):
    """`ChallengeCalendar.Mix` (fmix32)."""
    h &= MASK
    h ^= h >> 16
    h = (h * 0x85EBCA6B) & MASK
    h ^= h >> 13
    h = (h * 0xC2B2AE35) & MASK
    h ^= h >> 16
    return h


class Rng:
    """`ChallengeRng`: xorshift32 over a seed, never nought."""

    def __init__(self, seed):
        self.s = (seed * 2654435761) & MASK
        if self.s == 0:
            self.s = 2463534242

    def next(self):
        x = self.s
        x ^= (x << 13) & MASK
        x ^= x >> 17
        x ^= (x << 5) & MASK
        self.s = x
        return x

    def below(self, n):
        return 0 if n <= 0 else self.next() % n


def parse_token(tok):
    """`PairsGems.TryParse` -> kind, or None."""
    if tok == "o":
        return CURSE
    if len(tok) != 2 or tok[0] not in LANES or not tok[1].isdigit():
        return None
    v = int(tok[1])
    if not 1 <= v <= VARIANTS:
        return None
    return LANES.index(tok[0]) * VARIANTS + v - 1


def token(kind):
    return "o" if kind == CURSE else f"{LANES[kind // VARIANTS]}{kind % VARIANTS + 1}"


def colour(kind):
    return -1 if kind == CURSE else kind // VARIANTS


def kinds_of(row):
    out = []
    for line in row["rows"]:
        for tok in line.split():
            k = parse_token(tok)
            if k is None:
                raise ValueError(f"{row['id']}: '{tok}' is not a gem")
            out.append(k)
    return out


def faults(row):
    """`PairsPuzzle.Fault`, as far as a row's shape goes."""
    out = []
    if len(row.get("rows") or []) != row.get("height"):
        return [f"must have {row.get('height')} rows"]
    counts, curses = {}, 0
    for y, line in enumerate(row["rows"]):
        toks = line.split()
        if len(toks) != row.get("width"):
            out.append(f"row {y} holds {len(toks)} tokens, not {row.get('width')}")
            continue
        for tok in toks:
            k = parse_token(tok)
            if k is None:
                out.append(f"row {y} holds '{tok}', which is not a gem or the curse")
            elif k == CURSE:
                curses += 1
            else:
                counts[k] = counts.get(k, 0) + 1
    for k, n in counts.items():
        if n % 2:
            out.append(f"deals {n} '{token(k)}', which cannot all pair")
    if not counts:
        out.append("has no pair")
    if curses > MOST_CURSES:
        out.append(f"deals {curses} cursed stones; at most {MOST_CURSES}")
    return out


def deal(kinds, seed, d):
    """`PairsPuzzle.Shuffle`: Fisher-Yates over `ChallengeRng(Mix(seed * golden ^ deal) | 1)`."""
    k = list(kinds)
    if d == 0:
        return k
    rng = Rng(mix(((seed * 0x9E3779B1) & MASK) ^ d) | 1)
    for i in range(len(k) - 1, 0, -1):
        j = rng.below(i + 1)
        k[i], k[j] = k[j], k[i]
    return k


def bot(board, bolts):
    """The perfect-memory player (`ChallengeTests.PerfectMemory`), turn by turn through the
    real rules. -> list of walked resolves, each four lanes of bolts fed; the solving turn is
    not in it (it never walks), and a curse adds one empty resolve after its own (a stumble)."""
    n = len(board)
    known = [False] * n
    matched = [False] * n
    first = -1
    streak = 0
    walked = []
    pairs = sum(1 for k in board if k != CURSE) // 2
    made = 0

    def tap(c):
        """One tap. -> ('first'|'match'|'miss'|'curse', feeds) with feeds only on a walked turn."""
        nonlocal first, streak, made
        known[c] = True
        if board[c] == CURSE:
            first = -1
            streak = 0
            return "curse"
        if first < 0:
            first = c
            return "first"
        a, first = first, -1
        if board[a] == board[c]:
            matched[a] = matched[c] = True
            made += 1
            streak += 1
            return "match"
        streak = 0
        return "miss"

    def walk(result, kind):
        feeds = [0, 0, 0, 0]
        if result == "match":
            feeds[colour(kind)] = bolts * min(streak, COMBO_CAP)
        if made == pairs:
            return True                                  # solved before the hill walks
        walked.append(feeds)
        if result == "curse":
            walked.append([0, 0, 0, 0])
        return False

    def hidden(c):
        return not matched[c] and c != first

    guard = 0
    while made < pairs and guard < 4 * n * n:
        guard += 1
        pair = None
        for i in range(n):
            if known[i] and hidden(i) and board[i] != CURSE:
                for j in range(i + 1, n):
                    if known[j] and hidden(j) and board[j] == board[i]:
                        pair = (i, j)
                        break
            if pair:
                break
        if pair:
            tap(pair[0])
            r = tap(pair[1])
            if walk(r, board[pair[1]]):
                break
            continue

        a = next(i for i in range(n) if not known[i] and not matched[i])
        r = tap(a)
        if r == "curse":
            if walk(r, CURSE):
                break
            continue
        partner = next((j for j in range(n) if j != a and known[j] and hidden(j) and board[j] == board[a]), -1)
        if partner < 0:
            b = next(i for i in range(n) if not known[i] and not matched[i] and i != a)
        else:
            b = partner
        r = tap(b)
        if walk(r, board[b]):
            break
    return walked


# ------------------------------------------------------------------ the hill
class Hill:
    """`ChallengeHill`, line for line: fire, step, strike, muster. Every Pairs feed banks."""

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
    return [f"{t} " + " ".join(f"{LANES[lane]}{hp}" for lane, hp in raiders) for t, raiders in waves]


def run(walked, hill_length, waves, pace=100):
    """`ChallengeTests.HoldsAt`: the bot's run at `pace` hundredths of its speed, against a
    fresh hill. -> (won, health left, empty walked turns, raiders summed over walked turns,
    walked turns)."""
    hill = Hill(hill_length, waves)
    total = len(walked) + 1
    finish = (total * pace + 99) // 100
    empty = crowd = steps = 0
    for k in range(1, finish):
        progress = min(total - 1, k * 100 // pace)
        if progress >= 1:
            for lane, bolts in enumerate(walked[progress - 1]):
                hill.feed(lane, bolts)
        on = hill.present()
        empty += on == 0
        crowd += on
        steps += 1
        hill.resolve()
        if not hill.standing():
            return False, 0, empty, crowd, steps
    return True, sum(w[0] for w in hill.wards), empty, crowd, steps


def dry(walked):
    """The same run with every bolt taken away."""
    return [[0, 0, 0, 0] for _ in walked]


def slack_of(walked, hill_length, waves):
    best = 100
    for p in range(105, 501, 5):
        if not run(walked, hill_length, waves, p)[0]:
            break
        best = p
    return best


# ------------------------------------------------------------------ reading a row
def schedules(row):
    """Every deal's walked resolves, and the median deal (`ChallengeTests.MedianDeal`)."""
    kinds = kinds_of(row)
    bolts = max(1, row.get("bolts") or 1)
    runs = [(d, bot(deal(kinds, row["seed"], d), bolts)) for d in range(1, DEALS + 1)]
    ranked = sorted(runs, key=lambda r: (len(r[1]), r[0]))
    return runs, ranked[len(ranked) // 2]


def tier_of(row):
    kinds = kinds_of(row)
    pairs = sum(1 for k in kinds if k != CURSE) // 2
    return "hard" if pairs >= HARD_PAIRS else "medium"


def report(row, runs=None, median=None):
    if runs is None:
        runs, median = schedules(row)
    waves = read_waves(row["waves"])
    hill = row["hill"]
    worst_health, losses = 99, []
    for d, walked in runs:
        won, left, _, _, _ = run(walked, hill, waves)
        if not won:
            losses.append(d)
        worst_health = min(worst_health, left)
    won, left, empty, crowd, steps = run(median[1], hill, waves)
    kinds = kinds_of(row)
    turns = sorted(len(w) + 1 for _, w in runs)
    return {
        "id": row["id"], "tier": tier_of(row), "pairs": sum(1 for k in kinds if k != CURSE) // 2,
        "curses": kinds.count(CURSE), "colours": "".join(sorted({LANES[colour(k)] for k in kinds if k != CURSE},
                                                                  key=LANES.index)),
        "median_deal": median[0], "turns": len(median[1]) + 1, "fewest": turns[0], "most": turns[-1],
        "won": won, "health": left, "empty": empty, "density": crowd / max(1, steps),
        "slack": slack_of(median[1], hill, waves), "losses": losses, "worst_health": worst_health,
        "hill": hill, "dry": slack_of(dry(median[1]), hill, waves),
    }


def bad(r):
    lo, hi = SLACK_BAND
    return (not r["won"] or r["health"] < 11 or r["empty"] or r["losses"] or r["density"] > MOST_CROWD
            or r["worst_health"] < LUCK_FLOOR[r["tier"]]
            or not lo <= r["slack"] <= hi or r["slack"] - r["dry"] < FIRE_WORTH)


def print_report(r):
    flag = "  <-- " if bad(r) else ""
    print(f"{r['id']:<12} {r['tier']:<6} {r['pairs']:>2} pairs {r['curses']} curse(s) [{r['colours']:<4}]  "
          f"bot {r['turns']:>2} turns (deals {r['fewest']}-{r['most']})  hill {r['hill']:>2}  "
          f"slack {r['slack'] / 100:.2f}x (fire buys {(r['slack'] - r['dry']) / 100:.2f}x)  line {r['health']}/12, worst deal {r['worst_health']}/12, "
          f"{r['density']:.1f} on the hill"
          + (f", LOST on deal(s) {r['losses']}" if r['losses'] else "")
          + (f", EMPTY on {r['empty']} turn(s)" if r['empty'] else "") + flag)


# ------------------------------------------------------------------ tuning the hill
def streams(lanes, pairs_of, hill_length, period, health, stagger, end):
    """**Every colour on the board is a stream of raiders**, opened `stagger` turns apart in
    lane order, each sending a raider every `period` turns - sooner for a colour with more pairs
    on the board, since more pairs mean more bolts for it. The first opens before the first
    move, so the hill is never empty. -> [(turn, [(lane, health)])]"""
    mean = sum(pairs_of[l] for l in lanes) / len(lanes)
    at = {}
    for n, lane in enumerate(lanes):
        every = max(3, int(round(period * mean / pairs_of[lane])))
        t = n * stagger
        while t <= end:
            at.setdefault(t, []).append((lane, health))
            t += every
    return sorted(at.items())


def tune(row, target=None):
    """The hill and waves that put a row's median-deal slack nearest its tier's `TARGET`, with
    every deal won at the bot's pace with `LUCK_FLOOR` of the line left, the median deal's line
    whole but for one blow, the hill
    never empty, and the turrets worth `FIRE_WORTH`. Among the tunings that hit, the most crowded
    hill wins (up to three on an average turn), then a raider of about four bolts - a pair of the
    right colour, combo'd, fells one, which is a kill a player can watch - then a hill nearest
    0.6 of the bot's turns.
    -> (fields, reading) or None."""
    runs, median = schedules(row)
    tier = tier_of(row)
    target = target or TARGET[tier]
    kinds = kinds_of(row)
    pairs_of = {}
    for k in kinds:
        if k != CURSE:
            pairs_of[colour(k)] = pairs_of.get(colour(k), 0) + 1
    lanes = sorted(pairs_of)
    for lane in lanes:
        pairs_of[lane] //= 2
    total = len(median[1]) + 1
    end = int(total * STREAM_END)
    best = None
    for hill_length in range(max(LEAST_HILL, int(total * .35)), max(LEAST_HILL, int(total * .9)) + 1):
        for period in (3, 4, 5, 6, 7, 8, 10):
            for health in (2, 3, 4, 5, 6):
                for stagger in (0, 1, 2, 3):
                    waves = streams(lanes, pairs_of, hill_length, period, health, stagger, end)
                    won, left, empty, crowd, steps = run(median[1], hill_length, waves)
                    if not won or left < 11 or empty:
                        continue
                    got = slack_of(median[1], hill_length, waves)
                    if abs(got - target) > 10:
                        continue
                    if got - slack_of(dry(median[1]), hill_length, waves) < FIRE_WORTH:
                        continue
                    if any(run(w, hill_length, waves)[1] < LUCK_FLOOR[tier] for _, w in runs):
                        continue
                    density = crowd / max(1, steps)
                    if density > MOST_CROWD:
                        continue
                    score = (abs(got - target) // 6, -min(density, 3.0), abs(health - 4), abs(hill_length - total * .6))
                    if best is None or score < best[0]:
                        best = (score, {"hill": hill_length, "waves": wave_strings(waves)},
                                {"tier": tier, "target": target, "slack": got, "turns": total,
                                 "density": density, "period": period, "health": health, "stagger": stagger})
    return None if best is None else (best[1], best[2])


# ------------------------------------------------------------------ drafting a board
#: The slate, as specs: a size, the colours dealt, how many cursed stones, and a seed. The
#: kinds are drawn from it (`compose`), so a spec is a *board shape* and the tool picks the
#: stones. **Medium** rows are 9-12 pairs; **hard** rows 13-17, most with curses, several
#: with two or three colours so that every stone of a colour is on the board at once - six
#: reds that are six different shapes, which is what a memory game is hardest at.
DRAFTS = {
    # id            w  h  colours curses seed
    "d01_pairs": (6, 3, "rgby", 0, 1101),
    "d39_pairs": (5, 4, "rgb", 0, 3901),
    "d40_pairs": (6, 3, "gby", 0, 4001),
    "d41_pairs": (5, 4, "rgby", 0, 4101),
    "d42_pairs": (6, 4, "rgb", 0, 4201),
    "d43_pairs": (5, 5, "rgby", 1, 4301),
    "d44_pairs": (6, 3, "rb", 0, 4401),
    "d45_pairs": (6, 4, "rgby", 0, 4501),
    "d46_pairs": (7, 3, "gby", 1, 4601),
    "d47_pairs": (5, 4, "gy", 0, 4701),
    "d48_pairs": (7, 3, "rby", 3, 4801),
    "d49_pairs": (5, 5, "rby", 1, 4901),
    "d50_pairs": (7, 3, "rgby", 1, 5001),
    "d51_pairs": (6, 4, "rg", 0, 5101),
    "d52_pairs": (6, 4, "rgby", 2, 5201),
    "d53_pairs": (6, 5, "rgby", 0, 5301),
    "d54_pairs": (7, 4, "rgby", 0, 5401),
    "d55_pairs": (6, 5, "rgby", 2, 5501),
    "d56_pairs": (7, 4, "rgb", 2, 5601),
    "d57_pairs": (6, 5, "gby", 0, 5701),
    "d58_pairs": (7, 5, "rgby", 1, 5801),
    "d59_pairs": (6, 5, "gby", 2, 5901),
    "d60_pairs": (7, 4, "rby", 0, 6001),
    "d61_pairs": (7, 5, "rgb", 1, 6101),
    "d62_pairs": (6, 5, "rgy", 0, 6201),
    "d63_pairs": (7, 5, "rby", 3, 6301),
    "d64_pairs": (7, 4, "rgby", 2, 6401),
    "d65_pairs": (6, 5, "rby", 2, 6501),
    "d66_pairs": (7, 5, "gby", 3, 6601),
    "d67_pairs": (7, 5, "rgy", 1, 6701),
    "d68_pairs": (7, 5, "rgby", 3, 6801),
}


def compose(row_id):
    """A row's cards from its spec: the pairs shared across its colours as evenly as they go
    (the first colours take the odd ones), each colour's stones a seeded draw of distinct
    variants, and the cards laid out in a seeded order - which is only how the file reads,
    since every play is shuffled. -> the `rows` field."""
    w, h, colours, curses, seed = DRAFTS[row_id]
    cells = w * h
    if (cells - curses) % 2:
        raise ValueError(f"{row_id}: {cells} cells less {curses} curse(s) is odd")
    pairs = (cells - curses) // 2
    share = [pairs // len(colours) + (1 if i < pairs % len(colours) else 0) for i in range(len(colours))]
    rng = Rng(seed)
    cards = []
    for letter, n in zip(colours, share):
        if n > VARIANTS:
            raise ValueError(f"{row_id}: {n} pairs of '{letter}' but only {VARIANTS} stones")
        variants = list(range(1, VARIANTS + 1))
        for i in range(len(variants) - 1, 0, -1):
            j = rng.below(i + 1)
            variants[i], variants[j] = variants[j], variants[i]
        for v in sorted(variants[:n]):
            cards += [f"{letter}{v}"] * 2
    cards += ["o"] * curses
    for i in range(len(cards) - 1, 0, -1):
        j = rng.below(i + 1)
        cards[i], cards[j] = cards[j], cards[i]
    return [" ".join(cards[y * w:(y + 1) * w]) for y in range(h)]


# ------------------------------------------------------------------ the file
def render_row(row):
    lines = ["    {",
             f'      "id": {json.dumps(row["id"])},',
             '      "genre": "pairs",',
             f'      "seed": {row["seed"]},',
             f'      "width": {row["width"]},',
             f'      "height": {row["height"]},',
             '      "rows": [']
    lines += [f"        {json.dumps(r)}" + ("," if i < len(row["rows"]) - 1 else "") for i, r in enumerate(row["rows"])]
    lines += ["      ],",
              f'      "hill": {row["hill"]},',
              f'      "bolts": {row["bolts"]},',
              '      "waves": [']
    lines += [f"        {json.dumps(v)}" + ("," if i < len(row["waves"]) - 1 else "") for i, v in enumerate(row["waves"])]
    lines += ["      ]", "    }"]
    return "\n".join(lines)


def place(text, row):
    """Replace a row's whole block, or insert it after the last pairs row, keeping every other
    byte of the hand-laid file as it was."""
    block = render_row(row)
    m = re.search(r'\n    \{\n      "id": %s,.*?\n    \}' % re.escape(json.dumps(row["id"])), text, re.S)
    if m:
        return text[:m.start()] + "\n" + block + text[m.end():]
    last = None
    for m in re.finditer(r'\n    \{\n      "id": "[^"]+",\n      "genre": "pairs",.*?\n    \}', text, re.S):
        last = m
    if last is None:
        raise ValueError("challenges.json has no pairs row to place a new one after")
    return text[:last.end()] + ",\n" + block + text[last.end():]


def load():
    raw = open(FILE, "rb").read()
    crlf = b"\r\n" in raw
    return raw.decode("utf-8").replace("\r\n", "\n"), crlf


def save(text, crlf):
    out = text.replace("\n", "\r\n") if crlf else text
    open(FILE, "wb").write(out.encode("utf-8"))


def pairs_rows():
    return [r for r in json.load(open(FILE, encoding="utf-8"))["challenges"] if r["genre"] == "pairs"]


def draft(ids):
    text, crlf = load()
    for row_id in DRAFTS:
        if ids and row_id not in ids:
            continue
        w, h, _, _, seed = DRAFTS[row_id]
        row = {"id": row_id, "seed": seed, "width": w, "height": h, "rows": compose(row_id),
               "hill": LEAST_HILL, "bolts": 2, "waves": ["0 r1"]}
        got = tune(row)
        if not got:
            print(f"{row_id}: nothing in the search space hits its target", flush=True)
            return 1
        fields, reading = got
        row.update(fields)
        text = place(text, row)
        save(text, crlf)
        print(f"{row_id}: {reading['tier']} hill {fields['hill']}, a {reading['health']}-bolt raider every "
              f"~{reading['period']} turns a lane, lanes {reading['stagger']} turn(s) apart, "
              f"{reading['density']:.1f} on the hill, slack {reading['slack'] / 100:.2f}x "
              f"(target {reading['target'] / 100:.2f}x) over {reading['turns']} turns", flush=True)
    return verify(ids)


def write(ids):
    text, crlf = load()
    for row in pairs_rows():
        if ids and row["id"] not in ids:
            continue
        got = tune(row)
        if not got:
            print(f"{row['id']}: nothing in the search space hits its target", flush=True)
            return 1
        fields, reading = got
        row.update(fields)
        text = place(text, row)
        print(f"{row['id']}: hill {fields['hill']}, slack {reading['slack'] / 100:.2f}x", flush=True)
    save(text, crlf)
    return verify(ids)


def verify(ids):
    failed = []
    for row in pairs_rows():
        if ids and row["id"] not in ids:
            continue
        f = faults(row)
        r = report(row)
        if f or bad(r):
            failed.append(row["id"])
    if failed:
        print("written, and these read back with a fault: " + ", ".join(failed))
        return 1
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--draft", action="store_true")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--id", action="append")
    a = ap.parse_args()
    ids = set(a.id or [])
    if a.draft:
        return draft(ids)
    if a.write:
        return write(ids)
    rc = 0
    for row in pairs_rows():
        if ids and row["id"] not in ids:
            continue
        f = faults(row)
        if f:
            print(f"{row['id']}: " + "; ".join(f))
            rc = 1
            continue
        r = report(row)
        print_report(r)
        rc |= bad(r)
    return rc


if __name__ == "__main__":
    sys.exit(main())
