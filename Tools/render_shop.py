# -*- coding: utf-8 -*-
"""Draws the storefront at the size a phone draws it, with the real sprites.

    python Tools/render_shop.py                 # the gem shelf
    python Tools/render_shop.py --shelf coins   # any shelf in progression.json
    python Tools/render_shop.py --all           # every shelf, side by side

**Why this exists.** Nothing in this project can open a PNG on the way to a build, and the
Editor cannot photograph a `ScreenSpaceOverlay` canvas — so a storefront's *look* is judged
the way every board here is judged, by a Python mirror that reads the same sprites and the
same numbers the screen does. `render_siege.py` earned its place six times over; this is the
same bargain for the one screen that takes money, and it has now earned it twice — once on a
yellow price bar drawn on a yellow card, and once on a ribbon whose words ran onto its tails.

**A mirror only shows what it mirrors, and that cost a round.** This drew cards with no
coloured seat and no fan of rays behind the picture, because it was written after those were
already on the way out — so the shop it drew was *cleaner than the one on the phone*, and the
decoration the owner then asked to have removed was invisible here. A render is a diagnostic
for the things it draws and says nothing at all about the things it does not; when a report
from a device disagrees with this picture, this picture is the one that is wrong.

**It is a mirror, and mirrors drift.** Every constant below is named after the field it
copies (`ShopScreen.HeaderHeight`, `ProductCard`'s offsets, `ProductCardBadges`), so a change
on one side is findable on the other.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

W, H = K.W, K.H
REPO = K.REPO
UI = K.UI

# ShopScreen
HEADER, TABROW = 300.0, 132.0
RESTORE = 92.0
COLUMNS, CELLW, CELLH = 2, 508.0, 560.0

# ProductCard / ProductCardBadges
PLATE_X, PLATE_Y = 34.0, 40.0
PLATEW, PLATEH = CELLW - PLATE_X, CELLH - PLATE_Y
ART, ART_DROP = 300.0, 168.0
AMOUNT_RISE, SUB_RISE, FACE_RISE = 196.0, 150.0, 74.0
FACEW, FACEH = CELLW - 110.0, 96.0
# `SEAL_TILT` carries `ProductCardBadges.SealTilt`'s **own sign**, and is rotated by without
# negation: PIL turns a picture anticlockwise for a positive angle and so does
# `Quaternion.Euler(0, 0, z)`, so the two agree as written. It was stored negated and
# negated again at the call, which drew the right badge for the wrong reason — the trap
# invariant 37aj names, where a mirror re-derives a sign in an axis that runs the other way.
SEAL, SEAL_DISC, SEAL_TILT = 164.0, .86, -9.0
RIBBONW, RIBBONH, RIBBON_INSET, RIBBON_DROP = 268.0, 76.0, 96.0, 34.0
RIBBON_TILT = 6.0
CLEARANCE, MARGIN = 10.0, 6.0

# `ProductCardBadges.SealInset` / `.SealDrop`, derived rather than copied: the badge walks in
# by itself when the bonus plate beside it grows, and a typed number here would go stale the
# first time it did.
SEAL_REACH = SEAL * SEAL_DISC * .5
RIBBON_REACH = (RIBBONW * .5 * math.cos(math.radians(RIBBON_TILT))
                + RIBBONH * .5 * math.sin(math.radians(RIBBON_TILT)))
NEIGHBOUR_RIBBON = CELLW - PLATEW * .5 + RIBBON_INSET - RIBBON_REACH
SEAL_INSET = PLATEW * .5 - min(PLATEW * .5 - MARGIN - SEAL_REACH,
                               NEIGHBOUR_RIBBON - CLEARANCE - SEAL_REACH)
SEAL_DROP = SEAL_REACH - (PLATE_Y - CLEARANCE)

# Skins.Accent — what tells one shelf from another now the plate is one teal on all of them.
ACCENT = {"gems": K.BLOOM, "coins": K.GOLD, "bundles": K.MINT,
          "supplies": K.ROSE, "utilities": K.SUN}

SPOT_ALPHA = .22

# `ShopAdShelf.For` — the two shelves whose first spot is a rewarded video, and what each
# pays. Read out of `progression.json` rather than typed, for this file's standing rule.
AD_SHELF = {"coins": "coin_bonus", "supplies": "heart_refill"}

# `RewardArt.Token` for the two kinds a shelf can pay: credits are frame nought of the
# spinning coin, hearts are the game's own glyph.
AD_TOKEN = {"credits": "Coin/f0", "hearts": "ic_heart"}
AD_UNIT = {"credits": "Coins", "hearts": "Hearts"}
AD_TINT = {"credits": K.GOLD, "hearts": K.ROSE}


# --------------------------------------------------------------------------- TokenPile
def pile(total, token):
    """`TokenPile.Of` — a pyramid, back row first, each row drawn from its ends inwards.

    Mirrored rather than approximated because the arrangement is the whole of why a heap
    reads as a heap: a row drawn left to right shingles one way and points the pile, and a
    back row drawn last is drawn in front of the thing it stands behind.
    """
    if total <= 0:
        return []

    front = (total + 2) // 2
    back = total - front
    step = token * .60 if back else 0.0

    def row(count, y):
        out = []
        for n in range(count):
            i = n // 2 if n % 2 == 0 else count - 1 - n // 2
            frm = i - (count - 1) * .5
            reach = (count - 1) * .5
            out.append((frm * token * .80, y, 0.0 if reach <= 0 else -(frm / reach) * 12.0))
        return out

    return row(back, step * .5) + row(front, -step * .5)


# --------------------------------------------------------------------------- the card
def card(sheet, x, top, shelf, picture, amount, sub, price, badge, bonus=None, live=True):
    """One product card, laid out the way `ProductCard`'s constructor lays one out."""
    plate_cx, plate_cy = x, top + CELLH / 2
    K.paste(sheet, K.skin("Hud/card", PLATEW, PLATEH), plate_cx, plate_cy)

    ptop = plate_cy - PLATEH / 2
    pbot = plate_cy + PLATEH / 2

    # The shelf's own light under the goods, which is the job the coloured frame used to do.
    K.paste(sheet, K.glow(370, 1.6, ACCENT.get(shelf, K.BLOOM), SPOT_ALPHA),
            plate_cx, ptop + ART_DROP)

    art = Image.open(UI / "Shop" / f"{picture}.png").convert("RGBA")
    K.paste(sheet, K.fit(art, (ART, ART)), plate_cx, ptop + ART_DROP)

    K.text(sheet, amount, plate_cx, pbot - AMOUNT_RISE, 46, outline=4)
    K.text(sheet, sub, plate_cx, pbot - SUB_RISE, 26, fill=(255, 245, 225), outline=2)

    face = K.skin("btn_orange", FACEW, FACEH)               # Skins.Buy
    if not live:
        face = K.tint(face, K.MUTED)
    K.paste(sheet, face, plate_cx, pbot - FACE_RISE)
    K.text(sheet, price, plate_cx, pbot - FACE_RISE, 34, outline=3)

    if bonus:
        # `Skins.Title` is a real cloth ribbon under this kit, so it hangs again — the tilt
        # went to nought only while the mark was a machined plate. Drawn rather than sliced,
        # for the reason `Skins.Title` gives: the tails are the silhouette.
        rib = K.fit(K.load("Hud/title")[0], (RIBBONW, RIBBONH))
        rib = rib.rotate(RIBBON_TILT, Image.BICUBIC, expand=True)
        K.paste(sheet, rib, plate_cx - PLATEW / 2 + RIBBON_INSET, ptop + RIBBON_DROP)
        K.text(sheet, bonus, plate_cx - PLATEW / 2 + RIBBON_INSET,
               ptop + RIBBON_DROP - 4, 25, fill=K.SUN, outline=3)


    if badge:
        seal = K.tint(K.fit(K.load("Hud/burst")[0], (SEAL, SEAL)), K.ROSE)
        # The caption is a *child* of the seal on the screen, so it turns with it. Drawn flat
        # here it said the badge was upright however far the disc had been leant over, which
        # is the one thing this picture is being asked about.
        face = Image.new("RGBA", (int(SEAL), int(SEAL)), (0, 0, 0, 0))
        K.text(face, badge, SEAL / 2, SEAL / 2, 22, outline=2)
        seal.alpha_composite(face)
        seal = seal.rotate(SEAL_TILT, Image.BICUBIC, expand=True)
        K.paste(sheet, seal, plate_cx + PLATEW / 2 - SEAL_INSET, ptop + SEAL_DROP)


