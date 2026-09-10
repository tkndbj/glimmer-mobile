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
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image                                       # noqa: E402
import hudkit as K                                          # noqa: E402

W, H = K.W, K.H

# HomeScreen
TOPBAR_Y, TOPBAR_H = 116.0, 168.0
RES_Y, RES_H = 250.0, 92.0
DAILY_Y, DAILY_W, DAILY_H = 424.0, 910.0, 240.0
ROW_TOP, ROW_HEIGHT, ROW_WIDTH, ROW_GAP = 556.0, 300.0, 900.0, 24.0
HERO_Y, HERO_W, HERO_H = 150.0, 620.0, 700.0
PLAY_W, PLAY_H = 600.0, 172.0


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


def daily(sheet):
    """`HomeScreen.BuildDaily` — the kit's panel, a progress rail and three chests on it."""
    cy = DAILY_Y
    K.paste(sheet, K.skin("Hud/panel", DAILY_W, DAILY_H), W / 2, cy)

    top = cy - DAILY_H / 2
    K.text(sheet, "DAILY BONUSES", W / 2 - DAILY_W / 2 + 148, top + 38, 32, fill=K.GOLD, anchor="l")
    K.text(sheet, "resets in 6h 12m", W / 2 + DAILY_W / 2 - 40, top + 38, 26,
           fill=(255, 242, 214), outline=0, anchor="r")

    try:
        K.paste(sheet, K.fit(Image.open(K.UI / "ic_gift.png").convert("RGBA"), (72, 72)),
                W / 2 - DAILY_W / 2 + 62, top + 42)
    except FileNotFoundError:
        pass

    ty = cy - 4
    K.paste(sheet, K.skin("Hud/trough", 816, 42), W / 2, ty)
    K.paste(sheet, K.tint(K.skin("Hud/fill", 806 * .55, 32), K.GOLD),
            W / 2 - 403 + 806 * .55 / 2, ty)

    for i in range(3):
        px = W / 2 - 408 + 816 * ((i + 1) / 3.0)
        ready = i == 0
        try:
            chest = Image.open(K.UI / ("ic_chest.png" if not (i == 2) else "ic_chest_open.png")).convert("RGBA")
        except FileNotFoundError:
            continue
        chest = K.fit(chest, (96 if ready else 78, 96 if ready else 78))
        if not ready and i != 2:
            chest = K.tint(chest, (128, 138, 148))
        K.paste(sheet, chest, px, ty - 8)

    K.text(sheet, "PLAY 2 MORE GLADES FOR THE NEXT CHEST", W / 2, cy + DAILY_H / 2 - 24, 24,
           fill=(255, 242, 214), outline=0)


def feature(sheet, paired=True):
    """`HomeScreen.BuildFeature` — the streak, and whatever the player is working toward.

    **This drew two empty boxes with a "3" in them for as long as it existed, and that was a
    lie about the screen.** The real cards carry a 138-unit flame or event mark on the left at
    `gx = -w/2 + 115`, the count at `vx = -w/2 + 299` with its caption under it, a full-width
    strip along the bottom holding an icon and a line of copy, and a gold badge on the corner —
    every one of which `HomeScreen.BuildStreakBox` and `BuildEventBox` have always built. A
    mirror that under-draws makes the screen look emptier than it is, which is the one way a
    render can send you off to fix something that was never broken.
    """
    half = (ROW_WIDTH - ROW_GAP) * .5
    cy = ROW_TOP + ROW_HEIGHT * .5
    x = (ROW_WIDTH - half) * .5

    boxes = [(-x if paired else 0.0, half if paired else ROW_WIDTH, K.SUN, "FLAME", "",
              "4", "NIGHT STREAK", "ic_gift", "KEEP IT UP - 5 GEMS", 2),
             (x, half, K.BLOOM, "SUMMER EVENT", "2d 14h",
              "7/12", "GLADES", None, None, 3)]
    if not paired:
        boxes = boxes[:1]

    for bx, bw, colour, title, meta, value, caption, icon, line, badge in boxes:
        cx = W / 2 + bx
        K.paste(sheet, K.skin("Hud/card", bw, ROW_HEIGHT), cx, cy)

        # the lamp along the top edge, which is what tells the two boxes apart now that the
        # plate is the same navy on both
        K.paste(sheet, K.glow(max(bw * .78, 86), 1.9, colour, .38), cx, cy - ROW_HEIGHT / 2 + 14)

        K.text(sheet, title, cx - bw / 2 + 30, cy - ROW_HEIGHT / 2 + 36, 25,
               fill=colour, outline=2, anchor="l")
        if meta:
            K.text(sheet, meta, cx + bw / 2 - 30 - 70, cy - ROW_HEIGHT / 2 + 36, 23,
                   fill=(255, 242, 214), outline=0, anchor="r")

        # the flame or the event mark, on its own glow
        gx = cx - bw / 2 + 115
        K.paste(sheet, K.glow(200, 2.0, colour, .30), gx, cy + 4)
        art = "Flame/f00" if icon else "ic_stars"
        try:
            mark = Image.open(K.UI / f"{art}.png").convert("RGBA")
            K.paste(sheet, K.fit(mark, (138, 138) if icon else (128, 128)), gx, cy + 4)
        except FileNotFoundError:
            pass

        # the count, and what it counts
        vx = cx - bw / 2 + 299
        # `FeatureValue` places the count at (vx, +20) and the caption at (vx, -32) in
        # Unity's y-up space, so on an image they are 20 *above* and 32 *below* the
        # card's middle. Drawn both below, they overlap by twelve units — which is what
        # this did, and it read as a bug in the screen rather than in the mirror.
        K.text(sheet, value, vx, cy - 20, 62, fill=K.CREAM, outline=3)
        K.text(sheet, caption, vx, cy + 32, 22, fill=colour, outline=0)

        # the strip along the bottom: a line of copy, or a bar with milestone pips on it
        d = K.ImageDraw.Draw(sheet)
        sw = bw - 72
        sy = cy + ROW_HEIGHT / 2 - 46
        d.rounded_rectangle([cx - sw / 2, sy - 27, cx + sw / 2, sy + 27], 18, fill=(3, 10, 13, 158))

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


def screen(paired=True):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.room(sheet)
    K.rail(sheet, top=True)

    top_bar(sheet)
    resources(sheet)
    daily(sheet)
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

    out = screen(paired=not args.no_event)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}  {out.width}x{out.height}  - look at it")


if __name__ == "__main__":
    main()
