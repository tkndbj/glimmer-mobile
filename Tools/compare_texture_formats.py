#!/usr/bin/env python3
"""What Android texture compression actually does to this game's art.

Nothing in this project ever set a compression format, so every texture took Unity's
automatic default for Android -- ETC2 RGBA8 -- and the first store bundle came out at
about 220 MB of download per device against Play's 200 MB ceiling.  `DevBuild` pins ASTC
now.  That is smaller *and* better on colour, which is not the usual trade, and the one
thing it gives up is alpha, where ETC2's separate EAC block is strong.

Numbers cannot settle the alpha question -- 39 dB is either a clean edge or a visible
fringe depending on the art -- so this draws it.  `--contact` is the gate that matters;
`--measure` is the reproducible half.

    python Tools/compare_texture_formats.py --measure
    python Tools/compare_texture_formats.py --contact

Both need `astc-encoder-py`, `etcpak` and `texture2ddecoder`, and both are skipped by the
build: this is an instrument, not a check.  Neither writes anything into Assets/.
"""

import argparse
import glob
import math
import os
import random
import sys

try:
    import numpy as np
    from PIL import Image, ImageDraw
    import astc_encoder as A
    import etcpak
    import texture2ddecoder as T
except ImportError as e:  # pragma: no cover - an instrument, not a gate
    sys.stderr.write(
        "compare_texture_formats needs: pip install --user "
        "astc-encoder-py etcpak texture2ddecoder pillow numpy\n  (%s)\n" % e)
    sys.exit(2)

ART = "Assets/Game/Art"
OUT = "Tools/out"

# The folders that decide this, worst alpha first.  A portrait and a turret are the two
# with hard edges over a plate; the reels are soft and the backdrops are opaque.
GROUPS = [
    ("portraits", ART + "/Companions/*.png"),
    ("turrets",   ART + "/Siege/Wards/*/*.png"),
    ("ui",        ART + "/Ui/*.png"),
    ("reels",     ART + "/Fx/Siege/*/*.png"),
    ("backdrops", ART + "/Bg/*.png"),
]

# What the game draws these on: the kit's navy plate, and a light ground for the same
# reason `make_notification_icons.py` draws both -- a fringe hides on one and not the other.
DARK = (26, 31, 54)
LIGHT = (232, 226, 210)


def pad4(im):
    h, w = im.shape[:2]
    ph, pw = (-h) % 4, (-w) % 4
    if ph or pw:
        im = np.pad(im, ((0, ph), (0, pw), (0, 0)), mode="edge")
    return im, h, w


def etc2(im):
    """ETC2 RGBA8, 8 bpp -- what every texture in this build shipped as before ASTC."""
    p, h, w = pad4(im)
    ph, pw = p.shape[0], p.shape[1]
    comp = etcpak.compress_etc2_rgba(p.tobytes(), pw, ph)
    dec = T.decode_etc2a8(comp, pw, ph)
    out = np.frombuffer(dec, dtype=np.uint8).reshape(ph, pw, 4)[:, :, [2, 1, 0, 3]]
    return np.ascontiguousarray(out[:h, :w])


def astc(im, bw, bh):
    h, w = im.shape[:2]
    cfg = A.ASTCConfig(A.ASTCProfile.LDR, bw, bh, 1, A.ASTCQualityPreset.MEDIUM)
    ctx = A.ASTCContext(cfg)
    src = A.ASTCImage(A.ASTCType.U8, w, h, 1, data=im.tobytes())
    sw = A.ASTCSwizzle.from_str("RGBA")
    dst = A.ASTCImage(A.ASTCType.U8, w, h, 1)
    ctx.decompress(ctx.compress(src, sw), dst, sw)
    return np.frombuffer(dst.data, dtype=np.uint8).reshape(h, w, 4).copy()


def psnr(a, b):
    mse = np.mean((a.astype(np.float64) - b.astype(np.float64)) ** 2)
    return 99.0 if mse == 0 else 10 * math.log10(255.0 * 255.0 / mse)


def over(rgba, bg):
    """Composite onto a flat ground, which is the only way an alpha fault is visible."""
    a = rgba[:, :, 3:4].astype(np.float32) / 255.0
    rgb = rgba[:, :, :3].astype(np.float32)
    out = rgb * a + np.array(bg, dtype=np.float32) * (1.0 - a)
    return Image.fromarray(out.clip(0, 255).astype(np.uint8), "RGB")


