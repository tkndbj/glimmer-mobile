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

#: `SiegeLayout.Cells` - everything a cell may hold, which is the four gems **and the cog**. A
#: second alphabet rather than a fifth letter, because a cog is not a colour: every rule that reads
#: a cell is asking either "what colour is this" or "what is standing here", and one alphabet would
#: answer the first with a thing that has none.
CELLS = "rgby*"

#: `SiegeLayout.Cog`.
COG = "*"

WARD_LETTERS = "rgby"
RAIDER_LETTERS = "rgbyRGBY"

#: `SiegeLayout.BossNames` - how a level names its boss: a **kind** and the colour it wears,
#: ``"warlord:r"``.
#:
#: It was one letter whose case said which of two bosses this was, which is exactly right for two
#: kinds and has nowhere to go for a third - and with four, the two warlords a chapter shipped were
#: told apart by nothing but hue. A boss is a way of fighting rather than a size, so a level says
#: which one out loud, and the retired one-letter form is refused rather than reinterpreted.
BOSS_NAMES = {
    "blightcaller": "blightcaller",
    "warlord": "warlord",
    "warbringer": "warbringer",
    "overlord": "overlord",
}

#: The colour a boss may wear. Lower case only - case no longer means anything.
BOSS_COLOURS = "rgby"

#: `SiegeLayout.MaxWards`, `.MaxRaiders`, `.MaxCogRate` and `SiegeTuning.MostCogs`.
MAX_WARDS = 4
MAX_RAIDERS = 60
MAX_COG_RATE = 20
MOST_COGS = 3

#: `SiegeTuning`. Every one of these is a constant on the C# side too - a level authors none of
#: them, so a retune is one edit in each of two files and the vectors below would catch a drift.
MIN_RUN = 3

#: `SiegeTuning.FuelPerGemTenths` and `.FuelPerShotTenths` - two fuel a gem against a bolt's one.
#:
#: **A gem buys two bolts, and it used to buy exactly one.** That identity was what let
#: `PERFECT_MATCH` read as "gems x damage x 2"; the wards were asked to shoot more, so a gem is
#: worth twice the fuel and a bolt half the damage - the same match, the same damage, twice as many
#: bolts. Subdividing the *fuel* rather than halving the *bolt* is what keeps `FUEL_SHOT_TENTHS`
#: and the whole rank ladder bit-identical.
FUEL_PER_GEM_TENTHS = 20
FUEL_PER_SHOT_TENTHS = 10

FUEL_PER_GEM = FUEL_PER_GEM_TENTHS / 10.0

#: `SiegeTuning.ShotDamage`. **Twenty rather than two, with every raider's health up by the same
#: ten**, so not one graded number moved - par is health over `PERFECT_MATCH` and both sides of that
#: division scaled together. What the scale buys is a *ten per cent* step, which is what a ward's
#: rank is worth and which two could not represent in integers.
#:
#: **Then ten, because the wards were asked to shoot more**: a gem now buys two bolts and each is
#: worth half, so a match delivers what it always did over twice as many of them. Ten is the floor
#: for the ten per cent step (10, 11, 12, 13, 14 is still exact and has nothing under it).
SHOT_DAMAGE = 10
WEAK_MULTIPLIER = 2

#: `SiegeTuning.MaxRank` - how many cogs one ward can take.
MAX_RANK = 4

# Fuel leaves a ward as a bolt and no other way - it used to fade on a
# clock and the rule was withdrawn after play. Nothing here reads it.
CREEPER_HEALTH = 200
BRUTE_HEALTH = 480
CREEPER_BLOW = 1
BRUTE_BLOW = 2
WARD_HEALTH = 14

#: `SiegeLayout.Shield` - written before a colour letter, it makes that raider a **bulwark**.
#:
#: A modifier rather than four more letters, because case already carries one axis (a capital is a
#: brute) and a one-character-per-raider alphabet has nowhere left to put a third kind. What it
#: costs is that a wave string's *length* is no longer its raider count, which is why `Layout`
#: parses each wave once into `coming` and nothing here counts characters any more.
SHIELD = "#"

