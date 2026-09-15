# -*- coding: utf-8 -*-
"""Draws the public profile and the two panels that reach it, at the size a phone draws them.

    python Tools/render_keeper.py                  # the public profile page
    python Tools/render_keeper.py --keeper         # the chooser a board row opens
    python Tools/render_keeper.py --report         # the report panel, both subjects
    python Tools/render_keeper.py --report --sent  # …with one subject already spent
    python Tools/render_keeper.py --ranks          # how the boards work (19r)
    python Tools/render_keeper.py --contact        # every live screen side by side

**Why this exists.** Every question these three raise is a picture, and no numeric gate in this
project can open one (invariant 32b). Does a stranger's profile read as *theirs* rather than as
yours; is the companion grid a wall of dim discs or a collection; do four turrets with five-star
ladders under them fit the cell they are given; does the report panel read as two deliberate
choices or as a wall of red; is the chooser's portrait big enough to be the thing that says who
this row was. The Editor cannot photograph a `ScreenSpaceOverlay` canvas, so this is where those
are judged — the way every screen in this game is (`CRAFT.md`).

**It reads the shipped content**, so a retune redraws rather than going stale: the roster out of
`manifest.json`, the turret roster out of `progression.json`, every caption out of `loc/en.json`,
and the sprites off disk. A mirror with its own numbers answers questions about a screen the game
does not draw (invariant 44d) — which is why the furniture comes from `hudkit`, shared with the
home, shop, tasks, season and loadout mirrors rather than re-cut here.

**It does not draw the tweens.** Nothing here says whether the portrait's bob reads; what it says
is whether the thing that bobs is the right size and in the right place.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

W, H = K.W, K.H

CONTENT = K.REPO / "Assets" / "StreamingAssets" / "Content"
ART = K.REPO / "Assets" / "Game" / "Art"
OUT = K.REPO / "Tools" / "out"

# ---------------------------------------------------------------- PublicProfileScreen
CARD_W = 980.0
GAP = 28.0
HEADER_H = 250.0
SAFE_TOP = 0.0                       # the reference canvas has no inset; a phone adds its own

KEEPER_DISC, KEEPER_MARGIN = 252.0, 34.0
KEEPER_H = KEEPER_DISC + 2 * KEEPER_MARGIN
WATCH_H, GROVE_H = 214.0, 258.0
PER_ROW, CELL, STEP, ROW_GAP = 6, 148.0, 156.0, 26.0
COMPANION_TITLE_ROOM, COMPANION_FOOT = 96.0, 24.0

SEAT_CELL, SEAT_STEP = 196.0, 212.0
SEAT_TITLE_ROOM, SEAT_CAPTION_H, SEAT_LADDER_H, SEAT_FOOT = 84.0, 34.0, 30.0, 16.0
LINE_H = SEAT_TITLE_ROOM + SEAT_CELL + SEAT_CAPTION_H + SEAT_LADDER_H + SEAT_FOOT

#: `UIKit.Box` pivots at centre (invariant 44d), so a left-aligned label anchored to a card's
#: left edge is positioned at `left + width / 2`. Getting this wrong draws the box — and the
#: text in it — off the side of the plate, which is what this mirror caught first.
WATCH_TEXT_LEFT, WATCH_TEXT_W = 190.0, 560.0
GROVE_TEXT_LEFT, GROVE_TEXT_W = 104.0, 520.0
VISIT_W, VISIT_INSET = 340.0, 104.0
STAR_SIZE, STAR_STEP = 34.0, 40.0

# KeeperOverlay
KEEPER_W = 860.0
KEEPER_TITLE_ROW = 150.0
KEEPER_PORTRAIT_H, KEEPER_AFTER_PORTRAIT = 250.0, 6.0
KEEPER_NAME_H, KEEPER_LINE_H, KEEPER_AFTER_NAME = 62.0, 40.0, 26.0
KEEPER_BUTTON_H, KEEPER_BETWEEN, KEEPER_FOOT = 132.0, 20.0, 34.0

# ReportOverlay
REPORT_W = 880.0
REPORT_BUTTON_H, REPORT_AFTER = 126.0, 26.0
REPORT_TITLE_ROW, REPORT_BODY_H, REPORT_AFTER_BODY = 172.0, 150.0, 26.0
REPORT_BEFORE_CANCEL, REPORT_CANCEL_H, REPORT_FOOT = 26.0, 132.0, 36.0

# ModalView.MakePanel
RIBBON_FRACTION, RIBBON_H, RIBBON_RISE, RIBBON_TILT = 0.78, 130.0, 22.0, -1.6
SCRIM = 0.72

#: `PanelStack.TallestPanel` — the shortest canvas, with the ribbon's overhang counted at both
#: ends because a modal is centred (invariant: `PanelStack`'s own note).
TALLEST = K.W * 1.75 - 2 * 87.0

INK = (59, 38, 26)

# --------------------------------------------------------------------- PanelStack
# `RanksInfoOverlay` is laid out by `PanelStack` (Domain) rather than by offsets of its own, so
# these are that type's constants and nothing else. Mirrored rather than eyeballed because the
# whole point of that type is that the arithmetic is checkable: a mirror with its own spacing
# would draw a panel the game does not (invariant 44d).
PS_SEAT, PS_SEAT_CY = 100.0, 54.0
PS_HEAD_CY, PS_HEAD_H = 30.0, 42.0
PS_BODY_TOP, PS_BODY_H = 66.0, 100.0
PS_W, PS_HOST_INSET = 960.0, 90.0
PS_TEXT_LEFT, PS_TEXT_W = 122.0, 700.0
PS_SECTION_H = PS_BODY_TOP + PS_BODY_H
PS_PITCH = PS_SECTION_H + 30.0
PS_FIRST_TOP, PS_FOOT_GAP = 60.0, 50.0
PS_BUTTON_H, PS_BUTTON_BOTTOM = 112.0, 56.0


def ps_top(row):
    return PS_FIRST_TOP + row * PS_PITCH


def ps_height(sections):
    body = (ps_top(sections - 1) + PS_SECTION_H + PS_FOOT_GAP) if sections else PS_FIRST_TOP
    return body + PS_BUTTON_H + PS_BUTTON_BOTTOM


# --------------------------------------------------------------------------- content
def strings():
    table = json.loads((CONTENT / "loc" / "en.json").read_text(encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


LOCS = strings()


def say(key, *args):
    text = LOCS.get(key, key)
    for i, value in enumerate(args):
        text = text.replace("{%d}" % i, str(value))
    return text


def roster():
    """The companion roster, in the manifest's own order."""
    manifest = json.loads((CONTENT / "manifest.json").read_text(encoding="utf-8"))
    return [c for c in manifest.get("companions", []) if c.get("id") and not c.get("disabled")]


