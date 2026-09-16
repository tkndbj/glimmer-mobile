# -*- coding: utf-8 -*-
"""Rasterises a Wavefront OBJ orthographically, on the CPU, with no Unity and no GPU.

WHY THIS EXISTS AND WHY IT IS NOT AN EDITOR TOOL. Every art tool in this project is
offline Python with a `--check` that proves what is on disk is what the tool writes;
the single exception is the siege projectile bake, and it is an exception for one
stated reason — no Python script can rasterise a particle system. A *static mesh* is
not that. It is a few hundred triangles, one texture atlas and a projection, so
rendering it here means the grove's whole catalogue can be rebuilt on a checkout with
no Editor open, gated by the same offline check as everything else, and reproduced
byte for byte by anybody. An Editor bake would have bought nothing and cost the gate.

WHAT IT IS DELIBERATELY NOT. It is not a renderer. There is no shadowing, no
transparency, no reflection and no anti-aliasing beyond supersampling, because the
thing being drawn is a flat-shaded low-poly model whose own art direction is exactly
that. Anything more would be inventing light the pack's author did not draw.

THREE RULES IT ENFORCES, each of which is a mistake this project has already made
once somewhere else:

  * **One extent across every facing.** A model rendered four times and trimmed four
    times comes out at four sizes, so a piece changes size when the player turns it.
    `Extent.over` measures the union and every facing is drawn into it — which is
    `make_siege_art.one_canvas`'s rule, arrived at from the same direction.
  * **Nearest-neighbour texture sampling.** These atlases are *palette grids*: flat
    blocks of colour packed a few pixels apart. Bilinear sampling across a block
    boundary invents a colour that is in neither block, which reads as a coloured
    fringe along every UV seam.
  * **Premultiply before downsampling.** The colour under a transparent pixel is
    black, so resizing straight alpha averages real colour with black and draws a
    dark rim around everything. It is invisible at full size and unmistakable at the
    size a phone draws a grove piece.
"""
import math
import os

import numpy as np
from PIL import Image


# --------------------------------------------------------------------------- mesh
class Mesh(object):
    """Positions, texture coordinates, normals and triangles, as a model file holds them.

    Triangles are `(vertex, uv, normal)` index triples with -1 for absent, which is what
    an OBJ face line can legally say. Faces with more than three corners are
    fan-triangulated at load, because a rasteriser wants triangles and nothing else here
    cares about the original polygons.
    """

    __slots__ = ("verts", "uvs", "norms", "tris", "mtllib")

    def __init__(self, verts, uvs, norms, tris, mtllib):
        self.verts, self.uvs, self.norms, self.tris, self.mtllib = verts, uvs, norms, tris, mtllib

    def __len__(self):
        return len(self.tris)


def load(path):
    """Reads one OBJ file. Only the statements these packs actually use are understood."""
    v, vt, vn, tris = [], [], [], []
    mtllib = None
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            part = line.split()
            if not part:
                continue
            head = part[0]
            if head == "v":
                v.append((float(part[1]), float(part[2]), float(part[3])))
            elif head == "vt":
                vt.append((float(part[1]), float(part[2])))
            elif head == "vn":
                vn.append((float(part[1]), float(part[2]), float(part[3])))
            elif head == "mtllib":
                mtllib = part[1]
            elif head == "f":
                corner = []
                for token in part[1:]:
                    bits = (token.split("/") + ["", ""])[:3]
                    corner.append(tuple(int(b) - 1 if b else -1 for b in bits))
                for k in range(1, len(corner) - 1):
                    tris.append((corner[0], corner[k], corner[k + 1]))

    return Mesh(np.array(v, np.float64),
                np.array(vt, np.float64) if vt else np.zeros((0, 2)),
                np.array(vn, np.float64) if vn else np.zeros((0, 3)),
                tris, mtllib)