def ad_card(sheet, x, top, shelf, kind, amount):
    """The shelf's first spot: `ProductCard.Draw(AdOffer, StoreShelf)`.

    The one question this can answer and nothing else can: **does a free card read as a
    different kind of offer from the six prices under it, without reading as a broken one?**
    Three things are supposed to do that and all three are pictures — a composed heap with a
    play mark on it where the packs carry a painted chest, a green face where they carry
    orange, and FREE on the ribbon where they carry a percentage. A number can say none of it.
    """
    plate_cx, plate_cy = x, top + CELLH / 2
    K.paste(sheet, K.skin("Hud/card", PLATEW, PLATEH), plate_cx, plate_cy)

    ptop = plate_cy - PLATEH / 2
    pbot = plate_cy + PLATEH / 2

    K.paste(sheet, K.glow(370, 1.6, ACCENT.get(shelf, K.BLOOM), SPOT_ALPHA),
            plate_cx, ptop + ART_DROP)

    # `ShopArt.PaintAd` — the heap, then the play mark standing in it. The box is ART wide,
    # which is what the fractions below are measured against on both sides. **Two tokens**,
    # which is the count no pack ladder uses: the picture's job is to be a different card from
    # the six prices under it, and a heap of three beside a fifteen-heart pack is the same
    # picture twice (invariant 18e). This mirror is what caught that.
    token = ART * .44
    art = Image.open(UI / f"{AD_TOKEN[kind]}.png").convert("RGBA")
    for dx, dy, tilt in pile(2, token):
        one = K.fit(art, (token, token)).rotate(tilt, Image.BICUBIC, expand=True)
        # Unity's y is positive upward and an image's is positive downward, so the lift and
        # the row step are both negated here - once, at the point of placement. Drawn the
        # other way up the pyramid stands on its point, which is the one thing about a heap
        # that is obvious in a picture and invisible in the arithmetic (invariant 44d).
        K.paste(sheet, one, plate_cx + dx, ptop + ART_DROP - (ART * .06 + dy))

    seat = ART * .34
    disc = Image.new("RGBA", (int(seat), int(seat)), (0, 0, 0, 0))
    ImageDraw.Draw(disc).ellipse([0, 0, seat - 1, seat - 1], fill=(*K.MINT, 217))
    ImageDraw.Draw(disc).ellipse([3, 3, seat - 4, seat - 4], fill=(13, 23, 15, 224))
    K.paste(sheet, disc, plate_cx, ptop + ART_DROP + ART * .24)

    play = K.tint(K.fit(Image.open(UI / "ic_play.png").convert("RGBA"), (ART * .17, ART * .17)),
                  K.CREAM)
    K.paste(sheet, play, plate_cx + ART * .012, ptop + ART_DROP + ART * .24)

    K.text(sheet, f"{amount:,}", plate_cx, pbot - AMOUNT_RISE, 46, fill=AD_TINT[kind], outline=4)
    K.text(sheet, AD_UNIT[kind], plate_cx, pbot - SUB_RISE, 26, fill=(255, 245, 225), outline=2)

    # `Skins.Affirm`, which is the same green the watch button on `AdOfferOverlay` wears.
    K.paste(sheet, K.skin("btn_green", FACEW, FACEH), plate_cx, pbot - FACE_RISE)

    # `UIKit.CentreGlyph`: the caption and the mark are one block and the block is centred, so
    # the two offsets are measured off the caption rather than typed. A mirror with its own
    # numbers answers the wrong question about the one thing a face has to get right.
    glyph = FACEH * .34
    wide = K.font(34).getlength("WATCH")
    gap = glyph * .28
    left = plate_cx - (wide + gap + glyph) * .5
    mark = K.tint(K.fit(Image.open(UI / "ic_play.png").convert("RGBA"), (glyph, glyph)), K.CREAM)
    K.paste(sheet, mark, left + glyph * .5, pbot - FACE_RISE)
    K.text(sheet, "WATCH", left + glyph + gap + wide * .5, pbot - FACE_RISE, 34, outline=3)

    rib = K.fit(K.load("Hud/title")[0], (RIBBONW, RIBBONH))
    rib = rib.rotate(RIBBON_TILT, Image.BICUBIC, expand=True)
    K.paste(sheet, rib, plate_cx - PLATEW / 2 + RIBBON_INSET, ptop + RIBBON_DROP)
    K.text(sheet, "FREE", plate_cx - PLATEW / 2 + RIBBON_INSET, ptop + RIBBON_DROP - 4, 25,
           fill=K.SUN, outline=3)


