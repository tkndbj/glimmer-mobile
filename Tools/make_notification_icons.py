#!/usr/bin/env python3
"""Cuts the Android status-bar mark the notifications are drawn with.

    python Tools/make_notification_icons.py              # write the androidlib
    python Tools/make_notification_icons.py --check      # prove the shipped PNGs reproduce
    python Tools/make_notification_icons.py --contact <png>   # draw it at true size; look at it

Output  Assets/Plugins/Android/GlimmerNotifications.androidlib/
            AndroidManifest.xml, project.properties,
            res/drawable-{m,h,xh,xxh,xxx}dpi/ic_stat_glimmer.png

WHY THIS FILE EXISTS AT ALL

Android >= 5.0 draws a notification's *small* icon as a silhouette: it throws the
colour away and keeps the alpha channel alone, then tints what is left. The Unity
notifications package resolves the icon by name and, finding none, falls back to
`getApplicationInfo().icon` -- the launcher icon, whose alpha is a solid square.
So the *default* behaviour of shipping no small icon is a white blob in the status
bar, on every device, and nothing in the build says so.

WHY IT IS DRAWN RATHER THAN DERIVED FROM THE LAUNCHER ART

The obvious move is to downscale `make_launcher_icons.py`'s output, and it was
tried three ways before this one; every one of them is in the repository history
as a picture. All three turrets filled reads as a bird. One turret filled -- cut
out of the launcher foreground by assigning every pixel to its nearest coloured
turret, which works perfectly -- reads as a blob, because the artwork's shape is
carried entirely by *interior* outlines that a filled alpha throws away. Punching
those outlines back out as a stencil turns into confetti, because they are a
quarter of a pixel wide at 24dp.

The finding is general and it is the reason this tool draws:

    **A 24dp mark is not a small picture, it is a different picture.** Illustrated
    artwork carries its shape in detail that is sub-pixel at notification size, so
    a notification icon has to be authored for 24dp -- a handful of strokes no
    thinner than 2dp -- and the only instrument that can say whether one works is
    a render at 24, 36 and 48 real pixels.

So the glyph is three primitives in normalised coordinates, rasterised at 2048 and
resampled down: a plinth, a domed body, and a barrel raised to the right. Nothing
here is thinner than 1/12 of the canvas, which is the 2dp floor at the smallest
bucket. `--contact` is the gate that matters; `--check` only proves reproducibility.

WHY AN ANDROIDLIB

`Assets/Plugins/Android/res` is deprecated -- Unity's own build warns that
resources belong in an AAR or an Android Library -- and this project already has
the shape beside it in `GlimmerMeasurement.androidlib`. A library's `res` is merged
into the application package, which is what lets `Resources.getIdentifier(name,
"drawable", packageName)` find it at runtime.

There is deliberately **no large icon**. A large icon is for content-specific
imagery -- an avatar, a picture the notification is *about*. Filling it with the
app icon duplicates the app name Android already prints beside it, and on Android
12+ it demotes the small icon to a corner badge, which is the one thing here that
has to be legible.

iOS needs nothing from this file: it draws the app icon on a notification itself,
from the slot `LauncherIcons` already fills.
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
LIB = ROOT / "Assets" / "Plugins" / "Android" / "GlimmerNotifications.androidlib"
RES = LIB / "res"

# The name the runtime asks for. It is a contract with `Notifications.SmallIcon` in
# C# and must not be renamed on one side alone -- an icon that does not resolve is
# not an error, it is the launcher-icon blob this file exists to prevent.
ICON = "ic_stat_glimmer"

# Android's density buckets and what 24dp is in each. Five buckets covers every
# device; a bare `drawable/` fallback would only ever be chosen when one is missing.
BUCKETS = {"mdpi": 24, "hdpi": 36, "xhdpi": 48, "xxhdpi": 72, "xxxhdpi": 96}

SUPERSAMPLE = 2048

# How much of the 24dp box the glyph fills. Android's own icons leave a hair of
# breathing room inside the canvas; filling it edge to edge makes the mark read as
# cropped when the shade draws it inside a circle.
FILL = 0.94


def glyph() -> Image.Image:
    """The mark, drawn large. White on transparent; only the alpha is ever used.

    Coordinates are fractions of the canvas so the proportions are the design and
    the resolution is not. The three numbers that matter are the plinth's height,
    the body's width and the barrel's width -- each is at least 1/12, which is 2dp
    at the mdpi bucket and therefore the thinnest thing Android will still draw.
    """
    size = SUPERSAMPLE
    image = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(image)

    def box(*fractions: float) -> list[float]:
        return [value * size for value in fractions]

    # The plinth. A rounded bar rather than the artwork's ellipse: an ellipse at
    # 24dp loses its ends and reads as a rectangle anyway, and a bar keeps its
    # height where an ellipse's is only full at the centre.
    plinth = (0.07, 0.80, 0.93, 0.96)
    draw.rounded_rectangle(box(*plinth), radius=(plinth[3] - plinth[1]) * size / 2, fill=255)

    # The body.
    body = (0.20, 0.40, 0.74, 0.82)
    draw.rounded_rectangle(box(*body), radius=(body[2] - body[0]) * size * 0.34, fill=255)

    # The barrel, raised right, with round caps so the muzzle has an end rather
    # than a corner. Drawn as a line plus two discs because PIL's `joint` does not
    # cap the ends.
    width = 0.20
    hub, muzzle = (0.47, 0.60), (0.94, 0.16)
    draw.line(box(hub[0], hub[1], muzzle[0], muzzle[1]), fill=255, width=int(width * size))
    radius = width * size / 2
    for x, y in (hub, muzzle):
        draw.ellipse([x * size - radius, y * size - radius,
                      x * size + radius, y * size + radius], fill=255)

    return image


def rasterise(mark: Image.Image, target: int) -> Image.Image:
    """The glyph fitted into one density bucket, as white pixels carrying alpha.

    The RGB channels are written white rather than left at zero even though Android
    discards them. A file whose colour is black and whose alpha is the shape looks
    like an empty image in every previewer and every art tool, which is how a
    correct icon comes to be "fixed" by somebody who opened it.
    """
    cropped = mark.crop(mark.getbbox())
    width, height = cropped.size
    scale = min(target * FILL / width, target * FILL / height)
    drawn = cropped.resize((max(1, round(width * scale)), max(1, round(height * scale))),
                           Image.LANCZOS)

    alpha = Image.new("L", (target, target), 0)
    alpha.paste(drawn, ((target - drawn.size[0]) // 2, (target - drawn.size[1]) // 2))

    out = Image.new("RGBA", (target, target), (255, 255, 255, 0))
    out.putalpha(alpha)
    return out


# NOTE: an XML comment may not contain a double hyphen, and it is not a style rule
# somebody can bend. Unity parses this file with System.Xml to read the library's
# package name, so one double hyphen anywhere in the prose below aborts the whole
# Android build with `XmlException: An XML comment cannot contain '--'` thrown from
# deep inside Unity's gradle generator, naming neither this file nor this project.
# It shipped that way once, from an em dash written as two hyphens. `verify_xml`
# below is what stops it recurring; keep the prose free of them anyway.
MANIFEST = """<?xml version="1.0" encoding="utf-8"?>
<!--
    Carries the notification status-bar mark, and nothing else.

    Android 5.0 and up draws a small icon from its alpha channel alone, so the
    launcher icon cannot be used for one: its alpha is a solid square and it renders
    as a white blob. The Unity notifications package looks the icon up by name
    (UnityNotificationUtilities.findResourceIdInContextByName, mipmap then drawable)
    and falls back, silently, to the launcher icon when it finds nothing. This
    library's `res` is merged into the application package, which is what makes that
    lookup succeed.

    Generated by Tools/make_notification_icons.py. Do not edit by hand; the tool's
    check mode proves the shipped files are what it cuts.

    The package name is a namespace rather than code, and must be unique: AGP 9
    refuses a namespace shared by two modules.
