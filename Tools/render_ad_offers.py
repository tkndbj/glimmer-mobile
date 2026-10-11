"""Draws the two opt-in video offers where they are sold, for judging by eye.

    python Tools/render_ad_offers.py                          # the lost-run panel: video key over the gem key
    python Tools/render_ad_offers.py --panel continue --state loading|opening|short|alone
    python Tools/render_ad_offers.py --panel unlock           # a turret for sale, discount key under the price
    python Tools/render_ad_offers.py --panel unlock --state short|loading
    python Tools/render_ad_offers.py --panel star             # the next star, discount key under the price
    python Tools/render_ad_offers.py --lang de                # any Latin-script table
    python Tools/render_ad_offers.py --contact                # every panel and state on one sheet
    python Tools/render_ad_offers.py --measure                # every caption, every Latin table; exit 1 on a tight fit

The panels:

- **continue** - `ContinueOverlay` with `ContinueVideo` (2026-10-11): the DEFEAT banner, the offer
  line, the green video key (`ic_play`), the short-of-gems line, the violet gem key, NO THANKS.
  Heights are `ContinuePanel.HeightFor`'s walk, so a row that comes and goes moves the rest as it
  does on the device. `--state alone` is the offer standing on its video for a player with no way
  to pay gems.
- **unlock** - `WardPreviewOverlay`'s for-sale branch with `WardDiscountKey` (2026-10-10): the price
  in the upper key slot, the discount key in the lower, the status line saying what it does.
- **star** - `WardUpgradeOverlay` grown by one key for the discount.

A mirror, not a gate for anything but `--measure`: every number here is those classes' and the two
are kept in step by hand (44d). What is judged is the key band and the lines - **the firing stage is
drawn as its empty well and the stat bars and star ladder are left out**, because `render_ward_preview.py`
owns the stage and none of the three is changed by these offers. The backdrop is the hub's room under
the scrim, where the device has the run's board or the shelf; the scrim is the same 72%. Arabic is
not drawn - the game shapes it through `ArabicText` at runtime and this font has no Arabic forms - so
`--measure` skips it and `ArabicTextTests` carries that table.
"""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import hudkit as K  # noqa: E402

W, H = K.W, K.H
LOC = K.REPO / "Assets/StreamingAssets/Content/loc"
LATIN = ["en", "es", "pt", "fr", "de", "it", "tr", "pl"]

LIFT = K.PILL_FACE_LIFT                      # UIKit.PillFaceLift
GLYPH_GAP = 18.0                             # UIKit.GlyphGap
LABEL_INSET = 40.0                           # UIKit.TextButton: label width is size.x - 40
GLYPH_K = .34                                # UIKit.TextButton: glyph is size.y * .34 square

# Palette (Pal and the parchment inks the panels write in).
ROSE = (232, 97, 90)                         # Pal.Rose #E8615A
EMBER = (255, 107, 87)                       # Pal.Ember #FF6B57
OFFER_INK = (77, 54, 38)                     # ContinueOverlay offer line (.30, .21, .15)
GREEN_WORD = (46, 158, 58)                   # ContinueOverlay.OfferLine #2E9E3A
INK = (92, 64, 46)                           # WardPreviewOverlay.Ink (.36, .25, .18)
SHORT = (148, 79, 15)                        # .Short (.58, .31, .06)
HELD = (46, 107, 54)                         # .Held (.18, .42, .21)
WELL = (23, 31, 41)                          # the stage well (.09, .12, .16, .96)

# ContinuePanel
C_W, C_CONTENT = 880.0, 720.0
C_HEAD, C_OFFER_H, C_OFFER_GAP = 150.0, 170.0, 18.0
C_SHORT_H, C_SHORT_GAP = 58.0, 8.0
C_BUTTON_H, C_BUTTON_GAP = 148.0, 16.0
C_VIDEO_H, C_VIDEO_GAP = 148.0, 16.0
C_GIVE_H, C_FOOT = 96.0, 46.0
C_KEY_W, C_GIVE_W = 600.0, 420.0
BANNER_H, BANNER_GAP, TITLE_OVERHANG = 112.0, 26.0, 87.0

