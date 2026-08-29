"""What a glade actually asks of the player, counted rather than argued about.

A board can carry brittle stone, a taproot and three crossings and still be a rotation
exercise with a theme painted on it. That is not a matter of taste and it does not need
playtesting to see: every tile on this board has one *authored* orientation, and the
question is how much of that orientation the player has to work out from anything other
than pipe-fitting.

Two tiles are joined when both point at each other, and an arm pointing at empty ground
is always wrong. So most of a board is decided by that alone - which neighbours exist -
with no reference to colour, to the dark, or to what any other mechanic is for. This
enumerates every arrangement the *arms* allow and then asks which of them win:

    solutions   arrangements where every arm mates and none dangles
    wins        those of them that also light every critter
    decided     tiles whose orientation varies across `solutions` but is the same in
                every one of `wins` - the tiles the player can only place by reasoning
                about colour or the dark
    slack       tiles that vary even across `wins` - places the player may be wrong

`decided` is the number worth reading. **When `solutions` is 1, every mechanic on the
board except the arms is decoration**: the player fits pipes, the lights come on, and the
crossing they never thought about was never turnable. That is measurable before anybody
plays it, which is the whole point of the file.

The same reading, per mechanic:

    colour    arrangements the critters alone reject
    root      how much the binding removes - `solutions` with the roots dissolved
    brittle   turns owed against turns survived, and whether the tile is `decided`

Mirrors Puzzle.Alike / Puzzle.Evaluate through `author.Board`, so it judges the shipped
JSON rather than a model of it. It is an aid, not a gate: nothing here fails a build.

    python Tools/verify/difficulty.py                       # every chapter
    python Tools/verify/difficulty.py c02_millvale --detail # one, with per-tile reasons
"""
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

from author import Board, BITS, STEP, OPP, LETTER, alike, rotl   # noqa: E402

CHAPTERS = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters")
CAP = 400000          # enough that no shipped board reaches it; a guard, not a sample


# --------------------------------------------------------------------- loading
def read_arms(tok, p):
    mask = 0
    while p < len(tok) and tok[p] in "NESW":
        mask |= {"N": 1, "E": 2, "S": 4, "W": 8}[tok[p]]
        p += 1
    return mask, p


def board_of(level):
    """The shipped rows, back into an author.Board. Mirrors content.parse_token."""
    w, h = level["width"], level["height"]
    b = Board(w, h)
    colours = {1: "R", 2: "G", 4: "B", 3: "Y", 5: "M", 6: "C", 7: "W", 0: "A"}
    for y, row in enumerate(level["rows"]):
        for x, tok in enumerate(row.split()):
            if tok == ".":
                continue
            kind = {"-": "pipe", "=": "cross", "%": "briar", "*": "source",
                    "@": "lamp"}[tok[0]]
            mask, p = read_arms(tok, 1)
            cross = gate = 0
            if p < len(tok) and tok[p] == "+":
                second, p = read_arms(tok, p + 1)
                # the named pair, which is one flow of a crossing and the open way of a briar
                if kind == "briar":
                    gate, mask = mask, mask | second
                else:
                    cross, mask = mask, mask | second
            colour, rot, locked, fragile, link = 0, 0, False, 0, None
            if p < len(tok) and tok[p] == "#":
                colour = {"R": 1, "G": 2, "B": 4, "Y": 3, "M": 5, "C": 6, "W": 7, "A": 0}[tok[p + 1]]
                p += 2
            if p < len(tok) and tok[p] == "/":
                rot = int(tok[p + 1]); p += 2
            if p < len(tok) and tok[p] == "!":
                locked = True; p += 1
            if p < len(tok) and tok[p] == "~":
                fragile = int(tok[p + 1]); p += 2
            if p < len(tok) and tok[p] == "&":
                link = tok[p + 1]; p += 2
            b.cells[(x, y)] = dict(kind=kind, colour=colour, rot=rot, locked=locked,
                                   fragile=fragile, link=link, cross=cross, gate=gate)
            # edges are derived from the masks, so a board loaded this way is the same
            # object `author` builds from runs and joins
            for d in range(4):
                if mask & BITS[d]:
                    q = (x + STEP[d][0], y + STEP[d][1])
                    b.edges.add(frozenset(((x, y), q)))
    # an arm pointing off the board or at empty ground is the level's problem, not ours;
    # drop the half-edge so `mask()` still answers what the token said
    b.edges = {e for e in b.edges if all(p in b.cells for p in e)}
    return b


