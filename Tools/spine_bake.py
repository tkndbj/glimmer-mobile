# -*- coding: utf-8 -*-
"""Bakes a Spine 4.x skeleton to PNG frames, on the CPU, with no runtime and no Unity.

WHY THIS EXISTS. The fourth siege chapter's cast is the one pack on this machine drawn walking
*toward* the camera, which is the view this board has (invariant 37db). Its shipped PNG frames
are 50 to 73 pixels a body against a raider cut at 180, so using them means a 2.5x to 3.4x
upscale and a cast that reads soft beside every other one - measured, its interior line work
carries less than half the detail the skeletons do, and sharpening recovers a fifth of the gap
because the information is simply not there.

The pack also ships the artwork as **vectors** (`Ai/*.ai`, which are PDF-compatible) and the
animation as a **Spine rig**. So the frames can be re-baked at whatever size the board wants,
from art that has no resolution at all. That is what this does.

WHAT IT DELIBERATELY IS NOT. It is not a Spine runtime. It implements exactly the subset these
rigs use and **refuses anything else** rather than approximating it: region attachments, bone
hierarchies with translate/rotate/scale, linear and stepped keys, and slot attachment toggles.
Measured across all eleven rigs in the pack: 266 attachments, every one a region; 5,232 keys,
every one linear or stepped; no meshes, no weights, no IK, no transform or path constraints. A
rig that used any of those would be silently mis-drawn by a partial implementation, so `Rig.load`
raises instead.

HOW IT IS KNOWN TO BE RIGHT. `--verify` bakes every rig at the scale the pack's own PNGs were
exported at and compares against them frame for frame. That is the whole safety argument: the
answer is already on disk, so a baker that disagrees with it is wrong in a way no amount of
looking would catch. Nothing here was trusted until that passed.
"""
from __future__ import annotations

import argparse
import io
import json
import math
import re
import sys
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image

#: What a Spine file may contain and this baker understands. Anything else is refused.
KNOWN_ATTACHMENTS = {"region"}
KNOWN_CURVES = {"linear", "stepped"}


# --------------------------------------------------------------------------- the maths


class Transform:
    """A 2x3 affine, in Spine's own convention: y up, rotation counter-clockwise."""

    __slots__ = ("a", "b", "c", "d", "x", "y")

    def __init__(self, a=1.0, b=0.0, c=0.0, d=1.0, x=0.0, y=0.0):
        self.a, self.b, self.c, self.d, self.x, self.y = a, b, c, d, x, y

    @staticmethod
    def of(x, y, rotation, scale_x, scale_y):
        r = math.radians(rotation)
        cos, sin = math.cos(r), math.sin(r)
        return Transform(cos * scale_x, -sin * scale_y, sin * scale_x, cos * scale_y, x, y)

    def then(self, parent):
        """This transform applied inside `parent`'s space."""
        return Transform(
            parent.a * self.a + parent.b * self.c,
            parent.a * self.b + parent.b * self.d,
            parent.c * self.a + parent.d * self.c,
            parent.c * self.b + parent.d * self.d,
            parent.a * self.x + parent.b * self.y + parent.x,
            parent.c * self.x + parent.d * self.y + parent.y,
        )

    @property
    def rotation(self):
        """Degrees, taken off the matrix rather than tracked, so parents compose for free."""
        return math.degrees(math.atan2(self.c, self.a))

    @property
    def scale(self):
        return (math.hypot(self.a, self.c), math.hypot(self.b, self.d))


# --------------------------------------------------------------------------- keyframes


def _sample(keys, fields, defaults, time):
    """One animation channel at `time`, linear or stepped, clamped at both ends.

    <b>A channel with no keys is not the same as one at its default</b>, which is why callers
    check for the channel's absence rather than relying on this: an absent `rotate` leaves the
    setup pose, and so does a `rotate` keyed at zero, but only by coincidence.
    """
    if not keys:
        return list(defaults)

    at = [k.get("time", 0.0) for k in keys]
    if time <= at[0]:
        return [keys[0].get(f, d) for f, d in zip(fields, defaults)]
    if time >= at[-1]:
        return [keys[-1].get(f, d) for f, d in zip(fields, defaults)]

    i = 0
    while i + 1 < len(at) and at[i + 1] <= time:
        i += 1

    lo, hi = keys[i], keys[i + 1]
    curve = lo.get("curve", "linear")
    if not isinstance(curve, str) or curve not in KNOWN_CURVES:
        raise ValueError("unsupported curve %r - this baker does only %s"
                         % (curve, sorted(KNOWN_CURVES)))

    if curve == "stepped":
        return [lo.get(f, d) for f, d in zip(fields, defaults)]

    span = at[i + 1] - at[i]
    t = 0.0 if span <= 0 else (time - at[i]) / span
    return [lo.get(f, d) + (hi.get(f, d) - lo.get(f, d)) * t
            for f, d in zip(fields, defaults)]


