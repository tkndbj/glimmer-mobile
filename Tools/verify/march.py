# -*- coding: utf-8 -*-
"""Hollowmarch's rules, mirrored offline.

`MarchBoard.cs` in C#; this file in Python. The two copies exist for the reason every other
mirror in this project does: the content gate has to run with no Unity anywhere, and a rule
that only exists inside the Editor is a rule nobody checks on the way past.

**Where the two are allowed to differ: nowhere.** Every wave order, threshold and refusal
below is contract. The disagreements that matter are silent - a chain that stops one wave
early makes a board *harder* and par comes out one higher, which looks exactly like a level
somebody authored (Budburst's lesson, in CLAUDE.md). So `ProtoLadderTests` pins the shipped
boards inline on the C# side and `content.py` pins them here.

Read `MarchBoard.cs` first; the names match deliberately.

The mode in one paragraph: the raiders are marching a line of pods along a haul-road to a
portal, and some of those pods are carrying caged critters. You stand at the tail with a
launcher. Fire a core and it wedges into the line beside its own colour; three alike go off;
the line slides shut behind them, and if the closure makes three more it goes again. Every
shot the line marches one step nearer the portal, so the allowance is not a number in a
corner - it is how far the raiders still have to walk.
"""

# ---------------------------------------------------------------------------- vocabulary

GROUND = "."
RUBBLE = "#"
RAIL = "+"
PORTAL = "Z"
LAUNCHER = "A"

#: The colours a pod may wear. Four, and the fourth is what makes a board hard to read by
#: accident: three colours on a line of a dozen makes a run of three almost unavoidable.
COLOURS = "RGBY"

#: The same four, carrying a caged critter. Lower case throughout, so a board reads as a
#: line of colours with the cargo picked out rather than as two alphabets.
CAGED = "rgby"

HAULER = "H"
WARDEN = "W"

#: A warden with one plate gone. **Not authorable** - it is a state a board reaches and never
#: a state a board is written in, exactly as a blend is in Budburst. It still has to be in the
#: key, or two boards a plate apart would merge in the search.
WARDEN_HIT = "V"

LETTERS = GROUND + RUBBLE + RAIL + PORTAL + LAUNCHER + COLOURS + CAGED + HAULER + WARDEN

#: Everything that stands *on* the road, so everything the line can be made of.
PODS = COLOURS + CAGED + HAULER + WARDEN

#: Every road cell: the rail itself, the two ends, and anything standing on it.
ROAD = RAIL + PORTAL + LAUNCHER + PODS

#: Pods destroyed in one shot that forge a Spark into the next core.
#:
#: **Counted over the whole shot rather than over one run**, which is the difference between a
#: reward for a lucky run of five and a reward for a chain the player engineered. It is the
#: only thing in this mode the player *makes* (invariant 20m), so it is deliberately the thing
#: the mode's best move is aimed at.
FORGE_AT = 5

#: Pods alike that go off. The threshold the chain has to clear again on every wave, which is
#: what stops a cascade being a solvent (invariant 20j's third test).
BURST_AT = 3

STEPS = ((0, -1), (1, 0), (0, 1), (-1, 0))


def is_colour(c):
    """Whether this pod wears a colour a core can match. Never a substring test - see CLAUDE.md."""
    return len(c) == 1 and (c in COLOURS or c in CAGED)


def hue(c):
    """The colour a pod wears, upper case, or '\\0' for something that wears none."""
    if len(c) != 1:
        return "\0"
    if c in COLOURS:
        return c
    if c in CAGED:
        return c.upper()
    return "\0"


def is_caged(c):
    return len(c) == 1 and c in CAGED


def is_raider(c):
    return c in (HAULER, WARDEN, WARDEN_HIT)


def is_goal(c):
    """What the board is counting down: a caged critter, and every plated raider hauling it."""
    return is_caged(c) or is_raider(c)


def is_road(c):
    return len(c) == 1 and c in ROAD