def turrets():
    """The turret roster, in shelf order."""
    table = json.loads((CONTENT / "progression.json").read_text(encoding="utf-8"))
    models = [m for m in table.get("wards", {}).get("models", []) if m.get("id")]
    models.sort(key=lambda m: m.get("order", 0))
    return models


ROSTER = roster()
TURRETS = turrets()
COLOURS = "rgby"

#: `SiegeView.TintOf` — the seat colours, which is the one thing a turret's silhouette cannot say.
SEAT_TINT = {"r": (255, 104, 104), "g": (126, 222, 120), "b": (108, 176, 255), "y": (255, 206, 92)}


def portrait(companion_id):
    path = ART / "Companions" / f"{companion_id}.png"
    if not path.exists():
        return None
    return Image.open(path).convert("RGBA")


def turret_body(ward_id, colour):
    path = ART / "Siege" / "Wards" / f"{ward_id}_{colour}.png"
    if not path.exists():
        return None
    return Image.open(path).convert("RGBA")


# --------------------------------------------------------------------------- pieces
def disc(size, colour, alpha=1.0):
    im = Image.new("RGBA", (int(size), int(size)), (0, 0, 0, 0))
    ImageDraw.Draw(im).ellipse([0, 0, size - 1, size - 1],
                               fill=(*colour, int(255 * alpha)))
    return im


