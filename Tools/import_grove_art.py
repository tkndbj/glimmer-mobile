# -*- coding: utf-8 -*-
"""Turns the roster and the rendered art into the grove's catalogue.

    python Tools/import_grove_art.py [--dry-run]

It reads `Tools/grove_pieces.tsv` and the PNGs `make_grove_art.py` wrote, and rewrites the
`pieces` array of `homestead.json`, the `ui.piece.*` strings in `loc/en.json`, and
`groveVersion` in `manifest.json`. It copies nothing and cuts nothing: the art is already
on disk, and this is the step that writes down *what it is*.

WHAT IT DERIVES, AND WHY THAT IS THE POINT. `scale`, `lift`, `w`, `h` and the hit masks are
all facts about a picture. They used to be typed — `scale` and `lift` by hand in a TSV,
because a flat cut-out knows neither how big the thing was nor where its feet are. A
rendered model knows both exactly, in world units, so all five are measured here and none
of them is a judgement anybody can get wrong. That is `UIKit.PillFaceLift`'s rule and
`GroveFloor.TileFaceRatio`'s: a number describing a picture is only true until the picture
is re-cut, so it is generated beside the picture and checked against it.

THREE RULES IT ENFORCES, each of which is a mistake this project has made once somewhere.

  * **An id is permanent.** It is written into save files twice over — into the stock and
    into every tile holding one (invariant 1). `grove_roster` refuses a retired one
    outright; this refuses to *drop* one silently, because a row that vanishes empties the
    tiles of everybody who placed it.
  * **The catalogue may not describe art it does not ship.** Every row's facts are read off
    the PNGs, and a piece whose pictures are missing stops the whole import rather than
    being written with a plausible-looking size.
  * **A bundle must divide its price.** Otherwise a copy is worth `cost/bundle` rounded
    down and every grove holding one is scored short — on the one number that reaches a
    public leaderboard (invariant 16h).
"""
import argparse
import collections
import io
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import grove_art_facts as facts                                        # noqa: E402
import grove_roster as roster                                          # noqa: E402
import make_grove_art as art                                           # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
CONTENT = os.path.join(ROOT, "Assets", "StreamingAssets", "Content")
CATALOG = os.path.join(CONTENT, "homestead.json")
MANIFEST = os.path.join(CONTENT, "manifest.json")
LOC = os.path.join(CONTENT, "loc", "en.json")

#: `GroveFloor.TileWidth` and `HomesteadScreen.PieceScale`, as `make_grove_art` mirrors them.
#: Held to the C# below rather than trusted, because every derived `scale` rests on them.
CSHARP = [
    ("Assets/Game/Scripts/Domain/Homestead/GroveFloor.cs",
     "public const float TileWidth = %g" % art.TILE_WIDTH),
    ("Assets/Game/Scripts/Domain/Homestead/GroveFloor.cs",
     # The C# spells it `.5628f` with no leading zero, so the needle does too. A literal
     # matched loosely is a mirror that agrees with something it was not compared to.
     "public const float TileFaceRatio = %s" % ("%.4f" % art.TILE_FACE_RATIO).lstrip("0")),
    ("Assets/Game/Scripts/Presentation/App/GroveTileArt.cs",
     "PieceScale = %.2f" % art.PIECE_SCALE),
]


def held_to_csharp():
    """Proves the three numbers every derived `scale` and `lift` rests on are still the
    game's own.

    They are mirrored in `make_grove_art.py` because nothing there reads C#, and a mirror
    that can drift in silence is worse than no mirror — this project has the scar
    (`SiegeTuning.PerfectMatch`, and `TileFaceRatio` before it). Grepping the source for the
    literal is crude and it is the whole of what is needed: the day somebody retunes one,
    this stops rather than quietly writing a catalogue that draws every piece at the wrong
    size.
    """
    bad = []
    for relative, needle in CSHARP:
        path = os.path.join(ROOT, relative.replace("/", os.sep))
        text = io.open(path, encoding="utf-8").read() if os.path.isfile(path) else ""
        if needle not in text:
            bad.append("%s no longer says '%s'" % (relative, needle))
    if bad:
        sys.exit("make_grove_art.py has drifted from the game:\n  " + "\n  ".join(bad))