def good_card(sheet, x, top, kind, amount, gems):
    """A gem-priced good: `ProductCard.Draw(StoreGood, GoodOfferState)` and `ShopArt.PaintGood`.

    The supplies shelf could not be drawn here at all until the free spot went onto it — this
    mirror only ever knew how to draw a painted ladder, and hearts are *composed* because the
    pile is the amount. So a shelf nobody could photograph is one this project has been
    judging by arithmetic, which is the state invariant 32b is about.
    """
    plate_cx, plate_cy = x, top + CELLH / 2
    K.paste(sheet, K.skin("Hud/card", PLATEW, PLATEH), plate_cx, plate_cy)

    ptop = plate_cy - PLATEH / 2
    pbot = plate_cy + PLATEH / 2

    K.paste(sheet, K.glow(370, 1.6, ACCENT["supplies"], SPOT_ALPHA), plate_cx, ptop + ART_DROP)

    if kind == "heart_boost":
        boost = K.fit(Image.open(UI / "ic_heart_boost.png").convert("RGBA"), (ART * .74, ART * .74))
        K.paste(sheet, boost, plate_cx, ptop + ART_DROP)
        headline, unit, tint = f"{amount}h", "Faster hearts", K.SUN
    else:
        shown = 1 if amount <= 5 else 3 if amount <= 20 else 5
        token = ART * (.68 if shown == 1 else .44 if shown == 3 else .36)
        heart = Image.open(UI / "ic_heart.png").convert("RGBA")
        for dx, dy, tilt in pile(shown, token):
            one = K.fit(heart, (token, token)).rotate(tilt, Image.BICUBIC, expand=True)
            K.paste(sheet, one, plate_cx + dx, ptop + ART_DROP - dy)
        headline, unit, tint = f"{amount:,}", "Hearts", K.ROSE

    K.text(sheet, headline, plate_cx, pbot - AMOUNT_RISE, 46, fill=tint, outline=4)
    K.text(sheet, unit, plate_cx, pbot - SUB_RISE, 26, fill=(255, 245, 225), outline=2)

    K.paste(sheet, K.skin("btn_violet", FACEW, FACEH), plate_cx, pbot - FACE_RISE)   # Skins.Gem
    glyph = FACEH * .34
    said = f"{gems:,}"
    wide = K.font(34).getlength(said)
    gap = glyph * .28
    left = plate_cx - (wide + gap + glyph) * .5
    gem = K.fit(Image.open(UI / "ic_gem.png").convert("RGBA"), (glyph, glyph))
    K.paste(sheet, gem, left + glyph * .5, pbot - FACE_RISE)
    K.text(sheet, said, left + glyph + gap + wide * .5, pbot - FACE_RISE, 34, outline=3)


