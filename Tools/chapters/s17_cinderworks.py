"""Cinderworks - the fifteenth Thornwatch chapter: the foundry over the lava, twenty rungs, four duels.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**The owner's brief, 2026-10-25, and every clause of it is a rule somewhere.**

  * **Twenty rungs, levels 211 to 230**, on the owner's own painting - a foundry of floating rocks
    over a lava sea carrying smoking furnaces, a crucible pouring, a forge press, a crane, a cog
    tower, a boiler, an ore pier and a welding mech, joined by an iron road of riveted plates
    edged in hazard stripes - with every node standing **on the road** (`make_map_seats.TRACES[15]`).
  * **"Base 110% health bonus, and some levels should have spikes."** The floor is 2.1
    (`SiegeTuning.Traded`, row `(14, 21)` - a tenth over Rustwaste's 2.0, under the 2.4 the ladder
    would derive), and **each rung deals its own figure at or above it** (`LEVELS`' ``surge``), the
    shape Rustwaste introduced (MODES.md 37fh): every block of five climbs to a spike on its fourth
    rung and the duel after it stands a tenth or two under the spike. The spikes climb, 2.3, 2.4,
    2.5, 2.6, and the chapter averages 2.295 against Rustwaste's 2.155.
  * **"Players never had the need to buy coins to upgrade turrets ... some levels should be truly
    challenging."** Measured on the line a real player stands, not only the gate's:
    `TheFifteenthChapterIsHarder` plays it on the veteran line (pyre, pyre, leech and harpoon) at
    three stars, at four, and on the gate's three-star embers and strongest line, against
    Rustwaste - and refuses a wall only on the four-star veteran line, the upgrade the brief asks
    a player to want.
  * **Every raider and every boss is one the player has already fought.** The cast is the *wild*
    (`SiegeMode.MainCasts`): the table would otherwise wrap onto the first chapter's insects, and
    rock golems, minotaurs and mud clods are the crowd a foundry hewn out of the rock asks for,
    drawn whole only once before, by the fifth chapter. The four duels are four pairs nobody has
    fought, of eight verbs, none twice in the chapter (37ey).
  * **The curse and the void stone carry on**, as every taught mechanic does.

**The crowd is Rustwaste's, and the health is the step**: held to no less than Rustwaste's on
every count `prove` reads and to no more than `MOST_OVER` past it, for Ironbastion's lesson that
spiky rungs wall the good line long before a surge does - the spikes here are spikes of health,
which bites the strong line, rather than of headcount, which pays cogs and charms back (37ff).

**The four duels, and what each pair asks.**

  * **Rung 5, a blightcaller and a harrower** - a douse puts the fullest ward's fire out while a
    harrow takes a rank off a ward and drops it on the hill as a cog to win back.
  * **Rung 10, a warbringer and a thunderer** - a rally hurries the crowd down the hill while a
    drain empties a ward's tube: the hill closes faster than the line can feed.
  * **Rung 15, an overlord and a gravemaw** - a sunder breaks a ward's guard while a devour eats
    every cog and bomb left lying: spend what the hill drops at once.
  * **Rung 20, a hollowking and a bonecaller** - a wane bleeds the line while a raise sends the dead
    back up the hill as a crowd. **Cogs at fifty**, the finale's argument since Cloudkeep.

**No sunlord**: at 1.9 it walled every duel it stood in (37fg), and nothing here is lighter.

**And there is no move allowance anywhere in it** (invariant 37b).
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(REPO / "Tools"))
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import chapters.mapart as mapart                                        # noqa: E402
import proto                                                            # noqa: E402
import siege                                                            # noqa: E402
import siege_sweep as sweep                                             # noqa: E402

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s17_cinderworks.json"

CHAPTER = "s17_cinderworks"

#: The chapter this one is commissioned against: harder than it.
BEFORE = OUT.parent / "s16_rustwaste.json"

#: This chapter's place inside its own mode and track, which decides its map (`map15`, the owner's
#: foundry), its skies (the third block again - forty skies wrap at four chapters), its cast
#: (`SiegeMode.MainCasts`, the wild), its charms (all six; `SiegeCharms.Upto` clamps) and its surge
#: floor (the eighth row of `SiegeTuning.Traded`).
ORDINAL = 15

#: The floor every rung deals at least, in tenths - the owner's "base 110% health bonus", held here
#: so a change to `siege.TRADED` is a re-measurement rather than a silent retune.
FLOOR = 21

#: The most any rung deals, in tenths. A ceiling on the spike, so a retune that pushes one rung
#: past it is a decision rather than a drift.
PEAK = 26

#: How far over the floor the chapter's highest spike must climb, in tenths - the brief's "some
#: levels should have spikes", and "truly challenging", as a number.
SPIKE = 5

#: Every fifth rung is a duel. Held here and proved below, rather than trusted to the table.
BOSS_EVERY = 5

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: How many rungs this chapter has, and so how many seats its map carries (`mapart.NODES`).
RUNGS = 20

#: The fewest and most raiders a wave of this chapter sends, **bosses aside** - Rustwaste's bounds,
#: kept: the step is the health, and a wider wave is the spike that walls a good line.
LEAST_WAVE, MOST_WAVE = 12, 18

#: The fewest brutes a rung that is not a duel sends - Rustwaste's floor, kept.
LEAST_BRUTES = 6

#: **Harder by health, and not by a crowd that walls**: the crowd may not fall under Rustwaste's on
#: any of these and may not run further past it than this, in per cent. Hill (par) is printed only,
#: because it carries every rung's surge.
MOST_OVER = {"raiders": 4, "brutes": 8, "shields": 15, "bombers": 40}

#: Every rung, in order.
#:
#: ``surge`` is the rung's own health figure in tenths (at least `FLOOR`, at most `PEAK`). ``seed``
#: deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and is
#: recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled raider
#: leaves one on the hill, **per hundred kills**, and ``boss`` names a duel - two bosses joined by
#: `+` - or is empty. Every field opens at six swaps, out of a seed range no shipped chapter deals
#: from (4170000000 on). **No seed and no field is shared with any shipped chapter.**
#:
#: **Sixteen fields were chosen by play, not by the sweep** (2026-10-25), Rustwaste's lesson (37fh)
#: met again: the first deal held rungs 9 to 20 at nought of nine, eleven of them, on the veteran
#: line at four stars - the upgrade the brief asks a player to want - and the same waves at the same
#: surge read from 0 to 9 of 9 by field alone. So every rung but the four that already held 2 of 9
#: was played on fifty six-swap fields on that line and given the one that holds it **2 to 5 of 9**
#: (one field to a rung, never shared); the rungs that read too easy (6 to 8 of 9) were brought into
#: the same band, so the chapter is hard everywhere rather than in patches.
LEVELS = (
    # ---------------------------------------------------------------- the weld rocks
    # **The iron span**, at the floor: the crowd Rustwaste ended on, two shields and brutes in plate.
    dict(id="s17_ironspan", surge=21, seed=4170007230, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby#grgbyrgb", "rgby#yRGby!rgbyRG", "rgBY#b#rrgbyRGbyg"]),

    # Past the welding mech: a tenth heavier, brutes closing every wave.
    dict(id="s17_weldbay", surge=22, seed=4170012175, swaps=6, cogs=20, boss="",
         waves=["rgby#grgbyRGbyrgb", "rgbyRGby#bbrgbyRGy", "rgbyRGbyrgBY#yrgr"]),

    # **Pincers** at the spark anvil, back at the floor: two colours, the other two, then all four.
    dict(id="s17_sparkanvil", surge=21, seed=4170013267, swaps=6, cogs=20, boss="",
         waves=["yyrrYRyyrr#yyrr", "ggbbGBggbb#ggbb", "rgbyRGBY#g#ybrrgby"]),

    # **The first spike: the girder walk at 2.3**, bombers two to a wave.
    dict(id="s17_girderwalk", surge=23, seed=4170152318, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby!gRGbyrb", "rgBY#r!yRGby!grb", "rgby!b#gRGbyRGby!r"]),

    # **The first duel: a blightcaller and a harrower** under the cog tower - a douse puts the
    # fullest fire out while a harrow tears a rank off a ward and drops it on the hill.
    dict(id="s17_cogtower", surge=22, seed=4170127780, swaps=6, cogs=35,
         boss="blightcaller:g+harrower:r",
         waves=["rgbyRGbyrgbyrgb", "rgby#g!bRGbyRGb", "rgBY#r#yRGbyrg"]),

    # ---------------------------------------------------------------- the press rocks
    # **One colour at a time, in a flood**, down the hazard line, back at the floor.
    dict(id="s17_hazardline", surge=21, seed=4170025675, swaps=6, cogs=20, boss="",
         waves=["ggGGGggggg#gyyy", "bbBBBbbbbb#brrr", "rrRR#rrrrYYY#yyyy"]),

    # Under the lantern hook at 2.3: four waves, the third armoured and bombed at once.
    dict(id="s17_lanternhook", surge=23, seed=4170105023, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgbyrgb", "rgby#y#rRGbyRGb", "rgby!ggbRG#brgyy", "rgbyRGby#rrgbyRG"]),

    # **The forge press**: a pair of shields in every wave.
    dict(id="s17_forgepress", surge=22, seed=4170120261, swaps=6, cogs=20, boss="",
         waves=["rgby#b#gRGby!rgby", "rgby#r#yRGBYrgbyg", "rgBY#y#rRGbyrgby"]),

    # **The second spike: the ramp at 2.4** - the longest climb before the second duel, four
    # waves and a brute pair in every one.
    dict(id="s17_rampway", surge=24, seed=4170390786, swaps=6, cogs=20, boss="",
         waves=["rgby#rrgbyRGbyrgb", "rgby#grgbyRGbyy", "rgby#y!bbgBYrgby", "rgbyrgbyRGBYrgr"]),

    # **The second duel: a warbringer and a thunderer** at the boiler head - the crowd is hurried
    # down the hill while a ward's tube is drained.
    dict(id="s17_boilerhead", surge=23, seed=4170382895, swaps=6, cogs=50,
         boss="warbringer:b+thunderer:y",
         waves=["rgbyRGbyrgbyg", "rgby#bRGbyrgbRG", "rgby#rRGbyrgb"]),

    # ---------------------------------------------------------------- the pipe rocks
    # Past the shell rack: the crowd climbs, fifteen, sixteen, seventeen.
    dict(id="s17_shellrack", surge=22, seed=4170132029, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGby!rgy", "rgBY#g#byrgbyRGbyr", "rgby!b#rRGbyrgBYgy"]),

    # Along the steam pipes at 2.4: bombs and plate in every wave.
    dict(id="s17_steampipe", surge=24, seed=4170119415, swaps=6, cogs=20, boss="",
         waves=["rgby#g!rRGbyrgby", "rgby#b#y!rRGbyrgb", "rgBY!y#gRGbyrgBY"]),

    # The spillway at 2.3: brutes from the first wave.
    dict(id="s17_spillway", surge=23, seed=4170140465, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby#brgbyrgb", "rgbyRGby#rRGbyrg", "RGbyrgby#ggrbyrgBY"]),

    # **The third spike: the slag fall at 2.5** - a column, three shields at the head of every
    # wave and cogs at 25. A column is the most field-bound shape in the mode (37fg).
    dict(id="s17_slagfall", surge=25, seed=4170108372, swaps=6, cogs=25, boss="",
         waves=["#g#b#rRGBYrgbyrg", "#y#r#bRGbyrgbyrgb", "#b#g#yRGbyrgbyrg"]),

    # **The third duel: an overlord and a gravemaw** at the kiln's mouth - a ward's guard is
    # sundered while the hill's cogs and bombs are eaten.
    dict(id="s17_kilnmouth", surge=23, seed=4170106183, swaps=6, cogs=45,
         boss="overlord:r+gravemaw:b",
         waves=["rgbyRGbyrgbyg", "rgby#yRGbyRGb", "rgBY#bRGbyrgb"]),

    # ---------------------------------------------------------------- the furnace rocks
    # The ladle pour: brutes of one colour at a time, a pair at the head of its creepers.
    dict(id="s17_ladlepour", surge=22, seed=4170140579, swaps=6, cogs=20, boss="",
         waves=["rrRRR#rrrryyg", "ggGGG#ggggg#gb", "bbBBB#bbbbyy#yy"]),

    # **The stamp mill at 2.4**: four waves under the press, the last led by every colour's brute.
    dict(id="s17_stampmill", surge=24, seed=4170333288, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyrb", "rgby#yyrgbyRGby", "rgby#g#b!rrgbyRG", "RGBYrgbyRGbygr"]),

    # Beside the magma fall at 2.4: a bomber at the head of every wave and a second in the middle.
    dict(id="s17_magmafall", surge=24, seed=4170097064, swaps=6, cogs=20, boss="",
         waves=["!rrgbyRGbyrgbyRG", "!bygby#gRGby!rgb", "!ggrBY#yRGbyrgBYr"]),

    # **The peak: the smokestack at 2.6**, everything the chapter taught at once - brutes, three
    # shields and a bomb in one rung, at the most health any rung of the ladder deals.
    dict(id="s17_smokestack", surge=26, seed=4170088592, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyRGbyg", "rgby#r#y#bRGby!rgb", "rgby!g#bRGbyrgBYRG"]),

    # **The finale: a hollowking and a bonecaller** at 2.4 - the line is bled while the dead rise
    # as a crowd. **Cogs at fifty**, Cloudkeep's finale's argument.
    dict(id="s17_cindercrown", surge=24, seed=4170090810, swaps=6, cogs=50,
         boss="hollowking:g+bonecaller:y",
         waves=["rgbyrgbyRGbyrg", "rgby#bbrgbyRGbyr", "rgbyRGbyrgbyRG"]),
)

#: What every rung here stands, deals and carries: the whole line, all four colours, the first
#: `ORDINAL` charms of the roster (which clamps at six, so all of them), the curse and the void.
WARDS = GEMS = "rgby"


def _shipped_seeds():
    """The fields every shipped siege chapter already deals, so none is reused here. Read off
    the bodies rather than typed, so a chapter added later is covered without an edit."""
    taken = {}
    for path in sorted(OUT.parent.glob("s*.json")):
        if path.name == OUT.name:
            continue
        body = json.loads(path.read_text(encoding="utf-8"))
        for lv in body.get("levels", []):
            block = lv.get("siege") or {}
            rows = block.get("rows")
            if rows:
                taken["".join(rows)] = lv["id"]
    return taken


def rows_of(rung):
    """This rung's field, re-derived from its seed rather than typed."""
    cells = sweep.deal(rung["seed"], WIDE, TALL, GEMS)
    return sweep.rows_of(cells, WIDE, TALL)