#: `SiegeLayout.Web` / `Loot` - the two raiders that stop on the hill and work on the *field*.
#:
#: A weaver locks cells (a locked gem cannot be swapped and cannot line up); a thief takes gems away
#: altogether, leaving a sack that is no colour at all. Both are undone when the last of their kind
#: is dead, and neither takes any ward health - which is why `threatens` has to be asked rather
#: than assumed for a level that sends them.
WEB = "~"
LOOT = "$"

#: Every prefix a wave may carry, and the kind each one names - `SiegeLayout.Modifiers`.
#:
#: A table rather than three branches, which is what the third modifier bought: the shield shipped
#: as a special case in four places and every one of them would have had to be extended twice more,
#: in step, by hand.
MODIFIERS = {SHIELD: "bulwark", WEB: "weaver", LOOT: "thief"}

WAVE_LETTERS = RAIDER_LETTERS + "".join(MODIFIERS)

#: `SiegeLayout.Sack` - a cell holding a gem a thief has taken. Never authored, so it is not in
#: `CELLS`: a file carrying one is a file written against rules this build does not have.
SACK = "%"

#: `SiegeTuning.BulwarkHealth` / `BulwarkMarch` / `BulwarkBlow`, and what a shield soaks.
#:
#: A bulwark halves every bolt that is **not** its own colour and takes its own in full, so the
#: spread between feeding the right ward and any other is fourfold rather than the usual double.
#: It is slower than anything else that is not a boss, which is what it pays for the shield with.
BULWARK_HEALTH = 700
BULWARK_BLOW = 2
SHIELD_SOAK_TENTHS = 5

#: `SiegeTuning.WeaverHealth` / `ThiefHealth`. Between a creeper and a bulwark: every bolt spent on
#: one is a bolt not spent on something that can bring a ward down, so their health *is* the price
#: of answering them.
WEAVER_HEALTH = 620
THIEF_HEALTH = 540

#: `SiegeTuning`, one row per boss: health, what a spell takes off a ward, and what its spell
#: *does*.
#:
#: **Four verbs rather than four numbers, and that is the whole of what makes them four bosses.**
#: A `smite` takes a ward's health, a `douse` takes its fire, a `rally` takes the player's clock and
#: a `sunder` takes a rank they earned - so two of the four take no health at all, which is why
#: `threatens` stopped answering True for any siege that sends a boss.
BOSSES = {
    "blightcaller": {"health": 1100, "cast": 0, "spell": "douse"},
    "warlord": {"health": 1800, "cast": 3, "spell": "smite"},
    "warbringer": {"health": 2400, "cast": 2, "spell": "rally"},
    "overlord": {"health": 3200, "cast": 5, "spell": "sunder"},
}

BOSS_HEALTH = BOSSES["warlord"]["health"]
BOSS_CAST = BOSSES["warlord"]["cast"]
OVERLORD_HEALTH = BOSSES["overlord"]["health"]
OVERLORD_CAST = BOSSES["overlord"]["cast"]

#: `SiegeTuning.MatchGemsTenths` - gems an ordinary match clears, cascades included, in tenths.
#: Measured over a played run rather than reasoned about; see the C# side for why the reasoned
#: version was wrong and what pins this one.
MATCH_GEMS_TENTHS = 55

