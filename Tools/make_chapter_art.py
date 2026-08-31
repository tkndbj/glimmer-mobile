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

**A backdrop keeps its own colours and is turned to the level's accent.** It used to
be reduced to luminance and mapped back through a slate-to-accent ramp, which is a
duotone: every pixel of every backdrop held one hue, and since most authored accents
here are gold, most boards in the game were a painting behind an amber gel. `vivid`
rotates the picture's dominant hue onto the accent and moves every other hue in it by
the same amount, so a chapter's ten glades still read as ten places — the thing the
duotone was for — while the painting keeps having more than one colour in it. It is
then lifted to `BG_TARGET_LUMA`, which is what makes "cheerful" a number rather than
an adjective.

**The map is scaled to whole strips, never stretched to them.** The source is
resized on its own aspect until its height is exactly `strips x 1200`, and the
surplus width is trimmed from the centre. Stretching would be the easy fix and it
shows: every tree on the map would be the wrong shape by the same few per cent,
which reads as cheapness without ever reading as an error.
"""
import argparse, colorsys, io, json, math, os, sys

try:
    from PIL import Image, ImageEnhance, ImageFilter, ImageOps, ImageStat
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
            if len(parts) not in (3, 4):
                sys.exit(f"chapter_art.tsv line {n}: expected 3 or 4 tab-separated columns, "
                         f"got {len(parts)}")
            # The fourth column is optional and defaults to 0, so every row written before map
            # grading existed keeps producing exactly the map it produced.
            grade = float(parts[3]) if len(parts) == 4 and parts[3].strip() else 0.0
            # A lone `-` in the map column says this chapter's strips are not cut by this
            # tool. Four chapters share one hand-made strip set, and a row that had to name
            # *some* map source in order to exist is a row that quietly overwrites theirs the
            # first time somebody runs it without `--only backdrops`.
            map_src = None if parts[1].strip() == "-" else parts[1]

            # And the same `-` on the backdrops, for the same reason read the other way round.
            # A chapter that *borrows* a backdrop — every non-glade chapter does, from the nine
            # c01_shallows cuts — has a row that must not name a source for it: the tool writes
            # `play_8.png` graded to whichever chapter it was run for, so a row naming any
            # source at all would silently re-cut a picture four other chapters draw. It was
            # exactly the trap the map column already documents, waiting on the other column.
            bg_srcs = [] if parts[2].strip() == "-" else [q for q in parts[2].split(",") if q]
            out[parts[0]] = (map_src, bg_srcs, grade)
    return out


def hexcolour(value, fallback):
    value = (value or "").lstrip("#")
    if len(value) != 6:
        value = fallback.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def stops(deep, mid, high, knee=.55):
    """A 256-entry lookup from luminance to colour, through three stops.

    Three rather than two because a straight dark-to-light fade washes the middle of
    the image — which is exactly where a painted background keeps its structure —
    into a flat band of one hue.
    """
    table = []
    for i in range(256):
        t = i / 255.0
        table.append(lerp(deep, mid, t / knee) if t < knee else
                     lerp(mid, high, (t - knee) / (1.0 - knee)))
    return table


def ramp(slate, accent):
    """The night grade: slate, through a lifted mid, to accent.

    Kept exactly as it was because it is what the **maps** are graded with (`night`),
    and a map is the thing being looked at rather than something a board is drawn over.
    Board backdrops use `vivid` instead — see the note there.
    """
    # The darkest stop is the slate barely deepened, never black: a silhouette painted
    # black reads as a hole punched in the screen rather than as something standing in
    # front of the sky, and it takes the level's colour with it.
    return stops(lerp(slate, (0, 0, 0), .18), lerp(slate, accent, .38),
                 lerp(accent, (255, 255, 255), .08))


def lit(rgb, value, sat=None):
    """The same hue at a chosen brightness, and optionally a floor under its saturation.

    Working in HSV rather than lerping toward white is the whole difference between a
    colour that is *bright* and one that is *washed out*: `lerp(#123640, white, .6)` is
    a pale grey-blue with a sixth of the saturation it started with, while the same hue
    taken to V=.62 is a real teal. Every authored `slate` in this game is a near-black
    (V is .10 to .25), so a daylight backdrop cannot be reached by lightening one — it
    has to be re-lit.
    """
    h, s, _ = colorsys.rgb_to_hsv(*[c / 255.0 for c in rgb])
    if sat is not None:
        s = max(s, sat)
    return tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb(h, s, value))


def opened(root, spec):
    """One source picture, from a file or a `+`-joined stack of layer files.

    <b>A layer PNG is mostly holes, and `convert("RGB")` fills them with whatever
    happened to be under the alpha.</b> Three of the eight sources named in
    `chapter_art.tsv` are overlay layers out of layered packs — 71% and 75% transparent —
    and they were being opened straight to RGB, so most of `c03_amberwood` and
    `c04_nightbriar` was undefined paper with a few branches on it. Nothing could see it:
    the duotone that used to run here threw the colour away and remapped luminance, so a
    hole came out as a perfectly plausible ramp value. It only became visible the moment
    the grade started keeping the source's own colours.

    So a source is composited rather than flattened, and a spec may name a **stack** —
    `l1-sky.png+l2-ground.png` — which is what a layered pack is for and what
    `Scenery.Layered` already does with the same art at runtime. The bottom layer is the
    ground and everything else lands on it in order.

    The check is on the **bottom** layer only, and it is an error rather than a warning:
    a stack whose ground is itself transparent has nothing to composite onto, so it would
    silently go back to being paper — which is the exact failure this function exists to
    end.
    """
    layers = [os.path.join(root, q.strip()) for q in spec.split("+") if q.strip()]
    base = Image.open(layers[0]).convert("RGBA")
    holes = sum(1 for v in base.getchannel("A").resize((80, 80)).tobytes() if v < 250) / 6400.0
    if holes > .02:
        sys.exit(f"{os.path.basename(layers[0])} is {holes * 100:.0f}% transparent, so it "
                 f"cannot be the bottom of a stack — name an opaque background first")
    for path in layers[1:]:
        top = Image.open(path).convert("RGBA")
        if top.size != base.size:
            top = top.resize(base.size, Image.LANCZOS)
        base.alpha_composite(top)
    return base.convert("RGB")


# ------------------------------------------------------------------ tuning
#
# The three numbers `vivid` is held to. They are here rather than beside the code
# that reads them because between them they are the whole look of every board
# backdrop in the game, and a look that has been retuned four times wants its dials
# in one place.

# The floor a board backdrop is lifted to, in mean perceived brightness out of 255.
#
# A floor and not a level: a source that already arrives brighter than this is left
# exactly as painted, because there is nothing wrong with it. Only the genuinely dark
# packs are raised, and they are raised by opening their shadows rather than by washing
# the whole picture toward white.
#
# It can be set this high only because the lift runs in V alone. It was 112 while the
# lift was a per-channel RGB gamma, and it had to be: `c04_nightbriar` cuts a dungeon
# interior at mean V .25, and lifting that in RGB turned a brown cave into pale mud —
# dimmer-looking than the cave was, because it lost its colour as well as its contrast.
# Lifting V leaves saturation untouched, so the same cave comes up as a real colour and
# the floor can be a number chosen for a sky. See `vivid`.
#
# The point of having a number at all is that "cheerful" is otherwise an adjective
# nobody can check, and the grade this replaced was argued to be a daylight grade while
# shipping forty backdrops between 28 and 105.
BG_FLOOR_LUMA = 150.0

# The mean saturation a backdrop is scaled to reach, 0-1, and the range of scaling
# allowed to get there.
#
# A *target* rather than a fixed multiply, because the packs are not equally colourful:
# the sky pack `c01_shallows` cuts is already vivid and wants roughly 1.0, while the
# dungeon interior `c04_nightbriar` cuts is brown and drab and needs nearly double before
# its accent reads as a colour at all. One constant served the first and left the other
# three muddy.
#
# It is still a pure multiply — see `turned` — so hitting the target never costs the
# picture a single ratio. The ceiling is what stops a nearly-grey source being dragged to
# poster paint, which is the failure at the other end.
SATURATION_TARGET = .46
SATURATION_RANGE = (1.0, 2.1)

# A gamma on saturation, applied before the turn. Above 1 it pulls the *least* saturated
# pixels down hardest and leaves the most saturated nearly alone — so on a sky it whitens
# the clouds and not the blue behind them, which widens the gap between the two and is
# what makes the clouds read.
#
# Note the two things that keep it from being the saturation *floor* this file warns
# about in `turned`. A floor moved the low end **up**, closing that same gap from the
# other side; this moves it **down**. And it is a curve rather than a clamp, so a cloud
# stays as much paler than its sky as the pack painted it, only more so — nothing is
# collapsed onto one value.
#
# It is measured before `saturation_gain`, deliberately: the gain then scales the whole
# picture back to `SATURATION_TARGET`, so bleaching the clouds costs the backdrop none of
# its overall colour. The two work against each other on the mean and together on the gap.
CLOUD_BLEACH = 1.30


def key_hue(image):
    """The picture's own dominant hue, as a circular mean weighted by saturation.

    A mean of hue *numbers* is wrong at the wrap — the average of 350 degrees and 10 is
    zero, not 180 — so the hues are summed as unit vectors and the answer read back off
    the resultant. Weighting by saturation is what stops a white cloud voting: a blue sky
    with white clouds on it has one hue that matters, and it is the sky.
    """
    small = image.resize((64, 96), Image.LANCZOS).convert("HSV")
    hue, sat, _ = small.split()
    x = y = 0.0
    for h, s in zip(hue.tobytes(), sat.tobytes()):
        angle = h / 255.0 * 2.0 * math.pi
        x += math.cos(angle) * s
        y += math.sin(angle) * s
    return (math.atan2(y, x) / (2.0 * math.pi)) % 1.0


def bleached(image, power=CLOUD_BLEACH):
    """Saturation pulled down at the pale end, so what is nearly white goes whiter.

    See `CLOUD_BLEACH` for why this is safe where a saturation floor is not.
    """
    if power == 1.0:
        return image
    hue, sat, val = image.convert("HSV").split()
    sat = sat.point([int(round(255 * (i / 255.0) ** power)) for i in range(256)])
    return Image.merge("HSV", (hue, sat, val)).convert("RGB")


def saturation_gain(image):
    """The multiply that brings this picture's mean saturation to `SATURATION_TARGET`."""
    mean = ImageStat.Stat(image.convert("HSV")).mean[1] / 255.0
    low, high = SATURATION_RANGE
    return min(high, max(low, SATURATION_TARGET / max(mean, .01)))