# ------------------------------------------------------------------ the search
class Reading:
    """Every arm-valid arrangement of one board, and what the mechanics do to them."""

    def __init__(self, b, dissolve_roots=False):
        self.b = b
        self.pts = sorted(b.cells)
        self.index = {p: i for i, p in enumerate(self.pts)}
        self.mask = {p: b.mask(p) for p in self.pts}
        self.period = {p: b.period(p) for p in self.pts}

        # A locked tile can never be turned, so its authored offset is its only state.
        # Everything else may sit anywhere inside its own period - two rotations that
        # read the same tile are one state, which is exactly what Puzzle.Alike says.
        self.domain = {}
        for p in self.pts:
            c = b.cells[p]
            self.domain[p] = [c["rot"] % self.period[p]] if c["locked"] else list(range(self.period[p]))

        self.roots = {}
        if not dissolve_roots:
            for p in self.pts:
                rune = b.cells[p]["link"]
                if rune:
                    self.roots.setdefault(rune, []).append(p)

        self.solutions = []
        self.capped = False
        self._build_units()
        self._search()

    def _root_states(self, members):
        """One k per arrangement a root can actually take, not one per quarter turn."""
        seen, out = set(), []
        for k in range(4):
            key = tuple(k % self.period[p] for p in members)
            if key in seen:
                continue
            seen.add(key)
            out.append(k)
        return out

    # ---- constraints
    def _fits(self, p, tp, assign):
        """Whether this tile at this offset agrees with every neighbour already placed."""
        mp = rotl(self.mask[p], tp)
        for d in range(4):
            q = (p[0] + STEP[d][0], p[1] + STEP[d][1])
            arm = bool(mp & BITS[d])
            if q not in self.b.cells:
                if arm:
                    return False
                continue
            if q not in assign:
                continue
            back = bool(rotl(self.mask[q], assign[q]) & BITS[OPP[d]])
            if arm != back:
                return False
        return True

    def _build_units(self):
        """One variable per independent decision: a tile, or a whole taproot."""
        bound = {p for ms in self.roots.values() for p in ms}
        units = [("root", rune, members) for rune, members in self.roots.items()]
        units += [("tile", p, [p]) for p in self.pts if p not in bound]

        def pressure(unit):
            free = sum(1 for p in unit[2] for d in range(4)
                       if (p[0] + STEP[d][0], p[1] + STEP[d][1]) in self.b.cells)
            return (len(unit[2]) * 4 - free, -len(unit[2]))

        units.sort(key=pressure)
        self.units = units
        self.owner = {p: key for _, key, members in units for p in members}

    def _search(self):
        units = self.units
        assign = {}
        out = self.solutions

        def place(i):
            if len(out) >= CAP:
                self.capped = True
                return
            if i == len(units):
                out.append(dict(assign))
                return
            kind, key, members = units[i]
            if kind == "tile":
                p = members[0]
                for t in self.domain[p]:
                    if self._fits(p, t, assign):
                        assign[p] = t
                        place(i + 1)
                        del assign[p]
                return
            # A root turns every member by the same k, so it is one decision however many
            # conduits it holds - the same reason par charges it once. Each member's own
            # state is that k modulo its period, because a straight conduit reads the same
            # twice a turn round and simply follows whatever the elbows on its root demand.
            # A root of nothing but straights therefore has two states, not four: counting
            # k rather than the arrangement it produces would report a board as twice as
            # open as it is. Two members can be neighbours, so all are placed before any
            # is judged.
            for k in self._root_states(members):
                for p in members:
                    assign[p] = k % self.period[p]
                if all(self._fits(p, assign[p], assign) for p in members):
                    place(i + 1)
                for p in members:
                    del assign[p]

        place(0)

    # ---- readings
    def glanced(self):
        """Tiles a player cannot place by looking at that tile and the ground around it.

        The honest measure of what a board asks, and a much weaker test than `settled`
        below: a person does not run arc consistency, they look. An orientation survives
        a glance when every arm of it lands on ground that is *there* and could point
        back, and every gap of it faces ground that could decline to. Whether some chain
        of reasoning four tiles away rules it out is exactly what the player has to work
        out, and it is why a tile can be forced and still be work.

        This is the number that separates the two kinds of board. On open ground an arm
        has almost nowhere to go, so nearly every tile reads at a glance and the glade is
        a connect-the-dots; on full ground every arm has four candidates and every tile is
        a question. Density is the lever, and this is what it moves.
        """
        out = set()
        for p in self.pts:
            if len(self.domain[p]) < 2:
                continue
            live = 0
            for t in self.domain[p]:
                mp = rotl(self.mask[p], t)
                ok = True
                for d in range(4):
                    q = (p[0] + STEP[d][0], p[1] + STEP[d][1])
                    arm = bool(mp & BITS[d])
                    if q not in self.b.cells:
                        if arm:
                            ok = False
                        continue
                    # what the neighbour could offer, over its own orientations
                    could = [bool(rotl(self.mask[q], u) & BITS[OPP[d]]) for u in self.domain[q]]
                    if arm not in could:
                        ok = False
                if ok:
                    live += 1
            if live > 1:
                out.add(p)
        return out

    def settled(self):
        """Which tiles a player can place by looking only at the tile and its neighbours.

        Arc consistency over the arm rule alone: an orientation survives only if every
        neighbour has some orientation that agrees with it. What is left over needs
        lookahead or another mechanic, and a board where nothing is left over can be
        solved greedily, tile by tile, with no backtracking and nothing to hold in mind.
        """
        vals = {}
        for kind, key, members in self.units:
            if kind == "tile":
                p = members[0]
                vals[key] = [{p: t} for t in self.domain[p]]
            else:
                vals[key] = [{p: k % self.period[p] for p in members}
                             for k in self._root_states(members)]

        pairs = []
        for kind, key, members in self.units:
            for p in members:
                for d in range(4):
                    q = (p[0] + STEP[d][0], p[1] + STEP[d][1])
                    if q in self.b.cells and self.owner[q] != key:
                        pairs.append((key, self.owner[q]))

        def agrees(va, vb):
            for p, tp in va.items():
                mp = rotl(self.mask[p], tp)
                for d in range(4):
                    q = (p[0] + STEP[d][0], p[1] + STEP[d][1])
                    if q not in vb:
                        continue
                    if bool(mp & BITS[d]) != bool(rotl(self.mask[q], vb[q]) & BITS[OPP[d]]):
                        return False
            return True

        # an arm pointing at ground that is not there is wrong wherever it is
        for key, options in vals.items():
            keep = []
            for v in options:
                ok = True
                for p, tp in v.items():
                    mp = rotl(self.mask[p], tp)
                    for d in range(4):
                        q = (p[0] + STEP[d][0], p[1] + STEP[d][1])
                        if q not in self.b.cells and mp & BITS[d]:
                            ok = False
                if ok:
                    keep.append(v)
            vals[key] = keep

        changed = True
        while changed:
            changed = False
            for a, bkey in pairs:
                keep = [va for va in vals[a] if any(agrees(va, vb) for vb in vals[bkey])]
                if len(keep) != len(vals[a]):
                    vals[a] = keep
                    changed = True

        open_cells = set()
        for kind, key, members in self.units:
            if len(vals[key]) > 1:
                open_cells.update(members)
        return open_cells

    def evaluate(self, rots):
        comp, colour = self.b.solve_state(rots)
        for p, c in self.b.cells.items():
            if c["kind"] != "lamp":
                continue
            have = self.b.energy(p, comp, colour)
            want = c["colour"]
            if not ((have != 0) if want == 0 else (have == want)):
                return False
        return True

    def report(self):
        wins, only_colour = [], 0
        for rots in self.solutions:
            if self.evaluate(rots):
                wins.append(rots)
            else:
                only_colour += 1

        def varying(sols):
            if not sols:
                return set()
            return {p for p in self.pts if len({s[p] for s in sols}) > 1}

        varies = varying(self.solutions)
        slack = varying(wins)
        # How much of the glade the player is handed already finished. Every other reading
        # here is about the board's *solution*; this one is about the board as it is
        # *dealt*, and it is the only fault in this file that a player meets in the first
        # second. It was reported from play as glades that "start half done" - thirty-four
        # of the forty opened with a critter already awake or better than a third of their
        # conduits already right, and nothing anywhere could say so, because a
        # part-solved board is solvable, correctly par'd and passes every gate there is.
        lit, done, free = self.b.astray()
        return dict(solutions=len(self.solutions), capped=self.capped, wins=len(wins),
                    colour_only=only_colour,
                    decided=sorted(varies - slack), slack=sorted(slack),
                    open=sorted(self.settled()), glance=sorted(self.glanced()),
                    tiles=len(self.pts), dealt=(lit, done, free))