def ring(size, width, colour, alpha=1.0):
    im = Image.new("RGBA", (int(size), int(size)), (0, 0, 0, 0))
    ImageDraw.Draw(im).ellipse([width / 2, width / 2, size - 1 - width / 2, size - 1 - width / 2],
                               outline=(*colour, int(255 * alpha)), width=int(width))
    return im


def round_outline(w, h, radius, width, colour, alpha=1.0):
    im = Image.new("RGBA", (int(w), int(h)), (0, 0, 0, 0))
    ImageDraw.Draw(im).rounded_rectangle([width / 2, width / 2, w - 1 - width / 2, h - 1 - width / 2],
                                         radius=radius, outline=(*colour, int(255 * alpha)),
                                         width=int(width))
    return im


def stars(sheet, cx, cy, size, spacing, filled, count):
    """`StarRow` — one row, centred, no arc."""
    for i in range(count):
        x = cx + (i - (count - 1) * .5) * spacing
        path = K.UI / ("star_full.png" if i < filled else "star_empty.png")
        mark = K.fit(Image.open(path).convert("RGBA"), (size, size))
        K.paste(sheet, mark, x, cy)


def left_text(sheet, body, left, cy, box_w, size, floor, fill=K.CREAM, outline=3):
    """`UIKit.Shrinkable` over a `TextAnchor.MiddleLeft` label in a box `box_w` wide.

    The box is what the screen gives the label and the text begins at its left edge, so a long
    name shrinks rather than running off the plate — which is the difference between what this
    mirror draws and what Unity draws, and therefore the whole reason to mirror it rather than
    draw the string at its nominal size.
    """
    draw = ImageDraw.Draw(sheet)
    px = int(size)
    while px > int(floor) and draw.textlength(body, font=K.font(px)) > box_w:
        px -= 1
    K.text(sheet, body, left, cy, px, fill=fill, outline=outline, anchor="l")
    return px


def card_title(sheet, cy_top, key):
    """`PublicProfileScreen.CardTitle` — inset 40 from the plate's own left edge."""
    K.text(sheet, say(key).upper(), W / 2 - CARD_W / 2 + 40 + 8, cy_top + 44, 30,
           fill=K.GOLD, outline=3, anchor="l")


# --------------------------------------------------------------------------- the page
def profile(level=24, name="Fern Willow", wave=31, held=None, line=None):
    """`PublicProfileScreen` — the whole scroller, drawn from the top."""
    held = held if held is not None else {c["id"] for c in ROSTER[:9]}
    line = line or [("r", "cleaver", 3), ("g", "beacon", 2), ("b", "rime", 5), ("y", "bolt", 1)]

    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    K.plain(sheet)

    cursor = HEADER_H + GAP

    # No grove card and no worth line: both are held with the Grovement. See
    # `PublicProfileScreen`.
    cursor = keeper_card(sheet, cursor, level, name)
    cursor = watch_card(sheet, cursor, wave)
    cursor = companion_card(sheet, cursor, held)
    cursor = line_card(sheet, cursor, line)

    header(sheet)
    return sheet