VESSELS = ["potion2", "potion4", "potion6"]


def container_card(sheet, x, top, rung_at, cap, price, badge):
    """A heart container: `ShopArt.PaintContainer` — a vessel with hearts over its lip."""
    plate_cx, plate_cy = x, top + CELLH / 2
    K.paste(sheet, K.skin("Hud/card", PLATEW, PLATEH), plate_cx, plate_cy)

    ptop = plate_cy - PLATEH / 2
    pbot = plate_cy + PLATEH / 2

    K.paste(sheet, K.glow(370, 1.6, ACCENT["supplies"], SPOT_ALPHA), plate_cx, ptop + ART_DROP)

    vessel = ART * (.62 + rung_at * .07)
    bottle = Image.open(UI / f"{VESSELS[rung_at]}.png").convert("RGBA")
    K.paste(sheet, K.fit(bottle, (vessel * .72, vessel)), plate_cx, ptop + ART_DROP + ART * .10)

    token = ART * .26
    heart = Image.open(UI / "ic_heart.png").convert("RGBA")
    for dx, dy, tilt in pile(3 + rung_at, token):
        one = K.fit(heart, (token, token)).rotate(tilt, Image.BICUBIC, expand=True)
        K.paste(sheet, one, plate_cx + dx, ptop + ART_DROP - (ART * .20 + dy))

    K.text(sheet, f"{cap:,}", plate_cx, pbot - AMOUNT_RISE, 46, fill=K.ROSE, outline=4)
    K.text(sheet, "Heart limit", plate_cx, pbot - SUB_RISE, 26, fill=(255, 245, 225), outline=2)

    K.paste(sheet, K.skin("btn_orange", FACEW, FACEH), plate_cx, pbot - FACE_RISE)
    K.text(sheet, price, plate_cx, pbot - FACE_RISE, 34, outline=3)

    if badge:
        seal = K.tint(K.fit(K.load("Hud/burst")[0], (SEAL, SEAL)), K.ROSE)
        face = Image.new("RGBA", (int(SEAL), int(SEAL)), (0, 0, 0, 0))
        K.text(face, badge, SEAL / 2, SEAL / 2, 22, outline=2)
        seal.alpha_composite(face)
        seal = seal.rotate(SEAL_TILT, Image.BICUBIC, expand=True)
        K.paste(sheet, seal, plate_cx + PLATEW / 2 - SEAL_INSET, ptop + SEAL_DROP)


