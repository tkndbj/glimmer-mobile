# -*- coding: utf-8 -*-
"""Cuts the owner-supplied charm stones into Assets/Game/Art/Siege/.

    python Tools/make_charm_gems.py --source C:/Users/Digikey/Downloads/gems
    python Tools/make_charm_gems.py --check      # prove the shipped PNGs are what this cuts
    python Tools/make_charm_gems.py --contact    # one sheet, at the size a cell really draws

**Three of the six charms are drawn art now, and the other three are still cut from the gem
pack.** A lance, an hourglass and an anvil are supplied as twelve finished PNGs - three stones in
each of the four board colours - so nothing here hue-rotates anything: the colour is authored and
carrying it through untouched is the whole point of being handed the art. `make_siege_art.py`
still cuts the prism, the stormglass and the furnace out of the RPG gem pack, because nobody has
drawn those; `CHARM_GEMS` over there names only what it still cuts, so the two tools never write
the same file and both `--check` runs stay honest.

**Why a second tool rather than a branch in the first.** `make_siege_art.py` needs six licensed
packs to run at all and answers "nothing to do" without them; these twelve want to be re-cuttable
on a checkout that has none of them, and the source here is one folder of the owner's own artwork
rather than a pack. It is the arrangement `make_ad_art.py` already uses for the shop's supplied
pictures: the source lives outside the repo, the cut PNG is the committed artifact.

**The cut is two decisions and both are the pack-cut stones' own.** Trim to the drawing, then fit
the long edge to `SCALE` of a `TILE` box - the same room `make_siege_art.charm_gem` gives a
charmed stone (0.94 against a plain gem's 0.88), so these draw exactly as large as the stormglass
and the furnace standing beside them. No recolour and no grade: a supplied colour that needed
pulling onto the ward hue would be a fault to send back, not one to paint over.

**The trim is taken at alpha 8 rather than at anything non-zero**, for `make_ad_art.py`'s reason
and measured the same way: every hourglass export carries a faint speck out to its left that moves
the bounding box 120 pixels and would hang the stone off-centre in its cell. All twelve boxes are
stable from 8 up to 128.

**`--check` measures the colour as well as the size, because the one silent failure here is a
mis-filed stone.** Twelve files differing only by a word in the name is exactly the shape that
ships an amber lance addressed as the red one - and it would validate green everywhere, draw
perfectly, and simply pay the wrong ward in the player's hand. So the check reads each committed
PNG's own median hue back and holds it to `WARD_HUES`, which is the one place the four colours are
written down. The tolerance is well inside the closest pair (poppy and amber are 0.089 apart), so
any swap of two colours fails rather than rounds.
"""

import argparse
import colorsys
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Siege"

sys.path.insert(0, str(ROOT / "Tools"))

# The four colours and the size of a stone come from the tool that cuts every other gem on this
# board, never from a second copy here - a fifth hue or a different cell would be invisible until
# somebody looked at a screenshot.
from make_siege_art import TILE, WARD_HUES  # noqa: E402

#: How much of the cell a charmed stone fills. `make_siege_art.charm_gem`'s own figure, and it is
#: not a plain gem's 0.88: these shapes are pointed or round where the four gems are broad, so
#: fitted to the same box they draw visibly smaller than the stones beside them.
SCALE = 0.94

#: Below this the alpha is an export fringe rather than the drawing. See the header.
TRIM_ALPHA = 8

#: Which charms are supplied art. The rest of the roster is `make_siege_art.CHARM_GEMS`.
CHARMS = ("anvil", "hourglass", "lance")

#: What the supplied files call each board colour. The game's letters are permanent (`WARD_HUES`,
#: and every address is built out of one), so the mapping is written here rather than either side
#: being renamed to meet the other.
COLOURS = (("red", "r"), ("green", "g"), ("blue", "b"), ("orange", "y"))

#: How far a supplied stone's own hue may sit from the ward hue it is filed under. The four ward
#: hues' closest pair is poppy and amber at 0.089, so anything under 0.045 makes a swapped pair
#: unambiguous; 0.06 is the far side of that and still catches every mis-file, while leaving room
#: for the drawn blues, which measure about 0.04 cooler than azure.
HUE_SLACK = 0.06

#: A pixel has to be solid, coloured and lit to be evidence about the stone's hue - a stone's
#: highlights are white and its facets are nearly black, and neither says anything about which
#: colour it is.
HUE_MIN_ALPHA, HUE_MIN_SAT, HUE_MIN_VAL = 200, 0.35, 0.25


def name_of(charm, letter):
    return "gem_%s_%s.png" % (charm, letter)


def load(source, charm, colour):
    from PIL import Image

    path = pathlib.Path(source) / ("%s%s.png" % (charm, colour))
    if not path.exists():
        sys.exit("no artwork at %s" % path)

    return Image.open(path).convert("RGBA")


