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

#: `SiegeLayout.Cells` - everything a cell may hold, which is the four gems and nothing else.
#:
#: **The field is gems again.** It carried the cog as a second alphabet for as long as a cog stood
#: *on the field*; a cog is dropped by a felled raider onto the hill now, so the question "what is
#: standing here" has no second answer and the split went with the thing that needed it.
CELLS = "rgby"

#: `SiegeLayout.RetiredCog` - refused by name rather than ignored (invariant 5f). A body carrying
#: one was authored for a build that no longer exists, and reading it as a gem would ship a field
#: nobody composed.
RETIRED_COG = "*"

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
    "gravemaw": "gravemaw",
    "bonecaller": "bonecaller",
    "shackler": "shackler",
    "ironclad": "ironclad",
}

#: The colour a boss may wear. Lower case only - case no longer means anything.
BOSS_COLOURS = "rgby"

#: `SiegeLayout.MaxWards`, `.MinWards`, `.MaxRaiders`, `.MaxCogRate` and `SiegeTuning.MostCogs`.
#:
#: **Three wards is the floor and it is a measurement.** Under the colour lock a field deals
#: exactly the colours its line stands, so the ward count *is* the colour count - and a two-colour
#: match-three is not a board: over twenty thousand dealt seeds not one two-colour field is
#: settled, and a match on one clears 202 gems against a four-colour field's 5.6.
#:
#: **And the cog rate counts kills rather than dealt gems**, so a hundred is the honest ceiling:
#: a level sends a few dozen raiders, and what bounds the mechanic is the rank ladder and
#: `MOST_COGS` rather than this.
MAX_WARDS = 4
MIN_WARDS = 3
MAX_RAIDERS = 60
MAX_COG_RATE = 100
MOST_COGS = 3

#: `SiegeTuning`. Every one of these is a constant on the C# side too - a level authors none of
#: them, so a retune is one edit in each of two files and the vectors below would catch a drift.
MIN_RUN = 3

#: `SiegeTuning.FuelPerGemTenths` and `.FuelPerShotTenths` - one bolt a gem, out of two fuel each.
#:
#: **A gem bought one bolt, then two, and now one again.** That first identity was what let
#: `PERFECT_MATCH` read as "gems x damage x 2"; the wards were asked to shoot more, so a gem became
#: worth twice the fuel and a bolt half the damage - the same match, the same damage, twice as many
#: bolts.
#:
#: **Then the mode came back as too fast paced, and a bolt got twice as heavy and twice as dear.**
#: The cadence doubled with it (`SiegeTuning.FireEvery`), so the wards fire half as often for
#: exactly the same output: same damage a match, same seconds a match, same par. Slowing the
#: cadence *alone* is the version that is not available - it cuts the line's damage a second and
#: loses the warbringer rung outright.
#:
#: **Both of these must be multiples of ten.** A rank is ten per cent off the bolt's fuel, so a
#: base that is not a multiple of ten truncates and some of the four cogs a player spends buy
#: nothing.
FUEL_PER_GEM_TENTHS = 20
FUEL_PER_SHOT_TENTHS = 20

FUEL_PER_GEM = FUEL_PER_GEM_TENTHS / 10.0

#: `SiegeTuning.ShotDamage`. **Twenty rather than two, with every raider's health up by the same
#: ten**, so not one graded number moved - par is health over `PERFECT_MATCH` and both sides of that
#: division scaled together. What the scale buys is a *ten per cent* step, which is what a ward's
#: rank is worth and which two could not represent in integers.
#:
#: **Then ten, because the wards were asked to shoot more**: a gem bought two bolts and each was
#: worth half, so a match delivered what it always did over twice as many of them. Ten was the
#: floor for the ten per cent step (10, 11, 12, 13, 14 is exact and has nothing under it).
#:
#: **And twenty again, because the mode came back as too fast paced.** A bolt is twice as heavy,
#: costs twice the fuel and leaves at twice the interval, so the line's output is untouched and
#: only the number of bolts moves. A bolt's weight and a bolt's cost have to move *together*:
#: moving this one alone halves or doubles every par in the mode, which is the identity
#: `PERFECT_MATCH` exists to say out loud.
SHOT_DAMAGE = 20
WEAK_MULTIPLIER = 2

#: `SiegeTuning.MaxRank` - how many cogs one ward can take.
MAX_RANK = 4

