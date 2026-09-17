# -*- coding: utf-8 -*-
"""Draws the loadout's turret preview stage, at the size the panel draws it.

    python Tools/render_ward_preview.py --ward starfall
    python Tools/render_ward_preview.py --ward eclipse --beats 0,0.08,0.2,0.45

**Why this exists, written the day it was wanted.** `WardPreviewOverlay` is the screen a player
decides on a turret from — it is one tap in front of the shelf and it fires the real reels at the
real sizes — and it was the one screen in this mode with no mirror at all. `render_siege.py` draws
the board and `render_loadout.py` draws the shelf; between them sat the panel, and the fault that
sent a drop back twice (a bolt drawn at full length on the frame it was fired, so its trail was
painted back through the turret, `SiegeView.Emerged`) was reported off *this* panel both times. A
screen judged by eye and owning no instrument is a screen that costs a device round-trip for every
question asked of it.

**What it mirrors.** `WardFiringStage.Attach` and `.Shoot`: the stage box and its mask, the turret
at `TurretFoot`/`TurretTall`, the marks the ability stands (`Where`, `Stand`, `Row`, `Column`), and
one bolt per beat with the muzzle flash over it. The numbers are the stage's own, named after it,
so a change there that is not made here shows up as the mirror disagreeing with the screen rather
than as the mirror quietly lying (invariant 44d).

**What it cannot say.** Whether a reel reads at thirty frames a second. It draws stills, and the
one thing stills are good for is *where things are* — which is the whole of what was wrong.
"""
from __future__ import annotations

import argparse
import glob
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parent.parent
ART = REPO / "Assets" / "Game" / "Art" / "Siege"
FX = REPO / "Assets" / "Game" / "Art" / "Fx" / "Siege"

# ----------------------------------------------------------------- WardPreviewOverlay
#: `WardPreviewOverlay.StageW/StageH/StageCell` - the stage's box and the cell its contents are
#: multiples of. The cell is a real board's, which is the whole point of the panel.
STAGE_W, STAGE_H, CELL = 800.0, 640.0, 104.0

# ----------------------------------------------------------------- WardFiringStage
#: `.TurretWide/.TurretTall` (which are `SiegeView.BodyWide/.BodyTall`) and `.TurretFoot`.
TURRET_WIDE, TURRET_TALL, TURRET_FOOT = 1.72, 2.15, 0.10

#: `.BandTop` / `.BandClear` - where the band of targets starts and how much air it leaves the
#: turret. `.BoltWide`, `.MuzzleWide`, `.HitWide` and `.Flight` are the stage's own.
BAND_TOP, BAND_CLEAR = 1.6, 0.60
BOLT_WIDE, MUZZLE_WIDE, HIT_WIDE = 1.0, 2.7, 3.2

#: `SiegeView.HeadAt` / `.MuzzleAt` / `.HeadRoom`, and `.BoltScale`'s named rungs.
HEAD_AT, MUZZLE_AT, HEAD_ROOM = 0.82, 0.22, 0.35
BOLT_SCALES = {"apex": 1.55, "eclipse": 1.62, "breaker": 1.24,
               "spectrum": 1.16, "harpoon": 1.10}
LEGENDARY_BOLT = 1.18

#: The formations `WardFiringStage.Load` stands, as (across, down, size) in cells. Only the ones
#: a legendary can ask for are listed; anything else falls back to the plain single mark.
FORMATIONS = {
    "splash": [(0.0, .34, 1.12), (-1.55, .06, .92), (1.55, .06, .92)],
    "chain":  [(0.0, .34, 1.06), (-1.35, .10, .92), (1.35, .10, .92)],
    "none":   [(0.0, .34, 1.12)],
}


def roster():
    models = json.loads((REPO / "Assets/StreamingAssets/Content/progression.json")
                        .read_text(encoding="utf-8"))["wards"]["models"]
    return {m["id"]: m for m in models}


