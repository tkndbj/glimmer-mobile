#!/usr/bin/env python3
"""
Draw a grove exactly as the game draws it, without Unity.

    python Tools/render_grove.py --card showcase-01            # read the live card
    python Tools/render_grove.py --layout out/showcase-01.json # read a local layout
    python Tools/render_grove.py --all --out out/              # every showcase card

## Why this exists

A grove is the one thing in this project whose quality is *only* visible as a picture.
Everything else here is proved by a compile, a test or a validator, and none of those can
tell a composed village from a scatter of two hundred props — the offline checks passed on
ten villages that looked, in the owner's words, horrendous. Opening Unity to look is a
domain reload, a play session and a screenshot per iteration, which is far too slow a loop
to *design* against.

So this reimplements the drawing half of `GroveFieldView` / `GroveVisitScreen` against the
same art and the same numbers:

  * the grid is `GroveFloor` — 220px tiles, a 0.5628 face ratio (not 0.5, see there),
    `x = (col - row) * w/2`, `y = (col + row) * h/2`
  * every tile of ground is drawn first, back to front, and every piece over all of it —
    `GroveFieldView`'s two layers, which is what keeps a tile's skirt off the base of the
    piece behind it
  * a piece stands on its footprint (`cols` x `rows`, axes exchanged on an odd quarter
    turn, the hall's from the floor) and is drawn at the footprint's centre, sorted by its
    front tile —
    `GroveFootprint.Depth`, with a single tile one step in front of a larger one on the
    same front tile
  * a piece draws at authored `w` x `h` (the PNG's size) x `piece.scale` x 1.15, lifted by
    `size.y * piece.lift` — `GroveTileArt.LayPiece`
  * a resident is its critter flipbook's first frame at scale .95, lift .45

It is a *renderer*, deliberately: it takes no view on whether a layout is good. That is
what eyes are for, and this is what puts the picture in front of them.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import urllib.request
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parent.parent
CONTENT = REPO / "Assets" / "StreamingAssets" / "Content"
ART = REPO / "Assets" / "Game" / "Art"

PROJECT = "glimmer-groove-1cd60"
FS = f"https://firestore.googleapis.com/v1/projects/{PROJECT}/databases/(default)/documents"

# ------------------------------------------------------------------ geometry
TILE_W = 220.0
FACE_RATIO = 0.5628                 # GroveFloor.TileFaceRatio — measured off the tile art
TILE_H = TILE_W * FACE_RATIO
TILE_OVERLAP = 1.06                 # HomesteadArt.TileOverlap
PIECE_SCALE = 1.15                  # HomesteadScreen/GroveVisitScreen.PieceScale

RESIDENT_SCALE = 0.95               # GroveResidents.Scale
RESIDENT_LIFT = 0.45                # GroveResidents.Lift


def tile_x(col: float, row: float) -> float:
    return (col - row) * TILE_W * 0.5


def tile_y(col: float, row: float) -> float:
    return (col + row) * TILE_H * 0.5


def draw_order(col: int, row: int) -> int:
    return (col + row) * 1024 + col


# ------------------------------------------------------------------- catalog
def load_catalog():
    homestead = json.loads((CONTENT / "homestead.json").read_text(encoding="utf8"))
    manifest = json.loads((CONTENT / "manifest.json").read_text(encoding="utf8"))

    pieces = {}
    for p in homestead["pieces"]:
        pieces[p["id"]] = {
            "art": p["art"],
            "animated": bool(p.get("animated")),
            "scale": float(p.get("scale", 1.0)),
            "lift": float(p.get("lift", 0.45)),
            "kind": p.get("kind", "decor"),
            "slot": p.get("slot", "ground"),
            "cols": int(p.get("cols") or 1),
            "rows": int(p.get("rows") or 1),
            "facings": int(p.get("facings") or 1),
            "w": int(p.get("w") or 0),
            "h": int(p.get("h") or 0),
        }

    # Residents are projected in, exactly as GroveResidents.From does.
    for c in manifest.get("companions", []):
        if c.get("disabled"):
            continue
        animated = bool(c.get("animated"))
        pieces["friend_" + c["id"]] = {
            "art": ("Critters/" + c["animated"]) if animated else ("Companions/" + c["portrait"]),
            "animated": animated,
            "scale": RESIDENT_SCALE,
            "lift": RESIDENT_LIFT,
            "kind": "resident",
            "slot": "ground",
            "cols": 1,
            "rows": 1,
            "facings": 1,
            "w": 0,
            "h": 0,
        }

    return homestead["floor"], pieces


_sprites: dict[str, Image.Image | None] = {}


def sprite(art: str):
    """The still picture for an art key — a PNG, or an animated folder's first frame."""
    if art in _sprites:
        return _sprites[art]

    path = ART / (art + ".png")
    if not path.exists():
        folder = ART / art
        frames = sorted(folder.glob("*.png")) if folder.is_dir() else []
        path = frames[0] if frames else None

    img = Image.open(path).convert("RGBA") if path and path.exists() else None
    if img is None:
        print(f"  note: no art for {art}", file=sys.stderr)

    _sprites[art] = img
    return img