def keeper_card(sheet, top, level, name):
    cy = top + KEEPER_H / 2
    K.paste(sheet, K.skin("Hud/plate_blue", int(CARD_W), int(KEEPER_H)), W / 2, cy)

    mx, my = W / 2 - 306, cy
    K.paste(sheet, K.glow(320, 2.1, K.GOLD, .26), mx, my)
    K.paste(sheet, disc(KEEPER_DISC, (8, 51, 60), .95), mx, my)
    K.paste(sheet, ring(KEEPER_DISC, 13, K.GOLD, .92), mx, my)

    face = portrait(ROSTER[0]["id"]) if ROSTER else None
    if face:
        K.paste(sheet, K.fit(face, (186, 186)), mx, my - 6)

    K.paste(sheet, disc(92, K.GOLD), mx + 80, my + 80)
    K.text(sheet, str(level), mx + 80, my + 80, 44, fill=(77, 51, 13), outline=0)

    # Two rows, re-centred on the card rather than left at the top of it — `NameY` and
    # `RibbonY`, which moved when the worth line and the star row went.
    left_text(sheet, name, W / 2 + 160 - 280, cy - 44, 560, 50, 28, fill=K.CREAM, outline=4)

    rib = K.fit(K.load("ribbon_flat")[0], (380, 74))
    K.paste(sheet, rib, W / 2 + 100, cy + 42)
    K.shrunk(sheet, say(title_key(level)), W / 2 + 100, cy + 42, 340, 50, 32,
             20, fill=(87, 56, 31), outline=0)

    return top + KEEPER_H + GAP


def title_key(level):
    """`KeeperTitle.KeyFor`, near enough for a picture: the honorific bands."""
    for key in ("ui.title.keeper", "ui.title.warden", "ui.title.sentinel"):
        if key in LOCS:
            return key
    return "ui.profile.public_title"


def watch_card(sheet, top, wave):
    cy = top + WATCH_H / 2
    K.paste(sheet, K.skin("Hud/plate_blue", int(CARD_W), int(WATCH_H)), W / 2, cy)
    card_title(sheet, top, "ui.board.endless")

    x = W / 2 - CARD_W / 2 + 104
    played = wave > 0
    if played:
        K.paste(sheet, K.glow(150, 2.1, K.SUN, .26), x, cy + 14)

    mark = K.UI / "ic_battle.png"
    if mark.exists():
        K.paste(sheet, K.fit(Image.open(mark).convert("RGBA"), (76, 76)), x, cy + 14)

    tx = W / 2 - CARD_W / 2 + WATCH_TEXT_LEFT
    left_text(sheet, say("ui.endless.best", wave) if played else say("ui.endless.unplayed"),
              tx, cy - 2, WATCH_TEXT_W, 38, 22,
              fill=K.CREAM if played else (255, 245, 224), outline=3)

    if played:
        left_text(sheet, say("ui.endless.standing", 12), tx, cy + 44, WATCH_TEXT_W, 26, 18,
                  fill=K.SUN, outline=3)

    return top + WATCH_H + GAP


