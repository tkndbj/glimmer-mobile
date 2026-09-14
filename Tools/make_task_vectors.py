# -*- coding: utf-8 -*-
"""Writes the task-chest vectors into `firebase/shared/reward-vectors.json`.

    python Tools/make_task_vectors.py          # regenerate the taskChestCases block
    python Tools/make_task_vectors.py --check  # prove the block on disk is what this writes

A task's chest is rolled by `ChestDefinition.Roll(ChestSeed.ForSubject(...))` on the client
and by `rollTaskChest` in `functions/src/tasks.ts` on the server, and the two are held
together by the vectors both sides run as a test (invariant 9a for a chest). This is a
*third* copy of the generator — FNV-1a over UTF-16 code units, xorshift32, the stream
numbers, the single weighted pick and the same-kind merge — kept here so the expected
answers are produced by something that is neither side. If both harnesses go red at once
after a change, the change moved the contract; if only one does, that side drifted.

The config is synthetic on purpose (the daily chest vectors' rule): what is pinned is the
arithmetic, not this season's drop rates, so retuning `progression.json` must never turn the
vectors red.
"""
import io
import json
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VECTORS = os.path.join(REPO, "firebase", "shared", "reward-vectors.json")

MASK = 0xFFFFFFFF
FNV_OFFSET = 2166136261
FNV_PRIME = 16777619

STREAM_PICK = 0
STREAM_AMOUNT = 1


def absorb_char(h, c):
    h = ((h ^ (c & 0xFF)) * FNV_PRIME) & MASK
    h = ((h ^ ((c >> 8) & 0xFF)) * FNV_PRIME) & MASK
    return h


def absorb_string(h, text):
    """FNV-1a over UTF-16 code units, low byte first - ChestRandom.Absorb's rule."""
    units = text.encode("utf-16-le")
    for i in range(0, len(units), 2):
        h = absorb_char(h, units[i] | (units[i + 1] << 8))
    return h


def absorb_int(h, value):
    if value < 0:
        h = absorb_char(h, ord("-"))
        value = -value
    for digit in str(value):
        h = absorb_char(h, ord(digit))
    return h


def subject_seed(player_key, tag, subject, stream):
    h = FNV_OFFSET
    h = absorb_string(h, player_key)
    h = absorb_char(h, ord("|"))
    h = absorb_string(h, tag)
    h = absorb_char(h, ord("|"))
    h = absorb_string(h, subject)
    h = absorb_char(h, ord("|"))
    h = absorb_int(h, stream)
    return h if h != 0 else FNV_OFFSET


class Rolls:
    def __init__(self, seed):
        self.state = seed & MASK

    def next(self):
        x = self.state
        x ^= (x << 13) & MASK
        x ^= x >> 17
        x ^= (x << 5) & MASK
        self.state = x & MASK
        return self.state

    def below(self, bound):
        return 0 if bound <= 1 else self.next() % bound

    def between(self, lo, hi):
        return lo if hi <= lo else lo + self.below(hi - lo + 1)


def clamp(band):
    lo = max(1, int(band["min"]))
    hi = max(lo, int(band["max"]))
    return band["kind"], lo, hi, band.get("item") or ""


def roll(chest, seed_for):
    drops = []

    def merge(kind, amount, item):
        if amount <= 0:
            return
        for d in drops:
            if d["kind"] == kind and d.get("item", "") == item:
                d["amount"] += amount
                return
        row = {"kind": kind, "amount": amount}
        if item:
            row["item"] = item
        drops.append(row)

    for i, band in enumerate(chest.get("guaranteed") or []):
        kind, lo, hi, item = clamp(band)
        merge(kind, Rolls(seed_for(100 + i)).between(lo, hi), item)

    options = chest.get("options") or []
    total = sum(max(1, int(o["weight"])) for o in options)
    if options and total > 0:
        target = Rolls(seed_for(STREAM_PICK)).below(total)
        chosen = options[-1]
        for o in options:
            target -= max(1, int(o["weight"]))
            if target < 0:
                chosen = o
                break
        kind, lo, hi, item = clamp(chosen)
        merge(kind, Rolls(seed_for(STREAM_AMOUNT)).between(lo, hi), item)

    return drops


def roll_task(chest, player_key, period, key, task_id):
    subject = "%s:%d:%s" % (period, key, task_id)
    return roll(chest, lambda stream: subject_seed(player_key, "task", subject, stream))


