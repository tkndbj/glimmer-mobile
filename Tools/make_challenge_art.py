#!/usr/bin/env python3
"""Cuts the daily challenges' owner-supplied genre pictures into Assets/Game/Art/Ui/.

    python Tools/make_challenge_art.py --source C:/Users/Digikey/Downloads   # re-cut from the artwork
    python Tools/make_challenge_art.py --check                               # prove what ships is what this draws
    python Tools/make_challenge_art.py --contact                             # one sheet, on the card's own plate

**Why a tool rather than four files dropped in** — `make_ad_art.py`'s reason, said of a card
mark: the supplied artwork is 1.25k square and the card draws it at 176, so importing it as-is
ships fifty times the pixels for nothing. The cut is two decisions, trim to the art and fit the
long edge, and both are written down here rather than done once in an image editor.

**The address is derived from the genre's spelling** (`Ui/challenge_{spelling}`,
`ChallengeArt.GenreMark`), which is invariant 7c's shape: a genre added to the enum names its own
picture and nothing here has to be told. The four names are listed by hand in
`AssetManifest.UiSprites` so `artnames.py` can see them and so they are in the global set — the
list is the first screen a player sees after the hub and an `Image` with no sprite is a white
rectangle (7b). `ChallengeLedgerTests.EveryGenresMarkIsPreloadedAndOnDisk` walks the enum against
both, so a fifth genre with no picture fails offline rather than on a device.

**These carry their own frame**, a rounded square in the genre's colour, so the card draws
nothing around them.

**The deal furniture comes from a second source, the shop's Layer Lab pack** (`PACK_CUTS`): the
crowned chest the deal band wears and one cut stone per deal rung on the sheet. The pack has its
own default path (`make_shop_art.DEFAULT_SOURCE`), so it needs no flag; when it is absent the
shipped PNGs are taken as the artifact, as every art tool here does with a licensed source.
"""
import argparse
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Ui"

# The card draws the mark at 176 square at 1080 (`DailyChallengesScreen.MarkSize`); 384 leaves
# tablet headroom without approaching `/Art/Ui/`'s 1024 cap.
LONG_EDGE = 384

# Below this the alpha is the export's soft edge rather than the drawing (`make_ad_art.TRIM_ALPHA`).
TRIM_ALPHA = 8

# source stem -> genre spelling (`ChallengeGenres.Names`). The address is `challenge_{spelling}`.
CUTS = {
    "pairs": "pairs",
    "merge": "merge",
    "push": "sokoban",
}

# Genres whose mark is *drawn* here rather than cut from artwork: the glade took the pipes'
# seat on 2026-09-23 with no owner picture, so its card is the same frame the others wear
# (a rounded square in a colour) holding a conduit cross with a lit gem on it, composed from
# the siege's own gem and turret sprites so it sits beside the three illustrated ones. The
# day the owner supplies `glade.png`, move the spelling into CUTS and this branch is unused.
DRAWN = {
    "glade": (0x4F, 0xC1, 0xFF),   # azure frame, the glade's own accent
}

#: The siege art the drawn card composes from (committed PNGs; no licensed pack needed).
SIEGE_ART = ROOT / "Assets" / "Game" / "Art" / "Siege"

# The deal furniture, cut from the same licensed Layer Lab pack the shop's money ladders come
# from (`make_shop_art.PACK`, outside the repo — see the art-source-packs note). The band on
# the list page wears the crowned chest on its left, at the owner's instruction on 2026-09-23,
# and each row of the deal sheet wears one cut stone; three deals, three colours, in the order
# the owner showed them. **A deal's picture is keyed on its rung, never its id**
# (`ChallengeArt.DealMark`): a retune that renames `bronze` keeps its stone, and a fourth
# deal is a fourth PNG here, which `ChallengeLedgerTests.EveryShippedDealHasAStone` asks for.
# The pack is optional, as every art source is: without it the shipped PNGs are the artifact.
PACK_CUTS = {
    "chest_event": "challenge_chest",
    "gem_diamond_purple": "challenge_deal_1",
    "gem_diamond_blue": "challenge_deal_2",
    "gem_diamond_green": "challenge_deal_3",
}

# The band draws the chest at 140 and a sheet row its stone at 136 (`DailyChallengesScreen.
# ChestSize`, `ChallengeTierOverlay.StoneSize`); 256 is the same tablet headroom LONG_EDGE
# leaves the genre marks, and the 2x sources are 916 and 462 wide, so both are a downscale.
PACK_EDGE = 256


