"""Cut the boards' top-three frames (`Ui/rank1..3`) as nine-slices that wrap a row's plate.

    python Tools/make_board_frames.py                  # cut from ~/Downloads/rank{1,2,3}.png
    python Tools/make_board_frames.py --source DIR     # cut from another folder
    python Tools/make_board_frames.py --check          # the committed PNGs and .meta borders
                                                       # are what a re-cut writes (passes with
                                                       # the sources absent)

**The frames are full borders with chamfered corners, drawn around `LeaderboardScreen.Row`'s
plate.** The plate is a fixed 940 x 160 and the art is ~3.2:1, so the frame is a nine-slice:
each corner (outer and inner chamfer together) kept whole and only the straight runs stretched,
so the corners are the shape the artist drew on every row (44a).

What the cut decides, so the screen does not have to:

* **Specks are dropped.** The sources carry stray painted flecks round the frame; anything that
  is not the band or its own glow (within `GLOW` source pixels of the band) goes.
* **One band thickness for all three.** Each is scaled so its top band is `BAND` sprite pixels,
  so first, second and third wear the same weight however their artists drew them.
* **The crop is the band's hard edge plus `MARGIN`** for the glow, so the screen places all
  three with one number (`Row.FrameReach`) whatever the source canvas was.
* **Twice the drawn density** (`PPU` 200 against the canvas's 100), under the Ui folder's
  1024 cap (ProjectSetup.Caps), so the facets stay sharp on a 1440-wide phone.
"""
import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

REPO = Path(__file__).resolve().parents[1]
OUT = REPO / "Assets" / "Game" / "Art" / "Ui"

BAND = 40          # sprite px of the top band: 20 canvas units at PPU 200
MARGIN = 12        # sprite px of glow kept outside the band's hard edge (6 units)
PPU = 200
CAP = 1024         # ProjectSetup.Caps for /Art/Ui/
GLOW = 40          # source px of soft glow kept round the band; anything further is a speck
HARD = 128         # alpha that counts as the band itself


def hard_mask(alpha):
    """The band alone: alpha at `HARD` or over, opened so flecks smaller than 9 px vanish."""
    m = Image.fromarray(((alpha >= HARD) * 255).astype(np.uint8))
    return m.filter(ImageFilter.MinFilter(9)).filter(ImageFilter.MaxFilter(9))


def cut(src):
    im = Image.open(src).convert("RGBA")
    rgba = np.array(im)
    band = hard_mask(rgba[..., 3])

    # Keep the glow within GLOW of the band, feathered, and nothing else.
    small = band.resize((band.width // 4, band.height // 4), Image.BILINEAR)
    near = small.filter(ImageFilter.MaxFilter(2 * (GLOW // 4) + 1)).filter(ImageFilter.GaussianBlur(2))
    near = np.array(near.resize(band.size, Image.BILINEAR)).astype(np.float32) / 255
    rgba[..., 3] = (rgba[..., 3] * near).round().astype(np.uint8)

    b = np.array(band) > 0
    ys, xs = np.where(b)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    top = int(np.argmax(~b[y0:, (x0 + x1) // 2]))          # top band thickness, centre column
    s = BAND / top

    pad = MARGIN / s
    box = (int(round(x0 - pad)), int(round(y0 - pad)), int(round(x1 + pad)), int(round(y1 + pad)))
    out = Image.fromarray(rgba).crop(box)
    w, h = int(round(out.width * s)), int(round(out.height * s))
    if w > CAP or h > CAP:
        sys.exit(f"{src.name}: {w}x{h} is over the Ui cap of {CAP}")
    out = out.resize((w, h), Image.LANCZOS)
    return out, border(out)


def border(im):
    """Where each corner ends: the first column (row) from each side past which the band's
    profile is the straight run's, plus two pixels so a stretch never takes a chamfer's tail."""
    a = np.array(im.getchannel("A")) >= HARD
    h, w = a.shape

    def profile(line):                                     # hard edges along one line
        return np.flatnonzero(np.diff(line.astype(np.int8)))

    def settle(lines, ref):
        n = len(lines)
        for i in range(n):
            if all(len(profile(l)) == len(ref) and np.all(np.abs(profile(l) - ref) <= 1)
                   for l in lines[i:i + 6]):
                return i
        return n // 2

    col_ref, row_ref = profile(a[:, w // 2]), profile(a[h // 2])
    left = settle([a[:, x] for x in range(w)], col_ref) + 2
    right = settle([a[:, x] for x in range(w - 1, -1, -1)], col_ref) + 2
    top = settle([a[y] for y in range(h)], row_ref) + 2
    bottom = settle([a[y] for y in range(h - 1, -1, -1)], row_ref) + 2
    return left, bottom, right, top                        # Unity's x, y, z, w


def meta_text(path, b):
    """The sprite's `.meta` with its border, density and full-rect mesh (a sliced sprite's)."""
    raw = path.read_bytes().decode("utf8")
    nl = "\r\n" if "\r\n" in raw else "\n"
    out = []
    for line in raw.split(nl):
        key = line.strip().split(":", 1)[0]
        ind = line[: len(line) - len(line.lstrip())]
        if key == "spriteBorder" and ind == "  ":
            line = ind + "spriteBorder: {x: %d, y: %d, z: %d, w: %d}" % b
        elif key == "spritePixelsToUnits" and ind == "  ":
            line = ind + "spritePixelsToUnits: %d" % PPU
        elif key == "spriteMeshType" and ind == "  ":
            line = ind + "spriteMeshType: 0"
        out.append(line)
    return nl.join(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", type=Path, default=Path.home() / "Downloads")
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    bad = 0
    for n in (1, 2, 3):
        src, png = args.source / f"rank{n}.png", OUT / f"rank{n}.png"
        meta = Path(str(png) + ".meta")
        if not src.exists():
            if args.check:
                print(f"rank{n}: source absent, committed art stands")
                continue
            sys.exit(f"missing {src}")
        im, b = cut(src)
        text = meta_text(meta, b)
        if args.check:
            same = (np.array_equal(np.array(Image.open(png).convert("RGBA")), np.array(im))
                    and meta.read_bytes().decode("utf8") == text)
            print(f"rank{n}: {'ok' if same else 'DIFFERS'}")
            bad += not same
            continue
        im.save(png, optimize=True)
        meta.write_bytes(text.encode("utf8"))
        print(f"rank{n}: {im.width}x{im.height} px, border {b}, "
              f"{im.width * 100 // PPU}x{im.height * 100 // PPU} units native")
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
