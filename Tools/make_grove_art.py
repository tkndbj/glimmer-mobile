# -*- coding: utf-8 -*-
"""Renders the grove's pieces from CC0 models, at the floor's own isometric projection.

    python Tools/make_grove_art.py                 # render the roster into Art/Homestead
    python Tools/make_grove_art.py --check         # prove what is on disk is what it writes
    python Tools/make_grove_art.py --contact       # a sheet, at the size a phone draws them
    python Tools/make_grove_art.py --survey hex --source <bundle>
    python Tools/make_grove_art.py --vendor --source <bundle>

WHAT IT REPLACED, AND WHY. The grove used to be cut from flat isometric sheets. That
works and it costs two things that only show up later: a piece can never be *turned*,
because one drawing is one angle and there is no second sprite to turn to; and every
number describing it — how big it is, where its feet are — has to be typed by hand,
because nothing about a cut-out knows how large the thing was. Rendering from a model
answers both. Four facings are four camera yaws, and the size and the footing are
*measured*, in world units, by the thing that drew them.

THE PROJECTION IS NOT A CHOICE. A unit square on the ground projects to a diamond whose
height over width is `sin(pitch)`, so the pitch that agrees with a floor of face ratio r
is `asin(r)` and nothing else. `GroveFloor.TileFaceRatio` is 0.5628, so `PITCH` is
34.25 degrees — and if that constant ever moves, this one moves with it or every piece
in the grove stands at an angle the ground under it denies.

WHAT IS DERIVED AND MUST NEVER BE TYPED. `scale`, `lift`, `w`, `h` and the hit mask are
all facts about a picture, and this tool is what makes the picture. They are written
into `homestead.json` by `import_grove_art.py` from what was actually rendered. The one
number here that is a *judgement* is `Pack.units_per_tile`, and it is a judgement
because "how many tiles ought a church cover" is not a fact about any file.

THE MODELS ARE COMMITTED (CC0, KayKit — see `Tools/kaykit/CREDITS.txt`), so any checkout
can re-render with nothing downloaded. `--vendor` is what puts them there, and it copies
only what the roster names, so the repo holds its dependencies and no more.
"""
import argparse
import io
import math
import os
import shutil
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kaykit                                                          # noqa: E402
import grove_roster as roster                                          # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ART = os.path.join(ROOT, "Assets", "Game", "Art", "Homestead")
OUT = os.path.join(HERE, "out")

# ----------------------------------------------------------------- the projection
#: `GroveFloor.TileFaceRatio`. Mirrored rather than imported because nothing here reads
#: C#; `import_grove_art.py` holds the file to it, so the two cannot drift in silence.
TILE_FACE_RATIO = 0.5628

#: See the module docstring — `asin(TILE_FACE_RATIO)`, never tuned.
PITCH = math.degrees(math.asin(TILE_FACE_RATIO))

#: The four camera yaws, in the order a facing index means. 45 degrees is what turns a
#: square grid cell into a diamond; the quarter turns are the facings themselves.
YAWS = (45.0, 135.0, 225.0, 315.0)

# ---------------------------------------------------------------------- resolution
#: Art pixels per floor tile. Chosen so a one-tile prop is drawn at very slightly above
#: its own size on a phone at full zoom (`GroveFloor.TileWidth` 220 against
#: `HomesteadScreen.PieceScale` 1.15), because upscaling is the plainest "cheap" signal
#: there is and it is invisible in every gate (invariant 37at).
PX_PER_TILE = 192

#: `ArtImportRules.Caps` for `Art/Homestead/`. A piece whose extent would ask for more
#: is rendered at the cap and its `scale` derived from what it actually got — so the
#: largest buildings are softer at full zoom and nothing is ever cut off.
MAX_PX = 512

#: Supersampling. Four is where the stair-stepping on a low-poly silhouette stops being
#: visible at the size the shop draws a thumbnail.
SUPER = 4

# --------------------------------------------------------------------------- light
#: One rig for every piece, which is the whole reason a rendered catalogue is more
#: consistent than a cut one: a church and a barrel cannot arrive at different
#: brightnesses, so nothing has to be normalised afterwards (`CAST_VALUE`'s job, which
#: only exists because that cast was cut from packs that disagreed).
KEY = (-0.40, 0.86, -0.52)
AMBIENT = 0.62
RIM = 0.10

#: How much of its own size a piece is padded by, so a rim has somewhere to land.
PAD = 0.03