def atlas_path(obj_path, mtllib=None):
    """Where an OBJ's texture atlas is.

    Resolved relative to the OBJ rather than to the pack root, because every one of
    these packs ships one copy of its atlas per folder — so a model always finds the
    palette its own folder was exported with, and two folders of the same pack may
    legitimately differ (the hexagon buildings ship one atlas per team colour).

    The last resort is *the only PNG in the folder* rather than a name typed here.
    A name would be one pack's name, so the first model in the second pack whose `.mtl`
    happened to be missing looked for the first pack's atlas and reported "no texture"
    for a file sitting beside it. Every one of these folders holds exactly one atlas,
    which is a stronger thing to rely on than a spelling.
    """
    folder = os.path.dirname(obj_path)

    for candidate in (mtllib, os.path.basename(os.path.splitext(obj_path)[0] + ".mtl")):
        if not candidate:
            continue
        mtl = os.path.join(folder, candidate)
        if not os.path.isfile(mtl):
            continue
        with open(mtl, encoding="utf-8", errors="replace") as f:
            for line in f:
                if line.startswith("map_Kd"):
                    name = line.split(None, 1)[1].strip().replace("\\", "/").split("/")[-1]
                    path = os.path.join(folder, name)
                    if os.path.isfile(path):
                        return path

    pngs = sorted(n for n in os.listdir(folder) if n.lower().endswith(".png"))
    if len(pngs) == 1:
        return os.path.join(folder, pngs[0])
    raise IOError("no texture for %s (folder holds %d PNGs)" % (obj_path, len(pngs)))


def texture(obj_path, mtllib=None):
    """The atlas an OBJ's material points at, as RGB. See :func:`atlas_path`."""
    return np.array(Image.open(atlas_path(obj_path, mtllib)).convert("RGB"))


def merge(meshes):
    """Several models as one, re-indexed.

    These packs export a hierarchy as one file per object, so a tower's roof, a
    watermill's wheel and a windmill's sails are all separate models that only mean
    anything standing on their parent — a mill rendered alone is a mill with no sails.
    Which parts belong together is **named in the roster** rather than guessed from the
    filenames, because the naming does not support guessing: `tree_single_A_cut` is a
    stump and not a part of `tree_single_A`, and `fence_wood_straight_gate` is a gate
    and not a part of the fence beside it. One wrong guess welds two objects together
    for ever and the only thing that would notice is an eye.
    """
    meshes = [m for m in meshes if m is not None]
    if len(meshes) == 1:
        return meshes[0]

    verts, uvs, norms, tris = [], [], [], []
    for m in meshes:
        dv, dt, dn = len(verts), len(uvs), len(norms)
        verts.extend(m.verts.tolist())
        uvs.extend(m.uvs.tolist())
        norms.extend(m.norms.tolist())
        for tri in m.tris:
            tris.append(tuple((v + dv if v >= 0 else -1,
                               t + dt if t >= 0 else -1,
                               n + dn if n >= 0 else -1) for v, t, n in tri))

    return Mesh(np.array(verts, np.float64),
                np.array(uvs, np.float64) if uvs else np.zeros((0, 2)),
                np.array(norms, np.float64) if norms else np.zeros((0, 3)),
                tris, meshes[0].mtllib)


# ------------------------------------------------------------------------- camera
def basis(pitch_deg, yaw_deg):
    """The camera's right, up and view vectors for an orthographic look from above.

    The camera stands on the unit sphere at `(yaw, pitch)` and looks at the origin. The
    identity worth stating, because it is what pins `pitch` to the floor the pieces
    stand on: a unit square lying on the ground projects to a diamond whose height over
    its width is exactly `sin(pitch)`. So the pitch that agrees with a floor of face
    ratio r is `asin(r)` — there is no tuning in it, and a piece rendered at any other
    pitch stands at an angle its own ground denies.
    """
    p, a = math.radians(pitch_deg), math.radians(yaw_deg)
    sp, cp, sa, ca = math.sin(p), math.cos(p), math.sin(a), math.cos(a)
    view = np.array([sa * cp, sp, ca * cp])          # camera -> origin, negated
    right = np.array([ca, 0.0, -sa])
    up = np.array([-sa * sp, cp, -ca * sp])
    return right, up, view


