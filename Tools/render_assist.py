"""Draws the helping hand's gift panel (`AssistGiftOverlay`, invariant 66) for judging by eye.

    python Tools/render_assist.py                 # the shipped gift, in English
    python Tools/render_assist.py --lang de       # another table's words
    python Tools/render_assist.py --measure       # every Latin table; exit 1 on a caption at its floor

A mirror, not a gate for anything but `--measure`: every number here is `AssistGiftOverlay`'s, and
the two are kept in step by hand (44d). The frame and the bands are `GiftOverlay`'s, which
`render_gift.py` mirrors; the cards are `AssistGiftOverlay.BuildCard`: no plate, a glow in the
item's shop colour behind its own picture drawn large, and "x1" on one line beside it. The gift drawn is what
`progression.json`'s `assist` block hands over. The Arabic table is shaped at runtime, which
Pillow cannot mirror, so `--measure` skips it and `ArabicTextTests` carries that table.
"""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import hudkit as K  # noqa: E402

W, H = K.W, K.H
UI = K.UI

# AssistGiftOverlay
PANEL_W = 900.0                                   # VictoryFrame.PanelWidth
ART_Y, ART = 170.0, 220.0
BODY_Y, BODY_W, BODY_H, BODY_SIZE, BODY_FLOOR = 340.0, 740.0, 110.0, 34, 24
CARDS_TOP, CARD_H, CARD_GAP, CARD_MAX_W, CARDS_ROOM = 430.0, 250.0, 40.0, 300.0, 780.0
KEY_W, KEY_H, KEY_Y, FOOT_H, TAIL = 440.0, 120.0, 85.0, 170.0, 30.0
PANEL_H = CARDS_TOP + CARD_H + TAIL + FOOT_H

# AssistGiftOverlay.BuildCard (centres from the card's top edge).
ICON_MAX, ICON_SHARE, INSET, ICON_Y = 200.0, .66, 10.0, CARD_H / 2
COUNT_Y, COUNT_SIZE, COUNT_FLOOR, COUNT_GAP = 160.0, 60, 34, -20.0

# VictoryFrame - the same constants `render_gift.py` mirrors.
CREST_REACH = 202.0
PANEL_INK = (150, 184, 176)
CROWN_Y, BANNER_Y, BANNER_W, BANNER_H = 114.0, -30.0, 566.0, 157.0
WORD_LIFT, WORD_W, WORD_H, WORD_SIZE, WORD_FLOOR = 34.0, 356.0, 74.0, 58, 32

LATIN = ("en", "es", "pt", "fr", "de", "it", "tr", "pl")
CONTENT = K.REPO / "Assets/StreamingAssets/Content"


