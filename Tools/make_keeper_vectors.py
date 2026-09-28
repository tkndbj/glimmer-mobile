#!/usr/bin/env python3
"""Writes the keeper price ladder's shared vectors into firebase/shared/grove-vectors.json.

    python Tools/make_keeper_vectors.py            # rewrite the block
    python Tools/make_keeper_vectors.py --check    # prove the committed block is what this draws

**What is under contract.** A keeper level can be bought outright (invariant 57), and the price
of the level reached is decided twice: `KeeperLadder.PriceFor` on the device, which shows it and
debits it, and `keeperPrice` in `functions/src/keeper.ts`, which the server holds the debit to. A
drift is a purchase refused as underpaid - loud, because the client drops the debit and takes
the level back, but a player who has just been shown a price and lost it. So both runtimes are
held to the same cases, drawn here by a third copy written from the prose rule rather than from
either implementation.

Three blocks: the ladders the cases run on (`keeperPriceLadders`, one of which is the **shipped**
block copied out of `progression.json`, so a retune re-runs this tool), the price cases
(`keeperPriceCases`), the blocks that must resolve to *nothing* (`keeperPriceRejected`), and the spend ids
both sides must read identically (`keeperSpendIds`). Do not hand-edit.
"""

import argparse
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
VECTORS = ROOT / "firebase" / "shared" / "grove-vectors.json"
PROGRESSION = ROOT / "Assets" / "StreamingAssets" / "Content" / "progression.json"

# `KeeperLadderLimits` / `KEEPER_*` in keeper.ts.
LOWEST, MAX_TOP, MAX_ANCHORS, MAX_PRICE = 2, 200, 16, 10_000_000
CURRENCIES = ("credits", "gems")


def usable(block):
    """`KeeperLadder.Resolve` / `usableKeeperLadder`, from the prose. None sells nothing."""
    if not isinstance(block, dict):
        return None
    top = block.get("top")
    if not isinstance(top, int) or top < LOWEST or top > MAX_TOP:
        return None
    rows = block.get("anchors")
    if not isinstance(rows, list) or not rows or len(rows) > MAX_ANCHORS:
        return None
    out, last = [], None
    for row in rows:
        if not isinstance(row, dict):
            return None
        level, price, currency = row.get("level"), row.get("price"), row.get("currency")
        if not isinstance(level, int) or not isinstance(price, int):
            return None
        if level < LOWEST or level > top or price < 1 or price > MAX_PRICE:
            return None
        if currency not in CURRENCIES:
            return None
        if last and level <= last["level"]:
            return None
        if last and last["currency"] == currency and price < last["price"]:
            return None
        last = {"level": level, "currency": currency, "price": price}
        out.append(last)
    if last["level"] != top:
        return None
    return {"top": top, "anchors": out}


def between(a, b, step, steps):
    """Round-half-up interpolation in integers. Python's // floors, and the numerator is never
    negative (a rising band), so this is the same figure C#'s and JavaScript's truncation gives."""
    if steps <= 0:
        return a
    num = (b - a) * step
    return a + (2 * num + steps) // (2 * steps)


def price_of(ladder, level):
    """`KeeperLadder.PriceFor` / `keeperPrice`, from the prose. None when not sold."""
    if ladder is None or not isinstance(level, int):
        return None
    anchors = ladder["anchors"]
    if level < anchors[0]["level"] or level > ladder["top"]:
        return None
    i = 0
    while i + 1 < len(anchors) and anchors[i + 1]["level"] <= level:
        i += 1
    lower = anchors[i]
    if i + 1 >= len(anchors) or lower["level"] == level:
        return {"currency": lower["currency"], "price": lower["price"]}
    upper = anchors[i + 1]
    if lower["currency"] != upper["currency"]:
        return {"currency": lower["currency"], "price": lower["price"]}
    return {"currency": lower["currency"],
            "price": between(lower["price"], upper["price"],
                             level - lower["level"], upper["level"] - lower["level"])}


def shipped():
    block = json.loads(PROGRESSION.read_text(encoding="utf-8")).get("keeperLevels")
    if usable(block) is None:
        sys.exit("progression.json's keeperLevels block does not resolve; fix it before pinning it")
    return usable(block)


