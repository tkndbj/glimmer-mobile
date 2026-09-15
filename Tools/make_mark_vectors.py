# -*- coding: utf-8 -*-
"""Writes the season-chest (mark) vectors into `firebase/shared/reward-vectors.json`.

    python Tools/make_mark_vectors.py          # regenerate the markChestCases block
    python Tools/make_mark_vectors.py --check  # prove the block on disk is what this writes

A season rung's chest is rolled by `ChestDefinition.Roll(SeasonLedger.SeedFor(...))` on the
client and by `rollMarkChest` in `functions/src/season.ts` on the server, and the two are
held together by the vectors both sides run as a test (invariant 9a for a chest). This is a
*fourth* copy of the generator — FNV-1a over UTF-16 code units, xorshift32, the stream
numbers, the single weighted pick and the same-kind merge — kept here so the expected
answers are produced by something that is neither side. If both harnesses go red at once
after a change, the change moved the contract; if only one does, that side drifted.

It shares `Tools/make_task_vectors.py`'s arithmetic on purpose rather than importing it:
these two files exist to be *independent* readings of one rule, and a shared module would
make them one reading with two names.

The tiers are the task vectors' synthetic ones, for their reason (the daily chest vectors'
rule): what is pinned is the seeding, the stream numbers and the merge, so retuning
`progression.json` must never turn the vectors red.
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

#: Matches `SeasonLedger.SeedTag` and `MARK_SEED_TAG`. Contract.
TAG = "mark"


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


def roll_bloom(chest, player_key, season, track, goal):
    """`SeasonLedger.Subject`: '{season}:{track}:{goal}'."""
    subject = "%s:%s:%d" % (season, track, goal)
    return roll(chest, lambda stream: subject_seed(player_key, TAG, subject, stream))


#: The task vectors' synthetic tiers, unchanged: one whose floor and bonus share a kind (the
#: merge), one with no bonus slot (fixed), one with two guaranteed bands plus a utility (the
#: item survives), and a wide band (the modulo).
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

#: The two tracks at one goal are the case that matters most: they are the same account, the
#: same season and the same rung, and they must roll *differently* or one claim would collect
#: both. The rest cover a first rung, a last rung and a second season with the same ladder.
SUBJECTS = [
    ("first_watch", "free", 5),
    ("first_watch", "pass", 5),
    ("first_watch", "free", 200),
    ("first_watch", "pass", 200),
    ("first_watch", "free", 100),
    ("second_watch", "free", 5),
    ("second_watch", "pass", 200),
]


def cases():
    out = []
    for player in PLAYERS:
        for season, track, goal in SUBJECTS:
            for tier in TIERS:
                out.append({
                    "name": "(%s)@%s:%s:%d#%s" % (player or "empty", season, track, goal, tier["id"]),
                    "playerKey": player,
                    "seasonId": season,
                    "track": track,
                    "goal": goal,
                    "tier": tier["id"],
                    "drops": roll_bloom(tier["chest"], player, season, track, goal),
                })
    return out


COMMENT = [
    "The contract between SeasonLedger/ChestSeed.cs and functions/src/season.ts.",
    "",
    "A season rung's chest is a chest seeded from a *subject* rather than a day: the account,",
    "the tag 'mark', and '{seasonId}:{track}:{goal}'. Same generator as the task chest with a",
    "different subject, so the two tracks at one rung roll differently - which is the whole",
    "reason the track is in the subject at all, since one claim would otherwise collect both.",
    "",
    "The tiers below are the task vectors' synthetic ones, not the shipped ladder: what is",
    "pinned is the seeding, the stream numbers and the merge, so retuning progression.json",
    "must not turn these red. Generated by Tools/make_mark_vectors.py, a fourth copy of the",
    "generator that is neither side - if both harnesses go red the contract moved; if one",
    "does, that side drifted.",
]

KEYS = ("_markComment", "markChestTiers", "markChestCases")


def render(block):
    """The block as text, spliced before the document's closing brace so the rest of the
    file keeps the hand-written formatting it has."""
    body = json.dumps(block, indent=2, ensure_ascii=False)
    return "\n  " + body[1:-1].strip() + "\n"


def main():
    raw = io.open(VECTORS, encoding="utf-8").read()
    doc = json.loads(raw)
    block = {"_markComment": COMMENT, "markChestTiers": TIERS, "markChestCases": cases()}

    if "--check" in sys.argv:
        for k, v in block.items():
            if doc.get(k) != v:
                sys.exit("reward-vectors.json '%s' differs from what this tool writes" % k)
        print("%d season chest vector(s) reproduce" % len(block["markChestCases"]))
        return

    # A previous block is cut off at its comment; otherwise the closing brace is.
    marker = '  "_markComment"'
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
    print("wrote %d season chest vector(s)" % len(block["markChestCases"]))


if __name__ == "__main__":
    main()