# --------------------------------------------------------------------------- the screen
SHELVES = ["gems", "coins", "bundles", "supplies", "utilities"]
TAB_GLYPH = {"gems": "ic_gem", "coins": "Shop/pouch", "bundles": "ic_gift",
             "supplies": "ic_heart", "utilities": "Utility/firepot"}


def rules():
    return json.loads((REPO / "Assets" / "StreamingAssets" / "Content" / "progression.json")
                      .read_text(encoding="utf8"))


def products(shelf):
    store = rules()["store"]
    on = [p for p in store["products"] if p.get("shelf") == shelf]
    on.sort(key=lambda p: p.get("referenceUsdCents", 0))
    return on


def ad_offer(shelf):
    """What this shelf's free spot pays, or None when it has none.

    Read out of the published table rather than typed, for the reason `ShopScreen.Reload`
    reads it there: an offer the table does not carry takes the card off the shelf, so a
    mirror with its own number would go on drawing a card the game had stopped drawing.
    """
    placement = AD_SHELF.get(shelf)
    if placement is None:
        return None

    for p in rules().get("ads", {}).get("placements", []):
        if p.get("id") == placement and p.get("kind") in AD_TOKEN and p.get("amount", 0) > 0:
            return p["kind"], p["amount"]

    return None


def rung(tier, size, rungs):
    if rungs <= 1:
        return 0
    if size <= 1 or tier <= 0:
        return rungs - 1
    steps = size - 1
    r = ((tier - 1) * (rungs - 1) * 2 + steps) // (steps * 2)
    return max(0, min(rungs - 1, r))


