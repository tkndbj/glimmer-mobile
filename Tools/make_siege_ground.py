"""Thornwatch's hill: a plain tiled floor, laid small, out of one bought tileset.

<b>This is the sixth ground and the shortest, and the instruction that produced it was to stop
designing.</b> The five before it were a drawn wash, a grass pack, a mine floor read through ten
gradient maps, eight tilesets laid four across, and a *composition* - bedrock plates with chasms
running through them, strata of warmer stone, rockfalls banked against ledges, the raid's own
fortifications strewn about (invariants 37ab, 37aw). Each was more deliberate than the one before
it, and the owner's verdict on the last was that the design was bad and that none of it was
wanted: <em>plain tiles, and small ones</em>.

<b>So there is no composition here at all.</b> No props, no rubble, no chasms, no seams, no strata,
no scatter, no jitter, no overlap, no per-tile shading. A rung is a grid of tiles, picked at random
from one family and turned a quarter at a time, and that is the whole of it. The prop pack is not
read. What tells one rung from another is <b>which tiles and what hue</b> - nothing else, because
anything else is a decision nobody asked for.

<b>Small is a number and it is `ACROSS`.</b> Sixteen tiles across the board, against the six the
composition used and the four of the tilesets before it. On a phone that is about 74 points a tile,
roughly half a gem cell, which is the size that reads as a *floor* rather than as paving slabs.

<b>Two things that are not design and are kept.</b> The canvas is authored at the aspect the hill
band actually is, and the view envelopes rather than stretches it (37au) - that is a bug fix, and
without it every tile here is drawn as a rectangle. And every ground is renormalised onto
`GROUND_MEAN`, which sits below `CAST_VALUE` so the floor stays darker than the raiders walking
over it - which is the half of the previous round that was asked for, and is untouched.

<b>Licensed, so this tool is silent without it</b> - the same bargain `make_siege_art.py` already
strikes with the CraftPix zips. The ten PNGs it writes are committed; the sheets are not. Note the
source moved: this reads `~/Downloads/tiles`, eight loose PNGs, and no longer opens the mine zip at
all.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

#: <b>The eight top-down tile sheets, which are the floors this hill is actually paved from.</b>
#: Their own download rather than a zip, one PNG each: a 6x5 grid of thirty tiles. Eight whole
#: materials - mossy blue stone, sandstone, ice, lava, granite, grass, mossy cobble and sand -
#: against the three families the mine pack could offer, and <b>240 genuinely different tiles</b>
#: against twenty-two recoloured ten ways.
#:
#: <b>They were here all along, and losing them is worth writing down.</b> They floored this hill
#: two grounds ago; that version was laid <em>four tiles across</em> and was reported as the tiles
#: being huge, so the whole thing was replaced by a composition (37ab) and the sheets stopped being
#: read. When the composition was thrown out in turn and the brief became *plain tiles, small*, the
#: floor was rebuilt from the mine pack that happened to be open - at the right size, from the
#: wrong source. **A source rejected at the wrong size is not a source rejected.**
SHEETS = Path.home() / "Downloads" / "tiles"

#: How a sheet is divided. Every one is six by five, and the cut is taken from the sheet's own
#: alpha box because three of the eight ship a transparent margin - dividing the file instead puts
#: every seam a few pixels out and slices a strip of each tile onto its neighbour, which reads as a
#: grubby grid rather than as an error.
SHEET_COLS, SHEET_ROWS = 6, 5

#: What the board draws, at the shape and the size it is drawn at.
#:
#: <b>The aspect is a bug fix rather than a decision</b> (37au). It was authored 512x640 -
#: *portrait*, an aspect of 0.80 - and `SiegeView.Ground` stretches it into a band whose aspect is
#: 0.99 on a 21:9 phone, 1.14 on an iPhone, 1.77 on a 16:9 sheet and 2.85 on an iPad, so every
#: square tile arrived as a wide rectangle on every device this game runs on. 1024x788 is 1.30,
#: which is where a phone's band sits, and the view envelopes rather than stretching - so what
#: varies between devices is how much of the edges is cropped, and never the shape of a tile.
W, H = 1024, 788

#: How many tiles across, which is the whole of what "small" means here.
#:
#: <b>Sixteen, against the composition's six and the tilesets' four.</b> At 1024 across that is a
#: 64-pixel tile drawn about 74 points wide on a phone - roughly half a gem cell. The two answers
#: before this were both reported as *huge*; this is the size at which a tile stops reading as a
#: paving slab and starts reading as floor.
#:
#: How many rows there are is <b>not</b> here: it is derived per sheet inside `bed`, from that
#: sheet's own tile aspect, because the eight are not all cut the same. See `bed`.
ACROSS = 16


# --------------------------------------------------------------------------- the pack


def _sheet(path):
    """One sheet's thirty tiles, cut from its own alpha box.

    <b>From the alpha box rather than from the file size</b>, because three of the eight ship a
    transparent margin: dividing the file instead puts every seam a few pixels out and slices a
    strip of each tile onto its neighbour, which reads as a grubby grid rather than as an error.
    """
    im = Image.open(path).convert("RGBA")
    im = im.crop(im.getbbox())

    w, h = im.width / SHEET_COLS, im.height / SHEET_ROWS
    return [im.crop((int(c * w), int(r * h), int((c + 1) * w), int((r + 1) * h)))
            for r in range(SHEET_ROWS) for c in range(SHEET_COLS)]


def _plain(tiles):
    """A sheet's *floor* tiles, dropping the edge and transition pieces.

    <b>A sheet is not thirty floors.</b> Six by five holds the plain faces plus the pieces that end
    a floor - notched corners, the run that sits above a lava pit - and laid at random those are
    holes and orange strips scattered over a floor nobody put them on, which is the fault the mine
    pack's chasm tiles were excluded for. Two measurements, both relative to the sheet itself so no
    number here is tuned to one pack.

    <b>Coverage</b> catches the notched ones: every tile is a rounded rectangle, so all of them are
    a little transparent and only a transition piece is *much* more so. <b>Colour</b> catches the
    rest: a lava-edge tile is fully covered and simply does not look like the floor it belongs to,
    so anything far from the sheet's own median is dropped.
    """
    cover = np.array([float((np.asarray(t)[..., 3] > 128).mean()) for t in tiles])
    whole = cover >= cover.max() * .97

    hue = np.array([np.asarray(t).astype(np.float32)[..., :3]
                    .reshape(-1, 3)[np.asarray(t)[..., 3].reshape(-1) > 128].mean(0)
                    for t in tiles])
    near = np.abs(hue - np.median(hue[whole], 0)).mean(1) < 30.0

    keep = [t for t, ok in zip(tiles, whole & near) if ok]
    return keep if len(keep) >= 6 else [t for t, ok in zip(tiles, whole) if ok]


def _sheets():
    """Every sheet, by file name. None when the download is not here."""
    found = sorted(SHEETS.glob("tile*.png"))
    return {p.stem: _plain(_sheet(p)) for p in found} if found else None


# --------------------------------------------------------------------------- colour


#: How much of a sheet's own colour survives, per rung. See `damp`.
#:
#: <b>Measured rather than chosen.</b> Laid raw these sheets come out at chroma <b>8 to 24</b>
#: against the 3.6-10.1 of the floors before them, and two of them sit *on* `GROUND_CHROMA_CAP`
#: already - a lava floor is `Pal.Amber`, an ice floor is `Pal.Azure`, and a floor wearing a colour
#: the game asks you to find is a cost paid on every run (37f). Swept per sheet: about .35 keeps a
#: material unmistakably itself and lands it near 9, and the lava is held higher on purpose because
#: at .35 it stops reading as lava at all.
CALM = .35


def recolour(im, hue, sat):
    """Put a material in a colour of its own choosing rather than its own.

    <b>The counterpart to `damp`, and it is here because five materials have to cover ten rungs.</b>
    Damping keeps a sheet's own colour and only turns it down; this overwrites hue and saturation
    outright, so granite can be a teal rung and a brown rung and an olive rung without three
    tilesets. Every crack, bevel and speck is still the sheet's - what changes is the paint.

    <b>Saturation is *set* rather than scaled</b>, so a material's own colour cannot make one rung
    louder than another: sandstone ships at about half saturation and granite at almost none, and
    scaling would put them two bands apart from one number.
    """
    a = np.asarray(im).astype(np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3:]

    v = rgb.max(2)
    s_ = np.full_like(v, float(sat))

    # Plain Python numbers: `np.floor` hands back a NumPy scalar, which is *strong* under NEP 50
    # where a Python float is weak, and one of those promotes the whole float32 pipeline to float64.
    sector = int(hue * 6.0) % 6
    f = float(hue * 6.0 - int(hue * 6.0))
    p_, q, t = v * (1 - s_), v * (1 - f * s_), v * (1 - (1 - f) * s_)

    lut = [(v, t, p_), (q, v, p_), (p_, v, t), (p_, q, v), (t, p_, v), (v, p_, q)]
    out = np.dstack(lut[sector])

    return Image.fromarray(
        (np.dstack([np.clip(out, 0, 1), alpha]) * 255.0).astype(np.uint8), "RGBA")


def damp(im, keep):
    """Turn a tile's colour down without touching what it is.

    <b>This is the opposite operation to the one it replaced, and the difference is the whole
    point of the round.</b> `recolour` *overwrites* hue and saturation, so twenty-two mine tiles could
    be dressed as ten floors - which is recolouring, and what it cannot do is make sandstone look
    like ice. These sheets are eight real materials, so nothing needs inventing: what they need is
    only to stop shouting, which is a scale toward their own luminance and leaves every crack,
    speck and bevel exactly where the artist drew it.
    """
    a = np.asarray(im).astype(np.float32)
    lum = a[..., 0] * .30 + a[..., 1] * .59 + a[..., 2] * .11
    a[..., :3] = np.clip(lum[..., None] + (a[..., :3] - lum[..., None]) * keep, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


# --------------------------------------------------------------------------- the floor


def bed(tiles, rng, keep=CALM, lift=1.0, across=ACROSS, tint=None):
    """A plain grid of tiles: one per cell, picked at random.

    <b>`down` is derived from the tile's own aspect, never typed.</b> These sheets are not all cut
    the same - a tile is 231x214 on one and 249x189 on another, 1.08 against 1.32 - so one row
    count for all eight would squash some sheets by a fifth to fit a cell that is not their shape.
    That is the stretch this ground has already shipped once (37au), one layer further in, and it
    is unrepresentable here: the cell is built from the tile rather than the tile fitted to the
    cell, and every sheet lands within 3% of its own proportions.

    <b>No rotation.</b> The mine pack's faces were square and turning them was free variety; these
    are rectangular and drawn with a light from one side, so a quarter turn is a tile lying on its
    side next to itself. Thirty faces a sheet is enough without it.
    """
    wide = tiles[0].width / tiles[0].height
    cw = W / across
    down = max(1, int(round(H / (cw / wide))))
    ch = H / down

    floor = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    size = (int(cw) + 1, int(ch) + 1)
    faces = [(recolour(t, tint[0], tint[1]) if tint else damp(t, keep))
             .resize(size, Image.LANCZOS) for t in tiles]
    if lift != 1.0:
        faces = [Image.fromarray(
            np.clip(np.asarray(f).astype(np.float32) * [lift, lift, lift, 1.0], 0, 255)
            .astype(np.uint8), "RGBA") for f in faces]

    for row in range(down):
        for col in range(across):
            floor.alpha_composite(faces[rng.randint(len(faces))], (int(col * cw), int(row * ch)))

    return floor


# --------------------------------------------------------------------------- what is in the pack

#: <b>Three of the eight sheets are out of the ten, and two of them were taken out by the owner.</b>
#:
#: `tile1` (mossy bluestone) and `tile3` (ice) went after play: they are the two blue stone sheets,
#: they read as each other, and they were named by the levels they sat on. The instruction was to
#: drop them *wherever* they appeared, so `tile1` left rung one as well - it had not been named,
#: because the rung nobody lingers on is the rung nobody reports.
#:
#: `tile6` (grass) is out for a measurement rather than a verdict: damped to a third it still sits
#: *on* `GROUND_CHROMA_CAP` where every other sheet has come down to nine. It has also been judged
#: before - the second ground this hill ever wore was a grass pack "chosen for brightness", and the
#: owner's next call moved it to a mine.


# --------------------------------------------------------------------------- the ten floors

#: Ten floors from <b>five materials</b>, half of them wearing a colour of their own choosing.
#:
#: <b>Recolouring is back, and this time it was asked for</b> - "use from the others (recoloured)".
#: It had been the whole trick when ten floors came from three mine families, and the honest answer
#: to *are these actually different tiles* was no. With five real tilesets it is doing the opposite
#: job: the material is always genuinely different from its neighbours, and the paint is what lets
#: five cover ten. Rungs 1-4 and 10 keep their own colour (`calm`); 5-9 are recoloured (`tint`).
#:
#: <b>No two rungs running share a material, and a repeat sits at least four rungs from its twin.</b>
#: Granite carries three rungs because it is the greyest sheet in the set and so takes paint best -
#: a recoloured sandstone is sandstone in a jumper, where recoloured granite is a new floor.
#:
#: <b>Granite is damped hard (.16) where the other own-colour rungs sit at .30-.85</b>, because the
#: sheet is a blue-grey and the two sheets just taken out were the blue ones. At .40 rung one still
#: read as *that* floor; at .16 it is stone.
#:
#: <b>The tint saturations are a third of what the same trick wanted on the mine tiles</b>, and the
#: reason is the material rather than the colour: those faces were dark cave rock, these sheets are
#: bright, and `recolour` builds its value from the tile - so the same number lands twice as loud
#: here. Calibrated per rung against the own-colour rungs' own 6.9-11.2.
#:
#: Lava is last because rung ten is the finale and it is the only floor in the set with light of
#: its own. Every tint clears all four board colours - `Pal.Poppy` .99, `Pal.Amber` .075,
#: `Pal.Mint` .31, `Pal.Azure` .56 - by at least .04 of the wheel.
GROUND = [
    dict(key="hill1", seed=1741, sheet="tile5", name="granite", calm=.16),
    dict(key="hill2", seed=2213, sheet="tile2", name="sandstone", calm=.30),
    dict(key="hill3", seed=3307, sheet="tile7", name="cobble", calm=.85),
    dict(key="hill4", seed=4421, sheet="tile8", name="sand", calm=.18),

    dict(key="hill5", seed=5503, sheet="tile5", name="granite / teal", tint=(.47, .11)),
    dict(key="hill6", seed=6607, sheet="tile7", name="cobble / violet", tint=(.79, .13)),
    dict(key="hill7", seed=7717, sheet="tile2", name="sandstone / indigo", tint=(.66, .09)),
    dict(key="hill8", seed=8821, sheet="tile8", name="sand / mauve", tint=(.87, .07)),
    dict(key="hill9", seed=9923, sheet="tile5", name="granite / olive", tint=(.17, .12)),

    dict(key="hill10", seed=1013, sheet="tile4", name="lava", calm=.55),
]






# --------------------------------------------------------------------------- assembly


def build(spec, sheets):
    """One floor: a grid of one sheet's tiles, and nothing on it."""
    rng = np.random.RandomState(spec["seed"])
    return bed(sheets[spec["sheet"]], rng, spec.get("calm", CALM), spec.get("lift", 1.0),
               tint=spec.get("tint"))


def grounds():
    """Every ground, keyed as `make_siege_art` names them. None when the download is not here."""
    sheets = _sheets()
    if sheets is None:
        return None

    return {spec["key"]: build(spec, sheets) for spec in GROUND}