def companion_card(sheet, top, held, worn=None):
    """`PublicProfileScreen.BuildCompanionCard` — what they hold, never what the roster is."""
    mine = [c for c in ROSTER if c["id"] in held]
    rows = max(1, (len(mine) + PER_ROW - 1) // PER_ROW)

    height = (COMPANION_TITLE_ROOM + rows * CELL + (rows - 1) * ROW_GAP + COMPANION_FOOT
              if mine else COMPANION_TITLE_ROOM + 70.0)

    cy = top + height / 2
    K.paste(sheet, K.skin("Hud/plate_blue", int(CARD_W), int(height)), W / 2, cy)
    card_title(sheet, top, "ui.profile.companions")

    K.text(sheet, say("ui.profile.unlocked", len(mine), len(ROSTER)),
           W / 2 + CARD_W / 2 - 40, top + 44, 26, fill=(255, 245, 224), outline=3, anchor="r")

    if not mine:
        K.text(sheet, say("ui.profile.public_no_companions"), W / 2,
               top + COMPANION_TITLE_ROOM + 20, 26, fill=(255, 245, 224), outline=3)
        return top + height + GAP

    worn = worn or mine[0]["id"]

    for i, companion in enumerate(mine):
        row, col = divmod(i, PER_ROW)
        in_row = min(PER_ROW, len(mine) - row * PER_ROW)

        x = W / 2 + (col - (in_row - 1) * .5) * STEP
        y = top + COMPANION_TITLE_ROOM + row * (CELL + ROW_GAP) + CELL / 2

        K.paste(sheet, disc(CELL, (8, 51, 60), .92), x, y)

        is_worn = companion["id"] == worn
        K.paste(sheet, ring(CELL, 9 if is_worn else 6,
                            K.GOLD if is_worn else (255, 255, 255),
                            .95 if is_worn else .22), x, y)

        face = portrait(companion["id"])
        if face:
            K.paste(sheet, K.fit(face, (CELL - 44, CELL - 44)), x, y - 4)

    return top + height + GAP


def line_card(sheet, top, line):
    cy = top + LINE_H / 2
    K.paste(sheet, K.skin("Hud/plate_blue", int(CARD_W), int(LINE_H)), W / 2, cy)
    card_title(sheet, top, "ui.loadout.wards")

    # Measured down from the plate's top edge, exactly as the screen measures it.
    cell_y = top + SEAT_TITLE_ROOM + SEAT_CELL / 2
    caption_y = cell_y + SEAT_CELL / 2 + SEAT_CAPTION_H / 2
    ladder_y = caption_y + SEAT_CAPTION_H / 2 + SEAT_LADDER_H / 2

    for i, colour in enumerate(COLOURS):
        seat = next((s for s in line if s[0] == colour), (colour, TURRETS[0]["id"], 1))
        x = W / 2 + (i - (len(COLOURS) - 1) * .5) * SEAT_STEP

        K.paste(sheet, K.skin("Hud/card", int(SEAT_CELL), int(SEAT_CELL)), x, cell_y)
        K.paste(sheet, round_outline(SEAT_CELL - 10, SEAT_CELL - 10, 26, 5,
                                     SEAT_TINT[colour], .85), x, cell_y)

        body = turret_body(seat[1], colour)
        if body:
            K.paste(sheet, K.fit(body, (SEAT_CELL - 46, SEAT_CELL - 46)), x, cell_y)

        K.shrunk(sheet, say(f"ward.{seat[1]}.name"), x, caption_y,
                 SEAT_STEP - 8, SEAT_CAPTION_H, 24, 16, fill=(255, 245, 224), outline=3)
        stars(sheet, x, ladder_y, 22, 26, seat[2], 5)

    return top + LINE_H + GAP


def header(sheet):
    fade = Image.new("RGBA", (W, int(HEADER_H + 30)), (0, 0, 0, 0))
    d = ImageDraw.Draw(fade)
    for y in range(fade.height):
        k = 1.0 - y / float(fade.height)
        d.line([(0, y), (W, y)], fill=(5, 15, 23, int(255 * .82 * k)))
    sheet.alpha_composite(fade, (0, 0))

    rib = K.load("ribbon_orange")[0].resize((470, 128), Image.LANCZOS)
    K.paste(sheet, rib, W / 2, 106)
    K.shrunk(sheet, say("ui.profile.public_title").upper(), W / 2, 106, 400, 80, 36, 22,
             fill=K.CREAM, outline=4)

    K.paste(sheet, K.skin("sq_blue", 112, 112), 92, 104)
    back = K.UI / "ic_left.png"
    if back.exists():
        K.paste(sheet, K.fit(Image.open(back).convert("RGBA"), (48, 48)), 92, 104)

    K.paste(sheet, K.skin("btn_blue", 232, 72), W - 136, 104)
    K.shrunk(sheet, say("ui.visit.report"), W - 136, 98, 190, 44, 24, 16,
             fill=K.CREAM, outline=3)


# --------------------------------------------------------------------------- the panels
def panel(height, title):
    """`ModalView.MakePanel` — scrim, plate, tilted ribbon."""
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    K.plain(sheet)
    sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * SCRIM))))
    return sheet