LADDER = {"gems": 6, "coins": 4, "bundles": 3}


def supplies(sheet, top, shift):
    """The one shelf that is two lists: the gem-priced goods, then the heart containers.

    `ShopScreen.Reload` fills both and `ShopCell.Bind` walks the goods first, so the order
    here is the order there — a mirror that sorted them by price would draw a shelf the game
    never lays out.
    """
    store = rules()["store"]
    goods = store.get("goods", [])
    cans = sorted([p for p in store["products"] if p.get("shelf") == "supplies"],
                  key=lambda p: p.get("referenceUsdCents", 0))

    for n in range(len(goods) + len(cans)):
        i = n + shift
        col, row = i % COLUMNS, i // COLUMNS
        x = W / 2 + (col - (COLUMNS - 1) * .5) * CELLW
        y = top + row * CELLH
        if y + CELLH > H - K.NAV_HEIGHT - RESTORE:
            break

        if n < len(goods):
            g = goods[n]
            good_card(sheet, x, y, g["kind"], g["amount"], g["gems"])
            continue

        p = cans[n - len(goods)]
        badge = {"best_value": "BEST", "popular": "POPULAR",
                 "starter": "STARTER"}.get(p.get("badge"))
        container_card(sheet, x, y, n - len(goods), p["heartCapacity"],
                       f"${p['referenceUsdCents'] / 100:.2f}", badge)


