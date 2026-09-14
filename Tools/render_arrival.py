# -*- coding: utf-8 -*-
"""Draws the purchase-arriving panel at the size a phone draws it, with the real sprites.

    python Tools/render_arrival.py                  # the ordinary wait, naming what was bought
    python Tools/render_arrival.py --relaxed        # once it has taken long enough to offer a way out
    python Tools/render_arrival.py --many           # two purchases in flight, so it names neither
    python Tools/render_arrival.py --contact        # all three side by side
    python Tools/render_arrival.py --out out/arrival.png

**Why this exists.** The panel replaced a word on the face of a button, and the whole of what
it is for is that a payment deserves the middle of the screen rather than a corner of a card.
Nothing in this project can open a PNG on the way to a build and the Editor cannot photograph a
`ScreenSpaceOverlay` canvas, so whether a panel *reads* is judged here — `StoreArrivalTests`
proves it cannot get stuck and says nothing whatever about how it looks.

**What it is for in particular is the four rows.** `ShopArrivalOverlay` lays itself out with
absolute offsets rather than a cursor, which is right for a panel with one height and is
exactly the arrangement that shipped `ShopSupplyOverlay` with its held-line printed through its
own button. This adds them up with the real font at the real size, so a row that runs into the
one under it is visible rather than argued about. The second thing it answers is the ring: an
amber sweep on a *light parchment* is a different question from `BusyVeil`'s cream arc on a
dark plate, and only a picture settles it.

**It is a mirror, and mirrors drift.** Every constant below is named after the field it copies
(`ShopArrivalOverlay.RingSize`, `ModalView.MakePanel`'s ribbon, `ArrivalWatch.Patience`), so a
change on one side is findable on the other — and when a device disagrees with this picture,
the picture is the one that is wrong. It draws the furniture and the copy; it does not turn the
ring, so nothing here says whether the spin reads as a wait rather than as a decoration.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

REPO = K.REPO
LOC = REPO / "Assets" / "StreamingAssets" / "Content" / "loc" / "en.json"

W, H = K.W, K.H

# --------------------------------------------------------------- ShopArrivalOverlay
PANEL_W = 820.0
HEAD_ROOM = 150.0
RING_SIZE = 340.0
ART_SIZE = 260.0
NAME_H = 56.0
NOTE_H = 96.0
BUTTON_H = 112.0

#: Deeper than the panels this borrows from: `panel_main` carries a 60-unit nine-slice rim, so
#: a button seated in the fifties has its foot on the parchment's lip rather than on its face.
#: This render is what found that, and it is the kind of thing only a render can find.
FOOT_ROOM = 88.0

NAME_SIZE, NOTE_SIZE, BUTTON_SIZE = 34, 27, 32
TEXT_W = 660.0
BUTTON_W = 440.0

#: `ShopArrivalOverlay.Ink` / `.Accent` — dark copy, because `panel_main` is a light parchment
#: and the board's own accents were reported unreadable on it (`ShopSupplyOverlay.Ink`).
INK = (92, 64, 46)
ACCENT = (219, 120, 31)

#: `Art.Ring` / `Art.Arc(160, 11, .28)` — the track and the sweep turning inside it. The track
#: is `Pal.A(Ink, .22f)`: faint enough to be a groove rather than a second mark, strong enough
#: that the sweep reads as travelling round something.
RING_THICK = 11.0
SWEEP = 0.28
TRACK_ALPHA = 0.26

# --------------------------------------------------------------------- ModalView
#: `MakePanel`: the ribbon is 78% of the panel's width, 130 tall, standing 22 proud of the
#: top edge and leaning by `-1.6` degrees.
RIBBON_FRACTION, RIBBON_H, RIBBON_RISE, RIBBON_TILT = 0.78, 130.0, 22.0, -1.6
TITLE_SIZE = 54

#: `UIKit.Scrim(Content, .72f)`.
SCRIM = 0.72

#: `UIKit.Halo(panel, Pal.Sun, 620, .20)`.
HALO_SIZE, HALO_ALPHA = 620.0, 0.20

#: `UIKit.PillFaceLift` — a kit button's caption sits on its painted face rather than in the
#: middle of its rect. Measured off the moulds by `make_hud_kit_art.py` rather than typed
#: (invariant 44b). Unity's y counts up and an image's counts down, so it is *subtracted* here
#: — the axis flip `UIKit`'s own render mirror was caught getting wrong (invariant 44d).
FACE_LIFT = 0.0231

def panel_height(relaxed):
    """`ShopArrivalOverlay.Build`'s sum — two heights, and the button is the term that moves."""
    y = HEAD_ROOM + RING_SIZE + 18.0 + NAME_H + 12.0 + NOTE_H + 22.0
    if relaxed:
        y += BUTTON_H + 22.0
    return y + FOOT_ROOM

