# -*- coding: utf-8 -*-
"""Imports the grove's shop art from a source pack folder, driven by grove_art.tsv.

    python Tools/import_grove_art.py --source "C:/path/to/_extracted" [--dry-run]

What it does, in order:

  1. reads Tools/grove_art.tsv — the mapping of source PNG to permanent piece id
  2. copies each source into Assets/Game/Art/Homestead/<id>.png
  3. writes the display name into StreamingAssets/Content/loc/en.json
  4. rewrites the `pieces` array of homestead.json from the file
  5. bumps groveVersion in manifest.json, so clients refetch the catalog

Three rules it enforces, because all three are mistakes this project has already
made once somewhere:

  * **An id is permanent.** It is written into save files twice over — into the
    owned set and into every slot holding one (invariant 1). Re-pointing an id at
    different art is fine and expected; *renaming* one silently empties the slots
    of everybody who placed it, so a row that disappears from the file is reported
    rather than acted on, and the art is left on disk.
  * **Nothing is hand-copied.** The import is re-runnable and the diff shows the
    mapping, the price and the source path together. The next pack is a column.
  * **Existing pieces are preserved.** The rows already in homestead.json that this
    file does not mention — the residents, the home ladder, the original decor —
    are carried through untouched. This file owns what it lists and nothing else.
"""
import argparse, io, json, collections, os, shutil, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
TSV = os.path.join(HERE, "grove_art.tsv")
ART = os.path.join(ROOT, "Assets", "Game", "Art", "Homestead")
CONTENT = os.path.join(ROOT, "Assets", "StreamingAssets", "Content")
CATALOG = os.path.join(CONTENT, "homestead.json")
MANIFEST = os.path.join(CONTENT, "manifest.json")
LOC = os.path.join(CONTENT, "loc", "en.json")

KINDS = ("ground", "structure", "bed", "path", "edge", "canopy")


def rows():
    out = []
    with io.open(TSV, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split("\t")
            if len(parts) != 7:
                sys.exit("%s:%d has %d columns, expected 7" % (TSV, n, len(parts)))
            src, pid, slot, cost, scale, lift, name = parts
            if slot not in KINDS:
                sys.exit("%s:%d unknown slot kind '%s'" % (TSV, n, slot))
            out.append(dict(src=src, id=pid, slot=slot, cost=int(cost),
                            scale=float(scale), lift=float(lift), name=name))
    return out


def dims(path):
    with open(path, "rb") as f:
        head = f.read(33)
    return struct.unpack(">II", head[16:24]) if head[:8] == b"\x89PNG\r\n\x1a\n" else (0, 0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", required=True, help="folder the pack paths are relative to")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    wanted = rows()
    ids = [r["id"] for r in wanted]
    dupes = [k for k, v in collections.Counter(ids).items() if v > 1]
    if dupes:
        sys.exit("duplicate piece ids in grove_art.tsv: " + ", ".join(dupes))

    # ------------------------------------------------------------------ art
    copied = missing = same = 0
    biggest = (0, "")
    for r in wanted:
        src = os.path.join(args.source, r["src"].replace("/", os.sep))
        dst = os.path.join(ART, r["id"] + ".png")

        if not os.path.isfile(src):
            print("MISSING  %-18s %s" % (r["id"], r["src"]))
            missing += 1
            continue

        w, h = dims(src)
        if max(w, h) > biggest[0]:
            biggest = (max(w, h), r["id"])

        if os.path.isfile(dst) and open(dst, "rb").read() == open(src, "rb").read():
            same += 1
            continue

        if not args.dry_run:
            shutil.copyfile(src, dst)
        copied += 1

    if missing:
        sys.exit("%d source file(s) missing — nothing was written to the catalog" % missing)

    # -------------------------------------------------------------- catalog
    catalog = json.load(io.open(CATALOG, encoding="utf-8"),
                        object_pairs_hook=collections.OrderedDict)

    mine = {r["id"] for r in wanted}
    kept = [p for p in catalog["pieces"] if p.get("id") not in mine]

    dropped = [p["id"] for p in catalog["pieces"]
               if p.get("id") not in mine and p.get("slot") and p.get("_imported")]
    for pid in dropped:
        print("WARNING  '%s' was imported before and is no longer in grove_art.tsv; "
              "it is left in the catalog, because removing an id empties the slots of "
              "everybody who placed it" % pid)

    fresh = []
    for r in wanted:
        fresh.append(collections.OrderedDict([
            ("id", r["id"]),
            ("art", "Homestead/" + r["id"]),
            ("kind", "decor"),
            ("slot", r["slot"]),
            ("cost", r["cost"]),
            ("scale", r["scale"]),
            ("lift", r["lift"]),
            ("_imported", True),
        ]))

    # Residents, then the home ladder, then decor by kind and price — the order the
    # shop draws them in, so the file reads the way the screen does.
    order = {"resident": 0, "dwelling": 1, "decor": 2}
    kind_order = {k: i for i, k in enumerate(("structure", "canopy", "bed", "edge", "path", "ground"))}
    pieces = kept + fresh
    pieces.sort(key=lambda p: (order.get(p.get("kind", "decor"), 2),
                               p.get("tier", 0),
                               kind_order.get(p.get("slot", "ground"), 9),
                               p.get("cost", 0)))
    catalog["pieces"] = pieces

    # ------------------------------------------------------------------ loc
    loc = json.load(io.open(LOC, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    have = {e["key"]: e for e in loc["entries"]}
    added = 0
    for r in wanted:
        key = "ui.piece." + r["id"]
        if key in have:
            have[key]["text"] = r["name"]
        else:
            loc["entries"].append(collections.OrderedDict([("key", key), ("text", r["name"])]))
            added += 1

    # ------------------------------------------------------------- manifest
    manifest = json.load(io.open(MANIFEST, encoding="utf-8"),
                         object_pairs_hook=collections.OrderedDict)
    manifest["groveVersion"] = int(manifest.get("groveVersion", 1)) + 1

    if not args.dry_run:
        io.open(CATALOG, "w", encoding="utf-8", newline="\n").write(
            json.dumps(catalog, indent=2, ensure_ascii=False) + "\n")
        io.open(LOC, "w", encoding="utf-8", newline="\n").write(
            json.dumps(loc, indent=2, ensure_ascii=False) + "\n")
        io.open(MANIFEST, "w", encoding="utf-8", newline="\n").write(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")

    by_kind = collections.Counter(r["slot"] for r in wanted)
    spend = sum(r["cost"] for r in wanted)
    print("\n%s%d piece(s): %d copied, %d unchanged, %d new string(s)" %
          ("DRY RUN — " if args.dry_run else "", len(wanted), copied, same, added))
    print("   by slot: " + ", ".join("%s %d" % (k, n) for k, n in sorted(by_kind.items())))
    print("   catalogue is now %d piece(s); the new ones add %d credits" %
          (len(pieces), spend))
    print("   largest sprite: %s at %dpx — the importer caps Homestead art at 512" % (biggest[1], biggest[0]))
    print("   groveVersion -> %d" % manifest["groveVersion"])
    print("\nNext, in this order:")
    print("  1. Glimmer Grove > Addressables > Sync All Assets")
    print("     The importer hook addresses art as it lands, but it does not fire for")
    print("     files copied in while the Editor was closed or mid-reload, which is")
    print("     every run of this script. Unaddressed art loads as nothing and the")
    print("     cell draws blank. The build gate would catch it; the Editor will not.")
    print("  2. Glimmer Grove > Validate Content")
    print("  3. python Tools/verify/content.py")


if __name__ == "__main__":
    main()