def is_pod(c):
    return len(c) == 1 and c in (PODS + WARDEN_HIT)


def struck(c):
    """A raider one plate down, or None when that plate was its last."""
    if c == WARDEN:
        return WARDEN_HIT
    return None


# ---------------------------------------------------------------------------- the road

class Layout(object):
    """`MarchLayout`. The road walked once, and the line as it was authored.

    **The road is derived rather than authored twice.** A level draws a winding rail through
    the grid and the order is read off it, which is the only shape where what is drawn and
    what is played cannot come apart. It costs one rule: every road cell has exactly two road
    neighbours except the portal and the launcher, which have one - so the walk is unambiguous
    and a road that forks is refused by cell rather than read some arbitrary way.
    """

    def __init__(self, grid, spare, cores):
        self.grid = grid
        self.spare = spare
        self.cores = "".join(c for c in (cores or "") if c in COLOURS) or COLOURS[:3]

        self.path = self._walk()
        # The slots a pod may stand on: the road less its two ends. The portal is a mouth and
        # the launcher is where you are standing, and neither is a place anything waits.
        self.slots = self.path[1:-1]

        line, head = [], -1
        for at, cell in enumerate(self.slots):
            c = grid.cells[cell]
            if not is_pod(c):
                continue
            if head < 0:
                head = at
            line.append(c)

        self.line = line
        self.head = 0 if head < 0 else head

    @property
    def w(self):
        return self.grid.w

    @property
    def h(self):
        return self.grid.h

    @property
    def track(self):
        return len(self.slots)

    def _walk(self):
        """The road from the portal to the launcher, or ValueError naming the cell at fault."""
        grid = self.grid

        mouths = [i for i, c in enumerate(grid.cells) if c == PORTAL]
        if len(mouths) != 1:
            raise ValueError("a haul-road has exactly one portal '%s'; this one draws %d"
                             % (PORTAL, len(mouths)))

        pads = [i for i, c in enumerate(grid.cells) if c == LAUNCHER]
        if len(pads) != 1:
            raise ValueError("a haul-road has exactly one launcher '%s'; this one draws %d"
                             % (LAUNCHER, len(pads)))

        def neighbours(i):
            x, y = grid.x_of(i), grid.y_of(i)
            out = []
            for dx, dy in STEPS:
                nx, ny = x + dx, y + dy
                if grid.inside(nx, ny) and is_road(grid.xy(nx, ny)):
                    out.append(grid.index(nx, ny))
            return out

        for i, c in enumerate(grid.cells):
            if not is_road(c):
                continue
            want = 1 if c in (PORTAL, LAUNCHER) else 2
            got = len(neighbours(i))
            if got != want:
                raise ValueError(
                    "the road cell at %d,%d has %d road neighbours and needs %d, so the walk "
                    "from the portal is ambiguous - a haul-road is one unbranched line"
                    % (grid.x_of(i), grid.y_of(i), got, want))

        path = [mouths[0]]
        seen = {mouths[0]}
        while True:
            step = [n for n in neighbours(path[-1]) if n not in seen]
            if not step:
                break
            path.append(step[0])
            seen.add(step[0])

        if path[-1] != pads[0]:
            raise ValueError("the road from the portal does not reach the launcher, so the "
                             "board draws two roads rather than one")

        if len(path) < 5:
            raise ValueError("a haul-road of %d cells is too short to march anything along"
                             % len(path))

        return path


# ---------------------------------------------------------------------------- one shot

class Shot(object):
    """`MarchShot`. What one core did, in the order it happened, for the view to replay."""

    def __init__(self):
        self.deeds = []
        self.waves = 0
        self.took = 0          # pods destroyed
        self.freed = 0         # critters out of cages
        self.scrapped = 0      # raiders destroyed
        self.staggered = 0     # raiders that lost a plate and stood
        self.escaped = 0       # plain pods through the gate this shot
        self.forged = False
        self.lanced = False

    @property
    def goals(self):
        return self.freed + self.scrapped