def turned(image, offset, saturation):
    """Every hue rotated by one constant offset; saturation scaled, never clamped.

    <b>A constant offset, never a pull toward a target.</b> Rotating each hue a fraction
    of the way to one destination collapses the picture's variety toward that
    destination — a brown trunk and a blue sky both come out amber-ish and the picture
    stops having two colours in it, which is the duotone this replaced arriving by a
    slower road. Adding the same offset to every hue moves the whole palette and leaves
    every difference inside it exactly as painted.

    <b>And saturation is a multiply with no floor and no cap.</b> That is the part that
    was got wrong first. A floor lifts the *least* saturated pixels most, so a white
    cloud on a blue sky is pushed up to meet the sky and the clouds stop reading; a cap
    pulls the sky down to meet the clouds and does the same thing from the other side.
    Both flatten precisely the contrast the picture was bought for. Scaling preserves
    every ratio, so a cloud stays exactly as much paler than its sky as the pack painted
    it.
    """
    hue, sat, val = image.convert("HSV").split()
    step = int(round(offset * 255))
    hue = hue.point([(i + step) % 256 for i in range(256)])
    sat = sat.point([min(255, int(round(i * saturation))) for i in range(256)])
    return Image.merge("HSV", (hue, sat, val)).convert("RGB")


def luma(image):
    """Mean perceived brightness, 0-255. The one number this grade is held to."""
    r, g, b = ImageStat.Stat(image).mean
    return .2126 * r + .7152 * g + .0722 * b