def ladders():
    return {
        "shipped": shipped(),
        # A coin band handing over to a gem band, with a rounding step that is not a whole number.
        "bands": {"top": 12, "anchors": [
            {"level": 2, "currency": "credits", "price": 100},
            {"level": 5, "currency": "credits", "price": 250},
            {"level": 6, "currency": "gems", "price": 10},
            {"level": 12, "currency": "gems", "price": 25},
        ]},
        # One anchor: everything sold at one price, and the top is the only level.
        "single": {"top": 3, "anchors": [{"level": 3, "currency": "gems", "price": 7}]},
        # A flat band (same price at both ends) and a level-two start.
        "flat": {"top": 4, "anchors": [
            {"level": 2, "currency": "credits", "price": 500},
            {"level": 4, "currency": "credits", "price": 500},
        ]},
    }


def cases(lads):
    out = []

    def case(name, ladder, level):
        got = price_of(lads[ladder], level)
        out.append({"name": name, "ladder": ladder, "level": level,
                    "sold": got is not None,
                    "currency": got["currency"] if got else None,
                    "price": got["price"] if got else 0})

    # ------------------------------------------------------------- the shipped ladder
    s = lads["shipped"]
    for anchor in s["anchors"]:
        case(f"shipped level {anchor['level']} is its anchor", "shipped", anchor["level"])
    for level in (3, 5, 9, 12, 15, 19, 25, 30, 35, 39, 45, 50, 55, 60, 65, 69):
        if LOWEST <= level <= s["top"]:
            case(f"shipped level {level} is drawn between its anchors", "shipped", level)
    case("shipped level 1 is nobody's purchase", "shipped", 1)
    case("shipped top plus one is not sold", "shipped", s["top"] + 1)
    case("shipped nought is not sold", "shipped", 0)
    case("shipped a negative level is not sold", "shipped", -4)

    # -------------------------------------------------------------- the hand-over
    case("the last coin level costs the coin anchor", "bands", 5)
    case("the first gem level costs the gem anchor", "bands", 6)
    case("a coin level between anchors is on the line", "bands", 3)
    case("a coin level with a half step rounds up", "bands", 4)       # 100 + 150*2/3 = 200
    case("a gem level between anchors rounds half up", "bands", 9)   # 10 + 15*3/6 = 17.5 -> 18
    case("a gem level just under the top", "bands", 11)              # 10 + 15*5/6 = 22.5 -> 23
    case("the top itself", "bands", 12)
    case("above the top is not sold", "bands", 13)
    case("below the first anchor is not sold", "bands", 1)

    # --------------------------------------------------------------- the edges
    case("a single anchor sells its one level", "single", 3)
    case("a single anchor sells nothing below it", "single", 2)
    case("a single anchor sells nothing above it", "single", 4)
    case("a flat band costs the same at its foot", "flat", 2)
    case("a flat band costs the same in the middle", "flat", 3)
    case("a flat band costs the same at its top", "flat", 4)
    return out


def rejected():
    """Blocks that resolve to *nothing* on both sides - each one a fault the reader refuses."""
    ok = {"top": 4, "anchors": [{"level": 2, "currency": "gems", "price": 5},
                                {"level": 4, "currency": "gems", "price": 9}]}
    return [
        {"name": "no block at all", "block": None},
        {"name": "an empty block", "block": {}},
        {"name": "a top with no anchors", "block": {"top": 4, "anchors": []}},
        {"name": "a top below the lowest level", "block": {"top": 1, "anchors": [{"level": 1, "currency": "gems", "price": 1}]}},
        {"name": "a top above the ceiling", "block": {"top": MAX_TOP + 1, "anchors": [{"level": MAX_TOP + 1, "currency": "gems", "price": 1}]}},
        {"name": "a last anchor that is not the top", "block": {"top": 5, "anchors": ok["anchors"]}},
        {"name": "anchors that do not climb", "block": {"top": 4, "anchors": [
            {"level": 4, "currency": "gems", "price": 5}, {"level": 2, "currency": "gems", "price": 9}]}},
        {"name": "two anchors on one level", "block": {"top": 4, "anchors": [
            {"level": 4, "currency": "gems", "price": 5}, {"level": 4, "currency": "gems", "price": 9}]}},
        {"name": "a band that gets cheaper", "block": {"top": 4, "anchors": [
            {"level": 2, "currency": "gems", "price": 9}, {"level": 4, "currency": "gems", "price": 5}]}},
        {"name": "a currency nobody spends", "block": {"top": 4, "anchors": [
            {"level": 2, "currency": "hearts", "price": 5}, {"level": 4, "currency": "hearts", "price": 9}]}},
        {"name": "a price of nought", "block": {"top": 4, "anchors": [
            {"level": 2, "currency": "gems", "price": 0}, {"level": 4, "currency": "gems", "price": 9}]}},
        {"name": "a price above the ceiling", "block": {"top": 4, "anchors": [
            {"level": 2, "currency": "gems", "price": 5}, {"level": 4, "currency": "gems", "price": MAX_PRICE + 1}]}},
        {"name": "an anchor above the top", "block": {"top": 4, "anchors": [
            {"level": 2, "currency": "gems", "price": 5}, {"level": 6, "currency": "gems", "price": 9}]}},
        {"name": "too many anchors", "block": {"top": MAX_ANCHORS + 2, "anchors": [
            {"level": i + 2, "currency": "gems", "price": i + 1} for i in range(MAX_ANCHORS + 1)]}},
    ]


