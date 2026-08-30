"""End-to-end check of the shipped content, mirroring LevelValidator.cs
and ChapterMapValidator.cs."""
import json
import re, math, os, sys
from collections import deque

import fall                                  # Lightfall's rules, mirrored - see fall.py
import keeper                                # Groovekeeper's rules, mirrored - see keeper.py
import bud                                   # Budburst's rules, mirrored - see bud.py

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


    # 'x' was the duskcap and is deliberately not here. A retired head must be refused
    # rather than ignored, or a chapter file written for a build that no longer exists
    # validates green with a tile nothing on the board knows what to do with.
    if tok[0] not in '-=%*@':
        errors.append(f"{ctx}: unknown head '{tok[0]}' in '{tok}'")
        return None

    kind = {'-': 'pipe', '=': 'cross', '%': 'briar', '*': 'source',
            '@': 'lamp'}[tok[0]]
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
    if kind == 'cross' and colour:
        errors.append(f"{ctx}: a crossing takes no colour ('{tok}')")
    if kind == 'briar' and colour:
        errors.append(f"{ctx}: a briar takes no colour; it decides which way light may go, "
                      f"never which light may go there ('{tok}')")

    return dict(kind=kind, solved=mask, rot=rot, locked=locked, colour=colour,
                fragile=fragile, link=link, cross=cross, gate=gate)


# Difficulty, mirroring LevelTuning.cs. Both star thresholds and the losing line are
# multiples of par, and par is derived from the board - so a glade authors no difficulty
# number at all unless it wants a looser budget than the default.
#
# There is nothing here about a clock. A glade used to be graded on the worse of its turns
# and its time, which meant the turn thresholds - the only half that measures whether the
# board was solved well - decided nothing for any player who stopped to think. The clock is
# gone from every mode, and with it `timeFactor`, the published `clockScale` and the tap-rate
# warnings this file used to raise.
DEFAULT_BUDGET_FACTOR = 1.60
GOLD_FACTOR, SILVER_FACTOR = 1.20, 1.40


MODE_BLOCKS = ("fall", "keeper", "bud")


#: Mirrors `BudValidator`. Lower than the other two because branching is the flower count.
BUD_NODE_WARNING, BUD_NODE_CEILING = 20_000, 60_000

#: Below this many shortest plays the grove is a puzzle, which this mode is deliberately not.
BUD_TOO_FEW_WAYS = 2


