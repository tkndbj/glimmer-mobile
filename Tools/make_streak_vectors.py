# -*- coding: utf-8 -*-
"""Writes the streak-chest vectors into `firebase/shared/reward-vectors.json`.

    python Tools/make_streak_vectors.py          # regenerate the streakChestCases block
    python Tools/make_streak_vectors.py --check  # prove the block on disk is what this writes

A streak night can pay a chest, and that chest is rolled by
`ChestDefinition.Roll(DailyStreak.SeedFor(...))` on the client and by `rollStreakChest` in
`functions/src/streak.ts` on the server. The two are held together by the vectors both sides
run as a test (invariant 9a for a chest). This is a *fifth* copy of the generator - FNV-1a
over UTF-16 code units, xorshift32, the stream numbers, the single weighted pick and the
same-kind merge - kept here so the expected answers are produced by something that is
neither side. If both harnesses go red at once after a change, the change moved the
contract; if only one does, that side drifted.

It shares `Tools/make_task_vectors.py`'s arithmetic on purpose rather than importing it:
these files exist to be *independent* readings of one rule, and a shared module would make
them one reading with several names.

What is pinned here that the task and season vectors cannot pin is the **subject**:
`{dayKey}:{night}`, the night's own calendar day rather than today's. A night collected a
week late has to roll what it always would have, and a server that reached for the day the
claim arrived on would pay a different chest from the one the board drew.

**Run this last.** Each of the three chest generators splices its block in by cutting the
file from its own marker to the end, so running `make_task_vectors.py` after this one would
take this block off with the season's. The order is task, then mark, then streak.
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

#: Matches `DailyStreak.SeedTag` and `STREAK_SEED_TAG`. Contract.
TAG = "streak"


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


def roll_night(chest, player_key, day_key, night):
    """`DailyStreak.Subject`: '{dayKey}:{night}'."""
    subject = "%d:%d" % (day_key, night)
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

PLAYERS = ["", "uid_abc123", "player-7", u"Ünïcödé", "x" * 40]

#: The cases that matter most are the first three: **one night, two days** and **one day,
#: two nights**. A seed that dropped either half of the subject would roll them the same,
#: and the symptom would be a player who collects two nights and is paid for one of them
#: twice. The rest cover a first night, a night past the end of the first lap (the subject
#: carries the absolute night, so night 8 is not night 1), and a night dated far in the past,
#: which is what a claim collected after a long time offline looks like.
SUBJECTS = [
    (20500, 3),
    (20501, 3),
    (20500, 4),
    (20500, 1),
    (20500, 8),
    (20500, 100),
    (20100, 3),
]


def cases():
    out = []
    for player in PLAYERS:
        for day, night in SUBJECTS:
            for tier in TIERS:
                out.append({
                    "name": "(%s)@%d:%d#%s" % (player or "empty", day, night, tier["id"]),
                    "playerKey": player,
                    "dayKey": day,
                    "night": night,
                    "tier": tier["id"],
                    "drops": roll_night(tier["chest"], player, day, night),
                })
    return out


COMMENT = [
    "The contract between DailyStreak/ChestSeed.cs and functions/src/streak.ts.",
    "",
    "A streak night can pay a chest, and it is a chest seeded from a *subject* rather than a",
    "day: the account, the tag 'streak', and '{dayKey}:{night}'. Same generator as the task",
    "and season chests with a different subject - the day is the *night's own* calendar day,",
    "never today's, so a night collected a week late rolls what it always would have.",
    "",
    "The tiers below are the task vectors' synthetic ones, not the shipped ladder: what is",
    "pinned is the seeding, the stream numbers and the merge, so retuning progression.json",
    "must not turn these red. Generated by Tools/make_streak_vectors.py, a fifth copy of the",
    "generator that is neither side - if both harnesses go red the contract moved; if one",
    "does, that side drifted.",
]

KEYS = ("_streakChestComment", "streakChestTiers", "streakChestCases")


def render(block):
    """The block as text, spliced before the document's closing brace so the rest of the
    file keeps the hand-written formatting it has."""
    body = json.dumps(block, indent=2, ensure_ascii=False)
    return "\n  " + body[1:-1].strip() + "\n"


def main():
    raw = io.open(VECTORS, encoding="utf-8").read()
    doc = json.loads(raw)
    block = {"_streakChestComment": COMMENT, "streakChestTiers": TIERS,
             "streakChestCases": cases()}

    if "--check" in sys.argv:
        for k, v in block.items():
            if doc.get(k) != v:
                sys.exit("reward-vectors.json '%s' differs from what this tool writes" % k)
        print("%d streak chest vector(s) reproduce" % len(block["streakChestCases"]))
        return

    # A previous block is cut off at its comment; otherwise the closing brace is.
    marker = '  "_streakChestComment"'
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
    print("wrote %d streak chest vector(s)" % len(block["streakChestCases"]))


if __name__ == "__main__":
    main()
