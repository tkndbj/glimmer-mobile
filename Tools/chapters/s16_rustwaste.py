"""Rustwaste - the fourteenth Thornwatch chapter: the scrapyard in the sky, twenty rungs, four duels.

Run it with no arguments to check the shipped body against what this file says; `--write` to
rewrite it. Every field is re-derived from its seed rather than typed, so a board can never drift
from the thing that made it (invariant 32d).

**The owner's brief, 2026-10-25, and every clause of it is a rule somewhere.**

  * **Twenty rungs, levels 191 to 210**, on the owner's own painting - a wasteland scrapyard of
    floating desert rocks carrying a junk heap, a rusted giant robot, wrecked cars under a crane,
    wind turbines, pylons, toxic pools and a watchtower on shipping containers, joined by a grey
    asphalt road - with every node standing **on the road** (`make_map_seats.TRACES[14]`).
  * **"100% health bonus at minimum ... some levels should have 110%, 120%, etc."** The floor is
    2.0 (`SiegeTuning.Traded`, row `(13, 20)` - a tenth over Wyrmforge's 1.9, under the 2.2 the
    ladder would derive), and **each rung deals its own figure at or above it** (`SURGES`): the
    first chapter whose rungs do not share one, so the difficulty comes in waves rather than as a
    line (MODES.md 37fh). Every block of five climbs to a spike on its fourth rung and the duel
    after it stands a tenth or two under the spike; the spikes themselves climb, 2.2, 2.3, 2.3,
    2.4, and the chapter averages 2.155.
  * **"I genuinely want players to feel the difficulty."** Measured on lines real players stand,
    not only the gate's: `TheFourteenthChapterIsHarder` plays it on the one live player who has
    cleared every level so far (pyre, pyre, leech and harpoon at three stars), on the same line at
    four stars, and on the gate's three-star embers and strongest line, against Wyrmforge.
  * **Every raider and every boss is one the player has already fought.** The cast is the
    *rabble* (`SiegeMode.MainCasts`): the table would otherwise wrap onto the first chapter's
    insects, and the neighbourhood's zombies - a sledgehammer, a manhole cover held up as a shield
    - are the crowd a wasteland asks for, drawn whole only once before, in the fourth chapter. The
    four duels are four pairs nobody has fought, of eight verbs, none twice in the chapter (37ey).
  * **The curse and the void stone carry on**, as every taught mechanic does.

**The crowd is Wyrmforge's, leaned on, and the health is the step**: held to no less than
Wyrmforge's on every count `prove` reads and to no more than `MOST_OVER` past it, because
Ironbastion's lesson was that spiky rungs wall the good line long before a surge does - so the
spikes here are spikes of health, which bites the strong line, rather than of headcount, which
pays cogs and charms back (37ff).

**The four duels, and what each pair asks.**

  * **Rung 5, a blightcaller and an ironclad** - a douse puts the fullest ward's fire out while
    the ironclad's aegis turns aside everything but the one ward it is weak to.
  * **Rung 10, a gravemaw and a thunderer** - a devour eats every cog and bomb left lying while a
    drain empties a ward's tube: spend what the hill drops at once, and keep feeding.
  * **Rung 15, a harrower and a warlord** - a harrow takes a rank off a ward and drops it on the
    hill as a cog to win back, while a smite takes a ward's health.
  * **Rung 20, a bonecaller and a gorgon** - a raise sends the dead back up the hill as a crowd
    while a glare turns the fullest ward stone-struck. **Cogs at fifty**, the finale's argument
    since Cloudkeep.

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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s16_rustwaste.json"

CHAPTER = "s16_rustwaste"

#: The chapter this one is commissioned against: harder than it.
BEFORE = OUT.parent / "s15_wyrmforge.json"

#: This chapter's place inside its own mode and track, which decides its map (`map14`, the owner's
#: scrapyard), its skies (the second block again - forty skies wrap at four chapters), its cast
#: (`SiegeMode.MainCasts`, the rabble), its charms (all six; `SiegeCharms.Upto` clamps) and its
#: surge floor (the seventh row of `SiegeTuning.Traded`).
ORDINAL = 14

#: The floor every rung deals at least, in tenths - the owner's "100% health bonus at minimum",
#: held here so a change to `siege.TRADED` is a re-measurement rather than a silent retune.
FLOOR = 20

#: The most any rung deals, in tenths. A ceiling on the spike, so a retune that pushes one rung
#: past it is a decision rather than a drift.
PEAK = 24

#: Every fifth rung is a duel. Held here and proved below, rather than trusted to the table.
BOSS_EVERY = 5

#: Every field in this chapter, in cells - the same eight by five every chapter before it uses.
WIDE, TALL = 8, 5

#: How many rungs this chapter has, and so how many seats its map carries (`mapart.NODES`).
RUNGS = 20

#: The fewest and most raiders a wave of this chapter sends, **bosses aside** - Wyrmforge's bounds,
#: kept: the step is the health, and a wider wave is the spike that walls a good line.
LEAST_WAVE, MOST_WAVE = 12, 18

#: The fewest brutes a rung that is not a duel sends - Wyrmforge's floor, kept.
LEAST_BRUTES = 6

#: **Harder by health, and not by a crowd that walls**: the crowd may not fall under Wyrmforge's on
#: any of these and may not run further past it than this, in per cent. Hill (par) is printed only,
#: because it carries every rung's surge.
MOST_OVER = {"raiders": 4, "brutes": 8, "shields": 15, "bombers": 40}

#: Every rung, in order.
#:
#: ``surge`` is the rung's own health figure in tenths (at least `FLOOR`, at most `PEAK`). ``seed``
#: deals the field (see `Tools/siege_sweep.py`); ``swaps`` is what that seed measured and is
#: recorded so a re-sweep can be checked rather than trusted. ``cogs`` is how often a felled raider
#: leaves one on the hill, **per hundred kills**, and ``boss`` names a duel - two bosses joined by
#: `+` - or is empty. Every field opens at six swaps. **No seed and no field is shared with any
#: shipped chapter.**
#:
#: **Eighteen fields were chosen by play, not by the sweep** (2026-10-25): the first deal walled
#: eight rungs on the veteran line at four stars, and the same waves at the same surge read from 0
#: to 9 of 9 by field alone. So each walled rung was dealt ten fresh six-swap fields and kept one
#: that held it 2 to 5 of 9 on that line - and a second pass did the same to the rungs that line
#: found too easy (7 and 8 of 9), so the chapter reads hard everywhere rather than in patches. A
#: duel's field also had to let the strongest line reach its boss (`EveryShippedBossRungIsAFight`).
LEVELS = (
    # ---------------------------------------------------------------- the wreck rocks
    # **The wreck yard**, at the floor: the crowd Wyrmforge ended on, two shields and brutes in plate.
    dict(id="s16_wreckyard", surge=20, seed=681217265, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby#rgbyrgb", "rgby#bRGby!rgbyRG", "rgBY#y#grgbyRGbyr"]),

    # Under the crane's hook: a tenth heavier, brutes closing every wave.
    dict(id="s16_cranehook", surge=21, seed=3100600313, swaps=6, cogs=20, boss="",
         waves=["rgby#rgbyRGbyrgby", "rgbyRGby#rrgbyRGby", "rgbyRGbyrgBY#grgb"]),

    # **Pincers** down the dust pipe, back at the floor: two colours, the other two, then all four.
    dict(id="s16_dustpipe", surge=20, seed=3100028370, swaps=6, cogs=20, boss="",
         waves=["rrggRGrrgg#rrgg", "bbyyBYbbyy#bbyy", "rgbyRGBY#r#bgyrgby"]),

    # **The first spike: the toxic pool at 2.2**, bombers two to a wave.
    dict(id="s16_toxicpool", surge=22, seed=3100665966, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby!rRGbyrg", "rgBY#g!bRGby!yrg", "rgby!g#rRGbyRGby!b"]),

    # **The first duel: a blightcaller and an ironclad** - a douse puts the fullest fire out while
    # the aegis turns aside all but one ward.
    dict(id="s16_scrapgate", surge=21, seed=3100734699, swaps=6, cogs=35,
         boss="blightcaller:b+ironclad:y",
         waves=["rgbyRGbyrgbyrgb", "rgby#r!gRGbyRGb", "rgBY#g#yRGbyrg"]),

    # ---------------------------------------------------------------- the pylon rocks
    # **One colour at a time, in a flood**, down the pylon run, back at the floor.
    dict(id="s16_pylonrun", surge=20, seed=3100079582, swaps=6, cogs=20, boss="",
         waves=["yyYYYyyyyy#yggg", "rrRRRrrrrr#rbbb", "bbBB#bbbbGGG#gggg"]),

    # Across the solar field at 2.2: four waves, the third armoured and bombed at once.
    dict(id="s16_solarfield", surge=22, seed=3100116057, swaps=6, cogs=20, boss="",
         waves=["rgbyRGbyrgbyrgb", "rgby#b#gRGbyRGb", "rgby!rrbRG#yrrgy", "rgbyRGby#grgbyRG"]),

    # **The rust bridge**: a pair of shields in every wave.
    dict(id="s16_rustbridge", surge=21, seed=3100143726, swaps=6, cogs=20, boss="",
         waves=["rgby#y#rRGby!rgby", "rgby#g#bRGBYrgbyr", "rgBY#b#gRGbyrgby"]),

    # **The second spike: the oil barrels at 2.3** - the longest climb before the second duel,
    # four waves and a brute pair in every one.
    dict(id="s16_oilbarrels", surge=23, seed=3100209325, swaps=6, cogs=20, boss="",
         waves=["rgby#grgbyRGbyrgb", "rgby#yrgbyRGbyr", "rgby#b!rrgBYrgby", "rgbyrgbyRGBYrgb"]),

    # **The second duel: a gravemaw and a thunderer** - the hill's cogs and bombs are eaten while a
    # ward's tube is drained.
    dict(id="s16_engineshelf", surge=21, seed=3100227075, swaps=6, cogs=50,
         boss="gravemaw:g+thunderer:r",
         waves=["rgbyRGbyrgbyr", "rgby#yRGbyrgbRG", "rgby#bRGbyrgb"]),

    # ---------------------------------------------------------------- the turbine rocks
    # Under the turbines: the crowd climbs, fifteen, sixteen, seventeen.
    dict(id="s16_turbinehill", surge=21, seed=1910634096, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGby!rgb", "rgBY#r#gyrgbyRGbyb", "rgby!y#bRGbyrgBYgr"]),

    # **The breather at the cable cross**, back at the floor: bombs and plate in every wave.
    dict(id="s16_cablecross", surge=20, seed=3100761739, swaps=6, cogs=20, boss="",
         waves=["rgby#r!gRGbyrgby", "rgby#y#b!gRGbyrgb", "rgBY!b#rRGbyrgBY"]),

    # Tumbleweed across the road at 2.2: brutes from the first wave.
    dict(id="s16_tumbleweed", surge=22, seed=3100335830, swaps=6, cogs=20, boss="",
         waves=["rgbyRGby#yrgbyrgb", "rgbyRGby#gRGbyrg", "RGbyrgby#rrgbyrgBY"]),

    # **The third spike: the container stack at 2.3** - a column, three shields at the head of
    # every wave and cogs at 25. A column is the most field-bound shape in the mode (37fg).
    dict(id="s16_containerstack", surge=23, seed=3100394792, swaps=6, cogs=25, boss="",
         waves=["#r#g#yRGBYrgbyrg", "#b#y#gRGbyrgbyrgb", "#g#r#bRGbyrgbyrg"]),

    # **The third duel: a harrower and a warlord** under the watchtower - a rank torn off one ward
    # and dropped on the hill while a smite takes another's health.
    dict(id="s16_lookout", surge=22, seed=3100852942, swaps=6, cogs=45,
         boss="harrower:y+warlord:b",
         waves=["rgbyRGbyrgbyr", "rgby#rRGbyRGb", "rgBY#gRGbyrgb"]),

    # ---------------------------------------------------------------- the robot rocks
    # The sludge mire: brutes of one colour at a time, a pair at the head of its creepers.
    dict(id="s16_sludgemire", surge=21, seed=2378456940, swaps=6, cogs=20, boss="",
         waves=["yyYYY#yyyyrrb", "bbBBB#bbbbb#bg", "rrRRR#rrrrgg#gg"]),

    # **The iron hand at 2.3**: four waves past the robot's fallen arm, the last led by every
    # colour's brute. **Cogs at forty**, the lever that moves no par: at twenty the veteran line at
    # four stars held it 1 of 9 on its best field, at forty 3.
    dict(id="s16_ironhand", surge=23, seed=3100441904, swaps=6, cogs=40, boss="",
         waves=["rgbyrgbyRGbyry", "rgby#bbrgbyRGby", "rgby#r#y!ggrbyRG", "RGBYrgbyRGbyrg"]),

    # Under the robot's gaze at 2.2: a bomber at the head of every wave and a second in the middle.
    dict(id="s16_robotgaze", surge=22, seed=3100838730, swaps=6, cogs=20, boss="",
         waves=["!grgbyRGbyrgbyRG", "!yrgby#bRGby!rrgb", "!rrgBY#gRGbyrgBYy"]),

    # **The peak: the junk spire at 2.4**, everything the chapter taught at once - brutes, three
    # shields and a bomb in one rung, at the most health any rung of the ladder deals.
    dict(id="s16_junkspire", surge=24, seed=3100487655, swaps=6, cogs=20, boss="",
         waves=["rgbyrgbyRGbyRGbyb", "rgby#g#b#rRGby!rgb", "rgby!y#gRGbyrgBYRG"]),

    # **The finale: a bonecaller and a gorgon** at 2.2 - the dead rise as a crowd while the fullest
    # ward is struck to stone. **Cogs at fifty**, Cloudkeep's finale's argument. A colossus and an
    # overlord stood here first and the veteran line at four stars held them 1 of 9 on every field
    # tried, so the fight gate moved the pair, not the surge.
    dict(id="s16_rustcrown", surge=22, seed=3100567094, swaps=6, cogs=50,
         boss="bonecaller:r+gorgon:g",
         waves=["rgbyrgbyRGbyrg", "rgby#ggrbyRGbyr", "rgbyRGbyrgbyRG"]),
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
        "accent": "#FF8A5C",
        "slate": "#2B1A2E",
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
    floor and the peak, and holds the crowd to the brief: no less than Wyrmforge's on every count,
    and no more than `MOST_OVER` past it. Whether a rung can be **held** is
    `SiegeRuleTests.TheFourteenthChapterIsHarder`, and whether every duel is a fight is
    `SiegeRuleTests.EveryShippedBossRungIsAFight`.
    """
    if len(LEVELS) != RUNGS:
        sys.exit("this chapter is %d rungs long, not %d" % (len(LEVELS), RUNGS))

    if mapart.map_of(ORDINAL) != 14:
        sys.exit("ordinal %d draws map%d, not the scrapyard" % (ORDINAL, mapart.map_of(ORDINAL)))

    if len(mapart.places(ORDINAL)) < RUNGS:
        sys.exit("map%d seats %d nodes; this chapter needs %d (mapart.NODES)"
                 % (mapart.map_of(ORDINAL), len(mapart.places(ORDINAL)), RUNGS))

    # The floor is a measured figure; a change to it is a re-measurement, so it is held here.
    if siege.toughness_for(ORDINAL - 1) != FLOOR:
        sys.exit("this chapter was measured on a floor of %d tenths, and the rule gives %d"
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
        sys.exit("no rung deals the floor of %d tenths; the brief is 100%% at minimum" % FLOOR)
    if max(surges) - FLOOR < 3:
        sys.exit("the surge never climbs three tenths over the floor; the brief asks for waves")
    for a, b in zip(surges, surges[1:]):
        if abs(a - b) > 2:
            sys.exit("two neighbouring rungs deal %d and %d tenths; a step of more than two is a "
                     "cliff" % (a, b))
    print("surge by rung: %s (average %.3f)" % (" ".join("%d.%d" % (s // 10, s % 10) for s in surges),
                                                 sum(surges) / 10.0 / len(surges)))

    # **The crowd, against the chapter it was commissioned against**, read through one mirror:
    # never less than Wyrmforge on any count, and never more than `MOST_OVER` past it. Hill is
    # printed only.
    ours = totals(written["levels"])
    theirs = totals(json.loads(BEFORE.read_text(encoding="utf-8"))["levels"])
    for name, mine, before in zip(("raiders", "brutes", "shields", "bombers", "hill (par)"),
                                  ours, theirs):
        over = (mine - before) * 100.0 / before
        print("%-11s %5d against Wyrmforge's %5d (%+.1f%%)" % (name, mine, before, over))
        if name not in MOST_OVER:
            continue
        if mine < before:
            sys.exit("the brief is harder than Wyrmforge, and this chapter sends %d %s against %d"
                     % (mine, name, before))
        if over > MOST_OVER[name]:
            sys.exit("the step is the health and not the crowd, and %s is %+.1f%% over Wyrmforge "
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
