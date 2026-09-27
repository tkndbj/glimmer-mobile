# -*- coding: utf-8 -*-
"""Cuts the Pairs challenge's art: the card a gem hides under, the face it is shown on, the
twenty-four gems a board is dealt from, and the cursed stone.

    python Tools/make_pairs_art.py            # write every PNG
    python Tools/make_pairs_art.py --check    # prove the committed PNGs are what this cuts
    python Tools/make_pairs_art.py --contact  # out/pairs_art.png, the sheet to judge by eye

**A pair is a gem, never a colour** (CLAUDE.md 56m). The first cut matched on the four turret
colours alone, so a 6x3 board held four or five of each and any two reds paired - a memory game
with nothing to remember. Now each colour is six *different* stones, cut from the one licensed
pack the siege's own gems come from (`make_siege_art.GEMPACK`), so a red heart and a red kite
are two cards that both feed the red turret and do not match each other. That is the whole
difficulty dial a memory game has, and it costs pictures rather than code.

**Six silhouettes a colour, and the silhouette is what is remembered.** Within a colour every
stone is a different shape - heart, drop, hexagon, triangle, brilliant, kite - because two reds
told apart by shading alone would be a test of eyesight rather than of memory. Across colours
shapes may repeat; the colour already separates them.

**Pulled onto the four turret hues rather than cut as the pack paints them** (`hued`, the
function the siege's wards and charms are painted with), so a card's colour *is* the turret it
feeds: a stone that came out pink beside the poppy red would be inventing a fifth colour on a
board where the colour of a pair is a rule. The pull is partial, so each stone keeps its own
highlights and warm and cool notes.

**The card is drawn, not cut**, in the owner's own picture of this genre
(`Art/Ui/challenge_pairs`): a stone frame, a royal-blue back with a crown on it, a cream face.
Drawn at four times size and reduced, so the edges are clean at every size a phone draws it.

The pack is optional: without it `--check` still passes on the committed gems (every art tool
here passes with its licensed source absent, because the PNGs are committed), and the two cards
need no source at all.
"""
from __future__ import annotations

import argparse
import io
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "Tools"))

import make_siege_art as siege  # noqa: E402  (the pack root and `hued`)

OUT = REPO / "Assets" / "Game" / "Art" / "Challenge"
CONTACT = REPO / "out" / "pairs_art.png"

#: One gem, in pixels: the siege's own gem size, drawn at about 0.6 of a cell.
GEM = 192

#: One card, in pixels. A card fills a cell (up to 200 units), so 256 is not an upscale on any
#: phone this game ships to; `ArtImportRules.Caps` holds the folder to it.
CARD = 256

#: The supersampling a card is drawn at before it is reduced.
SS = 4

#: The four turret hues, as the siege paints its wards (`make_siege_art.WARD_HUES`).
HUES = dict(siege.WARD_HUES)

#: How far a stone is pulled toward its turret's hue. Most of the way - a card's colour is a
#: rule - but not all of it, so a stone keeps the pack's own shading.
PULL = 0.82

#: Which stone each gem is cut from, per colour, variant 1 to 6, and what it is. The variant is
#: the digit in a row's token (`r1` .. `y6`, `PairsGems.TryParse`) and names the file
#: (`pair_r1`), so the order here is permanent once a row names it - a re-cut that swaps two
#: lines swaps two pictures under every shipped board.
GEMS = {
    "r": [("50.png", "heart"), ("19.png", "drop"), ("1.png", "hexagon"),
          ("32.png", "triangle"), ("9.png", "round brilliant"), ("93.png", "kite")],
    "g": [("63.png", "emerald cut"), ("74.png", "drop"), ("65.png", "round"),
          ("77.png", "rhombus"), ("18.png", "trapezoid"), ("58.png", "trillion")],
    "b": [("10.png", "hexagon"), ("83.png", "triangle"), ("92.png", "star"),
          ("26.png", "rhombus"), ("82.png", "orb"), ("95-2.png", "crystal")],
    "y": [("60.png", "hexagon"), ("51.png", "oval"), ("24.png", "ingot"),
          ("75.png", "kite"), ("2.png", "trillion"), ("14.png", "nugget")],
}

