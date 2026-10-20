#!/usr/bin/env python3
"""Draws a Shuffle refresh's odds panel (`ShuffleOddsOverlay`, MODES.md 59p) without Unity.

The panel's figures are read off `ShuffleOddsOverlay.cs`, the words off `loc/*.json`, and the
odds off `shuffle.json`'s opening table (or the heated/wild one with --hand), so the mirror holds
no copy of any of them. `--measure` draws every language and fails on a caption at its floor,
which is the one fault a compile and a test cannot see.

    python Tools/render_shuffle_odds.py                      # English, hand 5 (the epic promise)
    python Tools/render_shuffle_odds.py --lang de --hand 1
    python Tools/render_shuffle_odds.py --measure            # every language, fail on a floor
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import hudkit as K                                           # noqa: E402

REPO = K.REPO
OVERLAY = REPO / "Assets" / "Game" / "Scripts" / "Presentation" / "Screens" / "ShuffleOddsOverlay.cs"
LOC = REPO / "Assets" / "StreamingAssets" / "Content" / "loc"
TUNING = REPO / "firebase" / "seed" / "shuffle.json"
W, H = K.W, K.H

TIERS = ["common", "rare", "epic", "legendary"]
# `ShuffleChoiceOverlay.TintOf`: Pal.Mint, Pal.Azure, Pal.Foxglove, Pal.Gold.
TINT = {"common": K.MINT, "rare": (79, 193, 255), "epic": (180, 120, 255), "legendary": K.GOLD}
INK, HEAD, AMBER = (59, 38, 26), (71, 46, 31), (199, 107, 26)


def figures():
    """Every `X = n` constant on the overlay, read off the C#."""
    text = OVERLAY.read_text(encoding="utf-8")
    return {k: float(v) for k, v in re.findall(r"(\w+) = (-?[\d.]+)f", text)}


def strings(lang):
    entries = json.loads((LOC / f"{lang}.json").read_text(encoding="utf-8"))["entries"]
    return {e["key"]: e["text"] for e in entries}


def upper(s, lang):
    """`Loc.Upper`: Turkish dotted i to dotted capital."""
    return s.replace("i", "İ").upper() if lang == "tr" else s.upper()


def odds_for(hand):
    """`ShuffleOdds.For` on a fresh build: the hand's table off shuffle.json, and its promise."""
    deck = json.loads(TUNING.read_text(encoding="utf-8"))["deck"]
    table = deck["wild"] if hand >= deck["wildFromHand"] else deck["heated"] if hand >= deck["heatedFromHand"] else deck["opening"]
    floor = "epic" if hand % deck["epicEveryHands"] == 0 else "rare" if hand % deck["rareEveryHands"] == 0 else None
    return table, floor


#: `ShuffleSchools.AffinityStep` and its ceiling, the figures the lean's line prints (MODES.md 59r).
SCHOOLS = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Modes" / "Shuffle" / "ShuffleSchool.cs"


def lean_figures():
    import re
    text = SCHOOLS.read_text(encoding="utf-8")
    step = int(re.search(r"AffinityStep = (\d+);", text).group(1))
    most = int(re.search(r"AffinityMostPoints = (\d+);", text).group(1))
    return step, step * most