def art_for(piece, facing: int):
    """The picture a piece shows at one facing — `HomesteadArt.Still`'s choice.

    A piece with one facing is a single sprite at its own address. A piece with four is a
    folder of renders indexed by facing, which is the same machinery an animated piece
    uses for a different reason (see `HomesteadPiece.Facings`), so the two may never be
    both and this reader does not have to tell them apart.
    """
    facings = int(piece.get("facings") or 1)
    if facings <= 1:
        return sprite(piece["art"])
    return sprite(f"{piece['art']}/f{facing % facings}")


# -------------------------------------------------------------------- render
def render(floor, pieces, land, placed, dwelling, scale=0.5, pad_top=760.0):
    """
    `placed` is {tileId: (pieceId, facing)}; `land` is a list of region ids.

    Only owned ground is drawn, because that is what the game draws — a grove is exactly
    the land its keeper bought (invariant 16e), so a renderer that drew the whole field
    would flatter every layout by hiding its outline.
    """
    cols, rows = floor["cols"], floor["rows"]
    regions = {r["id"]: r for r in floor["regions"]}

    # Starter land is never written down — "absent" and "bought nothing" are the same fact
    # (invariant 16e), so a card's `land` holds only the regions that were paid for. A
    # renderer reading it literally draws a ring with a hole where the hall stands.
    #
    # **Starter means no price at all, in either currency** — `GroveFloor.IsStarter` is
    # `Cost <= 0 && Gems <= 0`. Reading it as `cost <= 0` alone is invariant 16j's own trap:
    # a gem-priced stretch carries `cost: 0`, so five of the eight paid regions read as free
    # and this drew half the world nobody had bought. The game fixed that predicate; this
    # mirror had not, so every picture it has drawn since the gem ladder shipped has been
    # flattering.
    def starter(r):
        return int(r.get("cost") or 0) <= 0 and int(r.get("gems") or 0) <= 0

    held = [r for r in floor["regions"] if starter(r) or r["id"] in land]

    owned = set()
    for r in held:
        for c in range(r["col"], r["col"] + r["cols"]):
            for w in range(r["row"], r["row"] + r["rows"]):
                owned.add((c, w))

    min_x = -(rows - 1) * TILE_W * 0.5 - TILE_W * 0.5
    max_x = (cols - 1) * TILE_W * 0.5 + TILE_W * 0.5
    max_y = (cols + rows - 2) * TILE_H * 0.5 + TILE_H

    width = int((max_x - min_x) * scale)
    height = int((max_y + pad_top) * scale)

    canvas = Image.new("RGBA", (width, height), (32, 44, 40, 255))

    def paste(img, cx, cy, w, h):
        """Centre `img` at (cx, cy) in floor space, drawn `w` x `h`.

        No transform. A facing used to be a mirror and was drawn by flipping the sprite;
        it is a quarter turn now and the art is a *different picture* per facing, chosen
        in `art_for` — which is what the game does (`HomesteadArt.Still`).
        """
        if img is None or w < 1 or h < 1:
            return
        s = img.resize((max(1, int(w * scale)), max(1, int(h * scale))), Image.LANCZOS)
        x = int((cx - min_x) * scale - s.width / 2)
        y = int((cy + pad_top) * scale - s.height / 2)
        canvas.alpha_composite(s, (x, y))

    # ---- ground
    tile_art = sprite(floor["tileArt"])
    if tile_art is not None:
        aspect = tile_art.height / tile_art.width
        gw = TILE_W * TILE_OVERLAP
        gh = gw * aspect
        drop = (gh - TILE_H * TILE_OVERLAP) * 0.5

        for (c, w) in sorted(owned, key=lambda t: draw_order(*t)):
            paste(tile_art, tile_x(c, w), tile_y(c, w) + drop, gw, gh)

    # ---- everything standing on it, back to front by footprint depth
    hall = floor["hallTile"]
    hall_cols, hall_rows = int(floor.get("hallCols") or 1), int(floor.get("hallRows") or 1)

    standing = []   # (depth, anchor col, anchor row, footprint cols, rows, piece id, facing)

    def stand(c, w, pid, facing, fcols, frows):
        front = draw_order(c + fcols - 1, w + frows - 1)
        depth = front * 2 + (1 if fcols == 1 and frows == 1 else 0)
        standing.append((depth, c, w, fcols, frows, pid, facing))

    for (c, w) in owned:
        tid = f"t_{c:03d}_{w:03d}"
        if tid == hall:
            stand(c, w, dwelling, 0, hall_cols, hall_rows)
        elif tid in placed:
            pid, facing = placed[tid]
            piece = pieces.get(pid)
            fcols, frows = (piece["cols"], piece["rows"]) if piece else (1, 1)
            # An odd quarter turn exchanges the axes; an even one puts them back.
            if facing % 2:
                fcols, frows = frows, fcols
            stand(c, w, pid, facing, fcols, frows)

    standing.sort()

    for (_depth, c, w, fcols, frows, pid, facing) in standing:
        piece = pieces.get(pid)
        if not piece:
            print(f"  note: unknown piece {pid}", file=sys.stderr)
            continue

        img = art_for(piece, facing)
        if img is None:
            continue

        k = PIECE_SCALE * piece["scale"]
        aw = piece["w"] or img.width
        ah = piece["h"] or img.height
        pw, ph = aw * k, ah * k

        cc = c + (fcols - 1) * 0.5
        cw = w + (frows - 1) * 0.5
        paste(img, tile_x(cc, cw), tile_y(cc, cw) - ph * piece["lift"], pw, ph)

    return canvas.convert("RGB")