# Fuel leaves a ward as a bolt and no other way - it used to fade on a
# clock and the rule was withdrawn after play. Nothing here reads it.
CREEPER_HEALTH = 200
BRUTE_HEALTH = 480
CREEPER_BLOW = 1
BRUTE_BLOW = 2
#: `SiegeTuning.WardHealth` - what the **starter** holds, which is what a level is authored
#: against. A bought turret may now trade toughness for a heavier bolt (`WardModel.GuardTenths`),
#: and this deliberately does not follow it: `threatens` asks whether the *level* can take a line
#: down, and a level is judged against the line every player already has rather than against the
#: flimsiest one somebody could choose. What measures a chosen line is the hold simulation in
#: `SiegeRuleTests`, which plays both the starter and the flimsiest turret in the roster.
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

#: `SiegeLayout.Drop` - a bomber. It stands on the field like a weaver and a thief and drops
#: bombs onto cells; what a bomb is worth is the player's, so nothing about it is read here
#: beyond its health and the fact that it takes no ward health.
DROP = "!"

#: Every prefix a wave may carry, and the kind each one names - `SiegeLayout.Modifiers`.
#:
#: A table rather than three branches, which is what the third modifier bought: the shield shipped
#: as a special case in four places and every one of them would have had to be extended twice more,
#: in step, by hand.
MODIFIERS = {SHIELD: "bulwark", DROP: "bomber"}

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
#: Retired with the weaver and the thief (invariant 40h). Kept as a note so a file carrying
#: `~` or `$` is recognisably written against rules this build does not have.

#: `SiegeTuning.BomberHealth`. Dearer than either, because a bomb is worth having and the answer
#: to all three is the same single ward.
BOMBER_HEALTH = 200

#: `SiegeTuning`, one row per boss: health, what a spell takes off a ward, and what its spell
#: *does*.
#:
#: **Four verbs rather than four numbers, and that is the whole of what makes them four bosses.**
#: A `smite` takes a ward's health, a `douse` takes its fire, a `rally` takes the player's clock and
#: a `sunder` takes a rank they earned - so two of the four take no health at all, which is why
#: `threatens` stopped answering True for any siege that sends a boss.
#:
#: **All four went up fifteen per cent when the bosses stopped having a colour to hide behind**
#: (`SiegeTuning.EveryWardReaches`): every ward answers a boss now, and a part-weight bolt costs a
#: part of the fuel, so a duel converts a match at the same rate a wave does and par is as honest
#: over one as over the other. They were 1100, 1800, 2400 and 3200, scaled together so the
#: relationships they were tuned on survive. **This table is a mirror**: it moves in the same
#: change as the C# or every par printed here is a different game's.
#: **Six verbs now, and the two new ones take neither health nor fire.** A `devour` takes the cogs
#: and bombs lying on the hill and a `raise` puts fresh creepers on it - so `cast` is nought for
#: both, exactly as it is for a douse, and `endangers` can no longer be read off that column alone
#: (see below).
BOSSES = {
    "blightcaller": {"health": 1250, "cast": 0, "spell": "douse"},
    "warlord": {"health": 2050, "cast": 3, "spell": "smite"},
    "warbringer": {"health": 2750, "cast": 1, "spell": "rally"},
    "overlord": {"health": 3650, "cast": 5, "spell": "sunder"},
    "gravemaw": {"health": 1500, "cast": 0, "spell": "devour"},
    "bonecaller": {"health": 3000, "cast": 0, "spell": "raise"},

    #: The fourth chapter's two, and between them they take the one thing the first six leave.
    #:
    #: A **shackler** chains a ward - it keeps its fuel, its rank and its charges and cannot fire
    #: for `SiegeTuning.ShacklerBind` seconds - so its `cast` is nought exactly as a douse's and a
    #: devour's are, and `endangers` answers False for it: a rung whose only threat were one could
    #: not be lost, so it rides the last authored wave (invariant 37ad).
    #:
    #: An **ironclad** is the one boss in this mode that is *not* answered by the whole line
    #: (`SiegeTuning.EveryWardReaches`): only the ward wearing its colour will fire at it, and the
    #: other three bank and dump overcharges instead. **None of that reaches par**, which is why
    #: there is no third column here for it - every bolt and every charge that lands on it lands at
    #: full weight, which is exactly what `PERFECT_MATCH` already assumes of every gem. What its
    #: aegis really costs is the *clock*, and this mode's fail state is a clock rather than a move
    #: budget (invariant 37b), so its health is set under an overlord's to pay for it.
    "shackler": {"health": 1700, "cast": 0, "spell": "bind"},
    "ironclad": {"health": 2600, "cast": 2, "spell": "aegis"},
}