def draw(lang, hand, cards, leaning=False):
    F = figures()
    s = strings(lang)
    table, floor = odds_for(hand)
    floored = []

    def caption(key, text, cx, cy, w, h, size, low, fill, outline=0):
        px = K.shrunk(sheet, text, cx, cy, w, h, size, low, fill=fill, outline=outline)
        if px <= low:
            floored.append(f"{lang} {key} '{text}' at its floor {low}")

    ph = (F["HeadRoom"] + F["HeaderH"] + 4 * (F["RowH"] + F["RowGap"]) + (F["FloorH"] if floor else 0) + F["NoteH"]
          + (F["LeanH"] if leaning else 0) + F["FootRoom"])
    pw = F["PanelW"]

    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .72))))

    top = H / 2 - ph / 2
    cx = W / 2
    K.paste(sheet, K.skin("panel_main", pw, ph), cx, H / 2)
    rib = K.skin("ribbon_orange", pw * .78, 130).rotate(1.6, Image.BICUBIC, expand=True)
    K.paste(sheet, rib, cx, top - 22)
    caption("title", s["ui.shuffle.odds_title"], cx, top - 28, pw * .78 - 120, 70, 54, 30, K.CREAM, 4)

    y = top + F["HeadRoom"]
    for key, x, w in (("ui.shuffle.odds_rarity", F["RarityX"], F["RarityW"]),
                      ("ui.shuffle.odds_chance", F["ChanceX"], F["ChanceW"]),
                      ("ui.shuffle.odds_left", F["LeftX"], F["LeftW"])):
        caption(key, upper(s[key], lang), cx + x, y + F["HeaderH"] / 2, w, F["HeaderH"], 26, 16, HEAD)
    y += F["HeaderH"]

    for t, tier in enumerate(TIERS):
        mid = y + F["RowH"] / 2
        K.paste(sheet, K.skin("Hud/trough", F["RarityW"], F["RowH"] - 12), cx + F["RarityX"], mid)
        caption("tier." + tier, upper(s["ui.shuffle.tier." + tier], lang), cx + F["RarityX"], mid,
                F["RarityW"] - 28, F["RowH"] - 12, 30, 18, TINT[tier], 3)
        K.text(sheet, s["ui.shuffle.percent"].format(table[t]), cx + F["ChanceX"], mid, 38, fill=AMBER, outline=0)
        K.text(sheet, str(cards[t]), cx + F["LeftX"], mid, 34, fill=INK, outline=0)
        y += F["RowH"] + F["RowGap"]

    if floor:
        line = s["ui.shuffle.odds_floor"].format(s["ui.shuffle.tier." + floor])
        caption("odds_floor", line, cx, y + F["FloorH"] / 2, F["BodyW"], F["FloorH"], 30, 20, INK)
        y += F["FloorH"]

    # `ShuffleOddsOverlay`: the lean's line in the note's place when a school leans (MODES.md 59r).
    if leaning:
        tall = F["NoteH"] + F["LeanH"]
        caption("odds_lean", s["ui.shuffle.odds_lean"].format(*lean_figures()), cx, y + tall / 2,
                F["BodyW"], tall, 26, 17, INK)
    else:
        caption("odds_equal", s["ui.shuffle.odds_equal"], cx, y + F["NoteH"] / 2, F["BodyW"], F["NoteH"], 28, 18, INK)

    btn_y = top + ph - 92
    K.paste(sheet, K.skin("btn_green", 560, 120), cx, btn_y)
    caption("got_it", s["ui.common.got_it"], cx, btn_y - 120 * K.PILL_FACE_LIFT, 520, 80, 44, 24, K.CREAM, 3)

    return sheet.convert("RGB"), floored


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--lang", default="en")
    ap.add_argument("--hand", type=int, default=5, help="hand number: 1 opening, 6+ heated, 10+ wild; 3/5 carry a promise")
    ap.add_argument("--cards", default="22,22,13,8", help="cards left per rarity, lowest first")
    ap.add_argument("--measure", action="store_true", help="every language and the promise line; fail on a caption at its floor")
    ap.add_argument("--lean", action="store_true", help="draw the line a started school's lean adds (MODES.md 59r)")
    ap.add_argument("--out", type=Path, default=REPO / "Tools" / "out" / "shuffle_odds.png")
    args = ap.parse_args()

    cards = [int(n) for n in args.cards.split(",")]

    if args.measure:
        floored = []
        for lang in sorted(p.stem for p in LOC.glob("*.json")):
            for hand in (1, 3, 5):
                for leaning in (False, True):
                    floored += draw(lang, hand, cards, leaning)[1]
        if floored:
            sys.exit("captions at their floor:\n  " + "\n  ".join(sorted(set(floored))))
        print("odds panel: every caption fits in every language")
        return

    out, floored = draw(args.lang, args.hand, cards, args.lean)
    for f in floored:
        print("  !!", f)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}  {out.width}x{out.height}  - look at it")


if __name__ == "__main__":
    main()