# ---------------------------------------------------------------- live cards
def token() -> str:
    # shell=True because gcloud is a .cmd on Windows and CreateProcess will not find it.
    return subprocess.check_output("gcloud auth print-access-token", shell=True, text=True).strip()


def fetch_card(uid: str, bearer: str):
    req = urllib.request.Request(f"{FS}/groves/{uid}", headers={"Authorization": f"Bearer {bearer}"})
    with urllib.request.urlopen(req) as r:
        doc = json.load(r)

    f = doc["fields"]
    land = [v["stringValue"] for v in f.get("land", {}).get("arrayValue", {}).get("values", [])]

    placed = {}
    for tid, v in f.get("placed", {}).get("mapValue", {}).get("fields", {}).items():
        if "stringValue" in v:
            placed[tid] = (v["stringValue"], 0)
        else:
            inner = v["mapValue"]["fields"]
            placed[tid] = (inner["piece"]["stringValue"],
                           int(inner.get("facing", {}).get("integerValue", "0")))

    return {
        "name": f.get("name", {}).get("stringValue", uid),
        "land": land,
        "placed": placed,
        "dwelling": f.get("dwelling", {}).get("stringValue", "cottage"),
    }


def read_layout(path: Path):
    d = json.loads(path.read_text(encoding="utf8"))
    placed = {p["slot"]: (p["piece"], int(p.get("facing") or 0)) for p in d["placements"]}
    return {"name": d.get("name", path.stem), "land": d["land"],
            "placed": placed, "dwelling": d.get("dwelling", "cottage")}


# ------------------------------------------------------------------ the tool
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--card", action="append", default=[], help="grove card id to read live")
    ap.add_argument("--layout", action="append", default=[], help="local layout json")
    ap.add_argument("--all", action="store_true", help="the ten showcase cards")
    ap.add_argument("--out", default="grove-renders", help="where the PNGs go")
    ap.add_argument("--scale", type=float, default=0.5)
    args = ap.parse_args()

    floor, pieces = load_catalog()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    jobs = []
    ids = list(args.card)
    if args.all:
        ids += [f"showcase-{n:02d}" for n in range(1, 11)]

    if ids:
        bearer = token()
        for uid in ids:
            jobs.append((uid, fetch_card(uid, bearer)))

    for path in args.layout:
        p = Path(path)
        jobs.append((p.stem, read_layout(p)))

    for uid, card in jobs:
        img = render(floor, pieces, card["land"], card["placed"], card["dwelling"], args.scale)
        dest = out / f"{uid}.png"
        img.save(dest)
        print(f"{uid}  {card['name']}  {len(card['placed'])} placed  ->  {dest}")


if __name__ == "__main__":
    main()
