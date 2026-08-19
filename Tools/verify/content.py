"""End-to-end check of the shipped content, mirroring LevelValidator.cs
and ChapterMapValidator.cs."""
import json, math, os, sys
from collections import deque

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "..", "..", "Assets", "StreamingAssets", "Content")
N, E, S, W = 1, 2, 4, 8
BITS = [N, E, S, W]
STEP = [(0, -1), (1, 0), (0, 1), (-1, 0)]   # N E S W, matching Puzzle.Step
PAL = {'R': 1, 'G': 2, 'B': 4}
COLOURS = {
    'R': 1, 'G': 2, 'B': 4,
    'Y': 1 | 2, 'M': 1 | 4, 'C': 2 | 4, 'W': 1 | 2 | 4, 'A': 0,
}

errors, warnings = [], []


def rotl(mask, turns):
    turns &= 3
    out = 0
    for i in range(4):
        if mask & (1 << i):
            out |= 1 << ((i + turns) & 3)
    return out


def parse_token(tok, ctx):
    """-> dict(kind, solved, rot, locked, colour, fragile, link) or None for empty"""
    if tok == '.':
        return None


    if tok[0] not in '-*@x':
        errors.append(f"{ctx}: unknown head '{tok[0]}' in '{tok}'")
        return None

    kind = {'-': 'pipe', '*': 'source', '@': 'lamp', 'x': 'duskcap'}[tok[0]]
    p, mask = 1, 0
    while p < len(tok) and tok[p] in 'NESW':
        mask |= {'N': N, 'E': E, 'S': S, 'W': W}[tok[p]]
        p += 1
    if mask == 0:
        errors.append(f"{ctx}: '{tok}' has no arms")
        return None

    colour, rot, locked, fragile, link = 0, 0, False, 0, 0
    if p < len(tok) and tok[p] == '#':
        c = tok[p + 1]
        if c not in COLOURS:
            errors.append(f"{ctx}: unknown colour '{c}' in '{tok}'")
        colour = COLOURS.get(c, 0)
        p += 2
    if p < len(tok) and tok[p] == '/':
        rot = int(tok[p + 1]); p += 2
    if p < len(tok) and tok[p] == '!':
        locked = True; p += 1
    if p < len(tok) and tok[p] == '~':
        n = tok[p + 1] if p + 1 < len(tok) else ''
        if n < '1' or n > '9':
            errors.append(f"{ctx}: fragility '{n}' out of range in '{tok}', expected 1 to 9")
        else:
            fragile = int(n)
        p += 2
    if p < len(tok) and tok[p] == '&':
        r = tok[p + 1] if p + 1 < len(tok) else ''
        if r < 'A' or r > 'Z':
            errors.append(f"{ctx}: root rune '{r}' out of range in '{tok}', expected A to Z")
        else:
            link = ord(r) - ord('A') + 1
        p += 2
    if p != len(tok):
        errors.append(f"{ctx}: trailing '{tok[p:]}' in '{tok}'")
    if fragile and kind != 'pipe':
        errors.append(f"{ctx}: only a conduit can be fragile ('{tok}')")
    if link and kind != 'pipe':
        errors.append(f"{ctx}: only a conduit can share a taproot ('{tok}')")
    if link and locked:
        errors.append(f"{ctx}: '{tok}' is both rooted and bound to a taproot")
    if link and fragile:
        errors.append(f"{ctx}: '{tok}' is both brittle and bound to a taproot")
    if kind == 'source' and colour == 0:
        errors.append(f"{ctx}: heart-crystal '{tok}' emits no colour")
    if kind == 'duskcap' and colour:
        errors.append(f"{ctx}: a duskcap takes no colour ('{tok}')")

    return dict(kind=kind, solved=mask, rot=rot, locked=locked, colour=colour,
                fragile=fragile, link=link)