def draw_panel(sheet, width, height, title):
    cy = H / 2
    K.paste(sheet, K.skin("panel_main", int(width), int(height)), W / 2, cy)

    rib = K.load("ribbon_orange")[0].resize(
        (int(width * RIBBON_FRACTION), int(RIBBON_H)), Image.LANCZOS)
    rib = rib.rotate(-RIBBON_TILT, expand=True, resample=Image.BICUBIC)

    # **Minus, not plus** - the ribbon stands *proud* of the panel's top edge. `MakePanel`
    # anchors it at (.5, 1) with `anchoredPosition` (0, 22) and `UIKit.Box` always pivots at
    # centre (invariant 44d), so its centre is 22 units **above** the plate and its lower lip
    # lands 43 below it - which is exactly what `PanelStack.TitleOverhang`'s 87 is measured
    # from. Drawn at `+RIBBON_RISE` it sat 44 units low and its lip reached 87 into the plate,
    # which is far enough to swallow the top of a `PanelStack` panel's first heading. The two
    # panels this tool drew before start their content lower, so nothing could see it: the
    # wrong sign in an axis that runs the other way, which is the trap invariant 37aj names.
    ribbon_cy = cy - height / 2 - RIBBON_RISE
    K.paste(sheet, rib, W / 2, ribbon_cy)
    K.shrunk(sheet, title, W / 2, ribbon_cy, width * .68, 80, 54, 30,
             fill=K.CREAM, outline=4)

    return cy - height / 2


def report(sent=False):
    # `ReportSubjects.All`, which holds the grovement subject while that feature is
    # rebuilt — and the panel's height is the sum of what it draws, so the picture shrinks
    # with it rather than leaving a gap where the second key was.
    subjects = ["ui.report.name"]

    height = (REPORT_TITLE_ROW + REPORT_BODY_H + REPORT_AFTER_BODY
              + len(subjects) * (REPORT_BUTTON_H + REPORT_AFTER)
              + REPORT_BEFORE_CANCEL + REPORT_CANCEL_H + REPORT_FOOT)

    sheet = panel(height, say("ui.report.title"))
    top = draw_panel(sheet, REPORT_W, height, say("ui.report.title"))

    cursor = top + REPORT_TITLE_ROW
    K.shrunk(sheet, say("ui.report.body"), W / 2, cursor + REPORT_BODY_H / 2,
             REPORT_W - 180, REPORT_BODY_H, 29, 20, fill=INK, outline=0)

    cursor += REPORT_BODY_H + REPORT_AFTER_BODY

    for i, key in enumerate(subjects):
        spent = sent and i == 0
        skin = "sq_dark" if spent else "btn_red"
        K.paste(sheet, K.skin(skin, int(REPORT_W - 260), int(REPORT_BUTTON_H)),
                W / 2, cursor + REPORT_BUTTON_H / 2)
        K.shrunk(sheet, say("ui.report.sent") if spent else say(key),
                 W / 2, cursor + REPORT_BUTTON_H / 2 - 8, REPORT_W - 300, 70, 38, 22,
                 fill=K.CREAM if not spent else (168, 178, 192), outline=3)

        cursor += REPORT_BUTTON_H + REPORT_AFTER

    foot = H / 2 + height / 2 - REPORT_FOOT - REPORT_CANCEL_H / 2
    K.paste(sheet, K.skin("btn_green", int(REPORT_W - 260), int(REPORT_CANCEL_H)), W / 2, foot)
    K.shrunk(sheet, say("ui.common.cancel"), W / 2, foot - 8, REPORT_W - 300, 70, 42, 24,
             fill=K.CREAM, outline=3)

    fit_note(sheet, height)
    return sheet


