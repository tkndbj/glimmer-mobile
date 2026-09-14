#!/usr/bin/env python3
"""Builds every launcher-icon asset the game ships from one authored master image.

Run it from the repository root:

    python Tools/make_launcher_icons.py

Input   Tools/IconSource/glimmer_launcher.png   (the authored artwork)
Output  Assets/Game/Branding/Icons/*.png        (checked in, referenced by PlayerSettings)

The artwork is a full-bleed square: three turrets on a plinth row, firing, over a
radial burst of blue. Five shapes come out of it, which is what the two platforms
between them actually want:

  icon_master_1024                  the artwork as drawn, opaque, no alpha
                                    -> every iOS slot, including the 1024 App Store
                                       icon (Apple rejects icons with an alpha
                                       channel, so this one is written as RGB)
  icon_android_legacy_512           the same art, 20% rounded corners, alpha
                                    -> Android pre-adaptive launchers
  icon_android_round_512            circular composition
                                    -> Android round-icon launchers
  icon_android_adaptive_background  the burst alone, full bleed
  icon_android_adaptive_foreground  the turrets alone, inside the safe zone
                                    -> Android 8+ adaptive icon, which is what every
                                       device this game supports actually uses
                                       (AndroidMinSdkVersion is 26)

Nothing here is hand-traced. The two derivations that are worth understanding:

* **Cutting the turrets out.** Colour cannot do it: the cyan turret reads (20, 240,
  253) and the background near the burst's centre reads (76, 233, 253), which is the
  same colour to any threshold that would also keep the blue plate. What separates
  them is that the artwork outlines every turret in near-black. So the *background*
  is found instead — flood-filled inward from the border across everything that is
  neither outline nor flame — and the subject is whatever that flood cannot reach.
  Excluding the flames from the flood is the load-bearing half: a flame leaves the
  muzzle without an outline, so with them passable the fill walks up the barrel and
  hollows out the turret behind it. That is exactly how the cyan turret was lost on
  the first cut, with nothing else about the run looking wrong.

* **Rebuilding the burst behind them.** An adaptive icon's background layer has to
  cover the whole canvas, including the part the turrets stand in front of. Erasing
  and blurring leaves a ghost of the silhouette, and extending each ray inward from
  the last pixel it can be seen at smears the plinths' ground shadow into a cone. So
  the burst is *fitted* instead, as the one thing it is: brightness that varies with
  radius (a hot centre, a vignette at the corners) times a colour that varies with
  angle (the rays) — separable, fitted in the log of each channel, three passes with
  the outliers thrown out so the sparkles and the shadow do not drag it. That yields
  a burst with the artwork's own ray angles and palette and nothing to ghost. The
  sparkles are then composited back on top.

Regenerate and re-run 'Glimmer Grove > Apply Launcher Icons' after any change to
the artwork. The generated files are checked in so a clone can build without Python.
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "Tools" / "IconSource" / "glimmer_launcher.png"
OUT_DIR = ROOT / "Assets" / "Game" / "Branding" / "Icons"

# What counts as the artwork's outline. Every turret, barrel and plinth is drawn
# inside one; nothing in the background is anywhere near this dark.
OUTLINE_MAX = 95

# What counts as flame. The background is blue to the point of having almost no red
# in it at all, so any pixel with red meaningfully above blue belongs to a muzzle
# flash — which is why this can be a plain colour rule where the turrets cannot.
FLAME_RED_OVER_BLUE = 50
FLAME_MIN_RED = 110

# A component smaller than this fraction of the canvas is a speck, not a turret.
MIN_SUBJECT_FRACTION = 0.0008

# Adaptive icons are authored at 108 dp. Only the middle 72 dp survives every
# launcher mask, and only a 66 dp circle inside that is guaranteed. 108 dp maps to
# 432 px at xxxhdpi; the subject is fitted into 286 px, a little under 72 dp, which
# keeps the outermost flame tips clear of a circular mask.
ADAPTIVE_PX = 432
SAFE_PX = 286

# The corner radius a legacy Android launcher icon is drawn with, as a fraction of
# its width. The artwork itself is square to the edge, so this is ours to choose.
CORNER_RADIUS = 0.20

# Resolution of the burst fit: angular bins around the circle, radial bins out to
# the far corner. The radial profile is read back by interpolation rather than by
# bin, because a piecewise-constant one draws visible rings across the plate.
BURST_ANGLES = 720
BURST_RADII = 48


def load_master() -> np.ndarray:
    """The artwork as a full-bleed opaque square."""
    rgb = np.asarray(Image.open(SOURCE).convert("RGB"))
    height, width = rgb.shape[:2]
    side = min(height, width)
    top, left = (height - side) // 2, (width - side) // 2
    return rgb[top:top + side, left:left + side]


def cut_subject(master: np.ndarray) -> np.ndarray:
    """Boolean mask of the turrets, their plinths and their muzzle flashes."""
    rgb = master.astype(np.int32)
    red, blue = rgb[..., 0], rgb[..., 2]

    outline = rgb.max(2) < OUTLINE_MAX
    flame = (red - blue > FLAME_RED_OVER_BLUE) & (red > FLAME_MIN_RED)

    # The background is everything the border can reach without crossing an outline
    # or a flame. Leave the flames passable and the fill walks up a barrel through
    # its own muzzle and empties the turret behind it.
    labels, _ = ndimage.label(~outline & ~flame)
    border = set(np.unique(np.concatenate(
        [labels[0], labels[-1], labels[:, 0], labels[:, -1]]))) - {0}

    subject = ndimage.binary_fill_holes(
        ndimage.binary_closing(~np.isin(labels, list(border)), np.ones((11, 11))))

    labels, count = ndimage.label(subject)
    sizes = ndimage.sum(subject, labels, range(1, count + 1))
    solid = [i + 1 for i, size in enumerate(sizes)
             if size > MIN_SUBJECT_FRACTION * subject.size]
    if not solid:
        raise SystemExit(f"{SOURCE.name}: found no subject, only background")
    return np.isin(labels, solid)


def burst_centre(master: np.ndarray, visible: np.ndarray) -> tuple[float, float]:
    """Where the rays converge, found rather than typed.

    A ray is a stretch of one colour at one angle, so the right centre is the one
    that makes the background's brightness depend on angle and not much else. The
    search scores a candidate by how little the brightness still varies *within* an
    angular bin once the radial trend is taken out, and keeps the lowest.
    """
    side = master.shape[0]
    ys, xs = np.mgrid[0:side, 0:side].astype(np.float32)
    luma = master.astype(np.float32).mean(2)

    def score(cx: float, cy: float) -> float:
        radius = np.hypot(xs - cx, ys - cy)
        angle = np.arctan2(ys - cy, xs - cx)
        take = visible & (radius > side * 0.25) & (radius < side * 0.72)
        if take.sum() < 5000:
            return np.inf

        bins = np.clip((radius[take] / (side * 0.72) * 24).astype(int), 0, 23)
        values = luma[take]
        radial = (np.bincount(bins, values, 24)
                  / np.maximum(np.bincount(bins, None, 24), 1))
        detrended = values - radial[bins]

        spokes = ((angle[take] + np.pi) / (2 * np.pi) * 360).astype(int) % 360
        counts = np.maximum(np.bincount(spokes, None, 360), 1)
        mean = np.bincount(spokes, detrended, 360) / counts
        return float(np.bincount(spokes, (detrended - mean[spokes]) ** 2, 360).sum()
                     / len(detrended))

    best = (np.inf, side / 2.0, side / 2.0)
    for step, span in ((side // 32, side // 2), (side // 160, side // 16)):
        _, cx0, cy0 = best
        for cy in np.arange(cy0 - span / 2, cy0 + span / 2 + 1, step):
            for cx in np.arange(cx0 - span / 2, cx0 + span / 2 + 1, step):
                best = min(best, (score(cx, cy), float(cx), float(cy)))
    return best[1], best[2]


def rebuild_background(master: np.ndarray, subject: np.ndarray) -> np.ndarray:
    """The burst across the whole canvas, including behind the subject."""
    side = master.shape[0]
    rgb = master.astype(np.float32)

    # Fit only where the background is plainly visible: clear of the outlines, and
    # clear of the flames' soft outer glow, which carries far past the flame itself
    # and would otherwise paint a warm streak down the rays it sits on.
    glow = ndimage.binary_dilation(rgb[..., 0] > rgb[..., 2], np.ones((9, 9)))
    visible = ~ndimage.binary_dilation(subject, np.ones((31, 31))) & ~glow

    cx, cy = burst_centre(master, visible)
    ys, xs = np.mgrid[0:side, 0:side].astype(np.float32)
    radius = np.hypot(xs - cx, ys - cy)
    angle = np.arctan2(ys - cy, xs - cx)
    far = float(radius.max())

    spoke = np.clip(((angle + np.pi) / (2 * np.pi) * BURST_ANGLES).astype(int)
                    % BURST_ANGLES, 0, BURST_ANGLES - 1)
    ring = np.clip((radius / far * BURST_RADII).astype(int), 0, BURST_RADII - 1)
    scaled = radius / far * BURST_RADII - 0.5

    # Fitted in the log of each channel, so "a ray is this much lighter" is one
    # number wherever on the plate it is read.
    log = np.log(rgb + 8.0)
    model = np.zeros_like(log)
    keep = visible.copy()

    for _ in range(3):
        radial = np.stack([_profile(ring[keep], log[..., c][keep], BURST_RADII, 60)
                           for c in range(3)], axis=1)
        fitted = np.stack([np.interp(scaled.ravel(), np.arange(BURST_RADII),
                                     radial[:, c]).reshape(side, side)
                           for c in range(3)], axis=-1)

        residual = log - fitted
        angular = np.stack([_profile(spoke[keep], residual[..., c][keep],
                                     BURST_ANGLES, 40, wrap=True)
                            for c in range(3)], axis=1)
        angular = np.stack([ndimage.uniform_filter1d(angular[:, c], 5, mode="wrap")
                            for c in range(3)], axis=-1)

        model = fitted + angular[spoke]
        error = np.abs(log - model).max(2)
        keep = visible & (error < 3.0 * np.median(error[visible]) * 1.4826)

    burst = np.clip(np.exp(model) - 8.0, 0, 255)

    # Put the sparkles back — small bright blobs only. Anything large that the fit
    # could not explain is the subject's ground shadow, which belongs to the
    # turrets rather than to the plate they stand on.
    excess = rgb.max(2) - burst.max(2)
    sparkle = (excess > 22) & ~subject
    labels, count = ndimage.label(sparkle)
    sizes = ndimage.sum(sparkle, labels, range(1, count + 1))
    compact = [i + 1 for i, size in enumerate(sizes) if size < 2600]
    sparkle = np.isin(labels, compact)

    halo = ndimage.gaussian_filter(np.where(sparkle, excess, 0.0).astype(np.float32), 1.2)
    return np.clip(burst + halo[..., None], 0, 255).astype(np.uint8)


def _profile(bins: np.ndarray, values: np.ndarray, count: int,
             minimum: int, wrap: bool = False) -> np.ndarray:
    """Median per bin, with thin and empty bins interpolated from their neighbours.

    A median rather than a mean because what is being averaged over still holds a
    little of everything the masks did not catch — a flame's last glow, the edge of
    a shadow — and a mean carries that straight into the plate as a streak.
    """
    out = np.full(count, np.nan)
    order = np.argsort(bins, kind="stable")
    edges = np.searchsorted(bins[order], np.arange(count + 1))
    sorted_values = values[order]
    for k in range(count):
        sample = sorted_values[edges[k]:edges[k + 1]]
        if len(sample) >= minimum:
            out[k] = np.median(sample)

    known = np.where(~np.isnan(out))[0]
    if len(known) == 0:
        raise SystemExit("burst fit: no bin held enough background to measure")
    if wrap:
        return np.interp(np.arange(count),
                         np.concatenate([known - count, known, known + count]),
                         np.concatenate([out[known]] * 3))
    return np.interp(np.arange(count), known, out[known])


def subject_cutout(master: np.ndarray, subject: np.ndarray) -> Image.Image:
    """The subject on transparency, cropped tight, with a soft one-pixel edge."""
    edge = ndimage.binary_erosion(subject, np.ones((5, 5))).astype(np.float32)
    alpha = np.clip(ndimage.gaussian_filter(edge, 1.6), 0, 1)
    rgba = np.dstack([master, (alpha * 255).astype(np.uint8)])

    ys, xs = np.where(subject)
    return Image.fromarray(rgba[ys.min():ys.max() + 1, xs.min():xs.max() + 1], "RGBA")


def fit_into_safe_zone(cutout: Image.Image) -> Image.Image:
    """The cutout centred on a transparent adaptive-icon canvas."""
    scale = SAFE_PX / max(cutout.size)
    scaled = cutout.resize(
        (max(1, round(cutout.width * scale)), max(1, round(cutout.height * scale))),
        Image.LANCZOS,
    )
    canvas = Image.new("RGBA", (ADAPTIVE_PX, ADAPTIVE_PX), (0, 0, 0, 0))
    canvas.paste(scaled, ((ADAPTIVE_PX - scaled.width) // 2,
                          (ADAPTIVE_PX - scaled.height) // 2), scaled)
    return canvas


def masked(image: Image.Image, draw_shape) -> Image.Image:
    mask = Image.new("L", image.size, 0)
    draw_shape(ImageDraw.Draw(mask), image.size[0])
    out = Image.new("RGBA", image.size, (0, 0, 0, 0))
    out.paste(image.convert("RGBA"), (0, 0), mask)
    return out


def main() -> int:
    if not SOURCE.exists():
        print(f"missing source artwork: {SOURCE}", file=sys.stderr)
        return 1
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    master = load_master()
    subject = cut_subject(master)
    background = rebuild_background(master, subject)
    print(f"master {master.shape[0]}px, subject covers {subject.mean():.0%} of it")

    written: list[tuple[str, Image.Image]] = []

    # iOS, and the source every downscaled iOS slot is resampled from. RGB, because
    # App Store Connect rejects a 1024 icon that carries an alpha channel.
    written.append((
        "icon_master_1024.png",
        Image.fromarray(master).resize((1024, 1024), Image.LANCZOS).convert("RGB"),
    ))

    # Android adaptive: burst behind, turrets in front.
    written.append((
        "icon_android_adaptive_background_432.png",
        Image.fromarray(background).resize((ADAPTIVE_PX, ADAPTIVE_PX), Image.LANCZOS).convert("RGB"),
    ))
    foreground = fit_into_safe_zone(subject_cutout(master, subject))
    written.append(("icon_android_adaptive_foreground_432.png", foreground))

    # Android legacy: the artwork as drawn, with a launcher's corner radius on it.
    legacy = Image.fromarray(master).resize((512, 512), Image.LANCZOS)
    written.append(("icon_android_legacy_512.png", masked(
        legacy,
        lambda d, s: d.rounded_rectangle([0, 0, s - 1, s - 1], radius=s * CORNER_RADIUS, fill=255),
    )))

    # Android round: the adaptive composition, so nothing important meets the circle.
    plate = Image.fromarray(background).resize((ADAPTIVE_PX, ADAPTIVE_PX), Image.LANCZOS)
    composed = Image.alpha_composite(plate.convert("RGBA"), foreground).resize((512, 512), Image.LANCZOS)
    written.append(("icon_android_round_512.png", masked(
        composed, lambda d, s: d.ellipse([0, 0, s - 1, s - 1], fill=255),
    )))

    for name, image in written:
        path = OUT_DIR / name
        image.save(path, "PNG", optimize=True)
        print(f"  wrote {path.relative_to(ROOT)} {image.size[0]}x{image.size[1]} {image.mode}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