def cut(image):
    """Trim to the drawing, fit the long edge, centre it on a `TILE` square. Nothing else."""
    from PIL import Image

    alpha = image.split()[-1]
    box = alpha.point(lambda v: 255 if v >= TRIM_ALPHA else 0).getbbox()
    if box is None:
        sys.exit("the artwork is entirely transparent")

    art = image.crop(box)

    room = max(1, int(TILE * SCALE))
    ratio = min(room / float(art.width), room / float(art.height))
    art = art.resize((max(1, int(art.width * ratio)), max(1, int(art.height * ratio))),
                     Image.LANCZOS)

    out = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    out.alpha_composite(art, ((TILE - art.width) // 2, (TILE - art.height) // 2))
    return out


def median_hue(image):
    """The stone's own hue, off its solid lit pixels. None when there are too few to be evidence."""
    import numpy as np

    arr = np.asarray(image.convert("RGBA")).astype(np.float32)
    lit = arr[arr[..., 3] > HUE_MIN_ALPHA][..., :3] / 255.0

    hues = []
    for r, g, b in lit:
        h, s, v = colorsys.rgb_to_hsv(r, g, b)
        if s >= HUE_MIN_SAT and v >= HUE_MIN_VAL:
            hues.append(h)

    if len(hues) < 64:
        return None

    hues.sort()
    return hues[len(hues) // 2]


def hue_gap(a, b):
    """Distance round the wheel, so poppy at 0.99 and a red measuring 0.002 are next door."""
    d = abs(a - b) % 1.0
    return min(d, 1.0 - d)


def draw(source):
    made = {}
    for charm in CHARMS:
        for colour, letter in COLOURS:
            made[name_of(charm, letter)] = cut(load(source, charm, colour))
    return made


def check():
    """Hold the twelve committed PNGs to their size, their room and their colour.

    A check that compares this tool against itself cannot fail, and without the artwork that is
    all a re-cut could do - so what is asked here is what a checkout can answer on its own: is
    every stone present, is it a `TILE` square of RGBA, does its drawing fill the room a charmed
    stone gets, and is it the colour its own name says it is. The re-cut proof is `--source` plus
    a clean `git status`.
    """
    from PIL import Image

    room = max(1, int(TILE * SCALE))
    hues = dict(WARD_HUES)
    faults = []

    for charm in CHARMS:
        for _, letter in COLOURS:
            name = name_of(charm, letter)
            path = OUT / name
            if not path.exists():
                faults.append("%s: missing" % name)
                continue

            im = Image.open(path)
            if im.mode != "RGBA":
                faults.append("%s: %s, not RGBA" % (name, im.mode))
                continue
            if im.size != (TILE, TILE):
                faults.append("%s: %dx%d, not %d square" % (name, im.size[0], im.size[1], TILE))
                continue

            box = im.split()[-1].point(lambda v: 255 if v >= TRIM_ALPHA else 0).getbbox()
            if box is None:
                faults.append("%s: nothing drawn on it" % name)
                continue

            drawn = max(box[2] - box[0], box[3] - box[1])
            if abs(drawn - room) > 1:
                faults.append("%s: drawn %d across where a charmed stone fills %d - re-cut it"
                              % (name, drawn, room))

            hue = median_hue(im)
            if hue is None:
                faults.append("%s: too little solid colour to read a hue off" % name)
            elif hue_gap(hue, hues[letter]) > HUE_SLACK:
                faults.append("%s: reads hue %.3f where '%s' is %.3f - filed under the wrong "
                              "colour?" % (name, hue, letter, hues[letter]))

    if faults:
        for fault in faults:
            print(fault, file=sys.stderr)
        return 1

    print("charm gems: %d stones, %d square, drawn %d across, each the colour its name says"
          % (len(CHARMS) * len(COLOURS), TILE, room))
    return 0


def contact(made):
    """One sheet at the size a cell really draws, on the board's own dark ground.

    **The four plain gems and the two pack-cut charms are on it too**, and that is the whole
    point: the question a supplied stone has to answer is not "is it a nice drawing" but "does it
    stand beside the eight already on this board without looking like it came from somewhere
    else", which is a comparison and cannot be made one picture at a time.
    """
    from PIL import Image, ImageDraw

    cell, pad, label = 96, 14, 18
    rows = [(charm, [name_of(charm, letter) for _, letter in COLOURS]) for charm in CHARMS]
    rows.append(("on the board already",
                 ["gem_r.png", "gem_g.png", "gem_b.png", "gem_y.png",
                  "gem_prism.png", "gem_storm_r.png", "gem_furnace_r.png"]))

    cols = max(len(names) for _, names in rows)
    sheet = Image.new("RGBA",
                      (pad + cols * (cell + pad), pad + len(rows) * (cell + label + pad)),
                      (22, 28, 36, 255))
    pen = ImageDraw.Draw(sheet)

    for r, (title, names) in enumerate(rows):
        y = pad + r * (cell + label + pad)
        for c, name in enumerate(names):
            art = made.get(name)
            if art is None:
                path = OUT / name
                if not path.exists():
                    continue
                art = Image.open(path).convert("RGBA")

            thumb = art.copy()
            thumb.thumbnail((cell, cell), Image.LANCZOS)
            sheet.alpha_composite(thumb, (pad + c * (cell + pad) + (cell - thumb.width) // 2,
                                          y + (cell - thumb.height) // 2))

        pen.text((pad, y + cell + 3), title, fill=(228, 230, 224, 255))

    out = ROOT / "charm_gems.png"
    sheet.convert("RGB").save(out)
    print("wrote %s  %dx%d  - look at it" % (out, sheet.width, sheet.height))


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", help="folder holding anvilred.png, lanceblue.png and the rest")
    ap.add_argument("--check", action="store_true",
                    help="hold the committed PNGs to their size and their colour; change nothing")
    ap.add_argument("--contact", action="store_true", help="draw the sheet and change nothing")
    args = ap.parse_args()

    if args.check or not (args.source or args.contact):
        return check()

    made = draw(args.source) if args.source else {}

    if args.contact:
        contact(made)
        return 0

    for name in sorted(made):
        path = OUT / name
        made[name].save(path)
        print("  %s  %dx%d" % (path.name, made[name].width, made[name].height))

    print("charm gems: %d stone(s) written to %s" % (len(made), OUT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
