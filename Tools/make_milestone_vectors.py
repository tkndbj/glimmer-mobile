# -*- coding: utf-8 -*-
"""Writes the keeper-milestone vectors into `firebase/shared/grove-vectors.json`.

    python Tools/make_milestone_vectors.py          # regenerate the keeperMilestone* blocks
    python Tools/make_milestone_vectors.py --check  # prove the blocks on disk are what this writes

A keeper milestone pays a chest (invariant 57d), rolled by
`ChestDefinition.Roll(KeeperMilestoneLedger.SeedFor(level))` on the client and by
`rollMilestoneChest` in `functions/src/keeper.ts` on the server. The two are held together by
the vectors both sides run as a test (invariant 9a for a chest): `KeeperMilestoneTests`
through `TestJson` and `firebase/functions/test/keeper.mjs` through the compiled function.
This is another copy of the generator - FNV-1a over UTF-16 code units, xorshift32, the stream
numbers, the single weighted pick and the same-kind merge - kept here so the expected answers
are produced by something that is neither side. It shares `Tools/make_streak_vectors.py`'s
arithmetic on purpose rather than importing it: these files exist to be *independent*
readings of one rule.

What is pinned here that the other chest vectors cannot pin is the **subject**: the tag
`milestone` and the level alone, `'{level}'`. A server that folded the day into it would pay
a chest collected tomorrow differently from the one the ladder drew today.

Beside the price ladder's blocks in `grove-vectors.json` rather than in `reward-vectors.json`,
because the three chest generators there splice by cutting the file from their own marker to
its end and a fourth block after the streak's would be cut off by the next re-run. This tool
rewrites the whole document the way `make_keeper_vectors.py` does, so the two can run in any
order and both `--check`s hold.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VECTORS = ROOT / "firebase" / "shared" / "grove-vectors.json"
PROGRESSION = ROOT / "Assets" / "StreamingAssets" / "Content" / "progression.json"

MASK = 0xFFFFFFFF
FNV_OFFSET = 2166136261
FNV_PRIME = 16777619

STREAM_PICK = 0
STREAM_AMOUNT = 1

#: Matches `KeeperMilestoneLedger.SeedTag` and `KEEPER_MILESTONE_SEED_TAG`. Contract.
TAG = "milestone"


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


def roll_milestone(chest, player_key, level):
    """`KeeperMilestoneLedger.Subject`: the level alone."""
    subject = "%d" % level
    return roll(chest, lambda stream: subject_seed(player_key, TAG, subject, stream))


#: The streak vectors' synthetic tiers, unchanged: one whose floor and bonus share a kind (the
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

#: Neighbouring levels, so a seed that dropped the level would roll them the same; the first
#: milestone; a level past the shipped ladder's top, which is still a level; and one that
#: shares its digits with a day key, which must not matter because the tag separates them.
LEVELS = [4, 5, 8, 2, 70, 500, 20500]


def cases():
    out = []
    for player in PLAYERS:
        for level in LEVELS:
            for tier in TIERS:
                out.append({
                    "name": "(%s)@L%d#%s" % (player or "empty", level, tier["id"]),
                    "playerKey": player,
                    "level": level,
                    "tier": tier["id"],
                    "drops": roll_milestone(tier["chest"], player, level),
                })
    return out


def claim_ids():
    """`GrantEntry.KeeperMilestoneId` / `parseMilestoneClaim`: what both readers must agree on."""
    return [
        {"id": "milestone:4:credits", "level": 4, "currency": "credits"},
        {"id": "milestone:70:gems", "level": 70, "currency": "gems"},
        {"id": "milestone:2:credits", "level": 2, "currency": "credits"},
        {"id": "milestone:10000:credits", "level": 10000, "currency": "credits"},
        {"id": "milestone:1:credits", "invalid": True},          # nobody is paid for standing at the start
        {"id": "milestone:0:credits", "invalid": True},
        {"id": "milestone:-4:credits", "invalid": True},
        {"id": "milestone:04:credits", "invalid": True},         # two spellings would pay twice
        {"id": "milestone:4.5:credits", "invalid": True},
        {"id": "milestone:4:", "invalid": True},
        {"id": "milestone:4", "invalid": True},
        {"id": "milestone:4:credits:extra", "invalid": True},
        {"id": "milestone:10001:credits", "invalid": True},      # above the curve's own ceiling
        {"id": "keeper:1:4", "invalid": True},                   # the debit's prefix is not the grant's
        {"id": "streak:20500:4:credits", "invalid": True},
    ]


def rejected():
    """Blocks both readers must resolve to nothing, against the vector tiers."""
    return [
        {"name": "no rows", "block": {"rows": []}},
        {"name": "a level below 2", "block": {"rows": [{"level": 1, "tier": "wood"}]}},
        {"name": "rows that do not climb", "block": {"rows": [{"level": 8, "tier": "wood"}, {"level": 4, "tier": "wood"}]}},
        {"name": "a level twice", "block": {"rows": [{"level": 4, "tier": "wood"}, {"level": 4, "tier": "gold"}]}},
        {"name": "an unknown tier", "block": {"rows": [{"level": 4, "tier": "diamond"}]}},
        {"name": "a missing tier", "block": {"rows": [{"level": 4}]}},
        {"name": "an empty row", "block": {"rows": [None]}},
        {"name": "a level above the ceiling", "block": {"rows": [{"level": 10001, "tier": "wood"}]}},
        {"name": "too many rows", "block": {"rows": [{"level": 2 + i, "tier": "wood"} for i in range(65)]}},
        {"name": "not an object", "block": [{"level": 4, "tier": "wood"}]},
    ]


def shipped():
    block = json.loads(PROGRESSION.read_text(encoding="utf-8")).get("keeperMilestones")
    return block if block else None


COMMENT = [
    "The contract between KeeperMilestones.cs and functions/src/keeper.ts (invariant 57d).",
    "",
    "A keeper milestone pays a chest seeded from a *subject*: the account, the tag 'milestone'",
    "and the level alone. Same generator as the task, season and streak chests with a different",
    "subject - no day in it, so a chest collected a week after the level was reached rolls what",
    "the ladder drew the day it was reached.",
    "",
    "`keeperMilestoneChestTiers` are the streak vectors' synthetic tiers, not the shipped ladder:",
    "what is pinned is the seeding, the stream numbers and the merge, so retuning progression.json",
    "must not turn these red. `keeperMilestoneClaimIds` are grant ids both parsers must read alike,",
    "`keeperMilestoneRejected` are blocks both readers must resolve to nothing, and",
    "`keeperMilestoneShipped` is progression.json's own block, so a retune re-runs this tool.",
    "",
    "Written by Tools/make_milestone_vectors.py - a copy of the generator that is neither side.",
    "Do not hand-edit.",
]


def build(existing):
    out = dict(existing)
    out["_keeperMilestoneComment"] = COMMENT
    out["keeperMilestoneChestTiers"] = TIERS
    out["keeperMilestoneChestCases"] = cases()
    out["keeperMilestoneClaimIds"] = claim_ids()
    out["keeperMilestoneRejected"] = rejected()
    out["keeperMilestoneShipped"] = shipped()
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true",
                    help="prove the committed blocks are what this draws, and change nothing")
    args = ap.parse_args()

    existing = json.loads(VECTORS.read_text(encoding="utf-8"))
    built = build(existing)
    text = json.dumps(built, indent=2, ensure_ascii=False) + "\n"

    if args.check:
        if VECTORS.read_text(encoding="utf-8") != text:
            print("grove-vectors.json is not what make_milestone_vectors.py draws; re-run without "
                  "--check", file=sys.stderr)
            sys.exit(1)
        print(f"grove-vectors.json: milestone block is current ({len(built['keeperMilestoneChestCases'])} "
              f"cases, {len(built['keeperMilestoneClaimIds'])} ids, {len(built['keeperMilestoneRejected'])} refusals)")
        return

    VECTORS.write_text(text, encoding="utf-8")
    print(f"wrote {VECTORS.relative_to(ROOT)}: {len(built['keeperMilestoneChestCases'])} milestone cases")


if __name__ == "__main__":
    main()
