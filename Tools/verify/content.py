"""End-to-end check of the shipped content, mirroring LevelValidator.cs
and ChapterMapValidator.cs."""
import json
import re, math, os, sys
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


def alike(solved, cross, turns, gate=0):
    """Whether a tile turned this far from its solution is indistinguishable from it.

    Mirrors Puzzle.Alike, and it is the whole of "is this tile solved" everywhere here.
    Both four-armed tiles wear all four arms at every angle, so the bare mask comparison
    this replaced calls every one of them solved - deriving a par short by one per twisted
    crossing, on a board that cannot be finished.

    A briar's `gate` is the stricter reading and is asked first: a turn that merely swapped
    a crossing's two interchangeable labels has moved a briar's thorns onto the way the
    light was using.
    """
    if rotl(solved, turns) != solved:
        return False
    if gate:
        return rotl(gate, turns) == gate
    if not cross:
        return True
    strand = rotl(cross, turns)
    return strand == cross or strand == (solved & ~cross & 15)


def live(c):
    """The arms of a cell that actually carry light, in the authored solution.

    Mirrors Puzzle.Live. Every tile but a briar conducts along every arm it draws; a briar
    draws four and conducts two, which is the one place where "there is an arm here" and
    "light may go this way" are different questions.
    """
    return c['gate'] or c['solved']


def read_arms(tok, p):
    mask = 0
    while p < len(tok) and tok[p] in 'NESW':
        mask |= {'N': N, 'E': E, 'S': S, 'W': W}[tok[p]]
        p += 1
    return mask, p


def parse_token(tok, ctx):
    """-> dict(kind, solved, rot, locked, colour, fragile, link, cross) or None for empty"""
    if tok == '.':
        return None


    if tok[0] not in '-=%*@x':
        errors.append(f"{ctx}: unknown head '{tok[0]}' in '{tok}'")
        return None

    kind = {'-': 'pipe', '=': 'cross', '%': 'briar', '*': 'source', '@': 'lamp',
            'x': 'duskcap'}[tok[0]]
    mask, p = read_arms(tok, 1)
    if mask == 0:
        errors.append(f"{ctx}: '{tok}' has no arms")
        return None

    # '+' names one of a four-armed tile's two pairs: on a crossing the arms carrying one
    # flow, on a briar the arms that are open. The order matters on a briar and not on a
    # crossing, which is the difference between the two mechanics stated in the grammar.
    cross = gate = 0
    if p < len(tok) and tok[p] == '+':
        if kind not in ('cross', 'briar'):
            errors.append(f"{ctx}: '+' separates the two pairs of arms on a crossing ('=') "
                          f"or a briar ('%') ('{tok}')")
        second, p = read_arms(tok, p + 1)
        if second == 0:
            errors.append(f"{ctx}: '+' with no arms after it in '{tok}'")
        elif mask & second:
            errors.append(f"{ctx}: the two pairs of '{tok}' share an arm")
        if kind == 'briar':
            gate = mask
        else:
            cross = mask
        mask |= second

    if kind in ('cross', 'briar'):
        named = cross or gate
        other = mask & ~named & 15
        if not named:
            errors.append(f"{ctx}: '{tok}' must say which arms are which pair, as "
                          f"'{'=NS+EW' if kind == 'cross' else '%NS+EW'}'")
        elif bin(named).count('1') != 2 or bin(other).count('1') != 2:
            errors.append(f"{ctx}: a {kind} carries exactly two arms on each of its two "
                          f"pairs ('{tok}')")

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
    if fragile and kind not in ('pipe', 'cross', 'briar'):
        errors.append(f"{ctx}: only a conduit can be fragile ('{tok}')")
    if link and kind not in ('pipe', 'cross', 'briar'):
        errors.append(f"{ctx}: only a conduit can share a taproot ('{tok}')")
    if link and locked:
        errors.append(f"{ctx}: '{tok}' is both rooted and bound to a taproot")
    if link and fragile:
        errors.append(f"{ctx}: '{tok}' is both brittle and bound to a taproot")
    if kind == 'source' and colour == 0:
        errors.append(f"{ctx}: heart-crystal '{tok}' emits no colour")
    if kind == 'duskcap' and colour:
        errors.append(f"{ctx}: a duskcap takes no colour ('{tok}')")
    if kind == 'cross' and colour:
        errors.append(f"{ctx}: a crossing takes no colour ('{tok}')")
    if kind == 'briar' and colour:
        errors.append(f"{ctx}: a briar takes no colour; it decides which way light may go, "
                      f"never which light may go there ('{tok}')")

    return dict(kind=kind, solved=mask, rot=rot, locked=locked, colour=colour,
                fragile=fragile, link=link, cross=cross, gate=gate)


