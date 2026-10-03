# -*- coding: utf-8 -*-
"""Cuts the Pairs challenge's art: the card a gem hides under, the face it is shown on, the
twenty-four gems a board is dealt from, and the cursed stone.

    python Tools/make_pairs_art.py            # write every PNG
    python Tools/make_pairs_art.py --check    # prove the committed PNGs are what this cuts
    python Tools/make_pairs_art.py --contact  # out/pairs_art.png, the sheet to judge by eye

**A pair is a picture, never a colour** (CLAUDE.md 56m), **and no two pictures look alike**
(2026-10-02, the owner: "many same colored gems ... makes the game very hard to remember"). The
first cut matched on the four turret colours alone; the second gave each colour six stones
pulled onto that colour's hue, so a seventeen-pair board held five or six reds told apart by
silhouette alone - a test of eyesight rather than of memory. Now each of the twenty-four is its
own *thing* - a pocket watch, a key, a heart, a padlock, a potion, a star, an orb - in the
colours its artist painted, cut from the match-3 jewellery set (`MATCH3`) and topped up from
the RPG gem pack (`siege.GEMPACK`) with stones whose colour and shape repeat nothing else on the
sheet. Left out on purpose: the set's bomb (a black ball beside the black cursed stone) and its
silver key and chest (the gold ones' silhouettes).

**The turret a card feeds is a pip, not the picture's colour.** A picture no longer wears its
turret's hue, so each carries a small disc of that hue in its corner (`PIP`), baked into the
PNG so the view did not change. Which picture feeds which turret is only the row token (`r1` ..
`y6`) and the order of `GEMS`; the rules never see a picture.

**The card is drawn, not cut**, in the owner's own picture of this genre
(`Art/Ui/challenge_pairs`): a stone frame, a royal-blue back with a crown on it, a cream face.
Drawn at four times size and reduced, so the edges are clean at every size a phone draws it.

The packs are optional: without them `--check` still passes on the committed pictures (every art tool
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

import make_siege_art as siege  # noqa: E402  (the gem pack's root)

OUT = REPO / "Assets" / "Game" / "Art" / "Challenge"
CONTACT = REPO / "out" / "pairs_art.png"

#: One gem, in pixels: the siege's own gem size, drawn at about 0.6 of a cell.
GEM = 192

#: One card, in pixels. A card fills a cell (up to 200 units), so 256 is not an upscale on any
#: phone this game ships to; `ArtImportRules.Caps` holds the folder to it.
CARD = 256

#: The supersampling a card is drawn at before it is reduced.
SS = 4

#: The match-3 jewellery set most of the pictures are cut from.
MATCH3 = Path(r"C:\Users\Digikey\Downloads\craftpix-net-298179-match-3-game-asset-set")

#: Which picture each card is cut from, per turret colour, variant 1 to 6: (pack, file, what it
#: is), the pack being `m3` (`MATCH3`) or `gem` (`siege.GEMPACK`). The variant is the digit in a
#: row's token (`r1` .. `y6`, `PairsGems.TryParse`) and names the file (`pair_r1`), so the order
#: here is permanent once a row names it - a re-cut that swaps two lines swaps two pictures under
#: every shipped board. Leaned toward the turret's colour where a picture allowed; the pip says
#: the rest.
GEMS = {
    "r": [("m3", "8.png", "red heart"), ("gem", "89.png", "red pyramid"),
          ("gem", "57.png", "crimson cushion"), ("m3", "chest gold.png", "treasure chest"),
          ("m3", "magnet.png", "magnet"), ("m3", "4.png", "magenta square")],
    "g": [("m3", "5.png", "green octagon"), ("gem", "74.png", "emerald drop"),
          ("gem", "100.png", "olive egg"), ("m3", "3.png", "rainbow stone"),
          ("m3", "padlock.png", "padlock"), ("m3", "2.png", "pearl octagon")],
    "b": [("m3", "1.png", "cyan diamond"), ("m3", "7.png", "blue rhombus"),
          ("gem", "28.png", "rune stone"), ("gem", "92.png", "ice star"),
          ("m3", "potion.png", "potion"), ("gem", "23.png", "galaxy orb")],
    "y": [("m3", "6.png", "amber bar"), ("gem", "13.png", "ore nugget"),
          ("gem", "31.png", "gold orb"), ("m3", "clock.png", "pocket watch"),
          ("m3", "key gold.png", "gold key"), ("m3", "mosaic.png", "puzzle piece")],
}

#: The pip a card carries for the turret it feeds: the siege's four ward colours
#: (`make_siege_art.WARD_HUES`, as `SiegeView.TintOf` draws them).
PIP = {"r": (242, 64, 79), "g": (123, 216, 106), "b": (79, 193, 255), "y": (255, 138, 43)}

#: Below this alpha a source pixel is the vendor's soft glow round an object rather than the
#: object, and is dropped - or a glowing key is framed to its halo and drawn small inside it.
HALO = 72

#: The cursed stone: the gem pack's one black jewel, cut untouched and wearing no pip - it is
#: no colour and feeds no turret.
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
def trimmed(im, room=GEM - 12):
    """A picture cropped to its own ink and centred on a square of `GEM`, a margin all round."""
    box = im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    if box:
        im = im.crop(box)
    scale = min(room / im.width, room / im.height)
    im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
    out = Image.new("RGBA", (GEM, GEM), (0, 0, 0, 0))
    out.alpha_composite(im, ((GEM - im.width) // 2, (GEM - im.height) // 2))
    return out


def dehaloed(im):
    """The vendor's glow taken off: alpha under `HALO` goes and the rest is re-ramped to full."""
    a = np.asarray(im.getchannel("A")).astype(np.float32)
    a = np.clip((a - HALO) * 255.0 / (255 - HALO), 0, 255).astype(np.uint8)
    out = im.copy()
    out.putalpha(Image.fromarray(a, "L"))
    return out


