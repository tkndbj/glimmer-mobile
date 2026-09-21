# -*- coding: utf-8 -*-
"""Draws the hub at the size a phone draws it, with the real sprites.

    python Tools/render_home.py
    python Tools/render_home.py --no-event      # the row with no event running
    python Tools/render_home.py --quiet         # the event box with nothing to collect
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

# HomeScreen's foot, measured up from the nav bar exactly as the screen measures it. The
# companion that used to stand between the feature row and the key is gone, and so is the
# `hero` that drew it here.
FOOT_GAP = 14.0
CHALLENGE_W, CHALLENGE_H = 960.0, 280.0
BANNER_INSET = 8.0
PLAY_W, PLAY_H = 620.0, 178.0
LINE_W = 960.0
LINE_CELL, LINE_CELL_GAP, LINE_STAR = 168.0, 20.0, 20.0
LINE_PAD, LINE_HEAD_H, LINE_HEAD_GAP, LINE_FOOT = 8.0, 34.0, 8.0, 10.0
LINE_H = LINE_PAD + LINE_HEAD_H + LINE_HEAD_GAP + LINE_CELL + LINE_FOOT
LINE_CELL_Y = LINE_H / 2 - LINE_PAD - LINE_HEAD_H - LINE_HEAD_GAP - LINE_CELL / 2

CHALLENGE_Y = K.NAV_HEIGHT + FOOT_GAP + CHALLENGE_H / 2

#: `Btn.Interactable` false, and the COMING SOON tag it goes with. See `coming_soon`.
SHUT_PLATE = (158, 168, 179)
TAG_W, TAG_H = 310.0, 62.0
TAG_MARGIN_X, TAG_MARGIN_Y = 22.0, 20.0
LINE_Y = CHALLENGE_Y + CHALLENGE_H / 2 + FOOT_GAP + LINE_H / 2
PLAY_Y = LINE_Y + LINE_H / 2 + FOOT_GAP + PLAY_H / 2

# SiegeView.Tints, in the order WardLine.Colours names them: Pal.Poppy, Mint, Azure, Amber.
SEAT_TINTS = [(0xF2, 0x40, 0x4F), (0x7B, 0xD8, 0x6A), (0x4F, 0xC1, 0xFF), (0xFF, 0x8A, 0x2B)]

# HomeScreen.BuildChestRow
CHEST_TALL, CHEST_SHORT = 188.0, 130.0
CHEST_DIP, CHEST_FLOOR, CHEST_OVERLAP = 20.0, -92.0, .07


def top_bar(sheet):
    """`HomeScreen.BuildTopBar` — the player card, the avatar slot and the two corner keys."""
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
    K.paste(sheet, K.tint(K.skin("Hud/burst", 104, 104), K.GOLD), W / 2 - ROW_WIDTH / 2 + 46, top + 44)
    K.text(sheet, "+2", W / 2 - ROW_WIDTH / 2 + 46, top + 42, 30, fill=(43, 28, 5), outline=0)


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
            # `FeatureHeader`'s `badged` inset: 70 units of clearance so a countdown never
            # runs under the corner disc — and nothing, so it does not sit oddly short, on a
            # box with no disc on it.
            K.text(sheet, meta, cx + bw / 2 - 30 - (70 if badge else 0),
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

        # the gold count badge, pinned to the corner and hanging off it
        if badge:
            bxx, byy = cx + bw / 2 - 30, cy - ROW_HEIGHT / 2 + 28
            d.ellipse([bxx - 33, byy - 33, bxx + 33, byy + 33], fill=K.GOLD,
                      outline=(41, 31, 10), width=7)
            K.text(sheet, str(badge), bxx, byy, 36, fill=(43, 28, 5), outline=0)


def loadout(sheet):
    """`HomeScreen.BuildLoadout` — the four turrets on the line, with their star rungs.

    The stars are read off `WardStarRow`: five always, the earned ones gold and the rest the
    kit's hollow star, because a row that grew would say how far a player has come and never how
    far there is to go.
    """
    cy = H - LINE_Y
    K.paste(sheet, K.skin("Hud/panel", LINE_W, LINE_H), W / 2, cy)
    head_y = cy - LINE_H / 2 + LINE_PAD + LINE_HEAD_H / 2
    K.text(sheet, "LOADOUT", W / 2, head_y, 28, fill=K.GOLD)

    try:
        gear = Image.open(K.UI / "ic_gear.png").convert("RGBA")
        K.paste(sheet, K.tint(K.fit(gear, (LINE_HEAD_H, LINE_HEAD_H)), K.CREAM, .7),
                W / 2 + LINE_W / 2 - 34, head_y)
    except FileNotFoundError:
        pass

    step = LINE_CELL + LINE_CELL_GAP
    starter = _starter_ward()

    for i in range(4):
        x = W / 2 + (i - 1.5) * step
        y = cy - LINE_CELL_Y               # the strip's own stack; Unity's y counts up
        K.paste(sheet, K.skin("Hud/card", LINE_CELL, LINE_CELL), x, y)
        K.paste(sheet, K.round_rect(LINE_CELL - 10, LINE_CELL - 10, 26, SEAT_TINTS[i], .85, 5), x, y)

        if starter:
            # AssetManifest.WardArt -> Art/Siege/Wards/{id}_{colour}, which is WardModel.ArtFor.
            colour = "rgby"[i]
            try:
                body = Image.open(K.REPO / "Assets" / "Game" / "Art" / "Siege" / "Wards"
                                  / f"{starter}_{colour}.png").convert("RGBA")
                K.paste(sheet, K.fit(body, (LINE_CELL - 50, LINE_CELL - 50)), x, y - 14)
            except FileNotFoundError:
                pass

        # WardStarRow, at the size the hub draws it: one star lit, which is what a turret is
        # worth the moment it is bought (`WardStars.Least`).
        gap = LINE_STAR * .22
        width = 5 * LINE_STAR + 4 * gap
        for j in range(5):
            sx = x - width / 2 + LINE_STAR / 2 + j * (LINE_STAR + gap)
            name = "star_full" if j == 0 else "star_empty"
            try:
                star = Image.open(K.UI / f"{name}.png").convert("RGBA")
                K.paste(sheet, K.tint(K.fit(star, (LINE_STAR, LINE_STAR)),
                                      K.GOLD if j == 0 else (255, 255, 255),
                                      1.0 if j == 0 else .45),
                        sx, y + LINE_CELL / 2 - 26)
            except FileNotFoundError:
                pass


def _starter_ward():
    """The roster's starter, derived off `progression.json` the way `WardCatalog` derives it.

    A starter is the first model in shelf order that costs nothing in *either* currency
    (`WardModel.IsStarter`) — never a named default, because a named one is a second place the
    roster says which turret is free, and the two drift the first time a drop reorders the shelf.
    Both prices are asked, which is invariant 16j.
    """
    import json
    try:
        table = json.loads((K.REPO / "Assets" / "StreamingAssets" / "Content"
                            / "progression.json").read_text(encoding="utf8"))
    except (FileNotFoundError, ValueError):
        return None

    rows = sorted(table.get("wards", {}).get("models", []), key=lambda r: r.get("order", 0))
    for row in rows:
        if row.get("coinPrice", 0) <= 0 and row.get("gemPrice", 0) <= 0:
            return row.get("id")
    return rows[0].get("id") if rows else None


def challenges(sheet):
    """`HomeScreen.BuildChallenges` — the painted banner filling the kit's blue plate.

    **The plate is a window**: a `Mask` cut from the plate's own sprite, with the picture
    cover-fitted behind it. Mirrored here with the sprite's alpha as the mask, which is the same
    question uGUI asks — so this is the only picture that can answer whether the crop lands
    somewhere the art can afford, and whether the rounded corners still read.

    **The door is shut.** `card.Interactable = false`, which is `Image.color` taken to
    `Btn`'s grey - so the plate greys and the picture over it does not, which is the whole of
    what the state looks like on a card whose plate is covered. The COMING SOON tag is the rest
    of it, and it is drawn last because it is the Mask's sibling rather than its child.

    The shine is deliberately absent: `Sheen` is a tween, and this file draws furniture rather
    than motion (its own header says so).
    """
    cy = H - CHALLENGE_Y
    plate = K.tint(K.skin("Hud/plate_blue", CHALLENGE_W, CHALLENGE_H), SHUT_PLATE, .85)
    K.paste(sheet, plate, W / 2, cy)

    try:
        art = Image.open(K.UI / "challenges.png").convert("RGBA")
    except FileNotFoundError:
        return

    # The window sits `BANNER_INSET` inside the plate, so the plate's own bevel is drawn all
    # the way round rather than being covered by the picture.
    ww, wh = CHALLENGE_W - BANNER_INSET * 2, CHALLENGE_H - BANNER_INSET * 2

    # Cover: the larger of the two scales that fill an axis, which on this art is width-led.
    cover = max(ww / art.width, wh / art.height)
    art = art.resize((max(1, int(art.width * cover)), max(1, int(art.height * cover))),
                     Image.LANCZOS)

    window = Image.new("RGBA", (int(ww), int(wh)), (0, 0, 0, 0))
    window.alpha_composite(art, ((window.width - art.width) // 2,
                                 (window.height - art.height) // 2))

    # The mask is the window's own copy of the plate sprite, nine-sliced at the window's size —
    # so its corner radius is the plate's (a nine-slice never stretches a corner) and the
    # picture is cut to the same curve, one inset in.
    mask = K.skin("Hud/plate_blue", ww, wh)
    window.putalpha(Image.fromarray(
        (_np().array(window.split()[3], dtype="uint16")
         * _np().array(mask.split()[3], dtype="uint16") // 255).astype("uint8")))

    K.paste(sheet, window, W / 2, cy)
    coming_soon(sheet, cy)


def coming_soon(sheet, cy):
    """`Scenery.Pill` on the plate's top corner, placed by `UIKit.Corner`.

    Box always pivots at centre, so the margin is the margin *plus half the tag* on both
    axes - the trap that has shipped twice (invariant 44d).
    """
    x = W / 2 + CHALLENGE_W / 2 - (TAG_MARGIN_X + TAG_W / 2)
    y = cy - CHALLENGE_H / 2 + (TAG_MARGIN_Y + TAG_H / 2)

    K.paste(sheet, K.round_rect(TAG_W, TAG_H, 28, (13, 23, 41), .88), x, y)
    K.paste(sheet, K.round_rect(TAG_W, TAG_H, 28, (255, 255, 255), .13, width=3), x, y)

    # `Scenery.Pill` stretches its label with 20 left, 16 right and 4 of bottom pad, which is
    # two units off centre - not nothing at this size, and the kind of thing this mirror
    # exists to be able to answer.
    K.shrunk(sheet, txt("ui.home.coming_soon"), x - 2, y + 2, TAG_W - 36, TAG_H - 4, 28, 18)


def _np():
    import numpy
    return numpy


def play(sheet):
    """`HomeScreen.BuildPlay` — the one control this screen exists to offer.

    **`Skins.Battle` and the word BATTLE**, with the kit's own painted glyph beside it at 112
    rather than at the third-of-the-height a pill gives a small mark. This drew a green PLAY key
    and a NEXT UP trough under it for as long as it existed, and neither has been on the screen
    since the siege became the game (invariant 44d).

    **And the halo under it went on 2026-09-20**, at the owner's instruction — it was the one
    thing on this screen that read as a light rather than as a control, and a mirror is the
    only place its size could ever be judged against the wall it lit.
    """
    cy = H - PLAY_Y
    K.paste(sheet, K.skin("Hud/btn_gold", PLAY_W, PLAY_H), W / 2, cy)

    # UIKit.FitLabel: the glyph and the caption are centred as one block, the glyph leading.
    lift = PLAY_H * .0231
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


def clearance():
    """What is left between the feature row and the loadout strip on the squarest phone.

    **The one thing this picture cannot show, because it is drawn at one canvas.** The hub is
    two fixed stacks growing toward each other — the top one hangs off the safe area and the
    foot one stands on the nav bar — and the gap between them is whatever the canvas has left.
    `Layout.CanvasFit` hands anything squarer than 7:4 a widened canvas, so the *shortest* one
    this game is ever drawn on is exactly 1080 x 1890, and that is the number to print.

    A negative reading is the hub overlapping itself, which is CRAFT.md's own recorded iPad
    report (the companion drawn 176 units through the streak box) asked before a device asks it.
    """
    top = ROW_TOP + ROW_HEIGHT
    foot = K.NAV_HEIGHT + FOOT_GAP + CHALLENGE_H + FOOT_GAP + PLAY_H + FOOT_GAP + LINE_H
    shortest = 1890.0
    return top, foot, shortest - top - foot


def screen(paired=True, verbose=False, waiting=True):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.room(sheet)
    K.rail(sheet, top=True)

    top_bar(sheet)
    resources(sheet)
    tasks(sheet, verbose=verbose)
    feature(sheet, paired, waiting)
    play(sheet)
    loadout(sheet)
    challenges(sheet)
    K.navbar(sheet, "home")
    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--no-event", action="store_true",
                    help="the feature row with nothing running, so the streak takes the width")
    ap.add_argument("--quiet", action="store_true",
                    help="the event box with nothing waiting: no rim, no badge, no COLLECT")
    ap.add_argument("--out", type=Path, default=Path("home.png"))
    args = ap.parse_args()

    out = screen(paired=not args.no_event, verbose=True, waiting=not args.quiet)

    top, foot, spare = clearance()
    print(f"  stack: {top:.0f} from the top, {foot:.0f} from the nav bar, "
          f"{spare:.0f} spare on the squarest phone (1080x1890)"
          + ("" if spare >= 0 else "   <-- THE HUB OVERLAPS ITSELF"))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}  {out.width}x{out.height}  - look at it")


if __name__ == "__main__":
    main()
