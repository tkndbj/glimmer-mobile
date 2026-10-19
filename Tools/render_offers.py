"""Draws the personal offer panel (`OfferOverlay`, invariant 64) over the hub, for judging by eye.

    python Tools/render_offers.py                          # every shipped offer, one PNG each
    python Tools/render_offers.py --offer near_miss        # one offer
    python Tools/render_offers.py --device ipad            # on a tablet's canvas
    python Tools/render_offers.py --lang de                # in another Latin-script table
    python Tools/render_offers.py --contact sheet.png      # every offer on one sheet
    python Tools/render_offers.py --measure                # every look, language and device: fail on a caption at its floor

A mirror, not a screenshot: every number is `OfferLayout.Panel`'s and `VictoryFrame`'s, kept in step
by hand (44d), and every caption is fitted the way `UIKit.Shrinkable` fits it (`hudkit.shrunk`). The
key shows the product's reference price; on a device it is the store's own string in the player's
currency. Arabic is not drawn - the game shapes it through `ArabicText` at runtime and this font has
no Arabic forms - so `--measure` skips it, as `render_reminders.py` does.
"""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import hudkit as K  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")

REPO = K.REPO
UI = K.UI
LOC = REPO / "Assets" / "StreamingAssets" / "Content" / "loc"
LATIN = ["en", "es", "pt", "fr", "de", "it", "tr", "pl"]

#: `CanvasFit`: a phone keeps the 1080 width and grows tall; anything squarer than 1.75 widens until
#: its height is 2160. These are the canvases a panel is ever laid out on.
DEVICES = {
    "phone": (1080, 2340),        # 19.5:9, most phones
    "classic": (1080, 1920),      # 16:9
    "squat": (1080, 1890),        # the shortest phone canvas (CanvasFit.PhoneFloor)
    "ipad": (1620, 2160),         # 4:3
    "fold": (1800, 2160),         # the squarest device
}

# VictoryFrame
CREST_REACH, FIT_MARGIN = 202.0, 20.0
PANEL_INK = (150, 184, 176)
CROWN_Y, BANNER_Y, BANNER_W, BANNER_H = 114.0, -30.0, 566.0, 157.0
WORD_LIFT, WORD_W, WORD_H = 34.0, 356.0, 74.0

# OfferLayout.Panel
P = dict(Width=1030.0, Height=990.0, LineY=184.0, LineW=900.0, LineH=64.0, LineSize=40, LineFloor=24,
         RowY=470.0, ArtX=-240.0, ArtSize=380.0, GlowSize=580.0, ColumnX=210.0, ColumnW=480.0,
         SealX=-400.0, SealY=300.0, SealSize=176.0, SealWordSize=44, SealWordFloor=22,
         AmountY=382.0, AmountH=110.0, GemSize=88.0, GemArt=170.0, AmountSize=96, AmountFloor=48,
         CoinsY=462.0, CoinsH=64.0, CoinsSize=50, CoinsFloor=26, EndsY=534.0, EndsH=42.0, EndsSize=30, EndsFloor=20,
         TimerY=594.0, TimerH=80.0, TimerSize=62, TimerFloor=32, KeyY=762.0, KeyW=480.0, KeyH=124.0, KeySize=48,
         FootKeyY=70.0, FootKeyW=340.0, FootKeyH=92.0, FootKeySize=30)
POLLEN = (255, 221, 87)   # Pal.Pollen, the coins line
GLYPH_GAP = 18.0          # UIKit.GlyphGap
POPPY = (242, 64, 79)
URGENT = (255, 92, 72)


def table(lang):
    return {e["key"]: e["text"] for e in json.loads((LOC / f"{lang}.json").read_text(encoding="utf-8"))["entries"]}


def upper(s, lang):
    """`Loc.Upper`: Turkish dotted i."""
    return s.replace("i", "İ").upper() if lang == "tr" else s.upper()


def store():
    return json.loads((REPO / "Assets/StreamingAssets/Content/progression.json").read_text(encoding="utf-8"))["store"]["products"]


def tuning():
    """The shipped offer tuning, `firebase/seed/offers.json` (revision 1 is `OfferTuning.ShippedJson`)."""
    return json.loads((REPO / "firebase" / "seed" / "offers.json").read_text(encoding="utf-8"))


