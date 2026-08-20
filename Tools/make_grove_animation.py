# -*- coding: utf-8 -*-
"""Turns still grove props into short flipbooks, and points the catalog at them.

    python Tools/make_grove_animation.py [--only torch,candle] [--dry-run]

**Why this exists.** None of the seventeen isometric packs ships a single animated asset —
every prop in the grove is one still PNG. So a torch that never flickers is not an oversight
in the content, it is the only thing the content could be, and the fix is to generate the
motion. This is the second tool of that kind; `make_waterfall.py` composes one piece out of
two, and this one takes a piece that is already right and makes part of it move.

**The rule every recipe here obeys: never draw outside what was already drawn.** Two of the
torches sit inside a glass globe, so a flame allowed to swell past its own silhouette is
drawn *over* the glass instead of behind it and the prop stops being a lantern. Every recipe
therefore either clips its work to a mask taken from the source, or paints into a region that
was flat colour to begin with (the well's shaft), or erases a region that stood in free space
before redrawing it (a banner's streamers). Nothing is inpainted and nothing is guessed.

**And no soft glows.** The pack is flat vector — there is not one gradient in it — so a
blurred halo reads as a smudge rather than as light, and it clips at the sprite's edge into a
visible box. What carries the effect instead is a **brightness swing**, which is both in the
idiom and the cue that survives being shrunk to a 170-point shop cell. That mattered here
because these animate in the shop as well as in the grove.

**Six frames at 12fps**, which is `HomesteadArt.Paint`'s rate, so half a second a loop. The
frames are the source's own size: unlike the waterfall there is no composition to downscale,
and a prop redrawn at its authored size needs no change to `scale` or `lift`.
"""
import argparse, collections, io, json, math, os, shutil, sys

try:
    from PIL import Image, ImageDraw
except ImportError:
    sys.exit("this needs Pillow: python -m pip install pillow")

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ART = os.path.join(ROOT, "Assets", "Game", "Art", "Homestead")
ANIM = os.path.join(ART, "Anim")
CONTENT = os.path.join(ROOT, "Assets", "StreamingAssets", "Content")
CATALOG = os.path.join(CONTENT, "homestead.json")
MANIFEST = os.path.join(CONTENT, "manifest.json")

FRAMES = 6

# Address prefix for a generated flipbook. A folder of its own rather than a folder beside
# `torch.png`, so an animated piece is visible as one in the project and no address differs
# from a still one only by capitalisation.
ADDRESS = "Homestead/Anim/"


# --------------------------------------------------------------------------- masks
def opaque(rgb, al):
    return al > 40


def flame_mask(rgb, al):
    """Saturated fire yellow. Excludes the torches' rope binding, which is warm but pale."""
    return opaque(rgb, al) & (rgb[..., 0] > 200) & (rgb[..., 1] > 140) & (rgb[..., 2] < 110)


def lantern_mask(rgb, al):
    return opaque(rgb, al) & (rgb[..., 0] > 200) & (rgb[..., 1] > 190) \
        & (rgb[..., 2] > 100) & (rgb[..., 2] < 200)


def warm_gem_mask(rgb, al):
    return opaque(rgb, al) & (rgb[..., 0] > 180) & (rgb[..., 1] > 90) & (rgb[..., 2] < 160)


def cool_gem_mask(rgb, al):
    return opaque(rgb, al) & (rgb[..., 2] > 110) & (rgb[..., 2] > rgb[..., 0] + 15)


def red_cloth_mask(rgb, al):
    return opaque(rgb, al) & (rgb[..., 0] > 180) & (rgb[..., 1] < 130) & (rgb[..., 2] < 150)


def blue_cloth_mask(rgb, al):
    return opaque(rgb, al) & (rgb[..., 2] > 150) & (rgb[..., 2] > rgb[..., 0] + 30)


def shaft_mask(rgb, al):
    """The well's open shaft: the flat mid-grey polygon in the top of the stone."""
    grey = (np.abs(rgb[..., 0] - rgb[..., 1]) < 8) & (np.abs(rgb[..., 1] - rgb[..., 2]) < 8)
    return (al > 200) & grey & (rgb[..., 0] > 75) & (rgb[..., 0] < 135)


# --------------------------------------------------------------------------- helpers
def load(name):
    return Image.open(os.path.join(ART, name + ".png")).convert("RGBA")


def mask_of(im, test):
    a = np.array(im).astype(int)
    return test(a[..., :3], a[..., 3])


def largest_blob(mask):
    """The biggest connected run of a mask, so a stray pixel elsewhere cannot widen it."""
    seen = np.zeros(mask.shape, bool)
    best = None
    h, w = mask.shape
    for sy in range(h):
        for sx in range(w):
            if not mask[sy, sx] or seen[sy, sx]:
                continue
            stack, blob = [(sy, sx)], []
            seen[sy, sx] = True
            while stack:
                y, x = stack.pop()
                blob.append((y, x))
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            if best is None or len(blob) > len(best):
                best = blob

    out = np.zeros(mask.shape, bool)
    if best:
        ys, xs = zip(*best)
        out[list(ys), list(xs)] = True
    return out


