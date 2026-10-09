# -*- coding: utf-8 -*-
"""Draws a Shuffle hand - three upgrade cards over a held board - at the size a phone draws it.

    python Tools/render_shuffle.py                       # a common, a rare and an epic
    python Tools/render_shuffle.py --cards phoenix,sure_sight,heavy_bolts
    python Tools/render_shuffle.py --lang de             # the longest names are not English
    python Tools/render_shuffle.py --held heavy_bolts=2  # a card the build already holds
    python Tools/render_shuffle.py --out out/hand.png

**Why this exists.** `ShuffleHandLayout.IsClear` proves the four things on a card do not overlap
and the row fits its panel, and says nothing about whether a translated name still reads at the
floor size, whether a note wraps a third line under the tier, or whether a picture painted on
its own ground reads in the kit's seat. Only a picture with the shipped face can, so this
mirrors `ShuffleChoiceOverlay` and prints the size every caption settled at.

**It is a mirror, and mirrors drift.** Every constant is named after the field it copies
(`ShuffleHandLayout.CardWidth`, `SeatDown`, `TierDown`); when a device disagrees with this
picture, the picture is the one that is wrong.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

REPO = K.REPO
ART = REPO / "Assets" / "Game" / "Art"
LOC = REPO / "Assets" / "StreamingAssets" / "Content" / "loc"
LAYOUT = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Layout" / "ShuffleHandLayout.cs"
CATALOG = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Modes" / "Shuffle" / "ShuffleCard.cs"

W, H = K.W, K.H

#: `ShuffleChoiceOverlay.TintOf`, by tier.
TINTS = {"Common": K.MINT, "Rare": (79, 193, 255), "Epic": (180, 120, 255), "Legendary": K.GOLD}

#: `VictoryFrame`, which the hand wears since 2026-10-09 (the Deals sheet's frame): the crest's
#: reach above the window, the window's ink, the crown, the banner and the word's box.
CREST_REACH = 202.0
PANEL_INK = (150, 184, 176)
CROWN_Y, BANNER_Y, BANNER_W, BANNER_H = 114.0, -30.0, 566.0, 157.0
WORD_LIFT, WORD_W, WORD_H = 34.0, 356.0, 74.0


def layout():
    """The figures, read off the C# so the mirror cannot hold its own copy of them."""
    text = LAYOUT.read_text(encoding="utf-8")
    found = {}
    for name, value in re.findall(r"public (?:float|int) (\w+) = ([\d.]+)f?", text):
        found[name] = float(value)
    for line in re.findall(r"public (?:float|int) ([\w =\d.,f]+);", text):
        for part in line.split(","):
            if "=" in part:
                name, value = part.split("=")
                found[name.strip()] = float(value.strip().rstrip("f"))
    need = ("PanelWidth", "CrestFoot", "NoteY", "NoteHeightLine", "CardWidth", "CardGap", "CardHeight", "CardsTop", "Inset",
            "SeatSize", "IconSize", "SeatDown", "NameSize", "NameFloor", "NameHeight", "NameDown",
            "NoteSize", "NoteFloor", "NoteHeight", "NoteDown", "TierHeight", "TierDown",
            "TierSize", "TierFloor", "Foot")
    missing = [n for n in need if n not in found]
    if missing:
        sys.exit("ShuffleHandLayout.cs no longer carries %s" % ", ".join(missing))
    return found


def catalog():
    rows = re.findall(r'new ShuffleCard\("([a-z_]+)",\s*ShuffleTier\.(\w+)', CATALOG.read_text(encoding="utf-8"))
    return {cid: tier for cid, tier in rows}


def table(lang):
    rows = json.loads((LOC / ("%s.json" % lang)).read_text(encoding="utf-8"))["entries"]
    return {r["key"]: r["text"] for r in rows}


def wrapped(sheet, text, cx, top, box_w, box_h, size, floor, fill, outline, anchor_top):
    """`UIKit.Titled` with wrap on and `UIKit.Shrinkable`: shrunk until the lines fit the box."""
    while True:
        font = K.font(size)
        words, lines, line = text.split(), [], ""
        for word in words:
            trial = (line + " " + word).strip()
            if font.getlength(trial) <= box_w or not line:
                line = trial
            else:
                lines.append(line)
                line = word
        lines.append(line)
        pitch = size * 1.15
        if len(lines) * pitch <= box_h or size <= floor:
            break
        size -= 1
    pitch = size * 1.15
    start = top + pitch / 2 if anchor_top else top + box_h / 2 - (len(lines) - 1) * pitch / 2
    for i, each in enumerate(lines):
        K.text(sheet, each, cx, start + i * pitch, size, fill=fill, outline=outline)
    return size, len(lines)