class Extent(object):
    """The one box every facing of a model is drawn into.

    See the module docstring: measured over the union of the facings, so a piece is the
    same size whichever way it is turned. `pad` is a fraction of the larger side and
    exists so a keyline drawn later has somewhere to go.
    """

    __slots__ = ("cx", "cy", "span")

    def __init__(self, cx, cy, span):
        self.cx, self.cy, self.span = cx, cy, span

    @classmethod
    def over(cls, mesh, pitch, yaws, pad=0.0):
        lo_x = lo_y = float("inf")
        hi_x = hi_y = float("-inf")
        for yaw in yaws:
            right, up, _ = basis(pitch, yaw)
            sx, sy = mesh.verts @ right, mesh.verts @ up
            lo_x, hi_x = min(lo_x, sx.min()), max(hi_x, sx.max())
            lo_y, hi_y = min(lo_y, sy.min()), max(hi_y, sy.max())

        span = max(hi_x - lo_x, hi_y - lo_y)
        return cls((hi_x + lo_x) / 2.0, (hi_y + lo_y) / 2.0, span * (1.0 + pad))

    @property
    def aspect(self):
        """Width over height of what was measured. Always 1 — the box is square by
        construction, because a rotating piece sweeps a square envelope."""
        return 1.0


# ---------------------------------------------------------------------- rasteriser
def cullable(mesh, tex, pitch, yaws, probe=96):
    """Whether back-face culling this model changes nothing, **decided by measuring it**.

    Culling is worth about 1.7x and it is only safe on a solid: a windmill's sails, a
    flag's cloth and a blade of grass are single sheets, and culling one makes it vanish
    from the facings that see its back. Two structural tests were tried and neither
    separates the cases here — these models are open-bottomed, so `every edge shared by
    two triangles` says no to a church, and they are sparse, so `volume against bounding
    box` ranks a field of stumps below a sail.

    So it is not guessed at. The model is rendered small, both ways, at every facing it
    will be drawn at, and culled only if every pixel agreed. A sheet that disappears
    disappears at any resolution, and the test costs about one per cent of the render it
    is deciding about. **Any difference at all means no**, so the failure direction is
    a slower bake rather than a missing sail.
    """
    for yaw in yaws:
        extent = Extent.over(mesh, pitch, yaws)
        a = render(mesh, tex, pitch, yaw, probe, extent, super_sample=2, cull=False)
        b = render(mesh, tex, pitch, yaw, probe, extent, super_sample=2, cull=True)
        if not np.array_equal(a, b):
            return False
    return True


