"""Anchorhold - the sixteenth Thornwatch chapter: the gun battery above the clouds, twenty rungs, four duels.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**The owner's brief, 2026-10-25, and every clause of it is a rule somewhere.**

  * **Twenty rungs, levels 231 to 250**, on the owner's own painting - a gun battery of floating
    rocks above the clouds carrying naval turrets, a field gun behind sandbags, rocket racks, a
    lighthouse, radio masts, signal flags, fuel tanks, a windmill and a watchtower, joined by a grey
    road edged in white lines - with every node standing **on the road** (`make_map_seats.TRACES[16]`).
  * **"Base health bonus 120%, and with spikes."** The floor is 2.2 (`SiegeTuning.Traded`, row
    `(15, 22)` - a tenth over Cinderworks' 2.1, under the 2.5 the ladder would derive), and **each
    rung deals its own figure at or above it** (`LEVELS`' ``surge``), Rustwaste's shape (MODES.md
    37fh): every block of five climbs to a spike on its fourth rung and the duel after it stands a
    tenth or two under the spike. The spikes climb, 2.4, 2.5, 2.6, 2.7, and the chapter averages
    2.395 against Cinderworks' 2.295.
  * **Harder than the chapter before, and still not a wall.** `TheSixteenthChapterIsHarder` plays
    it against Cinderworks on the veteran line (pyre, pyre, leech and harpoon) at three stars and at
    four and on the gate's three-star embers and strongest line - and refuses a wall only on the
    four-star veteran line, the upgrade the brief asks a player to want (37fi).
  * **Every raider and every boss is one the player has already fought.** The cast is the
    *vanguard* (`SiegeMode.MainCasts`): the table would otherwise wrap onto the first chapter's
    insects, and the fifth square - drawn whole only once before, by the eleventh chapter - is the
    crowd a fortified battery asks for. The four duels are four pairs nobody has fought, of eight
    verbs, none twice in the chapter (37ey).
  * **The curse and the void stone carry on**, as every taught mechanic does.

**The crowd is Cinderworks', and the health is the step**: held to no less than Cinderworks' on
every count `prove` reads and to no more than `MOST_OVER` past it, for Ironbastion's lesson that
spiky rungs wall the good line long before a surge does (37ff).

**The four duels, and what each pair asks.**

  * **Rung 5, an ironclad and a gravemaw** - the aegis turns aside all but one ward while a devour
    eats every cog and bomb left lying.
  * **Rung 10, a warlord and a gorgon** - a smite takes a ward's health while a glare turns the
    fullest ward stone-struck.
  * **Rung 15, a colossus and a shackler** - a bury heaps the hill while a bind locks a ward.
  * **Rung 20, a hollowking and a harrower** - a wane bleeds the line while a harrow tears a rank off
    a ward and drops it on the hill. **Cogs at fifty**, the finale's argument since Cloudkeep.

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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s18_anchorhold.json"

CHAPTER = "s18_anchorhold"

#: The chapter this one is commissioned against: harder than it.
BEFORE = OUT.parent / "s17_cinderworks.json"

#: This chapter's place inside its own mode and track, which decides its map (`map16`, the owner's
#: battery), its skies (the fourth block again - forty skies wrap at four chapters), its cast
#: (`SiegeMode.MainCasts`, the vanguard), its charms (all six; `SiegeCharms.Upto` clamps) and its
#: surge floor (the ninth row of `SiegeTuning.Traded`).
ORDINAL = 16

#: The floor every rung deals at least, in tenths - the owner's "base 120% health bonus", held here
#: so a change to `siege.TRADED` is a re-measurement rather than a silent retune.
FLOOR = 22

#: The most any rung deals, in tenths. A ceiling on the spike, so a retune that pushes one rung
#: past it is a decision rather than a drift.
PEAK = 27

#: How far over the floor the chapter's highest spike must climb, in tenths - the brief's "with
#: spikes", held to Cinderworks' five tenths so the spike is no smaller than the last chapter's.
SPIKE = 5

#: Every fifth rung is a duel. Held here and proved below, rather than trusted to the table.
BOSS_EVERY = 5

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: How many rungs this chapter has, and so how many seats its map carries (`mapart.NODES`).
RUNGS = 20

#: The fewest and most raiders a wave of this chapter sends, **bosses aside** - Cinderworks' bounds,
#: kept: the step is the health, and a wider wave is the spike that walls a good line.
LEAST_WAVE, MOST_WAVE = 12, 18

#: The fewest brutes a rung that is not a duel sends - Cinderworks' floor, kept.
LEAST_BRUTES = 6

#: **Harder by health, and not by a crowd that walls**: the crowd may not fall under Cinderworks' on
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
#: **Every field was chosen by play, not by the sweep** (2026-10-25), 37fh's lesson a third time and
#: the sharpest yet: the first deal held 8 runs of 180 on the veteran line at four stars - the
#: upgrade the brief asks a player to want - with sixteen rungs at nought of nine. So eighteen rungs
#: were played on forty fresh six-swap fields on that line (the field gun's column on sixty more,
#: where only one field in a hundred held it twice), and every field that held its rung 2 to 5 of 9
#: there was then played on the veteran line at three stars and on three-star embers too. **Each rung
#: has the field that holds it 2 to 4 of 9 on the four-star line** - one field to a rung, never
#: shared - chosen among those so the chapter reads under Cinderworks on all three lines at once
#: while as many rungs as possible stay winnable on the three-star line (ten of twenty); picking on
#: the four-star line alone left that line and embers over Cinderworks' (`TheSixteenthChapterIsHarder`).
LEVELS = (
    # ---------------------------------------------------------------- the windmill rocks
    # **The cloud step**, at the floor: shields and brutes from the start.
    dict(id="s18_cloudstep", surge=22, seed=4171326185, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby#yrgbyrgb", "rgby#gRGby!rgbyRG", "rgBY#r#brgbyRGbyy"]),

    # Past the windmill: a tenth heavier, brutes closing every wave.
    dict(id="s18_millvane", surge=23, seed=4171363162, swaps=6, cogs=20, boss="",
         waves=["rgby#brgbyRGbyrgb", "rgbyRGby#rrgbyRGb", "rgbyRGbyrgBY#grgy"]),

    # **Pincers** over the truss bridge, back at the floor: two colours, the other two, then all four.
    dict(id="s18_trussbridge", surge=22, seed=4171311764, swaps=6, cogs=20, boss="",
         waves=["bbggBGbbgg#bbgg", "rryyRYrryy#rryy", "rgbyRGBY#b#rygrgby"]),

    # **The first spike: the cog rock at 2.4**, bombers two to a wave.
    dict(id="s18_cogrock", surge=24, seed=4170488228, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby!yRGbyrg", "rgBY#b!rRGby!gry", "rgby!r#yRGbyRGby!b"]),

    # **The first duel: an ironclad and a gravemaw** by the rocket rest - the aegis turns aside all
    # but one ward while a devour eats every cog and bomb left lying.
    dict(id="s18_rocketrest", surge=23, seed=4171322941, swaps=6, cogs=35,
         boss="ironclad:r+gravemaw:g",
         waves=["rgbyRGbyrgbyrgb", "rgby#y!rRGbyRGb", "rgBY#g#bRGbyrg"]),

    # ---------------------------------------------------------------- the tank rocks
    # **One colour at a time, in a flood**, under the lamp post, back at the floor.
    dict(id="s18_lamppost", surge=22, seed=4171271143, swaps=6, cogs=20, boss="",
         waves=["yyYYYyyyyy#ybbb", "rrRRRrrrrr#rggg", "ggGG#ggggBBB#bbbb"]),

    # Past the fuel tanks at 2.4: four waves, the third armoured and bombed at once.
    dict(id="s18_tankfarm", surge=24, seed=4171206071, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgbyrgb", "rgby#g#bRGbyRGb", "rgby!yyrRG#rrgbb", "rgbyRGby#bbgbyRG"]),

    # **The shaded ramp**: a pair of shields in every wave.
    dict(id="s18_shadowramp", surge=23, seed=4171361515, swaps=6, cogs=20, boss="",
         waves=["rgby#r#yRGby!rgby", "rgby#b#gRGBYrgbyr", "rgBY#g#bRGbyrgby"]),

    # **The second spike: the crate yard at 2.5** - the longest climb before the second duel, four
    # waves and a brute pair in every one.
    dict(id="s18_crateyard", surge=25, seed=4171273392, swaps=6, cogs=20, boss="",
         waves=["rgby#bbgbyRGbyrgb", "rgby#ygbyRGbyrr", "rgby#r!yygBYrgby", "rgbyrgbyRGBYrgby"]),

    # **The second duel: a warlord and a gorgon** under the radio mast - a smite takes a ward's
    # health while a glare strikes the fullest one to stone.
    dict(id="s18_radiomast", surge=24, seed=4171322500, swaps=6, cogs=50,
         boss="warlord:b+gorgon:y",
         waves=["rgbyRGbyrgbyr", "rgby#gRGbyrgbRG", "rgby#yRGbyrgb"]),

    # ---------------------------------------------------------------- the gun rocks
    # Along the sandbag line: the crowd climbs, fifteen, sixteen, seventeen.
    dict(id="s18_sandbagline", surge=23, seed=4171207933, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGby!rgb", "rgBY#y#ryrgbyRGbyg", "rgby!r#gRGbyrgBYby"]),

    # Between the twin guns at 2.5: bombs and plate in every wave.
    dict(id="s18_twinguns", surge=25, seed=4171233976, swaps=6, cogs=20, boss="",
         waves=["rgby#y!gRGbyrgby", "rgby#r#b!gRGbyrgb", "rgBY!b#yRGbyrgBY"]),

    # Under the searchlight at 2.4: brutes from the first wave.
    dict(id="s18_glarelamp", surge=24, seed=4171222756, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby#grgbyrgb", "rgbyRGby#yRGbyrg", "RGbyrgby#rrgbyrgBY"]),

    # **The third spike: the field gun at 2.6** - a column, three shields at the head of every
    # wave and cogs at 25. A column is the most field-bound shape in the mode (37fg).
    dict(id="s18_fieldgun", surge=26, seed=4171374196, swaps=6, cogs=25, boss="",
         waves=["#y#r#gRGBYrgbyrg", "#b#g#rRGbyrgbyrgb", "#r#y#bRGbyrgbyrg"]),

    # **The third duel: a colossus and a shackler** at the lighthouse - a bury heaps the hill while
    # a bind locks a ward.
    dict(id="s18_beaconrock", surge=24, seed=4171226320, swaps=6, cogs=45,
         boss="colossus:g+shackler:b",
         waves=["rgbyRGbyrgbyr", "rgby#rRGbyRGb", "rgBY#yRGbyrgb"]),

    # ---------------------------------------------------------------- the beacon rocks
    # The anchor chain: brutes of one colour at a time, a pair at the head of its creepers.
    dict(id="s18_anchorchain", surge=23, seed=4170526582, swaps=6, cogs=20, boss="",
         waves=["bbBBB#bbbbrrg", "yyYYY#yyyyy#yr", "ggGGG#ggggbb#bb"]),

    # **The signal flags at 2.5**: four waves, the last led by every colour's brute.
    dict(id="s18_signalflags", surge=25, seed=4171216881, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbygr", "rgby#bbrgbyRGby", "rgby#y#r!ggrbyRG", "RGBYrgbyRGbyry"]),

    # Beside the rocket rack at 2.5: a bomber at the head of every wave and a second in the middle.
    dict(id="s18_rocketrack", surge=25, seed=4171326695, swaps=6, cogs=20, boss="",
         waves=["!yrgbyRGbyrgbyRG", "!grgby#rRGby!yrb", "!bbgBY#rRGbyrgBYg"]),

    # **The peak: the turret deck at 2.7**, everything the chapter taught at once - brutes, three
    # shields and a bomb in one rung, at the most health any rung of the ladder deals.
    dict(id="s18_turretdeck", surge=27, seed=4171297536, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyRGbyr", "rgby#g#b#yRGby!rgb", "rgby!r#yRGbyrgBYRG"]),

    # **The finale: a hollowking and a harrower** at 2.5 under the crow's nest - the line is bled
    # while a rank is torn off a ward. **Cogs at fifty**, Cloudkeep's finale's argument.
    dict(id="s18_crowsnest", surge=25, seed=4171300920, swaps=6, cogs=50,
         boss="hollowking:r+harrower:y",
         waves=["rgbyrgbyRGbygb", "rgby#yyrgbyRGbyr", "rgbyRGbyrgbyRG"]),
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
        "accent": "#7FC8F8",
        "slate": "#18233A",
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
    floor and the peak, and holds the crowd to the brief: no less than Cinderworks' on every count,
    and no more than `MOST_OVER` past it. Whether a rung can be **held** is
    `SiegeRuleTests.TheSixteenthChapterIsHarder`, and whether every duel is a fight is
    `SiegeRuleTests.EveryShippedBossRungIsAFight`.
    """
    if len(LEVELS) != RUNGS:
        sys.exit("this chapter is %d rungs long, not %d" % (len(LEVELS), RUNGS))

    if mapart.map_of(ORDINAL) != 16:
        sys.exit("ordinal %d draws map%d, not the battery" % (ORDINAL, mapart.map_of(ORDINAL)))

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
        sys.exit("no rung deals the floor of %d tenths; the brief is 120%% at base" % FLOOR)
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
    # never less than Cinderworks on any count, and never more than `MOST_OVER` past it. Hill is
    # printed only.
    ours = totals(written["levels"])
    theirs = totals(json.loads(BEFORE.read_text(encoding="utf-8"))["levels"])
    for name, mine, before in zip(("raiders", "brutes", "shields", "bombers", "hill (par)"),
                                  ours, theirs):
        over = (mine - before) * 100.0 / before
        print("%-11s %5d against Cinderworks' %5d (%+.1f%%)" % (name, mine, before, over))
        if name not in MOST_OVER:
            continue
        if mine < before:
            sys.exit("the brief is harder than Cinderworks, and this chapter sends %d %s against %d"
                     % (mine, name, before))
        if over > MOST_OVER[name]:
            sys.exit("the step is the health and not the crowd, and %s is %+.1f%% over Cinderworks "
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
