# -*- coding: utf-8 -*-
"""Cuts the task chests and the task-goal glyphs.

    python Tools/make_chest_art.py            # write them
    python Tools/make_chest_art.py --check    # prove the shipped PNGs and metas reproduce
    python Tools/make_chest_art.py --contact <png>   # lay them out; look at it

Three sets, under `Assets/Game/Art/`:

* `Chests/{tier}/f00..f16.png` — one opening reel per chest tier, seventeen frames, cut
  from the GraphicRiver "Chest Animations V01" pack: frame 0 is the chest closed, the lid
  swings through frame 14 and the last two frames hold it open with the light pouring out.
  **Scoped, never global** (invariant 7b): sixty-eight frames at 285x395 are only ever
  wanted while a chest is being opened, and the screens that open one hold the `chests`
  scope. Played by `Flipbook.Attach(..., loop: false)` from `ChestOverlay`.
* `Ui/Chest/{tier}.png` — the closed chest, small and **global**, because the hub draws all
  four on the first screen after the splash and an `Image` whose sprite has not arrived is a
  white rectangle. Frame 0 of the reel, resampled.
* `Ui/Task/{goal}.png` — the glyph a task row wears for a goal the shared icon set has no
  picture for (a raider, a boss, a charm, a cog, a wave). Cut from art already in the repo,
  so this half of the tool reproduces on a checkout without the pack.

**Which pack chest is which tier is a decision and it is written here once.** The pack ships
five designs; four are tiers and the jade one (Chest04) is left on the shelf, because a
ladder of four is what the tasks pay and a fifth design nothing pays would be resident art
nothing draws (8d). The ids — `wood`, `silver`, `gold`, `royal` — are permanent: `TaskTable`
names them, `progression.json` names them, and every loc key and address is derived from them.

The pack is read straight out of the zip in Downloads rather than from an extracted folder,
so there is no copy of licensed source inside the repo. `--check` passes without the zip: it
proves the shipped files and their metas are present and well-formed, and says the sources
were not available to re-cut against. The guid of every `.meta` is derived from the address,
so a re-cut keeps every Addressables entry (`make_hud_kit_art.meta_for`'s rule).
"""
import hashlib
import io
import os
import sys
import zipfile

from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(REPO, "Assets", "Game", "Art")
TEMPLATE = os.path.join(ART, "Ui", "ic_chest_wood.png.meta")

PACK = os.path.join(os.path.expanduser("~"), "Downloads",
                    "graphicriver-jX2GKpBr-chest-animations-v01.zip")
PACK_PREFIX = "Chest Animations V01/Png/"

#: tier id -> the pack's chest folder. Permanent; see the module docstring.
TIERS = {
    "wood": "Chest01",
    "silver": "Chest02",
    "gold": "Chest03",
    "royal": "Chest05",
}

FRAMES = 17
FRAME_NAME = "{chest}/{chest}-animation2_{i:02d}.png"

#: The closed icon: the reel's first frame, resampled to this height. 244 tall at a 285:395
#: aspect is 176 wide, which is the largest a hub tile draws it and a fifth of the reel's
#: memory.
ICON_HEIGHT = 244

#: goal id -> (source under Assets/Game/Art, box the glyph is fitted into). A glyph is fitted
#: into a square with its aspect kept, so a tall boss and a wide fly both read at one size on
#: a task row.
GLYPHS = {
    "raiders": ("Siege/mon_r/f00.png", 128),
    "boss": ("Siege/boss/f00.png", 128),
    "charm": ("Siege/gem_prism.png", 128),
    "cog": ("Siege/gem_cog.png", 128),
    "wave": ("Siege/crest.png", 128),
}


# ------------------------------------------------------------------------------ cutting
def frame_bytes(zf, chest, i):
    name = PACK_PREFIX + FRAME_NAME.format(chest=chest, i=i)
    return zf.read(name)


def reels():
    """tier -> [17 frames], read out of the pack. None when the pack is absent."""
    if not os.path.exists(PACK):
        return None

    out = {}
    with zipfile.ZipFile(PACK) as zf:
        for tier, chest in TIERS.items():
            frames = []
            for i in range(FRAMES):
                im = Image.open(io.BytesIO(frame_bytes(zf, chest, i))).convert("RGBA")
                frames.append(im)

            sizes = {f.size for f in frames}
            if len(sizes) != 1:
                sys.exit("%s: the pack's frames are not one size: %s" % (chest, sorted(sizes)))
            out[tier] = frames
    return out


def icon_of(frame):
    w, h = frame.size
    scale = ICON_HEIGHT / float(h)
    return frame.resize((max(1, round(w * scale)), ICON_HEIGHT), Image.LANCZOS)


