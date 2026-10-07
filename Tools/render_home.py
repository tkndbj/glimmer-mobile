# -*- coding: utf-8 -*-
"""Draws the hub at the size a phone draws it, with the real sprites.

    python Tools/render_home.py
    python Tools/render_home.py --no-event      # the row with no event running
    python Tools/render_home.py --quiet         # the event box with nothing to collect
    python Tools/render_home.py --welcome       # the foot with the welcome door under it
    python Tools/render_home.py --out home.png

**Why this exists.** Nothing in this project can open a PNG on the way to a build, and the
Editor cannot photograph a `ScreenSpaceOverlay` canvas — so the hub's *look* is judged the way
every board here is judged, by a Python mirror that reads the same sprites and the same
arithmetic the screen does. `render_siege.py` has earned its place six times over and
`render_shop.py` twice; this is the same bargain for the first screen a player ever sees.

**Look at it.** It is the only thing that can see a beam that does not land on its pad, a
plate whose nine-slice smears its corner brackets, a caption drawn over a lamp, or a nav cap
that has come off the rail — none of which any numeric gate here can reach.

**It is a mirror, and mirrors drift.** Every constant is named after the field it copies
(`HomeScreen.RowTop`, `NavBar.Height`, `Scenery.RailTopH`), so a change on one side is
findable on the other. It draws the *furniture* and the copy that goes on it; it does not draw
the tweens, so nothing here says whether an entrance reads well.
"""
from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image                                       # noqa: E402
import hudkit as K                                          # noqa: E402

W, H = K.W, K.H

# HomeScreen
TOPBAR_Y, TOPBAR_H = 116.0, 168.0
RES_Y, RES_H = 254.0, 104.0
TASKS_Y, TASKS_H = 436.0, 240.0
ROW_TOP, ROW_HEIGHT, ROW_WIDTH, ROW_GAP = 570.0, 300.0, 960.0, 24.0

# HomeScreen's foot, measured up from the nav bar exactly as the screen measures it - and in
# the two shapes it takes (`HubFoot`): with the welcome door under Daily Challenges while the
# bonus is live and unclaimed (invariant 58), and without it for ever after. The companion that
# used to stand between the feature row and the key is gone, and so is the `hero` that drew it.
CHALLENGE_W = LINE_W = 960.0                                              # HubFoot.Width
LINE_PAD, LINE_HEAD_H, LINE_HEAD_GAP, LINE_FOOT = 8.0, 34.0, 8.0, 10.0    # HubFoot.Line*

# HomeScreen.WelcomeArt* / WelcomeCaptionRight - the four turrets on the door's left and the
# room the caption stops short of, clear of the badge on the top-right corner.
WELCOME_ART_H, WELCOME_ART_W, WELCOME_ART_LEFT, WELCOME_ART_STEP, WELCOME_ART_LIFT = 120.0, 96.0, 64.0, 80.0, 6.0
WELCOME_CAPTION_RIGHT = 130.0


class Foot:
    """`HubFoot.For(welcome)`, field for field. Every size and centre the foot draws at."""

    def __init__(self, welcome):
        self.welcome = welcome
        if welcome:
            (self.gap, self.welcome_h, self.challenge_h, self.play_w, self.play_h,
             self.line_cell, self.line_cell_gap, self.line_star) = 12.0, 136.0, 224.0, 620.0, 160.0, 150.0, 20.0, 20.0
        else:
            (self.gap, self.welcome_h, self.challenge_h, self.play_w, self.play_h,
             self.line_cell, self.line_cell_gap, self.line_star) = 14.0, 0.0, 280.0, 620.0, 178.0, 168.0, 20.0, 20.0

    @property
    def line_h(self):
        return LINE_PAD + LINE_HEAD_H + LINE_HEAD_GAP + self.line_cell + LINE_FOOT

    @property
    def line_cell_y(self):
        return self.line_h / 2 - LINE_PAD - LINE_HEAD_H - LINE_HEAD_GAP - self.line_cell / 2

    @property
    def welcome_y(self):
        return K.NAV_HEIGHT + self.gap + self.welcome_h / 2

    @property
    def challenge_y(self):
        under = self.welcome_y + self.welcome_h / 2 + self.gap if self.welcome else K.NAV_HEIGHT + self.gap
        return under + self.challenge_h / 2

    @property
    def line_y(self):
        return self.challenge_y + self.challenge_h / 2 + self.gap + self.line_h / 2

    @property
    def play_y(self):
        return self.line_y + self.line_h / 2 + self.gap + self.play_h / 2

    @property
    def top(self):
        return self.play_y + self.play_h / 2