def bolt_scale(wid):
    if wid in BOLT_SCALES:
        return BOLT_SCALES[wid]
    return LEGENDARY_BOLT if roster().get(wid, {}).get("legendary") else 1.0


def emerged(flown, cell, tall):
    """`SiegeView.Emerged` - how much of its own frame a bolt draws, top down."""
    if tall <= 0:
        return 1.0
    return max(0.0, min(1.0, (1.0 - HEAD_AT) + (HEAD_ROOM * cell + flown) / tall))


def frames(folder):
    return sorted(glob.glob(str(FX / folder / "*.png")))


def loudest(folder):
    best = None
    for f in frames(folder):
        im = Image.open(f).convert("RGBA")
        ink = np.asarray(im, np.float32)[..., 3].sum()
        if best is None or ink > best[0]:
            best = (ink, im)
    return best[1] if best else None


def body_reel(folder, frame=0):
    """One frame of a body reel, which lives beside the board's art rather than under `Fx`."""
    fs = sorted(glob.glob(str(ART / folder / "*.png")))
    if not fs:
        return None
    return Image.open(fs[min(frame, len(fs) - 1)]).convert("RGBA")


def at_beat(folder, t):
    fs = frames(folder)
    if not fs:
        return None
    return Image.open(fs[min(len(fs) - 1, int(t * (len(fs) - 1)))]).convert("RGBA")


def aimed(sheet, im, hx, hy, wide, ux, uy, anchor, fill=1.0):
    """`SiegeView.Lend` / `WardFiringStage.Head`: turned to the aim, anchored `anchor` from the
    frame's bottom, and drawn only as far as `fill` from its top."""
    if im is None:
        return

    tall = wide * im.height / im.width
    im = im.resize((max(1, int(wide)), max(1, int(tall))), Image.LANCZOS)

    # Cropped before the turn, which is the order the board draws it in: the cut is a fact about
    # the reel, and the turn carries it round with the shot.
    if fill < 1.0:
        im = im.crop((0, 0, im.width, max(1, int(round(tall * fill)))))

    spin = -math.degrees(math.atan2(ux, -uy))
    turned = im.rotate(spin, Image.BICUBIC, expand=True)

    back = (anchor - 0.5) * tall
    cx, cy = hx - ux * back, hy - uy * back

    # **A crop shrinks the picture; `Image.fillAmount` does not shrink the rect.** The game leaves
    # the reel's box where it is and stops drawing part of it, so the head never moves. A cropped
    # PIL image pasted at the full frame's centre lands half the missing length too far back down
    # the shot. The centre moves toward the head by half of what was cut.
    slid = (tall - tall * fill) * 0.5
    cx, cy = cx + ux * slid, cy + uy * slid

    sheet.alpha_composite(turned, (int(cx - turned.width / 2), int(cy - turned.height / 2)))