#: `SiegeTuning.RaiseSize` and `.Raises` - how many creepers one raise puts on the hill, and how
#: many times a bonecaller may do it.
#:
#: **The cap is what lets this mode keep a par at all.** Par is the hill's health over the most one
#: match could deliver, so a boss that could add bodies for as long as it lived would make par a
#: function of how the player played. `par` counts every one of the twelve whether it is ever
#: raised or not, which overstates a run that kills the boss early - the direction invariant 22
#: says to err in.
RAISE_SIZE, RAISES = 4, 3

#: `SiegeTuning.ChapterToughStep`, `.ToughFrom` and `.MostTough` - how much tougher each chapter's
#: raiders are than the chapter before it, in tenths.
#:
#: **The one lever a fourth, fifth and sixth chapter has**, because the hill fills up long before
#: difficulty runs out: everything else a chapter can vary is composition, and by the third it is
#: already four-wave rungs with two shields in them. What it costs the player is the clock - a hill
#: with more health walks the same distance in the same time, so the line has to kill faster, which
#: is what a bought turret does. It does **not** make three stars harder: par rises with it and the
#: star lines rise with par.
#:
#: **Health only; a blow is never surged**, and **the first two chapters are the baseline**.
CHAPTER_TOUGH_STEP, TOUGH_FROM, MOST_TOUGH = 1, 2, 40


def toughness_for(ordinal):
    """`SiegeTuning.ToughnessFor` - what a chapter at this ordinal deals, in tenths."""
    return 10 if ordinal < TOUGH_FROM else 10 + (ordinal - TOUGH_FROM + 1) * CHAPTER_TOUGH_STEP


def surged(health, tenths):
    """`SiegeSurge.Health` - this kind's health, surged."""
    return health * (10 if tenths < 10 else tenths) // 10

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


#: What a siege authors as its three-star and two-star lines, as factors on par.
#:
#: **Under one, which no other mode in this game needs, and it is a measurement rather than a
#: taste.** Everywhere else par is a *search* - the shortest solution - so a run cannot beat it and
#: a gold line above one is the only shape that makes sense. A siege has nothing to search
#: (invariant 37a), so its par is arithmetic: the hill's health over what one match could ideally
#: be worth. That is not a floor. Bombs, cogs, an overcharge and the elemental double all deliver
#: more than the formula credits, so real runs come in **under** it - measured across 270 swept
#: runs of all three chapters, every finished one spent between **49% and 100% of par**, and the
#: shipped three-star line sat at 120%.
#:
#: What that produced is a ladder with one rung: every run that was held was also three-starred, on
#: every chapter, on both lines. Two whole bands of the grade rejected nothing, which is invariant
#: 5d asked of the star ladder.
#:
#: **Authored per level rather than retuned globally**, because `LevelTuning.DefaultGoldFactor` is
#: shared by every mode in the game and the glade, the fall and the prism all derive par by search
#: where 1.20 is correct. A siege is the only mode whose par overstates, so it is the only one that
#: should carry its own.
#:
#: **Per chapter, because par overstates by a different amount on each one.** A surge raises par in
#: proportion to the hill's health, and a run's *matches* do not rise with it in proportion - bombs,
#: cogs and an overcharge deliver a flat amount that does not scale - so a tougher chapter's runs
#: come in at a lower share of par than a gentler one's. Measured: chapter two's clears land between
#: 58% and 100% of par and chapter three's between 49% and 93%, on the same sweep. One pair for the
#: mode would grade the two nothing alike.
#:
#: Set from each chapter's own sweep so that most clears land on two stars, a good run takes three
#: and a scrappy one takes one.
STAR_FACTORS = {
    1: (0.75, 0.92),
    2: (0.75, 0.92),
    3: (0.67, 0.84),

    #: Ashenhold, whose raiders carry **two** tenths more health than the baseline against
    #: Barrowfell's one. Par scales exactly with a surge and a run's *matches* do not - bombs,
    #: cogs and an overcharge deliver a flat amount that does not scale with the hill - so a
    #: tougher chapter's clears land at a lower share of par than a gentler one's, and its lines
    #: have to come down with it (invariant 37cb). Provisional until this chapter's own sweep has
    #: **Measured on this chapter's own sweep**: every clear came in between 40% and 73% of
#: par, so Barrowfell's 0.67 would have three-starred almost all of them - invariant 5d
#: asked of the grade, which is exactly what the shipped ladder did for three chapters.
    4: (0.52, 0.66),
}


