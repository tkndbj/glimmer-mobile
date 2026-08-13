#!/usr/bin/env python3
"""Builds every launcher-icon asset the game ships from one authored master image.

Run it from the repository root:

    python Tools/make_launcher_icons.py

Input   Tools/IconSource/glimmer_launcher.jpeg   (the authored artwork)
Output  Assets/Game/Branding/Icons/*.png         (checked in, referenced by PlayerSettings)

The artwork arrives as a rounded-square badge sitting on a black field. Every
platform masks the icon itself, so shipping that black field would draw a black
frame around the real icon on both stores. This script removes it and derives the
five shapes the two platforms actually want:

  icon_master_1024                  full-bleed square, opaque, no alpha
                                    -> every iOS slot, including the 1024 App Store
                                       icon (Apple rejects icons with an alpha
                                       channel, so this one is written as RGB)
  icon_android_legacy_512           the same art, 20% rounded corners, alpha
                                    -> Android pre-adaptive launchers
  icon_android_round_512            circular composition
                                    -> Android round-icon launchers
  icon_android_adaptive_background  the gradient alone, full bleed
  icon_android_adaptive_foreground  the character alone, inside the safe zone
                                    -> Android 8+ adaptive icon, which is what every
                                       device this game supports actually uses
                                       (AndroidMinSdkVersion is 26)

Nothing here is hand-traced. The three derivations that are worth understanding:

* **Un-masking the badge.** The black field is everything outside the artwork's
  own rounded rectangle. The rectangle is found by threshold, inset far enough to
  drop the glass rim the artist drew along its edge, and the corners are then
  filled by extending the nearest real pixel outward. The result is a true square
  with no rim and no black.

* **Cutting the character out.** The character is whatever the dark outline
  encloses. The plinth has no outline, so it is found by colour instead: the
  background is teal (blue about equal to green) and the plinth is green and tan
  (blue far below green). That single rule separates them without touching the
  character's own teal shell, which the outline pass has already claimed.

* **Rebuilding the gradient behind it.** An adaptive icon's background layer has
  to cover the whole canvas, including the part the character was standing in
  front of. Erasing and blurring leaves a ghost of the silhouette, so instead a
  cubic polynomial is fitted per channel to the pixels that *are* background —
  three passes, rejecting outliers, so the sparkles do not drag the fit. That
  yields an exactly smooth field with nothing to ghost. The sparkles are then
  composited back on top; the long light rays deliberately are not, because they
  radiate from behind the character and would be cut off where he used to be.

Regenerate and re-run 'Glimmer Grove > Apply Launcher Icons' after any change to
the artwork. The generated files are checked in so a clone can build without
Python.
"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "Tools" / "IconSource" / "glimmer_launcher.jpeg"
OUT_DIR = ROOT / "Assets" / "Game" / "Branding" / "Icons"

# How far inside the artwork's rounded rectangle to cut. Large enough to drop the
# glass rim the artist drew along the edge; small enough to leave the plinth
# untouched, which reaches to within ~110 px of it.
RIM_INSET = 45

# Adaptive icons are authored at 108 dp. Only the middle 72 dp survives every
# launcher mask, and only a 66 dp circle inside that is guaranteed. 108 dp maps to
# 432 px at xxxhdpi; the subject is fitted into 286 px, a little under 72 dp, which
# keeps the crown and the plinth clear of a circular mask.
ADAPTIVE_PX = 432
SAFE_PX = 286

# The corner radius the artwork itself was drawn with, as a fraction of its width.
CORNER_RADIUS = 0.20


def load_master() -> np.ndarray:
    """The artwork as a full-bleed opaque square: black field and rim removed."""
    rgb = np.asarray(Image.open(SOURCE).convert("RGB"))

    # The badge is the one large blob that is not the black field.
    lit = ndimage.binary_fill_holes(rgb.max(2) > 24)
    labels, count = ndimage.label(lit)
    if count == 0:
        raise SystemExit(f"{SOURCE.name}: found no artwork, only background")
    sizes = ndimage.sum(lit, labels, range(1, count + 1))
    badge = labels == int(np.argmax(sizes)) + 1

    kernel = np.ones((RIM_INSET * 2 + 1, RIM_INSET * 2 + 1))
    badge = ndimage.binary_erosion(badge, kernel)

    ys, xs = np.where(badge)
    rgb = rgb[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    badge = badge[ys.min():ys.max() + 1, xs.min():xs.max() + 1]

    # Push the nearest real pixel outward into the rounded corners.
    nearest = ndimage.distance_transform_edt(~badge, return_indices=True)[1]
    filled = rgb[nearest[0], nearest[1]]

    height, width = filled.shape[:2]
    side = min(height, width)
    top, left = (height - side) // 2, (width - side) // 2
    return filled[top:top + side, left:left + side]


def cut_subject(master: np.ndarray) -> np.ndarray:
    """Boolean mask of the character and the plinth he stands on."""
    rgb = master.astype(np.float32)

    # The character is whatever the dark outline encloses: label everything that is
    # not outline, discard the components that reach the border, keep the largest.
    labels, _ = ndimage.label(rgb.max(2) >= 80)
    border = np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]])
    enclosed = ~np.isin(labels, list(set(np.unique(border)) - {0}))

    labels, count = ndimage.label(enclosed)
    sizes = ndimage.sum(enclosed, labels, range(1, count + 1))
    character = ndimage.binary_fill_holes(labels == int(np.argmax(sizes)) + 1)

    # The plinth has no outline to enclose it, but the background never does this:
    # blue well below green. Teal background and teal shell both stay clear of it.
    plinth = ndimage.binary_opening(rgb[..., 2] < 0.62 * rgb[..., 1], np.ones((7, 7)))

    labels, count = ndimage.label(plinth | character)
    sizes = ndimage.sum(plinth | character, labels, range(1, count + 1))
    solid = [i + 1 for i, size in enumerate(sizes) if size > 0.004 * plinth.size]

    subject = ndimage.binary_fill_holes(np.isin(labels, solid))
    subject = ndimage.binary_closing(subject, np.ones((9, 9)))

    labels, count = ndimage.label(subject)
    sizes = ndimage.sum(subject, labels, range(1, count + 1))
    return ndimage.binary_fill_holes(labels == int(np.argmax(sizes)) + 1)


def rebuild_background(master: np.ndarray, subject: np.ndarray) -> np.ndarray:
    """The gradient across the whole canvas, including behind the subject."""
    side = master.shape[0]
    rgb = master.astype(np.float32)

    y, x = np.mgrid[0:side, 0:side] / (side - 1.0) * 2 - 1
    basis = np.stack([t.ravel() for t in (
        np.ones_like(x), x, y, x * x, x * y, y * y,
        x ** 3, x * x * y, x * y * y, y ** 3,
    )], axis=1)

    # Fit only where the background is actually visible, well clear of the outline.
    visible = (~ndimage.binary_dilation(subject, np.ones((25, 25)))).ravel()
    fitted = np.zeros_like(rgb)

    for channel in range(3):
        values = rgb[..., channel].ravel()
        use = visible.copy()
        for _ in range(3):  # re-fit without the sparkles, which are outliers
            coefficients, *_ = np.linalg.lstsq(basis[use], values[use], rcond=None)
            residual = values - basis @ coefficients
            use = visible & (np.abs(residual) < 2.2 * residual[visible].std())
        fitted[..., channel] = (basis @ coefficients).reshape(side, side)

    fitted = np.clip(fitted, 0, 255)

    # Put the sparkles back — small bright blobs only. The long light rays are left
    # out on purpose: they radiate from behind the character and would end abruptly.
    excess = master.max(2).astype(np.float32) - fitted.max(2)
    sparkle = (excess > 22) & ~subject
    labels, count = ndimage.label(sparkle)
    sizes = ndimage.sum(sparkle, labels, range(1, count + 1))
    compact = [i + 1 for i, size in enumerate(sizes) if size < 2600]
    sparkle = np.isin(labels, compact)

    glow = ndimage.gaussian_filter(np.where(sparkle, excess, 0.0).astype(np.float32), 1.2)
    return np.clip(fitted + glow[..., None], 0, 255).astype(np.uint8)


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

    # Android adaptive: gradient behind, character in front.
    written.append((
        "icon_android_adaptive_background_432.png",
        Image.fromarray(background).resize((ADAPTIVE_PX, ADAPTIVE_PX), Image.LANCZOS).convert("RGB"),
    ))
    foreground = fit_into_safe_zone(subject_cutout(master, subject))
    written.append(("icon_android_adaptive_foreground_432.png", foreground))

    # Android legacy: the artwork as drawn, with its own corner radius back.
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