def pack_folder():
    """Where the shop pack's 2x sprites are, or None when this checkout has no pack."""
    sys.path.insert(0, str(ROOT / "Tools"))
    import make_shop_art
    folder = make_shop_art.DEFAULT_SOURCE / make_shop_art.PACK
    return folder if folder.exists() else None


def address(spelling):
    return "challenge_" + spelling


def draw_glade(frame_rgb):
    """The glade's card: a framed board of four conduits, one lit, a gem on the hub and the
    blue turret in the corner, at LONG_EDGE square."""
    from PIL import Image, ImageDraw

    size = LONG_EDGE
    card = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(card)

    def rounded(box, radius, fill):
        d.rounded_rectangle(box, radius=radius, fill=fill)

    # The frame and the board's floor, both rounded squares like the owner's marks.
    rim = int(size * .07)
    rounded((0, 0, size - 1, size - 1), int(size * .16), (*frame_rgb, 255))
    rounded((rim, rim, size - 1 - rim, size - 1 - rim), int(size * .12), (14, 42, 74, 255))

    # A 3x3 of conduits: the middle row lit from a red crystal on the left to a critter ring
    # on the right, the rest dormant.
    cell = (size - 2 * rim) / 3.0
    thick = int(cell * .26)
    dormant, lit = (58, 80, 100, 255), (242, 64, 79, 255)

    def centre(cx, cy):
        return rim + (cx + .5) * cell, rim + (cy + .5) * cell

    arms = {(0, 1): "E", (1, 1): "EW", (2, 1): "W", (1, 0): "S", (1, 2): "NE", (2, 0): "SW", (0, 2): "NE", (2, 2): "NW", (0, 0): "ES"}
    for (cx, cy), a in arms.items():
        x, y = centre(cx, cy)
        colour = lit if cy == 1 else dormant
        for letter in a:
            ox, oy = {"N": (0, -1), "E": (1, 0), "S": (0, 1), "W": (-1, 0)}[letter]
            x2, y2 = x + ox * cell * .5, y + oy * cell * .5
            d.line([(x, y), (x2, y2)], fill=colour, width=thick)
        r = thick * .65
        d.ellipse([x - r, y - r, x + r, y + r], fill=colour)

    # The crystal (a red gem) on the left, the critter's ring on the right, and the blue turret
    # standing in the bottom corner as every other card's turret does.
    def sprite(rel):
        p = SIEGE_ART / rel
        return Image.open(p).convert("RGBA") if p.exists() else None

    gem = sprite("gem_r.png")
    if gem is not None:
        g = int(cell * .62)
        gem = gem.resize((g, g), Image.LANCZOS)
        x, y = centre(0, 1)
        card.alpha_composite(gem, (int(x - g / 2), int(y - g / 2)))

    x, y = centre(2, 1)
    r = cell * .30
    d.ellipse([x - r, y - r, x + r, y + r], outline=(255, 244, 206, 255), width=int(cell * .07))

    turret = sprite("Wards/bolt_b.png")
    if turret is not None:
        t = int(size * .42)
        scale = t / float(max(turret.size))
        turret = turret.resize((max(1, int(turret.width * scale)), max(1, int(turret.height * scale))), Image.LANCZOS)
        card.alpha_composite(turret, (rim + int(size * .02), size - rim - turret.height + int(size * .04)))

    return card


def load(source, stem):
    from PIL import Image

    path = pathlib.Path(source) / (stem + ".png")
    if not path.exists():
        sys.exit(f"no artwork at {path}")

    return Image.open(path).convert("RGBA")


def cut(image, edge=LONG_EDGE):
    """Trim to the drawing, then fit the long edge. Nothing else — no recolour, no grade."""
    from PIL import Image

    alpha = image.split()[-1]
    box = alpha.point(lambda v: 255 if v >= TRIM_ALPHA else 0).getbbox()
    if box is None:
        sys.exit("the artwork is entirely transparent")

    art = image.crop(box)

    w, h = art.size
    scale = edge / float(max(w, h))
    if scale < 1.0:
        art = art.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)

    return art


def packed():
    """The deal furniture, cut from the shop pack; empty when the pack is not on this machine."""
    from PIL import Image

    folder = pack_folder()
    if folder is None:
        return {}
    made = {}
    for stem, name in PACK_CUTS.items():
        path = folder / (stem + ".png")
        if not path.exists():
            sys.exit(f"the shop pack has no {stem}.png at {path}")
        made[name] = cut(Image.open(path).convert("RGBA"), PACK_EDGE)
    return made


def same(path, art):
    """Whether the PNG on disk is this picture, byte for byte."""
    from PIL import Image

    if not path.exists():
        return False
    shipped = Image.open(path).convert("RGBA")
    return shipped.size == art.size and shipped.tobytes() == art.tobytes()