def pip(letter):
    """The turret's disc in the lower right corner: its colour, a dark rim and a highlight."""
    s = SS
    W = GEM * s
    layer = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    r = 27 * s
    cx = cy = W - r - 3 * s
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=STONE_OUT + (255,))
    ri = r - 5 * s
    d.ellipse((cx - ri, cy - ri, cx + ri, cy + ri), fill=PIP[letter] + (255,))
    hl = ri * .42
    hx, hy = cx - ri * .3, cy - ri * .3
    d.ellipse((hx - hl, hy - hl, hx + hl, hy + hl), fill=(255, 255, 255, 120))
    return layer.resize((GEM, GEM), Image.LANCZOS)


def cut_gem(letter, pack, file):
    root = siege.GEMPACK if pack == "gem" else MATCH3
    im = Image.open(root / "PNG" / file).convert("RGBA")
    if letter is None:
        return trimmed(im)
    out = trimmed(dehaloed(im), GEM - 24)
    out.alpha_composite(pip(letter))
    return out


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
    missing = [str(p) for p in (siege.GEMPACK, MATCH3) if not (p / "PNG").is_dir()]
    if missing:
        if pack_needed:
            sys.exit(f"not on this machine: {', '.join(missing)}; the pictures cannot be cut")
        return out, False
    for letter, stones in GEMS.items():
        for n, (pack, file, _) in enumerate(stones, 1):
            out[f"pair_{letter}{n}.png"] = cut_gem(letter, pack, file)
    out["pair_curse.png"] = cut_gem(None, "gem", CURSE[0])
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
    on_disk = sorted(p.name for p in OUT.glob("pair*.png")) if OUT.exists() else []
    if len(on_disk) != expected:
        bad.append(f"{len(on_disk)} picture(s) on disk, the tool cuts {expected}")
    if not gems:
        print("a source pack is absent: the cards were checked, the committed pictures were not")
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