def spend_ids():
    """`keeper:{ordinal}:{level}`, read by `SpendEntry.TryParseKeeperLevelId` and `parseKeeperSpendId`."""
    return [
        {"id": "keeper:1:2", "ordinal": 1, "level": 2},
        {"id": "keeper:3:17", "ordinal": 3, "level": 17},
        {"id": "keeper:69:70", "ordinal": 69, "level": 70},
        {"id": "keeper:1:1", "invalid": True},          # a purchase reaches at least ordinal + 1
        {"id": "keeper:2:2", "invalid": True},
        {"id": "keeper:0:5", "invalid": True},
        {"id": "keeper:01:5", "invalid": True},         # two spellings of one purchase
        {"id": "keeper:1:05", "invalid": True},
        {"id": "keeper:1", "invalid": True},
        {"id": "keeper:1:2:3", "invalid": True},
        {"id": "keeper::2", "invalid": True},
        {"id": "keeper:a:2", "invalid": True},
        {"id": "keeper:1:-2", "invalid": True},
        {"id": "keeper:1:2.0", "invalid": True},
        {"id": "keeper:1:9999999999", "invalid": True},  # ten digits is past the strict width
        {"id": "pass:watch_0001", "invalid": True},
        {"id": "", "invalid": True},
    ]


COMMENT = [
    "The keeper price ladder, derived twice and pinned here (invariant 57).",
    "",
    "A keeper level can be bought outright. The price of the level reached is decided on the",
    "device (KeeperLadder.PriceFor, which shows and debits it) and on the server (keeperPrice in",
    "keeper.ts, which holds the debit to it). A drift is a purchase refused as underpaid: the",
    "client drops the debit and takes the level back, so nothing is lost but a player's trust.",
    "",
    "`keeperPriceLadders` are the blocks the cases run on; `shipped` is progression.json's own, so",
    "a retune re-runs this tool. `keeperPriceCases` are prices, `keeperPriceRejected` are blocks",
    "both readers must resolve to nothing, and `keeperSpendIds` are debit ids both parsers must",
    "read alike. The curve blocks beside these (`keeperCurve`, `keeperCases`) are the level curve's.",
    "",
    "Written by Tools/make_keeper_vectors.py - a third implementation. Do not hand-edit.",
]


def build(existing):
    out = dict(existing)
    lads = ladders()
    out["_keeperPriceComment"] = COMMENT
    out["keeperPriceLadders"] = lads
    out["keeperPriceCases"] = cases(lads)
    out["keeperPriceRejected"] = rejected()
    out["keeperSpendIds"] = spend_ids()
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--check", action="store_true",
                    help="prove the committed block is what this draws, and change nothing")
    args = ap.parse_args()

    existing = json.loads(VECTORS.read_text(encoding="utf-8"))
    built = build(existing)
    text = json.dumps(built, indent=2, ensure_ascii=False) + "\n"

    if args.check:
        if VECTORS.read_text(encoding="utf-8") != text:
            print("grove-vectors.json is not what make_keeper_vectors.py draws; re-run without "
                  "--check", file=sys.stderr)
            sys.exit(1)
        print(f"grove-vectors.json: keeper block is current ({len(built['keeperPriceCases'])} cases, "
              f"{len(built['keeperPriceRejected'])} refusals, {len(built['keeperSpendIds'])} ids)")
        return

    VECTORS.write_text(text, encoding="utf-8")
    print(f"wrote {VECTORS.relative_to(ROOT)}: {len(built['keeperPriceCases'])} keeper cases")


if __name__ == "__main__":
    main()