class Board(object):
    """`MarchBoard`. The line, where its front has got to, and what is still in the magazine."""

    def __init__(self, layout, line=None, head=None, index=0, spark=False,
                 freed=0, scrapped=0):
        self.layout = layout
        self.line = list(layout.line) if line is None else line
        self.head = layout.head if head is None else head
        self.index = index
        self.spark = spark
        self.freed = freed
        self.scrapped = scrapped

    def fork(self):
        return Board(self.layout, list(self.line), self.head, self.index, self.spark,
                     self.freed, self.scrapped)

    # ------------------------------------------------------------------ reading
    @property
    def track(self):
        return self.layout.track

    @property
    def tail(self):
        """The first slot past the back of the line."""
        return self.head + len(self.line)

    @property
    def room(self):
        """Whether a core that does not go off has anywhere to sit."""
        return self.tail < self.track

    @property
    def runway(self):
        """Shots before the front of the line goes through the portal."""
        return self.head

    @property
    def menace(self):
        """Shots before the line jams at the gate, if nothing is done about the front of it.

        **The reading an author tunes the pressure with.** Under it, nothing is lost at all
        and the march is only a picture; over it, the front of the line starts going through
        the gate and every pod that does is match material the player no longer has. So a
        teaching board wants this comfortably above its allowance and a hard one wants it
        well below, and neither is a fail state - which is the point (see `stranded`).
        """
        for at, c in enumerate(self.line):
            if is_goal(c):
                return self.head + at
        return self.head + len(self.line)

    @property
    def core(self):
        """The colour in hand. The magazine repeats, so one lap is enough to author."""
        return self.layout.cores[self.index % len(self.layout.cores)]

    @property
    def goals(self):
        return sum(1 for c in self.layout.line if is_goal(c))

    @property
    def goals_left(self):
        return self.goals - self.freed - self.scrapped

    @property
    def finished(self):
        return self.goals_left == 0

    @property
    def jammed(self):
        """Whether the front of the line has reached the gate and cannot go further."""
        return self.head == 0 and len(self.line) > 0 and is_goal(self.line[0])

    @property
    def stranded(self):
        """`MarchBoard.Stranded`. **Always false here, and the jam is why.**

        Every other prototype mode can reach a board it can be proved never to finish from,
        so this predicate decides whether it would be honest to sell a continue (invariant
        28f). This mode cannot, by construction: the raiders will not abandon their cargo, so
        a caged critter that reaches the gate *jams the line* rather than going through it,
        and every goal a board opens with is still standing on it however long the run goes
        on. More cores therefore always help, which is exactly what a deficit of nought says.

        It is written out rather than left off, because the reason is a rule and not an
        oversight - and because the alternative was tried: letting a cage through the portal
        makes a board that can be neither won nor lost, which is the state invariant 20g
        names and the one thing a mode may never ship.
        """
        return False

    # ------------------------------------------------------------------ the moves
    def runs(self):
        """Every maximal block of one colour in the line, as (at, length, hue)."""
        out = []
        at = 0
        while at < len(self.line):
            h = hue(self.line[at])
            if h == "\0":
                at += 1
                continue
            end = at
            while end + 1 < len(self.line) and hue(self.line[end + 1]) == h:
                end += 1
            out.append((at, end - at + 1, h))
            at = end + 1
        return out

    def moves(self):
        """Every legal shot, as ('join'|'lance'|'dump', at).

        **A core wedges in beside its own colour, or it is dumped at the tail.** Restricting
        it to the runs it matches is what keeps the branching small enough for the shared
        breadth-first search to find par at the depths this mode is authored to - and it is
        also the right rule for a thumb, because it is the set of moves a player can see.
        Every position inside one run reaches the same board, so a run is one move and not
        several.
        """
        out = []

        if self.spark:
            for at, length, _ in self.runs():
                out.append(("lance", at))
        else:
            h = self.core
            for at, length, run_hue in self.runs():
                if run_hue != h:
                    continue
                # A core that will not go off has to have somewhere to sit; one that will is
                # taking two pods off the line and always fits.
                if length + 1 < BURST_AT and not self.room:
                    continue
                out.append(("join", at))

        if self.room:
            out.append(("dump", len(self.line)))

        return out

    @property
    def any_move(self):
        return len(self.moves()) > 0

    # ------------------------------------------------------------------ firing
    def fire(self, move):
        """Plays one shot, or None when it is not legal. Mutates; fork first to look ahead."""
        kind, at = move
        log = Shot()

        if kind == "lance":
            if not self.spark or not (0 <= at < len(self.line)):
                return None
            self.spark = False
            self.index += 1
            log.lanced = True
            self._lance(at, log)
        elif kind == "join":
            h = self.core
            if not (0 <= at < len(self.line)) or hue(self.line[at]) != h:
                return None
            length = 1
            while at + length < len(self.line) and hue(self.line[at + length]) == h:
                length += 1
            if length + 1 < BURST_AT and not self.room:
                return None
            self.index += 1
            self.line.insert(at, h)
            log.deeds.append(("wedge", at, h, 0))
            self._settle(log)
        elif kind == "dump":
            if not self.room:
                return None
            h = self.core
            self.index += 1
            self.line.append(h)
            log.deeds.append(("wedge", len(self.line) - 1, h, 0))
            self._settle(log)
        else:
            return None

        if log.took >= FORGE_AT and not self.spark:
            self.spark = True
            log.forged = True

        self._march(log)
        return log

    def _lance(self, at, log):
        """The Spark: the run it lands in and the group either side of it, whatever they are.

        **A lance cuts plating**, which is the one thing a colour match cannot do to a warden
        in a single shot - so the forged core is not a bigger version of an ordinary one, it
        does a different job (invariant 26g's test, asked of the thing the player makes).
        """
        line = self.line

        h = hue(line[at])
        lo = at
        while lo > 0 and hue(line[lo - 1]) == h:
            lo -= 1
        hi = at
        while hi + 1 < len(line) and hue(line[hi + 1]) == h:
            hi += 1

        # One group on each side, whatever it is: a run of another colour, or a single raider.
        if lo > 0:
            left = hue(line[lo - 1])
            lo -= 1
            while lo > 0 and left != "\0" and hue(line[lo - 1]) == left:
                lo -= 1
        if hi + 1 < len(line):
            right = hue(line[hi + 1])
            hi += 1
            while hi + 1 < len(line) and right != "\0" and hue(line[hi + 1]) == right:
                hi += 1

        log.deeds.append(("lance", lo, hi, 1))
        self._blow(list(range(lo, hi + 1)), log, 1, cuts=True)
        self._settle(log, beat=2)

    def _settle(self, log, beat=1):
        """Wave after wave, until nothing is three alike any more.

        Every wave is read off the line **as it stands** and only then acted on, so nothing
        here depends on which end the loop started from - the rule invariant 26h asks of a
        resolution that a second runtime has to reproduce exactly.
        """
        guard = 0
        while guard <= self.track * 2:
            guard += 1

            going = []
            for at, length, _ in self.runs():
                if length >= BURST_AT:
                    going.extend(range(at, at + length))

            if not going:
                break

            self._blow(going, log, beat)
            beat += 1

        log.waves = beat - 1

    def _blow(self, going, log, beat, cuts=False):
        """Takes a set of pods off the line, cracks what is beside them, and closes the gap."""
        if not going:
            return

        going = sorted(set(going))
        line = self.line

        # Read every consequence off the line as it stands before anything is removed, so a
        # raider beside two bursting runs is cracked once and not twice.
        touched = set()
        for i in going:
            for j in (i - 1, i + 1):
                if 0 <= j < len(line) and j not in going and is_raider(line[j]):
                    touched.add(j)

        for i in going:
            c = line[i]
            if is_caged(c):
                self.freed += 1
                log.freed += 1
                log.deeds.append(("free", i, c, beat))
            elif is_raider(c):
                # Only a lance reaches a raider inside the blast; a colour run never holds one,
                # so this is the Spark cutting straight through plating.
                self.scrapped += 1
                log.scrapped += 1
                log.deeds.append(("scrap", i, c, beat))
            else:
                log.deeds.append(("burst", i, c, beat))
            log.took += 1

        for i in sorted(touched):
            c = line[i]
            left = struck(c)
            if left is None or cuts:
                self.scrapped += 1
                log.scrapped += 1
                log.deeds.append(("scrap", i, c, beat))
                going.append(i)
                log.took += 1
            else:
                line[i] = left
                log.staggered += 1
                log.deeds.append(("stagger", i, c, beat))

        gone = set(going)
        self.line = [c for i, c in enumerate(line) if i not in gone]

        log.deeds.append(("close", len(gone), "\0", beat))

    def _march(self, log):
        """One step nearer the portal, and whatever that costs.

        **This is the allowance made visible**, and the reason this mode draws its pressure on
        the board rather than only in a corner: every core spent is a step the raiders take,
        so how much time is left is a thing the player can see rather than a number they have
        to keep reading.

        **A plain pod goes through the gate and is gone; a goal jams.** Both halves matter. The
        first is a real cost with no fail state attached - the line bleeds the very material a
        core needs to match, so a player who ignores the front of the line finds their options
        narrowing - and the second is what keeps every board winnable for as long as there are
        cores to fire (see `stranded`).
        """
        if not self.line:
            if self.head > 0:
                self.head -= 1
            return

        if self.jammed:
            log.deeds.append(("jam", 0, self.line[0], log.waves + 1))
            return

        if self.head > 0:
            self.head -= 1
            return

        c = self.line.pop(0)
        log.escaped += 1
        log.deeds.append(("escape", 0, c, log.waves + 1))

    # ------------------------------------------------------------------ the key
    def key(self):
        """Everything a rule reads and nothing else.

        `index` is left out deliberately: the search walks in layers and one shot is one
        layer, so the magazine's position is the depth and carrying it would only stop two
        identical boards merging. `head` is carried even though it is also the depth, because
        it is cheap and a board that stopped agreeing with its own front would be silent.
        """
        return "%d|%s|%d" % (self.head, "".join(self.line), 1 if self.spark else 0)


