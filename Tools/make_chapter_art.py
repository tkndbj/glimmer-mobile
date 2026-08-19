# -*- coding: utf-8 -*-
"""Cuts a chapter's map strips and board backdrops from source art, driven by chapter_art.tsv.

    python Tools/make_chapter_art.py c02_millvale --source "C:/path/to/_extracted"

Two outputs, both named by the chapter file rather than by this script:

  * `Assets/Game/Art/Map/<strip>.png` — one 1080x1200 slice per name in `mapStrips`,
    cut **bottom-upward** out of one tall image, because strip 0 is the foot of the
    map and the trail is walked upward from there.
  * `Assets/Game/Art/Bg/<backdrop>.png` — one 720x1280 board backdrop per distinct
    backdrop the chapter or any of its levels names.

Three decisions worth not re-litigating.

**The names come from the content and the palette comes from the content.** This
script is told which pack to cut from and nothing else: the strip names, the
backdrop names and the accent/slate each backdrop is graded to are all read out of
`chapters/<id>.json`. So retuning a glade's colour regrades its backdrop with no
second place to remember, and a chapter that adds an eleventh glade gets an
eleventh backdrop by being authored, not by anybody editing a list here. It is the
same rule `AssetManifest.ChapterAssets` follows on the other side of the pipeline —
art is derived from the catalog, never hand-listed.

**A backdrop is graded, not merely darkened.** The source is reduced to luminance,
softened, and then mapped onto a three-stop ramp built from the level's own slate
and accent. That keeps the painted structure — a treeline, a ridge, a sky — while
guaranteeing the result cannot fight the board in front of it, and it is why ten
glades can share two source images without looking like ten crops of two images.

**The map is scaled to whole strips, never stretched to them.** The source is
resized on its own aspect until its height is exactly `strips x 1200`, and the
surplus width is trimmed from the centre. Stretching would be the easy fix and it
shows: every tree on the map would be the wrong shape by the same few per cent,
which reads as cheapness without ever reading as an error.
"""
import argparse, io, json, os, sys

try:
    from PIL import Image, ImageFilter, ImageOps
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

Image.MAX_IMAGE_PIXELS = None

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
TSV = os.path.join(HERE, "chapter_art.tsv")
CHAPTERS = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters")
MAP_ART = os.path.join(ROOT, "Assets", "Game", "Art", "Map")
BG_ART = os.path.join(ROOT, "Assets", "Game", "Art", "Bg")

# ChapterMap.Width and ChapterMap.StripHeight, in canvas units, which is also the
# pixel size the strips are authored at. Mirrored rather than imported for the
# reason content.py mirrors the validator: this runs with no Unity anywhere.
STRIP_W, STRIP_H = 1080, 1200

# The board backdrop's own size, matching every backdrop already shipped. Portrait,
# and deliberately smaller than the screen: it is scaled up behind a board that is
# covering most of it, so pixels spent here are pixels nobody sees.
BG_W, BG_H = 720, 1280