#: The cursed stone: the pack's one black jewel, cut untouched - it is no colour and feeds no
#: turret, so `hued` never sees it.
CURSE = ("53.png", "obsidian")

# ------------------------------------------------------------------ colours of the card
STONE_OUT = (27, 38, 66)        # the frame's outline and the gaps between its blocks
STONE = (168, 182, 212)
STONE_LIT = (214, 223, 240)
STONE_SHADE = (118, 133, 170)
BACK_TOP, BACK_FOOT = (38, 104, 226), (18, 64, 168)
BACK_LINE = (96, 186, 255)
FACE_TOP, FACE_FOOT = (250, 244, 224), (238, 226, 192)
FACE_LINE = (122, 92, 64)


# ------------------------------------------------------------------ gems
def trimmed(im):
    """A stone cropped to its own ink and centred on a square of `GEM`, a margin all round."""
    box = im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    if box:
        im = im.crop(box)
    room = GEM - 12
    scale = min(room / im.width, room / im.height)
    im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
    out = Image.new("RGBA", (GEM, GEM), (0, 0, 0, 0))
    out.alpha_composite(im, ((GEM - im.width) // 2, (GEM - im.height) // 2))
    return out


def cut_gem(letter, file):
    im = Image.open(siege.GEMPACK / "PNG" / file).convert("RGBA")
    if letter is not None:
        im = siege.hued(im, HUES[letter], pull=PULL, sat_gain=0.55, sat_floor=0.42,
                        val_gain=1.04, val_lift=0.03)
    return trimmed(im)


# ------------------------------------------------------------------ the card
def gradient(size, top, foot):
    h = size[1]
    ramp = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None, None]
    a = np.array(top, np.float32)[None, None, :]
    b = np.array(foot, np.float32)[None, None, :]
    rgb = a + (b - a) * ramp
    rgb = np.broadcast_to(rgb, (h, size[0], 3))
    return Image.fromarray(np.clip(rgb + 0.5, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def rounded_mask(size, box, radius):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rounded_rectangle(box, radius=radius, fill=255)
    return m


def stone_frame(s):
    """The stone rim every card wears: bevelled blocks with dark joints, as the owner drew it."""
    W = CARD * s
    im = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    outer = (2 * s, 2 * s, W - 2 * s, W - 2 * s)
    r_out = 38 * s
    rim = 22 * s

    # The outline, then the stone inside it.
    im.paste(Image.new("RGBA", (W, W), STONE_OUT + (255,)), (0, 0), rounded_mask((W, W), outer, r_out))
    body = (outer[0] + 4 * s, outer[1] + 4 * s, outer[2] - 4 * s, outer[3] - 4 * s)
    im.paste(Image.new("RGBA", (W, W), STONE + (255,)), (0, 0), rounded_mask((W, W), body, r_out - 4 * s))

    # Light from the top left: a lit band along the top and left, a shade along the foot and right.
    lit = Image.new("L", (W, W), 0)
    d = ImageDraw.Draw(lit)
    d.rounded_rectangle(body, radius=r_out - 4 * s, fill=255)
    d.rounded_rectangle((body[0] + 7 * s, body[1] + 7 * s, body[2] + 30 * s, body[3] + 30 * s),
                        radius=r_out, fill=0)
    im.paste(Image.new("RGBA", (W, W), STONE_LIT + (255,)), (0, 0), lit)

    shade = Image.new("L", (W, W), 0)
    d = ImageDraw.Draw(shade)
    d.rounded_rectangle(body, radius=r_out - 4 * s, fill=255)
    d.rounded_rectangle((body[0] - 30 * s, body[1] - 30 * s, body[2] - 7 * s, body[3] - 7 * s),
                        radius=r_out, fill=0)
    im.paste(Image.new("RGBA", (W, W), STONE_SHADE + (255,)), (0, 0), shade)

    # The joints between the blocks: three blocks along each edge, cut through the rim only.
    d = ImageDraw.Draw(im)
    # From just inside the outline to the field's edge, so a joint parts two blocks and never
    # reads as a tick drawn on the outside of the card.
    near, far = outer[0] + 4 * s, outer[0] + rim
    for k in (1, 2):
        at = outer[0] + (outer[2] - outer[0]) * k / 3
        j = 2 * s
        d.rectangle((at - j, near, at + j, far), fill=STONE_OUT + (255,))            # top
        d.rectangle((at - j, W - far, at + j, W - near), fill=STONE_OUT + (255,))    # foot
        d.rectangle((near, at - j, far, at + j), fill=STONE_OUT + (255,))            # left
        d.rectangle((W - far, at - j, W - near, at + j), fill=STONE_OUT + (255,))    # right
    return im, (outer[0] + rim, outer[1] + rim, outer[2] - rim, outer[3] - rim), r_out - rim + 6 * s


def crown(d, cx, cy, w, h, width, colour):
    """The crown on the card's back: a line drawing, as on the owner's card."""
    x0, x1 = cx - w / 2, cx + w / 2
    top, base = cy - h / 2, cy + h / 2
    pts = [(x0, base), (x0 - w * .02, top + h * .12), (cx - w * .26, cy - h * .04),
           (cx, top), (cx + w * .26, cy - h * .04), (x1 + w * .02, top + h * .12), (x1, base)]
    d.line(pts + [pts[0]], fill=colour, width=width, joint="curve")
    for p in (pts[1], pts[3], pts[5]):
        r = width * 1.1
        d.ellipse((p[0] - r, p[1] - r, p[0] + r, p[1] + r), fill=colour)


def notched(d, box, notch, width, colour):
    """A rectangle with its corners cut in, the inner border on the owner's card back."""
    x0, y0, x1, y1 = box
    n = notch
    pts = [(x0 + n, y0), (x1 - n, y0), (x1, y0 + n), (x1, y1 - n), (x1 - n, y1), (x0 + n, y1),
           (x0, y1 - n), (x0, y0 + n), (x0 + n, y0)]
    d.line(pts, fill=colour, width=width, joint="curve")


def card(kind):
    s = SS
    W = CARD * s
    frame, inner, radius = stone_frame(s)

    top, foot = (BACK_TOP, BACK_FOOT) if kind == "back" else (FACE_TOP, FACE_FOOT)
    field = gradient((W, W), top, foot)
    mask = rounded_mask((W, W), inner, radius)
    frame.paste(field, (0, 0), mask)

    d = ImageDraw.Draw(frame)
    inset = 16 * s
    box = (inner[0] + inset, inner[1] + inset, inner[2] - inset, inner[3] - inset)
    if kind == "back":
        notched(d, box, 12 * s, 4 * s, BACK_LINE + (255,))
        cx, cy = W / 2, W / 2 + 4 * s
        crown(d, cx, cy, 86 * s, 58 * s, 7 * s, BACK_LINE + (255,))
        # A sheen across the top of the field, so the back reads as a glossy card.
        sheen = Image.new("L", (W, W), 0)
        ImageDraw.Draw(sheen).polygon([(inner[0], inner[1]), (inner[2], inner[1]),
                                       (inner[2], inner[1] + 40 * s), (inner[0], inner[1] + 96 * s)], fill=46)
        sheen = Image.fromarray(np.minimum(np.asarray(sheen), np.asarray(mask)))
        frame.paste(Image.new("RGBA", (W, W), (255, 255, 255, 255)), (0, 0), sheen)
    else:
        d.rounded_rectangle(box, radius=max(4 * s, radius - inset), outline=FACE_LINE + (150,), width=3 * s)

    return frame.resize((CARD, CARD), Image.LANCZOS)


# ------------------------------------------------------------------ the set
def everything(pack_needed):
    """-> {filename: Image}. Gems only when the pack is on this machine (or asked for)."""
    out = {"pairs_back.png": card("back"), "pairs_face.png": card("face")}
    have = (siege.GEMPACK / "PNG").is_dir()
    if not have:
        if pack_needed:
            sys.exit(f"the gem pack is not at {siege.GEMPACK}; the gems cannot be cut")
        return out, False
    for letter, stones in GEMS.items():
        for n, (file, _) in enumerate(stones, 1):
            out[f"pair_{letter}{n}.png"] = cut_gem(letter, file)
    out["pair_curse.png"] = cut_gem(None, CURSE[0])
    return out, True


def png_bytes(im):
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def write():
    images, _ = everything(pack_needed=True)
    OUT.mkdir(parents=True, exist_ok=True)
    for name, im in images.items():
        (OUT / name).write_bytes(png_bytes(im))
    print(f"wrote {len(images)} picture(s) to {OUT.relative_to(REPO).as_posix()}")


def check():
    images, gems = everything(pack_needed=False)
    bad = []
    for name, im in images.items():
        path = OUT / name
        if not path.exists():
            bad.append(f"{name} is not on disk")
            continue
        got = np.asarray(Image.open(path).convert("RGBA")).astype(np.int16)
        want = np.asarray(im).astype(np.int16)
        if got.shape != want.shape or np.abs(got - want).max() > 1:
            bad.append(f"{name} is not what the tool cuts")
    expected = 2 + sum(len(v) for v in GEMS.values()) + 1
    on_disk = sorted(p.name for p in OUT.glob("*.png")) if OUT.exists() else []
    if len(on_disk) != expected:
        bad.append(f"{len(on_disk)} picture(s) on disk, the tool cuts {expected}")
    if not gems:
        print("the gem pack is absent: the cards were checked, the committed gems were not")
    if bad:
        print("\n".join(bad))
        sys.exit(1)
    print(f"ok: {len(images)} picture(s) are what the tool cuts")


def contact():
    images, _ = everything(pack_needed=False)
    if len(images) < 3:
        images.update({p.name: Image.open(p).convert("RGBA") for p in OUT.glob("pair_*.png")})
    cell = 150
    sheet = Image.new("RGBA", (cell * 7, cell * 6), (15, 42, 74, 255))
    face = images["pairs_face.png"].resize((cell - 10, cell - 10), Image.LANCZOS)
    back = images["pairs_back.png"].resize((cell - 10, cell - 10), Image.LANCZOS)
    for row, letter in enumerate("rgby"):
        sheet.alpha_composite(back, (5, row * cell + 5))
        for n in range(1, 7):
            x, y = n * cell, row * cell
            sheet.alpha_composite(face, (x + 5, y + 5))
            gem = images.get(f"pair_{letter}{n}.png")
            if gem is not None:
                g = gem.resize((int(cell * .62), int(cell * .62)), Image.LANCZOS)
                sheet.alpha_composite(g, (x + (cell - g.width) // 2, y + (cell - g.height) // 2))
    sheet.alpha_composite(face, (cell + 5, 4 * cell + 5))
    curse = images.get("pair_curse.png")
    if curse is not None:
        g = curse.resize((int(cell * .62), int(cell * .62)), Image.LANCZOS)
        sheet.alpha_composite(g, (cell + (cell - g.width) // 2, 4 * cell + (cell - g.height) // 2))
    big = images["pairs_back.png"]
    sheet.alpha_composite(big, (cell * 7 - CARD - 10, cell * 6 - CARD - 10))
    CONTACT.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(CONTACT)
    print(f"wrote {CONTACT.relative_to(REPO).as_posix()}")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--contact", action="store_true")
    a = ap.parse_args()
    if a.check:
        check()
    elif a.contact:
        contact()
    else:
        write()


if __name__ == "__main__":
    main()