def check_bud(lid, chapter_id, level, block):
    """Everything a Budburst grove has to prove, mirroring `BudValidator`.

    Two of the checks are the house rules read backwards and that is deliberate: everywhere else
    a board almost anything finishes is a warning (invariant 5d) and a careless player finishing
    is one too. This mode's brief is a board almost anything finishes, so what is worth refusing
    is a grove a careless player *cannot* finish.
    """
    empty = dict(id=lid, chapter=chapter_id, w=0, h=0, par=0, budget=0,
                 gold=0, silver=0, lamps=0, sources=0, fragile=0, bound=0,
                 crossings=0, briars=0, mode='bud',
                 ways=0, greedy=-1, nodes=0, buds=0, cocoons=0, ready=0, deal='')

    w, h = block.get('width') or 0, block.get('height') or 0
    rows = block.get('rows') or []
    deal = block.get('colours') or ''

    if not (bud.MIN_SIDE <= w <= bud.MAX_SIDE):
        errors.append("%s: a grove is %d..%d wide; this one says %d"
                      % (lid, bud.MIN_SIDE, bud.MAX_SIDE, w))
        return empty

    if not (bud.MIN_SIDE <= h <= bud.MAX_SIDE):
        errors.append("%s: a grove is %d..%d tall; this one says %d"
                      % (lid, bud.MIN_SIDE, bud.MAX_SIDE, h))
        return empty

    try:
        grove = bud.Grove(rows, deal)
    except (ValueError, KeyError, IndexError) as bad:
        errors.append("%s: %s" % (lid, bad))
        return empty

    if grove.w != w or grove.h != h:
        errors.append("%s: declares %dx%d and writes %dx%d" % (lid, w, h, grove.w, grove.h))
        return empty

    start = bud.Board(grove)

    if not start.shut:
        errors.append("%s: nobody is shut in on this grove, so it is already finished" % lid)
        return empty

    if not start.flowers:
        errors.append("%s: this grove has no flower on it, so there is nothing to tap" % lid)
        return empty

    # A grove holding a bunch of three before a tap is spent is a board that goes off on its own
    # in the first frame - the player is shown a chain they did not cause, and par is measured
    # against a position they never met. Mirrors `BudValidator.Settled`.
    blobs = start.groups()
    if blobs:
        where = blobs[0][1][0]
        errors.append("%s: this grove already holds a bunch of three alike at %d,%d, so it "
                      "bursts before the player has touched it - author a settled board"
                      % (lid, where % grove.w, where // grove.w))
        return empty

    # A cocoon with no flower beside it can never be cracked, because nothing here grows one
    # back. The search catches it as "nobody can finish this", which is true and says nothing
    # about what to move. Mirrors `BudValidator.Reachable`.
    for i in range(grove.count):
        if grove.ground[i] != "c":
            continue
        if any(grove.ground[n] == "f" for n in grove.beside(i)):
            continue
        errors.append("%s: the cocoon at %d,%d has no flower beside it, and nothing in a grove "
                      "ever grows one - so no chain can ever crack it"
                      % (lid, i % grove.w, i // grove.w))

    par, ways, nodes, proved = bud.search(rows, deal)

    if not proved:
        errors.append("%s: this grove could not be proved inside %d positions (it looked at "
                      "%d) or within %d taps - the player's device runs the same search to work "
                      "out par" % (lid, bud.NODE_BUDGET, nodes, bud.MAX_TAPS))
        return empty

    if par < 1:
        errors.append("%s: no order of taps frees every critter on this grove" % lid)
        return empty

    budget_h = factor_of(level, 'budgetFactor', bud.BUDGET_HUNDREDTHS)
    gold_h = factor_of(level, 'goldFactor', bud.GOLD_HUNDREDTHS)
    silver_h = factor_of(level, 'silverFactor', bud.SILVER_HUNDREDTHS)

    spare = block.get('spare') or bud.DEFAULT_SPARE
    budget = (par + spare) if budget_h > 0 else 0

    if budget_h > 0 and budget_h != bud.BUDGET_HUNDREDTHS:
        errors.append("%s: this grove authors budgetFactor %.2f, which does nothing - room "
                      "above par is 'spare', counted in taps" % (lid, budget_h / 100.0))

    gold, silver = bud.over(par, gold_h), bud.over(par, silver_h)

    if gold_h >= silver_h:
        errors.append("%s: goldFactor and silverFactor leave the two-star band empty" % lid)
    elif budget and budget <= gold:
        errors.append("%s: the satchel is at or under the three-star line, so every surviving "
                      "run would be a three-star run" % lid)
    elif budget and budget <= silver:
        warnings.append("%s: the satchel is inside the two-star band, so one star can never be "
                        "scored" % lid)

    if nodes > BUD_NODE_CEILING:
        errors.append("%s: proving this grove took %d positions, above the %d a level may "
                      "cost - the player's device runs the same search when somebody opens it"
                      % (lid, nodes, BUD_NODE_CEILING))
    elif nodes > BUD_NODE_WARNING:
        warnings.append("%s: proving this grove took %d positions against the %d a level is "
                        "expected to cost (the refusal is at %d)"
                        % (lid, nodes, BUD_NODE_WARNING, BUD_NODE_CEILING))

    if ways < BUD_TOO_FEW_WAYS:
        warnings.append("%s: there is only one play of %d taps that frees every critter here, "
                        "so this grove is a puzzle rather than a place to make a mess"
                        % (lid, par))

    careless = bud.careless(rows, deal, budget or (par + bud.DEFAULT_SPARE))
    if careless < 0:
        warnings.append("%s: a player who always taps whatever sets off the biggest chain never "
                        "finishes this grove, which is the bar this mode is held to" % lid)
    elif budget and careless > budget:
        warnings.append("%s: a careless player takes %d taps against a satchel of %d"
                        % (lid, careless, budget))

    return dict(id=lid, chapter=chapter_id, w=grove.w, h=grove.h, par=par,
                budget=budget, gold=gold, silver=silver, lamps=0, sources=0, fragile=0,
                bound=0, crossings=0, briars=0, mode='bud',
                ways=ways, greedy=careless, nodes=nodes,
                buds=start.flowers, cocoons=start.shut,
                ready=len(set(grove.colour[i] for i in range(grove.count)
                              if grove.ground[i] == "f")),
                deal=deal)


#: Where a well stops being cheap to prove, and where it stops being shippable. Mirrors
#: `FallValidator`. These are about the *player's* device: the search runs once per level, on
#: the phone, when somebody opens it. Forty thousand positions is about twenty milliseconds of
#: desktop .NET and a few tens on a phone; a hundred and twenty thousand is a quarter of a
#: second, which is a pause on the way into a level.
FALL_NODE_WARNING, FALL_NODE_CEILING = 40_000, 120_000

#: Above this many shortest solutions the board is not deciding much. Invariant 5d, counted.
FALL_TOO_MANY_WAYS = 400


def check_fall(lid, chapter_id, level, block):
    """Everything a Lightfall well has to prove, mirroring `FallValidator`.

    The one thing that cannot be checked by reading the file is whether the well can be
    *emptied*, so most of this is a search. Every failure below looks like a perfectly
    authored board in the JSON, which is the whole reason the gate exists.
    """
    empty = dict(id=lid, chapter=chapter_id, w=0, h=0, par=0, budget=0,
                 gold=0, silver=0, lamps=0, sources=0, fragile=0, bound=0,
                 crossings=0, briars=0, mode='fall',
                 ways=0, greedy=-1, nodes=0, fall_motes=0, headroom=0, deal='')

    w, h = block.get('width', 0), block.get('height', 0)
    if not (4 <= w <= 8):
        errors.append("%s: a well is 4..8 wide; this one says %s" % (lid, w))
        return empty
    if not (6 <= h <= 14):
        errors.append("%s: a well is 6..14 tall; this one says %s" % (lid, h))
        return empty

    # Retired with the score attack. Named rather than ignored, for ChapterDto.order's
    # reason: a number that does nothing is worse than a missing one, because somebody
    # believes it.
    if block.get('seed'):
        errors.append("%s: 'seed' is retired - a well is authored rather than dealt, and a "
                      "seed here does nothing" % lid)

    try:
        cells, ww, hh = fall.parse_rows(block.get('rows') or [], w, h)
    except ValueError as why:
        errors.append("%s: %s" % (lid, why))
        return empty

    try:
        deal = fall.parse_deal(block.get('motes') or '')
    except ValueError as why:
        errors.append("%s: %s" % (lid, why))
        return empty

    well = fall.Well(cells, ww, hh)

    if well.motes == 0:
        errors.append("%s: an empty well is already won" % lid)
        return empty

    for x in range(ww):
        if cells[fall.BRIM * ww + x]:
            errors.append("%s: there is a mote standing in column %d of the brim row, which "
                          "is the row that ends the run - this level begins lost" % (lid, x))
            break

    for x in range(ww):
        air = False
        for y in range(hh - 1, -1, -1):
            here = bool(cells[y * ww + x])
            if not here:
                air = True
                continue
            if not air:
                continue
            errors.append("%s: the mote at column %d row %d has nothing under it, so the well "
                          "would settle differently from the way it is written the first time "
                          "anything bursts" % (lid, x, y))
            break

    # Every channel, not merely every channel the board wants now: a drop onto bare ground
    # makes a fresh pure mote, so a two-colour procession can be walked into a position no
    # amount of play recovers from - and on the opening well, which is authored without a
    # supply, that is a board that can be neither won nor lost.
    if fall._channels(deal) != fall.ALL:
        absent = fall.ALL & ~fall._channels(deal)
        errors.append("%s: this procession never deals %s, so a mote that ends up wanting it "
                      "could never be finished - and a drop onto bare ground makes one. A deal "
                      "has to carry all three channels" % (lid, fall.LETTER_OF[absent]))

    par, ways, nodes, proved = fall.search(cells, ww, hh, deal)

    if not proved:
        errors.append("%s: this well could not be proved inside %d positions (it looked at %d) "
                      "or within %d drops - it may be unsolvable, or simply too big to prove, "
                      "and either way the player's device runs the same search to work out par"
                      % (lid, fall.NODE_BUDGET, nodes, fall.MAX_DROPS))
        return empty

    if par < 1:
        errors.append("%s: no sequence of drops empties this well without flooding it, so "
                      "nobody can finish it" % lid)
        return empty

    budget_h = factor_of(level, 'budgetFactor', fall.BUDGET_HUNDREDTHS)
    gold_h = factor_of(level, 'goldFactor', fall.GOLD_HUNDREDTHS)
    silver_h = factor_of(level, 'silverFactor', fall.SILVER_HUNDREDTHS)

    # A well's room is a count of wasted drops rather than a multiple of par: a wrong drop is
    # permanent *and* leaves a mote that still has to be cooked, so a mistake costs about two
    # drops wherever it happens, while a fraction of par gives a short well almost none.
    # Mirrors LevelTuning.Slack and FallRules.DefaultSpare.
    spare = block.get('spare') or fall.DEFAULT_SPARE
    budget = (par + spare) if budget_h > 0 else 0

    # A budgetFactor on a well is overruled by `spare` and therefore does nothing. Named rather
    # than ignored - a number that silently means nothing is worse than a missing one. A
    # negative one is not an override: it turns the budget off, which spare cannot express.
    if budget_h > 0 and budget_h != fall.BUDGET_HUNDREDTHS:
        errors.append("%s: this well authors budgetFactor %.2f, which does nothing - a well's "
                      "room above par is 'spare', counted in drops. Use 'spare', or a negative "
                      "budgetFactor if it is meant to be unlosable"
                      % (lid, budget_h / 100.0))
    gold, silver = fall.over(par, gold_h), fall.over(par, silver_h)

    if gold_h >= silver_h:
        errors.append("%s: goldFactor and silverFactor leave the two-star band empty" % lid)
    elif budget and budget <= gold:
        errors.append("%s: the supply is at or under the three-star line, so every surviving "
                      "run would be a three-star run" % lid)
    elif budget and budget <= silver:
        warnings.append("%s: the supply is inside the two-star band, so one star can never "
                        "be scored" % lid)

    if nodes > FALL_NODE_CEILING:
        errors.append("%s: proving this well took %d positions, above the %d a level may cost - "
                      "the player's device runs the same search when somebody opens the level, "
                      "so this is about a quarter of a second of nothing happening on the way "
                      "in" % (lid, nodes, FALL_NODE_CEILING))
    elif nodes > FALL_NODE_WARNING:
        warnings.append("%s: proving this well took %d positions against the %d a level is "
                        "expected to cost (the refusal is at %d)"
                        % (lid, nodes, FALL_NODE_WARNING, FALL_NODE_CEILING))

    if ways > FALL_TOO_MANY_WAYS:
        warnings.append("%s: %d different sequences of %d drops empty this well, so almost "
                        "any tidy play wins and the procession is deciding nothing"
                        % (lid, ways, par))

    greedy = fall.greedy(cells, ww, hh, deal)
    if par > 3 and 0 <= greedy <= max(budget, 1):
        warnings.append("%s: a player who never looks ahead empties this well in %d drops "
                        "against a supply of %d" % (lid, greedy, budget))

    if well.headroom <= 0:
        warnings.append("%s: the fill reaches the row below the brim, so the very first "
                        "careless drop on the tallest column ends the run" % lid)

    return dict(id=lid, chapter=chapter_id, w=ww, h=hh, par=par, budget=budget,
                gold=gold, silver=silver, lamps=0, sources=0, fragile=0, bound=0,
                crossings=0, briars=0, mode='fall',
                ways=ways, greedy=greedy, nodes=nodes,
                fall_motes=well.motes, headroom=well.headroom, deal=block.get('motes'))


#: Mirrors `KeeperValidator`. Lower than Lightfall's pair because a position here costs more to
#: expand: the floor this search prunes on walks every bed and every standing tile.
KEEPER_NODE_WARNING, KEEPER_NODE_CEILING = 30_000, 90_000

#: Above this many shortest answers the ground is not deciding much. Invariant 5d, counted.
KEEPER_TOO_MANY_WAYS = 300


def check_keeper(lid, chapter_id, level, block):
    """Everything a Groovekeeper grove has to prove, mirroring `KeeperValidator`.

    The one thing that cannot be checked by reading the file is whether every bed can be
    *opened*, so most of this is a search. Every failure below looks like a perfectly authored
    grove in the JSON, which is the whole reason the gate exists.
    """
    empty = dict(id=lid, chapter=chapter_id, w=0, h=0, par=0, budget=0,
                 gold=0, silver=0, lamps=0, sources=0, fragile=0, bound=0,
                 crossings=0, briars=0, mode='keeper',
                 ways=0, greedy=-1, nodes=0, beds=0, hearts=0, deal='')

    w, h = block.get('width') or 0, block.get('height') or 0

    if not (keeper.MIN_SIDE <= w <= keeper.MAX_SIDE):
        errors.append("%s: a grove is %d..%d wide; this one says %d"
                      % (lid, keeper.MIN_SIDE, keeper.MAX_SIDE, w))
        return empty

    if not (keeper.MIN_SIDE <= h <= keeper.MAX_SIDE):
        errors.append("%s: a grove is %d..%d tall; this one says %d"
                      % (lid, keeper.MIN_SIDE, keeper.MAX_SIDE, h))
        return empty

    # A grove used to be dealt from a seed. An author who writes one now is describing a board
    # that no longer exists, and JsonUtility would drop it without a word - the same tripwire
    # ChapterDto.order is.
    if block.get('seed'):
        errors.append("%s: this grove authors 'seed', which does nothing: a grove is authored "
                      "now rather than rolled, so its rows and its procession are the whole "
                      "board" % lid)

    try:
        grove = keeper.Grove(block.get('rows') or [], block.get('tiles') or '')
    except ValueError as bad:
        errors.append("%s: %s" % (lid, bad))
        return empty

    if grove.width != w or grove.height != h:
        errors.append("%s: declares %dx%d and writes %dx%d"
                      % (lid, w, h, grove.width, grove.height))
        return empty

    if not grove.sprig_count:
        errors.append("%s: this grove has no sprig, so there is nothing to lay the first tile "
                      "beside and no way to start it" % lid)
        return empty

    if not grove.beds:
        errors.append("%s: this grove has no bed, so it is already finished" % lid)
        return empty

    # A procession that cannot supply a colour some heartbed insists on makes that bed
    # unopenable however many tiles are bought. The search would catch it, but not in words
    # anybody could act on.
    if grove.wanted & ~grove.channels:
        absent = grove.wanted & ~grove.channels
        errors.append("%s: a heartbed here insists on %s and this procession never deals it, so "
                      "that bed could never be opened by anybody"
                      % (lid, keeper.LETTER.get(absent, '?')))

    # Note what is deliberately *not* checked: that the procession carries all three channels.
    # Lightfall refuses a deal that does not and has to; nothing here does the same thing, because
    # a tile that cannot bloom is simply a tile and the sprigs are permanent. Two of the ten
    # grooves that ship are finished with a two-colour basket. What matters is that every bed can
    # be opened, and the search below proves exactly that. See `KeeperValidator`.

    par, ways, nodes, proved = keeper.search(grove)

    if not proved:
        errors.append("%s: this grove could not be proved inside %d positions (it looked at %d) "
                      "or within %d tiles - it may be unsolvable, or simply too big to prove, "
                      "and either way the player's device runs the same search to work out par"
                      % (lid, keeper.NODE_BUDGET, nodes, keeper.MAX_TILES))
        return empty

    if par < 1:
        errors.append("%s: no sequence of tiles opens every bed on this grove, so nobody can "
                      "finish it" % lid)
        return empty

    budget_h = factor_of(level, 'budgetFactor', keeper.BUDGET_HUNDREDTHS)
    gold_h = factor_of(level, 'goldFactor', keeper.GOLD_HUNDREDTHS)
    silver_h = factor_of(level, 'silverFactor', keeper.SILVER_HUNDREDTHS)

    # A grove's room is a count of wasted tiles rather than a multiple of par: a wrong tile is
    # permanent *and* takes a cell of ground a bed beside it may have needed, so a mistake costs
    # about two tiles wherever it happens. Mirrors LevelTuning.Slack and KeeperRules.DefaultSpare.
    spare = block.get('spare') or keeper.DEFAULT_SPARE
    budget = (par + spare) if budget_h > 0 else 0

    if budget_h > 0 and budget_h != keeper.BUDGET_HUNDREDTHS:
        errors.append("%s: this grove authors budgetFactor %.2f, which does nothing - a grove's "
                      "room above par is 'spare', counted in tiles. Use 'spare', or a negative "
                      "budgetFactor if it is meant to be unlosable" % (lid, budget_h / 100.0))

    gold, silver = keeper.over(par, gold_h), keeper.over(par, silver_h)

    if gold_h >= silver_h:
        errors.append("%s: goldFactor and silverFactor leave the two-star band empty" % lid)
    elif budget and budget <= gold:
        errors.append("%s: the basket is at or under the three-star line, so every surviving run "
                      "would be a three-star run" % lid)
    elif budget and budget <= silver:
        warnings.append("%s: the basket is inside the two-star band, so one star can never be "
                        "scored" % lid)

    if nodes > KEEPER_NODE_CEILING:
        errors.append("%s: proving this grove took %d positions, above the %d a level may cost - "
                      "the player's device runs the same search when somebody opens the level, "
                      "so this is about a quarter of a second of nothing happening on the way in"
                      % (lid, nodes, KEEPER_NODE_CEILING))
    elif nodes > KEEPER_NODE_WARNING:
        warnings.append("%s: proving this grove took %d positions against the %d a level is "
                        "expected to cost (the refusal is at %d)"
                        % (lid, nodes, KEEPER_NODE_WARNING, KEEPER_NODE_CEILING))

    # A basket bigger than the ground can hold ends the run on the one fail state a continue
    # cannot rescue, with tiles still in the basket.
    room = grove.room - grove.sprig_count
    if budget and budget > room:
        warnings.append("%s: this grove is dealt %d tiles onto %d cells of bare ground, so a "
                        "careless run runs out of somewhere to plant before it runs out of tiles"
                        % (lid, budget, room))

    if ways > KEEPER_TOO_MANY_WAYS:
        warnings.append("%s: %d different groves of %d tiles open every bed here, so almost any "
                        "tidy play wins and the ground is deciding nothing" % (lid, ways, par))

    greedy = keeper.greedy(grove, budget or (par + keeper.DEFAULT_SPARE))
    if par > 3 and 0 <= greedy <= max(budget, 1):
        warnings.append("%s: a player who never looks ahead finishes this grove in %d tiles "
                        "against a basket of %d" % (lid, greedy, budget))

    return dict(id=lid, chapter=chapter_id, w=grove.width, h=grove.height, par=par,
                budget=budget, gold=gold, silver=silver, lamps=0, sources=0, fragile=0,
                bound=0, crossings=0, briars=0, mode='keeper',
                ways=ways, greedy=greedy, nodes=nodes,
                beds=len(grove.beds), hearts=len(grove.heartbeds),
                deal=block.get('tiles'))


def factor_of(level, key, fallback_hundredths):
    """An authored tuning factor as hundredths, mirroring LevelTuning.

    Read off the *level*, beside mapX and the id, and never out of the mode's own block - that
    mistake takes the default on every level that authored one, which is how the well that
    cannot be lost came out with a supply of four.

    0 means "not written" and takes the default; a negative turns the line off entirely, which
    is the only way to author a level that cannot be lost.
    """
    raw = level.get(key) or 0
    if raw == 0:
        return fallback_hundredths
    if raw < 0:
        return 0
    return int(round(raw * 100))


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

        # Every mode that ships now authors its whole level in the file, so every one of them is
        # provable here rather than only in the Editor. That was not always true - a Lightweave
        # board was generated from a seed, and the mode has been removed.
        if claimed[0] == 'fall':
            return check_fall(lid, chapter_id, level, block)

        # Groovekeeper is the other mode whose whole level is in the file, so this gate proves
        # it too: the ground, the procession and the search that turns the two into par.
        if claimed[0] == 'keeper':
            return check_keeper(lid, chapter_id, level, block)

        # Budburst is the third, and for the same reason: everything about a grove is in the
        # file, so the flowers, the basket and the search that turns the two into par can all be
        # proved with no Unity anywhere.
        if claimed[0] == 'bud':
            return check_bud(lid, chapter_id, level, block)

        return dict(id=lid, chapter=chapter_id,
                    w=block.get('width', 0), h=block.get('height', 0), par=0, budget=0,
                    gold=0, silver=0, lamps=0, sources=0, fragile=0, bound=0,
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
    # what lets a second network run *through* a live one instead of only beside it.
    #
    # Parameterised by `rots` rather than pinned to the solution, because one rule needs to
    # ask what happens when a single tile is turned - see `decidable` below. Everything else
    # passes nothing and gets the authored board, which is what every other proof here is
    # about. Mirrors author.Board.solve_state, and the default is the whole of what the old
    # inline walk did.
    def strands(c):
        return 2 if c and c['cross'] else 1

    def strand_at(c, d, r=0):
        if not c['cross']:
            return 0
        return 0 if rotl(c['cross'], r) & BITS[d] else 1

    def solve(rots=None):
        rots = rots or {}
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
                ra = rots.get(a, 0)
                for d in range(4):
                    if not (rotl(live(ca), ra) & BITS[d]):
                        continue
                    if strand_at(ca, d, ra) != sa:
                        continue
                    bx, by = ax + STEP[d][0], ay + STEP[d][1]
                    nb = at(bx, by)
                    if nb is None:
                        continue
                    back = (d + 2) % 4
                    rb = rots.get(by * w + bx, 0)
                    if not (rotl(live(nb), rb) & BITS[back]):
                        continue
                    into = (by * w + bx) * 2 + strand_at(nb, back, rb)
                    if comp[into] != -1:
                        continue
                    comp[into] = g
                    q.append(into)
            comp_colour.append(colour)
        return comp, comp_colour

    comp, comp_colour = solve()

    def energy_in(i, state):
        """Every colour reaching a cell in the given (comp, comp_colour), across its strands."""
        found, colours = state
        mix = 0
        for st in range(strands(cells[i])):
            g = found[i * 2 + st]
            if g >= 0:
                mix |= colours[g]
        return mix

    def energy(i):
        return energy_in(i, (comp, comp_colour))

    def wins(state):
        """Whether every critter on the board is correctly lit in this arrangement."""
        any_lamp = False
        for i, c in enumerate(cells):
            if not c or c['kind'] != 'lamp':
                continue
            any_lamp = True
            have, want = energy_in(i, state), c['colour']
            if not ((have != 0) if want == 0 else (have == want)):
                return False
        return any_lamp

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

    def thorns_separate(i, c):
        """Whether taking this briar's thorns off would join anything to anything.

        No longer the rule - `decidable` is - but kept as the *reason* attached to its
        warning, because it is the commonest cause and the most actionable one. Only the
        thorned ways are asked about, and the way has to be open on the *other* side too, or
        lifting these thorns would still join nothing, which is what two briars back to back
        are. Mirrors LevelValidator.ThornsSeparate.
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

    def decidable(i, c):
        """Whether turning this four-armed tile one step off its solution un-finishes the glade.

        Mirrors LevelValidator.CheckDecidableTiles, and it is the rule rather than a proxy
        for it. A crossing and a briar wear all four arms at every angle, so every neighbour
        mates them however they are turned and nothing about the pipe-fitting says which way
        either one goes - which is what makes them worth authoring with and how they fail. If
        the glade still finishes with one turned, the player cannot place it by looking and
        has no reason on the board to place it either way.

        Asking the consequence is what fixed the topology check this replaces, which was
        wrong in both directions: it missed a tile separating two networks of *compatible*
        colour, and it fired on a briar whose open pair is the only way into a pocket
        (invariant 5f).
        """
        return not wins(solve({i: 1}))

    solution_wins = wins((comp, comp_colour))

    lamps = lit = crossings = briars = 0
    for i, c in enumerate(cells):
        if not c:
            continue
        have = energy(i)
        if c['kind'] in ('briar', 'cross'):
            # A straight crossing reads the same at every angle - architecture, and
            # Stonebridge roots four of them on purpose. A rooted tile cannot be turned at
            # all, so it decides nothing by construction and saying so would be noise. And
            # on a board whose solution does not win, the critter count below has the real
            # complaint and this would bury it.
            if (solution_wins and not c['locked']
                    and not alike(c['solved'], c['cross'], 1, c['gate'])
                    and not decidable(i, c)):
                bx, by = i % w, i // w
                why = ("every way it has leads back into one network"
                       if c['kind'] == 'briar' and not thorns_separate(i, c)
                       else "the two things it holds apart are answering the same colour")
                warnings.append(f"{ctx}: turning the {c['kind']} at {bx},{by} one step from "
                                f"its solution still finishes the glade, so nothing on this "
                                f"board settles it - {why}")
        if c['kind'] == 'briar':
            briars += 1
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

    # The losing line, derived exactly as LevelTuning.MoveBudget does: a multiple of par,
    # with 0 meaning "not authored" and only a negative value removing the budget. There is
    # no floor - an authored factor means what it says. It sits below SILVER_FACTOR at the
    # shipped default, which is deliberate: the budget is the only way a glade can be lost
    # since the clock was removed, so it has to bite before the player has already stopped
    # earning stars.
    gold = -(-int(round(par * GOLD_FACTOR * 100)) // 100)
    silver = -(-int(round(par * SILVER_FACTOR * 100)) // 100)

    budget_factor = level.get('budgetFactor', 0) or DEFAULT_BUDGET_FACTOR
    budget = 0 if budget_factor < 0 else -(-int(round(par * budget_factor * 100)) // 100)

    # The three lines have to be ordered and all three have to be landable, or a band
    # quietly stops existing while every number in the file still looks plausible. This is
    # the check that would have caught shipping a 1.60 budget against a 2.00 silver line.
    #
    # It reads the *factors* rather than the thresholds they derive: on a board of par 1 or 2
    # all three round onto the same number however the factors are set, so a check on
    # thresholds would report a tuning fault whose real cause is the board's size. Mirrors
    # LevelValidator.CheckStarBands.
    gold_f, silver_f = GOLD_FACTOR, SILVER_FACTOR

    if gold_f >= silver_f:
        errors.append(f"{ctx}: goldFactor {gold_f:g} is not below silverFactor {silver_f:g}, "
                      "so the two-star band is empty")
    if budget and budget_factor <= gold_f:
        errors.append(f"{ctx}: the run ends at par x {budget_factor:g} and three stars is "
                      f"par x {gold_f:g}, so no run can be graded")
    elif budget and budget_factor <= silver_f:
        warnings.append(f"{ctx}: the run ends at par x {budget_factor:g} and two stars is "
                        f"par x {silver_f:g}, so one star can never be scored - every clear "
                        "is worth two or three")

    return dict(id=lid, chapter=chapter_id, w=w, h=h, par=par, budget=budget,
                gold=gold, silver=silver,
                lamps=lamps, sources=sources, fragile=fragile, bound=bound,
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
STORE_SHELVES = {"gems", "coins", "bundles", "supplies"}

# The heart-container rungs. Mirrors StoreLimits.MinHeartCapacity / MaxHeartCapacity and
# products.ts MAX_CAPACITY.
MIN_CAPACITY = 6
MAX_CAPACITY = 50
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


def bonus_wheel(progression):
    """The published wheel and what it really makes `win_bonus` worth.

    The wheel multiplies that placement's authored amount, so from the moment it is
    published the amount stops being what a view is worth and nothing else in the file
    says so. `BonusWheel.Resolve` is the shipping copy of these rules and
    `functions/src/wheel.ts` is the server's; this is the third, and it exists because
    the other two need Unity and a deploy respectively.

    Returns None when there is no wheel, which is the flat offer and not a mistake.
    """
    ads = progression.get("ads") or {}
    wheel = ads.get("wheel") or {}
    slices = wheel.get("slices") or []
    if not slices:
        return None

    base, cap = 0, 0
    for placement in ads.get("placements") or []:
        if placement.get("id") == "win_bonus":
            base = placement.get("amount", 0)
            cap = placement.get("dailyCap", 0)

    percents = [int(entry.get("percent", 0)) for entry in slices]

    return {
        "percents": percents,
        "count": len(percents),
        "base": base,
        "cap": cap,
        # Rounded rather than truncated, matching BonusWheel.MeanPercent: a systematic
        # half-percent downward bias in a report is a report agreeing with itself.
        "mean": (sum(percents) + len(percents) // 2) // len(percents),
        "top": max(percents),
    }


def check_wheel(progression, warnings):
    """The rules BonusWheel.Resolve refuses on, mirrored so a bad wheel fails offline too.

    Every refusal here is a refusal there and on the server, and all three are refusals
    rather than repairs on purpose: a reader that quietly fixed a slice would accept a
    table another had rejected, and the two would then disagree about money.
    """
    wheel = bonus_wheel(progression)
    if wheel is None:
        return []

    errors = []

    if not 4 <= wheel["count"] <= 12:
        errors.append(f"ads wheel has {wheel['count']} slices; it must have between 4 and 12. "
                      "Fewer than four is a coin flip drawn as a wheel, and more than twelve "
                      "cannot be read while it turns")

    for i, percent in enumerate(wheel["percents"]):
        if percent < 100:
            errors.append(f"ads wheel slice {i} pays {percent}%, below 100%. The wheel may only "
                          "ever add - a slice under 100 would pay less than the flat offer the "
                          "button promised")
        if percent > 1000:
            errors.append(f"ads wheel slice {i} pays {percent}%, above the supported 1000%")

    if wheel["top"] <= 100:
        errors.append("ads wheel has no slice paying above the flat offer; every spin would land "
                      "on the same figure, so the spin is decoration")

    if not wheel["base"]:
        errors.append("ads authors a wheel but no 'win_bonus' placement for it to multiply; a "
                      "wheel is that placement's payout made variable, not a reward of its own")

    # The rim is drawn in the authored order, so two equal figures side by side make the
    # wheel look like it has fewer prizes than it has. A warning, because a deliberately
    # repeated figure on a big wheel is a coherent thing to want.
    for i, percent in enumerate(wheel["percents"]):
        nxt = (i + 1) % wheel["count"]
        if percent == wheel["percents"][nxt]:
            warnings.append(f"wheel slices {i} and {nxt} both pay {percent}% and sit side by "
                            "side; the rim is drawn in the authored order, so interleave them")

    return errors


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
        capacity = int(entry.get("heartCapacity") or 0)

        # A heart container: the one non-currency thing a real-money product may grant,
        # because a capacity is an idempotent permanent entitlement rather than an amount.
        # See StoreProduct.HeartCapacity for the rule and why it widens invariant 18 rather
        # than breaking it.
        if capacity and (credits or gems):
            errors.append(f"store product '{pid}' sells a heart capacity and also grants "
                          "currency; a real-money product may grant one or the other, never "
                          "both")
        elif capacity:
            if not MIN_CAPACITY <= capacity <= MAX_CAPACITY:
                errors.append(f"store product '{pid}' sells a heart capacity of {capacity}, "
                              f"outside {MIN_CAPACITY}..{MAX_CAPACITY}")
            if capacity > ceiling:
                errors.append(f"store product '{pid}' sells a heart capacity of {capacity}, "
                              f"above the published ceiling of {ceiling}; the timer would "
                              "carry a player past the most they are allowed to hold, so "
                              "every grant would be refused while the clock kept paying")
            if entry.get("kind") != "nonconsumable":
                errors.append(f"store product '{pid}' sells a heart capacity as a consumable; "
                              "a permanent upgrade must be nonconsumable so the store itself "
                              "refuses to sell it twice")
            if entry.get("shelf") != "supplies":
                errors.append(f"store product '{pid}' sells a heart capacity but sits on the "
                              f"'{entry.get('shelf')}' shelf; capacities belong on 'supplies', "
                              "which is where everything about hearts is")
        elif entry.get("shelf") == "supplies":
            errors.append(f"store product '{pid}' sits on the supplies shelf without selling a "
                          "heart capacity; that shelf is otherwise for goods bought with gems")

        if credits <= 0 and gems <= 0 and capacity <= 0:
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

    # The container ladder, which the money ladder below cannot see: a container grants no
    # currency, so its value per unit of money is zero and it would fail any shelf it was
    # ranked on. What has to hold instead is that a dearer vessel holds more — a rung that
    # costs more and holds no more is a card nobody can be right to buy, and it is invisible
    # in the file because the two numbers sit in different columns.
    vessels = sorted(
        (int(e.get("referenceUsdCents") or 0), int(e.get("heartCapacity") or 0), e.get("id"))
        for e in products if int(e.get("heartCapacity") or 0) > 0)

    for (cents0, cap0, id0), (cents1, cap1, id1) in zip(vessels, vessels[1:]):
        if cap1 <= cap0:
            errors.append(f"store: heart container '{id1}' costs more than '{id0}' and holds "
                          f"{cap1} against {cap0}. A ladder that stops getting better is a "
                          "rung nobody can be right to buy")
        elif cents1 == cents0:
            warnings.append(f"store: heart containers '{id0}' and '{id1}' are the same price; "
                            "which one a shelf draws first is then arbitrary")

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
        "vessels": [(cap, cents) for cents, cap, _ in vessels],
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


BOARD_VECTORS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "board-vectors.json")
FALL_VECTORS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fall-vectors.json")
KEEPER_VECTORS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "keeper-vectors.json")
BUD_VECTORS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bud-vectors.json")

# The marker the four-armed-tile rule's warning carries, in all three copies of it.
DECIDES_MARKER = "still finishes the glade"


def run_fall_vectors():
    """Runs `fall-vectors.json` through this file's copy of Lightfall's rules.

    The burst-and-wash rule exists twice - `FallBoard`/`FallSolver`, which is what ships, and
    `fall.py`, which is what this gate and the chapter scripts run because they have no Unity
    anywhere. Invariant 9a's answer for a board rule: the vector file is the contract, this
    proves the Python side of it and `FallVectorTests` proves the C# side.

    The cases are the places a loose transcription reads plausibly and answers differently -
    the wash being read before the fall, a mote that already holds the washed colour being
    left alone, a flooded position counting as dead rather than losing, and an unsolvable
    board coming back *proved* rather than timed out.
    """
    doc = json.load(open(FALL_VECTORS, encoding="utf-8"))
    cases = doc.get("cases") or []
    if not cases:
        errors.append("fall-vectors.json has no cases")
        return "fall vectors: none found"

    bad = []
    for case in cases:
        name = case.get("name", "?")
        try:
            cells, w, h = fall.parse_rows(case["rows"])
            deal = fall.parse_deal(case["motes"])
        except ValueError as why:
            bad.append("%s: %s" % (name, why))
            continue

        par, ways, _, proved = fall.search(cells, w, h, deal)
        well = fall.Well(cells, w, h)

        for label, got, want in (("proved", proved, case["proved"]),
                                 ("par", par, case["par"]),
                                 ("greedy", fall.greedy(cells, w, h, deal), case["greedy"]),
                                 ("headroom", well.headroom, case["headroom"]),
                                 ("standing", well.motes, case["standing"])):
            if got != want:
                bad.append("%s: %s is %r, vectors say %r" % (name, label, got, want))

        if case["par"] > 0 and ways != case["ways"]:
            bad.append("%s: ways is %r, vectors say %r" % (name, ways, case["ways"]))

    # A vector set that has quietly lost its teeth is worse than none: it passes, it is printed
    # beside the word "ok", and nothing says the rule stopped being checked.
    covers = dict(chain=False, only_one=False, unsolvable=False, brim=False)
    for case in cases:
        if case["par"] == 1 and case["standing"] >= 4:
            covers["chain"] = True
        if case["par"] > 0 and case["ways"] == 1:
            covers["only_one"] = True
        if case["proved"] and case["par"] < 0:
            covers["unsolvable"] = True
        if case["headroom"] == 0:
            covers["brim"] = True

    for what, held in covers.items():
        if not held:
            bad.append("no case covering '%s', so nothing here would notice that rule going away"
                       % what)

    for b in bad:
        errors.append("fall vectors: " + b)

    return (f"fall vectors: {len(cases)} case(s), the offline rules agree"
            if not bad else f"fall vectors: {len(bad)} disagreement(s)")


def run_keeper_vectors():
    """Runs `keeper-vectors.json` through this file's copy of Groovekeeper's rules.

    The bloom rule exists twice - `KeeperBoard`/`KeeperSolver`, which is what ships, and
    `keeper.py`, which is what this gate and the chapter script run because they have no Unity
    anywhere. Invariant 9a's answer for a board rule: the vector file is the contract, this
    proves the Python side of it and `KeeperVectorTests` proves the C# side.

    The cases are the places a loose transcription reads plausibly and answers differently -
    a tile that was already blooming being counted a second time, a heartbed accepting a
    colour it should refuse, stone conducting, a prism carrying one channel instead of three,
    and a walled-off bed coming back *timed out* rather than proved unopenable.
    """
    doc = json.load(open(KEEPER_VECTORS, encoding="utf-8"))
    cases = doc.get("cases") or []
    if not cases:
        errors.append("keeper-vectors.json has no cases")
        return "keeper vectors: none found"

    bad = []
    for case in cases:
        name = case.get("name", "?")
        try:
            grove = keeper.Grove(case["rows"], case["tiles"])
        except ValueError as why:
            bad.append("%s: %s" % (name, why))
            continue

        par, ways, _, proved = keeper.search(grove)
        budget = par + keeper.DEFAULT_SPARE if par else 0

        for label, got, want in (("proved", proved, case["proved"]),
                                 ("par", par, case["par"]),
                                 ("beds", len(grove.beds), case["beds"]),
                                 ("heartbeds", len(grove.heartbeds), case["heartbeds"]),
                                 ("room", grove.room, case["room"]),
                                 ("sprigs", grove.sprig_count, case["sprigs"])):
            if got != want:
                bad.append("%s: %s is %r, vectors say %r" % (name, label, got, want))

        if case["par"] > 0:
            if ways != case["ways"]:
                bad.append("%s: ways is %r, vectors say %r" % (name, ways, case["ways"]))

            greedy = keeper.greedy(grove, budget)
            if greedy != case["greedy"]:
                bad.append("%s: greedy is %r, vectors say %r" % (name, greedy, case["greedy"]))

    # A vector set that has quietly lost its teeth is worse than none: it passes, it is printed
    # beside the word "ok", and nothing says the rule stopped being checked.
    covers = dict(flourish=False, unopenable=False, heartbed=False, stone=False,
                  prism=False, only_one=False)
    for case in cases:
        if case["beds"] >= 4:
            covers["flourish"] = True
        if case["proved"] and case["par"] == 0:
            covers["unopenable"] = True
        if case["heartbeds"] > 0:
            covers["heartbed"] = True
        if case["room"] < len(case["rows"]) * len(case["rows"][0].replace(" ", "")):
            covers["stone"] = True
        if keeper.PRISM in case["tiles"]:
            covers["prism"] = True
        if case["par"] > 0 and case["ways"] == 1:
            covers["only_one"] = True

    for what, held in covers.items():
        if not held:
            bad.append("no case covering '%s', so nothing here would notice that rule going away"
                       % what)

    for b in bad:
        errors.append("keeper vectors: " + b)

    return (f"keeper vectors: {len(cases)} case(s), the offline rules agree"
            if not bad else f"keeper vectors: {len(bad)} disagreement(s)")


def run_bud_vectors():
    """Runs `bud-vectors.json` through this file's copy of Budburst's rules.

    The chain rule exists twice - `BudBoard`/`BudSolver`, which is what ships, and `bud.py`,
    which is what this gate and the chapter script run because they have no Unity anywhere.
    Invariant 9a's answer for a board rule: the vector file is the contract, this proves the
    Python side of it and `BudVectorTests` proves the C# side.

    The cases are the places a loose transcription reads plausibly and answers differently - a
    bunch of two going off, a burst forgetting to wash its colour outward, old wood carrying a
    chain, a cocoon taking every crack of a wave at once, and a tap that mixes nothing being
    allowed to spend a colour.

    Every case with a par also carries the taps of one shortest play and what each one came to,
    which is the half par alone cannot pin: two copies can agree exactly about how many taps a
    grove costs and still disagree about how far the chain ran.
    """
    doc = json.load(open(BUD_VECTORS, encoding="utf-8"))
    cases = doc.get("cases") or []
    if not cases:
        errors.append("bud-vectors.json has no cases")
        return "bud vectors: none found"

    bad = []
    for case in cases:
        name = case.get("name", "?")
        try:
            grove = bud.Grove(case["rows"], case["colours"])
        except (ValueError, KeyError, IndexError) as why:
            bad.append("%s: %s" % (name, why))
            continue

        start = bud.Board(grove)
        par, ways, _, proved = bud.search(case["rows"], case["colours"])
        best, _where = bud.biggest(case["rows"], case["colours"])

        for label, got, want in (("proved", proved, case["proved"]),
                                 ("par", par, case["par"]),
                                 ("flowers", start.flowers, case["flowers"]),
                                 ("cocoons", start.shut, case["cocoons"]),
                                 ("bestBurst", best[0], case["bestBurst"]),
                                 ("bestWaves", best[1], case["bestWaves"]),
                                 ("bestFreed", best[2], case["bestFreed"])):
            if got != want:
                bad.append("%s: %s is %r, vectors say %r" % (name, label, got, want))

        if case["par"] > 0:
            if ways != case["ways"]:
                bad.append("%s: ways is %r, vectors say %r" % (name, ways, case["ways"]))

            careless = bud.careless(case["rows"], case["colours"],
                                    case["par"] + bud.DEFAULT_SPARE)
            if careless != case["careless"]:
                bad.append("%s: careless is %r, vectors say %r"
                           % (name, careless, case["careless"]))

        board = bud.Board(grove)
        for nth, beat in enumerate(case.get("beats") or []):
            at = beat["tap"]
            allowed = board.can_tap(at)

            if allowed != beat["allowed"]:
                bad.append("%s: tap %d allowed is %r, vectors say %r"
                           % (name, nth + 1, allowed, beat["allowed"]))

            b, w, f, c = board.tap(at) if allowed else (0, 0, 0, 0)

            for label, got in (("burst", b), ("waves", w), ("freed", f), ("cracked", c),
                               ("flowersLeft", board.flowers), ("shut", board.shut)):
                if got != beat[label]:
                    bad.append("%s: tap %d %s is %r, vectors say %r"
                               % (name, nth + 1, label, got, beat[label]))


    # A vector set that has quietly lost its teeth is worse than none: it passes, it is printed
    # beside the word "ok", and nothing says the rule stopped being checked.
    covers = dict(chain=False, unfinishable=False, nomove=False, tough=False, wood=False,
                  refused=False)
    for case in cases:
        if case["bestWaves"] >= 3:
            covers["chain"] = True
        if case["par"] == 0:
            covers["unfinishable"] = True
        if case["proved"] and case["par"] == 0 and case["flowers"] > 0:
            covers["nomove"] = True
        if case["tough"] > 0:
            covers["tough"] = True
        if case["stones"] > 0:
            covers["wood"] = True
        if any(not beat["allowed"] for beat in (case.get("beats") or [])):
            covers["refused"] = True

    for what, held in covers.items():
        if not held:
            bad.append("no case covering '%s', so nothing here would notice that rule going away"
                       % what)

    for b in bad:
        errors.append("bud vectors: " + b)

    return (f"bud vectors: {len(cases)} case(s), the offline rules agree"
            if not bad else f"bud vectors: {len(bad)} disagreement(s)")


def run_board_vectors():
    """Runs `board-vectors.json` through *both* Python copies of the four-armed-tile rule.

    One rule, three implementations - LevelValidator.CheckDecidableTiles, this file's
    `decidable`, and author.Board.decides - because the Editor, this gate and the authoring
    aid each need it and none can call the others. Three copies drift, and this one already
    did: the topology check these replaced was wrong in two opposite ways and two tools
    disagreed about a whole chapter without anything noticing.

    So it is invariant 9a's answer, for a board rule rather than for money. The C# copy is
    proved against the same file by `BoardVectorTests`; this proves the two Python copies are.
    Failures land in `errors`, so a drift fails this gate rather than printing beside the
    word "ok".

    `author.Board` is reached through `difficulty.board_of`, which is the one place that
    turns shipped rows back into the authoring DSL - so the aid is exercised the way a
    chapter module exercises it rather than through a second transcription.
    """
    import difficulty                       # imported here: it reads boards through author.py

    doc = json.load(open(BOARD_VECTORS, encoding="utf-8"))
    cases = doc.get("cases") or []
    if not cases:
        errors.append("board-vectors.json has no cases")
        return "board vectors: none found"

    def gate_said(messages):
        """The tiles a run of messages complained about, as {'x,y'}.

        Both copies name the tile after " at ": this file writes `1,1` and author.py writes
        a tuple, `(1, 1)`. One regex reads either, which is deliberate - making the two print
        identically would couple an authoring aid's output to a gate's for the sake of a test.
        """
        out = set()
        for m in messages:
            if DECIDES_MARKER not in m:
                continue
            found = re.search(r" at \(?\s*(\d+)\s*,\s*(\d+)\s*\)?", m)
            if found:
                out.add(f"{found.group(1)},{found.group(2)}")
        return out

    bad = []
    for case in cases:
        name = case["name"]
        want = set(case.get("undecided") or [])
        rows = case["rows"]
        level = {"id": "vector_" + name, "width": len(rows[0].split()),
                 "height": len(rows), "rows": rows}

        before_e, before_w = len(errors), len(warnings)
        check_level(level, "vectors")
        mine = gate_said(warnings[before_w:])
        raised = errors[before_e:]
        del errors[before_e:]
        del warnings[before_w:]

        if raised:
            bad.append(f"{name}: the board itself is invalid - {raised[0]}")
            continue
        if mine != want:
            bad.append(f"{name}: content.py said {sorted(mine) or 'nothing'}, "
                       f"vectors say {sorted(want) or 'nothing'}")

        board = difficulty.board_of(level)
        errs, warns = board.check()
        theirs = gate_said(warns)
        if errs:
            bad.append(f"{name}: author.py refuses the board - {errs[0]}")
        elif theirs != want:
            bad.append(f"{name}: author.py said {sorted(theirs) or 'nothing'}, "
                       f"vectors say {sorted(want) or 'nothing'}")

    for b in bad:
        errors.append("board vectors: " + b)

    return (f"board vectors: {len(cases)} case(s), both Python copies agree"
            if not bad else f"board vectors: {len(bad)} disagreement(s)")


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
            # `lesson` was a third key here and is retired — see LevelDefinition.
            for suffix, field in (("name", "nameKey"), ("tagline", "taglineKey")):
                k = level.get(field) or f"level.{lid}.{suffix}"
                if k not in keys:
                    errors.append(f"level '{lid}' missing string '{k}'")

    print(f"{'#':<3}{'level id':<22}{'chapter':<16}{'size':<7}{'par':<5}{'gold':<6}{'silver':<7}"
          f"{'budget':<8}{'hearts':<7}{'critters':<9}{'brittle':<8}"
          f"{'roots':<7}{'crossings':<11}briars")
    for i, s in enumerate(summaries, 1):
        budget = "none" if not s['budget'] else str(s['budget'])
        print(f"{i:<3}{s['id']:<22}{s['chapter']:<16}{str(s['w'])+'x'+str(s['h']):<7}"
              f"{s['par']:<5}{s['gold']:<6}{s['silver']:<7}{budget:<8}"
              f"{s['sources']:<7}{s['lamps']:<9}{s['fragile']:<8}"
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
        print(f"  {'level':<24}{'mode':<9}{'board':<7}{'par':<5}{'3*':<5}{'2*':<5}"
              f"{'budget':<8}{'ways':<6}{'greedy':<8}{'nodes':<8}what it holds")

        for c in others:
            # Everything except the classic glade is proved here now, so every one of them has
            # the same three readings to print - par against the ladder, `ways` for invariant 5d,
            # and `nodes` for what the player's device pays. What differs is the last column,
            # which is whatever that mode's board is made of.
            greedy = c['greedy'] if c.get('greedy', -1) >= 0 else '-'

            if c['mode'] == 'fall':
                held = f"{c['fall_motes']} mote(s), {c['headroom']} headroom, deals {c['deal']}"
            elif c['mode'] == 'keeper':
                held = f"{c['beds']} bed(s), {c['hearts']} heartbed(s), deals {c['deal']}"
            elif c['mode'] == 'bud':
                held = (f"{c['buds']} flower(s) in {c['ready']} colour(s), "
                        f"{c['cocoons']} critter(s) shut in, deals {c['deal']}")
            else:
                held = ""

            print(f"  {c['id']:<24}{c['mode']:<9}{str(c['w']) + 'x' + str(c['h']):<7}"
                  f"{c['par']:<5}{c['gold']:<5}{c['silver']:<5}"
                  f"{str(c['budget'] or 'free'):<8}{c['ways']:<6}{str(greedy):<8}"
                  f"{c['nodes']:<8}{held}")

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
    errors.extend(check_wheel(progression, warnings))

    if shop:
        shelves = ", ".join(f"{n} {shelf}" for shelf, n in sorted(shop["shelves"].items()))
        print(f"\nshop: {shop['products']} product(s) ({shelves}), {shop['goods']} good(s) "
              f"- one gem is worth about {shop['per_gem']} credits across the two ladders")
        print(f"      free play collects about {per_day_credits} credit(s) and "
              f"{per_day_gems} gem(s) a day")

        if shop["vessels"]:
            ladder = ", ".join(f"{cap} for ${cents / 100:.2f}"
                               for cap, cents in shop["vessels"])
            print(f"      heart containers: {ladder} (free cap "
                  f"{(progression.get('hearts') or {}).get('refillCap', 5)})")

        if grove and per_day_credits:
            # `worth` rather than a sum written out here: the home ladder is already inside
            # the pieces total, so adding it again double-counted 49,500 credits, and the
            # companion roster - the largest sink in the game - was missing altogether.
            sinks = grove["worth"]
            print(f"      every credit sink in the game is {sinks} credits, "
                  f"about {sinks // per_day_credits} day(s) of play")

    wheel = bonus_wheel(progression)
    if wheel:
        rim = " ".join(f"{p}%" for p in wheel["percents"])
        per_view = wheel["base"] * wheel["mean"] // 100
        best = wheel["base"] * wheel["top"] // 100
        print()
        print(f"bonus wheel: {rim} - 1 in {wheel['count']} each, mean {wheel['mean']}%")
        print(f"       'win_bonus' authors {wheel['base']} a view and really pays about "
              f"{per_view} (best {best}); at a cap of {wheel['cap']} that is up to about "
              f"{per_view * wheel['cap']} a day on average")
        if per_day_credits:
            print(f"       against about {per_day_credits} credit(s) a day of free play")

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

    gate = progression.get("chapterGate") or {}
    stars_per_level = gate.get("starsPerLevel", 2)
    if stars_per_level < 0:
        stars_per_level = 2
    print()
    if stars_per_level <= 0:
        print("chapter gate: off - every chapter stands open from a new player's first launch")
    else:
        print(f"chapter gate: {stars_per_level} star(s) a level of the chapter behind it")

        # Per mode, because a gate counts the chapter before this one *in the same mode* -
        # the ladders never chain (invariant 20a), so the last chapter of a mode gates
        # nothing and the first of one is always open.
        lanes = {}
        for chapter in sorted(manifest.get("chapters", []), key=lambda c: c.get("order", 0)):
            lanes.setdefault(chapter.get("mode") or "glade", []).append(chapter)

        for mode, lane in sorted(lanes.items()):
            for i, chapter in enumerate(lane[:-1]):
                levels = len(chapter.get("levels") or [])
                if not levels:
                    continue
                print(f"       {lane[i + 1].get('id')} opens at "
                      f"{stars_per_level * levels} of the {levels * 3} stars "
                      f"in {chapter.get('id')}")
        if stars_per_level >= 3:
            print("       that is every star a level can pay - no room for a single "
                  "two-star clear anywhere")

    hearts_block = progression.get("hearts") or {}

    carry = progression.get("continueRun") or {}
    carry_on = carry.get("enabled", -1) != 0
    print()
    if not carry_on:
        print("continue: withdrawn - a lost run ends, and the only way back in is a heart")
    else:
        gems = carry.get("gems", 20)
        if gems < 0:
            gems = 20
        step = carry.get("gemsStep", 0)
        if step < 0:
            step = 0
        turns = carry.get("turns", 15)
        if turns < 0:
            turns = 15
        motes = carry.get("motes", 6)
        if motes < 0:
            motes = 6
        tiles = carry.get("tiles", 6)
        if tiles < 0:
            tiles = 6
        taps = carry.get("taps", 4)
        if taps < 0:
            taps = 4

        # `ink` and `stones` are deliberately absent. They were Lightweave's and Ripplewake's
        # units, both modes are gone, and the fields are kept in the DTO only so a published table
        # still carrying the key does not read as malformed - printing one here would say a mode
        # this build cannot play is still priced.
        print(f"continue: {gems} gem(s) for +{turns} turn(s) on a glade, "
              f"+{motes} mote(s) on a well, +{tiles} tile(s) on a groove, "
              f"+{taps} tap(s) on a grove")

        # What the price means, said in the two units a player actually earns gems in.
        # A price nobody can reach is the failure mode this whole block is content for.
        entry = min((int(x.get("gems") or 0)
                     for x in (progression.get("store") or {}).get("products") or []
                     if int(x.get("gems") or 0) > 0), default=0)

        if per_day_gems:
            line = f"       about {gems / per_day_gems:.1f} day(s) of free play"
            if entry:
                line += f", or {gems / entry:.0%} of the {entry}-gem entry rung"
            print(line)
        if step:
            print(f"       and {step} more each time, so a third continue on one run "
                  f"costs {gems + step * 2}")
        else:
            print("       flat, so a run may be continued as often as the player can pay")

        print("       a continued run is already past the two-star line, so it can only "
              "ever score one - the offer sells a finish, never a grade")

    rescue_hearts = hearts_block.get("rescueHearts", 2)
    if rescue_hearts < 0:
        rescue_hearts = 2

    print()
    if rescue_hearts == 0:
        print("heart rescue: withdrawn - a player out of hearts waits, watches a video, "
              "or leaves")
    else:
        rescue_gems = hearts_block.get("rescueGems", 20)
        if rescue_gems < 0:
            rescue_gems = 20

        print(f"heart rescue: {rescue_gems} gem(s) for +{rescue_hearts} heart(s) on the "
              f"defeat panel")
        print("       a fresh attempt graded like any other, never a continue - hearts pay "
              "nothing, so this buys sooner and nothing else")

        entry = min((int(x.get("gems") or 0)
                     for x in (progression.get("store") or {}).get("products") or []
                     if int(x.get("gems") or 0) > 0), default=0)

        if per_day_gems:
            line = f"       about {rescue_gems / per_day_gems:.1f} day(s) of free play"
            if entry:
                line += f", or {rescue_gems / entry:.0%} of the {entry}-gem entry rung"
            print(line)

        # The shop's *entry* heart pack - its smallest - rather than its best rate. A bulk
        # pack is a volume discount and every rescue will always be dearer per heart than
        # one, so comparing against the best would fire on every honest tuning and become a
        # line nobody reads. The entry pack is the like-for-like: it is what the same player
        # would otherwise buy for the same reason, and beating it is not required - matching
        # it is. Cross-multiplied so the comparison is exact integers, the rule this project
        # keeps for anything a player counts towards.
        entry_pack = None
        for good in (progression.get("store") or {}).get("goods") or []:
            if good.get("kind") != "hearts":
                continue
            amount, gems = int(good.get("amount") or 0), int(good.get("gems") or 0)
            if amount <= 0 or gems <= 0:
                continue
            if entry_pack is None or amount < entry_pack[1]:
                entry_pack = (gems, amount)

        if entry_pack and rescue_gems * entry_pack[1] > entry_pack[0] * rescue_hearts:
            print(f"       WARNING: dearer per heart than the shop's smallest pack "
                  f"({entry_pack[0]} for {entry_pack[1]}) - a premium charged at the moment "
                  "a player cannot compare")

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
    print(run_board_vectors())
    print(run_fall_vectors())
    print(run_keeper_vectors())
    print(run_bud_vectors())

    for w in warnings:
        print("WARN  " + w)
    for e in errors:
        print("ERROR " + e)
    print(f"\n{len(summaries)} level(s): {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


sys.exit(main())