def level(index, rung):
    x, y, afloat = mapart.places(ORDINAL)[index]

    block = {
        "width": WIDE,
        "height": TALL,
        "rows": rows_of(rung),
        "gems": GEMS,
        "wards": WARDS,
        "waves": list(rung["waves"]),
        "boss": rung["boss"],
    }

    if rung["cogs"] > 0:
        block["cogs"] = rung["cogs"]

    block["charms"] = siege.charms_upto(ORDINAL)

    # **The curse and the void stone carry on from the chapters that taught them** - whether is
    # content, how often is the mode (`SiegeTuning.ObsidianPercent`, `.SingularityPermille`).
    block["obsidian"] = True
    block["singularity"] = True

    # **The rung's own surge, over the chapter's floor** - the floor is derived from the ordinal
    # (`siege.toughness_for`, a trade), and the sawtooth over it is this chapter's authored shape.
    block["tough"] = rung["surge"]

    return {
        "id": rung["id"],
        "mapX": round(x, 3),
        "mapY": round(y, 3),
        "afloat": afloat,
        "budgetFactor": -1.0,
        "goldFactor": siege.star_factors(ORDINAL)[0],
        "silverFactor": siege.star_factors(ORDINAL)[1],
        "backdrop": mapart.sky(ORDINAL, index, "siege"),
        "siege": block,
    }


