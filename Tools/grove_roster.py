# -*- coding: utf-8 -*-
"""The grove's catalogue, as a file: which model each piece is, and what it costs.

WHY IT IS A FILE AND NOT A TABLE IN A TOOL. Two programs need it and they are written
in different places for different reasons — `make_grove_art.py` turns it into sprites
and `import_grove_art.py` turns it into `homestead.json` — so a table inside either one
would be a second copy the other had to be kept in step with. It is the same argument
`grove_art.tsv` was written under and the same one `manifest.json` is built on: where a
fact is read twice, it is written once.

WHY A PIECE NAMES A MODEL AND NOT A PICTURE. Every grove piece is rendered from a CC0
model at the floor's own isometric projection, so what a row names is the thing the art
is *made from* — which means a re-cut at a different angle, a different light or a
different resolution is a re-run of a tool rather than a hand-edit of 160 PNGs. The
pictures are derived; this file and the models are the source.

WHAT A ROW MAY NOT SAY. There is no `scale` and no `lift` column, and their absence is
the point. Both used to be hand-tuned per piece because the art was cut from a flat
sheet and nothing could know how big the thing was or where its feet were. A rendered
model knows both exactly — the renderer works in world units and the model stands on
y=0 — so they are *derived* and written into the catalogue by the importer. A number
that can be measured is never typed (`UIKit.PillFaceLift`'s rule, and `TileFaceRatio`'s).
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
TSV = os.path.join(HERE, "grove_pieces.tsv")
RETIRED = os.path.join(HERE, "grove_retired.txt")


def retired():
    """Every piece id that has ever shipped. See `grove_retired.txt` for why."""
    out = set()
    with io.open(RETIRED, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#"):
                out.add(line)
    return out

#: Where the committed models live. Copied out of the KayKit bundle by
#: `make_grove_art.py --vendor`, so a checkout can re-bake with nothing downloaded.
VENDOR = os.path.join(HERE, "kaykit")


class Pack(object):
    """One source pack: where its models are, and how big its world is.

    `units_per_tile` is the calibration that makes two packs agree about size. Every
    pack models at whatever scale suited its own game — the hexagon pack draws a house
    0.79 units wide to stand on a 2.0-unit hex, the forest pack draws life-sized
    foliage — so a number per pack is what stops a bought bush towering over a bought
    church. It is chosen by rendering the pack's most recognisable object and asking
    how many floor tiles it ought to cover, which is a judgement; everything
    downstream of it is arithmetic.
    """

    __slots__ = ("key", "path", "units_per_tile", "credit")

    def __init__(self, key, path, units_per_tile, credit):
        self.key, self.path, self.units_per_tile, self.credit = key, path, units_per_tile, credit

    @property
    def tile_on_screen(self):
        """How much world one tile spans **across the screen**, which is not its side.

        A tile is `units_per_tile` along both world axes, and the camera looks down the
        diagonal — screen x is `(X - Z) * cos 45`, so a square of side `u` projects to a
        diamond `u * sqrt(2)` wide. Everything that converts a rendered width into floor
        units divides by *this*, never by the side.

        Getting it wrong is a mistake nothing could catch, because it is a constant factor:
        every piece comes out 1.41 times too big together, so they still agree with each
        other and only disagree with the ground they stand on. The floor tile is what pins
        it — a generated one-tile prism must draw at exactly `GroveFloor.TileWidth`, and
        `make_grove_art.py --floor` asserts precisely that.
        """
        return self.units_per_tile * (2.0 ** 0.5)


#: The three packs the village is built from. Names are the prefix a roster row uses,
#: which is `make_sfx.py`'s `rpg:` idiom for the third time in this project.
PACKS = {
    "hex": Pack(
        "hex",
        "_extracted/KayKit_Medieval_Hexagon_Pack_1.0_FREE/"
        "KayKit_Medieval_Hexagon_Pack_1.0_FREE/Assets/obj",
        0.40,
        "KayKit Medieval Hexagon Pack 1.0 (CC0) — Kay Lousberg"),
    "forest": Pack(
        "forest",
        "_extracted/KayKit_Forest_Nature_Pack_1.0_FREE/"
        "KayKit_Forest_Nature_Pack_1.0_FREE/Assets/obj",
        1.30,
        "KayKit Forest Nature Pack 1.0 (CC0) — Kay Lousberg"),
    "dungeon": Pack(
        "dungeon",
        "KayKit_Dungeon_Pack_1.1_FREE/KayKit_Dungeon_Pack_1.1_FREE/Assets/obj",
        1.00,
        "KayKit Dungeon Pack 1.1 (CC0) — Kay Lousberg"),
}

#: The shop's shelves, in the order the tabs are drawn. These are the `slot` values a
#: row may carry and they must match `HomesteadSlotKind` exactly — `import_grove_art`
#: writes them straight into the catalogue, and a name this list allows and the enum
#: does not is a piece the reader drops without a word.
#:
#: **They were not renamed for the village and that is deliberate.** The six read as a
#: grove's vocabulary and describe a village exactly as well: a structure anchors a
#: stretch of ground whether it is a well or a smithy, an edge is a fence either way,
#: a canopy is what is drawn tall. Renaming them would have churned the enum, the six
#: shelf atlases, their addresses and `GroveShelf`'s three-way agreement between tab,
#: atlas and asset scope — to say the same thing in different words. What a player
#: reads is the loc string, and that is free to change.
SHELVES = ("structure", "canopy", "bed", "edge", "path", "ground")

#: The home ladder, in tiers. A dwelling is not decor: exactly one stands, on the hall's
#: own tile, and the best one owned is the one drawn — so it is not in the roster's
#: shelves and it is priced as a ladder rather than as a shelf.
#:
#: **Every tier shares one footprint** and it is the floor's `hallCols` x `hallRows`.
#: That is invariant 16i and it is load-bearing rather than tidy: a grander home that
#: occupied more tiles would evict whatever the player had built beside the cabin.
#: The models escalate visually instead, which is what the ladder is for.
#: How many ways a home may be turned: one.
#:
#: Not because a house has no front — it plainly does — but because a dwelling is never
#: placed by hand. Exactly one stands, on the hall's own tile, and `HomesteadLayout.Turn`
#: refuses it (`stand.IsHall`). Four facings would be four renders of which three could
#: never be drawn, which is four times the download for a control that does not exist.
DWELLING_FACINGS = 4

DWELLINGS = [
    # model                      id              tier  cost   name          size
    #
    # `size` is the hall's plot rather than the model's own: every rung stands on the floor's
    # `hallCols` x `hallRows` (invariant 16i), which is four tiles, and the pack draws a
    # cottage for one. So the two small rungs are scaled up to sit on the land they reserve,
    # and the two big ones are already drawn at that size and are left alone — which is what
    # made the barracks and the castle usable as homes at all.
    ("hex:buildings/blue/building_home_A_blue", "home_cottage", 1, 0, "Cottage", 1.30),
    ("hex:buildings/blue/building_home_B_blue", "home_farmhouse", 2, 2500, "Farmhouse", 1.35),
    ("hex:buildings/blue/building_barracks_blue", "home_barracks", 3, 13000, "Barracks", 1.00),
    ("hex:buildings/blue/building_castle_blue", "home_citadel", 4, 30000, "Citadel", 1.00),
]


class Row(object):
    """One piece. See `read` for what each column means."""

    __slots__ = ("model", "id", "shelf", "cost", "bundle", "facings",
                 "cols", "rows", "name", "size", "line")

    def __init__(self, **kw):
        for k in self.__slots__:
            setattr(self, k, kw.get(k))

    @property
    def pack(self):
        return PACKS[self.model.split(":", 1)[0]]

    @property
    def parts(self):
        """Every model this piece is made of, inside its pack, without extensions.

        A row may name more than one, joined by ``+``, because these packs export a
        hierarchy as one file per object: a tower's roof, a watermill's wheel and a
        windmill's sails are separate models, and a mill rendered without them is a
        mill with no sails. The second and later names may be bare filenames, which
        are resolved in the first one's folder.

        It is spelled out rather than inferred from the names, and that is not
        fastidiousness — the pack's naming cannot support inference. `tree_single_A_cut`
        is a stump, not a part of `tree_single_A`; `fence_wood_straight_gate` is a gate,
        not a part of the fence. Either guess welds two objects together permanently,
        and the only thing in this project that could notice is somebody looking at a
        contact sheet.
        """
        names = [p.strip() for p in self.model.split(":", 1)[1].split("+")]
        folder = os.path.dirname(names[0])
        return [names[0]] + [n if "/" in n else (folder + "/" + n if folder else n)
                             for n in names[1:]]

    @property
    def relative(self):
        """The piece's primary model — the one its folder and atlas come from."""
        return self.parts[0]

    def sources(self, bundle):
        """Where this piece's models are in an unpacked KayKit bundle."""
        root = os.path.join(bundle, self.pack.path.replace("/", os.sep))
        return [os.path.join(root, p.replace("/", os.sep) + ".obj") for p in self.parts]

    def vendored(self):
        """Where this piece's models are in the repo, once `--vendor` has copied them."""
        root = os.path.join(VENDOR, self.pack.key)
        return [os.path.join(root, p.replace("/", os.sep) + ".obj") for p in self.parts]

    @property
    def unit_cost(self):
        return self.cost if self.bundle <= 1 else self.cost // self.bundle