def draw(wid, beats, out):
    info = roster().get(wid)
    if info is None:
        raise SystemExit("no turret called %s" % wid)

    legend = bool(info.get("legendary"))
    body = ART / "Wards" / ("%s.png" % wid if legend else "%s_r.png" % wid)
    if not body.exists():
        raise SystemExit("missing %s - run: python Tools/make_siege_art.py --write" % body.name)

    marks = FORMATIONS.get(info.get("ability", "none"), FORMATIONS["none"])
    scale = bolt_scale(wid)
    shot = "shot_%s" % wid if legend else "shot_%s_r" % wid
    muzz = "muzzle_%s" % wid if legend else "muzzle_%s_r" % wid

    pad, gap = 26, 18
    W = int(len(beats) * STAGE_W + (len(beats) + 1) * gap)
    H = int(STAGE_H + pad * 2 + 34)
    sheet = Image.new("RGBA", (W, H), (18, 21, 19, 255))
    pen = ImageDraw.Draw(sheet)

    for k, t in enumerate(beats):
        x0 = gap + k * (STAGE_W + gap)
        y0 = pad

        # The stage box, which is masked on the real panel - so everything below is drawn into
        # its own layer and pasted clipped, exactly as `RectMask2D` does.
        stage = Image.new("RGBA", (int(STAGE_W), int(STAGE_H)), (26, 28, 32, 255))

        # The turret, standing on the box's floor.
        bw, bh = CELL * TURRET_WIDE, CELL * TURRET_TALL
        turret = Image.open(body).convert("RGBA").resize((int(bw), int(bh)), Image.LANCZOS)
        ty = STAGE_H - CELL * TURRET_FOOT - bh
        stage.alpha_composite(turret, (int(STAGE_W / 2 - bw / 2), int(ty)))

        # `BarrelTop` - the muzzle, measured down from the box's top.
        mx, my = STAGE_W / 2, STAGE_H - (TURRET_FOOT + TURRET_TALL) * CELL

        # The marks, in the formation the ability stands (`Where`).
        foot = max(BAND_TOP, STAGE_H / CELL - (TURRET_FOOT + TURRET_TALL) - BAND_CLEAR)
        placed = []
        for across, down, size in marks:
            my_y = BAND_TOP + (foot - BAND_TOP) * min(1.0, max(0.0, down))
            placed.append((STAGE_W / 2 + across * CELL, my_y * CELL, CELL * size))

        for px, py, size in placed:
            # **Out of `Art/Siege` and not `Art/Fx/Siege`** - a raider is a body, not an effect.
            # Asked for the ward's own colour, which is what the panel stands for most abilities.
            raider = body_reel("mon_r")
            if raider is not None:
                r = raider.resize((int(size), int(size * raider.height / raider.width)),
                                  Image.LANCZOS)
                stage.alpha_composite(r, (int(px - r.width / 2), int(py - r.height / 2)))

        # One bolt, at this beat, at the primary mark.
        lx, ly, _ = placed[0]
        dx, dy = lx - mx, ly - my
        far = math.hypot(dx, dy) or 1.0
        ux, uy = dx / far, dy / far

        reel = at_beat(shot, t)
        wide = CELL * BOLT_WIDE * scale
        tall = wide * reel.height / reel.width if reel else 0.0

        aimed(stage, reel, mx + dx * t, my + dy * t, wide, ux, uy, HEAD_AT,
              emerged(far * t, CELL, tall))

        # Over the bolt, which is `WardFiringStage.Shoot`'s order: the crop's straight edge lives
        # at the barrel and this is what covers it.
        aimed(stage, at_beat(muzz, min(1.0, t * 3.0)), mx, my, CELL * MUZZLE_WIDE, ux, uy,
              MUZZLE_AT)

        sheet.alpha_composite(stage, (int(x0), int(y0)))
        pen.rectangle([x0, y0, x0 + STAGE_W - 1, y0 + STAGE_H - 1], outline=(70, 80, 76, 255))
        pen.line([x0 + STAGE_W / 2 - 90, y0 + my, x0 + STAGE_W / 2 + 90, y0 + my],
                 fill=(255, 80, 80, 170), width=2)
        pen.text((x0 + 10, y0 + STAGE_H + 8), "t = %.2f   (red line = the barrel)" % t,
                 fill=(215, 228, 218, 255))

    pen.text((gap, 8), "%s  -  WardPreviewOverlay stage, %gx%g at cell %g"
             % (wid, STAGE_W, STAGE_H, CELL), fill=(232, 240, 232, 255))

    out = Path(out)
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print("wrote %s" % out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ward", default="starfall")
    ap.add_argument("--beats", default="0,0.10,0.28,0.55")
    ap.add_argument("--out", default=str(REPO / "Tools" / "out" / "ward_preview.png"))
    a = ap.parse_args()

    draw(a.ward, [float(x) for x in a.beats.split(",")], a.out)


if __name__ == "__main__":
    main()