def screen(shelf):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.room(sheet)

    # ---- the header band, then the rail over it
    fade = Image.new("RGBA", (W, int(HEADER + TABROW)), (0, 0, 0, 0))
    fd = ImageDraw.Draw(fade)
    for y in range(fade.height):
        a = int(255 * .62 * (1 - y / float(fade.height)) ** .8)
        fd.line([(0, y), (W, y)], fill=(5, 13, 20, a))
    sheet.alpha_composite(fade, (0, 0))

    K.rail(sheet, top=True)

    # the back key
    K.paste(sheet, K.skin("sq_blue", 112, 112), 94, 128)
    K.paste(sheet, K.tint(K.fit(Image.open(UI / "ic_left.png").convert("RGBA"), (56, 56)), K.CREAM),
            94, 128)

    K.paste(sheet, K.skin("Hud/title", 440, 104), W / 2, 112)
    K.text(sheet, "SHOP", W / 2, 112, 40, fill=K.SUN, outline=3)

    money = [("Coin/f0", "12,480"), ("ic_gem", "1,240"), ("ic_heart", "5/5")]
    for i, (glyph, value) in enumerate(money):
        cx = W / 2 + (i - 1) * (228 + 14)
        K.paste(sheet, K.skin("Hud/trough", 228, 74), cx, 228)
        try:
            K.paste(sheet, K.fit(Image.open(UI / f"{glyph}.png").convert("RGBA"), (48, 48)),
                    cx - 228 / 2 + 62, 228)
        except FileNotFoundError:
            pass
        K.text(sheet, value, cx + 14, 228, 30, outline=3)
        K.paste(sheet, K.fit(K.load("Hud/add")[0], (50, 50)), cx + 228 / 2 - 2, 228)

    # ---- the tabs, which are the nav bar's own caps one level down
    step = min(230.0, 1020.0 / len(SHELVES))
    for i, name in enumerate(SHELVES):
        cx = W / 2 + (i - (len(SHELVES) - 1) * .5) * step
        live = name == shelf
        cap = K.fit(K.load("Hud/cap_on" if live else "Hud/cap_off")[0], (TABROW - 6, TABROW - 6))
        K.paste(sheet, cap, cx, HEADER + TABROW / 2)
        try:
            mark = K.fit(Image.open(UI / f"{TAB_GLYPH[name]}.png").convert("RGBA"),
                         (TABROW - 74, TABROW - 74))
            if not live:
                mark.putalpha(mark.split()[3].point(lambda v: int(v * .55)))
            K.paste(sheet, mark, cx, HEADER + TABROW / 2)
        except FileNotFoundError:
            pass


    K.text(sheet, shelf.upper(), W / 2, HEADER + TABROW + 22, 26,
           fill=(255, 245, 225), outline=2)

    # ---- the grid
    top = HEADER + TABROW + 44
    rows = products(shelf)
    rungs = LADDER.get(shelf, 3)

    # The free spot, and everything under it shifted by one cell. `ShopScreen.Bind` does the
    # shift the same way and for the same reason, so the two lists cannot come apart.
    ad = ad_offer(shelf)
    shift = 1 if ad else 0
    if ad:
        ad_card(sheet, W / 2 - CELLW / 2, top, shelf, ad[0], ad[1])

    if shelf == "supplies":
        supplies(sheet, top, shift)
        K.navbar(sheet, "shop")
        return sheet.convert("RGB")

    for n, p in enumerate(rows):
        i = n + shift
        col, row = i % COLUMNS, i // COLUMNS
        x = W / 2 + (col - (COLUMNS - 1) * .5) * CELLW
        y = top + row * CELLH
        if y + CELLH > H - K.NAV_HEIGHT - RESTORE:
            break

        # `n`, not `i`: a rung is a product's place on its own ladder, and the free spot is
        # not on it. Measured from the shifted index every card would draw the picture of the
        # rung above it, which is exactly invariant 18e's fault — a ladder longer than its
        # shelf, individually plausible on every card.
        at = rungs - 1 if p.get("kind") == "nonconsumable" else rung(n + 1, len(rows), rungs)
        picture = f"{'bundles' if shelf == 'bundles' else shelf}_{at + 1}"

        grant = p.get("gems") or p.get("credits") or p.get("heartCapacity") or 0
        # `ui.shop.gems` / `.coins` / `.hearts` as the table spells them. `ProductCard` does
        # **not** upper-case this line — only the ribbon, the badge and the titles are shouted
        # — so a mirror that did made every card here disagree with the phone by a letter case,
        # which is exactly the sort of difference somebody then "fixes" on the screen.
        unit = "Gems" if p.get("gems") else "Coins" if p.get("credits") else "Hearts"
        badge = {"best_value": "BEST", "popular": "POPULAR", "starter": "STARTER"}.get(p.get("badge"))

        # `ProductCard.PaintRibbon` shows one at 5% or better; the figure is arithmetic over
        # the ladder, so the widest string on a shelf is what has to fit.
        bonus = f"+{12 + n * 14}% EXTRA" if n else None

        card(sheet, x, y, shelf, picture, f"{grant:,}", unit,
             f"${p['referenceUsdCents'] / 100:.2f}", badge, bonus)

    # `ShopScreen.BuildRestore` builds a 420x72 `TextButton` in `Skins.Resting`, not a bare
    # caption. Drawn as text alone this read as a loose line floating on the world — which is
    # a fault in the mirror rather than in the screen, and the kind that sends you off to fix
    # something that was never broken.
    ry = H - K.NAV_HEIGHT - RESTORE / 2
    K.paste(sheet, K.skin("sq_dark", 420, 72), W / 2, ry)
    K.text(sheet, "RESTORE PURCHASES", W / 2, ry, 26, fill=(205, 215, 232), outline=2)

    K.navbar(sheet, "shop")
    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--shelf", default="gems", choices=SHELVES)
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--out", type=Path, default=Path("shop.png"))
    args = ap.parse_args()

    if args.all:
        shelves = [s for s in SHELVES if s in LADDER or s == "supplies"]
        sheets = [screen(s) for s in shelves]
        out = Image.new("RGB", (W * len(sheets), H))
        for i, one in enumerate(sheets):
            out.paste(one, (i * W, 0))
    else:
        out = screen(args.shelf)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}  {out.width}x{out.height}  - look at it")


if __name__ == "__main__":
    main()