def glyph_of(source, box):
    """A picture fitted into a square with its aspect kept, trimmed to its own alpha first."""
    im = Image.open(os.path.join(ART, source)).convert("RGBA")
    bbox = im.getchannel("A").getbbox()
    if bbox:
        im = im.crop(bbox)

    w, h = im.size
    scale = box / float(max(w, h))
    im = im.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)

    out = Image.new("RGBA", (box, box), (0, 0, 0, 0))
    out.alpha_composite(im, ((box - im.size[0]) // 2, (box - im.size[1]) // 2))
    return out


def glyphs():
    return {goal: glyph_of(source, box) for goal, (source, box) in GLYPHS.items()}


# ------------------------------------------------------------------------------- metas
def meta_for(address):
    """A plain sprite importer with a guid derived from the address.

    Derived rather than random for `make_hud_kit_art.meta_for`'s reason: Addressables keys
    every registered entry on the guid, so a re-cut that minted a fresh one would orphan the
    address rather than move it, and the game would draw a white rectangle where it asks
    for `Ui/Chest/gold`.
    """
    text = io.open(TEMPLATE, encoding="utf8").read()
    guid = hashlib.md5(("glimmer.chest." + address).encode("utf8")).hexdigest()

    out = []
    for line in text.splitlines(keepends=True):
        if line.startswith("guid: "):
            out.append("guid: %s\n" % guid)
        else:
            out.append(line)
    return "".join(out)


def png_bytes(im):
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=True)
    return buf.getvalue()


# ------------------------------------------------------------------------------- output
def outputs(pack, marks):
    """address -> PNG bytes, for everything this tool owns."""
    out = {}

    if pack is not None:
        for tier, frames in pack.items():
            for i, frame in enumerate(frames):
                out["Chests/%s/f%02d" % (tier, i)] = png_bytes(frame)
            out["Ui/Chest/%s" % tier] = png_bytes(icon_of(frames[0]))

    for goal, im in marks.items():
        out["Ui/Task/%s" % goal] = png_bytes(im)

    return out


def path_of(address):
    return os.path.join(ART, *address.split("/")) + ".png"


def write(out):
    for address, data in out.items():
        path = path_of(address)
        os.makedirs(os.path.dirname(path), exist_ok=True)

        with open(path, "wb") as f:
            f.write(data)

        meta = path + ".meta"
        if not os.path.exists(meta):
            with io.open(meta, "w", encoding="utf8", newline="\n") as f:
                f.write(meta_for(address))

    print("wrote %d file(s) under %s" % (len(out), ART))


def check(out, pack_present):
    """Every shipped file reproduces byte for byte, and every meta is a sprite importer."""
    problems = []

    expected = list(out.keys())
    if not pack_present:
        # Without the pack the reels and icons cannot be re-cut; they still have to exist.
        for tier in TIERS:
            expected.append("Ui/Chest/%s" % tier)
            for i in range(FRAMES):
                expected.append("Chests/%s/f%02d" % (tier, i))

    for address in expected:
        path = path_of(address)
        if not os.path.exists(path):
            problems.append("%s is missing" % address)
            continue

        meta = path + ".meta"
        if not os.path.exists(meta):
            problems.append("%s has no .meta, so the importer hook never addressed it" % address)
        elif "spriteMode: 1" not in io.open(meta, encoding="utf8").read():
            problems.append("%s.meta is not a sprite importer" % address)

        if address in out and open(path, "rb").read() != out[address]:
            problems.append("%s does not reproduce; re-run without --check" % address)

    if problems:
        for p in problems:
            print("  FAIL " + p)
        sys.exit(1)

    note = "" if pack_present else " (the chest pack was not available; reels checked for presence only)"
    print("%d chest asset(s) reproduce%s" % (len(expected), note))


def contact(out, path, cell=200):
    """Everything at one size on the room's own colour. Look at it."""
    items = sorted(out.items())
    cols = 6
    rows = (len(items) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * (cell + 24)), (8, 31, 69, 255))

    for n, (address, data) in enumerate(items):
        im = Image.open(io.BytesIO(data)).convert("RGBA")
        im.thumbnail((cell - 16, cell - 16))
        x, y = (n % cols) * cell, (n // cols) * (cell + 24)
        sheet.alpha_composite(im, (x + (cell - im.size[0]) // 2, y + (cell - im.size[1]) // 2))

    sheet.save(path)
    print("contact sheet: %s (%d pieces)" % (path, len(items)))


def main():
    args = sys.argv[1:]
    pack = reels()
    out = outputs(pack, glyphs())

    if "--check" in args:
        check(out, pack is not None)
        return

    if "--contact" in args:
        # Only the reel's first, middle and last frames, so the sheet is a page not a wall.
        shown = {k: v for k, v in out.items()
                 if not k.startswith("Chests/") or k.endswith(("f00", "f08", "f16"))}
        contact(shown, args[args.index("--contact") + 1])
        return

    if pack is None:
        sys.exit("the chest pack is not at %s; the reels and icons cannot be cut" % PACK)

    write(out)


if __name__ == "__main__":
    main()