#: `SiegeTuning.PerfectMatch` - what one match delivers, in integer arithmetic.
#:
#: **Bolts rather than gems.** It read `gems x damage x 2`, which is the same thing only while a
#: gem buys exactly one bolt - an identity nothing said out loud and that every par in this mode
#: rested on. One division, at the end, so nothing truncates on the way.
PERFECT_MATCH = (MATCH_GEMS_TENTHS * FUEL_PER_GEM_TENTHS * SHOT_DAMAGE * WEAK_MULTIPLIER
                 // (FUEL_PER_SHOT_TENTHS * 10))


def tidy(raw, legal):
    return "".join(c for c in (raw or "") if c != " " and c in legal)


def is_gem(cell):
    """`SiegeLayout.IsGem` - something that can line up and is worth fuel. Never a cog, never a hole.

    **A predicate rather than the test written out four times**, which is exactly where a mode with
    a second kind of cell goes quietly wrong: `'' in 'rgby'` is True in Python, so a hole read as a
    gem the day `proto.py` forgot it (CLAUDE.md's own hard-won note), and a cog would line up with
    the cog beside it the day this one did.
    """
    return cell in LETTERS and cell != ""


def runs(cells, width, height):
    """Every cell standing in a run of three or more. Mirrors `SiegeLayout.Runs`."""
    hit = set()

    for y in range(height):
        run = 1
        for x in range(1, width + 1):
            same = (x < width and is_gem(cells[y * width + x])
                    and cells[y * width + x] == cells[y * width + x - 1])
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
            same = (y < height and is_gem(cells[y * width + x])
                    and cells[y * width + x] == cells[(y - 1) * width + x])
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

    def __init__(self, grid, deal, wards, waves, boss=None, cogs=0, endless=False):
        self.grid = grid
        self.endless = bool(endless)
        self.deal = tidy(deal, LETTERS)
        self.cogs = max(0, int(cogs or 0))
        self.wards = list(tidy(wards, WARD_LETTERS))
        self.waves = [w for w in (sweep(tidy(x, WAVE_LETTERS)) for x in (waves or [])) if w]

        # Exactly one legal name and one legal colour, or nothing - never `tidy`, which would
        # salvage an 'r' out of "dragon:r" and ship a warlord nobody authored. See `SiegeLayout`'s
        # constructor; the same clause refuses the retired one-letter form.
        self.boss_kind, self.boss = named_boss(boss)
        self.boss_wave = -1

        # The warlord is *appended* rather than authored into a wave - the last wave is the boss
        # wave by rule, so it can neither be put in the middle of a siege nor left off the end of
        # one. See `SiegeLayout.Boss`.
        if self.boss:
            self.boss_wave = len(self.waves)
            self.waves = self.waves + [self.boss]

        # Every wave parsed once into (colour, kind) pairs. Nothing below asks a wave's text
        # how many raiders it holds - see `SHIELD`.
        self.coming = [read_wave(w, self.boss_kind, i == self.boss_wave)
                       for i, w in enumerate(self.waves)]

        # `SiegeLayout.Hash` - FNV-1a over the authored field, 32-bit throughout. Where the
        # refill stream starts, and (on an endless lane) what deals every wave.
        h = 2166136261
        for c in (grid.cells if grid is not None else ()):
            h = ((h ^ ord(c)) * 16777619) & 0xFFFFFFFF
        self.seed = h or 1

        self.fault = self._check(boss)

    def _check(self, boss):
        if self.grid is None:
            return "no field"

        if boss and boss.strip() and not self.boss:
            return ("'%s' is not a boss this mode knows; a boss is written as a kind and the "
                    "colour it wears (%s, colour one of '%s'), and an empty field is how a siege "
                    "says it sends none"
                    % (boss, ", ".join("%s:<colour>" % n for n in BOSS_NAMES), BOSS_COLOURS))

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

        if self.cogs > MAX_COG_RATE:
            return ("this field deals a cog %d times in a hundred; %d is the most a level may ask "
                    "for, and past it the line is upgraded whatever the player does"
                    % (self.cogs, MAX_COG_RATE))

        standing = sum(1 for c in self.grid.cells if c == COG)
        if standing > MOST_COGS:
            return ("this field is authored with %d cogs standing on it; %d is the most that may "
                    "ever be on the board at once, so the rest could never be dealt back in"
                    % (standing, MOST_COGS))

        for ward in self.wards:
            if ward not in self.deal:
                return ("the '%s' ward stands on a field that never deals a '%s' gem, so nothing "
                        "the player does could ever fuel it" % (ward, ward))

        # An endless lane authors no waves - the muster is a rule (`endless_wave`) - so the two
        # clauses that walk the authored list have nothing to walk. The field is still proved
        # settled below, which is the one thing that matters either way.
        if self.endless:
            return self._settled()

        if not self.waves:
            return "nothing is coming, so there is nothing to hold"

        raiders = self.raiders
        if raiders > MAX_RAIDERS:
            return ("this level sends %d raiders; %d is the most a run may hold"
                    % (raiders, MAX_RAIDERS))

        for w, line in enumerate(self.coming):
            for colour, kind in line:
                if colour in self.wards:
                    continue
                what = self.boss_kind if w == self.boss_wave else kind
                return ("wave %d sends a '%s' %s and no ward on this line carries '%s', "
                        "so nothing here is strong against it" % (w + 1, colour, what, colour))

        return self._settled()

    def _settled(self):
        if runs(self.grid.cells, self.grid.w, self.grid.h):
            return ("three alike are already touching on this field, so it would go off before "
                    "anybody had moved a gem - a field is authored settled")

        return None

    @property
    def raiders(self):
        return sum(len(line) for line in self.coming)


def sweep(wave):
    """Drops any modifier that has no colour letter after it - `SiegeLayout.Sweep`."""
    kept = []
    for i, ch in enumerate(wave):
        if ch in MODIFIERS and (i + 1 >= len(wave) or wave[i + 1] in MODIFIERS):
            continue
        kept.append(ch)
    return "".join(kept)


def read_wave(wave, boss_kind, boss):
    """One authored wave, as (colour, kind) pairs - `SiegeLayout.Read`."""
    made = []
    i = 0
    while i < len(wave):
        mark = wave[i] if wave[i] in MODIFIERS else None
        if mark is not None:
            i += 1
            if i >= len(wave):
                break

        letter = wave[i]
        kind = (boss_kind if boss
                else MODIFIERS[mark] if mark is not None
                else "brute" if letter.isupper() else "creeper")
        made.append((letter.lower(), kind))
        i += 1

    return made


def named_boss(token):
    """Reads ``"warlord:r"``, and answers ``(None, None)`` for anything that is not that shape.

    Mirrors `SiegeLayout.Named`: no trimming inside the halves and no case folding, because a token
    is either what a build of this game writes or it is content from somewhere else.
    """
    token = (token or "").strip()
    if ":" not in token:
        return None, None

    name, _, colour = token.partition(":")
    if name not in BOSS_NAMES or len(colour) != 1 or colour not in BOSS_COLOURS:
        return None, None

    return BOSS_NAMES[name], colour


def health_of(kind):
    """`SiegeTuning.HealthOf`. One table, keyed on what the layout says the raider is."""
    if kind in BOSSES:
        return BOSSES[kind]["health"]
    if kind == "bulwark":
        return BULWARK_HEALTH
    if kind == "weaver":
        return WEAVER_HEALTH
    if kind == "thief":
        return THIEF_HEALTH
    return BRUTE_HEALTH if kind == "brute" else CREEPER_HEALTH


def kind_at(layout, wave, index):
    """`SiegeLayout.KindAt` - the wave decides, never the letter."""
    if wave == layout.boss_wave:
        return layout.boss_kind

    return layout.coming[wave][index][1]


def par(layout):
    """`SiegeTuning.Par` - what the level sends, over the most one match could ever be worth."""
    health = 0
    for w, line in enumerate(layout.coming):
        for i in range(len(line)):
            health += health_of(kind_at(layout, w, i))

    return max(1, -(-health // PERFECT_MATCH))


#: `SiegeValidator.SwingsBeforeAnswered`. **Two, and one was wrong.** A raider that reaches the
#: line goes on swinging every `SiegeTuning.BlowEvery` until something kills it, so counting a
#: single blow each named half a shipped chapter as unlosable when every one of those rungs bleeds
#: the line in play. What actually measures the threat is the hold simulation in
#: `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine`.
SWINGS_BEFORE_ANSWERED = 2


def endangers(kind):
    """`SiegeTuning.EndangersTheLine` - whether this boss can bring a ward down, given long enough.

    **One of the four cannot.** A blightcaller takes a ward's fire rather than its health, so a
    level whose only threat were one could not be lost. It was two while the warbringer took ground
    instead of health, which was withdrawn after play.
    """
    row = BOSSES.get(kind)
    return bool(row) and row["cast"] > 0


def threatens(layout):
    """Whether one wave could ever fell a ward. Mirrors `SiegeValidator.Threatens`."""
    if layout.boss and endangers(layout.boss_kind):
        return True

    for w, line in enumerate(layout.coming):
        if w == layout.boss_wave:
            continue
        blow = sum(blow_of(kind) for _, kind in line)
        if blow * SWINGS_BEFORE_ANSWERED >= WARD_HEALTH:
            return True
    return False


def blow_of(kind):
    """`SiegeTuning.BlowOf`. Nought for anything that never reaches the line.

    A boss holds the middle of the hill and casts from there; a weaver and a thief hold it and work
    on the *field*. Neither swings, which is why `threatens` cannot assume a hill full of raiders is
    a hill that can bring a ward down.
    """
    if kind in BOSSES or kind in ("weaver", "thief"):
        return 0
    if kind == "bulwark":
        return BULWARK_BLOW
    return BRUTE_BLOW if kind == "brute" else CREEPER_BLOW


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

            # A cog may be *swapped* like any other cell - it simply never lines up itself, which
            # `runs` already knows. Refusing to move one here would be a second opinion about that.
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
    brutes = bulwarks = 0
    for w, line in enumerate(layout.coming):
        for colour, kind in line:
            colours.add(colour)
            if w == layout.boss_wave:
                continue
            if kind == "brute":
                brutes += 1
            elif kind == "bulwark":
                bulwarks += 1

    standing = sum(1 for c in layout.grid.cells if c == COG)

    weavers = sum(1 for line in layout.coming for _, k in line if k == "weaver")
    thieves = sum(1 for line in layout.coming for _, k in line if k == "thief")

    return dict(waves=len(layout.waves), raiders=layout.raiders, brutes=brutes,
                bulwarks=bulwarks, weavers=weavers, thieves=thieves,
                colours=len(colours), wards=len(layout.wards),
                boss=layout.boss or "",
                kind=layout.boss_kind or "",
                spell=BOSSES[layout.boss_kind]["spell"] if layout.boss_kind else "",
                cogs=layout.cogs, stood=standing,
                threat=1 if threatens(layout) else 0,
                swap=1 if any_swap(layout) else 0)


# --------------------------------------------------------------------------- the endless lane
#: `SiegeEndless` - a siege whose waves never stop, as a pure function of the wave number.
#:
#: **Mirrored here for the reason every rule in this project that exists twice is**: the offline
#: gate has to be able to say what wave forty sends without running Unity, and a ramp that only
#: existed in C# would be a ramp nothing checked. What is *not* mirrored is the run itself - an
#: endless lane has no search and no par to derive (invariant 37a), so what a gate can prove about
#: one is that its field is playable, its line is legal and its ramp sends nothing the line cannot
#: answer.
BOSS_EVERY = 4
PAIRS_AFTER = 16
PAIR_EVERY = 5

ENDLESS_BOSSES = ("blightcaller", "warlord", "warbringer", "overlord")

ENDLESS_PAIRS = ((0, 1), (0, 2), (0, 3), (1, 2), (1, 3), (2, 3))

HEALTH_STEP_TENTHS = 12
BLOW_STEP_TENTHS = 4

FIRST_WAVE, MOST_RAIDERS = 5, 22

BRUTES_FROM, BULWARKS_FROM, WEAVERS_FROM, THIEVES_FROM = 3, 6, 9, 13


def is_boss_wave(wave):
    """`SiegeEndless.IsBossWave` - `wave` is 1-based."""
    if wave < 1:
        return False
    if wave <= PAIRS_AFTER:
        return wave % BOSS_EVERY == 0
    return (wave - PAIRS_AFTER) % PAIR_EVERY == 0


def bosses_at(wave):
    """`SiegeEndless.BossesAt` - none, one, or two."""
    if not is_boss_wave(wave):
        return []

    if wave <= PAIRS_AFTER:
        return [ENDLESS_BOSSES[(wave // BOSS_EVERY - 1) % len(ENDLESS_BOSSES)]]

    step = (wave - PAIRS_AFTER) // PAIR_EVERY - 1
    a, b = ENDLESS_PAIRS[step % len(ENDLESS_PAIRS)]
    return [ENDLESS_BOSSES[a], ENDLESS_BOSSES[b]]


def surge_at(wave):
    """`SiegeEndless.SurgeAt` - (health tenths, blow tenths)."""
    wave = max(1, wave)
    return (10 + (wave - 1) * HEALTH_STEP_TENTHS, 10 + (wave - 1) * BLOW_STEP_TENTHS)


def _roll(seed, at):
    """`SiegeEndless.Roll` - a hash rather than a stream, so each wave is dealt on its own."""
    h = (seed ^ 2166136261) & 0xFFFFFFFF
    h = (h ^ (at & 0xFFFFFFFF)) & 0xFFFFFFFF
    h = (h * 16777619) & 0xFFFFFFFF
    h ^= h >> 15
    h = (h * 2246822519) & 0xFFFFFFFF
    h ^= h >> 13
    return h & 0xFFFFFFFF


def _share(wave, frm, ceiling):
    grown = (wave - frm) * 4
    return ceiling if grown > ceiling else grown


def _colour(colours, seed, wave, i):
    return colours[_roll(seed, (wave * 977 + i * 61) & 0xFFFFFFFF) % len(colours)]


#: The three that can bring a ward down - `SiegeTuning.EndangersTheLine`. A blightcaller takes a
#: ward's *fire* and never its health, so it is the one boss an empty hill makes harmless.
BITES = ("warlord", "warbringer", "overlord")


def endless_wave(colours, seed, wave):
    """`SiegeEndless.WaveAt` - what wave `wave` (1-based) sends, as (colour, kind) pairs."""
    wave = max(1, wave)
    bosses = bosses_at(wave)

    size = min(MOST_RAIDERS, MAX_RAIDERS, FIRST_WAVE + (wave - 1) // 2)

    if bosses:
        made = [(_colour(colours, seed, wave, i), kind) for i, kind in enumerate(bosses)]

        # A boss that takes no health arrives with an escort, or it is a wave nothing can go
        # wrong on - see `SiegeEndless.WaveAt`.
        if any(kind in BITES for kind in bosses):
            return made

        escort = max(0, min(size, MAX_RAIDERS - len(bosses)))

        for i in range(len(bosses), len(bosses) + escort):
            made.append((_colour(colours, seed, wave, i), _kind_at(wave, i, seed)))

        return made

    made = []

    for i in range(size):
        made.append((_colour(colours, seed, wave, i), _kind_at(wave, i, seed)))

    return made


def _kind_at(wave, i, seed):
    """`SiegeEndless.KindAt`."""
    roll = _roll(seed, (wave * 131 + i * 17) & 0xFFFFFFFF)

    if wave >= THIEVES_FROM and i == 0:
        return "thief"
    if wave >= WEAVERS_FROM and i == 1:
        return "weaver"
    if wave >= BULWARKS_FROM and roll % 100 < _share(wave, BULWARKS_FROM, 30):
        return "bulwark"
    if wave >= BRUTES_FROM and roll % 100 < _share(wave, BRUTES_FROM, 55):
        return "brute"
    return "creeper"
