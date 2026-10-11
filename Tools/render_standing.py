"""Draws the map's two corner readings - the chapter standing under the back key (invariant 62)
and the star count under the "i" - for judging by eye, and measures the standing's sentences.

    python Tools/render_standing.py                      # English, finished: "top 12%"
    python Tools/render_standing.py --locked             # not finished: the "clear every level" sentence
    python Tools/render_standing.py --lang de            # any Latin-script table
    python Tools/render_standing.py --contact            # every Latin table, both sentences, one sheet
    python Tools/render_standing.py --measure            # every Latin table, both sentences; exit 1 on a tight fit

A mirror, not a gate for anything but `--measure`: every number here is `StandingReadout`'s and
`LevelsScreen`'s and the two are kept in step by hand (44d). The sentence is wrapped and shrunk
the way `UIKit.Shrinkable` shrinks it, and a size at its floor is the tell that a translation has
outgrown the plate (19n). Arabic is not drawn - the game shapes it through `ArabicText` at runtime
and this font has no Arabic forms - so `--measure` skips it and `ArabicTextTests` carries that table.
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

# LevelsScreen: the corner keys and the left column.
CORNER_SIZE, CORNER_X, CORNER_Y = 118.0, 96.0, 132.0
BOOST_GAP = 18.0
COLUMN_TOP = CORNER_Y + CORNER_SIZE * .5 + BOOST_GAP          # Unity y is up; the sheet counts down

# StandingReadout
S_W, S_H = 260.0, 112.0
PAD, ICON = 14.0, 54.0
TEXT_LEFT = PAD + ICON + 10.0
TEXT_W = S_W - TEXT_LEFT - PAD
TEXT_H = S_H - PAD
FONT, FLOOR = 26, 16
PLATE = (15, 31, 43)                                          # new Color(.06f, .12f, .17f, .72f)
PLATE_A = .72

# LevelsScreen: the star pill under the "i".
STARS_W, STARS_H, STARS_GAP = 196.0, 78.0, 22.0

LATIN = ["en", "es", "pt", "fr", "de", "it", "tr", "pl"]
SAMPLE_TOP = 12


def table(lang):
    rows = json.loads((LOC / ("%s.json" % lang)).read_text(encoding="utf-8"))["entries"]
    return {e["key"]: e["text"] for e in rows}


def sentence(lang, locked):
    s = table(lang)
    return s["ui.levels.standing_locked"] if locked else s["ui.levels.standing_top"].replace("{0}", str(SAMPLE_TOP))


def corner_key(sheet, cx, cy, icon):
    """`UIKit.IconButton` with `Skins.Nav`: a rimmed square and a glyph, close enough to judge the column."""
    K.paste(sheet, K.round_rect(CORNER_SIZE, CORNER_SIZE, 26, (12, 40, 86)), cx, cy)
    K.paste(sheet, K.round_rect(CORNER_SIZE, CORNER_SIZE, 26, (255, 255, 255), .18, width=3), cx, cy)
    im, _ = K.load(icon)
    K.paste(sheet, K.fit(im, (CORNER_SIZE * .5, CORNER_SIZE * .5)), cx, cy)


def standing(sheet, left, top, text):
    """`StandingReadout.Build`, at the seat `LevelsScreen.StandingX/Y` gives it. Returns the settled size."""
    K.paste(sheet, K.round_rect(S_W, S_H, 28, PLATE, PLATE_A), left + S_W / 2, top + S_H / 2)
    K.paste(sheet, K.round_rect(S_W, S_H, 28, (255, 255, 255), .13, width=3), left + S_W / 2, top + S_H / 2)
    trophy, _ = K.load("ic_trophy")
    K.paste(sheet, K.fit(trophy, (ICON, ICON)), left + PAD + ICON / 2, top + S_H / 2)
    return K.shrunk_left(sheet, text, left + TEXT_LEFT, top + PAD / 2, TEXT_W, TEXT_H, FONT, FLOOR)


def star_pill(sheet):
    """The other corner's reading, so the pair can be judged as a pair."""
    right = W - (CORNER_X - CORNER_SIZE * .5)
    cx = right - STARS_W / 2
    cy = CORNER_Y + CORNER_SIZE * .5 + STARS_GAP + STARS_H / 2
    K.paste(sheet, K.round_rect(STARS_W, STARS_H, 28, PLATE, PLATE_A), cx, cy)
    K.paste(sheet, K.round_rect(STARS_W, STARS_H, 28, (255, 255, 255), .13, width=3), cx, cy)
    star, _ = K.load("ic_star")
    K.paste(sheet, K.fit(star, (STARS_H * .52, STARS_H * .52)), cx - STARS_W / 2 + STARS_H * .42, cy)
    K.text(sheet, "17 / 30", cx + STARS_H * .3, cy, 36, outline=3, anchor="c")


def page(lang, locked):
    sheet = Image.new("RGBA", (W, H), (28, 64, 54, 255))     # a map's green, near enough
    corner_key(sheet, CORNER_X, CORNER_Y, "ic_left")
    corner_key(sheet, W - CORNER_X, CORNER_Y, "ic_info")
    star_pill(sheet)

    left = CORNER_X - CORNER_SIZE * .5
    size = standing(sheet, left, COLUMN_TOP, sentence(lang, locked))
    return sheet.convert("RGB"), size


def contact():
    cell_w, cell_h = S_W + 40, S_H + 40
    sheet = Image.new("RGB", (int(cell_w * 2), int(cell_h * len(LATIN))), (28, 64, 54))
    for row, lang in enumerate(LATIN):
        for col, locked in enumerate((False, True)):
            tile = Image.new("RGBA", (int(cell_w), int(cell_h)), (28, 64, 54, 255))
            size = standing(tile, 20, 20, sentence(lang, locked))
            K.text(tile, "%s %d" % (lang, size), cell_w - 30, 12, 16, outline=1, anchor="r")
            sheet.paste(tile.convert("RGB"), (int(col * cell_w), int(row * cell_h)))
    return sheet


def measure():
    tight = 0
    for lang in LATIN:
        line = "  %-2s" % lang
        for name, locked in (("top", False), ("locked", True)):
            probe = Image.new("RGBA", (int(S_W), int(S_H)), (0, 0, 0, 0))
            size = standing(probe, 0, 0, sentence(lang, locked))
            flag = "  TIGHT" if size <= FLOOR else ""
            tight += bool(flag)
            line += "  %-6s %d/%d%s" % (name, size, FLOOR, flag)
        print(line)
    print("  ar     not drawn here (shaped at runtime; see ArabicTextTests)")
    return tight


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--locked", action="store_true", help="the chapter not yet finished")
    ap.add_argument("--lang", default="en", choices=LATIN)
    ap.add_argument("--contact", action="store_true", help="every Latin table, both sentences")
    ap.add_argument("--measure", action="store_true", help="every Latin table, both sentences; exit 1 on a tight fit")
    ap.add_argument("--out", type=Path, default=Path("standing.png"))
    args = ap.parse_args()

    if args.measure:
        tight = measure()
        print("  %d caption(s) at their floor" % tight)
        sys.exit(1 if tight else 0)

    if args.contact:
        contact().save(args.out)
        print("wrote %s" % args.out)
        return

    out, size = page(args.lang, args.locked)
    out.save(args.out)
    print("wrote %s  (sentence at %d, floor %d)" % (args.out, size, FLOOR))


if __name__ == "__main__":
    main()