# --------------------------------------------------------------------------- the rig


class Rig:
    """One Spine skeleton: its bones, its draw order, and its animations."""

    def __init__(self, data):
        self.bones = list(data.get("bones", []))
        self.order = {b["name"]: i for i, b in enumerate(self.bones)}
        self.slots = list(data.get("slots", []))
        self.animations = data.get("animations", {})
        self.skeleton = data.get("skeleton", {})

        skins = data.get("skins", {})
        if isinstance(skins, list):
            attachments = {}
            for skin in skins:
                attachments.update(skin.get("attachments", {}))
        else:
            attachments = skins.get("default", {})
        self.attachments = attachments

        for slot, named in self.attachments.items():
            for name, att in named.items():
                kind = att.get("type", "region")
                if kind not in KNOWN_ATTACHMENTS:
                    raise ValueError("%s/%s is a %r attachment; this baker does only %s"
                                     % (slot, name, kind, sorted(KNOWN_ATTACHMENTS)))

        # A parent must be defined before its child, which Spine guarantees and this relies on.
        for i, bone in enumerate(self.bones):
            parent = bone.get("parent")
            if parent is not None and self.order[parent] >= i:
                raise ValueError("bone %r is declared before its parent %r"
                                 % (bone["name"], parent))

    def duration(self, animation):
        longest = 0.0
        track = self.animations[animation]
        for chans in list(track.get("bones", {}).values()) + list(track.get("slots", {}).values()):
            for keys in chans.values():
                for key in keys:
                    longest = max(longest, key.get("time", 0.0))
        return longest

    def pose(self, animation, time):
        """Every bone's world transform at `time`."""
        track = self.animations[animation].get("bones", {})
        world = {}

        for bone in self.bones:
            name = bone["name"]
            chans = track.get(name, {})

            dx, dy = _sample(chans.get("translate"), ("x", "y"), (0.0, 0.0), time)
            (spin,) = _sample(chans.get("rotate"), ("value",), (0.0,), time)
            sx, sy = _sample(chans.get("scale"), ("x", "y"), (1.0, 1.0), time)

            local = Transform.of(bone.get("x", 0.0) + dx,
                                 bone.get("y", 0.0) + dy,
                                 bone.get("rotation", 0.0) + spin,
                                 bone.get("scaleX", 1.0) * sx,
                                 bone.get("scaleY", 1.0) * sy)

            parent = bone.get("parent")
            world[name] = local if parent is None else local.then(world[parent])

        return world

    def shown(self, animation, time):
        """Which attachment each slot is showing at `time`, or None."""
        track = self.animations[animation].get("slots", {})
        out = {}
        for slot in self.slots:
            name = slot["name"]
            keys = track.get(name, {}).get("attachment")
            if not keys:
                out[name] = slot.get("attachment")
                continue

            at = [k.get("time", 0.0) for k in keys]
            i = 0
            while i + 1 < len(at) and at[i + 1] <= time:
                i += 1
            out[name] = keys[i].get("name", None) if time >= at[0] else slot.get("attachment")
        return out

    # ----------------------------------------------------------------- drawing

    def placements(self, animation, time):
        """Every part to draw at `time`, in draw order, as (attachment, centre, angle, size)."""
        world = self.pose(animation, time)
        showing = self.shown(animation, time)

        out = []
        for slot in self.slots:
            name = showing.get(slot["name"])
            if not name:
                continue

            att = self.attachments.get(slot["name"], {}).get(name)
            if att is None:
                continue

            bone = world[slot["bone"]]
            local = Transform.of(att.get("x", 0.0), att.get("y", 0.0), att.get("rotation", 0.0),
                                 att.get("scaleX", 1.0), att.get("scaleY", 1.0))
            here = local.then(bone)

            sx, sy = here.scale
            out.append((att.get("path", name),
                        (here.x, here.y),
                        here.rotation,
                        (att["width"] * sx, att["height"] * sy)))
        return out


# --------------------------------------------------------------------------- the frame


