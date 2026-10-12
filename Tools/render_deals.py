"""Draws the limited-time deals panel (`DealOverlay`, invariant 60c) over the hub, for judging by eye.

    python Tools/render_deals.py                       # one deal, a day left
    python Tools/render_deals.py --deals 3 --urgent    # three rows, the first in its last hour
    python Tools/render_deals.py --deals 5 --states    # a scrolling list with a bought and an ended row
    python Tools/render_deals.py --welcome             # the unbought welcome bundle heading the list

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
CELL_W, CELL_H = 950.0, 330.0 + 30.0
PAD_TOP, PAD_BOTTOM = 6.0, 6.0
ROWS_TOP, TAIL, FOOT_H = 196.0, 30.0, 170.0  # the Daily Challenges deals sheet's layout
KEY_W, KEY_H, KEY_Y = 400.0, 110.0, 85.0

# VictoryFrame - the same constants `render_challenges.py` mirrors.
CREST_REACH = 202.0
PANEL_INK = (150, 184, 176)
CROWN_Y, BANNER_Y, BANNER_W, BANNER_H = 114.0, -30.0, 566.0, 157.0
WORD_LIFT, WORD_W, WORD_H = 34.0, 356.0, 74.0

# DealOverlay.DealRow - `DealCard.BuildOpen`: no box, the coffer big on a warm glow at the left, the
# seal over its corner, and a column on the right with the amount, COINS, the clock and the price key.
ROW_W, ROW_H = 940.0, 330.0
ART, ART_LEFT, GLOW, GLOW_INK, GLOW_ALPHA = 360.0, 20.0, 520.0, (255, 209, 97), .50
SEAL_X, SEAL_Y = 74.0, 70.0                     # the seal's middle, from the row's top-left
COLUMN_W, AMOUNT_SIZE = 520.0, 100
AMOUNT_Y, UNIT_Y, TIMER_Y, BUY_Y = 108.0, 44.0, -16.0, -108.0   # up from the row's middle
TIMER_W, TIMER_H, BUY_W, BUY_H = 360.0, 62.0, 400.0, 96.0
URGENT = (255, 92, 72)
GLYPH_GAP = 18.0                                # UIKit.GlyphGap

# DealOverlay.DealRow.RuleW / RuleH / Rule - the thin white rule on every row's top edge but the
# first, which is the middle of the air between two cards.
RULE_W, RULE_H, RULE_ALPHA = 860.0, 3.0, .85

LOCS = {e["key"]: e["text"] for e in json.loads(
    (K.REPO / "Assets/StreamingAssets/Content/loc/en.json").read_text(encoding="utf-8"))["entries"]}


def txt(key):
    return LOCS.get(key, key)


def timer(seconds):
    """`DealClock.Timer`."""
    s = max(0, int(seconds))
    d, h, m, sec = s // 86400, s % 86400 // 3600, s % 3600 // 60, s % 60
    return ("%dd " % d if d else "") + "%02d:%02d:%02d" % (h, m, sec)


def rule(sheet, cx, cy):
    """The rule above a row centred on (cx, cy): on the cell's top edge."""
    bar = Image.new("RGBA", (int(RULE_W), int(RULE_H)), (255, 255, 255, int(255 * RULE_ALPHA)))
    K.paste(sheet, bar, cx, cy - CELL_H / 2)


def bundle_value(product, store):
    """`DealValue.BundlePercent`: the bundle's gems and coins at the cheapest packs' prices, over
    its own price - floored to ten, nought at or under 100, capped at 9990. Passes are not packs."""
    packs = [p for p in store if not p.get("passDays")]
    gems = [p for p in packs if p.get("shelf") == "gems" and p.get("gems")]
    coins = [p for p in packs if p.get("shelf") == "coins" and p.get("credits")]
    if not gems or not coins:
        return 0
    g = min(gems, key=lambda p: p["referenceUsdCents"])
    c = min(coins, key=lambda p: p["referenceUsdCents"])
    worth = (product.get("credits", 0) * c["referenceUsdCents"] * g["gems"]
             + product.get("gems", 0) * g["referenceUsdCents"] * c["credits"])
    price = product["referenceUsdCents"] * c["credits"] * g["gems"]
    pct = worth * 100 // price
    return 0 if pct <= 100 else min(9990, pct - pct % 10)


