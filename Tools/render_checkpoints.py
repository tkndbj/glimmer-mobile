# -*- coding: utf-8 -*-
"""Draws the Infinite lane's checkpoint sheet (`EndlessCheckpointOverlay`) at rest, off the shipped table.

    python Tools/render_checkpoints.py                       # best 42, the beginning chosen
    python Tools/render_checkpoints.py --best 42 --chosen 22 # a checkpoint chosen
    python Tools/render_checkpoints.py --best 12             # nothing open yet
    python Tools/render_checkpoints.py --best 9999           # every row open
    python Tools/render_checkpoints.py --short               # the squarest canvas this game lays out for
    python Tools/render_checkpoints.py --contact             # the four states side by side

**Why this exists.** The sheet is the deal sheet's frame (`VictoryFrame`) with rows cut to what a
checkpoint says (MODES.md 43f), and every row is content: the wave, the best that opens it and the
head start are read off `progression.json`, so a retune that adds a row or a translation that
lengthens a sentence is one push away at all times. This measures every caption against the box
it is drawn in (invariant 19n) and prints where each one settled - `UIKit.Shrinkable` truncates
silently, and the tell is a caption at its floor.

**It is a mirror** (invariant 44d): every constant is named after the field it copies
(`EndlessCheckpointOverlay.RowH`, `.DiscSize`, `.KeySize`), and the frame's own geometry is
`render_challenges.py`'s, which mirrors `VictoryFrame` for the deal sheet. When a device disagrees
with this picture the picture is the one that is wrong.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image                                       # noqa: E402
import hudkit as K                                          # noqa: E402
import render_challenges as RC                              # noqa: E402

REPO = K.REPO
CONTENT = REPO / "Assets" / "StreamingAssets" / "Content"
TABLE = CONTENT / "progression.json"

#: `EndlessCheckpointOverlay.NoteY` / `.RowsTop` / `.RowH` / `.RowGap` / `.Tail` / `.FootH`.
NOTE_Y, ROWS_TOP, ROW_H, ROW_GAP, TAIL, FOOT_H = 150.0, 200.0, 150.0, 12.0, 30.0, 170.0
#: `EndlessCheckpointOverlay.PanelW` / `.RowW` / `.DiscSize` / `.DiscX` / `.TextX` / `.TextW` / `.KeySize` / `.KeyInset`.
PANEL_W, ROW_W, DISC_SIZE, DISC_X, TEXT_X, TEXT_W = 1000.0, 880.0, 118.0, 84.0, 168.0, 420.0
KEY_W, KEY_H, KEY_INSET = 250.0, 100.0, 16.0

#: `CanvasFit.ShortestCanvas`.
SHORT = (1080, int(1080 * 1.75))

DARK_INK = (82, 54, 15)          # the medal's number: `new Color(.32f, .21f, .06f)`


def checkpoints():
    """`endlessCheckpoints.rows` as the reader resolves it: (wave, unlockAt, cogs)."""
    block = json.loads(TABLE.read_text(encoding="utf-8")).get("endlessCheckpoints") or {}
    return [(r["wave"], r["unlockAt"], r.get("cogs", 0)) for r in block.get("rows") or []]


def line_for(txt, row, best, open_):
    """`EndlessCheckpointOverlay.LineFor`."""
    if row is None:
        return txt("ui.endless.checkpoint.beginning")
    wave, unlock, cogs = row
    if not open_:
        return txt("ui.endless.checkpoint.locked_line").replace("{0}", str(unlock)).replace("{1}", str(best))
    if cogs <= 0:
        return txt("ui.endless.checkpoint.bare")
    if cogs == 1:
        return txt("ui.endless.checkpoint.boost_one")
    return txt("ui.endless.checkpoint.boost").replace("{0}", str(cogs))


def one_line_size(caption, room, size, floor):
    """`UIKit.OneLine`: the size a caption settles at on one line in its room."""
    px = size
    while px > floor and K.font(px).getlength(caption) > room:
        px -= 1
    return px


def render(txt, canvas=(1080, 1920), best=42, chosen=1):
    W, H = canvas
    K.W, K.H = W, H
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    K.plain(sheet)
    sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .72))))

    rows = checkpoints()
    # `EndlessCheckpointTable.Resolve(chosen, best)`: a choice the best does not support is the beginning.
    picked = next((r for r in rows if r[0] == chosen and best >= r[1]), None)

    row_count = len(rows) + 1
    rows_h = row_count * (ROW_H + ROW_GAP) - ROW_GAP
    panel_h = ROWS_TOP + rows_h + TAIL + FOOT_H

    # `VictoryFrame.MakeFit`.
    reach = panel_h + RC.CREST_REACH
    fit = min(1.0, (H - 20 * 2) / reach)

    block = Image.new("RGBA", (1680, int(reach + 500)), (0, 0, 0, 0))
    bw, bh = block.size
    bcx = bw / 2
    # `VictoryFrame.Build` stands the window at (0, -CrestReach/2) inside the fit - Unity's y runs
    # up, so that is half the crest *below* the fit's centre, which is what centres the block the
    # player sees (window plus crest). Image rows run down, hence the plus.
    panel_top = bh / 2 + RC.CREST_REACH / 2 - panel_h / 2
    crest_cy = panel_top - RC.CREST_REACH / 2

    fan = K.rays(1680, 14)
    fan_rgba = Image.new("RGBA", fan.size, (255, 204, 77, 0))
    fan_rgba.putalpha(fan.point(lambda v: int(v * .20)))
    K.paste(block, fan_rgba, bcx, crest_cy + 240)
    K.paste(block, K.glow(1240, 2.4, (255, 209, 97), .22), bcx, crest_cy + 200)

    window = K.tint(K.skin("Win/window", PANEL_W, panel_h), RC.PANEL_INK)
    K.paste(block, window, bcx, panel_top + panel_h / 2)
    K.paste(block, K.glow(int(PANEL_W - 60), 1.9, (255, 245, 209), .11), bcx, panel_top + 70)

    crown = K.fit(K.load("Win/crown")[0], (180, 162))
    K.paste(block, crown, bcx, panel_top - RC.CROWN_Y)
    banner = K.fit(K.load("Win/banner")[0], (RC.BANNER_W, RC.BANNER_H_WIN))
    K.paste(block, banner, bcx, panel_top - RC.BANNER_Y)

    floors = []
    floors.append(("word", K.shrunk(block, txt("ui.endless.checkpoint.title"), bcx,
                                    panel_top - RC.BANNER_Y - RC.WORD_LIFT, RC.WORD_W, RC.WORD_H,
                                    58, 32, outline=5), 32))
    floors.append(("note", K.shrunk(block, txt("ui.endless.checkpoint.note"), bcx, panel_top + NOTE_Y,
                                    860, 60, 32, 20, fill=(255, 245, 224), outline=2), 20))

    y = panel_top + ROWS_TOP + ROW_H / 2
    for row in [None] + rows:
        beginning = row is None
        wave = 1 if beginning else row[0]
        open_ = beginning or best >= row[1]
        selected = (picked is None and beginning) or (picked is not None and not beginning and picked[0] == wave)

        K.paste(block, K.round_rect(ROW_W, ROW_H, 28, (0, 0, 0), .32), bcx, y)
        edge = (K.GOLD, .62) if selected else ((255, 245, 219), .14)
        K.paste(block, K.round_rect(ROW_W, ROW_H, 28, edge[0], edge[1], width=3), bcx, y)
        left = bcx - ROW_W / 2

        # The medallion, lit when it can be started from.
        K.paste(block, K.glow(int(DISC_SIZE * 1.8), 2.1, K.GOLD if selected else (255, 116, 212),
                              .34 if selected else .14), left + DISC_X, y)
        disc = K.fit(K.load("Hud/cap_on" if open_ else "Hud/cap_off")[0], (DISC_SIZE, DISC_SIZE))
        K.paste(block, disc, left + DISC_X, y)
        if open_:
            floors.append(("disc %d" % wave,
                           K.shrunk(block, str(wave), left + DISC_X, y - 3, DISC_SIZE * .78, DISC_SIZE * .56,
                                    50, 28, fill=DARK_INK, outline=0), 28))
        else:
            lock = K.fit(Image.open(K.UI / "ic_padlock.png").convert("RGBA"), (DISC_SIZE * .46, DISC_SIZE * .46))
            K.paste(block, lock, left + DISC_X, y)

        name = txt("ui.endless.wave").replace("{0}", str(wave))
        name_fill = K.GOLD if selected else (K.CREAM if open_ else (205, 196, 180))
        floors.append(("name %d" % wave,
                       K.shrunk_left(block, name, left + TEXT_X, y - 30 - 29, TEXT_W, 58, 46, 26,
                                     fill=name_fill, outline=3), 26))
        floors.append(("line %d" % wave,
                       K.shrunk_left(block, line_for(txt, row, best, open_), left + TEXT_X, y + 30 - 33,
                                     TEXT_W, 66, 28, 18, fill=(255, 245, 224), outline=2), 18))

        key_cx = bcx + ROW_W / 2 - KEY_INSET - KEY_W / 2
        if selected:
            K.paste(block, K.round_rect(KEY_W, KEY_H, 24, K.GOLD, .95), key_cx, y)
            floors.append(("chosen tag", K.shrunk(block, txt("ui.endless.checkpoint.chosen").upper(), key_cx, y,
                                                  KEY_W, KEY_H, 32, 18, fill=K.INK, outline=0), 18))
        else:
            caption = txt("ui.endless.checkpoint.choose" if open_ else "ui.endless.checkpoint.locked").upper()
            K.paste(block, K.skin("btn_green" if open_ else "btn_gray", KEY_W, KEY_H), key_cx, y)
            glyph = 0
            if not open_:
                glyph = KEY_H / 3
                lock = K.fit(Image.open(K.UI / "ic_padlock.png").convert("RGBA"), (glyph, glyph))
            room = KEY_W - 40 - (glyph + 14 if glyph else 0)
            size = one_line_size(caption, room, 34, 20)
            wide = K.font(size).getlength(caption)
            start = key_cx - (wide + (glyph + 14 if glyph else 0)) / 2
            if glyph:
                K.paste(block, lock, start + glyph / 2, y - 4)
                start += glyph + 14
            K.text(block, caption, start + wide / 2, y - 4, size, outline=3)
            floors.append(("key %d" % wave, size, 20))
        y += ROW_H + ROW_GAP

    close_cy = panel_top + panel_h - (30 + 55)
    K.paste(block, K.skin("btn_blue", 400, 110), bcx, close_cy)
    K.one_line(block, txt("ui.endless.checkpoint.close").upper(), bcx, close_cy - 4, 340, 36, 18, outline=3)

    if fit < 1.0:
        block = block.resize((int(bw * fit), int(bh * fit)), Image.LANCZOS)
    K.paste(sheet, block, W / 2, H / 2)

    bad = 0
    for what, got, floor in floors:
        flag = "  <- AT ITS FLOOR: the string is too long for its box" if got <= floor else ""
        bad += 1 if flag else 0
        print("  %-14s settled at %2d (floor %d)%s" % (what, got, floor, flag))
    print("  checkpoints: %d row(s) and the beginning, best %d, chosen %s, panel %.0f tall (+%.0f crest) "
          "fitted at %.2f on %dx%d" % (len(rows), best, picked[0] if picked else 1, panel_h,
                                       RC.CREST_REACH, fit, W, H))
    return sheet, bad


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--best", type=int, default=42, help="the lane's best (waves cleared)")
    ap.add_argument("--chosen", type=int, default=1, help="the wave the player chose; 1 is the beginning")
    ap.add_argument("--short", action="store_true", help="the squarest canvas this game lays out for")
    ap.add_argument("--contact", action="store_true", help="four states side by side: none open, some, chosen, all")
    ap.add_argument("--out", type=Path)
    args = ap.parse_args()

    txt = lambda key: RC.loc().get(key, key)
    canvas = SHORT if args.short else (1080, 1920)

    if args.contact:
        states = [(12, 1), (42, 1), (42, 22), (9999, 37)]
        sheets, bad = [], 0
        for best, chosen in states:
            s, b = render(txt, canvas, best, chosen)
            sheets.append(s)
            bad += b
        scale = .5
        tiles = [s.resize((int(s.width * scale), int(s.height * scale)), Image.LANCZOS) for s in sheets]
        out = Image.new("RGBA", (sum(t.width for t in tiles) + 12 * (len(tiles) - 1), tiles[0].height), (0, 0, 0, 255))
        x = 0
        for t in tiles:
            out.alpha_composite(t, (x, 0))
            x += t.width + 12
    else:
        out, bad = render(txt, canvas, args.best, args.chosen)

    path = args.out or (REPO / "out" / "checkpoints.png")
    path.parent.mkdir(parents=True, exist_ok=True)
    out.convert("RGB").save(path)
    print("wrote %s  %dx%d  - look at it" % (path, out.width, out.height))
    if bad:
        sys.exit("%d caption(s) at their floor" % bad)


if __name__ == "__main__":
    main()
