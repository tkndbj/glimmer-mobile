#!/usr/bin/env python3
"""Cut the first sortie's art: the Gemfire wordmark and Sprocket's two reels (invariant 53h).

Three things into `Assets/Game/Art/`:

    Bg/gemfire_logo        the wordmark the tutorial's finale is crowned with
    Ui/Guide/idle/fNN      Sprocket standing and talking, 20 frames
    Ui/Guide/walk/fNN      Sprocket walking, 20 frames

    python Tools/make_sortie_art.py             # cut them, with their .meta files
    python Tools/make_sortie_art.py --check     # prove the shipped PNGs are what this cuts
    python Tools/make_sortie_art.py --contact   # out/sortie_art.png, every piece on one sheet

**The wordmark is the owner's own file**, committed beside the launcher icon's source as
`Tools/IconSource/gemfire_logo.png`, so it is always on this machine and always checkable. It is
trimmed to its glow and drawn down to `LOGO_W`: the finale draws it 880 units across, which is
1,173 pixels on a 1,440-wide phone, so 1,400 is the first width that is never an upscale on a
shipping display and no more. Resized **premultiplied**, because a straight-alpha resample drags
the transparent texels' black into the glow and leaves a dark seam round every letter.

**Sprocket is `Monster 3` of the quirky-monsters pack**, the critter the hidden glade mode draws
as `Critters/c3` and the profile offers as an avatar. That reel was cut at 176 pixels for a cell;
the guide stands 300 units tall, so it is cut again from the pack at the pack's own size rather
than blown up. **Every frame of both reels shares one crop** - the union of every frame's lit
box - so the feet stay planted and a reel swapped mid-stride does not jump. The pack draws him
facing left, which is the way he walks in from the right edge; walking out, the screen mirrors
the image. The PNGs are committed, so every gate passes with the pack absent.

The `.meta` files carry derived guids, so two machines cutting the same file agree about its
identity, and their importer settings are a template from the folder the file lands in - the
folder rules (`ArtImportRules`) are what decide them, and the template only saves a re-import.
"""

import argparse
import hashlib
import io
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "Assets" / "Game" / "Art"

LOGO_SOURCE = ROOT / "Tools" / "IconSource" / "gemfire_logo.png"
LOGO_OUT = ART / "Bg" / "gemfire_logo.png"
LOGO_TEMPLATE = ART / "Bg" / "plain.png.meta"

#: The wordmark's width in pixels. See the head of this file.
LOGO_W = 1400

#: Air left round the trimmed glow, so a bilinear sample at the edge reads transparent.
LOGO_PAD = 6

#: Alpha below this is the source's own antialiasing haze rather than glow, and is not kept as
#: part of the box the logo is trimmed to.
TRIM_ALPHA = 6

PACK = Path(r"C:\Users\Digikey\Downloads\2D ASSETS\_extracted"
            r"\craftpix-net-731843-quirky-monsters-game-asset-pack-v19\Png\Monster 3")

#: reel -> the pack's folder and file stem.
REELS = {
    "idle": ("Idle", "Monster3-Idle_"),
    "walk": ("Walk", "Monster3-Walk_"),
}

FRAMES = 20
GUIDE_OUT = ART / "Ui" / "Guide"
GUIDE_TEMPLATE = ART / "Ui" / "ic_trophy_gold.png.meta"

#: Air left round the shared crop, for the same reason as `LOGO_PAD`.
GUIDE_PAD = 4

#: `GuideDock.HeadClear` / `.GunLine`: the speech bubble tucks this share of the figure's width
#: into the figure's box, everywhere above this share of its height. `--check` proves every frame
#: of both reels leaves that corner empty, which is what makes the two numbers safe to lay out by.
HEAD_CLEAR, GUN_LINE = 0.22, 0.42

#: Alpha at or under this is empty for that proof: the faintest antialiasing a phone would draw.
EMPTY_ALPHA = 16


# ------------------------------------------------------------------ cutting
def premultiplied_resize(image, size):
    """A resample that does not drag transparent black into the edge."""
    return image.convert("RGBa").resize(size, Image.LANCZOS).convert("RGBA")


def lit_box(image, floor):
    """The box round every texel whose alpha reaches `floor`."""
    alpha = image.getchannel("A").point(lambda a: 255 if a >= floor else 0)
    return alpha.getbbox()


def cut_logo():
    source = Image.open(LOGO_SOURCE).convert("RGBA")
    box = lit_box(source, TRIM_ALPHA)
    if box is None:
        sys.exit(f"{LOGO_SOURCE} is empty")

    trimmed = source.crop(box)
    k = (LOGO_W - LOGO_PAD * 2) / trimmed.width
    drawn = premultiplied_resize(trimmed, (LOGO_W - LOGO_PAD * 2,
                                           max(1, int(round(trimmed.height * k)))))

    out = Image.new("RGBA", (LOGO_W, drawn.height + LOGO_PAD * 2), (0, 0, 0, 0))
    out.alpha_composite(drawn, (LOGO_PAD, LOGO_PAD))
    return out


def cut_guide():
    """Every reel, or None when this machine does not have the pack."""
    if not PACK.is_dir():
        return None

    sources = {}
    for reel, (folder, stem) in REELS.items():
        frames = []
        for i in range(FRAMES):
            path = PACK / folder / f"{stem}{i:02d}.png"
            if not path.exists():
                sys.exit(f"the pack has no {path}")
            frames.append(Image.open(path).convert("RGBA"))
        sources[reel] = frames

    # One crop for both reels: the union of every frame's lit box.
    left = top = 10 ** 9
    right = bottom = -1
    size = None
    for frames in sources.values():
        for frame in frames:
            if size is None:
                size = frame.size
            elif frame.size != size:
                sys.exit("the pack's frames are not all one size; a shared crop would misalign them")
            box = lit_box(frame, 1)
            if box is None:
                continue
            left, top = min(left, box[0]), min(top, box[1])
            right, bottom = max(right, box[2]), max(bottom, box[3])

    crop = (max(0, left - GUIDE_PAD), max(0, top - GUIDE_PAD),
            min(size[0], right + GUIDE_PAD), min(size[1], bottom + GUIDE_PAD))

    return {reel: [frame.crop(crop) for frame in frames] for reel, frames in sources.items()}