# WardPreviewOverlay (the stage band down to the keys) and WardDiscountKey.
P_W = 880.0
P_NOTE_TOP, P_NOTE_H = 120.0, 110.0
P_STATUS_TOP, P_STATUS_H = 240.0, 44.0
P_STAGE_TOP, P_STAGE_W, P_STAGE_H = 300.0, 800.0, 640.0
BARS_H = 134.0                               # WardStatBars.Height
P_STAT_TOP = P_STAGE_TOP + P_STAGE_H + 24.0
P_STARS_H = 62.0
P_STARS_TOP = P_STAT_TOP + BARS_H + 10.0
ACT_W, ACT_H, ACT_GAP = 480.0, 124.0, 16.0
P_ACT_TOP = P_STARS_TOP + P_STARS_H + 22.0
P_H = P_ACT_TOP + ACT_H * 2 + ACT_GAP + 118.0
P_ACT_UPPER = P_ACT_TOP + ACT_H / 2
P_ACT_LOWER = P_ACT_TOP + ACT_H + ACT_GAP + ACT_H / 2
LINE_W = P_W - 150.0                         # the note and status lines' width
PRICE_SHIFT, COIN_X = 18.0, -118.0           # the price key's caption and coin

# WardUpgradeOverlay
S_H = 980.0
S_STARS_TOP, S_STARS_H = 132.0, 86.0
S_NOTE_TOP, S_NOTE_H = 238.0, 62.0
S_BARS_TOP = 328.0
S_STATUS_TOP = S_BARS_TOP + BARS_H + 24.0
S_ACT_TOP = 700.0

# WardDiscountKey
D_PLAY_X, D_PLAY = -150.0, 52.0
D_COIN_X, D_COIN = -88.0, 46.0
D_PRICE_X, D_PRICE_W, D_PRICE_H = 65.0, 250.0, 62.0
D_SEAL, D_SEAL_INSET, D_SEAL_TURN = 104.0, -6.0, -9.0   # WardDiscountKey.SealSize, .SealInset
D_OFF_W, D_OFF_H, D_OFF_Y = 74.0, 44.0, 2.0

DISCOUNT = 50                                # progression.json ads.turretDiscountPercent
PRICE, SHORT_BY = 12000, 3200                # a turret's price, and a purse short of it
STAR_PRICE = 4500
GEMS = 20                                    # the first continue's price


def table(lang):
    rows = json.loads((LOC / ("%s.json" % lang)).read_text(encoding="utf-8"))["entries"]
    return {e["key"]: e["text"] for e in rows}


def fmt(s, *args):
    for i, a in enumerate(args):
        s = s.replace("{%d}" % i, str(a))
    return s


def discounted(cost):
    """`WardDiscount.PriceOf`: the share off rounds down, so the odd coin stays with the shop."""
    return cost - cost * DISCOUNT // 100


def coin():
    """`Art.CoinFace`: frame nought of the `Ui/Coin` reel."""
    return Image.open(K.UI / "Coin" / "f0.png").convert("RGBA")


def backdrop(draw):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    if draw:
        K.room(sheet)
        sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .72))))
    return sheet


def panel_frame(sheet, width, height, title):
    """`ModalView.MakePanel`: `panel_main`, the orange ribbon 22 over the top edge, tilted 1.6."""
    top = H / 2 - height / 2
    K.paste(sheet, K.skin("panel_main", width, height), W / 2, H / 2)
    if title is not None:
        rib = K.skin("ribbon_orange", width * .78, 130).rotate(1.6, Image.BICUBIC, expand=True)
        K.paste(sheet, rib, W / 2, top - 22)
        K.shrunk(sheet, title, W / 2, top - 28, width * .78 - 120, 100, 54, 30, outline=4)
    return top