#: `PanelStack.TallestPanel` — the shortest canvas this game is drawn on, with the ribbon's
#: overhang counted at *both* ends because a modal is centred.
TALLEST = K.W * 1.75 - 2 * 87.0


def strings():
    table = json.loads(LOC.read_text(encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


def wrapped(draw, s, size, width):
    """`UIKit.Titled(..., wrap: true)` — greedy, which is what Unity's own wrapping is."""
    f = K.font(size)
    lines, line = [], ""

    for word in s.split():
        trial = word if not line else line + " " + word
        if draw.textlength(trial, font=f) <= width or not line:
            line = trial
        else:
            lines.append(line)
            line = word

    if line:
        lines.append(line)
    return lines


def ring(size, thickness, sweep, colour, alpha=1.0):
    """`Art.Ring` when `sweep` is 1, `Art.Arc` otherwise. Drawn from twelve o'clock."""
    px = int(round(size))
    im = Image.new("RGBA", (px * 4, px * 4), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    box = [thickness * 2, thickness * 2, px * 4 - thickness * 2, px * 4 - thickness * 2]
    d.arc(box, -90, -90 + 360 * sweep,
          fill=(*colour, int(round(255 * alpha))), width=int(round(thickness * 4)))

    return im.resize((px, px), Image.LANCZOS)


def panel(loc, relaxed, named):
    """One panel, drawn onto its own transparent sheet at canvas scale."""
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(sheet)

    height = panel_height(relaxed)
    cx, cy = W / 2, H / 2
    top = cy - height / 2

    K.paste(sheet, K.skin("panel_main", PANEL_W, height), cx, cy)

    # The ribbon, leaning the way `MakePanel` leans it. PIL turns anticlockwise for a positive
    # angle and so does `Quaternion.Euler(0, 0, z)`, so the sign is carried as written
    # (invariant 37aj, and the trap `render_shop.py` records paying for).
    ribbon = K.skin("ribbon_orange", PANEL_W * RIBBON_FRACTION, RIBBON_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.text(plate, loc["ui.shop.arriving"], plate.width / 2, plate.height / 2, TITLE_SIZE,
           fill=K.CREAM, outline=4)
    plate = plate.rotate(RIBBON_TILT, resample=Image.BICUBIC, expand=True)
    K.paste(sheet, plate, cx, top + RIBBON_RISE)

    # The rows, walked down from the panel's top edge exactly as the overlay walks them.
    y = HEAD_ROOM
    ring_y = top + y + RING_SIZE / 2;   y += RING_SIZE + 18.0
    name_y = top + y + NAME_H / 2;      y += NAME_H + 12.0
    note_y = top + y + NOTE_H / 2;      y += NOTE_H + 22.0
    button_y = top + y + BUTTON_H / 2

    sheet.alpha_composite(K.glow(HALO_SIZE, 2.1, K.SUN, HALO_ALPHA),
                          (int(cx - HALO_SIZE / 2), int(ring_y - HALO_SIZE / 2)))

    K.paste(sheet, ring(RING_SIZE, RING_THICK, 1.0, INK, TRACK_ALPHA), cx, ring_y)
    K.paste(sheet, ring(RING_SIZE, RING_THICK, SWEEP, ACCENT), cx, ring_y)

    if named:
        art = Image.open(K.UI / "Shop" / "gems_4.png").convert("RGBA")
        K.paste(sheet, K.fit(art, (ART_SIZE, ART_SIZE)), cx, ring_y)
        K.text(sheet, loc["store.product.gg_gems_4"], cx, name_y, NAME_SIZE, fill=INK, outline=0)
    else:
        K.text(sheet, loc["ui.shop.arriving_many"].format(2), cx, name_y, NAME_SIZE,
               fill=INK, outline=0)

    note = loc["ui.shop.awaiting" if relaxed else "ui.shop.arriving_note"]
    lines = wrapped(draw, note, NOTE_SIZE, TEXT_W)
    for i, line in enumerate(lines):
        K.text(sheet, line, cx, note_y - NOTE_H / 2 + NOTE_SIZE * (0.72 + 1.25 * i),
               NOTE_SIZE, fill=INK, outline=0)

    if relaxed:
        K.paste(sheet, K.skin("btn_blue", BUTTON_W, BUTTON_H), cx, button_y)
        K.text(sheet, loc["ui.common.got_it"], cx, button_y - BUTTON_H * FACE_LIFT,
               BUTTON_SIZE, fill=K.CREAM, outline=3)

    return sheet, dict(top=top, height=height, ring=ring_y, name=name_y, note=note_y,
                       button=button_y if relaxed else None, lines=len(lines))


def shot(loc, relaxed, named):
    """The panel over a scrim over a plain ground, which is all a modal ever has behind it."""
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * SCRIM))))

    body, marks = panel(loc, relaxed, named)
    sheet.alpha_composite(body)
    return sheet, marks


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--relaxed", action="store_true",
                    help="after ArrivalWatch.Patience: the sentence changes and a way out appears")
    ap.add_argument("--many", action="store_true",
                    help="two purchases in flight, so the panel names neither")
    ap.add_argument("--contact", action="store_true", help="all three side by side")
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    loc = strings()

    if args.contact:
        cuts = [shot(loc, False, True), shot(loc, True, True), shot(loc, False, False)]
        sheet = Image.new("RGBA", (W * 3, H), (0, 0, 0, 255))
        for i, (cut, _) in enumerate(cuts):
            sheet.alpha_composite(cut, (W * i, 0))
        marks = cuts[0][1]
        out = args.out or "Tools/out/arrival_contact.png"
    else:
        sheet, marks = shot(loc, args.relaxed, not args.many)
        out = args.out or "Tools/out/arrival.png"

    path = REPO / out
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(path)

    print(f"panel {PANEL_W:.0f} x {panel_height(False):.0f} waiting, "
          f"{panel_height(True):.0f} once a way out is offered   "
          f"(tallest a centred panel may be: {TALLEST:.0f})")
    print(f"  ring   centre {marks['ring'] - marks['top']:7.1f} from the panel's top")
    print(f"  name   centre {marks['name'] - marks['top']:7.1f}")
    print(f"  note   centre {marks['note'] - marks['top']:7.1f}  ({marks['lines']} line(s))")
    if marks["button"] is not None:
        foot = marks["button"] + BUTTON_H / 2 - marks["top"]
        print(f"  button centre {marks['button'] - marks['top']:7.1f}, foot at "
              f"{foot:.1f} of {marks['height']:.0f} — {marks['height'] - foot:.0f} clear "
              f"of the edge, against a 60-unit rim")
    print(f"wrote {out}")

    for relaxed in (False, True):
        if panel_height(relaxed) > TALLEST:
            print("FAIL: the panel is taller than the shortest canvas holds")
            return 1

    # The note is the one row that can grow: it carries two different sentences and the longer
    # one is the shop's own, which nothing here owns. Both have to fit the room reserved.
    for key in ("ui.shop.arriving_note", "ui.shop.awaiting"):
        lines = wrapped(ImageDraw.Draw(Image.new("RGBA", (1, 1))), loc[key], NOTE_SIZE, TEXT_W)
        if len(lines) * NOTE_SIZE * 1.25 > NOTE_H:
            print(f"FAIL: '{key}' is {len(lines)} lines, taller than the room reserved for it")
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