# ----------------------------------------------------------------------- the sun
# **The key is a sun and the fill is the sky, and before this they were one grey.**
#
# `AMBIENT` alone says how *much* light a face turned away from the key gets and can never
# say what *colour* it is, so every piece in the catalogue was lit by one white lamp: a
# roof and the wall under it differed only in how dark they were. That renders a flat-
# shaded pack as a diagram of itself — correct, even, and with nothing in it that says
# outdoors. The verdict was "dark and dead", and the count behind it is that the whole
# village used exactly one hue of light.
#
# So the key carries a warm sun and the fill carries a cool sky, which is the one thing
# two numbers can say that one cannot. `AMBIENT` and the scalar path it belongs to are
# still there and still exactly what they were — `kaykit.render` falls back to them when
# nothing asks for a colour, which is what let the refactor be proved byte-identical
# before any of these numbers were chosen.
#
# **Both are above and below one on purpose.** `SUN` is over-unity, so a face the sun
# really lands on is drawn *brighter than the pack painted it* rather than merely
# undimmed; measured across the roster nothing clips, because these atlases top out well
# short of white. `SHADE` is barely cool — three per cent — because a shadow tinted any
# harder turns the pack's neutral stone blue, and a village of blue towers is the same
# complaint from the other side.
SUN = (1.32, 1.21, 1.02)
SHADE = (0.74, 0.75, 0.775)

#: Saturation, applied to the finished picture. Lifting light divides apparent saturation
#: by the same factor (`_grade` is the same arithmetic the ground runs), so a sun that
#: is not paid for in chroma buys brightness and loses colour — which is the half of
#: "dead" that brightness alone does not fix. Luminance is left alone: `SUN` has already
#: done that, and doing it twice is how a picture goes pastel.
PIECE_CHROMA = 1.24


# --------------------------------------------------------------------------- render
class Rendered(object):
    """One piece's facings, and the facts about the picture the catalogue needs."""

    __slots__ = ("row", "images", "w", "h", "scale", "lift", "alike")

    def __init__(self, row, images, w, h, scale, lift, alike=False):
        self.row, self.images = row, images
        self.w, self.h, self.scale, self.lift = w, h, scale, lift
        #: True when every facing rendered identically — see `piece`.
        self.alike = alike


def piece(row, model_paths):
    """Renders one roster row into its facings, trimmed to one shared box.

    Three things are shared across the facings on purpose, and each is a fault if it is
    not: the **extent**, or the piece changes size when it is turned; the **trim**, or it
    jumps sideways; and the **box in pixels**, or `scale` differs per facing and the
    catalogue can only carry one.
    """
    parts = [kaykit.load(p) for p in model_paths]
    mesh = kaykit.merge(parts)
    tex = kaykit.texture(model_paths[0], parts[0].mtllib)

    # **The model itself is scaled, before anything is measured.** See `Row.size`: this pack
    # draws buildings that sit *on* a hex and walls that *are* one, so a handful of models
    # have to come down to object scale. Doing it here rather than to the finished number
    # means the extent, the footprint, the drawn size and the footing all follow from one
    # change — correcting `scale` afterwards would leave the footprint describing a piece
    # that is no longer that size, which is the class of disagreement this whole rework was
    # about.
    if row.size != 1.0:
        mesh = kaykit.Mesh(mesh.verts * row.size, mesh.uvs, mesh.norms, mesh.tris, mesh.mtllib)

    yaws = YAWS[:row.facings] if row.facings > 1 else YAWS[:1]
    extent = kaykit.Extent.over(mesh, PITCH, yaws, pad=PAD)

    tiles = extent.span / row.pack.units_per_tile
    box = int(min(MAX_PX, max(32, round(tiles * PX_PER_TILE))))

    cull = kaykit.cullable(mesh, tex, PITCH, yaws)
    buffers = [kaykit.render(mesh, tex, PITCH, yaw, box, extent, super_sample=SUPER,
                             light=KEY, ambient=AMBIENT, rim=RIM, cull=cull,
                             sun=SUN, shadow=SHADE)
               for yaw in yaws]

    # One trim over every facing. `getbbox` on each and then the union, rather than the
    # union of the alpha arrays, because a facing that renders empty (a model with no
    # geometry above the ground) must not drag the box out to the full frame.
    boxes = [kaykit.to_image(b).getbbox() for b in buffers]
    boxes = [b for b in boxes if b]
    if not boxes:
        raise ValueError("%s rendered nothing at all" % row.id)
    left = min(b[0] for b in boxes)
    top = min(b[1] for b in boxes)
    right = max(b[2] for b in boxes)
    bottom = max(b[3] for b in boxes)

    # Graded **after** the trim and before anything is measured off it, which is the whole
    # of why it is safe: `_grade` touches the three colour channels and nothing else, so
    # the alpha the box was found from, the size the catalogue carries and the hit mask
    # the build gate reads are all exactly what they would have been. A grade that moved
    # any of those would be a catalogue describing a piece that is no longer that shape.
    images = [_grade(kaykit.to_image(b).crop((left, top, right, bottom)), 1.0, PIECE_CHROMA)
              for b in buffers]
    w, h = right - left, bottom - top

    # **Four facings that all look the same is four times the download for nothing**, and it
    # is invisible everywhere else: the piece is correct, the catalogue is correct, the shop
    # draws it and the player can turn it — round to exactly where it was. A cube, a barrel
    # and a boulder are all symmetric enough to land here. Reported rather than corrected,
    # because whether two renders are "the same" at the size a phone draws them is a
    # judgement and the roster is where judgements live.
    alike = (row.facings > 1
             and all(np.array_equal(np.asarray(images[0]), np.asarray(im)) for im in images[1:]))

    # ------------------------------------------------------------------ the facts
    # How big to draw it. Independent of the trim, which is why it can be stated once for
    # all facings — it only depends on how much world one art pixel stands for.
    world_per_px = extent.span / float(box)
    scale = world_per_px / row.pack.tile_on_screen * TILE_WIDTH / PIECE_SCALE

    # Where its feet are: the model stands on y=0 and its origin is the tile it occupies,
    # so project the world origin and ask which row of the trimmed picture it landed on.
    # `GroveTileArt.Offset` lifts the art by `size.y * lift` above the tile's point, so
    # the lift that puts the origin *on* the point is how far below centre it fell.
    _, up, _ = kaykit.basis(PITCH, yaws[0])
    origin_y = float(np.array([0.0, 0.0, 0.0]) @ up)
    origin_row = (box / 2.0 - (origin_y - extent.cy) * (box / extent.span)) - top
    lift = origin_row / float(h) - 0.5

    return Rendered(row, images, w, h, scale, lift, alike)


