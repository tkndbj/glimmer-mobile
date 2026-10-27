#!/usr/bin/env python3
"""Cut the free bonus's medal from the owner's artwork (invariant 67).

One sprite into `Assets/Game/Art/Ui/`:

    ic_free_bonus   the red starburst medal the hub's FREE BONUS badge wears (the owner, 2026-10-27)

    python Tools/make_free_bonus_art.py            # cut it, with its .meta
    python Tools/make_free_bonus_art.py --check    # prove the shipped PNG is what this cuts
    python Tools/make_free_bonus_art.py --measure  # print the disc the caption is laid in

The source is committed (`Tools/IconSource/free_bonus_medal.png`), so the cut is reproducible on any
machine. It is a generated picture and needs three repairs, each measured on the source:

* **1,163 specks.** Faint, disconnected pixels around the medal (alpha up to ~60). Only the largest
  connected shape is kept; on a dark hub they would be grit around the badge.
* **A body at alpha 252-253, not 255.** Every pixel at or above `SOLID` is made opaque, so the medal
  does not let the room show faintly through it. Anti-aliased edges, below it, are untouched.
* **Padding.** Trimmed to the shape, so `HomeScreen` can size the medal by its drawn edge.

`--measure` prints the medal's aspect and the inner red disc as fractions of the cut, the numbers
`FreeBonusBadge` lays the caption against; the C# copies are held to it by `FreeBonusTests`.
"""

import argparse
import hashlib
import io
import sys
from collections import deque
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Tools" / "IconSource" / "free_bonus_medal.png"
OUT = ROOT / "Assets" / "Game" / "Art" / "Ui"
TEMPLATE = OUT / "ic_update.png.meta"
NAME = "ic_free_bonus"

#: Long edge in pixels: drawn about 190 units tall on a 1080 canvas, so 2x on the densest phones.
EDGE = 384

#: Alpha at or above which a pixel is body, not edge.
SOLID = 240


def cut():
    import numpy as np
    from PIL import Image

    image = Image.open(SOURCE).convert("RGBA")
    px = np.array(image)
    alpha = px[..., 3]

    # The largest 4-connected shape of visible pixels: the medal. Everything else is a speck.
    seen = np.zeros(alpha.shape, dtype=bool)
    visible = alpha > 3
    best = None
    h, w = alpha.shape
    for y0, x0 in zip(*np.nonzero(visible)):
        if seen[y0, x0]:
            continue
        seen[y0, x0] = True
        queue, pixels = deque([(y0, x0)]), []
        while queue:
            y, x = queue.popleft()
            pixels.append((y, x))
            for yy, xx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= yy < h and 0 <= xx < w and visible[yy, xx] and not seen[yy, xx]:
                    seen[yy, xx] = True
                    queue.append((yy, xx))
        if best is None or len(pixels) > len(best):
            best = pixels

    keep = np.zeros(alpha.shape, dtype=bool)
    ys, xs = zip(*best)
    keep[list(ys), list(xs)] = True

    out = px.copy()
    out[..., 3] = np.where(keep, np.where(alpha >= SOLID, 255, alpha), 0)
    out[~keep] = 0

    medal = Image.fromarray(out, "RGBA")
    medal = medal.crop(medal.getbbox())
    k = EDGE / max(medal.size)
    return medal.resize((max(1, round(medal.width * k)), max(1, round(medal.height * k))), Image.LANCZOS)


def measure(medal):
    """The cut's aspect, and the inner red disc (centre and diameter) as fractions of its width/height."""
    import numpy as np

    px = np.array(medal).astype(int)
    r, g, b, a = px[..., 0], px[..., 1], px[..., 2], px[..., 3]
    gold = (a > 200) & (r > 200) & (g > 150) & (b < 120)

    # Walk out from the middle along the centre column and row until the gold ring is met.
    h, w = gold.shape
    cy = int(h * 0.42)
    cx = w // 2
    row = np.nonzero(gold[cy])[0]
    left = row[row < cx].max()
    right = row[row > cx].min()
    mid_x = (left + right) // 2
    col = np.nonzero(gold[:, mid_x])[0]
    top = col[col < cy].max()
    bottom = col[col > cy].min()
    return {
        "aspect": w / h,
        "disc_x": (left + right) / 2 / w,
        "disc_y": (top + bottom) / 2 / h,
        "disc_w": (right - left) / w,
        "disc_h": (bottom - top) / h,
    }


def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def meta():
    guid = hashlib.md5(("glimmer.ui.freebonus." + NAME).encode("utf8")).hexdigest()
    out = []
    for line in TEMPLATE.read_text(encoding="utf8").splitlines(keepends=True):
        out.append(f"guid: {guid}\n" if line.startswith("guid: ") else line)
    return "".join(out)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="prove the shipped PNG is what this tool cuts")
    parser.add_argument("--measure", action="store_true", help="print the medal's aspect and caption disc")
    args = parser.parse_args()

    medal = cut()
    png = OUT / f"{NAME}.png"

    if args.measure:
        for key, value in measure(medal).items():
            print(f"  {key:7s} {value:.4f}")
        return 0

    if args.check:
        bad = []
        if not png.exists():
            bad.append(f"{NAME}.png is missing")
        elif png.read_bytes() != png_bytes(medal):
            bad.append(f"{NAME}.png is not what this tool cuts")
        if not (OUT / f"{NAME}.png.meta").exists():
            bad.append(f"{NAME}.png.meta is missing")
        if bad:
            for line in bad:
                print(line, file=sys.stderr)
            print("re-run without --check", file=sys.stderr)
            return 1
        print("free bonus art: 1 piece, reproducible")
        return 0

    png.write_bytes(png_bytes(medal))
    side = OUT / f"{NAME}.png.meta"
    if not side.exists():
        side.write_text(meta(), encoding="utf8", newline="\n")
    print(f"  {NAME}: {medal.width}x{medal.height}")
    print(f"free bonus art: wrote {png}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