def worst_edge(src, cand, box):
    """The crop where the two disagree most -- never a fixed corner, which is how a
    contact sheet comes to show the one patch that happens to be fine."""
    d = np.abs(src.astype(np.int16) - cand.astype(np.int16)).sum(axis=2)
    h, w = d.shape
    box = min(box, h, w)
    if h == box and w == box:
        return 0, 0, box
    best, by, bx = -1.0, 0, 0
    step = max(4, box // 4)
    for y in range(0, h - box + 1, step):
        for x in range(0, w - box + 1, step):
            s = float(d[y:y + box, x:x + box].sum())
            if s > best:
                best, by, bx = s, y, x
    return by, bx, box


def load(pattern, n, seed):
    files = sorted(glob.glob(pattern))
    if not files:
        return []
    random.Random(seed).shuffle(files)
    out = []
    for f in files:
        im = np.array(Image.open(f).convert("RGBA"))
        if im.shape[0] < 16 or im.shape[1] < 16:
            continue
        out.append((os.path.basename(os.path.dirname(f)) + "/" + os.path.basename(f), im))
        if len(out) >= n:
            break
    return out


def measure(args):
    print("  %-11s %-18s %5s  %8s %8s   %s"
          % ("folder", "format", "bpp", "PSNR rgb", "PSNR a", "size"))
    for name, pat in GROUPS:
        ims = [im for _, im in load(pat, args.sample, args.seed)]
        if not ims:
            continue
        e = [etc2(i) for i in ims]
        br = sum(psnr(i[:, :, :3], j[:, :, :3]) for i, j in zip(ims, e)) / len(ims)
        ba = sum(psnr(i[:, :, 3], j[:, :, 3]) for i, j in zip(ims, e)) / len(ims)
        print("  %-11s %-18s %5.2f  %8.2f %8.2f   100%%  <- before" % (name, "ETC2 RGBA8", 8.0, br, ba))
        for bw, bh, bpp in [(4, 4, 8.0), (5, 5, 5.12), (6, 6, 3.56)]:
            c = [astc(i, bw, bh) for i in ims]
            r = sum(psnr(i[:, :, :3], j[:, :, :3]) for i, j in zip(ims, c)) / len(ims)
            a = sum(psnr(i[:, :, 3], j[:, :, 3]) for i, j in zip(ims, c)) / len(ims)
            tag = "  <- shipped" if (bw, bh) == (6, 6) else ""
            print("  %-11s ASTC %dx%-13d %5.2f  %8.2f %8.2f   %3.0f%%%s"
                  % ("", bw, bh, bpp, r, a, bpp / 8.0 * 100, tag))
        print()


def contact(args):
    rows = []
    for name, pat in GROUPS:
        for label, im in load(pat, args.per_group, args.seed):
            rows.append((name, label, im))
    if not rows:
        sys.stderr.write("no art found under %s\n" % ART)
        return 1

    CELL, ZOOM, PAD, HDR, LBL = 168, 168, 14, 34, 128
    cols = ["source", "ETC2  (before)", "ASTC 6x6  (now)", "edge 6x  before", "edge 6x  now"]
    W = LBL + len(cols) * (CELL + PAD) + PAD
    H = HDR + len(rows) * (CELL + PAD) + PAD

    sheet = Image.new("RGB", (W, H), (18, 18, 22))
    d = ImageDraw.Draw(sheet)
    for i, c in enumerate(cols):
        d.text((LBL + i * (CELL + PAD) + 4, 12), c, fill=(225, 225, 235))

    for r, (group, label, src) in enumerate(rows):
        y = HDR + r * (CELL + PAD)
        d.text((6, y + CELL // 2 - 14), group, fill=(200, 200, 215))
        d.text((6, y + CELL // 2), label[:20], fill=(120, 120, 140))

        e, a6 = etc2(src), astc(src, 6, 6)

        def fit(rgba, bg):
            img = over(rgba, bg)
            img.thumbnail((CELL, CELL), Image.LANCZOS)
            return img

        for i, (img, bg) in enumerate([(src, DARK), (e, DARK), (a6, DARK)]):
            cell = fit(img, bg)
            sheet.paste(cell, (LBL + i * (CELL + PAD) + (CELL - cell.width) // 2,
                               y + (CELL - cell.height) // 2))

        # The edge crops go on the light ground: a dark fringe hides on navy.
        by, bx, box = worst_edge(src, a6, max(16, min(src.shape[0], src.shape[1]) // 6))
        for i, img in enumerate([e, a6]):
            crop = over(img[by:by + box, bx:bx + box], LIGHT).resize(
                (CELL, CELL), Image.NEAREST)
            sheet.paste(crop, (LBL + (3 + i) * (CELL + PAD), y))
            d.rectangle([LBL + (3 + i) * (CELL + PAD), y,
                         LBL + (3 + i) * (CELL + PAD) + CELL - 1, y + CELL - 1],
                        outline=(70, 70, 85))

    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, "texture_formats.png")
    sheet.save(p)
    print("wrote %s  (%d rows, %dx%d)" % (p, len(rows), W, H))
    print("Look at the two right-hand columns: that is a %dx crop of wherever the two "
          "formats disagree most, on a light ground." % 6)
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--contact", action="store_true", help="draw the comparison sheet")
    ap.add_argument("--measure", action="store_true", help="print PSNR per folder")
    ap.add_argument("--sample", type=int, default=14)
    ap.add_argument("--per-group", type=int, default=2)
    ap.add_argument("--seed", type=int, default=7)
    args = ap.parse_args()
    if not (args.contact or args.measure):
        ap.error("pass --contact or --measure")
    if args.measure:
        measure(args)
    return contact(args) if args.contact else 0


if __name__ == "__main__":
    sys.exit(main())