def bbox(mask):
    ys, xs = np.nonzero(mask)
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def cut(im, mask):
    a = np.array(im).copy()
    a[..., 3] = np.where(mask, a[..., 3], 0)
    return Image.fromarray(a)


def erase(im, mask):
    a = np.array(im).copy()
    a[..., 3] = np.where(mask, 0, a[..., 3])
    return Image.fromarray(a)


def clipped(layer, mask, brightness=1.0):
    """A layer confined to `mask`, optionally brightened. The rule the module docstring names."""
    a = np.array(layer).astype(float)
    a[..., 3] *= mask
    if brightness != 1.0:
        a[..., :3] = np.clip(a[..., :3] * brightness, 0, 255)
    return Image.fromarray(a.astype(np.uint8))


# --------------------------------------------------------------------------- recipes
def pulse(name, test, grow=.14, heat=.52, drift=1.4, rise=.45, n=FRAMES):
    """Something lit: it brightens and dims, and its interior shifts inside its own outline.

    Used for fire and for gems alike. What separates them is only how far the brightness
    swings — a flame flickers hard, a crystal breathes.
    """
    base = load(name)
    w, h = base.size
    m = mask_of(base, test)
    if not m.any():
        sys.exit("no region found in '%s'" % name)

    x0, y0, x1, y1 = bbox(m)
    region = cut(base, m).crop((x0, y0, x1, y1))
    rw, rh = region.size
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2

    out = []
    for i in range(n):
        t = i / n
        beat = .5 + .5 * math.sin(t * 2 * math.pi)
        lick = .5 + .5 * math.sin(t * 2 * math.pi + 2.3)

        k = 1.0 + grow * lick
        swollen = region.resize((max(1, int(rw * k)), max(1, int(rh * k))), Image.LANCZOS)

        layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        layer.alpha_composite(swollen,
                              (int(round(cx - swollen.width / 2 + drift * math.sin(t * 2 * math.pi))),
                               int(round(cy - swollen.height / 2 - (swollen.height - rh) * rise))))

        frame = base.copy()
        frame.alpha_composite(clipped(layer, m, 1 + heat * (beat - .35)))
        out.append(frame)

    return out


def well_water(name, n=FRAMES):
    """Water in the shaft, which the still art leaves as flat grey.

    Painted *into* a region that was one colour, so nothing is covered that mattered. The
    highlights travel across it on the loop, which is the whole animation — a well that is
    plainly wet reads better than a well with a moving rope, and costs no inpainting.
    """
    base = load(name)
    w, h = base.size

    shaft = mask_of(base, shaft_mask)
    shaft[int(h * .45):, :] = False          # below this is the well's own shadow
    shaft = largest_blob(shaft)

    x0, y0, x1, y1 = bbox(shaft)
    sw, sh = x1 - x0, y1 - y0

    DEEP = (36, 96, 132, 255)
    BODY = (52, 138, 176, 255)
    LIGHT = (104, 190, 218, 255)

    out = []
    for i in range(n):
        t = i / n

        water = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        d = ImageDraw.Draw(water)
        d.rectangle([x0, y0, x1, y1], fill=BODY)

        # A darker top, so the water sits *down* the shaft rather than level with the stone.
        d.rectangle([x0, y0, x1, y0 + sh * .30], fill=DEEP)

        for j, (yf, wf, sp) in enumerate([(.45, .52, 1), (.62, .34, 1), (.78, .44, 2)]):
            p = (t * sp + j / 3) % 1.0
            cxx = x0 + sw * (.18 + .64 * p)
            cyy = y0 + sh * yf
            rx = sw * wf * .5 * (.55 + .45 * math.sin(p * math.pi))
            d.ellipse([cxx - rx, cyy - 2.2, cxx + rx, cyy + 2.2], fill=LIGHT)

        frame = base.copy()
        frame.alpha_composite(clipped(water, shaft))
        out.append(frame)

    return out