#: `GroveFloor.TileWidth` and `HomesteadScreen.PieceScale`, mirrored here for the two
#: derivations above. `import_grove_art.py` holds the C# to both.
TILE_WIDTH = 220.0
PIECE_SCALE = 1.15


def _one(row):
    """A worker's whole job: render one row and hand back something picklable."""
    r = piece(row, row.vendored())
    return (row.id, [(im.tobytes(), im.size) for im in r.images],
            r.w, r.h, r.scale, r.lift, r.alike)


def render_all(rows, jobs=0):
    """Every row, across processes.

    A render is a pure function of a model file and a handful of constants, so this
    fans out with nothing shared and no ordering to preserve — which is what makes
    `--check` affordable enough to actually run. Rendering the whole roster in one
    process is about twenty-five minutes; a gate nobody runs proves nothing.
    """
    if jobs == 1 or len(rows) == 1:
        return [piece(r, r.vendored()) for r in rows]

    import multiprocessing
    # Half the cores and never more than eight. Each worker holds a supersampled
    # frame buffer of its own — about 130 MB while a 512-pixel piece is in flight —
    # so this is bounded by memory rather than by cores, and sixteen workers is an
    # out-of-memory failure on a 32 GB machine with anything else running.
    # Four, not one per core. Each worker peaks near a hundred megabytes while a
    # 512-pixel piece is in flight, and what binds is Windows' *commit* limit rather
    # than free RAM — with an editor open this machine had 5 GB of commit headroom
    # against 8 GB of free memory, and eight workers failed on a one-megabyte array.
    jobs = jobs or max(1, min(multiprocessing.cpu_count() // 4, 4))

    index = dict((r.id, r) for r in rows)
    out = []
    with multiprocessing.Pool(jobs) as pool:
        for pid, frames, w, h, scale, lift, alike in pool.imap(_one, rows, chunksize=1):
            images = [Image.frombytes("RGBA", size, blob) for blob, size in frames]
            out.append(Rendered(index[pid], images, w, h, scale, lift, alike))

    out.sort(key=lambda r: [x.id for x in rows].index(r.row.id))
    return out


# --------------------------------------------------------------------------- output
def paths_for(row):
    """Where a piece's facings are written.

    A one-facing piece is a single PNG at `Homestead/<id>.png`, which is what every
    piece in the grove was before this and what `AssetManifest` addresses. A four-facing
    piece is a folder of `f0..f3`, addressed the same way a flipbook's frames are —
    so the loader needs nothing new to find them.
    """
    if row.facings == 1:
        return [os.path.join(ART, row.id + ".png")]
    return [os.path.join(ART, row.id, "f%d.png" % k) for k in range(row.facings)]


def write(rendered, dry=False):
    """Writes a piece's facings, and says whether anything actually changed."""
    changed = 0
    for path, image in zip(paths_for(rendered.row), rendered.images):
        blob = io.BytesIO()
        image.save(blob, "PNG", optimize=True)
        blob = blob.getvalue()

        if os.path.isfile(path) and open(path, "rb").read() == blob:
            continue
        changed += 1
        if not dry:
            folder = os.path.dirname(path)
            if not os.path.isdir(folder):
                os.makedirs(folder)
            with open(path, "wb") as f:
                f.write(blob)
    return changed


# --------------------------------------------------------------------------- survey
# ---------------------------------------------------------------------- the floor
#: How deep the tile's side wall is, as a fraction of its own side. The floor is drawn as
#: blocks rather than flat lozenges — `GroveTileArt.LayGround` hangs the sprite by half its
#: skirt so the *top face* lands on the tile's point — and a skirt is what makes the ground
#: read as ground rather than as a sheet of paper.
FLOOR_SKIRT = 0.28

#: Which model the floor borrows its colours from. The tile itself is *built* rather than
#: cut, because the pack's ground is hexagonal and a hexagon cannot tile a square grid; but
#: its paint is the pack's own, sampled through this model's UVs, so the floor and the
#: things standing on it come from one hand.
FLOOR_PALETTE = "hex/tiles/base/hex_grass"

# How much brighter the ground is drawn than the pack painted it, how much of its colour is
# handed back afterwards, and how far its hue is turned.
#
# The grass this borrows is drawn to be seen in the pack's own daylight, and on this floor it
# read as too dark a green. It cannot be fixed anywhere else: the tile is drawn at
# `Color.white` and `Image.color` is a multiply, so a run-time tint can only ever take light
# away (invariant 37l). So it is graded here, once, into the picture that ships.
#
# **Affine on luminance rather than a gamma**, which is `make_siege_ground`'s own finding from
# the other direction: a gamma lifts the shadows with everything else and the whole tile goes
# pastel, where a straight scale keeps the relation between the lit top and the earth skirt.
# And the chroma is multiplied back, because lifting luminance divides apparent saturation by
# the same factor — brightening alone is what *makes* a green washy.
#
# **And the hue is turned, which is the part brightness could never do.** The pack's grass is
# a *yellow* green — hue about 62 degrees, which is olive — and the sun added above pushes it
# further that way, because warm light on a yellow-green is more yellow still. Lifting and
# saturating an olive gives a brighter olive, so the complaint ("make the floor a brighter
# green") is answered by moving the hue as well as the level: `FLOOR_HUE` is where the grass
# lands and `FLOOR_TURN` is what share of the way it goes. It is turned rather than replaced
# so the tile keeps the pack's own spread between its lit top and its darker walls — a floor
# painted one flat colour stops reading as blocks and goes back to being a sheet of paper,
# which is what `FLOOR_SKIRT` exists to prevent.
FLOOR_LIFT = 1.26
FLOOR_CHROMA = 1.22
FLOOR_HUE = 98.0
FLOOR_TURN = 0.72


def _grade(image, lift, chroma):
    """Lifts an image's luminance and multiplies its chroma back. Alpha is untouched.

    The one grade both the ground and every piece run through, because they are two halves
    of one picture and a floor graded on its own would be a lawn somebody else's village is
    standing on. Neither the size nor the alpha moves, so every measurement a caller makes
    afterwards — the one-tile scale, the top face's share of the width, a piece's box and
    its hit mask — reads exactly what it would have read without it.
    """
    rgba = np.asarray(image.convert("RGBA")).astype(np.float32)
    rgb = rgba[..., :3]

    # Rec. 601, which is what the eye weights these three at.
    luma = (rgb * np.array([0.299, 0.587, 0.114], np.float32)).sum(axis=2, keepdims=True)
    lit = np.clip(luma * lift + (rgb - luma) * chroma, 0.0, 255.0)

    out = rgba.copy()
    out[..., :3] = lit
    return Image.fromarray(out.astype(np.uint8), "RGBA")


def _turn(image, hue, share):
    """Turns every colour in `image` `share` of the way toward `hue`, keeping S and V.

    By the shortest way round the wheel, which is the only definition that does not
    occasionally send a colour the long way through red on its way from yellow to green
    (invariant 37p's blend, arrived at for the same reason).

    Grey is left where it is: a pixel with no saturation has no hue to turn, and turning
    one anyway is how a white highlight comes out tinted. Alpha and size are untouched,
    for the reason `_grade` gives.
    """
    rgba = np.asarray(image.convert("RGBA")).astype(np.float32) / 255.0
    rgb = rgba[..., :3]

    hi = rgb.max(axis=2)
    lo = rgb.min(axis=2)
    span = hi - lo

    safe = np.maximum(span, 1e-6)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.where(hi == r, (g - b) / safe % 6.0,
                 np.where(hi == g, (b - r) / safe + 2.0, (r - g) / safe + 4.0)) * 60.0

    # The shortest arc, then a share of it.
    delta = (hue - h + 540.0) % 360.0 - 180.0
    h = (h + delta * share * np.where(span > 1e-6, 1.0, 0.0)) % 360.0

    # Back to RGB at the same saturation and value, which is what "turn" means here.
    c = span
    x = c * (1.0 - np.abs((h / 60.0) % 2.0 - 1.0))
    m = lo
    sector = (h / 60.0).astype(np.int32) % 6
    zero = np.zeros_like(c)
    table = [(c, x, zero), (x, c, zero), (zero, c, x),
             (zero, x, c), (x, zero, c), (c, zero, x)]
    out = np.zeros_like(rgb)
    for k, (rr, gg, bb) in enumerate(table):
        hit = sector == k
        out[..., 0] = np.where(hit, rr, out[..., 0])
        out[..., 1] = np.where(hit, gg, out[..., 1])
        out[..., 2] = np.where(hit, bb, out[..., 2])
    out += m[..., None]

    rgba[..., :3] = np.clip(out, 0.0, 1.0)
    return Image.fromarray(np.rint(rgba * 255.0).astype(np.uint8), "RGBA")


def floor(dry=False):
    """Builds the grove's ground tile and writes it, and pins the world-to-screen scale.

    **Built rather than cut, for one reason that admits no workaround.** Every ground tile
    in this pack is a hexagon, and a hexagon does not tile a square grid — laid on one it
    leaves gaps no amount of overlap closes. What the pack *can* supply is the paint, so
    the geometry is a square prism of the grid's own size and the colours are sampled
    through `hex_grass`'s own UVs: the same green on top and the same earth down the side
    that every other piece in the catalogue was drawn beside.

    **And it is the calibration.** A one-tile prism must draw at exactly
    `GroveFloor.TileWidth`, so this asserts that the scale it derives is 1. Nothing else
    could catch a wrong world-to-screen conversion: it is a constant factor, so every piece
    comes out too big *together* and they still agree with each other — they simply stop
    agreeing with the ground. See `Pack.tile_on_screen`.
    """
    pack = roster.PACKS["hex"]
    u = pack.units_per_tile
    source = os.path.join(roster.VENDOR, FLOOR_PALETTE.replace("/", os.sep) + ".obj")
    if not os.path.isfile(source):
        sys.exit("the floor borrows its colours from %s, which is not vendored" % FLOOR_PALETTE)

    donor = kaykit.load(source)
    tex = kaykit.texture(source, donor.mtllib)
    top_uv, side_uv = _palette(donor)

    mesh = _prism(u, u * FLOOR_SKIRT, top_uv, side_uv)
    extent = kaykit.Extent.over(mesh, PITCH, YAWS[:1])

    box = int(round(extent.span / u * PX_PER_TILE))
    # Not culled. The prism is five quads with no interior to hide, so culling buys
    # nothing — and it is the one mesh here whose winding is written by hand rather than
    # exported, so a back face is exactly as likely as a front one. It was culled once and
    # the *top* went, leaving a silhouette two thirds the right height with every other
    # number still plausible.
    buf = kaykit.render(mesh, tex, PITCH, YAWS[0], box, extent,
                        super_sample=SUPER, light=KEY, ambient=AMBIENT, rim=0.0, cull=False,
                        sun=SUN, shadow=SHADE)

    image = kaykit.to_image(buf)
    crop = image.getbbox()
    image = image.crop(crop)
    # Turned first and then lifted: turning is a pure hue move that keeps S and V, so it
    # cannot undo the level, where lifting first would be grading a colour that is about to
    # be replaced.
    image = _grade(_turn(image, FLOOR_HUE, FLOOR_TURN), FLOOR_LIFT, FLOOR_CHROMA)

    # The assertion this function exists for. A tile's own art must draw at one.
    scale = (extent.span / float(box)) / pack.tile_on_screen * TILE_WIDTH / PIECE_SCALE
    drawn = image.width * scale * PIECE_SCALE
    if abs(drawn - TILE_WIDTH) > 1.0:
        sys.exit("a one-tile prism draws at %.1f where GroveFloor.TileWidth is %.0f — the "
                 "world-to-screen conversion is wrong, and every piece in the grove is that "
                 "factor out against the ground. See Pack.tile_on_screen."
                 % (drawn, TILE_WIDTH))

    # And the projection the build gate measures off the alpha: the widest row is the
    # tile's horizontal axis, and twice the distance to it from the top is the face. The
    # arithmetic is `ContentValidation.CheckTileProjection`'s, to the threshold and to the
    # rounding — a mirror that measured the same picture a slightly different way would
    # pass here and fail the build, which is worse than not checking.
    alpha = np.asarray(image.convert("RGBA"))[..., 3] > int(.06 * 255)
    counts = alpha.sum(axis=1)
    widest = int(np.argmax(counts))
    ratio = (widest * 2.0) / image.width
    if abs(ratio - TILE_FACE_RATIO) > 0.01:
        sys.exit("the tile's top face measures %.4f of its width against "
                 "GroveFloor.TileFaceRatio %.4f; the floor will not tessellate"
                 % (ratio, TILE_FACE_RATIO))

    path = os.path.join(ART, "floor_grass.png")
    blob = io.BytesIO()
    image.save(blob, "PNG", optimize=True)
    blob = blob.getvalue()
    same = os.path.isfile(path) and open(path, "rb").read() == blob
    if not (dry or same):
        with open(path, "wb") as f:
            f.write(blob)

    print("floor_grass %dx%d, top face %.4f of its width, draws at %.1f (tile is %.0f)%s"
          % (image.width, image.height, ratio, drawn, TILE_WIDTH,
             "" if not same else " — unchanged"))
    return 0 if same else 1


def _palette(mesh):
    """The donor's top-face and side-wall texture coordinates.

    Picked by geometry rather than by a hard-coded pair of numbers: the triangle whose
    normal points most nearly up is the top face, and the one pointing most nearly
    sideways is the wall. A pair of typed UVs would be two numbers describing a picture,
    which is the class of fact this whole tool exists to stop anybody typing.
    """
    best_up, best_side = None, None
    up_score, side_score = -2.0, -2.0

    for (ia, ta, _), (ib, tb, _), (ic, tc, _) in mesh.tris:
        if ta < 0:
            continue
        normal = np.cross(mesh.verts[ib] - mesh.verts[ia], mesh.verts[ic] - mesh.verts[ia])
        length = np.linalg.norm(normal)
        if length < 1e-12:
            continue
        normal = normal / length

        centroid = (mesh.uvs[ta] + mesh.uvs[tb] + mesh.uvs[tc]) / 3.0
        if normal[1] > up_score:
            up_score, best_up = normal[1], centroid
        flat = abs(normal[1])
        if -flat > side_score:
            side_score, best_side = -flat, centroid

    if best_up is None or best_side is None:
        sys.exit("could not read a palette off %s" % FLOOR_PALETTE)
    return best_up, best_side


def _prism(side, depth, top_uv, side_uv):
    """A square block one tile across, its top face on y=0 and its skirt hanging below.

    The top face is on y=0 rather than centred because that is where every other model in
    this catalogue stands, so the tile and the things on it are measured against one
    ground plane.
    """
    h = side * 0.5
    verts = [
        (-h, 0.0, -h), (h, 0.0, -h), (h, 0.0, h), (-h, 0.0, h),               # top
        (-h, -depth, -h), (h, -depth, -h), (h, -depth, h), (-h, -depth, h),   # bottom
    ]
    uvs = [tuple(top_uv), tuple(side_uv)]

    quads = [
        ((0, 1, 2, 3), 0),          # top
        ((3, 2, 6, 7), 1),          # +z wall
        ((2, 1, 5, 6), 1),          # +x wall
        ((1, 0, 4, 5), 1),          # -z wall
        ((0, 3, 7, 4), 1),          # -x wall
    ]

    tris = []
    for (a, b, c, d), uv in quads:
        tris.append(((a, uv, -1), (b, uv, -1), (c, uv, -1)))
        tris.append(((a, uv, -1), (c, uv, -1), (d, uv, -1)))

    return kaykit.Mesh(np.array(verts, np.float64), np.array(uvs, np.float64),
                       np.zeros((0, 3)), tris, None)


def survey(bundle, key, out_dir):
    """Every model in a pack, rendered at the grove's own projection, onto sheets.

    The reason this exists is the reason `Survey Projectile Pack` exists: a pack's file
    names say a family and its thumbnails are grey cubes, so the only way to *choose*
    from 263 models is to look at all of them at the size they will be drawn. It is not
    part of a build; it is how a roster row gets written.
    """
    pack = roster.PACKS[key]
    folder = os.path.join(bundle, pack.path.replace("/", os.sep))
    if not os.path.isdir(folder):
        sys.exit("no such pack folder: %s" % folder)

    models = []
    for base, _, files in os.walk(folder):
        for name in sorted(files):
            if name.endswith(".obj"):
                models.append(os.path.join(base, name))
    if not models:
        sys.exit("no models under %s" % folder)

    cell, across = 168, 8
    rows = (len(models) + across - 1) // across
    sheet = Image.new("RGBA", (cell * across, (cell + 16) * rows), (34, 38, 52, 255))

    from PIL import ImageDraw
    draw = ImageDraw.Draw(sheet)

    for i, path in enumerate(models):
        mesh = kaykit.load(path)
        if not len(mesh):
            continue
        tex = kaykit.texture(path, mesh.mtllib)
        extent = kaykit.Extent.over(mesh, PITCH, YAWS[:1], pad=PAD)
        buf = kaykit.render(mesh, tex, PITCH, YAWS[0], cell, extent,
                            super_sample=2, light=KEY, ambient=AMBIENT, rim=RIM)

        x, y = (i % across) * cell, (i // across) * (cell + 16)
        sheet.alpha_composite(kaykit.to_image(buf), (x, y))

        size = mesh.verts.max(0) - mesh.verts.min(0)
        label = "%s %.1fx%.1f" % (os.path.basename(path)[:-4][:22],
                                  size[0] / pack.units_per_tile,
                                  size[2] / pack.units_per_tile)
        draw.text((x + 3, y + cell + 2), label, fill=(214, 220, 234, 255))

    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)
    where = os.path.join(out_dir, "survey_%s.png" % key)
    sheet.save(where)
    print("%d model(s) -> %s" % (len(models), where))


# --------------------------------------------------------------------------- vendor
def vendor(bundle, rows):
    """Copies the models the roster names into the repo, with their atlases.

    Only what is named, so the repo holds its dependencies and not a bundle. A model
    brings its `.mtl` and the atlas that `.mtl` points at, because `kaykit.texture`
    resolves relative to the model — which is what lets the hexagon pack's five team
    colours be five folders of the same model names.
    """
    copied = skipped = 0
    for row in rows:
        for src, dst in zip(row.sources(bundle), row.vendored()):
            if not os.path.isfile(src):
                sys.exit("missing model: %s (row %d, '%s')" % (src, row.line, row.id))

            folder = os.path.dirname(dst)
            if not os.path.isdir(folder):
                os.makedirs(folder)

            for ext in (".obj", ".mtl"):
                a, b = os.path.splitext(src)[0] + ext, os.path.splitext(dst)[0] + ext
                if not os.path.isfile(a):
                    continue
                if _same(a, b):
                    skipped += 1
                else:
                    shutil.copyfile(a, b)
                    copied += 1

            tex_src = _atlas_beside(src)
            tex_dst = os.path.join(folder, os.path.basename(tex_src))
            if not _same(tex_src, tex_dst):
                shutil.copyfile(tex_src, tex_dst)
                copied += 1

    credits = os.path.join(roster.VENDOR, "CREDITS.txt")
    if not os.path.isdir(roster.VENDOR):
        os.makedirs(roster.VENDOR)
    with io.open(credits, "w", encoding="utf-8", newline="\n") as f:
        f.write("The grove's pieces are rendered from these packs. All CC0.\n\n")
        for key in sorted(roster.PACKS):
            f.write("  %-8s %s\n" % (key, roster.PACKS[key].credit))
        f.write("\nOnly the models Tools/grove_pieces.tsv names are kept here; "
                "`make_grove_art.py --vendor --source <bundle>` restores them.\n")

    print("vendored %d file(s), %d already present" % (copied, skipped))


def _atlas_beside(obj_path):
    """The atlas a model needs, through the one resolver that knows how (`kaykit`)."""
    return kaykit.atlas_path(obj_path, kaykit.load(obj_path).mtllib)


def _same(a, b):
    if not (os.path.isfile(a) and os.path.isfile(b)):
        return False
    return open(a, "rb").read() == open(b, "rb").read()


# -------------------------------------------------------------------------- contact
def contact(done, out_dir):
    """Every shipped piece at the size a phone draws it, four facings deep.

    The gate `--check` gives is reproducibility, which says nothing at all about whether
    a piece reads — that distinction has cost this project four broken shop cards and
    five illegible goals. This is the half that needs an eye.
    """
    from PIL import ImageDraw
    cell = 148
    across = 10
    rows = (len(done) + across - 1) // across
    deep = max(len(d.images) for d in done)
    sheet = Image.new("RGBA", (cell * across, (cell * deep + 16) * rows), (34, 38, 52, 255))
    draw = ImageDraw.Draw(sheet)

    for i, r in enumerate(done):
        x, y = (i % across) * cell, (i // across) * (cell * deep + 16)
        for k, image in enumerate(r.images):
            fit = image.copy()
            fit.thumbnail((cell, cell), Image.LANCZOS)
            sheet.alpha_composite(fit, (x + (cell - fit.width) // 2,
                                        y + k * cell + (cell - fit.height) // 2))
        draw.text((x + 3, y + cell * deep + 2),
                  "%s %dx%d" % (r.row.id[:18], r.row.cols, r.row.rows),
                  fill=(214, 220, 234, 255))

    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)
    where = os.path.join(out_dir, "grove_contact.png")
    sheet.save(where)
    print("%d piece(s) -> %s" % (len(done), where))


# ----------------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", help="an unpacked KayKit bundle; needed by --survey and --vendor")
    ap.add_argument("--survey", metavar="PACK", help="render a whole pack onto sheets and stop")
    ap.add_argument("--vendor", action="store_true", help="copy the roster's models into the repo")
    ap.add_argument("--floor", action="store_true", help="build the ground tile and stop")
    ap.add_argument("--check", action="store_true", help="prove the shipped art is what this writes")
    ap.add_argument("--contact", action="store_true", help="write a sheet to look at")
    ap.add_argument("--only", help="comma separated piece ids, for a quick look")
    ap.add_argument("--jobs", type=int, default=0,
                    help="worker processes; 0 picks one per core, 1 stays in-process")
    ap.add_argument("--out", default=OUT)
    args = ap.parse_args()

    if args.survey:
        if not args.source:
            sys.exit("--survey needs --source")
        return survey(args.source, args.survey, args.out)

    if args.floor:
        stale = floor(dry=args.check)
        if args.check and stale:
            sys.exit("the floor tile on disk is not what this tool builds. Re-run it.")
        return

    rows = roster.everything()
    if args.only:
        want = set(args.only.split(","))
        rows = [r for r in rows if r.id in want]
        if not rows:
            sys.exit("--only matched nothing")

    if args.vendor:
        if not args.source:
            sys.exit("--vendor needs --source")
        return vendor(args.source, rows)

    missing = [r for r in rows if not all(os.path.isfile(p) for p in r.vendored())]
    if missing:
        sys.exit("%d piece(s) have models that are not vendored, starting with '%s'. Run:\n"
                 "  python Tools/make_grove_art.py --vendor --source <bundle>"
                 % (len(missing), missing[0].id))

    done = render_all(rows, args.jobs)
    changed = sum(write(r, dry=args.check) for r in done)

    # The ground, built rather than rendered from a roster row, and part of every run so
    # that `--check` covers it: a floor that is not what this tool builds is a floor that
    # will not tessellate, and it is the one picture in the grove nothing else looks at.
    if not args.only:
        changed += floor(dry=args.check)

    if args.check:
        if changed:
            sys.exit("%d file(s) on disk differ from what this tool renders. Re-run it."
                     % changed)
        print("%d piece(s), %d file(s): all match" % (len(done), sum(len(d.images) for d in done)))
    else:
        print("%d piece(s), %d file(s) written, %d unchanged"
              % (len(done), changed, sum(len(d.images) for d in done) - changed))

    big = max(done, key=lambda d: max(d.w, d.h))
    print("   largest: %s at %dx%d (cap %d)" % (big.row.id, big.w, big.h, MAX_PX))

    alike = [d.row.id for d in done if d.alike]
    if alike:
        print("   %d piece(s) render identically at every facing and should ask for 1: %s"
              % (len(alike), ", ".join(alike)))

    if args.contact:
        contact(done, args.out)


if __name__ == "__main__":
    main()
