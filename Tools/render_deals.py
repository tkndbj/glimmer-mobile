"""Draws the limited-time deals panel (`DealOverlay`, invariant 60c) over the hub, for judging by eye.

    python Tools/render_deals.py                       # one deal, a day left
    python Tools/render_deals.py --deals 3 --urgent    # three rows, the first in its last hour
    python Tools/render_deals.py --deals 5 --states    # a scrolling list with a bought and an ended row

A mirror, not a gate: every number here is `DealOverlay`'s and `DealOverlay.DealRow`'s, and the
two are kept in step by hand (44d). The panel is `ModalView.MakePanel`'s: `panel_main` with the
orange ribbon title.
"""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import hudkit as K  # noqa: E402

W, H = K.W, K.H
UI = K.UI

# DealOverlay
PANEL_W = 1030.0
MOST_ROWS = 3
CELL_W, CELL_H = 950.0, 250.0
PAD_TOP, PAD_BOTTOM = 6.0, 6.0
FOOT_GAP, FOOT_H, FOOT_ROOM = 34.0, 100.0, 48.0
EDGE_ROOM = FOOT_GAP + FOOT_H + FOOT_ROOM     # the room above the list equals the room below it

# DealOverlay.DealRow
FRAME_W, FRAME_H, RIM = 930.0, 226.0, 11.0
ART, ART_X, BURST = 196.0, 130.0, 244.0
AMOUNT_X = 254.0
RIGHT_X, TIMER_W, TIMER_H, KEY_W, KEY_H = 186.0, 330.0, 72.0, 330.0, 104.0
URGENT = (255, 92, 72)

LOCS = {e["key"]: e["text"] for e in json.loads(
    (K.REPO / "Assets/StreamingAssets/Content/loc/en.json").read_text(encoding="utf-8"))["entries"]}


def txt(key):
    return LOCS.get(key, key)


def timer(seconds):
    """`DealClock.Timer`."""
    s = max(0, int(seconds))
    d, h, m, sec = s // 86400, s % 86400 // 3600, s % 3600 // 60, s % 60
    return ("%dd " % d if d else "") + "%02d:%02d:%02d" % (h, m, sec)


def row(sheet, cx, cy, credits, gems, left, state):
    K.paste(sheet, K.skin("Hud/plate_gold", FRAME_W, FRAME_H), cx, cy)
    K.paste(sheet, K.skin("Hud/plate_green", FRAME_W - 2 * RIM, FRAME_H - 2 * RIM), cx, cy)
    lx, rx = cx - FRAME_W / 2, cx + FRAME_W / 2

    ax = lx + ART_X
    K.paste(sheet, K.glow(round(BURST * 1.3), 2.0, K.SUN, .55), ax, cy)
    K.paste(sheet, K.tint(K.skin("Hud/burst", round(BURST), round(BURST)), K.AMBER).rotate(14, Image.BICUBIC), ax, cy)
    K.paste(sheet, K.fit(Image.open(UI / "Shop" / "coins_3.png").convert("RGBA"), (ART, ART)), ax, cy)

    K.text(sheet, f"{credits:,}", lx + AMOUNT_X, cy - round(74 * .044), 74, fill=K.SUN, outline=4, anchor="l")   # DealClock.DigitLift
    K.text(sheet, txt("ui.endless.coins").upper(), lx + AMOUNT_X, cy + 50, 34, fill=K.CREAM, outline=3, anchor="l")

    tx = rx - RIGHT_X
    K.paste(sheet, K.skin("Hud/trough", TIMER_W, TIMER_H), tx, cy - 54)
    label = timer(0 if state == "ended" else left)
    fill = (158, 168, 184) if state == "ended" else URGENT if state == "ready" and left < 3600 else (255, 255, 255)
    K.text(sheet, label, tx, cy - 54, 42, fill=fill, outline=3)

    pill = {"ready": "btn_violet", "bought": "btn_green", "ended": "btn_gray"}[state]
    K.paste(sheet, K.skin(pill, KEY_W, KEY_H), tx, cy + 48)
    if state == "ready":
        K.paste(sheet, K.fit(Image.open(UI / "ic_gem.png").convert("RGBA"), (54, 54)), tx - 58, cy + 46)
        K.text(sheet, f"{gems:,}", tx + 22, cy + 46, 42, fill=K.CREAM, outline=3)
    else:
        word = txt("ui.deal.bought_row" if state == "bought" else "ui.deal.ended_row").upper()
        K.text(sheet, word, tx, cy + 46, 40, fill=K.CREAM, outline=3)


def panel(deals):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.room(sheet)
    K.navbar(sheet, "home")
    sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .72))))

    visible = min(len(deals), MOST_ROWS)
    list_h = PAD_TOP + visible * CELL_H + PAD_BOTTOM
    y = EDGE_ROOM
    list_y = y; y += list_h + FOOT_GAP
    foot_y = y + FOOT_H / 2; y += FOOT_H + FOOT_ROOM
    ph = y

    top = H / 2 - ph / 2
    K.paste(sheet, K.skin("panel_main", PANEL_W, ph), W / 2, H / 2)
    rib = K.skin("ribbon_orange", PANEL_W * .78, 130).rotate(1.6, Image.BICUBIC, expand=True)
    K.paste(sheet, rib, W / 2, top - 22 + 65 - 65)
    K.text(sheet, txt("ui.deal.title").upper(), W / 2, top - 22 + 65 - 65 - 6, 54, outline=4)


    clip = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for i, (credits, gems, left, state) in enumerate(deals):
        cy = top + list_y + PAD_TOP + i * CELL_H + CELL_H / 2
        row(clip, W / 2, cy, credits, gems, left, state)
    mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(mask).rectangle([0, top + list_y, W, top + list_y + list_h], fill=255)
    sheet.paste(clip, (0, 0), Image.composite(clip, Image.new("RGBA", (W, H)), mask).split()[3])

    K.paste(sheet, K.skin("btn_red", 420, FOOT_H), W / 2, top + foot_y)
    K.text(sheet, txt("ui.common.cancel").upper(), W / 2, top + foot_y - 2, 36, outline=3)

    print(f"  panel {PANEL_W:.0f}x{ph:.0f}, {len(deals)} deal(s), {visible} visible")
    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--deals", type=int, default=1)
    ap.add_argument("--urgent", action="store_true", help="the first row in its last hour")
    ap.add_argument("--states", action="store_true", help="a bought second row and an ended third")
    ap.add_argument("--out", type=Path, default=Path("deals.png"))
    args = ap.parse_args()

    base = [(50000, 400, 93784), (26000, 250, 172800), (9000, 120, 4000), (75000, 900, 600000), (2500, 40, 36000)]
    deals = []
    for i in range(max(1, args.deals)):
        credits, gems, left = base[i % len(base)]
        if i == 0 and args.urgent:
            left = 1873
        state = "ready"
        if args.states and i == 1:
            state = "bought"
        if args.states and i == 2:
            state = "ended"
        deals.append((credits, gems, left, state))

    out = panel(deals)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}")


if __name__ == "__main__":
    main()