class Future(object):
    """`MarchFuture`. One arrangement as the solver sees it."""

    def __init__(self, board):
        self.board = board
        self._moves = board.moves()

    def won(self):
        return self.board.finished

    def move_count(self):
        return len(self._moves)

    def play(self, move):
        if not (0 <= move < len(self._moves)):
            return None
        forked = self.board.fork()
        if forked.fire(self._moves[move]) is None:
            return None
        return Future(forked)

    def gain(self, move):
        """What a player who never looks ahead would notice this move is worth.

        Goals count for four pods each, because a rescue is the thing on the screen a player
        is actually chasing and a reading that ignored it would describe nobody.
        """
        if not (0 <= move < len(self._moves)):
            return 0
        forked = self.board.fork()
        log = forked.fire(self._moves[move])
        return 0 if log is None else log.took + log.goals * 4

    def key(self):
        return self.board.key()

    # ---- the small protocol `content.py`'s shared prototype check asks for.
    def any_move(self):
        return self.board.any_move

    def goals(self):
        return self.board.goals

    def stranded(self):
        return self.board.stranded


# ---------------------------------------------------------------------------- readings


def readings(layout):
    """Everything an author wants to know about one haul-road, counted rather than argued about.

    `chain` is the mode's payoff measured - the most waves any single opening shot sets off.
    `forgeable` counts opening shots that forge a Spark, which is 26h's test made countable:
    a special nobody can make on the board as dealt is a special the author placed rather than
    one the player earned. `runway` is how many shots the raiders are from the portal, which
    has to be read against the allowance or the board is lost twice over.
    """
    board = Board(layout)

    cages = sum(1 for c in layout.line if is_caged(c))
    haulers = sum(1 for c in layout.line if c == HAULER)
    wardens = sum(1 for c in layout.line if c == WARDEN)

    chain, forgeable, biggest = 0, 0, 0
    for move in board.moves():
        forked = board.fork()
        log = forked.fire(move)
        if log is None:
            continue
        chain = max(chain, log.waves)
        biggest = max(biggest, log.took)
        if log.forged:
            forgeable += 1

    # How settled the line is as it is dealt: a board holding three alike already touching
    # goes off before anybody has touched it, which is Budburst's "authored settled" rule.
    settled = all(length < BURST_AT for _, length, _ in board.runs())

    deep = deepen(layout)

    return dict(pods=len(layout.line), cages=cages, haulers=haulers, wardens=wardens,
                track=layout.track, runway=board.runway, menace=board.menace,
                colours=len(set(layout.cores)),
                chain=chain, forgeable=forgeable, biggest=biggest,
                chained=deep["chained"], forged=deep["forged"], lanced=deep["lanced"],
                settled=1 if settled else 0)