def star_factors(ordinal):
    """The two lines a chapter at this 1-based ordinal authors."""
    return STAR_FACTORS.get(ordinal, STAR_FACTORS[max(STAR_FACTORS)])


#: `SiegeCharm`, and the letter a chapter body names each one by. In the order they are
#: introduced, which is a fact rather than a convenience: a chapter deals the first *n* of these,
#: so this order *is* the ladder and `charms_upto` is the only thing that reads it.
PRISM, LANCE, STORM = "prism", "lance", "storm"

CHARM_ROSTER = (("p", PRISM), ("l", LANCE), ("s", STORM))

CHARM_LETTERS = "".join(letter for letter, _ in CHARM_ROSTER)

#: `SiegeTuning.CharmWithin` - the **most** gems that may pass between one charm and the next.
#:
#: **A window rather than a chance**, because this stream is deterministic: a rate means a
#: geometric gap, and a long gap on a shipped board is not bad luck that evens out, it is the same
#: board dealing the same nothing to every player who ever opens it. Held here so both gates can
#: say how many a run expects and refuse a level too short to deal one.
CHARM_WITHIN = 112

#: `SiegeTuning.CharmOpening` - the first charm of a run falls in the first `CHARM_WITHIN` / this
#: gems, rather than anywhere in a whole window.
#:
#: **The window bounded the gap between two charms and left the opening gap exactly as long**,
#: which on a short rung is longer than the run: `s01_firstwatch` is par 14, about 77 gems, against
#: an opening gap that could be 112. What a gate can say about it is the floor - a run is now
#: guaranteed one charm inside its first quarter-window and one per window after that.
CHARM_OPENING = 2

#: `SiegeTuning.CharmStorm` and its double against its own colour.
CHARM_STORM = 300
CHARM_STORM_OWN_TENTHS = 10 * WEAK_MULTIPLIER