# ------------------------------------------------------------------- reporting
def analyse(level):
    b = board_of(level)
    r = Reading(b).report()
    bare = Reading(b, dissolve_roots=True).report() if any(
        c["link"] for c in b.cells.values()) else None

    brittle = []
    for p, c in sorted(b.cells.items()):
        if c["fragile"]:
            owed = b.group_turns(p)
            brittle.append((p, owed, c["fragile"], p in r["decided"]))

    briars = [p for p, c in sorted(b.cells.items()) if c["kind"] == "briar"]
    roots = {}
    for p, c in sorted(b.cells.items()):
        if c["link"]:
            roots.setdefault(c["link"], []).append(p)

    return dict(board=b, par=b.par(), hazards=len(b.hazards()), reading=r, bare=bare,
                brittle=brittle, briars=briars, roots=roots)


def line(lid, a):
    r = a["reading"]
    sol = f"{r['solutions']}{'+' if r['capped'] else ''}"
    marks = []
    if a["brittle"]:
        live = sum(1 for _, owed, f, dec in a["brittle"] if dec or owed >= f)
        marks.append(f"brittle {live}/{len(a['brittle'])}")
    if a["briars"]:
        # every briar is a free decision by construction, so what is worth printing is how
        # many of them a mechanic actually settles rather than how many there are
        dec = sum(1 for p in a["briars"] if p in r["decided"])
        marks.append(f"briar {dec}/{len(a['briars'])}")
    if a["roots"]:
        gain = a["bare"]["solutions"] - r["solutions"] if a["bare"] else 0
        marks.append(f"root -{gain}")
    lit, done, free = r['dealt']
    return (f"  {lid:<28} par {a['par']:>3}  arms {sol:>5}  wins {r['wins']:>3}  "
            f"glance {len(r['glance']):>3}/{r['tiles']:<3} open {len(r['open']):>2}  "
            f"decided {len(r['decided']):>2}  colour {r['colour_only']:>4}  "
            f"dealt {done:>2}/{free:<3}{'' if not lit else f' +{lit} lit'} "
            + "  ".join(marks))