-->
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
          package="com.tekoworld.glimmergroove.notifications"
          android:versionCode="1"
          android:versionName="1.0">
</manifest>
"""

PROPERTIES = "target=android-9\nandroid.library=true\n"


def verify_xml(text: str) -> None:
    """Refuses a manifest that is not well-formed XML, before it can reach a build.

    This is the cheapest gate in the repository and it is here because the expensive
    version already happened: Unity reads this file with System.Xml to find the
    library's package name, so a malformed one does not produce a warning or a bad
    icon, it aborts the entire Android build from inside
    `AndroidProjectGradle.GenerateLibraryBuildGradle` with a stack trace that names
    Unity's own source files and never mentions this project. The offline gates were
    all green; the Editor compiled; the failure appeared only at BuildPlayer.

    A double hyphen in a comment is the way it will happen again, so it is called out
    by name rather than left to a generic parse error.
    """
    import xml.etree.ElementTree as ElementTree

    for line, body in enumerate(text.splitlines(), start=1):
        if "--" in body and not body.strip().startswith("<!--") and "-->" not in body:
            raise SystemExit(
                f"AndroidManifest.xml line {line} contains '--', which is illegal inside an "
                f"XML comment and aborts the Android build:\n    {body.strip()}")

    try:
        ElementTree.fromstring(text)
    except ElementTree.ParseError as error:
        raise SystemExit(f"AndroidManifest.xml is not well-formed XML: {error}")


def files() -> dict[Path, bytes]:
    """Every file this tool owns, as bytes, without touching the disk."""
    import io

    verify_xml(MANIFEST)
    mark = glyph()
    out: dict[Path, bytes] = {
        LIB / "AndroidManifest.xml": MANIFEST.encode("utf-8"),
        LIB / "project.properties": PROPERTIES.encode("utf-8"),
    }
    for bucket, pixels in BUCKETS.items():
        buffer = io.BytesIO()
        rasterise(mark, pixels).save(buffer, "PNG", optimize=True)
        out[RES / f"drawable-{bucket}" / f"{ICON}.png"] = buffer.getvalue()
    return out


def contact(path: str) -> None:
    """The three sizes a phone actually draws, magnified, on a dark shade and a light one.

    Both grounds, because Android tints the mark to suit the shade it is in and a
    glyph that only works on one of them is a glyph nobody checked.
    """
    mark = glyph()
    magnify, pad = 8, 18
    sizes = (24, 36, 48)
    width = pad + sum(size * magnify + pad for size in sizes)
    height = pad + 2 * (48 * magnify + pad)
    sheet = Image.new("RGB", (width, height), (24, 24, 28))
    ImageDraw.Draw(sheet).rectangle([0, height // 2, width, height], fill=(238, 238, 240))

    # White on the dark half, near-black on the light half: those are the two tints
    # Android actually applies, so they are the two the sheet has to answer for.
    for row, ink in enumerate(((255, 255, 255), (60, 62, 68))):
        x = pad
        for size in sizes:
            drawn = rasterise(mark, size).split()[-1]
            magnified = drawn.resize((size * magnify, size * magnify), Image.NEAREST)
            tile = Image.new("RGB", magnified.size, ink)
            sheet.paste(tile, (x, pad + row * (48 * magnify + pad)), magnified)
            x += size * magnify + pad

    sheet.save(path)
    print(f"wrote {path}")


def main() -> int:
    args = sys.argv[1:]

    if "--contact" in args:
        contact(args[args.index("--contact") + 1])
        return 0

    wanted = files()

    if "--check" in args:
        problems = []
        for path, blob in wanted.items():
            if not path.exists():
                problems.append(f"{path.relative_to(ROOT)} is missing")
            elif path.read_bytes() != blob:
                problems.append(f"{path.relative_to(ROOT)} does not reproduce; re-run without --check")
        for problem in problems:
            print(problem, file=sys.stderr)
        print(f"notification icons: {len(wanted) - len(problems)}/{len(wanted)} reproduce")
        return 1 if problems else 0

    for path, blob in wanted.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(blob)
        print(f"  wrote {path.relative_to(ROOT)}")

    print(f"\n{ICON} written into {len(BUCKETS)} density buckets.")
    print("Look at it before believing it: "
          "python Tools/make_notification_icons.py --contact Tools/out/notify_icon.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