# SiegeView.Tints, in the order WardLine.Colours names them: Pal.Poppy, Mint, Azure, Amber.
SEAT_TINTS = [(0xF2, 0x40, 0x4F), (0x7B, 0xD8, 0x6A), (0x4F, 0xC1, 0xFF), (0xFF, 0x8A, 0x2B)]

# HomeScreen.BuildChestRow
CHEST_TALL, CHEST_SHORT = 188.0, 130.0
CHEST_DIP, CHEST_FLOOR, CHEST_OVERLAP = 20.0, -92.0, .07


def top_bar(sheet, waiting=True):
    """`HomeScreen.BuildTopBar` — the player card, the avatar slot, the two corner keys, and the
    green starburst on the card's top-right corner saying a keeper milestone chest is waiting
    (invariant 57d). `waiting` is the flag, for 44l's reason: a sheet that could not draw the
    badge could not be asked whether it clears the settings key."""
    cy = TOPBAR_Y

    # the card, 620x138 with its left edge 352 right of the bar's own left edge
    card_cx = 352.0
    K.paste(sheet, K.skin("Hud/card", 620, 138), card_cx, cy)

    # the avatar's slot, and a companion frame standing in it
    slot_cx = card_cx - 620 / 2 + 70
    K.paste(sheet, K.skin("Hud/slot", 116, 116), slot_cx, cy)
    try:
        face = Image.open(K.REPO / "Assets" / "Game" / "Art" / "Critters" / "c5" / "f00.png").convert("RGBA")
        K.paste(sheet, K.fit(face, (84, 84)), slot_cx, cy + 2)
    except FileNotFoundError:
        pass

    # the rank disc on the slot's bottom-right corner
    K.paste(sheet, K.glow(50, 1.0, K.GOLD, 1.0), slot_cx + 58 - 4, cy + 58 - 4)
    K.text(sheet, "7", slot_cx + 54, cy + 54, 30, fill=(82, 54, 15), outline=0)

    # **The name and the rank bar are placed by their *centres*, not their left edges.**
    # `UIKit.Box` always pivots at centre, so an `anchoredPosition` of 362 puts the middle of a
    # 460-wide label there and its left edge 230 units back. Read as an edge — which is what
    # this drew for as long as it existed — the name sat 230 units right of where the game puts
    # it and the rank bar ran 176 units off the end of the card it lives inside. That is this
    # file's own recorded trap (invariant 44d), and it matters more than a cosmetic slip: the
    # whole point of these renders is to catch a widget hanging off its plate, so a render that
    # invents one is an instrument lying about the thing it exists to judge.
    name_cx = card_cx - 620 / 2 + 362
    K.text(sheet, "Keeper", name_cx - 460 / 2, cy - 22, 36, anchor="l")

    # the rank bar
    tx = card_cx - 620 / 2 + 356 - 440 / 2
    K.paste(sheet, K.skin("Hud/trough", 440, 30), tx + 220, cy + 24)
    K.paste(sheet, K.tint(K.skin("Hud/fill", 432 * .62, 22), K.MINT),
            tx + 4 + 432 * .62 / 2, cy + 24)

    # the milestone badge, top right of the card - `WaitingBadge.GiftTopRight(card, Pal.Mint)`
    if waiting:
        hub_burst(sheet, card_cx + 620 / 2 - 46, cy - 138 / 2 + 44, K.MINT, None, scale=1.0)

    for i, glyph in enumerate(("ic_gear", "ic_info")):
        bx = W - (92 if i == 0 else 208)
        K.paste(sheet, K.skin("sq_orange", 106, 106), bx, cy)
        try:
            K.paste(sheet, K.tint(K.fit(Image.open(K.UI / f"{glyph}.png").convert("RGBA"), (53, 53)),
                                  K.CREAM), bx, cy)
        except FileNotFoundError:
            pass


def resources(sheet):
    """`HomeScreen.BuildResources` — three troughs, each with the kit's own "+" on the end."""
    money = [(K.ROSE, "ic_heart", "5/5"), (K.GOLD, "Coin/f0", "12,480"), (K.BLOOM, "ic_gem", "1,240")]
    for i, (colour, glyph, value) in enumerate(money):
        cx = W / 2 + (i - 1) * 322
        K.paste(sheet, K.skin("Hud/trough", 304, 96), cx, RES_Y)

        gx = cx - 304 / 2 + 66
        K.paste(sheet, K.glow(120, 2.0, colour, .30), gx, RES_Y)
        try:
            K.paste(sheet, K.fit(Image.open(K.UI / f"{glyph}.png").convert("RGBA"), (62, 62)), gx, RES_Y)
        except FileNotFoundError:
            pass

        K.text(sheet, value, cx + 20, RES_Y, 34)
        K.paste(sheet, K.fit(K.load("Hud/add")[0], (58, 58)), cx + 276 / 2 - 14, RES_Y)


