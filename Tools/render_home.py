# -*- coding: utf-8 -*-
"""Draws the hub at the size a phone draws it, with the real sprites.

    python Tools/render_home.py
    python Tools/render_home.py --no-event      # the row with no event running
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
HERO_Y, HERO_W, HERO_H = 150.0, 620.0, 700.0
PLAY_W, PLAY_H = 600.0, 172.0

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
        cx = W / 2 + (i - 1) * 318
        K.paste(sheet, K.skin("Hud/trough", 276, 84), cx, RES_Y)

        gx = cx - 276 / 2 + 66
        K.paste(sheet, K.glow(120, 2.0, colour, .30), gx, RES_Y)
        try:
            K.paste(sheet, K.fit(Image.open(K.UI / f"{glyph}.png").convert("RGBA"), (56, 56)), gx, RES_Y)
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

    # the rays and the shelf, both clipped to the plate in the game (RectMask2D)
    plate = Image.new("RGBA", (int(ROW_WIDTH) - 12, int(TASKS_H) - 12), (0, 0, 0, 0))
    fan = K.rays(256, 14).resize((860, 860), Image.LANCZOS)
    tinted = Image.new("RGBA", fan.size, (*K.SUN, 0))
    tinted.putalpha(fan.point(lambda v: int(v * .22)))
    plate.alpha_composite(tinted, (plate.width // 2 - 430, plate.height // 2 - 430 + 34))

    shelf = K.glow(170, 1.7, (41, 5, 61), .34).resize((840, 170), Image.LANCZOS)
    plate.alpha_composite(shelf, (plate.width // 2 - 420, plate.height // 2 - 85 + 84))
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


def feature(sheet, paired=True):
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
    """
    half = (ROW_WIDTH - ROW_GAP) * .5
    cy = ROW_TOP + ROW_HEIGHT * .5
    x = (ROW_WIDTH - half) * .5

    # plate, lamp alpha, title colour, caption colour — `BuildStreakBox` passes `Pal.Cream`
    # as its tint and `BuildEventBox` `Pal.Bloom`, and the lamp is white at `edge * .55` in
    # both, not the box's colour.
    boxes = [(-x if paired else 0.0, half if paired else ROW_WIDTH,
              "plate_orange", .30, K.CREAM, K.CREAM, "STREAK", "",
              "4", "4 NIGHTS", "ic_gift", "KEEP IT UP - 5 GEMS", 2),
             (x, half, "plate_violet", .50, K.BLOOM, K.BLOOM, "THE FIRST WATCH", "2d 14h",
              "7/12", "MARKS", None, None, 3)]
    if not paired:
        boxes = boxes[:1]

    for (bx, bw, plate, edge, colour, capcol, title, meta,
         value, caption, icon, line, badge) in boxes:
        cx = W / 2 + bx
        K.paste(sheet, K.skin("Hud/" + plate, bw, ROW_HEIGHT), cx, cy)

        # the lamp along the top edge — white, and the box's own colour is the plate
        K.paste(sheet, K.glow(max(bw * .78, 86), 1.9, (255, 255, 255), edge * .55),
                cx, cy - ROW_HEIGHT / 2 + 14)

        K.text(sheet, title, cx - bw / 2 + 30, cy - ROW_HEIGHT / 2 + 36, 25,
               fill=colour, outline=0, anchor="l")
        if meta:
            K.text(sheet, meta, cx + bw / 2 - 30 - 70, cy - ROW_HEIGHT / 2 + 36, 23,
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
        # `FeatureValue` places the count at (vx, +20) and the caption at (vx, -32) in
        # Unity's y-up space, so on an image they are 20 *above* and 32 *below* the
        # card's middle. Drawn both below, they overlap by twelve units — which is what
        # this did, and it read as a bug in the screen rather than in the mirror.
        K.text(sheet, value, vx, cy - 20, 62, fill=K.CREAM, outline=3)
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


def hero(sheet):
    """`HomeScreen.BuildHero` â€” the beam, the pad, the companion and its name plate."""
    cx, cy = W / 2, H / 2 + HERO_Y
    K.paste(sheet, K.glow(500, 2.1, K.AQUA, .18), cx, cy)
    beam = K.fit(K.load("Hud/beam")[0], (408, 296))
    K.paste(sheet, K.tint(beam, (255, 255, 255), .50), cx, cy - 126)
    pad = K.fit(K.load("Hud/lander")[0], (352, 214))
    K.paste(sheet, pad, cx, cy + 132)
    try:
        critter = Image.open(K.REPO / "Assets" / "Game" / "Art" / "Critters" / "c5" / "f00.png").convert("RGBA")
        K.paste(sheet, K.fit(critter, (286, 286)), cx, cy - 66)
    except FileNotFoundError:
        pass
    K.paste(sheet, K.fit(K.load("Hud/title")[0], (384, 103)), cx, cy + 258)
    K.text(sheet, "MONARCH", cx, cy + 256, 34, fill=K.SUN)


def play(sheet):
    """`HomeScreen.BuildPlay` — the one control this screen exists to offer."""
    cy = H - K.NAV_HEIGHT - 190
    K.paste(sheet, K.glow(760, 2.1, K.SUN, .26), W / 2, cy)
    K.paste(sheet, K.skin("btn_green", PLAY_W, PLAY_H), W / 2, cy)
    K.text(sheet, "PLAY", W / 2, cy, 66, outline=4)

    ny = H - K.NAV_HEIGHT - 84
    K.paste(sheet, K.skin("Hud/trough", 640, 62), W / 2, ny)
    K.text(sheet, "NEXT UP - THE FIRST WATCH", W / 2, ny, 27, fill=(255, 245, 220), outline=0)


def screen(paired=True, verbose=False):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.room(sheet)
    K.rail(sheet, top=True)

    top_bar(sheet)
    resources(sheet)
    tasks(sheet, verbose=verbose)
    feature(sheet, paired)
    hero(sheet)
    play(sheet)
    K.navbar(sheet, "home")
    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--no-event", action="store_true",
                    help="the feature row with nothing running, so the streak takes the width")
    ap.add_argument("--out", type=Path, default=Path("home.png"))
    args = ap.parse_args()

    out = screen(paired=not args.no_event, verbose=True)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}  {out.width}x{out.height}  - look at it")


if __name__ == "__main__":
    main()