def ripple(name, test, amp=5.0, waves=1.6, n=FRAMES):
    """Cloth in the wind: the streamers that hang in free space beside a pole.

    They are the one region on these pieces that can be *erased* and redrawn, because there
    is nothing behind them — which is what lets this deform rather than merely re-tint. The
    banner's own cloth is left alone: it hangs over the pole, so erasing it would leave a
    hole where the pole should be.
    """
    base = load(name)
    w, h = base.size
    m = mask_of(base, test)
    if not m.any():
        sys.exit("no cloth found in '%s'" % name)

    x0, y0, x1, y1 = bbox(m)
    span = max(1, x1 - x0)

    stripped = np.array(erase(base, m))
    cloth = np.array(cut(base, m))

    out = []
    for i in range(n):
        t = i / n
        frame = stripped.copy()

        for x in range(x0, x1):
            # Anchored at the pole and freest at the tip, so the cloth stays attached.
            grip = (x - x0) / span
            dy = int(round(amp * grip * math.sin(waves * 2 * math.pi * grip - t * 2 * math.pi)))
            column = cloth[:, x]
            if dy:
                column = np.roll(column, dy, axis=0)
                if dy > 0:
                    column[:dy] = 0
                else:
                    column[dy:] = 0

            keep = column[..., 3] > 0
            frame[keep, x] = column[keep]

        out.append(Image.fromarray(frame))

    return out


RECIPES = collections.OrderedDict([
    # fire: a hard flicker, because that is what fire does and what reads in a grid cell
    ("torch", lambda: pulse("torch", flame_mask)),
    ("torch_low", lambda: pulse("torch_low", flame_mask)),
    ("torch_tall", lambda: pulse("torch_tall", flame_mask)),
    ("candle", lambda: pulse("candle", flame_mask)),
    # a lantern's glass, which glows rather than burns
    ("lantern", lambda: pulse("lantern", lantern_mask, grow=.06, heat=.34, drift=.5)),
    # gems breathe: a slower, shallower swing than fire
    ("crystal", lambda: pulse("crystal", warm_gem_mask, grow=.05, heat=.30, drift=.6, rise=.5)),
    ("crystal_shards", lambda: pulse("crystal_shards", cool_gem_mask, grow=.06, heat=.34, drift=.5, rise=.5)),
    # cloth in the wind
    ("flag_red", lambda: ripple("flag_red", red_cloth_mask, amp=5)),
    ("banner_gold", lambda: ripple("banner_gold", red_cloth_mask, amp=4)),
    ("banner_crimson", lambda: ripple("banner_crimson", blue_cloth_mask, amp=4)),
    # water where the still art had a hole
    ("well", lambda: well_water("well")),
])


# --------------------------------------------------------------------------- writing
def write(pid, frames, dry):
    folder = os.path.join(ANIM, pid)
    if dry:
        return

    if os.path.isdir(folder):
        shutil.rmtree(folder)
    os.makedirs(folder)

    for i, im in enumerate(frames):
        im.save(os.path.join(folder, "f%02d.png" % i))

    # The still it replaces. Left on disk it would be an addressed asset nothing requests,
    # which the addressable audit reports and every build would carry into a bundle.
    for suffix in ("", ".meta"):
        stale = os.path.join(ART, pid + ".png" + suffix)
        if os.path.exists(stale):
            os.remove(stale)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", help="comma-separated piece ids, for iterating on one")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    wanted = list(RECIPES)
    if args.only:
        wanted = [p.strip() for p in args.only.split(",") if p.strip()]
        for pid in wanted:
            if pid not in RECIPES:
                sys.exit("no recipe for '%s'; have: %s" % (pid, ", ".join(RECIPES)))

    catalog = json.load(io.open(CATALOG, encoding="utf-8"),
                        object_pairs_hook=collections.OrderedDict)
    rows = {p.get("id"): p for p in catalog["pieces"]}

    for pid in wanted:
        if pid not in rows:
            sys.exit("no '%s' row in homestead.json; this file animates pieces that exist, "
                     "because the id is in save files already" % pid)

    for pid in wanted:
        frames = RECIPES[pid]()
        write(pid, frames, args.dry_run)

        row = rows[pid]
        row["art"] = ADDRESS + pid
        row["animated"] = True
        # Each tool marks the rows it owns, so the other one does not warn about a row it
        # has lost — see grove_art.tsv.
        row.pop("_imported", None)
        row["_generated"] = True

        print("  %-16s %d frame(s) at %dx%d -> %s%s"
              % (pid, len(frames), frames[0].width, frames[0].height, ADDRESS, pid))

    if args.dry_run:
        print("dry run; nothing written")
        return

    io.open(CATALOG, "w", encoding="utf-8", newline="\n").write(
        json.dumps(catalog, indent=2, ensure_ascii=False) + "\n")

    manifest = io.open(MANIFEST, encoding="utf-8", newline="").read()
    before = json.loads(manifest).get("groveVersion", 1)
    io.open(MANIFEST, "w", encoding="utf-8", newline="").write(
        manifest.replace('"groveVersion": %d,' % before, '"groveVersion": %d,' % (before + 1), 1))

    print("\ngroveVersion %d -> %d" % (before, before + 1))
    print()
    print("In the Editor, in this order:")
    print("  1. Glimmer Grove > Addressables > Sync All Assets")
    print("  2. Glimmer Grove > Addressables > Rebuild Grove Atlases")
    print("  3. Glimmer Grove > Validate Art, then Validate Content")


if __name__ == "__main__":
    main()
