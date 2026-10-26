# -*- coding: utf-8 -*-
"""Draws the opening tutorial at the size a phone draws it, with the real sprites.

    python Tools/render_tutorial.py --opening      # the board as it is met, before the wave
    python Tools/render_tutorial.py --charged      # a tube armed and a wave walking
    python Tools/render_tutorial.py --finale       # the finale both endings raise (invariant 53h)
    python Tools/render_tutorial.py --contact      # all three side by side
    python Tools/render_tutorial.py --captions     # measure every caption, and error on a spill
    python Tools/render_tutorial.py --out out/tutorial.png

**Why this exists.** `TutorialTests` plays the whole script against the real rules and proves
it terminates with the line intact; it says nothing whatever about whether the screen *reads*.
That is three questions no fixture can be asked. Does the title clear the board it sits over.
Does a worded SKIP key in the corner look like the way out rather than like a control in the
game. And does the last panel read as a finish rather than as an interruption.

**The board underneath is drawn by `render_siege`, which is the point rather than a shortcut.**
The tutorial is that mode, dealt from `SiegeTutorial` instead of from a chapter, so a second
drawing of it here would be a second opinion about a picture this repo already has — and one
that would go stale the first time the hill changed. This imports that file, hands it the
tutorial's own field read straight out of `SiegeTutorial.cs`, and paints only what the
tutorial adds.

**It draws the ground, and for a while it did not — which is exactly the lie invariant 44d
warns about.** `render_siege` fills its own sheet with a flat slate, because on a rung the
backdrop is the chapter's and that file does not know it; this mirror inherited that and so
showed a dark surround on a screen that was actually painting a *salmon* sky behind the board.
Nobody could see the real ground until somebody built it. The wall is drawn as its own flat
colour rather than as its pattern, which is the one simplification left here: `Scenery.Plain`
adds no shade, no vignette and no parallax, so hue and value are exact and what this cannot
answer is whether the wall's confetti reads from behind the board.

**What it deliberately does not draw**, because a mirror that shows a piece the screen does not
is worse than no mirror at all (invariant 44d): the coaching hand and the rings it travels
between. Those are `CoachHand`, placed at run time off the real widgets' own rectangles, and
they are already pinned by `CoachStrokeTests`. Nothing here can say whether a hand over a gem
reads, and nothing here pretends to.

**It is a mirror, and mirrors drift.** Every constant below is named after the field it copies
(`TutorialScreen.HostInset`, `.BannerH`, `Scenery.TitleRibbon`, `UIKit.PillFaceLift`), the
field itself is read out of the C# rather than retyped, and when a device disagrees with this
picture the picture is the one that is wrong.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402
import render_siege as S                                    # noqa: E402

REPO = K.REPO
LOC = REPO / "Assets" / "StreamingAssets" / "Content" / "loc" / "en.json"
SOURCE = REPO / "Assets" / "Game" / "Scripts" / "Domain" / "Content" / "Modes" / "SiegeTutorial.cs"

W, H = K.W, K.H

# ------------------------------------------------------------------ TutorialScreen
#: `TutorialScreen.HostInset` — (left, bottom, right, top), `ModeScreen`'s own order.
HOST_INSET = (24.0, 96.0, 24.0, 214.0)

#: `TutorialScreen.ChromeSize` / `.BannerH`.
CHROME, BANNER_H = 92.0, 112.0

#: `Scenery.TitleRibbon`: the cloth is `TutorialScreen.BannerW` wide, tilted by -1.6 degrees,
#: and its caption is fitted to 74% of that on ONE line with a floor of 24
#: (`UIKit.OneLineLabel`). Smaller than the page standard on purpose - see `.BannerH`.
RIBBON_W, RIBBON_TILT, TITLE_SIZE, TITLE_FLOOR = 620.0, -1.6, 34, 24
RIBBON_ROOM = 0.74

#: The skip key: `UIKit.TextButton` on `Skins.Shut`, anchored to the top right corner.
SKIP_W, SKIP_SIZE, SKIP_MARGIN = 168.0, 26, 96.0

#: `UIKit.PillFaceLift` — a kit button's caption sits on its painted face rather than in the
#: middle of its rect. Unity's y counts up and an image's counts down, so it is *subtracted*
#: here, which is the axis flip a mirror was once caught getting wrong (invariant 44d).
FACE_LIFT = 0.0231

#: `TutorialScreen.Curtain` — the scrim over the board.
SCRIM = 0.66

#: `FinaleStack` — the wordmark, the line and the key, hung from the top of the safe area and
#: ending `FLOOR` above its middle, where the ward line stands. Scaled as one on a short canvas.
STACK_TOP, FLOOR = 64.0, 170.0
LOGO_W = 900.0
LINE_W, LINE_H = 880.0, 156.0
GO_W, GO_H = 460.0, 122.0
LOGO_GAP, KEY_GAP = 18.0, 40.0

#: `TutorialScreen.Curtain` — the line's size and floor, and the key's caption.
LINE_SIZE, LINE_FLOOR, GO_SIZE = 66, 34, 34

#: `TutorialScreen.Crown` — the light behind the wordmark at rest: the halo's box and alpha, and
#: the fan of rays' size and alpha (`Art.Rays(256, 14)` in `Pal.Sun`).
HALO_BOX, HALO_ALPHA = (1240.0, 620.0), 0.36
RAYS_SIZE, RAYS_COUNT, RAYS_ALPHA = 1120, 14, 0.16

LOGO = REPO / "Assets" / "Game" / "Art" / "Bg" / "gemfire_logo.png"


def strings():
    table = json.loads(LOC.read_text(encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


# ------------------------------------------------------------------ the field
def field():
    """The tutorial's board, read out of `SiegeTutorial.cs` rather than retyped here.

    **Read rather than mirrored, which is the one thing this file does differently from every
    other render in the repo.** Those mirror a *layout* — numbers that describe where a widget
    goes — and a number retyped is a number somebody can find. This is a board: eight letters a
    row, five rows, one of which was deliberately changed to plant the move the hand points at.
    A retyped copy of that is one keystroke from drawing a picture of a field the game does not
    deal, and no eye would catch it.
    """
    text = SOURCE.read_text(encoding="utf-8")

    def quoted(name):
        m = re.search(r'public const string %s\s*=\s*"([^"]*)"\s*;' % name, text)
        if not m:
            sys.exit("SiegeTutorial.%s is not where this render expects it" % name)
        return m.group(1)

    rows = re.search(r'public static readonly string\[\] Rows\s*=\s*\{(.*?)\};', text, re.S)
    if not rows:
        sys.exit("SiegeTutorial.Rows is not where this render expects it")

    grid = re.findall(r'"([a-z]+)"', rows.group(1))
    wide, tall = len(grid[0]) if grid else 0, len(grid)

    # **The declared size is checked against the rows rather than used instead of them.** The
    # board's own reader would refuse a mismatch outright (`ProtoGrid.TryRead`), so a picture
    # drawn from the declaration would be a picture of a board the game refuses to deal.
    said = re.search(r'public const int Width\s*=\s*(\d+)\s*,\s*Height\s*=\s*(\d+)\s*;', text)
    if not said:
        sys.exit("SiegeTutorial.Width/Height are not where this render expects them")

    if (int(said.group(1)), int(said.group(2))) != (wide, tall):
        sys.exit("SiegeTutorial says %sx%s and carries %dx%d rows"
                 % (said.group(1), said.group(2), wide, tall))

    return {
        "id": "tutorial",
        "siege": {
            "width": wide,
            "height": tall,
            "rows": grid,
            "gems": quoted("Deal"),
            "wards": quoted("Line"),
            "waves": [quoted("Wave")],
            "boss": "",
        },
    }


def inset():
    """`TutorialScreen.HostInset`, measured from the display the way `render_siege.inset` is.

    The screen's own numbers are measured inside the safe layer and this file has no safe
    layer, so the bottom carries `SAFE_BOTTOM` on top of it — which is the same correction
    `render_siege.inset` makes for the action bar, and it is written as a sum for that
    function's reason: on a display with nothing in the way the two are equal, which is exactly
    what makes a missing one invisible.
    """
    left, bottom, right, top = HOST_INSET
    return (left, bottom + S.SAFE_BOTTOM, right, top)


def wall():
    """`Scenery.Plain`'s own colour, read off the sprite rather than typed.

    Sampled instead of copied because it is a painting's colour and not a constant anybody
    wrote down: `Skins.Sky` is a near neighbour and is *not* what this screen stands on, so a
    typed value would be a picture of a blue nobody chose. See the note at the top about what
    is simplified here.
    """
    im = Image.open(K.REPO / "Assets" / "Game" / "Art" / "Bg" / "plain.png").convert("RGB")
    return im.resize((1, 1), Image.LANCZOS).getpixel((0, 0))


def board(raiders, armed=()):
    """The tutorial's siege, on the tutorial's ground and at the tutorial's own inset.

    Both are monkeypatched around one call rather than forked: `render_siege` is the mirror of
    this board and every line of it should go on being the one that ships.
    """
    kept_inset, kept_back = S.inset, S.BACK
    S.inset = inset
    S.BACK = wall()
    try:
        return S.draw(field(), raiders, bolts=bool(raiders), rung=0, armed=armed)
    finally:
        S.inset, S.BACK = kept_inset, kept_back


# ------------------------------------------------------------------ the chrome
def chrome(sheet, txt, report=None, retired=False):
    """The title and the way out. `TutorialScreen.BuildChrome`.

    `retired` is `TutorialScreen.Retire`: once the finale is up there is nothing left to skip,
    so the key is faded away and the corner is empty - and the title with it, because the
    wordmark stands where it stood. Drawn as absent rather than as dim, because that is what a
    group faded to nought is."""
    cy = 22.0 + BANNER_H / 2.0

    if retired:
        return

    ribbon = K.skin("ribbon_orange", RIBBON_W, BANNER_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)

    room = RIBBON_W * RIBBON_ROOM
    size = K.one_line(plate, txt("ui.tutorial.title").upper(), plate.width / 2,
                      plate.height / 2, room, TITLE_SIZE, TITLE_FLOOR,
                      what="tutorial title" if report is not None else None, outline=4)

    if report is not None:
        report.append(("title", txt("ui.tutorial.title").upper(), size, TITLE_SIZE,
                       K.font(size).getlength(txt("ui.tutorial.title").upper()), room))

    K.paste(sheet, plate.rotate(RIBBON_TILT, Image.BICUBIC, expand=True), W / 2, cy)

    if not retired:
        pill(sheet, "btn_gray", txt("ui.tutorial.skip").upper(), SKIP_SIZE,
             W - SKIP_MARGIN, cy, SKIP_W, CHROME, report, "skip")


def pill(sheet, skin, caption, size, cx, cy, wide, tall, report=None, what=None):
    """`UIKit.TextButton` — the kit's pill, with its caption on the painted face."""
    K.paste(sheet, K.skin(skin, wide, tall), cx, cy)

    room = wide - 40.0
    drawn = K.one_line(sheet, caption, cx, cy - tall * FACE_LIFT, room, size,
                       max(1, size // 2), what=what if report is not None else None)

    if report is not None and what:
        report.append((what, caption, drawn, size, K.font(drawn).getlength(caption), room))


# ------------------------------------------------------------------ the finale
def stack(safe_h, aspect):
    """`FinaleStack.For`, line for line: (logo height, stack height, scale, stack top)."""
    logo_h = LOGO_W / aspect
    height = logo_h + LOGO_GAP + LINE_H + KEY_GAP + GO_H
    room = safe_h * 0.5 - FLOOR - STACK_TOP
    scale = 0.0 if room <= 0 else min(1.0, room / height)
    spare = max(0.0, room - height * scale)
    return logo_h, height, scale, STACK_TOP + spare * 0.5


def curtain(sheet, txt, report=None):
    """`TutorialScreen.Curtain` and `.Crown` at rest: the scrim, the wordmark in its light, the
    line, and the one key - every centre out of `stack`, every size multiplied by its scale."""
    wash = Image.new("RGBA", (W, H), (10, 18, 26, int(255 * SCRIM)))
    sheet.alpha_composite(wash)

    mark = Image.open(LOGO).convert("RGBA")
    logo_h, height, k, top = stack(H, mark.width / mark.height)

    def y(offset):
        return top + offset * k

    logo_y = y(logo_h / 2)
    line_y = y(logo_h + LOGO_GAP + LINE_H / 2)
    key_y = y(logo_h + LOGO_GAP + LINE_H + KEY_GAP + GO_H / 2)

    halo = K.glow(int(HALO_BOX[0] * k), 2.1, K.GOLD, HALO_ALPHA)
    K.paste(sheet, halo.resize((int(HALO_BOX[0] * k), int(HALO_BOX[1] * k)), Image.LANCZOS),
            W / 2, logo_y)

    fan = K.rays(int(RAYS_SIZE * k), RAYS_COUNT)
    light = Image.new("RGBA", fan.size, (*K.SUN, 0))
    light.putalpha(fan.point(lambda v: int(v * RAYS_ALPHA)))
    K.paste(sheet, light, W / 2, logo_y)

    K.paste(sheet, K.fit(mark, (LOGO_W * k, logo_h * k)), W / 2, logo_y)

    size = K.shrunk(sheet, txt("ui.tutorial.win").upper(), W / 2, line_y,
                    LINE_W * k, LINE_H * k, int(LINE_SIZE * k), int(LINE_FLOOR * k), outline=3)

    if report is not None:
        report.append(("win line", txt("ui.tutorial.win").upper(), size, int(LINE_SIZE * k),
                       K.font(size).getlength(txt("ui.tutorial.win").upper()), LINE_W * k))

    pill(sheet, "btn_green", txt("ui.tutorial.go").upper(), int(GO_SIZE * k),
         W / 2, key_y, GO_W * k, GO_H * k, report, "continue")

    if report is not None:
        report.append(("stack", "bottom %.0f of %.0f, scale %.2f" % (y(height), H / 2 - FLOOR, k),
                       0, 0, y(height), H / 2 - FLOOR))


# ------------------------------------------------------------------ the pictures
def opening(txt, report=None):
    """The board as it is met: nothing on the hill yet, and the first panel about to go up."""
    sheet = board(0)
    chrome(sheet, txt, report)
    return sheet


def charged(txt):
    """A tube armed over a wave that has walked on - the frame the second panel rings.

    Three bodies, because that is what the board really has on it by then: the script waits for
    `SiegeTutorial.Crowd` before raising the panel, and the tube takes about three matches to
    fill, by which time a third raider has walked on (measured, not guessed)."""
    sheet = board(3, armed=(1,))
    chrome(sheet, txt)
    return sheet


def finale(txt, report=None):
    """The hill cleared: the wordmark, the line, and the one key on (invariant 53h)."""
    sheet = board(0)
    chrome(sheet, txt, retired=True)
    curtain(sheet, txt, report)
    return sheet


def measure(txt):
    """Every caption the tutorial can say, against the box it is drawn in.

    The one gate here that can fail. A tutorial's strings are the first English a player ever
    reads in this game and a translation of them is not this project's to choose the length of,
    so a caption that lands on its floor — or past it — is a caption somebody has to shorten.
    """
    report = []

    opening(txt, report)
    finale(txt, report)

    print("%-10s %-26s %5s %5s %8s %8s" % ("what", "caption", "at", "asked", "drawn", "room"))
    spilled = []

    for what, caption, at, asked, drawn, room in report:
        mark = "  " if drawn <= room + 0.5 else "<-"
        if drawn > room + 0.5:
            spilled.append((what, caption))

        print("%s %-8s %-26s %5d %5d %8.1f %8.1f%s"
              % (mark, what, caption[:26], at, asked, drawn, room,
                 "   TIGHT" if at < asked else ""))

    if spilled:
        print("\n%d caption(s) drawn outside the box:" % len(spilled))
        for what, caption in spilled:
            print("  %s: %s" % (what, caption))
        return False

    print("\nevery caption fits.")
    return True


def contact(txt):
    shots = [("opening", opening(txt)), ("charged", charged(txt)), ("finale", finale(txt))]

    pad = 24
    sheet = Image.new("RGBA", (len(shots) * (W + pad) + pad, H + pad * 2), (24, 24, 30, 255))
    for i, (_, shot) in enumerate(shots):
        sheet.alpha_composite(shot, (pad + i * (W + pad), pad))

    return sheet, ", ".join(name for name, _ in shots)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--opening", action="store_true")
    ap.add_argument("--charged", action="store_true")
    ap.add_argument("--finale", action="store_true")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--captions", action="store_true",
                    help="measure every caption against its box and error on a spill")
    ap.add_argument("--out", default="")
    args = ap.parse_args()

    txt_table = strings()

    def txt(key):
        return txt_table.get(key, key)

    if args.captions:
        sys.exit(0 if measure(txt) else 1)

    if args.contact or not (args.opening or args.charged or args.finale):
        sheet, named = contact(txt)
        out = Path(args.out or "out/tutorial_contact.png")
    elif args.charged:
        sheet, named = charged(txt), "charged"
        out = Path(args.out or "out/tutorial_charged.png")
    elif args.finale:
        sheet, named = finale(txt), "finale"
        out = Path(args.out or "out/tutorial_finale.png")
    else:
        sheet, named = opening(txt), "opening"
        out = Path(args.out or "out/tutorial_opening.png")

    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print("wrote %s (%s)" % (out, named))


if __name__ == "__main__":
    main()