#: What `deepen` will expand before giving up. The same walk as `ProtoSearch.Solve`, so it
#: cannot be dearer than the search that already ran.
DEEP_BUDGET = 90000
DEEP_DEPTH = 24


def deepen(layout):
    """What the *shortest* answers actually do, rather than what the opening move can do.

    **This is the reading that judges the mechanics, and the opening-move one is not.**
    `chain` above asks what a player could set off on move one, which collapses exactly when
    a board is good: a line where the very first shot sets off a four-wave cascade is a line
    that is *over* in three shots, so tuning against it selects for short boards. What an
    author actually wants to know is whether the mode's payoff is on the road to the answer -
    so this walks every shortest solution and reports the most waves any of them sets off,
    whether any of them forges a Spark, and whether any of them spends one.

    That is Budburst's `kindled` and Nova Raid's `fired` asked here (invariants 20m, 26h):
    measured over **every** shortest solution rather than the first one the search reaches,
    because `ways` is rarely one and the first winning line is arbitrary among several.

    Carried along the frontier rather than searched again: a state reached at depth d by two
    routes keeps the better of the two, so what comes back at the winning layer is a fact
    about the set of shortest solutions and costs one extra integer per state.
    """
    start = Board(layout)
    if start.finished:
        return dict(chained=0, forged=0, lanced=0)

    def blank():
        return (0, 0, 0)

    def better(a, b):
        return (max(a[0], b[0]), max(a[1], b[1]), max(a[2], b[2]))

    seen = {start.key()}
    frontier = [(start, blank())]
    nodes = 0

    for _ in range(DEEP_DEPTH):
        nxt, index = [], {}
        won = None

        for board, carried in frontier:
            nodes += 1
            if nodes > DEEP_BUDGET:
                return dict(chained=0, forged=0, lanced=0)

            for move in board.moves():
                forked = board.fork()
                log = forked.fire(move)
                if log is None:
                    continue

                mark = (max(carried[0], log.waves),
                        carried[1] + (1 if log.forged else 0),
                        carried[2] + (1 if log.lanced else 0))

                if forked.finished:
                    won = mark if won is None else better(won, mark)
                    continue

                key = forked.key()
                if key in seen:
                    continue

                if key in index:
                    at = index[key]
                    nxt[at] = (nxt[at][0], better(nxt[at][1], mark))
                    continue

                index[key] = len(nxt)
                nxt.append((forked, mark))

        if won is not None:
            return dict(chained=won[0], forged=won[1], lanced=won[2])

        if not nxt:
            return dict(chained=0, forged=0, lanced=0)

        seen.update(index)
        frontier = nxt

    return dict(chained=0, forged=0, lanced=0)
