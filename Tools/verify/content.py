"""End-to-end check of the shipped content, mirroring LevelValidator.cs
and ChapterMapValidator.cs."""
import json
import re, math, os, sys
from collections import deque

import fall                                  # Lightfall's rules, mirrored - see fall.py
import proto                                 # the prototype modes, mirrored - see proto.py

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


MODE_BLOCKS = ("fall", "prism", "siege")

#: Block names that named a mode this build no longer has. Refused by name rather than ignored,
#: for the duskcap's reason (invariant 5f): JsonUtility drops an unknown field without a word, so
#: a chapter written for a build that is gone would validate, index and ship as a level nobody
#: Block names that named a mode this build no longer has. Refused **by name** rather than
#: ignored, for the duskcap's reason (invariant 5f): JsonUtility drops an unknown field without
#: a word, so a chapter body still carrying one would index, derive a plausible glade and ship
#: as something nobody authored. Ten modes have been withdrawn after play now - Groovekeeper,
#: four of the five prototypes that took its slot, Deep Orbit and Moonwake, then Toppleglen and
#: Nova Raid, then the Iron Quarry, and then Kindlewake, whose slot Prismvale has.
RETIRED_BLOCKS = ("keeper", "nectar", "ribbon", "fling", "warren", "orbit", "moonwake",
                  "topple", "nova", "quarry", "kindle", "bud", "march", "ember")

#: Mirrors `ProtoValidator`. About the *player's* device: par is resolved lazily when somebody
#: opens the level, so this is the beat between tapping a node and the board arriving.
PROTO_NODE_WARNING, PROTO_NODE_CEILING = 30_000, 90_000

#: Above this many shortest answers the board is not deciding much (invariant 5d).
PROTO_TOO_MANY_WAYS = 300

#: Mirrors `PrismValidator.DealtLitPercent`. Invariant 5g: a board dealt with most of its veins
#: already running is a board that starts half done, and nothing else notices.
PRISM_DEALT_LIT_PERCENT = 35

#: Mirrors `PrismValidator.TooManyIdle`. A lantern with no gem at all beside it reads as a route
#: that is not there, and a lantern is the brightest thing on the field.
PRISM_TOO_MANY_IDLE = 1


def check_fall(lid, chapter_id, level, block):
    """Everything a Lightfall well has to prove, mirroring `FallValidator`.

    The one thing that cannot be checked by reading the file is whether the well can be
    *emptied*, so most of this is a search. Every failure below looks like a perfectly
    authored board in the JSON, which is the whole reason the gate exists.
    """
    empty = dict(id=lid, chapter=chapter_id, w=0, h=0, par=0, budget=0,
                 gold=0, silver=0, lamps=0, sources=0, fragile=0, bound=0,
                 crossings=0, briars=0, mode='fall',
                 ways=0, greedy=-1, nodes=0, fall_motes=0, headroom=0, deal='',
                 lenses=0, whorls=0, fused=0, kindled=0, reach=0, aim=0, charged=0)

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

    # Glass is only ever cleared by a burst beside it, so a well of nothing but lenses can
    # never lose one. The search proves it unwinnable but only after spending the whole budget,
    # and in words about a search rather than about the board.
    if well.lenses and well.lenses + well.whorls == well.motes:
        errors.append("%s: every one of this well's %d cells is glass, and glass is only ever "
                      "cleared by a burst beside it - with no mote to cook there can never be a "
                      "burst" % (lid, well.lenses))

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

    # Invariant 5d for the lens. Glass exists to make light travel; glass whose every beam lands
    # on the cell next door has done the neighbour wash's job with an extra object on the board.
    # A reading of the opening position rather than a proof, so it is said rather than refused.
    aim, reach = fall.blast(cells, ww, hh)
    if well.lenses and aim < 1:
        warnings.append("%s: this well stands %d lens(es) and not one of them is pointing "
                        "sideways at anything - both shots would leave the well the moment it "
                        "set off, so three drops of charging would buy nothing and the board "
                        "would play the same without the glass" % (lid, well.lenses))

    # Invariant 5d for the whorl, and the strict reading of it. A whorl that never draws in a
    # *pair* has moved one mote sideways; a pair whose union never reaches white has tidied the
    # board without deciding anything. Measured over every shortest solution rather than the
    # first one the search happens to reach, or an author would be tuning against a coin toss.
    #
    # Said rather than refused, for `aim`'s reason: the well collapses under every chain, so a
    # whorl merges from wherever it has fallen to, and the first board of a chapter may carry one
    # as scenery while the verb is being taught.
    fused, kindled = fall.best_merges(cells, ww, hh, deal, par)

    if well.whorls and not kindled:
        warnings.append("%s: this well stands %d whorl(s) and no shortest solution ever merges a "
                        "pair that reaches white, so the arrangement is not deciding anything - "
                        "the board would play the same with the whorls taken out of it"
                        % (lid, well.whorls))

    # And the exact half of it, which the lens has no equivalent of: gravity never moves a whorl
    # sideways, so a whorl authored against a wall has one side for its whole life and can never
    # merge a pair whatever the well collapses into.
    walled = sum(1 for at, cell in enumerate(cells)
                 if fall.is_whorl(cell) and at % ww in (0, ww - 1))
    if walled:
        warnings.append("%s: %d of this well's %d whorl(s) stand against a wall, and gravity "
                        "never moves a whorl sideways - so those can only ever draw in one mote "
                        "and can never merge a pair" % (lid, walled, well.whorls))

    return dict(id=lid, chapter=chapter_id, w=ww, h=hh, par=par, budget=budget,
                gold=gold, silver=silver, lamps=0, sources=0, fragile=0, bound=0,
                crossings=0, briars=0, mode='fall',
                ways=ways, greedy=greedy, nodes=nodes,
                fall_motes=well.motes, headroom=well.headroom, deal=block.get('motes'),
                lenses=well.lenses, whorls=well.whorls, reach=reach, aim=aim,
                fused=fused, kindled=kindled,
                charged=sum(1 for g in well.glass if g))


def check_proto(mode, lid, chapter_id, level, block):
    """Everything a prototype board has to prove, mirroring `ProtoValidator`.

    One function however many modes, because they share a shape rather than a rule: each authors
    a whole board, each searches it for par, and every graded number derives from the answer. What
    differs is the handful of questions only one mode's own rules can ask, and those are at the
    bottom. It carried five while five prototypes were being judged and did not move when four of
    them were withdrawn.

    The one thing that cannot be checked by reading the file is whether the board can be
    *finished*, so most of this is a search. Every failure below looks like a perfectly authored
    board in the JSON, which is the whole reason the gate exists.
    """
    empty = dict(id=lid, chapter=chapter_id, w=0, h=0, par=0, budget=0,
                 gold=0, silver=0, lamps=0, sources=0, fragile=0, bound=0,
                 crossings=0, briars=0, mode=mode,
                 ways=0, greedy=-1, nodes=0, goals=0, deal='')

    w, h = block.get('width') or 0, block.get('height') or 0

    try:
        grid, board = proto.build(mode, block)
    except ValueError as bad:
        errors.append("%s: %s" % (lid, bad))
        return empty

    if grid.w != w or grid.h != h:
        errors.append("%s: declares %dx%d and writes %dx%d" % (lid, w, h, grid.w, grid.h))
        return empty

    if getattr(board, 'stirred', False):
        errors.append("%s: this board is not authored at rest - it settles before the player "
                      "touches it, so the board proved is not the board that opens" % lid)
        return empty

    if board.won():
        errors.append("%s: this board is finished before the player touches it" % lid)
        return empty

    if not board.any_move():
        errors.append("%s: this board has no legal move on it, so the run is over before it "
                      "begins" % lid)
        return empty

    problem = MODE_RULES[mode](lid, grid, board)
    if problem:
        errors.append("%s: %s" % (lid, problem))
        return empty

    par, ways, nodes, proved = proto.search(board)

    if not proved:
        errors.append("%s: this board could not be proved inside %d positions (it looked at %d) "
                      "or within %d moves - it may be unsolvable, or simply too big to prove, "
                      "and either way the player's device runs the same search to work out par"
                      % (lid, proto.NODE_BUDGET, nodes, proto.MAX_DEPTH))
        return empty

    if par < 1:
        errors.append("%s: no sequence of moves finishes this board, so nobody can clear it" % lid)
        return empty

    budget_h = factor_of(level, 'budgetFactor', 160)
    gold_h = factor_of(level, 'goldFactor', proto.GOLD_HUNDREDTHS)
    silver_h = factor_of(level, 'silverFactor', proto.SILVER_HUNDREDTHS)

    # Room above par is a count of wasted moves rather than a multiple of par: every wrong move
    # in every one of these modes is permanent *and* makes the board worse, so a mistake costs
    # about the same wherever it happens (invariant 26e). Mirrors ProtoLevelRules.DefaultSpare.
    spare = block.get('spare') or proto.DEFAULT_SPARE
    budget = (par + spare) if budget_h > 0 else 0

    if budget_h > 0 and budget_h != 160:
        errors.append("%s: this board authors budgetFactor %.2f, which does nothing - room above "
                      "par here is 'spare', counted in moves. Use 'spare', or a negative "
                      "budgetFactor if it is meant to be unlosable" % (lid, budget_h / 100.0))

    gold, silver = proto.over(par, gold_h), proto.over(par, silver_h)

    if gold_h >= silver_h:
        errors.append("%s: goldFactor and silverFactor leave the two-star band empty" % lid)
    elif budget and budget <= gold:
        errors.append("%s: the allowance is at or under the three-star line, so every surviving "
                      "run would be a three-star run" % lid)
    elif budget and budget <= silver:
        warnings.append("%s: the allowance is inside the two-star band, so one star can never be "
                        "scored" % lid)

    if nodes > PROTO_NODE_CEILING:
        errors.append("%s: proving this board took %d positions, above the %d a level may cost - "
                      "the player's device runs the same search when somebody opens the level"
                      % (lid, nodes, PROTO_NODE_CEILING))
    elif nodes > PROTO_NODE_WARNING:
        warnings.append("%s: proving this board took %d positions against the %d a level is "
                        "expected to cost (the refusal is at %d)"
                        % (lid, nodes, PROTO_NODE_WARNING, PROTO_NODE_CEILING))

    if ways > PROTO_TOO_MANY_WAYS:
        warnings.append("%s: %d different runs of %d moves finish this board, so almost any play "
                        "wins and the arrangement is deciding nothing" % (lid, ways, par))

    greedy = proto.careless(proto.build(mode, block)[1], budget or (par + proto.DEFAULT_SPARE))
    if par > 2 and greedy > 0:
        warnings.append("%s: a player who never looks ahead finishes this board in %d moves "
                        "against an allowance of %s"
                        % (lid, greedy, budget if budget else "none at all"))

    out = dict(id=lid, chapter=chapter_id, w=grid.w, h=grid.h, par=par,
               budget=budget, gold=gold, silver=silver, lamps=0, sources=0, fragile=0,
               bound=0, crossings=0, briars=0, mode=mode,
               ways=ways, greedy=greedy, nodes=nodes,
               goals=board.goals() if hasattr(board, 'goals') else 0,
               deal='')

    # The handful of numbers only this mode's own rules can give, carried out so the printed
    # table can show them beside par. `chain` is the mode's payoff measured and `shatter` is
    # what separates a prism from a charge with a longer reach.
    if mode == 'prism':
        import prism as rules
        out.update(rules.readings(rules.Layout(grid, 0), budget))

    return out