def glyph_key(sheet, pill, cx, cy, w, h, caption, icon, size, floor, what):
    """`UIKit.TextButton` with a glyph, fitted by `UIKit.OneLine`: the caption is sized on one line
    in the room the glyph leaves, then glyph and caption are centred as one block (`CentreGlyph`).
    Returns (size, floor) for the measure."""
    K.paste(sheet, K.skin(pill, w, h), cx, cy)
    glyph = h * GLYPH_K
    room = w - LABEL_INSET - glyph - GLYPH_GAP

    got = size
    while got > floor and K.font(got).getlength(caption) > room:
        got -= 1
    wide = min(room, K.font(got).getlength(caption))
    if K.font(got).getlength(caption) > room:
        print("  TIGHT  %s needs %.0f units and has %.0f: '%s'"
              % (what, K.font(got).getlength(caption), room, caption))

    face = cy - h * LIFT
    left = cx - (glyph + GLYPH_GAP + wide) / 2
    icon_im = K.fit(K.load(icon)[0], (glyph, glyph))
    K.paste(sheet, K.tint(icon_im, K.CREAM), left + glyph / 2, face)
    K.text(sheet, caption, left + glyph + GLYPH_GAP + wide / 2, face, got, outline=3)
    return got, floor


def plain_key(sheet, pill, cx, cy, w, h, caption, size, floor, what):
    """`UIKit.TextButton` with no glyph, held to one line (`OneLine(b, size / 2)` by default)."""
    K.paste(sheet, K.skin(pill, w, h), cx, cy)
    got = K.one_line(sheet, caption, cx, cy - h * LIFT, w - LABEL_INSET, size, floor, what=what, outline=3)
    return got, floor


def line(sheet, s, cx, cy, width, size, fill, what):
    """A `UIKit.Label` with no Best Fit and no wrap: it does not shrink, so a sentence wider than its
    box runs past both ends un-clipped. Measured against the box rather than fitted."""
    wide = K.font(size).getlength(s)
    K.text(sheet, s, cx, cy, size, fill=fill, outline=0)
    if wide > width:
        print("  TIGHT  %s needs %.0f units and has %.0f: '%s'" % (what, wide, width, s))
    # A size equal to the floor reads as tight in the measure; an overflow is reported as one.
    return (size, size + 1) if wide > width else (size, 0)