# Difficulty, mirroring LevelTuning.cs and DifficultyRuleTable.cs. The star thresholds are
# factors over par rather than fractions of the limit, which is what lets the published
# clockScale move where a run is lost without moving what a clear is worth.
DEFAULT_TIME_FACTOR = 1.70
TIME_GOLD_FACTOR, TIME_SILVER_FACTOR = 1.00, 1.50
MIN_CLOCK_SCALE, MAX_CLOCK_SCALE = 0.60, 2.00
FINISH_TAP_RATE, STAR_TAP_RATE = 1.2, 1.8


MODE_BLOCKS = ("fall", "keeper", "weave")


def check_level(level, chapter_id):
    """A level is a glade, or it carries exactly one mode block.

    Mirrors ContentMapper: the modes are asked which one claims the level rather than this
    growing a branch each time one is added. Nothing outside the classic mode has authored
    difficulty, so there is nothing here to prove about them beyond the block being sane -
    the real checks live in each LevelMode.Validate and run in the Editor.
    """
    lid = level.get('id', '?')

    claimed = [b for b in MODE_BLOCKS if level.get(b)]
    if len(claimed) > 1:
        errors.append("%s: carries %s blocks; a level is played one way"
                      % (lid, " and ".join(claimed)))

    if claimed:
        block = level[claimed[0]]
        if level.get('rows'):
            errors.append("%s: carries both a grid and a '%s' block" % (lid, claimed[0]))
        return dict(id=lid, chapter=chapter_id,
                    w=block.get('width', 0), h=block.get('height', 0), par=0, limit=0, rate=0,
                    gold=0, silver=0, lamps=0, sources=0, fragile=0, caps=0, bound=0,
                    crossings=0, briars=0, mode=claimed[0])

    # From here it is a glade: a grid of conduits, with everything that has to be proved
    # about one.
    ctx = lid
    if not level.get('rows'):
        errors.append("%s: has no grid and no mode block, so there is no way to play it" % lid)
        return None

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

    # 2. the authored solution (all rot = 0) must light every critter.
    #
    # Walked over strands rather than cells: an ordinary tile has one, and a crossing has
    # two that pass through one another and never meet. Mirrors Puzzle.Evaluate, which is
    # what lets a dark island run *through* a live network instead of only beside it.
    def strands(c):
        return 2 if c and c['cross'] else 1

    def strand_at(c, d):
        if not c['cross']:
            return 0
        return 0 if c['cross'] & BITS[d] else 1

    comp = [-1] * (len(cells) * 2)
    comp_colour = []
    for start in range(len(cells) * 2):
        i, st = start // 2, start % 2
        c = cells[i]
        if not c or st >= strands(c) or comp[start] != -1:
            continue
        g = len(comp_colour)
        colour = 0
        q = deque([start]); comp[start] = g
        while q:
            node = q.popleft()
            a, sa = node // 2, node % 2
            ca = cells[a]
            if ca['kind'] == 'source':
                colour |= ca['colour']
            ax, ay = a % w, a // w
            for d in range(4):
                if not (live(ca) & BITS[d]):
                    continue
                if strand_at(ca, d) != sa:
                    continue
                bx, by = ax + STEP[d][0], ay + STEP[d][1]
                nb = at(bx, by)
                if nb is None:
                    continue
                back = (d + 2) % 4
                if not (live(nb) & BITS[back]):
                    continue
                into = (by * w + bx) * 2 + strand_at(nb, back)
                if comp[into] != -1:
                    continue
                comp[into] = g
                q.append(into)
        comp_colour.append(colour)

    def energy(i):
        """Every colour reaching a cell, across all of its strands."""
        mix = 0
        for st in range(strands(cells[i])):
            g = comp[i * 2 + st]
            if g >= 0:
                mix |= comp_colour[g]
        return mix

    # A rooted tile must already read as solved, because every proof above ran against
    # a copy of the board with every rotation zeroed - and a rooted tile can never be
    # turned, so one authored away from its solution means what was proved is not what
    # ships. Mirrors LevelValidator.CheckRootedTiles.
    for i, c in enumerate(cells):
        if not c or not c['locked']:
            continue
        if alike(c['solved'], c['cross'], c['rot'], c['gate']):
            continue
        owed = 0
        for k in range(4):
            if alike(c['solved'], c['cross'], (c['rot'] + k) & 3, c['gate']):
                owed = k
                break
        errors.append(f"{ctx}: the rooted tile at {i % w},{i // w} starts {owed} turn(s) from "
                      "its solution and can never be turned; author it at /0")

    # a fragile conduit must survive long enough to reach its own solution, or the
    # level is unwinnable while looking perfectly fine. Mirrors CheckFragileConduits.
    for i, c in enumerate(cells):
        if not c or not c.get('fragile'):
            continue
        fx, fy = i % w, i // w
        if c['locked']:
            warnings.append(f"{ctx}: the fragile conduit at {fx},{fy} is also rooted, so it never wears")
            continue
        if alike(c['solved'], c['cross'], 1, c['gate']):
            warnings.append(f"{ctx}: the conduit at {fx},{fy} is the same in every orientation, "
                            "so its fragility can never matter")
            continue
        owed = 0
        for k in range(4):
            if alike(c['solved'], c['cross'], (c['rot'] + k) & 3, c['gate']):
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
        if not any(all(alike(cells[i]['solved'], cells[i]['cross'], (cells[i]['rot'] + k) & 3,
                             cells[i]['gate'])
                       for i in members) for k in range(4)):
            errors.append(f"{ctx}: the conduits on taproot '{letter}' can never all be right "
                          "at once, so the glade cannot be finished")

    # Past this the pips stop telling the roots apart. Mirrors Puzzle.MaxReadableRunes.
    MAX_READABLE_RUNES = 6
    real_roots = sum(1 for members in roots.values() if len(members) > 1)
    if real_roots > MAX_READABLE_RUNES:
        warnings.append(f"{ctx}: carries {real_roots} taproots but a mark can only tell "
                        f"{MAX_READABLE_RUNES} of them apart")

    def separates(i, c):
        """Whether taking this briar's thorns off would join anything to anything.

        Mirrors LevelValidator.CheckBriars. Only the thorned ways are asked about, and the
        way has to be open on the *other* side too, or lifting these thorns would still join
        nothing - which is what two briars back to back are. Deliberately not asked: whether
        the tile carries any light, because a briar standing in an island of dark with its
        thorns facing the grove is one of the best tiles this mechanic has.
        """
        mine = comp[i * 2]
        for d in range(4):
            if c['gate'] & BITS[d] or not c['solved'] & BITS[d]:
                continue
            bx, by = i % w + STEP[d][0], i // w + STEP[d][1]
            nb = at(bx, by)
            if nb is None:
                continue
            back = (d + 2) % 4
            if not live(nb) & BITS[back]:
                continue
            if comp[(by * w + bx) * 2 + strand_at(nb, back)] != mine:
                return True
        return False

    lamps = lit = caps = woken = crossings = briars = 0
    for i, c in enumerate(cells):
        if not c:
            continue
        have = energy(i)
        if c['kind'] == 'briar':
            briars += 1
            if not separates(i, c):
                bx, by = i % w, i // w
                warnings.append(f"{ctx}: the thorns on the briar at {bx},{by} close nothing "
                                "off in the authored solution - every way it has leads back "
                                "into one network")
            continue
        if c['kind'] == 'cross':
            crossings += 1
            cx, cy = i % w, i // w
            if comp[i * 2] == comp[i * 2 + 1]:
                warnings.append(f"{ctx}: the two strands of the crossing at {cx},{cy} are joined "
                                "elsewhere in the authored solution, so it crosses nothing")
            elif not have:
                warnings.append(f"{ctx}: neither strand of the crossing at {cx},{cy} carries any "
                                "light in the authored solution")
            continue
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
                if all(alike(cells[i]['solved'], cells[i]['cross'], (cells[i]['rot'] + k) & 3,
                             cells[i]['gate'])
                       for i in members):
                    par += k
                    break
            continue
        if alike(c['solved'], c['cross'], 1, c['gate']):
            continue
        for k in range(4):
            if alike(c['solved'], c['cross'], (c['rot'] + k) & 3, c['gate']):
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
    # "not authored" and only a negative value removing the timer.
    time_factor = level.get('timeFactor', 0) or DEFAULT_TIME_FACTOR
    limit = 0 if time_factor < 0 else -(-int(round(par * time_factor * 1000)) // 1) // 1000

    # Gold is held against par, never against the limit, so a retuned clock cannot move it -
    # LevelTuning.TimeGoldFactor. Clamped to the limit for the same reason it is there.
    gold_seconds = 0 if not limit else min(limit, par * TIME_GOLD_FACTOR)
    star_rate = 0 if not gold_seconds else (-(-par * 135 // 100)) / gold_seconds

    # The tightest clock a published clockScale could cut this to - DifficultyLimits. A glade
    # merely demanding as authored can be unwinnable as retuned, and the retune never passes
    # back through this file.
    if limit:
        at_floor = par / (limit * MIN_CLOCK_SCALE)
        if par / limit <= FINISH_TAP_RATE < at_floor:
            warnings.append(f"{ctx}: the clock allows {limit}s as authored, but a published "
                            f"clockScale of {MIN_CLOCK_SCALE:g} would cut it to "
                            f"{limit * MIN_CLOCK_SCALE:.0f}s and need {at_floor:.1f} taps a "
                            "second just to finish")

    return dict(id=lid, chapter=chapter_id, w=w, h=h, par=par, limit=limit, rate=star_rate,
                gold=-(-par * 135 // 100), silver=-(-par * 200 // 100),
                lamps=lamps, sources=sources, fragile=fragile, caps=caps, bound=bound,
                crossings=crossings, briars=briars)


# Canvas geometry, mirroring ChapterMap.cs. mapX/mapY are fractions of the chapter's
# own map, and a chapter is as tall as the strips it declares - so the same pair of
# fractions is a collision in a one-strip chapter and half a screen apart in a six-strip
# one. Comparing raw fractions would be wrong for every chapter but one size.
MAP_WIDTH, STRIP_HEIGHT = 1080.0, 1200.0
NODE_DIAMETER, NODE_CLEARANCE = 196.0, 24.0
MIN_SEPARATION = NODE_DIAMETER + NODE_CLEARANCE
TEASER_GAP, TEASER_HEADROOM, TEASER_X = 0.22, 500.0, 0.66


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
    # Mirrors ChapterMap.TeaserPosition: the gap above the last glade is a fraction of
    # the map, the room kept clear at the top is a distance in canvas units. Only the
    # across-axis is authorable, and 0 there means "not authored" - ChapterMap.TeaserAcross.
    across = chapter.get("teaserX") or 0.0
    if not (0.0 < across <= 1.0):
        across = TEASER_X
    ceiling = max(0.0, min(1.0, 1.0 - TEASER_HEADROOM / height))
    teaser = (across, min(ceiling, highest + TEASER_GAP))

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


def check_grove(keys, level_ids, chapter_ids, companions, companion_costs=None):
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
    piece_starters = for_sale = earned = bundled = 0
    total = 0
    dwellings = []
    decor_kinds = set()
    bundle_kinds = {}

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

        # A bundle is how many copies one purchase grants (save v20, HomesteadPiece.Bundle).
        #
        # THE DIVISIBILITY CHECK IS AN ERROR AND HAS TO BE. A copy is worth cost/bundle, so a
        # fence costing 95 in tens makes every copy worth 9 and a player who buys the bundle is
        # scored 90 for 95 credits spent. It looks perfectly authored, it cannot be seen on a
        # device, and the server derives the same short figure — so nothing anywhere would
        # disagree and report it, on the one number that reaches a public leaderboard.
        bundle = int(piece.get("bundle", 1) or 1)
        if bundle < 1:
            errors.append(f"grove piece '{pid}' is sold in bundles of {bundle}; "
                          "a purchase grants at least one copy")
            bundle = 1
        elif bundle > 1:
            if cost <= 0:
                errors.append(f"grove piece '{pid}' has no price but is sold in bundles of "
                              f"{bundle}; an unpriced piece is an entitlement and is never "
                              "counted in copies")
            elif kind != "decor":
                errors.append(f"grove piece '{pid}' is a {kind} sold in bundles of {bundle}; "
                              "only decor is bought by the copy")
            elif cost % bundle:
                errors.append(f"grove piece '{pid}' costs {cost} in bundles of {bundle}, which "
                              f"does not divide it - a copy would be worth {cost // bundle} and "
                              f"the bundle {(cost // bundle) * bundle}, so the grove's worth "
                              "would silently fall short of what was paid")
            elif bundle > MAX_COPIES:
                errors.append(f"grove piece '{pid}' is sold in bundles of {bundle}, above the "
                              f"{MAX_COPIES} copies a player may hold")

        if cost > 0:
            for_sale += 1
            total += cost
            if bundle > 1:
                bundled += 1
                bundle_kinds[piece.get("slot") or "ground"] = bundle
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

    # ---------------------------------------------------------------- star ladder
    # What a grove has to be worth to earn each star. Content rather than constants
    # because the catalog grows with every drop, so a rung that reads as "you have built
    # nearly everything" today reads as "you have made a start" in a year - see
    # GroveScoreTable. Mirrored here because the Editor's own check needs a Unity session.
    MAX_STARS = 8

    ladder = ((grove.get("score") or {}).get("stars"))
    if ladder is None:
        ladder = [10000, 20000, 50000, 100000, 200000]
        warnings.append("the grove names no star ladder, so the built-in one stands; author "
                        "one in homestead.json so a drop can retune it")

    if not ladder:
        errors.append("the grove's star ladder has no rungs; the score would show no stars "
                      "at any value")
    if len(ladder) > MAX_STARS:
        errors.append(f"the grove's star ladder has {len(ladder)} rungs, more than the "
                      f"{MAX_STARS} the readout can draw")

    previous = 0
    for at in ladder:
        if not isinstance(at, int) or at <= 0:
            errors.append(f"the grove's star ladder holds {at!r}; no score is below it, so the "
                          "star is awarded to an empty grove")
        elif at <= previous:
            errors.append(f"the grove's star ladder does not rise: {at} comes after {previous}, "
                          "so two stars land at once")
        else:
            previous = at

    # Everything with a price, which is what a complete grove is worth. A rung above it is
    # a star nobody in the world can ever win, and nothing about reading the file says so.
    #
    # The companions are in it because a resident *is* a companion (invariant 16a) and the
    # grove's own shop sells them on a shelf of their own - GroveScore walks the composed
    # catalog, which is the authored pieces with the roster projected in, so leaving them
    # out here would make this disagree with both the game and the build gate.
    roster = sum((companion_costs or {}).values())
    everything = total + land_total + roster

    if everything <= 0:
        warnings.append("nothing in the grove has a price, so its score can never leave zero "
                        "and no star is reachable")
    elif ladder and isinstance(ladder[-1], int) and ladder[-1] > everything:
        warnings.append(f"the grove's last star asks for {ladder[-1]} credits and the whole "
                        f"catalog is worth {everything}; nobody can ever win it")

    return {
        "homes": len(dwellings), "ladder": sum(c for _t, _p, c in dwellings),
        "cols": cols, "rows": rows, "regions": len(regions), "free_regions": starters,
        "owned_tiles": len(owner), "land": land_total,
        "slots": cols * rows, "pieces": len(pieces),
        "residents": len(companions),
        "for_sale": for_sale, "earned": earned, "starters": piece_starters, "total": total,
        "bundled": bundled, "bundle_kinds": bundle_kinds,
        "stars": ladder, "worth": everything, "roster": roster,
    }


# ---------------------------------------------------------------------------- the shop
# What a card may promise, mirrored from StoreLimits so a content push cannot exceed what
# the reader and the server will both accept.
MAX_GRANT = 5_000_000
STORE_SHELVES = {"gems", "coins", "bundles"}
STORE_KINDS = {"consumable", "nonconsumable"}
HINT_DEFAULTS = {"refillCap": 3, "ceiling": 3, "refillSeconds": 8 * 60 * 60}

# Mirrors GroveStock.MaxCopies — the structural ceiling on how many copies of one piece a
# save may hold. A permanent const on both sides rather than anything published, for
# HeartLimits.HardCeiling's reason: lowering a published one would cut a counter the merge
# proof requires to be monotonic.
MAX_COPIES = 9999


def hint_pool(progression):
    """The published hint pool, with the built-in numbers where a field is unwritten.

    Mirrors HintRuleTable.Resolve, including the one repair it makes: a ceiling under the
    cap is a contradiction rather than a smaller ceiling, so it is raised to the cap.
    """
    block = progression.get("hints") or {}

    def read(name):
        value = block.get(name, -1)
        return HINT_DEFAULTS[name] if not isinstance(value, int) or value < 0 else value

    cap = read("refillCap")
    ceiling = max(read("ceiling"), cap)

    offer_id, offer, offer_cap = "hint_refill", 0, 0
    for placement in (progression.get("ads") or {}).get("placements") or []:
        if placement.get("id") == offer_id and placement.get("kind") == "hints":
            offer = placement.get("amount", 0)
            offer_cap = placement.get("dailyCap", 0)

    return {"cap": cap, "ceiling": ceiling, "seconds": read("refillSeconds"),
            "offer_id": offer_id, "offer": offer, "offer_cap": offer_cap}


def check_hints(progression, warnings):
    """The two things the reader cannot know. ContentValidation.ValidateHints, offline."""
    hints = hint_pool(progression)
    errors = []

    to_full = hints["cap"] * hints["seconds"]
    if to_full < 3600:
        warnings.append(f"hints refill a full pool in {to_full // 60} minutes; at that rate a "
                        "hint costs nothing and the pool is decoration")

    # Note what is deliberately *not* warned about: a ceiling equal to the cap. That is the
    # shipped shape, so a warning would fire on every run for ever, and a warning that always
    # fires is one nobody reads. It is printed as a fact in the report instead, and the thing
    # that has to be true because of it - that no offer is made at a full pool - is held by
    # RewardedAds.WouldBenefit and pinned by HintsTests rather than by a reminder here.

    # A payout larger than the whole pool can never land in full, whatever the player holds.
    if hints["offer"] > hints["ceiling"]:
        errors.append(f"'{hints['offer_id']}' pays {hints['offer']} hint(s) into a pool that "
                      f"holds {hints['ceiling']}; the surplus is refused, not banked")

    return errors


GOOD_KINDS = {"hearts", "heart_boost"}


def check_store(progression, keys):
    """The shop: what money buys, and what gems buy.

    Checked offline as well as in the Editor, and for a sharper reason than the grove is.
    The Editor's own check reaches `Application.dataPath` and therefore only runs with a
    Unity session open, and the seeder's check only runs when somebody publishes — so
    without this, the one table in the project where a mistake is charged to a card would
    be the table nobody could check from a terminal.

    Errors rather than warnings throughout, unlike every other block in this file. A
    mistuned chest is a weekend decision; a mispriced product is a payment honoured for a
    figure nobody meant, and the only repair is one refund at a time.
    """
    store = progression.get("store")
    if not store:
        warnings.append("progression.json has no 'store' block, so nothing can be bought")
        return None

    products = store.get("products") or []
    goods = store.get("goods") or []

    if not products:
        errors.append("the store block lists no products; remove the block entirely to close "
                      "the shop deliberately")
        return None

    hearts = progression.get("hearts") or {}
    ceiling = hearts.get("ceiling", 50)
    max_boost = hearts.get("maxBoostHours", 72)

    seen = set()
    shelves = {}

    for entry in products:
        pid = entry.get("id") or ""

        if not re.fullmatch(r"[a-z0-9_]{1,64}", pid):
            errors.append(f"store product id '{pid}' is unusable; ids are lower case letters, "
                          "digits and underscores, because a receipt is looked up by this "
                          "string for the life of the account")
            continue

        if pid in seen:
            errors.append(f"store lists '{pid}' twice")
            continue
        seen.add(pid)

        if entry.get("kind") not in STORE_KINDS:
            errors.append(f"store product '{pid}' has kind '{entry.get('kind')}'; it must be "
                          "consumable or nonconsumable, and the store itself enforces that a "
                          "nonconsumable is sold once per account")

        if entry.get("shelf") not in STORE_SHELVES:
            errors.append(f"store product '{pid}' names unknown shelf '{entry.get('shelf')}'")

        credits = int(entry.get("credits") or 0)
        gems = int(entry.get("gems") or 0)

        if credits <= 0 and gems <= 0:
            errors.append(f"store product '{pid}' grants nothing")
        if credits > MAX_GRANT or gems > MAX_GRANT:
            errors.append(f"store product '{pid}' grants more than {MAX_GRANT}; the server "
                          "refuses rather than clamping, so every purchase of it would fail")

        cents = int(entry.get("referenceUsdCents") or 0)
        if not 49 <= cents <= 100000:
            errors.append(f"store product '{pid}' has referenceUsdCents {cents}, outside "
                          "49..100000. It is never shown to a player, but the value ladder "
                          "is proved against it")

        key = f"store.product.{pid}"
        if key not in keys:
            errors.append(f"store product '{pid}' missing string '{key}'")

        # One-time offers are deliberately better value than the ladder and cannot undercut
        # it, because the store will not sell one twice. See ValidateStoreLadder.
        if entry.get("kind") != "nonconsumable" and cents:
            shelves.setdefault(entry["shelf"], []).append((cents, pid, credits, gems))

    # Credits per gem, from the cheapest rung of each money shelf. Mirrors StoreCatalog.
    per_gem = 1
    gem_base = min(shelves.get("gems", []), default=None)
    coin_base = min(shelves.get("coins", []), default=None)
    if gem_base and coin_base and gem_base[3] and coin_base[0]:
        per_gem = max(1, (coin_base[2] * gem_base[0]) // (gem_base[3] * coin_base[0]))

    for shelf, rungs in shelves.items():
        rungs.sort()
        for (c0, id0, cr0, gm0), (c1, id1, cr1, gm1) in zip(rungs, rungs[1:]):
            if c0 == c1:
                warnings.append(f"store shelf '{shelf}': '{id0}' and '{id1}' are the same price; "
                                "two cards at one price point make a player do arithmetic")
                continue

            before = ((cr0 + gm0 * per_gem) * 10000) // c0
            after = ((cr1 + gm1 * per_gem) * 10000) // c1

            if after < before:
                errors.append(f"store shelf '{shelf}': '{id1}' costs more than '{id0}' and gives "
                              "less per unit of money. A ladder that gets worse as it gets "
                              "bigger is a shop nobody buys the large size in")

    for good in goods:
        gid = good.get("id") or ""

        if not re.fullmatch(r"[a-z0-9_]{1,64}", gid):
            errors.append(f"store good id '{gid}' is unusable")
            continue

        if gid in seen:
            errors.append(f"store lists '{gid}' twice")
        seen.add(gid)

        kind = good.get("kind")
        if kind not in GOOD_KINDS:
            errors.append(f"store good '{gid}' names kind '{kind}'. Only hearts and heart_boost "
                          "can be bought with gems - currency cannot, because only the server "
                          "may grant it")
            continue

        amount = int(good.get("amount") or 0)
        gems = int(good.get("gems") or 0)

        if amount < 1 or gems < 1:
            errors.append(f"store good '{gid}' hands over {amount} for {gems} gems")

        if kind == "hearts" and amount > ceiling:
            errors.append(f"store good '{gid}' hands over {amount} hearts, above the ceiling of "
                          f"{ceiling}; it can never be bought")

        if kind == "heart_boost" and amount > max_boost:
            errors.append(f"store good '{gid}' hands over {amount}h of boost, above the "
                          f"{max_boost}h cap; it can never be bought")

        key = f"store.good.{gid}"
        if key not in keys:
            errors.append(f"store good '{gid}' missing string '{key}'")

    return {
        "products": len(products),
        "goods": len(goods),
        "per_gem": per_gem,
        "shelves": {shelf: len(rungs) for shelf, rungs in shelves.items()},
    }


def daily_income(progression):
    """Credits and gems an engaged player collects in a day, from the published tables.

    The same derivation ContentValidation makes, and it exists here for the same reason it
    exists there: a price only means something beside the income that has to pay it, and
    nobody should have to work that out by hand twice.
    """
    credits = gems = 0.0

    daily = progression.get("daily") or {}
    for chest in daily.get("chests") or []:
        for band in chest.get("guaranteed") or []:
            mid = (band.get("min", 0) + band.get("max", 0)) * 0.5
            if band.get("kind") == "credits":
                credits += mid
            elif band.get("kind") == "gems":
                gems += mid

        options = chest.get("options") or []
        total = sum(max(1, o.get("weight", 1)) for o in options) or 1

        for option in options:
            mid = (option.get("min", 0) + option.get("max", 0)) * 0.5
            share = max(1, option.get("weight", 1)) / total
            if option.get("kind") == "credits":
                credits += mid * share
            elif option.get("kind") == "gems":
                gems += mid * share

    rungs = ((progression.get("streak") or {}).get("rungs")) or []
    if rungs:
        credits += sum(r.get("amount", 0) for r in rungs if r.get("kind") == "credits") / len(rungs)
        gems += sum(r.get("amount", 0) for r in rungs if r.get("kind") == "gems") / len(rungs)

    return int(credits), int(gems)


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
          f"{'duskcaps':<10}{'roots':<7}{'crossings':<11}briars")
    for i, s in enumerate(summaries, 1):
        clock = "-" if not s['limit'] else f"{s['limit']}s"
        rate = "-" if not s['rate'] else f"{s['rate']:.2f}"
        print(f"{i:<3}{s['id']:<22}{s['chapter']:<16}{str(s['w'])+'x'+str(s['h']):<7}"
              f"{s['par']:<5}{s['gold']:<6}{s['silver']:<7}{clock:<7}{rate:<10}"
              f"{s['sources']:<7}{s['lamps']:<9}{s['fragile']:<8}{s['caps']:<10}"
              f"{s['bound']:<7}{s['crossings']:<11}{s['briars']}")

    live_companions = [c for c in (manifest.get("companions") or [])
                       if c.get("id") and not c.get("disabled")]

    grove = check_grove(keys,
                        {lv for e in manifest["chapters"] for lv in (e.get("levels") or [])},
                        {e["id"] for e in manifest["chapters"] if e.get("id")},
                        {c["id"] for c in live_companions},
                        {c["id"]: int(c.get("unlockCost") or 0) for c in live_companions})

    others = [x for x in summaries if x.get("mode")]
    if others:
        print()
        print("other modes:")
        for c in others:
            print(f"  {c['id']:<24} {c['mode']:<10} {c['w']}x{c['h']}")

    if grove:
        print(f"\ngrove: {grove['cols']}x{grove['rows']} floor, {grove['slots']} tile(s), "
              f"{grove['pieces']} piece(s) - {grove['residents']} resident(s) from the roster, "
              f"{grove['starters']} free, {grove['earned']} earned, {grove['for_sale']} for sale "
              f"({grove['total']} credits in all)")
        print(f"       land: {grove['regions']} region(s), {grove['free_regions']} free, "
              f"{grove['owned_tiles']} tile(s) sellable - {grove['land']} credits to own it all")
        print(f"       home ladder: {grove['homes']} rung(s), {grove['ladder']} credits to the top")
        if grove["bundled"]:
            shelves = ", ".join(f"{k} x{n}" for k, n in sorted(grove["bundle_kinds"].items()))
            print(f"       bundles: {grove['bundled']} of {grove['for_sale']} priced piece(s) "
                  f"are sold by the bundle ({shelves}) - a copy is worth cost/bundle, so a "
                  "bundle is worth what was paid for it")
        else:
            print("       bundles: every priced piece sells one copy at a time")
        if grove["worth"] > 0:
            rungs = ", ".join(f"{n + 1}@{at} ({round(at * 100 / grove['worth'])}%)"
                              for n, at in enumerate(grove["stars"]))
            print(f"       score: {grove['worth']} credits for everything "
                  f"({grove['total']} decor and homes, {grove['land']} land, "
                  f"{grove['roster']} residents) - stars at {rungs}")

    progression_path = os.path.join(ROOT, "progression.json")
    progression = json.load(open(progression_path, encoding="utf-8")) \
        if os.path.exists(progression_path) else {}

    shop = check_store(progression, keys)
    per_day_credits, per_day_gems = daily_income(progression)

    errors.extend(check_hints(progression, warnings))

    if shop:
        shelves = ", ".join(f"{n} {shelf}" for shelf, n in sorted(shop["shelves"].items()))
        print(f"\nshop: {shop['products']} product(s) ({shelves}), {shop['goods']} good(s) "
              f"- one gem is worth about {shop['per_gem']} credits across the two ladders")
        print(f"      free play collects about {per_day_credits} credit(s) and "
              f"{per_day_gems} gem(s) a day")

        if grove and per_day_credits:
            # `worth` rather than a sum written out here: the home ladder is already inside
            # the pieces total, so adding it again double-counted 49,500 credits, and the
            # companion roster - the largest sink in the game - was missing altogether.
            sinks = grove["worth"]
            print(f"      every credit sink in the game is {sinks} credits, "
                  f"about {sinks // per_day_credits} day(s) of play")

    hints = hint_pool(progression)
    print()
    print(f"hints: refill to {hints['cap']} every {hints['seconds'] / 3600:g}h, "
          f"hold up to {hints['ceiling']} "
          f"({hints['cap'] * hints['seconds'] / 3600:g}h from empty to full)"
          + (f" - '{hints['offer_id']}' pays {hints['offer']} per video, "
             f"{hints['offer_cap']}/day" if hints["offer"] else ""))
    if hints["ceiling"] <= hints["cap"]:
        print("       the ceiling is the cap, so a granted hint at a full pool is refused "
              "rather than banked - nothing may offer one there")

    prompts = progression.get("prompts") or {}
    chapter_budget = prompts.get("chapterBudget", 2)
    purchase_budget = prompts.get("purchaseBudget", 3)
    quiet_hours = prompts.get("quietHours", 48)
    print()
    print(f"account prompt: {chapter_budget} ask(s) after a chapter, "
          f"{purchase_budget} after a purchase, "
          f"{quiet_hours}h apart whatever raised them")
    if not chapter_budget and not purchase_budget:
        print("       both budgets are zero, so the panel never opens by itself - the shop's "
              "standing notice is the whole warning")
    elif not purchase_budget:
        print("       purchaseBudget is zero, so a guest who pays is never asked to protect it")

    print()
    for w in warnings:
        print("WARN  " + w)
    for e in errors:
        print("ERROR " + e)
    print(f"\n{len(summaries)} level(s): {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


sys.exit(main())