def welcome_row(sheet, cx, cy, product, store, state):
    """`DealRow.Bind` for the welcome bundle - `DealCard.PaintBundle` and `PaintBundle`: the top
    bundle chest, the gems in their colour over the coins in gold, the value seal, ONE TIME ONLY in
    the trough and the store's price on the money face."""
    lx = cx - ROW_W / 2
    top = cy - ROW_H / 2
    ax = lx + ART_LEFT + ART / 2
    K.paste(sheet, K.glow(GLOW, 2.4, GLOW_INK, GLOW_ALPHA), ax, cy)
    K.paste(sheet, K.fit(Image.open(UI / "Shop" / "bundles_3.png").convert("RGBA"), (ART, ART)), ax, cy)

    pct = bundle_value(product, store)
    K.value_seal(sheet, lx + SEAL_X, top + SEAL_Y, pct, txt("ui.deal.value"))

    col = lx + ROW_W - COLUMN_W / 2
    # `DealCard.SetAmountGlyph`: the `Shop/gems_1` gem then the figure, centred as one block
    # (`UIKit.CentreGlyph`, gap `UIKit.GlyphGap`).
    said = compact(product["gems"])
    gem, art, gap = 84.0, 162.0, GLYPH_GAP           # DealCard.OpenGemSize / OpenGemArt
    wide = K.font(AMOUNT_SIZE).getlength(said)
    left = col - (gem + gap + wide) / 2
    ay = cy - AMOUNT_Y - round(AMOUNT_SIZE * .044)
    K.paste(sheet, K.fit(Image.open(UI / "Shop" / "gems_1.png").convert("RGBA"), (art, art)), left + gem / 2, cy - AMOUNT_Y)
    K.text(sheet, said, left + gem + gap + wide / 2, ay, AMOUNT_SIZE, fill=K.BLOOM, outline=5)
    K.text(sheet, txt("ui.shop.plus_coins").replace("{0}", compact(product["credits"])).upper(),
           col, cy - UNIT_Y, 34, fill=K.GOLD, outline=3)

    K.paste(sheet, K.skin("Hud/trough", TIMER_W, TIMER_H), col, cy - TIMER_Y)
    K.text(sheet, txt("ui.shop.badge_starter").upper(), col, cy - TIMER_Y, 36, fill=(255, 255, 255), outline=3)

    by = cy - BUY_Y
    pill = {"ready": "btn_orange", "bought": "btn_blue", "waiting": "btn_gray"}[state]
    word = {"ready": f"${product['referenceUsdCents'] / 100:.2f}",
            "bought": txt("ui.deal.bought_row").upper(),
            "waiting": txt("ui.shop.purchasing")}[state]
    K.paste(sheet, K.skin(pill, BUY_W, BUY_H), col, by)
    K.text(sheet, word, col, by - 2, 42 if state == "ready" else 38, fill=K.CREAM, outline=3)
    print(f"  welcome row: {product['id']}, {product['gems']} gems + {product['credits']:,} coins, "
          f"seal {pct}%, key {word!r}")


def compact(n):
    """`Compact.Number`: digits up to 9,999, then one tenth digit truncated - 22500 -> 22.5K."""
    if -9_999 <= n <= 9_999:
        return str(n)
    scale, suffix = (1_000_000, "M") if abs(n) >= 1_000_000 else (1_000, "K")
    whole = int(n / scale)
    tenth = abs(int((n % scale) * 10 / scale))
    return "%d%s" % (whole, suffix) if tenth == 0 else "%d.%d%s" % (whole, tenth, suffix)


def row(sheet, cx, cy, credits, gems, left, state):
    """One row, centred on (cx, cy) - `CellH` tall, the open card in its middle."""
    lx = cx - ROW_W / 2
    top = cy - ROW_H / 2
    ax = lx + ART_LEFT + ART / 2
    K.paste(sheet, K.glow(GLOW, 2.4, GLOW_INK, GLOW_ALPHA), ax, cy)
    K.paste(sheet, K.fit(Image.open(UI / "Shop" / "coins_3.png").convert("RGBA"), (ART, ART)), ax, cy)
    K.deal_seal(sheet, lx + SEAL_X, top + SEAL_Y, credits, gems, txt("ui.deal.value"))

    col = lx + ROW_W - COLUMN_W / 2
    K.text(sheet, f"{credits:,}", col, cy - AMOUNT_Y - round(AMOUNT_SIZE * .044), AMOUNT_SIZE, fill=K.SUN, outline=5)
    K.text(sheet, txt("ui.endless.coins").upper(), col, cy - UNIT_Y, 34, fill=K.CREAM, outline=3)

    K.paste(sheet, K.skin("Hud/trough", TIMER_W, TIMER_H), col, cy - TIMER_Y)
    fill = (158, 168, 184) if state == "ended" else URGENT if state == "ready" and left < 3600 else (255, 255, 255)
    K.text(sheet, timer(0 if state == "ended" else left), col, cy - TIMER_Y, 36, fill=fill, outline=3)

    by = cy - BUY_Y
    pill = {"ready": "btn_violet", "bought": "btn_blue", "ended": "btn_gray"}[state]
    K.paste(sheet, K.skin(pill, BUY_W, BUY_H), col, by)
    if state == "ready":
        K.paste(sheet, K.fit(Image.open(UI / "ic_gem.png").convert("RGBA"), (54, 54)), col - 56, by - 2)
        K.text(sheet, f"{gems:,}", col + 22, by - 2, 42, fill=K.CREAM, outline=3)
    else:
        word = txt("ui.deal.bought_row" if state == "bought" else "ui.deal.ended_row").upper()
        K.text(sheet, word, col, by - 2, 38, fill=K.CREAM, outline=3)


def panel(deals, welcome=None):
    """`DealOverlay` on `VictoryFrame`: the scrim, the fan and bloom, the green window with the
    crown and banner carrying the title, the deal rows, and the blue key at the foot. The fit is
    mirrored, so a tall list is drawn scaled as the device draws it."""
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.room(sheet)
    K.navbar(sheet, "home")
    sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .72))))

    lead = 1 if welcome else 0
    visible = min(lead + len(deals), MOST_ROWS)
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

    for i in range(visible):
        cy = top + ROWS_TOP + PAD_TOP + i * CELL_H + CELL_H / 2
        if i > 0:
            rule(block, bcx, cy)
        if welcome and i == 0:
            product, store, state = welcome
            welcome_row(block, bcx, cy, product, store, state)
            continue
        credits, gems, left, state = deals[i - lead]
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
    ap.add_argument("--welcome", choices=("ready", "bought", "waiting"), nargs="?", const="ready",
                    help="the welcome bundle heading the list (unbought), in the state given")
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

    welcome = None
    if args.welcome:
        store = json.loads((K.REPO / "Assets/StreamingAssets/Content/progression.json")
                           .read_text(encoding="utf-8"))["store"]["products"]
        starter = next(p for p in store if p.get("badge") == "starter" and p.get("kind") == "nonconsumable")
        welcome = (starter, store, args.welcome)

    out = panel(deals, welcome)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}")


if __name__ == "__main__":
    main()