#: Synthetic tiers: one whose floor and bonus share a kind (the merge), one with no bonus
#: slot (fixed), one with two guaranteed bands plus a utility (the item survives), and a
#: wide band (the modulo).
TIERS = [
    {"id": "wood", "chest": {
        "guaranteed": [{"kind": "credits", "min": 10, "max": 99}],
        "options": [
            {"kind": "credits", "min": 5, "max": 5, "weight": 2},
            {"kind": "hearts", "min": 1, "max": 3, "weight": 7},
            {"kind": "gems", "min": 1, "max": 2, "weight": 1},
        ]}},
    {"id": "silver", "chest": {
        "guaranteed": [{"kind": "gems", "min": 4, "max": 4}],
        "options": []}},
    {"id": "gold", "chest": {
        "guaranteed": [
            {"kind": "credits", "min": 100, "max": 100},
            {"kind": "utility", "item": "firepot", "min": 1, "max": 1},
        ],
        "options": [
            {"kind": "utility", "item": "firepot", "min": 2, "max": 2, "weight": 1},
            {"kind": "utility", "item": "surge", "min": 1, "max": 1, "weight": 1},
            {"kind": "heart_boost", "min": 6, "max": 12, "weight": 1},
        ]}},
    {"id": "royal", "chest": {
        "guaranteed": [{"kind": "credits", "min": 1, "max": 100000}],
        "options": [{"kind": "gems", "min": 1, "max": 50, "weight": 1}]}},
]

PLAYERS = ["", "uid_abc123", "player-7", "Ünïcödé", "x" * 40]
SUBJECTS = [
    ("daily", 0, "d_play"),
    ("daily", 20304, "d_play"),
    ("daily", 20304, "d_raiders"),
    ("daily", 20305, "d_play"),
    ("weekly", 2901, "w_win"),
    ("weekly", 2901, "d_play"),      # same id, other period: a different chest
    ("weekly", 2902, "w_win"),
    ("daily", 2901, "w_win"),        # a day key that equals a week key: a different chest
]


def cases():
    out = []
    for player in PLAYERS:
        for period, key, task in SUBJECTS:
            for tier in TIERS:
                out.append({
                    "name": "(%s)@%s:%d:%s#%s" % (player or "empty", period, key, task, tier["id"]),
                    "playerKey": player,
                    "period": period,
                    "key": key,
                    "taskId": task,
                    "tier": tier["id"],
                    "drops": roll_task(tier["chest"], player, period, key, task),
                })
    return out


COMMENT = [
    "The contract between TaskLedger/ChestSeed.cs and functions/src/tasks.ts.",
    "",
    "A task's chest is a chest seeded from a *subject* rather than a day: the account, the",
    "tag 'task', and '{period}:{key}:{taskId}'. Same generator as the daily chest, different",
    "first hash (subjectSeed / ChestRandom's tag constructor). Rolled on the client to show",
    "the reward and on the server to grant it, so the two have to agree byte for byte.",
    "",
    "The tiers below are synthetic, not the shipped ladder: what is pinned is the seeding, the",
    "stream numbers and the merge, so retuning progression.json must not turn these red.",
    "Generated by Tools/make_task_vectors.py, a third copy of the generator that is neither",
    "side - if both harnesses go red the contract moved; if one does, that side drifted.",
]

KEYS = ("_taskComment", "taskChestTiers", "taskChestCases")


def render(block):
    """The block as text, spliced before the document's closing brace so the rest of the
    file keeps the hand-written formatting it has."""
    body = json.dumps(block, indent=2, ensure_ascii=False)
    return "\n  " + body[1:-1].strip() + "\n"


def main():
    raw = io.open(VECTORS, encoding="utf-8").read()
    doc = json.loads(raw)
    block = {"_taskComment": COMMENT, "taskChestTiers": TIERS, "taskChestCases": cases()}

    if "--check" in sys.argv:
        for k, v in block.items():
            if doc.get(k) != v:
                sys.exit("reward-vectors.json '%s' differs from what this tool writes" % k)
        print("%d task chest vector(s) reproduce" % len(block["taskChestCases"]))
        return

    # A previous block is cut off at its comment; otherwise the closing brace is.
    marker = '  "_taskComment"'
    if marker in raw:
        head = raw[:raw.index(marker)].rstrip().rstrip(",")
    else:
        head = raw.rstrip()
        assert head.endswith("}")
        head = head[:-1].rstrip()

    text = head + "," + render(block) + "}\n"
    written = json.loads(text)                        # it has to still be a document
    for k in KEYS:
        assert written[k] == block[k]

    io.open(VECTORS, "w", encoding="utf-8", newline="\n").write(text)
    print("wrote %d task chest vector(s)" % len(block["taskChestCases"]))


if __name__ == "__main__":
    main()