def body():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        "accent": "#FFA63D",
        "slate": "#2A1512",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        "teaserX": mapart.marker(ORDINAL),
        "teaserAfloat": mapart.marker_afloat(ORDINAL),
        "levels": [level(i, rung) for i, rung in enumerate(LEVELS)],
    }


def _layout(block):
    grid = proto.Grid(block["rows"], block["width"], block["height"], siege.CELLS)
    return siege.Layout(grid, block["gems"], block["wards"], block["waves"],
                        block.get("boss"), block.get("cogs", 0),
                        tough=block.get("tough", 0),
                        charms=block.get("charms", ""),
                        obsidian=block.get("obsidian", False),
                        singularity=block.get("singularity", False))


def totals(levels):
    """Raiders, brutes, shields, bombers and par summed over a chapter's rungs, through the one
    mirror."""
    raiders = brutes = shields = bombers = hill = 0
    for lv in levels:
        layout = _layout(lv["siege"])
        read = siege.readings(layout)
        raiders += read["raiders"]
        brutes += read["brutes"]
        shields += read["bulwarks"]
        bombers += sum(1 for line in layout.coming for _, kind in line if kind == "bomber")
        hill += siege.par(layout)
    return raiders, brutes, shields, bombers, hill


def prove(written):
    """Run every rung through the offline mirror and print what it reads.

    This proves the *layout* - the waves parse, every duel names two bosses the mode knows, the
    field has a legal swap - derives par and both star lines, holds every rung's surge to the
    floor and the peak, and holds the crowd to the brief: no less than Rustwaste's on every count,
    and no more than `MOST_OVER` past it. Whether a rung can be **held** is
    `SiegeRuleTests.TheFifteenthChapterIsHarder`, and whether every duel is a fight is
    `SiegeRuleTests.EveryShippedBossRungIsAFight`.
    """
    if len(LEVELS) != RUNGS:
        sys.exit("this chapter is %d rungs long, not %d" % (len(LEVELS), RUNGS))

    if mapart.map_of(ORDINAL) != 15:
        sys.exit("ordinal %d draws map%d, not the foundry" % (ORDINAL, mapart.map_of(ORDINAL)))

    if len(mapart.places(ORDINAL)) < RUNGS:
        sys.exit("map%d seats %d nodes; this chapter needs %d (mapart.NODES)"
                 % (mapart.map_of(ORDINAL), len(mapart.places(ORDINAL)), RUNGS))

    # The floor is a measured figure; a change to it is a re-measurement, so it is held here.
    if siege.toughness_for(ORDINAL - 1) != FLOOR:
        sys.exit("this chapter was built on a floor of %d tenths, and the rule gives %d"
                 % (FLOOR, siege.toughness_for(ORDINAL - 1)))

    shipped = _shipped_seeds()
    verbs = {}
    total = 0
    surges = []

    for index, level_json in enumerate(written["levels"]):
        rung = LEVELS[index]
        block = level_json["siege"]
        layout = _layout(block)

        if layout.fault:
            sys.exit("%s: %s" % (level_json["id"], layout.fault))

        if not FLOOR <= rung["surge"] <= PEAK:
            sys.exit("%s deals a surge of %d tenths; this chapter's rungs deal %d to %d"
                     % (level_json["id"], rung["surge"], FLOOR, PEAK))
        surges.append(rung["surge"])

        if not layout.cursed or not layout.singular:
            sys.exit("%s: the curse and the void stone carry on through this chapter and this "
                     "rung does not deal both" % level_json["id"])

        again = shipped.get("".join(block["rows"]))
        if again:
            sys.exit("%s deals the same field as the shipped %s" % (level_json["id"], again))

        # **Every fifth rung is a duel, and no other rung sends a boss.**
        want_duel = (index + 1) % BOSS_EVERY == 0
        if want_duel != (len(layout.boss_kinds) == 2) or (not want_duel and layout.boss_kinds):
            sys.exit("%s: rung %d %s" % (level_json["id"], index + 1,
                                         "must be a duel" if want_duel else "sends a boss"))

        # **No verb twice across this chapter's four duels** - every duel is a fight of its own.
        for kind in layout.boss_kinds:
            spell = siege.BOSSES[kind]["spell"]
            if spell in verbs:
                sys.exit("%s sends a %s and so does %s" % (level_json["id"], spell, verbs[spell]))
            verbs[spell] = level_json["id"]

        for w, line in enumerate(layout.coming):
            if w == layout.boss_wave:
                continue
            if not LEAST_WAVE <= len(line) <= MOST_WAVE:
                sys.exit("%s: wave %d sends %d raiders; this chapter's waves send %d to %d"
                         % (level_json["id"], w + 1, len(line), LEAST_WAVE, MOST_WAVE))

        if layout.raiders > siege.MAX_RAIDERS:
            sys.exit("%s: %d raiders" % (level_json["id"], layout.raiders))

        par = siege.par(layout)
        read = siege.readings(layout)
        total += read["raiders"]

        if not want_duel and read["brutes"] < LEAST_BRUTES:
            sys.exit("%s sends %d brutes; every ordinary rung of this chapter sends at least %d"
                     % (level_json["id"], read["brutes"], LEAST_BRUTES))

        made = sweep.swaps(list("".join(block["rows"])), block["width"], block["height"])
        if made != rung["swaps"]:
            sys.exit("%s: seed %d now deals %d opening swaps, not the %d recorded"
                     % (level_json["id"], rung["seed"], made, rung["swaps"]))

        widest = max(len(line) for line in layout.coming)
        tail = ""
        if read["kind"]:
            tail += ", a duel of %s (%s) last" % (read["kind"], read["spell"])

        print("%-20s %d.%d  par %-4d 3* %-4d 2* %-4d %2d raider(s) in %d wave(s), widest %2d, "
              "%2d brute(s), %d shielded, cogs %2d%% (~%d a run)%s"
              % (level_json["id"], rung["surge"] // 10, rung["surge"] % 10, par,
                 proto.over(par, round(siege.star_factors(ORDINAL)[0] * 100)),
                 proto.over(par, round(siege.star_factors(ORDINAL)[1] * 100)),
                 read["raiders"], read["waves"], widest, read["brutes"], read["bulwarks"],
                 read["cogs"], read["drops"], tail))

        if not read["swap"]:
            sys.exit("%s: this field has no opening swap" % level_json["id"])

    ids = [lv["id"] for lv in written["levels"]]
    if len(set(ids)) != len(ids):
        sys.exit("two levels of this chapter share an id")

    # **Waves of difficulty, not a line**: the floor is met, the spikes are real, and no two
    # neighbouring rungs are more than two tenths apart (a cliff between two rungs reads as a bug).
    if min(surges) != FLOOR:
        sys.exit("no rung deals the floor of %d tenths; the brief is 110%% at base" % FLOOR)
    if max(surges) - FLOOR < SPIKE:
        sys.exit("the surge never climbs %d tenths over the floor; the brief asks for spikes"
                 % SPIKE)
    for a, b in zip(surges, surges[1:]):
        if abs(a - b) > 2:
            sys.exit("two neighbouring rungs deal %d and %d tenths; a step of more than two is a "
                     "cliff" % (a, b))
    print("surge by rung: %s (average %.3f)" % (" ".join("%d.%d" % (s // 10, s % 10) for s in surges),
                                                 sum(surges) / 10.0 / len(surges)))

    # **The crowd, against the chapter it was commissioned against**, read through one mirror:
    # never less than Rustwaste on any count, and never more than `MOST_OVER` past it. Hill is
    # printed only.
    ours = totals(written["levels"])
    theirs = totals(json.loads(BEFORE.read_text(encoding="utf-8"))["levels"])
    for name, mine, before in zip(("raiders", "brutes", "shields", "bombers", "hill (par)"),
                                  ours, theirs):
        over = (mine - before) * 100.0 / before
        print("%-11s %5d against Rustwaste's %5d (%+.1f%%)" % (name, mine, before, over))
        if name not in MOST_OVER:
            continue
        if mine < before:
            sys.exit("the brief is harder than Rustwaste, and this chapter sends %d %s against %d"
                     % (mine, name, before))
        if over > MOST_OVER[name]:
            sys.exit("the step is the health and not the crowd, and %s is %+.1f%% over Rustwaste "
                     "against a ceiling of %d%%" % (name, over, MOST_OVER[name]))

    print("%d raiders across the chapter" % total)
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    written = body()
    text = json.dumps(written, indent=1, ensure_ascii=False) + "\n"

    if not prove(written):
        return 1

    if args.write:
        OUT.write_bytes(text.encode("utf-8"))
        print("wrote %s" % OUT)
        return 0

    if not OUT.exists():
        print("%s does not exist - run with --write" % OUT)
        return 1

    if OUT.read_text(encoding="utf-8") != text:
        print("%s differs from what this file says - run with --write" % OUT)
        return 1

    print("%s is what this file says" % OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
