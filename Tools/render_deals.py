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
CELL_W, CELL_H = 950.0, 190.0 + (K.DEAL_SEAL_SIZE / 2 - K.DEAL_SEAL_DROP) + 18.0
PAD_TOP, PAD_BOTTOM = 6.0, 6.0
ROWS_TOP, TAIL, FOOT_H = 196.0, 30.0, 170.0  # the Daily Challenges deals sheet's layout
KEY_W, KEY_H, KEY_Y = 400.0, 110.0, 85.0

# VictoryFrame - the same constants `render_challenges.py` mirrors.
CREST_REACH = 202.0
PANEL_INK = (150, 184, 176)
CROWN_Y, BANNER_Y, BANNER_W, BANNER_H = 114.0, -30.0, 566.0, 157.0
WORD_LIFT, WORD_W, WORD_H = 34.0, 356.0, 74.0

# DealOverlay.DealRow - a `DealCard` at the foot of the row; the clock over the price key on its right.
CARD_W, CARD_H, AMOUNT_W = 940.0, 190.0, 380.0
RIGHT_X, TIMER_W, TIMER_H, KEY_W, KEY_H = 172.0, 300.0, 58.0, 300.0, 82.0
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
    """One row, centred on (cx, cy) - `CellH` tall, the card at its foot."""
    ccy = cy + CELL_H / 2 - CARD_H / 2 - 4
    _lx, rx = K.deal_card(sheet, cx, ccy, CARD_W, CARD_H, credits, gems,
                          txt("ui.deal.value"), txt("ui.endless.coins").upper(), AMOUNT_W)

    tx = rx - RIGHT_X
    K.paste(sheet, K.skin("Hud/trough", TIMER_W, TIMER_H), tx, ccy - 40)
    fill = (158, 168, 184) if state == "ended" else URGENT if state == "ready" and left < 3600 else (255, 255, 255)
    K.text(sheet, timer(0 if state == "ended" else left), tx, ccy - 40, 36, fill=fill, outline=3)

    pill = {"ready": "btn_violet", "bought": "btn_blue", "ended": "btn_gray"}[state]
    K.paste(sheet, K.skin(pill, KEY_W, KEY_H), tx, ccy + 36)
    if state == "ready":
        K.paste(sheet, K.fit(Image.open(UI / "ic_gem.png").convert("RGBA"), (48, 48)), tx - 50, ccy + 34)
        K.text(sheet, f"{gems:,}", tx + 20, ccy + 34, 38, fill=K.CREAM, outline=3)
    else:
        word = txt("ui.deal.bought_row" if state == "bought" else "ui.deal.ended_row").upper()
        K.text(sheet, word, tx, ccy + 34, 36, fill=K.CREAM, outline=3)


def panel(deals):
    """`DealOverlay` on `VictoryFrame`: the scrim, the fan and bloom, the green window with the
    crown and banner carrying the title, the deal rows, and the blue key at the foot. The fit is
    mirrored, so a tall list is drawn scaled as the device draws it."""
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.room(sheet)
    K.navbar(sheet, "home")
    sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .72))))

    visible = min(len(deals), MOST_ROWS)
    list_h = PAD_TOP + visible * CELL_H + PAD_BOTTOM
    ph = ROWS_TOP + list_h + TAIL + FOOT_H

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
    K.shrunk(block, txt("ui.deal.title"), bcx, top - BANNER_Y - WORD_LIFT, WORD_W, WORD_H, 58, 32, outline=5)

    for i, (credits, gems, left, state) in enumerate(deals[:visible]):
        cy = top + ROWS_TOP + PAD_TOP + i * CELL_H + CELL_H / 2
        row(block, bcx, cy, credits, gems, left, state)

    ky = top + ph - KEY_Y
    K.paste(block, K.skin("btn_blue", KEY_W, KEY_H), bcx, ky)
    K.text(block, txt("ui.common.cancel").upper(), bcx, ky - 2, 36, outline=3)

    if fit < 1.0:
        block = block.resize((int(bw * fit), int(bh * fit)), Image.LANCZOS)
    K.paste(sheet, block, W / 2, H / 2)

    print(f"  panel {PANEL_W:.0f}x{ph:.0f} (fit {fit:.2f}), {len(deals)} deal(s), {visible} visible")
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