def ranks_info(built_ago="6h 12m"):
    """The panel the boards screen's `i` opens — `RanksInfoOverlay`.

    **The one question it can answer**: does a paragraph explaining that a board is a tally
    taken once a day still read at the size it is drawn, in a box `UIKit.Shrinkable` will
    shrink rather than grow? Three sections is well inside what `PanelStackTests` proves fits,
    so the geometry is not in doubt; what is in doubt is the *English*, and a translation half
    again as long lands on whatever size best-fit settles at (invariant 19n — "22 against a
    floor of 22" is the tell that a string has outgrown its plate).
    """
    sections = [
        ("ic_trophy", "ui.board.info_boards_title",
         say("ui.board.info_boards_body", say("ui.board.endless"))),
        ("ic_restart", "ui.board.info_tally_title",
         say("ui.board.info_tally_built", 24, built_ago)),
        # `ic_profile`, not `ic_rank`: the sentence sends the reader to their own profile for
        # the percentile, and the rank badge is a coloured emblem that fights the two flat
        # white glyphs above it - which is a thing only this picture could say.
        ("ic_profile", "ui.board.info_place_title", say("ui.board.info_place_body", 100)),
    ]

    height = ps_height(len(sections))
    title = say("ui.board.info_title").upper()

    sheet = panel(height, title)
    top = draw_panel(sheet, PS_W, height, title)

    host_left = W / 2 - (PS_W - PS_HOST_INSET) / 2

    for row, (icon, title_key, body) in enumerate(sections):
        stop = top + ps_top(row)

        K.paste(sheet, disc(PS_SEAT, (240, 214, 163), .85),
                host_left + PS_SEAT * .6, stop + PS_SEAT_CY)
        try:
            glyph = K.fit(Image.open(ART / "Ui" / f"{icon}.png").convert("RGBA"), (70, 70))
            K.paste(sheet, glyph, host_left + PS_SEAT * .6, stop + PS_SEAT_CY)
        except FileNotFoundError:
            pass

        # `UIKit.Box` pivots at centre, so a left-aligned label anchored to the column's left
        # edge is positioned at `left + width / 2` — the pivot rule this tool caught three
        # faults with on its first run.
        # Both are **left-aligned** (`TextAnchor.MiddleLeft` and `.UpperLeft`), which is what
        # makes the glyphs a column of their own and lets somebody find the one answer they
        # came for without reading the panel. Drawn centred here it would be a ragged column
        # down the middle of a panel whose real text is a flush-left block.
        text_left = host_left + PS_TEXT_LEFT

        K.shrunk_left(sheet, say(title_key).upper(), text_left,
                      stop + PS_HEAD_CY - PS_HEAD_H / 2, PS_TEXT_W, PS_HEAD_H,
                      34, 22, fill=(71, 46, 31), outline=0)
        K.shrunk_left(sheet, body, text_left, stop + PS_BODY_TOP,
                      PS_TEXT_W, PS_BODY_H, 27, 18, fill=INK, outline=0)

    foot = H / 2 + height / 2 - PS_BUTTON_BOTTOM - PS_BUTTON_H / 2
    K.paste(sheet, K.skin("btn_green", 560, int(PS_BUTTON_H)), W / 2, foot)
    K.shrunk(sheet, say("ui.common.got_it"), W / 2, foot - 8, 500, 70, 44, 24,
             fill=K.CREAM, outline=3)

    fit_note(sheet, height)
    return sheet