def locs(lang):
    table = json.loads((CONTENT / "loc" / f"{lang}.json").read_text(encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


def gift():
    """What the shipped `assist` block hands over, as (id, count) rows."""
    progression = json.loads((CONTENT / "progression.json").read_text(encoding="utf-8"))
    return [(row["id"], row["count"]) for row in progression["assist"]["gift"]]


def upper(s, lang):
    """`Loc.Upper`: the Turkish dotted capital, everything else as Python capitalises it."""
    return s.replace("i", "İ").upper() if lang == "tr" else s.upper()


def panel(lang, draw=True):
    """`AssistGiftOverlay` on `VictoryFrame`, over the hub. Answers the sheet and, per caption, the
    size it settled on against its floor."""
    txt = locs(lang)
    rows = gift()
    sizes = {}

    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.room(sheet)
    K.navbar(sheet, "home")
    sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .78))))

    ph = PANEL_H
    reach = ph + CREST_REACH
    fit = min(1.0, (H - 40) / reach)

    block = Image.new("RGBA", (1680, int(reach + 500)), (0, 0, 0, 0))
    bw, bh = block.size
    bcx = bw / 2
    top = bh / 2 - CREST_REACH / 2 - ph / 2
    crest = top - CREST_REACH / 2

    fan = K.rays(1680, 14)
    fan_rgba = Image.new("RGBA", fan.size, (255, 204, 77, 0))
    fan_rgba.putalpha(fan.point(lambda v: int(v * .20)))
    K.paste(block, fan_rgba, bcx, crest + 240)
    K.paste(block, K.glow(1240, 2.4, (255, 209, 97), .22), bcx, crest + 200)

    K.paste(block, K.tint(K.skin("Win/window", PANEL_W, ph), PANEL_INK), bcx, top + ph / 2)
    K.paste(block, K.glow(int(PANEL_W - 60), 1.9, (255, 245, 209), .11), bcx, top + 70)

    K.paste(block, K.fit(K.load("Win/crown")[0], (180, 162)), bcx, top - CROWN_Y)
    K.paste(block, K.fit(K.load("Win/banner")[0], (BANNER_W, BANNER_H)), bcx, top - BANNER_Y)
    sizes["title"] = (K.shrunk(block, txt["ui.assist.title"], bcx, top - BANNER_Y - WORD_LIFT,
                               WORD_W, WORD_H, WORD_SIZE, WORD_FLOOR, outline=5), WORD_FLOOR)

    K.paste(block, K.glow(int(ART * 1.9), 2.1, K.GOLD, .38), bcx, top + ART_Y)
    K.paste(block, K.fit(Image.open(UI / "ic_gift.png").convert("RGBA"), (ART, ART)), bcx, top + ART_Y)
    sizes["body"] = (K.shrunk(block, txt["ui.assist.body"], bcx, top + BODY_Y, BODY_W, BODY_H,
                              BODY_SIZE, BODY_FLOOR, outline=3), BODY_FLOOR)

    n = len(rows)
    card_w = min(CARD_MAX_W, (CARDS_ROOM - CARD_GAP * (n - 1)) / n)
    side = min(ICON_MAX, card_w * ICON_SHARE)
    icon_x = -card_w / 2 + INSET + side / 2
    count_w = card_w / 2 - (icon_x + side / 2) - COUNT_GAP
    count_left = card_w / 2 - count_w                  # TextAnchor.MiddleLeft
    step = card_w + CARD_GAP
    x = bcx - step * (n - 1) / 2
    for item, count in rows:
        ctop = top + CARDS_TOP
        K.paste(block, K.glow(int(card_w), 2.1, K.GOLD, .30), x + icon_x, ctop + CARD_H / 2)
        icon = Image.open(UI / "Utility" / f"{item}.png").convert("RGBA")
        K.paste(block, K.fit(icon, (side, side)), x + icon_x, ctop + ICON_Y)
        label = f"x{count}"
        got = COUNT_SIZE
        while got > COUNT_FLOOR and K.font(got).getlength(label) > count_w:
            got -= 1
        K.text(block, label, x + count_left + K.font(got).getlength(label) / 2, ctop + COUNT_Y, got, outline=3)
        sizes[item] = (got, COUNT_FLOOR)
        x += step

    ky = top + ph - KEY_Y
    K.paste(block, K.skin("btn_orange", KEY_W, KEY_H), bcx, ky)
    K.text(block, upper(txt["ui.assist.claim"], lang), bcx, ky - 2, 44, outline=3)

    if fit < 1.0:
        block = block.resize((int(bw * fit), int(bh * fit)), Image.LANCZOS)
    K.paste(sheet, block, W / 2, H / 2)

    return sheet.convert("RGB"), sizes


def measure():
    tight = 0
    for lang in LATIN:
        _, sizes = panel(lang, draw=False)
        line = "  %-2s" % lang
        for part, (got, floor) in sizes.items():
            flag = " TIGHT" if got <= floor else ""
            tight += bool(flag)
            line += "  %s %d/%d%s" % (part, got, floor, flag)
        print(line)
    print("  ar  not drawn here (shaped at runtime; see ArabicTextTests)")
    return tight


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--lang", default="en", choices=LATIN)
    ap.add_argument("--measure", action="store_true", help="every Latin table; exit 1 on a caption at its floor")
    ap.add_argument("--out", type=Path, default=Path("assist.png"))
    args = ap.parse_args()

    if args.measure:
        tight = measure()
        print("  %d caption(s) at their floor" % tight)
        sys.exit(1 if tight else 0)

    out, sizes = panel(args.lang)
    for part, (got, floor) in sizes.items():
        print("  %-10s %d (floor %d)" % (part, got, floor))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print("  wrote %s" % args.out)


if __name__ == "__main__":
    main()