# ------------------------------------------------------------------------------- continue
def continue_panel(lang, state, draw=True):
    """`ContinueOverlay`. States: ready (video live, gems in hand), loading, opening, short (gems to
    buy), alone (no gems can be had: the offer stands on its video)."""
    s = table(lang)
    sheet = backdrop(draw)
    sizes = {}

    video = True
    gems = state != "alone"
    buying = state == "short"

    # ContinuePanel.HeightFor's walk, and the overlay's offsets out of the same walk.
    y = C_HEAD
    offer_y = y + C_OFFER_H / 2
    y += C_OFFER_H + C_OFFER_GAP
    video_y = y + C_VIDEO_H / 2
    y += C_VIDEO_H + C_VIDEO_GAP
    short_y = buy_y = 0.0
    if gems:
        if buying:
            short_y = y + C_SHORT_H / 2
            y += C_SHORT_H + C_SHORT_GAP
        buy_y = y + C_BUTTON_H / 2
        y += C_BUTTON_H + C_BUTTON_GAP
    give_y = y + C_GIVE_H / 2
    height = y + C_GIVE_H + C_FOOT

    top = panel_frame(sheet, C_W, height, s["ui.continue.title"])

    # The banner where it rests: ContinuePanel.BannerCentre, measured up from the middle.
    rest = height / 2 + TITLE_OVERHANG + BANNER_GAP + BANNER_H / 2
    K.shrunk(sheet, s["ui.continue.defeat"], W / 2, H / 2 - rest, C_W, BANNER_H, 84, 44, fill=ROSE, outline=8)
    K.shrunk(sheet, s["ui.continue.wards_title"], W / 2, H / 2 - rest + BANNER_H / 2 + 4, C_W, 40, 30, 20,
             fill=(232, 222, 205), outline=3)

    # The offer line; the green phrase is rich text on the device and plain ink here - the measure
    # is of the sentence's length, which the colour does not change.
    offer = fmt(s["ui.continue.wards_offer"], s["ui.continue.full_health"])
    sizes["offer"] = (K.shrunk(sheet, offer, W / 2, top + offer_y, C_CONTENT, C_OFFER_H, 38, 26,
                               fill=OFFER_INK, outline=0), 26)

    caption = {"loading": s["ui.ads.btn_loading"], "opening": s["ui.ads.btn_opening"]}.get(
        state, s["ui.continue.watch"])
    pill = "btn_gray" if state in ("loading", "opening") else "btn_green"
    sizes["video"] = glyph_key(sheet, pill, W / 2, top + video_y, C_KEY_W, C_VIDEO_H, caption, "ic_play",
                               42, 24, "%s video key (%s)" % (lang, state))

    if gems and buying:
        sizes["short"] = (K.shrunk(sheet, fmt(s["ui.continue.short"], GEMS), W / 2, top + short_y, C_CONTENT,
                                   C_SHORT_H, 30, 19, fill=EMBER, outline=0), 19)

    if gems:
        word = s["ui.continue.get_gems"] if buying else fmt(s["ui.shop.gem_price"], GEMS)
        sizes["gems"] = glyph_key(sheet, "btn_violet", W / 2, top + buy_y, C_KEY_W, C_BUTTON_H, word, "ic_gem",
                                  42, 24, "%s gem key" % lang)

    sizes["give"] = plain_key(sheet, "btn_red", W / 2, top + give_y, C_GIVE_W, C_GIVE_H,
                              s["ui.continue.give_up"], 32, 20, "%s no thanks" % lang)

    # ContinuePanel.IsClear: the banner's top on the shortest canvas this game is laid out on.
    room = 1890.0 / 2
    needed = height / 2 + TITLE_OVERHANG + BANNER_GAP + BANNER_H
    if needed > room:
        print("  CLIPPED  the banner stands %.0f units off the shortest canvas" % (needed - room))

    print("  continue %-8s %s: panel %.0f tall" % (state, lang, height))
    return sheet.convert("RGB"), sizes


# ------------------------------------------------------------------------------ the turret panels
def discount_key(sheet, cx, cy, price, live, s, lang):
    """`WardDiscountKey`: play mark, coin, the discounted price left-aligned against the coin, and
    the rose burst over the top-right corner saying the share off."""
    K.paste(sheet, K.skin("btn_blue" if live else "btn_gray", ACT_W, ACT_H), cx, cy)
    face = cy - ACT_H * LIFT

    K.paste(sheet, K.tint(K.fit(K.load("ic_play")[0], (D_PLAY, D_PLAY)), K.CREAM), cx + D_PLAY_X, face)
    K.paste(sheet, K.fit(coin(), (D_COIN, D_COIN)), cx + D_COIN_X, face)
    K.shrunk_left(sheet, "{:,}".format(price), cx + D_PRICE_X - D_PRICE_W / 2, face - D_PRICE_H / 2,
                  D_PRICE_W, D_PRICE_H, 38, 22)

    # The burst: straddling the key's right edge at the face's height, its centre `SealInset` in
    # from the edge, turned -9 degrees (Unity's z is anticlockwise-positive, so -9 is clockwise;
    # PIL's rotate is anticlockwise).
    seal = K.tint(K.fit(K.load("Hud/burst")[0], (D_SEAL, D_SEAL)), ROSE)
    seal = seal.rotate(-D_SEAL_TURN, Image.BICUBIC, expand=True)
    sx, sy = cx + ACT_W / 2 + D_SEAL_INSET, face
    K.paste(sheet, seal, sx, sy)

    # The figure is counter-turned inside the burst, so it stands upright.
    off = fmt(s["ui.loadout.discount_badge"], DISCOUNT)
    got = K.shrunk(sheet, off, sx, sy - D_OFF_Y, D_OFF_W, D_OFF_H, 28, 16, outline=2)
    if K.font(got).getlength(off) > D_OFF_W:
        print("  TIGHT  %s discount burst needs %.0f units and has %.0f: '%s'"
              % (lang, K.font(got).getlength(off), D_OFF_W, off))
    return got, 16