def charm_floor(par_moves):
    """`SiegeBoard`'s two windows - the least charms a run of this par is dealt.

    The first falls inside `CHARM_WITHIN // CHARM_OPENING` gems and each one after it inside a whole
    `CHARM_WITHIN`, so a run that deals fewer gems than the opening window is the only one that can
    honestly be said to be guaranteed none.
    """
    gems = par_moves * MATCH_GEMS_TENTHS // 10
    opening = max(1, CHARM_WITHIN // CHARM_OPENING)

    if gems < opening:
        return 0

    return 1 + (gems - opening) // CHARM_WITHIN


def charms_upto(ordinal):
    """`SiegeCharms.Upto` - the charms a chapter at this 1-based ordinal deals."""
    if ordinal <= 0:
        return ""

    return CHARM_LETTERS[:min(ordinal, len(CHARM_ROSTER))]


def charmed(raw):
    """`SiegeLayout.Charmed` - the charms a body names, or **None** for a letter this build has
    never heard of.

    None rather than "the ones I recognised", which is the whole difference between this and
    `tidy`: a body naming a retired or unknown charm was written for a build that is not this one,
    and salvaging what is left of it ships a field its author never composed (invariant 5f).
    """
    if not raw:
        return []

    kept = []
    for c in raw:
        if c == " ":
            continue

        named = dict(CHARM_ROSTER).get(c)
        if named is None:
            return None

        # A letter written twice is an author saying one thing twice, not two charms: the deal
        # picks uniformly from this list, so keeping the repeat would weight one charm double.
        if named not in kept:
            kept.append(named)

    return kept


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


def runs(cells, width, height, charms=None, paid=None):
    """Every cell standing in a run of three or more. Mirrors `SiegeLayout.Runs`.

    **Scanned once per colour rather than once per row, and the prism is the whole reason.** It
    used to compare each cell with the one before it, which is exactly right while every gem is its
    own colour and cannot survive a wild: ``"r P r"`` has no two neighbours alike in it and is a
    run, and ``"r r P g g"`` is *two* runs sharing one cell. So a run of colour *c* is defined
    instead as a maximal block of cells that are *c*-or-wild holding at least one real *c*, and the
    four colours are walked in turn.

    ``paid`` is a list the length of the field, written with the colour each cleared cell is worth
    - which for a prism is the colour of the run it joined and never the letter underneath. First
    claim wins, so the scan order (rows before columns, `LETTERS` in order) is a stated rule rather
    than an accident; `SiegeLayout.Runs` says the same thing in the same words.
    """
    hit = set()

    def wild(i):
        return charms is not None and charms[i] == PRISM

    def take(i, colour):
        hit.add(i)
        if paid is not None and not paid[i]:
            paid[i] = colour if wild(i) else cells[i]

    def scan(colour, first, step, span):
        run = real = 0

        for k in range(span + 1):
            i = first + k * step

            joins = (k < span and is_gem(cells[i])
                     and (wild(i) or cells[i] == colour))

            if joins:
                run += 1
                if not wild(i):
                    real += 1
                continue

            if run >= MIN_RUN and real > 0:
                for back in range(k - run, k):
                    take(first + back * step, colour)

            run = real = 0

    for colour in LETTERS:
        for y in range(height):
            scan(colour, y * width, 1, width)
        for x in range(width):
            scan(colour, x, width, height)

    return hit


class Layout(object):
    """`SiegeLayout`. `fault` is None when the level is readable, and the sentence when it is not."""

    def __init__(self, grid, deal, wards, waves, boss=None, cogs=0, endless=False, tough=0,
                 charms=None):
        self.grid = grid
        self.endless = bool(endless)

        # `SiegeLayout.Charms`. None is a body naming a charm this build does not have, which
        # `_check` turns into a sentence rather than guessing at.
        self.charms = charmed(charms)
        self.deal = tidy(deal, LETTERS)
        self.cogs = max(0, int(cogs or 0))
        self.wards = list(tidy(wards, WARD_LETTERS))
        self.waves = [w for w in (sweep(tidy(x, WAVE_LETTERS)) for x in (waves or [])) if w]

        # Exactly one legal name and one legal colour, or nothing - never `tidy`, which would
        # salvage an 'r' out of "dragon:r" and ship a warlord nobody authored. See `SiegeLayout`'s
        # constructor; the same clause refuses the retired one-letter form.
        # `SiegeLayout.Tough`. Nought is what every body written before the field existed says,
        # and it means the plain figure.
        self.tough = 10 if not tough else tough

        self.boss_kind, self.boss = named_boss(boss)
        self.boss_wave = -1

        # The boss is *derived into* the wave list rather than authored into one - the last wave is
        # the boss wave by rule, so it can neither be put in the middle of a siege nor left off the
        # end of one. See `SiegeLayout.Boss`.
        #
        # **A boss that cannot bring a ward down rides the last authored wave rather than getting
        # one of its own.** A warlord, a warbringer and an overlord shell the line for as long as
        # they live, so a wave to themselves is the point; a blightcaller takes a ward's *fire*,
        # which is worth exactly what there is to burn, so alone on a cleared hill it costs
        # nothing at all (invariant 5d, reported from play twice). Merging is the endless lane's
        # escort said in the idiom of an authored ladder, and it costs par nothing because the
        # company is the raiders the level already sends.
        if self.boss:
            rides = not endangers(self.boss_kind) and len(self.waves) > 0

            if rides:
                self.boss_wave = len(self.waves) - 1
                self.waves = (self.waves[:-1]
                              + [self.boss + self.waves[-1]])
            else:
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

        self.fault = self._check(boss, charms)

    def _check(self, boss, charms):
        if self.grid is None:
            return "no field"

        # Refused by name, for the boss token's reason below: a body naming a charm this build does
        # not have would otherwise deal the ones it recognised and ship a field nobody composed,
        # with every other gate green (invariant 5f).
        if self.charms is None:
            return ("'%s' names a charm this mode does not have; a charm is one of '%s' and an "
                    "empty field is how a level says it deals none" % (charms, CHARM_LETTERS))

        if boss and boss.strip() and not self.boss:
            return ("'%s' is not a boss this mode knows; a boss is written as a kind and the "
                    "colour it wears (%s, colour one of '%s'), and an empty field is how a siege "
                    "says it sends none"
                    % (boss, ", ".join("%s:<colour>" % n for n in BOSS_NAMES), BOSS_COLOURS))

        if not (MIN_WARDS <= len(self.wards) <= MAX_WARDS):
            return ("a ward line holds %d to %d wards; this one names %d. Fewer than %d is a "
                    "field of fewer than %d colours, which cascades without stopping and cannot "
                    "be authored settled at all"
                    % (MIN_WARDS, MAX_WARDS, len(self.wards), MIN_WARDS, MIN_WARDS))

        for i in range(len(self.wards)):
            for j in range(i + 1, len(self.wards)):
                if self.wards[i] == self.wards[j]:
                    return ("two wards on this line are both '%s', so one of them can never be "
                            "the only thing a colour feeds and half the line is a spare part"
                            % self.wards[i])

        if self.colours < MIN_WARDS:
            return ("the field refills from %d colour(s); %d is the fewest a field can hold and "
                    "still be a board" % (self.colours, MIN_WARDS))

        if self.cogs > MAX_COG_RATE:
            return ("this hill drops a cog %d times in a hundred kills; %d is the most a level "
                    "may ask for" % (self.cogs, MAX_COG_RATE))

        for ward in self.wards:
            if ward not in self.deal:
                return ("the '%s' ward stands on a field that never deals a '%s' gem, so nothing "
                        "the player does could ever fuel it" % (ward, ward))

        # **And the other way round, which only became a rule when the lock arrived.** A ward
        # burns its own colour and nothing else, so a gem no ward carries is fuel with nowhere to
        # go: every match of it is a move spent for nothing, and no reading anywhere would report
        # it.
        for gem in self.deal:
            if gem not in self.wards:
                return ("this field deals '%s' gems and no ward on the line burns '%s', so every "
                        "match of that colour is a move spent on nothing. A siege deals exactly "
                        "the colours its line stands" % (gem, gem))

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

        # A raider no ward on the line is strong against is one the player cannot answer properly.
        # **For a boss the reason changed and the rule did not**: every ward answers a boss now
        # (`SiegeTuning.EveryWardReaches`), so one wearing a colour nobody carries is killable - it
        # is simply never *doubled*, which is a duel fought at half rate from the first bolt to the
        # last with nothing anywhere saying so.
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

    @property
    def colours(self):
        """`SiegeLayout.Colours` - how many *distinct* colours this field deals."""
        return len(set(self.deal))


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

        # The first raider of the boss wave, and only the first: a boss that rides the last
        # authored wave stands at its head with an ordinary wave behind it.
        kind = (boss_kind if boss and not made
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
    if kind == "bomber":
        return BOMBER_HEALTH
    return BRUTE_HEALTH if kind == "brute" else CREEPER_HEALTH


def kind_at(layout, wave, index):
    """`SiegeLayout.KindAt` - the wave decides, never the letter.

    **The first raider of the boss wave, and only the first.** It answered `boss_kind` for every
    index of that wave, which was exactly right while a boss wave held nothing but its boss - and
    a boss that rides the last authored wave (see `Layout`) would otherwise have read every raider
    standing beside it as another boss: measured on `s01_stonewatch`, par came out 63 against the
    36 `SiegeTuning.Par` computes, because nine raiders were valued at a blightcaller's health
    each.
    """
    if wave == layout.boss_wave and index == 0:
        return layout.boss_kind

    return layout.coming[wave][index][1]


def par(layout):
    """`SiegeTuning.Par` - what the level sends, over the most one match could ever be worth.

    **What a bonecaller will raise is counted in full and whether it ever does** - see `RAISE_SIZE`.
    """
    health = 0
    for w, line in enumerate(layout.coming):
        for i in range(len(line)):
            kind = kind_at(layout, w, i)

            # Surged, because the hill really is - see `toughness_for`. A chapter whose raiders
            # carry more health and whose par did not move would put three stars out of reach for
            # the whole chapter, with every number in the file plausible.
            health += surged(health_of(kind), layout.tough)
            health += surged(raises_in_all(kind) * CREEPER_HEALTH, layout.tough)

    return max(1, -(-health // PERFECT_MATCH))


#: `SiegeValidator.SwingsBeforeAnswered`. **Two, and one was wrong.** A raider that reaches the
#: line goes on swinging every `SiegeTuning.BlowEvery` until something kills it, so counting a
#: single blow each named half a shipped chapter as unlosable when every one of those rungs bleeds
#: the line in play. What actually measures the threat is the hold simulation in
#: `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine`.
SWINGS_BEFORE_ANSWERED = 2


def endangers(kind):
    """`SiegeTuning.EndangersTheLine` - whether this boss can bring a ward down, given long enough.

    **Three of the eight cannot.** A blightcaller takes a ward's fire rather than its health, a
    gravemaw takes what is lying on the ground, and a shackler takes six seconds and nothing else -
    so a level whose only threat were any of them could not be lost. It was one while the warbringer took ground instead of health, which was withdrawn
    after play.

    **And it stopped being readable off the damage column**, which is what the bonecaller bought:
    a raise takes no ward health at all and what it raises *walks and swings*, so a run can be lost
    to a boss that never touches a ward itself.
    """
    row = BOSSES.get(kind)
    return bool(row) and (row["cast"] > 0 or row["spell"] == "raise")


def raises_in_all(kind):
    """`SiegeTuning.RaisesInAll` - every raider this boss will ever add, raised or not."""
    row = BOSSES.get(kind)
    return RAISE_SIZE * RAISES if row and row["spell"] == "raise" else 0


def threatens(layout):
    """Whether one wave could ever fell a ward. Mirrors `SiegeValidator.Threatens`."""
    if layout.boss and endangers(layout.boss_kind):
        return True

    # **Every wave, the boss's included.** `blow_of` already answers nought for anything that
    # never reaches the line, so skipping the boss wave was only ever a way of saying the same
    # thing twice - and it became wrong the day a boss could ride a wave of real raiders.
    # `ModeValidator.Threatens` has always walked all of them.
    for line in layout.coming:
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
    if kind in BOSSES:
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
    # The boss's own kind is neither a brute nor a bulwark, so nothing has to be skipped to keep
    # it out of these counts - and skipping its whole wave would now lose the raiders riding with
    # it (see the constructor).
    for line in layout.coming:
        for colour, kind in line:
            colours.add(colour)
            if kind == "brute":
                brutes += 1
            elif kind == "bulwark":
                bulwarks += 1

    weavers = sum(1 for line in layout.coming for _, k in line if k == "weaver")
    thieves = sum(1 for line in layout.coming for _, k in line if k == "thief")

    return dict(waves=len(layout.waves), raiders=layout.raiders, brutes=brutes,
                bulwarks=bulwarks, weavers=weavers, thieves=thieves,
                colours=len(colours), wards=len(layout.wards),
                boss=layout.boss or "",
                kind=layout.boss_kind or "",
                spell=BOSSES[layout.boss_kind]["spell"] if layout.boss_kind else "",
                cogs=layout.cogs, drops=layout.raiders * layout.cogs // 100,
                tough=layout.tough,
                charms="".join(dict((c, l) for l, c in CHARM_ROSTER)[c]
                               for c in (layout.charms or ())),
                # **How many charms a run is *guaranteed*, which is the only reading that can say
                # a level has shipped one nobody will ever see** (invariant 5d, the same question
                # the cog's drop rate is asked). The count it *expects* is about twice this,
                # because a gap inside a window averages half of it. The floor is what a gate may
                # refuse on.
                #
                # **The opening gap is its own, narrower window** (`CHARM_OPENING`), which is what
                # makes this floor honest on a short rung: the first charm falls inside
                # `CHARM_WITHIN / CHARM_OPENING` gems and every one after it inside a whole window.
                # Counted as one window each would have said a rung too short for a full window
                # deals none, which stopped being true the day the opening was bounded separately.
                sparks=charm_floor(par(layout)),
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

BRUTES_FROM, BULWARKS_FROM, BOMBERS_FROM = 3, 6, 9


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

    if wave >= BOMBERS_FROM and i == 1:
        return "bomber"
    if wave >= BULWARKS_FROM and roll % 100 < _share(wave, BULWARKS_FROM, 30):
        return "bulwark"
    if wave >= BRUTES_FROM and roll % 100 < _share(wave, BRUTES_FROM, 55):
        return "brute"
    return "creeper"