def drawn():
    """The marks this tool draws itself, which need no source."""
    return {address(spelling): draw_glade(rgb) for spelling, rgb in DRAWN.items()}


def draw(source):
    made = {address(spelling): cut(load(source, stem)) for stem, spelling in CUTS.items()}
    made.update(drawn())
    return made


def spellings():
    return sorted(list(CUTS.values()) + list(DRAWN.keys()))


def contact():
    """One sheet, each mark on the card's own plate, at the size the card draws it."""
    from PIL import Image

    sys.path.insert(0, str(ROOT / "Tools"))
    import hudkit as K

    cell, pad = 300, 24
    # Two rows: the genre marks on the card's plate at the card's size, and the deal furniture
    # on the band's plate at the band's size, so each is judged on the ground it is seen on.
    rows = [([address(s) for s in spellings()], "Hud/plate_blue", 216),
            (list(PACK_CUTS.values()), "Hud/plate_navy", 140)]
    cols = max(len(names) for names, _, _ in rows)
    sheet = Image.new("RGBA", (cols * (cell + pad) + pad, len(rows) * (cell + pad) + pad), (0, 0, 0, 0))

    for r, (names, plate, drawn_at) in enumerate(rows):
        for i, name in enumerate(names):
            x = pad + i * (cell + pad)
            y = pad + r * (cell + pad)
            K.paste(sheet, K.skin(plate, cell, cell), x + cell / 2, y + cell / 2)
            path = OUT / (name + ".png")
            if not path.exists():
                K.text(sheet, "missing", x + cell / 2, y + cell / 2, 24, fill=(255, 120, 120), outline=2)
            else:
                art = Image.open(path).convert("RGBA")
                K.paste(sheet, K.fit(art, (drawn_at, drawn_at)), x + cell / 2, y + cell / 2 - 14)
            K.text(sheet, name, x + cell / 2, y + cell - 26, 20, fill=(255, 245, 225), outline=2)

    out = ROOT / "out" / "challenge_art.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print(f"wrote {out}  {sheet.width}x{sheet.height}  - look at it")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", help="folder holding pairs.png, pipe.png, merge.png and push.png")
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped PNGs are what this cuts from --source, and change nothing")
    ap.add_argument("--contact", action="store_true", help="draw the sheet and change nothing")
    args = ap.parse_args()

    if args.contact:
        contact()
        return

    # The deal furniture rides on every run: the pack is a second source with its own
    # default, so it needs no flag, and it is proved (or written) whenever it is present.
    pack = packed()
    if not pack:
        print("  (no shop pack on this machine; the deal chest and stones are taken as shipped)")

    if not args.source:
        # The source lives outside the repo (`art-source-packs`); without it the shipped PNGs are
        # the artifact and there is nothing to compare against. Reproducibility is proved with
        # `--source` plus a clean `git status` — except for the drawn marks, which need no
        # source and are proved (or written) here either way.
        made = drawn()
        made.update(pack)
        if args.check:
            wanted = [address(s) for s in spellings()] + list(PACK_CUTS.values())
            missing = [name for name in wanted if not (OUT / (name + ".png")).exists()]
            if missing:
                sys.exit("missing on disk: " + ", ".join(missing))
            drift = [name for name, art in made.items() if not same(OUT / (name + ".png"), art)]
            if drift:
                sys.exit("not what this draws: " + ", ".join(drift))
            print(f"challenge art: {len(wanted)} picture(s) on disk, {len(made)} drawn or cut from the pack "
                  "and reproducible; --source is needed to prove the owner's marks")
            return

        OUT.mkdir(parents=True, exist_ok=True)
        for name, art in made.items():
            art.save(OUT / (name + ".png"))
            print(f"  {name:<20} {art.width}x{art.height}  ({'pack' if name in pack else 'drawn'})")
        print(f"drew {len(made)} picture(s) into {OUT}; --source cuts the rest; then Addressables > Sync All Assets and save")
        return

    made = draw(args.source)
    made.update(pack)

    if args.check:
        drift = [name for name, art in made.items() if not same(OUT / (name + ".png"), art)]
        if drift:
            sys.exit("not what this cuts: " + ", ".join(drift))
        print(f"challenge art: {len(made)} picture(s), reproducible")
        return

    OUT.mkdir(parents=True, exist_ok=True)
    for name, art in made.items():
        art.save(OUT / (name + ".png"))
        print(f"  {name:<20} {art.width}x{art.height}")
    print(f"cut {len(made)} picture(s) into {OUT}; then Addressables > Sync All Assets and save")


if __name__ == "__main__":
    main()