def vivid(cut, slate, accent):
    """The board grade: the painting, softened and a little livelier. Nothing else.

    <b>There is no tint here, and that is the whole point of this function.</b> Two
    grades shipped before it and both recoloured the art. The first reduced the source
    to luminance and mapped it back through a `slate`-to-`accent` ramp — a duotone, so
    every pixel of every backdrop held one hue, and since most authored accents here are
    gold, most boards in the game were a painting behind an amber gel. The second kept
    the picture's own hues but rotated them all onto the accent, which is a smaller
    mistake of exactly the same kind: the sky the pack painted blue still did not arrive
    blue.

    So the source's colours are simply kept. A blue sky with white clouds is a blue sky
    with white clouds, and `slate` and `accent` no longer reach this picture at all —
    they still name the board's own plate and its light, which is where a level's
    identity belongs.

    What is left is three things a backdrop genuinely owes the board in front of it:

      * **softened**, so the painting's detail cannot compete with the tiles. This is the
        one thing every version of this grade has agreed about.
      * **a little livelier** — a modest saturation and contrast lift, the amount a
        photograph gets rather than the amount a filter gets. Enough to stop a licensed
        pack's fairly restrained palette reading as flat behind bright tiles.
      * **lifted to `BG_FLOOR_LUMA` if, and only if, it is under it**, by opening the
        shadows with a gamma rather than by adding white. A dungeon interior needs this
        and a midday sky must not be touched by it.

    <b>The cost of having no tint is that a chapter's glades share their source's
    palette</b>, which is what the duotone was really buying — `c01_shallows` cuts nine
    backdrops from two skies, so nine glades are now nine crops of one blue sky rather
    than nine colours. That is a decision about the art rather than about this code: the
    fix is more sources in `chapter_art.tsv`, not a filter put back over these ones.
    """
    # Softened first. A blur after any tone work would smear that work's own banding
    # across the picture instead of the painting's detail.
    out = bleached(cut.filter(ImageFilter.GaussianBlur(6)))

    # Shortest way round the wheel, so a hue two thirds of a turn clockwise is read as a
    # third anticlockwise and a picture never takes the long route to its own accent.
    accent_hue, _, _ = colorsys.rgb_to_hsv(*[c / 255.0 for c in accent])
    out = turned(out, ((accent_hue - key_hue(out) + .5) % 1.0) - .5, saturation_gain(out))
    out = ImageEnhance.Contrast(out).enhance(1.06)

    # Shadows opened until the picture clears the floor, never past it.
    #
    # <b>In V alone, never on the three RGB channels.</b> A gamma applied per channel
    # raises the *smallest* channel proportionally most, so it lifts a colour toward grey
    # as a side effect of lifting it toward light: it turned `c04_nightbriar`'s brown cave
    # into pale mud, which reads dimmer than the cave did because it lost its colour as
    # well as its contrast. Lifting V leaves hue and saturation exactly alone, so a dark
    # brown becomes a bright orange rather than a beige.
    #
    # Gamma rather than a multiply for the same reason it was gamma before: a multiply
    # raises the highlights that are already correct and flattens the picture into paper.
    for _ in range(6):
        here = luma(out)
        if here >= BG_FLOOR_LUMA - 3.0:
            break
        gamma = max(.45, (here / BG_FLOOR_LUMA) ** .6)
        table = [min(255, int(round((i / 255.0) ** gamma * 255))) for i in range(256)]
        hue, sat, val = out.convert("HSV").split()
        out = Image.merge("HSV", (hue, sat, val.point(table))).convert("RGB")

    # A vignette that frames rather than darkens, toward a soft warm paper. Neither this
    # nor anything above reads `accent`, so it is the same at every level of a chapter —
    # which is correct: framing is not identity.
    return Image.composite(out, Image.new("RGB", out.size, (250, 246, 236)),
                           vignette(out.size, .10))