def dwellings():
    """The home ladder as ordinary roster rows.

    So that one code path renders every piece in the grove. The ladder is authored in
    `DWELLINGS` rather than in the TSV because a dwelling is not decor — it has a tier
    rather than a shelf and exactly one of them ever stands — but nothing about *rendering*
    one differs, and a second path through the bake would be a second place for the
    projection, the light and the trim to drift.

    The footprint here is a placeholder: a dwelling's real one is the floor's own
    `hallCols` x `hallRows` (invariant 16i) and the importer reads it from there, while the
    renderer does not care about footprints at all.
    """
    return [Row(model=model, id=pid, shelf="structure", cost=cost, bundle=1,
                facings=DWELLING_FACINGS, cols=1, rows=1, name=name, size=size, line=0)
            for model, pid, _tier, cost, name, size in DWELLINGS]


def everything(path=TSV):
    """Every piece the grove ships: the shelves and the home ladder."""
    return read(path) + dwellings()


def read(path=TSV):
    """Parses the roster, refusing anything a later stage could only fail on silently.

    Columns, tab separated:

      ``model``    ``pack:path/to/model`` — no extension. The pack key indexes `PACKS`.
      ``id``       the permanent piece id. Written into save files, so it never changes.
      ``shelf``    which tab of the shop sells it; one of `SHELVES`.
      ``cost``     credits. Zero means it is not for sale (the starter home).
      ``bundle``   copies one purchase grants. Must divide `cost` exactly.
      ``facings``  1 or 4. Four for anything with a front; one for anything symmetric.
      ``cols``     tiles it occupies across, as drawn.
      ``rows``     tiles it occupies back, as drawn.
      ``name``     the English display string, written to `loc/en.json` as
                   ``ui.piece.<id>``.
    """
    out, seen, spent = [], {}, retired()
    with io.open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            line = line.rstrip("\n")
            if not line.strip() or line.lstrip().startswith("#"):
                continue

            part = line.split("\t")
            if len(part) != 10:
                sys.exit("%s:%d has %d columns, expected 10" % (path, n, len(part)))
            model, pid, shelf, cost, bundle, facings, cols, rows, name, size = (
                p.strip() for p in part)

            if ":" not in model or model.split(":", 1)[0] not in PACKS:
                sys.exit("%s:%d '%s' does not start with a known pack (%s)"
                         % (path, n, model, ", ".join(sorted(PACKS))))
            if shelf not in SHELVES:
                sys.exit("%s:%d unknown shelf '%s'" % (path, n, shelf))
            if pid in seen:
                sys.exit("%s:%d duplicate piece id '%s' (first at line %d)"
                         % (path, n, pid, seen[pid]))
            if pid in spent:
                sys.exit("%s:%d '%s' is a retired piece id and may never be reused — see "
                         "Tools/grove_retired.txt. Pick another; a save that still names "
                         "it would resolve to an object nobody bought." % (path, n, pid))
            seen[pid] = n

            cost, bundle, facings = int(cost), int(bundle), int(facings)
            cols, rows = int(cols), int(rows)

            # A bundle has to divide the price exactly, or a copy is worth `cost/bundle`
            # rounded down and every grove holding one is scored short — on the one number
            # that reaches a public leaderboard (invariant 16h).
            if bundle < 1:
                sys.exit("%s:%d bundle must be at least 1" % (path, n))
            if bundle > 1 and cost % bundle:
                sys.exit("%s:%d '%s' costs %d, which its bundle of %d does not divide"
                         % (path, n, pid, cost, bundle))
            if bundle > 1 and cost <= 0:
                sys.exit("%s:%d '%s' is free, so it is an entitlement and cannot be sold "
                         "in bundles" % (path, n, pid))
            if facings not in (1, 4):
                sys.exit("%s:%d '%s' asks for %d facings; only 1 and 4 exist"
                         % (path, n, pid, facings))

            # `GroveFootprint.MaxSide`. A piece larger than this is clamped at read time,
            # which means it would occupy less than it paints — invisible until somebody
            # builds beside it.
            if not (1 <= cols <= 4 and 1 <= rows <= 4):
                sys.exit("%s:%d '%s' is %dx%d; a footprint side must be 1..4"
                         % (path, n, pid, cols, rows))

            # A square footprint cannot tell its facings apart on the grid, and a
            # non-square one must be free to swap its axes, which a quarter turn does.
            # Nothing refuses either — this only says what the four facings mean.
            size = float(size)
            if not (0.1 <= size <= 1.0):
                sys.exit("%s:%d '%s' is drawn at %.2f of its own scale; that column brings a "
                         "model *down* to object scale and 1 is the pack's own"
                         % (path, n, pid, size))

            out.append(Row(model=model, id=pid, shelf=shelf, cost=cost, bundle=bundle,
                           facings=facings, cols=cols, rows=rows, name=name, size=size, line=n))
    return out


def by_shelf(rows):
    """The roster grouped the way the shop draws it."""
    out = dict((s, []) for s in SHELVES)
    for r in rows:
        out[r.shelf].append(r)
    for s in out:
        out[s].sort(key=lambda r: (r.cost, r.id))
    return out