# ------------------------------------------------------------------ writing
def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def meta(template, identity):
    guid = hashlib.md5(("glimmer.sortie." + identity).encode("utf8")).hexdigest()
    out = []
    for line in template.read_text(encoding="utf8").splitlines(keepends=True):
        out.append(f"guid: {guid}\n" if line.startswith("guid: ") else line)
    return "".join(out)


def pieces():
    """(path, image, template, identity) for everything this machine can cut."""
    made = [(LOGO_OUT, cut_logo(), LOGO_TEMPLATE, "logo")]

    guide = cut_guide()
    if guide is not None:
        for reel, frames in guide.items():
            for i, frame in enumerate(frames):
                made.append((GUIDE_OUT / reel / f"f{i:02d}.png", frame, GUIDE_TEMPLATE,
                             f"guide.{reel}.{i:02d}"))

    return made, guide is not None


def committed_guide():
    return [GUIDE_OUT / reel / f"f{i:02d}.png" for reel in REELS for i in range(FRAMES)]


def corner_faults(frames):
    """Every frame that draws inside the corner `GuideDock` tucks the bubble into."""
    faults = []
    for path, image in frames:
        w, h = image.size
        box = (0, 0, int(w * HEAD_CLEAR + 0.999), int(h * (1.0 - GUN_LINE) + 0.999))
        corner = image.crop(box).getchannel("A")
        if corner.point(lambda a: 255 if a > EMPTY_ALPHA else 0).getbbox() is not None:
            faults.append(f"{path.relative_to(ROOT)} draws inside the bubble's corner {box}")
    return faults


def folder_meta(folder, identity):
    """A folder's own `.meta`. Unity writes one for every folder; a tool that makes the folder
    with the Editor closed must too, or the first import invents a guid of its own."""
    guid = hashlib.md5(("glimmer.sortie.folder." + identity).encode("utf8")).hexdigest()
    return ("fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "folderAsset: yes\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n")


def contact(made):
    """Every piece on one sheet over the game's wall colour, at its own size."""
    pad = 24
    logo = made[0][1]
    frames = [image for _, image, _, _ in made[1:]]

    cols = 10
    fw = max((f.width for f in frames), default=0)
    fh = max((f.height for f in frames), default=0)
    rows = (len(frames) + cols - 1) // cols

    width = max(logo.width, cols * (fw + pad)) + pad * 2
    height = logo.height + pad * 3 + rows * (fh + pad)
    sheet = Image.new("RGBA", (width, height), (14, 38, 81, 255))
    sheet.alpha_composite(logo, (pad, pad))

    for i, frame in enumerate(frames):
        x = pad + (i % cols) * (fw + pad)
        y = logo.height + pad * 2 + (i // cols) * (fh + pad)
        sheet.alpha_composite(frame, (x, y))

    out = ROOT / "out" / "sortie_art.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print(f"wrote {out}")


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true",
                        help="prove the shipped PNGs are what this tool cuts")
    parser.add_argument("--contact", action="store_true",
                        help="draw every piece on one sheet")
    args = parser.parse_args()

    made, has_pack = pieces()

    if not has_pack:
        missing = [str(p.relative_to(ROOT)) for p in committed_guide() if not p.exists()]
        if missing:
            sys.exit("the monster pack is not on this machine and these are not committed: "
                     + ", ".join(missing[:4]) + ("..." if len(missing) > 4 else ""))
        print("sortie art: no monster pack on this machine; the committed guide reels stand")

    if args.contact:
        contact(made)
        return 0

    if args.check:
        shipped = [(p, Image.open(p).convert("RGBA")) for p in committed_guide() if p.exists()]
        bad = corner_faults(shipped)
        for path, image, _, _ in made:
            if not path.exists():
                bad.append(f"{path.relative_to(ROOT)} is missing")
            elif path.read_bytes() != png_bytes(image):
                bad.append(f"{path.relative_to(ROOT)} is not what this tool cuts")
            if not Path(str(path) + ".meta").exists():
                bad.append(f"{path.relative_to(ROOT)}.meta is missing")
        if bad:
            for line in bad:
                print(line, file=sys.stderr)
            print("re-run without --check", file=sys.stderr)
            return 1
        print(f"sortie art: {len(made)} piece(s), reproducible")
        return 0

    for folder, identity in ((GUIDE_OUT, "guide"),
                             (GUIDE_OUT / "idle", "guide.idle"),
                             (GUIDE_OUT / "walk", "guide.walk")):
        if not has_pack:
            break
        folder.mkdir(parents=True, exist_ok=True)
        side = Path(str(folder) + ".meta")
        if not side.exists():
            side.write_text(folder_meta(folder, identity), encoding="utf8", newline="\n")

    for path, image, template, identity in made:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(png_bytes(image))
        side = Path(str(path) + ".meta")
        if not side.exists():
            side.write_text(meta(template, identity), encoding="utf8", newline="\n")

    logo = made[0][1]
    print(f"  gemfire_logo: {logo.width}x{logo.height}")
    if has_pack:
        frame = made[1][1]
        print(f"  guide reels: 2 x {FRAMES} frames at {frame.width}x{frame.height}")
    print(f"sortie art: wrote {len(made)} piece(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
