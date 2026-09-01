# -*- coding: utf-8 -*-
"""Cuts the board backdrops every chapter of every mode draws: one cloud sky per colour.

    python Tools/make_sky_art.py --source "C:/path/to/_extracted"
    python Tools/make_sky_art.py --source "C:/path/to/_extracted" --check

Writes `Assets/Game/Art/Bg/sky_00.png` .. `sky_39.png`.

**One family for the whole game, and that is the point.** Every board backdrop used to be
cut per chapter, from that chapter's own source pack and graded to that level's own accent
- so the game held forty-one backdrops out of six different paintings, a glade chapter got
ten places and a Lightfall chapter got *one* picture for all ten of its wells. Two
consequences, both reported: the modes did not read as one game, and adding a chapter meant
choosing a source pack, adding a row to `chapter_art.tsv` and cutting ten more textures.

So the backdrop is now a **rule** instead of a decision. A level draws `sky_NN` chosen by
where it sits (`Tools/chapters/mapart.py` owns that arithmetic, and `make_chapter_art.py`
no longer cuts backdrops at all). Every one of them is the same soft cloud sky at a
different colour, which is what makes forty screens look like forty rooms of one house
rather than six houses - and a chapter published next year costs **no backdrop art at
all**.

Three decisions worth not re-litigating.

**The source is the pack's `layers/` sky art, never a `_preview` sheet.** The preview sheets
in these packs carry the vendor's own dummy lettering, which survives a grade as two legible
smudges and is invisible to every gate in the repo - see `art-source-packs`. These two layer
files are clean cloud paintings and are what `c01_shallows` was cut from, which is the set
the owner asked for more of.

**The colour is a hue, and the hues are dealt with a stride.** Forty evenly spaced hues put
`sky_07` and `sky_08` nine degrees apart, which is two levels in a row that look identical;
dealing them `STRIDE` apart around the wheel means consecutive levels are always most of a
turn from each other while the set as a whole still covers the wheel exactly once. Nothing
downstream knows about this - a level names a file.

**The grading is `make_chapter_art.vivid`, imported rather than copied.** It is the grade
that took four attempts to get right (the duotone, the tint, the per-channel lift, the
saturation floor - all four failures are written up in `CRAFT.md`), and a second copy of it
here would be a second thing to get wrong.
"""
import argparse, colorsys, io, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)

import make_chapter_art as chapter_art                              # noqa: E402

BG_ART = os.path.join(ROOT, "Assets", "Game", "Art", "Bg")

#: How many skies exist. Ten per chapter, four chapters deep, which is as far as any mode
#: has reached - a fifth chapter wraps round to the first block rather than needing art
#: (`mapart.skies`). Raising this is additive and costs nothing already shipped; lowering
#: it would strand a level on a file that no longer exists, so it only ever goes up.
COUNT = 40

#: Saturation and value of the colour each sky is turned onto. `vivid` rotates the
#: painting's own dominant hue onto this one and scales saturation to its own target, so
#: only the hue of this colour is really read - the other two are here to make the ladder
#: printable as real colours.
TARGET_S, TARGET_V = .72, 1.0

#: How far round the wheel consecutive skies are dealt, in slots. Coprime with `COUNT`, so
#: the forty slots are still visited exactly once each. See the module docstring.
STRIDE = 13

#: The two clean cloud paintings, alternated so neighbouring levels differ in their cloud
#: shapes as well as in colour. A `+`-joined stack is legal here exactly as it is in
#: `chapter_art.tsv`; neither of these needs one.
SOURCES = (
    "craftpix-135791-level-map-2d-game-backgrounds/_PNG/01/layers/l1-sky.png",
    "craftpix-135791-level-map-2d-game-backgrounds/_PNG/02/layers/l1-sky.png",
)

#: Passed to `vivid` and unread by it - the board's plate and its light are the level's
#: own `slate`, which no backdrop has any business carrying.
SLATE = (18, 34, 46)


def name(index):
    return "sky_%02d" % index


def colour(index):
    """The colour sky `index` is turned onto, as an (r, g, b) triple."""
    hue = ((index * STRIDE) % COUNT) / float(COUNT)
    return tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb(hue, TARGET_S, TARGET_V))


def cut(root, index):
    """One graded sky."""
    source = SOURCES[index % len(SOURCES)]

    # Windows slide across each source rather than being taken in a row, so the two
    # paintings hand out forty crops instead of two crops forty times.
    per = COUNT // len(SOURCES)
    window = (index // len(SOURCES)) / float(per - 1)

    return chapter_art.backdrop(root, source, window, SLATE, colour(index))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", required=True, help="folder the art packs were extracted into")
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped skies are what this tool would cut, and write nothing")
    ap.add_argument("--only", type=int, help="cut one sky, for judging a change to the ladder")
    ap.add_argument("--out", help="write somewhere other than the project")
    args = ap.parse_args()

    out_dir = args.out or BG_ART
    wanted = [args.only] if args.only is not None else list(range(COUNT))

    if not args.check:
        os.makedirs(out_dir, exist_ok=True)

    bad = 0
    for index in wanted:
        image = cut(args.source, index)
        path = os.path.join(out_dir, name(index) + ".png")
        r, g, b = colour(index)

        if args.check:
            if not os.path.exists(path):
                print(f"  {name(index)}  MISSING")
                bad += 1
                continue
            buffer = io.BytesIO()
            image.save(buffer, format="PNG")
            with open(path, "rb") as f:
                same = f.read() == buffer.getvalue()
            print(f"  {name(index)}  #{r:02X}{g:02X}{b:02X}  {'ok' if same else 'DIFFERS'}")
            if not same:
                bad += 1
            continue

        print(f"  {name(index)}  #{r:02X}{g:02X}{b:02X}  {image.size[0]}x{image.size[1]}")
        image.save(path)

    if args.check:
        if bad:
            sys.exit(f"{bad} of {len(wanted)} sky/skies are not what this tool cuts")
        print(f"all {len(wanted)} skies are what this tool cuts")
        return

    print("\nNext: Glimmer Grove > Addressables > Sync All Assets, then > Validate Art.")
    print("The importer hook only addresses art that arrives while the Editor is running.")


if __name__ == "__main__":
    main()
