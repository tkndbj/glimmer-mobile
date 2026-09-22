"""Rig a name frame for the 2D Animation package, offline.

A name frame is one flattened painting (``Assets/Game/Art/Frames/<id>.png``) that the game draws
round a player's name and animates in place. Nothing about it is separate parts, so the only way
to move any of it is *mesh deformation*: a grid of vertices over the painting, a small skeleton,
and a weight per vertex saying how much of each bone it follows. The Skinning Editor authors
exactly that by hand; this tool writes the same data from a description of the painting, so a
frame is rigged the moment its PNG lands and the Editor never has to be opened to do it.

The output is ``Assets/Game/Editor/FrameRigs/<id>.rig.json`` -- **beside the Editor code rather
than beside the painting**, because every file under ``Assets/Game/Art/`` is given an
Addressables entry by the importer hook (``AddressableAddresses.TryAddressFor`` strips the
extension and asks nothing else), and a rig is a build-time instruction, not a thing the game
loads. ``FrameRigImport`` applies it on import through the package's own data providers, which
means the rig is a real 2D Animation rig: it opens in the Skinning Editor, can be refined there,
and the runtime reads it through ``Sprite.GetBones()`` like any other.

    python Tools/make_frame_rig.py            # write every rig whose description is below
    python Tools/make_frame_rig.py --check    # prove the committed rigs are what this writes
    python Tools/make_frame_rig.py --preview  # draw the head at its nod extremes, to look at

Coordinates in a description are **pixels of the painting, y down**, as you would read them off
an image editor; the rig converts to the sprite's own frame (pixels, y up, origin bottom-left),
and a child bone to its parent's frame, which is how the package stores a skeleton.

**What a preview can catch that nothing else can**: a bone whose weights reach into a part of
the painting that should stand still, which shows as the frame's bar bending when the head nods.
Look at ``--preview`` after touching a polygon.

The flat arrays (``vertices`` as x0,y0,x1,y1..., four bone slots per vertex) are the shape
``JsonUtility`` can read; it has no notion of a nested list.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FRAMES = os.path.join(ROOT, "Assets", "Game", "Art", "Frames")
RIGS = os.path.join(ROOT, "Assets", "Game", "Editor", "FrameRigs")

# Vertex spacing, in painting pixels. 24 over a 2172 x 724 painting is ~1,900 vertices, which is
# nothing for a skin of three bones, and fine enough that a nod bends smoothly across the neck.
CELL = 24

# How wide the blend band round a bone's polygon is, in pixels. Weight goes from 1 this far inside
# the edge to 0 this far outside it, so a nod never shears along a hard line.
FEATHER = 36

# The bone the runtime nods. A frame with no bone of this name stands still, which is allowed.
NOD_BONE = "head"


def smoothstep(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def signed_distance(poly, w, h):
    """Signed distance from every pixel to the polygon edge: negative inside, in pixels."""
    from scipy import ndimage
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).polygon(poly, fill=255)
    inside = np.array(mask) > 0
    d_in = ndimage.distance_transform_edt(inside)
    d_out = ndimage.distance_transform_edt(~inside)
    return np.where(inside, -d_in, d_out)


# --------------------------------------------------------------------------- the descriptions
#
# One entry per frame. A bone is (name, parent, head xy, tail xy) in painting pixels; its weight
# region is a polygon (also painting pixels), feathered by FEATHER. Vertices outside every region
# belong to the root, which never moves. Bones are listed parent-first.
#
# `eye` is where the eye glow sits, `hole` the clear box a name is laid out inside and `plate`
# the box a card's plate may fill behind the frame — its four edges entirely under paint, so the
# frame reads as the card's edge with nothing peeking out (measured by walking the alpha along
# each edge; a plate one pixel too tall shows a line of colour above the gold). All in painting
# pixels; `FrameCatalog` carries the same three as fractions, and `FrameTests` holds the copies
# together.
#
# dragon: the head nods about the neck. The polygon takes the horns and the mane (they are the
# head), stops short of the wing root at the top right (the wing belongs to the body) and fades
# out across the gold collar, which is where the neck really bends in the painting.
DESCRIPTIONS = {
    "dragon": {
        "bones": [
            ("root", None, (1086, 362), (1186, 362)),
            ("neck", "root", (330, 372), (330, 300)),
            ("head", "neck", (330, 300), (470, 200)),
        ],
        "regions": {
            "head": [
                (85, 45), (230, 26), (340, 34), (400, 66), (412, 150), (515, 172), (528, 235),
                (505, 300), (445, 330), (385, 348), (300, 366), (230, 360), (170, 336),
                (108, 300), (60, 232), (40, 150), (58, 92),
            ],
        },
        "eye": (363, 182),
        "hole": (640, 246, 2055, 522),
        "plate": (300, 190, 2085, 580),
    },
}


def build(spec, png):
    im = Image.open(png).convert("RGBA")
    w, h = im.size
    alpha = np.array(im)[..., 3] > 0

    # -- vertices: grid points whose cell touches any ink, one ring of cells beyond it
    from scipy import ndimage
    coverage = ndimage.maximum_filter(alpha, size=CELL * 2 + 1)
    xs = np.arange(0, w + CELL, CELL)
    ys = np.arange(0, h + CELL, CELL)
    xs[-1] = min(xs[-1], w)
    ys[-1] = min(ys[-1], h)
    index = -np.ones((len(ys), len(xs)), dtype=int)
    verts = []
    for j, y in enumerate(ys):
        for i, x in enumerate(xs):
            if coverage[min(y, h - 1), min(x, w - 1)]:
                index[j, i] = len(verts)
                verts.append((float(x), float(y)))
    verts = np.array(verts)

    tris = []
    for j in range(len(ys) - 1):
        for i in range(len(xs) - 1):
            a, b, c, d = index[j, i], index[j, i + 1], index[j + 1, i], index[j + 1, i + 1]
            if min(a, b, c, d) < 0:
                continue
            # Painting space is y down; the sprite is y up, so this winding is counter-clockwise
            # once flipped, which is the way Unity's sprite mesh faces.
            tris.append((a, c, b))
            tris.append((b, c, d))

    # -- weights: each region's feathered field, sampled at the vertices
    bones = spec["bones"]
    bone_index = {b[0]: k for k, b in enumerate(bones)}
    fields = {}
    for bone, poly in spec["regions"].items():
        sd = signed_distance(poly, w, h)
        fields[bone] = smoothstep((FEATHER - sd) / (2.0 * FEATHER))

    slots_index, slots_weight = [], []
    for x, y in verts:
        xi, yi = int(min(x, w - 1)), int(min(y, h - 1))
        parts = [(bone_index[b], float(f[yi, xi])) for b, f in fields.items()]
        parts = [(k, v) for k, v in parts if v > 1e-3]
        total = sum(v for _, v in parts)
        if total > 1.0:
            parts = [(k, v / total) for k, v in parts]
            total = 1.0
        parts.append((0, 1.0 - total))
        parts.sort(key=lambda kv: -kv[1])
        parts = (parts + [(0, 0.0)] * 4)[:4]
        # Rounded so that the four still sum to one exactly: the package warns on a sum under
        # 0.999, and four independently rounded figures can land at 0.9998.
        rounded = [round(v, 4) for _, v in parts]
        rounded[0] = round(1.0 - sum(rounded[1:]), 4)
        slots_index.extend(int(k) for k, _ in parts)
        slots_weight.extend(rounded)

    # -- to sprite space: pixels, y up, origin at the painting's bottom-left
    def up(p):
        return (float(p[0]), float(h - p[1]))

    world = {}
    out_bones = []
    for bname, parent, head, tail in bones:
        hx, hy = up(head)
        tx, ty = up(tail)
        angle = math.degrees(math.atan2(ty - hy, tx - hx))
        world[bname] = (hx, hy, angle)
        if parent is None:
            lx, ly, la = hx, hy, angle
        else:
            px, py, pa = world[parent]
            # A child is stored in its parent's frame: offset turned back by the parent's angle.
            dx, dy = hx - px, hy - py
            c, s = math.cos(math.radians(-pa)), math.sin(math.radians(-pa))
            lx, ly = c * dx - s * dy, s * dx + c * dy
            la = angle - pa
        out_bones.append({
            "name": bname,
            "parent": -1 if parent is None else bone_index[parent],
            "x": round(lx, 3), "y": round(ly, 3),
            "angle": round(la, 3),
            "length": round(math.hypot(tx - hx, ty - hy), 2),
        })

    ex, ey = up(spec["eye"])
    hx0, hy0 = up((spec["hole"][0], spec["hole"][3]))
    hx1, hy1 = up((spec["hole"][2], spec["hole"][1]))
    px0, py0 = up((spec["plate"][0], spec["plate"][3]))
    px1, py1 = up((spec["plate"][2], spec["plate"][1]))
    out = {
        "source": os.path.basename(png),
        "width": w, "height": h,
        "cell": CELL, "feather": FEATHER,
        "nodBone": NOD_BONE,
        "bones": out_bones,
        "vertices": [round(float(v), 1) for xy in verts for v in up(xy)],
        "triangles": [int(k) for t in tris for k in t],
        "boneIndex": slots_index,
        "boneWeight": slots_weight,
        "eye": [round(ex, 1), round(ey, 1)],
        "hole": [round(hx0, 1), round(hy0, 1), round(hx1, 1), round(hy1, 1)],
        "plate": [round(px0, 1), round(py0, 1), round(px1, 1), round(py1, 1)],
    }
    return out, fields


def preview(spec, png, fields, path, degrees=9.0):
    """Warp the painting by the head bone at the nod's reach either way, three sheets stacked."""
    from scipy import ndimage
    im = Image.open(png).convert("RGBA")
    w, h = im.size
    src = np.array(im)
    px, py = spec["bones"][1][2]  # the neck's head: the pivot the nod turns about
    field = fields[NOD_BONE]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    sheets = []
    for deg in (-degrees, 0.0, degrees):
        t = math.radians(deg)
        c, s = math.cos(t), math.sin(t)
        dx, dy = xx - px, yy - py
        rx, ry = px + c * dx - s * dy, py + s * dx + c * dy
        sx = xx + (xx - rx) * field
        sy = yy + (yy - ry) * field
        chans = [ndimage.map_coordinates(src[..., k], [sy, sx], order=1, mode="constant", cval=0)
                 for k in range(4)]
        sheets.append(Image.fromarray(np.stack(chans, -1).astype(np.uint8)).crop((0, 0, 1100, h)))
    sheet = Image.new("RGBA", (1100, h * 3 + 20), (40, 40, 48, 255))
    for k, s in enumerate(sheets):
        sheet.alpha_composite(s, (0, k * (h + 10)))
    d = ImageDraw.Draw(sheet)
    poly = spec["regions"][NOD_BONE]
    for k in range(3):
        oy = k * (h + 10)
        d.polygon([(x, y + oy) for x, y in poly], outline=(0, 255, 255, 160))
        d.ellipse((px - 5, py - 5 + oy, px + 5, py + 5 + oy), fill=(0, 255, 0, 255))
    sheet.save(path)


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--out", default=None, help="preview folder (default: Tools/out)")
    ap.add_argument("--degrees", type=float, default=9.0, help="how far the preview turns the head")
    args = ap.parse_args(argv)

    failed = 0
    for name, spec in DESCRIPTIONS.items():
        png = os.path.join(FRAMES, name + ".png")
        if not os.path.exists(png):
            print(f"{name}: no painting at {png}")
            failed += 1
            continue
        rig, fields = build(spec, png)
        target = os.path.join(RIGS, name + ".rig.json")
        text = json.dumps(rig, separators=(",", ":"))
        summary = (f"{len(rig['vertices']) // 2} vertices, {len(rig['triangles']) // 3} "
                   f"triangles, {len(rig['bones'])} bones")
        if args.preview:
            folder = args.out or os.path.join(ROOT, "Tools", "out")
            os.makedirs(folder, exist_ok=True)
            out = os.path.join(folder, name + "_rig_preview.png")
            preview(spec, png, fields, out, args.degrees)
            print(f"{name}: preview at {out}")
        elif args.check:
            have = open(target, encoding="utf-8").read() if os.path.exists(target) else None
            if have != text:
                print(f"{name}: {target} is not what this tool writes")
                failed += 1
            else:
                print(f"{name}: rig matches ({summary})")
        else:
            os.makedirs(RIGS, exist_ok=True)
            with open(target, "w", encoding="utf-8") as f:
                f.write(text)
            print(f"{name}: wrote {target} ({summary})")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