def detail(lid, a):
    r = a["reading"]
    print(f"\n--- {lid}   par {a['par']}  {a['board'].w}x{a['board'].h}")
    print(f"    arm-valid arrangements : {r['solutions']}{'+' if r['capped'] else ''}")
    print(f"    of them, winning       : {r['wins']}")
    print(f"    rejected by colour     : {r['colour_only']}")
    print(f"    decided by a mechanic  : {len(r['decided'])} {r['decided'][:14]}")
    print(f"    free even when won     : {len(r['slack'])} {r['slack'][:14]}")
    if a["bare"]:
        print(f"    roots remove           : {a['bare']['solutions'] - r['solutions']} "
              f"arrangements ({a['bare']['solutions']} without them)")
    for rune, members in a["roots"].items():
        dec = sum(1 for p in members if p in r["decided"])
        print(f"      root {rune}: {len(members)} members, {dec} of them decided by a mechanic")
    for p, owed, f, dec in a["brittle"]:
        note = "must be deduced" if dec else "forced by its arms alone"
        print(f"      brittle {p}: owes {owed}, survives {f} "
              f"(slack {f - owed}) - {note}")
    for p in a["briars"]:
        note = "settled by a mechanic" if p in r["decided"] else (
            "free even in a winning board" if p in r["slack"] else "settled by its own arms")
        print(f"      briar {p}: {note}")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    want_detail = "--detail" in sys.argv
    files = sorted(f for f in os.listdir(CHAPTERS) if f.endswith(".json"))
    if args:
        files = [f for f in files if any(a in f for a in args)]

    for f in files:
        doc = json.load(io.open(os.path.join(CHAPTERS, f), encoding="utf-8"))

        # Everything here enumerates rotations of a grid of conduits, so it has an answer
        # for a glade and none at all for a hollow, a fall or a weave - those are searched
        # or generated and have their own instruments (`HollowSolver`, `Survey Lightweave`).
        # Said out loud rather than skipped silently: a chapter quietly missing from a report
        # somebody is using to judge difficulty is worse than one that says why it is absent.
        # It used to read `level["width"]` unguarded and stop the whole run on a KeyError at
        # the first non-glade chapter, which is every run since the Hollow shipped.
        glades = [lv for lv in doc["levels"] if lv.get("rows")]
        other = len(doc["levels"]) - len(glades)

        print(f"\n{doc['id']}")
        if not glades:
            print(f"  {other} level(s), none of them glades - nothing here to enumerate")
            continue
        if other:
            print(f"  ({other} level(s) skipped: not a grid of conduits)")

        for level in glades:
            a = analyse(level)
            print(line(level["id"], a))
            if want_detail:
                detail(level["id"], a)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