def extra(product, products):
    """`OfferValue.Extra`."""
    packs = [p for p in products if not p.get("passDays")]
    g = min((p for p in packs if p.get("shelf") == "gems" and p.get("gems")), key=lambda p: p["referenceUsdCents"])
    c = min((p for p in packs if p.get("shelf") == "coins" and p.get("credits")), key=lambda p: p["referenceUsdCents"])
    worth = product.get("credits", 0) * c["referenceUsdCents"] * g["gems"] + product.get("gems", 0) * g["referenceUsdCents"] * c["credits"]
    pct = worth * 100 // (product["referenceUsdCents"] * c["credits"] * g["gems"])
    pct = min(9990, pct - pct % 10)
    return pct - 100 if pct > 100 else 0


def picture(product, products):
    """`ShopArt.OfferPicture`: the product's rung of the offers shelf (`ShopLadder.Rung`)."""
    offers = sorted((p for p in products if p.get("shelf") == "offers"), key=lambda p: p["referenceUsdCents"])
    tier, size, rungs = offers.index(product) + 1, len(offers), 7
    steps = size - 1
    r = 6 if size <= 1 else max(0, min(rungs - 1, ((tier - 1) * (rungs - 1) * 2 + steps) // (steps * 2)))
    return UI / "Shop" / f"offer_{r + 1}.png"


def timer(seconds):
    s = max(0, int(seconds))
    d, h, m, sec = s // 86400, s % 86400 // 3600, s % 3600 // 60, s % 60
    return ("%dd " % d if d else "") + "%02d:%02d:%02d" % (h, m, sec)


def panel(offer, product, products, lang="en", device="phone", left=None, state="ready", draw=True):
    """`OfferOverlay.Build` on `VictoryFrame`, over the hub. Returns the sheet and every caption's
    settled size, as (what, size, floor)."""
    w, h = DEVICES[device]
    K.W, K.H = w, h
    s = table(lang)
    sheet = Image.new("RGBA", (w, h), (*K.GROUND, 255))
    if draw:
        K.room(sheet)
        K.navbar(sheet, "home")
        sheet.alpha_composite(Image.new("RGBA", (w, h), (0, 0, 0, int(255 * .74))))

    ph = P["Height"]
    reach = ph + CREST_REACH
    fit = min(1.0, (h - FIT_MARGIN * 2) / reach)

    block = Image.new("RGBA", (1680, int(reach + 500)), (0, 0, 0, 0))
    bw, bh = block.size
    cx = bw / 2
    top = bh / 2 - CREST_REACH / 2 - ph / 2
    crest = top - CREST_REACH / 2
    sizes = []

    def fitted(what, text, x, y, box_w, box_h, size, floor, fill=K.CREAM, outline=3):
        got = K.shrunk(block, text, x, y, box_w, box_h, size, floor, fill=fill, outline=outline)
        sizes.append((what, text, got, floor))
        return got

    if draw:
        fan = K.rays(1680, 14)
        fan_rgba = Image.new("RGBA", fan.size, (255, 204, 77, 0))
        fan_rgba.putalpha(fan.point(lambda v: int(v * .20)))
        K.paste(block, fan_rgba, cx, crest + 240)
        K.paste(block, K.glow(1240, 2.4, (255, 209, 97), .22), cx, crest + 200)
        K.paste(block, K.tint(K.skin("Win/window", P["Width"], ph), PANEL_INK), cx, top + ph / 2)
        K.paste(block, K.glow(int(P["Width"] - 60), 1.9, (255, 245, 209), .11), cx, top + 70)

        K.paste(block, K.fit(K.load("Win/crown")[0], (180, 162)), cx, top - CROWN_Y)
        K.paste(block, K.fit(K.load("Win/banner")[0], (BANNER_W, BANNER_H)), cx, top - BANNER_Y)
    look = offer["look"]
    fitted("title", upper(s[f"ui.offer.look.{look}"], lang), cx, top - BANNER_Y - WORD_LIFT, WORD_W, WORD_H, 58, 32, outline=5)

    fitted("line", s[f"ui.offer.line.{look}"], cx, top + P["LineY"], P["LineW"], P["LineH"], P["LineSize"], P["LineFloor"])

    ax = cx + P["ArtX"]
    if draw:
        K.paste(block, K.glow(P["GlowSize"], 2.4, (255, 209, 97), .55), ax, top + P["RowY"])
        K.paste(block, K.fit(Image.open(picture(product, products)).convert("RGBA"), (P["ArtSize"], P["ArtSize"])), ax, top + P["RowY"])

    pct = extra(product, products)
    if pct:
        sx, sy = cx + P["SealX"], top + P["SealY"]
        K.paste(block, K.tint(K.skin("Hud/burst", round(P["SealSize"]), round(P["SealSize"])), POPPY).rotate(8, Image.BICUBIC), sx, sy)
        fitted("seal", s["ui.offer.bonus"].replace("{0}", str(pct)), sx, sy - 2, P["SealSize"] * .72, P["SealSize"] * .5,
               P["SealWordSize"], P["SealWordFloor"], fill=(255, 255, 255))

    # The column at the right: the gems with the shop's gem before them (`UIKit.CentreGlyph`),
    # the coins under in Pal.Pollen, then ENDS IN over the time left, with no trough.
    col = cx + P["ColumnX"]
    said = f"{product['gems']:,}"
    size = P["AmountSize"]
    wide = K.font(size).getlength(said)
    lx = col - (P["GemSize"] + GLYPH_GAP + wide) / 2
    ay = top + P["AmountY"]
    K.paste(block, K.fit(Image.open(UI / "Shop" / "gems_1.png").convert("RGBA"), (P["GemArt"], P["GemArt"])),
            lx + P["GemSize"] / 2, ay + round(size * .044))
    K.text(block, said, lx + P["GemSize"] + GLYPH_GAP + wide / 2, ay, size, fill=K.BLOOM, outline=5)
    if P["GemSize"] + GLYPH_GAP + wide > P["ColumnW"]:
        sizes.append(("gems", said, P["AmountFloor"], P["AmountFloor"]))
    else:
        sizes.append(("gems", said, size, P["AmountFloor"]))

    fitted("coins", upper(s["ui.shop.plus_coins"].replace("{0}", f"{product['credits']:,}"), lang), col, top + P["CoinsY"],
           P["ColumnW"], P["CoinsH"], P["CoinsSize"], P["CoinsFloor"], fill=POLLEN, outline=4)

    fitted("ends", upper(s["ui.deal.ends_label"], lang), col, top + P["EndsY"], P["ColumnW"], P["EndsH"], P["EndsSize"], P["EndsFloor"])
    left = offer["lastsMinutes"] * 60 if left is None else left
    fitted("timer", timer(left), col, top + P["TimerY"], P["ColumnW"], P["TimerH"], P["TimerSize"], P["TimerFloor"],
           fill=URGENT if 0 < left < 3600 else (158, 168, 184) if left <= 0 else (255, 255, 255), outline=4)

    ky = top + P["KeyY"]
    pill, word = {
        "ready": ("btn_orange", f"${product['referenceUsdCents'] / 100:.2f}"),
        "waiting": ("btn_gray", s["ui.shop.purchasing"]),
        "bought": ("btn_green", upper(s["ui.deal.bought_row"], lang)),
        "ended": ("btn_gray", upper(s["ui.deal.ended_row"], lang)),
    }["ready" if state == "ready" and left > 0 else ("ended" if left <= 0 else state)]
    K.paste(block, K.skin(pill, P["KeyW"], P["KeyH"]), cx, ky)
    fitted("key", word, cx, ky - P["KeyH"] * K.PILL_FACE_LIFT, P["KeyW"] - 40, P["KeyH"] * .72, P["KeySize"], 16)

    fy = top + ph - P["FootKeyY"]
    K.paste(block, K.skin("btn_blue", P["FootKeyW"], P["FootKeyH"]), cx, fy)
    fitted("not now", upper(s["ui.common.cancel"], lang), cx, fy - P["FootKeyH"] * K.PILL_FACE_LIFT, P["FootKeyW"] - 40,
           P["FootKeyH"] * .72, P["FootKeySize"], 16)

    if fit < 1.0:
        block = block.resize((int(bw * fit), int(bh * fit)), Image.LANCZOS)
    if draw:
        K.paste(sheet, block, w / 2, h / 2)
    return sheet.convert("RGB"), sizes, fit


def measure():
    """Every look and product in every Latin table on every canvas: a caption at its floor fails (19n)."""
    products = store()
    offers = tuning()["offers"]
    worst, failed = {}, []
    for lang in LATIN:
        for offer in offers:
            for pid in offer["products"]:
                product = next(p for p in products if p["id"] == pid)
                # Every caption's box is fixed in panel units, so the canvas changes only the fit,
                # measured once per canvas below.
                for device in ("phone",):
                    _, sizes, fit = panel(offer, product, products, lang, device, draw=False)
                    for what, said, got, floor in sizes:
                        key = (lang, what)
                        if key not in worst or got < worst[key][0]:
                            worst[key] = (got, floor, said)
                        if got <= floor:
                            failed.append(f"{lang} {what} '{said}' sits at its floor {floor} ({offer['id']}, {device})")
    for device, (w, h) in DEVICES.items():
        fit = min(1.0, (h - FIT_MARGIN * 2) / (P["Height"] + CREST_REACH))
        print(f"  {device:<8} {w}x{h}: the panel {'fits whole' if fit >= 1.0 else 'scales to %.2f' % fit}")
        if fit < 1.0:
            failed.append(f"{device}: the panel scales to {fit:.2f}")
    for lang in LATIN:
        row = "  ".join(f"{what} {worst[(lang, what)][0]}/{worst[(lang, what)][1]}"
                        for what in ("title", "line", "seal", "coins", "ends", "timer", "key", "not now"))
        print(f"  {lang}  {row}")
    print("  ar  not drawn here (shaped at runtime; see ArabicTextTests)")
    for line in sorted(set(failed)):
        print("  FAIL " + line)
    print(f"{'FAIL' if failed else 'ok'}: {len(offers)} offers x {len(LATIN)} languages x {len(DEVICES)} canvases")
    return not failed


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--offer", help="one offer id of firebase/seed/offers.json; every one when absent")
    ap.add_argument("--product", help="which of the offer's products (default its first)")
    ap.add_argument("--lang", default="en", choices=LATIN)
    ap.add_argument("--device", default="phone", choices=list(DEVICES))
    ap.add_argument("--left", type=int, help="seconds left on the clock (default the offer's whole window)")
    ap.add_argument("--state", default="ready", choices=("ready", "waiting", "bought"))
    ap.add_argument("--out", type=Path, default=Path("offers"), help="a folder, or a .png for one offer")
    ap.add_argument("--contact", type=Path, metavar="PNG", help="also lay every drawn panel on one sheet")
    ap.add_argument("--measure", action="store_true")
    args = ap.parse_args()

    if args.measure:
        sys.exit(0 if measure() else 1)

    products = store()
    offers = [o for o in tuning()["offers"] if not args.offer or o["id"] == args.offer]
    if not offers:
        sys.exit(f"no offer '{args.offer}'")

    drawn = []
    for offer in offers:
        pid = args.product or offer["products"][0]
        product = next(p for p in products if p["id"] == pid)
        sheet, sizes, fit = panel(offer, product, products, args.lang, args.device, args.left, args.state)
        tight = [f"{what} {got}/{floor}" for what, _, got, floor in sizes if got <= floor]
        out = args.out if args.out.suffix == ".png" else args.out / f"offer_{offer['id']}_{args.lang}_{args.device}.png"
        out.parent.mkdir(parents=True, exist_ok=True)
        sheet.save(out)
        drawn.append((offer["id"], sheet))
        print(f"  {offer['id']:<15} {pid}  ${product['referenceUsdCents'] / 100:.2f}  +{extra(product, products)}%  "
              f"fit {fit:.2f}{'  TIGHT ' + ', '.join(tight) if tight else ''}  -> {out}")

    if args.contact:
        cols = min(4, len(drawn))
        rows = (len(drawn) + cols - 1) // cols
        tw = 540
        th = int(tw * drawn[0][1].height / drawn[0][1].width)
        sheet = Image.new("RGB", (cols * tw, rows * th), (0, 0, 0))
        for i, (_, im) in enumerate(drawn):
            sheet.paste(im.resize((tw, th), Image.LANCZOS), ((i % cols) * tw, (i // cols) * th))
        args.contact.parent.mkdir(parents=True, exist_ok=True)
        sheet.save(args.contact)
        print(f"  wrote {args.contact}")


if __name__ == "__main__":
    main()