def render(mesh, tex, pitch, yaw, px, extent, super_sample=4,
           light=(-0.40, 0.85, -0.50), ambient=0.55, rim=0.0, cull=None,
           sun=None, shadow=None):
    """One facing, as a straight-alpha RGBA float array of shape (px, px, 4), 0..255.

    Pure numpy and free of any randomness, so two runs on two machines agree byte for
    byte — which is what lets `--check` be an exact comparison rather than a tolerance.

    `cull` drops back faces. It defaults to off; :func:`cullable` is what decides
    whether a given model may have it turned on.

    **The light is two colours, and by default they are the two greys it always was.**
    A face is lit by ``shadow + n.key * (sun - shadow)``, so `sun` is what a fully lit
    face is multiplied by and `shadow` is what a face turned away from the key gets. Leave both
    out and they are ``(1,1,1)`` and ``(ambient,)*3``, which is exactly the scalar
    ``ambient + (1 - ambient) * n.key`` this used to compute — so nothing that does not
    ask for a colour can move. Ask for one and the key becomes a *sun*: warm where it
    lands, cool where it does not, which is the one thing a single grey cannot say and
    the reason a flat-shaded pack renders as a diagram rather than as a place.

    The pack's author drew no such light (see the module docstring), and this is the
    deliberate exception to that: the grove is a screen rather than a spritesheet, and it
    was asked to look like somewhere the sun reaches.
    """
    right, up, view = basis(pitch, yaw)
    verts = mesh.verts
    sx, sy, sz = verts @ right, verts @ up, verts @ view
    cull = bool(cull)

    size = px * super_sample
    scale = size / extent.span
    px_x = (sx - extent.cx) * scale + size / 2.0
    px_y = size / 2.0 - (sy - extent.cy) * scale

    colour = np.zeros((size, size, 3), np.float32)
    alpha = np.zeros((size, size), np.float32)
    # float32 rather than float64: a depth buffer at 4x supersample is the largest
    # allocation here, and these models span about two world units, which single
    # precision resolves a thousand times finer than one pixel of parallax.
    depth = np.full((size, size), -np.inf, np.float32)

    key = np.asarray(light, np.float64)
    key = key / np.linalg.norm(key)

    # The two ends of the light, always a colour and by default two greys — see above.
    # float32 because they are about to multiply a float32 texture sample per pixel and a
    # float64 scalar promotes the whole buffer (the NEP 50 trap this project has already
    # paid for once).
    lit = np.asarray(sun if sun is not None else (1.0, 1.0, 1.0), np.float32)
    dark = np.asarray(shadow if shadow is not None else (ambient,) * 3, np.float32)
    swing = lit - dark
    tex_h, tex_w = tex.shape[:2]
    has_uv, has_n = len(mesh.uvs) > 0, len(mesh.norms) > 0

    for tri in mesh.tris:
        (ia, ta, na), (ib, tb, nb), (ic, tc, nc) = tri

        x0, x1, x2 = px_x[ia], px_x[ib], px_x[ic]
        y0, y1, y2 = px_y[ia], px_y[ib], px_y[ic]
        area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
        if abs(area) < 1e-12 or (cull and area > 0.0):
            continue

        x_lo = int(max(0, math.floor(min(x0, x1, x2))))
        x_hi = int(min(size - 1, math.ceil(max(x0, x1, x2))))
        y_lo = int(max(0, math.floor(min(y0, y1, y2))))
        y_hi = int(min(size - 1, math.ceil(max(y0, y1, y2))))
        if x_lo > x_hi or y_lo > y_hi:
            continue

        # Broadcast rather than meshgrid: the same arithmetic without materialising two
        # full grids per triangle, which on a 2048-square frame is most of the traffic.
        gx = (np.arange(x_lo, x_hi + 1) + 0.5)[None, :]
        gy = (np.arange(y_lo, y_hi + 1) + 0.5)[:, None]

        w0 = ((x1 - gx) * (y2 - gy) - (x2 - gx) * (y1 - gy)) / area
        w1 = ((x2 - gx) * (y0 - gy) - (x0 - gx) * (y2 - gy)) / area
        w2 = 1.0 - w0 - w1
        covered = (w0 >= 0.0) & (w1 >= 0.0) & (w2 >= 0.0)
        if not covered.any():
            continue

        z = w0 * sz[ia] + w1 * sz[ib] + w2 * sz[ic]
        near = depth[y_lo:y_hi + 1, x_lo:x_hi + 1]
        win = covered & (z > near)
        if not win.any():
            continue

        # Shading. **A flat triangle is shaded by one colour, not by a buffer of them**,
        # and that is not a micro-optimisation: a low-poly pack is flat-shaded almost everywhere,
        # and one wall of a church covers a quarter of a 2048-square frame — so the
        # per-pixel normal buffer for a single triangle ran to tens of megabytes and the
        # bake died of it on a machine with seven gigabytes free. Interpolating is kept
        # for the rare triangle whose corner normals really do differ.
        if has_n and na >= 0:
            n0, n1, n2 = mesh.norms[na], mesh.norms[nb], mesh.norms[nc]
            smooth = not (np.array_equal(n0, n1) and np.array_equal(n1, n2))
        else:
            n0 = n1 = n2 = np.cross(verts[ib] - verts[ia], verts[ic] - verts[ia])
            smooth = False

        # Both branches end holding a **colour** per shaded point rather than a number:
        # `(..., 3)` where the normals are interpolated and `(3,)` where the triangle is
        # flat. The rim is added as white to all three channels, because a rim is the sky
        # behind the object and not the sun.
        if smooth:
            n = (w0[..., None] * n0 + w1[..., None] * n1 + w2[..., None] * n2)
            n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-12)
            ndl = np.clip(n @ key, 0.0, 1.0).astype(np.float32)
            shade = dark + ndl[..., None] * swing
            if rim > 0.0:
                shade = shade + (rim * (1.0 - np.abs(n @ view)) ** 3).astype(np.float32)[..., None]
        else:
            n = n0 / max(float(np.linalg.norm(n0)), 1e-12)
            ndl = np.float32(min(max(float(n @ key), 0.0), 1.0))
            shade = dark + ndl * swing
            if rim > 0.0:
                # Light along the view axis falls off toward a silhouette, so `1 - |n.view|`
                # is brightest exactly at the rim. It lifts an edge off the ground behind it
                # without the keyline a flat render otherwise needs.
                shade = shade + np.float32(rim * (1.0 - abs(float(n @ view))) ** 3)

        # Sample and shade **only the pixels that won the depth test**. Doing the whole
        # bounding box and then selecting is the same picture and several times the
        # memory, which on the largest triangles is the difference between a bake that
        # runs and one that reports an out-of-memory a quarter of the way through.
        a0, a1, a2 = w0[win], w1[win], w2[win]

        if has_uv and ta >= 0:
            u = a0 * mesh.uvs[ta, 0] + a1 * mesh.uvs[tb, 0] + a2 * mesh.uvs[tc, 0]
            v = a0 * mesh.uvs[ta, 1] + a1 * mesh.uvs[tb, 1] + a2 * mesh.uvs[tc, 1]
            # Nearest, never bilinear — see the module docstring.
            tx = np.clip((u % 1.0) * tex_w, 0, tex_w - 1).astype(np.int32)
            ty = np.clip((1.0 - (v % 1.0)) * tex_h, 0, tex_h - 1).astype(np.int32)
            rgb = tex[ty, tx].astype(np.float32)
        else:
            rgb = np.full((a0.shape[0], 3), 180.0, np.float32)

        rgb *= shade[win] if smooth else shade

        colour[y_lo:y_hi + 1, x_lo:x_hi + 1][win] = rgb
        alpha[y_lo:y_hi + 1, x_lo:x_hi + 1][win] = 1.0
        near[win] = z[win]

    return downsample(colour, alpha, super_sample)