def row_for(piece, prior):
    """One catalogue row, in the order the file reads best."""
    out = collections.OrderedDict()
    out["id"] = piece.id
    out["art"] = "Homestead/" + piece.id
    out["kind"] = "decor"
    out["slot"] = piece.shelf
    out["cost"] = piece.cost

    # Omitted when it is 1, which is what the reader assumes for an absent field — so a
    # catalogue whose pieces sell singly and one written before bundles existed are the same
    # file, and a drop's diff shows only what actually bundles.
    if piece.bundle > 1:
        out["bundle"] = piece.bundle
    if piece.facings > 1:
        out["facings"] = piece.facings
    if piece.cols > 1 or piece.rows > 1:
        out["cols"] = piece.cols
        out["rows"] = piece.rows

    return out


def dwelling_row(model, pid, tier, cost, level, floor):
    out = collections.OrderedDict()
    out["id"] = pid
    out["art"] = "Homestead/" + pid
    out["kind"] = "dwelling"
    out["tier"] = tier
    out["cost"] = cost

    # The keeper level that opens the rung. Omitted when it is nought, which is what the
    # reader assumes for an absent field - so the free first rung and a catalogue written
    # before the ladder was gated are the same file, and a drop's diff shows only the rungs
    # that really ask for something. A home is the only kind allowed to carry it
    # (`HomesteadMapper` refuses it anywhere else), and a gated rung must also be priced or
    # reaching the level would hand it over - both content gates prove that.
    if level > 0:
        out["requiresKeeperLevel"] = level

    # Every tier shares the floor's own hall footprint. Invariant 16i, and load-bearing: a
    # grander home that occupied more tiles would evict whatever stood beside the cabin.
    out["cols"] = int(floor.get("hallCols", 2))
    out["rows"] = int(floor.get("hallRows", 2))

    # A home turns like anything else now that its seat is the player's rather than the
    # floor's (save v26). It was rendered at one yaw for as long as nothing could turn it,
    # and a facing is a different *picture* here (16m) - so this is a mask per facing, not a
    # flag, and leaving it off would mean the hall hit-tested against the wrong drawing the
    # first time somebody turned it.
    if roster.DWELLING_FACINGS > 1:
        out["facings"] = roster.DWELLING_FACINGS
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    held_to_csharp()
    rows = roster.read()

    catalog = json.load(io.open(CATALOG, encoding="utf-8"),
                        object_pairs_hook=collections.OrderedDict)
    before = dict((p.get("id"), p) for p in catalog.get("pieces", []))
    floor = catalog.get("floor", {})

    # ------------------------------------------------------------------ pieces
    pieces, problems = [], []

    for model, pid, tier, cost, _name, _size, level in roster.DWELLINGS:
        pieces.append(dwelling_row(model, pid, tier, cost, level, floor))

    for piece in rows:
        pieces.append(row_for(piece, before.get(piece.id)))

    # The facts about each picture, read off the PNGs that were actually rendered. A piece
    # whose art is missing stops the import: writing a plausible size for a picture that is
    # not there is exactly how a catalogue comes to describe art it does not ship.
    for row in pieces:
        facings = int(row.get("facings", 1))
        fields = ("w", "h", "hits" if facings > 1 else "hit")
        _changed, problem = facts.apply(row, row["art"], False, fields, facings)
        if problem:
            problems.append(problem)

    if problems:
        sys.exit("%d piece(s) have no art. Run make_grove_art.py first.\n  %s"
                 % (len(problems), "\n  ".join(problems)))

    # `scale` and `lift`, derived from the render rather than typed. Done after the masks so
    # a row carries its size before anything reads it.
    for row, (scale, lift) in zip(pieces, measure(pieces, rows)):
        row["scale"] = round(scale, 4)
        row["lift"] = round(lift, 4)

    # The home ladder, then decor by shelf and price — the order the shop draws them in, so
    # the file reads the way the screen does.
    shelf_order = dict((s, i) for i, s in enumerate(roster.SHELVES))
    kind_order = {"dwelling": 1, "decor": 2}
    pieces.sort(key=lambda p: (kind_order.get(p.get("kind", "decor"), 2),
                               p.get("tier", 0),
                               shelf_order.get(p.get("slot", "ground"), 9),
                               p.get("cost", 0),
                               p.get("id", "")))
    catalog["pieces"] = pieces

    # ------------------------------------------------------------------- loc
    #
    # **This tool owns the whole `ui.piece.` namespace**, so a key whose piece is gone goes
    # with it. That is narrower than it sounds and it is the only honest arrangement: every
    # one of these keys is written here, none is written anywhere else, and a piece's key is
    # *derived from its id* (invariant 5a) — which is exactly why `Tools/verify/loc.py`
    # cannot see one orphaned. It scans for written keys, and there is no call site to find.
    # Left alone, replacing the catalogue stranded 163 strings that named pieces which no
    # longer exist, in a file that ships, with nothing anywhere able to report it.
    #
    # The *ids* are not forgotten — `Tools/grove_retired.txt` is the permanent record and is
    # what stops one ever being reused. What is dropped here is only the English for them.
    loc = json.load(io.open(LOC, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    wanted = collections.OrderedDict(
        [(d[1], d[4]) for d in roster.DWELLINGS] + [(r.id, r.name) for r in rows])
    mine = set("ui.piece." + pid for pid in wanted)

    dropped = [e["key"] for e in loc["entries"]
               if e["key"].startswith("ui.piece.") and e["key"] not in mine]
    loc["entries"] = [e for e in loc["entries"] if e["key"] not in set(dropped)]

    have = dict((e["key"], e) for e in loc["entries"])
    added = 0
    for pid, name in wanted.items():
        key = "ui.piece." + pid
        if key in have:
            have[key]["text"] = name
        else:
            loc["entries"].append(collections.OrderedDict([("key", key), ("text", name)]))
            added += 1

    # -------------------------------------------------------------- manifest
    manifest = json.load(io.open(MANIFEST, encoding="utf-8"),
                         object_pairs_hook=collections.OrderedDict)
    manifest["groveVersion"] = int(manifest.get("groveVersion", 1)) + 1

    if not args.dry_run:
        write(CATALOG, catalog)
        write(LOC, loc)
        write(MANIFEST, manifest)

    say(catalog, rows, added, len(dropped), manifest["groveVersion"], args.dry_run)


def measure(pieces, rows):
    """`(scale, lift)` for each row, re-derived from the models the art was rendered from.

    Re-derived rather than carried out of `make_grove_art` in a side file: the two tools run
    separately, and a file passed between them is a third place for the truth to live. The
    arithmetic is the renderer's own — `make_grove_art.piece` is called, not copied — so the
    two cannot come to disagree about what a picture means.

    It is the slowest step of the import by a long way, because it re-renders every facing
    to measure it. That is the honest cost of deriving these rather than typing them, and
    an import is not a gate.
    """
    by_id = dict((r.id, r) for r in rows + roster.dwellings())
    done = art.render_all([by_id[p["id"]] for p in pieces])
    return [(r.scale, r.lift) for r in done]


def write(path, doc):
    io.open(path, "w", encoding="utf-8", newline="\n").write(
        json.dumps(doc, indent=2, ensure_ascii=False) + "\n")


def say(catalog, rows, added, dropped, grove_version, dry):
    by_shelf = collections.Counter(r.shelf for r in rows)
    spend = sum(r.cost for r in rows) + sum(d[3] for d in roster.DWELLINGS)
    print("%s%d piece(s): %d decor, %d dwelling(s), %d new string(s), %d orphaned string(s) dropped"
          % ("DRY RUN — " if dry else "", len(catalog["pieces"]), len(rows),
             len(roster.DWELLINGS), added, dropped))
    print("   by shelf: " + ", ".join("%s %d" % (k, n) for k, n in sorted(by_shelf.items())))
    print("   the whole catalogue is %d credits" % spend)
    print("   groveVersion -> %d" % grove_version)
    print("\nNext, in this order:")
    print("  1. Glimmer Grove > Addressables > Sync All Assets, then SAVE")
    print("     The importer hook addresses art as it lands and does not fire for files a")
    print("     tool wrote while the Editor was closed — which is every run of the bake.")
    print("     Unaddressed art loads as nothing and a tile draws blank (invariant 7b).")
    print("  2. Glimmer Grove > Addressables > Rebuild Grove Atlases")
    print("     The shop browses through one atlas per shelf. A piece with no thumbnail in")
    print("     its shelf's atlas draws a blank plate on the device and not in the Editor.")
    print("  3. Glimmer Grove > Validate Content   and   > Validate Art")
    print("  4. python Tools/verify/content.py")


if __name__ == "__main__":
    main()