def rows():
    out = {}
    with io.open(TSV, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split("\t")
            if len(parts) != 3:
                sys.exit(f"chapter_art.tsv line {n}: expected 3 tab-separated columns, got {len(parts)}")
            out[parts[0]] = (parts[1], [p for p in parts[2].split(",") if p])
    return out


def hexcolour(value, fallback):
    value = (value or "").lstrip("#")
    if len(value) != 6:
        value = fallback.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def ramp(slate, accent):
    """A 256-entry lookup from luminance to colour: slate, through a lifted mid, to accent.

    Three stops rather than two because a straight slate-to-accent fade washes the
    middle of the image — which is exactly where a painted background keeps its
    structure — into a flat band of the accent's hue.
    """
    # The darkest stop is the slate barely deepened, never black: a silhouette painted
    # black reads as a hole punched in the screen rather than as something standing in
    # front of the sky, and it takes the level's colour with it.
    deep = lerp(slate, (0, 0, 0), .18)
    mid = lerp(slate, accent, .38)
    high = lerp(accent, (255, 255, 255), .08)

    table = []
    for i in range(256):
        t = i / 255.0
        table.append(lerp(deep, mid, t / .55) if t < .55 else lerp(mid, high, (t - .55) / .45))
    return table


def vertical_wash(size, top=.42, bottom=1.0):
    """A soft top-down darkening, so the status bar end of the screen sits back."""
    w, h = size
    grad = Image.new("L", (1, h))
    for y in range(h):
        t = y / float(h - 1)
        grad.putpixel((0, y), int(round(255 * (top + (bottom - top) * t))))
    return grad.resize(size, Image.BILINEAR)


def vignette(size, strength=.55):
    w, h = size
    small = Image.new("L", (w // 8, h // 8), 0)
    inner = Image.new("L", (int(w / 8 * .82), int(h / 8 * .86)), 255)
    small.paste(inner, ((small.size[0] - inner.size[0]) // 2, (small.size[1] - inner.size[1]) // 2))
    small = small.filter(ImageFilter.GaussianBlur(small.size[0] * .22))
    mask = small.resize(size, Image.BILINEAR)
    return Image.eval(mask, lambda v: int(255 - (255 - v) * strength))


def backdrop(source, window, slate, accent):
    """One graded board backdrop, cut from a window of a source painting."""
    src = Image.open(source).convert("RGB")
    w, h = src.size

    # A portrait window the height of the source, slid across it by index. Taken at
    # the source's own scale wherever it can be — these paintings are about the
    # height of a phone already, so cutting rather than resampling keeps the edges.
    want = max(1, int(round(h * BG_W / float(BG_H))))
    span = max(1, w - want)
    x = int(round(span * window)) if w > want else 0
    cut = src.crop((x, 0, min(w, x + want), h)).resize((BG_W, BG_H), Image.LANCZOS)

    # Softened first, then graded. A blur after the grade would smear the ramp's
    # own banding across the image instead of the painting's detail.
    grey = ImageOps.autocontrast(cut.convert("L"), cutoff=2)
    grey = grey.filter(ImageFilter.GaussianBlur(7))

    table = ramp(slate, accent)
    graded = Image.merge("RGB", [
        grey.point([table[i][channel] for i in range(256)]) for channel in range(3)
    ])

    graded = Image.composite(graded, Image.new("RGB", graded.size, (0, 0, 0)),
                             vertical_wash(graded.size))
    return Image.composite(graded, Image.new("RGB", graded.size, lerp(slate, (0, 0, 0), .55)),
                           vignette(graded.size))


def strips(source, names):
    """The map, cut bottom-upward so strip 0 is the foot of the trail."""
    src = Image.open(source).convert("RGB")
    w, h = src.size
    total = len(names) * STRIP_H

    scaled = src.resize((max(STRIP_W, int(round(w * total / float(h)))), total), Image.LANCZOS)
    x = (scaled.size[0] - STRIP_W) // 2
    board = scaled.crop((x, 0, x + STRIP_W, total))

    for i, name in enumerate(names):
        bottom = total - i * STRIP_H
        yield name, board.crop((0, bottom - STRIP_H, STRIP_W, bottom))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("chapter")
    ap.add_argument("--source", required=True, help="folder the art packs were extracted into")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    table = rows()
    if args.chapter not in table:
        sys.exit(f"no row for '{args.chapter}' in Tools/chapter_art.tsv")
    map_src, bg_srcs = table[args.chapter]

    path = os.path.join(CHAPTERS, args.chapter + ".json")
    if not os.path.exists(path):
        sys.exit(f"no chapter body at {path}")
    chapter = json.load(io.open(path, encoding="utf-8"))

    fallback_accent = chapter.get("accent") or "#FFC93C"
    fallback_slate = chapter.get("slate") or "#123640"

    # Every backdrop this chapter draws, in the order the player meets them, with the
    # palette it is graded to. The chapter's own backdrop is first because a level
    # that overrides nothing inherits it.
    wanted, seen = [], set()

    def want(name, accent, slate):
        if name and name not in seen:
            seen.add(name)
            wanted.append((name, accent, slate))

    want(chapter.get("backdrop"), fallback_accent, fallback_slate)
    for level in chapter.get("levels", []):
        want(level.get("backdrop") or chapter.get("backdrop"),
             level.get("accent") or fallback_accent,
             level.get("slate") or fallback_slate)

    print(f"{args.chapter}: {len(chapter.get('mapStrips') or [])} strip(s), {len(wanted)} backdrop(s)")

    if not args.dry_run:
        os.makedirs(MAP_ART, exist_ok=True)
        os.makedirs(BG_ART, exist_ok=True)

    for name, image in strips(os.path.join(args.source, map_src), chapter.get("mapStrips") or []):
        out = os.path.join(MAP_ART, name + ".png")
        print(f"  map  {name:<16} {image.size[0]}x{image.size[1]}")
        if not args.dry_run:
            image.save(out)

    for i, (name, accent, slate) in enumerate(wanted):
        source = os.path.join(args.source, bg_srcs[i % len(bg_srcs)])
        # Windows are spread across each source rather than taken in a row, so two
        # paintings do not hand out two near-identical crops to adjacent glades.
        per = max(1, (len(wanted) + len(bg_srcs) - 1) // len(bg_srcs))
        window = (i // len(bg_srcs)) / float(max(1, per - 1)) if per > 1 else .5
        image = backdrop(source, window, hexcolour(slate, fallback_slate),
                         hexcolour(accent, fallback_accent))
        print(f"  bg   {name:<16} {accent} on {slate}")
        if not args.dry_run:
            image.save(os.path.join(BG_ART, name + ".png"))

    print("\nNext: Glimmer Grove > Addressables > Sync All Assets, then > Validate Art.")
    print("The importer hook only addresses art that arrives while the Editor is running.")


if __name__ == "__main__":
    main()