def keeper(level=24, name="Fern Willow", worth=48200):
    height = (KEEPER_TITLE_ROW + KEEPER_PORTRAIT_H + KEEPER_AFTER_PORTRAIT
              + KEEPER_NAME_H + KEEPER_LINE_H + KEEPER_AFTER_NAME
              + KEEPER_BUTTON_H + KEEPER_BETWEEN + KEEPER_BUTTON_H + KEEPER_FOOT)

    sheet = panel(height, say("ui.keeper.title"))
    top = draw_panel(sheet, KEEPER_W, height, say("ui.keeper.title"))

    cursor = top + KEEPER_TITLE_ROW
    my = cursor + KEEPER_PORTRAIT_H / 2

    K.paste(sheet, disc(KEEPER_PORTRAIT_H - 24, (8, 51, 60), .95), W / 2, my)
    K.paste(sheet, ring(KEEPER_PORTRAIT_H - 24, 11, K.GOLD, .92), W / 2, my)

    face = portrait(ROSTER[0]["id"]) if ROSTER else None
    if face:
        K.paste(sheet, K.fit(face, (KEEPER_PORTRAIT_H - 100, KEEPER_PORTRAIT_H - 100)), W / 2, my + 4)

    bx = W / 2 + (KEEPER_PORTRAIT_H - 24) / 2 - 41
    by = my + (KEEPER_PORTRAIT_H - 24) / 2 - 41
    K.paste(sheet, disc(78, K.GOLD), bx, by)
    K.text(sheet, str(level), bx, by, 38, fill=(77, 51, 13), outline=0)

    cursor += KEEPER_PORTRAIT_H + KEEPER_AFTER_PORTRAIT
    K.shrunk(sheet, name, W / 2, cursor + KEEPER_NAME_H / 2, KEEPER_W - 160, KEEPER_NAME_H,
             46, 26, fill=(77, 54, 36), outline=0)

    cursor += KEEPER_NAME_H
    K.shrunk(sheet, say("ui.board.row_worth", f"{worth:,}", level),
             W / 2, cursor + KEEPER_LINE_H / 2, KEEPER_W - 160, KEEPER_LINE_H, 28, 19,
             fill=(112, 84, 61), outline=0)

    cursor += KEEPER_LINE_H + KEEPER_AFTER_NAME

    for skin, key in (("btn_green", "ui.keeper.see_grove"), ("btn_orange", "ui.keeper.see_profile")):
        K.paste(sheet, K.skin(skin, int(KEEPER_W - 220), int(KEEPER_BUTTON_H)),
                W / 2, cursor + KEEPER_BUTTON_H / 2)
        K.shrunk(sheet, say(key), W / 2 + 22, cursor + KEEPER_BUTTON_H / 2 - 8,
                 KEEPER_W - 300, 70, 38, 22, fill=K.CREAM, outline=3)
        cursor += KEEPER_BUTTON_H + KEEPER_BETWEEN

    fit_note(sheet, height)
    return sheet


def fit_note(sheet, height):
    """Prints the one number a panel can silently get wrong. `PanelStack.TallestPanel`."""
    verdict = "fits" if height <= TALLEST else f"TOO TALL by {height - TALLEST:.0f}"
    K.text(sheet, f"panel {height:.0f} / {TALLEST:.0f}  —  {verdict}",
           W / 2, H - 40, 24, fill=K.MINT if height <= TALLEST else K.ROSE, outline=3)


# --------------------------------------------------------------------------- main
def save(sheet, name):
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / name
    sheet.convert("RGB").save(path, quality=95)
    print(f"wrote {path}")
    return path


def contact():
    # **The chooser is not on the sheet, because a board row no longer opens one.** With the
    # grovement held there is one door left, so `LeaderboardScreen.Visit` walks straight into
    # the profile — and a contact sheet carrying a panel the game never raises is the fault
    # invariant 44d names. `--keeper` still draws it, which is a deliberate ask rather than
    # something the gate shows you every run.
    sheets = [profile(), report(), ranks_info()]
    pad = 24
    strip = Image.new("RGB", (len(sheets) * W + (len(sheets) + 1) * pad, H + 2 * pad), (16, 18, 22))
    for i, sheet in enumerate(sheets):
        strip.paste(sheet.convert("RGB"), (pad + i * (W + pad), pad))
    return strip


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--keeper", action="store_true",
                    help="the chooser a board row used to open (held with the grovement)")
    ap.add_argument("--report", action="store_true", help="the report panel")
    ap.add_argument("--sent", action="store_true", help="…with one subject already spent")
    ap.add_argument("--unplayed", action="store_true",
                    help="a keeper who has never run the Infinite lane")
    ap.add_argument("--ranks", action="store_true", help="how the boards work (19r)")
    ap.add_argument("--contact", action="store_true", help="every live screen side by side")
    args = ap.parse_args()

    if args.contact:
        save(contact(), "keeper_contact.png")
        return

    if args.keeper:
        save(keeper(), "keeper_panel.png")
        return

    if args.report:
        save(report(sent=args.sent), "keeper_report.png")
        return

    if args.ranks:
        save(ranks_info(), "keeper_ranks_info.png")
        return

    save(profile(wave=0 if args.unplayed else 31), "keeper_profile.png")


if __name__ == "__main__":
    main()
