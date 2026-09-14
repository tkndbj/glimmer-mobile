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
import re
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
def render(floor, pieces, land, placed, dwelling, scale=0.5, pad_top=760.0, ground=(32, 44, 40),
           hall_seat=None):
    """
    `placed` is {tileId: (pieceId, facing)}; `land` is a list of region ids; `hall_seat` is
    the card's `(slot, facing)` for a keeper who moved their home, or None.

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

    # `ground` is None for `--screen`, which composites the village over a real sky.
    canvas = Image.new("RGBA", (width, height), (ground + (255,)) if ground else (0, 0, 0, 0))

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
    hall_cols, hall_rows = int(floor.get("hallCols") or 1), int(floor.get("hallRows") or 1)

    # The hall stands where its keeper moved it, and where the floor says only when they
    # never did — `GroveCard.HallSeat`, with the same fallback for a seat the plot cannot
    # fit (invariant 16q). This read the floor's constant for as long as a seat was content.
    def parse(tid):
        m = re.fullmatch(r"t_(\d{3})_(\d{3})", tid or "")
        return (int(m.group(1)), int(m.group(2))) if m else None

    hall_facing = 0
    hall_at = None
    if hall_seat:
        seat = parse(hall_seat[0])
        if seat and seat[0] + hall_cols <= cols and seat[1] + hall_rows <= rows:
            hall_at, hall_facing = seat, int(hall_seat[1] or 0) % 4
    if hall_at is None:
        hall_at = parse(floor["hallTile"])

    def on_hall(c, w, fcols, frows):
        return (hall_at is not None
                and c < hall_at[0] + hall_cols and hall_at[0] < c + fcols
                and w < hall_at[1] + hall_rows and hall_at[1] < w + frows)

    standing = []   # (depth, anchor col, anchor row, footprint cols, rows, piece id, facing)

    def stand(c, w, pid, facing, fcols, frows):
        front = draw_order(c + fcols - 1, w + frows - 1)
        depth = front * 2 + (1 if fcols == 1 and frows == 1 else 0)
        standing.append((depth, c, w, fcols, frows, pid, facing))

    if hall_at is not None and hall_at in owned:
        stand(hall_at[0], hall_at[1], dwelling, hall_facing, hall_cols, hall_rows)

    for (c, w) in owned:
        tid = f"t_{c:03d}_{w:03d}"
        if tid in placed:
            pid, facing = placed[tid]
            piece = pieces.get(pid)
            fcols, frows = (piece["cols"], piece["rows"]) if piece else (1, 1)
            # An odd quarter turn exchanges the axes; an even one puts them back.
            if facing % 2:
                fcols, frows = frows, fcols
            # Nothing stands on the hall's plot (invariant 16w): the game's index refuses a
            # stand that touches it, so a row a plot grew over is not drawn through the house.
            if on_hall(c, w, fcols, frows):
                print(f"  note: {pid} at {tid} stands on the hall's plot and is not drawn", file=sys.stderr)
                continue
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

    return canvas if ground is None else canvas.convert("RGB")


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
        "hall": (f.get("hall", {}).get("stringValue", ""),
                 int(f.get("hallFacing", {}).get("integerValue", "0"))),
    }


def read_layout(path: Path):
    d = json.loads(path.read_text(encoding="utf8"))
    placed = {p["slot"]: (p["piece"], int(p.get("facing") or 0)) for p in d["placements"]}
    return {"name": d.get("name", path.stem), "land": d["land"],
            "placed": placed, "dwelling": d.get("dwelling", "cottage"),
            "hall": (d.get("hall", ""), int(d.get("hallFacing") or 0))}


# ---------------------------------------------------------------- the screen
# `Boot.RefWidth` / `Boot.RefHeight`, and `Scenery.Cover`'s own 1.06 on the backdrop.
CANVAS_W, CANVAS_H = 1080, 1920
COVER_SCALE = 1.06

# `HomesteadScreen.Build` — the sky's shade and vignette, and `Scenery.Sun`.
SKY_DIM = 0.00
SKY_VIGNETTE = 0.14
VIGNETTE_RGB = (3, 10, 18)                      # Scenery.Cover's (.01, .04, .07)

SUN_LEAN = 0.078                                # of RefHeight, from the centre
SUN_RISE = 0.25                                 # of RefHeight, below the top edge
SUN_LAYERS = [                                  # (sprite power, size / RefHeight, rgba)
    (1.35, 1.10, (255, 224, 148, 0.30)),
    (2.80, 0.46, (255, 240, 184, 0.46)),
    (None, 0.095, (255, 251, 230, 0.92)),
]

# `HomesteadScreen.BuildHeader`'s TopFade: a full-bleed gradient, 1 at the very top and 0 at
# its own foot, drawn over the sky *and* over the field. It is in here because the sun is
# placed to clear it — a mirror that left it out would be drawing a screen this game does not
# have, and the number it exists to settle is exactly "is the sun under the banner".
FADE_RGB = (5, 15, 23)                          # (.02, .06, .09)
FADE_ALPHA = 0.42
FADE_DEPTH = 268                                # + SafeArea.Top, which `--notch` adds
NOTCH_TOP = 90


def _glow(size: int, power: float | None) -> Image.Image:
    """`Art.Glow` and `Art.Disc`, to the pixel — a soft radial, or a hard one.

    Written out rather than approximated because the whole value of this mode is that a
    number tuned here is the number the screen gets: a mirror that draws a *similar* glow
    answers "does a sun look nice" and not "does this sun look nice" (invariant 44d).
    """
    import numpy as np

    h = size * 0.5
    y, x = np.mgrid[0:size, 0:size]
    d = np.sqrt(((x + .5 - h) / h) ** 2 + ((y + .5 - h) / h) ** 2)
    a = np.clip(1.0 - d, 0, 1) ** power if power else np.clip((1.0 - d) * h, 0, 1)
    return Image.fromarray((a * 255).astype("uint8"), "L")


def screen(grove: Image.Image, notch: bool = False) -> Image.Image:
    """The Grovement as the phone draws it: sky, sun, vignette, then the village.

    **What this can and cannot answer.** It is a mirror of the *light* on this screen and
    not of its layout — the field is a pan-and-zoom view with a header and a shelf over it,
    and none of that is here. What it does hold exactly is every number `Scenery` uses, so
    "is the picture bright" and "is the sun where the models are lit from" are questions it
    settles, and "is the button reachable" is not one to ask it.
    """
    import numpy as np

    canvas = Image.new("RGB", (CANVAS_W, CANVAS_H))

    # ---- the sky, envelope-fitted and over-scaled exactly as `Scenery.Cover` does
    sky = Image.open(ART / "Bg" / "home_sky.png").convert("RGB")
    k = max(CANVAS_W / sky.width, CANVAS_H / sky.height) * COVER_SCALE
    sky = sky.resize((max(1, round(sky.width * k)), max(1, round(sky.height * k))), Image.LANCZOS)
    canvas.paste(sky, ((CANVAS_W - sky.width) // 2, (CANVAS_H - sky.height) // 2))

    if SKY_DIM > 0:
        canvas = Image.blend(canvas, Image.new("RGB", canvas.size, (8, 15, 23)), SKY_DIM)

    # ---- the vignette, stretched over the whole screen (`UIKit.Node` fills its parent, so
    # the sprite's circle is drawn as an ellipse — which is what the game shows).
    gy, gx = np.mgrid[0:CANVAS_H, 0:CANVAS_W]
    d = np.sqrt((gx / (CANVAS_W - 1) * 2 - 1) ** 2 + (gy / (CANVAS_H - 1) * 2 - 1) ** 2)
    a = np.clip((d - .45) / .75, 0, 1) * SKY_VIGNETTE
    shade = Image.new("RGBA", canvas.size, VIGNETTE_RGB + (0,))
    shade.putalpha(Image.fromarray((a * 255).astype("uint8"), "L"))
    canvas = Image.alpha_composite(canvas.convert("RGBA"), shade)

    # ---- the sun, after the shade and the vignette (`Scenery.Sun`)
    for power, share, (r, g, b, alpha) in SUN_LAYERS:
        px = max(4, round(CANVAS_H * share))
        mask = _glow(256 if power else 128, power).resize((px, px), Image.LANCZOS)
        mask = mask.point(lambda v, m=alpha: int(v * m))
        lamp = Image.new("RGBA", (px, px), (r, g, b, 0))
        lamp.putalpha(mask)
        # `UIKit.Box` always pivots at centre, anchored to the top edge (invariant 44d).
        x = round(CANVAS_W * .5 + CANVAS_H * SUN_LEAN - px * .5)
        y = round(CANVAS_H * SUN_RISE - px * .5)
        canvas.alpha_composite(lamp, (x, y))

    # ---- and the village over all of it
    fit = grove.convert("RGBA")
    if fit.width > CANVAS_W:
        fit = fit.resize((CANVAS_W, round(fit.height * CANVAS_W / fit.width)), Image.LANCZOS)
    canvas.alpha_composite(fit, ((CANVAS_W - fit.width) // 2,
                                 max(0, (CANVAS_H - fit.height) // 2)))

    # ---- and the header fade over all of it, because that is the order the screen builds in
    deep = FADE_DEPTH + (NOTCH_TOP if notch else 0)
    ramp = np.clip(1.0 - np.arange(CANVAS_H) / float(deep), 0, 1) * FADE_ALPHA
    veil = Image.new("RGBA", canvas.size, FADE_RGB + (0,))
    veil.putalpha(Image.fromarray(
        (np.repeat(ramp[:, None], CANVAS_W, axis=1) * 255).astype("uint8"), "L"))
    canvas = Image.alpha_composite(canvas, veil)
    return canvas.convert("RGB")


# ------------------------------------------------------------------ the tool
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--card", action="append", default=[], help="grove card id to read live")
    ap.add_argument("--layout", action="append", default=[], help="local layout json")
    ap.add_argument("--all", action="store_true", help="the ten showcase cards")
    ap.add_argument("--out", default="grove-renders", help="where the PNGs go")
    ap.add_argument("--scale", type=float, default=0.5)
    ap.add_argument("--screen", action="store_true",
                    help="draw it on the Grovement's own sky, sun, vignette and header fade")
    ap.add_argument("--notch", action="store_true",
                    help="with --screen, a display whose safe area has taken the top 90 units")
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
        img = render(floor, pieces, card["land"], card["placed"], card["dwelling"], args.scale,
                     ground=None if args.screen else (32, 44, 40), hall_seat=card.get("hall"))
        if args.screen:
            img = screen(img, notch=args.notch)
        dest = out / f"{uid}.png"
        img.save(dest)
        print(f"{uid}  {card['name']}  {len(card['placed'])} placed  ->  {dest}")


if __name__ == "__main__":
    main()