def prism_rules(lid, grid, board):
    """A field of gems: a critter light can reach, a lantern that is doing something, and colour
    that decides which.

    Mirrors `PrismMode.Compose` and `PrismValidator.Inspect`. Everything here is a reading the
    search cannot give in words an author could act on - "no sequence of swaps finishes this
    board" is true and useless, where "the critter at row 3 column 5 stands in a run of gems no
    lantern is touching" is the sentence that names the thing to move.
    """
    import prism as rules

    b = board.board
    lay = b.layout

    if lay.fault:
        return lay.fault

    if b.stirred():
        return ("a vein on this board is already touching a sleeping critter, so it would wake "
                "before anybody had moved a gem - a board is authored dark")

    if not b.any_move():
        return ("no two touching gems on this board are different colours, so there is no swap "
                "to make and the run is over before it begins")

    # Certain, exact, and it names the cell. This is the one thing the search genuinely cannot
    # say: it answers "unsolvable" and points at nothing. Which cells hold gems never changes,
    # so no arrangement can undo it.
    stuck = lay.stranded()
    if stuck:
        cell = stuck[0]
        return ("the critter at row %d column %d stands in a run of gems that no lantern is "
                "touching - or against no gem at all - so nothing that happens anywhere on this "
                "board could ever wake it" % (cell // lay.w, cell % lay.w))

    read = rules.readings(lay)

    if read['hues'] > 1 and read['used'] < 2:
        warnings.append("%s: this board stands %d lantern colours and no shortest run of it ever "
                        "wakes a critter with more than one of them, so the rest are decoration "
                        "and the colour decided nothing" % (lid, read['hues']))

    if read['gems'] and read['dealt'] * 100 > read['gems'] * PRISM_DEALT_LIT_PERCENT:
        warnings.append("%s: %d of this board's %d gems are already lit as it is dealt, which is "
                        "over %d%% of it - so the player is handed a board somebody else has half "
                        "finished" % (lid, read['dealt'], read['gems'], PRISM_DEALT_LIT_PERCENT))

    if not read['bare']:
        warnings.append("%s: this board holds no bare ground, so nothing shapes a vein and every "
                        "gem of a colour can reach every other one" % lid)

    if read['idle'] > PRISM_TOO_MANY_IDLE:
        warnings.append("%s: %d lanterns on this board have no gem at all standing against them, "
                        "so no vein could ever start there" % (lid, read['idle']))

    return None


MODE_RULES = {
    'prism': prism_rules,
}


#: Every cue a chapter body may write. Mirrors `StoryScript.TryReadCue`.
STORY_CUES = ("intro", "freed", "forged", "fired", "kill", "tight", "won", "lost")

#: Everyone who may speak. Mirrors `StoryCast.All`, and each name is also a folder of frames
#: under `Art/March/`, which is why a typo has to be an error rather than a silent drop: an
#: unknown speaker is a portrait that does not load, and a missing sprite draws as a **white
#: rectangle** rather than as nothing (invariant 7b).
STORY_CAST = ("bolt", "collector", "mon1", "mon2", "mon3")


def check_story(lid, story, keys):
    """Every line a level authors: a cue that exists, a speaker that exists, a key that resolves.

    **This is the gate that replaces deriving the key.** A level's own name and tagline are a
    pure function of its id (invariant 5a), so nothing can mistype one; a line of dialogue is
    authored, because nothing ever needs to name a line it has not read. What that costs is the
    protection a convention gives, and it is bought back here - and bought back *better*, since
    resolving the key against `loc/en.json` also catches one that is correctly shaped and simply
    absent. `Tools/verify/loc.py` cannot see these at all (they are unreachable from source), so
    if this is not checked here it is not checked anywhere.

    The runtime does the opposite thing on purpose: `ContentMapper.ReadStory` drops a malformed
    line rather than refusing the level, because losing a sentence is better than losing the
    board it was written for. That asymmetry is only safe while this exists.
    """
    if not story:
        return

    beats = story.get("beats")
    if not beats:
        errors.append(f"level '{lid}' carries a story block with no beats in it")
        return

    for i, beat in enumerate(beats):
        cue = (beat or {}).get("cue")
        if cue not in STORY_CUES:
            errors.append(f"level '{lid}' story beat {i} has cue '{cue}', which is not one of "
                          f"{', '.join(STORY_CUES)}")
            continue

        lines = beat.get("lines") or []
        if not lines:
            errors.append(f"level '{lid}' story beat {i} ('{cue}') has no lines")
            continue

        for j, line in enumerate(lines):
            who = (line or {}).get("who")
            key = (line or {}).get("key")

            if who not in STORY_CAST:
                errors.append(f"level '{lid}' story {cue}[{j}] is spoken by '{who}', who is not "
                              f"in the cast ({', '.join(STORY_CAST)})")
            if not key:
                errors.append(f"level '{lid}' story {cue}[{j}] has no loc key")
            elif key not in keys:
                errors.append(f"level '{lid}' story {cue}[{j}] says '{key}', which is not in "
                              "loc/en.json")


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


#: `SiegeValidator.ShortestPar`. A siege shorter than this is over before the fuel has faded
#: once, so nothing the mode is built on ever gets to bite.
SIEGE_SHORTEST_PAR = 6


def check_siege(lid, chapter_id, level, block):
    """A siege: a field, a ward line and what is coming down the hill at it.

    **The one mode here whose par is not a search.** Every other check in this file proves a board
    by walking its state graph; raiders walk while nobody is touching the field and the field
    refills, so there is no graph. What is proved instead is exactly what is provable about the
    file - the layout's own refusals, the arithmetic par, and the readings an author can act on -
    and `siege.par` is held to `SiegeTuning.Par` by being the same three lines of arithmetic.

    Mirrors `SiegeValidator`. Every failure below looks like a perfectly authored level in the
    JSON, which is the whole reason the gate exists.
    """
    import siege as rules

    empty = dict(id=lid, chapter=chapter_id, w=0, h=0, par=0, budget=0,
                 gold=0, silver=0, lamps=0, sources=0, fragile=0, bound=0,
                 crossings=0, briars=0, mode='siege',
                 ways=0, greedy=-1, nodes=0, goals=0, cogs=0, deal='',
                 # **`siege` is carried even on the refusal path.** The report loop reads it
                 # unconditionally for any level of this mode, so a level refused before its
                 # layout existed used to crash the gate on the way to printing the error it
                 # had just found. An empty reading prints as a level that sends nothing,
                 # which is exactly what a refused one is.
                 siege=dict(raiders=0, waves=0, brutes=0, bulwarks=0, colours=0, wards=0,
                            boss='', kind='', spell='', cogs=0, drops=0, threat=0, swap=0,
                            charms='', sparks=0))

    w, h = block.get('width') or 0, block.get('height') or 0

    # **The retired cog cell is refused by name rather than by falling through** (invariant 5f,
    # the duskcap's rule). A `*` was a cog standing on the field; cogs are dropped by felled
    # raiders now, so a body carrying one was authored for a build that is gone. `proto.Grid`
    # would refuse it anyway as an unknown cell - what a named refusal buys is that whoever meets
    # it is told why rather than left to guess which of five letters is wrong.
    for row, line in enumerate(block.get('rows') or []):
        if rules.RETIRED_COG not in (line or ''):
            continue

        errors.append("%s: row %d stands a '%s' on the field. A cog is no longer a cell - it is "
                      "dropped by a raider the line kills and lies on the hill until it is "
                      "tapped, so a field authors gems and nothing else. Drop the character and "
                      "set 'cogs' to a drop rate per hundred kills"
                      % (lid, row + 1, rules.RETIRED_COG))
        return empty

    try:
        grid = proto.Grid(block.get('rows') or [], w, h, rules.CELLS)
    except ValueError as bad:
        errors.append("%s: %s" % (lid, bad))
        return empty

    endless_block = block.get('endless') or {}

    layout = rules.Layout(grid, block.get('gems'), block.get('wards'), block.get('waves'),
                          block.get('boss'), block.get('cogs') or 0,
                          endless=bool(endless_block.get('goldWave')),
                          tough=block.get('tough') or 0,
                          charms=block.get('charms') or '')

    if layout.fault:
        errors.append("%s: %s" % (lid, layout.fault))
        return empty

    # **A lane whose waves never stop is a different level in three ways and the same in every
    # other**, which is what makes it worth checking here rather than somewhere of its own: the
    # field, the deal and the line are proved by exactly the code above, and what is left is the
    # ramp. See `check_endless`.
    endless = endless_block
    if endless.get('goldWave'):
        return check_endless(lid, chapter_id, level, block, grid, layout, endless)

    if block.get('endless'):
        errors.append("%s: this siege authors an 'endless' block with no goldWave; a lane whose "
                      "waves never stop still has to say how far a three-star run reaches, "
                      "because nothing can derive it" % lid)

    par = rules.par(layout)
    read = rules.readings(layout)

    budget_h = factor_of(level, 'budgetFactor', 160)
    gold_h = factor_of(level, 'goldFactor', proto.GOLD_HUNDREDTHS)
    silver_h = factor_of(level, 'silverFactor', proto.SILVER_HUNDREDTHS)

    # A siege is lost when the last ward falls, so a move allowance would be a second fail state
    # and its meter would count down to an ending that never happens.
    if budget_h > 0:
        errors.append("%s: this siege authors budgetFactor %.2f. A siege is lost when the last "
                      "ward falls, so an allowance would be a second fail state - author "
                      "budgetFactor -1" % (lid, budget_h / 100.0))

    if block.get('spare'):
        errors.append("%s: this siege authors 'spare', which does nothing here - there is no move "
                      "allowance to be spared from" % lid)

    if gold_h >= silver_h:
        errors.append("%s: goldFactor and silverFactor leave the two-star band empty" % lid)

    gold, silver = proto.over(par, gold_h), proto.over(par, silver_h)

    if par < SIEGE_SHORTEST_PAR:
        warnings.append("%s: this siege is over in %d matches at best, under the %d it takes for "
                        "a ward's fuel to fade and be wanted again" % (lid, par,
                                                                       SIEGE_SHORTEST_PAR))

    if read['waves'] < 2:
        warnings.append("%s: this siege sends one wave, so it never lets up and never comes back"
                        % lid)

    for ward in layout.wards:
        if any(ward == t.lower() for wave in layout.waves for t in wave):
            continue
        warnings.append("%s: nothing coming down this hill wears '%s', so that ward's bolts are "
                        "always worth half and the gems that feed it are worth half with them"
                        % (lid, ward))

    if read['colours'] < 2:
        warnings.append("%s: everything coming down this hill wears one colour, so which ward to "
                        "feed is not a question" % lid)

    # **Invariant 5d asked of the cogs, and what it asks moved with them.** It used to warn about
    # a line too short for "which colour takes this" to be a question; a cog is dropped by a felled
    # raider now and pays the ward whose colour it wore, so the decision is *when to reach for it*
    # and the thing that would quietly make it decide nothing is a hill so short that no drop rate
    # produces a cog at all.
    if layout.cogs and read['drops'] < 1:
        warnings.append("%s: this hill drops a cog %d times in a hundred kills and sends %d "
                        "raiders, so a run expects fewer than one - the mechanic, its art and its "
                        "lesson all ship and most players never see one"
                        % (lid, layout.cogs, layout.raiders))

    # **Invariant 5d asked of the charms, and it is the cog's question with a different
    # denominator.** A charm is dealt into a *refill* rather than dropped by a kill, so what
    # decides whether a player ever meets one is how many gems the level clears - which is par
    # times what a match takes. A rung that authors a charm its own length can never deal ships the
    # rule, its picture and its lesson to somebody who will never see any of them, which is
    # precisely what happened to two whole raider kinds (invariant 40a).
    # **One window, because that is what the mechanism guarantees.** A charm falls once a window at
    # a place inside it the roll picks, so a run that clears a whole window's worth of gems is
    # dealt at least one and a shorter run can be dealt none - which is the failure this is for,
    # and it is not hypothetical: the first version of this was a *rate*, and `s01_stonewatch`, the
    # rung that introduces the prism, put its first charm at deal 351 against a run that ends at
    # about 324. Dealt none, at every rhythm, for ever (invariant 37ci).
    #
    # **A floor and not an expectation.** What a run of these levels really meets is about twice
    # this, because the gap inside a window averages half of it - measured, the warned rungs deal
    # two to four. A gate may only refuse on the guarantee.
    if layout.charms and read['sparks'] < 1:
        warnings.append("%s: this field deals charms '%s' and clears about %d gems over a run at "
                        "par %d, which is under the %d-gem window a charm falls in - so a run can "
                        "be dealt none, and the mechanic, its art and its lesson all ship to "
                        "somebody who never sees one"
                        % (lid, read['charms'],
                           par * rules.MATCH_GEMS_TENTHS // 10, par,
                           rules.CHARM_WITHIN))

    # Certain. A stormglass throws at every raider standing on the hill and at nothing else, so a
    # field dealing one onto a hill nobody has to fight is a payoff that rejects nothing (5d) - and
    # the same is true of a level so short the charm arrives after the last wave is down.
    if rules.STORM in (layout.charms or ()) and layout.raiders < 4:
        errors.append("%s: this field deals a stormglass and sends %d raider(s). A stormglass is "
                      "worth what is standing on the hill when it goes, so on a hill this short "
                      "there is nothing for it to be worth" % (lid, layout.raiders))

    if not read['threat']:
        warnings.append("%s: no wave here holds enough raiders to bring a ward down even if every "
                        "one of them reached the line, so this siege cannot be lost" % lid)

    # Certain, and only this gate can see it: the board reshuffles rather than locking, so a field
    # authored with no opening swap costs the player the first second of a run whose clock is
    # already going.
    if not read['swap']:
        warnings.append("%s: no swap on this field lines anything up as it is dealt, so the board "
                        "deals itself again before the player has moved" % lid)

    return dict(id=lid, chapter=chapter_id, w=grid.w, h=grid.h, par=par,
                budget=0, gold=gold, silver=silver, lamps=0, sources=0, fragile=0,
                bound=0, crossings=0, briars=0, mode='siege',
                ways=0, greedy=-1, nodes=0, goals=layout.raiders,
                cogs=layout.cogs,
                deal=layout.deal, siege=read)


def check_endless(lid, chapter_id, level, block, grid, layout, endless):
    """A siege whose waves never stop.

    **Three things differ and everything else is the ordinary siege check above.** It authors no
    waves and no boss (the muster is a rule, `SiegeEndless`); it is graded on a count that
    *climbs*, so par is the wave a three-star run reaches and the two-star line is below it rather
    than above; and it can never be won, so there is nothing to search and nothing to prove about a
    solution. What is provable is that the field is playable, the line is legal, the ramp sends
    nothing the line cannot answer, and the two star lines are the right way round.
    """
    import siege as rules

    empty = dict(id=lid, chapter=chapter_id, w=grid.w, h=grid.h, par=0, budget=0,
                 gold=0, silver=0, lamps=0, sources=0, fragile=0, bound=0,
                 crossings=0, briars=0, mode='siege',
                 ways=0, greedy=-1, nodes=0, goals=0, cogs=0, deal='',
                 # **`siege` is carried even on the refusal path.** The report loop reads it
                 # unconditionally for any level of this mode, so a level refused before its
                 # layout existed used to crash the gate on the way to printing the error it
                 # had just found. An empty reading prints as a level that sends nothing,
                 # which is exactly what a refused one is.
                 siege=dict(raiders=0, waves=0, brutes=0, bulwarks=0, colours=0, wards=0,
                            boss='', kind='', spell='', cogs=0, drops=0, threat=0, swap=0,
                            charms='', sparks=0))

    if block.get('waves') or block.get('boss'):
        errors.append("%s: this siege authors both an endless ramp and its own waves. A lane whose "
                      "waves never stop has no list of them - drop 'waves' and 'boss', or drop "
                      "'endless'" % lid)
        return empty

    if factor_of(level, 'budgetFactor', 160) > 0:
        errors.append("%s: this siege authors a move budget. A siege is lost when the last ward "
                      "falls - author budgetFactor -1" % lid)
        return empty

    gold = int(endless.get('goldWave') or 0)
    silver_factor = float(endless.get('silverFactor') or 0.0)

    if gold < 1:
        errors.append("%s: an endless lane needs a goldWave of at least 1" % lid)
        return empty

    if not (0.0 < silver_factor < 1.0):
        errors.append("%s: this endless lane's silverFactor is %.2f; it is the fraction of the "
                      "three-star wave a two-star run reaches, so it lies between 0 and 1 - and "
                      "on a climbing level three stars asks for *more* than two, which is the one "
                      "place in this game the ordering inverts"
                      % (lid, silver_factor))
        return empty

    silver = max(1, -(-int(round(gold * silver_factor * 100)) // 100))

    if silver >= gold:
        errors.append("%s: this endless lane's two-star wave (%d) is not below its three-star "
                      "wave (%d), so a whole band of the ladder is unreachable"
                      % (lid, silver, gold))
        return empty

    # **The ramp is walked rather than trusted**, as far as the second time every boss has been
    # met - which is the point the schedule starts repeating (`SiegeEndless.PairsAfter`). What is
    # being asked is invariant 5d's question of a hill nobody authored: does the line have an
    # answer to everything that walks down it.
    colours = list(layout.deal)
    seen = set()

    for wave in range(1, rules.PAIRS_AFTER * 3 + 1):
        for colour, kind in rules.endless_wave(colours, layout.seed, wave):
            seen.add(colour)

            if colour in layout.wards:
                continue

            errors.append("%s: wave %d of this endless lane sends a '%s' %s and no ward on the "
                          "line carries '%s'" % (lid, wave, colour, kind, colour))
            return empty

    if len(seen) < 2:
        warnings.append("%s: everything this endless lane sends wears one colour, so which ward "
                        "to feed is not a question" % lid)

    read = rules.readings(layout)
    read['endless'] = gold
    read['waves'] = 0
    read['raiders'] = 0

    if not read['swap']:
        warnings.append("%s: no swap on this field lines anything up as it is dealt, so the board "
                        "deals itself again before the player has moved" % lid)

    return dict(id=lid, chapter=chapter_id, w=grid.w, h=grid.h, par=gold,
                budget=0, gold=gold, silver=silver, lamps=0, sources=0, fragile=0,
                bound=0, crossings=0, briars=0, mode='siege',
                ways=0, greedy=-1, nodes=0, goals=0,
                cogs=layout.cogs, deal=layout.deal, siege=read)


def check_level(level, chapter_id):
    """A level is a glade, or it carries exactly one mode block.

    Mirrors ContentMapper: the modes are asked which one claims the level rather than this
    growing a branch each time one is added. Nothing outside the classic mode has authored
    difficulty, so there is nothing here to prove about them beyond the block being sane -
    the real checks live in each LevelMode.Validate and run in the Editor.
    """
    lid = level.get('id', '?')

    stale = [b for b in RETIRED_BLOCKS if level.get(b)]
    for block_name in stale:
        errors.append("%s: carries a '%s' block, which named a mode this build no longer has. "
                      "JsonUtility drops an unknown field without a word, so this level would "
                      "index and ship as something nobody authored" % (lid, block_name))

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

        # Every prototype mode shares one check, because they share a level shape: a grid, a
        # deal and a slack, all of it searched for par. See check_proto.
        if claimed[0] in MODE_RULES:
            return check_proto(claimed[0], lid, chapter_id, level, block)

        # Thornwatch is the third, and the one that is *not* a search. See check_siege.
        if claimed[0] == 'siege':
            return check_siege(lid, chapter_id, level, block)

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
# TEASER_HEADROOM mirrors ChapterMap.TeaserHeadroom, which was raised from 500 to 700 when
# the mode switcher arrived under the banner - this copy was left behind, so the offline
# check was the looser of the two and could pass a marker the Editor would flag.
TEASER_GAP, TEASER_HEADROOM, TEASER_X = 0.22, 700.0, 0.66
# ChapterMap.Crown*/Body*: what a cleared glade draws above its disc (record and rank), and
# what any perch - a glade's or the marker's - hangs below and above its own centre. The disc
# distance above is not what collides; these are.
CROWN_HALF_WIDTH, CROWN_BOTTOM, CROWN_TOP = 204.0, 106.0, 302.0
BODY_HALF_WIDTH, BODY_BELOW, BODY_ABOVE = 180.0, 227.0, 118.0


def check_chapter_map(chapter, cid, ordered):
    """Placement checks that need the whole chapter: ChapterMapValidator.cs."""
    strips = len(chapter.get("mapStrips") or ["map1_strip0"])
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

    # Mirrors ChapterMap.Overshadows: a rectangle test, body over crown, because the
    # standing mark is four times wider than it is tall and a radius that cleared its
    # corners would refuse every alternating layout that ships.
    def overshadows(body, crown):
        dx = abs(body[0] - crown[0]) * MAP_WIDTH
        if dx >= BODY_HALF_WIDTH + CROWN_HALF_WIDTH:
            return False
        dy = (body[1] - crown[1]) * height
        return dy - BODY_BELOW < CROWN_TOP and dy + BODY_ABOVE > CROWN_BOTTOM

    for lid, p in pts:
        for other, q in pts:
            if other != lid and overshadows(q, p):
                warnings.append(f"chapter '{cid}': '{other}' stands on the standing mark above "
                                f"'{lid}'; put them on opposite sides of the map, or further apart")
        if overshadows(teaser, p):
            warnings.append(f"chapter '{cid}': the end-of-chapter marker at ({teaser[0]:.2f}, "
                            f"{teaser[1]:.2f}) stands on the standing mark above '{lid}'; end the "
                            "chapter on the other side of the map from the marker")


SLOT_KINDS = ("ground", "hearth", "structure", "bed", "path", "edge", "canopy")


# The five creatures the grove used to author, and the companion each was rewritten to.
# The mirror of GroveResidents.Retired - a save holding an old id has its placement
# rewritten at load, for ever, so a target that leaves the roster empties somebody's slot.
RESIDENT_PREFIX = "friend_"

#: The most a piece may stand on, a side - GroveFootprint.MaxSide.
MAX_FOOTPRINT = 4

#: One hit-mask cell every this many art pixels - GroveHitMask.CellPx.
HIT_CELL = 16

#: GroveFloor.TileWidth and HomesteadScreen.PieceScale, for the overhang reading below.
TILE_WIDTH = 220.0
PIECE_SCALE = 1.15

def painted_width(piece):
    """How wide a piece's *paint* is, in art pixels, at its widest facing.

    Read off the hit mask rather than the art's rectangle, and that distinction is the whole
    reason this reading is trustworthy. A piece with four facings is rendered into one square
    box sized to the envelope its rotations sweep — so a fence, which stands at the edge of
    its tile rather than in the middle, gets a box twice as wide as the fence with the rest
    transparent. Measured by the box, every fence in the catalogue reported as painting twice
    the ground it holds; measured by the ink, they paint 0.84 of it, which is correct and
    always was.
    """
    w, h = int(piece.get("w") or 0), int(piece.get("h") or 0)
    if w <= 0 or h <= 0:
        return 0

    cols = -(-w // HIT_CELL)
    rows = -(-h // HIT_CELL)
    masks = piece.get("hits") or ([piece.get("hit")] if piece.get("hit") else [])

    widest = 0
    for hexmask in masks:
        if not hexmask:
            continue
        bits = "".join(bin(int(ch, 16))[2:].zfill(4) for ch in hexmask)
        lo, hi = cols, -1
        for r in range(rows):
            row = bits[r * cols:(r + 1) * cols]
            for c, bit in enumerate(row):
                if bit == "1":
                    if c < lo:
                        lo = c
                    if c > hi:
                        hi = c
        if hi >= lo:
            widest = max(widest, (hi - lo + 1) * HIT_CELL)
    return widest


#: How much wider than its own footprint a piece may be drawn before it is worth saying so.
#:
#: Not 1.0: most pieces paint a little beyond the ground they hold and that is what makes a
#: village look built rather than laid out on a grid - the median across the shipped
#: catalogue is 0.88, and the largest honest ones sit near 1.2. This is the point past which
#: a piece is covering tiles another piece can still be built on.
FOOTPRINT_OVERHANG = 1.30


def hit_length(w, h):
    """How long a hit mask is for a picture this size - GroveHitMask.HexLengthFor."""
    side = lambda px: 0 if not px or px <= 0 else (px + HIT_CELL - 1) // HIT_CELL
    return (side(w) * side(h) + 3) // 4


def png_size(path):
    """A PNG's pixel size from its header alone, so no image library is needed here."""
    if not path or not os.path.isfile(path):
        return (None, None)
    with open(path, "rb") as f:
        head = f.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return (None, None)
    return (int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big"))

RETIRED_RESIDENTS = {
    "sunmote": "puff",
    "ripple": "timber",
    "prism": "sprocket",
    "burr": "thistle",
    "dusk": "monarch",
}


def land_price(region):
    """What a stretch of ground costs, as (credits, gems).

    Mirrors GroveRegion.Cost/Gems. A region is sold in one currency or the other; both
    zero is starter land. Nothing here converts one into the other, deliberately - the
    only credit figure ever read off a region is the grove's worth, and gem-priced land
    is worth nothing there (see GroveRegionDto.gems).
    """
    return int(region.get("cost", 0) or 0), int(region.get("gems", 0) or 0)


def is_starter_land(region):
    """Land nothing gates - what a new player builds on.

    Mirrors GroveRegion.IsStarter, *including* the correction that shipped with the gem
    prices: this is both prices and not just credits. The narrow reading was the whole
    rule while credits priced the whole floor and becomes "every gem-priced region is
    free" the moment they do not, which would have handed over half the floor at launch
    and read as correct in every file.
    """
    cost, gems = land_price(region)
    return cost <= 0 and gems <= 0


def check_grove(keys, level_ids, chapter_ids, companions, companion_costs=None, companion_rows=None):
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

    def art_file(key, animated):
        full = os.path.join(art_root, key.replace("/", os.sep))
        if not animated:
            return full + ".png"
        frames = sorted(f for f in os.listdir(full) if f.lower().endswith(".png")) if os.path.isdir(full) else []
        return os.path.join(full, frames[0]) if frames else None

    def art_exists(key, animated):
        full = os.path.join(art_root, key.replace("/", os.sep))
        return os.path.isdir(full) if animated else os.path.exists(full + ".png")

    def art_paths(key, animated, facings=1):
        """Every picture an art key stands for — `grove_art_facts.art_paths`'s three shapes.

        A turnable piece is a folder of `f0..f3` and every one of them is a real picture
        with a mask of its own; an animated one is a folder whose first frame speaks for
        the reel; anything else is one PNG.
        """
        full = os.path.join(art_root, key.replace("/", os.sep))
        if facings > 1:
            out = [os.path.join(full, "f%d.png" % k) for k in range(facings)]
            return out if all(os.path.isfile(f) for f in out) else []
        if animated:
            frames = sorted(f for f in os.listdir(full)
                            if f.lower().endswith(".png")) if os.path.isdir(full) else []
            return [os.path.join(full, frames[0])] if frames else []
        return [full + ".png"] if os.path.isfile(full + ".png") else []

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
    land_gems = 0
    rungs = {}
    sellable = 0

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
        cost, gems = land_price(region)
        order = int(region.get("order", 0))

        if rw <= 0 or rh <= 0:
            errors.append(f"grove region '{rid}' is {rw}x{rh}; it holds no tiles")
            continue

        if rc < 0 or rr < 0 or rc + rw > cols or rr + rh > rows:
            errors.append(f"grove region '{rid}' runs off a {cols}x{rows} field")
            continue

        # One currency or the other. Both is the mistake with no safe reading: the shop
        # cannot draw a button for a stretch that costs 5,000 credits and 600 gems, and
        # whichever half the code picked would be a price nobody authored.
        if cost > 0 and gems > 0:
            errors.append(f"grove region '{rid}' is priced in both credits ({cost}) and gems "
                          f"({gems}); a region is sold in one currency or the other")

        if is_starter_land(region):
            starters += 1
            # Starter land is not sold, so it is not on the ladder. A rung on it pushes every
            # real rung one place down and makes the free ground look like something to buy.
            if order > 0:
                errors.append(f"grove region '{rid}' is free but sits on ladder rung {order}; "
                              "starter land is not sold, so it is not on the ladder")
        else:
            land_total += cost
            land_gems += gems
            sellable += 1

            # GroveLand.NextForSale offers the lowest unowned rung and nothing else, so a
            # missing rung strands the stretch *and everything behind it*, and a duplicate
            # one is a tie broken by authoring order, which nobody thinks of as a decision.
            if order <= 0:
                errors.append(f"grove region '{rid}' is for sale but has no ladder rung "
                              "('order'); it would never be offered, and nothing behind it "
                              "would be either")
            elif order in rungs:
                errors.append(f"grove regions '{rungs[order]}' and '{rid}' both sit on ladder "
                              f"rung {order}; only one can be offered and which is decided by "
                              "authoring order")
            else:
                rungs[order] = rid

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

    for rung in range(1, sellable + 1):
        if rung not in rungs:
            errors.append(f"the grove's land ladder has no rung {rung}; the rungs must be 1 to "
                          f"{sellable} with no gaps")

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
        held = next((x for x in regions if x.get("id") == rid), None)
        if held is not None and not is_starter_land(held):
            cost, gems = land_price(held)
            errors.append(f"the grove's {what} stands on {tid}, in region '{rid}', which costs "
                          f"{cost or gems}; a new player would see it behind a padlock")
        return tid

    hall = named_tile("hallTile", "hall", True)
    starter_tile = named_tile("starterTile", "starter companion", False)

    # The hall's footprint is a fact about the floor (GroveFloor.HallFootprint): every
    # dwelling has to author the same one, or buying a bigger home would take ground.
    hall_cols = int(floor.get("hallCols") or 1)
    hall_rows = int(floor.get("hallRows") or 1)
    if not (1 <= hall_cols <= MAX_FOOTPRINT and 1 <= hall_rows <= MAX_FOOTPRINT):
        errors.append(f"the grove's hall is {hall_cols}x{hall_rows}; a footprint is 1..{MAX_FOOTPRINT} a side")

    hall_tiles = set()
    if hall:
        hc, hr = int(hall[2:5]), int(hall[6:9])
        for c in range(hc, hc + hall_cols):
            for r in range(hr, hr + hall_rows):
                tid = "t_%03d_%03d" % (c, r)
                hall_tiles.add(tid)
                if tid not in owner:
                    errors.append(f"the grove's hall ({hall_cols}x{hall_rows} from {hall}) covers {tid}, "
                                  "which belongs to no region")
                elif not is_starter_land(
                        next((x for x in regions if x.get("id") == owner[tid]), {})):
                    errors.append(f"the grove's hall covers {tid}, which is in a region that is "
                                  "for sale; a new player would see their home behind a padlock")
        if starter_tile in hall_tiles:
            errors.append(f"the grove's starter companion on {starter_tile} stands under the hall; "
                          "the game drops the companion")

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
            dwellings.append((piece.get("tier", 0), pid, cost,
                              int(piece.get("requiresKeeperLevel", 0) or 0)))
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
        animated = piece.get("animated", False)
        facings = int(piece.get("facings") or 1)

        # A piece drawn at four facings and one that animates both live in a folder, for
        # different reasons, so a piece claiming both has two readings of one folder and the
        # reader would silently take one. Refused rather than salvaged (HomesteadPiece.Facings).
        if facings not in (1, 4):
            errors.append(f"grove piece '{pid}' asks for {facings} facings; only 1 and 4 exist")
        if facings > 1 and animated:
            errors.append(f"grove piece '{pid}' is both animated and turnable, and both are "
                          "drawn from the same frames; it can only be one")

        found = art_paths(art, animated, facings)
        if not found:
            errors.append(f"grove piece '{pid}' has no art at Art/{art}"
                          f"{'/' if animated or facings > 1 else '.png'}"
                          + (f" (it is turned {facings} ways, so it wants f0..f{facings - 1})"
                             if facings > 1 else ""))
        else:
            # The picture's own facts, authored so the layout never waits for the sprite and a
            # tap tests paint rather than air (HomesteadPiece.ArtWidth, GroveHitMask). Written
            # by import_grove_art.py; the size is re-read off the PNG header here, and each
            # mask's shape is checked - their content is the generator's own --check.
            sizes = [png_size(f) for f in found]
            pw, ph = sizes[0]
            if len(set(sizes)) != 1:
                errors.append(f"grove piece '{pid}' has facings of different sizes {sizes}; the "
                              "catalogue carries one box for all of them, so three of the four "
                              "would be drawn in the wrong one")
            if piece.get("w") != pw or piece.get("h") != ph:
                errors.append(f"grove piece '{pid}' authors art size {piece.get('w')}x{piece.get('h')} "
                              f"and the PNG is {pw}x{ph}; run Tools/import_grove_art.py")

            masks = piece.get("hits") if facings > 1 else [piece.get("hit") or ""]
            masks = masks if isinstance(masks, list) else []
            if len(masks) != facings:
                errors.append(f"grove piece '{pid}' is turned {facings} ways and carries "
                              f"{len(masks)} hit mask(s); run Tools/import_grove_art.py")
            for k, hit in enumerate(masks):
                where = f"grove piece '{pid}'" + (f" facing {k}" if facings > 1 else "")
                if len(hit or "") != hit_length(pw, ph) or any(ch not in "0123456789abcdef" for ch in hit):
                    errors.append(f"{where} has no {hit_length(pw, ph)}-character hit mask for "
                                  f"a {pw}x{ph} picture; run Tools/import_grove_art.py")
                elif set(hit) == {"0"}:
                    errors.append(f"{where} has an empty hit mask, so nothing about it can "
                                  "be tapped; is the art blank?")

        # What the piece stands on. A judgement, so only its shape is checked here; the hall's
        # is the floor's and every dwelling must agree with it (GroveFloor.HallFootprint).
        fcols = int(piece.get("cols") or 1)
        frows = int(piece.get("rows") or 1)
        if not (1 <= fcols <= MAX_FOOTPRINT and 1 <= frows <= MAX_FOOTPRINT):
            errors.append(f"grove piece '{pid}' has a footprint of {fcols}x{frows}; a side is "
                          f"1..{MAX_FOOTPRINT}")
        if kind == "dwelling" and (fcols, frows) != (hall_cols, hall_rows):
            errors.append(f"grove home '{pid}' is {fcols}x{frows} and the hall is "
                          f"{hall_cols}x{hall_rows}; buying it would take or leave ground")

        # **How much of what a piece paints it actually occupies**, which is the one thing about
        # a footprint that nothing else could report. A footprint is a judgement (invariant 16i)
        # — a tree's is its trunk and it is *supposed* to overhang — so this warns rather than
        # refusing, and the canopy shelf is exempt because overhang is that shelf's whole point.
        #
        # It exists because the alternative is an eye. Shipped once: a wall five tiles long was
        # authored 3x1 and a three-tile fence gate 1x1, so they painted up to four times the
        # ground they held. Every gate was green — the piece is valid, the art is on disk, the
        # mask matches, the price divides — and what a player met was a starter plot with two
        # objects lying across each other.
        if piece.get("slot") != "canopy" and piece.get("w") and piece.get("scale"):
            ink = painted_width(piece)
            drawn = ink * piece["scale"] * PIECE_SCALE
            span = (fcols + frows) / 2.0 * TILE_WIDTH
            if ink and span > 0 and drawn / span > FOOTPRINT_OVERHANG:
                warnings.append(
                    f"grove piece '{pid}' paints {drawn:.0f} wide and occupies {span:.0f} "
                    f"({drawn / span:.2f}x); it covers tiles it does not hold, so things can "
                    "be built underneath it. Widen cols/rows, or say why not")

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

    # The masks' *content* - whether each one is what the PNG would produce today - is the
    # generator's own question, asked here so a re-cut sprite fails offline rather than only
    # when somebody remembers to run the tool. It needs Pillow, which every art tool in
    # Tools/ already needs; a machine without it cannot verify content and is told so.
    try:
        sys.path.insert(0, os.path.join(os.path.dirname(ROOT), "..", "..", "Tools"))
        import grove_art_facts
    except ImportError as e:
        errors.append(f"the grove's hit masks could not be checked ({e}); install Pillow")
    else:
        stale = 0
        for piece in pieces:
            facings = int(piece.get("facings") or 1)
            art, animated = grove_art_facts.piece_art(piece)
            facts = grove_art_facts.facts_for(art, animated, facings)
            if facts is None:
                continue
            w, h, masks = facts
            authored = piece.get("hits") if facings > 1 else [piece.get("hit")]
            if (piece.get("w"), piece.get("h")) != (w, h) or list(authored or []) != masks:
                stale += 1
                if stale <= 3:
                    errors.append(f"grove piece '{piece.get('id')}' has art facts that differ from "
                                  "its PNG; run Tools/import_grove_art.py")
        # A resident is a companion, and the grove draws its art too: the same facts live on
        # the manifest's companion entries (groveW / groveH / groveHit) under the same check.
        for companion in companion_rows or []:
            cid = companion.get("id", "")
            facts = grove_art_facts.facts_for(*grove_art_facts.companion_art(companion))
            if facts is None:
                errors.append(f"companion '{cid}' has no art for the grove to draw")
                continue
            facts = (facts[0], facts[1], facts[2][0])
            if (companion.get("groveW"), companion.get("groveH"), companion.get("groveHit")) != facts:
                stale += 1
                if stale <= 3:
                    errors.append(f"companion '{cid}' has grove art facts that differ from its "
                                  "PNG; run Tools/grove_art_facts.py")
        if stale > 3:
            errors.append(f"{stale} grove pieces or companions have art facts that differ from their PNGs")

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
    for tier, pid, _cost, _level in dwellings:
        if tier <= 0:
            errors.append(f"grove home '{pid}' has no tier; the ladder cannot be ordered")
        if tier in tiers:
            errors.append(f"grove homes '{tiers[tier]}' and '{pid}' are both tier {tier}")
        tiers[tier] = pid

    if dwellings:
        first = min(dwellings)
        first_rows = [p for p in pieces if p.get("id") == first[1]]
        if first[2] > 0 or first[3] > 0 or (first_rows and (first_rows[0].get("requiresLevel") or first_rows[0].get("requiresChapter"))):
            errors.append(f"the first home '{first[1]}' is not free; a new grove would open "
                          "with nothing on its hearth")

    # The keeper gates up the home ladder. Both failures below are invisible in the game.
    #
    # A rung asking for a level an earlier rung already demanded parses, validates, draws a
    # price and simply refuses nobody - invariant 5d's decoration arriving on the one purchase
    # the whole grove is composed around. And a gated rung with no price can never be held at
    # all: the gate is permission to pay rather than a route of its own (invariant 15a), so
    # nothing grants it, and the ladder silently ends one step early with every file correct.
    below, under = 0, None
    for tier, pid, cost, level in sorted(dwellings):
        if level > 0 and cost <= 0:
            errors.append(f"grove home '{pid}' opens at keeper level {level} and has no price; "
                          "the gate is permission to pay rather than a way of paying, so nothing "
                          "would ever grant it and the ladder would end there")
        if level <= 0:
            if below > 0:
                warnings.append(f"grove home '{pid}' is tier {tier} and opens at no keeper "
                                f"level, while '{under}' below it asks for {below}; the ladder "
                                "stops climbing there")
            continue
        if level <= below:
            errors.append(f"grove home '{pid}' opens at keeper level {level}, which '{under}' "
                          "below it has already passed; a gate that refuses nobody is not a gate")
        below, under = level, pid

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

    # land_total and not the gems beside it: gem-priced land is worth nothing to a grove's
    # score, so counting it here would put a star rung above what credits can ever reach.
    # See GroveRegionDto.gems for why the leaderboard cannot price a gem.
    everything = total + land_total + roster

    if everything <= 0:
        warnings.append("nothing in the grove has a price, so its score can never leave zero "
                        "and no star is reachable")
    elif ladder and isinstance(ladder[-1], int) and ladder[-1] > everything:
        warnings.append(f"the grove's last star asks for {ladder[-1]} credits and the whole "
                        f"catalog is worth {everything}; nobody can ever win it")

    return {
        "homes": len(dwellings), "ladder": sum(c for _t, _p, c, _l in dwellings),
        "home_rungs": [(p, c, l) for _t, p, c, l in sorted(dwellings)],
        "cols": cols, "rows": rows, "regions": len(regions), "free_regions": starters,
        "owned_tiles": len(owner), "land": land_total, "land_gems": land_gems,
        "ladder_names": " -> ".join(rungs[r] for r in sorted(rungs)),
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
STORE_SHELVES = {"gems", "coins", "bundles", "supplies", "event_pass"}

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


#: The utility kinds this build knows. Content may not invent one: what a utility *does* is a
#: rule with a fail state and a grade attached, so an entry naming an unknown kind is skipped
#: exactly as a chapter naming an unknown mode is (invariant 20). Mirrors `UtilityKinds`.
UTILITY_KINDS = {"blast", "mend", "surge", "storm"}

#: What the build actually carries a picture for. Which utilities exist is content and which
#: pictures exist is not, so adding one is a build - and an entry with no icon would draw a white
#: rectangle on the bar (invariant 7b). Mirrors the `Utility/` block in `AssetManifest.UiSprites`.
UTILITY_ART = {"firepot", "mending", "surge", "stormcall"}

#: The longest cooldown content may author, in whole seconds. Mirrors
#: `UtilityCooldown.MaxSeconds`. It is a guard against a *unit* rather than a balance opinion:
#: five minutes is longer than any run in this game, so anything past it is almost certainly a
#: figure written in milliseconds - which plays as an item usable once and validates perfectly.
UTILITY_MAX_COOLDOWN = 300


def check_utilities(progression, keys, warnings):
    """The action bar's catalog. `ContentValidation.ValidateUtilities`, offline.

    Four things the reader on the phone cannot know, and one it can but must not have to.

    * **Every id has a picture in this build.** A price is content; a PNG is not.
    * **Every id resolves its two loc keys.** They are *derived* from the id
      (`utility.{id}.name`, `.note`), so `loc.py` cannot see them at all - which is invariant
      30d's situation exactly, and the reason they are checked here or nowhere.
    * **The ladder is 1..N with no gaps and no ties**, because it is authored rather than
      derived: a duplicated rung reorders the bar under a player between two content pushes.
    * **Every cooldown is whole seconds inside the supported range.** Nought means none, which
      is what a file written before the field existed says and is the behaviour the bar had
      then; what is refused is a figure so large it can only have been written in another unit.
    * **Every utility a chest names exists**, which is the one cross-block check in this file
      that can be wrong in a way nothing else notices - a chest paying `utility:firepop` rolls,
      publishes, seeds and grants nothing.
    """
    errors = []
    block = progression.get("utilities")

    if block is None:
        # Absent is legitimate: the client falls back to its built-in catalog. Said out loud,
        # because "the bar went empty" is otherwise a silent symptom of an edit to the wrong file.
        warnings.append("progression.json has no 'utilities' block, so the built-in catalog "
                        "stands and the bar cannot be retuned")
        return errors, set()

    items = block.get("items") or []
    if not items:
        errors.append("utilities block lists no items")
        return errors, set()

    seen, orders, known = set(), [], set()

    for entry in items:
        uid = entry.get("id") or ""
        if not uid:
            errors.append("a utilities entry has no id; an id is what the save keys on")
            continue

        if uid in seen:
            errors.append(f"utilities names '{uid}' twice; an id is permanent and two entries "
                          "under one would be two things in one save row")
            continue
        seen.add(uid)

        kind = entry.get("kind") or ""
        if kind not in UTILITY_KINDS:
            errors.append(f"utilities entry '{uid}' names unknown kind '{kind}'")
            continue
        known.add(uid)

        if entry.get("magnitude", 0) < 1:
            errors.append(f"utilities entry '{uid}' has magnitude {entry.get('magnitude', 0)}; "
                          "a utility that does nothing spends a slot and gives nothing back")

        if "reach" in entry:
            # Removed when the blast became a box on the hill's grid rather than a radius: a
            # field nothing reads is a number somebody will tune and watch do nothing.
            errors.append(f"utilities entry '{uid}' names 'reach', which no longer exists; a "
                          "blast takes exactly what is standing in the box it is thrown at")

        if entry.get("gemPrice", 0) < 0:
            errors.append(f"utilities entry '{uid}' has a negative gem price")

        if entry.get("maxHeld", 0) < 1:
            errors.append(f"utilities entry '{uid}' may be held {entry.get('maxHeld', 0)} times, "
                          "so a grant could never land")

        cooldown = entry.get("cooldownSeconds", 0)
        if not isinstance(cooldown, int) or cooldown < 0 or cooldown > UTILITY_MAX_COOLDOWN:
            # Refused rather than clamped, for the reason the C# reader refuses it: a clamp
            # would hide the one mistake worth failing a build over.
            errors.append(f"utilities entry '{uid}' cools for {cooldown}s; a cooldown is whole "
                          f"seconds from 0 (none) to {UTILITY_MAX_COOLDOWN}, and anything past "
                          "that is a number written in the wrong unit")

        if uid not in UTILITY_ART:
            errors.append(f"utilities entry '{uid}' has no icon in this build - a picture is not "
                          "content, so adding a utility is a build (see AssetManifest)")

        for key in (f"utility.{uid}.name", f"utility.{uid}.note"):
            if key not in keys:
                errors.append(f"utilities entry '{uid}' needs loc key '{key}'")

        orders.append(entry.get("order", 0))

    if orders and sorted(orders) != list(range(1, len(orders) + 1)):
        errors.append(f"utilities orders are {sorted(orders)}; every entry needs its own rung "
                      "from 1 up, or the bar reshuffles itself on a retune")

    # And the cross-block half: a chest naming a utility that does not exist rolls, publishes,
    # seeds and grants nothing at all.
    for index, chest in enumerate(((progression.get("daily") or {}).get("chests")) or []):
        for role in ("guaranteed", "options"):
            for band in chest.get(role) or []:
                if band.get("kind") != "utility":
                    continue

                item = band.get("item") or ""
                if not item:
                    errors.append(f"daily chest {index} {role} pays 'utility' and names no item")
                elif item not in known:
                    errors.append(f"daily chest {index} {role} pays utility '{item}', which the "
                                  "utilities block does not define")

    return errors, known


#: The goals a task may name. Mirrors `TaskGoals`. A goal is a verb the game counts, so
#: content may not invent one: an entry naming an unknown goal is skipped by the reader
#: exactly as a chapter naming an unknown mode is (invariant 20), and reported here.
TASK_GOALS = {"runs", "wins", "stars", "three_stars", "matches", "raiders", "bosses",
              "charms", "cogs", "bombs", "utilities", "waves", "streak"}

#: A task or tier id: written into save files, claim ids and loc keys, so it is the same
#: alphabet a level id is. Mirrors `TaskDefinition.IsValidId`.
TASK_ID = __import__("re").compile(r"^[a-z0-9_]{1,32}$")


def check_tasks(progression, keys, utilities, art, warnings):
    """The task slates and their chest ladder. `ContentValidation.ValidateTasks`, offline.

    What the reader on the phone refuses is refused here too (a tier nobody defined, a
    duplicated id, a target of nought). What only a gate can see:

    * **Every tier has its two pictures in this build** - the closed icon (`Ui/Chest/{id}`)
      and the opening reel (`Chests/{id}`). Both are *built* from the id, so `artnames.py`
      cannot see either, and a missing one is a white rectangle on the hub or over the one
      ceremony in the game that is entirely a picture (invariant 7b).
    * **Every task and tier resolves its derived loc key** (`task.{id}.name`,
      `chest.{id}.name`); `loc.py` cannot see a derived key at all (invariant 5a).
    * **The ladder rises**: a dearer tier guarantees more than the one below it, or the harder
      task reads as the game punishing the player for taking it.
    * **A utility a chest names exists**, the daily chests' own cross-check.
    * **A slate deals what it says**: at least `activePerPeriod` live tasks, or the page shows
      fewer rows than the copy promises.
    """
    errors = []
    block = progression.get("tasks")
    if not block:
        warnings.append("progression.json has no 'tasks' block; the built-in slate ships")
        return errors, {}

    per = block.get("activePerPeriod", 3)
    tiers = block.get("tiers") or []
    if not tiers:
        errors.append("tasks block lists no chest tiers")
        return errors, {}

    tier_ids = []
    floors = []
    for index, tier in enumerate(tiers):
        tid = tier.get("id") or ""
        if not TASK_ID.match(tid):
            errors.append(f"tasks tier {index} has a bad id '{tid}'")
            continue
        if tid in tier_ids:
            errors.append(f"tasks tier '{tid}' is listed twice")
        tier_ids.append(tid)

        for address in (f"Ui/Chest/{tid}", f"Chests/{tid}"):
            if address not in art:
                errors.append(f"tasks tier '{tid}' draws '{address}', which is not on disk; "
                              "run Tools/make_chest_art.py - a picture is not content")

        key = f"chest.{tid}.name"
        if key not in keys:
            errors.append(f"tasks tier '{tid}' needs loc key '{key}'")

        chest = tier.get("chest") or {}
        guaranteed = chest.get("guaranteed") or []
        if not guaranteed:
            errors.append(f"tasks tier '{tid}' guarantees nothing; every chest must pay something")
        floors.append(sum(max(1, b.get("min", 0)) for b in guaranteed))

        for role in ("guaranteed", "options"):
            for band in chest.get(role) or []:
                kind = band.get("kind")
                if kind == "run_time":
                    errors.append(f"tasks tier '{tid}' {role} pays 'run_time', which is spent "
                                  "inside a run; a chest is opened where there is no run")
                if role == "options" and band.get("weight", 1) < 1:
                    errors.append(f"tasks tier '{tid}' option '{kind}' has weight "
                                  f"{band.get('weight')}; an option that can never be picked is "
                                  "an odds table that lies about itself")
                if kind != "utility":
                    continue
                item = band.get("item") or ""
                if not item:
                    errors.append(f"tasks tier '{tid}' {role} pays 'utility' and names no item")
                elif item not in utilities:
                    errors.append(f"tasks tier '{tid}' {role} pays utility '{item}', which the "
                                  "utilities block does not define")

    for i in range(1, len(floors)):
        if floors[i] <= floors[i - 1]:
            errors.append(f"tasks tier '{tier_ids[i]}' guarantees {floors[i]}, not more than "
                          f"'{tier_ids[i - 1]}' at {floors[i - 1]}; a dearer chest must pay more")

    # What each tier earns toward a season. Absent is nought and legal - a tier added for a
    # promotion should be able to move nobody's track - but a ladder that only ever falls is a
    # season where the humblest chest is worth the most, which is the same fault as a dearer
    # chest paying less, one rung up.
    marks = [max(0, int((block.get("tiers") or [])[i].get("marks") or 0))
              for i in range(len(tier_ids))]

    for i, worth in enumerate(marks):
        if worth > 1000:
            errors.append(f"tasks tier '{tier_ids[i]}' is worth {worth} marks, above the "
                          "supported 1000; a tier worth more than a whole season track is a typo")
        if i and worth and marks[i - 1] and worth < marks[i - 1]:
            errors.append(f"tasks tier '{tier_ids[i]}' is worth {worth} mark(s), fewer than "
                          f"'{tier_ids[i - 1]}' at {marks[i - 1]}; a dearer chest may not grow "
                          "the season less")

    ids = set()
    slates = {}
    for period in ("daily", "weekly"):
        entries = block.get(period) or []
        if not entries:
            errors.append(f"tasks block lists no {period} tasks")
            continue

        live = 0
        for index, entry in enumerate(entries):
            tid = entry.get("id") or ""
            if not TASK_ID.match(tid):
                errors.append(f"{period} task {index} has a bad id '{tid}'")
                continue
            if tid in ids:
                errors.append(f"task id '{tid}' is listed twice; it is written into the save "
                              "and a claim, so two tasks sharing one would pay as one")
            ids.add(tid)

            goal = entry.get("goal")
            if goal not in TASK_GOALS:
                errors.append(f"{period} task '{tid}' names unknown goal '{goal}'; the reader "
                              "skips it and the slate is one short")
            if entry.get("target", 0) < 1:
                errors.append(f"{period} task '{tid}' has target {entry.get('target')}")
            if entry.get("tier") not in tier_ids:
                errors.append(f"{period} task '{tid}' pays tier '{entry.get('tier')}', which "
                              "the block does not define")

            key = f"task.{tid}.name"
            if key not in keys:
                errors.append(f"{period} task '{tid}' needs loc key '{key}'")
            if entry.get("target") == 1 and f"task.{tid}.name_one" not in keys:
                warnings.append(f"{period} task '{tid}' has a target of one and no "
                                f"'task.{tid}.name_one'; the sentence reads '1 battles'")

            if not entry.get("retired"):
                live += 1

        if live < per:
            errors.append(f"the {period} slate has {live} live task(s) and deals {per}; the "
                          "page would show fewer rows than the copy promises")
        elif live < per * 2:
            warnings.append(f"the {period} slate has only {live} live task(s) for {per} a "
                            "period, so consecutive periods repeat tasks")
        slates[period] = live

    # About how many marks a day a player who claims everything is dealt: the daily slate
    # over a day plus the weekly slate over a week, at the rate the rotation deals them. The
    # season's reachability is measured against it, and it is the only figure that can say
    # whether a ladder can be climbed inside its own window.
    worth = dict(zip(tier_ids, marks))
    per_day = 0.0
    for period, live in slates.items():
        entries = [e for e in (block.get(period) or []) if not e.get("retired")]
        if not entries:
            continue
        average = sum(worth.get(e.get("tier"), 0) for e in entries) / len(entries)
        dealt = min(per, len(entries)) * average
        per_day += dealt / 7.0 if period == "weekly" else dealt

    return errors, {"tiers": tier_ids, "per": per, "slates": slates,
                    "marks": worth, "marksPerDay": per_day}


def check_seasons(manifest, progression, tasks, keys, warnings):
    """The seasons: the ladder, the tiers it names, and whether it can be climbed.

    Every rule here is one the client's `CatalogIndexBuilder.AddEvent` also enforces, plus the
    two it structurally cannot. **A rung's tier has to exist**, and that is invisible in either
    file on its own: the ladder is in `manifest.json` and the tiers are in `progression.json`,
    which version independently (invariant 9b) and are fetched separately. And **the ladder has
    to be climbable inside its own window**, which is arithmetic over both files and the one
    thing that says a season's last rungs are reachable at all.
    """
    errors = []
    seasons = (manifest or {}).get("events") or []
    tier_ids = set((tasks or {}).get("tiers") or [])
    ranks = {tid: i for i, tid in enumerate((tasks or {}).get("tiers") or [])}
    per_day = float((tasks or {}).get("marksPerDay") or 0.0)
    shipped = []

    for season in seasons:
        sid = season.get("id") or ""
        if season.get("disabled"):
            continue

        if not TASK_ID.match(sid):
            errors.append(f"season '{sid}' has an unusable id; one names a save row, a loc key "
                          "and every claim id its chests produce")
            continue

        for suffix in ("name", "blurb"):
            key = f"ui.event.{sid}.{suffix}"
            if key not in keys:
                errors.append(f"season '{sid}' needs loc key '{key}'")

        start = int(season.get("startUnix") or 0)
        end = int(season.get("endUnix") or 0)
        if end <= start:
            errors.append(f"season '{sid}' ends at or before it starts")
            continue

        days = (end - start) // 86400
        if days > 90:
            errors.append(f"season '{sid}' runs for {days} days, above the supported 90")

        gems = int(season.get("passGems") or 0)
        sells_pass = gems > 0

        if gems < 0 or gems > 100000:
            errors.append(f"season '{sid}' prices its pass at {gems} gems, outside 0..100000")
        rungs = season.get("milestones") or []

        if not rungs:
            errors.append(f"season '{sid}' has no rungs, so it pays nothing")
            continue
        if len(rungs) > 40:
            errors.append(f"season '{sid}' has {len(rungs)} rungs, above the supported 40")

        previous = 0
        for rung in rungs:
            goal = int(rung.get("goal") or 0)
            if goal <= previous:
                errors.append(f"season '{sid}' rung goals must rise: {goal} follows {previous}")
            previous = max(previous, goal)

            free = rung.get("tier") or ""
            paid = rung.get("premiumTier") or ""

            if free not in tier_ids:
                errors.append(f"season '{sid}' rung at {goal} pays free tier '{free}', which the "
                              "tasks block does not define; a rung paying a chest nobody can "
                              "price is a claim the server can never confirm")
            if sells_pass and paid not in tier_ids:
                errors.append(f"season '{sid}' sells a pass but its rung at {goal} pays pass "
                              f"tier '{paid}', which the tasks block does not define")
            if not sells_pass and paid:
                errors.append(f"season '{sid}' pays pass tier '{paid}' at {goal} but sells no "
                              "pass, so nobody could ever claim it")

            # The paid column has to be worth paying for, rung by rung. A pass selling the
            # chest the free track already gives is a product with nothing behind it, and it
            # is a warning rather than an error because a promotion may deliberately match.
            if sells_pass and free in ranks and paid in ranks and ranks[paid] <= ranks[free]:
                warnings.append(f"season '{sid}' pays '{paid}' on the pass track at {goal} "
                                f"marks against '{free}' free; the pass rung does not beat "
                                "the one beside it")

        top = previous
        if per_day > 0 and days > 0 and days * per_day < top:
            errors.append(f"season '{sid}' tops out at {top} marks but its {days}-day window "
                          f"deals about {int(days * per_day)} to a player who claims every "
                          "chest; its last rungs are unreachable")

        shipped.append({"id": sid, "days": days, "rungs": len(rungs), "top": top,
                        "pass": gems})

    return errors, shipped


#: The abilities a turret may name. Mirrors `WardAbilities`.
#:
#: An ability is a rule the board runs, so an entry naming one this build has never heard of still
#: stands and simply fires a plain bolt (invariant 20, one level down) - which is a warning here
#: rather than an error, exactly as an unknown chest kind is.
WARD_ABILITIES = {"none", "splash", "chain", "frost", "pierce", "rend", "siphon", "ember",
                  "stun", "beacon"}

#: The four colours a turret is cut in. `WardLine.Colours`.
WARD_COLOURS = "rgby"


#: `WardModel.Elemental` - the one turret whose bolt is a different element on each
#: colour. Named rather than derived; see the note in `check_wards`.
WARD_ELEMENTAL = "breaker"

#: `WardModel.Baseline` - the tenths a turret neither tougher nor harder-hitting than the free one
#: carries, and what an unauthored `power` or `guard` means.
WARD_BASELINE = 10

#: `SiegeTuning.LeastGuardTenths` - the least toughness a turret may be authored at.
WARD_LEAST_GUARD = 7

#: `WardTier.Count` and `WardStars.Steps` - how many bands the shelf is read in, and how many
#: upgrades there are (one fewer than there are stars).
WARD_TIERS = 3
WARD_STAR_STEPS = 4

#: `WardTier.Opens` - the shelf rung each band starts at, lowest first.
WARD_TIER_OPENS = (1, 11, 18)

#: `WardTier.Gates` and `WardTier.TopLevel` - the keeper level each band's rungs stand at or
#: above, and the highest level the top band may ask for.
#:
#: **The bands are the ladder now.** A turret used to be sealed until the rung below it on the
#: shelf was bought, so the three headers were a caption over an order that was already forced;
#: with the seal gone at the owner's decision, a keeper level is the whole of what opens a rung
#: and the header is the only thing saying when a stretch of the shelf opens. A rung authored
#: outside its band parses, prices, validates and plays - what it does is put a lie in a header.
WARD_TIER_GATES = (1, 20, 30)
WARD_TOP_LEVEL = 40


#: `EndlessHubLayout.Points` - how many short lines a hub says about the lane it draws.
HUB_POINTS = 3

#: `GameTrack.Laddered` - the lanes that are a chain of levels to walk. Everything else is a
#: single endless run and draws a hub instead of a map (`EndlessHub`), which is what needs the
#: extra strings below.
LADDERED_TRACKS = {"main"}


def check_tracks(manifest, keys):
    """Every lane a chapter names can draw its own screen. `ContentValidation.ValidateTracks`.

    **Here because nothing else can see these keys.** A lane's name, its tagline and - for a lane
    with no ladder - the three lines its hub says are all *derived* from the track id
    (`GameTrack.NameKey`, `TaglineKey`, `PointKey`), exactly as a turret's name is derived from
    its own. `loc.py` reads literals off call sites and there is no literal to read, so a
    mistyped or forgotten string is a blank row on a screen with every other gate green - which
    is the failure invariant 6 exists to make impossible.

    **Asked of the lanes the manifest actually names**, not of every lane this build knows: a
    track whose chapters are all disabled draws nothing and owes no copy.
    """
    errors = []

    lanes = sorted({(c.get("track") or "main") for c in manifest.get("chapters", [])})

    for track in lanes:
        for key in (f"track.{track}.name", f"track.{track}.tagline"):
            if key not in keys:
                errors.append(f"track '{track}' needs loc key '{key}'")

        if track in LADDERED_TRACKS:
            continue

        # A lane with no ladder draws a hub, and a hub says exactly this many lines.
        for i in range(1, HUB_POINTS + 1):
            key = f"track.{track}.point{i}"
            if key not in keys:
                errors.append(f"track '{track}' draws a hub rather than a map, so it needs loc "
                              f"key '{key}' - a hub says {HUB_POINTS} lines and an absent one "
                              "is a blank row")

    return errors


def reachable_keeper_level(manifest, progression):
    """The highest keeper level the shipped catalog can ever pay for, and the XP that does it.

    XP derives from the star ledger and from nothing else (invariant 9), so three stars on every
    live glade is the ceiling on what any account can reach - which is the number every keeper
    wall has to be measured against. Mirrors `ContentValidation.PerfectXp` and
    `ProgressionTable.LevelFor`.
    """
    base = progression.get("rewards") or {}
    over = {c.get("chapterId"): c for c in (progression.get("chapterRewards") or [])}

    def rule(chapter_id):
        return over.get(chapter_id, base)

    total = 0
    glades = 0
    for chapter in manifest.get("chapters", []):
        if chapter.get("disabled"):
            continue
        r = rule(chapter.get("id"))
        n = len(chapter.get("levels") or [])
        glades += n
        total += n * (r.get("xpFirstClear", 0) + 3 * r.get("xpPerStar", 0))

    steps = progression.get("xpToNext") or []
    tail = progression.get("tailXpToNext", 0)
    step = progression.get("tailXpIncrement", 0)
    top = progression.get("maxLevel", 1)

    level, spent, i = 1, 0, 0
    while level < top:
        need = steps[i] if i < len(steps) else tail + step * (i - len(steps))
        if need <= 0 or spent + need > total:
            break
        spent += need
        level += 1
        i += 1

    return level, total, glades, top


def check_keeper_walls(manifest, progression, warnings):
    """The keeper walls the manifest puts in front of chapters. `ContentValidation.ValidateKeeperWalls`.

    **The only way this goes wrong is invisible in either file alone.** A wall is one integer in
    `manifest.json`; what can reach it is every reward rule in `progression.json` multiplied by
    every glade in the catalog - so a wall above that ceiling is a lane padlocked for the life of
    the build, with the manifest, the index, the map and the hub all perfectly correct.

    An **error** above the level curve's own top, which is unreachable by arithmetic and can only
    be a mistake; a **warning** above what the catalog pays for, which is a decision somebody may
    genuinely want - a wall meant for content that has not shipped yet is how the home ladder's
    rungs are authored.
    """
    errors = []
    walls = []

    reach, _, glades, top = reachable_keeper_level(manifest, progression)

    for chapter in sorted(manifest.get("chapters", []), key=lambda c: c.get("order", 0)):
        wall = chapter.get("minKeeperLevel", 0) or 0

        if wall < 0:
            errors.append(f"chapter '{chapter.get('id')}' has a negative minKeeperLevel")
            continue
        if not wall:
            continue

        walls.append((chapter, wall))

        if wall > top:
            errors.append(f"chapter '{chapter.get('id')}' asks for keeper level {wall}, above "
                          f"the {top} the curve tops out at, so it can never be opened by "
                          "anybody")
        elif wall > reach:
            warnings.append(f"chapter '{chapter.get('id')}' asks for keeper level {wall} and "
                            f"three stars on all {glades} shipped glade(s) reaches only level "
                            f"{reach}, so nobody can open it until more content ships")

    return errors, walls, reach


def check_wards(progression, keys, warnings, art):
    """The turret roster. `ContentValidation.ValidateWards`, offline.

    Five things, and the first two are the ones only this gate can see.

    * **Every model has a picture in every colour, and a reel to fire with.** A price is content;
      a PNG is not. The addresses are *built* from the id (`WardModel.ArtFor`), which is the one
      place in this project a lookup is allowed to build a name - so `artnames.py` cannot check
      them and this is where they are checked instead. A missing one draws a white rectangle two
      cells tall on the object a player looks at for a whole run (invariant 7b).
    * **Every id resolves its two loc keys**, which are derived from the id and therefore
      invisible to `loc.py` (invariant 30d's situation, and the utilities' own).
    * **One price or the other.** Two prices for one turret is two answers to what it costs, and
      the shelf can only draw one of them (`HomesteadRegion`'s rule, invariant 16j).
    * **Every priced turret carries a keeper level, gems included, and the levels strictly
      climb.** That is the owner's reversal of what shipped - a gate used to belong to a credit
      price alone, so the dearest half of the shelf could be taken in any order by anybody holding
      gems. The climb is not a second taste: a turret is sealed until the rung below it is bought
      (`WardCatalog.Before`), so reaching a rung means having met every gate under it, and a rung
      asking for a level an earlier one already demanded could never refuse anybody - the
      decoration invariant 5d names.
    * **An id may not contain `:`**, which separates a turret from the colour it was bought for
      in `wardsOwned` (`WardHolding`). One that did would make every row about it ambiguous.
    * **At least one turret is free**, or a player who has bought nothing stands an empty line and
      a siege with no line cannot be played.
    """
    errors = []
    block = progression.get("wards")

    if block is None:
        warnings.append("progression.json has no 'wards' block, so the built-in roster stands - "
                        "which works, and cannot be retuned without a store review")
        return errors, set()

    models = block.get("models") or []
    if not models:
        errors.append("wards block lists no models")
        return errors, set()

    known, orders = set(), []
    starter = False

    for entry in models:
        wid = entry.get("id") or ""

        if not wid:
            errors.append("a wards entry has no id; an id is what the save keys on")
            continue

        if wid in known:
            errors.append(f"wards names '{wid}' twice; an id is permanent and two entries under "
                          "one would be two turrets in one save row")
            continue

        known.add(wid)

        ability = entry.get("ability") or "none"
        if ability not in WARD_ABILITIES:
            warnings.append(f"wards entry '{wid}' names unknown ability '{ability}'; it fires a "
                            "plain bolt")

        gem = int(entry.get("gemPrice") or 0)
        coin = int(entry.get("coinPrice") or 0)
        level = int(entry.get("minLevel") or 0)

        if gem < 0 or coin < 0:
            errors.append(f"wards entry '{wid}' has a negative price; nought is how a turret says "
                          "it cannot be bought that way")

        if gem > 0 and coin > 0:
            errors.append(f"wards entry '{wid}' is priced in both gems and credits; a turret "
                          "carries one price or the other")

        if level <= 0 and (gem > 0 or coin > 0):
            errors.append(f"wards entry '{wid}' is priced but asks for no keeper level; every "
                          "turret on the shelf is behind one, so nought is no longer how an "
                          "entry says it is ungated")

        if ":" in wid:
            errors.append(f"wards entry '{wid}' contains ':', which separates a turret from the "
                          "colour it was bought for in wardsOwned")

        # **A bolt may never be lighter than the baseline, and toughness may.** Par is the hill's
        # health over a match computed against the baseline bolt, so a turret that hit softer would
        # need more matches than par assumes and push three stars out of reach of whoever bought it
        # - a grade decided by a purchase, on a number that reaches a public board (19a). Health
        # reaches nothing that is graded, so it is the half allowed to be a trade; what it may not
        # do is make a rung impossible, which is what the floor is for.
        power = int(entry.get("power") or 0)
        guard = int(entry.get("guard") or 0)

        if power and power < WARD_BASELINE:
            errors.append(f"wards entry '{wid}' asks for power {power}, under the baseline "
                          f"{WARD_BASELINE}; a turret that hits softer than the free one pushes "
                          "three stars out of reach of whoever bought it")

        if guard and guard < WARD_LEAST_GUARD:
            errors.append(f"wards entry '{wid}' asks for guard {guard}, under the floor "
                          f"{WARD_LEAST_GUARD}; a turret may be a fragile choice and may not be "
                          "an impossible one")

        starter = starter or (gem <= 0 and coin <= 0)

        for colour in WARD_COLOURS:
            for address in (f"Siege/Wards/{wid}_{colour}", f"Siege/Wards/{wid}_{colour}_fire"):
                if address not in art:
                    errors.append(f"wards entry '{wid}' has no art at '{address}' - a picture is "
                                  "not content, so adding a turret is a build")

            # **What it throws.** Every turret owns three reels per colour - the bolt, the flash
            # it leaves at the barrel and what it does when it arrives - except the one that draws
            # the four elemental bolts instead (`WardModel.Elemental`). Named outright rather than
            # derived from the ability, which is the C# side's own rule: that reading was honest
            # only while the starter was the only model without one, and the owner has since moved
            # the elemental set onto a bought turret.
            if wid == WARD_ELEMENTAL:
                continue

            for kind in ("shot", "muzzle", "hit"):
                address = f"Fx/Siege/{kind}_{wid}_{colour}"
                if address not in art:
                    errors.append(f"wards entry '{wid}' has no projectile reel at '{address}' - "
                                  "run Art > Bake Turret Projectiles")

        if f"Ui/Wards/{wid}" not in art:
            errors.append(f"wards entry '{wid}' has no shelf thumbnail at 'Ui/Wards/{wid}'")

        for key in (f"ward.{wid}.name", f"ward.{wid}.note"):
            if key not in keys:
                errors.append(f"wards entry '{wid}' needs loc key '{key}'")

        orders.append(int(entry.get("order") or 0))

    if orders and sorted(orders) != list(range(1, len(orders) + 1)):
        errors.append(f"wards orders are {sorted(orders)}; every entry needs its own rung from 1 "
                      "up, or the shelf reshuffles itself under a player on a retune")

    if not starter:
        errors.append("wards lists no free turret; a player who has bought nothing would stand an "
                      "empty line, and a siege with no line cannot be played")

    # **The upgrade ladder.** Three bands, four prices each, every one a real price - nought is how
    # `WardStars.PriceOf` says a turret is already at the top, so an authored nought would silently
    # top one out rather than make an upgrade free. It has to climb, too: a rung that costs no more
    # than the one below it is a rung nobody chooses between, which is invariant 5d on a price.
    ladder = (block.get("upgrades") or {}).get("tiers")

    if ladder is None:
        warnings.append("wards has no 'upgrades' block, so the built-in upgrade ladder stands - "
                        "which works, and cannot be retuned without a store review")
    elif len(ladder) != WARD_TIERS:
        errors.append(f"wards.upgrades lists {len(ladder)} band(s); the shelf is read in "
                      f"{WARD_TIERS}, so a missing row is a band nobody can upgrade")
    else:
        for band, row in enumerate(ladder, start=1):
            prices = (row or {}).get("prices") or []

            if len(prices) != WARD_STAR_STEPS:
                errors.append(f"wards.upgrades band {band} needs {WARD_STAR_STEPS} prices, one per "
                              f"upgrade, and lists {len(prices)}")
                continue

            for step, price in enumerate(prices):
                if price <= 0:
                    errors.append(f"wards.upgrades band {band} prices star {step + 2} at {price}; "
                                  "nought is how the table says a turret is already at the top, "
                                  "so it may not be a price")

            climbs = all(b > a for a, b in zip(prices, prices[1:]))

            if not climbs:
                errors.append(f"wards.upgrades band {band} is {prices}; an upgrade that costs no "
                              "more than the one below it is a rung nobody chooses")

    # **No two rungs of the shelf are the same turret**, which is the one thing about this roster
    # that reads as correct in every other gate. A magnitude nobody reads makes two rungs of one
    # ability identical - `rend` and the withdrawn `prism` both shipped that way, so a
    # thousand-gem breaker was exactly a four-thousand-credit cleaver and the dearer one bought
    # nothing (invariant 5d, on the one thing a player pays for). Priced rungs only: the free
    # turret is not a rung.
    same = {}

    for entry in models:
        gem = int(entry.get("gemPrice") or 0)
        coin = int(entry.get("coinPrice") or 0)

        if gem <= 0 and coin <= 0:
            continue

        shape = (entry.get("ability") or "none",
                 int(entry.get("magnitude") or 0),
                 int(entry.get("extent") or 0),
                 int(entry.get("power") or WARD_BASELINE),
                 int(entry.get("guard") or WARD_BASELINE))

        if shape in same:
            errors.append(f"wards entries '{same[shape]}' and '{entry.get('id')}' are the same "
                          f"turret - {shape[0]} at {shape[1]}/{shape[2]}, power {shape[3]}, guard "
                          f"{shape[4]} - so whichever is dearer buys nothing at all")
        else:
            same[shape] = entry.get("id")

    # The shelf read as one ladder. `WardCatalog.LadderProblem`, offline - and it is asked of the
    # *sorted* roster rather than of the file's order, because what it is about is the shelf a
    # player reads top to bottom.
    #
    # **Two rules, and both of them moved when the sequential unlock went.** A wall used to have
    # to climb *strictly*, because a rung was sealed until the one below it was held: reaching it
    # meant having met every wall under it, so a level a lower rung had already asked for could
    # never refuse anybody (invariant 5d). That argument went with the seal - a level of twenty
    # refuses everybody under twenty whatever stands beside it - so ties are legal now and only a
    # *fall* is refused, on the plainer ground that the shelf climbs by reach and by price, so a
    # wall that drops opens the dearer, further-reaching turret first (37ax).
    #
    # What replaces the strictness is the band: every wall has to stand in the stretch its own
    # header promises, which is the rule the removal left uncovered.
    highest, below = 0, None

    for entry in sorted(models, key=lambda e: int(e.get("order") or 0)):
        gem = int(entry.get("gemPrice") or 0)
        coin = int(entry.get("coinPrice") or 0)

        if gem <= 0 and coin <= 0:
            continue

        level = int(entry.get("minLevel") or 0)
        order = int(entry.get("order") or 0)

        if level < highest:
            errors.append(f"wards entry '{entry.get('id')}' asks for keeper level {level}, under "
                          f"the {highest} that '{below}' below it on the shelf asks for; the shelf "
                          "climbs by reach and by price, so a wall that falls opens the dearer "
                          "turret first")
            break

        band = 1
        for i, opens in enumerate(WARD_TIER_OPENS):
            if order >= opens:
                band = i + 1

        opens = WARD_TIER_GATES[band - 1]
        closes = (WARD_TOP_LEVEL if band == WARD_TIERS
                  else WARD_TIER_GATES[band] - 1)

        if level < opens or level > closes:
            errors.append(f"wards entry '{entry.get('id')}' stands in band {band}, which opens "
                          f"between keeper level {opens} and {closes}, and asks for {level}; a "
                          "band is the only thing saying when a stretch of the shelf opens now "
                          "that no rung is sealed behind another")
            break

        highest, below = level, entry.get("id")

    return errors, known


def art_on_disk():
    """Every sprite and reel this build carries, as addresses.

    A folder of numbered PNGs is a reel and answers under the folder's own name, which is how
    `AssetLibrary.Frames` reads one - so `Siege/Wards/bolt_r_fire` is a reel and
    `Siege/Wards/bolt_r` is a sprite, and both are addresses.
    """
    import pathlib

    root = pathlib.Path(ROOT).parent.parent.parent / "Assets" / "Game" / "Art"
    found = set()

    if not root.is_dir():
        return found

    for path in root.rglob("*.png"):
        rel = path.relative_to(root).with_suffix("")
        found.add(rel.as_posix())
        found.add(rel.parent.as_posix())

    return found


GOOD_KINDS = {"hearts", "heart_boost"}


def check_store(progression, keys, manifest=None):
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
        pass_id = entry.get("eventPassId") or ""

        # An event pass: the second non-currency thing a real-money product may grant, and it
        # passes invariant 18d for the same reason a heart capacity does. What it buys is
        # `owned: true` on one account/event document, so applying it twice is applying it
        # once and there is nothing for a save to remember. Mirrors StoreCatalog.Resolve and
        # seed-config.mjs, which is why the clauses are the same clauses in the same order.
        if pass_id and (not re.fullmatch(r"[a-z0-9_]{1,64}", pass_id)
                        or entry.get("kind") != "nonconsumable"
                        or credits or gems or capacity
                        or entry.get("shelf") != "event_pass"):
            errors.append(f"store product '{pid}' has an invalid event pass entitlement; a pass "
                          "is a nonconsumable on the 'event_pass' shelf that grants no currency "
                          "and no capacity, because the receipt buys permission to claim rather "
                          "than an amount")
        elif entry.get("shelf") == "event_pass" and not pass_id:
            errors.append(f"store product '{pid}' sits on the event pass shelf without naming an "
                          "event; nothing would ever be unlocked by buying it")

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

        if credits <= 0 and gems <= 0 and capacity <= 0 and not pass_id:
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

    # A season's pass used to be a store product, and this is where the two files were held
    # to naming each other. It is priced in gems now - one number in the manifest, with
    # nothing on the other side of it to drift from - so what is left is a check that no
    # product still claims to sell one, because such a product would take real money and
    # unlock nothing.
    for entry in products:
        if entry.get("eventPassId"):
            errors.append(f"store product '{entry.get('id')}' carries an event pass entitlement, "
                          "which nothing grants any more - a season's pass is bought with gems "
                          "(`passGems`), so this product would take real money and unlock nothing")

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

    # The daily chest ladder is not counted: it is retired in place on this build (see
    # DailyChests), still seeded so an older client's claims are priced, and paid to nobody
    # on the build this gate proves. Its successor is the tasks block below.

    rungs = ((progression.get("streak") or {}).get("rungs")) or []
    if rungs:
        credits += sum(r.get("amount", 0) for r in rungs if r.get("kind") == "credits") / len(rungs)
        gems += sum(r.get("amount", 0) for r in rungs if r.get("kind") == "gems") / len(rungs)

    # The tasks: every dealt task's chest at its tier's expectation, the daily slate over a
    # day and the weekly over seven. Averaged over the whole slate rather than one period's
    # deal, because which three are dealt rotates and the income is a figure about a player,
    # not about a Tuesday.
    tasks = progression.get("tasks") or {}
    tiers = {t.get("id"): t.get("chest") or {} for t in tasks.get("tiers") or []}
    per = tasks.get("activePerPeriod", 3)

    def expected(chest):
        c = g = 0.0
        for band in chest.get("guaranteed") or []:
            mid = (band.get("min", 0) + band.get("max", 0)) * 0.5
            if band.get("kind") == "credits":
                c += mid
            elif band.get("kind") == "gems":
                g += mid
        options = chest.get("options") or []
        total = sum(max(1, o.get("weight", 1)) for o in options) or 1
        for option in options:
            mid = (option.get("min", 0) + option.get("max", 0)) * 0.5
            share = max(1, option.get("weight", 1)) / total
            if option.get("kind") == "credits":
                c += mid * share
            elif option.get("kind") == "gems":
                g += mid * share
        return c, g

    for period, days in (("daily", 1), ("weekly", 7)):
        live = [t for t in tasks.get(period) or [] if not t.get("retired")]
        if not live:
            continue
        c = g = 0.0
        for task in live:
            tc, tg = expected(tiers.get(task.get("tier"), {}))
            c += tc
            g += tg
        credits += c / len(live) * min(per, len(live)) / days
        gems += g / len(live) * min(per, len(live)) / days

    return int(credits), int(gems)


BOARD_VECTORS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "board-vectors.json")
FALL_VECTORS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fall-vectors.json")

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
                                 ("standing", well.motes, case["standing"]),
                                 ("lenses", well.lenses, case["lenses"]),
                                 ("whorls", well.whorls, case["whorls"]),
                                 ("charged", sum(1 for g in well.glass if g), case["charged"])):
            if got != want:
                bad.append("%s: %s is %r, vectors say %r" % (name, label, got, want))

        if case["par"] > 0 and ways != case["ways"]:
            bad.append("%s: ways is %r, vectors say %r" % (name, ways, case["ways"]))

    # A vector set that has quietly lost its teeth is worse than none: it passes, it is printed
    # beside the word "ok", and nothing says the rule stopped being checked.
    covers = dict(chain=False, only_one=False, unsolvable=False, brim=False,
                  glass=False, glass_charged=False, glass_empty=False,
                  whorl=False, whorl_kindles=False, whorl_pair=False, whorl_closes=False,
                  whorl_contested=False)
    for case in cases:
        if case["par"] == 1 and case["standing"] >= 4:
            covers["chain"] = True
        if case["par"] > 0 and case["ways"] == 1:
            covers["only_one"] = True
        if case["proved"] and case["par"] < 0:
            covers["unsolvable"] = True
        if case["headroom"] == 0:
            covers["brim"] = True
        if case["lenses"] > 0:
            covers["glass"] = True
        # Glass that starts part full is the chapter's difficulty dial, and glass that starts
        # empty is what the dial is measured against. Losing either would leave the set unable
        # to notice the charge going away entirely.
        if case["charged"] > 0:
            covers["glass_charged"] = True
        if case["lenses"] > case["charged"]:
            covers["glass_empty"] = True

        # The whorl is the third chapter's whole subject, and each of these is a separate rule
        # that would otherwise go away in silence: that a whorl merges at all, that a merge can
        # reach white, that two on one board are two separate events, that one with nothing
        # beside it closes rather than waiting, and that a mote two whorls both reach is let go
        # by both - which is the clause that keeps the wave free of a reading order.
        if case["whorls"] > 0:
            covers["whorl"] = True
        if case.get("kindles"):
            covers["whorl_kindles"] = True
        if case["whorls"] > 1:
            covers["whorl_pair"] = True
        if case.get("closes"):
            covers["whorl_closes"] = True
        if case.get("contested"):
            covers["whorl_contested"] = True

    for what, held in covers.items():
        if not held:
            bad.append("no case covering '%s', so nothing here would notice that rule going away"
                       % what)

    for b in bad:
        errors.append("fall vectors: " + b)

    return (f"fall vectors: {len(cases)} case(s), the offline rules agree"
            if not bad else f"fall vectors: {len(bad)} disagreement(s)")


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

            check_story(lid, level.get("story"), keys)

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
                        {c["id"]: int(c.get("unlockCost") or 0) for c in live_companions},
                        live_companions)

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
                # Out of two: a lens filled the ordinary way fires sideways, and only a lens
                # another lens strikes fires all four (see `fall.blast`).
                glass = (f", {c['lenses']} lens(es) ({c['charged']} part-charged) aiming at "
                         f"{c['aim']} of 2, reaching {c['reach']}") if c.get('lenses') else ""

                # Whorls named separately, because they are paid for in the other currency:
                # a pane costs three drops of three colours in any order at all, and a whorl
                # costs one arrangement - two particular motes standing in two particular cells.
                # `kindled` is what says the arrangement was worth making.
                whorls = (f", {c['whorls']} whorl(s) ({c['kindled']} merge(s) reaching "
                          f"white)") if c.get('whorls') else ""

                held = (f"{c['fall_motes']} mote(s), {c['headroom']} headroom{glass}{whorls}, "
                        f"deals {c['deal']}")
            elif c['mode'] == 'siege':
                # A siege counts what is coming and what is holding it, because neither is a
                # reading of a search - there is no search (see check_siege).
                r = c['siege']
                boss = (f", a '{r['boss']}' {r['kind']} ({r['spell']}) last"
                        if r.get('boss') else "")
                cogs = f", cogs {r['cogs']}%" if r.get('cogs') else ""
                held = (f"{r['raiders']} raider(s) in {r['waves']} wave(s) "
                        f"({r['brutes']} brute(s), {r['colours']} colour(s)){boss} against "
                        f"{r['wards']} ward(s){cogs}, deals {c['deal']}")
            elif c['mode'] in MODE_RULES:
                held = f"{c['goals']} to finish"
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
        gem_land = f" and {grove['land_gems']} gem(s)" if grove["land_gems"] else ""
        print(f"       land: {grove['regions']} region(s), {grove['free_regions']} free, "
              f"{grove['owned_tiles']} tile(s) sellable - {grove['land']} credits{gem_land} "
              "to own it all")
        print(f"       land ladder: {grove['ladder_names']}")
        if grove["land_gems"]:
            print("       gem-priced land is worth nothing to a grove's score - the score is "
                  "the credits' worth of what is held (16g) and the server's clamp is "
                  "denominated in credits (19a)")
        print(f"       home ladder: {grove['homes']} rung(s), {grove['ladder']} credits to the top")
        # The rungs written out, because a gate is the half of this ladder that no other line
        # reports and the half most likely to be wrong: a level and a price together are what
        # make a rung a goal rather than a shelf (15a), and only the two side by side say so.
        for pid, cost, level in grove["home_rungs"]:
            gate = f"keeper level {level}" if level > 0 else "no gate"
            price = f"{cost} credits" if cost > 0 else "free"
            print(f"           {pid:<16} {price:>14}, {gate}")
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

    shop = check_store(progression, keys, manifest)
    per_day_credits, per_day_gems = daily_income(progression)

    errors.extend(check_hints(progression, warnings))
    errors.extend(check_wheel(progression, warnings))

    utility_errors, utilities = check_utilities(progression, keys, warnings)
    errors.extend(utility_errors)

    # The task slates. Their art and copy are derived from ids (a tier's icon and reel, a
    # task's title), so neither artnames.py nor loc.py can see them - and a chest naming a
    # utility that does not exist rolls, seeds and grants nothing.
    task_errors, tasks = check_tasks(progression, keys, utilities, art_on_disk(), warnings)
    errors.extend(task_errors)

    # The seasons. They name their tiers across two files that version independently, so
    # this is the only place a rung paying a chest nobody can price is visible at all.
    season_errors, seasons = check_seasons(manifest, progression, tasks, keys, warnings)
    errors.extend(season_errors)

    # The turret roster. Checked here rather than nowhere: its art addresses are *built* from an
    # id (`WardModel.ArtFor`), so `artnames.py` cannot see them, and its loc keys are derived from
    # one, so `loc.py` cannot either.
    ward_errors, wards = check_wards(progression, keys, warnings, art_on_disk())
    errors.extend(ward_errors)

    # The lanes. Their copy is derived from the track id, so `loc.py` cannot see it either - and
    # a lane with no ladder draws a whole screen out of strings nothing else names.
    errors.extend(check_tracks(manifest, keys))

    # The keeper walls. One integer in the manifest against every reward rule in
    # progression.json times every glade in the catalog - a sum neither file can do alone.
    wall_errors, keeper_walls, keeper_reach = check_keeper_walls(manifest, progression, warnings)
    errors.extend(wall_errors)

    if utilities:
        # Printed rather than merely checked, because a cooldown is a number nobody can read off
        # a running game and the whole bar's pacing is four of them side by side.
        print("")
        print(f"action bar: {len(utilities)} utility(ies)")

        for entry in sorted(((progression.get("utilities") or {}).get("items")) or [],
                            key=lambda e: e.get("order", 0)):
            cools = entry.get("cooldownSeconds", 0)
            price = entry.get("gemPrice", 0)
            print("       {0:<10} {1:<6} {2:>4}  {3}, hold up to {4}, {5}".format(
                entry.get("id", "?"), entry.get("kind", "?"), entry.get("magnitude", 0),
                f"{price} gem(s)" if price else "chests only",
                entry.get("maxHeld", 0),
                f"{cools}s cooldown" if cools else "no cooldown"))

    if wards:
        print("")
        print(f"turrets: {len(wards)} on the shelf, four colours each - "
              "a run loads the four a player stood on the line")

    if tasks:
        slates = ", ".join(f"{n} {period}" for period, n in sorted(tasks["slates"].items()))
        print("")
        print(f"tasks: {slates} on the slate, {tasks['per']} of each dealt a period, paying "
              f"{len(tasks['tiers'])} chest tier(s) ({', '.join(tasks['tiers'])})")
        worth = ", ".join(f"{tid} +{n}" for tid, n in tasks["marks"].items())
        print(f"       a claimed chest earns {worth} - about "
              f"{tasks['marksPerDay']:.1f} a day to a player who claims every one")

    if seasons:
        print("")
        for season in seasons:
            reach = season["days"] * (tasks or {}).get("marksPerDay", 0)
            print(f"season {season['id']}: {season['rungs']} rung(s) over {season['days']} day(s), "
                  f"topping at {season['top']} mark(s)"
                  + (f", pass {season['pass']} gems" if season['pass'] else ""))
            print(f"       about {int(reach)} mark(s) are dealt in that window, so the ladder "
                  f"is {'reachable' if reach >= season['top'] else 'NOT reachable'}")

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

        # Per **lane** - a mode and a track together - because a gate counts the chapter before
        # this one on the same ladder. The ladders never chain (invariant 20a), so the last
        # chapter of a lane gates nothing and the first of one is always open; and an endless
        # lane, whose waves never stop, must never gate an ordinary chapter on stars nobody can
        # earn, which is `CatalogIndex.ChaptersIn` answering the main track alone.
        lanes = {}
        for chapter in sorted(manifest.get("chapters", []), key=lambda c: c.get("order", 0)):
            key = (chapter.get("mode") or "glade", chapter.get("track") or "main")
            lanes.setdefault(key, []).append(chapter)

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

    # Printed whether or not anything is wrong, for the chapter gate's reason: a wall decides
    # whether a whole way of playing is on the screen, and nobody should have to open a JSON
    # file to find out where it stands.
    print()
    if not keeper_walls:
        print(f"keeper walls: none - every chapter opens on stars alone "
              f"(three stars everywhere reaches level {keeper_reach})")
    else:
        print(f"keeper walls: {len(keeper_walls)} chapter(s), against level {keeper_reach} "
              "reachable on three stars everywhere")
        for chapter, wall in keeper_walls:
            lane = f"{chapter.get('mode') or 'glade'}/{chapter.get('track') or 'main'}"
            print(f"       {chapter.get('id')} ({lane}) opens at keeper level {wall}"
                  + ("" if wall <= keeper_reach else " - out of reach of today's content"))

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
        taps = carry.get("taps", 4)
        if taps < 0:
            taps = 4
        moves = carry.get("moves", 4)
        if moves < 0:
            moves = 4
        wards = carry.get("wards", 4)
        if wards < 0:
            wards = 4

        # `ink`, `stones` and `tiles` are deliberately absent. They were Lightweave's,
        # Ripplewake's and Groovekeeper's units, all three modes are gone, and the fields are kept
        # in the DTO only so a published table still carrying the key does not read as malformed -
        # printing one here would say a mode this build cannot play is still priced.
        print(f"continue: {gems} gem(s) for +{turns} turn(s) on a glade, "
              f"+{motes} mote(s) on a well, +{taps} tap(s) on a grove, "
              f"+{moves} move(s) on a prototype board, "
              f"+{wards} ward(s) on a siege line")

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

        # Thornwatch is the one mode where that is not free. It is graded in matches and lost
        # when its ward line falls, so a run can reach its fail state well *under* the three-star
        # line - `RunContinue.Toll` charges the difference against the graded count, which is the
        # only reason the sentence above stays true there. Nothing offline can check it (the toll
        # is per level and per run); it is said here so that a retune of the star factors is read
        # with it in mind.
        print("       on a siege the line is the fail state and matches are the grade, so a "
              "continued run is charged up to the two-star line rather than already past it")

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

    for w in warnings:
        print("WARN  " + w)
    for e in errors:
        print("ERROR " + e)
    print(f"\n{len(summaries)} level(s): {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


sys.exit(main())