def check_level(level, chapter_id):
    lid = level.get('id', '<no id>')
    ctx = lid
    rows = level['rows']
    h = level.get('height') or len(rows)
    w = level.get('width') or max(len(r.split()) for r in rows)

    if len(rows) != h:
        errors.append(f"{ctx}: declared {h} rows, found {len(rows)}")
        return

    cells = []
    for y, row in enumerate(rows):
        toks = row.split()
        if len(toks) != w:
            errors.append(f"{ctx}: row {y} has {len(toks)} tokens, expected {w}")
            return
        for x, tok in enumerate(toks):
            cells.append(parse_token(tok, f"{ctx} @{x},{y}"))

    def at(x, y):
        return cells[y * w + x] if 0 <= x < w and 0 <= y < h else None

    # 1. every arm mates with a neighbour
    for y in range(h):
        for x in range(w):
            c = at(x, y)
            if not c:
                continue
            for d in range(4):
                if not (c['solved'] & BITS[d]):
                    continue
                nb = at(x + STEP[d][0], y + STEP[d][1])
                if nb is None:
                    errors.append(f"{ctx}: arm at {x},{y} points off the board or at empty")
                elif not (nb['solved'] & BITS[(d + 2) % 4]):
                    errors.append(f"{ctx}: arm at {x},{y} unmated by neighbour")

    # 2. the authored solution (all rot = 0) must light every critter
    comp = [-1] * len(cells)
    comp_colour = []
    for i, c in enumerate(cells):
        if not c or comp[i] != -1:
            continue
        g = len(comp_colour)
        colour = 0
        q = deque([i]); comp[i] = g
        while q:
            a = q.popleft()
            ca = cells[a]
            if ca['kind'] == 'source':
                colour |= ca['colour']
            ax, ay = a % w, a // w
            for d in range(4):
                if not (ca['solved'] & BITS[d]):
                    continue
                bx, by = ax + STEP[d][0], ay + STEP[d][1]
                nb = at(bx, by)
                if nb is None:
                    continue
                b = by * w + bx
                if comp[b] != -1 or not (nb['solved'] & BITS[(d + 2) % 4]):
                    continue
                comp[b] = g
                q.append(b)
        comp_colour.append(colour)

    # a fragile conduit must survive long enough to reach its own solution, or the
    # level is unwinnable while looking perfectly fine. Mirrors CheckFragileConduits.
    for i, c in enumerate(cells):
        if not c or not c.get('fragile'):
            continue
        fx, fy = i % w, i // w
        if c['locked']:
            warnings.append(f"{ctx}: the fragile conduit at {fx},{fy} is also rooted, so it never wears")
            continue
        if rotl(c['solved'], 1) == c['solved']:
            warnings.append(f"{ctx}: the conduit at {fx},{fy} is the same in every orientation, "
                            "so its fragility can never matter")
            continue
        owed = 0
        for k in range(4):
            if rotl(c['solved'], (c['rot'] + k) & 3) == c['solved']:
                owed = k
                break
        if owed > c['fragile']:
            errors.append(f"{ctx}: the fragile conduit at {fx},{fy} needs {owed} turn(s) but "
                          f"survives only {c['fragile']}; the level cannot be won")


    # A taproot must be able to reach its own solution: one number of turns has to solve
    # every conduit on it at once. Mirrors LevelValidator.CheckBoundConduits, and the same
    # class of mistake as a brittle conduit owed more turns than it survives - a level
    # nobody can finish that looks perfectly authored.
    roots = {}
    for i, c in enumerate(cells):
        if c and c.get('link'):
            roots.setdefault(c['link'], []).append(i)
    for rune, members in sorted(roots.items()):
        letter = chr(ord('A') + rune - 1)
        if len(members) < 2:
            i = members[0]
            errors.append(f"{ctx}: taproot '{letter}' has only the conduit at {i % w},{i // w} "
                          "on it; a root of one wears a binding mark and binds nothing")
            continue
        if not any(all(rotl(cells[i]['solved'], (cells[i]['rot'] + k) & 3) == cells[i]['solved']
                       for i in members) for k in range(4)):
            errors.append(f"{ctx}: the conduits on taproot '{letter}' can never all be right "
                          "at once, so the glade cannot be finished")

    # Past this the pips stop telling the roots apart. Mirrors Puzzle.MaxReadableRunes.
    MAX_READABLE_RUNES = 6
    real_roots = sum(1 for members in roots.values() if len(members) > 1)
    if real_roots > MAX_READABLE_RUNES:
        warnings.append(f"{ctx}: carries {real_roots} taproots but a mark can only tell "
                        f"{MAX_READABLE_RUNES} of them apart")

    lamps = lit = caps = woken = 0
    for i, c in enumerate(cells):
        if not c:
            continue
        have = comp_colour[comp[i]] if comp[i] >= 0 else 0
        if c['kind'] == 'duskcap':
            caps += 1
            if have:
                woken += 1
            continue
        if c['kind'] != 'lamp':
            continue
        lamps += 1
        want = c['colour']
        if (have != 0) if want == 0 else (have == want):
            lit += 1
    if lamps == 0:
        errors.append(f"{ctx}: no critters, unwinnable")
    elif lit != lamps:
        errors.append(f"{ctx}: authored solution lights only {lit}/{lamps} critters")
    if woken:
        errors.append(f"{ctx}: authored solution wakes {woken}/{caps} duskcap(s); a duskcap's "
                      "conduits must reach no heart-crystal at all")

    sources = sum(1 for c in cells if c and c['kind'] == 'source')
    if sources == 0:
        errors.append(f"{ctx}: no heart-crystal")

    # 3. derived par. A taproot is charged once however many conduits ride on it, because
    # one tap turns all of them - mirrors PuzzleFactory.MinimumMoves.
    par = 0
    charged = set()
    for c in cells:
        if not c or c['locked']:
            continue
        if c.get('link'):
            if c['link'] in charged:
                continue
            charged.add(c['link'])
            members = roots[c['link']]
            for k in range(4):
                if all(rotl(cells[i]['solved'], (cells[i]['rot'] + k) & 3) == cells[i]['solved']
                       for i in members):
                    par += k
                    break
            continue
        if rotl(c['solved'], 1) == c['solved']:
            continue
        for k in range(4):
            if rotl(c['solved'], (c['rot'] + k) & 3) == c['solved']:
                par += k
                break

    authored = level.get('par', 0)
    if authored and authored != par:
        warnings.append(f"{ctx}: authored par {authored} != derived {par}")

    mx, my = level.get('mapX', 0), level.get('mapY', 0)
    if not (0 <= mx <= 1 and 0 <= my <= 1):
        warnings.append(f"{ctx}: map position ({mx},{my}) outside 0..1")

    fragile = sum(1 for c in cells if c and c.get('fragile'))
    bound = len(roots)

    # The clock, derived exactly as LevelTuning does: seconds per par turn, with 0 meaning
    # "not authored" and only a negative value removing the timer. Gold is half the limit.
    time_factor = level.get('timeFactor', 0) or 200 / 100
    limit = 0 if time_factor < 0 else -(-int(round(par * time_factor * 1000)) // 1) // 1000
    star_rate = 0 if not limit else (-(-par * 135 // 100)) / (limit * 0.5)

    return dict(id=lid, chapter=chapter_id, w=w, h=h, par=par, limit=limit, rate=star_rate,
                gold=-(-par * 135 // 100), silver=-(-par * 200 // 100),
                lamps=lamps, sources=sources, fragile=fragile, caps=caps, bound=bound)


# Canvas geometry, mirroring ChapterMap.cs. mapX/mapY are fractions of the chapter's
# own map, and a chapter is as tall as the strips it declares - so the same pair of
# fractions is a collision in a one-strip chapter and half a screen apart in a six-strip
# one. Comparing raw fractions would be wrong for every chapter but one size.
MAP_WIDTH, STRIP_HEIGHT = 1080.0, 1200.0
NODE_DIAMETER, NODE_CLEARANCE = 196.0, 24.0
MIN_SEPARATION = NODE_DIAMETER + NODE_CLEARANCE
TEASER_GAP, TEASER_CEILING, TEASER_X = 0.22, 0.95, 0.66


def check_chapter_map(chapter, cid, ordered):
    """Placement checks that need the whole chapter: ChapterMapValidator.cs."""
    strips = len(chapter.get("mapStrips") or ["strip0"])
    height = max(STRIP_HEIGHT, strips * STRIP_HEIGHT)

    def place(level):
        return (min(1.0, max(0.0, level.get("mapX", 0))),
                min(1.0, max(0.0, level.get("mapY", 0))))

    def sep(a, b):
        return math.hypot((a[0] - b[0]) * MAP_WIDTH, (a[1] - b[1]) * height)

    pts = [(level["id"], place(level)) for level in ordered]

    for i in range(len(pts)):
        for k in range(i + 1, len(pts)):
            gap = sep(pts[i][1], pts[k][1])
            if gap < MIN_SEPARATION:
                warnings.append(f"chapter '{cid}': '{pts[i][0]}' and '{pts[k][0]}' are {gap:.0f} "
                                f"canvas units apart but a disc is {NODE_DIAMETER:.0f} across, "
                                "so they overlap on the map")

    for i in range(1, len(pts)):
        if pts[i][1][1] <= pts[i - 1][1][1]:
            warnings.append(f"chapter '{cid}': '{pts[i][0]}' sits at or below '{pts[i - 1][0]}' "
                            f"(mapY {pts[i][1][1]:g} after {pts[i - 1][1][1]:g}), so the trail "
                            "between them runs back down the map")

    highest = max([p[1][1] for p in pts], default=0.0)
    teaser = (TEASER_X, min(TEASER_CEILING, highest + TEASER_GAP))

    for lid, p in pts:
        gap = sep(p, teaser)
        if gap < MIN_SEPARATION:
            warnings.append(f"chapter '{cid}': '{lid}' is {gap:.0f} canvas units from the "
                            f"end-of-chapter marker at ({teaser[0]:.2f}, {teaser[1]:.2f})")


SLOT_KINDS = ("ground", "hearth", "structure", "bed", "path", "edge", "canopy")


# The five creatures the grove used to author, and the companion each was rewritten to.
# The mirror of GroveResidents.Retired - a save holding an old id has its placement
# rewritten at load, for ever, so a target that leaves the roster empties somebody's slot.
RESIDENT_PREFIX = "friend_"

RETIRED_RESIDENTS = {
    "sunmote": "puff",
    "ripple": "timber",
    "prism": "sprocket",
    "burr": "thistle",
    "dusk": "monarch",
}


def check_grove(keys, level_ids, chapter_ids, companions):
    """The grove catalog: its land, its residents and its shop.

    The offline half of ContentValidation.ValidateHomestead. It matters more than the
    usual parity here, because the shipped-catalog tests in HomesteadTests reach
    Application.dataPath and are therefore Editor-only - without this, nothing offline
    would look at homestead.json at all.

    Two things are errors and everything else warns, which is the line the Editor
    validator draws: a validator may not overrule an economy decision, but it may refuse
    a rule violation and a grove nobody can use.
    """
    path = os.path.join(ROOT, "homestead.json")
    if not os.path.exists(path):
        warnings.append("no homestead.json; the Grovement will have nothing to show")
        return None

    grove = json.load(open(path, encoding="utf-8"))

    if grove.get("schemaVersion") != 3:
        errors.append(f"homestead.json is schema v{grove.get('schemaVersion')}, this build reads v3 "
                      "- the grove is a tile floor now, not floating islands")

    floor = grove.get("floor") or {}
    pieces = grove.get("pieces") or []

    art_root = os.path.abspath(os.path.join(os.path.dirname(ROOT), "..", "Game", "Art"))

    def art_exists(key, animated):
        full = os.path.join(art_root, key.replace("/", os.sep))
        return os.path.isdir(full) if animated else os.path.exists(full + ".png")

    cols = int(floor.get("cols") or 0)
    rows = int(floor.get("rows") or 0)
    regions = floor.get("regions") or []

    if cols <= 0 or rows <= 0:
        errors.append("the grove floor has no size; there is nowhere to build")
        return None

    # Which region owns each tile, built once. The same map answers overlap, holes and the
    # two named tiles, and walking the regions per question would be four passes over a
    # field that can be forty thousand tiles.
    owner = {}
    region_ids = set()
    starters = 0
    land_total = 0

    for region in regions:
        rid = region.get("id", "")
        if not rid:
            errors.append("a grove region has no id")
            continue
        if rid in region_ids:
            errors.append(f"grove lists region '{rid}' twice")
        region_ids.add(rid)

        rc, rr = int(region.get("col", 0)), int(region.get("row", 0))
        rw, rh = int(region.get("cols", 0)), int(region.get("rows", 0))
        cost = int(region.get("cost", 0))

        if rw <= 0 or rh <= 0:
            errors.append(f"grove region '{rid}' is {rw}x{rh}; it holds no tiles")
            continue

        if rc < 0 or rr < 0 or rc + rw > cols or rr + rh > rows:
            errors.append(f"grove region '{rid}' runs off a {cols}x{rows} field")
            continue

        if cost <= 0:
            starters += 1
        else:
            land_total += cost

        if f"ui.land.{rid}" not in keys:
            errors.append(f"grove region '{rid}' missing string 'ui.land.{rid}'")

        for c in range(rc, rc + rw):
            for r in range(rr, rr + rh):
                tid = "t_%03d_%03d" % (c, r)
                if tid in owner:
                    errors.append(f"grove regions '{owner[tid]}' and '{rid}' both hold tile "
                                  f"{tid}; who owns it would depend on the order of the file")
                    continue
                owner[tid] = rid

    # An error, because it is the one that ships a broken first launch: a floor with no free
    # region opens the Grovement onto a screen the player owns nothing on.
    if not starters:
        errors.append("no grove region is free from the first launch; a new player would open "
                      "the Grovement owning none of it")

    loose = cols * rows - len(owner)
    if loose:
        warnings.append(f"{loose} grove tile(s) belong to no region, so nobody can ever own "
                        "them; they are drawn locked for ever")

    # The hall has to be reachable on the first launch or the feature opens onto a padlock
    # where the house should be. Both of these look perfectly authored in the file.
    def named_tile(field, what, required):
        tid = floor.get(field) or ""
        if not tid:
            (errors if required else warnings).append(
                f"the grove floor names no tile for the {what}")
            return None
        if tid not in owner:
            errors.append(f"the grove's {what} stands on {tid}, which belongs to no region "
                          "and can never be owned")
            return None
        rid = owner[tid]
        cost = next((int(x.get("cost", 0)) for x in regions if x.get("id") == rid), 0)
        if cost > 0:
            errors.append(f"the grove's {what} stands on {tid}, in region '{rid}', which costs "
                          f"{cost}; a new player would see it behind a padlock")
        return tid

    hall = named_tile("hallTile", "hall", True)
    named_tile("starterTile", "starter companion", False)

    tile_art = floor.get("tileArt") or ""
    if tile_art and not art_exists(tile_art, False):
        errors.append(f"the grove floor names tile art at Art/{tile_art}.png, which is not there")

    hearths = [hall] if hall else []

    piece_ids = set()
    piece_starters = for_sale = earned = 0
    total = 0
    dwellings = []
    decor_kinds = set()

    for piece in pieces:
        pid = piece.get("id", "")
        if not pid:
            errors.append("a grove piece has no id")
            continue
        if pid in piece_ids:
            errors.append(f"grove lists piece '{pid}' twice")
        piece_ids.add(pid)

        kind = (piece.get("kind") or "decor").lower()
        cost = piece.get("cost", 0)
        needs = bool(piece.get("requiresLevel") or piece.get("requiresChapter"))

        # Residents are the companion roster now, projected in by GroveResidents rather
        # than authored here — so a row claiming to be one is a second creature list with
        # its own price and its own gate, which is the duplication projection removed.
        # HomesteadMapper drops it; this is the same refusal one file earlier.
        if kind == "resident":
            errors.append(f"grove piece '{pid}' is authored as a resident; residents are the "
                          "companion roster in manifest.json and are projected in, so this "
                          "row is ignored by the game — delete it")

        if kind == "dwelling":
            dwellings.append((piece.get("tier", 0), pid, cost))
        elif kind != "resident":
            slot_kind = piece.get("slot") or "ground"
            if slot_kind not in SLOT_KINDS or slot_kind == "hearth":
                errors.append(f"grove piece '{pid}' belongs in slot kind '{slot_kind}', "
                              "which is not a kind anything can be placed in")
            decor_kinds.add(slot_kind)

        if cost > 0:
            for_sale += 1
            total += cost
        if needs:
            earned += 1
        if not needs and cost <= 0:
            piece_starters += 1

        art = piece.get("art") or f"Homestead/{pid}"
        if not art_exists(art, piece.get("animated", False)):
            errors.append(f"grove piece '{pid}' has no art at Art/{art}"
                          f"{'/' if piece.get('animated') else '.png'}")

        if f"ui.piece.{pid}" not in keys:
            errors.append(f"grove piece '{pid}' missing string 'ui.piece.{pid}'")

        lvl = piece.get("requiresLevel")
        if lvl and lvl not in level_ids:
            warnings.append(f"grove piece '{pid}' is earned by clearing '{lvl}', which the "
                            "catalog does not carry")

        chap = piece.get("requiresChapter")
        if chap and chap not in chapter_ids:
            warnings.append(f"grove piece '{pid}' is earned by finishing chapter '{chap}', "
                            "which the catalog does not carry")

    if not piece_starters:
        errors.append("no grove piece is free from the first launch; a new player would open "
                      "the picker onto an empty list")

    if not companions:
        warnings.append("the manifest carries no companions; the grove's residents shelf is "
                        "the roster, so an empty roster empties a whole shelf of the shop")

    # The prefix is reserved. Companion ids and piece ids were minted independently and
    # already collided once ('pebble' is a rock and a companion), which is why a resident's
    # piece id is the companion's id prefixed - so the two spaces can never meet. An
    # authored piece wearing the prefix would put them back together.
    taken = {p for p in piece_ids if p.startswith(RESIDENT_PREFIX)}
    if taken:
        errors.append(f"'{RESIDENT_PREFIX}' is reserved for residents projected from the "
                      "companion roster; these authored pieces use it: " + ", ".join(sorted(taken)))

    # The five creatures the grove used to author, and the companion each was rewritten to.
    # It must stay in step with GroveResidents.Retired: a target that has left the roster
    # empties every slot holding the old id.
    for retired, became in RETIRED_RESIDENTS.items():
        if became not in companions:
            errors.append(f"the retired grove resident '{retired}' is rewritten to companion "
                          f"'{became}', which the roster no longer carries")

    # The home ladder. Every failure here is invisible in the game: a catalog with dwellings
    # and no hearth draws no home and looks exactly like one with no dwellings, and two rungs
    # on one tier make "the best one owned" depend on the order of the file.
    if dwellings and not hearths:
        errors.append(f"the grove has {len(dwellings)} home(s) and no hearth slot to draw one "
                      "on; they would be bought and never seen")
    if hearths and not dwellings:
        errors.append("the grove has a hearth and no home to stand on it")
    if len(hearths) > 1:
        warnings.append(f"{len(hearths)} hearth slots ({', '.join(hearths)}); the same home "
                        "draws on every one of them")

    tiers = {}
    for tier, pid, _cost in dwellings:
        if tier <= 0:
            errors.append(f"grove home '{pid}' has no tier; the ladder cannot be ordered")
        if tier in tiers:
            errors.append(f"grove homes '{tiers[tier]}' and '{pid}' are both tier {tier}")
        tiers[tier] = pid

    if dwellings:
        first = min(dwellings)
        first_rows = [p for p in pieces if p.get("id") == first[1]]
        if first[2] > 0 or (first_rows and (first_rows[0].get("requiresLevel") or first_rows[0].get("requiresChapter"))):
            errors.append(f"the first home '{first[1]}' is not free; a new grove would open "
                          "with nothing on its hearth")

    return {
        "homes": len(dwellings), "ladder": sum(c for _t, _p, c in dwellings),
        "cols": cols, "rows": rows, "regions": len(regions), "free_regions": starters,
        "owned_tiles": len(owner), "land": land_total,
        "slots": cols * rows, "pieces": len(pieces),
        "residents": len(companions),
        "for_sale": for_sale, "earned": earned, "starters": piece_starters, "total": total,
    }


def main():
    manifest = json.load(open(os.path.join(ROOT, "manifest.json"), encoding="utf-8"))
    loc = json.load(open(os.path.join(ROOT, "loc", "en.json"), encoding="utf-8"))
    keys = {e["key"] for e in loc["entries"]}

    print(f"manifest schema v{manifest['schemaVersion']}, "
          f"{len(manifest['chapters'])} chapter(s)\n")

    # Every file carrying a schemaVersion is checked, not just the manifest. Three
    # separate readers call ContentSchema.Explain - the manifest, each chapter body and
    # progression.json - so bumping the contract means touching all three. Checking only
    # the one you happened to think of is exactly how progression.json got left on v1.
    # The catalog and the reward table version independently: progression.json is
    # delivered on its own and changes at a different rate, so a catalog format bump
    # must not invalidate it for clients that have not updated. See ProgressionSchema.
    EXPECTED = {"manifest.json": 2, "progression.json": 1}
    for f in sorted(os.listdir(os.path.join(ROOT, "chapters"))):
        if f.endswith(".json"):
            EXPECTED[os.path.join("chapters", f)] = 2

    for rel, want in EXPECTED.items():
        full = os.path.join(ROOT, rel)
        if not os.path.exists(full):
            errors.append(f"{rel} is missing")
            continue
        got = json.load(open(full, encoding="utf-8")).get("schemaVersion")
        if got != want:
            errors.append(f"{rel} is schema v{got}, this build reads v{want}")

    # A chapter file nobody listed is not loaded and rejected - it is never opened, so
    # every other check here passes it in silence and the build ships without it. The
    # one check that cannot be made by reading the manifest, because its subject is what
    # the manifest failed to say. Mirrors ContentValidation.ValidateManifestCoverage.
    # Disabled entries count as listed: retired is not the same as forgotten.
    listed_ids = {e["id"] for e in manifest["chapters"] if e.get("id")}
    for f in sorted(os.listdir(os.path.join(ROOT, "chapters"))):
        if not f.endswith(".json"):
            continue
        if f[:-5] not in listed_ids:
            errors.append(f"chapters/{f} is not listed in manifest.json, so nothing will ever "
                          "read it - run Content > Sync Manifest to adopt it")

    # Orders are sparse so there is never a reason for two chapters to collide; a tie
    # sorts by id, which is deterministic but is never what anyone meant.
    seen_orders = {}
    for entry in manifest["chapters"]:
        order = entry.get("order", 0)
        if order in seen_orders:
            errors.append(f"chapters '{seen_orders[order]}' and '{entry['id']}' both claim order {order}")
        seen_orders[order] = entry["id"]

    summaries = []
    for entry in manifest["chapters"]:
        if entry.get("disabled"):
            continue
        cid = entry["id"]
        path = os.path.join(ROOT, "chapters", f"{cid}.json")
        if not os.path.exists(path):
            errors.append(f"manifest lists '{cid}' but {path} is missing")
            continue
        chapter = json.load(open(path, encoding="utf-8"))
        if chapter["id"] != cid:
            errors.append(f"{path} calls itself '{chapter['id']}'")

        # Order lives in the manifest. A body carrying one is a stale file whose author
        # believes a number that does nothing.
        if "order" in chapter:
            errors.append(f"chapter '{cid}' sets \"order\" in its body; order belongs in manifest.json")

        # A chapter that does not name its own backdrop inherits one, which puts its art
        # in another chapter's asset bundle.
        if not chapter.get("backdrop"):
            errors.append(f"chapter '{cid}' does not name a backdrop")

        # The manifest is the authority on membership and order; the body is the
        # authority on content. Sync Manifest generates one from the other, so any
        # disagreement means it was not run.
        listed = entry.get("levels") or []
        authored = [lv["id"] for lv in chapter["levels"]]
        if listed != authored:
            missing = [x for x in authored if x not in listed]
            extra = [x for x in listed if x not in authored]
            detail = []
            if missing:
                detail.append(f"body has unlisted {missing}")
            if extra:
                detail.append(f"manifest lists absent {extra}")
            if not detail:
                detail.append("same levels, different order")
            errors.append(f"chapter '{cid}' manifest/body mismatch: {'; '.join(detail)} "
                          "- run Content > Sync Manifest")

        ckey = f"chapter.{cid}.name"
        if ckey not in keys:
            errors.append(f"chapter '{cid}' missing string '{ckey}'")

        # Play order is the manifest's, and the map checks are about consecutive glades,
        # so they must see that order rather than the body's.
        by_id = {lv["id"]: lv for lv in chapter["levels"]}
        ordered = [by_id[i] for i in listed if i in by_id] or chapter["levels"]
        check_chapter_map(chapter, cid, ordered)

        for level in chapter["levels"]:
            s = check_level(level, cid)
            if s:
                summaries.append(s)
            lid = level["id"]
            for suffix, field in (("name", "nameKey"), ("tagline", "taglineKey"), ("lesson", "lessonKey")):
                k = level.get(field) or f"level.{lid}.{suffix}"
                if k not in keys:
                    errors.append(f"level '{lid}' missing string '{k}'")

    print(f"{'#':<3}{'level id':<22}{'chapter':<16}{'size':<7}{'par':<5}{'gold':<6}{'silver':<7}"
          f"{'clock':<7}{'3*taps/s':<10}{'hearts':<7}{'critters':<9}{'brittle':<8}"
          f"{'duskcaps':<10}roots")
    for i, s in enumerate(summaries, 1):
        clock = "-" if not s['limit'] else f"{s['limit']}s"
        rate = "-" if not s['rate'] else f"{s['rate']:.2f}"
        print(f"{i:<3}{s['id']:<22}{s['chapter']:<16}{str(s['w'])+'x'+str(s['h']):<7}"
              f"{s['par']:<5}{s['gold']:<6}{s['silver']:<7}{clock:<7}{rate:<10}"
              f"{s['sources']:<7}{s['lamps']:<9}{s['fragile']:<8}{s['caps']:<10}{s['bound']}")

    grove = check_grove(keys,
                        {lv for e in manifest["chapters"] for lv in (e.get("levels") or [])},
                        {e["id"] for e in manifest["chapters"] if e.get("id")},
                        {c["id"] for c in (manifest.get("companions") or [])
                         if c.get("id") and not c.get("disabled")})

    if grove:
        print(f"\ngrove: {grove['cols']}x{grove['rows']} floor, {grove['slots']} tile(s), "
              f"{grove['pieces']} piece(s) - {grove['residents']} resident(s) from the roster, "
              f"{grove['starters']} free, {grove['earned']} earned, {grove['for_sale']} for sale "
              f"({grove['total']} credits in all)")
        print(f"       land: {grove['regions']} region(s), {grove['free_regions']} free, "
              f"{grove['owned_tiles']} tile(s) sellable - {grove['land']} credits to own it all")
        print(f"       home ladder: {grove['homes']} rung(s), {grove['ladder']} credits to the top")

    print()
    for w in warnings:
        print("WARN  " + w)
    for e in errors:
        print("ERROR " + e)
    print(f"\n{len(summaries)} level(s): {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


sys.exit(main())