def vertical_wash(size, top=.42, bottom=1.0):
    """A soft top-down darkening, so the status bar end of the screen sits back.

    Gentle on a board backdrop and heavy on nothing: the header band has a `FadeUp`
    shade of its own in both `PlayScreen` and `ModeScreen`, so the readouts do not need
    the whole top third of the picture darkened to be legible — and darkening it is what
    made every backdrop read as dusk whatever it was graded to.
    """
    w, h = size
    grad = Image.new("L", (1, h))
    for y in range(h):
        t = y / float(h - 1)
        grad.putpixel((0, y), int(round(255 * (top + (bottom - top) * t))))
    return grad.resize(size, Image.BILINEAR)


def vignette(size, strength=.55):
    """A soft oval mask: 255 in the middle, falling to `255 - 255*strength` at the corners."""
    w, h = size
    small = Image.new("L", (w // 8, h // 8), 0)
    inner = Image.new("L", (int(w / 8 * .82), int(h / 8 * .86)), 255)
    small.paste(inner, ((small.size[0] - inner.size[0]) // 2, (small.size[1] - inner.size[1]) // 2))
    small = small.filter(ImageFilter.GaussianBlur(small.size[0] * .22))
    mask = small.resize(size, Image.BILINEAR)
    return Image.eval(mask, lambda v: int(255 - (255 - v) * strength))


def backdrop(root, source, window, slate, accent):
    """One graded board backdrop, cut from a window of a source painting."""
    src = opened(root, source)
    w, h = src.size

    # A portrait window the height of the source, slid across it by index. Taken at
    # the source's own scale wherever it can be — these paintings are about the
    # height of a phone already, so cutting rather than resampling keeps the edges.
    want = max(1, int(round(h * BG_W / float(BG_H))))
    span = max(1, w - want)
    x = int(round(span * window)) if w > want else 0
    cut = src.crop((x, 0, min(w, x + want), h)).resize((BG_W, BG_H), Image.LANCZOS)

    # Graded rather than duotoned. `vivid` owns the whole of it — the blur, the
    # recolouring, the brightness floor and the two settles — because those four have to
    # agree about ordering and a caller that could get one of them wrong is a caller that
    # eventually will. See its remarks for what each is for.
    return vivid(cut, slate, accent)


def night(image, slate, accent, strength):
    """Pulls a map toward the chapter's own palette, keeping its painted detail.

    A blend rather than a replacement, and that is the difference between a map and a
    silhouette: `backdrop` grades all the way because a board is drawn on top of it and
    detail there is noise, while a map *is* the thing being looked at. At .55 a daylight
    island map reads as the same islands at night, which is what lets two chapters share a
    source and still be two places -- the argument the backdrops already make.

    Zero returns the image untouched, so a chapter that does not ask for this gets exactly
    what it got before the option existed.
    """
    if strength <= 0:
        return image

    grey = ImageOps.autocontrast(image.convert("L"), cutoff=1)
    table = ramp(slate, accent)
    graded = Image.merge("RGB", [
        grey.point([table[i][channel] for i in range(256)]) for channel in range(3)
    ])
    return Image.blend(image, graded, strength)


def strips(root, source, names, slate=None, accent=None, grade=0.0):
    """The map, cut bottom-upward so strip 0 is the foot of the trail."""
    src = opened(root, source)
    w, h = src.size
    total = len(names) * STRIP_H

    scaled = src.resize((max(STRIP_W, int(round(w * total / float(h)))), total), Image.LANCZOS)
    x = (scaled.size[0] - STRIP_W) // 2
    board = scaled.crop((x, 0, x + STRIP_W, total))

    # Graded whole rather than per strip, or every strip would autocontrast against its
    # own slice and the map would step in brightness at each seam.
    board = night(board, slate, accent, grade)

    for i, name in enumerate(names):
        bottom = total - i * STRIP_H
        yield name, board.crop((0, bottom - STRIP_H, STRIP_W, bottom))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("chapter")
    ap.add_argument("--source", required=True, help="folder the art packs were extracted into")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--only", choices=("maps", "backdrops"),
                    help="write just one half. A re-grade of the board backdrops must not "
                         "silently re-cut the maps, which are a separate decision and are "
                         "graded with a separate ramp.")
    ap.add_argument("--out", help="write somewhere other than the project, for judging a "
                                  "grade before it lands.")
    args = ap.parse_args()

    map_art = os.path.join(args.out, "Map") if args.out else MAP_ART
    bg_art = os.path.join(args.out, "Bg") if args.out else BG_ART

    table = rows()
    if args.chapter not in table:
        sys.exit(f"no row for '{args.chapter}' in Tools/chapter_art.tsv")
    map_src, bg_srcs, map_grade = table[args.chapter]

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

    strip_count = 0 if map_src is None else len(chapter.get("mapStrips") or [])
    bg_count = 0 if not bg_srcs else len(wanted)
    print(f"{args.chapter}: {strip_count} strip(s), {bg_count} backdrop(s)"
          + ("" if bg_srcs else f" (it borrows {chapter.get('backdrop')}, which is not this "
                                "row's to cut)"))

    if not args.dry_run:
        os.makedirs(map_art, exist_ok=True)
        os.makedirs(bg_art, exist_ok=True)

    for name, image in ([] if args.only == "backdrops" or map_src is None else strips(args.source, map_src,
                              chapter.get("mapStrips") or [],
                              hexcolour(fallback_slate, "#123640"),
                              hexcolour(fallback_accent, "#FFC93C"),
                              map_grade)):
        out = os.path.join(map_art, name + ".png")
        print(f"  map  {name:<16} {image.size[0]}x{image.size[1]}")
        if not args.dry_run:
            image.save(out)

    for i, (name, accent, slate) in enumerate(
            [] if args.only == "maps" or not bg_srcs else wanted):
        source = bg_srcs[i % len(bg_srcs)]
        # Windows are spread across each source rather than taken in a row, so two
        # paintings do not hand out two near-identical crops to adjacent glades.
        per = max(1, (len(wanted) + len(bg_srcs) - 1) // len(bg_srcs))
        window = (i // len(bg_srcs)) / float(max(1, per - 1)) if per > 1 else .5
        image = backdrop(args.source, source, window, hexcolour(slate, fallback_slate),
                         hexcolour(accent, fallback_accent))
        print(f"  bg   {name:<16} {accent} on {slate}")
        if not args.dry_run:
            image.save(os.path.join(bg_art, name + ".png"))

    print("\nNext: Glimmer Grove > Addressables > Sync All Assets, then > Validate Art.")
    print("The importer hook only addresses art that arrives while the Editor is running.")


if __name__ == "__main__":
    main()