def price_key(sheet, cx, cy, price, live):
    """The price key: green when it can be paid, the shut grey when it cannot; coin and figure."""
    K.paste(sheet, K.skin("btn_green" if live else "btn_gray", ACT_W, ACT_H), cx, cy)
    face = cy - ACT_H * LIFT
    K.paste(sheet, K.fit(coin(), (46, 46)), cx + COIN_X, face)
    K.shrunk(sheet, "{:,}".format(price), cx + PRICE_SHIFT, face, 320, 62, 38, 22)


def status_for(s, state, short_by):
    """`WardDiscountKey.Status` over the panel's own shortfall line: watching, the offer, or loading -
    in the offer's green whenever the discounted price can be paid."""
    if state == "opening":
        return s["ui.ads.opening"], HELD
    if state == "loading":
        return s["ui.ads.loading"], HELD
    if state == "broke":
        return fmt(s["ui.shop.short_coins"], "{:,}".format(short_by)), SHORT
    return fmt(s["ui.loadout.discount_note"], DISCOUNT), HELD


def unlock_panel(lang, state, draw=True):
    """`WardPreviewOverlay`, a turret for sale. States: ready (the full price affordable), short
    (only the discounted price is), loading (no video yet), broke (neither price)."""
    s = table(lang)
    sheet = backdrop(draw)
    sizes = {}

    top = panel_frame(sheet, P_W, P_H, s.get("ward.lance.name", "Lance"))
    K.paste(sheet, K.skin("sq_blue", 92, 92), W / 2 + P_W / 2 - 44, top + 44)

    sizes["note"] = (K.shrunk(sheet, s.get("ward.lance.note", ""), W / 2, top + P_NOTE_TOP + P_NOTE_H / 2,
                              LINE_W, P_NOTE_H, 30, 22, fill=INK, outline=0), 22)

    short_by = {"ready": 0, "short": SHORT_BY, "loading": SHORT_BY,
                "broke": PRICE - discounted(PRICE) + SHORT_BY}[state]
    words, colour = status_for(s, state, short_by)
    sizes["status"] = line(sheet, words, W / 2, top + P_STATUS_TOP + P_STATUS_H / 2, LINE_W, 26, colour,
                           "%s unlock status (%s)" % (lang, state))

    K.paste(sheet, K.round_rect(P_STAGE_W, P_STAGE_H, 30, WELL, .96), W / 2, top + P_STAGE_TOP + P_STAGE_H / 2)

    price_key(sheet, W / 2, top + P_ACT_UPPER, PRICE, live=short_by == 0)
    sizes["burst"] = discount_key(sheet, W / 2, top + P_ACT_LOWER, discounted(PRICE),
                                  live=state in ("ready", "short"), s=s, lang=lang)

    print("  unlock   %-8s %s: panel %.0f tall" % (state, lang, P_H))
    return sheet.convert("RGB"), sizes