def chest_geometry():
    """The closed chest's headroom, **measured off the art rather than typed**.

    `HomeScreen` carries the same four numbers as constants (`ChestFill`, `ChestWide`,
    `ChestLift`, `ChestAspect`) because a screen cannot open a PNG. This can, so it does — and
    it prints what it found, which is the only thing that can say the constants have gone stale
    after a re-cut (44b's rule: measured, not typed).
    """
    out = {}
    for tier in ("wood", "silver", "gold", "royal"):
        im = Image.open(K.UI / "Chest" / f"{tier}.png").convert("RGBA")
        box = im.getchannel("A").getbbox()
        out[tier] = (im, box)
    return out


def strings():
    import json
    table = json.loads((K.REPO / "Assets" / "StreamingAssets" / "Content" / "loc" / "en.json")
                       .read_text(encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


LOCS = strings()


def txt(key):
    return LOCS.get(key, key)


def tasks(sheet, ready=("silver",), verbose=False):
    """`HomeScreen.BuildTasks` — the chest pack, a countdown and a starburst. No title.

    The chests are laid out here exactly as `BuildChestRow` lays them out: a cosine bell for
    the heights, a cursor for the positions, the run centred on what it measures. What this
    can see and nothing else can is whether "big and close" is actually what lands — a row that
    reads as four icons on a plate is the fault this card was rebuilt to fix.
    """
    cy = TASKS_Y
    K.paste(sheet, K.skin("Hud/" + "plate_violet", ROW_WIDTH, TASKS_H), W / 2, cy)

    # The shelf, clipped in the game to the *plate's own shape* rather than to its rect - a
    # `Mask` cut from `Hud/plate_violet`, not a `RectMask2D`. A rectangle let the light sit in
    # the notch between the keyline and the box at all four corners, and this mirror drew the
    # same rectangle, so it agreed with the fault instead of reporting it.
    #
    # **The turning starburst went with the screen's**, in the same change - a mirror still
    # drawing a piece the screen has dropped is worse than no mirror at all (invariant 44d).
    mould = K.skin("Hud/plate_violet", int(ROW_WIDTH) - 12, int(TASKS_H) - 12)
    plate = Image.new("RGBA", mould.size, (0, 0, 0, 0))

    shelf = K.glow(170, 1.7, (41, 5, 61), .34).resize((840, 170), Image.LANCZOS)
    plate.alpha_composite(shelf, (plate.width // 2 - 420, plate.height // 2 - 85 + 84))
    # The stencil is the mould's own alpha, so the shine stops exactly where the plate does.
    plate.putalpha(Image.composite(plate.getchannel("A"),
                                   Image.new("L", plate.size, 0), mould.getchannel("A")))
    K.paste(sheet, plate, W / 2, cy)

    tiers = ["wood", "silver", "gold", "royal"]
    art = chest_geometry()
    n = len(tiers)

    near, far = (n - 1) % 2, n - 1
    span = far - near
    bell = [math.cos((0.0 if span <= 0 else (abs(2 * i - (n - 1)) - near) / float(span)) * math.pi * .5)
            for i in range(n)]
    tall = [CHEST_SHORT + (CHEST_TALL - CHEST_SHORT) * b for b in bell]

    # the grandest chest takes the crest and the rest fall away from it
    seat = sorted(range(n), key=lambda k: (-bell[k], -k))
    stands = [None] * n
    for k, s_i in enumerate(seat):
        stands[s_i] = tiers[n - 1 - k]

    # the drawn width per drawn height, off the art itself
    wide = []
    for i in range(n):
        im, box = art[stands[i]]
        wide.append(tall[i] * (box[2] - box[0]) / float(box[3] - box[1]))

    x = [0.0] * n
    for i in range(1, n):
        x[i] = x[i - 1] + (wide[i - 1] + wide[i]) * .5 - min(wide[i - 1], wide[i]) * CHEST_OVERLAP
    mid = (x[0] - wide[0] * .5 + x[n - 1] + wide[n - 1] * .5) * .5

    if verbose:
        im, box = art["gold"]
        print("  chest art %dx%d, drawn box %s" % (im.width, im.height, box))
        print("  fill %.4f  wide %.4f  lift %.4f  aspect %.4f"
              % ((box[3] - box[1]) / im.height,
                 (box[2] - box[0]) / float(box[3] - box[1]),
                 ((box[1] + box[3]) * .5 - im.height * .5) / (box[3] - box[1]),
                 im.width / im.height))
        print("  row %.0f wide, %.0f%% of the plate" % (x[-1] + wide[-1] / 2 + wide[0] / 2,
                                                        100 * (x[-1] + wide[-1]) / ROW_WIDTH))

    # the contact shadows first, all of them (they are siblings, not children)
    for i in range(n):
        foot = CHEST_FLOOR - CHEST_DIP * bell[i]
        shade = K.glow(128, 1.9, (26, 5, 41), .42).resize(
            (max(1, int(wide[i] * 1.30)), max(1, int(tall[i] * .22))), Image.LANCZOS)
        K.paste(sheet, shade, W / 2 + x[i] - mid, cy - (foot - 2))

    # shortest first, so each chest is drawn over the smaller one beside it
    for i in sorted(range(n), key=lambda k: bell[k]):
        im, box = art[stands[i]]
        drawn = im.crop(box)
        w = max(1, int(round(wide[i])))
        h = max(1, int(round(tall[i])))
        drawn = drawn.resize((w, h), Image.LANCZOS)

        foot = CHEST_FLOOR - CHEST_DIP * bell[i]
        px = W / 2 + x[i] - mid
        py = cy - (foot + tall[i] * .5)

        if stands[i] in ready:
            K.paste(sheet, K.glow(int(tall[i] * 1.9), 1.7, K.GOLD, .55), px, py)
        else:
            drawn = K.tint(drawn, (230, 235, 245))
        K.paste(sheet, drawn, px, py)

    # the name, across the top
    top = cy - TASKS_H / 2
    K.text(sheet, txt("ui.tasks.title").upper(), W / 2, top + 26, 27, fill=K.GOLD)

    # the starburst, top left
    hub_burst(sheet, W / 2 - ROW_WIDTH / 2 + 46, top + 44, K.GOLD, None)


# `WaitingBadge.HubScale` - the four feature boxes wear the starburst a fifth larger.
HUB_SCALE = 1.2


def hub_burst(sheet, bx, by, tint, n, scale=HUB_SCALE, inked=False):
    """`WaitingBadge.HubTopLeft` / `HubTopRight` - the kit's starburst with a `+N`; with `n`
    None it is `WaitingBadge.Gift`, carrying the shop's gift box instead of a count."""
    size = round(104 * scale)
    K.paste(sheet, K.glow(round(190 * scale), 2.0, tint, .40), bx, by)
    K.paste(sheet, K.tint(K.skin("Hud/burst", size, size), tint).rotate(8, resample=Image.BICUBIC), bx, by)
    if n is None:
        gift = Image.open(K.UI / "ic_gift.png").convert("RGBA")
        K.paste(sheet, K.fit(gift, (round(66 * scale), round(66 * scale))), bx, by)
    else:
        # `WaitingBadge.Paint(string)`: a word (the welcome door's NEW) instead of a count.
        label = n if isinstance(n, str) else f"+{n}"
        size = round(30 * scale)
        while size > 18 and K.font(size).getlength(label) > 80 * scale:
            size -= 1                      # the 80-wide Shrinkable box, floor 18
        if inked:  # `WaitingBadge.WhiteInk`: white over a black outline
            K.text(sheet, label, bx, by - 2 * scale, size, fill=(255, 255, 255), outline=2)
        else:
            K.text(sheet, label, bx, by - 2 * scale, size, fill=(43, 28, 5), outline=0)


def feature(sheet, paired=True, waiting=True):
    """`HomeScreen.BuildFeature` — the streak, and whatever the player is working toward.

    **This drew two empty boxes with a "3" in them for as long as it existed, and that was a
    lie about the screen.** The real cards carry a 138-unit flame or event mark on the left at
    `gx = -w/2 + 115`, the count at `vx = -w/2 + 299` with its caption under it, a full-width
    strip along the bottom holding an icon and a line of copy, and a gold badge on the corner —
    every one of which `HomeScreen.BuildStreakBox` and `BuildEventBox` have always built. A
    mirror that under-draws makes the screen look emptier than it is, which is the one way a
    render can send you off to fix something that was never broken.

    **And the plate is the box's own, which this drew as the navy card for just as long.**
    `FeatureCard` is handed `Skins.PlateOrange` and `Skins.PlateViolet` and draws them
    untinted, so the streak box is bright orange and the event box bright violet — and a
    mirror that paints both navy cannot answer a single question about what a *dark* piece
    of furniture looks like standing on one. The trough's own drop-shadow read as a brown
    ring round every bar in this row and this render showed nothing at all.

    **And for as long as it existed it drew one state of the event box and drew it wrong.**
    The badge was a constant `3` sitting over the caption a box with *nothing* waiting says,
    which is a pair the screen cannot produce — so the sheet answered neither question. A box
    with something waiting lights a rim (`FeatureBeacon`), says COLLECT in gold and wears the
    count; a settled one wears none of the three and its countdown moves 70 units right,
    because `FeatureHeader`'s `badged` inset is what keeps the clock out from under the disc.
    `waiting` picks between them (`--quiet`), which is invariant 44l: the state the fault was
    in was the state no sheet could reach.
    """
    half = (ROW_WIDTH - ROW_GAP) * .5
    cy = ROW_TOP + ROW_HEIGHT * .5
    x = (ROW_WIDTH - half) * .5

    # plate, lamp alpha, title colour, caption colour — `BuildStreakBox` passes `Pal.Cream`
    # as its tint and `BuildEventBox` `Pal.Bloom`, and the lamp is white at `edge * .55` in
    # both, not the box's colour.
    # `BuildEventBox` reads `progress.AnyWaiting` three times — for the beacon, for the
    # header's inset and for the caption — so the mirror derives all three from one flag too.
    event_badge = 3 if waiting else 0
    event_caption = ("COLLECT", K.GOLD) if waiting else ("MARKS", K.BLOOM)

    boxes = [(-x if paired else 0.0, half if paired else ROW_WIDTH,
              "plate_orange", .30, K.CREAM, K.CREAM, "STREAK", "",
              "4", "4 NIGHTS", "ic_gift", "KEEP IT UP - 5 GEMS", 2),
             (x, half, "plate_violet", .50, K.BLOOM, event_caption[1],
              "THE FIRST WATCH", "2d 14h",
              "7/12", event_caption[0], None, None, event_badge)]
    if not paired:
        boxes = boxes[:1]

    for (bx, bw, plate, edge, colour, capcol, title, meta,
         value, caption, icon, line, badge) in boxes:
        cx = W / 2 + bx

        # `FeatureBeacon`, built before the card because it sits behind it: a gold glow
        # reaching 48 past every edge, plus a lit outline on the border. Drawn at the middle
        # of its tween rather than at an end, which is what a still can say about a pulse.
        if badge:
            K.paste(sheet, K.glow(max(bw, ROW_HEIGHT) + 96, 1.7, K.GOLD, .22),
                    cx, cy)

        K.paste(sheet, K.skin("Hud/" + plate, bw, ROW_HEIGHT), cx, cy)

        if badge:
            K.ImageDraw.Draw(sheet).rounded_rectangle(
                [cx - bw / 2 + 2, cy - ROW_HEIGHT / 2 + 2, cx + bw / 2 - 2, cy + ROW_HEIGHT / 2 - 2],
                radius=28, outline=(*K.GOLD, 150), width=4)

        # the lamp along the top edge — white, and the box's own colour is the plate
        K.paste(sheet, K.glow(max(bw * .78, 86), 1.9, (255, 255, 255), edge * .55),
                cx, cy - ROW_HEIGHT / 2 + 14)

        K.text(sheet, title, cx - bw / 2 + 30, cy - ROW_HEIGHT / 2 + 36, 25,
               fill=colour, outline=0, anchor="l")
        if meta:
            # `FeatureHeader`'s `badged` inset: 100 units of clearance so a countdown never
            # runs under the corner starburst — and nothing, so it does not sit oddly short, on a
            # box with no disc on it.
            K.text(sheet, meta, cx + bw / 2 - 26 - (100 if badge else 0),
                   cy - ROW_HEIGHT / 2 + 36, 23,
                   fill=(255, 242, 214), outline=0, anchor="r")

        # the streak's calendar or the season's crest, on its own glow.
        #
        # **The crest is `ic_season` at 148 and this drew `ic_stars` at 128**, which was the
        # nearest thing on disk while `SeasonCrest` *generated* the picture — a ring of pips
        # no mirror could reproduce, so this drew a stand-in and the one screen the crest is
        # on could not be looked at. It is a bought sprite now (`make_season_crest.py`), so
        # the mirror draws the sprite: `SeasonCrest.PaintCrest` sizes it to the 148-unit host
        # with `preserveAspect`, which is what `K.fit` is.
        gx = cx - bw / 2 + 115
        K.paste(sheet, K.glow(200, 2.0, K.SUN if icon else K.BLOOM, .30), gx, cy + 4)
        art = "ic_streak" if icon else "ic_season"
        try:
            mark = Image.open(K.UI / f"{art}.png").convert("RGBA")
            K.paste(sheet, K.fit(mark, (130, 130) if icon else (148, 148)), gx, cy + 4)
        except FileNotFoundError:
            pass

        # the count, and what it counts
        vx = cx - bw / 2 + 299
        # `FeatureValue` places the count at (vx, +29) and the caption at (vx, -32) in
        # Unity's y-up space, so on an image they are 20 *above* and 32 *below* the
        # card's middle. Drawn both below, they overlap by twelve units — which is what
        # this did, and it read as a bug in the screen rather than in the mirror.
        K.text(sheet, value, vx, cy - 29, 76, fill=K.CREAM, outline=3)
        K.text(sheet, caption, vx, cy + 32, 22, fill=capcol, outline=0)

        # the strip along the bottom: a line of copy, or a bar with milestone pips on it.
        # `FeatureStrip` is the kit's own trough at 58 — drawn here as a hand-rolled rounded
        # rectangle for as long as this existed, which is the one shape that cannot show what
        # a bought sprite's edge does against a coloured plate.
        d = K.ImageDraw.Draw(sheet)
        sw = bw - 72
        sy = cy + ROW_HEIGHT / 2 - 46
        K.paste(sheet, K.skin("Hud/trough", sw, 58), cx, sy)

        if icon:
            try:
                K.paste(sheet, K.fit(Image.open(K.UI / f"{icon}.png").convert("RGBA"), (40, 40)),
                        cx - sw / 2 + 38, sy)
            except FileNotFoundError:
                pass
            K.text(sheet, line, cx - sw / 2 + 70, sy, 22, fill=(255, 242, 214), outline=0, anchor="l")
        else:
            track = bw - 132
            K.paste(sheet, K.skin("Hud/trough", track, 22), cx, sy)
            K.paste(sheet, K.tint(K.skin("Hud/fill", (track - 6) * .58, 14), colour),
                    cx - track / 2 + 3 + (track - 6) * .58 / 2, sy)
            for rung in (.33, .66, 1.0):
                px = cx - track / 2 + 3 + (track - 6) * rung
                d.ellipse([px - 9, sy - 9, px + 9, sy + 9],
                          fill=K.GOLD if rung <= .58 else (120, 130, 140), outline=(3, 5, 10), width=3)

        # the hub's gift starburst, pinned to the corner (`WaitingBadge.HubGiftTopRight`)
        if badge:
            hub_burst(sheet, cx + bw / 2 - 46, cy - ROW_HEIGHT / 2 + 44, K.GOLD, None)


def loadout(sheet, foot):
    """`HomeScreen.BuildLoadout` — the four turrets on the line, with their star rungs.

    The stars are read off `WardStarRow`: five always, the earned ones gold and the rest the
    kit's hollow star, because a row that grew would say how far a player has come and never how
    far there is to go.
    """
    cy = H - foot.line_y
    K.paste(sheet, K.skin("Hud/panel", LINE_W, foot.line_h), W / 2, cy)
    head_y = cy - foot.line_h / 2 + LINE_PAD + LINE_HEAD_H / 2
    K.text(sheet, "LOADOUT", W / 2, head_y, 28, fill=K.GOLD)

    try:
        gear = Image.open(K.UI / "ic_gear.png").convert("RGBA")
        K.paste(sheet, K.tint(K.fit(gear, (LINE_HEAD_H, LINE_HEAD_H)), K.CREAM, .7),
                W / 2 + LINE_W / 2 - 34, head_y)
    except FileNotFoundError:
        pass

    cell, star = foot.line_cell, foot.line_star
    step = cell + foot.line_cell_gap
    starter = _starter_ward()

    for i in range(4):
        x = W / 2 + (i - 1.5) * step
        y = cy - foot.line_cell_y            # the strip's own stack; Unity's y counts up
        K.paste(sheet, K.skin("Hud/card", cell, cell), x, y)
        K.paste(sheet, K.round_rect(cell - 10, cell - 10, 26, SEAT_TINTS[i], .85, 5), x, y)

        if starter:
            # AssetManifest.WardArt -> Art/Siege/Wards/{id}_{colour}, which is WardModel.ArtFor.
            colour = "rgby"[i]
            try:
                body = Image.open(K.REPO / "Assets" / "Game" / "Art" / "Siege" / "Wards"
                                  / f"{starter}_{colour}.png").convert("RGBA")
                K.paste(sheet, K.fit(body, (cell - 50, cell - 50)), x, y - 14)
            except FileNotFoundError:
                pass

        # WardStarRow, at the size the hub draws it: one star lit, which is what a turret is
        # worth the moment it is bought (`WardStars.Least`).
        gap = star * .22
        width = 5 * star + 4 * gap
        for j in range(5):
            sx = x - width / 2 + star / 2 + j * (star + gap)
            name = "star_full" if j == 0 else "star_empty"
            try:
                im = Image.open(K.UI / f"{name}.png").convert("RGBA")
                K.paste(sheet, K.tint(K.fit(im, (star, star)),
                                      K.GOLD if j == 0 else (255, 255, 255),
                                      1.0 if j == 0 else .45),
                        sx, y + cell / 2 - 26)
            except FileNotFoundError:
                pass


def _progression():
    import json
    try:
        return json.loads((K.REPO / "Assets" / "StreamingAssets" / "Content"
                           / "progression.json").read_text(encoding="utf8"))
    except (FileNotFoundError, ValueError):
        return {}


def _starter_ward():
    """The roster's starter, derived off `progression.json` the way `WardCatalog` derives it.

    A starter is the first model in shelf order that costs nothing in *either* currency
    (`WardModel.IsStarter`) — never a named default, because a named one is a second place the
    roster says which turret is free, and the two drift the first time a drop reorders the shelf.
    Both prices are asked, which is invariant 16j.
    """
    rows = sorted(_progression().get("wards", {}).get("models", []), key=lambda r: r.get("order", 0))
    for row in rows:
        if row.get("coinPrice", 0) <= 0 and row.get("gemPrice", 0) <= 0:
            return row.get("id")
    return rows[0].get("id") if rows else None


def _welcome_quests():
    """The shipped `welcome` block's rows, in order - what the door and the page draw."""
    return list((_progression().get("welcome") or {}).get("quests") or [])


def challenges(sheet, foot):
    """`HomeScreen.BuildChallenges` - the keeper ladder's BUY LEVEL key (`Skins.Gem`, violet),
    `CHALLENGE_KEY_H` tall at the foot of its slot, with the owner's turret scene standing on its
    right and rising out of its top, and `ui.challenges.title` on two lines on its left. The
    slot is shorter while the welcome door stands under it, so the picture is drawn shorter and
    rises less. The shine is a tween and is not drawn."""
    slot_bottom = H - (foot.challenge_y - foot.challenge_h / 2)
    ky, size, room = K.door_key(sheet, W / 2, slot_bottom, CHALLENGE_W, foot.challenge_h,
                                txt("ui.challenges.title").upper(), "challenge_door")
    left = W / 2 - CHALLENGE_W / 2
    print("  challenges key: caption settled at %d (floor 26) in %d of room" % (size, room))

    # the badge, top left, in orange (`WaitingBadge.HubInkedTopLeft(card, Pal.Amber)`)
    hub_burst(sheet, left + 46, ky - K.DOOR_KEY_H / 2 + 44, K.AMBER, 2, inked=True)


def welcome_door(sheet, foot):
    """`HomeScreen.BuildWelcome` - the orange key (`Skins.Buy`) under Daily Challenges, the
    quests' four turrets standing on its foot in the four seat colours and rising out of its
    top, WELCOME BONUS on two lines on its right, and the hub's starburst on the top-right corner
    saying NEW (`WaitingBadge.HubInkedTopRight(card, Pal.Mint)`). Absent when the foot is plain."""
    if not foot.welcome:
        return
    cy = H - foot.welcome_y
    left = W / 2 - LINE_W / 2
    foot_y = cy + foot.welcome_h / 2
    K.paste(sheet, K.skin("btn_orange", LINE_W, foot.welcome_h), W / 2, cy)

    quests = _welcome_quests()[:4]
    for i, quest in enumerate(quests):
        colour = "rgby"[i % 4]
        try:
            body = Image.open(K.REPO / "Assets" / "Game" / "Art" / "Siege" / "Wards"
                              / f"{quest.get('ward')}_{colour}.png").convert("RGBA")
            K.paste(sheet, K.fit(body, (WELCOME_ART_W, WELCOME_ART_H)),
                    left + WELCOME_ART_LEFT + i * WELCOME_ART_STEP, foot_y - WELCOME_ART_LIFT - WELCOME_ART_H / 2)
        except FileNotFoundError:
            pass

    n = len(quests)
    cap_left = WELCOME_ART_LEFT + (n - 1) * WELCOME_ART_STEP + WELCOME_ART_W / 2 + 20 if n else 40.0
    cap_right = LINE_W - WELCOME_CAPTION_RIGHT
    room = cap_right - cap_left
    lines = K.door_two_lines(txt("ui.welcome.door").upper())
    size = 44
    while size > 26 and (max(K.font(size).getlength(ln) for ln in lines) > room
                         or len(lines) * size * 1.2 > foot.welcome_h * .82):
        size -= 1
    tx = left + (cap_left + cap_right) / 2
    top = cy - foot.welcome_h * K.PILL_FACE_LIFT - (len(lines) - 1) * size * 1.2 / 2
    for i, ln in enumerate(lines):
        K.text(sheet, ln, tx, top + i * size * 1.2, size)
    print("  welcome key: caption settled at %d (floor 26) in %d of room; the turrets' tops sit %d "
          "under the key's top" % (size, room, foot.welcome_h - WELCOME_ART_LIFT - WELCOME_ART_H))

    hub_burst(sheet, left + LINE_W - 46, cy - foot.welcome_h / 2 + 44, K.MINT,
              txt("ui.welcome.new").upper(), inked=True)


def _np():
    import numpy
    return numpy


def play(sheet, foot):
    """`HomeScreen.BuildPlay` — the one control this screen exists to offer.

    **`Skins.Battle` and the word BATTLE**, with the kit's own painted glyph beside it at 112
    rather than at the third-of-the-height a pill gives a small mark. This drew a green PLAY key
    and a NEXT UP trough under it for as long as it existed, and neither has been on the screen
    since the siege became the game (invariant 44d).

    **And the halo under it went on 2026-09-20**, at the owner's instruction — it was the one
    thing on this screen that read as a light rather than as a control, and a mirror is the
    only place its size could ever be judged against the wall it lit.
    """
    cy = H - foot.play_y
    K.paste(sheet, K.skin("Hud/btn_gold", foot.play_w, foot.play_h), W / 2, cy)

    # UIKit.FitLabel: the glyph and the caption are centred as one block, the glyph leading.
    lift = foot.play_h * .0231
    gap = 18.0
    try:
        icon = K.fit(Image.open(K.UI / "ic_battle.png").convert("RGBA"), (112, 112))
    except FileNotFoundError:
        icon = None

    word = K.font(62).getlength("BATTLE")
    block = (icon.width + gap if icon else 0) + word
    left = W / 2 - block / 2
    if icon:
        K.paste(sheet, icon, left + icon.width / 2, cy - lift)
        left += icon.width + gap
    K.text(sheet, "BATTLE", left + word / 2, cy - lift, 62, outline=4)


def clearance(foot):
    """What is left between the feature row and the BATTLE key on the squarest phone.

    **The one thing this picture cannot show, because it is drawn at one canvas.** The hub is
    two fixed stacks growing toward each other — the top one hangs off the safe area and the
    foot one stands on the nav bar — and the gap between them is whatever the canvas has left.
    `Layout.CanvasFit` hands anything squarer than 7:4 a widened canvas, so the *shortest* one
    this game is ever drawn on is exactly 1080 x 1890, and that is the number to print - for
    both shapes of the foot, because the welcome one is the taller (`HubFoot.Spare`,
    `HubFootTests`).

    A negative reading is the hub overlapping itself, which is CRAFT.md's own recorded iPad
    report (the companion drawn 176 units through the streak box) asked before a device asks it.
    """
    top = ROW_TOP + ROW_HEIGHT
    shortest = 1890.0
    return top, foot.top, shortest - top - foot.top


def screen(paired=True, verbose=False, waiting=True, welcome=False):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.room(sheet)
    K.rail(sheet, top=True)

    foot = Foot(welcome)
    top_bar(sheet, waiting)
    resources(sheet)
    tasks(sheet, verbose=verbose)
    feature(sheet, paired, waiting)
    play(sheet, foot)
    loadout(sheet, foot)
    challenges(sheet, foot)
    welcome_door(sheet, foot)
    K.navbar(sheet, "home")
    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--no-event", action="store_true",
                    help="the feature row with nothing running, so the streak takes the width")
    ap.add_argument("--quiet", action="store_true",
                    help="the event box with nothing waiting: no rim, no badge, no COLLECT")
    ap.add_argument("--welcome", action="store_true",
                    help="the foot with the welcome door under Daily Challenges (invariant 58)")
    ap.add_argument("--out", type=Path, default=Path("home.png"))
    args = ap.parse_args()

    out = screen(paired=not args.no_event, verbose=True, waiting=not args.quiet, welcome=args.welcome)

    for shape in (False, True):
        top, foot, spare = clearance(Foot(shape))
        print(f"  stack ({'welcome' if shape else 'plain'}): {top:.0f} from the top, {foot:.0f} from the "
              f"nav bar, {spare:.0f} spare on the squarest phone (1080x1890)"
              + ("" if spare >= 0 else "   <-- THE HUB OVERLAPS ITSELF"))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}  {out.width}x{out.height}  - look at it")


if __name__ == "__main__":
    main()
