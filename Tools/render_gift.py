"""Draws the gift panel (`GiftOverlay`, invariant 63) over the hub, for judging by eye.

    python Tools/render_gift.py                         # coins and gems
    python Tools/render_gift.py --credits 2500          # coins alone
    python Tools/render_gift.py --gems 150 --credits 0  # gems alone
    python Tools/render_gift.py --lang tr               # another table's words

A mirror, not a gate: every number here is `GiftOverlay`'s, and the two are kept in step by hand
(44d). The frame is `VictoryFrame`'s (the constants `render_deals.py` mirrors); the cards are the
ad offer's reward card (`AdOfferOverlay.BuildRewardCard`).
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

# GiftOverlay
PANEL_W = 900.0                                   # VictoryFrame.PanelWidth
ART_Y, ART = 170.0, 220.0
BODY_Y, BODY_W, BODY_H = 340.0, 740.0, 110.0
CARDS_TOP, CARD, CARD_GAP = 410.0, 300.0, 40.0
KEY_W, KEY_H, KEY_Y, FOOT_H, TAIL = 440.0, 120.0, 85.0, 170.0, 40.0
PANEL_H = CARDS_TOP + CARD + TAIL + FOOT_H

# VictoryFrame - the same constants `render_deals.py` mirrors.
CREST_REACH = 202.0
PANEL_INK = (150, 184, 176)
CROWN_Y, BANNER_Y, BANNER_W, BANNER_H = 114.0, -30.0, 566.0, 157.0
WORD_LIFT, WORD_W, WORD_H = 34.0, 356.0, 74.0

# AdOfferOverlay.BuildRewardCard: the plate, the mark, the amount, the name.
CARD_INK, CARD_ALPHA = (10, 23, 31), .82
ICON_W, ICON_H, ICON_Y = 176.0, 114.0, 68.0
AMOUNT_Y, AMOUNT_SIZE, KIND_Y, KIND_SIZE = 166.0, 52, 218.0, 28


def locs(lang):
    table = json.loads((K.REPO / "Assets/StreamingAssets/Content/loc" / f"{lang}.json").read_text(encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


def compact(n):
    """`Compact.Number`: digits up to 9,999, then one tenth digit truncated - 22500 -> 22.5K."""
    if -9_999 <= n <= 9_999:
        return str(n)
    scale, suffix = (1_000_000, "M") if abs(n) >= 1_000_000 else (1_000, "K")
    whole = int(n / scale)
    tenth = abs(int((n % scale) * 10 / scale))
    return "%d%s" % (whole, suffix) if tenth == 0 else "%d.%d%s" % (whole, tenth, suffix)


def card(sheet, cx, top, kind, amount, txt):
    """One reward card centred on `cx` with its top edge at `top`."""
    tint = K.GOLD if kind == "credits" else K.BLOOM
    cy = top + CARD / 2
    K.paste(sheet, K.glow(int(CARD * 1.1), 2.1, tint, .30), cx, cy)
    K.paste(sheet, K.round_rect(CARD, CARD, 26, CARD_INK, CARD_ALPHA), cx, cy)
    K.paste(sheet, K.round_rect(CARD, CARD, 26, tint, .55, width=3), cx, cy)

    icon = Image.open(UI / "Coin" / "f0.png" if kind == "credits" else UI / "ic_gem.png").convert("RGBA")
    K.paste(sheet, K.fit(icon, (ICON_W, ICON_H)), cx, top + ICON_Y + ICON_H / 2)
    K.text(sheet, "+" + compact(amount), cx, top + AMOUNT_Y + 31, AMOUNT_SIZE, outline=3)
    K.text(sheet, txt("ui.reward." + kind), cx, top + KIND_Y + 22, KIND_SIZE, fill=tint, outline=2)


def panel(credits, gems, txt):
    """`GiftOverlay` on `VictoryFrame`: the scrim, the fan and bloom, the green window with the crown
    and banner carrying the title, the present under it, the sentence, the cards and the key."""
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
    K.shrunk(block, txt("ui.gift.title"), bcx, top - BANNER_Y - WORD_LIFT, WORD_W, WORD_H, 58, 32, outline=5)

    # The present on its gold halo, and the sentence under it.
    K.paste(block, K.glow(int(ART * 1.9), 2.1, K.GOLD, .38), bcx, top + ART_Y)
    K.paste(block, K.fit(Image.open(UI / "ic_gift.png").convert("RGBA"), (ART, ART)), bcx, top + ART_Y)
    K.shrunk(block, txt("ui.gift.body"), bcx, top + BODY_Y, BODY_W, BODY_H, 34, 24, outline=3)

    kinds = [k for k, v in (("credits", credits), ("gems", gems)) if v > 0]
    step = CARD + CARD_GAP
    x = bcx - step / 2 if len(kinds) == 2 else bcx
    for kind in kinds:
        card(block, x, top + CARDS_TOP, kind, credits if kind == "credits" else gems, txt)
        x += step

    ky = top + ph - KEY_Y
    K.paste(block, K.skin("btn_orange", KEY_W, KEY_H), bcx, ky)
    K.text(block, txt("ui.gift.claim").upper(), bcx, ky - 2, 44, outline=3)

    if fit < 1.0:
        block = block.resize((int(bw * fit), int(bh * fit)), Image.LANCZOS)
    K.paste(sheet, block, W / 2, H / 2)

    print(f"  panel {PANEL_W:.0f}x{ph:.0f} (fit {fit:.2f}), {len(kinds)} card(s)")
    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--credits", type=int, default=5000)
    ap.add_argument("--gems", type=int, default=250)
    ap.add_argument("--lang", default="en")
    ap.add_argument("--out", type=Path, default=Path("gift.png"))
    args = ap.parse_args()
    if args.credits <= 0 and args.gems <= 0:
        ap.error("a gift carries some coins or some gems")

    table = locs(args.lang)
    out = panel(args.credits, args.gems, lambda key: table.get(key, key))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}")


if __name__ == "__main__":
    main()