def card(sheet, L, cid, tier, cx, top, strings, held):
    tint = TINTS[tier]
    cw, ch = L["CardWidth"], L["CardHeight"]
    K.paste(sheet, K.skin("Hud/card", cw, ch), cx, top + ch / 2)

    seat_y = top + L["SeatDown"]
    K.paste(sheet, K.glow(L["SeatSize"] * 1.9, 2.1, tint, 0.18 if tier == "Common" else 0.34), cx, seat_y)
    K.paste(sheet, K.skin("Hud/slot", L["SeatSize"], L["SeatSize"]), cx, seat_y)

    pic = ART / "Ui" / "Shuffle" / ("%s.png" % cid)
    if pic.exists():
        K.paste(sheet, K.fit(Image.open(pic).convert("RGBA"), (L["IconSize"], L["IconSize"])), cx, seat_y)

    name = strings.get("shuffle.%s.name" % cid, cid)
    size, lines = wrapped(sheet, name, cx, top + L["NameDown"] - L["NameHeight"] / 2,
                          cw - L["Inset"] * 2, L["NameHeight"], int(L["NameSize"]), int(L["NameFloor"]),
                          K.CREAM, 3, False)
    print("  %-16s name %-34r %2d lines at %dpx (floor %d)" % (cid, name, lines, size, L["NameFloor"]))

    note = strings.get("shuffle.%s.note" % cid, "")
    size, lines = wrapped(sheet, note, cx, top + L["NoteDown"] - L["NoteHeight"] / 2,
                          cw - L["Inset"] * 2, L["NoteHeight"], int(L["NoteSize"]), int(L["NoteFloor"]),
                          (255, 243, 220), 2, True)
    flag = "  !!" if size <= L["NoteFloor"] else ""
    print("  %-16s note %2d lines at %dpx (floor %d)%s" % ("", lines, size, L["NoteFloor"], flag))

    K.paste(sheet, K.skin("Hud/trough", cw - L["Inset"] * 2, L["TierHeight"]), cx, top + L["TierDown"])
    word = strings.get("ui.shuffle.tier.%s" % tier.lower(), tier).upper()
    if held:
        word = strings.get("ui.shuffle.held", "{0} x{1}").format(word, held + 1).upper()
    px = K.shrunk(sheet, word, cx, top + L["TierDown"], cw - L["Inset"] * 2 - 24, L["TierHeight"],
                  int(L["TierSize"]), int(L["TierFloor"]), fill=tint, outline=3)
    print("  %-16s tier %-22r at %dpx (floor %d)" % ("", word, px, L["TierFloor"]))


def screen(cards, lang, held, wave):
    L = layout()
    strings = table(lang)
    tiers = catalog()

    sheet = Image.new("RGBA", (W, H), (14, 20, 40, 255))
    # The held board under the scrim, as a flat band of the siege's ground colour: the panel is
    # what this mirror is about and the board is drawn by `render_siege.py`.
    scrim = Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .78)))
    sheet.alpha_composite(scrim)

    # `VictoryFrame.Build`, as `render_welcome.choice` draws it: the whole block on its own layer,
    # fitted to the canvas as `MakeFit` fits it.
    pw = L["PanelWidth"]
    ph = L["CardsTop"] + L["CardHeight"] + L["Foot"]
    reach = ph + CREST_REACH
    fit = min(1.0, (H - 20 * 2) / reach)

    block = Image.new("RGBA", (1680, int(reach + 500)), (0, 0, 0, 0))
    bw, bh = block.size
    bcx = bw / 2
    top = bh / 2 - CREST_REACH / 2 - ph / 2
    crest_cy = top - CREST_REACH / 2

    fan = K.rays(1680, 14)
    fan_rgba = Image.new("RGBA", fan.size, (255, 204, 77, 0))
    fan_rgba.putalpha(fan.point(lambda v: int(v * .20)))
    K.paste(block, fan_rgba, bcx, crest_cy + 240)
    K.paste(block, K.glow(1240, 2.4, (255, 209, 97), .22), bcx, crest_cy + 200)

    K.paste(block, K.tint(K.skin("Win/window", pw, ph), PANEL_INK), bcx, top + ph / 2)
    K.paste(block, K.glow(int(pw - 60), 1.9, (255, 245, 209), .11), bcx, top + 70)

    K.paste(block, K.fit(K.load("Win/crown")[0], (180, 162)), bcx, top - CROWN_Y)
    K.paste(block, K.fit(K.load("Win/banner")[0], (BANNER_W, BANNER_H)), bcx, top - BANNER_Y)
    # The banner carries the wave (one word, as DEALS and REWARD are) and the sentence sits
    # under it, where the Deals sheet prints its free line.
    word = strings.get("ui.shuffle.wave", "Wave {0}").format(wave).upper()
    px = K.shrunk(block, word, bcx, top - BANNER_Y - WORD_LIFT, WORD_W, WORD_H, 58, 32, outline=5)
    print("  word %-24r at %dpx (floor 32)%s" % (word, px, "  !!" if px <= 32 else ""))

    line = strings.get("ui.shuffle.choose", "Choose an upgrade")
    px = K.shrunk(block, line, bcx, top + L["NoteY"], pw - 80, L["NoteHeightLine"], 32, 20,
                  fill=(255, 245, 224), outline=2)
    print("  line %-28r at %dpx (floor 20)%s" % (line, px, "  !!" if px <= 20 else ""))

    pitch = L["CardWidth"] + L["CardGap"]
    left = bcx - (len(cards) - 1) * pitch / 2
    for i, cid in enumerate(cards):
        if cid not in tiers:
            sys.exit("'%s' is not a card the catalog names" % cid)
        card(block, L, cid, tiers[cid], left + i * pitch, top + L["CardsTop"], strings, held.get(cid, 0))

    if fit < 1.0:
        block = block.resize((int(bw * fit), int(bh * fit)), Image.LANCZOS)
    K.paste(sheet, block, W / 2, H / 2)
    print("  hand: window %.0fx%.0f (+%.0f crest) fitted at %.2f" % (pw, ph, CREST_REACH, fit))

    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--cards", default="heavy_bolts,chain_arc,second_wind")
    ap.add_argument("--lang", default="en")
    ap.add_argument("--held", default="", help="card=copies, comma separated")
    ap.add_argument("--wave", type=int, default=3)
    ap.add_argument("--out", type=Path, default=REPO / "Tools" / "out" / "shuffle_hand.png")
    args = ap.parse_args()

    held = {}
    for part in filter(None, args.held.split(",")):
        cid, n = part.split("=")
        held[cid] = int(n)

    out = screen(args.cards.split(","), args.lang, held, args.wave)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    main()