def downsample(colour, alpha, factor):
    """Box-filters a supersampled buffer down, in premultiplied space.

    See the module docstring: averaging straight alpha mixes real colour with the black
    under transparent pixels and rims everything in dark. Premultiplying first makes the
    average the one an image compositor would produce, and the colour is recovered only
    where there is alpha to recover it from.
    """
    if factor == 1:
        return np.concatenate([colour, alpha[..., None] * 255.0], axis=-1)

    size = colour.shape[0] // factor
    # Premultiplied **in place**. At four times supersampling the colour buffer for a
    # 512-pixel piece is fifty megabytes, and a copy of it here doubled the peak commit
    # of every worker — which on a machine already running an editor is the difference
    # between a bake that finishes and one that reports an out-of-memory on a one-megabyte
    # allocation. `colour` is the caller's own scratch and is not read again.
    colour *= alpha[..., None]
    pre = colour.reshape(size, factor, size, factor, 3).mean(axis=(1, 3))
    out_a = alpha.reshape(size, factor, size, factor).mean(axis=(1, 3))

    safe = np.maximum(out_a, 1e-6)[..., None]
    rgb = np.where(out_a[..., None] > 0.0, pre / safe, 0.0)
    return np.concatenate([rgb, out_a[..., None] * 255.0], axis=-1)


def to_image(buf):
    """A float RGBA buffer as a PIL image, rounded the one way both runs agree on."""
    return Image.fromarray(np.clip(np.rint(buf), 0, 255).astype(np.uint8), "RGBA")