def star_panel(lang, state, draw=True):
    """`WardUpgradeOverlay` with the discount on: one key and its gap taller than it shipped."""
    s = table(lang)
    sheet = backdrop(draw)
    sizes = {}

    height = S_H + ACT_H + ACT_GAP
    top = panel_frame(sheet, P_W, height, s.get("ward.lance.name", "Lance"))
    K.paste(sheet, K.skin("sq_blue", 92, 92), W / 2 + P_W / 2 - 44, top + 44)

    note = fmt(s["ui.loadout.upgrade_to"], 3)
    sizes["note"] = line(sheet, note, W / 2, top + S_NOTE_TOP + S_NOTE_H / 2, LINE_W, 30, INK,
                         "%s star note" % lang)

    short_by = 0 if state == "ready" else 1800
    words, colour = status_for(s, state if state != "short" else "ready", short_by)
    sizes["status"] = line(sheet, words, W / 2, top + S_STATUS_TOP + 22, LINE_W, 26, colour,
                           "%s star status (%s)" % (lang, state))

    act_mid = S_ACT_TOP + ACT_H / 2
    discount_mid = S_ACT_TOP + ACT_H + ACT_GAP + ACT_H / 2
    price_key(sheet, W / 2, top + act_mid, STAR_PRICE, live=short_by == 0)
    sizes["burst"] = discount_key(sheet, W / 2, top + discount_mid, discounted(STAR_PRICE),
                                  live=state in ("ready", "short"), s=s, lang=lang)

    print("  star     %-8s %s: panel %.0f tall" % (state, lang, height))
    return sheet.convert("RGB"), sizes


PANELS = {
    "continue": (continue_panel, ["ready", "loading", "opening", "short", "alone"]),
    "unlock": (unlock_panel, ["ready", "short", "loading", "broke"]),
    "star": (star_panel, ["ready", "short", "loading", "opening"]),
}


def measure():
    """Every caption these offers put on screen, in every Latin table and every state."""
    tight = 0
    for lang in LATIN:
        for name, (draw, states) in PANELS.items():
            for state in states:
                _, sizes = draw(lang, state, draw=False)
                row = "  %-2s %-8s %-8s" % (lang, name, state)
                for part, (got, floor) in sizes.items():
                    flag = "  TIGHT" if got <= floor else ""
                    tight += bool(flag)
                    row += "  %s %d/%d%s" % (part, got, floor, flag)
                print(row)
    print("  ar       not drawn here (shaped at runtime; see ArabicTextTests)")
    return tight


def contact(lang, out):
    """Every panel in every state, scaled onto one sheet."""
    shots = [draw(lang, state)[0] for draw, states in PANELS.values() for state in states]
    cols, scale = 5, .32
    tw, th = int(W * scale), int(H * scale)
    rows = (len(shots) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * tw, rows * th), K.GROUND)
    for i, shot in enumerate(shots):
        sheet.paste(shot.resize((tw, th), Image.LANCZOS), ((i % cols) * tw, (i // cols) * th))
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out)
    print("  wrote %s (%d panels)" % (out, len(shots)))


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--panel", default="continue", choices=sorted(PANELS))
    ap.add_argument("--state", default="ready")
    ap.add_argument("--lang", default="en", choices=LATIN)
    ap.add_argument("--contact", action="store_true", help="every panel and state on one sheet")
    ap.add_argument("--measure", action="store_true", help="every caption, every Latin table")
    ap.add_argument("--out", type=Path, default=None)
    args = ap.parse_args()

    if args.measure:
        tight = measure()
        print("  %d caption(s) at their floor or past their box" % tight)
        sys.exit(1 if tight else 0)

    if args.contact:
        contact(args.lang, args.out or Path("ad_offers_contact.png"))
        return

    draw, states = PANELS[args.panel]
    if args.state not in states:
        ap.error("--state for %s is one of: %s" % (args.panel, ", ".join(states)))

    out, sizes = draw(args.lang, args.state)
    for name, (got, floor) in sizes.items():
        print("  %-7s %d (floor %d)" % (name, got, floor))
    path = args.out or Path("ad_offers_%s_%s.png" % (args.panel, args.state))
    path.parent.mkdir(parents=True, exist_ok=True)
    out.save(path)
    print("  wrote %s" % path)


if __name__ == "__main__":
    main()
