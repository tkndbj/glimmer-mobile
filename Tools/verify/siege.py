# -*- coding: utf-8 -*-
"""Thornwatch's rules, mirrored offline.

`SiegeBoard.cs` / `SiegeMode.cs` in C#; this file in Python. The content gate has to run with no
Unity anywhere, and a rule that only exists inside the Editor is a rule nobody checks on the way
past.

**What is mirrored here is deliberately less than every other mode's mirror, and the reason is the
mode.** Everywhere else the offline copy re-implements the *board* so that par can be searched and
compared against the shipped C# answer; a siege has no search, because raiders walk while nobody is
touching the board and the field refills. So what is proved offline is exactly what is provable
about the file: the layout's refusals, the arithmetic par, and the readings a validator can act on.

**Where the two are allowed to differ: nowhere in the arithmetic.** Par decides both star lines, so
a divergence between `SiegeTuning.Par` and `par()` below would grade a level differently on a phone
and on a build machine - the class of failure that is silent in the way this project keeps paying
for.

Read `SiegeBoard.cs` first; the names match deliberately.
"""

LETTERS = "rgby"
WARD_LETTERS = "rgby"
RAIDER_LETTERS = "rgbyRGBY"

#: `SiegeLayout.MaxWards` and `.MaxRaiders`.
MAX_WARDS = 4
MAX_RAIDERS = 60

#: `SiegeTuning`. Every one of these is a constant on the C# side too - a level authors none of
#: them, so a retune is one edit in each of two files and the vectors below would catch a drift.
MIN_RUN = 3
FUEL_PER_GEM = 1
SHOT_DAMAGE = 2
WEAK_MULTIPLIER = 2

# Fuel leaves a ward as a bolt and no other way - it used to fade on a
# clock and the rule was withdrawn after play. Nothing here reads it.
CREEPER_HEALTH = 20
BRUTE_HEALTH = 48
CREEPER_BLOW = 1
BRUTE_BLOW = 2
WARD_HEALTH = 10

#: `SiegeTuning.MatchGemsTenths` - gems an ordinary match clears, cascades included, in tenths.
#: Measured over a played run rather than reasoned about; see the C# side for why the reasoned
#: version was wrong and what pins this one.
MATCH_GEMS_TENTHS = 55

#: `SiegeTuning.PerfectMatch` - what one match delivers, in integer arithmetic.
PERFECT_MATCH = MATCH_GEMS_TENTHS * SHOT_DAMAGE * WEAK_MULTIPLIER // 10


def tidy(raw, legal):
    return "".join(c for c in (raw or "") if c != " " and c in legal)


def runs(cells, width, height):
    """Every cell standing in a run of three or more. Mirrors `SiegeLayout.Runs`."""
    hit = set()

    for y in range(height):
        run = 1
        for x in range(1, width + 1):
            same = x < width and cells[y * width + x] == cells[y * width + x - 1]
            if same:
                run += 1
                continue
            if run >= MIN_RUN:
                for k in range(x - run, x):
                    hit.add(y * width + k)
            run = 1

    for x in range(width):
        run = 1
        for y in range(1, height + 1):
            same = y < height and cells[y * width + x] == cells[(y - 1) * width + x]
            if same:
                run += 1
                continue
            if run >= MIN_RUN:
                for k in range(y - run, y):
                    hit.add(k * width + x)
            run = 1

    return hit


class Layout(object):
    """`SiegeLayout`. `fault` is None when the level is readable, and the sentence when it is not."""

    def __init__(self, grid, deal, wards, waves):
        self.grid = grid
        self.deal = tidy(deal, LETTERS)
        self.wards = list(tidy(wards, WARD_LETTERS))
        self.waves = [w for w in (tidy(x, RAIDER_LETTERS) for x in (waves or [])) if w]
        self.fault = self._check()

    def _check(self):
        if self.grid is None:
            return "no field"

        if not (2 <= len(self.wards) <= MAX_WARDS):
            return ("a ward line holds 2 to %d wards; this one names %d"
                    % (MAX_WARDS, len(self.wards)))

        for i in range(len(self.wards)):
            for j in range(i + 1, len(self.wards)):
                if self.wards[i] == self.wards[j]:
                    return ("two wards on this line are both '%s', so one of them can never be "
                            "the only thing a colour feeds and half the line is a spare part"
                            % self.wards[i])

        if len(self.deal) < 2:
            return ("the field refills from fewer than two colours, so every arrangement is a "
                    "match and nothing is ever decided")

        for ward in self.wards:
            if ward not in self.deal:
                return ("the '%s' ward stands on a field that never deals a '%s' gem, so nothing "
                        "the player does could ever fuel it" % (ward, ward))

        if not self.waves:
            return "nothing is coming, so there is nothing to hold"

        raiders = sum(len(w) for w in self.waves)
        if raiders > MAX_RAIDERS:
            return ("this level sends %d raiders; %d is the most a run may hold"
                    % (raiders, MAX_RAIDERS))

        for w, wave in enumerate(self.waves):
            for token in wave:
                if token.lower() not in self.wards:
                    return ("wave %d sends a '%s' raider and no ward on this line carries '%s', "
                            "so nothing here is strong against it" % (w + 1, token.lower(),
                                                                      token.lower()))

        if runs(self.grid.cells, self.grid.w, self.grid.h):
            return ("three alike are already touching on this field, so it would go off before "
                    "anybody had moved a gem - a field is authored settled")

        return None

    @property
    def raiders(self):
        return sum(len(w) for w in self.waves)


def par(layout):
    """`SiegeTuning.Par` - what the level sends, over the most one match could ever be worth."""
    health = 0
    for wave in layout.waves:
        for token in wave:
            health += BRUTE_HEALTH if token.isupper() else CREEPER_HEALTH

    return max(1, -(-health // PERFECT_MATCH))


def threatens(layout):
    """Whether one wave could ever fell a ward. Mirrors `SiegeValidator.Threatens`."""
    for wave in layout.waves:
        blow = sum(BRUTE_BLOW if t.isupper() else CREEPER_BLOW for t in wave)
        if blow >= WARD_HEALTH:
            return True
    return False


def any_swap(layout):
    """Whether the field as dealt has a swap that lines anything up.

    Certain, and the one thing about a siege's *field* that can be wrong in the file: a field with
    no opening move is one the player watches themselves lose, because this mode's clock does not
    stop. The board reshuffles rather than locking, but a level authored into that state is a level
    whose first second is spent redealing.
    """
    grid = layout.grid
    cells = list(grid.cells)

    for y in range(grid.h):
        for x in range(grid.w):
            here = y * grid.w + x
            for other in ((here + 1) if x + 1 < grid.w else None,
                          (here + grid.w) if y + 1 < grid.h else None):
                if other is None or cells[here] == cells[other]:
                    continue
                cells[here], cells[other] = cells[other], cells[here]
                lined = bool(runs(cells, grid.w, grid.h))
                cells[here], cells[other] = cells[other], cells[here]
                if lined:
                    return True

    return False


def readings(layout):
    """The handful of numbers only this mode's own rules can give."""
    colours = set()
    brutes = 0
    for wave in layout.waves:
        for token in wave:
            colours.add(token.lower())
            if token.isupper():
                brutes += 1

    return dict(waves=len(layout.waves), raiders=layout.raiders, brutes=brutes,
                colours=len(colours), wards=len(layout.wards),
                threat=1 if threatens(layout) else 0,
                swap=1 if any_swap(layout) else 0)