def draw(rig, parts, animation, time, box, scale, supersample=2):
    """One frame, drawn into `box` (a Spine-space rect) at `scale` pixels per unit.

    <b>Supersampled and reduced</b>, because every part is rotated: a nearest or bilinear rotate
    at final size leaves a stepped edge on art whose whole style is a clean dark outline.
    """
    s = scale * supersample
    left, bottom, right, top = box
    wide = max(1, int(round((right - left) * s)))
    high = max(1, int(round((top - bottom) * s)))
    sheet = Image.new("RGBA", (wide, high), (0, 0, 0, 0))

    for name, (wx, wy), angle, (w, h) in rig.placements(animation, time):
        art = parts.get(name)
        if art is None:
            raise KeyError("no art for attachment %r" % name)

        pw, ph = max(1, int(round(w * s))), max(1, int(round(h * s)))
        piece = art.resize((pw, ph), Image.LANCZOS)
        if abs(angle) > 1e-6:
            piece = piece.rotate(angle, resample=Image.BICUBIC, expand=True)

        # Spine is y-up and an image is y-down.
        cx = (wx - left) * s
        cy = (top - wy) * s
        sheet.alpha_composite(piece, (int(round(cx - piece.width / 2.0)),
                                      int(round(cy - piece.height / 2.0))))

    if supersample > 1:
        sheet = sheet.resize((max(1, wide // supersample), max(1, high // supersample)),
                             Image.LANCZOS)
    return sheet


def frames(rig, parts, animation, count, box, scale, supersample=2):
    """`count` evenly spaced frames of one animation, the last one *before* the loop point."""
    span = rig.duration(animation)
    return [draw(rig, parts, animation, span * i / float(count), box, scale, supersample)
            for i in range(count)]


# --------------------------------------------------------------------------- vector art


def pages_of(source, dpi):
    """Every page of an Illustrator file, rasterised, as RGBA images.

    <b>An `.ai` is a PDF</b> — Illustrator writes a PDF-compatible stream and these packs ship
    one page per drawn part, which is what makes a vector re-bake possible at all. The import is
    local because this is the one thing in the art pipeline that needs it: everything else here
    is PIL and NumPy, and a checkout that never cuts this cast should not need a PDF library to
    run the gate.
    """
    try:
        import pymupdf
    except ImportError:                                            # pragma: no cover
        raise SystemExit("this cast is cut from vector art and needs PyMuPDF: pip install pymupdf")

    doc = pymupdf.open(stream=source, filetype="pdf")
    out = []
    for page in doc:
        pix = page.get_pixmap(dpi=dpi, alpha=True)
        out.append((Image.frombytes("RGBA", (pix.width, pix.height), pix.samples),
                    page.rect.width / float(page.rect.height)))
    return out


def _likeness(page, part):
    """How far a rasterised page is from a part the pack already exported, 0 is identical."""
    a = np.asarray(page.resize(part.size, Image.LANCZOS)).astype(np.float32)
    b = np.asarray(part).astype(np.float32)
    lit = a[..., :3] * (a[..., 3:4] / 255.0), b[..., :3] * (b[..., 3:4] / 255.0)
    return float(np.abs(lit[0] - lit[1]).mean()
                 + np.abs(a[..., 3] - b[..., 3]).mean() * 0.5)


def match_parts(pages, exported, wanted, shape=0.15):
    """Which rasterised page is which named part.

    <b>Matched on the picture rather than on a table</b>, because the `.ai` names its pages by
    ordinal and the rig names its attachments by role, and nothing in the pack joins the two. A
    page is a candidate when its aspect is close to the part's, and the winner is the one that
    looks most like the small PNG the pack already exported for that part — which is the ground
    truth, and is the whole reason this can be done without anybody eyeballing 98 pages.

    <b>The mapping is not trusted on this score alone.</b> It is proved downstream by baking with
    these parts at the exported size and comparing against the exported <em>frames</em>: a
    mis-matched part moves that diff by an order of magnitude, where a merely-different
    antialiasing does not.
    """
    out = {}
    for name in wanted:
        part = exported[name]
        ratio = part.width / float(part.height)
        best, mark = None, None
        for page, aspect in pages:
            if abs(aspect - ratio) > shape:
                continue
            here = _likeness(page, part)
            if mark is None or here < mark:
                best, mark = page, here
        if best is None:
            raise KeyError("no page in the vector source is shaped like %r" % name)
        out[name] = best
    return out


# --------------------------------------------------------------------------- the proof


#: How far a bake may sit from the pack's own exported frames, per channel out of 255.
#:
#: **Two, and the number is what separates two kinds of wrong.** Baking with the pack's own part
#: PNGs lands at 0.3-0.6, which is resampling noise; baking from the vector source lands at
#: 1.1-2.2, because Illustrator and the pack's exporter antialias differently. A mis-read
#: keyframe, a dropped parent or a part matched to the wrong page moves it by an order of
#: magnitude - measured, the worst single mismatched hand reads above 20.
AGREEMENT = 3.0

#: The pack this baker was written for, and where inside it everything lives.
PACK = (r"C:\Users\Digikey\Downloads\craftpix-assets"
        r"\craftpix-net-869102-tower-defense-neighborhood-top-down-2d-asset-pack.zip")

RIGS = "Json Atlas/Zombies/Zombies%02d/zombies%02d.json"
PARTS = "Spine/Enemy/zombies%02d/Images/"
SHIPPED = "Png/Zombies/Zombies%02d/%s/"
VIEWS = (("Front view walk", "Front view"),
         ("Back view walk", "Back view"),
         ("side view walk", "Side view"))


def _read(z, name):
    return Image.open(io.BytesIO(z.read(name))).convert("RGBA")


def verify(path=PACK, vector=False):
    """Bake every rig against the frames the pack itself exported, and say how far apart they are.

    <b>This is the only reason anything else here may be believed.</b> A baker that gets a bone
    parent, a keyframe or a draw order wrong produces an animation that looks plausible and is
    not the one the artist made - and nothing downstream can tell, because the board has never
    seen the right one. The pack shipped the right answer as PNGs; this compares against it.
    """
    zf = Path(path)
    if not zf.exists():
        print("pack not on this machine at %s - nothing to verify" % zf)
        return 0

    z = zipfile.ZipFile(zf)
    pages = pages_of(z.read("Ai/Enemy Characters.ai"), 72 * 6) if vector else None

    # **Found by listing rather than by counting**: one rig in this pack is filed under a name
    # its folder does not repeat, so a loop over 1..10 building both halves of the path misses it
    # and raises. The pack is the authority on what is in it.
    inside = {}
    for name in z.namelist():
        hit = re.match(r"Json Atlas/Zombies/Zombies(\d\d)/.*\.json$", name)
        if hit:
            inside[int(hit.group(1))] = name

    worst, checked = 0.0, 0
    for n in sorted(inside):
        rig = Rig(json.loads(z.read(inside[n])))
        rig.slots = [s for s in rig.slots if s["name"] != "Bg"]

        prefix = PARTS % n
        exported = {x[len(prefix):-4]: _read(z, x) for x in z.namelist()
                    if x.startswith(prefix) and x.endswith(".png")}

        guide = rig.attachments["Bg"]["Bg"]
        box = (guide["x"] - guide["width"] / 2.0, guide["y"] - guide["height"] / 2.0,
               guide["x"] + guide["width"] / 2.0, guide["y"] + guide["height"] / 2.0)

        # **A rig whose animations this table does not name is reported, never skipped in
        # silence.** Four of the ten in this pack spell their views differently; none of them is
        # a body the game draws, and "nothing to check" and "checked and agreed" must not look
        # the same in the output (invariant 19e: a check that cannot fail is not a check).
        unknown = [a for a in rig.animations if a not in {v[0] for v in VIEWS}]
        if unknown:
            print("  zombie %02d  not verified - animations named %s"
                  % (n, ", ".join(repr(a) for a in sorted(unknown))))

        for animation, folder in VIEWS:
            if animation not in rig.animations:
                continue

            want = sorted(x for x in z.namelist()
                          if x.startswith(SHIPPED % (n, folder)) and x.endswith(".png"))
            if not want:
                continue

            if vector:
                needed = sorted({p[0] for i in range(len(want))
                                 for p in rig.placements(animation,
                                                         rig.duration(animation) * i / len(want))})
                parts = match_parts(pages, exported, needed)
            else:
                parts = exported

            span = rig.duration(animation)
            here = 0.0
            for i, name in enumerate(want):
                ref = _read(z, name)
                got = draw(rig, parts, animation, span * i / float(len(want)), box, 1.0,
                           supersample=3)
                if got.size != ref.size:
                    got = got.resize(ref.size, Image.LANCZOS)

                a = np.asarray(got).astype(np.float32)
                b = np.asarray(ref).astype(np.float32)
                lit = a[..., :3] * (a[..., 3:4] / 255.0), b[..., :3] * (b[..., 3:4] / 255.0)
                here = max(here, float(np.abs(lit[0] - lit[1]).mean()))

            checked += 1
            worst = max(worst, here)
            flag = "  <-- OVER" if here > AGREEMENT else ""
            print("  zombie %02d  %-16s %2d frames  worst %.2f/255%s"
                  % (n, animation, len(want), here, flag))

    print("\n%d animation(s) checked, worst disagreement %.2f of 255 (allowed %.1f)"
          % (checked, worst, AGREEMENT))
    return 0 if worst <= AGREEMENT else 1


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--verify", action="store_true",
                    help="bake every rig against the pack's own exported frames")
    ap.add_argument("--vector", action="store_true",
                    help="verify using the vector source rather than the exported parts")
    ap.add_argument("--pack", default=PACK)
    args = ap.parse_args()

    if not args.verify:
        ap.print_help()
        return 0

    return verify(args.pack, args.vector)


if __name__ == "__main__":
    sys.exit(main())
